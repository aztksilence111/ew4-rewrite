using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;
using Ew4.Persistence;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
string baselineFolder = Path.GetFullPath(Path.Combine(root, "rewrite/data"));
string source = Path.Combine(root, "rewrite/content/eastasia");
string reports = Path.Combine(root, "maps/eastasia_rewrite");
bool builtin = args.Contains("--builtin");
string work = Path.Combine(root, "rewrite/local/eastasia-validation", builtin ? "builtin" : "overlay");
Directory.CreateDirectory(work);
if (builtin)
{
    string staged = source;
    source = Path.Combine(work, "inspection-source");
    Directory.CreateDirectory(Path.Combine(source, "original"));
    foreach (string file in new[] { "conquest7.btl", "conquest7.names.xml", "def_battlelist.xml" })
        File.Copy(Path.Combine(staged, "original", file), Path.Combine(source, "original", file), true);
    Directory.CreateDirectory(Path.Combine(source, "frontend"));
    File.Copy(Path.Combine(staged, "frontend/conquest-pages.json"), Path.Combine(source, "frontend/conquest-pages.json"), true);
}
var watch = Stopwatch.StartNew();
int checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new InvalidDataException(message);
}
byte[] BaselineRead(string path) => File.ReadAllBytes(path.StartsWith("frontend/", StringComparison.Ordinal)
    ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", path[9..]) : Path.Combine(baselineFolder, path));
var files = new[] { "original", "legacy", "client", "rewrite" }
    .SelectMany(part => Directory.EnumerateFiles(Path.Combine(baselineFolder, part), "*", SearchOption.AllDirectories)
        .Where(p => part != "legacy" || Path.GetDirectoryName(p) == Path.Combine(baselineFolder, part))
        .Select(p => Path.GetRelativePath(baselineFolder, p).Replace('\\', '/')))
    .Concat(["frontend/conquest-pages.json", "frontend/city-names.json"]);
var baseline = new ModBase(BaselineRead, files);
using var cityDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reports, "cities.json")));
int expectedCities = cityDocument.RootElement.GetArrayLength();
if (builtin)
{
    var template = MapDocument.Decode(baseline.Read("original/eastasia.map.json")).Map;
    Check(template.Cells.Count(c => c.Name != null) == expectedCities && template.Cells.All(c => c.Owner == null),
        "Built-in reusable settlements must be complete and politically neutral");
    Check(baseline.Scenarios.Length == 95 && baseline.Content(baseline.Read, baseline.Scenarios).MapNames[3] == "eastasia",
        "Built-in map is available without adding a formal scenario");
}
var before = new[] { "original/europe.bin", "original/america.bin", "original/def_map.xml", "client/manifest.json", "legacy/catalog.json" }
    .ToDictionary(p => p, p => SHA256.HashData(baseline.Read(p)));
var store = new ModStore(baseline, Path.Combine(work, "mods"), Path.Combine(work, "saves"));
using var facilityDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reports, "facilities.json")));
using var bridgeDocument = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(reports, "bridges.json")));
int expectedFacilities = facilityDocument.RootElement.GetArrayLength();
store.Seal(source, "eastasia-map", "东亚底图与城市检查", "0.1.0", baseline.Scenarios.Append("conquest7").ToArray(),
    ["地图检查场景使用原版国家和兵种占位，不代表历史势力或正式剧本。"]);
