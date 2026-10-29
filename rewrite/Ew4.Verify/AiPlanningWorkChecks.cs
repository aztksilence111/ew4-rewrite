using Ew4.Core;
using Ew4.Persistence;

internal static class AiPlanningWorkChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (RuleViolation error) { Check(error.Code == code, "Unexpected rejection: " + error.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        bool SamePlan(AiStepPlan actual, AiStepPlan expected) => actual.Production == expected.Production
            && actual.Commander == expected.Commander && (actual.Unit == null || expected.Unit == null
                ? actual.Unit == expected.Unit
                : actual.Unit.ExpectedRevision == expected.Unit.ExpectedRevision && actual.Unit.UnitId == expected.Unit.UnitId
                    && actual.Unit.InitialRandomState == expected.Unit.InitialRandomState && actual.Unit.FinalRandomState == expected.Unit.FinalRandomState
                    && actual.Unit.Candidates.SequenceEqual(expected.Unit.Candidates));
        var fixture = WorldChecks.Example();
        var ai = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var production = new WorldProductionRules();
        var turns = new WorldTurnRules();
        var cells = fixture.Cells.ToArray();
        var source = fixture with { Cells = Array.AsReadOnly(cells) };
        WorldState frozen;
        using (PlanningScope.Begin(ai, production, turns, snapshots: true))
        {
            source.Validate();
            frozen = source.Copy();
            Check(frozen.Cells is FrozenWorldCells && frozen.Cells is not IList<CellState>, "Frozen cells expose a mutable interface");
            Check(!ReferenceEquals(source.Cells, frozen.Cells), "Caller wrapper was trusted");
            Check(ReferenceEquals(frozen.Cells, frozen.Copy().Cells), "Owned frozen cells were cloned");
            Check(PlanningScope.IsValidated(frozen), "Validated copy lost its scoped proof");
            var queries = PlanningScope.Queries(source);
            var randomProjection = PlanningScope.WithAiRandomState(source, source.AiRandomState + 1);
            Check(ReferenceEquals(queries, PlanningScope.Queries(randomProjection)), "RNG-only projection lost spatial queries");
            Check(PlanningScope.Queries(source with { Revision = source.Revision + 1 }) == null, "Arbitrary clone inherited spatial queries");
        }
        Check(!PlanningScope.IsValidated(frozen), "Copy proof survived scope exit");
        Check(frozen.Copy().Cells is CellState[], "Public copy compatibility changed");
        byte[] encoded = WorldSave.Encode(frozen);
        cells[0] = cells[0] with { Owner = "missing" };
        Reject(source.Validate, "world.cell");
        Check(encoded.SequenceEqual(WorldSave.Encode(frozen)), "Caller mutation leaked into frozen cells");
        Reject(() => (frozen with { Factions = frozen.Factions.Where(f => f.Id != "loser").ToArray() }).Validate(), "world.cell");
        foreach (var invalid in new[] { fixture.Cells[0] with { Environment = -1 }, fixture.Cells[0] with { BuildingLevel = -1 },
            fixture.Cells[0] with { FireTerrain = "" }, fixture.Cells[0] with { Terrain = new() { TerrainCost = 0 } } })
            Reject(() => FrozenWorldCells.Copy([invalid]), "world.cell");
        var capacityWorld = frozen with { Cells = FrozenWorldCells.Copy(frozen.Cells.Select((c, i) => i == 1
            ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray()), ArmyCapacity = new() };
        capacityWorld.Validate();
        var levels = (Dictionary<string, int[]>)capacityWorld.ArmyCapacity!.Buildings;
        levels["city"] = [0];
        Reject(capacityWorld.Validate, "army.capacity_level");

        for (ulong seed = 0; seed < 24; seed++)
        {
            var world = fixture with { AiRandomState = seed, Units = fixture.Units.Select(u => u with {
                RemainingMovement = 6, Behavior = seed % 3 == 0 ? "3" : "default",
                Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Grade = "0" } } }).ToArray() };
            byte[] before = WorldSave.Encode(world);
            AiStepPlan expected;
            using (PlanningScope.Begin(ai, production, turns))
            {
                var attempt = WorldAi.PlanProduction(world, ai, production, turns);
                var projected = PlanningScope.WithAiRandomState(world, attempt.FinalRandomState);
                expected = new(attempt, WorldAi.PlanNextUnit(projected, ai));
            }
            var work = new WorldAi.StepWork(world, ai, production, turns);
            int stages = 0;
            while (work.Result == null)
            {
                using (PlanningScope.Begin(ai, production, turns)) work.Advance();
                Check(!PlanningScope.IsValidated(world), "Resumable planning retained a validation proof");
                Check(before.SequenceEqual(WorldSave.Encode(world)), "Partial planning changed durable state or RNG");
                Check(++stages < 16, "Planner failed to finish");
            }
            Check(stages > 2, "Fallback planning did not split into stages");
            Check(SamePlan(work.Result!, expected), "Resumed plan changed candidates, order or private RNG");
            Check(SamePlan(WorldAi.PlanStep(world, ai, production, turns), expected), "Synchronous planner changed decisions");
            Check(!work.Matches(world with { }, ai, production, turns) && !work.Matches(world, ai with { }, production, turns)
                && !work.Matches(world, ai, production with { }, turns) && !work.Matches(world, ai, production, turns with { }), "Stale planner identity accepted");
        }
        var mutable = fixture.Copy();
        var pending = new WorldAi.StepWork(mutable, ai, production, turns);
        using (PlanningScope.Begin(ai, production, turns)) pending.Advance();
        ((CellState[])mutable.Cells)[0] = mutable.Cells[0] with { Owner = "missing" };
        using (PlanningScope.Begin(ai, production, turns)) Reject(() => pending.Advance(), "world.cell");
        return checks;
    }
}
