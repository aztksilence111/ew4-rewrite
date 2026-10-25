using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record TerritoryMoveRules
{
    public bool ClaimPathNeighbors { get; init; } = true;
    public bool ClaimEndpoint { get; init; } = true;
    public bool ReleaseEmptySea { get; init; } = true;
    public IReadOnlyCollection<string> PassThroughBuildingKinds { get; init; } = new[] { "farmland" };
}

public static class TerritoryClaims
{
    public static IReadOnlyList<CellState> Expand(HexMap map, IReadOnlyList<CellState> cells, IReadOnlyCollection<int> centers,
        IReadOnlyCollection<int> occupiedCells, string owner, IReadOnlyCollection<string>? passThroughBuildings = null)
    {
        if (cells.Count != map.Count || string.IsNullOrWhiteSpace(owner))
            throw new RuleViolation("capture.expansion", "Invalid expansion map or owner");
        var result = cells.ToArray();
        var occupied = new HashSet<int>(occupiedCells);
        passThroughBuildings ??= new[] { "farmland" };
        foreach (int center in centers)
        {
            map.Check(center);
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = map.Neighbor(center, direction);
                if (neighbor < 0) continue;
                var cell = result[neighbor];
                if (cell.Terrain.CanTraverse() && !cell.Terrain.Sea && !cell.Objective && !cell.BlocksTerritoryExpansion && !occupied.Contains(neighbor)
                    && (cell.BuildingKind == null || passThroughBuildings.Contains(cell.BuildingKind)))
                    result[neighbor] = cell with { Owner = owner };
            }
        }
        return result;
    }

    public static IReadOnlyList<CellState> Move(HexMap map, IReadOnlyList<CellState> cells, IReadOnlyCollection<int> otherOccupiedCells,
        IReadOnlyList<int> route, string owner, TerritoryMoveRules? configuration = null)
    {
        var result = new OwnershipBuffer(cells);
        MoveOwners(map, cells, otherOccupiedCells, route, owner, configuration ?? new(), result);
        return result.Seal();
    }

    internal static void MoveOwners(HexMap map, IReadOnlyList<CellState> cells, IReadOnlyCollection<int> otherOccupiedCells,
        IReadOnlyList<int> route, string owner, TerritoryMoveRules rules, OwnershipBuffer result)
    {
        if (cells.Count != map.Count || route.Count < 2 || string.IsNullOrWhiteSpace(owner))
            throw new RuleViolation("capture.route", "Invalid capture route or owner");
        for (int i = 0; i < route.Count; i++)
        {
            map.Check(route[i]);
            if (i > 0 && map.Distance(route[i - 1], route[i]) != 1)
                throw new RuleViolation("capture.route", "Capture route must follow adjacent cells");
        }
        var occupied = new HashSet<int>(otherOccupiedCells);
        int start = route[0], end = route[route.Count - 1];
        if (rules.ReleaseEmptySea && !occupied.Contains(start) && cells[start].Terrain.Sea && cells[start].BuildingKind == null)
            result.SetOwner(start, null);
        void Claim(int cell)
        {
            var site = result[cell];
            if (site.Terrain.Barrier == TerrainBarrier.None && !site.Terrain.Sea && !occupied.Contains(cell) && !site.Objective && !site.BlocksTerritoryExpansion
                && (site.BuildingKind == null || rules.PassThroughBuildingKinds.Contains(site.BuildingKind)))
                result.SetOwner(cell, owner);
        }
        for (int i = 1; i < route.Count - 1; i++)
        {
            int intermediate = route[i];
            if (cells[intermediate].Terrain.Sea) continue;
            Claim(intermediate);
            if (!rules.ClaimPathNeighbors) continue;
            for (int direction = 0; direction < 6; direction++)
            {
                int neighbor = map.Neighbor(intermediate, direction);
                if (neighbor >= 0 && cells[neighbor].Terrain.Active) Claim(neighbor);
            }
        }
        if (rules.ClaimEndpoint && cells[end].Terrain.Barrier == TerrainBarrier.None) result.SetOwner(end, owner);
    }
}
