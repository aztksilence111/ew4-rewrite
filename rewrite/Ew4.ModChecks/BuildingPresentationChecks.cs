using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Legacy;
using Ew4.Mods;

internal static class BuildingPresentationChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; }
        string data = Path.Combine(root, "rewrite/data"), run = Path.Combine(root, "build/r-city-exchange-tests-20261101/mod-checks");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(data, path));
        var baseline = new ModBase(Read, ["legacy/catalog.json", "client/manifest.json"]);
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        var original = JsonNode.Parse(Read("client/manifest.json"))!;
        var definitions = XDocument.Load(Path.Combine(data, "original/def_construction.xml"));
        byte[] CatalogRead(string path) => Read(path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path);
        var bundle = LegacyCatalogFiles.Load(CatalogRead);
        var stock = LegacyBuildingCatalog.Read(bundle.Xml["def_construction.xml"], File.ReadAllText(Path.Combine(data, "original/def_capital.xml")));
        Check(stock.Definitions[0].MaximumUpgradeLevel == 6 && stock.NormalizeLevel(0, 7) == 7, "stock retains initial VII with upgrade ceiling VI");
        Check(stock.UpgradeRules().Steps.Any(s => s.Kind == "city" && s.FromLevel == 5 && s.ToLevel == 6)
            && stock.UpgradeRules().Steps.All(s => s.Kind != "city" || s.ToLevel <= 6), "stock can upgrade to VI but not VII");
        foreach (string route in new[] { "manifest.json", "r-" + baseline.Revision + "/manifest.json" })
        {
            var presented = JsonNode.Parse(store.ReadContent(route))!;
            foreach (var row in definitions.Root!.Elements())
                Check((int)presented["buildingUpgradeLimits"]![(string)row.Attribute("type")!]! == (int)row.Attribute("maxlevel")!, "effective XML upgrade ceiling");
            Check(presented["buildings"]!["city"]!.AsArray().Count == 7 && (int)presented["buildingUpgradeLimits"]!["city"]! == 6,
                "initial-only seventh city tier does not delay the frame");
            Check(JsonNode.DeepEquals(original["buildings"], presented["buildings"]), "all building rows retained");
        }
        string key = new('c', 64), folder = Path.Combine(run, "mods", key);
        Directory.CreateDirectory(Path.Combine(folder, "original"));
        Directory.CreateDirectory(Path.Combine(folder, "legacy"));
        File.WriteAllBytes(Path.Combine(folder, "legacy/catalog.json"), LegacyCatalogFiles.Encode(bundle.Catalog));
        var mod = new ModManifest(1, "building-ceiling", "Building ceiling", "1", baseline.Revision, [],
            new() { ["original/def_construction.xml"] = "unused-read-only-fixture", ["legacy/catalog.json"] = "unused-read-only-fixture" }, []);
        File.WriteAllText(Path.Combine(folder, "mod.json"), JsonSerializer.Serialize(mod, ClientJson));
        foreach (int maximum in new[] { 5, 7 })
        {
            definitions.Root!.Elements().Single(e => (string?)e.Attribute("type") == "city").SetAttributeValue("maxlevel", maximum);
            definitions.Save(Path.Combine(folder, "original/def_construction.xml"));
            var presented = JsonNode.Parse(store.ReadContent("r-" + key + "/manifest.json"))!;
            Check((int)presented["buildingUpgradeLimits"]!["city"]! == maximum, "previously installed Mod reads its own upgrade ceiling");
            Check((int)JsonNode.Parse(store.ReadContent("r-" + baseline.Revision + "/manifest.json"))!["buildingUpgradeLimits"]!["city"]! == 6,
                "in-flight builtin request keeps its own ceiling");
        }
        mod.Files.Remove("legacy/catalog.json");
        mod.Files.Add("legacy/buildings.json", "unused-read-only-fixture");
        File.WriteAllText(Path.Combine(folder, "mod.json"), JsonSerializer.Serialize(mod, ClientJson));
        var buildings = JsonNode.Parse(Read("legacy/buildings.json"))!;
        var city = buildings["buildings"]!.AsArray().Single(row => (string?)row!["type"] == "city")!;
        foreach (int maximum in new[] { 5, 7 })
        {
            city["maxlevel"] = maximum == 7 ? JsonValue.Create("7") : JsonValue.Create(5);
            File.WriteAllBytes(Path.Combine(folder, "legacy/buildings.json"), LegacyCatalogFiles.Encode(buildings));
            var presented = JsonNode.Parse(store.ReadContent("r-" + key + "/manifest.json"))!;
            Check((int)presented["buildingUpgradeLimits"]!["city"]! == maximum, "JSON Mod ceiling overrides original XML and accepts numbers or strings");
            var effective = LegacyCatalogFiles.Load(path => path == "data/buildings.json" ? LegacyCatalogFiles.Encode(buildings) : CatalogRead(path));
            var rules = LegacyBuildingCatalog.Read(effective.Xml["def_construction.xml"], File.ReadAllText(Path.Combine(data, "original/def_capital.xml")));
            Check(rules.Definitions[0].MaximumUpgradeLevel == maximum && rules.UpgradeRules().Steps.Any(s => s.Kind == "city" && s.ToLevel == maximum)
                && rules.UpgradeRules().Steps.All(s => s.Kind != "city" || s.ToLevel <= maximum), "JSON Mod gameplay and frame share upgrade ceiling");
            Check((int)JsonNode.Parse(store.ReadContent("manifest.json"))!["buildingUpgradeLimits"]!["city"]! == 6, "Mod override does not change builtin ceiling");
        }
        Check(store.Read("client/manifest.json").SequenceEqual(Read("client/manifest.json")), "presentation does not rewrite source bytes or Mod identity");
        return checks;
    }
    private static readonly JsonSerializerOptions ClientJson = new(JsonSerializerDefaults.Web);
}
