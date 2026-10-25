using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Legacy;
using Ew4.Mods;

internal static class CatalogFileModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Split Mod: " + label); checks++; }
        string data = Path.Combine(root, "rewrite/data");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        string run = Path.Combine(root, "build/catalog-split-20261024/mod-" + Guid.NewGuid().ToString("N"));
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        string author = Path.Combine(run, "author"); Directory.CreateDirectory(Path.Combine(author, "legacy"));
        void Write(string path, JsonNode value)
        {
            string full = Path.Combine(author, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllBytes(full, LegacyCatalogFiles.Encode(value));
        }
        var commanders = JsonNode.Parse(baseline.Read("legacy/commanders.json"))!;
        int id = (int)commanders["commanders"]![0]!["legacy_id"]!;
        commanders["commanders"]![0]!["business"] = 8;
        commanders["commanders"]![0]!["skills"]!.AsArray().Add("custom.direct");
        Write("legacy/commanders.json", commanders);
        Write("rewrite/extension.json", JsonSerializer.SerializeToNode(new { schema = 1, apiVersion = 1, skills = new[] { "rewrite/skills.json" } })!);
        Write("rewrite/skills.json", JsonNode.Parse("[{\"id\":\"custom.direct\",\"nameKey\":\"Direct skill\",\"effects\":[{\"event\":\"turn.start\",\"operation\":\"heal\",\"value\":3}]}]")!);
        store.Seal(author, "split-file-test", "Split files", "1.0", ["conquest1"]);
        var first = store.Validate(author);
        Check(first.Files.Count == 3, "a single authoring section packs with extension files");
        var info = store.Import(author, "ignored", "ignored"); store.Activate(info.Key);
        var content = baseline.Content(store.Read, ["conquest1"]);
        Check(content.Catalogs.Accounts.CommanderCommerce[id] == 8, "installed section controls commerce");
        Check(content.Catalogs.Runtime.Commanders[id].Definition.Skills.Contains("custom.direct"), "commander JSON binds a registered R skill directly");
        var started = JsonNode.Parse(store.Dispatch("/api/start", "{\"scenario\":\"conquest1\",\"country\":\"fra\"}"))!;
        Check((bool?)started["ok"] == true, "split-file Mod starts actual game");
        string archive = Path.Combine(run, "split.ew4mod"); store.Export(info.Key, archive);
        Check(store.Import(archive, "ignored", "ignored").Key == info.Key, "split sections survive export/reimport");
        commanders["commanders"]![0]!["business"] = 9; Write("legacy/commanders.json", commanders);
        store.Seal(author, "split-file-test", "Split files", "1.1", ["conquest1"]);
        Check(store.Validate(author).LogicRevision != first.LogicRevision, "editing a section changes save isolation fingerprint");

        string legacy = Path.Combine(run, "legacy"); Directory.CreateDirectory(Path.Combine(legacy, "legacy"));
        File.WriteAllBytes(Path.Combine(legacy, "legacy/catalog.json"), LegacyCatalogFiles.Encode(baseline.Catalog()));
        store.Seal(legacy, "old-format-test", "Old format", "1.0", ["conquest1"]);
        var old = store.Import(legacy, "ignored", "ignored"); store.Activate(old.Key);
        Check((bool?)JsonNode.Parse(store.Dispatch("/api/start", "{\"scenario\":\"conquest1\",\"country\":\"fra\"}"))!["ok"] == true, "schema-2 full-catalog overlays still start");
        return checks;
    }
}
