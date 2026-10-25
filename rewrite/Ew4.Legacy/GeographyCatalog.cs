using System.Security.Cryptography;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record GeographyOverride(int Column, int Row, string Type);
public sealed record GeographyMap(string Map, int Width, int Height, string BaseTerrainSha256, GeographyOverride[] TerrainOverrides);
public sealed record GeographyVisual(string[] Sprites, string Name);
public sealed record GeographyCatalog(int Schema, GeographyMap[] Maps, Dictionary<string, GeographyVisual>? Visuals = null)
{
    public static GeographyCatalog Empty { get; } = new(1, []);
    public static readonly string[] Types = ["impassable_mountain", "impassable_sea", "mountain_pass"];
    private static readonly GeographyVisual MountainVisual = new(Enumerable.Range(1, 6).Select(i => $"impassable_mountain_{i}.png").ToArray(), "不可通行");
    private static readonly GeographyVisual OceanVisual = new(["impassable_sea.png"], "不可通行");
    private static readonly GeographyVisual PassVisual = new(["mountain_1.png"], "");

    public GeographyVisual Visual(TerrainBarrier barrier) => Visual(Type(barrier));
    public GeographyVisual Visual(string type) => Visuals?.GetValueOrDefault(type) ??
        (type == Types[0] ? MountainVisual : type == Types[2] ? PassVisual : OceanVisual);
    public static string Type(MovementCell cell) => cell.Barrier == TerrainBarrier.None && cell.MinimumEntryCost > 0 ? Types[2] : Type(cell.Barrier);
    public static string Type(TerrainBarrier barrier) => barrier switch {
        TerrainBarrier.Mountain => Types[0], TerrainBarrier.Ocean => Types[1], _ => "none" };

    public void Validate(IReadOnlyDictionary<int, LegacyTerrainMap> maps, IReadOnlyDictionary<int, string> names,
        Func<int, byte[]> source)
    {
        if (Schema != 1 || Maps == null || Maps.Any(m => m == null || string.IsNullOrWhiteSpace(m.Map))
            || Maps.Select(m => m.Map).Distinct(StringComparer.Ordinal).Count() != Maps.Length)
            throw new RuleViolation("geography.configuration", "Invalid geography schema or duplicate map");
        if (Visuals != null && Visuals.Any(p => !Types.Contains(p.Key) || p.Value == null
            || string.IsNullOrWhiteSpace(p.Value.Name) || p.Value.Name.Length > 80 || p.Value.Sprites == null
            || p.Value.Sprites.Length is < 1 or > 16 || p.Value.Sprites.Any(s => string.IsNullOrWhiteSpace(s)
                || !s.EndsWith(".png", StringComparison.Ordinal) || s.Contains('/') || s.Contains('\\'))))
            throw new RuleViolation("geography.visual", "Invalid geography sprite/name override");
        foreach (var item in Maps)
        {
            var match = names.Where(p => p.Value == item.Map).ToArray();
            if (match.Length != 1) throw new RuleViolation("geography.map", item.Map + ": map is not registered");
            int id = match[0].Key;
            var map = maps[id];
            if (item.Width != map.Width || item.Height != map.Height || item.BaseTerrainSha256 == null
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(source(id))), item.BaseTerrainSha256, StringComparison.Ordinal))
                throw new RuleViolation("geography.source", item.Map + ": map dimensions or base terrain hash differ");
            if (item.TerrainOverrides == null || item.TerrainOverrides.Any(o => o == null || o.Column < 0
                || o.Row < 0 || o.Column >= map.Width || o.Row >= map.Height || !Types.Contains(o.Type))
                || item.TerrainOverrides.Select(o => o.Row * map.Width + o.Column).Distinct().Count() != item.TerrainOverrides.Length)
                throw new RuleViolation("geography.cells", item.Map + ": duplicate, invalid or unknown terrain override");
            foreach (var tile in item.TerrainOverrides)
            {
                bool sea = map.Cells[tile.Row * map.Width + tile.Column].BackgroundKind == 1;
                if (sea != (tile.Type == Types[1]))
                    throw new RuleViolation("geography.environment", $"{item.Map}: land/sea mismatch at {tile.Column},{tile.Row}");
            }
        }
    }

    public WorldMapDefinition Apply(string name, WorldMapDefinition map, int mountainCost = 6)
    {
        var configuration = Maps.SingleOrDefault(m => m.Map == name);
        if (configuration == null) return map;
        var edits = configuration.TerrainOverrides.Select(o => {
            int i = o.Row * map.Width + o.Column;
            var cell = map.Cells[i];
            bool mountain = o.Type != Types[1], pass = o.Type == Types[2];
            return new MapCellEdit(i, cell with {
                Terrain = cell.Terrain with { Barrier = pass ? TerrainBarrier.None : mountain ? TerrainBarrier.Mountain : TerrainBarrier.Ocean,
                    TerrainCost = pass ? mountainCost : cell.Terrain.TerrainCost,
                    MinimumEntryCost = pass ? mountainCost : 0 },
                DefenseTerrain = mountain ? "mountain" : "sea", FireTerrain = "open" });
        }).ToArray();
        return map.Edit(edits);
    }
}
