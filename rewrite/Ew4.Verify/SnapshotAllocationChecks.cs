using Ew4.Core;
using Ew4.Persistence;

internal static class SnapshotAllocationChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        var fixture = WorldChecks.Example();
        var cells = fixture.Cells.Select(c => c with { IncomeOverride = new(1, 2, 3) }).ToArray();
        var source = fixture with { Cells = cells };
        var snapshot = source.Copy();
        byte[] before = WorldSave.Encode(snapshot);
        Check(before.SequenceEqual(WorldSave.Encode(source)), "Snapshot changed cell serialization");
        cells[0] = cells[0] with { Owner = "missing", Terrain = cells[0].Terrain with { Active = false },
            IncomeOverride = cells[0].IncomeOverride! with { Money = 99 } };
        Check(before.SequenceEqual(WorldSave.Encode(snapshot)), "Source cell replacement changed snapshot");
        var copyCells = (CellState[])snapshot.Cells;
        copyCells[1] = copyCells[1] with { Name = "snapshot", Terrain = copyCells[1].Terrain with { TerrainCost = 99 } };
        Check(source.Cells[1].Name != "snapshot" && source.Cells[1].Terrain.TerrainCost != 99,
            "Snapshot cell replacement changed source");
        Check(snapshot.Cells[0].IncomeOverride!.Money == 1 && snapshot.Cells[0].Terrain.Active,
            "Nested immutable cell values changed");
        // Retain the former grouping predicates as an independent validation oracle.
        var random = new Random(241024);
        for (int sample = 0; sample < 300; sample++)
        {
            var factions = fixture.Factions.Select((f, i) => f with { Camp = sample % 3 == 0 ? "same" : "camp" + i }).ToArray();
            var units = Enumerable.Range(0, random.Next(0, 30)).Select(i => fixture.Units[0] with
            { Id = "check." + i, Owner = factions[random.Next(factions.Length)].Id, Cell = random.Next(fixture.Cells.Count) }).ToArray();
            var limits = new WorldLimits { UnitsPerCell = random.Next(1, 6), UnitsPerFaction = sample % 2 == 0 ? null : random.Next(1, 20) };
            var world = fixture with { Units = units, Factions = factions, Limits = limits };
            string? expected = units.GroupBy(u => u.Cell).Any(g => g.Count() > limits.UnitsPerCell)
                || units.GroupBy(u => u.Owner).Any(g => g.Count() > limits.UnitsPerFaction) ? "world.capacity"
                : units.GroupBy(u => u.Cell).Any(g => g.Select(u => factions.Single(f => f.Id == u.Owner).Camp)
                    .Distinct(StringComparer.Ordinal).Count() > 1) ? "world.occupants" : null;
            string? actual = null;
            try { world.Validate(); } catch (RuleViolation error) { actual = error.Code; }
            Check(actual == expected, "Occupancy validation differs from grouping oracle: " + sample);
        }
        return checks;
    }
}
