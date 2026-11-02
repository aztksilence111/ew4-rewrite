using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ResourceIncomePatch(long? Money = null, long? Industry = null, long? Food = null)
{
    public void Validate()
    {
        if (Money == null && Industry == null && Food == null || Money < 0 || Industry < 0 || Food < 0)
            throw new RuleViolation("scenario.income", "Income needs at least one nonnegative resource value");
    }
    public Resources Apply(Resources income) => new(Money ?? income.Money, Industry ?? income.Industry, Food ?? income.Food);
}

public sealed record FoodEconomyView(long Base, long Farmland, long Settlements, long Other, long Reduction, long Total, long Consumption)
{
    public long Net => Total - Consumption;
}

public sealed record BuildingIncomePatch(string Kind, int Level, ResourceIncomePatch Income);

public static class ScenarioEconomy
{
    public static WorldTurnRules BuildingRules(WorldTurnRules rules, IReadOnlyList<BuildingIncomePatch> patches)
    {
        if (patches.GroupBy(p => (p.Kind, p.Level)).Any(g => g.Count() > 1))
            throw new RuleViolation("scenario.building_income", "Duplicate building income level");
        foreach (var p in patches)
        {
            if (string.IsNullOrWhiteSpace(p.Kind) || p.Level < 0 || p.Income == null
                || !rules.Buildings.Any(b => b.Kind == p.Kind && b.Level == p.Level))
                throw new RuleViolation("scenario.building_income", "Unknown building income level");
            p.Income.Validate();
        }
        return rules with { Buildings = rules.Buildings.Select(b => patches.SingleOrDefault(p => p.Kind == b.Kind && p.Level == b.Level)
            is { } patch ? b with { Income = patch.Income.Apply(b.Income) } : b).ToArray() };
    }

    public static bool Enabled(WorldState world) => world.ScenarioEvents.SelectMany(ScenarioRules.Effects)
        .Any(e => e.Type is "faction_income" or "site_income" or "site_income_default" || e.Type == "faction_create" && e.Income != null);

    internal static WorldState Apply(WorldState world, string faction, ScenarioEffect effect)
    {
        effect.Income?.Validate();
        if (effect.Type == "faction_income")
            return world with { Factions = world.Factions.Select(f => f.Id == faction
                ? f with { IncomeBonus = effect.Income!.Apply(f.IncomeBonus) } : f).ToArray() };
        var cells = effect.Cells.ToHashSet();
        if (cells.Any(i => i < 0 || i >= world.Cells.Count || world.Cells[i].BuildingKind == null))
            throw new RuleViolation("scenario.income_site", "Site income requires existing buildings");
        return world with { Cells = world.Cells.Select((c, i) => !cells.Contains(i) ? c : c with
        {
            IncomeAdjustment = effect.Type == "site_income_default" ? null : Merge(c, effect.Income!)
        }).ToArray() };
    }

    private static BuildingIncomeOverride Merge(CellState cell, ResourceIncomePatch patch)
    {
        var prior = cell.IncomeAdjustment?.Kind == cell.BuildingKind ? cell.IncomeAdjustment : null;
        return new(cell.BuildingKind!, patch.Money ?? prior?.Money, patch.Industry ?? prior?.Industry, patch.Food ?? prior?.Food);
    }

    public static FoodEconomyView Food(WorldState world, FactionState faction, WorldTurnRules rules)
    {
        long farms = 0, settlements = 0, other = 0;
        var commanders = WorldUnitLayers.Surface(world).Where(u => u.CommanderId != null)
            .GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.First());
        for (int i = 0; i < world.Cells.Count; i++)
        {
            var cell = world.Cells[i];
            if (cell.Owner != faction.Id) continue;
            long food = WorldTurns.SiteIncome(cell, i, rules, commanders.GetValueOrDefault(i)).Food;
            if (cell.BuildingKind == "farmland") farms = checked(farms + food);
            else if (cell.BuildingKind is "city" or "industry" or "stable" or "port") settlements = checked(settlements + food);
            else other = checked(other + food);
        }
        long total = checked(faction.IncomeBonus.Food + farms + settlements + other);
        long reduced = WorldArmyCapacity.IsOverLimit(world, faction.Id) ? total / 2 : total;
        return new(faction.IncomeBonus.Food, farms, settlements, other, total - reduced, reduced, WorldTurns.FactionUpkeep(world, faction.Id));
    }
}
