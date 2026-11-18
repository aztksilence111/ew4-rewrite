using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed class LegacyItemTradingCatalog
{
    public IReadOnlyDictionary<int, ItemTradeDefinition> Items { get; }
    public string SourceRevision { get; }
    public string SourceXml { get; }
    private LegacyItemTradingCatalog(Dictionary<int, ItemTradeDefinition> items, string xml)
    {
        Items = new ReadOnlyDictionary<int, ItemTradeDefinition>(items); SourceXml = xml;
        SourceRevision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
    }
    public static LegacyItemTradingCatalog Read(string xml, bool correctOriginalEquipment = false, IReadOnlyDictionary<int, Equipment>? configuredItems = null)
    {
        var root = XDocument.Parse(xml).Root;
        if (root?.Name != "items") throw new RuleViolation("legacy.item_definition", "Unexpected item catalog root");
        var items = new Dictionary<int, ItemTradeDefinition>();
        bool correctItems = correctOriginalEquipment && LegacyEquipmentCorrections.IsOriginal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml))));
        foreach (var element in root.Elements("item"))
        {
            int Integer(string key, int fallback = 0) => (int?)element.Attribute(key) ?? fallback;
            int id = Integer("id", -1), price = Integer("price", -1), consumable = Integer("consumable"), flag = Integer("flag");
            string target = (string?)element.Attribute("target") ?? "all";
            var equipment = new Equipment("ew4.item." + id.ToString(CultureInfo.InvariantCulture), Ew4Commander.Effect(Integer("function")),
                Integer("value"), target == "all" ? null : target == "navy" ? "naval" : target, flag != 0, consumable != 0);
            if (correctItems) equipment = LegacyEquipmentCorrections.Apply(equipment, (string)element.Attribute("name")!);
            if (configuredItems != null) equipment = configuredItems[id];
            if (id < 0 || price < 0 || consumable is not (0 or 1) || flag is not (0 or 1) || string.IsNullOrWhiteSpace(target)
                || !items.TryAdd(id, new(equipment, price, Unique: false)))
                throw new RuleViolation("legacy.item_definition", "Invalid or duplicate item definition");
        }
        return new(items, xml);
    }
    public ItemTradingRules Rules() => new() { Definitions = new ReadOnlyDictionary<string, ItemTradeDefinition>(Items.Values.ToDictionary(i => i.Equipment.Id, StringComparer.Ordinal)) };
    public ItemMarket Import(LegacyMarket source, string id)
    {
        var offers = new List<ItemTradeOffer>();
        for (int i = 0; i < source.Slots.Count; i++)
        {
            var slot = source.Slots[i];
            if (slot.ItemId < 0) continue;
            if (!Items.TryGetValue(slot.ItemId, out var definition) || slot.Quantity < 0)
                throw new RuleViolation("legacy.market_item", "Unknown item or negative stock");
            // Permanent equipment has unlimited stock and may be bought repeatedly.
            offers.Add(new(id + "." + i.ToString(CultureInfo.InvariantCulture), definition.Equipment.Id, definition.Equipment.Consumable ? slot.Quantity : null));
        }
        if (string.IsNullOrWhiteSpace(id)) throw new RuleViolation("legacy.market_identity", "Empty market identity");
        return new(id, offers.AsReadOnly());
    }
}
