using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ProductionSite(Resources Income, float? CommanderMultiplier = null);

public static class EconomyProduction
{
    public static Resources Calculate(IEnumerable<ProductionSite> sites, float factionMultiplier, Resources bonus)
    {
        bonus.Validate();
        ValidateMultiplier(factionMultiplier);
        long money = 0, industry = 0, food = 0;
        foreach (var site in sites)
        {
            site.Income.Validate();
            var income = site.CommanderMultiplier.HasValue ? Scale(site.Income, site.CommanderMultiplier.Value) : site.Income;
            money = checked(money + income.Money);
            industry = checked(industry + income.Industry);
            food = checked(food + income.Food);
        }
        return new(checked(Scale(money, factionMultiplier) + bonus.Money),
            checked(Scale(industry, factionMultiplier) + bonus.Industry), checked(food + bonus.Food));
    }

    public static Resources Scale(Resources income, float multiplier) =>
        new(Scale(income.Money, multiplier), Scale(income.Industry, multiplier), Scale(income.Food, multiplier));

    private static void ValidateMultiplier(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
            throw new RuleViolation("economy.multiplier", "Income multiplier must be finite and nonnegative");
    }

    private static long Scale(long value, float multiplier)
    {
        ValidateMultiplier(multiplier);
        if (value < 0) throw new RuleViolation("resources.negative", "Income cannot be negative");
        float result = (float)value * multiplier;
        if (float.IsInfinity(result) || (double)result >= long.MaxValue)
            throw new RuleViolation("economy.overflow", "Income exceeds supported numeric range");
        return (long)result;
    }
}

public sealed record UnitEndInput
{
    public bool IgnoreSiteEffects { get; init; }
    public string Id { get; init; } = "";
    public int Health { get; init; }
    public int MaximumHealth { get; init; }
    public int ConstructionTurns { get; init; }
    public bool Acted { get; init; }
    public bool ForceRecovery { get; init; }
    public int Shortage { get; init; }
    public bool HasCommander { get; init; }
    public int VeterancyHealing { get; init; }
    public int FieldHealing { get; init; }
    public int AdjacentHealing { get; init; }
    public int TitleHealing { get; init; }
    public int Burning { get; init; }
}

public sealed record SiteEndInput
{
    public string? BuildingKind { get; init; }
    public int BuildingLevel { get; init; }
    public int BuildingHealing { get; init; }
    public int Environment { get; init; }
    public bool DecayEnvironment { get; init; } = true;
    public bool DegradeBuilding { get; init; } = true;
    public IReadOnlyList<UnitEndInput> Units { get; init; } = Array.Empty<UnitEndInput>();
}

public sealed record SiteEndRules
{
    public HazardRules Burning { get; init; } = Hazards.LegacyBurning();
    public HazardRules Environment { get; init; } = Hazards.LegacyBurning();
    public string VulnerableBuilding { get; init; } = "farmland";
    public int DegradedLevel { get; init; } = 1;
    public int DegradationProbability { get; init; } = 30;
}

public sealed record UnitEndResult(string Id, int Health, int Burning, bool ForceRecovery);
public sealed record SiteEndResult(IReadOnlyList<UnitEndResult> Units, int BuildingLevel, int Environment, ulong RandomState);

public static class TurnSettlement
{
    public static SiteEndResult EndSite(SiteEndInput input, Ew4Random random, SiteEndRules? configuration = null)
    {
        var rules = configuration ?? new();
        rules.Burning.Validate(); rules.Environment.Validate();
        static bool Known(int intensity, HazardRules stages) => intensity >= 0 && (intensity == 0 || stages.Stages.ContainsKey(intensity));
        if (input.BuildingLevel < 0 || input.BuildingHealing < 0 || !Known(input.Environment, rules.Environment)
            || string.IsNullOrWhiteSpace(rules.VulnerableBuilding) || rules.DegradedLevel < 0
            || rules.DegradationProbability < 0 || rules.DegradationProbability > 100)
            throw new RuleViolation("settlement.site", "Invalid end-turn site or rules");
        if (input.Units.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count() != input.Units.Count
            || input.Units.Any(u => string.IsNullOrWhiteSpace(u.Id) || u.Health < 1 || u.Health > u.MaximumHealth
                || u.ConstructionTurns < 0 || u.Shortage < 0 || u.VeterancyHealing < 0
                || u.FieldHealing < 0 || u.AdjacentHealing < 0 || u.TitleHealing < 0 || !Known(u.Burning, rules.Burning)))
            throw new RuleViolation("settlement.unit", "Invalid end-turn unit");
        var local = new Ew4Random(random.State);
        var units = new List<UnitEndResult>();
        foreach (var unit in input.Units)
        {
            int health = unit.Health;
            bool force = unit.ForceRecovery;
            void Heal(int amount) => health = (int)Math.Min(unit.MaximumHealth, (long)health + amount);
            if (unit.Shortage == 0)
            {
                if ((unit.ConstructionTurns == 0 && !unit.Acted) || force)
                {
                    if (!unit.IgnoreSiteEffects && input.BuildingKind != null) Heal(input.BuildingHealing);
                    force = false;
                    Heal(unit.VeterancyHealing);
                    if (unit.HasCommander && (unit.IgnoreSiteEffects || input.BuildingKind == null)) Heal(unit.FieldHealing);
                }
                Heal(unit.AdjacentHealing);
                if (unit.HasCommander) Heal(unit.TitleHealing);
            }
            var burn = Hazards.EndUnit(health, unit.MaximumHealth, unit.Burning, local, rules.Burning);
            units.Add(new(unit.Id, burn.Health, burn.Intensity, force));
        }
        int level = input.BuildingLevel;
        if (input.DegradeBuilding && input.BuildingKind == rules.VulnerableBuilding && level > rules.DegradedLevel
            && (input.Environment > 0 || units.Where((u, i) => !input.Units[i].IgnoreSiteEffects).Any(u => u.Burning > 0)) && local.Next(100) < rules.DegradationProbability)
            level = rules.DegradedLevel;
        if (input.Environment > 0)
            for (int i = 0; i < units.Count; i++)
                if (!input.Units[i].IgnoreSiteEffects)
                    units[i] = units[i] with { Health = Hazards.Damage(units[i].Health, input.Units[i].MaximumHealth,
                        rules.Environment.Stages[input.Environment].DamagePercent, rules.Environment.Nonlethal) };
        int environment = input.DecayEnvironment ? Hazards.Decay(input.Environment, local, rules.Environment) : input.Environment;
        random.Commit(local.State);
        return new(units, level, environment, local.State);
    }
}