Console.WriteLine("Validating additive content and scenario startup");
var manifest = store.Validate(source);
Check(manifest.Scenarios.Length == baseline.Scenarios.Length + 1, "Original scenarios must remain registered");
byte[] Read(string path) => manifest.Files.ContainsKey(path) ? File.ReadAllBytes(Path.Combine(source, path)) : baseline.Read(path);
var content = baseline.Content(Read, manifest.Scenarios);
Check(content.Maps.Count == 3 && content.MapNames[3] == "eastasia", "Additive third map registration");
var terrain = content.Maps[3];
Check(terrain.Width == 158 && terrain.Height == 136 && terrain.Cells.Count == 21488, "Expanded map dimensions");
var geometry = LegacyMapDefinitions.Import(terrain, content.Catalogs.Terrain, "eastasia", "东亚");
Check(geometry.Cells.Count == 21488, "Resolve every binary terrain record using real movement rules");
var terrainCounts = terrain.Cells.GroupBy(c => c.BackgroundKind).ToDictionary(g => (int)g.Key, g => g.Count());
foreach (var group in terrain.Cells.Select((cell, index) => (cell, index)).GroupBy(p => p.cell.BackgroundKind))
{
    string kind = content.Catalogs.Terrain.Definitions[group.Key].Kind;
    Check(group.All(p => geometry.Cells[p.index].Terrain.TerrainCost == content.Catalogs.Terrain.MovementCosts[kind]
        && geometry.Cells[p.index].DefenseTerrain == kind), "Physical terrain must affect actual movement and defense: " + kind);
}
Check(geometry.Cells.All(c => c.Owner == null), "Base map must not fix political ownership");
var grid = new HexMap(geometry.Width, geometry.Height);
for (int i = 0; i < geometry.Cells.Count; i++)
{
    var cell = geometry.Cells[i];
    for (int direction = 0; direction < 6; direction++)
        if ((cell.Terrain.RiverEdges & 1 << direction) != 0)
        {
            int neighbor = grid.Neighbor(i, direction);
            Check(neighbor >= 0 && (geometry.Cells[neighbor].Terrain.RiverEdges & 1 << ((direction + 3) % 6)) != 0,
                "River edges must agree with the core hex topology");
        }
}
var start = content.Start("conquest7", "fra");
Check(start.Session.World.Cells.Where(c => c.Name != null).All(c => c.Owner == null), "Inspection scene must leave city ownership unset");
Check(start.Session.World.Cells.Count(c => c.Name != null) == expectedCities, "All named cities must survive real scenario import");
Check(start.Session.World.Cells.Where(c => c.Name != null).All(c => !c.Terrain.Sea), "No city may be a water cell");
Check(start.Session.World.Units.Count == 2, "Two diagnostic units, no historical army deployment");
var world = start.Session.World;
if (builtin)
{
    Check(world.Cells.Count(c => c.Terrain.Barrier == TerrainBarrier.Mountain) == 121,
        "Real inspection scenario applies Himalayan barriers");
    Check(start.Map.Cells.Select(c => c.Terrain.Barrier).SequenceEqual(
        MapDocument.Decode(baseline.Read("original/eastasia.map.json")).Map.Cells.Select(c => c.Terrain.Barrier)),
        "Modern reusable template and live scenario agree on barriers");
}
var siteCells = new HashSet<int>();
foreach (var city in cityDocument.RootElement.EnumerateArray())
{
    int cell = city.GetProperty("cell").GetInt32(), level = city.GetProperty("building_level").GetInt32();
    string expectedKind = city.TryGetProperty("kind", out var namedKind) ? namedKind.GetString()! : "city";
    int buildingKind = city.TryGetProperty("building_kind", out var namedBuilding) ? namedBuilding.GetInt32() : 0;
    Check(siteCells.Add(cell), "Unique city cell");
    Check(world.Cells[cell].Name == city.GetProperty("name").GetString()
        && world.Cells[cell].BuildingKind == expectedKind && world.Cells[cell].BuildingLevel == level,
        "Actual city name and initial level: " + city.GetProperty("name").GetString());
    Check(level >= 1 && level <= content.Catalogs.Buildings.Definitions[buildingKind].Levels.Count, "Named site level has native rules and artwork: " + city.GetProperty("name").GetString());
    Check(TerritorialJurisdictions.IsCenter(world.Cells[cell]), "Named site remains a jurisdiction center");
    if (expectedKind is "industry" or "stable")
        Check(content.Catalogs.Buildings.Level(buildingKind, level)!.Recruitment.Count > 0, "Named factory or stable provides actual recruitment");
}
int magdagachi = cityDocument.RootElement.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "马格达加奇").GetProperty("cell").GetInt32();
var districtWorld = world with { Cells = world.Cells.Select(c => c with { Owner = c.Terrain.Sea ? null : "fra" }).ToArray() };
var districts = TerritorialJurisdictions.Create(districtWorld);
Check(districts.CellCenters[magdagachi] == magdagachi && districts.CellCenters.Count(c => c == magdagachi) > 10,
    "Magdagachi forms a nontrivial northern district in a unified ownership fixture");
