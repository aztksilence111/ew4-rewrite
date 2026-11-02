using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record Equipment(string Id, string Effect, int Value, string? TargetCategory = null,
    bool RequiresFlagSkill = false, bool Consumable = false)
{
    public IReadOnlyList<SkillSlot>? SkillSlots { get; init; }
    public SkillSlot? UseSkill { get; init; }
}

public sealed record Commander
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public IReadOnlyCollection<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SkillSlot> SkillSlots { get; init; } = Array.Empty<SkillSlot>();
    public IReadOnlyList<string> LegacySkillOverrides { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Equipment> Items { get; init; } = Array.Empty<Equipment>();
    public int Movement { get; init; }
    public IReadOnlyDictionary<string, int> Stars { get; init; } = new Dictionary<string, int>();
    public bool HasSkill(int legacyId) => Skills.Contains(Ew4Combat.Skill(legacyId)) && !LegacySkillOverrides.Contains(Ew4Combat.Skill(legacyId));
}

public sealed record CommanderEffects(int MinimumAttackBonus, int MaximumAttackBonus, int MovementBonus,
    int AttackEquipment, int DefenseEquipment, int FieldHealing, int AdjacentAttack, int AdjacentDefense,
    int AdjacentHealing, bool Geography, bool SeaImmunity, bool FullAttackRolls, bool FoodExempt, float EconomicMultiplier);

public static class Ew4Commander
{
    public static Combatant Attach(Combatant unit, Commander commander, LegacySkillParameters? skillParameters = null)
    {
        var effects = Resolve(commander, unit.Profile.Category, skillParameters);
        return unit with
        {
            Skills = commander.Skills.Where(s => !commander.LegacySkillOverrides.Contains(s)
                && !unit.Profile.LegacySkillOverrides.Contains(s)).ToArray(),
            SkillSlots = (commander.SkillSlots.Count > 0 ? commander.SkillSlots
                : commander.Skills.Select((id, i) => new SkillSlot(id, "legacy." + i)))
                .Concat(commander.Items.Where(i => i.TargetCategory == null || i.TargetCategory == unit.Profile.Category)
                    .SelectMany(i => i.SkillSlots ?? Array.Empty<SkillSlot>())).ToArray(),
            Stars = commander.Stars.TryGetValue(unit.Profile.CommanderAbility ?? unit.Profile.Category, out int stars) ? stars : 0,
            AttackBonus = checked(unit.AttackBonus + effects.AttackEquipment),
            DefenseBonus = checked(unit.DefenseBonus + effects.DefenseEquipment),
            FullAttackRolls = effects.FullAttackRolls,
            SeaImmunity = effects.SeaImmunity,
            Geography = effects.Geography,
            FireImmune = commander.HasSkill(2) || commander.Items.Any(i => i.Effect == Effect(16)),
            Profile = unit.Profile with { Movement = checked(unit.Profile.Movement + effects.MovementBonus) }
        };
    }

    public static string Effect(int legacyId) => "ew4.item_effect." + legacyId;
    public static CommanderEffects Resolve(Commander commander, string category, LegacySkillParameters? skillParameters = null)
    {
        var skills = skillParameters ?? LegacySkillParameters.Default;
        int Sum(int effect, bool targeted = false) => commander.Items
            .Where(i => i.Effect == Effect(effect) && (!targeted || i.TargetCategory == null || i.TargetCategory == category))
            .Sum(i => i.Value);
        bool HasItem(int effect) => commander.Items.Any(i => i.Effect == Effect(effect));
        int? min = category switch { "infantry" => 13, "cavalry" => 11, "artillery" => 7, _ => null };
        int? max = category switch { "infantry" => 12, "cavalry" => 10, "artillery" => 8, _ => null };
        int movement = category == "naval" ? (commander.HasSkill(18) ? skills.Bonus(18) : 0) : commander.Movement;
        if (category == "artillery" && commander.HasSkill(6)) movement = checked(movement + skills.Bonus(6));
        movement += Sum(9, true);
        float economics = commander.HasSkill(24) ? skills.Multiplier(24) : commander.HasSkill(23) ? skills.Multiplier(23) : 1f;
        var economicItem = commander.Items.Where(i => i.Effect == Effect(3)).OrderByDescending(i => i.Value).FirstOrDefault();
        if (economicItem != null) economics += economicItem.Value / 100f;
        return new CommanderEffects(min.HasValue && commander.HasSkill(min.Value) ? skills.Bonus(min.Value) : 0,
            max.HasValue && commander.HasSkill(max.Value) ? skills.Bonus(max.Value) : 0, movement, Sum(11, true), Sum(10, true),
            Sum(5), Sum(14), Sum(15), Sum(13), commander.HasSkill(1) || HasItem(0),
            commander.HasSkill(17) || HasItem(12), HasItem(4), commander.HasSkill(22), economics);
    }

    public static Commander Equip(Commander commander, Equipment item, int? capacity)
    {
        if (capacity < 0 || item.Consumable || commander.Items.Any(i => i.Id == item.Id)
            || (capacity.HasValue && commander.Items.Count >= capacity.Value)
            || (item.RequiresFlagSkill && !commander.HasSkill(0)))
            throw new RuleViolation("equipment.unavailable", $"Cannot equip {item.Id} on {commander.Id}");
        return commander with { Items = commander.Items.Concat(new[] { item }).ToArray() };
    }
}
