using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed class RuleViolation : Exception
{
    public string Code { get; }
    public RuleViolation(string code, string detail) : base(detail) => Code = code;
}

public sealed class Ew4Random
{
    public ulong State { get; private set; }
    public Ew4Random(ulong seed) => State = seed;
    internal void Commit(ulong state) => State = state;
    public int Next(int bound)
    {
        if (bound < 0) throw new ArgumentOutOfRangeException(nameof(bound));
        if (bound == 0) return 0;
        State = unchecked(State * 214013UL + 2531011UL);
        return (int)(((State >> 16) & 32767UL) % (ulong)bound);
    }
}

public sealed record AttackBand(int StrictMinimumPercent, int Rolls);

public sealed record UnitProfile
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Grade { get; init; } = "";
    public int MaxHealth { get; init; }
    public int Movement { get; init; }
    public int MinimumAttack { get; init; }
    public int MaximumAttack { get; init; }
    public int MinimumRange { get; init; } = 1;
    public int MaximumRange { get; init; } = 1;
    public IReadOnlyList<AttackBand> AttackBands { get; init; } = Array.Empty<AttackBand>();
    public IReadOnlyDictionary<string, float> DamageMultipliers { get; init; } = new Dictionary<string, float>();
    public IReadOnlyCollection<string> Traits { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SkillSlot> SkillSlots { get; init; } = Array.Empty<SkillSlot>();
    public IReadOnlyList<string> LegacySkillOverrides { get; init; } = Array.Empty<string>();
    public int KillExperience { get; init; } = 1;
    public int IgnitionNumerator { get; init; }
    public int IgnitionDenominator { get; init; } = 1;

    public bool HasTrait(string trait) => Traits.Contains(trait);
    public void Validate()
    {
        if (FrozenUnitProfiles.IsValidated(this)) return;
        ValidateCore();
        FrozenUnitProfiles.Validated(this);
    }
    private void ValidateCore()
    {
        SkillSlot.Validate(SkillSlots);
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Category) || MaxHealth <= 0
            || Movement < 0 || MinimumAttack < 0 || MaximumAttack < 0 || MinimumRange < 0
            || MaximumRange < MinimumRange || AttackBands.Any(b => b.Rolls <= 0) || KillExperience < 0
            || IgnitionNumerator < 0 || IgnitionDenominator < 1)
            throw new RuleViolation("profile.invalid", $"Invalid unit profile: {Id}");
        if (AttackBands.Select(b => b.StrictMinimumPercent).Distinct().Count() != AttackBands.Count)
            throw new RuleViolation("profile.attack_bands", $"Duplicate thresholds: {Id}");
        if (DamageMultipliers.Any(p => float.IsNaN(p.Value) || float.IsInfinity(p.Value) || p.Value < 0))
            throw new RuleViolation("profile.multiplier", $"Invalid multiplier: {Id}");
    }
}

