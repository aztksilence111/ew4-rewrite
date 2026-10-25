using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record AiUnitFacts
{
    public string Category { get; init; } = "";
    public string Grade { get; init; } = "0";
    public int Health { get; init; }
    public int MaximumHealth { get; init; }
    public int ExperienceLevel { get; init; }
    public bool AtSea { get; init; }
    public bool HasCommander { get; init; }
    public bool Transport { get; init; }
    public bool UnderConstruction { get; init; }
    public bool PriorityCommander { get; init; }
    public IReadOnlyCollection<string> Traits { get; init; } = Array.Empty<string>();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Category) || string.IsNullOrWhiteSpace(Grade) || Health < 1 || Health > MaximumHealth
            || ExperienceLevel < 0 || Traits.Any(string.IsNullOrWhiteSpace))
            throw new RuleViolation("ai.unit", "Invalid tactical unit facts");
    }
}
public sealed record AiSiteFacts(string? BuildingKind = null, int BuildingLevel = 0, string? InstallationKind = null,
    int Priority = 0, bool HumanOwned = false);
public sealed record AiBuildingValue(long Base, long PerLevel);

public sealed record AiAttackModifier
{
    public IReadOnlyCollection<string> AttackerCategories { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> DefenderCategories { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> ExcludedDefenderCategories { get; init; } = Array.Empty<string>();
    public string? AttackerTrait { get; init; }
    public string? DefenderTrait { get; init; }
    public string? SourceInstallation { get; init; }
    public string? TargetInstallation { get; init; }
    public bool? AttackerAtSea { get; init; }
    public bool? AttackerCommander { get; init; }
    public bool? DefenderTransport { get; init; }
    public bool? DefenderConstruction { get; init; }
    public bool? TargetBuilding { get; init; }
    public bool? TargetHuman { get; init; }
    public long Bonus { get; init; }
    public long PerBuildingLevel { get; init; }

    internal bool Matches(AiUnitFacts attacker, AiUnitFacts defender, AiSiteFacts source, AiSiteFacts target) =>
        (AttackerCategories.Count == 0 || AttackerCategories.Contains(attacker.Category))
        && (DefenderCategories.Count == 0 || DefenderCategories.Contains(defender.Category))
        && !ExcludedDefenderCategories.Contains(defender.Category)
        && (AttackerTrait == null || attacker.Traits.Contains(AttackerTrait))
        && (DefenderTrait == null || defender.Traits.Contains(DefenderTrait))
        && (SourceInstallation == null || source.InstallationKind == SourceInstallation)
        && (TargetInstallation == null || target.InstallationKind == TargetInstallation)
        && (!AttackerAtSea.HasValue || AttackerAtSea == attacker.AtSea)
        && (!AttackerCommander.HasValue || AttackerCommander == attacker.HasCommander)
        && (!DefenderTransport.HasValue || DefenderTransport == defender.Transport)
        && (!DefenderConstruction.HasValue || DefenderConstruction == defender.UnderConstruction)
        && (!TargetBuilding.HasValue || TargetBuilding == (target.BuildingKind != null))
        && (!TargetHuman.HasValue || TargetHuman == target.HumanOwned);
}

public sealed record AiTacticalRules
{
    public IReadOnlyDictionary<string, AiBuildingValue> Buildings { get; init; } = new Dictionary<string, AiBuildingValue>();
    public IReadOnlyDictionary<string, long> Installations { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<int, long> Priorities { get; init; } = new Dictionary<int, long>();
    public IReadOnlyDictionary<string, float> Grades { get; init; } = new Dictionary<string, float>();
    public IReadOnlyList<AiAttackModifier> Modifiers { get; init; } = Array.Empty<AiAttackModifier>();
    public string NavalCategory { get; init; } = "naval";
    public float NavalStrengthMultiplier { get; init; } = .5f;
    public float EmbarkedStrengthMultiplier { get; init; } = .6f;
    public long EmptySiteValue { get; init; } = 1;
    public long PriorityCommanderBonus { get; init; } = 200;
    public long ExperienceDifferenceBonus { get; init; } = 15;
    public long AlliedPenalty { get; init; } = -10000;
    public long HealthyAttackerBonus { get; init; } = 100;
    public long OverHalfHealthBonus { get; init; } = 60;
    public long UnderHalfHealthBonus { get; init; } = 20;
    public long FullHealthUnderHalfBonus { get; init; } = 40;
    public long StrikeBonus { get; init; } = 50;

    public void Validate()
    {
        if (FrozenAiTacticalRules.IsValidated(this)) return;
        ValidateCore();
    }
    private void ValidateCore()
    {
        bool Invalid(float value) => float.IsNaN(value) || float.IsInfinity(value) || value < 0;
        if (string.IsNullOrWhiteSpace(NavalCategory) || Invalid(NavalStrengthMultiplier) || Invalid(EmbarkedStrengthMultiplier)
            || Buildings.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value.Base < 0 || p.Value.PerLevel < 0)
            || Installations.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)
            || Grades.Any(p => string.IsNullOrWhiteSpace(p.Key) || Invalid(p.Value)) || Priorities.Any(p => p.Key < 0 || p.Value < 0)
            || EmptySiteValue < 0 || PriorityCommanderBonus < 0)
            throw new RuleViolation("ai.configuration", "Invalid tactical scoring configuration");
        foreach (var modifier in Modifiers)
            if (modifier.AttackerCategories.Concat(modifier.DefenderCategories).Concat(modifier.ExcludedDefenderCategories).Any(string.IsNullOrWhiteSpace)
                || new[] { modifier.AttackerTrait, modifier.DefenderTrait, modifier.SourceInstallation, modifier.TargetInstallation }.Any(s => s != null && string.IsNullOrWhiteSpace(s)))
                throw new RuleViolation("ai.configuration", "Invalid attack scoring modifier");
    }
}

public static class AiTactics
{
    private static void CheckSite(AiSiteFacts site)
    {
        if (site.BuildingLevel < 0 || site.Priority < 0 || site.BuildingKind != null && string.IsNullOrWhiteSpace(site.BuildingKind)
            || site.InstallationKind != null && string.IsNullOrWhiteSpace(site.InstallationKind))
            throw new RuleViolation("ai.site", "Invalid tactical site facts");
    }

