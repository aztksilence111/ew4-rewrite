using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record UnitPurchaseRules
{
    public Resources Cost { get; init; } = new(0, 0, 0);
    public string? DiscountSkill { get; init; }
    public int MoneyPercentWithSkill { get; init; } = 50;
    public bool AllowSea { get; init; } = true;
    public void Validate()
    {
        Cost.Validate();
        if (MoneyPercentWithSkill < 0 || MoneyPercentWithSkill > 100 || DiscountSkill != null && string.IsNullOrWhiteSpace(DiscountSkill))
            throw new RuleViolation("purchase.configuration", "Invalid purchase cost or discount");
    }
}
public sealed record InstallationBuildRules
{
    public string Kind { get; init; } = "";
    public IReadOnlyCollection<string> Builders { get; init; } = new[] { "infantry" };
    public UnitPurchaseRules Purchase { get; init; } = new();
}
public sealed record TransportPurchaseRules
{
    public IReadOnlyCollection<string> ExcludedCategories { get; init; } = new[] { "naval", "fort" };
    public UnitPurchaseRules Purchase { get; init; } = new();
}

public static class WorldSupportActions
{
    private static (FactionState faction, UnitState unit, CellState cell) Ready(WorldState state, long expectedRevision,
        string unitId, UnitPurchaseRules rules)
    {
        var faction = WorldActions.Ready(state, expectedRevision);
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id) throw new RuleViolation("purchase.unit", "Unit is not controlled by the active faction");
        var cell = state.Cells[unit.Cell];
        if (cell.Owner != faction.Id || !cell.Terrain.Active || !rules.AllowSea && cell.Terrain.Sea)
            throw new RuleViolation("purchase.site", "Purchase requires an eligible owned site");
        return (faction, unit, cell);
    }

    internal static Resources Price(UnitState unit, UnitPurchaseRules rules)
    {
        if (unit.CommanderId == null || rules.DiscountSkill == null || !unit.Combatant.Has(rules.DiscountSkill)) return rules.Cost;
        long value = rules.Cost.Money;
        long discounted = checked(value / 100 * rules.MoneyPercentWithSkill + value % 100 * rules.MoneyPercentWithSkill / 100);
        return rules.Cost with { Money = discounted };
    }

    private static WorldChange Commit(WorldState state, FactionState faction, UnitState unit, CellState? updatedCell,
        Resources cost, string notice)
    {
        var funds = faction.Resources;
        if (funds.Money < cost.Money || funds.Industry < cost.Industry || funds.Food < cost.Food)
            throw new RuleViolation("purchase.resources", "Insufficient purchase resources");
        var next = (state with
        {
            Factions = state.Factions.Select(f => f.Id == faction.Id ? f with { Resources = new(funds.Money-cost.Money, funds.Industry-cost.Industry, funds.Food-cost.Food) } : f).ToArray(),
            Units = state.Units.Select(u => u.Id == unit.Id ? unit : u).ToArray(),
            Cells = updatedCell == null ? state.Cells : state.Cells.Select((c, i) => i == unit.Cell ? updatedCell : c).ToArray(),
            Revision = checked(state.Revision + 1)
        }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice(notice, unit.Id, unit.Cell) });
    }

    public static WorldChange BuildInstallation(WorldState state, long expectedRevision, string unitId, InstallationBuildRules rules)
    {
        var (faction, unit, cell, cost) = PrepareInstallation(state, expectedRevision, unitId, rules);
        return Commit(state, faction, unit with { RemainingMovement = 0, Acted = true }, cell with { InstallationKind = rules.Kind },
            cost, "installation.built");
    }

    public static Resources InstallationCost(WorldState state, string unitId, InstallationBuildRules rules) =>
        PrepareInstallation(state, state.Revision, unitId, rules).cost;

    private static (FactionState faction, UnitState unit, CellState cell, Resources cost) PrepareInstallation(
        WorldState state, long expectedRevision, string unitId, InstallationBuildRules rules)
    {
        if (string.IsNullOrWhiteSpace(rules.Kind) || rules.Builders.Any(string.IsNullOrWhiteSpace))
            throw new RuleViolation("installation.configuration", "Invalid installation kind or builder categories");
        var (faction, unit, cell) = Ready(state, expectedRevision, unitId, rules.Purchase);
        if (!rules.Builders.Contains(unit.Combatant.Profile.Category) || cell.BuildingKind != null || cell.InstallationKind != null
            || !((unit.RemainingMovement > 0 && unit.Morale.Effective != -3) || unit.Combatant.ConstructionTurns < 1))
            throw new RuleViolation("installation.unavailable", "Unit or site is unavailable for installation construction");
        return (faction, unit, cell, Price(unit, rules.Purchase));
    }

    public static bool CanBuildInstallation(WorldState state, string unitId, InstallationBuildRules rules)
    {
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Purchase.Validate();
        if (string.IsNullOrWhiteSpace(rules.Kind) || rules.Builders.Any(string.IsNullOrWhiteSpace))
            throw new RuleViolation("installation.configuration", "Invalid installation kind or builder categories");
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id) return false;
        return CanBuildInstallationValidated(state, faction, unit, rules);
    }

    internal static bool CanBuildInstallationValidated(WorldState state, FactionState faction, UnitState unit, InstallationBuildRules rules)
    {
        var cell = state.Cells[unit.Cell];
        if (cell.Owner != faction.Id || !cell.Terrain.Active || !rules.Purchase.AllowSea && cell.Terrain.Sea
            || !rules.Builders.Contains(unit.Combatant.Profile.Category) || cell.BuildingKind != null || cell.InstallationKind != null
            || !((unit.RemainingMovement > 0 && unit.Morale.Effective != -3) || unit.Combatant.ConstructionTurns < 1)) return false;
        var cost = Price(unit, rules.Purchase);
        return faction.Resources.Money >= cost.Money && faction.Resources.Industry >= cost.Industry && faction.Resources.Food >= cost.Food;
    }

    public static Resources TransportCost(WorldState state, string unitId, TransportPurchaseRules rules) =>
        PrepareTransport(state, state.Revision, unitId, rules).cost;

    private static (FactionState faction, UnitState unit, Resources cost) PrepareTransport(WorldState state,
        long expectedRevision, string unitId, TransportPurchaseRules rules)
    {
        if (rules.ExcludedCategories.Any(string.IsNullOrWhiteSpace)) throw new RuleViolation("transport.configuration", "Invalid transport category policy");
        var (faction, unit, _) = Ready(state, expectedRevision, unitId, rules.Purchase);
        if (unit.Transport || rules.ExcludedCategories.Contains(unit.Combatant.Profile.Category))
            throw new RuleViolation("transport.unavailable", "Unit already has transport or its category is excluded");
        return (faction, unit, Price(unit, rules.Purchase));
    }

    public static WorldChange BuyTransport(WorldState state, long expectedRevision, string unitId, TransportPurchaseRules rules)
    {
        var (faction, unit, cost) = PrepareTransport(state, expectedRevision, unitId, rules);
        return Commit(state, faction, unit with { Transport = true }, null, cost, "unit.transport_purchased");
    }

    public static bool CanBuyTransport(WorldState state, string unitId, TransportPurchaseRules rules)
    {
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Purchase.Validate();
        if (rules.ExcludedCategories.Any(string.IsNullOrWhiteSpace)) throw new RuleViolation("transport.configuration", "Invalid transport category policy");
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id || unit.Transport || rules.ExcludedCategories.Contains(unit.Combatant.Profile.Category)) return false;
        return CanBuyTransportValidated(state, faction, unit, rules);
    }

    internal static bool CanBuyTransportValidated(WorldState state, FactionState faction, UnitState unit, TransportPurchaseRules rules)
    {
        if (unit.Owner != faction.Id || unit.Transport || rules.ExcludedCategories.Contains(unit.Combatant.Profile.Category)) return false;
        var cell = state.Cells[unit.Cell];
        if (!cell.Terrain.Active || cell.Owner != faction.Id || !rules.Purchase.AllowSea && cell.Terrain.Sea) return false;
        var price = Price(unit, rules.Purchase);
        return faction.Resources.Money >= price.Money && faction.Resources.Industry >= price.Industry && faction.Resources.Food >= price.Food;
    }
}
