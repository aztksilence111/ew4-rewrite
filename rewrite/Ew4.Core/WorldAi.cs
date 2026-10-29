using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public abstract record WorldAiCommand(long ExpectedRevision, string UnitId);
public sealed record AiStrikeCommand(long ExpectedRevision, string UnitId, string TargetId) : WorldAiCommand(ExpectedRevision, UnitId);
public sealed record AiMoveCommand(long ExpectedRevision, string UnitId, int Destination, string? TargetId = null) : WorldAiCommand(ExpectedRevision, UnitId);
public sealed record AiHoldCommand(long ExpectedRevision, string UnitId) : WorldAiCommand(ExpectedRevision, UnitId);
public sealed record WorldAiRules
{
    public AiTacticalRules Tactics { get; init; } = new();
    public IReadOnlyDictionary<string, int> ExperienceLevels { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ProfileTraits { get; init; } = new Dictionary<string, IReadOnlyCollection<string>>();
    public IReadOnlyDictionary<int, int> SitePriorities { get; init; } = new Dictionary<int, int>();
    public IReadOnlyCollection<string> PriorityBehaviors { get; init; } = new[] { "1" };
    public IReadOnlyCollection<string> NeutralCamps { get; init; } = Array.Empty<string>();
    public WorldMoveRules Movement { get; init; } = new();
    public AiMoveScoringRules MoveScoring { get; init; } = new();
    public WorldStrategyRules Strategy { get; init; } = new();
    public AiHoldRules Hold { get; init; } = new();
    public string HoldInfantryCategory { get; init; } = "infantry";
    public IReadOnlyDictionary<string, AiBehavior> Behaviors { get; init; } = new Dictionary<string, AiBehavior>
    {
        ["default"] = new(true, true, true), ["0"] = new(true, true, true), ["1"] = new(false, true, true),
        ["2"] = new(true, true, true), ["3"] = new(true, false, false)
    };
    public bool UseOriginForMoveAttackScore { get; init; }
    public void Validate() => PlanningScope.Validate(this, ValidateCore);
    private void ValidateCore()
    {
        Tactics.Validate(); MoveScoring.Validate(); Strategy.Validate();
        if (ExperienceLevels.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)
            || ProfileTraits.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value.Any(string.IsNullOrWhiteSpace))
            || SitePriorities.Any(p => p.Key < 0 || p.Value < 0) || PriorityBehaviors.Concat(NeutralCamps).Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(HoldInfantryCategory) || Behaviors.Any(p => string.IsNullOrWhiteSpace(p.Key))
            || Hold.EnvironmentPenalty < 0 || Hold.NavalPenalty < 0 || Hold.InstallationMultiplier < 0)
            throw new RuleViolation("ai.configuration", "Invalid world AI mappings");
    }
}

