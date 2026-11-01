using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ScenarioRuleChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Scenario rules: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + " got " + e.Code); return; }
            throw new Exception("Scenario rules accepted " + code);
        }
        var basis = WorldChecks.Example() with { ScenarioPlayerFaction = "winner",
            Factions = WorldChecks.Example().Factions.Select(f => f with { Resources = new(100, 20, 30) }).ToArray(),
            Cells = WorldChecks.Example().Cells.Select((c, i) => c with { BuildingKind = i == 8 || i == 0 ? "city" : null }).ToArray(),
            Commanders = [new() { Id = "general", Owner = "loser" }],
            Units = WorldChecks.Example().Units.Select(u => u.Id == "unit.1" ? u with { CommanderId = "general" } : u).ToArray() };
        basis.Validate();
        var numeric = new Dictionary<string, long> { ["money"] = 100, ["industry"] = 20, ["food"] = 30, ["round"] = 1,
            ["city_count"] = 1, ["objective_count"] = 0, ["territory_count"] = 4, ["unit_count"] = 1,
            ["kill_count"] = 0, ["unit_health"] = 100, ["building_level"] = 0, ["state"] = 0 };
        foreach (var p in numeric)
        {
            var condition = new ScenarioCondition { Type = p.Key, Compare = "eq", Value = p.Value, Unit = "unit.1", Cells = [8], Key = "scenario/test" };
            Check(ScenarioRules.Match(basis, condition), p.Key + " equality");
            Check(!ScenarioRules.Match(basis, condition with { Compare = "gt" }), p.Key + " strict boundary");
            Check(ScenarioRules.Match(basis, condition with { Compare = "le" }), p.Key + " upper boundary");
        }
        Check(ScenarioRules.Match(basis, new() { Type = "cells_owned", Cells = [8] }), "specific city owner");
        Check(!ScenarioRules.Match(basis, new() { Type = "cells_owned", Cells = [0] }), "enemy city not owned");
        Check(ScenarioRules.Match(basis, new() { Type = "unit_count", Camp = "a", Value = 1, Profile = "fixture.infantry" }), "camp and profile filters");
        Check(ScenarioRules.Match(basis, new() { Type = "unit_at", Unit = "unit.1", Cells = [0] }), "unit location");
        Check(!ScenarioRules.Match(basis, new() { Type = "commander_dead", Commander = "general" }), "living general is not dead");
        Check(!ScenarioRules.Match(basis with { Commanders = [basis.Commanders[0] with { Withdrawn = true }], Units = basis.Units.Skip(1).ToArray() },
            new() { Type = "commander_dead", Commander = "general" }), "withdrawal is not death");
        var conditionA = new ScenarioCondition { Type = "all", Conditions = [new() { Type = "money", Value = 100 },
            new() { Type = "any", Conditions = [new() { Type = "food", Value = 999 }, new() { Type = "industry", Value = 20 }] },
            new() { Type = "not", Conditions = [new() { Type = "faction_defeated" }] }] };
        var direct = new ScriptedScenarioEvent { Id = "direct", Condition = conditionA, Effects = [new() { Type = "money", Value = -10 },
            new() { Type = "industry", Value = 3 }, new() { Type = "food", Value = 4 }, new() { Type = "state", Key = "scenario/direct", Value = 1 }] };
        var world = basis with { ScenarioEvents = [direct] };
        ScenarioRules.Validate(world, true);
        var executed = ScenarioRules.Evaluate(world).State;
        Check(executed.Factions.Single(f => f.Id == "winner").Resources == new Resources(90, 23, 34), "composite condition applies exact resource changes");
        Check(world.Factions.Single(f => f.Id == "winner").Resources.Money == 100 && world.TriggeredScenarioEvents.Count == 0, "input snapshot unchanged");
        Check(ScenarioRules.Evaluate(executed).Notices.Count == 0, "events fire once");
        var chain = new ScriptedScenarioEvent { Id = "chain", Condition = new() { Type = "event_completed", Key = "direct" },
            Effects = [new() { Type = "camp", Faction = "loser", Camp = "b" }] };
        var chained = ScenarioRules.Evaluate(basis with { ScenarioEvents = [chain, direct] }).State;
        Check(chained.TriggeredScenarioEvents.Count == 2 && chained.Factions[0].Camp == "b", "reverse declaration chains and camp conversion");
        Check(ScenarioRules.Match(chained, new() { Type = "faction_camp", Faction = "loser", Camp = "b" }), "camp condition sees conversion");
        var popup = new ScriptedScenarioEvent { Id = "choice", Condition = new() { Type = "round", Value = 1 },
            Popup = new() { Title = "出征军议", Text = "请决定本次出征的补给方案。", Image = "rewrite/images/council.png", ImageAlt = "征服地图", Choices = Enumerable.Range(0, 12).Select(i => new ScenarioEventChoice {
                Id = "option." + i, Text = i == 10 ? "长期补给与沿途驻军调配方案" : "补给方案 " + i, Condition = i == 11 ? new() { Type = "money", Value = 999 } : null,
                Effects = [new() { Type = "money", Value = i + 1 }] }).ToArray() } };
        var paused = ScenarioRules.Evaluate(basis with { ScenarioEvents = [popup] }).State;
        Check(paused.PendingDialogue == ScenarioRules.DialogueId(popup.Id) && paused.Factions[1].Resources.Money == 100, "popup waits without executing results");
        Reject(() => WorldEvents.Acknowledge(paused, paused.PendingDialogue!), "scenario.choice_required");
        Reject(() => ScenarioRules.Choose(paused, paused.Revision + 1, "choice", "option.0"), "action.stale");
        Reject(() => ScenarioRules.Choose(paused, paused.Revision, "choice", "option.11"), "scenario.choice");
        Reject(() => ScenarioRules.Choose(paused, paused.Revision, "other", "option.0"), "scenario.choice");
        var loaded = WorldSave.Decode(WorldSave.Encode(paused), paused.ContentRevision);
        Check(loaded.ScenarioEvents[0].Popup!.Choices.Count == 12 && loaded.PendingDialogue == paused.PendingDialogue, "all buttons and pending popup survive save");
        Check(loaded.ScenarioEvents[0].Popup!.Image == popup.Popup.Image && loaded.ScenarioEvents[0].Popup!.ImageAlt == "征服地图", "popup image and caption survive save");
        Check(ExtensionLoader.ParseJson<ScenarioPopup>(Encoding.UTF8.GetBytes("{\"text\":\"legacy\"}")).Image == null, "old text popups remain compatible");
        var chosen = ScenarioRules.Choose(loaded, loaded.Revision, "choice", "option.4").State;
        Check(chosen.PendingDialogue == null && chosen.Factions[1].Resources.Money == 105 && chosen.ScenarioChoices["choice"] == "option.4", "selected result alone applies");
        Reject(() => ScenarioRules.Choose(chosen, chosen.Revision, "choice", "option.4"), "scenario.choice");
        Check(ScenarioRules.Evaluate(WorldSave.Decode(WorldSave.Encode(chosen), chosen.ContentRevision)).Notices.Count == 0, "resolved event does not replay after load");
        var death = new ScriptedScenarioEvent { Id = "death", Effects = [new() { Type = "destroy_unit", Unit = "unit.1" }] };
        var deathFollowup = direct with { Id = "death.followup", Condition = new() { Type = "all", Conditions = [
            new() { Type = "unit_destroyed", Unit = "unit.1" }, new() { Type = "commander_dead", Commander = "general" }] } };
        var dead = ScenarioRules.Evaluate(basis with { ScenarioEvents = [deathFollowup, death] }).State;
        Check(dead.DeadScenarioCommanders.Contains("general") && dead.DestroyedScenarioUnits.Contains("unit.1") && dead.TriggeredScenarioEvents.Count == 2, "event death uses death pipeline and triggers followup");
        Check(WorldSave.Decode(WorldSave.Encode(dead), dead.ContentRevision).DeadScenarioCommanders.Contains("general"), "death history persists");
        var failed = direct with { Effects = [new() { Type = "money", Value = -101 }] };
        Reject(() => ScenarioRules.Evaluate(basis with { ScenarioEvents = [failed] }), "scenario.resources");
        Check(basis.Factions[1].Resources.Money == 100, "failed transaction does not debit input");
        Reject(() => ScenarioRules.ValidateDefinitions([direct with { Condition = new() { Type = "unknown" } }]), "scenario.condition");
        Reject(() => ScenarioRules.ValidateDefinitions([popup with { Popup = popup.Popup! with { Choices = [] } }]), "scenario.definition");
        Reject(() => ScenarioRules.ValidateDefinitions([popup with { Popup = popup.Popup! with { Image = "rewrite/../secret.png" } }]), "scenario.definition");
        Reject(() => ScenarioRules.ValidateDefinitions([popup with { Popup = popup.Popup! with { Image = "https://example.com/picture.png" } }]), "scenario.definition");
        Reject(() => ScenarioRules.ValidateDefinitions([popup with { Popup = popup.Popup! with { Image = "rewrite/images/picture.svg" } }]), "scenario.definition");
        Reject(() => ScenarioRules.ValidateDefinitions([direct with { Effects = [new() { Type = "state", Key = "unit/skill/ready" }] }]), "scenario.effect");
        Reject(() => ScenarioRules.Validate(basis with { ScenarioEvents = [direct with { Condition = new() { Type = "cells_owned", Cells = [9999] } }] }, true), "scenario.reference");
        var copy = paused.Copy();
        ((ScenarioEventChoice[])copy.ScenarioEvents[0].Popup!.Choices)[0] = new() { Text = "Changed" };
        Check(paused.ScenarioEvents[0].Popup!.Choices[0].Text == popup.Popup!.Choices[0].Text, "snapshot owns nested choice arrays");
        var turns = new WorldTurnRules { Buildings = [new("city", 0, new(1, 2, 3))] };
        var incomePopup = popup with { Id = "income", Condition = new() { Type = "money", Faction = "loser", Value = 101 } };
        var turnPopup = popup with { Condition = new() { Type = "round", Value = 2 } };
        var turnStart = basis with { ScenarioEvents = [incomePopup, turnPopup] };
        var interrupted = WorldTurns.EndTurn(turnStart, turnStart.Revision, turns).State;
        Check(interrupted.Phase == TurnPhase.RoundEvents && interrupted.Round == 2 && interrupted.Factions[0].Resources.Money == 100, "round popup precedes income");
        interrupted = WorldSave.Decode(WorldSave.Encode(interrupted), interrupted.ContentRevision);
        var roundChosen = ScenarioRules.Choose(interrupted, interrupted.Revision, "choice", "option.0").State;
        var incomeInterrupted = WorldTurns.Resume(roundChosen, roundChosen.Revision, turns).State;
        Check(incomeInterrupted.Factions[0].Resources == new Resources(101, 22, 33) && ScenarioRules.Pending(incomeInterrupted)?.Id == "income", "income condition fires after exactly one settlement");
        var incomeChosen = ScenarioRules.Choose(incomeInterrupted, incomeInterrupted.Revision, "income", "option.0").State;
        Check(incomeChosen.Phase == TurnPhase.Active && incomeChosen.Factions[0].Resources.Money == 101, "post-income choice preserves settled resources");
        // Real client commands cover content loading, deployment, profile recovery and event selections.
        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string path) => overrides.TryGetValue(path, out var value) ? value : File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        void Put(string path, object value) => overrides[path] = JsonSerializer.SerializeToUtf8Bytes(value, ExtensionLoader.Json);
        Put("rewrite/extension.json", new { schema = 1, apiVersion = 1, scenarioEvents = "rewrite/events.json" });
        overrides[popup.Popup.Image!] = Read("client/ui/conquest_background.png");
        Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [popup] });
        var content = new ClientContent(Read, ["conquest1.btl"]);
        string folder = Path.Combine(root, "rewrite/reports/scenario-events-20261021"); Directory.CreateDirectory(folder);
        string clientFolder = Path.Combine(folder, "client-" + Guid.NewGuid().ToString("N"));
        var host = new ClientGame(content, clientFolder);
        JsonElement Call(string route, object body, string? error = null)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean() == (error == null), route + " " + doc.RootElement.GetRawText()[..Math.Min(240, doc.RootElement.GetRawText().Length)]);
            if (error != null) { Check(doc.RootElement.GetProperty("code").GetString() == error, error); return default; }
            return doc.RootElement.GetProperty("data").Clone();
        }
        long Rev(JsonElement v) => v.GetProperty("revision").GetInt64();
        var hq = Call("headquarters", new { });
        hq = Call("commander", new { op = "claim", offer = "princess.0", revision = Rev(hq) });
        string owned = hq.GetProperty("roster")[0].GetProperty("id").GetString()!;
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        Check(view.GetProperty("scenarioPopup").GetProperty("choices").GetArrayLength() == 12, "popup in actual client API");
        Call("action", new { op = "dialogue", revision = Rev(view) }, "scenario.choice_required");
        Call("save", new { mode = 2, slot = 1 }); view = Call("load", new { mode = 2, slot = 1 });
        Check(view.GetProperty("scenarioPopup").GetProperty("id").GetString() == "choice", "client load resumes exact choice");
        Check(view.GetProperty("scenarioPopup").GetProperty("image").GetString() == "rewrite/images/council.png"
            && view.GetProperty("scenarioPopup").GetProperty("imageAlt").GetString() == "征服地图", "client load projects image and caption");
        view = Call("action", new { op = "event_choice", offer = "choice", target = "option.2", revision = Rev(view) });
        var availableUnit = view.GetProperty("units").EnumerateArray().First(u => u.GetProperty("owner").GetString() == view.GetProperty("playerFaction").GetString()
            && u.GetProperty("commanderId").ValueKind == JsonValueKind.Null && view.GetProperty("cells")[u.GetProperty("cell").GetInt32()].GetProperty("owner").GetString() == view.GetProperty("playerFaction").GetString());
        string unitId = availableUnit.GetProperty("id").GetString()!;
        var available = Call("deployment", new { unit = unitId, revision = Rev(view) });
        Check(available.GetProperty("commanders").EnumerateArray().Any(c => c.GetString() == owned), "owned HQ commander is available for selected unit");
        view = Call("action", new { op = "deploy", unit = unitId, commander = owned, revision = Rev(view) });
        Check(view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == unitId).GetProperty("commanderId").GetString() == owned, "actual deployment attaches owned instance");
        var automatic = Call("load", new { mode = 2, slot = 0 });
        Check(automatic.GetProperty("units").EnumerateArray().Any(u => u.GetProperty("commanderId").GetString() == owned), "deployment is automatically saved");
        hq = Call("headquarters", new { });
        Check(hq.GetProperty("roster")[0].GetProperty("locked").GetBoolean(), "deployed commander locked in HQ");
        Call("action", new { op = "deploy", unit = unitId, commander = owned, revision = Rev(automatic) }, "session.roster_unavailable");
        var slot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(clientFolder, "saves/mode-2/slot-0.json")), ClientGame.Json)!;
        var reference = content.Start("conquest1", "fra").Session;
        var deployedSession = SessionSave.Decode(slot.Session, new(reference.ContentRevision, reference.World.ContentRevision, reference.Account.ContentRevision));
        string foreign = deployedSession.World.Factions.First(f => f.Id != deployedSession.PlayerFaction && !f.Defeated).Id;
        var transfer = new ScriptedScenarioEvent { Id = "transfer", Effects = [new() { Type = "unit_owner", Unit = unitId, Faction = foreign }] };
        deployedSession = deployedSession with { World = deployedSession.World with { ScenarioEvents = deployedSession.World.ScenarioEvents.Append(transfer).ToArray() } };
        var transferred = SessionActions.CommitWorld(deployedSession, deployedSession.Revision, ScenarioRules.Evaluate(deployedSession.World)).State;
        Check(transferred.World.Commanders.Single(c => c.Id == owned).Owner == foreign, "event transfers deployed commander with unit");
        Check(transferred.RosterDeployments.Single(q => q.Faction == deployedSession.PlayerFaction).CommanderIds.Contains(owned), "original deployment quota remains consumed after transfer");
        Check(SessionSave.Decode(SessionSave.Encode(transferred), new(reference.ContentRevision, reference.World.ContentRevision, reference.Account.ContentRevision))
            .World.Units.Single(u => u.Id == unitId).Owner == foreign, "transferred owned commander and history survive session reload");
        var russian = content.Start("conquest1", "rus").Session;
        var industrialEvent = new ScriptedScenarioEvent { Id = "resource.exchange", Condition = new() { Type = "all", Conditions = [
            new() { Type = "active_faction", Faction = russian.PlayerFaction }, new() { Type = "industry", Faction = russian.PlayerFaction,
                Value = russian.World.Factions.Single(f => f.Id == russian.PlayerFaction).Resources.Industry + 2 }] },
            Effects = [new() { Type = "food", Value = 17 }] };
        popup = popup with { Popup = popup.Popup! with { Text = string.Join("\n\n", Enumerable.Range(1, 18)
            .Select(i => $"军情第{i}条：各军团已经集结，请核对沿途驻军与补给，再决定本次出征的粮食、金币和军工调拨方案。")) } };
        Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [popup, industrialEvent] });
        content = new ClientContent(Read, ["conquest1.btl"]);
        host = new ClientGame(content, Path.Combine(folder, "exchange-" + Guid.NewGuid().ToString("N")));
        view = Call("start", new { scenario = "conquest1", country = "rus" });
        view = Call("action", new { op = "event_choice", offer = "choice", target = "option.0", revision = Rev(view) });
        int market = content.Battle("conquest1").Sites.First(s => s.State6 == 1 && view.GetProperty("cells")[s.Cell].GetProperty("owner").GetString() == russian.PlayerFaction).Cell;
        long foodBefore = view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == russian.PlayerFaction).GetProperty("resources").GetProperty("food").GetInt64();
        var exchange = Call("exchange", new { cell = market, op = "buy", offer = "0", revision = Rev(view) });
        Check(exchange.GetProperty("resources").GetProperty("food").GetInt64() == foodBefore + 17, "resource exchange immediately triggers event in same transaction");
        Call("save", new { mode = 2, slot = 1 }); view = Call("load", new { mode = 2, slot = 1 });
        Check(view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == russian.PlayerFaction).GetProperty("resources").GetProperty("food").GetInt64() == foodBefore + 17, "dynamic event result is saved once");
        var runtime = new ExtensionScriptRuntime(new Dictionary<string, string> { ["rewrite/test.lua"] = "function custom(c) return #c.unitIds == 2 and c:commander('general').rank == 0 and c:cell(8).kind == 'city' end" });
        Check(ScenarioRules.Match(basis, new() { Type = "script", Script = "rewrite/test.lua", Function = "custom" }, runtime), "custom Lua sees enumerated world and commander state");
        // Preserve a small real import fixture for browser acceptance.
        string fixture = Path.Combine(folder, "raw-fixture"); Directory.CreateDirectory(fixture);
        foreach (var p in overrides) { string target = Path.Combine(fixture, p.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, p.Value); }
        File.Copy(Path.Combine(root, "rewrite/data/original/conquest1.btl"), Path.Combine(fixture, "conquest1.btl"), true);
        checks += ScenarioArmyChecks.Run(root);
        checks += ScenarioRecurrenceDiplomacyChecks.Run(root);
        checks += ScenarioFactionChecks.Run(root);
        checks += ScenarioControlChecks.Run(root);
        File.WriteAllText(Path.Combine(folder, "core.json"), JsonSerializer.Serialize(new { checks, errors = 0, fixture, unitId, cell = availableUnit.GetProperty("cell").GetInt32() }));
        return checks;
    }
}
