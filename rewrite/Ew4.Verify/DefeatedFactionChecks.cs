using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class DefeatedFactionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Defeated faction: " + label); }
        var profile = WorldChecks.Example().Units[0].Combatant.Profile with { MinimumAttack = 20, MaximumAttack = 20,
            Grade = "0", AttackBands = [new(-1, 1)] };
        UnitState Unit(string id, string owner, int cell, int health) => new() { Id = id, Owner = owner, Cell = cell,
            RemainingMovement = 6, Combatant = new() { Health = health, Profile = profile } };
        var initial = new WorldState { ScenarioId = "defeated-faction", ContentRevision = "fixture.1", Width = 4, Height = 1,
            ActiveFaction = "swiss", RandomState = 1946, AiRandomState = 1926,
            Factions = [new() { Id = "swiss", Camp = "neutral" }, new() { Id = "player", Camp = "allies", Human = true },
                new() { Id = "enemy", Camp = "enemy" }],
            Cells = [new() { Owner = "swiss" }, new() { Owner = "player" }, new() { Owner = "enemy" }, new() { Owner = "swiss" }],
            Units = [Unit("swiss.last", "swiss", 0, 1), Unit("player.defender", "player", 1, 100), Unit("enemy.unit", "enemy", 2, 100)] };
        var battle = new WorldBattleRules { ResolveIgnition = false, KillDialogueProbability = 0 };
        var turns = new WorldTurnRules { PlayerCamp = "allies" };
        WorldChange Attack(WorldState world) => WorldBattle.Attack(world, world.Revision, "swiss.last", "player.defender", battle);
        var killed = Attack(initial);
        byte[] stuckBytes = WorldSave.Encode(killed.State);
        var recovery = WorldTurns.RecoverDefeatedFaction(killed.State, turns);
        Check(killed.State.Factions.Single(f => f.Id == "swiss").Defeated, "counterattack eliminates attacker faction");
        Check(recovery.State is { ActiveFaction: "player", Phase: TurnPhase.Active, Outcome: BattleOutcome.Continue }, "handoff to living faction");
        Check(recovery.State.Revision == killed.State.Revision + 1 && recovery.Notices.Count(n => n.Kind == "faction.started") == 1,
            "recovery is one revision with one start");
        Check(recovery.Notices.All(n => n.Kind is not "faction.ended" and not "unit.destroyed" and not "battle.damage"), "no repeated settlement or combat");
        Check(recovery.State.RandomState == killed.State.RandomState && recovery.State.AiRandomState == killed.State.AiRandomState, "handoff does not reroll combat");
        Check(stuckBytes.SequenceEqual(WorldSave.Encode(killed.State)), "recovery preserves source snapshot");
        var loaded = WorldSave.Decode(stuckBytes, "fixture.1");
        var restored = WorldTurns.RecoverDefeatedFaction(loaded, turns);
        Check(WorldSave.Encode(recovery.State).SequenceEqual(WorldSave.Encode(restored.State)) && recovery.Notices.SequenceEqual(restored.Notices), "old save recovers identically");
        var again = WorldTurns.RecoverDefeatedFaction(recovery.State, turns);
        Check(ReferenceEquals(again.State, recovery.State) && again.Notices.Count == 0, "recovery is idempotent");
        foreach (var control in new[] {
            initial with { Units = initial.Units.Append(Unit("reserve", "swiss", 3, 100)).ToArray() },
            initial with { Cells = initial.Cells.Select((c, i) => i == 3 ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() } })
        {
            var survived = Attack(control).State;
            Check(!survived.Factions[0].Defeated && ReferenceEquals(WorldTurns.RecoverDefeatedFaction(survived, turns).State, survived), "surviving assets keep current turn");
        }
        var ordinary = initial with { Units = initial.Units.Select(u => u.Id == "swiss.last" ? u with { Combatant = u.Combatant with { Health = 100 } }
            : u.Id == "player.defender" ? u with { Combatant = u.Combatant with { Health = 1 } } : u).ToArray() };
        var defenderLost = Attack(ordinary).State;
        Check(ReferenceEquals(WorldTurns.RecoverDefeatedFaction(defenderLost, turns).State, defenderLost), "eliminating a nonactive faction does not end attacker turn");
        var weapon = initial with { Units = initial.Units.Select(u => u.Id == "swiss.last" ? u with { Combatant = u.Combatant with { Health = 100,
            Profile = profile with { Traits = ["consumed_on_attack"] } } } : u).ToArray() };
        Check(WorldTurns.RecoverDefeatedFaction(Attack(weapon).State, turns).State.ActiveFaction == "player", "consumed last weapon also hands off");
        var scripted = WorldEvents.Surrender(initial, "swiss", null).State;
        Check(WorldTurns.RecoverDefeatedFaction(scripted, turns).State.ActiveFaction == "player", "script surrender uses same recovery");
        var dialogueWorld = initial with { Events = [new() { Id = "death.one", DialogueId = "one", Rules = [new() { Trigger = TriggerKind.UnitDestroyed, UnitId = "swiss.last" }] },
            new() { Id = "death.two", DialogueId = "two", Rules = [new() { Trigger = TriggerKind.UnitDestroyed, UnitId = "swiss.last" }] }] };
        var pending = WorldTurns.RecoverDefeatedFaction(Attack(dialogueWorld).State, turns);
        Check(pending.State is { ActiveFaction: "swiss", Phase: TurnPhase.EndFactionEvents, PendingDialogue: "one" }
            && pending.Notices.All(n => n.Kind != "faction.started"), "dialogue delays handoff");
        var first = WorldTurns.Resume(WorldSave.Decode(WorldSave.Encode(pending.State), "fixture.1"), pending.State.Revision, turns, "one");
        Check(first.State.PendingDialogue == "two" && first.State.ActiveFaction == "swiss", "queued dialogue keeps handoff pending");
        var last = WorldTurns.Resume(first.State, first.State.Revision, turns, "two");
        Check(last.State.ActiveFaction == "player" && last.State.PendingDialogue == null && last.Notices.Count(n => n.Kind == "faction.started") == 1, "last acknowledgement hands off once");
        var wrap = killed.State with { Factions = [killed.State.Factions[1], killed.State.Factions[2], killed.State.Factions[0]],
            Cells = killed.State.Cells.Select((c, i) => i == 1 ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() };
        var paid = WorldTurns.RecoverDefeatedFaction(wrap, turns with { Buildings = [new("city", 1, new(11, 7, 5))] });
        Check(paid.State.Round == 2 && paid.State.Factions[0].Resources == new Resources(11, 7, 5)
            && paid.Notices.Count(n => n.Kind == "round.started") == 1, "wrap pays next faction once");
        var playerLost = killed.State with { Factions = killed.State.Factions.Select(f => f with { Human = f.Id == "swiss" }).ToArray() };
        var defeat = WorldTurns.RecoverDefeatedFaction(playerLost, turns with { PlayerCamp = "neutral" });
        Check(defeat.State is { Phase: TurnPhase.Finished, Outcome: BattleOutcome.Defeat } && defeat.Notices.All(n => n.Kind != "faction.started"), "player defeat resolves before starting another faction");
        var mutual = initial with { Units = initial.Units.Select(u => u.Id == "player.defender" ? u with { Combatant = u.Combatant with { Health = 1 } } : u).ToArray() };
        Check(WorldTurns.RecoverDefeatedFaction(Attack(mutual).State, turns).State.Outcome == BattleOutcome.Defeat, "mutual last-unit death preserves player defeat");
        var finished = defeat.State;
        Check(ReferenceEquals(WorldTurns.RecoverDefeatedFaction(finished, turns).State, finished), "finished battle is untouched");
        var allDead = killed.State with { Units = [], Factions = killed.State.Factions.Select(f => f with { Defeated = true }).ToArray() };
        Check(WorldTurns.RecoverDefeatedFaction(allDead, new()).State is { Phase: TurnPhase.Finished, Outcome: BattleOutcome.Defeat }, "no surviving factions finishes cleanly");
        var local = killed.State with { ScenarioPlayerFaction = "swiss", LocalMultiplayer = new("swiss", ["swiss", "player"]),
            Factions = killed.State.Factions.Select(f => f with { Human = f.Id is "swiss" or "player" }).ToArray() };
        Check(WorldTurns.RecoverDefeatedFaction(local, turns).State.ActiveFaction == "player", "local play skips eliminated current human country");

        string assets = Path.Combine(root, "rewrite/data/original");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("original/", StringComparison.Ordinal) ? Path.Combine(assets, path[9..])
            : Path.Combine(root, "rewrite/data/legacy", path[5..]));
        var content = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        var start = content.Start("conquest1", "fra");
        var source = content.Battle("conquest1");
        int swissIndex = source.Factions.ToList().FindIndex(f => f.CountryId == "che");
        string swiss = LegacyScenarioInitialization.FactionId("conquest1", swissIndex, source.Factions[swissIndex]);
        string player = start.Session.PlayerFaction;
        var old = start.Session.World;
        var map = new HexMap(old.Width, old.Height);
        bool OpenLand(int cell) => cell >= 0 && old.Cells[cell].Terrain.CanTraverse() && !old.Cells[cell].Terrain.Sea
            && old.Cells[cell].BuildingKind == null && old.Cells[cell].InstallationKind == null && !old.Cells[cell].Objective
            && start.Rules.Ai.SitePriorities.GetValueOrDefault(cell) == 0;
        int attackCell = Enumerable.Range(0, old.Cells.Count).First(i => OpenLand(i) && Enumerable.Range(0, 6).Any(d => OpenLand(map.Neighbor(i, d))));
        int targetCell = Enumerable.Range(0, 6).Select(d => map.Neighbor(attackCell, d)).First(OpenLand);
        var commander = old.Commanders.First(c => c.Owner == swiss);
        var attacker = Unit("test.swiss", swiss, attackCell, 1) with { Behavior = "3", CommanderId = commander.Id,
            Combatant = new() { Health = 1, Profile = profile with { MaxHealth = 1 } } };
        var target = Unit("test.player", player, targetCell, 100) with { Combatant = new() { Health = 1000, Profile = profile with { MaxHealth = 1000 } } };
        var clientWorld = old with { ActiveFaction = swiss, Events = [], ScenarioEvents = [],
            Units = old.Units.Where(u => u.Owner != swiss && u.Cell != attackCell && u.Cell != targetCell).Append(attacker).Append(target).ToArray(),
            Factions = old.Factions.Select(f => f.Id == swiss ? f with { Resources = new(0, 0, 0), Recruitment = new Dictionary<string, RecruitmentGrant>() } : f).ToArray(),
            Cells = old.Cells.Select((c, i) => i == attackCell ? c with { Owner = swiss } : i == targetCell ? c with { Owner = player, Objective = true }
                : c.Owner == swiss ? c with { Owner = player } : c).ToArray() };
        clientWorld = WorldDiplomacy.Set(clientWorld, swiss, player, "hostile");
        var plan = WorldAi.PlanStep(clientWorld, start.Rules.Ai, start.Rules.Production[swiss], start.Rules.Turns);
        Check(plan.Unit != null && plan.Unit.Candidates[AiPlanning.Best(plan.Unit.Candidates)].Action is AiStrikeCommand, "real 1798 Swiss fixture chooses attack");
        string folder = Path.Combine(root, "rewrite/reports/defeated-faction-test", Guid.NewGuid().ToString("N"));
        ClientGame Host(string name, WorldState world)
        {
            string path = Path.Combine(folder, name); Directory.CreateDirectory(path);
            var session = start.Session with { World = world };
            File.WriteAllBytes(Path.Combine(path, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
            return new(content, path);
        }
        JsonElement Call(ClientGame host, string route, object command)
        {
            using var parsed = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(parsed.RootElement.GetProperty("ok").GetBoolean(), route + ": " + parsed.RootElement.GetRawText()[..Math.Min(200, parsed.RootElement.GetRawText().Length)]);
            return parsed.RootElement.GetProperty("data").Clone();
        }
        // Ordinary conquest removes a different faction while the attacker keeps its turn.
        foreach (bool lastCity in new[] { false, true })
        {
            var conqueror = target with { Id = "normal.attacker", Cell = attackCell, CommanderId = null };
            var defender = attacker with { Id = "normal.defender", Cell = targetCell, CommanderId = null };
            var normalWorld = clientWorld with { ActiveFaction = player,
                Units = clientWorld.Units.Where(u => u.Id != attacker.Id && u.Id != target.Id).Append(conqueror)
                    .Concat(lastCity ? [] : new[] { defender }).ToArray(),
                Cells = clientWorld.Cells.Select((c, i) => i == attackCell ? c with { Owner = player }
                    : i == targetCell ? c with { Owner = swiss, Objective = false,
                        BuildingKind = lastCity ? "city" : null, BuildingLevel = lastCity ? 1 : 0 } : c).ToArray() };
            var host = Host(lastCity ? "normal-last-city" : "normal-last-unit", normalWorld);
            var view = Call(host, "load", new { slot = 1 });
            view = Call(host, "action", new { op = lastCity ? "move" : "attack", unit = conqueror.Id,
                target = defender.Id, cell = targetCell, revision = view.GetProperty("revision").GetInt64() });
            Check(view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == swiss)
                .GetProperty("defeated").GetBoolean(), "ordinary " + (lastCity ? "last city capture" : "last unit kill") + " eliminates faction");
            Check(view.GetProperty("activeFaction").GetString() == player && view.GetProperty("phase").GetString() == "Active",
                "ordinary elimination keeps conqueror active");
            view = Call(host, "action", new { op = "end", revision = view.GetProperty("revision").GetInt64() });
            int batches = 0;
            while (view.GetProperty("activeFaction").GetString() != player && view.GetProperty("phase").GetString() != "Finished")
            {
                Check(view.GetProperty("activeFaction").GetString() != swiss, "ordinary turn rotation skips defeated country");
                Check(++batches <= 256, "ordinary elimination AI round terminates");
                var batch = Call(host, "action", new { op = "ai_batch", revision = view.GetProperty("revision").GetInt64() });
                Check(batch.GetProperty("error").ValueKind == JsonValueKind.Null, "ordinary elimination batch has no error");
                view = batch.GetProperty("state");
            }
            Check(batches > 0 && view.GetProperty("activeFaction").GetString() == player,
                "ordinary elimination completes a full AI round and returns to player");
        }
        byte[]? singleWorld = null;
        foreach (string op in new[] { "ai", "ai_batch" })
        {
            var host = Host(op, clientWorld);
            var view = Call(host, "load", new { slot = 1 });
            Check(view.GetProperty("revision").GetInt64() == start.Session.Revision, "loading healthy current faction preserves revision");
            var result = Call(host, "action", new { op, revision = view.GetProperty("revision").GetInt64() });
            if (op == "ai_batch") { Check(result.GetProperty("error").ValueKind == JsonValueKind.Null, "batch has no defeated-faction error"); result = result.GetProperty("state"); }
            Check(result.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == swiss).GetProperty("defeated").GetBoolean(), op + " actually eliminates Swiss faction");
            Check(result.GetProperty("activeFaction").GetString() != swiss && result.GetProperty("phase").GetString() == "Active", op + " commits handoff");
            Check(result.GetProperty("revision").GetInt64() == view.GetProperty("revision").GetInt64() + 1, op + " remains one session transaction");
            var autosave = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(folder, op, "saves/mode-2/slot-0.json")), ClientGame.Json)!;
            var session = SessionSave.Decode(autosave.Session, new(start.Session.ContentRevision, old.ContentRevision, start.Session.Account.ContentRevision));
            var bytes = WorldSave.Encode(session.World);
            if (singleWorld == null) singleWorld = bytes;
            else Check(singleWorld.SequenceEqual(bytes), "single and batch state including both random streams match");
        }
        var dialogueHost = Host("dialogues", clientWorld with { Events = [new() { Id = "death.one", DialogueId = "one",
            Rules = [new() { Trigger = TriggerKind.UnitDestroyed, UnitId = attacker.Id }] }, new() { Id = "death.two", DialogueId = "two",
            Rules = [new() { Trigger = TriggerKind.UnitDestroyed, UnitId = attacker.Id }] }] });
        var dialogueView = Call(dialogueHost, "load", new { slot = 1 });
        dialogueView = Call(dialogueHost, "action", new { op = "ai", revision = dialogueView.GetProperty("revision").GetInt64() });
        Check(dialogueView.GetProperty("phase").GetString() == "EndFactionEvents" && dialogueView.GetProperty("activeFaction").GetString() == swiss, "client waits for death dialogues");
        dialogueView = Call(dialogueHost, "action", new { op = "dialogue", revision = dialogueView.GetProperty("revision").GetInt64() });
        Check(dialogueView.GetProperty("activeFaction").GetString() == swiss, "client retains queued death dialogue");
        dialogueView = Call(dialogueHost, "action", new { op = "dialogue", revision = dialogueView.GetProperty("revision").GetInt64() });
        Check(dialogueView.GetProperty("activeFaction").GetString() != swiss && dialogueView.GetProperty("phase").GetString() == "Active", "client acknowledges then hands off");
        var popupHost = Host("popup", clientWorld with { ScenarioPlayerFaction = player, ScenarioEvents = [new() { Id = "swiss.dead",
            Condition = new() { Type = "faction_defeated", Faction = swiss }, Popup = new() { Text = "Defeated" } }] });
        var popupView = Call(popupHost, "load", new { slot = 1 });
        popupView = Call(popupHost, "action", new { op = "ai", revision = popupView.GetProperty("revision").GetInt64() });
        Check(popupView.GetProperty("phase").GetString() == "EndFactionEvents" && popupView.GetProperty("activeFaction").GetString() == swiss, "scenario popup blocks handoff");
        popupView = Call(popupHost, "action", new { op = "event_choice", offer = "swiss.dead", target = "ok", revision = popupView.GetProperty("revision").GetInt64() });
        Check(popupView.GetProperty("activeFaction").GetString() != swiss && popupView.GetProperty("phase").GetString() == "Active", "scenario choice resumes handoff");
        var beforeScript = clientWorld with { Units = clientWorld.Units.Select(u => u.Id == attacker.Id ? u with {
            Combatant = u.Combatant with { Health = 1000, Profile = profile with { MaxHealth = 1000 } } } : u).ToArray() };
        Check(!WorldBattle.Attack(beforeScript, beforeScript.Revision, attacker.Id, target.Id, start.Rules.Battle).State.Factions.Single(f => f.Id == swiss).Defeated,
            "script control survives ordinary combat");
        var scriptHost = Host("script", beforeScript with { ScenarioPlayerFaction = player, ScenarioEvents = [new() { Id = "script.death",
            Effects = [new() { Type = "destroy_unit", Unit = attacker.Id }] }] });
        var scriptView = Call(scriptHost, "load", new { slot = 1 });
        scriptView = Call(scriptHost, "action", new { op = "ai", revision = scriptView.GetProperty("revision").GetInt64() });
        Check(scriptView.GetProperty("activeFaction").GetString() != swiss, "script-triggered death hands off through client commit");
        var broken = WorldBattle.Attack(clientWorld, clientWorld.Revision, attacker.Id, target.Id, start.Rules.Battle).State;
        Check(broken.Factions.Single(f => f.Id == swiss).Defeated && broken.ActiveFaction == swiss, "legacy stuck save fixture");
        var legacyHost = Host("legacy", broken);
        var recoveredView = Call(legacyHost, "load", new { slot = 1 });
        Check(recoveredView.GetProperty("activeFaction").GetString() != swiss, "real client load recovers legacy save");
        var reload = Call(legacyHost, "load", new { slot = 0, mode = 2 });
        Check(reload.GetProperty("activeFaction").GetString() == recoveredView.GetProperty("activeFaction").GetString()
            && reload.GetProperty("revision").GetInt64() == recoveredView.GetProperty("revision").GetInt64(), "reloading recovered autosave does not advance again");
        var oldDialogue = clientWorld with { Events = [new() { Id = "old.death", DialogueId = "old.dialogue",
            Rules = [new() { Trigger = TriggerKind.UnitDestroyed, UnitId = attacker.Id }] }] };
        var oldDialogueHost = Host("old-dialogue", WorldBattle.Attack(oldDialogue, oldDialogue.Revision, attacker.Id, target.Id, start.Rules.Battle).State);
        var oldDialogueView = Call(oldDialogueHost, "load", new { slot = 1 });
        Check(oldDialogueView.GetProperty("phase").GetString() == "EndFactionEvents" && oldDialogueView.GetProperty("activeFaction").GetString() == swiss,
            "legacy load preserves pending death dialogue");
        oldDialogueView = Call(oldDialogueHost, "action", new { op = "dialogue", revision = oldDialogueView.GetProperty("revision").GetInt64() });
        Check(oldDialogueView.GetProperty("activeFaction").GetString() != swiss, "legacy death dialogue can finish handoff");
        return checks;
    }
}
