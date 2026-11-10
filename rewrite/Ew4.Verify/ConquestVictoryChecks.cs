using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class ConquestVictoryChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Conquest victories: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        string folder = Path.Combine(root, "build", "conquest-victories-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var host = new ClientGame(content, folder);
            JsonElement Call(string route, object command)
            {
                using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
                Check(doc.RootElement.GetProperty("ok").GetBoolean(), route + ": " + doc.RootElement);
                return doc.RootElement.GetProperty("data").Clone();
            }
            JsonElement Victories() => Call("catalog", new { }).GetProperty("conquestVictories");
            GameSession Finished(string scenario, string country, string run, BattleOutcome outcome = BattleOutcome.Victory)
            {
                var basis = content.Start(scenario, country).Session;
                return basis with { BattleId = run, World = basis.World with {
                    Round = 10, Phase = TurnPhase.Finished, Outcome = outcome, PendingDialogue = null, QueuedDialogues = [] } };
            }
            void Load(GameSession session, string country)
            {
                File.WriteAllBytes(Path.Combine(folder, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(
                    new ClientSlot(session.World.ScenarioId, country, SessionSave.Encode(session)), ClientGame.Json));
                Call("load", new { slot = 1 });
            }
            Check(!Victories().EnumerateObject().Any(), "fresh profile has no flags");
            var france = Finished("conquest1", "fra", "france");
            Load(france, "fra");
            Check(Victories().GetProperty("conquest1").EnumerateArray().Select(v => v.GetString()).SequenceEqual(["fra"]), "real load/settlement records winning player country");
            long medals = Call("action", new { op = "state" }).GetProperty("medals").GetInt64();
            Load(france, "fra");
            Check(Call("action", new { op = "state" }).GetProperty("medals").GetInt64() == medals, "old pre-settlement slot cannot pay twice");
            Load(Finished("conquest1", "fra", "france-again"), "fra");
            Load(Finished("conquest1", "gbr", "britain"), "gbr");
            Load(Finished("conquest2", "usa", "america"), "usa");
            Load(Finished("conquest2", "gbr", "defeat", BattleOutcome.Defeat), "gbr");
            Check(Victories().GetProperty("conquest1").EnumerateArray().Select(v => v.GetString()).SequenceEqual(["fra", "gbr"]), "repeated wins deduplicate and different countries accumulate");
            Check(Victories().GetProperty("conquest2").EnumerateArray().Select(v => v.GetString()).SequenceEqual(["usa"]), "scenario wins stay separate and defeats add no flag");
            host = new ClientGame(content, folder);
            Check(Victories().GetProperty("conquest1").GetArrayLength() == 2, "flags survive host restart without loading a battle");
            Check(!new ClientGame(content, Path.Combine(folder, "other-mod")).Dispatch("/api/catalog", "{}").Contains("\"conquest1\":["), "separate account namespace has no flags");

            var settled = BattleSettlement.Settle(france, 2, "conquest1", 100, 20, "fra");
            Check(settled.BattleResult!.Country == "fra", "country is persisted on settlement receipt");
            var decoded = SessionSave.Decode(SessionSave.Encode(settled), new(france.ContentRevision, france.World.ContentRevision, france.Account.ContentRevision));
            Check(decoded.Account.BattleResults["france"].Country == "fra", "country survives checksummed session round trip");
            var oldReceipt = settled.BattleResult with { Country = null };
            var oldAccount = settled.Account with { BattleResults = new Dictionary<string, BattleResult> { [oldReceipt.Id] = oldReceipt } };
            var migrated = BattleSettlement.Settle(france with { Account = oldAccount }, 2, "conquest1", 100, 20, "fra");
            Check(migrated.Account.BattleResults["france"].Country == "fra" && migrated.Account.Medals == oldAccount.Medals, "finished legacy receipt recovers country without awarding medals");
            var repeat = BattleSettlement.Settle(migrated, 2, "conquest1", 100, 20, "fra");
            Check(repeat.Account.Revision == migrated.Account.Revision, "country recovery is idempotent");
            var json = JsonSerializer.SerializeToNode(oldReceipt, ClientGame.Json)!;
            json.AsObject().Remove("country");
            Check(json.Deserialize<BattleResult>(ClientGame.Json)!.Country == null, "legacy receipt without country still loads");
            Check(BattleSettlement.Settle(france, 1, "campaign1_01", 100, 20, "fra").BattleResult!.Country == null, "campaign rewards do not create conquest flags");
            Check(BattleSettlement.Settle(france, 4, "", 100, 20, "fra").BattleResult!.Country == null, "local multiplayer rewards do not create conquest flags");
            Check(BattleSettlement.Settle(france with { World = france.World with { Outcome = BattleOutcome.Defeat } }, 2, "conquest1", 100, 20, "fra").BattleResult!.Country == null, "defeat receipt has no winning country");
            return checks;
        }
        finally { Directory.Delete(folder, true); }
    }
}
