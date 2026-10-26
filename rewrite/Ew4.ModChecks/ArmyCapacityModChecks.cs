using System.Text;
using System.Text.Json;
using Ew4.Legacy;
using Ew4.Mods;

internal static class ArmyCapacityModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Army capacity Mod: " + label); }
        string data = Path.Combine(root, "rewrite/data"), run = Path.Combine(root, "build/army-capacity-20261026/mod-checks");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", path[9..]) : Path.Combine(data, path));
        string[] names = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')).ToArray();
        var baseline = new ModBase(Read, names);
        var oldBase = new ModBase(Read, names.Where(p => p != ArmyCapacityConfiguration.Path));
        string author = Path.Combine(run, "author"), oldAuthor = Path.Combine(run, "old-author");
        Directory.CreateDirectory(Path.Combine(author, "rewrite"));
        Directory.CreateDirectory(Path.Combine(oldAuthor, "rewrite"));
        const string extension = "{\"schema\":1,\"apiVersion\":3}";
        File.WriteAllText(Path.Combine(author, "rewrite/extension.json"), extension);
        File.WriteAllText(Path.Combine(oldAuthor, "rewrite/extension.json"), extension);
        var oldStore = new ModStore(oldBase, Path.Combine(run, "old-mods"), Path.Combine(run, "old-saves"));
        oldStore.Seal(oldAuthor, "capacity-check", "Capacity check", "1.0", ["conquest1"]);
        var oldManifest = oldStore.Validate(oldAuthor);
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        Check(store.Validate(oldAuthor).LogicRevision == oldManifest.LogicRevision, "older installed Mod remains valid when default capacity file is added");
        File.WriteAllText(Path.Combine(author, ArmyCapacityConfiguration.Path), "{\"baseCapacity\":10}");
        store.Seal(author, "capacity-check", "Capacity check", "1.1", ["conquest1"]);
        var info = store.Import(author, "ignored", "ignored");
        Check(info.LogicRevision == oldManifest.LogicRevision, "capacity tuning does not fork existing account namespace");
        store.Activate(info.Key);
        JsonElement Call(string route, object body)
        {
            using var response = JsonDocument.Parse(store.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), route);
            return response.RootElement.GetProperty("data").Clone();
        }
        void France(JsonElement view, int limit)
        {
            var faction = view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == view.GetProperty("playerFaction").GetString());
            Check(faction.GetProperty("unitLimit").GetInt32() == limit && faction.GetProperty("unitCount").GetInt32() == 32, "installed capacity reaches client view");
        }
        France(Call("start", new { scenario = "conquest1", country = "fra" }), 51);
        Call("save", new { mode = 2, slot = 1 });
        string archive = Path.Combine(run, "capacity.ew4mod"); store.Export(info.Key, archive);
        var offline = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        var imported = offline.Import(archive, "ignored", "ignored");
        Check(imported.LogicRevision == info.LogicRevision, "export/import preserves capacity and save identity");
        File.WriteAllText(Path.Combine(author, ArmyCapacityConfiguration.Path), "{\"baseCapacity\":12}");
        store.Seal(author, "capacity-check", "Capacity check", "1.2", ["conquest1"]);
        var tuned = store.Import(author, "ignored", "ignored"); store.Activate(tuned.Key);
        France(Call("load", new { mode = 2, slot = 1 }), 53);
        store.Activate(null);
        France(Call("start", new { scenario = "conquest1", country = "fra" }), 49);
        var startup = StartupAssets.Prepare(new ModBase(p => p == ArmyCapacityConfiguration.Path
            ? Encoding.UTF8.GetBytes("{\"baseCapacity\":12}") : Read(p), names), Path.Combine(run, "startup"));
        Check(startup.Content.SaveNamespace == null, "editing only capacity retains builtin save namespace");
        return checks;
    }
}
