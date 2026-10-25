using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class ObjectiveChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Objectives: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code); return; }
            throw new InvalidOperationException("Expected objective rejection: " + code);
        }
        var objectives = new MetagameObjective[] {
            new("owned", "achievement", "Owned", "Inventory and roster", [new("commanderOwned"), new("itemOwned", Id: "ew4.item.0")], new(25, 2)),
            new("hidden", "achievement", "Hidden", "Unlock", [new("campaignUnlocked", Id: "battle1")], new(1)),
            new("capture", "task", "Capture", "Capture two cities", [new("citiesCaptured", 2)], new(10), Mode: 2, Scenario: "conquest1", Country: "fra"),
            new("kill", "achievement", "Kills", "Lifetime", [new("unitsDestroyed", 5, Scope: "lifetime")], new(0, 3), Mode: 2, Country: "fra"),
            new("control", "task", "Control", "Specific place", [new("cellControlled", Cell: 0)], new(), Scenario: "conquest1"),
            new("cities", "achievement", "Cities", "Held cities", [new("citiesControlled", 2)], new(), Mode: 2),
            new("win", "task", "Victory", "Good victory", [new("battleWon"), new("starsAtLeast", 4), new("roundAtMost", 20)], new(5), Mode: 2),
            new("any", "achievement", "Any", "Alternative", [new("itemOwned", 100), new("campaignStars", 3)], new(), Any: true),
            new("event", "achievement", "Event", "Script event", [new("eventTriggered", Id: "test-event")], new(), Scenario: "conquest1"),
            new("total-cities", "achievement", "Total", "Lifetime cities", [new("citiesCaptured", 3, Scope: "lifetime")], new()),
            new("total-wins", "achievement", "Wins", "Lifetime victories", [new("battleWon", 2, Scope: "lifetime")], new()),
            new("secret-item", "achievement", "Secret equipment", "Revealed on completion", [new("itemOwned", 2, Id: "ew4.item.0")], new(7), Hidden: true),
            new("secret-kills", "achievement", "Secret kills", "Still hidden", [new("unitsDestroyed", 100)], new(), Hidden: true)
        };
        MetagameObjectives.Validate(objectives); Check(true, "valid configuration");
        Reject(() => MetagameObjectives.Validate([objectives[0], objectives[0]]), "objectives.configuration");
        Reject(() => MetagameObjectives.Validate([objectives[0] with { Reward = new(-1) }]), "objectives.configuration");
        Reject(() => MetagameObjectives.Validate([objectives[0] with { Conditions = [new("misspelling")] }]), "objectives.condition");
        Reject(() => MetagameObjectives.Validate([objectives[0] with { Conditions = [new("cellControlled", Cell: 3)] }]), "objectives.condition");
        Reject(() => MetagameObjectives.Validate([objectives[0] with { Conditions = [new("itemOwned", Scope: "lifetime")] }]), "objectives.condition");
        Reject(() => MetagameObjectives.Validate([objectives[0] with { Kind = "task", Hidden = true }]), "objectives.configuration");
        string data = Path.Combine(root, "rewrite/data");
        var overrides = new Dictionary<string, byte[]> {
            ["rewrite/extension.json"] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, apiVersion = 2, metagame = "rewrite/objectives-test.json", presentation = "rewrite/objective-ui.json" }),
            ["rewrite/objective-ui.json"] = JsonSerializer.SerializeToUtf8Bytes(new { headquarters = new { left = 30, top = 28, width = 70, height = 40 }, tasks = new { width = 420, height = 240 }, achievements = new { width = 400, height = 220 } }),
            ["rewrite/objectives-test.json"] = JsonSerializer.SerializeToUtf8Bytes(new MetagameRules { Objectives = objectives.Where(o => o.Id != "hidden").ToArray() }, ClientGame.Json)
        };
        byte[] Read(string path) => overrides.GetValueOrDefault(path) ?? File.ReadAllBytes(Path.Combine(data,
            path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl"));
        Check(content.Extensions.Layouts["achievements"].Width == 400 && content.Extensions.Layouts["headquarters"].Width == 70, "author UI layout keys load");
        var session = content.Start("conquest1", "fra").Session with { BattleId = "test-run" };
        var account = session.Account with { Medals = 0, Badges = 0,
            Roster = [content.Catalogs.Accounts.FromDefinition(content.Catalogs.Runtime.Commanders.Keys.First(), "owned")],
            Inventory = new Dictionary<string, long> { ["ew4.item.0"] = 1 }, UnlockedCampaigns = ["battle1"],
            CampaignStars = new Dictionary<string, int> { ["battle1"] = 3 } };
        account = MetagameObjectives.Observe(account, objectives);
        Check(account.CompletedObjectives.Order().SequenceEqual(new[] { "any", "hidden", "owned" }), "account conditions and any combination");
        account.Validate();
        var claimed = MetagameObjectives.Claim(account, account.Revision, objectives[0]);
        Check(claimed.Medals == 25 && claimed.Badges == 2 && claimed.Revision == account.Revision + 1, "two resource award transaction");
        Reject(() => MetagameObjectives.Claim(claimed, account.Revision, objectives[0]), "account.revision");
        Reject(() => MetagameObjectives.Claim(claimed, claimed.Revision, objectives[0]), "objectives.claimed");
        Reject(() => MetagameObjectives.Claim(account, account.Revision, objectives[2]), "objectives.incomplete");
        try { MetagameObjectives.Claim(account with { Medals = long.MaxValue }, account.Revision, objectives[0]); throw new InvalidOperationException("Missing overflow check"); }
        catch (OverflowException) { Check(account.Medals == 0 && !account.ClaimedRewards.Any(), "overflow leaves original immutable account intact"); }
        var before = session.World with { Cells = session.World.Cells.Select((c, i) => i < 2 ? c with { Owner = null, BuildingKind = "city" } : c).ToArray() };
        var after = before with { Cells = before.Cells.Select((c, i) => i < 2 ? c with { Owner = session.PlayerFaction } : c).ToArray(),
            Factions = before.Factions.Select(f => f.Id == session.PlayerFaction ? f with { Kills = new Dictionary<string, long> { ["infantry"] = 3 } } : f).ToArray(),
            TriggeredScenarioEvents = ["test-event"] };
        var context = new ObjectiveContext(session with { Account = claimed, World = after }, 2, "fra", before);
        string ledgerKey = "test-run:" + session.PlayerFaction;
        var progressed = MetagameObjectives.Observe(claimed, objectives, context);
        Check(progressed.CompletedObjectives.Contains("capture") && progressed.CompletedObjectives.Contains("control")
            && progressed.CompletedObjectives.Contains("cities") && progressed.CompletedObjectives.Contains("event"), "capture, ownership, city count, event");
        Check(!progressed.CompletedObjectives.Contains("kill"), "three kills below lifetime threshold");
        var observedAgain = MetagameObjectives.Observe(progressed, objectives, context);
        Check(ReferenceEquals(observedAgain, progressed), "same observation is idempotent");
        Check(progressed.ObjectiveBattles[ledgerKey].CapturedCities.Count == 2, "captures count city cells once");
        var replay = MetagameObjectives.Observe(progressed, objectives, context with { Session = session with { World = before }, Previous = null });
        Check(replay.ObjectiveBattles[ledgerKey].UnitsDestroyed == 3 && replay.CompletedObjectives.Contains("capture"), "old world cannot roll back counters or completion");
        var recapture = MetagameObjectives.Observe(progressed, objectives, context);
        Check(recapture.ObjectiveBattles[ledgerKey].CapturedCities.Count == 2, "recapture cannot farm city counter");
        var secondWorld = after with { Factions = after.Factions.Select(f => f.Id == session.PlayerFaction
            ? f with { Kills = new Dictionary<string, long> { ["infantry"] = 2 } } : f).ToArray() };
        var second = new ObjectiveContext(session with { BattleId = "second-run", World = secondWorld }, 2, "fra", before);
        var lifetime = MetagameObjectives.Observe(progressed, objectives, second);
        Check(lifetime.CompletedObjectives.Contains("kill") && lifetime.CompletedObjectives.Contains("total-cities"), "lifetime adds different runs");
        var foreign = MetagameObjectives.Observe(progressed, objectives, second with { Country = "gbr" });
        Check(!foreign.CompletedObjectives.Contains("kill"), "country filter applies to lifetime counters");
        var wrongMode = MetagameObjectives.Observe(claimed, objectives, context with { Mode = 1 });
        Check(!wrongMode.CompletedObjectives.Contains("capture"), "campaign does not satisfy conquest restriction");
        Check(MetagameObjectives.Progress(lifetime, objectives[3], objectives[3].Conditions[0], null).Complete, "lifetime visible outside current game");
        Check(!MetagameObjectives.Progress(lifetime, objectives[2], objectives[2].Conditions[0], null).Complete, "single battle conditions need context");
        var lostInventory = MetagameObjectives.Observe(progressed with { Roster = [], Inventory = new Dictionary<string, long>() }, objectives);
        Check(lostInventory.CompletedObjectives.Contains("owned"), "completed ownership achievement remains completed");
        var result = new BattleResult("test-run", "conquest1", "Victory", 10, 4, 100, 20, 0, 0);
        var won = context with { Session = context.Session with { BattleResult = result } };
        var victory = MetagameObjectives.Observe(lifetime with { BattleResults = new Dictionary<string, BattleResult> { ["test-run"] = result } }, objectives, won);
        Check(victory.CompletedObjectives.Contains("win") && !victory.CompletedObjectives.Contains("total-wins"), "victory, stars, round conditions together");
        var twoWins = MetagameObjectives.Observe(victory, objectives, second with { Session = second.Session with { BattleResult = result with { Id = "second-run" } } });
        Check(twoWins.CompletedObjectives.Contains("total-wins"), "lifetime victory history");
        var otherFaction = after.Factions.First(f => f.Id != session.PlayerFaction).Id;
        var switched = MetagameObjectives.Observe(progressed, objectives, context with { Country = "gbr", Session = context.Session with { PlayerFaction = otherFaction } });
        Check(switched.ObjectiveBattles.Count == 2 && switched.ObjectiveBattles[ledgerKey].Country == "fra", "scenario player switch keeps each faction ledger separate");
        Check(!MetagameObjectives.Progress(switched, objectives[3], objectives[3].Conditions[0], null).Complete, "switch does not leak kills into restricted country");
        var roundTrip = JsonSerializer.Deserialize<PlayerAccount>(JsonSerializer.Serialize(lifetime, ClientGame.Json), ClientGame.Json)!;
        roundTrip.Validate();
        Check(roundTrip.ObjectiveBattles[ledgerKey].CapturedCities.SequenceEqual([0, 1]) && roundTrip.ClaimedRewards.Contains("objective.owned"), "account progress and receipts serialize");
        var copied = lifetime.Copy();
        Check(!ReferenceEquals(copied.ObjectiveBattles[ledgerKey].CapturedCities, lifetime.ObjectiveBattles[ledgerKey].CapturedCities), "copy owns its capture arrays");
        var oldJson = JsonNode.Parse(JsonSerializer.Serialize(account, ClientGame.Json))!.AsObject();
        oldJson.Remove("objectiveBattles");oldJson.Remove("completedObjectives");
        var oldAccount = oldJson.Deserialize<PlayerAccount>(ClientGame.Json)!;oldAccount.Validate();
        Check(oldAccount.ObjectiveBattles.Count == 0 && oldAccount.CompletedObjectives.Count == 0, "old account gets optional defaults");
        string folder = Path.Combine(root, "build/hq-objectives-20261025/checks", Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientGame.Json));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object? body = null, string? error = null)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body ?? new { }, ClientGame.Json)));
            var dataElement = response.RootElement;
            Check(dataElement.GetProperty("ok").GetBoolean() == (error == null), route + " API success");
            if (error != null) { Check(dataElement.GetProperty("code").GetString() == error, route + " API error");return default; }
            return dataElement.GetProperty("data").Clone();
        }
        var listing = Call("objectives");
        Check(listing.GetProperty("entries").GetArrayLength() == objectives.Count(o => o.Id != "hidden" && !o.Hidden), "author definitions appear but incomplete hidden achievements are excluded");
        Check(!listing.GetProperty("entries").EnumerateArray().Any(e => e.GetProperty("id").GetString()!.StartsWith("secret-")), "hidden names and conditions are absent from list response");
        long revision = listing.GetProperty("revision").GetInt64();
        var awarded = Call("objectives", new { op = "claim", revision, offer = "owned" });
        Check(awarded.GetProperty("medals").GetInt64() == 25 && awarded.GetProperty("badges").GetInt64() == 2, "claim writes resources");
        byte[] saved = File.ReadAllBytes(Path.Combine(folder, "account.json"));
        Call("objectives", new { op = "claim", revision, offer = "owned" }, "account.revision");
        Call("objectives", new { op = "claim", revision = revision + 1, offer = "owned" }, "objectives.claimed");
        Call("objectives", new { op = "claim", revision = revision + 1, offer = "capture" }, "objectives.incomplete");
        Call("objectives", new { op = "claim", revision = revision + 1, offer = "arbitrary" }, "objectives.unknown");
        Check(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "account.json"))), "rejected claims never change profile bytes");
        host = new ClientGame(content, folder);
        Check(Call("objectives").GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "owned").GetProperty("claimed").GetBoolean(), "restart keeps claimed state");
        var started = Call("start", new { scenario = "conquest1", country = "fra" });
        Call("save", new { slot = 1, mode = 2 });
        Call("load", new { slot = 1, mode = 2 });
        Check(Call("objectives").GetProperty("medals").GetInt64() == 25, "session save load cannot award again");
        Call("objectives", new { op = "claim", revision = Call("objectives").GetProperty("revision").GetInt64(), offer = "any" });
        Call("action", new { op = "state" });
        Check(Call("headquarters").GetProperty("medals").GetInt64() == 25, "active game account is synchronized after claiming");
        var equippedProfile = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Path.Combine(folder, "account.json")), ClientGame.Json)!;
        equippedProfile = equippedProfile with { Revision = equippedProfile.Revision + 1, Inventory = new Dictionary<string, long> { ["ew4.item.0"] = 2 } };
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(equippedProfile, ClientGame.Json));
        host = new ClientGame(content, folder);
        var revealed = Call("objectives");
        var secret = revealed.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "secret-item");
        Check(secret.GetProperty("completed").GetBoolean() && !secret.GetProperty("claimed").GetBoolean(), "hidden achievement appears when completed and is claimable");
        var secretClaim = Call("objectives", new { op = "claim", offer = "secret-item", revision = revealed.GetProperty("revision").GetInt64() });
        Check(secretClaim.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "secret-item").GetProperty("claimed").GetBoolean(), "claimed hidden achievement stays visible");
        host = new ClientGame(content, folder);
        Check(Call("objectives").GetProperty("entries").EnumerateArray().Any(e => e.GetProperty("id").GetString() == "secret-item" && e.GetProperty("claimed").GetBoolean()), "claimed secret remains visible after restart");
        Check(!Call("objectives").GetProperty("entries").EnumerateArray().Any(e => e.GetProperty("id").GetString() == "secret-kills"), "other incomplete secret remains invisible");
        var itemRules = content.Catalogs.Items.Rules();
        var item = itemRules.Definitions.First(p => p.Value.Equipment.Consumable && p.Key != "ew4.item.0");
        var source = content.Catalogs.Runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children").EnumerateArray()
            .Select(e => e.GetProperty("attributes")).First(a => a.GetProperty("drawlots").GetString() == "0"
                && "ew4.commander." + a.GetProperty("id").GetString() != account.Roster[0].DefinitionId
                && content.Catalogs.Runtime.Commanders[int.Parse(a.GetProperty("id").GetString()!)].Definition.Items.Count == 0);
        var exclusiveCommander = content.Catalogs.Accounts.FromDefinition(int.Parse(source.GetProperty("id").GetString()!), "exclusive-instance");
        var exclusive = new MetagameObjective("exclusive", "achievement", "Exclusive", "Author-only rewards",
            [new("itemOwned", Id: "ew4.item.0")], new(13, 2) {
                Items = new Dictionary<string, long> { [item.Key] = 2 }, Commanders = [exclusiveCommander.DefinitionId] }, Hidden: true);
        Reject(() => MetagameObjectives.Validate([exclusive with { Reward = exclusive.Reward with { Items = new Dictionary<string, long> { [item.Key] = 0 } } }]), "objectives.reward");
        Reject(() => MetagameObjectives.Validate([exclusive with { Reward = exclusive.Reward with { Commanders = [exclusiveCommander.DefinitionId, exclusiveCommander.DefinitionId] } }]), "objectives.reward");
        var eligible = MetagameObjectives.Observe(account, [exclusive]);
        var commanderRules = content.Catalogs.Accounts.Rules();
        PlayerAccount Grant(PlayerAccount input, ItemTradingRules? items = null, AccountCommanderRules? roster = null) =>
            MetagameObjectives.Claim(input, input.Revision, exclusive, items ?? itemRules, roster ?? commanderRules, [exclusiveCommander]);
        var exclusiveGranted = Grant(eligible);
        Check(exclusiveGranted.Inventory[item.Key] == 2 && exclusiveGranted.Roster.Any(c => c.DefinitionId == exclusiveCommander.DefinitionId)
            && exclusiveGranted.Medals == 13 && exclusiveGranted.Badges == 2, "catalog-only item and nonrecruitable commander granted together");
        Reject(() => Grant(exclusiveGranted), "objectives.claimed");
        Reject(() => Grant(eligible with { Roster = eligible.Roster.Append(exclusiveCommander).ToArray() }), "objectives.commander_owned");
        Reject(() => Grant(eligible, roster: commanderRules with { MaximumRoster = eligible.Roster.Count }), "account.capacity");
        Reject(() => Grant(eligible, itemRules with { MaximumDistinctItems = 1 }), "trading.capacity");
        var limitedItems = itemRules.Definitions.ToDictionary(p => p.Key, p => p.Value);
        limitedItems[item.Key] = item.Value with { MaximumOwned = 1 };
        Reject(() => Grant(eligible, itemRules with { Definitions = limitedItems }), "objectives.item_capacity");
        limitedItems[item.Key] = item.Value with { Unique = true };
        Reject(() => Grant(eligible, itemRules with { Definitions = limitedItems }), "objectives.item_capacity");
        try { Grant(eligible with { Inventory = new Dictionary<string, long> { [item.Key] = long.MaxValue } });throw new InvalidOperationException("Missing item overflow"); }
        catch (OverflowException) { Check(eligible.Roster.Count == account.Roster.Count && !eligible.Inventory.ContainsKey(item.Key)
            && !eligible.ClaimedRewards.Contains("objective.exclusive"), "failed reward is atomic"); }
        overrides["rewrite/objectives-test.json"] = JsonSerializer.SerializeToUtf8Bytes(new MetagameRules { Objectives = [exclusive], ShopSlots = [] }, ClientGame.Json);
        content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl"));
        string exclusiveFolder = Path.Combine(folder, "exclusive");Directory.CreateDirectory(exclusiveFolder);
        File.WriteAllBytes(Path.Combine(exclusiveFolder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientGame.Json));
        host = new ClientGame(content, exclusiveFolder);
        var exclusiveList = Call("objectives");
        Check(exclusiveList.GetProperty("entries")[0].GetProperty("reward").GetProperty("items").GetProperty(item.Key).GetInt64() == 2, "API returns author item reward quantity");
        Call("start", new { scenario = "conquest1", country = "fra" });
        Call("save", new { slot = 1, mode = 2 });
        Call("objectives", new { op = "claim", offer = "exclusive", revision = Call("objectives").GetProperty("revision").GetInt64() });
        var rewardedHQ = Call("headquarters");
        Check(rewardedHQ.GetProperty("roster").EnumerateArray().Any(c => c.GetProperty("definitionId").GetString() == exclusiveCommander.DefinitionId), "API grants nonrecruitable commander into active account");
        Check(!rewardedHQ.GetProperty("definitions").EnumerateArray().Single(c => c.GetProperty("id").GetString() == exclusiveCommander.DefinitionId).GetProperty("recruitable").GetBoolean(), "reward does not add commander to academy pool");
        Call("load", new { slot = 1, mode = 2 });
        host = new ClientGame(content, exclusiveFolder);
        var exclusiveSaved = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Path.Combine(exclusiveFolder, "account.json")), ClientGame.Json)!;
        Check(exclusiveSaved.Inventory[item.Key] == 2 && exclusiveSaved.Roster.Count(c => c.DefinitionId == exclusiveCommander.DefinitionId) == 1, "old session load and restart retain exclusive inventory and roster exactly once");
        var exclusiveReloaded = Call("objectives");
        Check(exclusiveReloaded.GetProperty("entries")[0].GetProperty("claimed").GetBoolean(), "hidden exclusive achievement remains visible after restart");
        Call("objectives", new { op = "claim", offer = "exclusive", revision = exclusiveReloaded.GetProperty("revision").GetInt64() }, "objectives.claimed");
        int section = Math.Clamp((int.Parse(source.GetProperty("star").GetString()!) - 1) / 3, 0, 2);
        overrides["rewrite/objectives-test.json"] = JsonSerializer.SerializeToUtf8Bytes(new MetagameRules { Objectives = [exclusive], ShopSlots = [],
            CommanderSectionCapacities = new Dictionary<string, int> { [section.ToString()] = 0 } }, ClientGame.Json);
        var fullContent = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl"));
        string fullFolder = Path.Combine(folder, "full-section");Directory.CreateDirectory(fullFolder);
        File.WriteAllBytes(Path.Combine(fullFolder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(eligible, ClientGame.Json));
        host = new ClientGame(fullContent, fullFolder);
        var fullList = Call("objectives");byte[] fullBefore = File.ReadAllBytes(Path.Combine(fullFolder, "account.json"));
        Call("objectives", new { op = "claim", offer = "exclusive", revision = fullList.GetProperty("revision").GetInt64() }, "account.capacity");
        Check(fullBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(fullFolder, "account.json"))), "full star section rejects entire reward without writing profile");
        foreach (var reward in new[] { exclusive.Reward with { Items = new Dictionary<string, long> { ["missing"] = 1 } }, exclusive.Reward with { Commanders = ["missing"] } })
        {
            overrides["rewrite/objectives-test.json"] = JsonSerializer.SerializeToUtf8Bytes(new MetagameRules { Objectives = [exclusive with { Reward = reward }] }, ClientGame.Json);
            Reject(() => new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl")), "objectives.reference");
        }
        var bad = new MetagameRules { Objectives = [objectives[0] with { Conditions = [new("itemOwned", Id: "missing")] }] };
        overrides["rewrite/objectives-test.json"] = JsonSerializer.SerializeToUtf8Bytes(bad, ClientGame.Json);
        Reject(() => new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl")), "objectives.reference");
        return checks;
    }
}
