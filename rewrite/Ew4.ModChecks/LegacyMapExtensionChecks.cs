using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Mods;

internal static class LegacyMapExtensionChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); checks++; }
        string data = Path.Combine(root, "rewrite/data");
        string run = Path.Combine(root, "build/campaign-selection-check-20261027/map-extension-checks", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(run, "source"); Directory.CreateDirectory(source);
        byte[] ReadBase(string p) => File.ReadAllBytes(p.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p));
        var maps = XDocument.Parse(System.Text.Encoding.UTF8.GetString(ReadBase("original/def_map.xml")));
        maps.Root!.Elements().Where(e => (int)e.Attribute("id")! > 2).Remove();
        byte[] mapBytes = System.Text.Encoding.UTF8.GetBytes(maps.ToString());
        var hashes = JsonNode.Parse(ReadBase("client/import-baseline.json"))!.AsObject();
        // The fixture reuses the existing raster maps; only its map registration set differs.
        hashes["def_map.xml"] = ModFiles.Hash(mapBytes);
        var baseline = new ModBase(p => p == "client/import-baseline.json"
            ? System.Text.Encoding.UTF8.GetBytes(hashes.ToJsonString()) : ReadBase(p),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        void Write(string file, byte[] bytes) => ModFiles.Write(source, file, bytes);
        Write("def_map.xml", mapBytes);
        foreach (var map in maps.Root.Elements()) Write((string)map.Attribute("file")!, ReadBase("original/" + (string)map.Attribute("file")!));
        Write("campaign1_01.btl", ReadBase("original/campaign1_01.btl"));
        Write("AndroidManifest.xml", "<manifest/>"u8.ToArray());
        var full = store.Import(source, "independent-map-full", "Independent maps"); store.Activate(full.Key);
        Check(JsonNode.Parse(store.Read("rewrite/extension.json"))!.AsObject().Count == 2, "Full legacy package does not inherit built-in extensions");
        using (var catalog = JsonDocument.Parse(store.Dispatch("/api/catalog", "{}")))
            Check(catalog.RootElement.GetProperty("ok").GetBoolean(), "Independent two-map full package opens catalog");
        using (var start = JsonDocument.Parse(store.Dispatch("/api/start", "{\"scenario\":\"campaign1_01\"}")))
            Check(start.RootElement.GetProperty("ok").GetBoolean(), "Independent map starts without eastasia geography");
        string exported = Path.Combine(run, "independent.ew4mod"); store.Export(full.Key, exported);
        Check(store.Import(exported, "ignored", "ignored").Key == full.Key, "Export and reimport retain independent extensions");
        File.Delete(Path.Combine(source, "AndroidManifest.xml"));
        var overlay = store.Import(source, "independent-map-overlay", "Independent map overlay"); store.Activate(overlay.Key);
        Check(JsonNode.Parse(store.Read("rewrite/extension.json"))!["geography"] == null, "Legacy map overlay does not inherit unrelated geography");
        Write("rewrite/extension.json", "{\"schema\":1,\"apiVersion\":3,\"campaignVisibility\":\"rewrite/visibility.json\"}"u8.ToArray());
        Write("rewrite/visibility.json", "{\"schema\":1,\"visibility\":{\"*\":\"visible\"}}"u8.ToArray());
        var explicitRules = store.Import(source, "explicit-map-rules", "Explicit map rules"); store.Activate(explicitRules.Key);
        Check(JsonNode.Parse(store.Read("rewrite/extension.json"))!["campaignVisibility"]!.GetValue<string>() == "rewrite/visibility.json", "Explicit package extension remains authoritative");
        string textureOnly = Path.Combine(run, "resource-overlay"); Directory.CreateDirectory(textureOnly);
        ModFiles.Write(textureOnly, "stringtable_cn.ini", ReadBase("original/stringtable_cn.ini"));
        var resources = store.Import(textureOnly, "resource-overlay", "Resource overlay"); store.Activate(resources.Key);
        Check(store.Read("rewrite/extension.json").SequenceEqual(ReadBase("rewrite/extension.json")), "Resource-only overlays retain built-in map rules");
        store.Activate(null);
        Check(store.Read("rewrite/extension.json").SequenceEqual(ReadBase("rewrite/extension.json")), "Returning to builtin restores its geography");
        return checks;
    }
}
