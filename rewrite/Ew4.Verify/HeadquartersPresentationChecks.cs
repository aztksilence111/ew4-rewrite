using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;

internal static class HeadquartersPresentationChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("HQ presentation: " + label); }
        string data = Path.Combine(root, "rewrite/data"), example = Path.Combine(root, "rewrite/mod-kit/examples/headquarters-layout");
        byte[] bytes = File.ReadAllBytes(Path.Combine(example, "rewrite/headquarters.json"));
        var overrides = new Dictionary<string, byte[]> {
            ["rewrite/extension.json"] = File.ReadAllBytes(Path.Combine(example, "rewrite/extension.json")),
            ["rewrite/headquarters.json"] = bytes
        };
        byte[] Read(string path) => overrides.GetValueOrDefault(path) ?? File.ReadAllBytes(Path.Combine(data,
            path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path));
        var extension = ExtensionLoader.Load(Read);
        Check(extension.HeadquartersPresentation!.Screens.Count == 3, "example loads all pages");
        Check(extension.HeadquartersPresentation.Screens["roster"].Actions.Count == 4, "button count comes from file");
        Check(!extension.HeadquartersPresentation.Screens["roster"].Actions.Values.Any(a => a.Command == "filter"), "filter is optional");
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl"));
        string temporary = Path.Combine(root, "build/hq-presentation-20261029/backend-profile");
        using var catalog = JsonDocument.Parse(new ClientGame(content, temporary).Dispatch("/api/catalog", "{}"));
        if (!catalog.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(catalog.RootElement.GetProperty("message").GetString());
        Check(catalog.RootElement.GetProperty("data").GetProperty("headquartersPresentation").GetProperty("screens").GetProperty("roster").GetProperty("layout")
            .GetProperty("attributes").GetProperty("w").GetString() == "640", "catalog exposes full layout without fixed 568 width");
        void Reject(Action<JsonObject> change, string code = "extension.headquarters_presentation")
        {
            var node = JsonNode.Parse(bytes)!.AsObject(); change(node);
            overrides["rewrite/headquarters.json"] = JsonSerializer.SerializeToUtf8Bytes(node);
            try { ExtensionLoader.Load(Read); }
            catch (RuleViolation error) { Check(error.Code == code, "reject " + code); return; }
            throw new InvalidOperationException("Expected invalid headquarters presentation rejection");
        }
        JsonObject Screen(JsonObject n) => n["screens"]!["roster"]!.AsObject();
        Reject(n => n["schema"] = 2);
        Reject(n => n["screens"] = null);
        Reject(n => Screen(n)["actions"]!["header/back"] = null);
        Reject(n => Screen(n)["actions"]!["header/back"]!["command"] = "executeJavascript");
        Reject(n => Screen(n)["actions"]!.AsObject().Remove("header/back"));
        Reject(n => Screen(n)["actions"]!["tools"]!["page"] = "missing");
        Reject(n => Screen(n)["actions"]!["tools"]!["page"] = "academy");
        Reject(n => Screen(n)["actions"]!["shop"]!["section"] = 1);
        Reject(n => Screen(n)["actions"]!["academy"]!["section"] = 3);
        Reject(n => Screen(n)["bindings"]!["roster"] = "header/title");
        Reject(n => Screen(n)["bindings"]!["arbitraryAccountField"] = "header/title");
        Reject(n => Screen(n)["bindings"]!["medals"] = "count");
        Reject(n => Screen(n)["bindings"]!["medals"] = "missing");
        Reject(n => Screen(n)["bindings"]!["roster"] = "header");
        Reject(n => Screen(n)["roster"]!["rows"] = 0);
        Reject(n => Screen(n)["roster"]!["cardScale"] = .1);
        Reject(n => Screen(n)["background"] = "url(https://invalid)");
        Reject(n => Screen(n)["backgroundImage"] = "rewrite/../outside.png");
        Reject(n => Screen(n)["backgroundImage"] = "missing.png");
        Reject(n => Screen(n)["layout"]!["attributes"]!["w"] = "0");
        Reject(n => Screen(n)["layout"]!["children"]![0]!["attributes"]!["type"] = "script");
        Reject(n => Screen(n)["layout"]!["children"]![0]!["attributes"]!["x"] = "NaN");
        Reject(n => Screen(n)["layout"]!["children"]![0]!["attributes"]!["onclick"] = "alert(1)");
        Reject(n => Screen(n)["layout"]!["children"]![0]!["attributes"]!["opacity"] = "2");
        Reject(n => Screen(n)["layout"]!["children"]![0]!["children"]![1]!["attributes"]!["color"] = "999,0,0,255");
        Reject(n => Screen(n)["layout"]!["children"]![0]!["children"]!.AsArray().Add(Screen(n)["layout"]!["children"]![0]!["children"]![0]!.DeepClone()));
        Reject(n => n["screens"]!["deployment"] = Screen(n).DeepClone());
        overrides["rewrite/headquarters.json"] = bytes;
        overrides["rewrite/extension.json"] = "{\"schema\":1,\"apiVersion\":2,\"headquartersPresentation\":\"rewrite/headquarters.json\"}"u8.ToArray();
        try { ExtensionLoader.Load(Read); throw new InvalidOperationException("Missing API version check"); }
        catch (RuleViolation error) { Check(error.Code == "extension.version", "requires API 3"); }
        overrides["rewrite/extension.json"] = File.ReadAllBytes(Path.Combine(example, "rewrite/extension.json"));
        var expanded = JsonNode.Parse(bytes)!;
        expanded["screens"]!["roster"]!["layout"]!["children"]!.AsArray().Add(JsonNode.Parse("{\"attributes\":{\"id\":\"extra\",\"type\":\"tmp_button\",\"x\":\"220\",\"y\":\"308\",\"w\":\"80\",\"h\":\"36\",\"text\":\"Tasks\"}}"));
        expanded["screens"]!["roster"]!["actions"]!["extra"] = JsonNode.Parse("{\"command\":\"tasks\"}");
        overrides["rewrite/headquarters.json"] = JsonSerializer.SerializeToUtf8Bytes(expanded);
        var changed = ExtensionLoader.Load(Read);
        Check(changed.HeadquartersPresentation!.Screens["roster"].Actions.Count == 5 && changed.Revision != extension.Revision, "extra arbitrary button loads and changes revision");
        overrides["rewrite/images/custom.png"] = Read("client/ui/image_ui_hd/medals.png");
        expanded["screens"]!["roster"]!["backgroundImage"] = "rewrite/images/custom.png";
        overrides["rewrite/headquarters.json"] = JsonSerializer.SerializeToUtf8Bytes(expanded);
        Check(ExtensionLoader.Load(Read).HeadquartersPresentation!.Screens["roster"].BackgroundImage == "rewrite/images/custom.png", "package PNG loads without atlas registration");
        overrides["rewrite/images/custom.png"] = [1, 2, 3];
        try { ExtensionLoader.Load(Read); throw new InvalidOperationException("Missing PNG validation"); }
        catch (RuleViolation error) { Check(error.Code == "extension.headquarters_presentation", "reject corrupt PNG"); }
        overrides["rewrite/extension.json"] = "{\"schema\":1,\"apiVersion\":3}"u8.ToArray();
        Check(ExtensionLoader.Load(Read).HeadquartersPresentation == null, "old mods preserve default interface");
        return checks;
    }
}
