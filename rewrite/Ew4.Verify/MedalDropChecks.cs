using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class MedalDropChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Medal drops: " + label); }
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/medal_drop_vectors.json")));
        string?[] kinds = [null, "city", "industry", "stable", "port", "farmland"];
        foreach (var v in data.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var successes = v.GetProperty("successes").EnumerateArray().Select(n => n.GetInt32()).ToHashSet();
            for (int roll = 0; roll < 100; roll++)
                Check(BattleMedals.Drops(v.GetProperty("damage").GetInt32(), kinds[v.GetProperty("kind").GetInt32() + 1],
                    v.GetProperty("level").GetInt32(), roll) == successes.Contains(roll), "native threshold " + v.GetProperty("kind") + "/" + roll);
        }
        var basis = WorldChecks.Example();
        var attacker = basis.Units.Single(u => u.Owner == "winner") with { Cell = 1, RemainingMovement = 6 };
        var defender = basis.Units.Single(u => u.Owner == "loser") with { Cell = 0 };
        var fixture = basis with { Units = [attacker, defender], Cells = basis.Cells.Select((c, i) => i == 0 ? c with { BuildingKind = "industry", BuildingLevel = 3 } : c).ToArray() };
        var rules = new WorldBattleRules { CollectMedals = true, CheckFactionSurvival = false, ResolveIgnition = false,
            RecalculateLocalMorale = false, KillDialogueProbability = 0 };
        for (ulong seed = 0; seed < 120; seed++)
        {
            foreach (var (humanAttack, humanDefend, dealt, taken) in new[] { (true, false, 35, 0), (false, true, 0, 35), (true, true, 35, 35),
                (false, false, 35, 35), (true, false, 0, 0), (true, false, 100, 0), (false, true, 0, 100) })
            {
                var state = fixture with { AiRandomState = seed, Factions = fixture.Factions.Select(f => f with { Human = f.Id == attacker.Owner ? humanAttack : humanDefend }).ToArray() };
                var battle = new ResolvedBattle(attacker.Id, defender.Id, taken, dealt, taken > 0, state.RandomState);
                var plain = WorldBattle.CommitResolved(state, battle, rules with { CollectMedals = false });
                var actual = WorldBattle.CommitResolved(state, battle, rules);
                var random = new AiRandom(seed); var expected = new List<string>();
                foreach (var (human, unit, damage) in new[] { (humanAttack, attacker, dealt), (humanDefend, defender, taken) })
                    if (human && BattleMedals.Drops(damage, "industry", 3, (int)random.Next(100))) expected.Add(unit.Owner);
                Check(actual.Notices.Where(n => n.Kind == "battle.medal_dropped").Select(n => n.Subject).SequenceEqual(expected), "human eligibility, attack/counter order and lethal damage");
                Check(actual.State.AiRandomState == random.State && actual.State.RandomState == plain.State.RandomState,
                    "system stream advances in order; combat random stream unchanged");
                Check(actual.State.Units.Select(u => (u.Id, u.Combatant.Health)).SequenceEqual(plain.State.Units.Select(u => (u.Id, u.Combatant.Health))), "drop does not alter damage");
            }
        }
        var noDrop = WorldBattle.CommitResolved(fixture, new(attacker.Id, defender.Id, 100, 0, true, fixture.RandomState), rules with { CheckFactionSurvival = true });
        Check(noDrop.State.Factions.Single(f => f.Human).Defeated && !noDrop.Notices.Any(n => n.Kind == "battle.medal_dropped")
            && noDrop.State.AiRandomState == fixture.AiRandomState, "defeated player bypasses entire drop branch");
        var aiState = fixture with { Factions = fixture.Factions.Select(f => f with { Human = f.Id == defender.Owner }).ToArray() };
        var aiRules = new WorldAiRules();
        foreach (WorldAiCommand command in new WorldAiCommand[] { new AiStrikeCommand(aiState.Revision, attacker.Id, defender.Id), new AiMoveCommand(aiState.Revision, attacker.Id, 3, defender.Id) })
        {
            const ulong afterPlanning = 2345;
            var plan = new AiUnitPlan(aiState.Revision, attacker.Id, aiState.AiRandomState, afterPlanning, [new(command, 1)]);
            var actual = WorldAi.ExecuteUnit(aiState, plan, aiRules, rules);
            var expectedRandom = new AiRandom(afterPlanning);expectedRandom.Next(100);
            Check(actual.State.AiRandomState == expectedRandom.State, "AI strike/move commits planning draws before player counter drop draw");
        }
        checks += AiUnitChecks.Run(root);
        var session = new GameSession { BattleId = "drop-run", ContentRevision = "session.1", PlayerFaction = "winner", World = fixture,
            Account = new() { Id = "account", ContentRevision = "account.1", Medals = 100 } };
        WorldNotice[] drops = [new("battle.medal_dropped", "winner", 1) { Target = attacker.Id }];
        var collected = SessionActions.CommitWorld(session, 0, new(fixture with { Revision = 1 }, drops)).State;
        Check(collected.CollectedMedals == 1 && collected.Account.Medals == 101 && collected.Account.CollectedMedalReceipts[session.BattleId] == 1, "immediate grant and cumulative receipt");
        var binding = new SessionContentBinding(session.ContentRevision, session.World.ContentRevision, session.Account.ContentRevision);
        var restored = SessionSave.Decode(SessionSave.Encode(collected), binding);
        Check(restored.CollectedMedals == 1 && restored.Account.CollectedMedalReceipts[session.BattleId] == 1, "session roundtrip retains count and paid receipt");
        var replay = SessionActions.CommitWorld(session with { Account = collected.Account }, 0, new(fixture with { Revision = 1 }, drops)).State;
        Check(replay.CollectedMedals == 1 && replay.Account.Medals == 101 && replay.Account.Revision == collected.Account.Revision, "old slot with current account cannot repeat grant");
        var second = SessionActions.CommitWorld(replay, replay.Revision, new(replay.World with { Revision = 2 }, drops)).State;
        Check(second.CollectedMedals == 2 && second.Account.Medals == 102, "only cumulative amount above receipt is paid");
        var finished = second with { World = second.World with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory } };
        var settled = BattleSettlement.Settle(finished, 1, "campaign", 20, 10);
        Check(settled.BattleResult!.CollectedMedals == 2 && settled.Account.Medals == 252, "settlement shows collection but only adds campaign award");
        Check(BattleSettlement.Settle(settled, 1, "campaign", 20, 10).Account.Medals == 252, "repeated settlement cannot repeat either grant");

        byte[] Read(string name) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", name.StartsWith("data/") ? "legacy/" + name[5..] : name));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var started = content.Start("campaign1_01", null);
        Check(started.Rules.Battle.CollectMedals && content.Start("conquest1", "fra").Rules.Battle.CollectMedals, "campaign and conquest enable drops after fingerprinting");
        var local = content.Start("campaign1_01", null, localPlayers: [started.Session.PlayerFaction]);
        Check(!local.Rules.Battle.CollectMedals, "local multiplayer disables account drops");
        string folder = Path.Combine(root, "build/medal-drops-20261022/profile-check");
        Directory.CreateDirectory(folder);
        var active = started.Session with { BattleId = "client-drop-run", CollectedMedals = 2, Account = started.Session.Account with {
            Revision = 500, Medals = 1002, CollectedMedalReceipts = new Dictionary<string, long> { ["client-drop-run"] = 2 } } };
        void Save(GameSession s, int slot) => File.WriteAllBytes(Path.Combine(folder, "slot-" + slot + ".json"),
            JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("campaign1_01", null, SessionSave.Encode(s)), ClientGame.Json));
        Save(active, 1);Save(active with { CollectedMedals = 0, Account = active.Account with { Revision = 1, Medals = 1000, CollectedMedalReceipts = new Dictionary<string, long>() } }, 2);
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(active.Account, ClientGame.Json));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object input)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(input, ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(), "client " + route + " " + doc.RootElement);
            return doc.RootElement.GetProperty("data").Clone();
        }
        Check(Call("load", new { slot = 1 }).GetProperty("medals").GetInt64() == 1002, "loading collection does not pay again");
        host = new ClientGame(content, folder);
        Check(Call("load", new { slot = 2 }).GetProperty("medals").GetInt64() == 1002, "restart and earlier slot retain account balance");
        var account = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Path.Combine(folder, "account.json")), ClientGame.Json)!;
        Check(account.CollectedMedalReceipts[active.BattleId] == 2, "restart/earlier slot retain paid receipt");
        var browserStart = content.Start("campaign1_02b", null);
        var browserState = browserStart.Session.World;
        ulong dropSeed = Enumerable.Range(0, 10000).Select(i => (ulong)i).First(s => new AiRandom(s).Next(100) == 99);
        var map = new HexMap(browserState.Width, browserState.Height);
        var pair = (from a in browserState.Units where a.Owner == browserStart.Session.PlayerFaction && !a.Acted && a.Combatant.ConstructionTurns == 0
            from d in browserState.Units where !WorldDiplomacy.Allied(browserState, a.Owner, d.Owner)
                && map.Distance(a.Cell, d.Cell) >= a.Combatant.Profile.MinimumRange && map.Distance(a.Cell, d.Cell) <= a.Combatant.Profile.MaximumRange
            let preview = WorldBattle.Preview(browserState, browserState.Revision, a.Id, d.Id, browserStart.Rules.Battle)
            where preview.Main.DefenderDamage >= 20 && preview.Main.DefenderDamage < d.Combatant.Health
            select (Attacker: a, Defender: d)).First();
        string browserFolder = Path.Combine(root, "build/medal-drops-20261022/browser-profile");Directory.CreateDirectory(browserFolder);
        var browserSession = browserStart.Session with { BattleId = "browser-medal-native-threshold", Account = browserStart.Session.Account with { Revision = 500 },
            World = browserState with { AiRandomState = dropSeed } };
        File.WriteAllBytes(Path.Combine(browserFolder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(browserSession.Account, ClientGame.Json));
        File.WriteAllBytes(Path.Combine(browserFolder, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("campaign1_02b", null, SessionSave.Encode(browserSession)), ClientGame.Json));
        File.WriteAllText(Path.Combine(root, "build/medal-drops-20261022/browser-fixture.json"), JsonSerializer.Serialize(new {
            attacker = pair.Attacker.Id, defender = pair.Defender.Id, attackerCell = pair.Attacker.Cell, defenderCell = pair.Defender.Cell,
            medals = browserSession.Account.Medals, seed = dropSeed, scope = "Actual campaign units/stats/positions and combat seed; only system-random seed fixed to exercise drop" }));
        return checks;
    }
}
