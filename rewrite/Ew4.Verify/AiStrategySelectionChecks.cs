using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class AiStrategySelectionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            checks++;
            if (!value) throw new InvalidOperationException(label);
        }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (RuleViolation e) { Check(e.Code == code, code + ": " + e.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        var scoring = new AiStrategyRules();
        var landing = new AiStrategyLandingRules();
        var nativeMap = new HexMap(9, 3);
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/ai_strategy_selection_vectors.json")));
        int negativeWeights = 0, inheritedMarkers = 0;
        foreach (var vector in native.RootElement.EnumerateArray())
        {
            var v = vector.GetProperty("inputs");
            int Int(string key) => v.GetProperty(key).GetInt32();
            bool Flag(string key) => v.GetProperty(key).GetBoolean();
            var sites = v.GetProperty("sites").EnumerateArray().ToArray();
            int SiteInt(int cell, string key) => sites[cell].GetProperty(key).GetInt32();
            bool SiteFlag(int cell, string key) => sites[cell].GetProperty(key).GetBoolean();
            var raw = v.GetProperty("raw_random").EnumerateArray().Select(x => x.GetInt64()).ToArray();
            var bounds = new List<uint>();
            int drawIndex = 0;
            long Draw(long bound)
            {
                bounds.Add(unchecked((uint)bound));
                return bound == 0 ? 0 : raw[drawIndex++] % bound;
            }
            var actor = new AiStrategyUnit(Int("health"), Int("maximum"), Int("movement"), Flag("naval"), Flag("transport"), Flag("commander"),
                SiteFlag(0, "sea"), SiteInt(0, "strength"), Int("pressure"));
            long rolling = (actor.Health > scoring.HealthyHealthThreshold ? scoring.HealthyBaseScore : scoring.BaseScore)
                + (actor.Health == actor.MaximumHealth ? scoring.FullHealthBonus : 0)
                + (actor.Naval || actor.Transport ? scoring.TransportOrNavalBonus : 0) + (actor.Commander ? scoring.CommanderBonus : 0);
            var actual = new List<long[]>();
            var chosenGoals = new List<int>();
            int marker = -1;
            foreach (int country in new[] { 1, 2, 5 })
            {
                var goals = new[] { 12 + country * 2, 13 + country * 2 }.Select(i => new AiStrategyGoalOption(i,
                    new(SiteInt(i, "value"), SiteInt(i, "strength"), SiteFlag(i, "present"), SiteFlag(i, "naval"), SiteFlag(i, "sea"), SiteFlag(i, "sea_approach")))).ToArray();
                int selected = AiStrategy.SelectGoal(actor, goals, scoring);
                if (selected < 0) continue;
                var goal = goals[selected];
                chosenGoals.Add(goal.Cell);
                // Replay the observed accumulator only in the verifier. Modern world scoring starts independently.
                rolling = AiStrategy.WeightScore(checked(rolling + AiStrategy.GoalScore(actor, goal.Facts, scoring)), actor, nativeMap.Distance(0, goal.Cell), scoring);
                var destinations = v.GetProperty("paths").EnumerateArray().Select(x => x.GetInt32()).Select(i => new AiStrategyDestination(i,
                    nativeMap.Distance(i, goal.Cell), nativeMap.Distance(0, i), SiteFlag(i, "present"), SiteInt(i, "environment"),
                    sites[i].GetProperty("relation").GetString() == "allied", SiteInt(i, "building") == 255 ? null : SiteInt(i, "building").ToString())).ToArray();
                int chosen = AiStrategy.SelectDestination(destinations, Int("behavior") == 2, landing, Draw);
                if (chosen < 0) continue;
                int destination = destinations[chosen].Cell;
                int previousMarker = marker;
                bool markedThisTime = false;
                if (!actor.AtSea && !actor.Transport && !actor.Naval)
                {
                    if (Flag("auto_transport")) markedThisTime = SiteFlag(destination, "sea_approach");
                    else if (Flag("affordable"))
                    {
                        long draw = Draw(100);
                        markedThisTime = SiteFlag(destination, "sea_approach") && draw > 50;
                    }
                    if (markedThisTime) marker = destination;
                }
                if (previousMarker >= 0 && !markedThisTime) inheritedMarkers++;
                var behavior = (AiStrategyBehavior)Int("behavior");
                if (rolling >= 0) rolling = AiStrategy.ApplyBehavior(rolling, behavior,
                    behavior is AiStrategyBehavior.Full or AiStrategyBehavior.Zero ? 0 : Draw(AiStrategy.RandomBound(rolling, behavior)));
                else
                {
                    negativeWeights++;
                    if (behavior == AiStrategyBehavior.Zero) rolling = 0;
                    else if (behavior != AiStrategyBehavior.Full)
                    {
                        long floor = rolling / (behavior == AiStrategyBehavior.VariedHalf ? 2 : 4);
                        rolling = floor + Draw(behavior == AiStrategyBehavior.VariedHalf ? floor : rolling - floor);
                    }
                }
                actual.Add([destination, marker, rolling]);
            }
            var expected = vector.GetProperty("expected").EnumerateArray().Select(row => row.EnumerateArray().Select(x => x.GetInt64()).ToArray()).ToArray();
            Check(chosenGoals.SequenceEqual(vector.GetProperty("selected_goals").EnumerateArray().Select(x => x.GetInt32())), "native best goal per country and goal eligibility");
            Check(actual.Count == expected.Length && actual.Zip(expected).All(pair => pair.First.SequenceEqual(pair.Second)), "native multi-country destinations/accumulator/transport markers");
            Check(bounds.SequenceEqual(vector.GetProperty("random_bounds").EnumerateArray().Select(x => x.GetUInt32())), "native allied-port/transport/behavior draw order and bounds");
            Check(drawIndex == bounds.Count(b => b != 0), "native zero-bound helper avoids rand");
        }
        Check(negativeWeights > 0 && inheritedMarkers > 0, "fixtures cover native negative score and stale transport marker defects");

        var fixture = WorldChecks.Example();
        var actorUnit = fixture.Units[1] with { Cell = 0, RemainingMovement = 6, Behavior = "1",
            Combatant = fixture.Units[1].Combatant with { Profile = fixture.Units[1].Combatant.Profile with { Grade = "0" } } };
        var enemyUnit = fixture.Units[0] with { Cell = 8,
            Combatant = fixture.Units[0].Combatant with { Profile = fixture.Units[0].Combatant.Profile with { Grade = "0" } } };
        var world = fixture with { Width = 9, Height = 1, Units = [actorUnit, enemyUnit], Cells = Enumerable.Range(0, 9).Select(i => new CellState
            { Owner = i == 8 ? enemyUnit.Owner : actorUnit.Owner, BuildingKind = i == 8 ? "0" : null, BuildingLevel = i == 8 ? 1 : 0 }).ToArray() };
        var rules = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        byte[] original = WorldSave.Encode(world);
        var plan = WorldAi.StrategicCandidates(world, actorUnit.Id, rules);
        Check(plan.Candidates.Count == 1 && plan.Candidates[0].Action.Destination == 2 && plan.Candidates[0].Action.GoalCell == 8, "strategic planning advances toward distant enemy");
        Check(original.SequenceEqual(WorldSave.Encode(world)), "strategic planning leaves source state and both RNGs unchanged");
        var advanced = WorldAi.ExecuteStrategic(world, plan, 0, rules).State;
        Check(advanced.Units.Single(u => u.Id == actorUnit.Id).Cell == 2 && advanced.Revision == world.Revision + 1
            && advanced.RandomState == world.RandomState && advanced.AiRandomState == plan.FinalRandomState, "strategic move commits one revision and only AI random state");
        Reject(() => WorldAi.ExecuteStrategic(advanced, plan, 0, rules), "action.stale");
        Reject(() => WorldAi.ExecuteStrategic(world with { AiRandomState = world.AiRandomState + 1 }, plan, 0, rules), "ai.strategy_stale_random");
        Reject(() => WorldAi.ExecuteStrategic(world, plan, -1, rules), "ai.strategy_selection");
        var randomWorld = world with { Units = [actorUnit with { Behavior = "default" }, enemyUnit] };
        var randomPlan = WorldAi.StrategicCandidates(randomWorld, actorUnit.Id, rules);
        Check(randomPlan.InitialRandomState != randomPlan.FinalRandomState, "strategic varied behavior samples a private AI RNG");
        var loadedPlan = WorldAi.StrategicCandidates(WorldSave.Decode(WorldSave.Encode(randomWorld), world.ContentRevision), actorUnit.Id, rules);
        Check(loadedPlan.Candidates.SequenceEqual(randomPlan.Candidates) && loadedPlan.FinalRandomState == randomPlan.FinalRandomState, "strategic candidates and next RNG state reproduce after save/load");
        var secondEnemy = world.Factions[0] with { Id = "enemy.z", Camp = "z" };
        var multi = world with { Factions = world.Factions.Append(secondEnemy).ToArray(), Cells = world.Cells.Select((c, i) => i != 6 ? c : c with
            { Owner = secondEnemy.Id, BuildingKind = "1", BuildingLevel = 2 }).ToArray() };
        var multiPlan = WorldAi.StrategicCandidates(multi, actorUnit.Id, rules);
        var reversedPlan = WorldAi.StrategicCandidates(multi with { Factions = multi.Factions.Reverse().ToArray() }, actorUnit.Id, rules);
        Check(multiPlan.Candidates.Count == 2 && multiPlan.Candidates.SequenceEqual(reversedPlan.Candidates), "modern independent goal scoring ignores faction collection order");
        foreach (var candidate in multiPlan.Candidates)
        {
            var alone = multi with { Factions = multi.Factions.Select(f => f.Id == (candidate.Action.GoalCell == 8 ? secondEnemy.Id : enemyUnit.Owner) ? f with { Camp = "b" } : f).ToArray() };
            var independent = WorldAi.StrategicCandidates(alone, actorUnit.Id, rules);
            Check(independent.Candidates.Single(c => c.Action.GoalCell == candidate.Action.GoalCell).Score == candidate.Score, "goal weight does not inherit a preceding faction's score");
        }
        var neutral = world with { Factions = world.Factions.Select(f => f.Id == enemyUnit.Owner ? f with { Camp = "neutral" } : f).ToArray() };
        Check(WorldAi.StrategicCandidates(neutral, actorUnit.Id, rules with { NeutralCamps = ["neutral"] }).Candidates.Count == 0, "neutral territory and units are not strategic enemies");
        var allied = world with { Factions = world.Factions.Select(f => f with { Camp = "b" }).ToArray() };
        Check(WorldAi.StrategicCandidates(allied, actorUnit.Id, rules).Candidates.Count == 0, "allied factions excluded as strategic goals");
        var exhausted = world with { Units = [actorUnit with { RemainingMovement = 0 }, enemyUnit] };
        Check(WorldAi.StrategicCandidates(exhausted, actorUnit.Id, rules).Candidates.Count == 0, "exhausted strategic unit has no candidate");
        var stationary = world with { Units = [actorUnit with { Behavior = "3" }, enemyUnit] };
        Check(WorldAi.StrategicCandidates(stationary, actorUnit.Id, rules).Candidates.Count == 0, "hold behavior does not wander toward zero-weight strategic targets");

        var barrier = world with { Width = 7, Height = 3, Units = [actorUnit with { Cell = 7 }, enemyUnit with { Cell = 13 }],
            Cells = Enumerable.Range(0, 21).Select(i => new CellState { Owner = i is 0 or 13 ? enemyUnit.Owner : actorUnit.Owner,
                BuildingKind = i is 0 or 13 ? "0" : null, BuildingLevel = i is 0 or 13 ? 1 : 0, Objective = i == 13,
                Terrain = new() { Sea = i % 7 == 3 } }).ToArray() };
        var fallback = WorldAi.StrategicCandidates(barrier, actorUnit.Id, rules);
        Check(fallback.Candidates.Single().Action.GoalCell == 0 && fallback.Candidates.Single().Action.Destination == 0,
            "unreachable valuable goal falls back to reachable city across no sea");
        var detourActor = actorUnit with { Cell = 5, RemainingMovement = 3, Combatant = actorUnit.Combatant with { Profile = actorUnit.Combatant.Profile with { Movement = 3 } } };
        var detour = world with { Width = 5, Height = 4, Units = [detourActor, enemyUnit with { Cell = 9 }],
            Cells = Enumerable.Range(0, 20).Select(i => new CellState { Owner = i == 9 ? enemyUnit.Owner : actorUnit.Owner, BuildingKind = i == 9 ? "0" : null,
                BuildingLevel = i == 9 ? 1 : 0, Terrain = new() { Active = i >= 5 && i is not (6 or 7 or 8 or 11) } }).ToArray() };
        var detourPlan = WorldAi.StrategicCandidates(detour, actorUnit.Id, rules);
        Check(detourPlan.Candidates.Single().Action.Destination == 10 && new HexMap(5, 4).Distance(10, 9) > new HexMap(5, 4).Distance(5, 9),
            "route potential permits necessary first step away from goal around obstacle");

        var transport = new TransportPurchaseRules { Purchase = new() { Cost = new(25, 9, 0) } };
        var seaWorld = world with { Width = 5, Height = 1, Units = [actorUnit, enemyUnit with { Cell = 4 }],
            Factions = world.Factions.Select(f => f.Id == actorUnit.Owner ? f with { Resources = new(100, 100, 100) } : f).ToArray(),
            Cells = Enumerable.Range(0, 5).Select(i => new CellState { Owner = i == 4 ? enemyUnit.Owner : actorUnit.Owner, BuildingKind = i == 4 ? "0" : null,
                BuildingLevel = i == 4 ? 1 : 0, Terrain = new() { Sea = i is > 0 and < 4 } }).ToArray() };
        var seaRules = rules with { Strategy = rules.Strategy with { Transport = transport } };
        var seaPlan = WorldAi.StrategicCandidates(seaWorld, actorUnit.Id, seaRules);
        Check(WorldAi.StrategicCandidates(seaWorld, actorUnit.Id, rules).Candidates.Count == 0 && seaPlan.Candidates.Single().Action.BuyTransport,
            "transport alternative opens previously unreachable island route");
        var crossedChange = WorldAi.ExecuteStrategic(seaWorld, seaPlan, 0, seaRules);
        var crossed = crossedChange.State;
        var sailor = crossed.Units.Single(u => u.Id == actorUnit.Id);
        Check(sailor.Transport && sailor.Cell == 1 && sailor.Combatant.AtSea && crossed.Revision == seaWorld.Revision + 1, "transport purchase then embarkation is one atomic strategic command");
        Check(crossed.Factions.Single(f => f.Id == actorUnit.Owner).Resources == new Resources(75, 91, 100)
            && crossedChange.Notices.Any(n => n.Kind == "unit.transport_purchased"), "strategic transport pays actual world quote and emits notice");
        var seaStart = crossed with { Width = 9, Height = 1,
            Units = [sailor with { RemainingMovement = 6 }, enemyUnit with { Cell = 8 }],
            Cells = Enumerable.Range(0, 9).Select(i => new CellState { Owner = i == 8 ? enemyUnit.Owner : actorUnit.Owner,
                BuildingKind = i == 8 ? "0" : null, BuildingLevel = i == 8 ? 1 : 0, Terrain = new() { Sea = i < 6 } }).ToArray() };
        var seaStartPlan = WorldAi.StrategicCandidates(seaStart, sailor.Id, seaRules);
        Check(seaStartPlan.Candidates.Single().Action.Destination == 3, "sea transport advances two cells toward an inland strategic goal");
        Check(WorldAi.ExecuteStrategic(seaStart, seaStartPlan, 0, seaRules).State.Units.Single(u => u.Id == sailor.Id).Cell == 3,
            "AI commits the same two-cell limit as player movement");
        byte[] seaBefore = WorldSave.Encode(seaWorld);
        var expensiveRules = seaRules with { Strategy = seaRules.Strategy with { Transport = transport with { Purchase = new() { Cost = new(1000, 9, 0) } } } };
        Reject(() => WorldAi.ExecuteStrategic(seaWorld, seaPlan, 0, expensiveRules), "purchase.resources");
        Check(seaBefore.SequenceEqual(WorldSave.Encode(seaWorld)), "failed strategic purchase changes no resources or RNG");
        var badPlan = seaPlan with { Candidates = [seaPlan.Candidates[0] with { Action = seaPlan.Candidates[0].Action with { Destination = 4 } }] };
        Reject(() => WorldAi.ExecuteStrategic(seaWorld, badPlan, 0, seaRules), "move.destination");
        Check(seaBefore.SequenceEqual(WorldSave.Encode(seaWorld)), "failure after successful private purchase rolls back entire strategic action");
        var poor = seaWorld with { Factions = seaWorld.Factions.Select(f => f with { Resources = new(0, 0, 0) }).ToArray() };
        Check(WorldAi.StrategicCandidates(poor, actorUnit.Id, seaRules).Candidates.Count == 0, "unaffordable transport never enters candidate plan");

        // An unused extension keeps the original unpruned path as an oracle.
        var fallbackBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { ["unused"] = new() { Id = "unused" } });
        for (int sample = 0; sample < 64; sample++)
        {
            bool naval = sample % 2 == 0;
            var ally = world.Factions[1] with { Id = "ally", Human = false };
            var testActor = actorUnit with { Behavior = "default", Transport = !naval,
                Combatant = actorUnit.Combatant with { AtSea = true, Health = 40 + sample % 61,
                    Profile = actorUnit.Combatant.Profile with { Category = naval ? "naval" : "infantry" } } };
            var testWorld = (world with { AiRandomState = (ulong)(sample + 1),
                Factions = world.Factions.Append(ally).ToArray(), Units = [testActor, enemyUnit],
                Cells = world.Cells.Select((c, i) => c with { Terrain = c.Terrain with { Sea = true },
                    Owner = i == 0 ? testActor.Owner : i == 1 ? ally.Id : enemyUnit.Owner,
                    BuildingKind = i == 1 ? "4" : c.BuildingKind,
                    Environment = i == 1 && sample % 4 == 0 ? 1 : 0 }).ToArray() }).Copy();
            var testRules = rules with { Strategy = rules.Strategy with {
                Landing = rules.Strategy.Landing with { AlliedBuildingChance = sample % 3 == 0
                    ? new Dictionary<string, int>() : new Dictionary<string, int> { ["4"] = 50 } },
                Scoring = rules.Strategy.Scoring with { BaseScore = sample % 5 == 0 ? -500 : 10 } } };
            var optimized = WorldAi.StrategicCandidates(testWorld, testActor.Id, testRules);
            var fallbackPlan = WorldAi.StrategicCandidates(testWorld, testActor.Id,
                testRules with { Movement = testRules.Movement with { Extensions = fallbackBus } });
            Check(JsonSerializer.Serialize(optimized) == JsonSerializer.Serialize(fallbackPlan),
                "Strategic pruning changed candidates/order/transport/allied landing RNG: " + sample);
        }

        var random = new Random(1937);
        var map = new HexMap(6, 4);
        for (int sample = 0; sample < 12; sample++)
        {
            var cells = Enumerable.Range(0, map.Count).Select(_ => new MovementCell { Active = random.Next(5) != 0, Sea = random.Next(3) == 0,
                TerrainCost = random.Next(1, 7), RiverEdges = (byte)random.Next(64), BridgeEdges = (byte)random.Next(64), BlockedSeaEdges = (byte)random.Next(64) }).ToArray();
            var mover = new MovementActor { Camp = "test", Category = sample % 3 == 0 ? "naval" : "cavalry", Transport = true, FullBudget = 6, Remaining = 6 };
            var moveRules = new MovementRules();
            var forward = Enumerable.Range(0, map.Count).Where(i => cells[i].Active && (mover.Category != "naval" || cells[i].Sea))
                .ToDictionary(i => i, i => Movement.Reachable(map, cells, i, mover, moveRules));
            var longForward = forward.Keys.ToDictionary(i => i, i => Movement.DistancesFrom(map, cells, i, mover, moveRules));
            foreach (int target in forward.Keys)
            {
                var distances = Movement.DistancesTo(map, cells, [target], mover, moveRules);
                var transportActor = Movement.AtOrigin(mover, new() { Sea = true });
                var transportDistances = Movement.DistancesTo(map, cells, [target], transportActor, moveRules);
                foreach (int source in forward.Keys)
                {
                    var sourceActor = Movement.AtOrigin(mover, cells[source]);
                    var sourceDistances = cells[source].Sea ? transportDistances : distances;
                    bool found = forward[source].TryGetValue(target, out var reached);
                    bool forwardFound = longForward[source].TryGetValue(target, out long forwardCost);
                    bool reverseFound = distances.TryGetValue(source, out long reverseCost);
                    Check(forwardFound == reverseFound && (!forwardFound || forwardCost == reverseCost),
                        "Transposed strategic distances differ on directed river/coast/terrain edges");
                    bool affordable = sourceDistances.TryGetValue(source, out long cost) && cost <= sourceActor.FullBudget;
                    Check(found == affordable && (!found || cost == sourceActor.FullBudget - reached!.Remaining), "reverse directed-edge routing agrees with origin-specific movement costs and turn budget");
                }
            }
        }
        var wide = new HexMap(6005, 1);
        var wideCells = Enumerable.Range(0, wide.Count).Select(_ => new MovementCell()).ToArray();
        var walker = new MovementActor { Camp = "a", FullBudget = 3, Remaining = 3 };
        var wideDistance = Movement.DistancesTo(wide, wideCells, [wide.Count - 1], walker);
        Check(wideDistance[0] == (wide.Count - 1L) * 3 && walker.Remaining == 3, "strategic route beyond original fixed search capacity leaves per-turn budget intact");
        var hugeCosts = Enumerable.Range(0, 3).Select(_ => new MovementCell { TerrainCost = int.MaxValue }).ToArray();
        var heavy = walker with { Category = "cavalry", FullBudget = int.MaxValue, Remaining = int.MaxValue };
        Check(Movement.DistancesTo(new(3, 1), hugeCosts, [2], heavy)[0] == 2L * int.MaxValue, "strategic distance uses 64-bit accumulated costs");
        Reject(() => Movement.DistancesTo(new(3, 1), hugeCosts, [2], walker with { FullBudget = 0, Remaining = 0 }), "movement.state");
        return checks;
    }
}
