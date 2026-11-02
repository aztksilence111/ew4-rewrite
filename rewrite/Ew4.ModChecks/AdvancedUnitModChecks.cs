using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Mods;
using Ew4.Persistence;

internal static class AdvancedUnitModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool condition, string label) { checks++; if (!condition) throw new Exception("Advanced Mod: " + label); }
        string data = Path.Combine(root, "rewrite/data"), run = Path.Combine(root, "build/advanced-units-20261102/mod");
        byte[] Read(string p) => File.ReadAllBytes(p.StartsWith("frontend/") ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p));
        var names = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')).ToArray();
        var baseline = new ModBase(Read, names);
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        string author = Path.Combine(run, "author"); Directory.CreateDirectory(Path.Combine(author, "rewrite")); Directory.CreateDirectory(Path.Combine(author, "client"));
        foreach (string file in new[] { "extension.json", "unit-layers.json", "construction.json" })
            File.Copy(Path.Combine(root, "rewrite/mod-kit/examples/advanced-units/rewrite", file), Path.Combine(author, "rewrite", file), true);
        var visual = JsonNode.Parse(Read("client/unit-visuals.json"))!.AsObject();
        visual["composites"] = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "rewrite/mod-kit/examples/advanced-units/client/unit-visuals.fragment.json")))!["composites"]!.DeepClone();
        var fx = JsonNode.Parse(Read("client/effects.json"))!;
        visual["definitions"]!["demo.attack.air"] = fx["motions"]!["Militia fra 1"]!.DeepClone();
        visual["definitions"]!["demo.air"] = fx["motions"]!["Militia fra 1"]!.DeepClone();
        visual["definitions"]!["demo.air"]!["attackVisuals"] = new JsonObject { ["air"] = "demo.attack.air" };
        visual["composites"]!["ew4.unit.fra.light_infantry.0"]![1]!["visual"] = "demo.air";
        File.WriteAllText(Path.Combine(author, "client/unit-visuals.json"), visual.ToJsonString());
        UnitVisualConfiguration.Validate(visual, JsonNode.Parse(Read("client/manifest.json"))!.AsObject(), fx.AsObject(), Read);
        var badVisual = visual.DeepClone().AsObject(); badVisual["composites"]!["ew4.unit.fra.light_infantry.0"]![0]!["visual"] = "missing";
        try { UnitVisualConfiguration.Validate(badVisual, JsonNode.Parse(Read("client/manifest.json"))!.AsObject(), fx.AsObject(), Read); throw new Exception("Invalid composite accepted"); }
        catch (InvalidDataException) { checks++; }
        badVisual = visual.DeepClone().AsObject(); badVisual["definitions"]!["demo.air"]!["attackVisuals"]!["air"] = "missing";
        try { UnitVisualConfiguration.Validate(badVisual, JsonNode.Parse(Read("client/manifest.json"))!.AsObject(), fx.AsObject(), Read); throw new Exception("Invalid attack override accepted"); }
        catch (InvalidDataException) { checks++; }
        store.Seal(author, "advanced-unit-check", "Advanced units", "1.0", ["conquest1"]);
        var installed = store.Import(author, "ignored", "ignored"); store.Activate(installed.Key);
        var content = baseline.Content(store.Read, ["conquest1"]);
        var start = content.Start("conquest1", "fra"); var world = start.Session.World;
        Check(world.Construction?.Enabled == true, "construction config installed");
        Check(world.Cells.All(c => c.BuildingKind != "airfield"), "no starting airport");
        Check(start.Rules.Recruitment[start.Session.PlayerFaction].ContainsKey("demo.heli"), "custom profile recruitment registered");
        Check(start.Rules.Recruitment.Where(p => p.Key != start.Session.PlayerFaction).All(p => !p.Value.ContainsKey("demo.heli")), "country whitelist respected");
        var engineerProfile = content.Catalogs.Runtime.Profiles["ew4.unit.fra.guards.0"].Profile;
        int cell = Enumerable.Range(0, world.Cells.Count).First(i => world.Cells[i].Owner == world.ActiveFaction && world.Cells[i].Terrain.CanTraverse()
            && !world.Cells[i].Terrain.Sea && world.Cells[i].BuildingKind == null && world.Cells[i].InstallationKind == null && world.Units.All(u => u.Cell != i));
        var engineer = new UnitState { Id = "demo.engineer", Owner = world.ActiveFaction, Cell = cell, RemainingMovement = engineerProfile.Movement,
            Combatant = new() { Profile = engineerProfile, Health = engineerProfile.MaxHealth } };
        var army = world.Units.Where(u => u.Owner != world.ActiveFaction || u.Cell != cell).Append(engineer).ToArray();
        world = world with { Units = army, Factions = world.Factions.Select(f => f.Id == world.ActiveFaction ? f with { Resources = new(10000, 10000, 10000) } : f).ToArray() };
        var seed = start.Session with { World = world }; seed.Validate();
        var aiPlan = WorldAi.PlanProduction(world, start.Rules.Ai, start.Rules.Production[world.ActiveFaction], start.Rules.Turns);
        Check(aiPlan.Command is AiBuildingCommand, "AI plans an airport using configured engineer");
        Check(WorldAi.ExecuteProduction(world, aiPlan, start.Rules.Production[world.ActiveFaction], start.Rules.Ai).State.Cells[cell].BuildingKind == "airfield", "AI constructs airport through same rules");
        string profile = Path.Combine(run, "saves/mods", installed.Id, installed.LogicRevision);
        Directory.CreateDirectory(profile);
        void Slot(int slot, GameSession session) => File.WriteAllBytes(Path.Combine(profile, "slot-" + slot + ".json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
        Slot(1, seed);
        JsonElement Call(string route, object body)
        {
            using var response = JsonDocument.Parse(store.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), route + ": " + response.RootElement.GetRawText()[..Math.Min(350, response.RootElement.GetRawText().Length)]);
            return response.RootElement.GetProperty("data").Clone();
        }
        var view = Call("load", new { slot = 1 });
        var selected = Call("action", new { op = "select", cell, unit = engineer.Id, revision = view.GetProperty("revision").GetInt64() });
        Check(selected.GetProperty("buildingOffers").GetArrayLength() == 1, "engineer sees airport build offer");
        view = Call("action", new { op = "building", unit = engineer.Id, offer = "airfield", revision = view.GetProperty("revision").GetInt64() });
        Check(view.GetProperty("cells")[cell].GetProperty("buildingKind").GetString() == "airfield", "build command creates airport");
        var built = WorldConstruction.Build(world, world.Revision, engineer.Id, "airfield").State;
        built = built with { Units = built.Units.Where(u => u.Id != engineer.Id).ToArray() };
        Check(AiTactics.Location(new AiSiteFacts("airfield", 1), null, start.Rules.Ai.Tactics) > 0, "AI can score custom airport");
        Check(start.Rules.Production[world.ActiveFaction].Upgrade.BuildingWeights.ContainsKey("airfield"), "AI knows airport upgrade weight");
        Check(WorldAi.PlanProduction(built, start.Rules.Ai, start.Rules.Production[world.ActiveFaction], start.Rules.Turns) != null, "AI production remains valid after building");
        Slot(2, seed with { World = built });
        view = Call("load", new { slot = 2 });
        selected = Call("action", new { op = "select", cell, revision = view.GetProperty("revision").GetInt64() });
        Check(selected.GetProperty("upgrade").GetBoolean(), "empty airport upgrade available");
        Check(selected.GetProperty("offers").EnumerateArray().Select(o => o.GetProperty("id").GetString()).SequenceEqual(new[] { "demo.heli" }), "level one only allows helicopter");
        view = Call("action", new { op = "upgrade", cell, revision = view.GetProperty("revision").GetInt64() });
        selected = Call("action", new { op = "select", cell, revision = view.GetProperty("revision").GetInt64() });
        Check(selected.GetProperty("offers").GetArrayLength() == 2, "airport upgrade unlocks second custom profile");
        view = Call("action", new { op = "recruit", cell, offer = "demo.heli", revision = view.GetProperty("revision").GetInt64() });
        var heli = view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("cell").GetInt32() == cell);
        Check(heli.GetProperty("occupiedLayers").GetArrayLength() == 2, "recruited helicopter has dual footprint");
        selected = Call("action", new { op = "select", cell, revision = view.GetProperty("revision").GetInt64() });
        Check(selected.GetProperty("occupants").GetArrayLength() == 1, "composite remains one selected instance");
        Check(selected.GetProperty("offers").GetArrayLength() == 0, "both airport layers now occupied");
        Call("save", new { slot = 3 });
        Check(Call("load", new { slot = 3 }).GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == heli.GetProperty("id").GetString()).GetProperty("occupiedLayers").GetArrayLength() == 2, "composite save reload");
        Slot(5, seed with { World = world with { Construction = null } });
        using (var mismatch = JsonDocument.Parse(store.Dispatch("/api/load", "{\"slot\":5}")))
            Check(!mismatch.RootElement.GetProperty("ok").GetBoolean() && mismatch.RootElement.GetProperty("code").GetString() == "construction.save", "missing construction policy cannot reinterpret save");
        string archive = Path.Combine(run, "advanced.ew4mod"); if (File.Exists(archive)) File.Delete(archive); store.Export(installed.Key, archive);
        var offline = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        Check(offline.Import(archive, "ignored", "ignored").LogicRevision == installed.LogicRevision, "new configuration exports and imports");
        store.Activate(null); var builtin = Call("start", new { scenario = "conquest1", country = "fra" });
        Check(!builtin.GetProperty("unitLayers").GetBoolean(), "builtin still disabled");
        // Independent browser fixtures cover construction and one-instance composite rendering.
        var hybridOffer = start.Rules.Recruitment[world.ActiveFaction]["demo.heli"];
        var hybrid = engineer with { Id = "demo.helicopter", Cell = cell, Combatant = hybridOffer.Template!, RemainingMovement = hybridOffer.Template!.Profile.Movement };
        var hybridWorld = built with { Units = built.Units.Append(hybrid).ToArray() };
        Slot(4, seed with { World = hybridWorld });
        File.WriteAllText(Path.Combine(run, "fixture.json"), JsonSerializer.Serialize(new { key = installed.Key, cell, engineer = engineer.Id, heli = hybrid.Id, profile,
            mods = Path.Combine(run, "mods"), saves = Path.Combine(run, "saves") }));
        return checks;
    }
}
