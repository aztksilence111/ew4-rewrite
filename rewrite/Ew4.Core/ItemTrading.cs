using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record TradePrice(long Medals, int AppliedPercent);
public sealed record ItemTradeDefinition(Equipment Equipment, long Price, bool Unique, long? MaximumOwned = null);
public sealed record ItemTradeOffer(string Id, string ItemId, long? Remaining = null);
public sealed record ItemMarket(string Id, IReadOnlyList<ItemTradeOffer> Offers);
public sealed record ItemTradingRules
{
    public IReadOnlyDictionary<string, ItemTradeDefinition> Definitions { get; init; } = new Dictionary<string, ItemTradeDefinition>();
    public int CommercePercentPerLevel { get; init; } = 4;
    public int SaleBasePercent { get; init; } = 60;
    public int? MaximumCommerceLevel { get; init; } = 5;
    public int? MaximumDistinctItems { get; init; }
    public void Validate()
    {
        if (CommercePercentPerLevel < 0 || SaleBasePercent < 0 || MaximumDistinctItems < 0 || MaximumCommerceLevel < 0
            || Definitions.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key != p.Value.Equipment.Id || p.Value.Price < 0 || p.Value.MaximumOwned < 0))
            throw new RuleViolation("trading.configuration", "Invalid item trading configuration");
    }
}

