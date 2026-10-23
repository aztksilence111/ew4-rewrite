using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class CampaignProgressChecks
{
    public static int Run(ClientContent content, string folder, string root)
    {
        int checks = 0;
        void Check(bool condition, string label) { checks++; if (!condition) throw new InvalidOperationException("Campaign progress: " + label); }
        Check(content.Presentations.Values.All(p => !p.Hidden), "builtin hidden campaigns visible on a fresh profile");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        content = new ClientContent(Read, content.Scenarios.Select(s => s.Id + ".btl"), showHiddenCampaigns: false);
        GameSession Cleared(string id)
        {
            var session = content.Start(id, null).Session;
            var source = content.Battle(id);
            var sites = source.Sites.Where(s => s.Marker == 2).Select(s => (int)s.Cell).ToHashSet();
            var units = source.Armies.Select((u, i) => (u, id: id + ".unit." + i)).Where(p => p.u.Objective == 2).Select(p => p.id).ToHashSet();
            return session with { World = session.World with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory, PendingDialogue = null,
                Cells = session.World.Cells.Select((c, i) => sites.Contains(i) ? c with { Owner = session.PlayerFaction } : c).ToArray(),
                Units = session.World.Units.Where(u => !units.Contains(u.Id)).ToArray() } };
        }
        var hidden = content.Presentations.Values.Where(p => p.Hidden).ToArray();
        Check(hidden.Length > 0, "original hidden-stage metadata imported");
        var records = new List<object>();
        var reached = new HashSet<string>();
        foreach (var entry in content.Presentations.Values.Where(p => p.Chapter > 0))
            foreach (string id in new[] { entry.Id, entry.Alternate }.Where(id => id != null).Cast<string>())
            {
                var started = content.Start(id, null).Session;
                var cleared = Cleared(id);
                Check(CampaignProgress.RemainingYellow(content, cleared.World) == 0, "capture/death clears yellow count " + id);
                Check(CampaignProgress.Settle(content, started).UnlockedCampaigns.Count == 0, "no mid-battle unlock " + id);
                Check(CampaignProgress.Settle(content, cleared with { World = cleared.World with { Outcome = BattleOutcome.Defeat } }).UnlockedCampaigns.Count == 0, "defeat never unlocks " + id);
                var account = CampaignProgress.Settle(content, cleared);
                foreach (string next in account.UnlockedCampaigns) reached.Add(next);
                Check(CampaignProgress.Settle(content, cleared with { Account = account }).Revision == account.Revision, "victory settlement is idempotent " + id);
                int remaining = CampaignProgress.RemainingYellow(content, started.World);
                if (remaining > 0)
                    Check(CampaignProgress.Settle(content, started with { World = started.World with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory } }).UnlockedCampaigns.Count == 0,
                        "remaining yellow blocks unlock even on victory " + id);
                records.Add(new { id, remaining, unlocked = account.UnlockedCampaigns });
            }
        Check(hidden.All(p => reached.Contains(p.Id)), "every hidden stage has a verified original predecessor/branch");
        Check(CampaignProgress.Settle(content, Cleared("campaign4_10")).UnlockedCampaigns.SequenceEqual(new[] { "campaign4_11" }), "Turkish branch unlocks Turkish hidden stage");
        Check(CampaignProgress.Settle(content, Cleared("campaign4_10b")).UnlockedCampaigns.SequenceEqual(new[] { "campaign4_12" }), "Russian branch unlocks Russian hidden stage");

        string saveRoot = Path.Combine(folder, "campaign-progress");
        Directory.CreateDirectory(saveRoot);
        var host = new ClientGame(content, saveRoot);
        JsonElement Call(string route, object command, bool success = true)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean() == success, "host " + route + ": " + doc.RootElement.GetRawText()[..Math.Min(160, doc.RootElement.GetRawText().Length)]);
            return doc.RootElement.GetProperty(success ? "data" : "code").Clone();
        }
        Check(Call("start", new { scenario = "campaign1_05" }, false).GetString() == "campaign.locked", "direct API cannot enter locked hidden stage");
        var finished = Cleared("campaign1_04");
        Check(CampaignProgress.Settle(content, finished).UnlockedCampaigns.Contains("campaign1_05"), "real yellow predecessor unlocks hidden stage");
        void Slot(GameSession session) => File.WriteAllBytes(Path.Combine(saveRoot, "session.json"),
            JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(session.World.ScenarioId, null, SessionSave.Encode(session)), ClientGame.Json));
        Slot(finished);
        Call("load", new { });
        Check(Call("catalog", new { }).GetProperty("unlockedCampaigns").EnumerateArray().Any(id => id.GetString() == "campaign1_05"), "loaded victory settles and publishes unlock");
        host = new ClientGame(content, saveRoot);
        Check(Call("catalog", new { }).GetProperty("unlockedCampaigns").GetArrayLength() == 1, "unlock survives host restart");
        Slot(content.Start("campaign1_01", null).Session);
        Call("load", new { });
        Check(Call("catalog", new { }).GetProperty("unlockedCampaigns").GetArrayLength() == 1, "older save cannot regress campaign unlocks");
        Call("start", new { scenario = "campaign1_05" });
        Call("save", new { });
        Call("load", new { });

        string browserProfile = Path.Combine(root, "rewrite/reports/objective_markers_20261019/test-profile");
        Directory.CreateDirectory(Path.Combine(browserProfile, "saves", "mode-1"));
        File.WriteAllBytes(Path.Combine(browserProfile, "saves", "mode-1", "slot-6.json"),
            JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(finished.World.ScenarioId, null, SessionSave.Encode(finished)), ClientGame.Json));
        File.WriteAllText(Path.Combine(root, "rewrite/reports/objective_markers_20261019/campaign-checks.json"),
            JsonSerializer.Serialize(new { checks, errors = 0, hidden = hidden.Select(p => p.Id), records }, new JsonSerializerOptions { WriteIndented = true }));
        return checks;
    }
}
