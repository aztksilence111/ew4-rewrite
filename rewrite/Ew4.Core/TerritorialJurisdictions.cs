using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record JurisdictionBorder(int First, int Second);
public sealed record TerritorialJurisdictions
{
    public int CenterCaptureVersion { get; init; }
    public IReadOnlyList<int> CellCenters { get; init; } = Array.Empty<int>();
    public IReadOnlyList<JurisdictionBorder> Borders { get; init; } = Array.Empty<JurisdictionBorder>();
    public IReadOnlyList<int> DetachedCells { get; init; } = Array.Empty<int>();

    public static bool IsCenter(CellState cell) => cell.BuildingKind is "city" or "industry" or "stable";
    private static bool Land(CellState cell) => cell.Terrain.Active && !cell.Terrain.Sea && cell.BuildingKind != "port";

    // Partition each country's connected land before play. Sea never joins districts.
    public static TerritorialJurisdictions Create(WorldState state)
    {
        var map = new HexMap(state.Width, state.Height);
        var centers = Enumerable.Repeat(-1, state.Cells.Count).ToArray();
        var queue = new Queue<int>();
        for (int i = 0; i < centers.Length; i++)
            if (Land(state.Cells[i]) && IsCenter(state.Cells[i]) && state.Cells[i].Owner != null)
            { centers[i] = i; queue.Enqueue(i); }
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                int next = map.Neighbor(cell, d);
                if (next < 0 || centers[next] >= 0 || !Land(state.Cells[next]) || state.Cells[next].Owner != state.Cells[cell].Owner) continue;
                centers[next] = centers[cell]; queue.Enqueue(next);
            }
        }
        var roots = centers.Distinct().Where(c => c >= 0).OrderBy(c => c).ToArray();
        var components = Enumerable.Repeat(-1, centers.Length).ToArray();
        for (int i = 0; i < centers.Length; i++)
        {
            if (!Land(state.Cells[i]) || components[i] >= 0) continue;
            components[i] = i; queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    int next = map.Neighbor(cell, d);
                    if (next < 0 || !Land(state.Cells[next]) || components[next] >= 0) continue;
                    components[next] = i; queue.Enqueue(next);
                }
            }
        }
        // Islands/enclaves still have an administrative label, without creating a sea frontier.
        var detached = new HashSet<int>();
        for (int i = 0; i < centers.Length; i++)
        {
            if (!Land(state.Cells[i]) || centers[i] >= 0 || roots.Length == 0) continue;
            var distances = Enumerable.Repeat(-1, centers.Length).ToArray();
            distances[i] = 0; queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    int next = map.Neighbor(cell, d);
                    if (next < 0 || !Land(state.Cells[next]) || distances[next] >= 0) continue;
                    distances[next] = distances[cell] + 1; queue.Enqueue(next);
                }
            }
            int nearest = roots.OrderBy(c => state.Cells[c].Owner == state.Cells[i].Owner ? 0 : 1)
                .ThenBy(c => distances[c] >= 0 ? 0 : 1).ThenBy(c => distances[c] >= 0 ? distances[c] : map.Distance(i, c)).ThenBy(c => c).First();
            centers[i] = nearest;
            if (components[nearest] != components[i] || state.Cells[nearest].Owner != state.Cells[i].Owner) detached.Add(i);
        }
        var borders = new HashSet<(int, int)>();
        for (int i = 0; i < centers.Length; i++)
        {
            if (centers[i] < 0 || state.Cells[i].Terrain.Sea || detached.Contains(i)) continue;
            for (int d = 0; d < 6; d++)
            {
                int next = map.Neighbor(i, d);
                if (next < 0 || state.Cells[next].Terrain.Sea || centers[next] < 0 || centers[next] == centers[i] || detached.Contains(next)) continue;
                borders.Add((Math.Min(centers[i], centers[next]), Math.Max(centers[i], centers[next])));
            }
        }
        return new() { CenterCaptureVersion = 2, CellCenters = centers, DetachedCells = detached.OrderBy(c => c).ToArray(), Borders = borders.OrderBy(b => b.Item1).ThenBy(b => b.Item2)
            .Select(b => new JurisdictionBorder(b.Item1, b.Item2)).ToArray() };
    }

    public static TerritorialJurisdictions FromAssignments(WorldState state, IReadOnlyList<int> assignments)
    {
        if (assignments.Count != state.Cells.Count) throw new RuleViolation("territory.jurisdictions", "手工辖区格子数量与地图不符");
        var centers = assignments.ToArray();
        for (int i = 0; i < centers.Length; i++)
        {
            int center = centers[i];
            if (center < -1 || center >= centers.Length || !Land(state.Cells[i]) && center != -1
                || Land(state.Cells[i]) && IsCenter(state.Cells[i]) && center != i
                || center >= 0 && (!Land(state.Cells[center]) || !IsCenter(state.Cells[center]) || centers[center] != center))
                throw new RuleViolation("territory.jurisdiction_cell", $"地块{i}的手工辖区中心无效: {center}；中心须为有效城市/工厂/马厩并指向自身，海格/港口/区域外须为-1");
        }
        // Administrative islands may share a label, but must not create a sea frontier.
        var map = new HexMap(state.Width, state.Height);
        var components = Enumerable.Repeat(-1, centers.Length).ToArray();
        var queue = new Queue<int>();
        for (int i = 0; i < centers.Length; i++)
        {
            if (!Land(state.Cells[i]) || components[i] >= 0) continue;
            components[i] = i;queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                for (int d = 0; d < 6; d++)
                {
                    int next = map.Neighbor(cell, d);
                    if (next < 0 || !Land(state.Cells[next]) || components[next] >= 0) continue;
                    components[next] = i;queue.Enqueue(next);
                }
            }
        }
        var detached = Enumerable.Range(0, centers.Length).Where(i => centers[i] >= 0 && components[i] != components[centers[i]]).ToHashSet();
        var borders = new HashSet<(int, int)>();
        for (int i = 0; i < centers.Length; i++)
        {
            if (centers[i] < 0 || detached.Contains(i)) continue;
            for (int d = 0; d < 6; d++)
            {
                int next = map.Neighbor(i, d);
                if (next < 0 || centers[next] < 0 || centers[next] == centers[i] || detached.Contains(next)) continue;
                borders.Add((Math.Min(centers[i], centers[next]), Math.Max(centers[i], centers[next])));
            }
        }
        var result = new TerritorialJurisdictions { CenterCaptureVersion = 2, CellCenters = centers,
            DetachedCells = detached.OrderBy(c => c).ToArray(), Borders = borders.OrderBy(b => b.Item1).ThenBy(b => b.Item2).Select(b => new JurisdictionBorder(b.Item1, b.Item2)).ToArray() };
        result.Validate(state);return result;
    }

    public void Validate(WorldState state)
    {
        if (CenterCaptureVersion is < 0 or > 2 || CellCenters.Count != state.Cells.Count || CellCenters.Any(c => c < -1 || c >= state.Cells.Count
            || c >= 0 && (!(IsCenter(state.Cells[c]) || CenterCaptureVersion < 2 && state.Cells[c].BuildingKind == "port") || !state.Cells[c].Terrain.Active || CellCenters[c] != c))
            || Borders.Any(b => b.First < 0 || b.Second <= b.First || b.Second >= state.Cells.Count
                || CellCenters[b.First] != b.First || CellCenters[b.Second] != b.Second)
            || Borders.Distinct().Count() != Borders.Count || DetachedCells.Any(c => c < 0 || c >= state.Cells.Count)
            || DetachedCells.Distinct().Count() != DetachedCells.Count)
            throw new RuleViolation("territory.jurisdictions", "Invalid fixed jurisdiction map or border graph");
    }
    public TerritorialJurisdictions Copy() => this with { CellCenters = CellCenters.ToArray(), Borders = Borders.ToArray(), DetachedCells = DetachedCells.ToArray() };
}

