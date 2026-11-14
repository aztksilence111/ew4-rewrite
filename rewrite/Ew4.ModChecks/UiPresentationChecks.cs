using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Mods;

internal static class UiPresentationChecks
{
    internal static int Run(string root, string scratch, bool real)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; }
        string data = System.IO.Path.Combine(root, "rewrite/data");
        byte[] Read(string p) => File.ReadAllBytes(p.StartsWith("frontend/") ? System.IO.Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : System.IO.Path.Combine(data, p));
        var files = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => System.IO.Path.GetRelativePath(data, p).Replace('\\', '/'))
            .Concat(["frontend/conquest-pages.json", "frontend/city-names.json"]).ToHashSet();
        var baseline = new ModBase(Read, files);
        var manifest = JsonNode.Parse(Read("client/manifest.json"))!.AsObject();
        JsonObject Config(string json) => UiPresentation.Read(Encoding.UTF8.GetBytes(json));
        var valid = Config("""{"schema":1,"screens":{"main":{"controls":{"entries":{"right":24,"y":90},"campaign":{"image":"btn_common_blue.png","pressedImage":"btn_common_red.png"}}},"settings":{"panel":{"background":"#102030"}}},"native":{"form_save":{"btn_save":{"x":20,"color":"#f0e0d0","fontSize":12}}}}""");
        // Locate a real unique leaf rather than depending on a particular save-slot form revision.
        valid["native"] = new JsonObject { ["form_pause"] = new JsonObject { ["btn_save"] = new JsonObject { ["x"] = 20, ["color"] = "#f0e0d0", ["fontSize"] = 12 } } };
        UiPresentation.Check(valid, manifest, files, Read); checks++;
        UiPresentation.Apply(manifest, valid);
        var save = manifest["layouts"]!["form_pause"]!["children"]!.AsArray().Single(n => (string?)n!["attributes"]!["id"] == "btn_save")!["attributes"]!;
        Check((string?)save["x"] == "20" && (string?)save["color"] == "240,224,208,255" && (string?)save["size"] == "12", "native patches match renderer attributes");
        Check(JsonNode.DeepEquals(manifest["uiPresentation"], valid), "frontend receives config copy");
        Check(JsonNode.Parse(Read("client/manifest.json"))!["uiPresentation"] == null, "base bytes unchanged");
        foreach (string bad in new[] {
            """{"schema":2}""", """{"schema":1,"script":"fetch('/api/action')"}""",
            """{"schema":1,"screens":{"settings":{"controls":{"export":{"x":0}}}}}""",
            """{"schema":1,"screens":{"main":{"controls":{"entries":{"x":10,"right":10}}}}}""",
            """{"schema":1,"screens":{"main":{"controls":{"entries":{"image":"rewrite/../secret.png"}}}}}""",
            """{"schema":1,"screens":{"main":{"controls":{"entries":{"image":"no-image.png"}}}}}""",
            """{"schema":1,"screens":{"main":{"panel":{"w":200}}}}""",
            """{"schema":1,"native":{"form_pause":{"missing":{"x":0}}}}""",
            """{"schema":1,"native":{"form_pause":{"btn_save":{"right":20}}}}""",
            """{"schema":1,"screens":{"battle":{"controls":{"pause":{"hidden":true}}}}}"""
        })
        {
            bool rejected = false; try { UiPresentation.Check(Config(bad), manifest, files, Read); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid UI configuration rejected");
        }
        Directory.CreateDirectory(scratch);
        string author = System.IO.Path.Combine(scratch, "skin-author"), empty = System.IO.Path.Combine(scratch, "empty-author");
        Directory.CreateDirectory(System.IO.Path.Combine(author, "rewrite")); Directory.CreateDirectory(empty);
        File.WriteAllText(System.IO.Path.Combine(author, UiPresentation.Path), valid.ToJsonString());
        var skinStore = new ModStore(baseline, System.IO.Path.Combine(scratch, "skin-mods"), System.IO.Path.Combine(scratch, "skin-saves"));
        skinStore.Seal(empty, "plain-skin", "Plain skin", "1");
        skinStore.Seal(author, "test-skin", "Test skin", "1");
        var plain = skinStore.Validate(empty); var themed = skinStore.Validate(author);
        Check(plain.LogicRevision == themed.LogicRevision, "skin configuration preserves rule fingerprint");
        var installed = skinStore.Import(author, "test-skin", "Test skin"); skinStore.Activate(installed.Key);
        Check(JsonNode.Parse(skinStore.ReadContent("manifest.json"))!["uiPresentation"] != null, "installed skin reaches active frontend");
        Check(JsonNode.Parse(skinStore.ReadContent("r-" + baseline.Revision + "/manifest.json"))!["uiPresentation"] == null, "inflight base request keeps its own presentation");
        skinStore.Activate(null);
        Check(JsonNode.Parse(skinStore.ReadContent("manifest.json"))!["uiPresentation"] == null, "deactivation restores builtin presentation");
        // The standard layout edits must survive even when 568h is present and unchanged.
        string fixture = System.IO.Path.Combine(scratch, "layout-fixture"); Directory.CreateDirectory(fixture);
        var references = JsonNode.Parse(Read("client/import-layouts.json"))!;
        var xml = XDocument.Parse((string)references["layout.xml"]!);
        var button = xml.Descendants().Single(n => (string?)n.Attribute("id") == "btn_save" && (string?)n.Parent?.Attribute("id") == "form_pause");
        button.SetAttributeValue("x", (double)button.Attribute("x")! + 9); button.SetAttributeValue("frm1", "btn_common_red.png");
        xml.Save(System.IO.Path.Combine(fixture, "layout.xml"));
        File.WriteAllText(System.IO.Path.Combine(fixture, "layout-568h.xml"), (string)references["layout-568h.xml"]!, new UTF8Encoding(false));
        var before = JsonNode.Parse(Read("client/manifest.json"))!.AsObject();
        var after = before.DeepClone().AsObject(); var warnings = new List<string>();
        using (var input = new ImportSource(fixture)) LegacyLayouts.Import(after, baseline, new LegacyResourceResolver(baseline, input, warnings), warnings);
        var patched = after["layouts"]!["form_pause"]!["children"]!.AsArray().Single(n => (string?)n!["attributes"]!["id"] == "btn_save")!["attributes"]!;
        Check((string?)after["layoutSources"]!["form_pause"] == "layout.xml", "modified standard layout selected");
        Check((string?)patched["x"] == "51" && (string?)patched["frm1"] == "btn_common_red.png", "device-relative geometry and art migrated");
        Check((double?)after["legacyUiPresentation"]!["screens"]!["pause"]!["controls"]!["save"]!["x"] == 51, "DOM pause consumes legacy position");
        Check(JsonNode.DeepEquals(before["layouts"]!["form_mainmenu"], after["layouts"]!["form_mainmenu"]), "untouched forms retain canonical device positions");
        if (real)
        {
            var results = new JsonArray();
            foreach (string apk in new[] { "大鏖战 老友版_1.6.6(1).apK", "欧陆战争4 一战MOD64位适配.APK" })
            {
                string name = results.Count == 0 ? "aozhan" : "ww1";
                using var input = new ImportSource(System.IO.Path.Combine(root, apk));
                var served = JsonNode.Parse(Read("client/manifest.json"))!.AsObject();
                var diagnostics = new List<string>();
                LegacyLayouts.Import(served, baseline, new LegacyResourceResolver(baseline, input, diagnostics), diagnostics);
                Check(served["layoutSources"] is JsonObject sources && sources.Count > 0, "real APK changes selected");
                if (name == "ww1") Check(served["layoutSources"]!.AsObject().Any(p => (string?)p.Value == "layout.xml"), "WW1 standard-only edits no longer lost");
                results.Add(new JsonObject { ["apk"] = apk, ["layoutConversion"] = true, ["fullImportVerified"] = false, ["layouts"] = served["layoutSources"]!.DeepClone(), ["bridge"] = served["legacyUiPresentation"]?.DeepClone() });
            }
            File.WriteAllText(System.IO.Path.Combine(scratch, "real-imports.json"), results.ToJsonString(new() { WriteIndented = true }));
        }
        return checks;
    }
}
