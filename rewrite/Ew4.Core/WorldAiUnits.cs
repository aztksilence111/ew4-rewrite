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
        using var planning = PlanningScope.Begin(rules);
        var work = new UnitWork(state, unitId, rules);
        while (!work.Advance()) { }
        return work.Result!;
    }

    // Candidates and private random draws retain their original order across yields.
    internal sealed class UnitWork
    {
        private readonly WorldState state;
        private readonly string unitId;
        private readonly WorldAiRules rules;
        private readonly UnitState unit;
        private readonly AiCandidateKind[] kinds;
        private readonly bool eligible;
        private readonly List<AiCandidate<WorldAiCommand>> result = new();
        private ulong finalRandom;
        private int index;
        internal AiUnitPlan? Result { get; private set; }
        internal UnitWork(WorldState state, string unitId, WorldAiRules rules)
        {
            this.state = state; this.unitId = unitId; this.rules = rules;
            var faction = WorldActions.Ready(state, state.Revision);
            rules.Validate();
            unit = (PlanningScope.Queries(state) is { } queries ? queries.Units.GetValueOrDefault(unitId)
                : state.Units.SingleOrDefault(u => u.Id == unitId))!;
            if (unit == null || unit.Owner != faction.Id) throw new RuleViolation("ai.unit", "Unknown active faction unit");
            finalRandom = state.AiRandomState;
            if (WorldDiplomacy.Dormant(state, faction.Id, rules.NeutralCamps))
            { kinds = []; return; }
            if (!rules.Behaviors.TryGetValue(unit.Behavior, out var behavior)) throw new RuleViolation("ai.behavior", "Unmapped unit AI behavior");
            var requests = AiPlanning.Requests(behavior, new(unit.RemainingMovement, unit.Morale.Effective, unit.Combatant.ConstructionTurns, unit.Acted));
            kinds = requests.Kinds.ToArray(); eligible = requests.Eligible;
        }
        internal bool Advance()
        {
            if (Result != null) return true;
            if (index < kinds.Length)
            {
                var kind = kinds[index++];
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
            if (index < kinds.Length) return false;
            if (eligible && result.Count == 0) result.Add(new(new AiHoldCommand(state.Revision, unitId), 0));
            Result = new(state.Revision, unitId, state.AiRandomState, finalRandom, result.ToArray());
            return true;
        }
    }

    public static AiUnitPlan? PlanNextUnit(WorldState state, WorldAiRules rules)
    {
        using var planning = PlanningScope.Begin(rules);
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Validate();
        if (WorldDiplomacy.Dormant(state, faction.Id, rules.NeutralCamps)) return null;
        foreach (var unit in state.Units.Where(u => u.Owner == faction.Id).OrderBy(u => u.Cell).ThenBy(u => u.Id, StringComparer.Ordinal))
        {
            // Empty request sets cannot consume random draws or produce a candidate.
            if (rules.Behaviors.TryGetValue(unit.Behavior, out var behavior)
                && !AiPlanning.Requests(behavior, new(unit.RemainingMovement, unit.Morale.Effective, unit.Combatant.ConstructionTurns, unit.Acted)).Eligible) continue;
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
        var executionState = PlanningScope.WithAiRandomState(state, plan.FinalRandomState);
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
                var next = (state with
                {
                    Revision = checked(state.Revision + 1),
                    Units = state.Units.Select(u => u.Id == unit.Id
                    ? u with { RemainingMovement = 0, Acted = true } : u).ToArray()
                }).Copy();
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
