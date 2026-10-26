using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;

internal static class CampaignTechnologyChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Campaign technology: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, label: code); return; }
            throw new InvalidOperationException("Expected rejection " + code);
        }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.GetFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var tech = content.Catalogs.Technology;
        var seed = new PlayerAccount { Id = "local", ContentRevision = content.Catalogs.Accounts.SourceRevision, TechnologyStars = 999 };
        // Independently recovered from the original ARM64 price table at 0x1910d0.
        int[][] costs = [
            [0,3,6,12], [1,4,7,13], [2,5,8,14], [3,6,9,15], [4,7,10,16], [8,9,12,18],
            [0,4,8,15], [3,6,10,17], [6,8,12,19], [9,10,14,21],
            [0,5,10,18], [4,7,12,20], [7,9,14,22], [10,11,16,24],
            [0,6,12,21], [5,8,14,23], [8,10,16,25], [11,12,18,27],
            [0,12,18,22], [6,15,20,25], [12,20,25,30], [0,10,16,20],
            [0,8,16,24], [0,15,25,35], [0,12,20,30], [0,8,12,16]
        ];
        for (int slot = 0; slot < 26; slot++)
        {
            for (int level = -1; level < 3; level++)
                Check(tech.UpgradeCost(slot, level) == costs[slot][level + 1], "real native price table");
            foreach (int zone in tech.Defaults.Keys)
            {
                int level = tech.Snapshot(seed, zone)[slot];
                var account = seed;
                while (level < 3)
                {
                    var before = account; int cost = tech.UpgradeCost(slot, level)!.Value;
                    account = tech.Upgrade(account, account.Revision, zone, slot);
                    Check(tech.Snapshot(account, zone)[slot] == ++level && account.TechnologyStars == before.TechnologyStars - cost,
                        "exact native level/payment result");
                    Check(tech.Snapshot(before, zone)[slot] == level - 1, "upgrade does not mutate input");
                    Check(tech.Snapshot(account, (zone + 1) % 6).SequenceEqual(tech.Snapshot(seed, (zone + 1) % 6)), "warzone isolation");
                    Check(account.Medals == before.Medals && account.Badges == before.Badges, "separate currency");
                    Check(tech.Snapshot(account.Copy(), zone).SequenceEqual(tech.Snapshot(account, zone)), "deep copy retains technology");
                }
                Reject(() => tech.Upgrade(account, account.Revision, zone, slot), "technology.maximum");
            }
        }
        Reject(() => tech.Upgrade(seed, -1, 0, 0), "technology.stale");
        Reject(() => tech.Upgrade(seed, 0, 0, 26), "technology.slot");
        Reject(() => tech.Upgrade(seed, 0, 999, 0), "legacy.technology");
        Reject(() => tech.Upgrade(seed with { TechnologyStars = 0 }, 0, 0, 0), "technology.stars");
        var legacy = seed with { TechnologyStars = null, CampaignStars = new Dictionary<string, int> { ["campaign1_01"] = 3 } };
        Check(CampaignTechnologyStars.Balance(legacy) == 3, "old account recovers earned stars");
        Check(CampaignTechnologyStars.Award(legacy, 5, 3) == 5 && CampaignTechnologyStars.Award(legacy, 2, 3) == 3, "only best-star improvement rewarded");
        Check(CampaignTechnologyStars.Award(seed, 5, 0) == 999, "native star balance cap");
        var scenario = content.Start("campaign1_01", null, legacy);
        var finished = scenario.Session with { BattleId = "technology-check", World = scenario.Session.World with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory, PendingDialogue = null } };
        var settled = BattleSettlement.Settle(finished, 1, "campaign1_01", 100, 100);
        Check(settled.Account.TechnologyStars == 5, "settlement credits star difference");
        Check(BattleSettlement.Settle(settled, 1, "campaign1_01", 100, 100).Account.TechnologyStars == 5, "receipt prevents double payout");
        var upgraded = tech.Upgrade(seed, 0, 0, 0);
        var upgradedScenario = content.Start("campaign1_01", null, upgraded);
        Check(upgradedScenario.TechnologySnapshot![0] == 2 && scenario.TechnologySnapshot![0] == 1, "client startup consumes account technology");
        var player = upgradedScenario.Session.World.Factions.Single(f => f.Id == upgradedScenario.Session.PlayerFaction);
        Check(player.Recruitment["ew4.card.0"].Veterancy == "2", "upgraded technology reaches actual recruitment grant");
        Check(content.Start("tutorials1", null, upgraded).TechnologySnapshot == null && content.Start("conquest1", "fra", upgraded).TechnologySnapshot == null,
            "tutorial/conquest ignore campaign technology");
        var logistics = seed;
        for (int slot = 22; slot < 26; slot++)
            while (tech.Snapshot(logistics, 0)[slot] < 3) logistics = tech.Upgrade(logistics, logistics.Revision, 0, slot);
        var logisticsStart = content.Start("campaign1_01", null, logistics);
        var logisticsPlayer = logisticsStart.Session.World.Factions.Single(f => f.Id == logisticsStart.Session.PlayerFaction);
        Check(logisticsPlayer.IncomeBonus == new Resources(60, 30, 30), "account upgrades reach all economy bonuses");
        Check(logisticsPlayer.Recruitment["ew4.card.0"].Transport
            && logisticsPlayer.Recruitment["ew4.card.18"].Transport
            && logisticsPlayer.Recruitment["ew4.card.30"].Transport, "account transport upgrade reaches all land categories");
        Reject(() => (seed with { TechnologyStars = -1 }).Validate(), "account.technology");

        string folder = Path.Combine(root, "build/campaign-technology-20261026/checks");
        Directory.CreateDirectory(folder);
        var old = JsonSerializer.SerializeToNode(legacy with { CampaignStars = Enumerable.Range(1, 8).ToDictionary(i => "campaign1_" + i.ToString("00"), _ => 5) }, ClientGame.Json)!;
        old.AsObject().Remove("technologyStars"); old.AsObject().Remove("campaignTechnology");
        File.WriteAllText(Path.Combine(folder, "account.json"), old.ToJsonString());
        ClientGame host = new(content, folder);
        JsonElement Call(string route, object? command = null, string? failure = null)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command ?? new { }, ClientGame.Json)));
            var json = response.RootElement;
            Check(json.GetProperty("ok").GetBoolean() == (failure == null), route + ": " + json.ToString()[..Math.Min(120, json.ToString().Length)]);
            if (failure != null) Check(json.GetProperty("code").GetString() == failure, "API rejection " + failure);
            return json.GetProperty(failure == null ? "data" : "code").Clone();
        }
        var view = Call("technology");Check(view.GetProperty("stars").GetInt32() == 40, "old JSON migration earns original best stars once");
        Check(view.GetProperty("entries").GetArrayLength() == 22, "only original visible unit technologies exposed");
        Call("technology", new { op = "upgrade", group = 0, slot = 25, revision = view.GetProperty("revision").GetInt64() }, "technology.slot");
        Call("start", new { scenario = "campaign1_01" }); Call("save", new { slot = 1, mode = 1 });
        var revision = view.GetProperty("revision").GetInt64();
        view = Call("technology", new { op = "upgrade", group = 0, slot = 0, revision });
        Check(view.GetProperty("stars").GetInt32() == 34, "API payment");
        Call("technology", new { op = "upgrade", group = 0, slot = 0, revision }, "technology.stale");
        Call("start", new { scenario = "campaign1_01" }); Call("save", new { slot = 2, mode = 1 });
        revision = view.GetProperty("revision").GetInt64();
        view = Call("technology", new { op = "upgrade", group = 0, slot = 0, revision });
        Check(view.GetProperty("stars").GetInt32() == 22, "second level payment");
        host = new(content, folder);
        Check(Call("technology").GetProperty("stars").GetInt32() == 22, "restart preserves payment");
        Call("load", new { slot = 2, mode = 1 });
        Check(Call("technology").GetProperty("stars").GetInt32() == 22, "older battle snapshot loads without account rollback");
        Call("load", new { slot = 1, mode = 1 });
        Check(Call("technology").GetProperty("entries")[0].GetProperty("level").GetInt32() == 3, "default-tech save cannot roll back upgrade");
        // An old slot has no outer snapshot; its authenticated content was built from defaults.
        string slotPath = Path.Combine(folder, "saves/mode-1/slot-1.json");
        var slotJson = JsonNode.Parse(File.ReadAllText(slotPath))!;slotJson.AsObject().Remove("technologySnapshot");File.WriteAllText(slotPath, slotJson.ToJsonString());
        host = new(content, folder);Call("load", new { slot = 1, mode = 1 });
        Check(Call("technology").GetProperty("stars").GetInt32() == 22, "legacy slot remains readable");
        Call("technology", new { op = "invalid" }, "technology.command");
        return checks;
    }
}