public sealed record Combatant
{
    public UnitProfile Profile { get; init; } = new();
    public int Health { get; init; }
    public int Morale { get; init; }
    public int ConstructionTurns { get; init; }
    public IReadOnlyCollection<string> Skills { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SkillSlot> SkillSlots { get; init; } = Array.Empty<SkillSlot>();
    public int Stars { get; init; }
    public IReadOnlyList<SkillStatus> Statuses { get; init; } = Array.Empty<SkillStatus>();
    public int AttackBonus { get; init; }
    public int DefenseBonus { get; init; }
    public int Armor { get; init; }
    public bool AtSea { get; init; }
    public bool FullAttackRolls { get; init; }
    public bool SeaImmunity { get; init; }
    public bool Geography { get; init; }
    public bool FireImmune { get; init; }
    public string? BuildingKind { get; init; }
    public float Evasion { get; init; }
    public float EvasionIgnoringBuilding { get; init; }
    public bool Has(string skill) => !Profile.LegacySkillOverrides.Contains(skill)
        && (Skills.Contains(skill) || Profile.SkillSlots.Any(s => s.Id == skill));
    public void Validate()
    {
        if (FrozenCombatants.IsValidated(this)) return;
        PlanningScope.Validate(this, ValidateCore);
        FrozenCombatants.Validated(this);
    }
    private void ValidateCore()
    {
        PlanningScope.Validate(Profile, Profile.Validate);
        SkillSlot.Validate(Profile.SkillSlots.Concat(SkillSlots));
        if (Statuses == null || Statuses.Count > 64 || Statuses.Any(s => s == null || string.IsNullOrWhiteSpace(s.Id)
            || s.Id.Length > 128 || s.RemainingTurns is < 1 or > 1000 || s.Level < 1 || s.Stacks is < 1 or > 32)
            || Statuses.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != Statuses.Count)
            throw new RuleViolation("combatant.status", "Invalid saved status");
        if (Health <= 0 || Health > Profile.MaxHealth || Morale < -3 || Morale > sbyte.MaxValue || Stars < 0
            || ConstructionTurns < 0 || Evasion < 0 || Evasion > 1 || float.IsNaN(Evasion)
            || EvasionIgnoringBuilding < 0 || EvasionIgnoringBuilding > 1 || float.IsNaN(EvasionIgnoringBuilding))
            throw new RuleViolation("combatant.invalid", $"Invalid combatant: {Profile.Id}");
    }
}

public sealed record CombatResult(int DefenderDamage, int AttackerDamage, bool Counterattack,
    bool OffensiveProc, bool DefensiveProc, ulong RandomState);

public sealed record CombatRules
{
    public LegacySkillParameters? LegacySkills { get; init; }
    public int StarRollCap { get; init; } = 5;
    public int MinimumDamage { get; init; } = 1;
    public bool LegacySiegeRetaliationBuildingType { get; init; }
    public bool LethalStrikePreventsCounterattack { get; init; }
    public RuleEventBus? Extensions { get; init; }
}

// Original skill semantics live in this compatibility module, not a fixed registry.
public static class Ew4Combat
{
    public static string Skill(int legacyId) => "ew4.skill." + legacyId;

    public static int AttackCount(Combatant unit)
    {
        int percent = (int)((long)unit.Health * 100 / unit.Profile.MaxHealth);
        if ((unit.Profile.Category == "infantry" && unit.Has(Skill(15))) || unit.FullAttackRolls)
            percent = 100;
        return unit.Profile.AttackBands.Where(b => percent > b.StrictMinimumPercent)
            .OrderByDescending(b => b.StrictMinimumPercent).Select(b => b.Rolls).DefaultIfEmpty(1).First();
    }

    public static CombatResult Calculate(Combatant attacker, Combatant defender, int distance,
        Ew4Random random, CombatRules? configuration = null)
    {
        attacker.Validate();
        defender.Validate();
        var rules = configuration ?? new CombatRules();
        if (distance < 0 || rules.StarRollCap < 0 || rules.MinimumDamage < 1)
            throw new RuleViolation("combat.rules", "Invalid combat rules or distance");
        var callerRandom = random;
        random = new Ew4Random(random.State);
        var a = attacker;
        var d = defender;
        int ac = AttackCount(a), dc = AttackCount(d);
        if (rules.Extensions != null)
        {
            ac = Math.Max(1, checked((int)rules.Extensions.Modify("combat.attack_count", "attack_count_add", ac, a, d, random)));
            dc = Math.Max(1, checked((int)rules.Extensions.Modify("combat.attack_count", "attack_count_add", dc, d, a, random)));
        }
        bool counter = !a.Profile.HasTrait("no_retaliation") && !d.Profile.HasTrait("no_retaliation")
            && distance >= d.Profile.MinimumRange && distance <= d.Profile.MaximumRange
            && d.Morale != -3 && !(d.Profile.Category == "fort" && d.ConstructionTurns > 0);
        if (rules.Extensions != null) counter = counter && rules.Extensions.Modify("combat.counterattack", "counterattack", 1, a, d, random) > 0;
        int am = MoraleModifier(a.Morale, ac), dm = MoraleModifier(d.Morale, dc);
        int aa = am + a.AttackBonus + Math.Min(ac, rules.StarRollCap) * a.Stars;
        int da = dm + d.AttackBonus + Math.Min(dc, rules.StarRollCap) * d.Stars;
        var skills = rules.LegacySkills ?? LegacySkillParameters.Default;
        bool offensive = a.Has(Skill(31)) && random.Next(100) < skills.Percent(31);
        bool defensive = d.Has(Skill(32)) && random.Next(100) < skills.Percent(32);
        int attack = Rolls(a, ac, offensive, random, skills);
        int dealt = Damage(a, d, aa + attack - Math.Min(0, dm) - d.DefenseBonus - d.Armor, false, rules, random);
        if (defensive) dealt = rules.MinimumDamage;
        if (rules.LethalStrikePreventsCounterattack && dealt >= d.Health) counter = false;
        int retaliation = counter ? Rolls(d, dc, false, random, skills) : 0;
        int taken = counter ? Damage(d, a, da + retaliation - Math.Min(0, am) - a.DefenseBonus - a.Armor, true, rules, random) : 0;
        var result = new CombatResult(dealt, taken, counter, offensive, defensive, random.State);
        callerRandom.Commit(random.State);
        return result;
    }

