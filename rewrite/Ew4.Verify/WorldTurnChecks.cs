using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class WorldTurnChecks
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
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + " != " + error.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        var rules = new WorldTurnRules
        {
            Buildings = [new("city", 1, new(10, 5, 5), Healing: 5), new("city", 2, new(20, 10, 10), Healing: 10, ClaimsNeighbors: true),
                new("farmland", 1, new(0, 0, 0)), new("farmland", 2, new(0, 0, 0))],
            VeterancyHealing = new Dictionary<string, int> { ["0"] = 3 }, NeutralCamps = ["4"]
        };
        var fixture = WorldChecks.Example();
        var player = fixture.Units.Single(u => u.Owner == "winner") with { Cell = 8, RemainingMovement = 6 };
        var enemy = fixture.Units.Single(u => u.Owner == "loser") with { Cell = 0 };
        var recruitment = new RecruitmentOffer { Id = "infantry", Cost = new(10, 5, 0), Template = player.Combatant,
            PerTurnLimit = 1, Support = new() { FoodConsumption = 7 } };
        var initial = fixture with
        {
            ActiveFaction = "loser", Units = [enemy, player],
            Factions = fixture.Factions.Select(f => f with { Resources = new(100, 100, 100),
                Recruitment = new Dictionary<string, RecruitmentGrant> { ["infantry"] = new() } }).ToArray(),
            Cells = fixture.Cells.Select((c, i) => c with { BuildingKind = i is 0 or 7 ? "city" : null, BuildingLevel = i is 0 or 7 ? 1 : 0 }).ToArray()
        };
        byte[] initialBytes = WorldSave.Encode(initial);
        var first = WorldTurns.EndTurn(initial, 0, rules).State;
        Check(first.Round == 1 && first.ActiveFaction == "winner" && first.Phase == TurnPhase.Active, "first-round faction advance");
        Check(first.Factions.Single(f => f.Id == "winner").Resources == new Resources(100, 100, 100), "first round skips income and consumption");
        Check(first.Units.Single(u => u.Id == player.Id).RemainingMovement == 6 && !first.Units.Single(u => u.Id == player.Id).Acted, "incoming faction units refresh");
        Check(initialBytes.SequenceEqual(WorldSave.Encode(initial)), "turn command leaves prior state untouched");
        var recruited = WorldActions.Recruit(first, first.Revision, 7, "new", recruitment).State;
        Check(recruited.Units.Single(u => u.Id == "new").Acted && recruited.Factions.Single(f => f.Id == "winner").Resources.Money == 90, "recruit before turn boundary");
        Reject(() => WorldActions.Recruit(recruited, recruited.Revision, 6, "another", recruitment), "recruit.site");
        var second = WorldTurns.EndTurn(recruited, recruited.Revision, rules).State;
        Check(second.Round == 2 && second.ActiveFaction == "loser", "round wraps at roster boundary");
        Check(second.Factions.Single(f => f.Id == "loser").Resources == new Resources(110, 105, 105), "round-two income applied once");
        var third = WorldTurns.EndTurn(second, second.Revision, rules).State;
        var restored = WorldSave.Decode(WorldSave.Encode(third), "fixture.1");
        var veteran = restored.Units.Single(u => u.Id == "new");
        Check(restored.Factions.Single(f => f.Id == "winner").Resources == new Resources(100, 100, 98), "newly recruited unit food paid on next owner turn");
        Check(!veteran.Acted && veteran.RemainingMovement == 6 && restored.Factions.Single(f => f.Id == "winner").TurnUses.Count == 0, "recruitment counters reset at next owner turn");
        var moved = WorldActions.Move(restored, restored.Revision, "new", 6).State;
        Check(moved.Units.Single(u => u.Id == "new").Cell == 6 && !moved.Units.Single(u => u.Id == "new").Acted, "recruit-end-refresh-save-move workflow");

        foreach (bool human in new[] { false, true })
        foreach (var sample in new[] { (100L, 0, 0), (99L, 1, 0), (75L, 1, 0),
            (74L, 2, -1), (50L, 2, -1), (49L, 3, -2), (25L, 3, -2), (24L, 4, -3), (0L, 4, -3) })
        {
            var supplyRules = rules with { Buildings = [new("city", 1, new(0, 0, 0))] };
            var supply = initial with
            {
                Round = 2,
                Factions = initial.Factions.Select(f => f.Id == player.Owner
                    ? f with { Human = human, Resources = new(100, 100, sample.Item1), Shortage = 0 } : f).ToArray(),
                Units = [enemy, player with { Morale = new(), Combatant = player.Combatant with { Morale = 0 },
                    Support = new() { FoodConsumption = 100 } }]
            };
            var hungry = WorldTurns.EndTurn(supply, supply.Revision, supplyRules).State;
            var hungryFaction = hungry.Factions.Single(f => f.Id == player.Owner);
            var hungryUnit = hungry.Units.Single(u => u.Id == player.Id);
            Check(hungryFaction.Shortage == sample.Item2 && hungryFaction.Resources.Food == 0,
                "actual turn entry calculates all shortage boundaries for human and AI factions");
            Check(hungryUnit.Morale.Effective == sample.Item3 && hungryUnit.Combatant.Morale == sample.Item3,
                "shortage reaches both unit and combat morale in the same incoming turn");
            var savedHungry = WorldSave.Decode(WorldSave.Encode(hungry), hungry.ContentRevision);
            Check(savedHungry.Factions.Single(f => f.Id == player.Owner).Shortage == sample.Item2
                && savedHungry.Units.Single(u => u.Id == player.Id).Morale.Effective == sample.Item3,
                "shortage and effective morale survive save/load");
            var outgoing = WorldTurns.EndTurn(savedHungry, savedHungry.Revision, supplyRules).State;
            outgoing = outgoing with { Factions = outgoing.Factions.Select(f => f.Id == player.Owner
                ? f with { Resources = new(100, 100, 100) } : f).ToArray() };
            var recovered = WorldTurns.EndTurn(outgoing, outgoing.Revision, supplyRules).State;
            Check(recovered.Factions.Single(f => f.Id == player.Owner).Shortage == 0
                && recovered.Units.Single(u => u.Id == player.Id).Morale.Effective == 0,
                "adequate food clears shortage and restores morale next owner turn");
        }
        foreach (var protectedUnit in new[] {
            player with { Support = new() { FoodConsumption = 100, Logistics = true } },
            player with { Support = new() { FoodConsumption = 100 }, Combatant = player.Combatant with { Skills = [Ew4Combat.Skill(22)] } },
            player with { Support = new() { FoodConsumption = 100 }, Combatant = player.Combatant with { Skills = [Ew4Combat.Skill(3)] } } })
        {
            var supply = initial with { Round = 2, Units = [enemy, protectedUnit],
                Factions = initial.Factions.Select(f => f with { Resources = new(100, 100, 0) }).ToArray() };
            var protectedState = WorldTurns.EndTurn(supply, supply.Revision,
                rules with { Buildings = [new("city", 1, new(0, 0, 0))] }).State;
            Check(protectedState.Units.Single(u => u.Id == player.Id).Morale.Effective == 0,
                "actual turn respects logistics and leadership shortage exemptions");
            Check(protectedState.Factions.Single(f => f.Id == player.Owner).Shortage
                == (protectedUnit.Combatant.Has(Ew4Combat.Skill(3)) ? 4 : 0),
                "logistics removes upkeep whereas leadership only protects morale");
        }

        var scripted = first with { Events = [new() { Id = "round.two.a", DialogueId = "opening", Rules = [new() { Round = 2, Trigger = TriggerKind.Round }] },
            new() { Id = "round.two.b", DialogueId = "orders", Rules = [new() { Round = 2, Trigger = TriggerKind.Round,
                Effect = EventEffectKind.SetMorale, Value = 1, FactionId = "loser" }] }] };
        byte[] scriptedBytes = WorldSave.Encode(scripted);
        var paused = WorldTurns.EndTurn(scripted, scripted.Revision, rules).State;
        Check(paused.Round == 2 && paused.Phase == TurnPhase.RoundEvents && paused.PendingDialogue == "opening", "pause before incoming economy");
        Check(paused.Factions.Single(f => f.Id == "loser").Resources == new Resources(100, 100, 100), "pending dialogue cannot advance economy");
        Check(paused.Units.Single(u => u.Id == enemy.Id).Acted, "pending dialogue cannot refresh incoming units");
        Reject(() => WorldActions.Move(paused, paused.Revision, enemy.Id, 1), "action.phase");
        Reject(() => WorldTurns.EndTurn(paused, paused.Revision, rules), "turn.phase");
        Reject(() => WorldTurns.Resume(paused, paused.Revision, rules), "action.dialogue_pending");
        Reject(() => WorldTurns.Resume(paused, paused.Revision, rules, "incorrect"), "event.dialogue_mismatch");
        var middle = WorldTurns.Resume(WorldSave.Decode(WorldSave.Encode(paused), "fixture.1"), paused.Revision, rules, "opening").State;
        Check(middle.Phase == TurnPhase.RoundEvents && middle.PendingDialogue == "orders" && middle.TriggeredEvents.Count == 2, "resume remaining ordered events");
        var completed = WorldTurns.Resume(WorldSave.Decode(WorldSave.Encode(middle), "fixture.1"), middle.Revision, rules, "orders").State;
        Check(completed.Phase == TurnPhase.Active && completed.PendingDialogue == null && completed.Factions.Single(f => f.Id == "loser").Resources == new Resources(110, 105, 105), "resume starts faction economy exactly once");
        Check(completed.Units.Single(u => u.Id == enemy.Id).Morale == new MoraleState(1, 2, 1), "round event runs before unit morale-duration refresh");
        Check(completed.Revision == scripted.Revision + 3, "one revision per committed command including dialogue resumes");
        Check(scriptedBytes.SequenceEqual(WorldSave.Encode(scripted)), "resumable turn detaches source");
        var directA = WorldTurns.Resume(WorldTurns.Resume(paused, paused.Revision, rules, "opening").State, middle.Revision, rules, "orders").State;
        Check(WorldSave.Encode(directA).SequenceEqual(WorldSave.Encode(completed)), "save at each dialogue resumes deterministically");
        Reject(() => WorldTurns.Resume(completed, completed.Revision, rules), "turn.phase");
        Reject(() => WorldTurns.EndTurn(completed, completed.Revision - 1, rules), "action.stale");

        var resourceFailure = first with { Factions = first.Factions.Select(f => f.Id == "loser" ? f with
            { Resources = new(long.MaxValue, 0, 0) } : f).ToArray() };
        byte[] beforeFailure = WorldSave.Encode(resourceFailure);
        try { WorldTurns.EndTurn(resourceFailure, resourceFailure.Revision, rules); throw new InvalidOperationException("Expected overflow"); }
        catch (OverflowException) { Check(beforeFailure.SequenceEqual(WorldSave.Encode(resourceFailure)), "failed economy leaves entire turn and RNG untouched"); }
        var badBuilding = initial with { Cells = initial.Cells.Select((c, i) => i == 0 ? c with { BuildingLevel = 99 } : c).ToArray() };
        Reject(() => WorldTurns.EndTurn(badBuilding, 0, rules), "turn.building_definition");

        var defeated = new FactionState { Id = "skip", Camp = "c", Defeated = true };
        var skipping = initial with { Factions = [initial.Factions[0], defeated, initial.Factions[1]] };
        Check(WorldTurns.EndTurn(skipping, 0, rules).State.ActiveFaction == "winner", "skip defeated faction in dynamic roster");
        var timed = WorldTurns.EndTurn(first, first.Revision, rules with { PlayerCamp = "b", Victory = new(RoundLimit: 1) }).State;
        Check(timed.Phase == TurnPhase.Finished && timed.Outcome == BattleOutcome.Defeat, "configured round limit ends battle before income");
        Reject(() => WorldTurns.EndTurn(timed, timed.Revision, rules), "turn.finished");
        Reject(() => WorldActions.Move(timed, timed.Revision, player.Id, 6), "action.phase");
        var noEnemies = first with { Factions = first.Factions.Select(f => f.Id == "loser" ? f with { Defeated = true } : f).ToArray(), Units = [first.Units.Single(u => u.Id == player.Id)] };
        Check(WorldTurns.EndTurn(noEnemies, noEnemies.Revision, rules with { PlayerCamp = "b" }).State.Outcome == BattleOutcome.Victory, "no surviving hostile faction means victory");

        var away = initial with { Units = [enemy, player with { Cell = 4, Combatant = player.Combatant with { Health = 50 } }] };
        var awayFirst = WorldTurns.EndTurn(away, 0, rules).State;
        Check(awayFirst.Units.Single(u => u.Id == player.Id).Combatant.Health == 50, "foreign territory owner cannot heal or refresh another faction unit");
        var awaySecond = WorldTurns.EndTurn(awayFirst, awayFirst.Revision, rules).State;
        Check(awaySecond.Units.Single(u => u.Id == player.Id).Combatant.Health == 53, "unit on allied or foreign land ends with its own faction");
        var stacked = first with { Limits = new() { UnitsPerCell = 3 }, Units = [first.Units[0], first.Units[1], first.Units[1] with { Id = "stack" }] };
        Check(WorldTurns.EndTurn(stacked, stacked.Revision, rules).State.Units.Count(u => u.Acted && u.Owner == "winner") == 2, "configured stack occupants all end once");

        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/turn_settlement_vectors.json")));
        var map = new HexMap(3, 3);
        foreach (var vector in native.RootElement.GetProperty("adjacency").EnumerateArray())
        {
            var facts = vector.GetProperty("neighbors").EnumerateArray().ToArray();
            var cells = Enumerable.Range(0, 9).Select(_ => new CellState()).ToArray();
            var center = enemy with { Cell = 4, Owner = "camp." + vector.GetProperty("camp").GetInt32() };
            var units = new List<UnitState> { center };
            for (int direction = 0; direction < 6; direction++)
            {
                var fact = facts[direction];
                int cell = map.Neighbor(4, direction);
                bool present = fact.GetProperty("present").GetBoolean();
                string? owner = fact.GetProperty("owner").ValueKind == JsonValueKind.Null ? null : "camp." + fact.GetProperty("owner").GetInt32();
                cells[cell] = new() { Owner = present ? owner : null, Terrain = new() { Active = present && fact.GetProperty("active").GetBoolean() } };
                if (present && fact.GetProperty("unit").GetBoolean()) units.Add(enemy with { Id = "neighbor." + direction, Cell = cell, Owner = owner!,
                    CommanderId = fact.GetProperty("commander").GetBoolean() ? "commander." + direction : null,
                    Support = new() { AdjacentHealing = fact.GetProperty("healing").GetInt32() } });
            }
            var state = initial with { Cells = cells, Units = units, Factions = Enumerable.Range(1, 4).Select(i => new FactionState { Id = "camp." + i, Camp = i.ToString() }).ToArray(), ActiveFaction = center.Owner };
            Check(WorldTurns.LocalPenalty(state, map, 4, center.Owner, ["4"]) == vector.GetProperty("penalty").GetInt32(), "native surround/flanking conditions");
            Check(WorldTurns.AdjacentHealing(state, map, center) == vector.GetProperty("healing").GetInt32(), "native allied adjacent commander healing");
        }
        foreach (var vector in native.RootElement.GetProperty("expansion").EnumerateArray())
        {
            var cells = Enumerable.Range(0, 9).Select(_ => new CellState()).ToArray();
            var occupied = new List<int>();
            var facts = vector.GetProperty("neighbors").EnumerateArray().ToArray();
            for (int direction = 0; direction < 6; direction++)
            {
                var fact = facts[direction];
                bool present = fact.GetProperty("present").GetBoolean();
                int cell = map.Neighbor(4, direction);
                cells[cell] = new() { Objective = fact.GetProperty("objective").GetBoolean(), Terrain = new()
                    { Active = present && fact.GetProperty("active").GetBoolean(), Sea = fact.GetProperty("sea").GetBoolean() },
                    BuildingKind = fact.GetProperty("building").ValueKind == JsonValueKind.Null ? null : fact.GetProperty("building").GetInt32() == 4 ? "farmland" : "city" };
                if (fact.GetProperty("unit").GetBoolean()) occupied.Add(cell);
            }
            int kind = vector.GetProperty("kind").ValueKind == JsonValueKind.Null ? -1 : vector.GetProperty("kind").GetInt32();
            bool expands = kind switch { 0 => vector.GetProperty("level").GetInt32() >= 4,
                1 => vector.GetProperty("level").GetInt32() >= 3, 2 => vector.GetProperty("level").GetInt32() >= 2, _ => false };
            var result = TerritoryClaims.Expand(map, cells, expands ? [4] : [], occupied, "winner");
            Check(Enumerable.Range(0, 6).Where(i => result[map.Neighbor(4, i)].Owner == "winner")
                .SequenceEqual(vector.GetProperty("claimed").EnumerateArray().Select(v => v.GetInt32())), "native city expansion candidates");
        }
        var cityStart = initial with { Cells = initial.Cells.Select((c, i) => i == 7 ? c with { BuildingLevel = 2 } : c).ToArray() };
        Check(WorldTurns.EndTurn(cityStart, 0, rules).State.Cells[4].Owner == "winner", "building expansion integrated before incoming refresh");
        var random = new Random(1927);
        for (int sample = 0; sample < 40; sample++)
        {
            int count = random.Next(4, 40);
            var factions = Enumerable.Range(0, count).Select(i => new FactionState { Id = "faction." + i, Camp = "camp." + i,
                Resources = new(20000, 12000, 500), IncomeBonus = new(11, 7, 3) }).ToArray();
            var state = new WorldState { ScenarioId = "many.factions", ContentRevision = "fixture.1", Width = count, Height = 1,
                ActiveFaction = factions[0].Id, Factions = factions, RandomState = (ulong)sample, AiRandomState = (ulong)sample * 3,
                Cells = factions.Select(f => new CellState { Owner = f.Id }).ToArray(),
                Units = factions.Select((f, i) => player with { Id = "many." + i, Owner = f.Id, Cell = i,
                    Support = new() { FoodConsumption = 5 } }).ToArray() };
            for (int turn = 0; turn < count * 3; turn++)
            {
                var uninterrupted = WorldTurns.EndTurn(state, state.Revision, rules).State;
                var resumed = WorldTurns.EndTurn(WorldSave.Decode(WorldSave.Encode(state), "fixture.1"), state.Revision, rules).State;
                Check(WorldSave.Encode(uninterrupted).SequenceEqual(WorldSave.Encode(resumed)), "dynamic-roster repeated save/load preserves complete turn");
                Check(uninterrupted.ActiveFaction == factions[(turn + 1) % count].Id && uninterrupted.Round == (turn + 1) / count + 1,
                    "dynamic roster has no fixed faction-slot schedule");
                state = resumed;
            }
            Check(state.Factions.All(f => f.Resources.Money > 20000 && f.Resources.Industry > 12000) && state.AiRandomState == (ulong)sample * 3,
                "resources exceed legacy cap and turn engine does not consume AI RNG");
        }
        return checks;
    }
}
