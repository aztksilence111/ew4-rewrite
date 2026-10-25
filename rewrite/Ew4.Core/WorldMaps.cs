using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Ew4.Core;

public sealed record MapImageLayer(string AssetId, double Left, double Top, double Width, double Height);
public sealed record MapCellEdit(int Cell, CellState Value);

public sealed class WorldMapDefinition
{
    public string Id { get; }
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<CellState> Cells { get; }
    public IReadOnlyList<MapImageLayer> Images { get; }

    public WorldMapDefinition(string id, string name, int width, int height, IReadOnlyList<CellState> cells,
        IReadOnlyList<MapImageLayer>? images = null)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || width < 1 || height < 1
            || (long)width * height > int.MaxValue || (long)width * height != cells.Count)
            throw new RuleViolation("map.definition", "Invalid map identity or dimensions");
        foreach (var cell in cells)
        {
            if (cell != null && cell.Terrain != null) Geography.Validate(cell);
            if (cell == null || cell.Terrain == null || cell.Owner != null || cell.Terrain.OccupantCamp != null
                || cell.Terrain.TerrainCost < 1 || cell.Terrain.RiverEdges > 63 || cell.Terrain.BridgeEdges > 63 || cell.Terrain.BlockedSeaEdges > 63
                || cell.BuildingLevel < 0 || cell.BuildingUpgradesThisTurn != 0 || cell.Environment != 0 || cell.Objective || cell.BlocksTerritoryExpansion
                || cell.Name != null && string.IsNullOrWhiteSpace(cell.Name) || cell.BuildingKind != null && string.IsNullOrWhiteSpace(cell.BuildingKind)
                || cell.InstallationKind != null && string.IsNullOrWhiteSpace(cell.InstallationKind)
                || string.IsNullOrWhiteSpace(cell.DefenseTerrain) || string.IsNullOrWhiteSpace(cell.FireTerrain))
                throw new RuleViolation("map.cell_definition", "Base maps need valid geography without faction or scenario runtime state");
            cell.IncomeOverride?.Validate();
        }
        var layers = images ?? Array.Empty<MapImageLayer>();
        bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        if (layers.Any(i => i == null || string.IsNullOrWhiteSpace(i.AssetId) || !Finite(i.Left) || !Finite(i.Top)
            || !Finite(i.Width) || !Finite(i.Height) || i.Width <= 0 || i.Height <= 0))
            throw new RuleViolation("map.image_definition", "Invalid map image placement");
        Id = id; Name = name; Width = width; Height = height;
        Cells = Array.AsReadOnly(cells.Select(c => c with { Terrain = c.Terrain with { } }).ToArray());
        Images = Array.AsReadOnly(layers.Select(i => i with { }).ToArray());
    }

    public WorldMapDefinition Edit(IReadOnlyList<MapCellEdit> edits)
    {
        if (edits.Any(e => e.Cell < 0 || e.Cell >= Cells.Count) || edits.Select(e => e.Cell).Distinct().Count() != edits.Count)
            throw new RuleViolation("map.edit", "Map edits need distinct existing cells");
        var cells = Cells.ToArray();
        foreach (var edit in edits) cells[edit.Cell] = edit.Value;
        return new(Id, Name, Width, Height, cells, Images);
    }
}

public sealed class WorldMapCatalog
{
    public IReadOnlyDictionary<string, WorldMapDefinition> Maps { get; }
    public WorldMapCatalog(IEnumerable<WorldMapDefinition> maps)
    {
        var result = new Dictionary<string, WorldMapDefinition>(StringComparer.Ordinal);
        foreach (var map in maps)
            if (!result.TryAdd(map.Id, map)) throw new RuleViolation("map.duplicate", "Duplicate map identity: " + map.Id);
        Maps = new ReadOnlyDictionary<string, WorldMapDefinition>(result);
    }
    public WorldMapDefinition Resolve(string id) => Maps.TryGetValue(id, out var map) ? map
        : throw new RuleViolation("map.reference", "Unknown map identity: " + id);
    public WorldMapCatalog Add(WorldMapDefinition map) => new(Maps.Values.Concat(new[] { map }));
    public WorldMapCatalog Replace(WorldMapDefinition map)
    {
        Resolve(map.Id);
        return new(Maps.Values.Select(m => m.Id == map.Id ? map : m));
    }
}