    private static int MoraleModifier(int morale, int rolls) =>
        rolls * (morale == 1 ? 1 : morale == -1 ? -1 : morale < -1 ? -2 : 0);

    public static (int Minimum, int Maximum) AttackBounds(Combatant unit, LegacySkillParameters? skillParameters = null)
    {
        var skills = skillParameters ?? LegacySkillParameters.Default;
        int? lowerSkill = unit.Profile.Category switch { "infantry" => 13, "cavalry" => 11, "artillery" => 7, _ => null };
        int? upperSkill = unit.Profile.Category switch { "infantry" => 12, "cavalry" => 10, "artillery" => 8, _ => null };
        int min = checked(unit.Profile.MinimumAttack + (lowerSkill.HasValue && unit.Has(Skill(lowerSkill.Value)) ? skills.Bonus(lowerSkill.Value) : 0));
        int max = checked(unit.Profile.MaximumAttack + (upperSkill.HasValue && unit.Has(Skill(upperSkill.Value)) ? skills.Bonus(upperSkill.Value) : 0));
        return (min, Math.Max(min, max));
    }

    private static int Rolls(Combatant unit, int count, bool maximumRoll, Ew4Random random, LegacySkillParameters skills)
    {
        var (min, max) = AttackBounds(unit, skills);
        int total = 0;
        for (int i = 0; i < count; i++)
            total = checked(total + (max <= min ? min : maximumRoll ? max : Math.Max(random.Next(checked(max + 1)), min)));
        return total;
    }

    private static int Damage(Combatant source, Combatant target, int raw, bool retaliation, CombatRules rules, Ew4Random random)
    {
        if (!source.Profile.DamageMultipliers.TryGetValue(target.Profile.Category, out float factor))
            throw new RuleViolation("combat.multiplier_missing", $"{source.Profile.Id} -> {target.Profile.Category}");
        if (source.AtSea && source.Profile.Category != "naval" && !source.Has(Skill(17)) && !source.SeaImmunity) factor *= .8f;
        var skills = rules.LegacySkills ?? LegacySkillParameters.Default;
        if (target.Profile.Category == "naval" && target.Has(Skill(16))) factor *= skills.Multiplier(16);
        if (target.Profile.Category == "fort" && source.Has(Skill(4))) factor *= skills.Multiplier(4);
        if (source.Profile.HasTrait("siege_gun"))
        {
            if (target.Profile.Category == "fort") factor *= 1.8f;
            else if (target.BuildingKind != null &&
                (retaliation && rules.LegacySiegeRetaliationBuildingType ? source.BuildingKind : target.BuildingKind) != "farmland")
                factor *= 1.4f;
        }
        if (source.Profile.HasTrait("coastal_gun"))
        {
            if (target.Profile.Category == "naval") factor += factor;
            else if (!target.AtSea) factor *= .5f;
        }
        int? ignoreSkill = source.Profile.Category switch { "infantry" => 14, "cavalry" => 9, "artillery" => 5, _ => null };
        bool ignore = ignoreSkill.HasValue && source.Has(Skill(ignoreSkill.Value));
        float evasion = ignore ? 0 : source.Has(Skill(29)) && target.BuildingKind != null ? target.EvasionIgnoringBuilding : target.Evasion;
        float combined = factor * (1f - evasion);
        double damage = Math.Max(rules.MinimumDamage, checked((int)(raw * combined)));
        if (rules.Extensions != null)
        {
            damage = rules.Extensions.Modify("combat.modify_damage", "damage_multiplier", damage, source, target, random);
            damage = rules.Extensions.Modify("combat.modify_damage", "damage_add", damage, source, target, random);
            damage = rules.Extensions.Modify("combat.modify_incoming_damage", "damage_multiplier", damage, target, source, random);
            damage = rules.Extensions.Modify("combat.modify_incoming_damage", "damage_add", damage, target, source, random);
        }
        return Math.Max(rules.MinimumDamage, checked((int)damage));
    }
}
