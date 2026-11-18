using System.Text.Json;
using System.Xml.Linq;

namespace Ew4.Client;

public sealed partial class ClientContent
{
    // Presentation aliases only: saves, equipment, scripts and resource names keep their original IDs.
    public IReadOnlyDictionary<string, string> ItemDisplayIds { get; private set; } = new Dictionary<string, string>();

    private IReadOnlyDictionary<string, string> BuildItemDisplayIds()
    {
        var bindings = Extensions.ItemBindings.Select(b => b.Target).ToHashSet(StringComparer.Ordinal);
        string Key(XElement element)
        {
            int id = (int)element.Attribute("id")!;
            var equipment = Catalogs.Items.Items[id].Equipment;
            if (bindings.Contains(equipment.Id)) return equipment.Id;
            var attributes = new XElement("item", element.Attributes().Where(a => a.Name != "id").OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)
                .Select(a => new XAttribute(a)));
            return attributes.ToString(SaveOptions.DisableFormatting) + "\n" + JsonSerializer.Serialize(equipment with { Id = "" });
        }
        return ItemDefinitions.GroupBy(Key, StringComparer.Ordinal).SelectMany(group =>
        {
            string canonical = "ew4.item." + group.Min(e => (int)e.Attribute("id")!);
            return group.Select(e => new KeyValuePair<string, string>("ew4.item." + (int)e.Attribute("id")!, canonical));
        }).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }
}