// Inventory counts include equipped copies; assignment must never make unique ownership disappear.
public sealed record PlayerAccount
{
    public string Id { get; init; } = "";
    public string ContentRevision { get; init; } = "";
    public long Revision { get; init; }
    public long Medals { get; init; }
    public long Badges { get; init; }
    public IReadOnlyDictionary<int, IReadOnlyList<string>> CommanderOffers { get; init; } = new Dictionary<int, IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, long> Inventory { get; init; } = new Dictionary<string, long>();
    public IReadOnlyList<OwnedCommander> Roster { get; init; } = Array.Empty<OwnedCommander>();
    public IReadOnlyList<string> ClaimedRewards { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CompletedObjectives { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, ObjectiveBattleProgress> ObjectiveBattles { get; init; } = new Dictionary<string, ObjectiveBattleProgress>();
    public IReadOnlyList<string> UnlockedCampaigns { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int> CampaignStars { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, BattleResult> BattleResults { get; init; } = new Dictionary<string, BattleResult>();
    public IReadOnlyDictionary<string, long> CollectedMedalReceipts { get; init; } = new Dictionary<string, long>();
    public ItemMarket? HeadquartersStock { get; init; }
    public IReadOnlyDictionary<string, RefreshBalance> RefreshBalances { get; init; } = new Dictionary<string, RefreshBalance>();
    public void Validate()
    {
        if (CompletedObjectives == null || CompletedObjectives.Any(string.IsNullOrWhiteSpace)
            || CompletedObjectives.Distinct(StringComparer.Ordinal).Count() != CompletedObjectives.Count || ObjectiveBattles == null
            || ObjectiveBattles.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || string.IsNullOrWhiteSpace(p.Value.Scenario)
                || string.IsNullOrWhiteSpace(p.Value.BattleId) || string.IsNullOrWhiteSpace(p.Value.Faction) || p.Key != p.Value.BattleId + ":" + p.Value.Faction
                || p.Value.Mode is not (1 or 2) || string.IsNullOrWhiteSpace(p.Value.Country) || p.Value.UnitsDestroyed < 0
                || p.Value.CapturedCities == null || p.Value.CapturedCities.Any(c => c < 0)
                || p.Value.CapturedCities.Distinct().Count() != p.Value.CapturedCities.Count))
            throw new RuleViolation("account.objectives", "Invalid objective progress");
        if (CollectedMedalReceipts.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0))
            throw new RuleViolation("account.medal_receipts", "Invalid collected medal receipt");
        if (RefreshBalances.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || p.Value.Remaining < 0 || p.Value.Victories < 0))
            throw new RuleViolation("account.refresh", "Invalid refresh balance");
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(ContentRevision) || Revision < 0 || Medals < 0 || Badges < 0
            || Inventory.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 1))
            throw new RuleViolation("account.state", "Invalid account identity, balance or inventory");
        if (Roster.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != Roster.Count)
            throw new RuleViolation("account.roster", "Duplicate roster instance identity");
        foreach (var commander in Roster) commander.Validate();
        if (ClaimedRewards.Any(string.IsNullOrWhiteSpace) || ClaimedRewards.Distinct(StringComparer.Ordinal).Count() != ClaimedRewards.Count)
            throw new RuleViolation("account.rewards", "Invalid reward claims");
        if (UnlockedCampaigns.Any(string.IsNullOrWhiteSpace) || UnlockedCampaigns.Distinct(StringComparer.Ordinal).Count() != UnlockedCampaigns.Count)
            throw new RuleViolation("account.campaigns", "Invalid campaign unlocks");
        if (CampaignStars.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 1 || p.Value > 5)
            || BattleResults.Any(p => p.Key != p.Value.Id || string.IsNullOrWhiteSpace(p.Key)
                || p.Value.Round < 1 || p.Value.Stars < 0 || p.Value.Stars > 5 || p.Value.AwardMedals < 0 || p.Value.CollectedMedals < 0))
            throw new RuleViolation("account.results", "Invalid battle result history");
        if (HeadquartersStock != null && (string.IsNullOrWhiteSpace(HeadquartersStock.Id)
            || HeadquartersStock.Offers.Any(o => string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.ItemId) || o.Remaining < 0)
            || HeadquartersStock.Offers.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != HeadquartersStock.Offers.Count))
            throw new RuleViolation("account.stock", "Invalid headquarters stock");
        if (CommanderOffers.Keys.Any(k => k < 0) || CommanderOffers.Values.SelectMany(v => v).Any(string.IsNullOrWhiteSpace)
            || CommanderOffers.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Count() != CommanderOffers.Values.Sum(v => v.Count))
            throw new RuleViolation("account.offers", "Invalid or duplicate commander offers");
        if (Roster.SelectMany(c => c.Items).GroupBy(id => id, StringComparer.Ordinal)
            .Any(g => !Inventory.TryGetValue(g.Key, out long owned) || owned < g.LongCount()))
            throw new RuleViolation("account.equipment", "Equipped items must be owned by this account");
    }
    public PlayerAccount Copy() => this with { Inventory = Inventory.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), Roster = Roster.Select(c => c.Copy()).ToArray(),
        CompletedObjectives = CompletedObjectives.ToArray(), ObjectiveBattles = ObjectiveBattles.ToDictionary(p => p.Key, p => p.Value with { CapturedCities = p.Value.CapturedCities.ToArray() }),
        CommanderOffers = CommanderOffers.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.ToArray()), ClaimedRewards = ClaimedRewards.ToArray(), UnlockedCampaigns = UnlockedCampaigns.ToArray(),
        CampaignStars = CampaignStars.ToDictionary(p => p.Key, p => p.Value), BattleResults = BattleResults.ToDictionary(p => p.Key, p => p.Value),
        CollectedMedalReceipts = CollectedMedalReceipts.ToDictionary(p => p.Key, p => p.Value),
        RefreshBalances = RefreshBalances.ToDictionary(p => p.Key, p => p.Value),
        HeadquartersStock = HeadquartersStock == null ? null : HeadquartersStock with { Offers = HeadquartersStock.Offers.ToArray() } };
}

public sealed record ItemTradeChange(PlayerAccount Account, ItemMarket Market, TradePrice Price);

