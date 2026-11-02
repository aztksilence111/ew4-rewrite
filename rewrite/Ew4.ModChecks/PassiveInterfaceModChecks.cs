using System.Text.Json;
using Ew4.Mods;

internal static class PassiveInterfaceModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Passive interface import: " + label); }
        string run = Path.Combine(root, "build/skill-interfaces-20261102/import", Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "rewrite/data"), example = Path.Combine(root, "rewrite/mod-kit/examples/passive-auras");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/")
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        string author = Path.Combine(run, "author");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(example, "rewrite"), "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(author, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
        }
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        JsonElement Call(string route, object body)
        {
            using var response = JsonDocument.Parse(store.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), route + ": " + response.RootElement.GetRawText()[..Math.Min(300, response.RootElement.GetRawText().Length)]);
            return response.RootElement.GetProperty("data").Clone();
        }
        Call("start", new { scenario = "conquest1", country = "fra" });
        string originalSave = Path.Combine(run, "saves/saves/mode-2/slot-0.json");
        byte[] originalBytes = File.ReadAllBytes(originalSave);
        store.Seal(author, "passive-interfaces", "被动光环接口示例", "1.0", ["conquest1"]);
        Check(store.Validate(author).Files.Keys.Count(p => p.StartsWith("rewrite/")) == 5, "extension-only author package seals");
        var imported = store.Import(author, "ignored", "ignored");
        store.Activate(imported.Key);
        var started = Call("start", new { scenario = "conquest1", country = "fra" });
        var unit = started.GetProperty("units").EnumerateArray().First(u => u.GetProperty("profile").GetString() == "ew4.unit.fra.heavy_artillery.1");
        string id = unit.GetProperty("id").GetString()!;
        Check(unit.GetProperty("skills").GetArrayLength() == 4 && unit.GetProperty("maximumRange").GetInt32() > 1, "actual client exposes passive skills and effective range");
        int hp = unit.GetProperty("maxHealth").GetInt32();
        Call("save", new { mode = 2, slot = 1 });
        var restored = Call("load", new { mode = 2, slot = 1 }).GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == id);
        Check(restored.GetProperty("maxHealth").GetInt32() == hp && restored.GetProperty("maximumRange").GetInt32() == unit.GetProperty("maximumRange").GetInt32(),
            "actual session save reload preserves attributes once");
        string archive = Path.Combine(run, "passive-interfaces.ew4mod"); store.Export(imported.Key, archive);
        var other = new ModStore(baseline, Path.Combine(run, "other-mods"), Path.Combine(run, "other-saves"));
        Check(other.Import(archive, "ignored", "ignored").LogicRevision == imported.LogicRevision, "offline archive roundtrip");
        store.Activate(null);
        Check(File.ReadAllBytes(originalSave).SequenceEqual(originalBytes), "original save bytes retained");
        return checks;
    }
}
