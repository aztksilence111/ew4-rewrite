using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

// The only mutation capability is SetOwner; sealing revokes it before a proof escapes.
internal sealed class OwnershipBuffer
{
    private readonly CellState[] cells;
    private readonly HashSet<int> touched = new();
    private bool sealedBuffer;
    internal OwnershipBuffer(IReadOnlyList<CellState> source) { cells = source.ToArray(); }
    internal CellState this[int index] => cells[index];
    internal IEnumerable<int> Touched => touched;
    internal void SetOwner(int index, string? owner)
    {
        if (sealedBuffer) throw new InvalidOperationException("Ownership buffer is sealed");
        if (cells[index].Owner == owner) return;
        cells[index] = cells[index] with { Owner = owner };
        touched.Add(index);
    }
    internal IReadOnlyList<CellState> Seal() { sealedBuffer = true; return Array.AsReadOnly(cells); }
}

internal sealed class MovementOwnership
{
    internal WorldState Source { get; }
    internal IReadOnlyList<CellState> Cells { get; }
    internal IReadOnlyList<int> Changed { get; }
    private MovementOwnership(WorldState source, OwnershipBuffer buffer)
    {
        Source = source;
        Changed = Array.AsReadOnly(buffer.Touched.Where(i => source.Cells[i].Owner != buffer[i].Owner).OrderBy(i => i).ToArray());
        Cells = buffer.Seal();
    }
    internal static MovementOwnership Move(WorldState source, HexMap map, IReadOnlyCollection<int> occupied,
        IReadOnlyList<int> path, string owner, TerritoryMoveRules rules)
    {
        var buffer = new OwnershipBuffer(source.Cells);
        TerritoryClaims.MoveOwners(map, source.Cells, occupied, path, owner, rules, buffer);
        JurisdictionClaims.MoveOwners(source, buffer, path, owner);
        return new(source, buffer);
    }
}
