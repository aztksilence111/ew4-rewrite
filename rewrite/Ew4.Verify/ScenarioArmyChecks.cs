using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ScenarioArmyChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Scenario armies: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + " got " + e.Code); return; }
            throw new Exception("Scenario armies accepted " + code);
        }
        var basis = WorldChecks.Example() with
        {
            ScenarioPlayerFaction = "winner",
            Factions = WorldChecks.Example().Factions.Select(f => f with { Resources = new(100, 100, 100) }).ToArray(),
            Cells = WorldChecks.Example().Cells.Select((c, i) => c with { BuildingKind = i is 0 or 8 ? "city" : null }).ToArray()
        };
        var actor = basis.Units[0].Combatant;
        var commander = new ScenarioCommanderTemplate
        {
            Definition = new() { Id = "person.1", DisplayName = "Test general", Stars = new Dictionary<string, int> { ["infantry"] = 7 }, Movement = 2 },
            Runtime = new() { Rank = new(1, 0), RankHealthBonus = 50 }, TitleHealing = new Dictionary<int, int> { [0] = 0 }
        };
        var spawn = new ScenarioEffect { Type = "unit_spawn", Faction = "winner", Unit = "reinforcement", Cells = [8, 7],
            UnitTemplate = new(actor, new() { FoodConsumption = 1 }), Commander = "new.general", CommanderTemplate = commander, Health = 60, Cost = new(10, 3, 2) };
        WorldState Execute(WorldState state, string id, params ScenarioEffect[] effects)
        {
            var ev = new ScriptedScenarioEvent { Id = id, Effects = effects };
            var ready = state with { ScenarioEvents = state.ScenarioEvents.Append(ev).ToArray() };
            ScenarioRules.Validate(ready, true);
            return ScenarioRules.Evaluate(ready).State;
        }
        var reinforced = Execute(basis, "reinforce", spawn);
        var unit = reinforced.Units.Single(u => u.Id == "reinforcement");
        Check(reinforced.Factions.Count == 2 && reinforced.Units.Count == 3, "existing faction receives army without country creation");
        Check(unit.Cell == 7 && unit.Combatant.Health == 60 && unit.Combatant.Profile.MaxHealth == 150, "fallback site and post-command health");
        Check(unit.Combatant.Stars == 7 && unit.CommanderId == "new.general", "actual non-capped commander ability attached");
        Check(unit.Acted && unit.RemainingMovement == 0, "reinforcement cannot act immediately");
        var immediate = Execute(basis, "exercise", spawn with { Ready = true, Commander = null, CommanderTemplate = null,
            UnitTemplate = new(actor, new() { FoodConsumption = 1 }, 9) });
        var readyUnit = immediate.Units.Single(u => u.Id == "reinforcement");
        Check(!readyUnit.Acted && readyUnit.RemainingMovement == 9, "explicit ready spawn uses resolved movement budget");
        var readySave = WorldSave.Decode(WorldSave.Encode(immediate), immediate.ContentRevision);
        Check(!readySave.Units.Single(u => u.Id == readyUnit.Id).Acted && readySave.ScenarioEvents.Last().Effects.Single().Ready,
            "ready spawn and remaining action survive save without replay");
        Reject(() => ScenarioArmies.ValidateEffect(new() { Type = "money", Ready = true }), "scenario.army_effect");
        Check(reinforced.Factions[1].Resources == new Resources(90, 97, 98), "cost paid once");
        Check(basis.Units.Count == 2 && basis.Factions[1].Resources.Money == 100, "input snapshot unchanged");
        var saved = WorldSave.Decode(WorldSave.Encode(reinforced), reinforced.ContentRevision);
        Check(saved.Commanders.Single().DefinitionId == "person.1" && saved.SpawnedScenarioUnits.Contains(unit.Id), "identity mapping and spawn history saved");
        Check(ScenarioRules.Evaluate(saved).Notices.Count == 0, "save does not replay spawn");
        var transfer = Execute(reinforced, "transfer", new ScenarioEffect { Type = "unit_owner", Faction = "loser", ExpectedOwner = "winner", Unit = unit.Id });
        var moved = transfer.Units.Single(u => u.Id == unit.Id);
        Check(moved.Owner == "loser" && moved.CommanderId == unit.CommanderId && moved.Combatant.Health == 60 && moved.Cell == 7, "original army and commander transfer without relocation or healing");
        Check(transfer.Commanders.Single().Owner == "loser", "runtime owner follows army");
        Reject(() => Execute(transfer, "steal", new ScenarioEffect { Type = "unit_owner", Faction = "winner", ExpectedOwner = "winner", Unit = unit.Id }), "scenario.army_owner");
        var detached = Execute(reinforced, "detach", new ScenarioEffect { Type = "commander_detach", Faction = "winner", Unit = unit.Id,
            Commander = "new.general", CommanderTemplate = commander, Disposition = "available" });
        var bare = detached.Units.Single(u => u.Id == unit.Id);
        Check(bare.CommanderId == null && bare.Combatant.Health == 40 && bare.Combatant.Profile.MaxHealth == 100, "detach preserves injured ratio");
        Check(ScenarioRules.Match(detached, new() { Type = "commander_available", Commander = "new.general" }), "available commander is alive and unattached");
        var reassigned = Execute(detached, "reassign", new ScenarioEffect { Type = "commander_assign", Faction = "winner", Unit = unit.Id,
            Commander = "new.general", CommanderTemplate = commander });
        Check(reassigned.Units.Single(u => u.Id == unit.Id).Combatant.Health == 60, "reassign does not heal");
        var withdrawn = Execute(reassigned, "withdraw", new ScenarioEffect { Type = "commander_detach", Faction = "winner", Unit = unit.Id,
            Commander = "new.general", CommanderTemplate = commander, Disposition = "withdrawn" });
        Check(ScenarioRules.Match(withdrawn, new() { Type = "commander_withdrawn", Commander = "new.general" })
            && !withdrawn.DeadScenarioCommanders.Contains("new.general"), "withdrawal is not death");
        Reject(() => Execute(withdrawn, "return", new ScenarioEffect { Type = "commander_assign", Faction = "winner", Unit = unit.Id,
            Commander = "new.general", CommanderTemplate = commander }), "commander.unavailable");
        Reject(() => Execute(reinforced, "occupied", new ScenarioEffect { Type = "commander_assign", Faction = "winner", Unit = unit.Id,
            Commander = "other.general", CommanderTemplate = commander with { Definition = commander.Definition with { Id = "person.2" } } }), "commander.occupied");
        var replacement = Execute(reinforced, "replace",
            new() { Type = "commander_detach", Faction = "winner", Unit = unit.Id, Commander = "new.general", CommanderTemplate = commander, Disposition = "withdrawn" },
            new() { Type = "commander_assign", Faction = "winner", Unit = unit.Id, Commander = "successor.general",
                CommanderTemplate = commander with { Definition = commander.Definition with { Id = "person.2" } } });
        Check(replacement.Units.Count == reinforced.Units.Count && replacement.Units.Single(u => u.Id == unit.Id).CommanderId == "successor.general"
            && replacement.Units.Single(u => u.Id == unit.Id).Combatant.Health == 60, "replacement keeps the same injured army");
        Check(replacement.Commanders.Single(c => c.Id == "new.general").Withdrawn && replacement.DeadScenarioCommanders.Count == 0, "outgoing replacement commander withdraws alive");
        var removed = Execute(reinforced, "remove", new ScenarioEffect { Type = "unit_remove", Faction = "winner", Unit = unit.Id, Disposition = "available" });
        Check(removed.Units.Count == 2 && removed.RetiredScenarioUnits.Contains(unit.Id), "peaceful removal keeps history");
        Check(removed.DestroyedScenarioUnits.Count == 0 && removed.DeadScenarioCommanders.Count == 0 && !removed.Commanders.Single().Withdrawn, "peaceful removal avoids death pipeline");
        Check(ScenarioRules.Match(removed, new() { Type = "unit_removed", Unit = unit.Id }), "removed condition");
        Reject(() => Execute(reinforced, "unspecified", new ScenarioEffect { Type = "unit_remove", Faction = "winner", Unit = unit.Id }), "scenario.army_disposition");
        var removedSave = WorldSave.Decode(WorldSave.Encode(removed), removed.ContentRevision);
        Reject(() => Execute(removedSave, "respawn", spawn with { Unit = "replacement", Commander = "clone.general" }), "scenario.army_commander");
        Reject(() => Execute(removedSave with { ScenarioEvents = [], TriggeredScenarioEvents = [] }, "reuse", spawn), "scenario.army_identity");
        var dead = Execute(reinforced, "kill", new ScenarioEffect { Type = "destroy_unit", Unit = unit.Id });
        Check(dead.DeadScenarioCommanders.Contains("new.general") && dead.DestroyedScenarioUnits.Contains(unit.Id), "destroy remains actual death");
        var plain = spawn with { Unit = "plain", Commander = null, CommanderTemplate = null, Cost = null, Health = null };
        var unitsOnly = basis with { Factions = basis.Factions.Select(f => f with { Survival = FactionSurvival.Legacy(1) }).ToArray() };
        var lastRemoved = Execute(unitsOnly, "last.remove", new ScenarioEffect { Type = "unit_remove", Faction = "winner", Unit = "unit.2" });
        Check(lastRemoved.Factions.Single(f => f.Id == "winner").Defeated && lastRemoved.DestroyedScenarioUnits.Count == 0, "last peaceful removal settles survival without army death");
        var armyReplacement = Execute(unitsOnly, "army.replace", new() { Type = "unit_remove", Faction = "winner", Unit = "unit.2" }, plain);
        Check(!armyReplacement.Factions.Single(f => f.Id == "winner").Defeated && armyReplacement.Units.Any(u => u.Id == "plain"), "remove and spawn settle survival after the whole batch");
        Check(Execute(basis, "plain", plain).Units.Single(u => u.Id == "plain").CommanderId == null, "uncommanded reinforcements supported");
        var foreign = Execute(basis, "foreign", plain with { Faction = "loser", Cells = [1] });
        Check(foreign.Units.Single(u => u.Id == "plain").Owner == "loser" && foreign.ActiveFaction == "winner", "cross-faction event does not switch active turn");
        var blocked = new ScriptedScenarioEvent { Id = "blocked", Effects = [new() { Type = "money", Faction = "winner", Value = -8 },
            plain with { Cells = [8] }] };
        var waiting = ScenarioRules.Evaluate(basis with { ScenarioEvents = [blocked] }).State;
        Check(waiting.TriggeredScenarioEvents.Count == 0 && waiting.Factions[1].Resources.Money == 100, "blocked transaction waits without charge or progress");
        var freed = waiting with { Units = waiting.Units.Select(u => u.Id == "unit.2" ? u with { Cell = 7 } : u).ToArray() };
        Check(ScenarioRules.Evaluate(freed).State.Units.Count == 3, "blocked event can retry after site clears");
        var popup = blocked with { Effects = [], Popup = new() { Choices = [new() { Id = "yes", Text = "Reinforce", Effects = blocked.Effects }] } };
        var pending = ScenarioRules.Evaluate(basis with { ScenarioEvents = [popup] }).State;
        var failedChoice = ScenarioRules.Choose(pending, pending.Revision, popup.Id, "yes").State;
        Check(failedChoice.PendingDialogue == pending.PendingDialogue && failedChoice.ScenarioChoices.Count == 0, "blocked popup choice remains pending");
        var shortage = new ScriptedScenarioEvent { Id = "cost", Effects = [spawn with { Cost = new(999, 0, 0) }],
            InsufficientResourcesEffects = [new() { Type = "state", Key = "scenario/cost", Value = 1 }] };
        var noFunds = ScenarioRules.Evaluate(basis with { ScenarioEvents = [shortage] }).State;
        Check(noFunds.Units.Count == 2 && noFunds.Commanders.Count == 0 && noFunds.SpawnedScenarioUnits.Count == 0
            && noFunds.ExtensionState["scenario/cost"] == 1, "failed cost removes entire pending spawn before fallback");
        var create = new ScenarioEffect { Type = "faction_create", Faction = "new.country", TemplateFaction = "winner", Name = "New country", Camp = "new" };
        var founded = Execute(basis, "found", create, new() { Type = "cells_owner", Faction = "new.country", Cells = [6] },
            plain with { Faction = "new.country", Cells = [6] });
        Check(founded.Factions.Count == 3 && founded.Units.Any(u => u.Owner == "new.country"), "create country and spawn in one transaction");
        var noCountry = ScenarioRules.Evaluate(basis with { ScenarioEvents = [new() { Id = "found.blocked", Effects = [create,
            plain with { Faction = "new.country", Cells = [8] }] }] }).State;
        Check(noCountry.Factions.Count == 2 && noCountry.TriggeredScenarioEvents.Count == 0, "blocked founding leaves no partial country");
        var follow = new ScriptedScenarioEvent { Id = "follow", Condition = new() { Type = "unit_alive", Unit = "plain" },
            Effects = [new() { Type = "state", Key = "scenario/follow", Value = 1 }] };
        var future = basis with { ScenarioEvents = [follow, new() { Id = "spawn", Effects = [plain] }] };
        ScenarioRules.Validate(future, true);
        Check(ScenarioRules.Evaluate(future).State.ExtensionState["scenario/follow"] == 1, "future army reference chains after spawn");
        var victory = new ExtensionVictoryRules { Win = new() { Type = "units_destroyed", Units = ["plain"] } };
        ExtensionVictory.ValidateTargets(victory, future, true);
        Check(ExtensionVictory.Evaluate(future, "b", [], victory, null) == BattleOutcome.Continue, "future army does not count as destroyed before arrival");
        var futureSpawned = ScenarioRules.Evaluate(future).State;
        Check(ExtensionVictory.Evaluate(futureSpawned, "b", [], victory, null) == BattleOutcome.Continue, "arrived living army does not satisfy loss objective");
        var futureLost = Execute(futureSpawned, "future.kill", new ScenarioEffect { Type = "destroy_unit", Unit = "plain" });
        Check(ExtensionVictory.Evaluate(futureLost, "b", [], victory, null) == BattleOutcome.Victory
            && ExtensionVictory.Evaluate(futureLost, "b", [], victory with { Win = victory.Win with { Type = "units_lost" } }, null) == BattleOutcome.Victory,
            "loss objectives recognize event armies after actual arrival");
        var repeat = new ScriptedScenarioEvent { Id = "repeat", Repeat = new() { MaxOccurrences = 2 }, Effects = [plain with { Unit = "wave.{occurrence}", Cells = [6, 7] }] };
        var wave1 = ScenarioRules.Evaluate(basis with { ScenarioEvents = [repeat] }).State;
        var wave2 = ScenarioRules.Evaluate(wave1 with { Round = 2 }).State;
        Check(wave2.Units.Any(u => u.Id == "wave.1") && wave2.Units.Any(u => u.Id == "wave.2"), "periodic reinforcements receive distinct stable IDs");
        Check(ScenarioArmies.Declared("wave.{occurrence}", "wave.12") && !ScenarioArmies.Declared("wave.{occurrence}", "wave.other"), "future repeated references are bounded patterns");
        var story = new StoryState { Library = new() { Scenes = new Dictionary<string, StoryScene> { ["army.scene"] = new("army.scene", [
            new() { Id = "choose", Choices = [new() { Id = "mobilize", Text = "Mobilize", Next = "done", Effects = [plain] }] },
            new() { Id = "done", End = true }]) } } };
        var storyWorld = basis with { Story = story, ScenarioEvents = [follow, new() { Id = "story.call", Effects = [new() { Type = "play_story", Key = "army.call", Scene = "army.scene" }] }] };
        ScenarioRules.Validate(storyWorld, true);
        storyWorld = ScenarioRules.Evaluate(storyWorld).State;
        var run = StoryPlayback.Current(storyWorld)!;
        var storyBlocked = storyWorld with { Units = storyWorld.Units.Append(storyWorld.Units[1] with { Id = "blocker", Cell = 7 }).ToArray() };
        Reject(() => StoryPlayback.Advance(storyBlocked, run.Id, "choose", "mobilize", false), "scenario.army_blocked");
        Check(StoryPlayback.Current(storyBlocked)!.Choices.Count == 0 && storyBlocked.SpawnedScenarioUnits.Count == 0, "blocked story choice has no partial commitment");
        storyWorld = StoryPlayback.Advance(storyWorld, run.Id, "choose", "mobilize", false).State;
        Check(storyWorld.Units.Count == basis.Units.Count && StoryPlayback.Current(storyWorld)!.PendingEffects.Count == 1, "story defers reinforcement until completion");
        storyWorld = WorldSave.Decode(WorldSave.Encode(storyWorld), storyWorld.ContentRevision);
        storyWorld = StoryPlayback.Advance(storyWorld, run.Id, "done", null, true).State;
        Check(ScenarioRules.Evaluate(storyWorld).State.ExtensionState["scenario/follow"] == 1, "saved story choice spawns a future referenced army");
        var removeBeforeStory = Execute(unitsOnly with { Story = story }, "story.remove",
            new() { Type = "unit_remove", Faction = "winner", Unit = "unit.2" }, new() { Type = "play_story", Key = "after.remove", Scene = "army.scene" });
        Check(removeBeforeStory.Factions.Single(f => f.Id == "winner").Defeated, "story boundary settles prior removals");
        Reject(() => Execute(basis, "health", plain with { Health = 1000 }), "scenario.army_health");
        var water = basis with { Cells = basis.Cells.Select((c, i) => i == 6 ? c with { Owner = null, Terrain = c.Terrain with { Sea = true } } : c).ToArray() };
        var naval = plain with { UnitTemplate = new(actor with { Profile = actor.Profile with { Category = "naval" } }, new()), Cells = [6] };
        Check(Execute(water, "naval", naval).Units.Single(u => u.Id == "plain").Combatant.AtSea, "naval reinforcement on unowned sea");
        Check(ScenarioRules.Evaluate(water with { ScenarioEvents = [new() { Id = "land.sea", Effects = [plain with { Cells = [6] }] }] }).State.Units.Count == 2, "land reinforcement rejects sea");
        var full = basis with { Limits = new() { UnitsPerFaction = 1 } };
        Check(ScenarioRules.Evaluate(full with { ScenarioEvents = [new() { Id = "capacity", Effects = [plain] }] }).State.Units.Count == 2, "capacity blocks reinforcements");
        Reject(() => ScenarioRules.ValidateDefinitions([new() { Id = "bad", Effects = [plain with { Cost = new(-1, 0, 0) }] }]), "resources.negative");
        checks += ClientChecks(root);
        return checks;
    }

    private static int ClientChecks(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Scenario army client: " + label); }
        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string path) => overrides.TryGetValue(path, out var value) ? value : File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var baseline = new ClientContent(Read, ["conquest1.btl"]);
        var initial = baseline.Start("conquest1", "fra");
        var world = initial.Session.World;
        var receiver = world.Units.First(u => u.Owner == initial.Session.PlayerFaction && u.CommanderId == null
            && world.Cells[u.Cell].Owner == u.Owner && u.Combatant.Profile.Category == "infantry");
        var original = world.Units.First(u => u.Owner == receiver.Owner && u.CommanderId != null);
        int originalDefinition = baseline.Battle("conquest1").Armies[int.Parse(original.Id.Split('.').Last())].Commander;
        int definition = baseline.Catalogs.Runtime.Commanders.Keys.First(id => !baseline.Battle("conquest1").Armies.Any(a => a.Commander == id));
        var ev = new ScriptedScenarioEvent { Id = "event.armies", Popup = new() { Choices = [new() { Id = "go", Text = "Mobilize", Effects = [
            new() { Type = "commander_assign", Unit = receiver.Id, Commander = "event.general", CommanderDefinition = definition },
            new() { Type = "unit_spawn", Unit = "event.army", Profile = receiver.Combatant.Profile.Id,
                Cells = world.Cells.Select((c, i) => (c, i)).Where(p => p.c.Owner == receiver.Owner && !p.c.Terrain.Sea && p.c.Terrain.CanTraverse()
                    && world.Units.All(u => u.Cell != p.i)).Take(20).Select(p => p.i).ToArray() },
            new() { Type = "commander_detach", Unit = original.Id, Commander = original.CommanderId, Disposition = "available" },
            new() { Type = "commander_assign", Unit = "event.army", Commander = original.CommanderId }
        ] }, new() { Id = "clone", Text = "Clone", Effects = [new() { Type = "commander_assign", Unit = receiver.Id,
            Commander = "clone.general", CommanderDefinition = originalDefinition }] }] } };
        overrides["rewrite/extension.json"] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, apiVersion = 1, scenarioEvents = "rewrite/army-events.json" }, ExtensionLoader.Json);
        overrides["rewrite/army-events.json"] = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [ev] }, ExtensionLoader.Json);
        var content = new ClientContent(Read, ["conquest1.btl"]);
        string folder = Path.Combine(root, "build/scenario-armies-client-20261031");
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object body)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), route + " " + response.RootElement.GetRawText()[..Math.Min(250, response.RootElement.GetRawText().Length)]);
            return response.RootElement.GetProperty("data").Clone();
        }
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        using (var clone = JsonDocument.Parse(host.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "event_choice", offer = ev.Id, target = "clone", revision = view.GetProperty("revision").GetInt64() }))))
            Check(!clone.RootElement.GetProperty("ok").GetBoolean(), "initial scenario person cannot be cloned under a new runtime ID");
        view = Call("action", new { op = "event_choice", offer = ev.Id, target = "go", revision = view.GetProperty("revision").GetInt64() });
        JsonElement General(JsonElement v) => v.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == receiver.Id);
        Check(General(view).GetProperty("commanderDefinitionId").GetString() == baseline.Catalogs.Runtime.Commanders[definition].Definition.Id, "dynamic portrait definition projected");
        Check(General(view).GetProperty("commanderImage").GetString() == baseline.Catalogs.Runtime.Commanders[definition].Definition.DisplayName, "dynamic portrait display name projected");
        var info = Call("unit-info", new { unit = receiver.Id, revision = view.GetProperty("revision").GetInt64() });
        Check(info.GetProperty("commander").GetProperty("definitionId").GetString() == General(view).GetProperty("commanderDefinitionId").GetString(), "unit info resolves dynamic commander");
        Check(view.GetProperty("units").EnumerateArray().Any(u => u.GetProperty("id").GetString() == "event.army"), "client renders new army");
        Check(view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == "event.army").GetProperty("commanderId").GetString() == original.CommanderId,
            "original scenario commander detaches and reassigns with canonical identity");
        Call("save", new { mode = 2, slot = 1 });
        view = Call("load", new { mode = 2, slot = 1 });
        Check(General(view).GetProperty("commanderId").GetString() == "event.general", "load preserves dynamic commander");
        Check(view.GetProperty("units").EnumerateArray().Count(u => u.GetProperty("id").GetString() == "event.army") == 1, "load does not duplicate reinforcement");
        Call("unit-info", new { unit = receiver.Id, revision = view.GetProperty("revision").GetInt64() });
        Call("unit-info", new { unit = "event.army", revision = view.GetProperty("revision").GetInt64() });
        var direct = new ScriptedScenarioEvent { Id = "direct.army", Effects = ev.Popup!.Choices[0].Effects };
        overrides["rewrite/army-events.json"] = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [direct] }, ExtensionLoader.Json);
        overrides["rewrite/army-capacity.json"] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, baseCapacity = 0, buildings = new { } });
        var limited = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra").Session.World;
        Check(!limited.TriggeredScenarioEvents.Contains(direct.Id) && limited.Units.Count == world.Units.Count
            && limited.Commanders.All(c => c.Id != "event.general"), "opening direct reinforcements obey configured country capacity atomically");
        overrides.Remove("rewrite/army-capacity.json");
        overrides["rewrite/army-events.json"] = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, ScriptedScenarioEvent[]> {
            ["conquest1"] = [direct with { Condition = new() { Type = "round", Value = 2 } }] }, ExtensionLoader.Json);
        overrides["rewrite/extension.json"] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, apiVersion = 1,
            scenarioEvents = "rewrite/army-events.json", victoryRules = "rewrite/army-victory.json" }, ExtensionLoader.Json);
        overrides["rewrite/army-victory.json"] = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, ExtensionVictoryRules> {
            ["conquest1"] = new() { Win = new() { Type = "units_destroyed", Units = ["event.army"] } } }, ExtensionLoader.Json);
        var futureStart = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra").Session.World;
        Check(futureStart.Outcome == BattleOutcome.Continue && futureStart.Units.All(u => u.Id != "event.army"), "client startup accepts future victory target without premature victory");
        overrides.Clear();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "rewrite/mod-kit/examples/scenario-armies/rewrite"), "*.json"))
            overrides["rewrite/" + Path.GetFileName(file)] = File.ReadAllBytes(file);
        var example = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra");
        foreach (string choice in new[] { "reinforce", "found", "cancel" })
        {
            var chosen = ScenarioRules.Choose(example.Session.World, example.Session.World.Revision, "army.example", choice).State;
            Check(chosen.PendingDialogue == null, "authored example choice succeeds: " + choice);
            Check(chosen.Units.Count == example.Session.World.Units.Count + (choice == "cancel" ? 0 : 1), "authored example adds exactly one army or cancels");
            Check(chosen.Factions.Count == example.Session.World.Factions.Count + (choice == "found" ? 1 : 0), "authored example creates country only on request");
        }
        return checks;
    }
}