foreach (var site in cityDocument.RootElement.EnumerateArray())
{
    int cell = site.GetProperty("cell").GetInt32();
    Check(districts.CellCenters[cell] == cell, "Converted named site retains its own district");
}
Check(world.Cells.Any(c => c.BuildingKind == "city" && c.BuildingLevel == content.Catalogs.Buildings.Definitions[0].Levels.Count),
    "Include starting maximum-level cities like native Europe");
Check(world.Cells.Where(c => c.Name != null).All(c => c.Name!.IndexOfAny(['/', '(', ')', '（', '）']) < 0), "Base city display names cannot contain combined alternatives");
var ocean = new HashSet<int>();
var oceanQueue = new Queue<int>();
for (int i = 0; i < geometry.Cells.Count; i++)
    if (geometry.Cells[i].Terrain.Sea && (i / grid.Width == 0 || i / grid.Width == grid.Height - 1 || i % grid.Width == 0 || i % grid.Width == grid.Width - 1))
    { ocean.Add(i); oceanQueue.Enqueue(i); }
while (oceanQueue.Count > 0)
{
    int cell = oceanQueue.Dequeue();
    for (int direction = 0; direction < 6; direction++)
    {
        int next = grid.Neighbor(cell, direction);
        if (next >= 0 && geometry.Cells[next].Terrain.Sea && ocean.Add(next)) oceanQueue.Enqueue(next);
    }
}
foreach (var facility in facilityDocument.RootElement.EnumerateArray())
{
    int cell = facility.GetProperty("cell").GetInt32(), anchor = facility.GetProperty("anchor_cell").GetInt32();
    int kind = facility.GetProperty("building_kind").GetInt32(), level = facility.GetProperty("level").GetInt32();
    string type = facility.GetProperty("kind").GetString()!;
    Check(siteCells.Add(cell), "Facilities cannot overwrite cities or each other");
    Check(world.Cells[cell].BuildingKind == type && world.Cells[cell].BuildingLevel == level
        && world.Cells[cell].Owner == null && world.Cells[cell].Name == null, "Actual facility type, level and unset owner");
    Check(level >= 1 && level <= content.Catalogs.Buildings.Definitions[kind].MaximumUpgradeLevel,
        "Facility level has native economic and recruitment rules");
    var nearby = new Dictionary<int, int> { [anchor] = 0 };
    int expectedDistance = facility.GetProperty("distance_hexes").GetInt32();
    var queue = new Queue<int>(); queue.Enqueue(anchor);
    while (queue.Count > 0)
    {
        int here = queue.Dequeue();
        if (nearby[here] == expectedDistance) continue;
        for (int direction = 0; direction < 6; direction++)
        {
            int next = grid.Neighbor(here, direction);
            if (next >= 0 && nearby.TryAdd(next, nearby[here] + 1)) queue.Enqueue(next);
        }
    }
    Check(nearby.TryGetValue(cell, out int distance) && distance == expectedDistance,
        "Facility reference distance must match the actual hex topology");
    Check(expectedDistance <= (type == "port" ? 3 : 10), "Regional sites stay within the configured distribution area");
    if (type != "port")
    {
        var landRoute = new Dictionary<int, int> { [anchor] = 0 };
        var landQueue = new Queue<int>(); landQueue.Enqueue(anchor);
        int pathLength = facility.GetProperty("land_path_hexes").GetInt32();
        while (landQueue.Count > 0)
        {
            int here = landQueue.Dequeue();
            if (landRoute[here] == pathLength) continue;
            for (int d = 0; d < 6; d++)
            {
                int next = grid.Neighbor(here, d);
                if (next >= 0 && !geometry.Cells[next].Terrain.Sea && landRoute.TryAdd(next, landRoute[here] + 1))
                    landQueue.Enqueue(next);
            }
        }
        Check(landRoute.TryGetValue(cell, out int steps) && steps == pathLength, "Regional facility has a real land route");
    }
    Check(type == "port" ? ocean.Contains(cell) && Enumerable.Range(0, 6).Select(d => grid.Neighbor(cell, d))
            .Any(n => n >= 0 && terrain.Cells[n].BackgroundKind == 0)
        : terrain.Cells[cell].BackgroundKind == 0, "Ports need ocean access and adjacent open land; land facilities need plains");
    if (type == "port")
    {
        int style = facility.GetProperty("building_style").GetInt32();
        var adjacentLand = Enumerable.Range(0, 6).Select(d => grid.Neighbor(cell, d)).Where(n => n >= 0 && !geometry.Cells[n].Terrain.Sea);
        Check(style is >= 1 and <= 4 && adjacentLand.Any(n => style switch {
            1 => n / grid.Width < cell / grid.Width, 4 => n / grid.Width > cell / grid.Width,
            2 => n % grid.Width * 64 + n / grid.Width % 2 * 32 > cell % grid.Width * 64 + cell / grid.Width % 2 * 32,
            _ => n % grid.Width * 64 + n / grid.Width % 2 * 32 < cell % grid.Width * 64 + cell / grid.Width % 2 * 32 }),
            "Port land-facing sprite must match an adjacent shore");
    }
    var levelRules = content.Catalogs.Buildings.Level(kind, level)!;
    Check(type == "farmland" ? levelRules.Turn.Income.Food > 0 : levelRules.Recruitment.Count > 0,
        "Facility supports native food production or recruitment");
}
Check(world.Cells.Count(c => c.BuildingKind != null) == expectedCities + expectedFacilities, "All requested sites survive startup");
Check(world.Units.All(u => !siteCells.Contains(u.Cell)), "Diagnostic units cannot preclaim an economic site");
var luzonAnchors = cityDocument.RootElement.EnumerateArray().Where(c => c.GetProperty("name").GetString() is "拉瓦格" or "阿帕里")
    .Select(c => c.GetProperty("cell").GetInt32()).ToHashSet();