public static partial class WorldAi
{
    private static AiUnitFacts Facts(WorldState state, UnitState value, WorldAiRules rules)
    {
        var cache = PlanningScope.Queries(state)?.Ai(rules).Units;
        if (cache != null && cache.TryGetValue(value, out var found)) return found;
        var facts = BuildFacts(state, value, rules);
        cache?.Add(value, facts);
        return facts;
    }
    private static AiUnitFacts BuildFacts(WorldState state, UnitState value, WorldAiRules rules)
    {
        if (!rules.ExperienceLevels.TryGetValue(value.Veterancy, out int level)) throw new RuleViolation("ai.stage", "Unmapped veterancy level");
        var profile = value.Combatant.Profile;
        return new()
        {
            Category = profile.Category, Grade = profile.Grade, Health = value.Combatant.Health, MaximumHealth = profile.MaxHealth,
            ExperienceLevel = level, AtSea = state.Cells[value.Cell].Terrain.Sea, HasCommander = value.CommanderId != null,
            Transport = value.Transport, UnderConstruction = value.Combatant.ConstructionTurns > 0, PriorityCommander = rules.PriorityBehaviors.Contains(value.Behavior),
            Traits = profile.Traits.Concat(rules.ProfileTraits.TryGetValue(profile.Id, out var traits) ? traits : Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static AiSiteFacts Site(WorldState state, int index, WorldAiRules rules)
    {
        var cache = PlanningScope.Queries(state)?.Ai(rules).Sites;
        if (cache != null && cache.TryGetValue(index, out var found)) return found;
        var site = state.Cells[index];
        var facts = new AiSiteFacts(site.BuildingKind, site.BuildingLevel, site.InstallationKind,
            rules.SitePriorities.TryGetValue(index, out int priority) ? priority : site.Objective ? 1 : 0,
            site.Owner != null && state.Factions.Single(f => f.Id == site.Owner).Human);
        cache?.Add(index, facts);
        return facts;
    }

    private static long ThreatStrength(WorldState state, UnitState unit, WorldAiRules rules)
    {
        var map = new HexMap(state.Width, state.Height);
        var queries = PlanningScope.Queries(state);
        var factions = queries?.Factions ?? state.Factions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var faction = factions[unit.Owner];
        long threat = 0;
        var nearby = queries != null && (long)rules.Strategy.ThreatRadius * rules.Strategy.ThreatRadius < state.Units.Count
            ? map.Within(unit.Cell, rules.Strategy.ThreatRadius).SelectMany(cell => queries.Occupants.GetValueOrDefault(cell) ?? [])
            : state.Units;
        foreach (var enemy in nearby)
        {
            var owner = factions[enemy.Owner];
            if (!WorldDiplomacy.Hostile(state, faction.Id, owner.Id, rules.NeutralCamps) || owner.Defeated) continue;
            int distance = map.Distance(unit.Cell, enemy.Cell);
            if (distance > 0 && distance <= rules.Strategy.ThreatRadius)
                threat = checked(threat + AiTactics.Strength(Facts(state, enemy, rules), rules.Tactics) / distance);
        }
        return threat;
    }

    public static IReadOnlyList<AiCandidate<AiStrikeCommand>> StrikeCandidates(WorldState state, string unitId, WorldAiRules rules)
    {
        var active = WorldActions.Ready(state, state.Revision);
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != active.Id) throw new RuleViolation("ai.unit", "Unknown active faction unit");
        var map = new HexMap(state.Width, state.Height);
        var factions = PlanningScope.Queries(state)?.Factions ?? state.Factions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var result = new List<AiCandidate<AiStrikeCommand>>();
        var attacker = Facts(state, unit, rules);
        var targets = PlanningScope.Queries(state)?.Nearby(map, unit.Cell, unit.Combatant.Profile.MaximumRange, true) ?? state.Units;
        foreach (var target in targets)
        {
            var owner = factions[target.Owner];
            if (!WorldDiplomacy.Hostile(state, active.Id, owner.Id, rules.NeutralCamps)) continue;
            if (AttackLegality.Check(true, true, unit.Combatant.ConstructionTurns, unit.Acted, unit.Morale.Effective,
                map.Distance(unit.Cell, target.Cell), unit.Combatant.Profile.MinimumRange, unit.Combatant.Profile.MaximumRange) != AttackBlock.None) continue;
            long score = AiTactics.AttackScore(attacker, Facts(state, target, rules), Site(state, unit.Cell, rules), Site(state, target.Cell, rules), false, rules.Tactics);
            result.Add(new(new(state.Revision, unit.Id, target.Id), AiTactics.StrikeScore(score, attacker, rules.Tactics)));
        }
        return result;
    }

    public static WorldChange ExecuteStrike(WorldState state, AiStrikeCommand command, WorldBattleRules battleRules) =>
        WorldBattle.Attack(state, command.ExpectedRevision, command.UnitId, command.TargetId, battleRules);

    public static IReadOnlyList<AiCandidate<AiMoveCommand>> TacticalMoveCandidates(WorldState state, string unitId, WorldAiRules rules)
    {
        using var planning = PlanningScope.Begin(rules);
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id) throw new RuleViolation("ai.unit", "Unknown active faction unit");
        if (WorldDiplomacy.Dormant(state, faction.Id, rules.NeutralCamps)) return Array.Empty<AiCandidate<AiMoveCommand>>();
        if (unit.RemainingMovement < 1 || unit.Morale.Effective == -3) return Array.Empty<AiCandidate<AiMoveCommand>>();
        var originalFacts = Facts(state, unit, rules);
        long sourceValue = AiTactics.Location(Site(state, unit.Cell, rules), originalFacts, rules.Tactics);
        AiCandidate<AiMoveCommand>? move = null, combo = null;
        var range = WorldActions.MovementRange(state, unit, rules.Movement);
        var occupied = PlanningScope.Queries(state)?.Occupants;
        foreach (int cellId in range.Keys.OrderBy(i => i))
        {
            if (cellId == unit.Cell || (occupied != null ? occupied.ContainsKey(cellId) : state.Units.Any(u => u.Cell == cellId))) continue;
            var site = state.Cells[cellId];
            var owner = site.Owner == null ? null : state.Factions.Single(f => f.Id == site.Owner);
            var relation = owner?.Id == faction.Id ? AiTerritoryRelation.Owned : owner != null && WorldDiplomacy.Allied(state, owner.Id, faction.Id) ? AiTerritoryRelation.Allied
                : owner == null || !WorldDiplomacy.Hostile(state, faction.Id, owner.Id, rules.NeutralCamps) ? AiTerritoryRelation.NonAllied : AiTerritoryRelation.Hostile;
            var facts = new AiMoveFacts(sourceValue, AiTactics.Location(Site(state, cellId, rules), null, rules.Tactics), relation,
                site.Environment, site.BuildingKind == null ? 0 : site.BuildingLevel, unit.Combatant.Health < unit.Combatant.Profile.MaxHealth, owner?.Human == true);
            var projected = WorldActions.ProjectMove(state, unit.Id, cellId, rules.Movement, range).State;
            long score = AiMovement.MoveScore(facts, rules.MoveScoring);
            if (score > 0 && (move == null || score > move.Score)) move = new(new(state.Revision, unit.Id, cellId), score);
            if (projected.Phase == TurnPhase.Finished || projected.PendingDialogue != null) continue;
            foreach (var target in StrikeCandidates(projected, unit.Id, rules))
            {
                var targetUnit = projected.Units.Single(u => u.Id == target.Action.TargetId);
                var scoreState = rules.UseOriginForMoveAttackScore ? state : projected;
                var scoreUnit = scoreState.Units.Single(u => u.Id == unit.Id);
                long attackScore = AiTactics.AttackScore(Facts(scoreState, scoreUnit, rules), Facts(projected, targetUnit, rules),
                    Site(scoreState, scoreUnit.Cell, rules), Site(projected, targetUnit.Cell, rules), false, rules.Tactics);
                long combined = AiMovement.MoveAttackScore(facts, attackScore, unit.Combatant.Health, unit.Combatant.Profile.MaxHealth, rules.MoveScoring);
                if (combined > 0 && (combo == null || combined > combo.Score)) combo = new(new(state.Revision, unit.Id, cellId, targetUnit.Id), combined);
            }
        }
        var candidates = new List<AiCandidate<AiMoveCommand>>();
        if (move != null) candidates.Add(move);
        if (combo != null) candidates.Add(combo);
        return candidates;
    }

    public static WorldChange ExecuteMove(WorldState state, AiMoveCommand command, WorldAiRules rules, WorldBattleRules battleRules)
    {
        rules.Validate();
        var moved = WorldActions.Move(state, command.ExpectedRevision, command.UnitId, command.Destination, rules.Movement);
        if (command.TargetId == null || moved.State.Phase == TurnPhase.Finished || moved.State.PendingDialogue != null) return moved;
        var attacked = WorldBattle.Attack(moved.State, moved.State.Revision, command.UnitId, command.TargetId, battleRules);
        var next = attacked.State with { Revision = checked(state.Revision + 1) };
        next.Validate();
        return new(next, moved.Notices.Concat(attacked.Notices).ToArray()) { Movements = moved.Movements };
    }
}
