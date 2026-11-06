using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;
using Ew4.Client;
using Ew4.Persistence;

string home = Path.GetFullPath(args[0]), workspace = Path.GetFullPath(args[1]);
string Inside(string relative) {
    string path = Path.GetFullPath(Path.Combine(home, relative));
    if (!path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Output escapes isolation");
    return path;
}
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
void Put(string relative, byte[] data) {
    string path = Inside(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    string pending = path + ".pending-" + Guid.NewGuid().ToString("N"); File.WriteAllBytes(pending, data);
    for (int attempt = 0; ; attempt++) {
        try { File.Move(pending, path, true); break; }
        catch (IOException) when (attempt < 12) { Thread.Sleep(250); }
    }
}
void Text(string relative, string value) => Put(relative, Encoding.UTF8.GetBytes(value));
void Json(string relative, object value) => Put(relative, JsonSerializer.SerializeToUtf8Bytes(value, json));
T Load<T>(string relative) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(Inside(relative)), json)!;
string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
var inputs = new Dictionary<string, string>();
byte[] Reference(string relative) {
    string path = Path.Combine(workspace, relative);
    byte[] data = File.ReadAllBytes(path); inputs[relative] = Hash(data); return data;
}
var cities = JsonSerializer.Deserialize<City[]>(Reference("maps/eastasia_rewrite/cities.json"), json)!;
var facilities = JsonSerializer.Deserialize<Facility[]>(Reference("maps/eastasia_rewrite/facilities.json"), json)!;
var bridges = JsonNode.Parse(Reference("maps/eastasia_rewrite/bridges.json"))!.AsArray();
var byName = cities.GroupBy(c => c.Name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
foreach (var c in cities) byName.Add(c.Group + "/" + c.Name, c);
var geo = Load<Geography>("source/geography.json");
var scenarios = Load<Conquest[]>("source/conquests.json");
var rosterRules = JsonNode.Parse(File.ReadAllBytes(Inside("source/roster-rules.json")))!;
var generals = new List<General>();
if (File.Exists(Inside("source/generals.json"))) generals.AddRange(Load<General[]>("source/generals.json"));
else {
    string plan = Encoding.UTF8.GetString(Reference("颓垣饿鸠-策划/GENERALS.md"));
    int id = 1001;
    foreach (string line in plan.Split('\n').Where(l => Regex.IsMatch(l, @"^\| J\d+ \|"))) {
        string[] columns = line.Split('|').Select(c => c.Trim()).ToArray();
        foreach (var (column, tier) in new[] { (3, 3), (4, 2), (5, 1) })
            foreach (string name in columns[column].Split('、').Where(n => n != "—" && n.Length > 0))
                generals.Add(new() { Id = id++, Name = name, Group = columns[1], Tier = tier, Note = columns[6] });
    }
    foreach (var extra in rosterRules["extras"]!.AsArray()) generals.Add(new() { Id = id++, Name = (string)extra!["name"]!, Group = (string)extra["group"]!,
        Tier = (int)extra["tier"]!, From = (int)extra["from"]!, Until = (int)extra["until"]!, Note = (string)extra["note"]! });
    foreach (var g in generals) {
        if (rosterRules["availability"]?[g.Name] is JsonArray span) { g.From = (int)span[0]!; g.Until = (int)span[1]!; }
        foreach (var role in rosterRules["roles"]!.AsObject()) if (role.Value!.AsArray().Any(n => (string?)n == g.Name)) g.Role = role.Key;
    }
    Json("source/generals.json", generals);
}
var people = generals.ToDictionary(g => g.Name);
Assert(generals.Count >= 208 && generals.Select(g => g.Id).Distinct().Count() == generals.Count, "Roster unique identities");
Assert(generals.All(g => g.Tier is >= 1 and <= 3), "Roster recruitment groups");

string data = Inside("runtime/data"), overlay = Inside("build/overlay");
byte[] ReadBase(string path) => File.ReadAllBytes(path.StartsWith("frontend/") ? Inside("runtime/wwwroot/" + path[9..]) : Path.Combine(data, path));
var baseFiles = new[] { "original", "legacy", "client" }.SelectMany(part => Directory.EnumerateFiles(Path.Combine(data, part), "*", SearchOption.AllDirectories)
    .Where(p => part != "legacy" || Path.GetDirectoryName(p) == Path.Combine(data, part)).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')))
    .Concat(new[] { "frontend/conquest-pages.json", "frontend/city-names.json" }).ToArray();
var baseline = new ModBase(ReadBase, baseFiles);
void Asset(string path, byte[] bytes) => Put("build/overlay/" + path, bytes);
void Xml(string path, XDocument doc) => Asset("original/" + path, Encoding.UTF8.GetBytes(doc.ToString()));
void AssetJson(string path, object value) => Asset(path, JsonSerializer.SerializeToUtf8Bytes(value, json));
var catalog = JsonNode.Parse(ReadBase("legacy/catalog.json"))!;
var manifest = JsonNode.Parse(ReadBase("client/manifest.json"))!;
var strings = new StringBuilder(Encoding.UTF8.GetString(ReadBase("original/stringtable_cn.ini")).TrimStart('\uFEFF'));
var commanderXml = new XElement("commanders");
foreach (var g in generals) {
    int ability = g.Tier + 1;
    var e = new XElement("commander", new XAttribute("id", g.Id), new XAttribute("name", "MG" + g.Id), new XAttribute("country", g.Group),
        new XAttribute("rank", g.Tier + 1), new XAttribute("nobilityrank", 0), new XAttribute("star", g.Tier * 3 - 1),
        new XAttribute("infantry", g.Role == "infantry" ? ability : g.Tier), new XAttribute("cavalry", g.Role == "cavalry" ? ability : 0),
        new XAttribute("artillery", g.Role == "artillery" ? ability : 1), new XAttribute("warship", 0), new XAttribute("fort", g.Tier),
        new XAttribute("business", g.Role == "logistics" ? ability : 1), new XAttribute("movement", Math.Min(g.Tier, 2)), new XAttribute("training", g.Tier),
        new XAttribute("skill1", -1), new XAttribute("skill2", -1), new XAttribute("skill3", -1), new XAttribute("skill4", -1),
        new XAttribute("item1", -1), new XAttribute("item2", -1), new XAttribute("drawlots", 1), new XAttribute("price", g.Tier * 700));
    commanderXml.Add(e); strings.AppendLine().Append("name_MG").Append(g.Id).Append('=').Append(g.Name);
}
Xml("def_commander.xml", new(commanderXml));
Xml("def_princess.xml", new(new XElement("princesses")));
Xml("def_battleline.xml", new(new XElement("battlelines")));
var tree = Tree(commanderXml);
catalog["source_trees"]!["def_commander.xml"] = tree;
catalog["commanders"] = new JsonArray(commanderXml.Elements().Select(e => (JsonNode)new JsonObject {
    ["id"] = "ew4.commander." + (string)e.Attribute("id")!, ["legacy_attributes"] = Attributes(e), ["skills"] = new JsonArray() }).ToArray());
manifest["commanders"] = new JsonObject(generals.Select(g => KeyValuePair.Create<string, JsonNode?>(g.Id.ToString(), JsonValue.Create("MG" + g.Id))));
var map = LegacyTerrainMap.Read(ReadBase("original/eastasia.bin"));
var geometry = LegacyMapDefinitions.Import(map, LegacyTerrainRules.FromXml(Encoding.UTF8.GetString(ReadBase("original/def_mapterrain.xml")).TrimStart('\uFEFF'), Encoding.UTF8.GetString(ReadBase("original/def_terraintype.xml")).TrimStart('\uFEFF')), "eastasia", "东亚");
var grid = new HexMap(map.Width, map.Height);
var regions = BuildRegions();
Json("build/cell-regions.json", regions);
var battles = new XElement("battlelist");
var allFactions = geo.Backgrounds.Concat(scenarios.SelectMany(s => s.Factions)).GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.Last());
foreach (var f in allFactions.Values) {
    strings.AppendLine().Append("name_").Append(f.Id).Append('=').Append(f.Name);
    AddFlag(f.Id);
}
var reports = new List<object>();
foreach (var scenario in scenarios) {
    Console.WriteLine("Authoring " + scenario.Id);
    var factions = scenario.Factions.Concat(geo.Backgrounds.Where(b => scenario.Factions.All(f => f.Id != b.Id))).ToArray();
    Assert(factions.Length < 255 && factions.Select(f => f.Id).Distinct().Count() == factions.Length, "Faction IDs " + scenario.Id);
    var indices = factions.Select((f, i) => (f.Id, Index: i)).ToDictionary(p => p.Id, p => p.Index);
    int Owner(City c) {
        string id = scenario.Cities.GetValueOrDefault(c.Group + "/" + c.Name) ?? scenario.Cities.GetValueOrDefault(c.Name) ?? scenario.Regions.GetValueOrDefault(c.Group) ?? geo.Cities.GetValueOrDefault(c.Name) ?? geo.Regions.GetValueOrDefault(c.Group) ?? scenario.DefaultOwner;
        Assert(indices.ContainsKey(id), scenario.Id + " unknown owner " + id + ":" + c.Name);
        return indices[id];
    }
    foreach (var key in scenario.Cities.Keys) Assert(byName.ContainsKey(key), "Unknown city override " + key);
    var owners = Enumerable.Repeat((byte)255, map.Cells.Count).ToArray();
    var cityOwners = cities.ToDictionary(c => c.Cell, Owner);
    // Political territory is resolved first, within historical administrative regions.
    var cityGroups = cities.GroupBy(c => c.Group).ToDictionary(g => g.Key, g => g.ToArray());
    for (int cell = 0; cell < owners.Length; cell++) {
        if (geometry.Cells[cell].Terrain.Sea) continue;
        City[] candidates = cityGroups.GetValueOrDefault(regions[cell]) ?? cities;
        var anchor = candidates.MinBy(c => Distance2(cell, c.Cell))!;
        owners[cell] = (byte)Owner(anchor);
    }
    foreach (var c in cities) owners[c.Cell] = (byte)Owner(c);
    foreach (var facility in facilities) {
        if (facility.Kind == "port") owners[facility.Cell] = owners[facility.AnchorCell];
        else if (owners[facility.Cell] == 255) owners[facility.Cell] = owners[facility.AnchorCell];
    }
    var sites = new Dictionary<int, byte[]>();
    foreach (var c in cities) sites[c.Cell] = Site(c.Cell, c.CityId, 0, c.BuildingLevel, c.BuildingStyle);
    foreach (var f in facilities) sites[f.Cell] = Site(f.Cell, -1, f.BuildingKind, f.Level, f.BuildingStyle);
    foreach (var bridge in bridges) foreach (var p in new[] { (Cell: (int)bridge!["cell"]!, Direction: (int)bridge["direction"]!), (Cell: (int)bridge!["other"]!, Direction: (int)bridge["direction"]! + 3) }) {
        if (!sites.ContainsKey(p.Cell)) sites[p.Cell] = Site(p.Cell, -1, 255, 0, 0);
        sites[p.Cell][11 + (int)bridge!["style"]!] |= (byte)(1 << p.Direction);
    }
    var deployed = new List<Deployment>();
    var occupied = new HashSet<int>();
    var placedPeople = new HashSet<string>();
    foreach (var f in factions) {
        Assert(byName.ContainsKey(f.Headquarters), "Unknown HQ: " + f.Headquarters);
        int index = indices[f.Id], anchor = byName[f.Headquarters].Cell;
        int total = f.Generals.Length + scenario.RuralBases.Where(b => b.Faction == f.Id).Sum(b => b.Generals.Length);
        Assert(f.Camp == 4 || total >= 2, scenario.Id + " needs two generals for " + f.Id);
        if (f.Camp != 4) Assert(owners[anchor] == index, scenario.Id + " HQ ownership " + f.Id);
        foreach (string name in f.Generals) PlaceGeneral(f, name, anchor, false);
        foreach (var c in cities.Where(c => cityOwners[c.Cell] == index))
            if (occupied.Add(c.Cell)) deployed.Add(new(c.Cell, 1, c.BuildingLevel >= 4 ? 1 : 0, -1, f.Id, "城市守备"));
    }
    foreach (var b in scenario.RuralBases) foreach (string name in b.Generals) PlaceGeneral(factions[indices[b.Faction]], name, byName[b.City].Cell, true);
    void PlaceGeneral(Faction f, string name, int anchor, bool rural) {
        Assert(people.TryGetValue(name, out var g), "Unregistered general " + name);
        int year = DateTime.Parse(scenario.Date).Year;
        Assert(g!.From <= year && g.Until >= year && !(name == "叶挺" && scenario.Date == "1946-06-26"), scenario.Id + " unavailable " + name);
        Assert(placedPeople.Add(name), scenario.Id + " duplicate deployed general " + name);
        int index = indices[f.Id];
        int at = Enumerable.Range(0, owners.Length).Where(c => !geometry.Cells[c].Terrain.Sea && geometry.Cells[c].Terrain.Active && !occupied.Contains(c)
            && (!rural ? owners[c] == index : !sites.ContainsKey(c) && Distance2(c, anchor) >= 4)).OrderBy(c => Distance2(c, anchor)).First();
        if (rural) {
            owners[at] = (byte)index;
            foreach (int neighbor in Enumerable.Range(0, 6).Select(d => grid.Neighbor(at, d)).Where(n => n >= 0 && !geometry.Cells[n].Terrain.Sea && !sites.ContainsKey(n) && !occupied.Contains(n))) owners[neighbor] = (byte)index;
        }
        occupied.Add(at);
        deployed.Add(new(at, g.Role == "cavalry" ? 6 : g.Role == "artillery" ? 10 : 1, year >= 1937 ? 1 : 0, g.Id, f.Id, rural ? "敌后/战区抽象部署" : "总部周边抽象部署"));
    }
    byte[] bytes = BattleBytes(factions, owners, sites.Values.ToArray(), deployed.ToArray());
    var centers = sites.Where(p => p.Value[4] is 0 or 1 or 2 && !geometry.Cells[p.Key].Terrain.Sea).Select(p => p.Key).ToArray();
    // Jurisdictions follow political borders and administrative regions, then proximity.
    var assignments = Enumerable.Repeat(-1, owners.Length).ToArray();
    int fallback = 0;
    for (int cell = 0; cell < assignments.Length; cell++) {
        if (geometry.Cells[cell].Terrain.Sea || !geometry.Cells[cell].Terrain.Active || sites.TryGetValue(cell, out var site) && site[4] == 3) continue;
        if (centers.Contains(cell)) { assignments[cell] = cell; continue; }
        var political = centers.Where(c => owners[c] == owners[cell]).ToArray();
        var administrative = political.Where(c => regions[c] == regions[cell]).ToArray();
        var candidates = administrative.Length > 0 ? administrative : political;
        if (administrative.Length == 0) fallback++;
        if (candidates.Length > 0) assignments[cell] = candidates.MinBy(c => Distance2(cell, c));
    }
    var jurisdiction = new LegacyJurisdictionData(1, 3, map.Width, map.Height, 0, 0, map.Width, map.Height, Hash(bytes), assignments);
    bytes = LegacyBattle.Read(bytes).WithJurisdictions(jurisdiction);
    var victory = new ExtensionVictoryRules {
        Win = new() { Type = "any", Conditions = new[] { new VictoryCondition { Type = "enemy_factions_defeated" }, new VictoryCondition { Type = "all", Conditions = new[] {
            new VictoryCondition { Type = "round_reached", Round = 30 }, new VictoryCondition { Type = "resources", Resources = new(5000, 1200, 2000) } } } } },
        RoundLimit = 180, RequirePlayerSurvival = true, DescriptionKey = "mg_victory"
    };
    bytes = LegacyBattle.Read(bytes).WithVictory(victory);
    Asset("original/" + scenario.Scenario + ".btl", bytes);
    var names = new XElement("citynames");
    if (DateTime.Parse(scenario.Date).Year is >= 1930 and <= 1946) names.Add(new XElement("city", new XAttribute("id", byName["北京"].CityId), new XAttribute("name", "北平")));
    if (DateTime.Parse(scenario.Date).Year >= 1944) names.Add(new XElement("city", new XAttribute("id", byName["库伦"].CityId), new XAttribute("name", "乌兰巴托")));
    Xml(scenario.Scenario + ".names.xml", new(names));
    battles.Add(new XElement("battle", new XAttribute("name", scenario.Id), new XAttribute("file", scenario.Scenario + ".btl"), new XAttribute("map", 3),
        new XAttribute("hide", 0), new XAttribute("age", scenario.Date), new XAttribute("citynames", scenario.Scenario + ".names.xml")));
    strings.AppendLine().Append("text_conquest_age_").Append(scenario.Scenario[8..]).Append('=').Append(scenario.Name);
    strings.AppendLine().Append("desc_").Append(scenario.Id).Append('=').Append(scenario.Description);
    var cityRows = cities.Select(c => new { c.Name, c.Cell, region = c.Group, owner = factions[cityOwners[c.Cell]].Id });
    Json("build/scenarios/" + scenario.Id + ".json", new { scenario.Id, scenario.Date, factions, cities = cityRows, deployments = deployed, jurisdictionFallbackCells = fallback,
        ownership = owners, cellCenters = assignments });
    Put("reports/maps/" + scenario.Id + ".png", OwnershipImage(owners, factions));
    reports.Add(new { scenario.Id, scenario.Scenario, date = scenario.Date, factions = factions.Length, playable = factions.Count(f => f.Camp != 4), generals = placedPeople.Count,
        units = deployed.Count, cities = cities.Length, jurisdictions = centers.Length, fallbackCells = fallback, hasJuri = true, hasVict = true });
}

Xml("def_battlelist.xml", new(battles));
strings.AppendLine().AppendLine("name_nanjing=国民政府中央军\nname_red=中共军队\nname_guangdong=粤军");
strings.AppendLine().AppendLine("mg_victory=试制胜利：消灭敌对阵营，或到达第30回合且玩家资源达到5000金钱/1200工业/2000粮食；180回合上限。");
Asset("original/stringtable_cn.ini", Encoding.UTF8.GetBytes(strings.ToString()));
foreach (string name in new[] { "def_commander.xml", "stringtable_cn.ini" }) catalog["source_sha256"]![name] = Hash(File.ReadAllBytes(Path.Combine(overlay, "original", name)));
AssetJson("legacy/catalog.json", catalog);
var stringMap = strings.ToString().Split('\n').Where(l => l.Contains('=') && !l.TrimStart().StartsWith(';')).Select(l => l.Split('=', 2)).GroupBy(p => p[0].Trim()).ToDictionary(g => g.Key, g => g.Last()[1].Trim());
manifest["strings"] = JsonSerializer.SerializeToNode(stringMap, json);
manifest["strategic"] = new JsonObject(battles.Elements().Select(e => KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("file")! is string file ? Path.GetFileNameWithoutExtension(file) : "", Attributes(e))));
AssetJson("client/manifest.json", manifest);
var pages = new JsonArray();
for (int offset = 0; offset < scenarios.Length; offset += 6) {
    var slots = new JsonArray();
    for (int i = 0; i < 6; i++) slots.Add(offset + i < scenarios.Length ? new JsonObject { ["scenario"] = scenarios[offset + i].Scenario } : null);
    pages.Add(new JsonObject { ["id"] = "working-title-" + offset / 6, ["slots"] = slots });
}
AssetJson("frontend/conquest-pages.json", new JsonObject { ["pages"] = pages });
var store = new ModStore(baseline, Inside("runtime/mods"), Inside("runtime/saves"));
store.Seal(overlay, "republic-working-title", "颓垣饿鸠", "0.1.0", scenarios.Select(s => s.Scenario).ToArray(), new[] {
    "《颓垣饿鸠》试制版。角色数值、兵种美术、历史边界与部署均为试制；不代表完成历史考证与平衡。",
    "按用户意见使用殖民势力范围同盟；早期法军双人任职未核实，保留中立，详见source/conquests.json。",
    "BTL包含JURI手工辖区；现代行政边界仅作稳定区域参考，热河/察哈尔/绥远/西康为草绘，缺少的城市辖界按同势力同区域就近补齐。",
    "未实现动态政变、外交倒戈与投降移交；跨年代招募池尚未限制年代。",
    "将领头像暂用无人物的占位标记；旗帜为势力识别色，不是考证后的军旗；战役暂未制作。"
});
Console.WriteLine("Validating package and every selectable faction");
var validated = store.Validate(overlay);
byte[] Read(string p) => validated.Files.ContainsKey(p) ? File.ReadAllBytes(Path.Combine(overlay, p)) : ReadBase(p);
var content = baseline.Content(Read, validated.Scenarios);
int starts = 0, reloads = 0, turnTransitions = 0;
foreach (var s in scenarios) {
    var b = content.Battle(s.Scenario);
    Assert(b.Jurisdictions != null && b.Victory != null, "Required BTL extensions");
    foreach (var f in b.Factions.Where(f => f.Camp != 4)) {
        var start = content.Start(s.Scenario, f.CountryId);
        var world = start.Session.World;
        world.Validate();
        Assert(world.Outcome == BattleOutcome.Continue, "No instant victory/defeat: " + s.Id + ":" + f.CountryId);
        Assert(world.Jurisdictions!.CellCenters.SequenceEqual(b.Jurisdictions!.CellCenters), "Exact JURI applied");
        Assert(world.Units.Count == b.Armies.Count, "No lost deployments");
        starts++;
        if (f == b.Factions.First(f => f.Camp != 4)) {
            byte[] saved = WorldSave.Encode(world);
            var loaded = WorldSave.Decode(saved, world.ContentRevision);
            Assert(loaded.Jurisdictions!.CellCenters.SequenceEqual(world.Jurisdictions.CellCenters), "JURI save/reload");
            var advanced = WorldTurns.EndTurn(loaded, loaded.Revision, start.Rules.Turns).State;
            advanced.Validate();
            Assert(advanced.ActiveFaction != loaded.ActiveFaction && advanced.Outcome == BattleOutcome.Continue, "Turn transition " + s.Id);
            turnTransitions++;
            reloads++;
        }
    }
    Console.WriteLine("Verified " + s.Id);
}
Assert(content.Catalogs.Runtime.Commanders.Count == generals.Count, "Only authored roster exposed");
var client = new ClientGame(content, Inside("build/check-profile"));
using (var hq = JsonDocument.Parse(client.Dispatch("/api/headquarters", "{}"))) {
    Assert(hq.RootElement.GetProperty("ok").GetBoolean(), "Headquarters API");
    var defs = hq.RootElement.GetProperty("data").GetProperty("definitions");
    Assert(defs.GetArrayLength() == generals.Count, "All general names visible in headquarters");
}
for (int group = 0; group < 3; group++) {
    using var before = JsonDocument.Parse(client.Dispatch("/api/headquarters", "{}"));
    long revision = before.RootElement.GetProperty("data").GetProperty("revision").GetInt64();
    using var refreshed = JsonDocument.Parse(client.Dispatch("/api/commander", JsonSerializer.Serialize(new { op = "refresh", revision, group })));
    Assert(refreshed.RootElement.GetProperty("ok").GetBoolean(), "Recruitment refresh " + group);
    using var after = JsonDocument.Parse(client.Dispatch("/api/headquarters", "{}"));
    var offers = after.RootElement.GetProperty("data").GetProperty("offers").GetProperty(group.ToString());
    Assert(offers.GetArrayLength() == new[] { 6, 4, 2 }[group], "Recruitment offer counts");
    foreach (var offered in offers.EnumerateArray()) {
        int id = int.Parse(offered.GetString()!.Split('.').Last());
        Assert(generals.Single(g => g.Id == id).Tier == group + 1, "Recruitment tier mapping");
    }
}
foreach (var input in inputs) Assert(Hash(File.ReadAllBytes(Path.Combine(workspace, input.Key))) == input.Value, "Reference source changed " + input.Key);
var installed = store.Import(overlay, "republic-working-title", "颓垣饿鸠");
store.Activate(installed.Key);
string dist = Inside("dist/republic-working-title-0.1.0.ew4mod");
if (File.Exists(dist)) File.Move(dist, Inside("build/previous-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssffff") + ".ew4mod"));
Directory.CreateDirectory(Path.GetDirectoryName(dist)!);
store.Export(installed.Key, dist);
Json("reports/validation.json", new { status = "passed", generals = generals.Count, groups = generals.Select(g => g.Group).Distinct().Count(),
    tiers = generals.GroupBy(g => g.Tier).ToDictionary(g => g.Key, g => g.Count()), scenarioCount = scenarios.Length, factionStarts = starts, saveReloads = reloads, turnTransitions, recruitmentSections = 3,
    requiredExtension = "JURI v1", scenarios = reports, referenceHashes = inputs, packageSha256 = Hash(File.ReadAllBytes(dist)) });
