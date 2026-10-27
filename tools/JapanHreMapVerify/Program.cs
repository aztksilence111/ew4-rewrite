using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var rules = LegacyTerrainRules.FromXml(File.ReadAllText(Path.Combine(root, "rewrite/data/original/def_mapterrain.xml")),
    File.ReadAllText(Path.Combine(root, "rewrite/data/original/def_terraintype.xml")));
int checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new InvalidDataException(message);
}
var results = new List<object>();
using var policyDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "maps/dual_map_config.json")));
var policy = policyDocument.RootElement.GetProperty("spacing");
int Required(string a, string b) => a == "port" || b == "port" ? policy.GetProperty("portMinimumHexDistance").GetInt32()
    : a == "city" && b == "city" ? policy.GetProperty("cityMinimumHexDistance").GetInt32()
    : policy.GetProperty("minimumHexDistance").GetInt32();
foreach (string key in new[] { "japan", "hre" })
{
    var timer = Stopwatch.StartNew();
    string folder = Path.Combine(root, "maps", key);
    var binary = File.ReadAllBytes(Path.Combine(folder, key + ".bin"));
    var terrain = LegacyTerrainMap.Read(binary);
    var geometry = LegacyMapDefinitions.Import(terrain, rules, key, key == "japan" ? "日本全图" : "神罗全图");
    using var buildingDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "buildings.json")));
    using var sourceDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "maps/settlement_plan_20261026", key + "_points.json")));
    using var crossingDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "crossings.json")));
    var sites = buildingDocument.RootElement.EnumerateArray().ToArray();
    using var exceptionDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "generalizations.json")));
    var declared = exceptionDocument.RootElement.GetProperty("spacing_exceptions").EnumerateArray()
        .Select(e => (Math.Min(e.GetProperty("first_cell").GetInt32(), e.GetProperty("second_cell").GetInt32()),
                      Math.Max(e.GetProperty("first_cell").GetInt32(), e.GetProperty("second_cell").GetInt32()))).ToHashSet();
    var observed = new HashSet<(int, int)>();
    var mapConfig = policyDocument.RootElement.GetProperty(key);
    var excludedNames = mapConfig.TryGetProperty("excludedSiteNames", out var excluded)
        ? excluded.EnumerateArray().Select(s => s.GetString()).ToHashSet() : new HashSet<string?>();
    var sourceIds = sourceDocument.RootElement.GetProperty("records").EnumerateArray()
        .Where(s => !excludedNames.Contains(s.GetProperty("name").GetString()))
        .Select(s => s.GetProperty("id").GetString()).ToHashSet();
    string revisionPath = Path.Combine(folder, "sengoku_plan.json");
    if (File.Exists(revisionPath))
    {
        using var revision = JsonDocument.Parse(File.ReadAllBytes(revisionPath));
        foreach (var site in revision.RootElement.GetProperty("additionalSites").EnumerateArray())
            sourceIds.Add(site.GetProperty("id").GetString());
    }
    Check(sourceIds.SetEquals(sites.Select(s => s.GetProperty("id").GetString())), "All approved stable identities preserved: " + key);
    var cells = geometry.Cells.ToArray();
    var grid = new HexMap(geometry.Width, geometry.Height);
    var occupied = new HashSet<int>();
    var islandExceptions = new List<object>();
    foreach (var site in sites)
    {
        int cell = site.GetProperty("cell").GetInt32();
        string kind = site.GetProperty("kind").GetString()!;
        Check(occupied.Add(cell), "Unique building cell");
        Check(cells[cell].Terrain.Sea == (kind == "port"), "Correct water or land for " + site.GetProperty("name").GetString());
        cells[cell] = cells[cell] with { Name = site.GetProperty("name").GetString(), BuildingKind = kind, BuildingLevel = site.GetProperty("level").GetInt32() };
        double limit = policy.GetProperty(kind == "port" ? "portMaximumShiftKm" : kind == "city" ? "cityMaximumShiftKm" : "maximumShiftKm").GetDouble();
        Check(site.GetProperty("displacement_km").GetDouble() <= limit + .001, "Geographic displacement limit");
    }
    for (int i = 0; i < sites.Length; i++)
    for (int j = i + 1; j < sites.Length; j++)
    {
        int a = sites[i].GetProperty("cell").GetInt32(), b = sites[j].GetProperty("cell").GetInt32();
        int distance = grid.Distance(a, b);
        Check(distance >= Required(sites[i].GetProperty("kind").GetString()!, sites[j].GetProperty("kind").GetString()!), "Geography-aware spacing");
        if (sites[i].GetProperty("kind").GetString() != "port" && sites[j].GetProperty("kind").GetString() != "port")
            foreach (var site in new[] { sites[i], sites[j] })
                if (site.TryGetProperty("minimumSpacing", out var minimum))
                    Check(distance >= minimum.GetInt32(), "New historical node retains deployment space");
        if (distance < policy.GetProperty("minimumHexDistance").GetInt32()) observed.Add((Math.Min(a, b), Math.Max(a, b)));
    }
    Check(observed.SetEquals(declared), "Every close pair has an explicit geographic exception");
    foreach (var bridge in crossingDocument.RootElement.EnumerateArray())
    {
        int a = bridge.GetProperty("cell").GetInt32(), b = bridge.GetProperty("other").GetInt32(), d = bridge.GetProperty("direction").GetInt32();
        Check(grid.Neighbor(a, d) == b && (cells[a].Terrain.RiverEdges & (1 << d)) != 0
            && (cells[b].Terrain.RiverEdges & (1 << ((d + 3) % 6))) != 0, "Crossing is on a reciprocal river edge");
        cells[a] = cells[a] with { Terrain = cells[a].Terrain with { BridgeEdges = (byte)(cells[a].Terrain.BridgeEdges | (1 << d)) } };
        cells[b] = cells[b] with { Terrain = cells[b].Terrain with { BridgeEdges = (byte)(cells[b].Terrain.BridgeEdges | (1 << ((d + 3) % 6))) } };
    }
    var movement = cells.Select(c => c.Terrain).ToArray();
    var actor = new MovementActor { Camp = "inspection", FullBudget = 15, Remaining = 15 };
    foreach (var site in sites.Where(s => s.GetProperty("kind").GetString() != "port"))
    {
        int cell = site.GetProperty("cell").GetInt32();
        int clearRing = grid.Within(cell, 1).Count(c => !movement[c].Sea && !occupied.Contains(c));
        if (clearRing < 3) islandExceptions.Add(new { name = site.GetProperty("name").GetString(), cell, clearRing });
        var distance = Movement.Reachable(grid, movement, cell, actor);
        Check(distance.ContainsKey(cell), "Native movement on full grid");
        Check(distance.Keys.All(c => !movement[c].Sea), "Land movement cannot enter sea without transport");
        if (site.GetProperty("kind").GetString() != "city")
            Check(grid.Within(cell, 3).All(c => !occupied.Contains(c) || cells[c].BuildingKind == "port"), "Facility deployment neighborhood");
    }
    for (int cell = 0; cell < cells.Length; cell++)
    {
        for (int d = 0; d < 6; d++)
        {
            int other = grid.Neighbor(cell, d);
            if ((movement[cell].RiverEdges & (1 << d)) == 0) continue;
            Check(other >= 0 && (movement[other].RiverEdges & (1 << ((d + 3) % 6))) != 0, "Native river reciprocity");
        }
    }
    var map = new WorldMapDefinition(key, geometry.Name, geometry.Width, geometry.Height, cells);
    byte[] bytes = MapDocument.Encode(map);
    var loaded = MapDocument.Decode(bytes);
    Check(MapDocument.Encode(loaded.Map).SequenceEqual(bytes), "Editable modern map round trip");
    Check(loaded.Map.Cells.All(c => c.Owner == null && c.Terrain.OccupantCamp == null), "No political or runtime state");
    Check(loaded.Map.Cells.Count(c => c.BuildingKind != null) == sites.Length, "No building loss in map document");
    File.WriteAllBytes(Path.Combine(folder, key + ".map.json"), bytes);
    results.Add(new { key, cells = cells.Length, buildings = sites.Length, bytes = bytes.Length,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), geographicSpacingExceptions = observed.Count, narrowCoastOrIsland = islandExceptions,
        seconds = Math.Round(timer.Elapsed.TotalSeconds, 2) });
}
var report = new { checks, errors = 0, scope = "Native terrain import, spacing, movement and neutral MapDocument; not scenario or Android integration", maps = results };
File.WriteAllText(Path.Combine(root, "maps/map_source_verification_20261026.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
