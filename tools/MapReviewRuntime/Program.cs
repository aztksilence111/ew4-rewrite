using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;
using Ew4.Persistence;

string root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? ".");
string data = Path.GetFullPath(Path.Combine(root, "rewrite/data"));
string work = Path.Combine(root, "build/map-review-tests-20261027");
string runtime = Path.Combine(root, "build/map-review-runtime-20261027");
string source = Path.Combine(work, "source");
string[] scenarios = ["conquest7", "conquest8", "conquest9"];
byte[] Read(string p) => File.ReadAllBytes(p.StartsWith("frontend/", StringComparison.Ordinal)
    ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p));
string[] files = new[] { "original", "legacy", "client", "rewrite" }.SelectMany(part =>
    Directory.EnumerateFiles(Path.Combine(data, part), "*", SearchOption.AllDirectories)
        .Where(p => part != "legacy" || Path.GetDirectoryName(p) == Path.Combine(data, part))
        .Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')))
    .Concat(["frontend/conquest-pages.json", "frontend/city-names.json"]).ToArray();
var baseline = new ModBase(Read, files);
int checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new InvalidDataException(label); }
void Reject(Action action, string label)
{
    bool failed = false;
    try { action(); } catch (RuleViolation) { failed = true; }
    Check(failed, label);
}
var before = files.ToDictionary(p => p, p => Convert.ToHexStringLower(SHA256.HashData(Read(p))));
var store = new ModStore(baseline, Path.Combine(runtime, "mods"), Path.Combine(runtime, "saves"));
store.Seal(source, "three-map-review", "东亚·日本·神罗地图检阅", "1.0.1", baseline.Scenarios.Concat(scenarios).ToArray(),
    ["三张地图的检阅场景；原版国家和两支部队仅作占位，尚未设定历史势力与辖区。"]);
Console.WriteLine("Validating combined Mod and all existing scenarios");
var manifest = store.Validate(source);
byte[] Combined(string p) => manifest.Files.ContainsKey(p) ? File.ReadAllBytes(Path.Combine(source, p)) : Read(p);
var content = baseline.Content(Combined, manifest.Scenarios);
Check(content.Catalogs.Buildings.Definitions[4].MaximumUpgradeLevel == 3
    && new[] { 1, 2, 3 }.Select(level => content.Catalogs.Buildings.Income(4, level).Food).SequenceEqual([5L, 15L, 40L]),
    "Native farmland ceiling and three food incomes");