public static class JurisdictionClaims
{
    public static bool CanBuildFort(WorldState state, int cell, string owner) => state.Jurisdictions == null
        || state.Jurisdictions.CellCenters[cell] is int center && center >= 0 && state.Cells[center].Owner == owner;

    private static bool SameDistrict(WorldState state, int first, int second) => state.Jurisdictions!.CellCenters[first] >= 0
        && state.Jurisdictions.CellCenters[first] == state.Jurisdictions.CellCenters[second];

    private static bool AlliedTerritory(WorldState state, int cell, string owner) => state.Cells[cell].Owner is string previous
        && previous != owner && WorldDiplomacy.Allied(state, previous, owner);

    private static IReadOnlyList<CellState> PreserveAllies(WorldState state, IReadOnlyList<CellState> proposed, string owner, HashSet<int>? occupied = null) =>
        proposed.Select((c, i) => c.Owner != state.Cells[i].Owner && AlliedTerritory(state, i, owner)
            && !(occupied?.Contains(i) == true && (TerritorialJurisdictions.IsCenter(c) || c.BuildingKind == "port")) ? state.Cells[i] : c).ToArray();

    private sealed class Policy
    {
        private readonly WorldState state;
        private readonly string owner;
        private readonly HexMap map;
        private readonly HashSet<int> allowed = new();
        private readonly Dictionary<int, string[]> forts;
        private readonly HashSet<int> detached;
        private readonly bool transferAllies;
        internal readonly HashSet<int> Released = new();
        internal Policy(WorldState state, string owner, bool transferAllies = false)
        {
            this.state = state; this.owner = owner; this.transferAllies = transferAllies; map = new(state.Width, state.Height);
            var areas = state.Jurisdictions!;
            detached = new(areas.DetachedCells);
            foreach (int center in areas.CellCenters.Distinct())
                if (center >= 0 && state.Cells[center].Owner == owner) allowed.Add(center);
            foreach (var edge in areas.Borders)
            {
                if (state.Cells[edge.First].Owner == owner) allowed.Add(edge.Second);
                if (state.Cells[edge.Second].Owner == owner) allowed.Add(edge.First);
            }
            forts = state.Units.Where(u => u.Combatant.Profile.Category == "fort")
                .GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.Select(u => u.Owner).ToArray());
        }
        internal IEnumerable<int> Ring(int center)
        {
            yield return center;
            for (int d = 0; d < 6; d++) { int n = map.Neighbor(center, d); if (n >= 0) yield return n; }
        }
        internal void OccupiedCenter(int center) => allowed.Add(center);
        internal bool CanClaim(int cell, bool occupyFarm = false, bool occupiedSite = false, bool requireFrontier = false)
        {
            var site = state.Cells[cell];
            if ((!transferAllies && AlliedTerritory(state, cell, owner)) || !site.Terrain.Active) return false;
            // Facilities only change hands through an actual occupation, never a neighboring ring.
            if (site.BuildingKind != null && !occupiedSite) return false;
            if (occupiedSite && (TerritorialJurisdictions.IsCenter(site) || site.BuildingKind == "port")) return true;
            if (site.Terrain.Sea
                || requireFrontier && (detached.Contains(cell) || !allowed.Contains(state.Jurisdictions!.CellCenters[cell]))) return false;
            foreach (int guard in Ring(cell))
            {
                if (Released.Contains(guard)) continue;
                if (!occupyFarm && state.Cells[guard].BuildingKind == "farmland" && state.Cells[guard].Owner is string farmer && farmer != owner) return false;
                if (SameDistrict(state, cell, guard) && forts.TryGetValue(guard, out var owners) && owners.Any(o => o != owner)) return false;
            }
            return true;
        }
    }

    public static IReadOnlyList<CellState> Filter(WorldState state, IReadOnlyList<CellState> proposed, string owner, bool requireFrontier = false)
    {
        proposed = PreserveAllies(state, proposed, owner);
        if (state.Jurisdictions == null) return proposed;
        var policy = new Policy(state, owner);
        return proposed.Select((c, i) => c.Owner == state.Cells[i].Owner || c.Owner == null && c.Terrain.Sea && state.Cells[i].BuildingKind == null
            || policy.CanClaim(i, requireFrontier: requireFrontier) ? c : state.Cells[i]).ToArray();
    }

    public static IReadOnlyList<CellState> Move(WorldState state, IReadOnlyList<CellState> proposed, IReadOnlyList<int> path, string owner)
    {
        var occupied = path.Skip(1).ToHashSet();
        proposed = PreserveAllies(state, proposed, owner, occupied);
        if (state.Jurisdictions == null) return proposed;
        var policy = new Policy(state, owner);
        var capturedFarms = new List<int>();
        foreach (int cell in path.Skip(1).Distinct())
        {
            if (state.Cells[cell].BuildingKind != "farmland" || state.Cells[cell].Owner == owner || proposed[cell].Owner != owner) continue;
            policy.Released.Add(cell);
            if (policy.CanClaim(cell, true, true, requireFrontier: false)) capturedFarms.Add(cell); else policy.Released.Remove(cell);
        }
        var result = proposed.Select((c, i) => c.Owner == state.Cells[i].Owner || c.Owner == null && c.Terrain.Sea && state.Cells[i].BuildingKind == null
            || occupied.Contains(i) && (TerritorialJurisdictions.IsCenter(c) || c.BuildingKind == "port") && AlliedTerritory(state, i, owner)
            || policy.CanClaim(i, capturedFarms.Contains(i), occupied.Contains(i)) ? c : state.Cells[i]).ToArray();
        foreach (int farm in capturedFarms)
            foreach (int cell in policy.Ring(farm))
                if (cell == farm || policy.CanClaim(cell, requireFrontier: true)) result[cell] = result[cell] with { Owner = owner };
        foreach (int center in path.Skip(1).Distinct().Where(i => TerritorialJurisdictions.IsCenter(state.Cells[i])
            && state.Cells[i].Owner != owner && result[i].Owner == owner))
            TransferDistrict(state, result, center, owner, new Policy(state, owner, true));
        return result;
    }

    private static void TransferDistrict(WorldState state, CellState[] result, int center, string owner, Policy policy)
    {
        policy.OccupiedCenter(center);
        for (int i = 0; i < result.Length; i++)
            if (state.Jurisdictions!.CellCenters[i] == center && policy.CanClaim(i, true)) result[i] = result[i] with { Owner = owner };
    }

    public static WorldState UpgradeCapturedDistricts(WorldState state, WorldState original)
    {
        if (state.Jurisdictions == null || state.Jurisdictions.CenterCaptureVersion >= 2) return state;
        state = state with { Jurisdictions = original.Jurisdictions! with { CenterCaptureVersion = 0 } };
        var result = state.Cells.ToArray();
        for (int center = 0; center < result.Length; center++)
        {
            string? owner = state.Cells[center].Owner;
            if (!TerritorialJurisdictions.IsCenter(state.Cells[center]) || owner == null || owner == original.Cells[center].Owner) continue;
            // Reconstruct the frontier before this old-save capture, without granting a deep/sea shortcut.
            var before = state with { Cells = state.Cells.Select((c, i) => i == center ? c with { Owner = original.Cells[i].Owner } : c).ToArray() };
            var policy = new Policy(before, owner, true);
            if (AlliedTerritory(before, center, owner) || policy.CanClaim(center, occupiedSite: true)) TransferDistrict(before, result, center, owner, policy);
        }
        return state with { Cells = result, Jurisdictions = state.Jurisdictions with { CenterCaptureVersion = 2 } };
    }

    public static IReadOnlyList<CellState> DestroyFort(WorldState before, int cell, string owner)
    {
        if (before.Jurisdictions == null) return before.Cells;
        var policy = new Policy(before, owner); policy.Released.Add(cell);
        // Destroying a deep or overseas fort cannot bypass the city frontier.
        if (!policy.CanClaim(cell, true, requireFrontier: true)) return before.Cells;
        var result = before.Cells.ToArray();
        foreach (int target in policy.Ring(cell))
            if (SameDistrict(before, cell, target) && policy.CanClaim(target, true, requireFrontier: true)) result[target] = result[target] with { Owner = owner };
        return result;
    }
}
