using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;

internal static class MapColorChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception("Map colors: " + label); }
        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string path) => overrides.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        void Put(string path, object value) => overrides[path] = JsonSerializer.SerializeToUtf8Bytes(value, ExtensionLoader.Json);
        var manifest = JsonNode.Parse(Read("rewrite/extension.json"))!.AsObject();
        manifest["mapColors"] = "rewrite/map-colors-test.json";
        Put("rewrite/extension.json", manifest);
        var configurations = new Dictionary<string, MapColorsPresentation> {
            ["*"] = new() { CampPalette = ["#01020360", "#040506"], Camps = new() { ["ew4.camp.1"] = "#11223380", ["rebels"] = "#00000000" } },
            ["conquest1"] = new() { Camps = new() { ["ew4.camp.1"] = "#AABBCC60", ["ew4.camp.2"] = "#123456" } }
        };
        Put("rewrite/map-colors-test.json", configurations);
        ClientContent Content() => new(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var content = Content();
        var effective = MapColorsPresentation.Resolve(content.Extensions.MapColors, "conquest1");
        Check(effective.Camps["ew4.camp.1"] == "#AABBCC60" && effective.Camps["rebels"] == "#00000000", "local overrides and global future camp retained");
        Check(effective.CampPalette!.SequenceEqual(configurations["*"].CampPalette!), "local missing palette inherits global");
        Check(MapColorsPresentation.Resolve(content.Extensions.MapColors, "conquest2").Camps["ew4.camp.1"] == "#11223380", "global fallback");
        configurations["conquest1"] = configurations["conquest1"] with { CampPalette = ["#102030"] };
        Put("rewrite/map-colors-test.json", configurations);
        var changed = Content();
        Check(changed.Extensions.Revision != content.Extensions.Revision, "configuration participates in content fingerprint");
        Check(MapColorsPresentation.Resolve(changed.Extensions.MapColors, "conquest1").CampPalette![0] == "#102030", "local palette overrides global");
        var countries = JsonNode.Parse(Read("client/countries.json"))!.AsObject();
        countries["countries"]!["fra"]!["color"] = "#237A4D80";
        Put("client/countries.json", countries);
        content = Content();
        string folder = Path.Combine(root, "build/map-colors-20261025/checks-" + Guid.NewGuid().ToString("N"));
        var game = new ClientGame(content, folder);
        JsonElement Call(string path, object body)
        {
            using var document = JsonDocument.Parse(game.Dispatch("/api/" + path, JsonSerializer.Serialize(body)));
            Check(document.RootElement.GetProperty("ok").GetBoolean(), path + " succeeds");
            return document.RootElement.GetProperty("data").Clone();
        }
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        var france = view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("country").GetString() == "fra");
        Check(france.GetProperty("color").GetInt32() == ScenarioFactions.PackedColor("#237A4D80"), "existing country color reaches renderer state");
        Check(view.GetProperty("mapColors").GetProperty("camps").GetProperty("ew4.camp.1").GetString() == "#AABBCC60", "effective camp colors reach renderer state");
        Call("save", new { mode = 2, slot = 1 });
        view = Call("load", new { mode = 2, slot = 1 });
        Check(view.GetProperty("mapColors").GetProperty("campPalette")[0].GetString() == "#102030", "load restores effective camp configuration");
        Check(view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("country").GetString() == "fra").GetProperty("color").GetInt32()
            == ScenarioFactions.PackedColor("#237A4D80"), "load preserves global country color");
        void Reject(string json, string code)
        {
            overrides["rewrite/map-colors-test.json"] = System.Text.Encoding.UTF8.GetBytes(json);
            try { Content(); } catch (RuleViolation error) { Check(error.Code == code, "reject " + code); return; }
            throw new Exception("Map colors accepted invalid configuration: " + json);
        }
        Reject("{\"*\":{\"campPalette\":[]}}", "extension.map_colors");
        Reject("{\"*\":{\"campPalette\":[null]}}", "extension.map_colors");
        Reject("{\"*\":{\"camps\":null}}", "extension.map_colors");
        Reject("{\"*\":null}", "extension.map_colors");
        Reject("{\"*\":{\"camps\":{\"\":\"#123456\"}}}", "extension.map_colors");
        Reject("{\"*\":{\"camps\":{\"x\":\"#zzzzzz\"}}}", "scenario.appearance");
        Reject("{\"missing-scenario\":{}}", "extension.map_colors");
        Put("rewrite/map-colors-test.json", configurations);
        manifest["apiVersion"] = 2; manifest.Remove("geography"); Put("rewrite/extension.json", manifest);
        try { Content(); } catch (RuleViolation error) { Check(error.Code == "extension.version", "API 3 required"); return checks; }
        throw new Exception("Map colors accepted API 2");
    }
}
