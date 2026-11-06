using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record TrainingStage(string Id, long? MoneyToNext, int HealingOnEntry = 0);
public sealed record TrainingRules
{
    public IReadOnlyList<TrainingStage> Stages { get; init; } = Array.Empty<TrainingStage>();
    public int FoodMultiplier { get; init; } = 3;

    public void Validate()
    {
        if (Stages.Count == 0 || FoodMultiplier < 0
            || Stages.Any(s => string.IsNullOrWhiteSpace(s.Id) || s.MoneyToNext < 0 || s.HealingOnEntry < 0)
            || Stages.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != Stages.Count
            || Stages.Take(Stages.Count - 1).Any(s => !s.MoneyToNext.HasValue)
            || Stages[Stages.Count - 1].MoneyToNext.HasValue)
            throw new RuleViolation("training.configuration", "Invalid training stages or costs");
    }
}

public sealed record BuildingUpgradeStep(string Kind, int FromLevel, int ToLevel, Resources Cost);
public sealed record BuildingUpgradeRules
{
    public IReadOnlyList<BuildingUpgradeStep> Steps { get; init; } = Array.Empty<BuildingUpgradeStep>();
    public int? PerTurnLimit { get; init; } = 1;
    public string DiscountSkill { get; init; } = Ew4Combat.Skill(30);
    public int DiscountPercent { get; init; } = 60;

    public void Validate()
    {
        if (PerTurnLimit < 1 || DiscountPercent < 0 || DiscountPercent > 100 || string.IsNullOrWhiteSpace(DiscountSkill)
            || Steps.Any(s => string.IsNullOrWhiteSpace(s.Kind) || s.FromLevel < 0 || s.ToLevel <= s.FromLevel)
            || Steps.GroupBy(s => (s.Kind, s.FromLevel)).Any(g => g.Count() > 1))
            throw new RuleViolation("upgrade.configuration", "Invalid building upgrade steps or limit");
        foreach (var step in Steps) step.Cost.Validate();
    }
}

public static class WorldImprovements
{
    private static bool Affordable(Resources funds, Resources cost) =>
        funds.Money >= cost.Money && funds.Industry >= cost.Industry && funds.Food >= cost.Food;

    private static Resources Spend(Resources funds, Resources cost) =>
        new(funds.Money - cost.Money, funds.Industry - cost.Industry, funds.Food - cost.Food);

    private static Resources UpgradePrice(WorldState state, int cellId, BuildingUpgradeStep step, BuildingUpgradeRules rules)
    {
        bool discount = WorldUnitLayers.Surface(state).Any(u => u.Cell == cellId && u.CommanderId != null && u.Combatant.Has(rules.DiscountSkill));
        long Discount(long value) => checked(value / 100 * rules.DiscountPercent + value % 100 * rules.DiscountPercent / 100);
        return discount ? step.Cost with { Money = Discount(step.Cost.Money), Industry = Discount(step.Cost.Industry) } : step.Cost;
    }

    public static bool CanUpgradeBuilding(WorldState state, int cellId, BuildingUpgradeRules rules)
    {
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Validate();
        return CanUpgradeBuildingValidated(state, faction, cellId, rules);
    }

    internal static bool CanUpgradeBuildingValidated(WorldState state, FactionState faction, int cellId, BuildingUpgradeRules rules)
    {
        if (cellId < 0 || cellId >= state.Cells.Count) return false;
        var cell = state.Cells[cellId];
        if (!cell.Terrain.Active || cell.Owner != faction.Id || cell.BuildingKind == null || cell.BuildingConstructionTurns > 0 || cell.BuildingUpgradesThisTurn >= rules.PerTurnLimit) return false;
        var step = rules.Steps.SingleOrDefault(s => s.Kind == cell.BuildingKind && s.FromLevel == cell.BuildingLevel);
        return step != null && Affordable(faction.Resources, UpgradePrice(state, cellId, step, rules));
    }

    public static Resources TrainingCost(WorldState state, string unitId, TrainingRules rules) =>
        PrepareTraining(state, WorldActions.Ready(state, state.Revision), unitId, rules).cost;

