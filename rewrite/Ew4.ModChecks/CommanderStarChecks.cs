using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Core;
using Ew4.Mods;
using Ew4.Persistence;

internal static class CommanderStarChecks
{
    internal static int Run(string root, string run)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Commander stars: " + label); }
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        string data = Path.Combine(root, "rewrite/data"), author = Path.Combine(run, "author");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        Directory.CreateDirectory(Path.Combine(author, "legacy"));
        var source = JsonNode.Parse(File.ReadAllBytes(Path.Combine(data, "legacy/commanders.json")))!;
        var definition = source["commanders"]![0]!;
        string[] fields = ["infantry", "cavalry", "artillery", "warship", "fort", "business", "movement"];
        string[] abilities = ["infantry", "cavalry", "artillery", "naval", "fort", "commerce", "movement"];
        for (int i = 0; i < fields.Length; i++) definition[fields[i]] = i + 6;
        File.WriteAllText(Path.Combine(author, "legacy/commanders.json"), source.ToJsonString());
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        store.Seal(author, "commander-stars", "Commander star check", "1.0", ["conquest1"]);
        var imported = store.Import(author, "ignored", "ignored"); store.Activate(imported.Key);
        string archive = Path.Combine(run, "commander-stars.ew4mod"); store.Export(imported.Key, archive);
        var copied = store.Import(archive, "ignored", "ignored");
        Check(copied.Key == imported.Key, "archive roundtrip preserves definitions");
        var content = baseline.Content(store.Read, ["conquest1"]);
        var owned = content.Catalogs.Accounts.FromDefinition(1, "star-check.commander");
        for (int i = 0; i < abilities.Length; i++) Check(owned.Learning.Abilities[abilities[i]] == i + 6, "imported " + abilities[i]);
        var resolved = AccountCommanders.Resolve(owned, content.Catalogs.Accounts.Rules());
        Check(resolved.Movement == 12, "resolved movement exceeds five");
        var profile = new UnitProfile { Id = "star-check", Category = "infantry", MaxHealth = 100, Movement = 3,
            MinimumAttack = 10, MaximumAttack = 10, AttackBands = [new(0, 5)], DamageMultipliers = new Dictionary<string, float> { ["infantry"] = 1 } };
        var unit = new Combatant { Profile = profile, Health = 100 };
        foreach (string category in abilities.Take(5))
        {
            var attached = Ew4Commander.Attach(unit with { Profile = profile with { Category = category } }, resolved);
            Check(attached.Stars == owned.Learning.Abilities[category], "combat attachment " + category);
        }
        Check(Ew4Commander.Attach(unit, resolved).Profile.Movement == 15, "land movement includes all twelve stars");
        var trading = content.Start("conquest1", "fra").Rules.Trading.Items;
        Check(trading.MaximumCommerceLevel == null, "actual shop has no five-star cap");
        foreach (int level in new[] { 0, 1, 5, 6, 10, 11, 25, 26, 100 })
        {
            Check(ItemTrading.PurchasePrice(100, level, trading).Medals == Math.Max(0, 100 - level * 4), "purchase four percent per star " + level);
            Check(ItemTrading.SalePrice(100, level, trading).Medals == 60 + level * 4, "sale four percentage points per star " + level);
        }
        Check(ItemTrading.PurchasePrice(101, 6, trading).Medals == 77 && ItemTrading.SalePrice(101, 6, trading).Medals == 84, "original integer rounding");
        var item = new Equipment("star-check.item", "test", 0);
        var transactionRules = trading with { Definitions = new Dictionary<string, ItemTradeDefinition> { [item.Id] = new(item, 100, false) } };
        var market = new ItemMarket("star-check.shop", [new("offer", item.Id)]);
        var wallet = new PlayerAccount { Id = "trader", ContentRevision = "test", Medals = 1000 };
        var bought = ItemTrading.Buy(wallet, 0, market, "offer", 6, transactionRules);
        Check(bought.Account.Medals == 924 && bought.Account.Inventory[item.Id] == 1, "six-star purchase deducts actual price");
        var sold = ItemTrading.Sell(bought.Account, bought.Account.Revision, market, item.Id, 6, [], transactionRules);
        Check(sold.Account.Medals == 1008 && !sold.Account.Inventory.ContainsKey(item.Id), "six-star sale credits actual recovery");
        foreach (int rolls in new[] { 1, 5, 7 })
        {
            var combatant = unit with { Profile = profile with { AttackBands = [new(0, rolls)] } };
            int Damage(int stars) => Ew4Combat.Calculate(combatant with { Stars = stars }, unit, 1, new(123)).DefenderDamage;
            Check(Damage(6) - Damage(5) == Math.Min(rolls, 5), "sixth star affects combat at " + rolls + " rolls");
        }
        var account = new PlayerAccount { Id = "local", ContentRevision = content.Catalogs.Accounts.SourceRevision, Roster = [owned] };
        string accountPath = Path.Combine(run, "saves/mods", imported.Id, imported.LogicRevision, "account.json");
        Directory.CreateDirectory(Path.GetDirectoryName(accountPath)!); File.WriteAllBytes(accountPath, JsonSerializer.SerializeToUtf8Bytes(account, json));
        JsonElement Call(ModStore host, string route, object body)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), route + " succeeds: " + response.RootElement);
            return response.RootElement.GetProperty("data").Clone();
        }
        var headquarters = Call(store, "headquarters", new { });
        Call(store, "commander", new { op = "tag", revision = headquarters.GetProperty("revision").GetInt64(), commander = owned.Id, tag = "verified" });
        var restarted = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        headquarters = Call(restarted, "headquarters", new { });
        var saved = headquarters.GetProperty("roster").EnumerateArray().Single(c => c.GetProperty("id").GetString() == owned.Id);
        foreach (string ability in abilities) Check(saved.GetProperty("learning").GetProperty("abilities").GetProperty(ability).GetInt32() == owned.Learning.Abilities[ability], "account restart " + ability);
        var session = content.Start("conquest1", "fra", account).Session;
        var restored = SessionSave.Decode(SessionSave.Encode(session), new(session.ContentRevision, session.World.ContentRevision, account.ContentRevision));
        foreach (string ability in abilities) Check(restored.Account.Roster.Single().Learning.Abilities[ability] == owned.Learning.Abilities[ability], "session roundtrip " + ability);
        File.WriteAllText(Path.Combine(run, "headquarters.json"), headquarters.GetRawText());
        return checks;
    }
}