Text("reports/ROSTER.md", "# 颓垣饿鸠 · 制作名册\n\n属性为角色模板试制值；技能与头像待制作。\n\n| ID | 姓名 | 组 | 招募区 | 专长 | 开局候选年份 |\n| --- | --- | --- | --- | --- | --- |\n" + string.Join('\n', generals.Select(g => $"| {g.Id} | {g.Name} | {g.Group} | {g.Tier}星 | {g.Role} | {g.From}—{g.Until} |")) + "\n");
Console.WriteLine($"PASS: {generals.Count} generals; {scenarios.Length} scenarios; {starts} starts; {reloads} save reloads");

void Assert(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
JsonObject Attributes(XElement e) => new(e.Attributes().Select(a => KeyValuePair.Create<string, JsonNode?>(a.Name.LocalName, JsonValue.Create(a.Value))));
JsonObject Tree(XElement e) => new() { ["tag"] = e.Name.LocalName, ["attributes"] = Attributes(e), ["text"] = "", ["children"] = new JsonArray(e.Elements().Select(c => (JsonNode)Tree(c)).ToArray()) };
double Distance2(int a, int b) {
    double dx = a % map.Width - b % map.Width + ((a / map.Width) % 2 - (b / map.Width) % 2) * .5;
    double dy = (a / map.Width - b / map.Width) * .84375;
    return dx * dx + dy * dy;
}
byte[] Site(int cell, int city, int kind, int level, int style) {
    byte[] bytes = new byte[16]; BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)cell); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), (short)city);
    bytes[4] = (byte)kind; bytes[5] = (byte)level; bytes[7] = (byte)style; bytes[8] = bytes[9] = bytes[11] = 255; return bytes;
}
byte[] BattleBytes(Faction[] factions, byte[] owners, byte[][] sites, Deployment[] deployments) {
    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
    foreach (int v in new[] { 2, 3, 0, 0, map.Width, map.Height, factions.Length, sites.Length, deployments.Length, 0, 0, 1, 180, 0, 0, 1, 0, 0 }) writer.Write(v);
    void Fixed(string value) { byte[] name = Encoding.ASCII.GetBytes(value); Assert(name.Length <= 15, "Faction string exceeds BTL field"); writer.Write(name); writer.Write(new byte[16 - name.Length]); }
    foreach (var faction in factions) {
        int count = cities.Count(c => owners[c.Cell] == Array.IndexOf(factions, faction));
        writer.Write(300 + count * 50); writer.Write(200 + count * 20); writer.Write(600 + count * 100);
        writer.Write(1); writer.Write(faction.Camp); writer.Write(0); Fixed(faction.Id); Fixed(faction.Id);
        writer.Write(1f); writer.Write(0f); writer.Write(Color(faction.Id));
        for (int u = 0; u < 26; u++) writer.Write(u < 22 ? u is 0 or 1 or 2 or 5 or 6 or 9 or 10 or 11 or 18 ? 0 : -1 : u == 25 ? 1 : 0);
        writer.Write(0); writer.Write(0);
    }
    writer.Write(owners); foreach (byte[] site in sites) writer.Write(site);
    foreach (var unit in deployments) {
        byte[] bytes = new byte[32]; BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)unit.Cell); bytes[2] = (byte)unit.Unit; bytes[3] = (byte)unit.Grade;
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 9999); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), 1);
        bytes[12] = 0; bytes[13] = 10; BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(16), (short)unit.Commander); bytes[26] = 255;
        writer.Write(bytes);
    }
    return stream.ToArray();
}
int Color(string id) {
    byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(id));
    return (80 + hash[0] % 150) << 16 | (70 + hash[1] % 150) << 8 | (70 + hash[2] % 150);
}
void AddFlag(string id) {
    int rgb = Color(id);
    byte[] flag = Png(40, 24, (x, y) => y is >= 10 and <= 13 ? (byte)232 : (byte)(rgb >> (16 - x % 3 * 8)), true, rgb);
    foreach (string atlasName in new[] { "buildings", "image_menu_hd" }) {
        for (int frame = 1; frame <= (atlasName == "buildings" ? 4 : 1); frame++) {
            string filename = id + frame + ".png";
            Asset("client/ui/" + atlasName + "/" + filename, flag);
            manifest["atlases"]![atlasName]![filename] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 40, ["h"] = 24, ["refx"] = 0, ["refy"] = 0 };
        }
    }
}
byte[] OwnershipImage(byte[] owners, Faction[] factions) {
    return Png(map.Width * 4, map.Height * 4, (_, _) => 0, false, 0, (x, y) => {
        int cell = y / 4 * map.Width + x / 4;
        return owners[cell] == 255 ? 0xB7DDE5 : cities.Any(c => c.Cell == cell) ? 0x202020 : Color(factions[owners[cell]].Id);
    });
}
byte[] Png(int width, int height, Func<int, int, byte> unused, bool flag = false, int flagColor = 0, Func<int, int, int>? pixel = null) {
    using var raw = new MemoryStream();
    for (int y = 0; y < height; y++) {
        raw.WriteByte(0);
        for (int x = 0; x < width; x++) {
            int rgb = pixel != null ? pixel(x, y) : flag && y is >= 10 and <= 13 ? 0xE8E8E8 : flagColor;
            raw.WriteByte((byte)(rgb >> 16)); raw.WriteByte((byte)(rgb >> 8)); raw.WriteByte((byte)rgb); raw.WriteByte(255);
        }
    }
    using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    void Chunk(string type, byte[] payload) {
        byte[] kind = Encoding.ASCII.GetBytes(type); byte[] len = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(len, payload.Length); output.Write(len); output.Write(kind); output.Write(payload);
        uint crc = 0xffffffff; foreach (byte b in kind.Concat(payload)) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1)); }
        BinaryPrimitives.WriteUInt32BigEndian(len, ~crc); output.Write(len);
    }
    byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height); header[8] = 8; header[9] = 6; Chunk("IHDR", header);
    using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true)) z.Write(raw.ToArray());
    Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>()); return output.ToArray();
}
string[] BuildRegions() {
    var result = new string[map.Cells.Count];
    var modernNames = new Dictionary<string, string> { ["北京市"]="河北及京津",["天津市"]="河北及京津",["河北省"]="河北及京津",["山西省"]="山西",["辽宁省"]="辽宁及辽东",["吉林省"]="吉林",["黑龙江省"]="黑龙江及北满",["上海市"]="江苏及上海",["江苏省"]="江苏及上海",["浙江省"]="浙江",["安徽省"]="安徽",["福建省"]="福建",["江西省"]="江西",["山东省"]="山东",["河南省"]="河南",["湖北省"]="湖北",["湖南省"]="湖南",["广东省"]="广东",["广西壮族自治区"]="广西",["海南省"]="海南",["四川省"]="四川",["重庆市"]="四川",["贵州省"]="贵州",["云南省"]="云南",["陕西省"]="陕西",["甘肃省"]="甘肃",["青海省"]="青海",["宁夏回族自治区"]="宁夏",["新疆维吾尔自治区"]="新疆",["西藏自治区"]="西藏及喜马拉雅",["台湾省"]="台湾及澎湖",["香港特别行政区"]="港澳租借地及殖民据点",["澳门特别行政区"]="港澳租借地及殖民据点" };
    var polygons = new List<(string Region, double[][][] Rings)>();
    void ReadPolygons(string relative, bool historical) {
        var root = JsonNode.Parse(File.ReadAllBytes(Inside(relative)))!;
        foreach (var feature in root["features"]!.AsArray()) {
            string? region = historical ? (string?)feature!["properties"]!["region"] : modernNames.GetValueOrDefault((string?)feature!["properties"]!["name"] ?? "");
            if (region == null) continue;
            var shape = feature!["geometry"]!;
            if ((string?)shape["type"] == "Polygon") polygons.Add((region, shape["coordinates"]!.Deserialize<double[][][]>()!));
            else if ((string?)shape["type"] == "MultiPolygon") foreach (var p in shape["coordinates"]!.Deserialize<double[][][][]>()!) polygons.Add((region, p));
        }
    }
    ReadPolygons("source/china-provinces-modern.geojson", false);
    ReadPolygons("source/historical-regions.geojson", true);
    double rad = Math.PI / 180, n = Math.Log(Math.Cos(25 * rad) / Math.Cos(47 * rad)) / Math.Log(Math.Tan(Math.PI / 4 + 47 * rad / 2) / Math.Tan(Math.PI / 4 + 25 * rad / 2));
    double f = Math.Cos(25 * rad) * Math.Pow(Math.Tan(Math.PI / 4 + 25 * rad / 2), n) / n, r = 6371008.8;
    double rho0 = r * f / Math.Pow(Math.Tan(Math.PI / 4 + 35 * rad / 2), n), ymax = -2050000 + 6600000d * 3583 / 4993;
    (double Lon, double Lat) Geographic(int cell) {
        double px = (cell % map.Width * 64 + (cell / map.Width % 2) * 32) / 2d, py = (cell / map.Width * 54 - 18) / 2d;
        double x = px * 6600000 / 4993 - 3350000, y = ymax - py * 6600000 / 4993;
        double rho = Math.Sqrt(x * x + (rho0 - y) * (rho0 - y));
        return ((110 * rad + Math.Atan2(x, rho0 - y) / n) / rad, (2 * Math.Atan(Math.Pow(r * f / rho, 1 / n)) - Math.PI / 2) / rad);
    }
    bool InRing(double lon, double lat, double[][] ring) {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) if ((ring[i][1] > lat) != (ring[j][1] > lat) && lon < (ring[j][0] - ring[i][0]) * (lat - ring[i][1]) / (ring[j][1] - ring[i][1]) + ring[i][0]) inside = !inside;
        return inside;
    }
    int matched = 0;
    for (int cell = 0; cell < result.Length; cell++) {
        result[cell] = cities.MinBy(c => Distance2(cell, c.Cell))!.Group;
        if (geometry.Cells[cell].Terrain.Sea) continue;
        var at = Geographic(cell);
        bool hit = false;
        foreach (var p in polygons) if (InRing(at.Lon, at.Lat, p.Rings[0]) && !p.Rings.Skip(1).Any(h => InRing(at.Lon, at.Lat, h))) { result[cell] = p.Region; hit = true; }
        if (hit) matched++;
    }
    foreach (var c in cities) result[c.Cell] = c.Group;
    Json("reports/administrative-boundaries.json", new { polygonMatchedLandCells = matched, totalLandCells = geometry.Cells.Count(c => !c.Terrain.Sea),
        source = "https://geo.datav.aliyun.com/areas_v3/bound/100000_full.json", modernReferenceSha256 = Hash(File.ReadAllBytes(Inside("source/china-provinces-modern.geojson"))),
        warning = "现代省界仅作未变动区域参考；历史变动四区为粗略草绘，其他缺失区域按最近城市组补齐；不是已校勘的历史边界。" });
    return result;
}

