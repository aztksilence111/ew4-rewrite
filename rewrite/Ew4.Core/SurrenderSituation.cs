using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

internal sealed record SurrenderSituation(string? Commander, bool CommanderLost, int LegionLead, int BuildingLevels, bool Superior)
{
    public static SurrenderSituation Measure(WorldState world, string actor, string target)
    {
        var units = world.Units.Where(u => u.Owner == target).ToArray();
        string? leader = world.Demands?.Leaders is { } leaders ? leaders.GetValueOrDefault(target)
            : world.Commanders.Where(c => c.Owner == target).OrderByDescending(c => c.Rank.Level).FirstOrDefault()?.Id;
        var own = world.Factions.SingleOrDefault(f => f.Id == actor);
        var other = world.Factions.SingleOrDefault(f => f.Id == target);
        int lead = own == null || other == null ? 0 : Math.Max(0,
            world.Factions.Count(f => !f.Defeated && f.Camp == own.Camp) - world.Factions.Count(f => !f.Defeated && f.Camp == other.Camp));
        int levels = checked(world.Cells.Where(c => c.Owner == target && c.BuildingKind != null).Sum(c => c.BuildingLevel));
        return new(leader, leader != null && !units.Any(u => u.CommanderId == leader), lead, levels,
            world.Units.Count(u => u.Owner == actor) > units.Length);
    }
}

public sealed record SurrenderPressureRules
{
    public int CommanderBonus { get; init; } = 25;
    public int LegionAdvantageBonus { get; init; } = 5;
    public int LegionAdvantageCap { get; init; } = 25;
    public int BuildingLevelPenalty { get; init; } = 2;
    public int BuildingPenaltyCap { get; init; } = 50;
    public int SuperiorityBonus { get; init; } = 15;

    public void Validate()
    {
        if (CommanderBonus is < 0 or > 25 || LegionAdvantageBonus is < 0 or > 100 || LegionAdvantageCap is < 0 or > 100
            || BuildingLevelPenalty is < 0 or > 100 || BuildingPenaltyCap is < 0 or > 100 || SuperiorityBonus is < 0 or > 100)
            throw new RuleViolation("diplomacy.pressure_rules", "Invalid surrender probability weights");
    }
}

public sealed record SurrenderChanceBreakdown(decimal LossPercent, decimal OccupationPercent, decimal CorePercent,
    decimal CommanderPercent, int LegionPercent, int BuildingPercent, int SuperiorityPercent,
    int LegionLead, int BuildingLevels, int MaximumChance, int? InitialCommanders = null, int DeadCommanders = 0);
