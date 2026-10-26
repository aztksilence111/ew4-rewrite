using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed class LegacyWarzoneTechnology
{
    // ARM64 2c89c/2c99c, table at relative 0x1910d0: cost for current levels -1,0,1,2.
    private static readonly int[][] Costs = [
        [0,3,6,12], [1,4,7,13], [2,5,8,14], [3,6,9,15], [4,7,10,16], [8,9,12,18],
        [0,4,8,15], [3,6,10,17], [6,8,12,19], [9,10,14,21],
        [0,5,10,18], [4,7,12,20], [7,9,14,22], [10,11,16,24],
        [0,6,12,21], [5,8,14,23], [8,10,16,25], [11,12,18,27],
        [0,12,18,22], [6,15,20,25], [12,20,25,30], [0,10,16,20],
        [0,8,16,24], [0,15,25,35], [0,12,20,30], [0,8,12,16]
    ];
    public IReadOnlyDictionary<int, IReadOnlyList<int>> Defaults { get; }
    public string SourceXml { get; }
    public string SourceRevision { get; }
    public IReadOnlyList<int> Snapshot(PlayerAccount account, int warzone)
    {
        if (!Defaults.TryGetValue(warzone, out var defaults))
            throw new RuleViolation("legacy.technology", "Unknown account warzone");
        var result = defaults.ToArray();
        if (account.CampaignTechnology.TryGetValue(warzone, out var saved))
            foreach (var (slot, level) in saved) result[slot] = level;
        return Array.AsReadOnly(result);
    }
    public int? UpgradeCost(int slot, int level)
    {
        if (slot is < 0 or >= 26 || level < -1)
            throw new RuleViolation("technology.slot", "Invalid technology slot or level");
        return level >= 3 ? null : Costs[slot][level + 1];
    }
    public PlayerAccount Upgrade(PlayerAccount account, long revision, int warzone, int slot)
    {
        account.Validate();
        if (revision != account.Revision) throw new RuleViolation("technology.stale", "科技账户已变更，请刷新后重试");
        var snapshot = Snapshot(account, warzone);
        if (slot is < 0 or >= 26) throw new RuleViolation("technology.slot", "无效科技项目");
        int cost = UpgradeCost(slot, snapshot[slot]) ?? throw new RuleViolation("technology.maximum", "科技已满级");
        int balance = CampaignTechnologyStars.Balance(account);
        if (balance < cost) throw new RuleViolation("technology.stars", "战役星数不足");
        var zones = account.CampaignTechnology.ToDictionary(z => z.Key, z => z.Value);
        var levels = zones.GetValueOrDefault(warzone)?.ToDictionary(t => t.Key, t => t.Value) ?? new Dictionary<int, int>();
        levels[slot] = snapshot[slot] + 1; zones[warzone] = new ReadOnlyDictionary<int, int>(levels);
        return account with { Revision = checked(account.Revision + 1), TechnologyStars = balance - cost,
            CampaignTechnology = new ReadOnlyDictionary<int, IReadOnlyDictionary<int, int>>(zones) };
    }
    private LegacyWarzoneTechnology(Dictionary<int, IReadOnlyList<int>> values, string source)
    { Defaults = new ReadOnlyDictionary<int, IReadOnlyList<int>>(values); SourceXml = source;
        SourceRevision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))); }

    public static LegacyWarzoneTechnology Read(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "warzones") throw new RuleViolation("legacy.technology", "Missing warzone technology root");
        var values = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var warzone in document.Root.Elements())
        {
            if (warzone.Name != "warzone" || !int.TryParse((string?)warzone.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
                throw new RuleViolation("legacy.technology", "Invalid warzone identity");
            var levels = new Dictionary<int, int>();
            foreach (var tech in warzone.Elements())
                if (tech.Name != "tech" || !int.TryParse((string?)tech.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    || !int.TryParse((string?)tech.Attribute("level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
                    || index < 0 || index >= 26 || level < -1 || !levels.TryAdd(index, level))
                    throw new RuleViolation("legacy.technology", "Invalid or duplicate legacy technology slot");
            if (levels.Count != 26 || !values.TryAdd(id, Array.AsReadOnly(Enumerable.Range(0, 26).Select(i => levels[i]).ToArray())))
                throw new RuleViolation("legacy.technology", "Incomplete technology table or duplicate warzone");
        }
        if (values.Count == 0) throw new RuleViolation("legacy.technology", "Missing warzones");
        return new(values, xml);
    }

    // Native 7dd18 overwrites only the selected player's table in numeric mode 1.
    public LegacyFaction Apply(LegacyFaction scenario, int mode, bool selectedPlayer, int warzone, IReadOnlyList<int>? accountSnapshot = null)
    {
        if (mode < 1 || mode > 4) throw new RuleViolation("legacy.technology_mode", "Unknown legacy game mode");
        if (mode != 1 || !selectedPlayer) return scenario;
        if (!Defaults.TryGetValue(warzone, out var defaults)) throw new RuleViolation("legacy.technology", "Unknown account warzone");
        var levels = accountSnapshot ?? defaults;
        if (levels.Count != 26 || levels.Any(level => level < -1))
            throw new RuleViolation("legacy.technology", "Invalid saved technology snapshot");
        return scenario with { Values68 = Array.AsReadOnly(levels.ToArray()) };
    }
}