    public static long Location(AiSiteFacts site, AiUnitFacts? occupant, AiTacticalRules rules)
    {
        rules.Validate(); CheckSite(site); occupant?.Validate();
        long value = rules.EmptySiteValue;
        if (site.BuildingKind != null)
        {
            if (!rules.Buildings.TryGetValue(site.BuildingKind, out var building)) throw new RuleViolation("ai.building", "Unknown building value");
            value = checked(building.Base + building.PerLevel * site.BuildingLevel);
        }
        if (site.InstallationKind != null)
        {
            if (!rules.Installations.TryGetValue(site.InstallationKind, out long installation)) throw new RuleViolation("ai.installation", "Unknown installation value");
            value = checked(value + installation);
        }
        if (rules.Priorities.TryGetValue(site.Priority, out long priority)) value = checked(value + priority);
        if (occupant != null && occupant.HasCommander && occupant.PriorityCommander) value = checked(value + rules.PriorityCommanderBonus);
        return value;
    }

    public static long Strength(AiUnitFacts unit, AiTacticalRules rules)
    {
        rules.Validate(); unit.Validate();
        if (!rules.Grades.TryGetValue(unit.Grade, out float multiplier)) throw new RuleViolation("ai.grade", "Unknown recruitment grade strength");
        long strength = checked((long)((float)unit.Health * multiplier));
        if (unit.Category == rules.NavalCategory) strength = checked((long)((float)strength * rules.NavalStrengthMultiplier));
        else if (unit.AtSea) strength = checked((long)((float)strength * rules.EmbarkedStrengthMultiplier));
        return strength;
    }

    public static long AttackScore(AiUnitFacts attacker, AiUnitFacts defender, AiSiteFacts source, AiSiteFacts target,
        bool allied, AiTacticalRules rules)
    {
        rules.Validate(); attacker.Validate(); defender.Validate(); CheckSite(source); CheckSite(target);
        long score = checked(((long)attacker.ExperienceLevel - defender.ExperienceLevel) * rules.ExperienceDifferenceBonus);
        if (allied) score = checked(score + rules.AlliedPenalty);
        else
        {
            score = checked(score + Location(target, defender, rules));
            if (attacker.Health >= defender.Health) score = checked(score + rules.HealthyAttackerBonus);
            else if ((float)attacker.Health > (float)defender.Health * .5f) score = checked(score + rules.OverHalfHealthBonus);
            else if ((float)attacker.Health < (float)defender.Health * .5f)
                score = checked(score + (attacker.Health == attacker.MaximumHealth ? rules.FullHealthUnderHalfBonus : rules.UnderHalfHealthBonus));
        }
        foreach (var modifier in rules.Modifiers)
            if (modifier.Matches(attacker, defender, source, target)) score = checked(score + modifier.Bonus + modifier.PerBuildingLevel * target.BuildingLevel);
        return score;
    }

