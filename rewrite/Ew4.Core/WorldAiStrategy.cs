using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record AiStrategicBehavior(AiStrategyBehavior Weight, bool AdjacentOnly = false);
public sealed record WorldStrategyRules
{
    public AiStrategyRules Scoring { get; init; } = new();
    public AiStrategyLandingRules Landing { get; init; } = new();
    public int ThreatRadius { get; init; } = 4;
    public long TransportScorePenalty { get; init; } = 20;
    public TransportPurchaseRules? Transport { get; init; }
    public IReadOnlyDictionary<string, AiStrategicBehavior> Behaviors { get; init; } = new Dictionary<string, AiStrategicBehavior>
    {
        ["default"] = new(AiStrategyBehavior.VariedHalf), ["0"] = new(AiStrategyBehavior.VariedHalf),
        ["1"] = new(AiStrategyBehavior.Full), ["2"] = new(AiStrategyBehavior.VariedQuarter, true), ["3"] = new(AiStrategyBehavior.Zero)
    };
    public void Validate()
    {
        Scoring.Validate(); Landing.Validate(); Transport?.Purchase.Validate();
        if (ThreatRadius < 0 || TransportScorePenalty < 0
            || Behaviors.Any(p => string.IsNullOrWhiteSpace(p.Key) || !Enum.IsDefined(typeof(AiStrategyBehavior), p.Value.Weight))
            || Transport != null && Transport.ExcludedCategories.Any(string.IsNullOrWhiteSpace))
            throw new RuleViolation("ai.strategy_configuration", "Invalid world strategic policy");
    }
}
public sealed record AiStrategicCommand(long ExpectedRevision, string UnitId, int Destination, int GoalCell, bool BuyTransport) : WorldAiCommand(ExpectedRevision, UnitId);
public sealed record AiStrategyPlan(long ExpectedRevision, string UnitId, ulong InitialRandomState, ulong FinalRandomState,
    IReadOnlyList<AiCandidate<AiStrategicCommand>> Candidates);

