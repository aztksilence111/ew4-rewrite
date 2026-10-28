using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class BattleSettlementChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Battle settlement: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/victory_settlement_vectors.json")));
        foreach (var v in vectors.RootElement.GetProperty("ratings").EnumerateArray())
            Check((v.GetProperty("won").GetBoolean() ? BattleSettlement.Stars(v.GetProperty("round").GetInt32(), v.GetProperty("minimum").GetInt32(), v.GetProperty("highest").GetInt32()) : 0)
                == v.GetProperty("stars").GetInt32(), "native rating " + v);
        foreach (var v in vectors.RootElement.GetProperty("conquest").EnumerateArray())
        {
            var resources = v.GetProperty("resources").EnumerateArray().Select(n => n.GetInt64()).ToArray();
            var roster = Enumerable.Range(0, v.GetProperty("generals").GetInt32()).Select(i => new OwnedCommander("g" + i, "g" + i,
                new(new(v.GetProperty("rank").GetInt32(), 0), new(v.GetProperty("title").GetInt32(), 0), new Dictionary<string, int>()), 0, [])).ToArray();
            var progress = Enumerable.Range(0, v.GetProperty("progress").GetInt32()).ToDictionary(i => "c" + i, _ => 1);
            var session = new GameSession { PlayerFaction = "p", Account = new() { Roster = roster, CampaignStars = progress },
                World = new() { Round = v.GetProperty("round").GetInt32(), Factions = [new() { Id = "p", Resources = new(resources[0], resources[1], resources[2]) }] } };
            Check(BattleSettlement.ConquestAward(session) == v.GetProperty("award").GetInt32(), "native conquest " + v);
        }
        int[] awards = vectors.RootElement.GetProperty("campaignAwards").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        for (int before = 0; before <= 5; before++)
            for (int after = 0; after <= 5; after++)
                Check(BattleSettlement.CampaignAward(after, before) == Math.Max(0, awards[after] - awards[before]), "campaign incremental award");

        string folder = Path.Combine(root, "rewrite/reports/victory_settlement_20261021");
        Directory.CreateDirectory(folder);
        string profile = Path.Combine(folder, "profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var host = new ClientGame(content, profile);
        JsonElement Call(string route, object command)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(), route + ": " + doc.RootElement.ToString()[..Math.Min(200, doc.RootElement.ToString().Length)]);
            return doc.RootElement.GetProperty("data").Clone();
        }
        var basis = content.Start("campaign1_01", null).Session with { BattleId = "settlement-test-campaign" };
        var source = content.Battle(basis.World.ScenarioId);
        GameSession Finish(GameSession session, int round, BattleOutcome outcome = BattleOutcome.Victory) => session with {
            World = session.World with { Round = round, Phase = TurnPhase.Finished, Outcome = outcome, PendingDialogue = null } };
        void Slot(GameSession session, int slot = 1) => File.WriteAllBytes(Path.Combine(profile, "slot-" + slot + ".json"),
            JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(session.World.ScenarioId, null, SessionSave.Encode(session)), ClientGame.Json));
        var won = Finish(basis, source.Header[13]);
        Slot(won);
        var first = Call("load", new { slot = 1 });
        long medals = first.GetProperty("medals").GetInt64();
        Check(first.GetProperty("battleResult").GetProperty("stars").GetInt32() == 5, "five stars at exact highest boundary");
        Check(first.GetProperty("battleResult").GetProperty("awardMedals").GetInt64() == 150, "first completion awards 150");
        Check(first.GetProperty("round").GetInt32() == source.Header[13], "actual round projected");
        Check(first.GetProperty("battleResult").GetProperty("highestRatingRound").GetInt32() == source.Header[13], "actual metadata projected");
        var repeat = Call("action", new { op = "state" });
        Check(repeat.GetProperty("medals").GetInt64() == medals, "refresh does not pay");
        Call("save", new { slot = 2 });
        host = new ClientGame(content, profile);
        var restored = Call("load", new { slot = 2 });
        Check(restored.GetProperty("medals").GetInt64() == medals && restored.GetProperty("battleResult").GetProperty("id").GetString() == "settlement-test-campaign", "restart and saved receipt");
        restored = Call("load", new { slot = 1 });
        Check(restored.GetProperty("medals").GetInt64() == medals, "pre-payout save cannot pay twice");
        Slot(Finish(basis, source.Header[12]), 3);
        restored = Call("load", new { slot = 3 });
        Check(restored.GetProperty("battleResult").GetProperty("stars").GetInt32() == 1 && restored.GetProperty("medals").GetInt64() == medals, "replayed run at different round shows current rating without paying");
        Slot(Finish(basis with { BattleId = "defeat" }, 10, BattleOutcome.Defeat), 4);
        var lost = Call("load", new { slot = 4 });
        Check(lost.GetProperty("battleResult").GetProperty("outcome").GetString() == "Defeat" && lost.GetProperty("battleResult").GetProperty("awardMedals").GetInt64() == 0, "campaign defeat has no victory reward");
        var restarted = Call("start", new { scenario = "campaign1_01" });
        Check(restarted.GetProperty("battleResult").ValueKind == JsonValueKind.Null, "new run resets result");
        Call("save", new { slot = 5 });
        var newSlot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(profile, "slot-5.json")), ClientGame.Json)!;
        var decoded = SessionSave.Decode(newSlot.Session, new(basis.ContentRevision, basis.World.ContentRevision, basis.Account.ContentRevision));
        Check(decoded.BattleId.Length == 32 && decoded.BattleId != basis.BattleId, "fresh run identity persists before ending");

        var account = basis.Account;
        var oneStar = BattleSettlement.Settle(Finish(basis, source.Header[12]), 1, "campaign1_01", source.Header[12], source.Header[13]);
        var improved = BattleSettlement.Settle(won with { BattleId = "improved", Account = oneStar.Account }, 1, "campaign1_01", source.Header[12], source.Header[13]);
        Check(oneStar.Account.Medals == account.Medals + 25 && improved.BattleResult!.AwardMedals == 125, "only improved star difference is paid");
        var pending = BattleSettlement.Settle(won with { World = won.World with { PendingDialogue = "pending" } }, 1, "campaign1_01", source.Header[12], source.Header[13]);
        Check(pending.BattleResult == null && pending.Account.Medals == account.Medals, "pending event dialogue blocks settlement");
        var replay = BattleSettlement.Settle(improved, 1, "campaign1_01", source.Header[12], source.Header[13]);
        Check(replay.Account.Revision == improved.Account.Revision, "settlement is idempotent");

        // Reach the final red objective through the real movement API, rather than forcing a result.
        var start = content.Start("campaign1_01", null);
        var world = start.Session.World with { ActiveFaction = start.Session.PlayerFaction, Phase = TurnPhase.Active, PendingDialogue = null, Events = [], TriggeredEvents = [], ScenarioEvents = [] };
        var mover = world.Units.First(u => u.Owner == start.Session.PlayerFaction && WorldActions.Reachable(world, u.Id, start.Rules.Movement).Keys
            .Any(c => c != u.Cell && !world.Cells[c].Terrain.Sea && !world.Units.Any(v => v.Cell == c)));
        int destination = WorldActions.Reachable(world, mover.Id, start.Rules.Movement).Keys.First(c => c != mover.Cell && !world.Cells[c].Terrain.Sea && !world.Units.Any(u => u.Cell == c));
        string enemy = world.Factions.First(f => f.Camp != world.Factions.Single(p => p.Id == start.Session.PlayerFaction).Camp && f.Camp != "4").Id;
        world = world with { Cells = world.Cells.Select((c, i) => c with { Objective = i == destination || i == mover.Cell, Owner = i == destination ? enemy : c.Owner }).ToArray(),
            Units = world.Units.Select(u => u with { Objective = false }).ToArray() };
        var fixture = start.Session with { BattleId = "movement-win", World = world };
        Slot(fixture, 6);
        var ready = Call("load", new { slot = 6 });
        var moved = Call("action", new { op = "move", revision = ready.GetProperty("revision").GetInt64(), unit = mover.Id, cell = destination });
        Check(moved.GetProperty("outcome").GetString() == "Victory" && moved.GetProperty("battleResult").ValueKind == JsonValueKind.Object,
            "last red capture produces and projects result: " + moved.GetProperty("outcome") + " owner=" + moved.GetProperty("cells")[destination].GetProperty("owner"));
        Check(content.Start("conquest1", "fra").Rules.Turns.Victory.ObjectivesEnabled == false, "conquest retains its elimination conditions");

        var aiFixture = fixture with { BattleId = "ai-result-equivalence", World = world with { ActiveFaction = enemy,
            Cells = world.Cells.Select((c, i) => c with { Objective = i == mover.Cell }).ToArray() } };
        ClientGame AiHost(string name)
        {
            string path = Path.Combine(profile, name);Directory.CreateDirectory(path);
            var stableAccount = aiFixture.Account with { CommanderOffers = new Dictionary<int, IReadOnlyList<string>> { [0] = [], [1] = [], [2] = [] } };
            File.WriteAllBytes(Path.Combine(path, "account.json"), JsonSerializer.SerializeToUtf8Bytes(stableAccount, ClientGame.Json));
            File.WriteAllBytes(Path.Combine(path, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(aiFixture.World.ScenarioId, null,
                SessionSave.Encode(aiFixture with { Account = stableAccount })), ClientGame.Json));
            return new(content, path);
        }
        JsonElement AiCall(ClientGame target, string op, long revision = -1)
        {
            using var doc = JsonDocument.Parse(target.Dispatch(op == "load" ? "/api/load" : "/api/action",
                JsonSerializer.Serialize(new { op, slot = 1, revision }, ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(), "AI result " + op + ": " + doc.RootElement.ToString()[..Math.Min(200, doc.RootElement.ToString().Length)]);return doc.RootElement.GetProperty("data").Clone();
        }
        var ordinary = AiHost("ordinary");var batched = AiHost("batch");
        var ordinaryState = AiCall(ordinary, "load");var batchState = AiCall(batched, "load");
        ordinaryState = AiCall(ordinary, "ai", ordinaryState.GetProperty("revision").GetInt64());
        var batch = AiCall(batched, "ai_batch", batchState.GetProperty("revision").GetInt64());batchState = batch.GetProperty("state");
        Check(batch.GetProperty("steps").GetInt32() == 1 && batch.GetProperty("error").ValueKind == JsonValueKind.Null, "batch stops on first finished commit");
        Check(ordinaryState.GetProperty("outcome").GetString() == "Victory" && ordinaryState.GetProperty("battleResult").GetRawText() == batchState.GetProperty("battleResult").GetRawText(), "ordinary and skipped AI produce identical final result");
        Check(ordinaryState.GetProperty("medals").GetInt64() == batchState.GetProperty("medals").GetInt64(), "ordinary and skipped rewards equal");
        var normalAuto = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(profile, "ordinary/saves/mode-1/slot-0.json")), ClientGame.Json)!;
        var batchAuto = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(profile, "batch/saves/mode-1/slot-0.json")), ClientGame.Json)!;
        Check(normalAuto.Session.SequenceEqual(batchAuto.Session), "ordinary and batch durable result/RNG/session identical");

        var oldSave = JsonNode.Parse(SessionSave.Encode(basis))!;
        var oldPayload = oldSave["payload"]!.AsObject();
        foreach (string key in new[] { "battleId", "battleResult", "collectedMedals" }) oldPayload.Remove(key);
        foreach (string key in new[] { "campaignStars", "battleResults" }) oldPayload["account"]!.AsObject().Remove(key);
        var oldOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        oldSave["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(oldPayload.ToJsonString(oldOptions))));
        var legacy = SessionSave.Decode(Encoding.UTF8.GetBytes(oldSave.ToJsonString(oldOptions)), new(basis.ContentRevision, basis.World.ContentRevision, basis.Account.ContentRevision));
        Check(legacy.BattleId == "" && legacy.Account.BattleResults.Count == 0 && legacy.Account.CampaignStars.Count == 0, "old payload without settlement fields decodes");

        var owned = content.Catalogs.Accounts.FromDefinition(content.Catalogs.Runtime.Commanders.Keys.First(), "settlement.general") with { Items = [] };
        var generals = start.Session with { BattleId = "generals-result", Account = start.Session.Account with { Revision = 1000, Roster = [owned] },
            World = start.Session.World with { Phase = TurnPhase.Active, PendingDialogue = null } };
        var deployUnit = generals.World.Units.First(u => RosterDeployment.CanDeploy(generals, u.Id, owned.Id, start.Rules.Roster));
        generals = RosterDeployment.Deploy(generals, generals.Revision, deployUnit.Id, owned.Id, start.Rules.Roster).State;
        Slot(Finish(generals, source.Header[13]), 6);
        var generalView = Call("load", new { slot = 6 });
        Check(generalView.GetProperty("battleResult").GetProperty("generals").GetArrayLength() == 1, "deployed owned general projected");
        Check(generalView.GetProperty("battleResult").GetProperty("generals")[0].GetProperty("image").GetString()!.EndsWith(".png"), "original commander portrait key");

        foreach (var outcome in new[] { BattleOutcome.Victory, BattleOutcome.Defeat })
        {
            var terminal = Finish(basis with { BattleId = "terminal-dialogue-" + outcome }, 10, outcome);
            terminal = terminal with { World = terminal.World with { PendingDialogue = "terminal.first", QueuedDialogues = ["terminal.last"],
                Events = [new() { Id = "terminal.first", DialogueId = "terminal.first" }, new() { Id = "terminal.last", DialogueId = "terminal.last" }],
                TriggeredEvents = ["terminal.first", "terminal.last"] } };
            Slot(terminal, 3);
            var waiting = Call("load", new { slot = 3 });
            Check(waiting.GetProperty("battleResult").ValueKind == JsonValueKind.Null, "terminal dialogue delays " + outcome + " settlement");
            long beforeMedals = waiting.GetProperty("medals").GetInt64();
            var firstAck = Call("action", new { op = "dialogue", revision = waiting.GetProperty("revision").GetInt64() });
            Check(firstAck.GetProperty("dialogue").GetString() == "terminal.last"
                && firstAck.GetProperty("battleResult").ValueKind == JsonValueKind.Null, "terminal dialogue queue advances before " + outcome + " settlement");
            host = new ClientGame(content, profile);
            var savedAck = Call("load", new { mode = 1 });
            Check(savedAck.GetProperty("dialogue").GetString() == "terminal.last", "terminal dialogue acknowledgement autosaves");
            var lastAck = Call("action", new { op = "dialogue", revision = savedAck.GetProperty("revision").GetInt64() });
            Check(lastAck.GetProperty("dialogue").ValueKind == JsonValueKind.Null
                && lastAck.GetProperty("battleResult").GetProperty("outcome").GetString() == outcome.ToString(), "last terminal dialogue releases " + outcome + " settlement");
            Check(lastAck.GetProperty("round").GetInt32() == 10 && lastAck.GetProperty("phase").GetString() == "Finished"
                && lastAck.GetProperty("activeFaction").GetString() == waiting.GetProperty("activeFaction").GetString(), "terminal acknowledgement does not resume turns");
            long afterMedals = lastAck.GetProperty("medals").GetInt64();
            Check(outcome != BattleOutcome.Defeat || afterMedals == beforeMedals, "terminal defeat gives no victory award");
            using var rejected = JsonDocument.Parse(host.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "end", revision = lastAck.GetProperty("revision").GetInt64() }, ClientGame.Json)));
            Check(!rejected.RootElement.GetProperty("ok").GetBoolean(), "terminal gameplay stays blocked");
            host = new ClientGame(content, profile);
            var terminalRestore = Call("load", new { mode = 1 });
            Check(terminalRestore.GetProperty("dialogue").ValueKind == JsonValueKind.Null
                && terminalRestore.GetProperty("battleResult").GetProperty("id").GetString() == "terminal-dialogue-" + outcome
                && terminalRestore.GetProperty("medals").GetInt64() == afterMedals, "terminal settlement autosaves and reloads without duplicate reward");
        }

        File.WriteAllText(Path.Combine(folder, "completed-state.json"), moved.GetRawText());
        File.WriteAllText(Path.Combine(folder, "defeat-state.json"), lost.GetRawText());
        File.WriteAllText(Path.Combine(folder, "checks.json"), JsonSerializer.Serialize(new { checks, errors = 0, profile }, new JsonSerializerOptions { WriteIndented = true }));
        return checks;
    }
}
