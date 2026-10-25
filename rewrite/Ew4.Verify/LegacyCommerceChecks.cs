using System.Buffers.Binary;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;

internal static class LegacyCommerceChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Legacy commerce: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, label: code + " actual=" + error.Code); return; }
            throw new InvalidOperationException("Expected commerce rejection: " + code);
        }
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/commerce_vectors.json")));
        foreach (var sample in vectors.RootElement.GetProperty("records").EnumerateArray())
        {
            byte[] raw = sample.GetProperty("raw").EnumerateArray().Select(v => v.GetByte()).ToArray();
            int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(offset));
            if (sample.GetProperty("kind").GetString() == "market")
            {
                var market = LegacyMarket.Read(raw);
                Check(market.Cell == Int(112) && market.Slots.Count == 14, "native shop key and capacity");
                for (int i = 0; i < 14; i++) Check(market.Slots[i] == new LegacyMarketSlot(Int(i * 8), Int(i * 8 + 4)), "native shop entries");
                Check(market.ToBytes().SequenceEqual(raw), "shop bytes retained");
                byte[] copy = market.ToBytes(); copy[0] ^= 0xff;
                Check(!copy.SequenceEqual(market.ToBytes()), "shop copy isolated");
                raw[0] ^= 0xff;
                Check(!raw.SequenceEqual(market.ToBytes()), "shop input isolated");
            }
            else
            {
                var tavern = LegacyTavern.Read(raw);
                Check(tavern.Cell == Int(104) && tavern.Candidates.Count == Int(100), "native tavern key and count");
                for (int i = 0; i < tavern.Candidates.Count; i++)
                    Check(tavern.Candidates[i] == new LegacyTavernCandidate(Int(i * 20), Int(i * 20 + 4), Int(i * 20 + 8), Int(i * 20 + 12), Int(i * 20 + 16)), "native tavern candidate fields");
                Check(tavern.ToBytes().SequenceEqual(raw), "tavern inactive slots retained");
                raw[0] ^= 0xff;
                Check(!raw.SequenceEqual(tavern.ToBytes()), "tavern input isolated");
            }
        }
        var empty = new PlayerAccount { Id = "player", ContentRevision = "test", Medals = 20000 };
        foreach (var sample in vectors.RootElement.GetProperty("availability").EnumerateArray())
        {
            bool consumable = sample.GetProperty("consumable").GetBoolean();
            int[] items = sample.GetProperty("items").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int[] quantities = sample.GetProperty("quantities").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            bool[] inventory = sample.GetProperty("inventory").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
            bool[] equipped = sample.GetProperty("equipped").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
            var definitions = Enumerable.Range(0, 14).ToDictionary(i => i.ToString(), i => new ItemTradeDefinition(new(i.ToString(), "test", 0, Consumable: consumable), 10, !consumable));
            var rules = new ItemTradingRules { Definitions = definitions };
            var account = empty with { Inventory = Enumerable.Range(0, 14).Where(i => inventory[i] || equipped[i]).ToDictionary(i => i.ToString(), _ => 1L) };
            for (int i = 0; i < items.Length; i++)
                Check((items[i] >= 0 && ItemTrading.Available(account, new(i.ToString(), items[i].ToString(), consumable ? quantities[i] : null), rules))
                    == (sample.GetProperty("expected")[i].GetInt32() != 0), "native availability includes equipped unique item ownership");
        }
        var defaults = new ItemTradingRules();
        foreach (var sample in vectors.RootElement.GetProperty("prices").EnumerateArray())
        {
            int commerce = sample.GetProperty("map").GetBoolean() && sample.GetProperty("human").GetBoolean() && sample.GetProperty("commander").GetBoolean()
                ? sample.GetProperty("commerce").GetInt32() : 0;
            long price = sample.GetProperty("price").GetInt64();
            var actual = sample.GetProperty("kind").GetString() == "purchase" ? ItemTrading.PurchasePrice(price, commerce, defaults) : ItemTrading.SalePrice(price, commerce, defaults);
            Check(actual == new TradePrice(sample.GetProperty("expected")[0].GetInt64(), sample.GetProperty("expected")[1].GetInt32()), "native purchase/sale price and reported discount");
        }
        string assets = Path.Combine(root, "apk_work/original/assets");
        var catalog = LegacyItemTradingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_item.xml")));
        var runtime = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var trade = catalog.Rules();
        Check(catalog.Items.Count == 228 && catalog.Items.All(p => p.Value.Equipment == runtime.Items[p.Key]), "item pricing catalog matches combat equipment");
        int markets = 0, taverns = 0, candidates = 0, offers = 0;
        var buildingStates = new Dictionary<int, int>();
        foreach (string path in Directory.EnumerateFiles(assets, "*.btl"))
        {
            byte[] raw = File.ReadAllBytes(path);
            var battle = LegacyBattle.Read(raw);
            foreach (var source in battle.Markets)
            {
                var market = catalog.Import(source, "shop." + markets++);
                offers += market.Offers.Count;
                Check(market.Offers.Count == source.Slots.Count(s => s.ItemId >= 0), "all original market entries mapped");
                for (int i = 0, offer = 0; i < 14; i++)
                {
                    var slot = source.Slots[i]; if (slot.ItemId < 0) continue;
                    var definition = catalog.Items[slot.ItemId]; var imported = market.Offers[offer++];
                    Check(imported.ItemId == definition.Equipment.Id && imported.Remaining == (definition.Equipment.Consumable ? (long?)slot.Quantity : null), "actual stock semantics and identity");
                }
                var section = battle.Sections.Single(s => s.Name == "opaque116");
                Check(source.ToBytes().SequenceEqual(raw.AsSpan(section.Offset + (battle.Markets.IndexOf(source)) * 116, 116).ToArray()), "real shop source preserved");
            }
            foreach (var source in battle.Taverns)
            {
                taverns++; candidates += source.Candidates.Count;
                Check(source.Candidates.All(c => runtime.Commanders.ContainsKey(c.CommanderId) && c.Money >= 0 && c.Industry >= 0 && c.Medals >= 0 && c.AvailableRound >= 1), "real tavern references and prices");
                var site = battle.Sites.Single(s => s.Cell == source.Cell);
                buildingStates[site.State6] = buildingStates.GetValueOrDefault(site.State6) + 1;
            }
            Check(raw.SequenceEqual(battle.ToBytes()), "typed sections preserve complete BTL");
        }
        Check(markets == 151 && taverns == 38, "complete original commerce corpus");
        var example = new ItemMarket("city.shop", [new("horse", "ew4.item.0"), new("star", "ew4.item.53", 2)]);
        byte[] before = JsonSerializer.SerializeToUtf8Bytes(empty), marketBefore = JsonSerializer.SerializeToUtf8Bytes(example);
        var bought = ItemTrading.Buy(empty, 0, example, "horse", 5, trade);
        Check(bought.Account.Medals == empty.Medals - 652 && bought.Account.Inventory["ew4.item.0"] == 1 && bought.Account.Revision == 1, "actual medal purchase");
        Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(empty)) && marketBefore.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(example)), "purchase input unchanged");
        Reject(() => ItemTrading.Buy(bought.Account, 0, bought.Market, "star", 0, trade), "account.revision");
        Reject(() => ItemTrading.Buy(bought.Account, 1, bought.Market, "horse", 0, trade), "trading.unavailable");
        Reject(() => ItemTrading.Sell(bought.Account, 1, bought.Market, "ew4.item.0", 0, ["ew4.item.0"], trade), "trading.unavailable");
        var sold = ItemTrading.Sell(bought.Account, 1, bought.Market, "ew4.item.0", 5, [], trade);
        Check(sold.Account.Inventory.Count == 0 && sold.Account.Medals == empty.Medals && sold.Account.Revision == 2, "sale returns real medals and clears ownership");
        var star1 = ItemTrading.Buy(empty, 0, example, "star", 0, trade);
        var star2 = ItemTrading.Buy(star1.Account, 1, star1.Market, "star", 0, trade);
        Check(star2.Account.Inventory["ew4.item.53"] == 2 && star2.Market.Offers.Single(o => o.Id == "star").Remaining == 0, "consumable stock transaction");
        Reject(() => ItemTrading.Buy(star2.Account, 2, star2.Market, "star", 0, trade), "trading.unavailable");
        Reject(() => ItemTrading.Buy(empty with { Medals = 4 }, 0, example, "star", 0, trade), "account.medals");
        Reject(() => ItemTrading.Buy(empty, 0, example, "star", 0, trade with { MaximumDistinctItems = 0 }), "trading.unavailable");
        Reject(() => ItemTrading.Buy(star1.Account, 1, star1.Market, "star", 0, trade with { Definitions = trade.Definitions.ToDictionary(p => p.Key, p => p.Key == "ew4.item.53" ? p.Value with { MaximumOwned = 1 } : p.Value) }), "trading.unavailable");
        Reject(() => ItemTrading.Sell(empty with { Medals = long.MaxValue, Inventory = new Dictionary<string, long> { ["ew4.item.0"] = 1 } }, 0, example, "missing", 0, [], trade), "trading.unavailable");
        bool overflow = false;
        try { ItemTrading.Sell(empty with { Medals = long.MaxValue, Inventory = new Dictionary<string, long> { ["ew4.item.0"] = 1 } }, 0, example, "ew4.item.0", 0, [], trade); }
        catch (OverflowException) { overflow = true; }
        Check(overflow, "overflow rejected without balance wraparound");
        var dynamicItems = Enumerable.Range(0, 100).ToDictionary(i => "custom." + i, i => new ItemTradeDefinition(new("custom." + i, "test", 0), 1, true));
        var dynamicRules = new ItemTradingRules { Definitions = dynamicItems };
        var dynamicMarket = new ItemMarket("large.shop", dynamicItems.Keys.Select(i => new ItemTradeOffer(i, i)).ToArray());
        var many = empty;
        foreach (string id in dynamicItems.Keys) many = ItemTrading.Buy(many, many.Revision, dynamicMarket, id, 0, dynamicRules).Account;
        Check(many.Inventory.Count == 100 && many.Revision == 100 && many.Medals == 19900, "no 14-slot market or 28-slot inventory ceiling");
        Check(ItemTrading.PurchasePrice(100, 6, defaults).Medals == 80
            && ItemTrading.PurchasePrice(100, 6, defaults with { MaximumCommerceLevel = null }).Medals == 76, "commerce cap is policy");
        Reject(() => LegacyMarket.Read(new byte[115]), "legacy.market_record");
        Reject(() => LegacyTavern.Read(new byte[107]), "legacy.tavern_record");
        var invalid = new byte[108]; BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(100), 6);
        Reject(() => LegacyTavern.Read(invalid), "legacy.tavern_count");
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_commerce.json"), JsonSerializer.Serialize(new
            { checks, errors = 0, markets, taverns, candidates, offers, items = catalog.Items.Count, tavernBuildingState6 = buildingStates,
                limits = "Typed records, item pricing and independent medal/inventory/market transactions; world/tavern session and roster persistence remain incomplete." }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }

    private static int IndexOf<T>(this IReadOnlyList<T> source, T value)
    {
        for (int i = 0; i < source.Count; i++) if (EqualityComparer<T>.Default.Equals(source[i], value)) return i;
        throw new InvalidOperationException("Missing source record");
    }
}
