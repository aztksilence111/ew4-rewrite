using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class PauseObjectivesChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Pause objectives: " + label); }
        string data = Path.Combine(root, "rewrite/data");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(data, path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl"));
        PauseObjectiveModel Project(WorldState world, VictoryRules rules, string player, int minimum = 50, int highest = 20) =>
            PauseObjectiveProjection.Build(world, rules, ["4"], player, minimum, highest, s => s, s => s);
        static IEnumerable<PauseObjectiveRow> Rows(PauseObjectiveRow row) => new[] { row }.Concat((row.Children ?? []).SelectMany(Rows));

        foreach (var scenario in content.Scenarios.Where(s => s.Mode is 1 or 2))
        {
            var country = scenario.Mode == 2 ? content.Countries.Canonical(content.Battle(scenario.Id).Factions.First(f => f.Camp != 4).CountryId) : null;
            var start = content.Start(scenario.Id, country); var world = start.Session.World; var rules = start.Rules.Turns.Victory;
            var btl = content.Battle(scenario.Id);
            var model = PauseObjectiveProjection.Build(world, rules, start.Rules.Turns.NeutralCamps, start.Session.PlayerFaction,
                btl.Header[12], btl.Header[13], s => s, s => s);
            Check(model.Win.Children?.Count == (rules.ObjectivesEnabled ? 2 : 1), scenario.Id + " default branches");
            for (int round = 1; round <= Math.Min(btl.Header[12] + 1, 1100); round++)
            {
                var matches = model.Ratings.Where(r => round >= r.Start && (!r.End.HasValue || round <= r.End)).ToArray();
                Check(round > rules.RoundLimit ? matches.Length == 0 : matches.Length == 1 && matches[0].Stars == BattleSettlement.Stars(round, btl.Header[12], btl.Header[13]), scenario.Id + " exact rating " + round);
            }
        }
        foreach (var (minimum, highest) in new[] { (50, 20), (22, 8), (3, 1), (1, 0), (5, 5), (3, 5), (999, 0) })
            foreach (int? limit in new int?[] { null, 1, 7, 50 })
                for (int round = 1; round <= 60; round++)
                {
                    var ratings = PauseObjectiveProjection.Ratings(round, minimum, highest, limit);
                    var band = ratings.SingleOrDefault(r => round >= r.Start && (!r.End.HasValue || round <= r.End));
                    Check(limit.HasValue && round > limit ? band == null : band?.Stars == BattleSettlement.Stars(round, minimum, highest), "rating boundary variants");
                    Check(ratings.All(r => r.Available == (!r.End.HasValue || round <= r.End)), "rating expiry");
                }
        var basis = content.Start("campaign1_01", null); var session = basis.Session; var source = session.World;
        string player = session.PlayerFaction;
        var custom = new ExtensionVictoryRules { Win = new() { Type = "all", Conditions = [new() { Type = "round_reached", Round = 4 },
            new() { Type = "any", Conditions = [new() { Type = "resources", Resources = new(999999, 0, 0) }, new() { Type = "script", Script = "rewrite/test.lua", Function = "win" }] }] },
            Lose = new() { Type = "round_reached", Round = 9 }, RoundLimit = 10, DescriptionKey = "Stage two" };
        var staged = source with { ScenarioVictory = custom, Round = 4 };
        var model2 = Project(staged, basis.Rules.Turns.Victory, player);
        Check(model2.Description == "Stage two" && model2.Win.Relation == "all" && model2.Win.Children![1].Relation == "any", "nested stage tree");
        Check(Rows(model2.Win).Single(r => r.Id == "win.1.1").Met == null, "script status remains unknown without execution");
        Check(model2.Limits.Any(r => r.Relation == "failure") && model2.Note.Contains("失败优先"), "failure tree and priority");
        Check(model2.Ratings.All(r => r.End <= 10), "dynamic round cap intersects ratings");
        Check(Project(staged with { ScenarioVictory = custom with { Priority = "victory", RoundLimit = null } }, basis.Rules.Turns.Victory, player).Note.Contains("胜利优先"), "priority update");
        Check(Project(source with { Round = 9 }, basis.Rules.Turns.Victory, player, 22, 8).Ratings.Single(r => r.Stars == 5).Available == false, "five star deadline expires after inclusive boundary");
        var before = JsonSerializer.Serialize(staged, ClientGame.Json);
        Project(staged, basis.Rules.Turns.Victory, player);
        Check(JsonSerializer.Serialize(staged, ClientGame.Json) == before, "observation preserves world and RNG");

        var local = content.Start("campaign1_01", null, localPlayers: [player]);
        var multiplayer = Project(local.Session.World, local.Rules.Turns.Victory, player);
        Check(multiplayer.Ratings.Count == 0 && multiplayer.Limits.All(r => r.Id != "limits.round"), "multiplayer has no single player rating or timeout");
        Check(multiplayer.Note.Contains("共享"), "campaign shares victory");
        var localStaged = Project(local.Session.World with { ScenarioVictory = custom }, local.Rules.Turns.Victory, player);
        Check(localStaged.Limits.All(r => r.Relation != "failure"), "multiplayer ignores single player loss tree");

        string folder = Path.Combine(root, "build/pause-objectives-20261025/check-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); var host = new ClientGame(content, folder);
        JsonElement Call(string route, object command)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(), route + ": " + doc.RootElement.ToString()[..Math.Min(180, doc.RootElement.ToString().Length)]);
            return doc.RootElement.GetProperty("data").Clone();
        }
        void Load(int round)
        {
            var world = source with { Round = round, Phase = TurnPhase.Active, ActiveFaction = player, PendingDialogue = null, Events = [], TriggeredEvents = [], ScenarioEvents = [] };
            File.WriteAllBytes(Path.Combine(folder, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(world.ScenarioId, null,
                SessionSave.Encode(session with { BattleId = "pause-cache", World = world })), ClientGame.Json));
            Call("load", new { slot = 1 });
        }
        Load(1); var first = Call("pause-objectives", new { }); string version = first.GetProperty("version").GetString()!;
        Check(first.GetProperty("snapshot").GetProperty("visible").GetBoolean(), "snapshot visible in campaign");
        var repeat = Call("pause-objectives", new { knownVersion = version });
        Check(repeat.GetProperty("snapshot").ValueKind == JsonValueKind.Null, "unchanged snapshot omitted");
        Load(2); var unchanged = Call("pause-objectives", new { knownVersion = version });
        Check(unchanged.GetProperty("version").GetString() == version && unchanged.GetProperty("snapshot").ValueKind == JsonValueKind.Null, "round without display changes preserves content version");
        Load(9); var changed = Call("pause-objectives", new { knownVersion = version });
        Check(changed.GetProperty("version").GetString() != version && changed.GetProperty("snapshot").ValueKind == JsonValueKind.Object, "crossing star boundary returns new snapshot");
        string fixtures = Path.Combine(root, "build/pause-objectives-20261025/fixtures");Directory.CreateDirectory(fixtures);
        foreach (var (slot, round) in new[] { (1, 1), (2, 2), (3, 9) })
        {
            var fixture = session with { BattleId = "pause-browser", World = source with { Round = round, Phase = TurnPhase.Active,
                ActiveFaction = player, PendingDialogue = null, Events = [], TriggeredEvents = [], ScenarioEvents = [] } };
            File.WriteAllBytes(Path.Combine(fixtures, "slot-" + slot + ".json"), JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot(source.ScenarioId, null, SessionSave.Encode(fixture)), ClientGame.Json));
        }
        Call("start", new { scenario = "campaign1_01" });
        Check(Call("pause-objectives", new { }).GetProperty("context").GetString() != first.GetProperty("context").GetString(), "new run context differs");

        var manifest = Read("client/manifest.json");
        PauseObjectivesPresentation.Validate(new Dictionary<string, PauseObjectivesPresentation> { ["*"] = new() }, manifest, Read);
        Check(true, "default presentation valid");
        void Reject(PauseObjectivesPresentation screen)
        {
            try { PauseObjectivesPresentation.Validate(new Dictionary<string, PauseObjectivesPresentation> { ["*"] = screen }, manifest, Read); }
            catch (RuleViolation e) { Check(e.Code == "extension.pause_objectives", "presentation rejected"); return; }
            throw new InvalidOperationException("Invalid pause presentation accepted");
        }
        Reject(new() { Width = 1 }); Reject(new() { Sections = ["win", "win", "ratings"] });
        Reject(new() { StarImage = "missing.png" }); Reject(new() { Background = "url(javascript:bad)" });
        Reject(new() { ConditionText = new() { ["unknown"] = "bad" } }); Reject(new() { EmptyStarImage = null! });
        var extension = JsonNode.Parse(Read("rewrite/extension.json"))!.AsObject();
        extension["pauseObjectivesPresentation"] = "rewrite/pause-screen-test.json";
        var screenBytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, PauseObjectivesPresentation> { ["*"] = new() { Title = "Test title", Width = 340 } }, ExtensionLoader.Json);
        byte[] ModRead(string path) => path == "rewrite/extension.json" ? JsonSerializer.SerializeToUtf8Bytes(extension)
            : path == "rewrite/pause-screen-test.json" ? screenBytes : Read(path);
        var configured = new ClientContent(ModRead, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl"));
        Check(configured.Extensions.PauseObjectivesPresentations["*"].Width == 340, "registered presentation loads through real extension entry");
        Check(configured.Extensions.Revision != content.Extensions.Revision, "presentation participates in extension fingerprint");
        return checks;
    }
}
