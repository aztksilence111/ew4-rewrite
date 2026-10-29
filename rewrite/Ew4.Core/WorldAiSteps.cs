using System;
using System.Linq;

namespace Ew4.Core;

public sealed record AiStepPlan(AiProductionPlan Production, AiUnitPlan? Unit, AiDeployCommand? Commander = null);

public static partial class WorldAi
{
    public static WorldChange ExecuteNextStep(WorldState state, WorldAiRules ai, WorldProductionRules production,
        WorldTurnRules turns, WorldBattleRules battle)
    {
        using var step = PlanningScope.Begin(ai, production, turns, battle);
        return ExecuteStep(state, PlanStep(state, ai, production, turns), ai, production, turns, battle);
    }

    public static AiStepPlan PlanStep(WorldState state, WorldAiRules ai, WorldProductionRules production, WorldTurnRules turns)
    {
        using var planning = PlanningScope.Begin(ai, production, turns);
        var work = new StepWork(state, ai, production, turns);
        while (!work.Advance()) { }
        return work.Result!;
    }

    internal sealed class StepWork(WorldState state, WorldAiRules ai, WorldProductionRules production, WorldTurnRules turns)
    {
        private int stage;
        private AiProductionPlan? attempt;
        private WorldState? projected;
        private UnitState[]? units;
        private int unitIndex;
        private UnitWork? unitWork;
        internal AiStepPlan? Result { get; private set; }
        internal bool Matches(WorldState world, WorldAiRules nextAi, WorldProductionRules nextProduction, WorldTurnRules nextTurns) =>
            ReferenceEquals(state, world) && ReferenceEquals(ai, nextAi) && ReferenceEquals(production, nextProduction) && ReferenceEquals(turns, nextTurns);
        internal bool Advance()
        {
            if (Result != null) return true;
            WorldActions.Ready(state, state.Revision);
            ai.Validate();
            if (stage == 0)
            {
                if (WorldDiplomacy.Dormant(state, state.ActiveFaction, ai.NeutralCamps))
                { Result = new(new(state.Revision, state.AiRandomState, state.AiRandomState, null, null), null); return true; }
                attempt = PlanProduction(state, ai, production, turns);
                if (attempt.Command != null) { Result = new(attempt, null); return true; }
                // Failed production consumed private draws; subsequent planning starts from that state.
                projected = PlanningScope.WithAiRandomState(state, attempt.FinalRandomState);
                stage = 1; return false;
            }
            if (stage == 1)
            {
                var commander = production.Commanders == null ? null : PlanCommander(projected!, production.Commanders, production.CommanderScoring);
                if (commander != null) { Result = new(attempt!, null, commander); return true; }
                units = state.Units.Where(u => u.Owner == state.ActiveFaction).OrderBy(u => u.Cell).ThenBy(u => u.Id, StringComparer.Ordinal).ToArray();
                stage = 2; return false;
            }
            if (unitWork == null)
            {
                while (unitIndex < units!.Length)
                {
                    var unit = units[unitIndex++];
                    if (ai.Behaviors.TryGetValue(unit.Behavior, out var behavior)
                        && !AiPlanning.Requests(behavior, new(unit.RemainingMovement, unit.Morale.Effective, unit.Combatant.ConstructionTurns, unit.Acted)).Eligible) continue;
                    unitWork = new UnitWork(projected!, unit.Id, ai); break;
                }
                if (unitWork == null) { Result = new(attempt!, null); return true; }
            }
            if (!unitWork.Advance()) return false;
            if (unitWork.Result!.Candidates.Count > 0) { Result = new(attempt!, unitWork.Result); return true; }
            unitWork = null; return false;
        }
    }

    public static WorldChange ExecuteStep(WorldState state, AiStepPlan plan, WorldAiRules ai, WorldProductionRules production,
        WorldTurnRules turns, WorldBattleRules battle)
    {
        using var execution = PlanningScope.Begin(ai, production, turns, battle);
        WorldActions.Ready(state, plan.Production.ExpectedRevision);
        ai.Validate(); production.Validate(); turns.Validate();
        if (state.AiRandomState != plan.Production.InitialRandomState)
            throw new RuleViolation("ai.step_stale_random", "AI random state changed after planning");
        if (plan.Production.Command != null)
        {
            if (plan.Unit != null || plan.Commander != null) throw new RuleViolation("ai.step_selection", "Step cannot execute production and a fallback command");
            return ExecuteProduction(state, plan.Production, production, ai);
        }
        var projected = PlanningScope.WithAiRandomState(state, plan.Production.FinalRandomState);
        if (plan.Commander != null)
        {
            if (plan.Unit != null || plan.Commander.ExpectedRevision != state.Revision || production.Commanders == null)
                throw new RuleViolation("ai.step_selection", "Invalid commander fallback command");
            return WorldCommanders.Deploy(projected, state.Revision, plan.Commander.UnitId, plan.Commander.OfferId, production.Commanders);
        }
        if (plan.Unit != null)
        {
            if (plan.Unit.ExpectedRevision != state.Revision || plan.Unit.InitialRandomState != projected.AiRandomState)
                throw new RuleViolation("ai.step_selection", "Fallback unit plan does not follow the production attempt");
            return ExecuteUnit(projected, plan.Unit, ai, battle);
        }
        return WorldTurns.EndTurn(projected, state.Revision, turns);
    }
}
