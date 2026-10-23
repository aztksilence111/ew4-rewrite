using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;

internal static class FeedbackConfigurationChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string message) { checks++; if (!pass) throw new InvalidOperationException("Feedback configuration: " + message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e is RuleViolation or JsonException) { checks++; return; }
            throw new InvalidOperationException("Accepted invalid configuration: " + message);
        }
        var files = new Dictionary<string, byte[]>();
        byte[] Read(string path) => files.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(path.StartsWith("data/")
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        void Put(string path, string text) => files[path] = Encoding.UTF8.GetBytes(text);
        ClientContent Content() => new(Read, ["campaign1_01.btl", "conquest1.btl"]);
        string folder = Path.Combine(root, "build/rewrite-feedback-fix-20261022/configuration", Guid.NewGuid().ToString("N"));
        DateTimeOffset now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        var baseline = Content(); var host = new ClientGame(baseline, folder, () => now);
        JsonElement Call(string route, object? args = null, string? error = null)
        {
            using var document = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(args, ClientGame.Json)));
            var reply = document.RootElement;
            Check(reply.GetProperty("ok").GetBoolean() == (error == null), route + ": " + reply.GetRawText()[..Math.Min(200, reply.GetRawText().Length)]);
            if (error != null) Check(reply.GetProperty("code").GetString() == error, "error: " + error);
            return reply.GetProperty(error == null ? "data" : "code").Clone();
        }
        long Rev(JsonElement data) => data.GetProperty("revision").GetInt64();
        var hq = Call("headquarters");
        Check(hq.GetProperty("medals").GetInt64() == 20_000_000 && hq.GetProperty("badges").GetInt64() == 20_000_000, "builtin initial assets unchanged");
        for (int i = 0; i < 4; i++) hq = Call("commander", new { op = "refresh", group = 0, revision = Rev(hq) });
        Check(hq.GetProperty("refresh").GetProperty("0").GetProperty("remaining").ValueKind == JsonValueKind.Null, "builtin academy unlimited");
        var stock = Call("commerce", new { cell = -1 });
        Check(stock.GetProperty("offers").GetArrayLength() == 7 && stock.GetProperty("offers").EnumerateArray().Count(o => o.GetProperty("remaining").ValueKind != JsonValueKind.Null) == 5, "native stock defaults");
        for (int i = 0; i < 4; i++) stock = Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) });
        Check(stock.GetProperty("refresh").GetProperty("remaining").ValueKind == JsonValueKind.Null && stock.GetProperty("medals").GetInt64() == 20_000_000, "builtin shop unlimited and free");

        const string config = "rewrite/metagame.json", introConfig = "rewrite/stage-intro.json";
        const string manifest = "{\"schema\":1,\"apiVersion\":2,\"metagame\":\"rewrite/metagame.json\"}";
        Put("rewrite/extension.json", manifest);
        foreach (string invalid in new[] { "{\"schema\":2}", "{\"initialMedals\":-1}", "{\"initialBadges\":\"1\"}", "{\"refresh\":null}",
            "{\"commanderSectionCapacities\":null}", "{\"commanderSectionCapacities\":{\"3\":20}}",
            "{\"commanderSectionCapacities\":{\"0\":-1}}", "{\"commanderSectionCapacities\":{\"0\":null}}",
            "{\"commanderSectionCapacities\":{\"0\":1.5}}", "{\"commanderSectionCapacities\":{\"0\":\"20\"}}",
            "{\"shopSlots\":[null]}", "{\"shopSlots\":[{\"slot\":14}]}", "{\"shopSlots\":[{\"slot\":0},{\"slot\":0}]}",
            "{\"shopSlots\":[{\"slot\":0,\"items\":[]}]}", "{\"refresh\":{\"academy.3\":{\"limit\":1}}}",
            "{\"refresh\":{\"shop\":{\"limit\":-1}}}", "{\"refresh\":{\"shop\":{\"limit\":1,\"initial\":2}}}",
            "{\"refresh\":{\"shop\":{\"recovery\":{\"intervalSeconds\":60}}}}",
            "{\"refresh\":{\"shop\":{\"limit\":2,\"recovery\":{}}}}",
            "{\"refresh\":{\"shop\":{\"limit\":2,\"recovery\":{\"intervalSeconds\":60,\"victories\":1}}}}",
            "{\"refresh\":{\"shop\":{\"limit\":2,\"recovery\":{\"victories\":0}}}}", "{\"typo\":1}" })
        { Put(config, invalid); Reject(() => Content(), invalid); }
        Put(config, "{\"shopSlots\":[{\"slot\":0,\"items\":[\"missing\"]}]}");Reject(() => Content(), "unknown item");
        string[] consumables = baseline.Catalogs.Items.Items.Values.Where(i => i.Equipment.Consumable).Take(2).Select(i => i.Equipment.Id).ToArray();
        string tuned = JsonSerializer.Serialize(new MetagameRules { InitialMedals = 123456, InitialBadges = 789,
            ShopSlots = [new(0, Items: [consumables[0]], Refresh: false, Quantity: 2), new(1, Items: [consumables[1]], Quantity: 3)],
            Refresh = new Dictionary<string, RefreshPolicy> {
                ["shop"] = new(2, Recovery: new(IntervalSeconds: 60)), ["academy.0"] = new(1, Recovery: new(Victories: 2)),
                ["academy.1"] = new(0), ["academy.2"] = new() } }, ClientGame.Json);
        Put(config, tuned);var content = Content();string revision = content.Extensions.Revision;
        folder = Path.Combine(folder, "mod");host = new(content, folder, () => now);
        hq = Call("headquarters");
        Check(hq.GetProperty("medals").GetInt64() == 123456 && hq.GetProperty("badges").GetInt64() == 789, "mod initial assets");
        Check(hq.GetProperty("refresh").GetProperty("0").GetProperty("remaining").GetInt32() == 1, "initial academy draw is free");
        Call("commander", new { op = "refresh", group = 1, revision = Rev(hq) }, "account.refresh");
        stock = Call("commerce", new { cell = -1 });
        Check(stock.GetProperty("offers").GetArrayLength() == 2 && stock.GetProperty("offers")[1].GetProperty("remaining").GetInt64() == 3, "mod slots and quantities");
        Check(stock.GetProperty("refresh").GetProperty("remaining").GetInt32() == 2, "initial shop draw is free");
        stock = Call("commerce", new { cell = -1, op = "buy", offer = "headquarters.0", revision = Rev(stock) });
        stock = Call("commerce", new { cell = -1, op = "buy", offer = "headquarters.1", revision = Rev(stock) });
        long stale = Rev(stock);
        stock = Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) });
        Check(stock.GetProperty("offers")[0].GetProperty("remaining").GetInt64() == 1 && stock.GetProperty("offers")[1].GetProperty("remaining").GetInt64() == 3, "fixed stock preserves depletion, rotating stock replenishes");
        Call("commerce", new { cell = -1, op = "refresh", revision = stale }, "account.revision");
        stock = Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) });
        byte[] before = File.ReadAllBytes(Path.Combine(folder, "account.json"));
        Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) }, "account.refresh");
        Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "account.json"))), "rejected refresh is atomic");
        now = now.AddSeconds(59);host = new(content, folder, () => now);stock = Call("commerce", new { cell = -1 });
        Check(stock.GetProperty("refresh").GetProperty("remaining").GetInt32() == 0, "depleted uses survive restart");
        now = now.AddSeconds(1);stock = Call("commerce", new { cell = -1 });
        Check(stock.GetProperty("refresh").GetProperty("remaining").GetInt32() == 1, "elapsed interval recovers one use");
        stock = Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) });
        Check(stock.GetProperty("refresh").GetProperty("remaining").GetInt32() == 0, "recovery consumed once");
        now = now.AddDays(20);stock = Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) });
        Check(stock.GetProperty("refresh").GetProperty("remaining").GetInt32() == 1, "offline recovery caps at limit");
        now = now.AddSeconds(-10);stock = Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) });
        Call("commerce", new { cell = -1, op = "refresh", revision = Rev(stock) }, "account.refresh");
        hq = Call("headquarters");hq = Call("commander", new { op = "refresh", group = 0, revision = Rev(hq) });
        Call("commander", new { op = "refresh", group = 0, revision = Rev(hq) }, "account.refresh");
        var policy = content.Extensions.Metagame.Refresh["academy.0"];
        var balance = new RefreshBalance(0, now, 0);
        Check(RefreshUses.Recover(balance, policy, now, 1).Remaining == 0 && RefreshUses.Recover(balance, policy, now, 2).Remaining == 1, "victory condition requires two completed victories");
        var account = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Path.Combine(folder, "account.json")), ClientGame.Json)!;
        var receipts = new Dictionary<string, BattleResult>();
        for (int i = 0; i < 2; i++) receipts["win." + i] = new("win." + i, "campaign1_01", "Victory", 1, 5, 22, 8, 0, 0);
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account with { BattleResults = receipts }, ClientGame.Json));
        host = new(content, folder, () => now);hq = Call("headquarters");
        Check(hq.GetProperty("refresh").GetProperty("0").GetProperty("remaining").GetInt32() == 1, "persisted victory receipts recover academy uses");
        hq = Call("commander", new { op = "refresh", group = 0, revision = Rev(hq) });
        Call("commander", new { op = "refresh", group = 0, revision = Rev(hq) }, "account.refresh");
        Check(Call("headquarters").GetProperty("badges").GetInt64() == 789, "reopening never resets initial balances");

        Put("rewrite/extension.json", manifest[..^1] + ",\"stageIntroPresentation\":\"rewrite/stage-intro.json\",\"victoryRules\":\"rewrite/victory.json\"}");
        Put("rewrite/victory.json", "{\"campaign1_01\":{\"win\":{\"type\":\"round_reached\",\"round\":3},\"descriptionKey\":\"守到第三回合\"}}");
        Put(introConfig, "{\"*\":{\"title\":\"全局标题\"},\"campaign1_01\":{\"title\":\"任务标题\",\"description\":\"任务说明\",\"victoryText\":\"坚守\"}}");
        content = Content();host = new(content, folder, () => now);
        var battle = Call("start", new { scenario = "campaign1_01" });var intro = battle.GetProperty("stageIntro");
        Check(intro.GetProperty("title").GetString() == "任务标题" && intro.GetProperty("objectives").GetString() == "守到第三回合", "specific intro and actual victory description");
        Check(intro.GetProperty("presentation").GetProperty("victoryText").GetString() == "坚守", "goal text interface reaches client");
        files["rewrite/images/briefing.png"] = Read("client/ui/commanders1_hd/Napoleon.png");
        Put(introConfig, "{\"*\":{\"commander\":\"Napoleon\",\"content\":[{\"type\":\"text\",\"text\":\"说明\"},{\"type\":\"commander\",\"commander\":\"Napoleon\"},{\"type\":\"image\",\"image\":\"rewrite/images/briefing.png\",\"width\":240}]}}");
        var rich = Content();host = new(rich, Path.Combine(folder, "rich"), () => now);
        var richIntro = Call("start", new { scenario = "campaign1_01" }).GetProperty("stageIntro");
        Check(richIntro.GetProperty("commander").GetString() == "Napoleon", "top-level commander uses portrait atlas rather than commandermark");
        Check(richIntro.GetProperty("presentation").GetProperty("content").GetArrayLength() == 3 && richIntro.GetProperty("presentation").GetProperty("layout").ValueKind == JsonValueKind.Null,
            "ordered text, portrait and packaged image preserve native layout");
        Check(rich.Extensions.Revision != content.Extensions.Revision, "rich content included in revision");
        Check(content.Extensions.Revision != revision, "intro and objectives included in logic fingerprint");
        var started = content.Start("campaign1_01", null);
        Check(started.Rules.Turns.Victory.Custom?.Win.Round == 3, "configured goal reaches actual victory rules");
        Check(ExtensionVictory.Evaluate(started.Session.World with { Round = 3 }, started.Rules.Turns.PlayerCamp!, started.Rules.Turns.NeutralCamps, started.Rules.Turns.Victory.Custom!, content.Extensions.Bus.Scripts) == BattleOutcome.Victory, "configured goal can actually finish battle");
        Put(introConfig, "{\"*\":{\"title\":\"全局任务窗口\"}}");
        host = new(Content(), Path.Combine(folder, "global"), () => now);
        Check(Call("start", new { scenario = "campaign1_01" }).GetProperty("stageIntro").GetProperty("title").GetString() == "全局任务窗口", "global intro fallback");
        host = new(content, folder, () => now);Call("start", new { scenario = "campaign1_01" });
        Call("save", new { mode = 1, slot = 1 });host = new(content, folder, () => now);battle = Call("load", new { mode = 1, slot = 1 });
        Check(battle.GetProperty("stageIntro").GetProperty("objectives").GetString() == "守到第三回合", "intro and goal survive save/load");
        foreach (string invalid in new[] { "{\"missing\":{}}", "{\"*\":{\"bindings\":{\"unknown\":null}}}", "{\"*\":{\"bindings\":{\"description\":\"missing\"}}}", "{\"*\":{\"bindings\":{\"description\":\"text_desc\",\"objectives\":\"text_desc\"}}}",
            "{\"*\":{\"bindings\":{\"description\":\"tcmder\"}}}", "{\"*\":{\"commander\":\"missing\"}}", "{\"*\":{\"layout\":{\"attributes\":{\"w\":\"0\",\"h\":\"200\"}}}}" })
        { Put(introConfig, invalid);Reject(() => Content(), invalid); }
        Put(introConfig, "{}");Put("rewrite/extension.json", "{\"schema\":1,\"apiVersion\":1,\"stageIntroPresentation\":\"rewrite/stage-intro.json\"}");
        Put("rewrite/extension.json", manifest[..^1] + ",\"stageIntroPresentation\":\"rewrite/stage-intro.json\"}");
        foreach (string block in new[] { "null", "{\"type\":\"html\"}", "{\"type\":\"text\",\"image\":\"x\"}",
            "{\"type\":\"image\",\"image\":\"../x.png\"}", "{\"type\":\"image\",\"image\":\"https://x/a.png\"}",
            "{\"type\":\"image\",\"image\":\"rewrite/images/missing.png\"}", "{\"type\":\"commander\",\"commander\":\"missing\"}",
            "{\"type\":\"image\",\"image\":\"rewrite/images/briefing.png\",\"width\":0}" })
        { Put(introConfig, "{\"*\":{\"content\":[" + block + "]}}");Reject(() => Content(), "invalid content: " + block); }
        Put(introConfig, "{\"*\":{\"content\":[{\"type\":\"image\",\"image\":\"rewrite/images/briefing.png\"}]}}");
        files["rewrite/images/briefing.png"] = [0, 1];Reject(() => Content(), "invalid PNG bytes");
        Put(introConfig, "{}");Put("rewrite/extension.json", "{\"schema\":1,\"apiVersion\":1,\"stageIntroPresentation\":\"rewrite/stage-intro.json\"}");
        Reject(() => Content(), "intro requires API 2");
        Put("rewrite/extension.json", manifest.Replace("\"apiVersion\":2", "\"apiVersion\":1"));Reject(() => Content(), "metagame requires API 2");
        Put("rewrite/extension.json", manifest.Replace(config, "../metagame.json"));Reject(() => Content(), "metagame path escape");
        files.Clear();var unchanged = Content();
        Check(unchanged.Catalogs.Accounts.SourceRevision == baseline.Catalogs.Accounts.SourceRevision && unchanged.Extensions.Revision == baseline.Extensions.Revision, "builtin fingerprints unchanged");
        return checks;
    }
}