public static partial class WorldAi
{
    public static AiStrategyPlan StrategicCandidates(WorldState state, string unitId, WorldAiRules rules)
    {
        var faction = WorldActions.Ready(state, state.Revision);
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id) throw new RuleViolation("ai.unit", "Unknown active faction unit");
        if (!rules.Strategy.Behaviors.TryGetValue(unit.Behavior, out var behavior))
            throw new RuleViolation("ai.strategy_behavior", "Unmapped strategic behavior");
        var random = new AiRandom(state.AiRandomState);
        var result = new List<AiCandidate<AiStrategicCommand>>();
        if (unit.RemainingMovement < 1 || unit.Combatant.Profile.Movement < 1 || unit.Morale.Effective == -3 || behavior.Weight == AiStrategyBehavior.Zero)
            return new(state.Revision, unitId, state.AiRandomState, random.State, result);
        var map = new HexMap(state.Width, state.Height);
        var factions = state.Factions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var occupants = state.Units.GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.ToArray());
        bool Hostile(string owner) => WorldDiplomacy.Hostile(state, faction.Id, owner, rules.NeutralCamps) && !factions[owner].Defeated;
        var facts = Facts(state, unit, rules);
        long threat = ThreatStrength(state, unit, rules);
        long strength = AiTactics.Strength(facts, rules.Tactics);
        var regularDestinations = new HashSet<(int Goal, int Cell)>();
        bool transportAvailable = rules.Strategy.Transport != null && WorldSupportActions.CanBuyTransport(state, unit.Id, rules.Strategy.Transport);
        foreach (bool purchase in transportAvailable ? new[] { false, true } : new[] { false })
        {
            var mover = purchase ? unit with { Transport = true } : unit;
            var context = WorldActions.MovementContext(state, mover, rules.Movement);
            int[]? components = null;
            var distanceFields = new Dictionary<string, IReadOnlyDictionary<int, long>>(StringComparer.Ordinal);
            var reachable = Movement.Reachable(map, context.Cells, unit.Cell, context.Actor, rules.Movement.Search);
            if (reachable.Count <= 1) continue;
            var strategyUnit = new AiStrategyUnit(unit.Combatant.Health, unit.Combatant.Profile.MaxHealth, unit.RemainingMovement,
                facts.Category == rules.Tactics.NavalCategory, mover.Transport, unit.CommanderId != null, facts.AtSea, strength, threat);
            // Stable identifiers keep independent faction candidates unaffected by collection ordering.
            foreach (var opponent in state.Factions.Where(f => Hostile(f.Id)).OrderBy(f => f.Id, StringComparer.Ordinal))
            {
                var goals = new List<AiStrategyGoalOption>();
                for (int i = 0; i < state.Cells.Count; i++)
                {
                    var cell = state.Cells[i];
                    var stationed = occupants.TryGetValue(i, out var group) ? group : Array.Empty<UnitState>();
                    if (!cell.Terrain.Active || cell.Owner != opponent.Id && !stationed.Any(u => u.Owner == opponent.Id)) continue;
                    if (stationed.Length > 0 && stationed.All(u => !Hostile(u.Owner))) continue;
                    long army = 0;
                    foreach (var soldier in stationed) army = checked(army + AiTactics.Strength(Facts(state, soldier, rules), rules.Tactics));
                    bool navalReachable = Enumerable.Range(0, 6).Select(d => map.Neighbor(i, d)).Any(n => n >= 0 && state.Cells[n].Terrain.Active && state.Cells[n].Terrain.Sea);
                    var goal = new AiStrategyGoal(AiTactics.Location(Site(state, i, rules), stationed.Length == 0 ? null : Facts(state, stationed[0], rules), rules.Tactics),
                        army, stationed.Length > 0, stationed.Any(u => u.Combatant.Profile.Category == rules.Tactics.NavalCategory), cell.Terrain.Sea, navalReachable);
                    if (AiStrategy.Eligible(strategyUnit, goal, rules.Strategy.Scoring)) goals.Add(new(i, goal));
                }
                foreach (var goal in goals.OrderByDescending(g => AiStrategy.GoalScore(strategyUnit, g.Facts, rules.Strategy.Scoring)).ThenBy(g => g.Cell))
                {
                    var endpoints = new List<int>();
                    // HexMap.Within preserves ascending cell order, including edge maps.
                    foreach (int i in goal.Facts.HasUnit ? map.Within(goal.Cell, unit.Combatant.Profile.MaximumRange, true) : new[] { goal.Cell })
                    {
                        var cell = state.Cells[i];
                        if (!cell.Terrain.Active || occupants.ContainsKey(i) && i != unit.Cell
                            || strategyUnit.Naval && !cell.Terrain.Sea || !strategyUnit.Naval && cell.Terrain.Sea && !mover.Transport) continue;
                        int d = map.Distance(i, goal.Cell);
                        if (i == goal.Cell && !goal.Facts.HasUnit || goal.Facts.HasUnit && d >= unit.Combatant.Profile.MinimumRange && d <= unit.Combatant.Profile.MaximumRange)
                            endpoints.Add(i);
                    }
                    if (endpoints.Count == 0) continue;
                    components ??= Movement.Components(map, context.Cells, context.Actor, rules.Movement.Search);
                    if (!endpoints.Any(cell => components[cell] == components[unit.Cell])) continue;
                    string fieldKey = string.Join(",", endpoints);
                    if (!distanceFields.TryGetValue(fieldKey, out var distances))
                    {
                        if (distanceFields.Count >= 8) distanceFields.Clear();
                        distanceFields[fieldKey] = distances = Movement.DistancesTo(map, context.Cells, endpoints, context.Actor, rules.Movement.Search);
                    }
                    if (!distances.TryGetValue(unit.Cell, out long sourceDistance)) continue;
                    var landings = new List<AiStrategyDestination>();
                    foreach (int index in reachable.Keys.OrderBy(i => i))
                    {
                        if (index == unit.Cell || !distances.TryGetValue(index, out long distance) || distance >= sourceDistance) continue;
                        var cell = state.Cells[index];
                        bool allied = cell.Owner != null && cell.Owner != faction.Id && WorldDiplomacy.Allied(state, cell.Owner, faction.Id);
                        landings.Add(new(index, distance, map.Distance(unit.Cell, index), occupants.ContainsKey(index), cell.Environment, allied, cell.BuildingKind));
                    }
                    int chosen = AiStrategy.SelectDestination(landings, behavior.AdjacentOnly, rules.Strategy.Landing, random.Next);
                    if (chosen < 0) continue;
                    int destination = landings[chosen].Cell;
                    if (purchase && regularDestinations.Contains((goal.Cell, destination))) continue;
                    long weight = AiStrategy.Weight(strategyUnit, goal.Facts, map.Distance(unit.Cell, goal.Cell), rules.Strategy.Scoring);
                    if (purchase) weight = checked(weight - rules.Strategy.TransportScorePenalty);
                    if (weight <= 0) continue;
                    weight = AiStrategy.ApplyBehavior(weight, behavior.Weight, random.Next(AiStrategy.RandomBound(weight, behavior.Weight)));
                    if (weight > 0)
                    {
                        result.Add(new(new(state.Revision, unitId, destination, goal.Cell, purchase), weight));
                        if (!purchase) regularDestinations.Add((goal.Cell, destination));
                        break;
                    }
                }
            }
        }
        return new(state.Revision, unitId, state.AiRandomState, random.State, result.ToArray());
    }

    public static WorldChange ExecuteStrategic(WorldState state, AiStrategyPlan plan, int selected, WorldAiRules rules)
    {
        WorldActions.Ready(state, plan.ExpectedRevision);
        rules.Validate();
        if (state.AiRandomState != plan.InitialRandomState) throw new RuleViolation("ai.strategy_stale_random", "AI random state changed after planning");
        if (selected < 0 || selected >= plan.Candidates.Count) throw new RuleViolation("ai.strategy_selection", "No such strategic candidate");
        var command = plan.Candidates[selected].Action;
        if (command.ExpectedRevision != plan.ExpectedRevision || command.UnitId != plan.UnitId)
            throw new RuleViolation("ai.strategy_selection", "Strategic command does not match its plan");
        var current = state;
        var notices = new List<WorldNotice>();
        if (command.BuyTransport)
        {
            if (rules.Strategy.Transport == null) throw new RuleViolation("ai.strategy_transport", "Transport policy is missing");
            var bought = WorldSupportActions.BuyTransport(current, current.Revision, command.UnitId, rules.Strategy.Transport);
            current = bought.State;
            notices.AddRange(bought.Notices);
        }
        var moved = WorldActions.Move(current, current.Revision, command.UnitId, command.Destination, rules.Movement);
        notices.AddRange(moved.Notices);
        notices.Add(new("ai.strategy_goal", command.UnitId, command.GoalCell));
        var next = moved.State with { Revision = checked(state.Revision + 1), AiRandomState = plan.FinalRandomState };
        next.Validate();
        return new(next, notices) { Movements = moved.Movements };
    }
}
