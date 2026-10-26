using System.Text.Json;
using System.Text.Json.Serialization;
using Ew4.Core;

namespace Ew4.Legacy;

[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record ArmyCapacityConfiguration
{
    public const string Path = "rewrite/army-capacity.json";
    public int Schema { get; init; } = 1;
    public int BaseCapacity { get; init; } = 8;
    public Dictionary<string, Dictionary<string, int>> FactionBases { get; init; } = new();
    public Dictionary<string, int[]> Buildings { get; init; } = new ArmyCapacityRules().Buildings.ToDictionary(p => p.Key, p => p.Value);

    public static ArmyCapacityConfiguration Read(Func<string, byte[]> read)
    {
        byte[] bytes;
        try { bytes = read(Path); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException)
        { return new(); }
        if (bytes.Length > 2 * 1024 * 1024) throw new RuleViolation("army.capacity_configuration", "Army capacity configuration is too large");
        var document = ExtensionLoader.ParseJson<JsonElement>(bytes, caseSensitiveKeys: true);
        var configuration = document.Deserialize<ArmyCapacityConfiguration>(new JsonSerializerOptions(ExtensionLoader.Json)
            { PropertyNameCaseInsensitive = false, NumberHandling = JsonNumberHandling.Strict })
            ?? throw new RuleViolation("army.capacity_configuration", "Missing army capacity configuration");
        configuration.Validate();
        return configuration;
    }

    public void Validate()
    {
        if (Schema != 1 || FactionBases == null || FactionBases.Any(s => string.IsNullOrWhiteSpace(s.Key) || s.Value == null
            || s.Value.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)))
            throw new RuleViolation("army.capacity_configuration", "Invalid army capacity schema or faction overrides");
        new ArmyCapacityRules { BaseCapacity = BaseCapacity, Buildings = Buildings }.Validate();
    }

    public ArmyCapacityRules Resolve(string scenario, IReadOnlyDictionary<string, string> countries)
    {
        var bases = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string scope in new[] { "*", scenario })
        {
            if (!FactionBases.TryGetValue(scope, out var overrides)) continue;
            foreach (var entry in overrides) bases[entry.Key] = entry.Value;
            foreach (var faction in countries)
            {
                if (overrides.TryGetValue(faction.Value, out int country)) bases[faction.Key] = country;
                if (overrides.TryGetValue(faction.Key, out int exact)) bases[faction.Key] = exact;
            }
        }
        return new ArmyCapacityRules { BaseCapacity = BaseCapacity, FactionBases = bases, Buildings = Buildings }.Copy();
    }
}
