using System.Text;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class SurrenderProbabilityChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Surrender probability: " + label); }
        void Reject(Action action)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == "diplomacy.pressure_rules", e.Code); return; }
            throw new InvalidOperationException("Expected invalid probability weights");
        }
        var basis = WorldChecks.Example();
        var world = basis with {
            Cells = basis.Cells.Select((c, i) => c with { BuildingKind = i < 4 ? "city" : null, BuildingLevel = i == 0 ? 2 : i < 4 ? 1 : 0,
                Owner = i switch { 1 => "winner", 2 => "ally1", _ => c.Owner } }).ToArray(),
            Units = basis.Units.Append(basis.Units[1] with { Id = "extra", Cell = 7 }).ToArray(),
            Factions = basis.Factions.Concat(Enumerable.Range(1, 5).Select(i => new FactionState { Id = "ally" + i, Name = "ally" + i, Camp = "b" })).ToArray(),
            Commanders = [new() { Id = "leader", Owner = "loser", Withdrawn = true }],
            DeadScenarioCommanders = ["leader"],
            Demands = new() { Ready = true, Seed = 43, Leaders = new Dictionary<string, string> { ["loser"] = "leader" },
                InitialCommanders = new Dictionary<string, string[]> { ["loser"] = ["leader"], ["winner"] = [] },
                Buildings = [new(0, "loser"), new(1, "loser"), new(2, "loser"), new(3, "loser"), new(0, "winner"), new(1, "winner")] }
        };
        world.Validate();
        var q = DemandDiplomacy.Quote(world, "winner", "loser");
        Check(q.Initial == 4 && q.Remaining == 2 && q.Occupied == 1 && q.SharedCores == 2, "core ownership and overlapping claims retained");
        Check(q.Breakdown!.LossPercent == 37.5m && q.Breakdown.OccupationPercent == 6.25m && q.Breakdown.CorePercent == 12.5m, "core weights contribute independently");
        Check(q.Breakdown.CommanderPercent == 25 && q.Breakdown.LegionPercent == 25 && q.Breakdown.SuperiorityPercent == 15, "reference discounts become probability bonuses");
        Check(q.Breakdown.BuildingLevels == 3 && q.Breakdown.BuildingPercent == -6 && q.Chance == 95, "current target facilities resist surrender and final chance caps");
        var old = world with { Demands = world.Demands! with { Rules = new() { Pressure = null } } };
        Check(DemandDiplomacy.Quote(old, "winner", "loser").Chance == 56, "explicit null retains old core-only formula");
        WorldState With(SurrenderPressureRules rules) => world with { Demands = world.Demands! with { Rules = new() { Pressure = rules } } };
        var zero = new SurrenderPressureRules { CommanderBonus = 0, LegionAdvantageBonus = 0, LegionAdvantageCap = 0,
            BuildingLevelPenalty = 0, BuildingPenaltyCap = 0, SuperiorityBonus = 0 };
        Check(DemandDiplomacy.Quote(With(zero), "winner", "loser").Chance == 56, "zero coefficients restore original price-independent probability");
        Check(DemandDiplomacy.Quote(With(zero with { CommanderBonus = 25 }), "winner", "loser").Chance == 81, "all initial commanders dead adds 25 points");
        Check(DemandDiplomacy.Quote(With(zero with { LegionAdvantageBonus = 5, LegionAdvantageCap = 25 }), "winner", "loser").Chance == 81, "camp advantage adds capped 25 points");
        Check(DemandDiplomacy.Quote(With(zero with { SuperiorityBonus = 15 }), "winner", "loser").Chance == 71, "live unit advantage adds 15 points");
        Check(DemandDiplomacy.Quote(With(zero with { BuildingLevelPenalty = 2, BuildingPenaltyCap = 50 }), "winner", "loser").Chance == 50, "facilities subtract 6 points before final rounding");
        var noLeader = world with { Demands = world.Demands! with { InitialCommanders = new Dictionary<string, string[]> { ["loser"] = [] } } };
        Check(DemandDiplomacy.Quote(noLeader, "winner", "loser").Breakdown!.CommanderPercent == 0, "no initial commander gives no death bonus");
        var living = world with { Units = world.Units.Select(u => u.Owner == "loser" ? u with { CommanderId = "leader" } : u).ToArray(),
            Commanders = world.Commanders.Select(c => c with { Withdrawn = false }).ToArray(), DeadScenarioCommanders = [] };
        Check(DemandDiplomacy.Quote(living, "winner", "loser").Breakdown!.CommanderPercent == 0, "no initial deaths gives no bonus");
        var opening = living with { Demands = new(), Commanders = [new() { Id = "a", Owner = "loser", Rank = new(5, 0) },
                new() { Id = "b", Owner = "loser" }, new() { Id = "reserve", Owner = "loser" }],
            Units = [basis.Units[0] with { CommanderId = "a" }, basis.Units[0] with { Id = "second", Cell = 3, CommanderId = "b" }, basis.Units[1]] };
        opening = DemandDiplomacy.Initialize(opening); opening.Validate();
        Check(opening.Demands!.InitialCommanders!["loser"].SequenceEqual(new[] { "a", "b" }), "freeze deployed opening commanders and exclude reserves");
        Check(opening.Demands.InitialCommanders["winner"].Length == 0, "record zero-commanders explicitly");
        var oneDead = opening with { Units = opening.Units.Where(u => u.CommanderId != "a").ToArray(), DeadScenarioCommanders = ["a"],
            Commanders = opening.Commanders.Select(c => c.Id == "a" ? c with { Withdrawn = true } : c).ToArray() };
        var half = DemandDiplomacy.Quote(oneDead, "winner", "loser").Breakdown!;
        Check(half.CommanderPercent == 12.5m && half.InitialCommanders == 2 && half.DeadCommanders == 1, "one of two initial deaths contributes exactly 12.5 points regardless of rank");
        var reserveDead = oneDead with { DeadScenarioCommanders = ["a", "reserve"],
            Commanders = oneDead.Commanders.Select(c => c.Id == "reserve" ? c with { Withdrawn = true } : c).ToArray() };
        Check(DemandDiplomacy.Quote(reserveDead, "winner", "loser").Breakdown!.CommanderPercent == 12.5m, "later reserve death cannot inflate initial losses");
        var detached = oneDead with { Units = oneDead.Units.Select(u => u.CommanderId == "b" ? u with { CommanderId = null } : u).ToArray(),
            Commanders = oneDead.Commanders.Select(c => c.Id == "b" ? c with { Withdrawn = true } : c).ToArray() };
        Check(DemandDiplomacy.Quote(detached, "winner", "loser").Breakdown!.CommanderPercent == 12.5m, "withdrawal or detach without death gives no additional bonus");
        var allDead = detached with { DeadScenarioCommanders = ["a", "b", "reserve"] };
        Check(DemandDiplomacy.Quote(allDead, "winner", "loser").Breakdown!.CommanderPercent == 25, "all opening deaths reach the 25 point cap");
        var copied = opening.Copy(); copied.Demands!.InitialCommanders!["loser"][0] = "reserve";
        Check(opening.Demands.InitialCommanders["loser"][0] == "a", "copy isolates opening roster arrays");
        var halfReloaded = WorldSave.Decode(WorldSave.Encode(oneDead), oneDead.ContentRevision);
        Check(DemandDiplomacy.Quote(halfReloaded, "winner", "loser").Breakdown == half, "initial roster and fractional loss survive save and load");
        var missing = world with { Demands = world.Demands! with { InitialCommanders = null } };
        var missingSaved = WorldSave.Decode(WorldSave.Encode(missing), missing.ContentRevision);
        Check(DemandDiplomacy.Quote(missingSaved, "winner", "loser").Breakdown is { InitialCommanders: null, CommanderPercent: 0 }, "legacy roster missing gives zero without inventing initial generals");
        Check(!Encoding.UTF8.GetString(WorldSave.Encode(missing)).Contains("\"initialCommanders\"", StringComparison.Ordinal), "missing legacy roster retains original payload shape");
        var exact = world with { Commanders = world.Commanders.Concat(new CommanderRuntime[] { new() { Id = "living1", Owner = "loser" }, new() { Id = "living2", Owner = "loser" } }).ToArray(),
            Demands = world.Demands! with { InitialCommanders = new Dictionary<string, string[]> { ["loser"] = ["leader", "living1", "living2"] },
                Rules = new() { LossWeight = 0, OccupationWeight = 0, CoreWeight = 1, Pressure = zero with { CommanderBonus = 25 } } } };
        Check(DemandDiplomacy.Quote(exact, "winner", "loser").Chance == 9, "one-third commander loss combines with half-point core before final rounding");
        var actual = opening with { ActiveFaction = "loser", Factions = opening.Factions.Select(f => f with { Capital = null }).ToArray(),
            Cells = opening.Cells.Select(c => c with { BuildingKind = null, BuildingLevel = 0 }).ToArray(),
            Units = opening.Units.Select(u => u.CommanderId == "a"
                ? u with { Burning = 1, Combatant = u.Combatant with { Health = 1 } } : u).ToArray() };
        var fatal = new HazardRules { Nonlethal = false, Stages = new Dictionary<int, HazardStage> { [1] = new(10, [new(0, 100)]) } };
        var died = WorldTurns.EndTurn(actual, actual.Revision, new() { VeterancyHealing = new Dictionary<string, int> { ["0"] = 0 },
            Settlement = new() { Burning = fatal, Environment = fatal } }).State;
        Check(died.DeadScenarioCommanders.Contains("a") && DemandDiplomacy.Quote(died, "winner", "loser").Breakdown!.CommanderPercent == 12.5m,
            "actual commander death settlement updates opening loss ratio");
        foreach (var roster in new Dictionary<string, string[]>[] {
            new() { ["loser"] = ["a", "a"] }, new() { ["missing-faction"] = [] }, new() { ["loser"] = ["missing-commander"] },
            new() { ["loser"] = ["a"], ["winner"] = ["a"] }, new() { ["loser"] = null! } })
        {
            try { (opening with { Demands = opening.Demands with { InitialCommanders = roster } }).Validate(); }
            catch (RuleViolation e) { Check(e.Code == "diplomacy.state", "reject invalid initial roster"); continue; }
            throw new InvalidOperationException("Expected invalid initial roster");
        }
        var deadCamp = world with { Factions = world.Factions.Select(f => f.Id.StartsWith("ally", StringComparison.Ordinal) ? f with { Defeated = true } : f).ToArray(),
            Cells = world.Cells.Select(c => c.Owner == "ally1" ? c with { Owner = null } : c).ToArray() };
        Check(DemandDiplomacy.Quote(deadCamp, "winner", "loser").Breakdown!.LegionPercent == 0, "defeated factions do not contribute camp advantage");
        var ally = WorldDiplomacy.Set(world, "winner", "loser", "allied");
        Check(DemandDiplomacy.Quote(ally, "winner", "loser").DemandBlocked == "peace", "probability bonuses cannot bypass hostility");
        var equal = world with { Units = world.Units.Where(u => u.Id != "extra").ToArray() };
        Check(DemandDiplomacy.Quote(equal, "winner", "loser").Breakdown!.SuperiorityPercent == 0, "equal unit counts give no superiority bonus");
        var lots = world with { Cells = world.Cells.Select((c, i) => i == 0 ? c with { BuildingLevel = 30 } : c).ToArray() };
        Check(DemandDiplomacy.Quote(lots, "winner", "loser").Breakdown!.BuildingPercent == -50, "building penalty stops at its cap");
        var noCore = world with { Demands = world.Demands! with { Buildings = [] } };
        Check(DemandDiplomacy.Quote(noCore, "winner", "loser") is { Chance: 0, DemandBlocked: "no_buildings" }, "no cores cannot gain probability from military factors");
        var noOccupation = world with { Cells = world.Cells.Select((c, i) => i == 1 ? c with { Owner = "loser" } : c).ToArray() };
        Check(DemandDiplomacy.Quote(noOccupation, "winner", "loser").DemandBlocked == "occupation", "bonuses do not bypass actual core occupation requirement");
        var negative = DemandDiplomacy.SetPair(lots, "winner", "loser", -100, false);
        Check(DemandDiplomacy.Quote(negative, "winner", "loser").Chance == 0, "combined result cannot be negative");
        var shared = DemandDiplomacy.SetHomeland(old, "winner", []);
        Check(DemandDiplomacy.Quote(shared, "winner", "loser").Chance == 44, "core events immediately change probability");
        var saved = WorldSave.Decode(WorldSave.Encode(world), world.ContentRevision);
        Check(DemandDiplomacy.Quote(saved, "winner", "loser") == q, "weights, initial roster, breakdown and chance persist through save and load");
        string oldJson = Encoding.UTF8.GetString(WorldSave.Encode(old));
        Check(!oldJson.Contains("\"pressure\"", StringComparison.Ordinal), "legacy rule payload omits new weights");
        var oldSaved = WorldSave.Decode(Encoding.UTF8.GetBytes(oldJson), old.ContentRevision);
        Check(oldSaved.Demands!.Rules.Pressure == null && DemandDiplomacy.Quote(oldSaved, "winner", "loser").Chance == 56, "missing field does not silently change an existing save's probability");
        Check(WorldSave.Encode(oldSaved).SequenceEqual(WorldSave.Encode(old)), "legacy probability snapshot roundtrips byte for byte");
        var coreOnly = With(zero) with { Demands = world.Demands! with { Rules = new() { Pressure = zero, LossWeight = 0, OccupationWeight = 0, CoreWeight = 1 } } };
        Check(DemandDiplomacy.Quote(coreOnly, "winner", "loser").Chance == 1, "half percentage point rounds away only at final probability");
        foreach (var invalid in new[] { zero with { CommanderBonus = -1 }, zero with { CommanderBonus = 26 }, zero with { LegionAdvantageBonus = 101 }, zero with { LegionAdvantageCap = -1 },
            zero with { BuildingLevelPenalty = -1 }, zero with { BuildingPenaltyCap = 101 }, zero with { SuperiorityBonus = 101 } })
            Reject(() => With(invalid).Validate());
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var config = DiplomacyConfiguration.Read(Read);
        Check(config.Rules.Pressure == new SurrenderPressureRules(), "new content enables the default reference-derived weights");
        var custom = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, rules = new { pressure = new { commanderBonus = 7 } } });
        Check(DiplomacyConfiguration.Read(p => p == DiplomacyConfiguration.Path ? custom : Read(p)).Rules.Pressure is { CommanderBonus: 7, LegionAdvantageBonus: 5 }, "Mod can override probability weights independently");
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var sample = With(zero with { CommanderBonus = 25 }) with { Demands = world.Demands! with { Seed = seed,
                Rules = new() { Pressure = zero with { CommanderBonus = 25 } } } };
            var reloaded = WorldSave.Decode(WorldSave.Encode(sample), sample.ContentRevision) with { RandomState = seed * 111, AiRandomState = seed * 173 };
            Check(DemandDiplomacy.Demand(sample, sample.Revision, "winner", "loser").Notices.Last().Kind
                == DemandDiplomacy.Demand(reloaded, reloaded.Revision, "winner", "loser").Notices.Last().Kind, "updated weighted attempts remain stable across reload " + seed);
        }
        return checks;
    }
}