    public static long StrikeScore(long attackScore, AiUnitFacts attacker, AiTacticalRules rules)
    {
        rules.Validate(); attacker.Validate();
        return checked((long)((float)checked(attackScore + rules.StrikeBonus) * ((float)attacker.Health / attacker.MaximumHealth)));
    }
}

public static class LegacyAiTactics
{
    public static IReadOnlyCollection<string> Traits(int id) => new[]
    {
        id is >= 10 and <= 12 ? "ai.direct_artillery" : null,
        id == 12 ? "siege_gun" : null, id == 13 ? "ai.rocket" : null,
        id is 5 or 9 ? "ai.counter_mounted" : null, id == 21 ? "coastal_gun" : null
    }.Where(s => s != null).Select(s => s!).ToArray();

    public static AiTacticalRules Rules() => new()
    {
        Buildings = new Dictionary<string, AiBuildingValue> { ["0"] = new(41, 20), ["1"] = new(81, 40), ["2"] = new(71, 35), ["3"] = new(61, 30), ["4"] = new(1, 15) },
        Installations = new Dictionary<string, long> { ["0"] = 35, ["1"] = 40, ["2"] = 40 },
        Priorities = new Dictionary<int, long> { [1] = 260, [2] = 200 },
        Grades = new Dictionary<string, float> { ["0"] = 1, ["1"] = 1.3f, ["2"] = 1.6f },
        Modifiers = new AiAttackModifier[]
        {
            new() { AttackerCategories = new[] { "artillery" }, AttackerTrait = "ai.direct_artillery", DefenderCategories = new[] { "infantry", "cavalry" }, Bonus = 250 },
            new() { AttackerCategories = new[] { "artillery" }, AttackerTrait = "ai.direct_artillery", DefenderCategories = new[] { "naval", "fort" }, Bonus = 150 },
            new() { AttackerCategories = new[] { "artillery" }, AttackerTrait = "siege_gun", DefenderCategories = new[] { "fort" }, Bonus = 200 },
            new() { AttackerCategories = new[] { "artillery" }, AttackerTrait = "siege_gun", TargetBuilding = true, PerBuildingLevel = 50 },
            new() { AttackerCategories = new[] { "artillery" }, AttackerTrait = "ai.rocket", Bonus = 500 },
            new() { AttackerCategories = new[] { "infantry", "cavalry" }, DefenderTrait = "ai.counter_mounted", Bonus = -60 },
            new() { AttackerCategories = new[] { "infantry" }, TargetInstallation = "0", Bonus = -60 },
            new() { AttackerCategories = new[] { "cavalry" }, TargetInstallation = "1", Bonus = -30 },
            new() { AttackerCategories = new[] { "infantry", "cavalry" }, DefenderCategories = new[] { "naval" }, Bonus = -30 },
            new() { AttackerCategories = new[] { "naval" }, ExcludedDefenderCategories = new[] { "naval" }, Bonus = 100 },
            new() { AttackerCategories = new[] { "naval" }, ExcludedDefenderCategories = new[] { "naval" }, DefenderTrait = "coastal_gun", Bonus = -250 },
            new() { AttackerCategories = new[] { "fort" }, DefenderTrait = "siege_gun", Bonus = -270 },
            new() { DefenderCategories = new[] { "artillery" }, Bonus = 150 },
            new() { DefenderConstruction = true, Bonus = 400 },
            new() { AttackerCategories = new[] { "infantry", "cavalry", "artillery" }, AttackerAtSea = true, DefenderCategories = new[] { "naval" }, Bonus = -180 },
            new() { AttackerCommander = true, Bonus = 40 },
            new() { DefenderTransport = true, Bonus = 5 },
            new() { TargetInstallation = "2", Bonus = -25 },
            new() { SourceInstallation = "0", Bonus = -20 }, new() { SourceInstallation = "1", Bonus = -20 },
            new() { TargetHuman = true, Bonus = 40 }
        }
    };
}