Check(content.Maps.Count == 5 && content.MapNames[4] == "japan" && content.MapNames[5] == "hre", "Five additive maps");
var results = new List<object>();
foreach (var (key, scenario, total, expected) in new[] { ("eastasia", "conquest7", 21488, 521),
    ("japan", "conquest8", 57939, 257), ("hre", "conquest9", 52461, 385) })
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var battle = content.Battle(scenario);
    var started = content.Start(scenario, "fra");
    var world = started.Session.World;
    Check(world.Cells.Count == total, "Full grid: " + key);
    int farms = key == "eastasia" ? 0 : JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "maps", key, "farmland.json"))).RootElement.GetProperty("records").GetArrayLength();
    Check(world.Cells.Count(c => c.BuildingKind != null) == expected + farms, "All reviewed buildings: " + key);
    Check(world.Cells.Where(c => c.BuildingKind != null).All(c => c.Owner == null), "Neutral buildings: " + key);
    Check(world.Units.Count == 2 && world.Units.All(u => u.Cell >= 0 && u.Cell < total), "Two valid placeholder units: " + key);
    Check(started.MapDiagnostics.Count == 0, "No silently discarded site records: " + key);
    if (key != "eastasia")
    {
        Check(battle.CellIndices != null && battle.Sites.Any(s => s.Cell > short.MaxValue), "Explicit large indices: " + key);
        Check(battle.ToBytes().SequenceEqual(Combined("original/"+scenario+".btl")), "Lossless extended BTL round trip");
        Check(LegacyBattle.Read(battle.WithCellIndices(battle.CellIndices!)).Sites.Select(s => s.Cell).SequenceEqual(battle.Sites.Select(s => s.Cell)), "CELL write/read");
        var indices = battle.CellIndices!;
        Reject(() => battle.WithCellIndices(indices with { BaseSha256 = new string('0', 64) }), "Reject changed body binding");
        Reject(() => battle.WithCellIndices(indices with { Sites = indices.Sites.Skip(1).ToArray() }), "Reject missing site mapping");
        Reject(() => battle.WithCellIndices(indices with { Armies = [-1, 0] }), "Reject negative full-map index");
        Reject(() => battle.WithCellIndices(indices with { Armies = [total, 0] }), "Reject out-of-map army");
        var differentMap = new WorldMapDefinition("wrong", "wrong", started.Map.Width+1, started.Map.Height,
            Enumerable.Repeat(started.Map.Cells[0], (started.Map.Width+1)*started.Map.Height).ToArray());
        Reject(() => LegacyScenarioMap.Import(differentMap, battle, world.Factions.Select(f => f.Id).ToArray(),
            LegacyBuildingCatalog.Kinds, LegacyRecruitmentCatalog.InstallationKinds), "Reject actual map dimension mismatch");
        var corrupted = battle.ToBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(corrupted.AsSpan(battle.LegacyLength+20), 2);
        Reject(() => LegacyBattle.Read(corrupted), "Reject unsupported required CELL version");
        using var planned = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "maps", key, "buildings.json")));
        foreach (var site in planned.RootElement.EnumerateArray())
        {
            int cell = site.GetProperty("cell").GetInt32();
            Check(world.Cells[cell].Name == site.GetProperty("name").GetString()
                && world.Cells[cell].BuildingKind == site.GetProperty("kind").GetString()
                && world.Cells[cell].BuildingLevel == site.GetProperty("level").GetInt32(), "Full site identity " + cell);
            if (site.GetProperty("kind").GetString() == "port")
                Check(battle.Sites.Single(s => s.Cell == cell).State7 == site.GetProperty("building_style").GetInt32(), "Native shore-facing port " + cell);
        }
        Check(world.Cells.Where(c => c.BuildingKind == "city").Select(c => c.BuildingLevel).Distinct().Order().SequenceEqual([1, 2, 3, 4, 5, 6, 7]),
            "Seven explicit initial city tiers: " + key);
        using var plannedFarms = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "maps", key, "farmland.json")));
        foreach (var farm in plannedFarms.RootElement.GetProperty("records").EnumerateArray())
        {
            int cell = farm.GetProperty("cell").GetInt32(), level = farm.GetProperty("level").GetInt32();
            Check(world.Cells[cell].BuildingKind == "farmland" && world.Cells[cell].BuildingLevel == level
                && !world.Cells[cell].Terrain.Sea, "Real farmland tier and dry cell " + cell);
        }
        Check(world.Cells.Where(c => c.BuildingKind == "farmland").Select(c => c.BuildingLevel).Distinct().Order().SequenceEqual([1, 2, 3]),
            "All three native farmland tiers: " + key);
        var plannedMap = MapDocument.Read(Path.Combine(root, "maps", key, key+".map.json")).Map;
        for (int i = 0; i < total; i++)
            Check(world.Cells[i].Terrain == plannedMap.Cells[i].Terrain && world.Cells[i].DefenseTerrain == plannedMap.Cells[i].DefenseTerrain
                && world.Cells[i].BuildingKind == plannedMap.Cells[i].BuildingKind && world.Cells[i].BuildingLevel == plannedMap.Cells[i].BuildingLevel,
                "Live game terrain equals approved movement/defense/river/bridge properties: " + i);
    }
    var encoded = SessionSave.Encode(started.Session);
    var restored = SessionSave.Decode(encoded, new(started.Session.ContentRevision, world.ContentRevision, started.Session.Account.ContentRevision));
    Check(SessionSave.Encode(restored).SequenceEqual(encoded), "Session save/load round trip: " + key);
    var mover = world.Units.First(u => u.Owner == started.Session.PlayerFaction);
    var cells = world.Cells.Select(c => c.Terrain).ToArray();
    var reachable = Movement.Reachable(new(world.Width, world.Height), cells, mover.Cell,
        new MovementActor { Camp = "review", FullBudget = 15, Remaining = 15 });
    Check(reachable.Count > 1, "Actual terrain-aware movement: " + key);
    results.Add(new { key, total, buildings = expected + farms, farms,
        farmLevels = world.Cells.Where(c => c.BuildingKind == "farmland").GroupBy(c => c.BuildingLevel).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
        cityLevels = world.Cells.Where(c => c.BuildingKind == "city").GroupBy(c => c.BuildingLevel).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()), sessionBytes = encoded.Length,
        highSites = battle.Sites.Count(s => s.Cell > short.MaxValue), seconds = timer.Elapsed.TotalSeconds });
    Console.WriteLine(key+": startup, terrain and saves verified");
}
foreach (string scenario in baseline.Scenarios)
    Check(LegacyBattle.Read(Read("original/"+scenario+".btl")).ToBytes().SequenceEqual(Read("original/"+scenario+".btl")), "Original BTL preserved " + scenario);
Console.WriteLine("Installing the combined review Mod in the independent preview profile");
var installed = store.Import(source, "three-map-review", "东亚·日本·神罗地图检阅");
store.Activate(installed.Key);
string package = Path.Combine(root, "rewrite/content/three-map-review.ew4mod");
string exported = Path.Combine(work, "three-map-review.ew4mod");
if (File.Exists(exported)) File.Delete(exported);
store.Export(installed.Key, exported);
File.Move(exported, package, true);
var reimport = new ModStore(baseline, Path.Combine(work, "reimport/mods"), Path.Combine(work, "reimport/saves"));
var imported = reimport.Import(package, "three-map-review", "东亚·日本·神罗地图检阅");
reimport.Activate(imported.Key);
foreach (string scenario in scenarios)
{
    using var response = JsonDocument.Parse(reimport.Dispatch("/api/start", JsonSerializer.Serialize(new { scenario, country = "fra" })));
    Check(response.RootElement.GetProperty("ok").GetBoolean(), "Exported package client start: " + scenario);
}
foreach (var (p, hash) in before) Check(Convert.ToHexStringLower(SHA256.HashData(Read(p))) == hash, "No baseline writes during verification: " + p);
var report = new { checks, errors = 0, maps = results, package, packageBytes = new FileInfo(package).Length,
    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(package))), activeKey = installed.Key,
    baseRevision = baseline.Revision, preview = runtime };
File.WriteAllText(Path.Combine(root, "maps/japan_hre_source_20261026/game-integration-verification.json"),
    JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
