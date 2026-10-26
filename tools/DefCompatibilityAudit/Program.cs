using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;

if (args.Length != 2) throw new ArgumentException("Usage: <repository root> <input directory>");
string repo = Path.GetFullPath(args[0]), inputs = Path.GetFullPath(args[1]);
string data = Path.Combine(repo, "rewrite/data");
string[] names = ["army", "commander", "item", "card", "construction", "terraintype", "warzonetech", "princess", "capital"];
var files = new List<object>();
var checks = new List<object>();
var findings = new List<object>();
var overlay = new Dictionary<string, byte[]>(StringComparer.Ordinal);
var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
var assembly = typeof(ModBase).Assembly;
var normalize = assembly.GetType("Ew4.Mods.LegacyXml", true)!.GetMethod("Normalize", BindingFlags.NonPublic | BindingFlags.Static)!;
var convert = assembly.GetType("Ew4.Mods.LegacyCatalogConverter", true)!.GetMethod("Convert", BindingFlags.NonPublic | BindingFlags.Static)!;
Exception Unwrap(Exception error) => error is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner) : error;
string Code(Exception error) => error is RuleViolation rule ? rule.Code : error.GetType().Name;
bool Check(string stage, Action action)
{
    try { action(); checks.Add(new { stage, status = "pass" }); return true; }
    catch (Exception error)
    {
        error = Unwrap(error);
        checks.Add(new { stage, status = "fail", code = Code(error), message = error.Message });
        return false;
    }
}
int Line(XObject element) => ((IXmlLineInfo)element).LineNumber;
foreach (string name in names)
{
    string path = Path.Combine(inputs, $"def_{name}(3).xml"), canonical = $"def_{name}.xml";
    byte[] bytes = File.ReadAllBytes(path);
    object?[] arguments = [bytes, false];
    byte[] normalized = (byte[])normalize.Invoke(null, arguments)!;
    var document = XDocument.Parse(Encoding.UTF8.GetString(normalized), LoadOptions.SetLineInfo);
    documents[canonical] = document;
    overlay["original/" + canonical] = normalized;
    files.Add(new { path, canonical, bytes = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        normalized = (bool)arguments[1]!, root = document.Root!.Name.LocalName, rows = document.Root.Elements().Count() });
    foreach (var duplicates in document.Root!.Elements().Where(e => e.Attribute("id") != null)
        .GroupBy(e => (string)e.Attribute("id")!).Where(g => g.Count() > 1))
        findings.Add(new { file = canonical, lines = duplicates.Select(Line), problem = "duplicate id", id = duplicates.Key });
}
byte[] BaseRead(string path) => File.ReadAllBytes(Path.Combine(data, path));
byte[] Read(string path) => overlay.TryGetValue(path, out var bytes) ? bytes : BaseRead(path);
string Text(string name)
{
    using var reader = new StreamReader(new MemoryStream(Read("original/" + name)), Encoding.UTF8, true);
    return reader.ReadToEnd();
}
var baseline = new ModBase(BaseRead, Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories)
    .Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
var bundle = LegacyCatalogFiles.Load(p => BaseRead(p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p));
Check("reproduce: builtin catalog with user XML replaced directly", () =>
    LegacyRuntimeCatalog.FromJson(bundle.Catalog.ToJsonString()).VerifySources(name => Read("original/" + name)));
JsonObject? catalog = null;
LegacyRuntimeCatalog? runtime = null;
Check("actual catalog converter", () => catalog = (JsonObject)convert.Invoke(null,
    [new Func<string, byte[]>(name => Read("original/" + name)), bundle.Catalog])!);