    private static (UnitState unit, int level, Resources cost) PrepareTraining(WorldState state, FactionState faction,
        string unitId, TrainingRules rules)
    {
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id || unit.CommanderId == null)
            throw new RuleViolation("training.unit", "Training requires an active faction unit with a commander");
        // Native eligibility has two independent alternatives, including the stationary unacted case.
        if (!((unit.RemainingMovement > 0 && unit.Morale.Effective != -3)
            || (unit.Combatant.ConstructionTurns < 1 && !unit.Acted)))
            throw new RuleViolation("training.unavailable", "Unit cannot train in its current action state");
        var commander = state.Commanders.Single(c => c.Id == unit.CommanderId);
        int level = rules.Stages.ToList().FindIndex(s => s.Id == unit.Veterancy);
        if (level < 0) throw new RuleViolation("training.stage", "Current veterancy is not in the configured training track");
        if (level >= commander.MaximumTrainingLevel || level == rules.Stages.Count - 1)
            throw new RuleViolation("training.limit", "Commander or training track limit reached");
        var cost = new Resources(rules.Stages[level].MoneyToNext!.Value, 0,
            checked(unit.Support.FoodConsumption * rules.FoodMultiplier));
        return (unit, level, cost);
    }

    public static WorldChange Train(WorldState state, long expectedRevision, string unitId, TrainingRules rules)
    {
        var faction = WorldActions.Ready(state, expectedRevision);
        var (unit, level, cost) = PrepareTraining(state, faction, unitId, rules);
        if (!Affordable(faction.Resources, cost)) throw new RuleViolation("training.resources", "Insufficient training resources");
        var trained = unit with
        {
            Veterancy = rules.Stages[level + 1].Id, RemainingMovement = 0, Acted = true,
            Combatant = unit.Combatant with { Health = (int)Math.Min(unit.Combatant.MaximumHealth,
                (long)unit.Combatant.Health + rules.Stages[level + 1].HealingOnEntry) }
        };
        var next = (state with
        {
            Units = state.Units.Select(u => u.Id == unitId ? trained : u).ToArray(),
            Factions = state.Factions.Select(f => f.Id == faction.Id ? f with { Resources = Spend(f.Resources, cost) } : f).ToArray(),
            Revision = checked(state.Revision + 1)
        }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice("unit.trained", unitId) });
    }

    public static Resources BuildingUpgradeCost(WorldState state, int cellId, BuildingUpgradeRules rules) =>
        PrepareUpgrade(state, WorldActions.Ready(state, state.Revision), cellId, rules).cost;

    private static (CellState cell, BuildingUpgradeStep step, Resources cost) PrepareUpgrade(WorldState state,
        FactionState faction, int cellId, BuildingUpgradeRules rules)
    {
        rules.Validate();
        if (cellId < 0 || cellId >= state.Cells.Count) throw new RuleViolation("upgrade.site", "Unknown building site");
        var cell = state.Cells[cellId];
        if (cell.BuildingConstructionTurns > 0) throw new RuleViolation("upgrade.construction", "Building is still under construction");
        if (!cell.Terrain.Active || cell.Owner != faction.Id || cell.BuildingKind == null)
            throw new RuleViolation("upgrade.site", "Building must be on an active faction site");
        if (cell.BuildingUpgradesThisTurn >= rules.PerTurnLimit)
            throw new RuleViolation("upgrade.used", "Building upgrade allowance used this turn");
        var step = rules.Steps.SingleOrDefault(s => s.Kind == cell.BuildingKind && s.FromLevel == cell.BuildingLevel)
            ?? throw new RuleViolation("upgrade.limit", "No configured upgrade from this building level");
        var cost = UpgradePrice(state, cellId, step, rules);
        return (cell, step, cost);
    }

    public static WorldChange UpgradeBuilding(WorldState state, long expectedRevision, int cellId, BuildingUpgradeRules rules)
    {
        var faction = WorldActions.Ready(state, expectedRevision);
        var (cell, step, cost) = PrepareUpgrade(state, faction, cellId, rules);
        if (!Affordable(faction.Resources, cost)) throw new RuleViolation("upgrade.resources", "Insufficient building upgrade resources");
        int turns = state.Construction?.Enabled == true && cell.BuildingKind != null
            && state.Construction.Buildings.TryGetValue(cell.BuildingKind, out var building) ? building.Levels[step.ToLevel - 1].ConstructionTurns : 0;
        var upgraded = cell with { BuildingLevel = step.ToLevel, BuildingUpgradesThisTurn = checked(cell.BuildingUpgradesThisTurn + 1), BuildingConstructionTurns = turns };
        var next = (state with
        {
            Cells = state.Cells.Select((c, i) => i == cellId ? upgraded : c).ToArray(),
            Factions = state.Factions.Select(f => f.Id == faction.Id ? f with { Resources = Spend(f.Resources, cost) } : f).ToArray(),
            Revision = checked(state.Revision + 1)
        }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice("building.upgraded", cellId.ToString(), step.ToLevel) });
    }
}
