using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ScenarioControlChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Scenario control: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + " got " + e.Code); return; }
            throw new Exception("Scenario control accepted " + code);
        }
        var basis = WorldChecks.Example() with { ScenarioPlayerFaction = "winner" };
        var delayedWin = new ExtensionVictoryRules { Win = new() { Type = "round_reached", Round = 50 }, RoundLimit = 60 };
        var control = new ScenarioEffect { Type = "player_control", Faction = "loser" };
        var ruleEffect = new ScenarioEffect { Type = "victory_rules", VictoryRules = delayedWin };
        var change = new ScriptedScenarioEvent { Id = "switch", Effects = [control, ruleEffect,
            new() { Type = "money", Value = 7 }] };
        var switched = ScenarioRules.Evaluate(basis with { ScenarioEvents = [change] }).State;
        Check(switched.ScenarioPlayerFaction == "loser" && switched.Factions.Count(f => f.Human) == 1 && switched.Factions[0].Human,
            "one human controls target; former country becomes AI");
        Check(switched.ActiveFaction == basis.ActiveFaction && switched.Units.SequenceEqual(basis.Units), "switch does not jump turns or reset army budgets");
        Check(switched.Factions[0].Resources.Money == 7 && switched.Factions[1].Resources.Money == 0, "subsequent player selector resolves new controlled country");
        Check(ScenarioRules.Match(switched, new() { Type = "player_faction", Faction = "loser" }), "role condition sees current country");
        var baseRules = new VictoryRules(1, true);
        Check(WorldVictory.Evaluate(switched with { Round = 2 }, "b", baseRules, []) == BattleOutcome.Continue, "runtime rules replace base round limit and objectives");
        Check(WorldVictory.Evaluate(switched with { Round = 50 }, "b", baseRules, []) == BattleOutcome.Victory, "new victory condition becomes true");
        Check(WorldVictory.Evaluate(switched with { Round = 61 }, "b", baseRules, []) == BattleOutcome.Defeat, "runtime round limit enforced");
        var saved = WorldSave.Decode(WorldSave.Encode(switched), switched.ContentRevision);
        Check(saved.ScenarioPlayerFaction == "loser" && saved.ScenarioVictory!.RoundLimit == 60, "role and rules survive save");
        var restored = ScenarioRules.Evaluate(switched with { ScenarioEvents = [change, new() { Id = "default", Effects = [new() { Type = "victory_default" }] }] }).State;
        Check(restored.ScenarioVictory == null && WorldVictory.Evaluate(restored with { Round = 2 }, "b", baseRules, []) == BattleOutcome.Defeat,
            "reset restores installed rules");
        var rollback = ScenarioRules.Evaluate(basis with { ScenarioEvents = [change with {
            Effects = [control, ruleEffect, new() { Type = "money", Value = -1 }],
            InsufficientResourcesEffects = [new() { Type = "state", Key = "scenario/shortage", Value = 1 }] }] }).State;
        Check(rollback.ScenarioPlayerFaction == "winner" && rollback.ScenarioVictory == null && rollback.Factions[1].Human,
            "resource fallback rolls back role and objective changes");
        foreach (string outcome in new[] { "victory", "defeat" })
        {
            var ending = ScenarioRules.Evaluate(basis with { ScenarioEvents = [new() { Id = "end", Effects = [control,
                new() { Type = "ending", Key = "rebels." + outcome, Outcome = outcome }] }] }).State;
            Check(ending.Phase == TurnPhase.Finished && ending.Outcome.ToString().Equals(outcome, StringComparison.OrdinalIgnoreCase)
                && ending.ScenarioEnding == "rebels." + outcome, "explicit ending belongs to current controlled country");
            Check(WorldSave.Decode(WorldSave.Encode(ending), ending.ContentRevision).ScenarioEnding == ending.ScenarioEnding, "ending branch persists");
        }
        Reject(() => ScenarioRules.Evaluate(basis with { Factions = basis.Factions.Select(f => f.Id == "loser" ? f with { Defeated = true } : f).ToArray(),
            ScenarioEvents = [change] }), "scenario.player_control");
        Reject(() => ScenarioRules.ValidateDefinitions([new() { Id = "bad", Effects = [new() { Type = "ending", Key = "x", Outcome = "continue" }] }]), "scenario.effect");
        Reject(() => ScenarioRules.ValidateDefinitions([new() { Id = "bad", Effects = [ruleEffect with { VictoryRules = delayedWin with { Win = new() { Type = "unknown" } } }] }]), "extension.victory");
        Reject(() => ScenarioRules.Validate(basis with { ScenarioEvents = [new() { Id = "bad", Effects = [ruleEffect with {
            VictoryRules = delayedWin with { Win = new() { Type = "cells_owned", Cells = [999] } } }] }] }, true), "extension.victory_reference");
        var upcoming = new ScenarioEffect { Type = "faction_create", Faction = "rebels", Name = "叛军", Camp = "rebels", TemplateFaction = "winner" };
        var filteredLand = ScenarioRules.Evaluate(basis with { ScenarioEvents = [new() { Id = "filtered.land", Effects = [
            new() { Type = "cells_owner", Faction = "winner", ExpectedOwner = "loser", Cells = [0, 1, 8] }] }] }).State;
        Check(filteredLand.Cells[0].Owner == "loser" && filteredLand.Cells[1].Owner == "winner" && filteredLand.Cells[8].Owner == "winner",
            "source-filtered land transfer preserves hostile occupied land and third-party ownership");
        var opening = new ScriptedScenarioEvent { Id = "opening.handoff", Effects = [upcoming,
            new() { Type = "unit_owner", Unit = "unit.1", Faction = "rebels" },
            new() { Type = "cells_owner", Cells = [0], Faction = "rebels" },
            new() { Type = "player_control", Faction = "rebels", OpeningTurn = true }] };
        var openingWorld = ScenarioRules.Evaluate(basis with { ScenarioEvents = [opening] }).State;
        Check(openingWorld.ActiveFaction == "winner" && openingWorld.ScenarioPlayerFaction == "rebels", "opening handoff waits for pending presentation");
        openingWorld = WorldSave.Decode(WorldSave.Encode(openingWorld), openingWorld.ContentRevision);
        var turnRules = new WorldTurnRules();
        var handed = WorldTurns.CompleteOpeningHandoff(openingWorld, turnRules).State;
        Check(handed.ActiveFaction == "rebels" && handed.Round == 1 && handed.Phase == TurnPhase.Active, "opening handoff starts the new army without intervening AI turns");
        Check(handed.Units.Single(u => u.Owner == "winner").Acted && !handed.Units.Single(u => u.Owner == "rebels").Acted,
            "outgoing army settles once and incoming army receives its first movement budget");
        Check(ReferenceEquals(handed, WorldTurns.CompleteOpeningHandoff(handed, turnRules).State), "opening handoff is consumed exactly once");
        var laterOpening = ScenarioRules.Evaluate(basis with { Round = 4, ScenarioEvents = [opening] }).State;
        laterOpening = WorldSave.Decode(WorldSave.Encode(laterOpening), laterOpening.ContentRevision);
        var laterHanded = WorldTurns.CompleteOpeningHandoff(laterOpening, turnRules).State;
        Check(laterHanded.ActiveFaction == "rebels" && laterHanded.Round == 4, "new provincial army can receive its first turn after a later response");
        Reject(() => ScenarioRules.Evaluate(basis with { ScenarioEvents = [opening with { Effects = opening.Effects.Select(e => e.Type == "player_control"
            ? e with { Faction = "loser" } : e).ToArray() }] }), "scenario.opening_turn");
        Reject(() => ScenarioRules.ValidateDefinitions([new() { Id = "bad", Effects = [new() { Type = "money", OpeningTurn = true }] }]), "scenario.effect");
        var reserved = basis with { ScenarioEvents = [new() { Id = "objectives", Effects = [ruleEffect with {
            VictoryRules = delayedWin with { Win = new() { Type = "factions_defeated", Factions = ["rebels"] } } }] },
            new() { Id = "later", Condition = new() { Type = "round", Value = 9 }, Effects = [upcoming] }] };
        ScenarioRules.Validate(reserved, true);
        var waiting = ScenarioRules.Evaluate(reserved).State;
        Check(WorldVictory.Evaluate(waiting, "b", baseRules, []) == BattleOutcome.Continue, "not-yet-created target cannot count as defeated");
        var independent = upcoming with { Faction = "free", TemplateFaction = null, Flag = "rus", Color = "#237A4D" };
        Reject(() => ScenarioRules.Evaluate(basis with { ScenarioEvents = [new() { Id = "order", Effects = [new() { Type = "player_control", Faction = "free" }, independent] }] }), "scenario.faction_missing");

        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string path) => overrides.TryGetValue(path, out var value) ? value : File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        void Put(string path, object value) => overrides[path] = JsonSerializer.SerializeToUtf8Bytes(value, ExtensionLoader.Json);
        Put("rewrite/extension.json", new { schema = 1, apiVersion = 1, scenarioEvents = "rewrite/events.json" });
        Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]>());
        var content = new ClientContent(Read, ["conquest1.btl"]);
        var original = content.Start("conquest1", "fra");
        string origin = original.Session.PlayerFaction;
        string folder = Path.Combine(root, "rewrite/reports/scenario-control-20261021"); Directory.CreateDirectory(folder);
        // Both origins use the same event results: a domestic split or a revolt in a foreign country.
        foreach (bool domestic in new[] { true, false })
        {
            string victim = domestic ? origin : original.Session.World.Factions.First(f => f.Id != origin && f.Camp != original.Session.World.Factions.Single(p => p.Id == origin).Camp).Id;
            var army = original.Session.World.Units.First(u => u.Owner == victim && original.Session.World.Cells[u.Cell].BuildingKind != null);
            var creation = domestic ? upcoming with { Faction = "free", TemplateFaction = "@player" } : independent;
            var popup = new ScriptedScenarioEvent { Id = "rebellion", Effects = [creation,
                new() { Type = "unit_owner", Faction = "free", Unit = army.Id }, new() { Type = "cells_owner", Faction = "free", Cells = [army.Cell] }, ruleEffect],
                Popup = new() { Title = "选择立场", Text = "是否转为控制新成立的叛军？", Choices = [
                    new() { Id = "join", Text = "扮演叛军", Effects = [new() { Type = "player_control", Faction = "free" }, new() { Type = "money", Value = 1000 }] },
                    new() { Id = "stay", Text = "继续扮演原国家" }] } };
            var finish = new ScriptedScenarioEvent { Id = "finish", Condition = new() { Type = "state", Key = "scenario/finish" },
                Effects = [new() { Type = "ending", Key = "rebels.independent", Outcome = "victory" }] };
            Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [popup, finish] });
            content = new ClientContent(Read, ["conquest1.btl"]);
            string saveDir = Path.Combine(folder, "client-" + Guid.NewGuid().ToString("N"));
            var host = new ClientGame(content, saveDir);
            JsonElement Call(string route, object body)
            {
                using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
                Check(doc.RootElement.GetProperty("ok").GetBoolean(), route + ": " + doc.RootElement.GetRawText()[..Math.Min(220, doc.RootElement.GetRawText().Length)]);
                return doc.RootElement.GetProperty("data").Clone();
            }
            var view = Call("start", new { scenario = "conquest1", country = "fra" });
            Check(view.GetProperty("playerFaction").GetString() == origin, "pending popup preserves control");
            view = Call("action", new { op = "event_choice", offer = "rebellion", target = "stay", revision = view.GetProperty("revision").GetInt64() });
            Check(view.GetProperty("playerFaction").GetString() == origin && view.GetProperty("factions").EnumerateArray().Any(f => f.GetProperty("id").GetString() == "free"),
                "declining control still permits independent rebellion");
            view = Call("start", new { scenario = "conquest1", country = "fra" });
            Call("save", new { mode = 2, slot = 1 }); view = Call("load", new { mode = 2, slot = 1 });
            view = Call("action", new { op = "event_choice", offer = "rebellion", target = "join", revision = view.GetProperty("revision").GetInt64() });
            Check(view.GetProperty("playerFaction").GetString() == "free" && view.GetProperty("factions").EnumerateArray().Count(f => f.GetProperty("human").GetBoolean()) == 1,
                "actual client switches single player role");
            Check(view.GetProperty("startCountry").GetString() == "fra" && view.GetProperty("outcome").GetString() == "Continue", "start country preserved; replacement objectives prevent premature result");
            Check(view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == "free").GetProperty("resources").GetProperty("money").GetInt64() == 1000,
                "new country receives subsequent player resource effects");
            Call("save", new { mode = 2, slot = 1 }); view = Call("load", new { mode = 2, slot = 1 });
            Check(view.GetProperty("playerFaction").GetString() == "free" && view.GetProperty("victoryRules").GetProperty("roundLimit").GetInt32() == 60, "session load retains role and objectives");
            var configured = content.Start("conquest1", "fra");
            var chosen = ScenarioRules.Choose(configured.Session.World, configured.Session.World.Revision, "rebellion", "join").State;
            var session = SessionActions.CommitWorld(configured.Session, configured.Session.Revision, new(chosen with { Revision = configured.Session.World.Revision + 1 }, [])).State;
            Check(session.PlayerFaction == "free" && session.RosterDeployments.Any(q => q.Faction == "free") && session.RosterDeployments.Any(q => q.Faction == origin),
                "session permissions and deployment history follow control");
            var loaded = SessionSave.Decode(SessionSave.Encode(session), new(session.ContentRevision, session.World.ContentRevision, session.Account.ContentRevision));
            Check(loaded.PlayerFaction == "free", "standalone session save binds new role");
            var endWorld = chosen with { ExtensionState = new Dictionary<string, long> { ["scenario/finish"] = 1 } };
            var ended = ScenarioRules.Evaluate(endWorld).State;
            Check(ended.ScenarioEnding == "rebels.independent" && ended.Outcome == BattleOutcome.Victory, "branch ending follows role change");
            var turns = configured.Rules.Turns;
            var turn = chosen;
            for (int i = 0; i < turn.Factions.Count + 1 && turn.ActiveFaction != "free"; i++)
                turn = WorldTurns.EndTurn(turn, turn.Revision, turns).State;
            Check(turn.ActiveFaction == "free" && turn.Factions.Single(f => f.Id == "free").Human && turn.Outcome == BattleOutcome.Continue,
                "normal turn handoff reaches controlled rebellion");
            if (!domestic)
            {
                string fixture = Path.Combine(folder, "raw-fixture"); Directory.CreateDirectory(fixture);
                foreach (var p in overrides) { string target = Path.Combine(fixture, p.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, p.Value); }
            }
        }
        // Lua victory handlers nested in event results are loaded and validated at import.
        overrides["rewrite/later.lua"] = System.Text.Encoding.UTF8.GetBytes("function win(ctx) return ctx.round >= 50 and 1 or 0 end");
        Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [new() { Id = "scripted", Effects = [ruleEffect with {
            VictoryRules = delayedWin with { Win = new() { Type = "script", Script = "rewrite/later.lua", Function = "win" } } }] }] });
        var scripted = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra");
        Check(scripted.Session.World.ScenarioVictory!.Win.Type == "script", "nested Lua victory rule loads without separate scripts list");
        Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [new() { Id = "direct", Effects = [
            independent, new() { Type = "unit_owner", Faction = "free", Unit = "conquest1.unit.10" },
            new() { Type = "cells_owner", Faction = "free", Cells = [493] }, new() { Type = "player_control", Faction = "free" }, ruleEffect] }] });
        var direct = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra").Session;
        direct.Validate();
        Check(direct.PlayerFaction == "free" && direct.World.PendingDialogue == null, "direct event switches startup session without popup");
        var exampleFiles = Directory.EnumerateFiles(Path.Combine(root, "rewrite/mod-kit/examples/scenario-control/rewrite"), "*.json");
        foreach (string file in exampleFiles) overrides["rewrite/" + Path.GetFileName(file)] = File.ReadAllBytes(file);
        var example = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra");
        var exampleJoined = ScenarioRules.Choose(example.Session.World, example.Session.World.Revision, "rebellion.choice", "join").State;
        Check(exampleJoined.ScenarioPlayerFaction == "civil.rebels" && exampleJoined.ScenarioVictory!.Win.Round == 5, "authored example starts and changes objectives");
        var fifthRound = ScenarioRules.Evaluate(exampleJoined with { Round = 5 }).State;
        Check(ScenarioRules.Pending(fifthRound)?.Id == "republic.ending", "example ending popup runs before automatic victory check");
        var concluded = ScenarioRules.Choose(fifthRound, fifthRound.Revision, "republic.ending", "finish").State;
        Check(concluded.ScenarioEnding == "republic.independence" && concluded.Outcome == BattleOutcome.Victory, "authored example submits branch ending");
        File.WriteAllText(Path.Combine(folder, "core.json"), JsonSerializer.Serialize(new { checks, errors = 0 }));
        return checks;
    }
}
