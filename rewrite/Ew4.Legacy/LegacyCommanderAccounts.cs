using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed class LegacyRosterCommander
{
    private readonly byte[] source;
    public int LegacyId { get; }
    public CommanderLearningState Learning { get; }
    public int Training { get; }
    public IReadOnlyList<int> Items { get; }
    private LegacyRosterCommander(byte[] raw, int id, CommanderLearningState learning, int training, int[] items)
    { source = raw; LegacyId = id; Learning = learning; Training = training; Items = Array.AsReadOnly(items); }
    public byte[] ToBytes() => (byte[])source.Clone();
    public static LegacyRosterCommander Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 60) throw new RuleViolation("legacy.roster_record", "Expected a 60-byte commander record");
        var abilities = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < 7; i++) abilities.Add(LegacyCommanderAccounts.Abilities[i], LegacyBytes.I32(raw, 20 + i * 4));
        var learning = new CommanderLearningState(new(LegacyBytes.I32(raw, 4), LegacyBytes.I32(raw, 12)),
            new(LegacyBytes.I32(raw, 8), LegacyBytes.I32(raw, 16)), new ReadOnlyDictionary<string, int>(abilities));
        return new(raw.ToArray(), LegacyBytes.I32(raw, 0), learning, LegacyBytes.I32(raw, 48), [LegacyBytes.I32(raw, 52), LegacyBytes.I32(raw, 56)]);
    }
}

public sealed class LegacyCommanderAccounts
{
    public static IReadOnlyList<string> Abilities { get; } = Array.AsReadOnly(new[] { "infantry", "cavalry", "artillery", "naval", "fort", "commerce", "movement" });
    private readonly LegacyRuntimeCatalog runtime;
    public IReadOnlyDictionary<int, int> TitleCommerce { get; }
    public IReadOnlyDictionary<int, int> CommanderCommerce { get; }
    public CommanderInheritanceRules Inheritance { get; }
    public string SourceRevision { get; }
    // Verified stock 0.2.19 -> schema-3 split: definitions are unchanged, only their encoding differs.
    public bool CanMigrateFrom(string revision) =>
        revision == "e47fb936518323a29848404514de602e25bfc04bbc5fafa191485fa32c7d39e5"
        && SourceRevision == "73e030e5c7d0f9a227b27cc1eb43cbe28f158177b02104788d02bca563ea12eb";
    private LegacyCommanderAccounts(LegacyRuntimeCatalog runtime, Dictionary<int, int> title, Dictionary<int, int> commanders, CommanderInheritanceRules inheritance, string revision)
    { this.runtime = runtime; TitleCommerce = new ReadOnlyDictionary<int, int>(title); CommanderCommerce = new ReadOnlyDictionary<int, int>(commanders); Inheritance = inheritance; SourceRevision = revision; }
    public static LegacyCommanderAccounts Read(LegacyRuntimeCatalog runtime, LegacyCombatCatalog combat, string nativeTables)
    {
        using var document = JsonDocument.Parse(nativeTables);
        var data = document.RootElement;
        if (data.GetProperty("schema_version").GetInt32() != 1 || data.GetProperty("source_sha256").GetString() != combat.NativeSourceHash)
            throw new RuleViolation("legacy.metagame_source", "Metagame tables differ from the combat native source");
        var commerce = data.GetProperty("title_commerce").EnumerateArray().Select((v, i) => (i, v: v.GetInt32())).ToDictionary(p => p.i, p => p.v);
        if (commerce.Count != combat.TitleProgression.Levels.Count || commerce.Values.Any(v => v < 0))
            throw new RuleViolation("legacy.metagame_tables", "Invalid title commerce stages");
        var skills = combat.SkillParameters ?? LegacySkillParameters.Default;
        var inheritance = new CommanderInheritanceRules { Rank = combat.RankProgression, Title = combat.TitleProgression,
            RankPercent = data.GetProperty("rank_percent").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
            TitlePercent = data.GetProperty("title_percent").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
            Abilities = Abilities.Select((id, i) => new InheritedAbilityRule(Ew4Combat.Skill(33 + i), id, skills.Gain(33 + i), skills.Maximum(33 + i))).ToArray() };
        inheritance.Validate();
        var business = runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children").EnumerateArray()
            .Select(v => v.GetProperty("attributes")).ToDictionary(v => int.Parse(v.GetProperty("id").GetString()!, CultureInfo.InvariantCulture),
                v => int.Parse(v.GetProperty("business").GetString()!, CultureInfo.InvariantCulture));
        if (business.Count != runtime.Commanders.Count || business.Any(p => !runtime.Commanders.ContainsKey(p.Key) || p.Value < 0))
            throw new RuleViolation("legacy.metagame_tables", "Commander commerce attributes disagree with the catalog");
        string revision = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { nativeTables, combat.SourceRevision, runtime.SourceHashes, runtime.DefinitionRevision })));
        return new(runtime, commerce, business, inheritance, revision);
    }
    public AccountCommanderRules Rules() => new() { Inheritance = Inheritance,
        Definitions = new ReadOnlyDictionary<string, Commander>(runtime.Commanders.Values.Select(c => c.Definition).ToDictionary(c => c.Id, StringComparer.Ordinal)),
        Items = new ReadOnlyDictionary<string, Equipment>(runtime.Items.Values.ToDictionary(i => i.Id, StringComparer.Ordinal)) };
    public RosterDeploymentRules DeploymentRules(LegacyCombatCatalog combat) => new() { Account = Rules(),
        RankHealth = combat.RankHealth.ToDictionary(p => int.Parse(p.Key, CultureInfo.InvariantCulture), p => p.Value),
        Assignment = new() { LegacySkills = combat.SkillParameters, TitleHealing = combat.TitleHealing.ToDictionary(p => int.Parse(p.Key, CultureInfo.InvariantCulture), p => p.Value),
            DeploymentBuilding = BuildingRequirement.Any } };
    public OwnedCommander FromDefinition(int id, string instanceId)
    {
        if (!runtime.Commanders.TryGetValue(id, out var source)) throw new RuleViolation("legacy.roster_definition", "Unknown owned commander definition");
        var abilities = source.Definition.Stars.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        abilities.Add("movement", source.Definition.Movement); abilities.Add("commerce", CommanderCommerce[id]);
        var result = new OwnedCommander(instanceId, source.Definition.Id, new(new(source.Rank, 0), new(source.Title, 0), abilities), source.Training,
            source.Definition.Items.Select(i => i.Id).ToArray());
        result.Validate(); return result;
    }
    public OwnedCommander Import(LegacyRosterCommander source, string instanceId)
    {
        if (!runtime.Commanders.TryGetValue(source.LegacyId, out var definition) || source.Items.Any(i => i >= 0 && !runtime.Items.ContainsKey(i)))
            throw new RuleViolation("legacy.roster_definition", "Unknown owned commander or item reference");
        var result = new OwnedCommander(instanceId, definition.Definition.Id, source.Learning, source.Training,
            source.Items.Where(i => i >= 0).Select(i => runtime.Items[i].Id).ToArray());
        result.Validate();
        if (result.Learning.Rank.Level >= Inheritance.Rank.Levels.Count || result.Learning.Title.Level >= Inheritance.Title.Levels.Count)
            throw new RuleViolation("legacy.roster_progression", "Unknown owned commander progression stage");
        AccountCommanders.Resolve(result, Rules()); return result.Copy();
    }
}
