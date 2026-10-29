using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

// Only this type owns its backing array. A caller's read-only wrapper is not proof.
internal sealed class FrozenWorldCells : IReadOnlyList<CellState>
{
    private readonly CellState[] cells;
    private readonly string[] owners;
    private readonly CellState[] buildings;
    private FrozenWorldCells(IReadOnlyList<CellState> source)
    {
        cells = source.ToArray();
        foreach (var cell in cells) WorldState.ValidateCellValue(cell);
        owners = cells.Where(c => c.Owner != null).Select(c => c.Owner!).Distinct(StringComparer.Ordinal).ToArray();
        buildings = cells.Where(c => c.Terrain.Active && c.BuildingKind != null).ToArray();
    }
    internal static IReadOnlyList<CellState> Copy(IReadOnlyList<CellState> source) =>
        source is FrozenWorldCells ? source : new FrozenWorldCells(source);
    internal void Validate(WorldState world, HashSet<string> factions)
    {
        if (owners.Any(owner => !factions.Contains(owner))) throw new RuleViolation("world.cell", "Invalid cell state or owner");
        if (world.ArmyCapacity != null)
            foreach (var cell in buildings) WorldArmyCapacity.BuildingCapacity(world, cell);
    }
    public int Count => cells.Length;
    public CellState this[int index] => cells[index];
    public IEnumerator<CellState> GetEnumerator() => ((IEnumerable<CellState>)cells).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
