using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record PaidSurrenderRules
{
    public int CommanderReduction { get; init; } = 25;
    public int LegionAdvantageReduction { get; init; } = 5;
    public int LegionAdvantageCap { get; init; } = 25;
    public int BuildingLevelPercent { get; init; } = 2;
    public int BuildingPercentCap { get; init; } = 50;
    public int SuperiorityReduction { get; init; } = 15;
    public void Validate()
    {
        if (CommanderReduction is < 0 or > 100 || LegionAdvantageReduction is < 0 or > 100 || LegionAdvantageCap is < 0 or > 100
            || BuildingLevelPercent is < 0 or > 100 || BuildingPercentCap is < 0 or > 200 || SuperiorityReduction is < 0 or > 100)
            throw new RuleViolation("diplomacy.purchase_rules", "Invalid paid surrender rules");
    }
}

public sealed record PaidSurrenderQuote(Resources BaseCost, Resources Cost, Resources Available, int Units, int BuildingLevels,
    int CommanderPercent, int LegionPercent, int BuildingPercent, int SuperiorityPercent, int TotalPercent, string? Blocked,
    int LegionLead, string? Commander);

public static class PaidSurrender
{
    public static PaidSurrenderQuote Quote(WorldState world, string actor, string target, IEnumerable<RecruitmentOffer> offers)
    {
        var policy = world.Demands?.Rules.Purchase ?? new PaidSurrenderRules(); policy.Validate();
        var original = DemandDiplomacy.Quote(world, actor, target);
        string? blocked = original.DemandBlocked is "occupation" or "progress" or "legacy" or "no_buildings" ? null : original.DemandBlocked;
        var units = world.Units.Where(u => u.Owner == target).ToArray();
        if (units.Length == 0) blocked ??= "no_units";
        var prices = offers.Where(o => o.Template != null).GroupBy(o => o.Template!.Profile.Id)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.Cost.Money).ThenByDescending(o => o.Cost.Industry).First().Cost);
        long money = 0, industry = 0;
        foreach (var unit in units)
        {
            string profile = (unit.CommanderBase?.Combatant ?? unit.Combatant).Profile.Id;
            if (!prices.TryGetValue(profile, out var cost)) { blocked ??= "unpriced"; continue; }
            money = checked(money + cost.Money); industry = checked(industry + cost.Industry);
        }
        var situation = SurrenderSituation.Measure(world, actor, target);
        int levels = situation.BuildingLevels, legionLead = situation.LegionLead;
        string? leader = situation.Commander;
        int commander = situation.CommanderLost ? -policy.CommanderReduction : 0;
        int legion = -(int)Math.Min(policy.LegionAdvantageCap, (long)legionLead * policy.LegionAdvantageReduction);
        int building = (int)Math.Min(policy.BuildingPercentCap, (long)levels * policy.BuildingLevelPercent);
        int superiority = situation.Superior ? -policy.SuperiorityReduction : 0;
        int adjustment = checked(commander + legion + building + superiority);
        long Price(long value) => checked((long)decimal.Round((decimal)value * Math.Max(0L, 100L + adjustment) / 100, 0, MidpointRounding.AwayFromZero));
        var required = new Resources(Price(money), Price(industry), 0);
        var available = world.Factions.Single(f => f.Id == actor).Resources;
        if (available.Money < required.Money || available.Industry < required.Industry) blocked ??= "resources";
        return new(new(money, industry, 0), required, available, units.Length, levels, commander, legion, building, superiority, adjustment, blocked, legionLead, leader);
    }

    public static WorldChange Demand(WorldState world, long revision, string actor, string target, IEnumerable<RecruitmentOffer> offers)
    {
        world.Validate();
        if (world.Revision != revision) throw new RuleViolation("world.stale", "Diplomacy state changed");
        var quote = Quote(world, actor, target, offers);
        if (quote.Blocked is { } reason) throw new RuleViolation("diplomacy." + reason, reason);
        var paid = world with { Factions = world.Factions.Select(f => f.Id != actor ? f : f with {
            Resources = f.Resources with { Money = checked(f.Resources.Money - quote.Cost.Money), Industry = checked(f.Resources.Industry - quote.Cost.Industry) }
        }).ToArray() };
        var change = WorldEvents.Surrender(paid, target, actor);
        var result = change.State with { Cells = change.State.Cells.Select((cell, index) => world.Cells[index].Owner == target
            && cell.BuildingKind == null && cell.InstallationKind == null ? cell with { Owner = null } : cell).ToArray() };
        result.Validate();
        return change with { State = result, Notices = change.Notices.Concat(new[] { new WorldNotice("diplomacy.accepted", target, 100) }).ToArray() };
    }
}