if (catalog != null)
{
    Check("runtime units, commanders, equipment and references", () => runtime = LegacyRuntimeCatalog.FromJson(catalog.ToJsonString()));
    if (runtime == null)
    {
        // Isolate rows so the first invalid commander does not hide later failures.
        foreach (var row in catalog["commanders"]!.AsArray())
        {
            var isolated = (JsonObject)catalog.DeepClone();
            isolated["commanders"] = new JsonArray(row!.DeepClone());
            try { LegacyRuntimeCatalog.FromJson(isolated.ToJsonString()); }
            catch (Exception error)
            {
                int id = int.Parse((string)row["legacy_attributes"]!["id"]!);
                findings.Add(new { file = "def_commander.xml", line = Line(documents["def_commander.xml"].Root!.Elements()
                    .Single(e => (int)e.Attribute("id")! == id)), id, code = Code(error), problem = error.Message });
            }
        }
    }
    else Check("source hashes after actual conversion", () => runtime.VerifySources(name => Read("original/" + name)));
    foreach (var (name, bytes) in LegacyCatalogFiles.Split(catalog, bundle.RuntimeRules, bundle.MetagameRules))
        overlay["legacy/" + name] = bytes;
}
LegacyBuildingCatalog? buildings = null;
LegacyRecruitmentCatalog? recruitment = null;
Check("building and capital reader", () => buildings = LegacyBuildingCatalog.Read(Text("def_construction.xml"), Text("def_capital.xml")));
Check("recruitment reader", () => recruitment = LegacyRecruitmentCatalog.Read(Text("def_card.xml")));
Check("item trading reader", () => LegacyItemTradingCatalog.Read(Text("def_item.xml"), configuredItems: runtime?.Items));
Check("technology reader", () => LegacyWarzoneTechnology.Read(Text("def_warzonetech.xml")));
Check("terrain reader (unmodified input)", () => LegacyTerrainRules.FromXml(Text("def_mapterrain.xml"), Text("def_terraintype.xml")));
if (runtime != null && buildings != null && recruitment != null)
    Check("recruitment offers for every resolved country", () =>
    {
        foreach (string country in runtime.Variants.Keys) recruitment.Offers(country, runtime, buildings);
    });
foreach (var terrain in documents["def_terraintype.xml"].Root!.Elements())
    if ((int?)terrain.Attribute("movementcost") is not >= 1)
        findings.Add(new { file = "def_terraintype.xml", line = Line(terrain), type = (string?)terrain.Attribute("type"),
            problem = "movementcost must be >= 1", actual = (string?)terrain.Attribute("movementcost") });
if (runtime != null)
    foreach (var princess in documents["def_princess.xml"].Root!.Elements("Princess"))
    {
        int commander = (int)princess.Attribute("commanderid")!;
        Check("princess commander " + commander, () =>
        {
            if (!runtime.Commanders.ContainsKey(commander)) throw new InvalidDataException("Unknown princess commander");
        });
    }
ClientContent? content = null;
Check("client content with converted catalog and original inputs", () => content = baseline.Content(Read, baseline.Scenarios));
bool diagnosticTerrainCorrection = false;
if (content == null && catalog != null)
{
    var terrain = new XDocument(documents["def_terraintype.xml"]);
    foreach (var row in terrain.Root!.Elements().Where(e => (int?)e.Attribute("movementcost") is not >= 1))
    { row.SetAttributeValue("movementcost", 1); diagnosticTerrainCorrection = true; }
    if (diagnosticTerrainCorrection)
    {
        overlay["original/def_terraintype.xml"] = Encoding.UTF8.GetBytes(terrain.ToString());
        catalog = (JsonObject)convert.Invoke(null, [new Func<string, byte[]>(name => Read("original/" + name)), bundle.Catalog])!;
        foreach (var (name, bytes) in LegacyCatalogFiles.Split(catalog, bundle.RuntimeRules, bundle.MetagameRules))
            overlay["legacy/" + name] = bytes;
        Check("diagnostic only: client content after movementcost=1 in memory", () => content = baseline.Content(Read, baseline.Scenarios));
    }
}
var scenarioErrors = new List<object>();
int started = 0, parsed = 0;
if (content != null)
    foreach (var scenario in content.Scenarios)
        try
        {
            var battle = content.Battle(scenario.Id); parsed++;
            if (scenario.Mode != 3)
            {
                content.Start(scenario.Id, scenario.Mode == 2 ? battle.Factions.First(f => f.Camp != 4).CountryId : null);
                started++;
            }
        }
        catch (Exception error) { scenarioErrors.Add(new { scenario = scenario.Id, code = Code(error), message = error.Message }); }
var summary = new
{
    files, checks, findings, diagnosticTerrainCorrection,
    armySourceComparison = new { builtinCatalog = (string?)bundle.Catalog["source_sha256"]!["def_army.xml"],
        actual = Convert.ToHexStringLower(SHA256.HashData(Read("original/def_army.xml"))),
        regeneratedCatalog = (string?)catalog?["source_sha256"]?["def_army.xml"],
        installedPlayerCatalogProvided = false },
    counts = new { armies = documents["def_army.xml"].Descendants("army").Count(), countries = documents["def_army.xml"].Root!.Elements().Count(),
        commanders = documents["def_commander.xml"].Root!.Elements().Count(), items = documents["def_item.xml"].Root!.Elements().Count(),
        cards = documents["def_card.xml"].Root!.Elements().Count() },
    scenarios = new { basis = "repository builtin scenarios, not the player's missing BTL/assets", parsed, started, errors = scenarioErrors }
};
Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