public static class ItemTrading
{
    private static long Percent(long value, int percent) => checked(value / 100 * percent + value % 100 * percent / 100);
    private static int Commerce(int level, ItemTradingRules rules)
    {
        rules.Validate();
        if (level < 0) throw new RuleViolation("trading.commerce", "Commerce level must be nonnegative");
        return checked((rules.MaximumCommerceLevel.HasValue ? Math.Min(level, rules.MaximumCommerceLevel.Value) : level) * rules.CommercePercentPerLevel);
    }
    public static TradePrice PurchasePrice(long price, int commerceLevel, ItemTradingRules rules)
    {
        if (price < 0) throw new RuleViolation("trading.price", "Negative item price");
        int percent = Math.Min(100, Commerce(commerceLevel, rules));
        long discount = Percent(price, percent);
        return new(price - discount, discount == 0 ? 0 : percent);
    }
    public static TradePrice SalePrice(long price, int commerceLevel, ItemTradingRules rules)
    {
        if (price < 0) throw new RuleViolation("trading.price", "Negative item price");
        int percent = Commerce(commerceLevel, rules);
        return new(Percent(price, checked(rules.SaleBasePercent + percent)), percent);
    }
    private static void Ready(PlayerAccount account, long revision, ItemTradingRules rules)
    {
        account.Validate(); rules.Validate();
        if (revision != account.Revision) throw new RuleViolation("account.revision", "Stale account transaction");
        foreach (var held in account.Inventory)
            if (!rules.Definitions.TryGetValue(held.Key, out var definition)
                || definition.Unique && held.Value > 1 || held.Value > definition.MaximumOwned)
                throw new RuleViolation("trading.inventory", "Inventory does not match trading rules");
        if (account.Inventory.Count > rules.MaximumDistinctItems) throw new RuleViolation("trading.capacity", "Inventory exceeds configured capacity");
    }
    private static void ValidateMarket(ItemMarket market, ItemTradingRules rules)
    {
        if (string.IsNullOrWhiteSpace(market.Id) || market.Offers.Any(o => string.IsNullOrWhiteSpace(o.Id) || !rules.Definitions.ContainsKey(o.ItemId) || o.Remaining < 0)
            || market.Offers.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != market.Offers.Count)
            throw new RuleViolation("trading.market", "Invalid market identity, stock or item reference");
    }
    public static bool Available(PlayerAccount account, ItemTradeOffer offer, ItemTradingRules rules)
    {
        Ready(account, account.Revision, rules);
        if (!rules.Definitions.TryGetValue(offer.ItemId, out var definition) || offer.Remaining < 0)
            throw new RuleViolation("trading.offer", "Invalid item offer");
        long owned = account.Inventory.TryGetValue(offer.ItemId, out long count) ? count : 0;
        return offer.Remaining != 0 && !(definition.Unique && owned > 0) && owned != long.MaxValue
            && (!definition.MaximumOwned.HasValue || owned < definition.MaximumOwned)
            && (owned > 0 || !rules.MaximumDistinctItems.HasValue || account.Inventory.Count < rules.MaximumDistinctItems);
    }
    public static ItemTradeChange Buy(PlayerAccount account, long expectedRevision, ItemMarket market, string offerId, int commerceLevel, ItemTradingRules rules)
    {
        Ready(account, expectedRevision, rules); ValidateMarket(market, rules);
        var offer = market.Offers.SingleOrDefault(o => o.Id == offerId);
        if (offer == null || !Available(account, offer, rules)) throw new RuleViolation("trading.unavailable", "Item cannot be acquired");
        var price = PurchasePrice(rules.Definitions[offer.ItemId].Price, commerceLevel, rules);
        if (account.Medals < price.Medals) throw new RuleViolation("account.medals", "Insufficient medals");
        var inventory = account.Inventory.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        inventory[offer.ItemId] = checked((inventory.TryGetValue(offer.ItemId, out long owned) ? owned : 0) + 1);
        var next = account.Copy() with { Revision = checked(account.Revision + 1), Medals = account.Medals - price.Medals, Inventory = inventory };
        var stock = new ItemMarket(market.Id, market.Offers.Select(o => o with { Remaining = o.Id == offer.Id && o.Remaining.HasValue ? o.Remaining - 1 : o.Remaining }).ToArray());
        next.Validate();
        return new(next, stock, price);
    }
    public static ItemTradeChange Sell(PlayerAccount account, long expectedRevision, ItemMarket market, string itemId, int commerceLevel,
        IReadOnlyCollection<string> equippedItems, ItemTradingRules rules)
    {
        Ready(account, expectedRevision, rules); ValidateMarket(market, rules);
        if (!account.Inventory.TryGetValue(itemId, out long owned) || !rules.Definitions.TryGetValue(itemId, out var definition)
            || Math.Max(equippedItems.LongCount(id => id == itemId), account.Roster.Sum(c => c.Items.LongCount(id => id == itemId))) >= owned)
            throw new RuleViolation("trading.unavailable", "Item is absent or all copies are equipped");
        var price = SalePrice(definition.Price, commerceLevel, rules);
        var inventory = account.Inventory.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (owned == 1) inventory.Remove(itemId); else inventory[itemId] = owned - 1;
        var next = account.Copy() with { Revision = checked(account.Revision + 1), Medals = checked(account.Medals + price.Medals), Inventory = inventory };
        next.Validate();
        return new(next, new(market.Id, market.Offers.Select(o => o with { }).ToArray()), price);
    }
}
