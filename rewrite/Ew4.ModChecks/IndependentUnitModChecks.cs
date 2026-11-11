using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;
using Ew4.Persistence;

internal static class IndependentUnitModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool condition, string label) { checks++; if (!condition) throw new Exception("Independent units: " + label); }
        void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is RuleViolation or InvalidDataException or JsonException) { checks++; return; } throw new Exception("Accepted invalid " + label); }
        string data = Path.Combine(root, "rewrite/data"), run = Path.Combine(root, "build/independent-unit-checks");
        Directory.CreateDirectory(run);
        byte[] Read(string p) => File.ReadAllBytes(Path.Combine(data, p));
        var names = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')).ToArray();
        var baseline = new ModBase(Read, names);
        var files = new Dictionary<string, byte[]>();
        void Put(string path, JsonNode value) => files[path] = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] Overlay(string p) => files.GetValueOrDefault(p) ?? Read(p);
        var units = JsonNode.Parse(Read("legacy/units.json"))!.AsObject();
        const string infantry = "example.unit.rifle.0", naval = "example.unit.cruiser.0";
        var manifest = JsonNode.Parse(Read("client/manifest.json"))!.AsObject();
        foreach (var (id, template) in new[] { (infantry, "ew4.unit.fra.militia.0"), (naval, "ew4.unit.fra.frigate.0") })
        {
            var profile = units["units"]!.AsArray().Single(p => (string?)p!["id"] == template)!.DeepClone();
            profile["id"] = id; profile["max_health"] = 377; units["units"]!.AsArray().Add(profile);
            var visual = manifest["profiles"]![template]!.DeepClone(); visual["name"] = id; visual["recruitGroup"] = id;
            manifest["profiles"]![id] = visual;
        }
        Put("legacy/units.json", units); Put("client/manifest.json", manifest);
        var offers = new JsonArray();
        foreach (var (id, kind) in new[] { (infantry, "city"), (naval, "port") })
            offers.Add(new JsonObject { ["id"] = id + ".recruit", ["profile"] = id, ["buildingKinds"] = new JsonArray(kind), ["minimumLevel"] = 1,
                ["cost"] = new JsonObject { ["money"] = 87, ["industry"] = 13, ["food"] = 5 }, ["countries"] = new JsonArray("fra"), ["scenarios"] = new JsonArray("conquest1") });
        var config = new JsonObject { ["schema"] = 1, ["offers"] = offers };
        var extension = new JsonObject { ["schema"] = 1, ["apiVersion"] = 3, ["unitRecruitment"] = "rewrite/unit-recruitment.json" };
        Put("rewrite/extension.json", extension); Put("rewrite/unit-recruitment.json", config);
        var content = baseline.Content(Overlay, ["conquest1", "conquest2"]);
        var started = content.Start("conquest1", "fra"); var world = started.Session.World; string player = started.Session.PlayerFaction;
        Check(content.Catalogs.Runtime.Profiles[infantry].Profile.MaxHealth == 377, "new profile attributes loaded");
        Check(content.Catalogs.Runtime.Profiles["ew4.unit.fra.militia.0"].Profile.MaxHealth != 377, "old profile retained unchanged");
        Check(world.Construction == null, "recruitment does not enable construction");
        Check(started.Rules.Recruitment[player].ContainsKey(infantry + ".recruit") && started.Rules.Recruitment[player].ContainsKey("ew4.card.0"), "old and new offers coexist");
        Check(started.Rules.Recruitment.Where(p => p.Key != player).All(p => !p.Value.ContainsKey(infantry + ".recruit")), "country scope");
        Check(content.Start("conquest2", "fra").Rules.Recruitment.Values.All(p => !p.ContainsKey(infantry + ".recruit")), "scenario scope");
        var policy = started.Rules.Production[player];
        Check(policy.Groups.Single(g => g.Id == naval + ".recruit").Naval, "AI naval production group");
        Check(!policy.Groups.Single(g => g.Id == infantry + ".recruit").Naval, "AI infantry production group");
        var creationEvents = new JsonObject { ["conquest1"] = new JsonArray(new JsonObject {
            ["id"] = "example.country", ["condition"] = new JsonObject { ["type"] = "round", ["compare"] = "ge", ["value"] = 999 },
            ["effects"] = new JsonArray(new JsonObject { ["type"] = "faction_create", ["faction"] = "example.faction", ["country"] = "fra", ["name"] = "Example faction", ["unitVariant"] = "fra", ["camp"] = "example.camp", ["flag"] = "fra", ["color"] = "#237a4d" }) }) };
        Put("rewrite/example-events.json", creationEvents); extension["scenarioEvents"] = "rewrite/example-events.json"; Put("rewrite/extension.json", extension);
        var future = baseline.Content(Overlay, ["conquest1"]).Start("conquest1", "fra");
        Check(future.Rules.Recruitment["example.faction"].ContainsKey(naval + ".recruit"), "future faction gets country-scoped offer");
        Check(future.Session.World.ScenarioEvents.Single(e => e.Id == "example.country").Effects[0].Recruitment!.ContainsKey(naval + ".recruit"), "future faction grant prepared");
        files.Remove("rewrite/example-events.json"); extension.Remove("scenarioEvents"); Put("rewrite/extension.json", extension);
        world = world with { ActiveFaction = player, PendingDialogue = null, QueuedDialogues = [], Factions = world.Factions.Select(f => f.Id == player ? f with { Resources = new(5000, 5000, 5000) } : f).ToArray() };
        var browserFixtures = new List<(int Slot, int Cell, string Profile, GameSession Session)>();
        foreach (var (id, sea, kind) in new[] { (infantry, false, "city"), (naval, true, "port") })
        {
            var offer = started.Rules.Recruitment[player][id + ".recruit"];
            int cell = Enumerable.Range(0, world.Cells.Count).First(i => world.Cells[i].Terrain.Active && world.Cells[i].Terrain.Sea == sea && world.Units.All(u => u.Cell != i));
            var cells = world.Cells.ToArray(); cells[cell] = cells[cell] with { Owner = player, BuildingKind = kind, BuildingLevel = 1, BuildingConstructionTurns = 0 };
            var candidate = world with { Cells = cells };
            browserFixtures.Add((sea ? 2 : 1, cell, id, started.Session with { World = candidate }));
            Check(WorldActions.CanRecruit(candidate, cell, offer), "recruit " + id);
            cells = candidate.Cells.ToArray(); cells[cell] = cells[cell] with { BuildingLevel = 0 };
            Check(!WorldActions.CanRecruit(candidate with { Cells = cells }, cell, offer), "minimum level " + id);
            cells[cell] = cells[cell] with { BuildingLevel = 1, BuildingKind = "stable" };
            Check(!WorldActions.CanRecruit(candidate with { Cells = cells }, cell, offer), "wrong building " + id);
            var before = candidate.Factions.Single(f => f.Id == player).Resources;
            var aiPlan = new AiProductionPlan(candidate.Revision, candidate.AiRandomState, candidate.AiRandomState,
                sea ? "naval" : "recruit", new AiRecruitCommand(candidate.Revision, cell, offer.Id, "ai." + id));
            Check(WorldAi.ExecuteProduction(candidate, aiPlan, policy, started.Rules.Ai).State.Units
                .Single(u => u.Id == "ai." + id).Combatant.Profile.Id == id, "AI executes independent recruitment " + id);
            world = WorldActions.Recruit(candidate, candidate.Revision, cell, "check." + id, offer, content.Extensions.Bus).State;
            Check(world.Units.Single(u => u.Id == "check." + id).Combatant.Profile.Id == id, "independent recruited identity");
            Check(world.Factions.Single(f => f.Id == player).Resources == new Resources(before.Money - 87, before.Industry - 13, before.Food - 5), "cost deducted once");
        }
        var saved = SessionSave.Encode(started.Session with { World = world });
        var restored = SessionSave.Decode(saved, new(started.Session.ContentRevision, world.ContentRevision, started.Session.Account.ContentRevision));
        Check(restored.World.Units.Count(u => u.Combatant.Profile.Id is infantry or naval) == 2, "both independent profiles survive save reload");
        Check(restored.World.Factions.Single(f => f.Id == player).Recruitment.ContainsKey(naval + ".recruit"), "recruitment grant saved");
        void Bad(JsonObject bad, string label)
        {
            Put("rewrite/unit-recruitment.json", bad);
            Reject(() => { var c = baseline.Content(Overlay, ["conquest1"]); c.Start("conquest1", "fra"); }, label);
            Put("rewrite/unit-recruitment.json", config);
        }
        foreach (var (key, value) in new (string, JsonNode)[] { ("profile", JsonValue.Create("missing.unit")!), ("buildingKinds", new JsonArray("typo")),
            ("countries", new JsonArray("missing")), ("scenarios", new JsonArray("missing")), ("minimumLevel", JsonValue.Create(0)!) })
        { var bad = config.DeepClone().AsObject(); bad["offers"]![0]![key] = value; Bad(bad, key); }
        var duplicate = config.DeepClone().AsObject(); duplicate["offers"]!.AsArray().Add(duplicate["offers"]![0]!.DeepClone()); Bad(duplicate, "duplicate offer");
        var negative = config.DeepClone().AsObject(); negative["offers"]![0]!["cost"]!["money"] = -1; Bad(negative, "negative cost");
        var unknown = config.DeepClone().AsObject(); unknown["offers"]![0]!["typo"] = 1; Bad(unknown, "unknown field");
        extension["apiVersion"] = 2; Put("rewrite/extension.json", extension); Reject(() => baseline.Content(Overlay, ["conquest1"]), "old API");
        extension["apiVersion"] = 3; Put("rewrite/extension.json", extension);
        var noVisual = manifest.DeepClone().AsObject(); noVisual["profiles"]!.AsObject().Remove(naval);
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        string author = Path.Combine(run, "author"); Directory.CreateDirectory(author);
        void Write() { foreach (var p in files) { string target = Path.Combine(author, p.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, p.Value); } }
        Put("client/manifest.json", noVisual); Write(); store.Seal(author, "independent-units", "Independent units", "1.0", ["conquest1"]);
        Reject(() => store.Validate(author), "missing visual dependency");
        Put("client/manifest.json", manifest); Write(); store.Seal(author, "independent-units", "Independent units", "1.0", ["conquest1"]);
        var installed = store.Import(author, "ignored", "ignored"); Check(installed.Id == "independent-units", "standard Mod import");
        store.Activate(installed.Key);
        string profileFolder = Path.Combine(run, "saves/mods", installed.Id, installed.LogicRevision); Directory.CreateDirectory(profileFolder);
        foreach (var fixture in browserFixtures)
            File.WriteAllBytes(Path.Combine(profileFolder, "slot-" + fixture.Slot + ".json"), JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot("conquest1", "fra", SessionSave.Encode(fixture.Session)), ClientGame.Json));
        File.WriteAllText(Path.Combine(run, "fixture.json"), JsonSerializer.Serialize(new { installed.Key,
            cases = browserFixtures.Select(f => new { f.Slot, f.Cell, f.Profile }) }, ClientGame.Json));
        string archive = Path.Combine(run, "units.ew4mod"); if (File.Exists(archive)) File.Delete(archive); store.Export(installed.Key, archive);
        var other = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        Check(other.Import(archive, "ignored", "ignored").LogicRevision == installed.LogicRevision, "standard Mod round trip");
        Check(baseline.Content(Read, ["conquest1"]).Catalogs.Runtime.Profiles.Keys.All(id => id != infantry && id != naval), "builtin remains unchanged");
        return checks;
    }
}
