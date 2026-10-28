using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Mods;

internal static class AndroidPackageChecks
{
    internal static int Run(string root, string apk)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; }
        using var archive = ZipFile.OpenRead(apk);
        byte[] Read(string path)
        {
            string asset = path.StartsWith("client/", StringComparison.Ordinal) ? "www/content/" + path[7..]
                : path.StartsWith("legacy/", StringComparison.Ordinal) ? "data/" + path[7..]
                : path.StartsWith("frontend/", StringComparison.Ordinal) ? "www/" + path[9..] : path;
            var entry = archive.GetEntry("assets/" + asset) ?? throw new FileNotFoundException(asset);
            using var input = entry.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
        }
        var files = archive.Entries.Select(e => e.FullName).Where(p => p.StartsWith("assets/original/", StringComparison.Ordinal)
            || p.StartsWith("assets/www/content/", StringComparison.Ordinal) || p.StartsWith("assets/data/", StringComparison.Ordinal))
            .Select(p => p.StartsWith("assets/www/content/", StringComparison.Ordinal) ? "client/" + p[19..]
                : p.StartsWith("assets/data/", StringComparison.Ordinal) ? "legacy/" + p[12..] : p[7..])
            .Concat(["frontend/conquest-pages.json", "frontend/city-names.json"]);
        var baseline = new ModBase(Read, files);
        string run = Path.Combine(root, "build/android-startup-20261024/check-" + Guid.NewGuid().ToString("N"));
        string saves = Path.Combine(run, "saves");
        var store = new ModStore(baseline, Path.Combine(run, "mods"), saves);
        JsonNode Request(string route, string body = "{}")
        {
            var response = JsonNode.Parse(store.Dispatch("/api/" + route, body))!;
            Check((bool?)response["ok"] == true, route + ": " + response.ToJsonString());
            return response["data"]!;
        }
        Request("catalog");
        Request("headquarters");
        Request("start", "{\"scenario\":\"conquest1\",\"country\":\"fra\"}");
        var goals = Request("pause-objectives");
        Check((bool?)goals["snapshot"]?["visible"] == true && goals["snapshot"]?["model"]?["win"] != null,
            "packaged pause objectives display conquest victory conditions");
        string goalVersion = (string)goals["version"]!;
        var unchangedGoals = Request("pause-objectives", JsonSerializer.Serialize(new { knownVersion = goalVersion }));
        Check((string?)unchangedGoals["version"] == goalVersion && unchangedGoals["snapshot"] == null,
            "packaged pause objectives reuse unchanged content");
        var content = baseline.Content(Read, baseline.Scenarios);
        string profile = Path.Combine(saves, "account.json");
        var legacy = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(profile), ClientGame.Json)! with {
            ContentRevision = "e47fb936518323a29848404514de602e25bfc04bbc5fafa191485fa32c7d39e5",
            Revision = 42, Medals = 123456, Badges = 321,
            Roster = [content.Catalogs.Accounts.FromDefinition(content.Catalogs.Runtime.Commanders.Keys.Min(), "kept-commander")],
            Inventory = new Dictionary<string, long> { ["ew4.item.20"] = 7 },
            UnlockedCampaigns = ["campaign1_02"], CampaignStars = new Dictionary<string, int> { ["campaign1_01"] = 3 }
        };
        byte[] previous = JsonSerializer.SerializeToUtf8Bytes(legacy, ClientGame.Json);
        File.WriteAllBytes(profile, previous);
        store = new ModStore(baseline, Path.Combine(run, "mods"), saves);
        Request("catalog"); Request("headquarters");
        var migrated = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(profile), ClientGame.Json)!;
        Check(migrated.ContentRevision == content.Catalogs.Accounts.SourceRevision && migrated.Revision == 43, "known stock account migrates once");
        Check(JsonSerializer.Serialize(migrated with { ContentRevision = legacy.ContentRevision, Revision = legacy.Revision }, ClientGame.Json)
            == JsonSerializer.Serialize(legacy, ClientGame.Json), "migration preserves all account fields");
        Check(File.ReadAllBytes(profile + ".bak").SequenceEqual(previous), "migration backs up original account");
        store = new ModStore(baseline, Path.Combine(run, "mods"), saves);
        Request("catalog");
        Check(File.ReadAllBytes(profile).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(migrated, ClientGame.Json)), "reopening account is idempotent");
        var unknown = legacy with { ContentRevision = "unknown-rules" };
        File.WriteAllBytes(profile, JsonSerializer.SerializeToUtf8Bytes(unknown, ClientGame.Json));
        store = new ModStore(baseline, Path.Combine(run, "mods"), saves);
        Request("catalog"); Request("headquarters");
        var reused = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(profile), ClientGame.Json)!;
        Check(reused.ContentRevision == content.Catalogs.Accounts.SourceRevision
            && JsonSerializer.Serialize(reused with { ContentRevision = unknown.ContentRevision, Revision = unknown.Revision }, ClientGame.Json)
                == JsonSerializer.Serialize(unknown, ClientGame.Json), "compatible account with unknown fingerprint preserves all fields");
        var invalid = unknown with { Inventory = new Dictionary<string, long> { ["missing.item"] = 1 } };
        byte[] invalidBytes = JsonSerializer.SerializeToUtf8Bytes(invalid, ClientGame.Json);
        File.WriteAllBytes(profile, invalidBytes);
        store = new ModStore(baseline, Path.Combine(run, "mods"), saves);
        for (int retry = 0; retry < 2; retry++)
        {
            var rejected = JsonNode.Parse(store.Dispatch("/api/catalog", "{}"))!;
            Check((bool?)rejected["ok"] == false && (string?)rejected["code"] == "save.missing_definition",
                "account referencing a missing item remains rejected on retry");
            Check(File.ReadAllBytes(profile).SequenceEqual(invalidBytes), "rejection preserves the incompatible account");
        }
        var missing = new ModStore(new ModBase(p => p == "legacy/items.json" ? throw new FileNotFoundException(p) : Read(p), files),
            Path.Combine(run, "missing-mods"), Path.Combine(run, "missing-saves"));
        var failure = JsonNode.Parse(missing.Dispatch("/api/catalog", "{}"))!;
        Check((bool?)failure["ok"] == false && (string?)failure["code"] == "mod.failure"
            && ((string?)failure["message"])?.Contains("items.json", StringComparison.Ordinal) == true, "missing section reports exact path");
        return checks;
    }
}
