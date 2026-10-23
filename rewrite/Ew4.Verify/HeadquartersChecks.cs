using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;

internal static class HeadquartersChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Headquarters: " + label); }
        string assets = Path.Combine(root, "rewrite/data/original");
        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string path) => overrides.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(path.StartsWith("original/", StringComparison.Ordinal)
            ? Path.Combine(assets, path[9..]) : Path.Combine(root, "rewrite/data/legacy", path[5..]));
        var content = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        string folder = Path.Combine(root, "rewrite/reports/headquarters-20261021", "run-" + Guid.NewGuid().ToString("N"));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object? command = null, string? error = null)
        {
            using var result = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            var response = result.RootElement;
            Check(response.GetProperty("ok").GetBoolean() == (error == null), route + ": " + response.GetRawText()[..Math.Min(150, response.GetRawText().Length)]);
            if (error != null) Check(response.GetProperty("code").GetString() == error, error);
            if (error == "account.capacity") Check(response.GetProperty("message").GetString() == "该星级区将领已达上限，无法购买", "localized capacity message");
            return response.GetProperty(error == null ? "data" : "code").Clone();
        }
        var hq = Call("headquarters");
        Check(hq.GetProperty("sectionCapacity").GetInt32() == 20, "20 slots per section");
        Check(hq.GetProperty("sectionCapacities").EnumerateArray().All(c => c.GetInt32() == 20), "default limits exposed for all sections");
        var offer = hq.GetProperty("offers").GetProperty("0")[0].GetString();
        hq = Call("commander", new { op = "recruit", revision = 0, commander = offer });
        string id = hq.GetProperty("roster")[0].GetProperty("id").GetString()!;
        hq = Call("commander", new { op = "tag", revision = 1, commander = id, tag = "  主力  " });
        Check(hq.GetProperty("roster")[0].GetProperty("tag").GetString() == "主力", "trimmed tag saved");
        long medals = hq.GetProperty("medals").GetInt64();
        Call("commander", new { op = "tag", revision = 1, commander = id, tag = "旧请求" }, "account.revision");
        Call("commander", new { op = "tag", revision = 2, commander = id, tag = new string('a', 13) }, "account.tag");
        Call("commander", new { op = "tag", revision = 2, commander = id, tag = "a\nb" }, "account.tag");
        Call("commander", new { op = "tag", revision = 2, commander = "missing", tag = "主力" }, "account.commander");
        host = new ClientGame(content, folder);
        Check(Call("headquarters").GetRawText() == hq.GetRawText(), "tag and account survive host restart");
        var battle = Call("start", new { scenario = "conquest1", country = "fra" });
        string unit = battle.GetProperty("units").EnumerateArray().First(u => u.GetProperty("owner").GetString() == battle.GetProperty("playerFaction").GetString()
            && u.GetProperty("commanderId").ValueKind == JsonValueKind.Null).GetProperty("id").GetString()!;
        battle = Call("action", new { op = "deploy", revision = battle.GetProperty("revision").GetInt64(), unit, commander = id });
        hq = Call("commander", new { op = "tag", revision = 2, commander = id, tag = "最爱" });
        Check(hq.GetProperty("roster")[0].GetProperty("locked").GetBoolean(), "tagging keeps deployment lock");
        Check(hq.GetProperty("medals").GetInt64() == medals, "tagging is free");
        Call("save", new { slot = 1, mode = 2 });
        Call("load", new { slot = 1, mode = 2 });
        Check(Call("headquarters").GetProperty("roster")[0].GetProperty("tag").GetString() == "最爱", "tag survives battle save and load");
        hq = Call("commander", new { op = "tag", revision = 3, commander = id, tag = "" });
        Check(hq.GetProperty("roster")[0].GetProperty("tag").GetString() == "", "tag can be removed");

        hq = Call("commander", new { op = "tags", revision = 4, taggedCommanders = new[] { id } });
        Check(hq.GetProperty("roster")[0].GetProperty("tag").GetString() == "置顶", "mode selection assigns a tag");
        byte[] beforeTags = File.ReadAllBytes(Path.Combine(folder, "account.json"));
        Call("commander", new { op = "tags", revision = 5, taggedCommanders = new[] { id, "missing" } }, "account.tag");
        Call("commander", new { op = "tags", revision = 5, taggedCommanders = new[] { id, id } }, "account.tag");
        Call("commander", new { op = "tags", revision = 5 }, "account.tag");
        Call("commander", new { op = "tags", revision = 4, taggedCommanders = Array.Empty<string>() }, "account.revision");
        Check(beforeTags.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "account.json"))), "invalid batch selections never partially save");
        string secondDefinition = hq.GetProperty("offers").GetProperty("0")[0].GetString()!;
        hq = Call("commander", new { op = "recruit", revision = 5, commander = secondDefinition });
        string secondId = hq.GetProperty("roster")[1].GetProperty("id").GetString()!;
        long beforeMedals = hq.GetProperty("medals").GetInt64();
        hq = Call("commander", new { op = "tags", revision = 6, taggedCommanders = new[] { secondId } });
        Check(hq.GetProperty("roster")[0].GetProperty("tag").GetString() == ""
            && hq.GetProperty("roster")[1].GetProperty("tag").GetString() == "置顶", "batch removes and adds tags together");
        Check(hq.GetProperty("revision").GetInt64() == 7 && hq.GetProperty("medals").GetInt64() == beforeMedals, "batch costs nothing and advances only one revision");
        host = new ClientGame(content, folder);
        var restartedTags = Call("headquarters");
        Check(restartedTags.GetProperty("revision").GetInt64() == 7
            && restartedTags.GetProperty("roster")[1].GetProperty("tag").GetString() == "置顶", "batch selection survives restart");
        hq = Call("commander", new { op = "tags", revision = 7, taggedCommanders = Array.Empty<string>() });
        Check(hq.GetProperty("roster").EnumerateArray().All(c => c.GetProperty("tag").GetString() == ""), "empty selection clears all tags");

        // Existing profiles can omit the new optional field.
        string profile = Path.Combine(folder, "account.json");
        var account = JsonNode.Parse(File.ReadAllText(profile))!;
        account["roster"]![0]!.AsObject().Remove("tag");
        File.WriteAllText(profile, account.ToJsonString());
        host = new ClientGame(content, folder);
        Check(Call("headquarters").GetProperty("roster")[0].GetProperty("tag").GetString() == "", "old profile defaults to untagged");

        var definitions = hq.GetProperty("definitions").EnumerateArray().ToArray();
        for (int group = 0; group < 3; group++)
        {
            var candidates = definitions.Where(d => d.GetProperty("recruitable").GetBoolean()
                && Math.Clamp((d.GetProperty("rating").GetInt32() - 1) / 3, 0, 2) == group
                && (d.GetProperty("price").GetInt64() > 0 || d.GetProperty("badgePrice").GetInt64() > 0)).ToArray();
            Check(candidates.Length > 1, "recruitable candidates in section " + group);
            var baseline = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllText(profile), ClientGame.Json)!;
            var template = content.Catalogs.Accounts.FromDefinition(candidates[0].GetProperty("legacyId").GetInt32(), "template");
            var roster = Enumerable.Range(0, 19).Select(i => template.Copy() with { Id = "capacity." + i }).ToArray();
            var inventory = roster.SelectMany(c => c.Items).GroupBy(x => x).ToDictionary(g => g.Key, g => g.LongCount());
            string recruit = candidates[1].GetProperty("id").GetString()!;
            var seeded = baseline with { Revision = 0, Roster = roster, Inventory = inventory,
                CommanderOffers = new Dictionary<int, IReadOnlyList<string>> { [group] = new[] { recruit } } };
            File.WriteAllText(profile, JsonSerializer.Serialize(seeded, ClientGame.Json));
            host = new ClientGame(content, folder);
            hq = Call("commander", new { op = candidates[1].GetProperty("price").GetInt64() > 0 ? "recruit" : "recruit_badge", revision = 0, commander = recruit, group });
            Check(hq.GetProperty("roster").GetArrayLength() == 20, "twentieth commander accepted, section " + group);
            // Retry another offer at capacity through both currencies; failures preserve exact bytes.
            var full = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllText(profile), ClientGame.Json)!;
            // Use a distinct definition so duplicate-ownership validation does not mask capacity.
            var spare = definitions.First(d => Math.Clamp((d.GetProperty("rating").GetInt32() - 1) / 3, 0, 2) == group && !full.Roster.Any(c => c.DefinitionId == d.GetProperty("id").GetString())
                && d.GetProperty("recruitable").GetBoolean() && (d.GetProperty("price").GetInt64() > 0 || d.GetProperty("badgePrice").GetInt64() > 0));
            recruit = spare.GetProperty("id").GetString()!;
            full = full with { CommanderOffers = new Dictionary<int, IReadOnlyList<string>> { [group] = new[] { recruit } } };
            File.WriteAllText(profile, JsonSerializer.Serialize(full, ClientGame.Json));
            host = new ClientGame(content, folder);
            byte[] before = File.ReadAllBytes(profile);
            foreach (string op in new[] { "recruit", "recruit_badge" }.Where(op => spare.GetProperty(op == "recruit" ? "price" : "badgePrice").GetInt64() > 0))
                Call("commander", new { op, revision = 1, commander = recruit, group }, "account.capacity");
            var princess = hq.GetProperty("princesses").EnumerateArray().FirstOrDefault(p => !p.GetProperty("claimed").GetBoolean()
                && definitions.Any(d => d.GetProperty("id").GetString() == p.GetProperty("definitionId").GetString()
                    && Math.Clamp((d.GetProperty("rating").GetInt32() - 1) / 3, 0, 2) == group));
            if (princess.ValueKind != JsonValueKind.Undefined)
                Call("commander", new { op = "claim", revision = 1, offer = princess.GetProperty("id").GetString() }, "account.capacity");
            Check(before.SequenceEqual(File.ReadAllBytes(profile)), "capacity failures do not mutate account, section " + group);
            int otherGroup = (group + 1) % 3;
            var other = definitions.First(d => d.GetProperty("recruitable").GetBoolean()
                && Math.Clamp((d.GetProperty("rating").GetInt32() - 1) / 3, 0, 2) == otherGroup
                && (d.GetProperty("price").GetInt64() > 0 || d.GetProperty("badgePrice").GetInt64() > 0));
            string otherId = other.GetProperty("id").GetString()!;
            full = full with { CommanderOffers = new Dictionary<int, IReadOnlyList<string>> { [otherGroup] = new[] { otherId } } };
            File.WriteAllText(profile, JsonSerializer.Serialize(full, ClientGame.Json));
            host = new ClientGame(content, folder);
            hq = Call("commander", new { op = other.GetProperty("price").GetInt64() > 0 ? "recruit" : "recruit_badge", revision = 1, commander = otherId, group = otherGroup });
            Check(hq.GetProperty("roster").GetArrayLength() == 21, "full section does not block another section " + group);
        }
        overrides["rewrite/extension.json"] = System.Text.Encoding.UTF8.GetBytes(
            "{\"schema\":1,\"apiVersion\":2,\"metagame\":\"rewrite/metagame.json\"}");
        overrides["rewrite/metagame.json"] = System.Text.Encoding.UTF8.GetBytes(
            "{\"commanderSectionCapacities\":{\"0\":0,\"1\":1,\"2\":25}}");
        var configured = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        Check(configured.Extensions.Revision != content.Extensions.Revision, "limits participate in extension fingerprint");
        int[] limits = [0, 1, 25];
        for (int group = 0; group < 3; group++)
        {
            var candidates = definitions.Where(d => d.GetProperty("recruitable").GetBoolean()
                && Math.Clamp((d.GetProperty("rating").GetInt32() - 1) / 3, 0, 2) == group).Take(3).ToArray();
            var template = configured.Catalogs.Accounts.FromDefinition(candidates[0].GetProperty("legacyId").GetInt32(), "template");
            var roster = Enumerable.Range(0, Math.Max(0, limits[group] - 1)).Select(i => template.Copy() with { Id = "mod.capacity." + i }).ToArray();
            var baseline = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllText(profile), ClientGame.Json)!;
            string offerId = candidates[1].GetProperty("id").GetString()!;
            var seeded = baseline with { ContentRevision = configured.Catalogs.Accounts.SourceRevision, Revision = 0, Roster = roster,
                Inventory = roster.SelectMany(c => c.Items).GroupBy(x => x).ToDictionary(g => g.Key, g => g.LongCount()),
                CommanderOffers = new Dictionary<int, IReadOnlyList<string>> { [group] = new[] { offerId, candidates[2].GetProperty("id").GetString()! } } };
            File.WriteAllText(profile, JsonSerializer.Serialize(seeded, ClientGame.Json));
            host = new ClientGame(configured, folder);
            hq = Call("headquarters");
            Check(hq.GetProperty("sectionCapacities").EnumerateArray().Select(c => c.GetInt32()).SequenceEqual(limits), "configured limits reach client");
            if (limits[group] > 0)
            {
                hq = Call("commander", new { op = "recruit_badge", revision = 0, commander = offerId, group });
                Check(hq.GetProperty("roster").GetArrayLength() == limits[group], "last configured slot accepted in section " + group);
                offerId = candidates[2].GetProperty("id").GetString()!;
            }
            byte[] before = File.ReadAllBytes(profile);
            foreach (string op in new[] { "recruit", "recruit_badge" }.Where(op => candidates[2].GetProperty(op == "recruit" ? "price" : "badgePrice").GetInt64() > 0))
                Call("commander", new { op, revision = hq.GetProperty("revision").GetInt64(), commander = offerId, group }, "account.capacity");
            Check(before.SequenceEqual(File.ReadAllBytes(profile)), "configured capacity failure preserves account bytes in section " + group);
            host = new ClientGame(configured, folder);
            Check(Call("headquarters").GetProperty("roster").GetArrayLength() == limits[group], "configured roster survives restart");
        }
        overrides["rewrite/metagame.json"] = System.Text.Encoding.UTF8.GetBytes("{\"commanderSectionCapacities\":{\"1\":7}}");
        var sparse = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        Check(sparse.Extensions.Metagame.CommanderSectionCapacity(0) == 20 && sparse.Extensions.Metagame.CommanderSectionCapacity(1) == 7
            && sparse.Extensions.Metagame.CommanderSectionCapacity(2) == 20, "omitted sections retain defaults");
        Check(sparse.Extensions.Revision != configured.Extensions.Revision, "changed limits change logical fingerprint");
        return checks;
    }
}
