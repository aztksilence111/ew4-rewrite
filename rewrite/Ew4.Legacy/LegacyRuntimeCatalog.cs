using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record LegacyUnitDefinition(UnitProfile Profile, long FoodConsumption);
public sealed record LegacyCommanderDefinition(Commander Definition, int Rank, int Title, int Training,
    int HealthBonus, int TitleHealing, int? RankExperiencePercent, int? TitleExperiencePercent);

public sealed class LegacyRuntimeCatalog
{
    private static readonly string[] UnitKeys = ["militia", "line_infantry", "light_infantry", "grenadier", "guards", "machine_gun",
        "light_cavalry", "heavy_cavalry", "guards_cavalry", "armored_car", "light_artillery", "heavy_artillery", "siege_artillery",
        "rocket", "privateer", "frigate", "battleship", "ironclad", "small_fortress", "fortress", "large_fortress", "coastal_fort"];
    public IReadOnlyDictionary<string, LegacyUnitDefinition> Profiles { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Variants { get; }
    public IReadOnlyDictionary<int, LegacyCommanderDefinition> Commanders { get; }
    public IReadOnlyDictionary<int, Equipment> Items { get; }
    public IReadOnlyDictionary<string, string> SourceHashes { get; }
    public JsonElement SourceTrees { get; }
    public JsonElement NativeTables { get; }
    public LegacySkillParameters? SkillParameters { get; }
    public string DefinitionRevision { get; private set; } = "";
    private LegacyRuntimeCatalog(Dictionary<string, LegacyUnitDefinition> profiles, Dictionary<string, IReadOnlyDictionary<string, string>> variants,
        Dictionary<int, LegacyCommanderDefinition> commanders, Dictionary<int, Equipment> items, Dictionary<string, string> sources,
        JsonElement trees, JsonElement tables, LegacySkillParameters? skillParameters)
    {
        Profiles = new ReadOnlyDictionary<string, LegacyUnitDefinition>(profiles);
        Variants = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(variants);
        Commanders = new ReadOnlyDictionary<int, LegacyCommanderDefinition>(commanders); Items = new ReadOnlyDictionary<int, Equipment>(items);
        SourceHashes = new ReadOnlyDictionary<string, string>(sources); SourceTrees = trees.Clone(); NativeTables = tables.Clone();
        SkillParameters = skillParameters;
    }

    public void VerifySources(Func<string, byte[]> readSource)
    {
        foreach (var source in SourceHashes)
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(readSource(source.Key))), source.Value, StringComparison.OrdinalIgnoreCase))
                throw new RuleViolation("legacy.source_changed", "Imported source differs: " + source.Key);
    }

    public LegacyUnitDefinition Unit(string variant, int legacyId, int grade)
    {
        if (legacyId < 0 || legacyId >= UnitKeys.Length || grade < 0 || grade > 2
            || string.IsNullOrWhiteSpace(variant))
            throw new RuleViolation("legacy.unit_definition", "Unknown legacy unit/grade/variant");
        // 702a4 falls back to the explicit 'others' table when the country has no entry.
        var choices = Variants.TryGetValue(variant, out var exact) ? exact
            : Variants.TryGetValue("others", out var fallback) ? fallback
            : throw new RuleViolation("legacy.unit_definition", "Missing original country fallback");
        if (!choices.TryGetValue(UnitKeys[legacyId] + "." + grade, out string? id))
            throw new RuleViolation("legacy.unit_definition", $"Missing legacy unit grade: variant={variant}, unit={legacyId}, grade={grade}");
        return Profiles[id];
    }

    public bool HasUnit(string variant, int legacyId, int grade)
    {
        if (legacyId < 0 || legacyId >= UnitKeys.Length || grade is < 0 or > 2 || string.IsNullOrWhiteSpace(variant)) return false;
        var choices = Variants.TryGetValue(variant, out var exact) ? exact : Variants.GetValueOrDefault("others");
        return choices?.ContainsKey(UnitKeys[legacyId] + "." + grade) == true;
    }

    public LegacyUnitDefinition SerializedUnit(string variant, int legacyId, int grade)
    {
        // 702a4 indexes a flat table at unit * 3 + grade, including nonstandard BTL grades.
        long slot = (long)legacyId * 3 + grade;
        if (legacyId < 0 || grade < 0 || slot < 0 || slot >= UnitKeys.Length * 3)
            throw new RuleViolation("legacy.unit_definition", $"Invalid serialized unit slot: unit={legacyId}, grade={grade}, variant={variant}");
        return Unit(variant, (int)slot / 3, (int)slot % 3);
    }

    public static int UnitIndex(string resourceName)
    {
        int index = Array.IndexOf(UnitKeys, resourceName.Replace(' ', '_').ToLowerInvariant());
        return index >= 0 ? index : throw new RuleViolation("legacy.unit_definition", "Unknown legacy unit name");
    }

    public static LegacyRuntimeCatalog FromJson(string json, bool correctOriginalEquipment = false, LegacySkillParameters? skillParameters = null,
        Func<Equipment, Equipment>? configureItem = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schema_version").GetInt32() != 2) throw new RuleViolation("legacy.catalog_version", "Unknown catalog version");
        var trees = root.GetProperty("source_trees"); var tables = root.GetProperty("native_tables");
        var skillIds = root.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var overridden = root.TryGetProperty("overridden_skills", out var overrides) ? overrides.EnumerateArray().Select(s => s.GetString()!).ToArray() : [];
        var profiles = new Dictionary<string, LegacyUnitDefinition>(StringComparer.Ordinal);
        foreach (var value in root.GetProperty("profiles").EnumerateArray())
        {
            string Text(string key) => value.GetProperty(key).GetString()!;
            int Int(string key) => value.GetProperty(key).GetInt32();
            var profile = new UnitProfile { Id = Text("id"), Category = Text("category"), Grade = Text("grade"),
                SkillSlots = value.TryGetProperty("skill_slots", out var unitSlots) ? unitSlots.Deserialize<SkillSlot[]>(ExtensionLoader.Json)! : [],
                LegacySkillOverrides = overridden,
                MaxHealth = Int("max_health"), Movement = Int("movement"), MinimumAttack = Int("minimum_attack"), MaximumAttack = Int("maximum_attack"),
                KillExperience = Int("kill_experience"), IgnitionNumerator = Int("ignition_numerator"), IgnitionDenominator = Int("ignition_denominator"),
                MinimumRange = Int("minimum_range"), MaximumRange = Int("maximum_range"),
                AttackBands = Array.AsReadOnly(value.GetProperty("attack_bands").EnumerateArray().Select(b =>
                    new AttackBand(b.GetProperty("strict_minimum_percent").GetInt32(), b.GetProperty("rolls").GetInt32())).ToArray()),
                Traits = Array.AsReadOnly(value.GetProperty("traits").EnumerateArray().Select(t => t.GetString()!).ToArray()),
                DamageMultipliers = new ReadOnlyDictionary<string, float>(value.GetProperty("damage_multipliers").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle(), StringComparer.Ordinal)) };
            profile.Validate();
            long food = value.GetProperty("food_consumption").GetInt64();
            if (food < 0 || !profiles.TryAdd(profile.Id, new(profile, food))) throw new RuleViolation("legacy.catalog_profile", "Invalid or duplicate profile");
        }
        var variants = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var variant in root.GetProperty("variants").EnumerateObject())
        {
            var values = variant.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
            if (values.Any(p => !profiles.ContainsKey(p.Value))) throw new RuleViolation("legacy.catalog_reference", "Unknown variant profile");
            variants.Add(variant.Name, new ReadOnlyDictionary<string, string>(values));
        }
        var items = new Dictionary<int, Equipment>();
        bool correctItems = correctOriginalEquipment && LegacyEquipmentCorrections.IsOriginal(
            root.GetProperty("source_sha256").GetProperty("def_item.xml").GetString()!);
        foreach (var item in trees.GetProperty("def_item.xml").GetProperty("children").EnumerateArray())
        {
            var attrs = item.GetProperty("attributes");
            int Integer(string key, int fallback = 0) => attrs.TryGetProperty(key, out var v) ? int.Parse(v.GetString()!, CultureInfo.InvariantCulture) : fallback;
            int id = Integer("id"); string target = attrs.GetProperty("target").GetString()!;
            var equipment = new Equipment("ew4.item." + id, Ew4Commander.Effect(Integer("function")), Integer("value"),
                target == "all" ? null : target == "navy" ? "naval" : target, Integer("flag") != 0, Integer("consumable") != 0);
            if (correctItems) equipment = LegacyEquipmentCorrections.Apply(equipment, attrs.GetProperty("name").GetString()!);
            items.Add(id, configureItem?.Invoke(equipment) ?? equipment);
        }
        int[] health = tables.GetProperty("commander_rank_health_bonus").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        int[] healing = tables.GetProperty("commander_nobility_healing").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var commanders = new Dictionary<int, LegacyCommanderDefinition>();
        foreach (var value in root.GetProperty("commanders").EnumerateArray())
        {
            var attrs = value.GetProperty("legacy_attributes");
            int Integer(string key) => int.Parse(attrs.GetProperty(key).GetString()!, CultureInfo.InvariantCulture);
            int id = Integer("id"), rank = Integer("rank"), title = Integer("nobilityrank");
            if (rank < 0 || rank >= health.Length || title < 0 || title >= healing.Length)
                throw new RuleViolation("legacy.commander_rank", $"Unknown commander rank or title: id={id}, name={attrs.GetProperty("name").GetString()}, rank={rank} (0..{health.Length - 1}), nobilityrank={title} (0..{healing.Length - 1})");
            var equipped = new List<Equipment>();
            foreach (string slot in new[] { "item1", "item2" })
            {
                int itemId = Integer(slot);
                if (itemId < 0) continue;
                if (!items.TryGetValue(itemId, out var item) || item.Consumable) throw new RuleViolation("legacy.commander_item", "Missing or consumable equipped item");
                equipped.Add(item);
            }
            var stars = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string category in new[] { "infantry", "cavalry", "artillery", "warship", "fort" })
                stars.Add(category == "warship" ? "naval" : category, Integer(category));
            var commander = new Commander { Id = value.GetProperty("id").GetString()!, DisplayName = attrs.GetProperty("name").GetString()!,
                SkillSlots = value.TryGetProperty("skill_slots", out var commanderSlots) ? commanderSlots.Deserialize<SkillSlot[]>(ExtensionLoader.Json)! : [],
                LegacySkillOverrides = overridden,
                Movement = Integer("movement"), Stars = new ReadOnlyDictionary<string, int>(stars), Items = equipped.AsReadOnly(),
                Skills = Array.AsReadOnly(value.GetProperty("skills").EnumerateArray().Select(s => s.GetString()!).ToArray()) };
            if (string.IsNullOrWhiteSpace(commander.Id) || commander.Movement < 0 || stars.Values.Any(v => v < 0)
                || commander.Skills.Any(s => !skillIds.Contains(s)) || Integer("training") < 0 || health[rank] < 0 || healing[title] < 0)
                throw new RuleViolation("legacy.commander_definition", "Invalid commander attributes or skill reference");
            int? Percent(int effect) => equipped.Where(i => i.Effect == Ew4Commander.Effect(effect)).Select(i => (int?)i.Value).FirstOrDefault();
            commanders.Add(id, new(commander, rank, title, Integer("training"), health[rank], healing[title], Percent(1), Percent(2)));
        }
        var sources = root.GetProperty("source_sha256").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
        return new(profiles, variants, commanders, items, sources, trees, tables, skillParameters) {
            DefinitionRevision = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))) };
    }
}
