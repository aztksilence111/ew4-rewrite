using Ew4.Core;

internal static class UnitNavigationChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            checks++;
            if (!value) throw new InvalidOperationException("Unit navigation: " + label);
        }
        var template = WorldChecks.Example();
        var unit = template.Units[1] with { Cell = 12, RemainingMovement = 6 };
        var basis = template with { Width = 5, Height = 5,
            Cells = Enumerable.Range(0, 25).Select(_ => new CellState()).ToArray(), Units = [unit] };
        var map = new HexMap(5, 5);
        var neighbors = Enumerable.Range(0, 6).Select(d => map.Neighbor(12, d)).ToArray();
        WorldState Blocked(bool allied) => basis with { Units = new[] { unit }.Concat(neighbors.Select((cell, i) => unit with
            { Id = "block." + i, Cell = cell, Owner = allied ? unit.Owner : "loser", RemainingMovement = 0 })).ToArray() };
        bool Available(WorldState world, WorldMoveRules? rules = null) => WorldActions.MovableUnits(world, unit.Owner, rules ?? new()).Contains(unit.Id);
        Check(Available(basis), "open ground allows movement");
        Check(!Available(Blocked(false)), "enemy enclosure has no landing");
        Check(Available(Blocked(true)), "allies can be crossed to reach a free landing");
        Check(!Available(Blocked(true) with { Units = Blocked(true).Units.Select(u => u.Id == unit.Id ? u with { RemainingMovement = 3 } : u).ToArray() }),
            "movement spent reaching allies does not count as a landing");
        var sea = basis with { Cells = basis.Cells.Select((c, i) => c with { Terrain = c.Terrain with { Sea = i != 12 } }).ToArray() };
        Check(!Available(sea), "land unit cannot land at sea");
        Check(Available(sea with { Units = [unit with { Transport = true }] }), "transport can enter sea");
        foreach (var inactive in new[] { unit with { RemainingMovement = 0 }, unit with { Morale = new(-3, 3, -3) },
            unit with { Combatant = unit.Combatant with { ConstructionTurns = 1 } },
            unit with { Combatant = unit.Combatant with { Health = 0 } },
            unit with { Combatant = unit.Combatant with { Profile = unit.Combatant.Profile with { Category = "fort" } } } })
            Check(!Available(basis with { Units = [inactive] }), "inactive unit cannot navigate");

        // Compare the short availability path with the full movement search under varied terrain and occupancy.
        var random = new Random(231023);
        for (int sample = 0; sample < 256; sample++)
        {
            var actor = unit with { RemainingMovement = random.Next(1, 7), Transport = sample % 3 == 0,
                Combatant = unit.Combatant with { Profile = unit.Combatant.Profile with
                    { Category = new[] { "infantry", "cavalry", "naval" }[sample % 3] } } };
            var units = new List<UnitState> { actor };
            var cells = basis.Cells.Select((c, i) => c with { Terrain = c.Terrain with
                { Active = i == actor.Cell || random.Next(6) != 0, Sea = random.Next(3) == 0,
                    TerrainCost = random.Next(1, 10), RiverEdges = (byte)random.Next(64), BridgeEdges = (byte)random.Next(64),
                    BlockedSeaEdges = (byte)random.Next(64) } }).ToArray();
            for (int cell = 0; cell < 25; cell++)
                if (cell != actor.Cell && cells[cell].Terrain.Active && random.Next(3) == 0)
                    units.Add(unit with { Id = "occupant." + cell, Cell = cell, Owner = random.Next(2) == 0 ? unit.Owner : "loser", RemainingMovement = 0 });
            var world = basis with { Cells = cells, Units = units };
            var rules = new WorldMoveRules { Search = new() { AllowOverdrawnEntry = sample % 2 == 0, BlockEnemyOccupied = sample % 7 != 0 } };
            var occupied = units.Select(u => u.Cell).ToHashSet();
            bool expected = WorldActions.Reachable(world, actor.Id, rules).Keys.Any(cell => !occupied.Contains(cell));
            Check(Available(world, rules) == expected, "matches full search " + sample);
        }
        return checks;
    }
}
