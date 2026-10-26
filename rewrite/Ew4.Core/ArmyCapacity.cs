using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ArmyCapacityRules
{
    public int BaseCapacity { get; init; } = 8;
    public IReadOnlyDictionary<string, int> FactionBases { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int[]> Buildings { get; init; } = new Dictionary<string, int[]>
    {
        ["city"] = [0, 2, 3, 4, 5, 6, 7, 8], ["industry"] = [0, 1, 2, 3, 4], ["stable"] = [0, 1, 2, 3],
        ["port"] = [0, 0, 0, 0, 0], ["farmland"] = [0, 0, 0, 0, 0]
    };

    public void Validate()
    {
        if (BaseCapacity < 0 || FactionBases == null || Buildings == null
            || FactionBases.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)
            || Buildings.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || p.Value.Length == 0
                || p.Value.Any(v => v < 0) || p.Value[0] != 0))
            throw new RuleViolation("army.capacity_configuration", "Army capacity requires nonnegative bases and level tables beginning with zero");
    }

    public ArmyCapacityRules Copy() => this with
    {
        FactionBases = FactionBases.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        Buildings = Buildings.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal)
    };
}

public static class WorldArmyCapacity
{
    public static int BuildingCapacity(WorldState world, CellState cell)
    {
        if (world.ArmyCapacity == null || !cell.Terrain.Active || cell.BuildingKind == null
            || !world.ArmyCapacity.Buildings.TryGetValue(cell.BuildingKind, out var levels)) return 0;
        if (cell.BuildingLevel < 0 || cell.BuildingLevel >= levels.Length)
            throw new RuleViolation("army.capacity_level", "Missing army capacity for " + cell.BuildingKind + " level " + cell.BuildingLevel);
        return levels[cell.BuildingLevel];
    }

    public static int? Limit(WorldState world, string faction)
    {
        int? limit = world.Limits.UnitsPerFaction;
        if (world.ArmyCapacity is not { } rules) return limit;
        long capacity = rules.FactionBases.GetValueOrDefault(faction, rules.BaseCapacity);
        foreach (var cell in world.Cells)
            if (cell.Owner == faction) capacity += BuildingCapacity(world, cell);
        return (int)Math.Min(limit ?? int.MaxValue, capacity);
    }

    public static bool CanAdd(WorldState world, string faction, int? count = null, int? limit = null) =>
        !((count ?? world.Units.Count(u => u.Owner == faction)) >= (limit ?? Limit(world, faction)));
}