sealed class General {
    public int Id { get; set; } public string Name { get; set; } = ""; public string Group { get; set; } = ""; public int Tier { get; set; }
    public string Role { get; set; } = "infantry"; public int From { get; set; } = 1915; public int Until { get; set; } = 1950; public string Note { get; set; } = "";
}
sealed class Faction {
    public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Headquarters { get; set; } = ""; public int Camp { get; set; } = 4;
    public string[] Generals { get; set; } = []; public string? SpherePartner { get; set; } public string? PendingReason { get; set; }
}
sealed class Conquest {
    public string Id { get; set; } = ""; public string Scenario { get; set; } = ""; public string Date { get; set; } = ""; public string Name { get; set; } = "";
    public string DefaultOwner { get; set; } = ""; public string Description { get; set; } = ""; public Dictionary<string, string> Regions { get; set; } = []; public Dictionary<string, string> Cities { get; set; } = [];
    public Faction[] Factions { get; set; } = []; public RuralBase[] RuralBases { get; set; } = [];
}
sealed class Geography { public Dictionary<string, string> Regions { get; set; } = []; public Dictionary<string, string> Cities { get; set; } = []; public Faction[] Backgrounds { get; set; } = []; }
sealed class RuralBase { public string Faction { get; set; } = ""; public string City { get; set; } = ""; public string[] Generals { get; set; } = []; }
sealed class City {
    public string Name { get; set; } = ""; public string Group { get; set; } = ""; public int Cell { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("city_id")] public int CityId { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("building_level")] public int BuildingLevel { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("building_style")] public int BuildingStyle { get; set; }
}
sealed class Facility {
    public int Cell { get; set; } public string Kind { get; set; } = ""; public int Level { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("building_kind")] public int BuildingKind { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("building_style")] public int BuildingStyle { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("anchor_cell")] public int AnchorCell { get; set; }
}
sealed record Deployment(int Cell, int Unit, int Grade, int Commander, string Faction, string Note);