var luzon = new HashSet<int>(luzonAnchors); var islandQueue = new Queue<int>(luzon);
while (islandQueue.Count > 0)
{
    int cell = islandQueue.Dequeue();
    for (int direction = 0; direction < 6; direction++)
    {
        int n = grid.Neighbor(cell, direction);
        if (n >= 0 && !geometry.Cells[n].Terrain.Sea && luzon.Add(n)) islandQueue.Enqueue(n);
    }
}
foreach (string kind in new[] { "industry", "stable", "farmland" })
    Check(facilityDocument.RootElement.EnumerateArray().Any(f => f.GetProperty("kind").GetString() == kind
        && luzonAnchors.Contains(f.GetProperty("anchor_cell").GetInt32()) && luzon.Contains(f.GetProperty("cell").GetInt32())),
        "Luzon colonial forces need local land-connected " + kind);
var luzonLandSites = facilityDocument.RootElement.EnumerateArray().Where(f => f.GetProperty("kind").GetString() != "port"
    && luzonAnchors.Contains(f.GetProperty("anchor_cell").GetInt32())).ToArray();
Check(luzonLandSites.All(f => luzonAnchors.All(c => grid.Distance(c, f.GetProperty("cell").GetInt32()) >= 2)),
    "Luzon facilities fill island gaps instead of adjoining the two cities");
Check(luzonLandSites.Max(f => f.GetProperty("row").GetInt32()) - luzonLandSites.Min(f => f.GetProperty("row").GetInt32()) >= 6,
    "Luzon facilities extend across the island interior and south");
