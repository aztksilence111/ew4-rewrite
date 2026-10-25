using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;

internal static class CatalogFileChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Catalog files: " + label); checks++; }
        string data = Path.Combine(root, "rewrite/data");
        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string path) => overrides.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(
            Path.Combine(data, path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path));
        void Put(string file, Action<JsonObject> change)
        {
            string path = "data/" + file;
            var json = JsonNode.Parse(Read(path))!.AsObject(); change(json); overrides[path] = LegacyCatalogFiles.Encode(json);
        }
        ClientContent Content() => new(Read, ["conquest1.btl"]);
        var bundle = LegacyCatalogFiles.Load(Read);
        var original = Content();
        Check(bundle.IsSplit && bundle.Catalog["profiles"]!.AsArray().Count == 369, "split catalog and original units load");
        Check(original.Catalogs.Runtime.Commanders.Count == 588 && original.Catalogs.Runtime.Items.Count == 228, "all commanders and items retained");
        var custom = (JsonObject)bundle.Catalog.DeepClone();
        custom["profiles"]![0]!["grade"] = "custom.grade";
        custom["profiles"]![0]!["weapon"] = "rocket";
        var customFiles = LegacyCatalogFiles.Split(custom, bundle.RuntimeRules, bundle.MetagameRules);
        Check(JsonNode.Parse(customFiles["units.json"])!["units"]![0]!["grade"]!.GetValue<string>() == "custom.grade"
            && JsonNode.Parse(customFiles["units.json"])!["units"]![0]!["weapon"]!.GetValue<string>() == "rocket", "custom grade and weapon survive splitting explicit combat profiles");
        var roundTrip = LegacyCatalogFiles.Split(bundle.Catalog, bundle.RuntimeRules, bundle.MetagameRules);
        var reloaded = LegacyCatalogFiles.Load(p => p.StartsWith("data/", StringComparison.Ordinal) && roundTrip.TryGetValue(p[5..], out var bytes) ? bytes : Read(p));
        var before = LegacyRuntimeCatalog.FromJson(bundle.Catalog.ToJsonString());
        var after = LegacyRuntimeCatalog.FromJson(reloaded.Catalog.ToJsonString());
        foreach (int id in before.Commanders.Keys)
            Check(JsonSerializer.Serialize(before.Commanders[id]) == JsonSerializer.Serialize(after.Commanders[id]), "commander split/reload " + id);
        foreach (string id in before.Profiles.Keys)
            Check(JsonSerializer.Serialize(before.Profiles[id]) == JsonSerializer.Serialize(after.Profiles[id]), "unit split/reload " + id);

        Put("commanders.json", j => { j["commanders"]![0]!["business"] = 9; j["commanders"]![0]!["movement"] = 12; j["commanders"]![0]!["price"] = 123; });
        var changed = Content(); int commander = (int)JsonNode.Parse(Read("data/commanders.json"))!["commanders"]![0]!["legacy_id"]!;
        Check(changed.Catalogs.Accounts.CommanderCommerce[commander] == 9 && changed.Catalogs.Runtime.Commanders[commander].Definition.Movement == 12,
            "commander commerce and battle attributes share JSON source");
        Check(changed.Catalogs.Runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children")[0]
            .GetProperty("attributes").GetProperty("price").GetString() == "123", "headquarters sees JSON price");
        Check(original.Catalogs.Accounts.SourceRevision != changed.Catalogs.Accounts.SourceRevision, "direct definition edits change account rules revision");
        Check(!changed.Catalogs.Accounts.CanMigrateFrom("e47fb936518323a29848404514de602e25bfc04bbc5fafa191485fa32c7d39e5"),
            "changed definitions cannot use the stock split migration");
        overrides.Clear();
        Put("skills.json", j => { j["skills"]![0]!["names"]!["zh_CN"] = "Custom flag"; j["skills"]![0]!["legacy_description"] = "Custom description"; });
        changed = Content();
        Check(changed.Label("name_skill_01") == "Custom flag" && changed.Label("desc_skill_01") == "Custom description", "skill JSON owns visible text");
        overrides.Clear();
        Put("items.json", j => { var item = j["items"]!.AsArray().Single(i => (int)i!["id"]! == 20)!; item["value"] = 99; item["price"] = 1234; });
        changed = Content();
        Check(changed.Catalogs.Runtime.Items[20].Value == 99 && changed.Catalogs.Items.Items[20].Price == 1234,
            "JSON item effect and market price agree without XML/hash edits");
        overrides.Clear();
        Put("combat-rules.json", j => j["tables"]!["weapon_category_multipliers"]![0] = 2.5);
        changed = Content();
        Check(changed.Catalogs.Runtime.Profiles["ew4.unit.fra.militia.0"].Profile.DamageMultipliers["infantry"] == 2.5f,
            "global weapon multipliers reach actual units");
        overrides.Clear();
        Put("recruitment.json", j => j["cards"]![0]!["price"] = 123);
        Check(Content().Catalogs.Cards.Cards[0].Cost.Money == 123, "recruitment JSON controls cost");
        overrides.Clear();
        Put("terrain-rules.json", j => j["terrain"]![0]!["penalty_infantry"] = 7);
        Check(Content().Catalogs.Combat.Context.TerrainDefense["land"]["infantry"] == 7, "terrain JSON reaches combat context");
        overrides.Clear();
        Put("installations.json", j => j["installations"]![0]!["penalty_infantry"] = 17);
        Check(Content().Catalogs.Combat.Context.InstallationDefense["trench"]["infantry"] == 17, "installation JSON reaches combat context");
        overrides.Clear();
        Put("buildings.json", j => j["buildings"]![0]!["children"]![0]!["tax"] = 123);
        Check(LegacyCatalogFiles.Load(Read).Xml["def_construction.xml"].Contains("tax=\"123\"", StringComparison.Ordinal), "nested building JSON updates generated definition");
        overrides.Clear();
        Put("commander-progression.json", j => j["rules"]!["commander_rank_health_bonus"]![0] = 17);
        changed = Content();
        Check(changed.Catalogs.Combat.RankHealth["0"] == 17 && changed.Catalogs.Runtime.NativeTables.GetProperty("commander_rank_health_bonus")[0].GetInt32() == 17,
            "commander progression has one source for catalog and combat");
        overrides.Clear();
        Put("unit-progression.json", j => j["rules"]!["training_money"]![0] = 71);
        Check(Content().Catalogs.Combat.Training.Stages[0].MoneyToNext == 71, "unit progression reaches training rules");
        overrides.Clear();
        Put("economy-rules.json", j => j["rules"]!["title_commerce"]![0] = 2);
        Check(Content().Catalogs.Accounts.TitleCommerce[0] == 2, "economy JSON reaches account rules");
        overrides.Clear();
        overrides["data/catalog.json"] = LegacyCatalogFiles.Encode(bundle.Catalog);
        Check(!LegacyCatalogFiles.Load(Read).IsSplit && Content().Catalogs.Runtime.Commanders.Count == 588, "old schema-2 catalogs still load");
        overrides.Clear();
        Put("skills.json", j => j["skills"]!.AsArray().Add(j["skills"]![0]!.DeepClone()));
        try { Content(); throw new Exception("duplicate skills accepted"); }
        catch (RuleViolation e) { Check(e.Code == "legacy.catalog_skill", "duplicate skill IDs rejected"); }
        overrides.Clear();
        Put("unit-progression.json", j => j["rules"]!["typo"] = 1);
        try { Content(); throw new Exception("unknown rule field accepted"); }
        catch (RuleViolation e) { Check(e.Code == "legacy.catalog_file", "unknown rule fields rejected"); }
        overrides.Clear();
        try { LegacyCatalogFiles.Load(p => p == "data/items.json" ? throw new FileNotFoundException(p) : Read(p)); throw new Exception("missing split file accepted"); }
        catch (FileNotFoundException) { checks++; }
        return checks;
    }
}
