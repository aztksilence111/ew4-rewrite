using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Mods;
using Ew4.Persistence;

internal static class UnitLayerModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Unit layer Mod: " + label); }
        string data = Path.Combine(root, "rewrite/data"), run = Path.Combine(root, "build/unit-layers-20261102/mod");
        byte[] Read(string p) => File.ReadAllBytes(p.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p));
        var names = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')).ToArray();
        var baseline = new ModBase(Read, names);
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        string author = Path.Combine(run, "author");
        Directory.CreateDirectory(Path.Combine(author, "rewrite"));
        foreach (string file in new[] { "extension.json", "unit-layers.json" })
            File.Copy(Path.Combine(root, "rewrite/mod-kit/examples/unit-layers/rewrite", file), Path.Combine(author, "rewrite", file), true);
        store.Seal(author, "unit-layer-check", "Unit layer check", "1.0", ["conquest1"]);
        var installed = store.Import(author, "ignored", "ignored");
        store.Activate(installed.Key);
        JsonElement Call(string route, object body)
        {
            using var response = JsonDocument.Parse(store.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), route + " " + response.RootElement.GetRawText()[..Math.Min(220, response.RootElement.GetRawText().Length)]);
            return response.RootElement.GetProperty("data").Clone();
        }
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        Check(view.GetProperty("unitLayers").GetBoolean(), "installed Mod opts in");
        Call("save", new { slot = 1 });
        Check(Call("load", new { slot = 1 }).GetProperty("unitLayers").GetBoolean(), "installed Mod save restores policy");
        string archive = Path.Combine(run, "unit-layers.ew4mod");
        if (File.Exists(archive)) File.Delete(archive);
        store.Export(installed.Key, archive);
        var offline = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        var imported = offline.Import(archive, "ignored", "ignored");
        Check(imported.LogicRevision == installed.LogicRevision, "export/import binds same rules");
        store.Activate(null);
        Check(!Call("start", new { scenario = "conquest1", country = "fra" }).GetProperty("unitLayers").GetBoolean(), "builtin remains disabled after switching from Mod");
        var disabled = File.ReadAllText(Path.Combine(author, "rewrite/unit-layers.json")).Replace("\"enabled\": true", "\"enabled\": false", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(author, "rewrite/unit-layers.json"), disabled);
        store.Seal(author, "unit-layer-check", "Unit layer check", "1.1", ["conquest1"]);
        var changed = store.Import(author, "ignored", "ignored");
        Check(changed.LogicRevision != installed.LogicRevision, "changing layer policy gets an isolated save namespace");

        // This temporary shared-cell save also drives the real desktop/mobile browser checks.
        store.Activate(installed.Key);
        var content = baseline.Content(store.Read, ["conquest1"]);
        var start = content.Start("conquest1", "fra");
        var w = start.Session.World;
        var ground = w.Units.First(u => u.Owner == start.Session.PlayerFaction && !WorldUnitLayers.Air(w, u) && u.Combatant.Profile.Category == "infantry");
        var plane = new UnitState { Id = "browser.air", Owner = ground.Owner, Cell = ground.Cell,
            Combatant = new() { Profile = content.Catalogs.Runtime.Profiles["ew4.unit.fra.light_infantry.0"].Profile,
                Health = content.Catalogs.Runtime.Profiles["ew4.unit.fra.light_infantry.0"].Profile.MaxHealth } };
        var session = start.Session with { World = w with { Units = w.Units.Append(plane).ToArray() } };
        session.Validate();
        string profile = Path.Combine(run, "saves/mods", installed.Id, installed.LogicRevision);
        File.WriteAllBytes(Path.Combine(profile, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
        File.WriteAllBytes(Path.Combine(run, "fixture.json"), JsonSerializer.SerializeToUtf8Bytes(new {
            key = installed.Key, ground = ground.Id, air = plane.Id, cell = ground.Cell, profile,
            mods = Path.Combine(run, "mods"), saves = Path.Combine(run, "saves") }));
        return checks;
    }
}
