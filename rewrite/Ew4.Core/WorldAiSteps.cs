using System;

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
        WorldActions.Ready(state, state.Revision);
        ai.Validate();
        if (WorldDiplomacy.Dormant(state, state.ActiveFaction, ai.NeutralCamps))
            return new(new(state.Revision, state.AiRandomState, state.AiRandomState, null, null), null);
        var attempt = PlanProduction(state, ai, production, turns);
        if (attempt.Command != null) return new(attempt, null);
        // Failed production consumed private draws; subsequent planning starts from that state.
        var projected = PlanningScope.WithAiRandomState(state, attempt.FinalRandomState);
        var commander = production.Commanders == null ? null : PlanCommander(projected, production.Commanders, production.CommanderScoring);
        if (commander != null) return new(attempt, null, commander);
        return new(attempt, PlanNextUnit(projected, ai));
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
