using Ew4.Core;

internal static class SearchWorkspaceChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value) { checks++; if (!value) throw new InvalidOperationException("Distance workspace regression"); }
        var random = new Random(20261024);
        for (int sample = 0; sample < 80; sample++)
        {
            var map = new HexMap(1 + random.Next(12), 1 + random.Next(10));
            var cells = Enumerable.Range(0, map.Count).Select(i => new MovementCell {
                Active = random.Next(6) != 0, Sea = random.Next(3) == 0, TerrainCost = 1 + random.Next(8),
                RiverEdges = (byte)random.Next(64), BridgeEdges = (byte)random.Next(64),
                BlockedSeaEdges = (byte)random.Next(64), OccupantCamp = i % 7 == 0 ? "other" : null }).ToArray();
            var actor = new MovementActor { Category = sample % 3 == 0 ? "naval" : "cavalry", Camp = "self",
                Transport = sample % 2 == 0, Geography = sample % 5 == 0, FullBudget = 9, Remaining = 5 };
            var rules = new MovementRules { AllowOverdrawnEntry = sample % 4 == 0, BlockEnemyOccupied = sample % 5 != 0 };
            int[] targets = [map.Count - 1, 0, map.Count - 1, random.Next(map.Count)];
            var expected = Reference(map, cells, targets, actor, rules);
            var actual = Movement.DistancesTo(map, cells, targets, actor, rules);
            Check(actual.SequenceEqual(expected));
            for (int origin = 0; origin < map.Count; origin++)
            {
                var requested = Enumerable.Range(0, map.Count).Where(i => (i + origin) % 3 == 0).ToArray();
                var bounded = Movement.DistancesForAdvance(map, cells, targets, actor, rules, origin, requested);
                Check(bounded.All(p => (p.Key == origin || requested.Contains(p.Key)) && expected.TryGetValue(p.Key, out long cost) && cost == p.Value));
                Check(bounded.TryGetValue(origin, out long source) == expected.ContainsKey(origin));
                Check(requested.All(i => !expected.TryGetValue(i, out long cost) || expected.ContainsKey(origin) && cost >= source
                    || bounded.TryGetValue(i, out long found) && found == cost));
            }
            Check(actual.Keys.SequenceEqual(expected.Keys) && actual.Values.SequenceEqual(expected.Values));
            Check(!actual.TryGetValue(-1, out _) && !actual.ContainsKey(map.Count));
            var captured = actual.ToArray();
            // Keep the first result alive while many different-sized heap leases finish.
            for (int repeat = 0; repeat < 8; repeat++) Movement.DistancesTo(map, cells, [random.Next(map.Count)], actor, rules);
            Check(actual.SequenceEqual(captured));
            cells[0] = cells[0] with { OccupantCamp = "self", Sea = false, Active = true };
            Check(Movement.DistancesTo(map, cells, targets, actor, rules).SequenceEqual(Reference(map, cells, targets, actor, rules)));
        }
        var nestedMap = new HexMap(5, 4);
        var nestedCells = Enumerable.Range(0, nestedMap.Count).Select(_ => new MovementCell()).ToArray();
        var walker = new MovementActor { Camp = "self", FullBudget = 6, Remaining = 6 };
        var nested = new NestedCells(nestedCells, () => {
            Check(Movement.DistancesTo(nestedMap, nestedCells, [19], walker)[0] > 0);
        });
        Check(Movement.DistancesTo(nestedMap, nested, [0], walker).SequenceEqual(Reference(nestedMap, nestedCells, [0], walker, new())));
        var savedAdvance = Movement.DistancesForAdvance(nestedMap, nestedCells, [19], walker, new(), 0, [1, 2, 3, 19]);
        var savedEntries = savedAdvance.ToArray();
        var boundedNested = new NestedCells(nestedCells, () => {
            Check(Movement.DistancesForAdvance(nestedMap, nestedCells, [0], walker, new(), 19, [0, 1, 18]).ContainsKey(19));
        });
        Check(Movement.DistancesForAdvance(nestedMap, boundedNested, [19], walker, new(), 0, [1, 2, 3, 19]).SequenceEqual(savedEntries));
        Check(savedAdvance.SequenceEqual(savedEntries));
        var invalid = nestedCells.ToArray(); invalid[0] = new() { TerrainCost = 0 };
        try { Movement.DistancesTo(nestedMap, invalid, [0], walker); throw new InvalidOperationException("Invalid entry accepted"); }
        catch (RuleViolation error) { Check(error.Code == "movement.configuration"); }
        Check(Movement.DistancesTo(nestedMap, nestedCells, [0], walker).Count == nestedMap.Count);
        return checks;
    }

    private sealed class NestedCells(MovementCell[] cells, Action search) : IReadOnlyList<MovementCell>
    {
        private bool called;
        public MovementCell this[int index] { get { if (!called) { called = true; search(); } return cells[index]; } }
        public int Count => cells.Length;
        public IEnumerator<MovementCell> GetEnumerator() => ((IEnumerable<MovementCell>)cells).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IReadOnlyDictionary<int, long> Reference(HexMap map, IReadOnlyList<MovementCell> cells,
        IReadOnlyList<int> targets, MovementActor actor, MovementRules rules)
    {
        var result = new Dictionary<int, long>();
        var queue = new SortedSet<(long Cost, int Cell)>();
        foreach (int cell in targets)
            if (cells[cell].Active) { result[cell] = 0; queue.Add((0, cell)); }
        while (queue.Count > 0)
        {
            var current = queue.Min; queue.Remove(current);
            if (result[current.Cell] != current.Cost) continue;
            for (int direction = 0; direction < 6; direction++)
            {
                int previous = map.Neighbor(current.Cell, direction);
                if (previous < 0 || !cells[previous].Active) continue;
                int? cost = Movement.EntryCost(cells[previous], cells[current.Cell], direction, actor, rules);
                if (!cost.HasValue || cost.Value > actor.FullBudget && !rules.AllowOverdrawnEntry) continue;
                long total = checked(current.Cost + cost.Value);
                if (result.TryGetValue(previous, out long old) && old <= total) continue;
                result[previous] = total; queue.Add((total, previous));
            }
        }
        return result;
    }
}
