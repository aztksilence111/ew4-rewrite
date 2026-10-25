using Ew4.Core;
using Ew4.Persistence;

internal static class ControlledMovementChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, "Projection error precedence: " + error.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        var fixture = WorldChecks.Example();
        var rules = new WorldAiRules();
        PlanningScope.VerifyMovementProjections = true;
        try
        {
            for (int seed = 0; seed < 80; seed++)
            {
                var source = fixture with { Units = fixture.Units.Select(u => u with { RemainingMovement = 4,
                    Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Movement = 6 } } }).ToArray() };
                byte[] original = WorldSave.Encode(source);
                using (PlanningScope.Begin(rules))
                {
                    source.Validate();
                    var actor = source.Units[seed % source.Units.Count];
                    var cells = source.Cells.ToArray();
                    int destination = Enumerable.Range(0, cells.Length).First(i => !source.Units.Any(u => u.Cell == i));
                    cells[destination] = cells[destination] with { Owner = actor.Owner };
                    var next = PlanningScope.ProjectMovement(source, actor, destination, seed % 7, cells);
                    next.ValidateUncached();
                    Check(next.Units.Single(u => u.Id == actor.Id).Cell == destination, "Movement projection lost its unit");
                    Check(WorldSave.Encode(next).SequenceEqual(WorldSave.Encode(PlanningScope.WithRevision(next, next.Revision))), "Revision proof changes serialized state");
                    Reject(() => PlanningScope.ProjectMovement(source, actor, destination, 7, cells), "world.unit");
                    Reject(() => PlanningScope.ProjectMovement(source, actor, source.Units.First(u => u != actor).Cell, 0, cells), "world.capacity");
                    var invalid = cells.ToArray(); invalid[destination] = invalid[destination] with { Owner = "unknown" };
                    Reject(() => PlanningScope.ProjectMovement(source, actor, destination, 0, invalid), "world.cell");
                    invalid[destination] = cells[destination] with { Environment = -1 };
                    Reject(() => PlanningScope.ProjectMovement(source, actor, destination, 0, invalid), "world.cell");
                    invalid[destination] = cells[destination] with { Terrain = cells[destination].Terrain with { Active = false } };
                    Reject(() => PlanningScope.ProjectMovement(source, actor, destination, 0, invalid), "world.unit");
                }
                Check(original.SequenceEqual(WorldSave.Encode(source)), "Projection mutates source snapshot");
            }
            // Scope exit and a disabled callback subtree must discard old proofs.
            var world = fixture.Copy();
            using (PlanningScope.Begin(rules))
            {
                world.Validate();
                using (PlanningScope.Begin(rules with { Movement = rules.Movement with {
                    Extensions = new RuleEventBus(new Dictionary<string, ExtensionSkill> { ["unknown"] = new() { Id = "unknown" } }) } }))
                    ((CellState[])world.Cells)[0] = world.Cells[0] with { Environment = -1 };
                Reject(() => world.Validate(), "world.cell");
            }
            for (int seed = 0; seed < 24; seed++)
            {
                var source = fixture.Copy();
                var actor = source.Units[1];
                source = source with { ActiveFaction = actor.Owner, Units = source.Units.Select(u => u with { RemainingMovement = 6 }).ToArray() };
                source = source with { Cells = source.Cells.Select((c, i) => c with {
                    Owner = source.Factions[(i + seed) % source.Factions.Count].Id,
                    BuildingKind = i % 3 == 0 ? "city" : i % 3 == 1 ? "farmland" : null,
                    BuildingLevel = 1 }).ToArray() };
                if (seed % 2 == 0) source = source with { Jurisdictions = TerritorialJurisdictions.Create(source) };
                byte[] original = WorldSave.Encode(source);
                using var scope = PlanningScope.Begin(rules);
                source.Validate();
                var firstQuery = PlanningScope.Queries(source);
                Check(ReferenceEquals(firstQuery, PlanningScope.Queries(source)), "Query identity must be reused");
                var sameRevision = source.Copy(); sameRevision.Validate();
                Check(PlanningScope.Queries(sameRevision) != null && !ReferenceEquals(firstQuery, PlanningScope.Queries(sameRevision)), "Different worlds must not share queries");
                var range = WorldActions.Reachable(source, actor.Id);
                foreach (int destination in range.Keys.Where(i => i != actor.Cell && !source.Units.Any(u => u.Cell == i)))
                {
                    var path = range[destination].Path;
                    var expected = JurisdictionClaims.Move(source, TerritoryClaims.Move(new(source.Width, source.Height), source.Cells,
                        source.Units.Where(u => u.Id != actor.Id).Select(u => u.Cell).ToArray(), path, actor.Owner), path, actor.Owner);
                    var ownership = MovementOwnership.Move(source, new(source.Width, source.Height),
                        source.Units.Where(u => u.Id != actor.Id).Select(u => u.Cell).ToArray(), path, actor.Owner, new());
                    Check(ownership.Cells.SequenceEqual(expected), "Sparse jurisdiction capture differs");
                    Check(ownership.Changed.SequenceEqual(Enumerable.Range(0, expected.Count).Where(i => expected[i].Owner != source.Cells[i].Owner)), "Ownership set lost writes or order");
                    var fast = WorldActions.ProjectMove(source, actor.Id, destination, new(), range);
                    var full = WorldActions.Move(source, source.Revision, actor.Id, destination);
                    Check(WorldSave.Encode(fast.State).SequenceEqual(WorldSave.Encode(full.State)), "Borrowed candidate differs from committed snapshot");
                    Check(fast.Notices.SequenceEqual(full.Notices), "Sparse capture notice order changed");
                    Check(fast.State.Cells is not CellState[] && fast.State.Units is not UnitState[], "Controlled storage must not expose arrays");
                }
                Check(original.SequenceEqual(WorldSave.Encode(source)), "Borrowed event processing mutates source");
            }
        }
        finally { PlanningScope.VerifyMovementProjections = false; }
        return checks;
    }
}