foreach (var bridge in bridgeDocument.RootElement.EnumerateArray())
{
    int a = bridge.GetProperty("cell").GetInt32(), b = bridge.GetProperty("other").GetInt32(), d = bridge.GetProperty("direction").GetInt32();
    var first = world.Cells[a].Terrain with { OccupantCamp = null };
    var second = world.Cells[b].Terrain with { OccupantCamp = null };
    Check(grid.Neighbor(a, d) == b && (first.RiverEdges & 1 << d) != 0 && (second.RiverEdges & 1 << (d + 3)) != 0,
        "Bridge spans a real reciprocal river barrier");
    Check((first.BridgeEdges & 1 << d) != 0 && (second.BridgeEdges & 1 << (d + 3)) != 0, "Bridge is functional in both directions");
    var traveler = new MovementActor { Camp = "map-check", Category = "infantry", FullBudget = 15, Remaining = 15 };
    var rules = new MovementRules();
    Check(Movement.EntryCost(first, second with { BridgeEdges = 0 }, d + 3, traveler, rules) == 15
        && Movement.EntryCost(second, first with { BridgeEdges = 0 }, d, traveler, rules) == 15,
        "Unbridged river crossing consumes a full move in both directions");
    Check(Movement.EntryCost(first, second, d + 3, traveler, rules) == 3
        && Movement.EntryCost(second, first, d, traveler, rules) == 3, "Bridge removes the river movement penalty");
}
int beijing = cityDocument.RootElement.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "北京").GetProperty("cell").GetInt32();
int beijingId = cityDocument.RootElement.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "北京").GetProperty("city_id").GetInt32();
byte[] renamedXml = System.Text.Encoding.UTF8.GetBytes($"<citynames><city id=\"{beijingId}\" name=\"北平\"/></citynames>");
var renamedContent = baseline.Content(path => path == "original/conquest7.names.xml" ? renamedXml : Read(path), manifest.Scenarios);
var renamed = renamedContent.Start("conquest7", "fra");
Check(renamed.Session.World.Cells[beijing].Name == "北平" && world.Cells[beijing].Name == "北京" && renamedContent.Cities[beijingId] == "北京",
    "Scenario override changes only its own display name, preserving the base");
Check(renamed.Session.World.Cells.Where((c,i) => i != beijing).Select(c => c.Name).SequenceEqual(world.Cells.Where((c,i) => i != beijing).Select(c => c.Name)),
    "Unspecified scenario names fall back to base names");
Check(renamed.Session.World.ContentRevision != world.ContentRevision, "Scenario names participate in world content fingerprints");
var clearedNames = Ew4.Client.ScenarioCityNames.Read("<citynames/>", content.Cities);
Check(clearedNames[beijingId] == "北京", "Empty scenario name table restores base name");
foreach (string invalid in new[] { "<citynames><city id=\"-1\" name=\"X\"/></citynames>",
    $"<citynames><city id=\"{beijingId}\" name=\"X\"/><city id=\"{beijingId}\" name=\"Y\"/></citynames>" })
{
    bool rejected = false;
    try { Ew4.Client.ScenarioCityNames.Read(invalid, content.Cities); } catch (RuleViolation error) when (error.Code == "client.city_names") { rejected = true; }
    Check(rejected, "Unknown and duplicate scenario city IDs must be rejected");
}
// Export reusable city/facility geography without the inspection scene's two unit-cell owners.
var settledMap = new WorldMapDefinition(geometry.Id, geometry.Name, geometry.Width, geometry.Height,
    geometry.Cells.Select((cell, i) => cell with { Name = world.Cells[i].Name,
        BuildingKind = world.Cells[i].BuildingKind, BuildingLevel = world.Cells[i].BuildingLevel,
        Terrain = cell.Terrain with { BridgeEdges = world.Cells[i].Terrain.BridgeEdges } }).ToArray(), geometry.Images);
var mapBytes = MapDocument.Encode(settledMap);
var loaded = MapDocument.Decode(mapBytes);
Check(MapDocument.Encode(loaded.Map).SequenceEqual(mapBytes), "Modern editable map JSON round trip");
Check(loaded.Map.Cells.All(c => c.Owner == null) && loaded.Map.Cells.Count(c => c.BuildingKind != null) == siteCells.Count,
    "Reusable base map preserves all settlements without political ownership");
