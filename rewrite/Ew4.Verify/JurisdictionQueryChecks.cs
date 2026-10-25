using Ew4.Core;
using Ew4.Persistence;

internal static class JurisdictionQueryChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
        var fixture = WorldChecks.Example();
        for (int seed = 0; seed < 48; seed++)
        {
            var cells = Enumerable.Range(0, 20).Select(i => new CellState {
                Owner = fixture.Factions[(i + seed) % 2].Id,
                BuildingKind = i is 0 or 19 ? "city" : i == 9 ? "farmland" : null,
                BuildingLevel = 1 }).ToArray();
            var units = fixture.Units.Select((u, i) => u with { Cell = i == 0 ? 1 : 18,
                Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Category = seed % 3 == 0 ? "fort" : "infantry" } } }).ToArray();
            var state = fixture with { Width = 5, Height = 4, Cells = cells, Units = units,
                Factions = fixture.Factions.Select(f => f with { Camp = seed % 2 == 0 ? "allied" : f.Camp }).ToArray(),
                Jurisdictions = new() { CenterCaptureVersion = 2,
                    CellCenters = Enumerable.Range(0, 20).Select(i => i < 10 ? 0 : 19).ToArray(),
                    Borders = [new(0, 19)], DetachedCells = seed % 4 == 0 ? [4, 5] : [] } };
            state.Validate();
            byte[] before = WorldSave.Encode(state);
            var expected = new Dictionary<string, byte[]>();
            foreach (string owner in state.Factions.Select(f => f.Id))
            {
                foreach (var path in new[] { new[] { 1, 0 }, new[] { 8, 9 } })
                {
                    var uncached = JurisdictionClaims.Move(state, TerritoryClaims.Move(new(5, 4), state.Cells,
                        units.Select(u => u.Cell).ToArray(), path, owner), path, owner);
                    expected[owner + "/" + path[1]] = WorldSave.Encode(state with { Cells = uncached });
                }
                expected[owner + "/fort"] = WorldSave.Encode(state with { Cells = JurisdictionClaims.DestroyFort(state, 1, owner) });
            }
            using (PlanningScope.Begin(new WorldAiRules()))
            {
                state.Validate();
                var queries = PlanningScope.Queries(state)!;
                foreach (string owner in state.Factions.Select(f => f.Id).Reverse())
                {
                    foreach (var path in new[] { new[] { 1, 0 }, new[] { 8, 9 }, new[] { 1, 0 } })
                    {
                        var ownership = MovementOwnership.Move(state, new(5, 4), units.Select(u => u.Cell).ToArray(), path, owner, new());
                        Check(expected[owner + "/" + path[1]].SequenceEqual(WorldSave.Encode(state with { Cells = ownership.Cells })), "Cached district transfer differs by actor or leaked releases");
                    }
                    Check(expected[owner + "/fort"].SequenceEqual(WorldSave.Encode(state with {
                        Cells = JurisdictionClaims.DestroyFort(state, 1, owner) })), "Cached fort release differs");
                }
                var topology = queries.Jurisdictions;
                Check(ReferenceEquals(topology, queries.Jurisdictions), "District topology not reused");
                Check(topology.Cells(0).SequenceEqual(Enumerable.Range(0, 10)), "District cell order differs");
                var cloned = state.Copy(); cloned.Validate();
                Check(!ReferenceEquals(topology, PlanningScope.Queries(cloned)!.Jurisdictions), "Topology escaped exact world identity");
                Check(topology.Allowed(state.Factions[0].Id).Order().SequenceEqual(new[] { 0, 19 }), "Frontier center membership differs");
            }
            Check(before.SequenceEqual(WorldSave.Encode(state)), "Cached capture mutated snapshot");
            Check(PlanningScope.Queries(state) == null, "District query escaped scope");
        }
        return checks;
    }
}
