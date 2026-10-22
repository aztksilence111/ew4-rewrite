using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record AiUnitPlan(long ExpectedRevision, string UnitId, ulong InitialRandomState, ulong FinalRandomState,
    IReadOnlyList<AiCandidate<WorldAiCommand>> Candidates);

public static partial class WorldAi
{
    public static AiUnitPlan PlanUnit(WorldState state, string unitId, WorldAiRules rules)
    {
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id) throw new RuleViolation("ai.unit", "Unknown active faction unit");
        if (WorldDiplomacy.Dormant(state, faction.Id, rules.NeutralCamps))
            return new(state.Revision, unitId, state.AiRandomState, state.AiRandomState, Array.Empty<AiCandidate<WorldAiCommand>>());
        if (!rules.Behaviors.TryGetValue(unit.Behavior, out var behavior)) throw new RuleViolation("ai.behavior", "Unmapped unit AI behavior");
        var requests = AiPlanning.Requests(behavior, new(unit.RemainingMovement, unit.Morale.Effective, unit.Combatant.ConstructionTurns, unit.Acted));
        var result = new List<AiCandidate<WorldAiCommand>>();
        ulong finalRandom = state.AiRandomState;
        foreach (var kind in requests.Kinds)
        {
            switch (kind)
            {
                case AiCandidateKind.Hold:
                    var facts = Facts(state, unit, rules);
                    var cell = state.Cells[unit.Cell];
                    long hold = AiPlanning.HoldScore(new(AiTactics.Location(Site(state, unit.Cell, rules), facts, rules.Tactics),
                        AiTactics.Strength(facts, rules.Tactics), ThreatStrength(state, unit, rules), cell.Environment,
                        facts.Category == rules.HoldInfantryCategory, cell.InstallationKind != null, facts.Category == rules.Tactics.NavalCategory,
                        facts.Health, facts.MaximumHealth), rules.Hold);
                    result.Add(new(new AiHoldCommand(state.Revision, unitId), hold));
                    break;
                case AiCandidateKind.TacticalMove:
                    foreach (var candidate in TacticalMoveCandidates(state, unitId, rules)) result.Add(new(candidate.Action, candidate.Score));
                    break;
                case AiCandidateKind.StrategicAdvance:
                    var strategic = StrategicCandidates(state, unitId, rules);
                    finalRandom = strategic.FinalRandomState;
                    foreach (var candidate in strategic.Candidates) result.Add(new(candidate.Action, candidate.Score));
                    break;
                case AiCandidateKind.Strike:
                    foreach (var candidate in StrikeCandidates(state, unitId, rules)) result.Add(new(candidate.Action, candidate.Score));
                    break;
            }
        }
        if (requests.Eligible && result.Count == 0) result.Add(new(new AiHoldCommand(state.Revision, unitId), 0));
        return new(state.Revision, unitId, state.AiRandomState, finalRandom, result.ToArray());
    }

    public static AiUnitPlan? PlanNextUnit(WorldState state, WorldAiRules rules)
    {
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Validate();
        foreach (var unit in state.Units.Where(u => u.Owner == faction.Id).OrderBy(u => u.Cell).ThenBy(u => u.Id, StringComparer.Ordinal))
        {
            var plan = PlanUnit(state, unit.Id, rules);
            if (plan.Candidates.Count > 0) return plan;
        }
        return null;
    }

    public static WorldChange ExecuteUnit(WorldState state, AiUnitPlan plan, WorldAiRules rules, WorldBattleRules battleRules, int? selected = null)
    {
        var faction = WorldActions.Ready(state, plan.ExpectedRevision);
        rules.Validate();
        if (state.AiRandomState != plan.InitialRandomState) throw new RuleViolation("ai.unit_stale_random", "AI random state changed after planning");
        int index = selected ?? AiPlanning.Best(plan.Candidates);
        if (index < 0 || index >= plan.Candidates.Count) throw new RuleViolation("ai.unit_selection", "No such unit candidate");
        var command = plan.Candidates[index].Action;
        if (command.ExpectedRevision != plan.ExpectedRevision || command.UnitId != plan.UnitId)
            throw new RuleViolation("ai.unit_selection", "Unit command does not match its plan");
        WorldChange change;
        var executionState = state with { AiRandomState = plan.FinalRandomState };
        switch (command)
        {
            case AiStrikeCommand strike: change = ExecuteStrike(executionState, strike, battleRules); break;
            case AiMoveCommand move: change = ExecuteMove(executionState, move, rules, battleRules); break;
            case AiStrategicCommand strategic:
                change = ExecuteStrategic(state, new(plan.ExpectedRevision, plan.UnitId, plan.InitialRandomState, plan.FinalRandomState,
                    new[] { new AiCandidate<AiStrategicCommand>(strategic, plan.Candidates[index].Score) }), 0, rules);
                break;
            case AiHoldCommand:
                var unit = state.Units.SingleOrDefault(u => u.Id == command.UnitId);
                if (unit == null || unit.Owner != faction.Id) throw new RuleViolation("ai.unit", "Unknown active faction unit");
                var next = (state with { Revision = checked(state.Revision + 1), Units = state.Units.Select(u => u.Id == unit.Id
                    ? u with { RemainingMovement = 0, Acted = true } : u).ToArray() }).Copy();
                change = new(next, new[] { new WorldNotice("ai.unit_held", unit.Id, unit.Cell) });
                break;
            default: throw new RuleViolation("ai.command", "Unsupported unit AI command");
        }
        var committed = command is AiStrikeCommand or AiMoveCommand ? change.State
            : change.State with { AiRandomState = plan.FinalRandomState };
        committed.Validate();
        return change with { State = committed };
    }
}