File.WriteAllBytes(Path.Combine(source, "eastasia.map.json"), mapBytes);
byte[] saved = SessionSave.Encode(start.Session);
var restored = SessionSave.Decode(saved, new(start.Session.ContentRevision, start.Session.World.ContentRevision, start.Session.Account.ContentRevision));
Check(SessionSave.Encode(restored).SequenceEqual(saved), "Large-map session save round trip");
var buildings = start.Session.World.Cells.Where(c => c.Name != null).Select(c => c.Name).ToHashSet();
foreach (string city in new[] { "上海", "香港", "澳门", "马公", "丰原", "亚历山德罗夫斯克", "南浦", "咸兴", "海州", "大田", "光州", "木浦", "勒克瑙", "巴特那", "古瓦哈提", "那格浦尔", "维沙卡帕特南" })
    Check(buildings.Contains(city), "Missing approved coastal or recent city: " + city);
// Exercise normal pathfinding on the larger imported grid, independently of rendering.
var movement = settledMap.Cells.Select(c => c.Terrain).ToArray();
var landCells = movement.Select((c,i) => (c,i)).Where(p => !p.c.Sea).Select(p => p.i).ToArray();
var actor = new MovementActor { Camp = "map-check", FullBudget = 15, Remaining = 15 };
var distances = Movement.DistancesTo(grid, movement, [landCells[landCells.Length / 2]], actor);
Check(distances.Count > 1000, "Expanded map must support normal terrain-aware pathfinding");
Console.WriteLine("Importing and enabling only the isolated verification profile");
var info = store.Import(source, "eastasia-map", "东亚底图与城市检查");
store.Activate(info.Key);
using (var response = JsonDocument.Parse(store.Dispatch("/api/start", "{\"scenario\":\"conquest7\",\"country\":\"fra\"}")))
{
    Check(response.RootElement.GetProperty("ok").GetBoolean(), "Client startup: " + response.RootElement.ToString()[..Math.Min(response.RootElement.ToString().Length, 180)]);
    var state = response.RootElement.GetProperty("data");
    Check(state.GetProperty("map").GetString() == "eastasia" && state.GetProperty("cells").GetArrayLength() == 21488, "Client map state and presentation binding");
}
foreach (var (path, hash) in before) Check(SHA256.HashData(baseline.Read(path)).SequenceEqual(hash), "Baseline content changed: " + path);
string archive = Path.Combine(root, "rewrite/content", builtin ? "eastasia-test-conquest.ew4mod" : "eastasia-map.ew4mod");
if (File.Exists(archive))
{
    string old = archive + ".previous";
    File.Move(archive, old, true);
}
store.Export(info.Key, archive);
var result = new { checks, errors = 0, maps = 3, cells = 21488, cities = expectedCities, scenarios = manifest.Scenarios.Length,
    facilities = expectedFacilities,
    bridges = bridgeDocument.RootElement.GetArrayLength(),
    nativeInitialMaximumCityLevel = new { europe = content.Battle("conquest1").Sites.Where(s => s.BuildingKind == 0).Max(s => s.BuildingLevel),
        america = content.Battle("conquest2").Sites.Where(s => s.BuildingKind == 0).Max(s => s.BuildingLevel) },
    cityLevels = world.Cells.Where(c => c.BuildingKind == "city").GroupBy(c => c.BuildingLevel).ToDictionary(g => g.Key, g => g.Count()),
    facilityCounts = world.Cells.Where(c => c.BuildingKind != null && c.BuildingKind != "city").GroupBy(c => c.BuildingKind!).ToDictionary(g => g.Key, g => g.Count()),
    terrainCounts,
    riverEdges = geometry.Cells.Sum(c => System.Numerics.BitOperations.PopCount(c.Terrain.RiverEdges)),
    mapDocumentBytes = mapBytes.Length, sessionBytes = saved.Length, pathfindingReachableCells = distances.Count,
    elapsedSeconds = Math.Round(watch.Elapsed.TotalSeconds, 2), package = archive, packageBytes = new FileInfo(archive).Length,
    packageSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))), activeKey = info.Key,
    previewSaves = Path.Combine(work, "saves"), previewMods = Path.Combine(work, "mods") };
File.WriteAllText(Path.Combine(builtin ? work : reports, "runtime-verification.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));
