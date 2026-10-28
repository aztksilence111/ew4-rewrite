using System.Text;
using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class CommanderDeploymentChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + ": " + error.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/commander_dispatch_vectors.json")));
        var scoring = new AiCommanderRules();
        string[] categories = ["infantry", "cavalry", "artillery", "naval", "fort"];
        foreach (var vector in vectors.RootElement.GetProperty("ai").EnumerateArray())
        {
            var sites = vector.GetProperty("inputs").GetProperty("sites").EnumerateArray().Select((s, i) => new AiCommanderSite(i,
                s.GetProperty("building").GetInt32() != 255, s.GetProperty("record").GetBoolean() && s.GetProperty("available").GetInt32() > 0,
                s.GetProperty("unit").GetBoolean(), s.GetProperty("assigned").GetBoolean(), s.GetProperty("sea").GetBoolean(),
                categories[s.GetProperty("category").GetInt32()], s.GetProperty("health").GetInt32())).ToArray();
            var expected = vector.GetProperty("expected").GetProperty("command");
            Check(AiCommanders.SelectSite(sites, scoring) == (expected.ValueKind == JsonValueKind.Null ? null : expected[1].GetInt32()),
                "native AI commander site thresholds and stable order");
        }
        foreach (var vector in vectors.RootElement.GetProperty("attachment").EnumerateArray())
        {
            var input = vector.GetProperty("inputs");
            int Int(string name) => input.GetProperty(name).GetInt32();
            var expected = vector.GetProperty("expected");
            int oldHealth = Int("old_health"), oldMax = Int("old_maximum"), baseMax = Int("base_health"), newMax = expected.GetProperty("maximum").GetInt32();
            Check(CommanderAttachment.ResizeHealth(oldHealth, oldMax, baseMax, newMax, true) == expected.GetProperty("health").GetInt32(),
                "native attachment double rounding reproduced by explicit compatibility calculation");
            int modern = CommanderAttachment.ResizeHealth(oldHealth, oldMax, baseMax, newMax);
            Check(modern == Math.Max(1, (long)oldHealth * newMax / oldMax), "modern assignment uses one ratio conversion and keeps a living unit alive");
            int remaining = Int("remaining"), budget = Int("base_movement");
            int nativeBudget = budget + (budget > 0 && expected.GetProperty("attached").GetBoolean() ? Int("movement_bonus") : 0);
            Check(CommanderAttachment.RebaseMovement(remaining, Math.Max(budget, remaining), nativeBudget, true) == expected.GetProperty("movement").GetInt32(),
                "native available movement refill reproduced by explicit compatibility option");
        }
        Check(CommanderAttachment.ResizeHealth(1, 1000, 100, 100) == 1, "modern unassignment does not create zero HP units");
        Check(CommanderAttachment.RebaseMovement(2, 8, 10) == 4 && CommanderAttachment.RebaseMovement(0, 8, 10) == 0,
            "assignment preserves spent movement and exhausted action");
        Check(AiCommanders.SelectSite([new(100_000, true, true, true, false, false, "new.category", 800)],
            scoring with { LandMinimumHealth = new Dictionary<string, int> { ["new.category"] = 800 } }) == 100_000,
            "commander AI categories and thresholds are data driven");
        Reject(() => CommanderAttachment.ResizeHealth(0, 0, 1, 1), "commander.health");
        Reject(() => CommanderAttachment.RebaseMovement(2, 1, 3), "commander.movement");

        var fixture = WorldChecks.Example();
        var unit = fixture.Units[1] with { Cell = 0, RemainingMovement = 2, Combatant = fixture.Units[1].Combatant with
            { Health = 65, Profile = fixture.Units[1].Combatant.Profile with { Grade = "0" } }, Support = new() { FoodConsumption = 10, FieldHealing = 2 } };
        var enemy = fixture.Units[0] with { Cell = 5, Combatant = unit.Combatant with { Health = 100 } };
        var world = fixture with { Width = 6, Height = 1, Units = [unit, enemy],
            Cells = Enumerable.Range(0, 6).Select(i => new CellState { Owner = i == 5 ? enemy.Owner : unit.Owner,
                BuildingKind = i == 0 ? "city" : null, BuildingLevel = i == 0 ? 1 : 0 }).ToArray(),
            Factions = fixture.Factions.Select(f => f with { Resources = new(500, 100, 200) }).ToArray(),
            Commanders = [new() { Id = "leader", Owner = unit.Owner, RankHealthBonus = 50 },
                new() { Id = "other", Owner = unit.Owner, RankHealthBonus = 100 }],
            CommanderOffers = [new("future", "other", unit.Owner, 0, 10, new(0, 0, 0)),
                new("available", "leader", unit.Owner, 0, 1, new(80, 10, 5), PerTurnLimit: 1)] };
        var leader = new Commander { Id = "leader", DisplayName = "测试将领", Movement = 2,
            Stars = new Dictionary<string, int> { ["infantry"] = 3 }, Skills = [Ew4Combat.Skill(22), Ew4Combat.Skill(24)],
            Items = [new("attack", Ew4Commander.Effect(11), 4), new("move", Ew4Commander.Effect(9), 1)] };
        var other = new Commander { Id = "other", Movement = 1, Stars = new Dictionary<string, int> { ["infantry"] = 2 } };
        var rules = new WorldCommanderRules { Definitions = new Dictionary<string, Commander> { ["leader"] = leader, ["other"] = other },
            TitleHealing = new Dictionary<int, int> { [0] = 3 } };
        byte[] before = WorldSave.Encode(world);
        Check(!WorldCommanders.CanDeploy(world, unit.Id, "future", rules) && WorldCommanders.CanDeploy(world, unit.Id, "available", rules),
            "deployment availability and affordability use specific offers");
        var aiPlan = WorldAi.PlanCommander(world with { Units = world.Units.Select(u => u.Id == unit.Id
            ? u with { Combatant = u.Combatant with { Health = 100 } } : u).ToArray() }, rules, scoring);
        Check(aiPlan is { UnitId: "unit.2", OfferId: "available" }, "AI selects available offer after a locked first item");
        var deployed = WorldCommanders.Deploy(world, world.Revision, unit.Id, "available", rules);
        var assigned = deployed.State.Units.Single(u => u.Id == unit.Id);
        Check(assigned.CommanderId == "leader" && assigned.CommanderBase != null && assigned.Combatant.Profile.MaxHealth == 150
            && assigned.Combatant.Health == 97 && assigned.Combatant.Profile.Movement == 9 && assigned.RemainingMovement == 5,
            "deployment applies rank health, profile movement and current damage ratio once");
        Check(assigned.Combatant.AttackBonus == unit.Combatant.AttackBonus + 4 && assigned.Combatant.Stars == 3
            && assigned.Support is { Logistics: true, TitleHealing: 3, FieldHealing: 2 } && assigned.Support.IncomeMultiplier == 1.8f,
            "assignment resolves combat, supply, healing and economic effects");
        Check(deployed.State.CommanderOffers.Count == 1 && deployed.State.Factions.Single(f => f.Id == unit.Owner).Resources == new Resources(420, 90, 195)
            && deployed.State.Factions.Single(f => f.Id == unit.Owner).TurnUses["commander.deployment"] == 1
            && deployed.State.Revision == world.Revision + 1 && deployed.State.RandomState == world.RandomState && deployed.State.AiRandomState == world.AiRandomState,
            "deployment removes consumed offers, pays costs and commits exactly one revision without RNG");
        Check(before.SequenceEqual(WorldSave.Encode(world)), "deployment does not mutate input world");
        var restored = WorldSave.Decode(WorldSave.Encode(deployed.State), deployed.State.ContentRevision);
        Check(restored.Units.Single(u => u.Id == unit.Id).CommanderBase != null && restored.CommanderOffers.Count == 1,
            "save retains unassignment base and remaining offers");
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(WorldSave.Encode(restored)).Replace("\"version\":" + WorldSave.Version, "\"version\":2")),
            restored.ContentRevision), "save.version");
        var detached = WorldCommanders.Detach(restored, restored.Revision, unit.Id, rules);
        var back = detached.State.Units.Single(u => u.Id == unit.Id);
        Check(back.CommanderId == null && back.CommanderBase == null && back.Combatant.Profile.Movement == 6 && back.RemainingMovement == 2
            && back.Combatant.Profile.MaxHealth == 100 && back.Combatant.Health == 64 && back.Support == unit.Support
            && back.Combatant.Stars == unit.Combatant.Stars && back.Combatant.AttackBonus == unit.Combatant.AttackBonus && back.Combatant.Skills.Count == 0,
            "unassignment restores all base bonuses after save without refunding spent movement or HP rounding");
        var replaceRules = rules with { AllowReplacement = true };
        var swapped = WorldCommanders.Assign(restored, restored.Revision, unit.Id, "other", replaceRules);
        var swappedUnit = swapped.State.Units.Single(u => u.Id == unit.Id);
        Check(swappedUnit.CommanderId == "other" && swappedUnit.Combatant.Profile.MaxHealth == 200 && swappedUnit.Combatant.Health == 129
            && swappedUnit.Combatant.Profile.Movement == 7 && swappedUnit.Combatant.AttackBonus == unit.Combatant.AttackBonus
            && !swappedUnit.Support.Logistics && swappedUnit.Support.IncomeMultiplier == 1 && swapped.Notices.Any(n => n.Kind == "commander.detached"),
            "replacement removes old bonuses and uses one direct health ratio");
        var redeployed = WorldCommanders.Assign(detached.State, detached.State.Revision, unit.Id, "leader", rules);
        Check(redeployed.State.Units.Single(u => u.Id == unit.Id).Combatant.AttackBonus == assigned.Combatant.AttackBonus,
            "reassignment never stacks attack equipment twice");
        Reject(() => WorldCommanders.Assign(restored, restored.Revision, unit.Id, "other", rules), "commander.occupied");
        Reject(() => WorldCommanders.Assign(restored, restored.Revision, unit.Id, "leader", rules), "commander.unavailable");
        Reject(() => WorldCommanders.Deploy(world, world.Revision, unit.Id, "future", rules), "commander.deployment");
        Reject(() => WorldCommanders.Deploy(world, world.Revision + 1, unit.Id, "available", rules), "action.stale");
        var unaffordable = world with { Factions = world.Factions.Select(f => f.Id == unit.Owner ? f with { Resources = new(10, 10, 10) } : f).ToArray() };
        Reject(() => WorldCommanders.Deploy(unaffordable, unaffordable.Revision, unit.Id, "available", rules), "commander.deployment");
        Check(before.SequenceEqual(WorldSave.Encode(world)), "failed commander actions leave all state unchanged");
        var imported = restored with { Units = restored.Units.Select(u => u.Id == unit.Id ? u with { CommanderBase = null } : u).ToArray() };
        Reject(() => WorldCommanders.Detach(imported, imported.Revision, unit.Id, rules), "commander.base_missing");
        var noMovement = WorldCommanders.Assign(world with { Units = world.Units.Select(u => u with { RemainingMovement = 0 }).ToArray() }, world.Revision, unit.Id, "leader", rules);
        Check(noMovement.State.Units.Single(u => u.Id == unit.Id).RemainingMovement == 0, "assignment cannot reactivate exhausted movement");
        var fortWorld = world with { Units = world.Units.Select(u => u.Id != unit.Id ? u : u with
            { RemainingMovement = 0, Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Category = "fort", Movement = 0 } } }).ToArray() };
        Check(WorldCommanders.Assign(fortWorld, fortWorld.Revision, unit.Id, "leader", rules).State.Units.Single(u => u.Id == unit.Id).Combatant.Profile.Movement == 0,
            "commander cannot make configured immobile fortresses walk");
        var clone = restored.Copy();
        var copiedSkills = (ICollection<string>)clone.Units.Single(u => u.Id == unit.Id).CommanderBase!.Combatant.Skills;
        Check(copiedSkills.IsReadOnly, "copied assignment base skills are immutable");
        try { copiedSkills.Clear(); throw new InvalidOperationException("copied assignment base allowed mutation"); }
        catch (NotSupportedException) { checks++; }
        ((IDictionary<string, float>)restored.Units.Single(u => u.Id == unit.Id).CommanderBase!.Combatant.Profile.DamageMultipliers)["infantry"] = 999;
        Check(clone.Units.Single(u => u.Id == unit.Id).CommanderBase!.Combatant.Profile.DamageMultipliers["infantry"] == 1,
            "world copies own nested assignment base collections");
        ((IDictionary<string, float>)restored.Units.Single(u => u.Id == unit.Id).CommanderBase!.Combatant.Profile.DamageMultipliers)["infantry"] = 1;
        var stacked = world with { Limits = new() { UnitsPerCell = 2 }, Units = [unit with { Combatant = unit.Combatant with { Health = 30 } },
            unit with { Id = "unit.3", Combatant = unit.Combatant with { Health = 100 } }, enemy] };
        Check(WorldAi.PlanCommander(stacked, rules, scoring)?.UnitId == "unit.3", "AI handles a weak first stacked unit and stronger second unit at the same city");
        var turns = new WorldTurnRules { Buildings = [new("city", 1, new(10, 5, 20))], VeterancyHealing = new Dictionary<string, int> { ["0"] = 0 } };
        var ai = new WorldAiRules { ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 }, Tactics = new()
            { Grades = new Dictionary<string, float> { ["0"] = 1 }, Buildings = new Dictionary<string, AiBuildingValue> { ["city"] = new(50, 10) } } };
        var production = new WorldProductionRules { Tasks = [], Commanders = rules };
        var strongWorld = world with { Units = world.Units.Select(u => u.Id == unit.Id ? u with { Combatant = u.Combatant with { Health = 100 } } : u).ToArray() };
        var stepPlan = WorldAi.PlanStep(strongWorld, ai, production, turns);
        Check(stepPlan.Production.Command == null && stepPlan.Commander?.OfferId == "available" && stepPlan.Unit == null,
            "commander deployment sits between failed production and unit actions");
        var step = WorldAi.ExecuteStep(strongWorld, stepPlan, ai, production, turns, new());
        Check(WorldSave.Encode(step.State).SequenceEqual(WorldSave.Encode(WorldCommanders.Deploy(strongWorld, strongWorld.Revision, unit.Id, "available", rules).State)),
            "AI dispatch uses public deployment cost, effects, notices and revision");
        var replay = WorldSave.Decode(WorldSave.Encode(strongWorld), strongWorld.ContentRevision);
        Check(WorldSave.Encode(step.State).SequenceEqual(WorldSave.Encode(WorldAi.ExecuteStep(replay, WorldAi.PlanStep(replay, ai, production, turns), ai, production, turns, new()).State)),
            "commander dispatch is deterministic after save restore");
        var attemptWorld = strongWorld with { Factions = strongWorld.Factions.Select(f => f.Id == unit.Owner ? f with
            { Recruitment = new Dictionary<string, RecruitmentGrant> { ["recruit"] = new("0") } } : f).ToArray() };
        var failedRecruit = production with { Tasks = [new("recruit", 1)], Offers = new Dictionary<string, RecruitmentOffer>
            { ["recruit"] = new() { Id = "recruit", Template = unit.Combatant } }, Groups = [new("infantry", ["recruit"])],
            Recruit = new() { Economy = [new(long.MinValue, int.MaxValue, 0)], TierAdmissionChance = [0] } };
        var commanderFallback = WorldAi.PlanStep(attemptWorld, ai, failedRecruit, turns);
        var fallbackChange = WorldAi.ExecuteStep(attemptWorld, commanderFallback, ai, failedRecruit, turns, new());
        Check(commanderFallback.Commander != null && commanderFallback.Unit == null && commanderFallback.Production.Command == null
            && commanderFallback.Production.FinalRandomState != attemptWorld.AiRandomState
            && fallbackChange.State.AiRandomState == commanderFallback.Production.FinalRandomState
            && fallbackChange.State.RandomState == attemptWorld.RandomState, "failed production RNG commits with commander deployment before unit actions");

        var inheritanceData = vectors.RootElement.GetProperty("inheritance_rules");
        ProgressionRules Track(string key) => new() { Levels = inheritanceData.GetProperty(key).EnumerateArray()
            .Select((x, i) => new ProgressionLevel(i.ToString(), x.GetInt64())).Append(new("cap", null)).ToArray() };
        string[] abilities = ["infantry", "cavalry", "artillery", "naval", "fort", "commerce", "movement"];
        var inheritance = new CommanderInheritanceRules
        {
            Rank = Track("rank_costs"), Title = Track("title_costs"),
            RankPercent = inheritanceData.GetProperty("rank_percent").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            TitlePercent = inheritanceData.GetProperty("title_percent").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            Abilities = abilities.Select((id, i) => new InheritedAbilityRule(Ew4Combat.Skill(33 + i), id, 1, 5)).ToArray()
        };
        foreach (var vector in vectors.RootElement.GetProperty("inheritance").EnumerateArray())
        {
            var input = vector.GetProperty("inputs");
            int Int(string key) => input.GetProperty(key).GetInt32();
            var expected = vector.GetProperty("expected").EnumerateArray().Select(x => x.GetInt64()).ToArray();
            var receiver = new CommanderLearningState(new(Int("receiver_rank"), Int("receiver_rank_xp")), new(Int("receiver_title"), Int("receiver_title_xp")),
                input.GetProperty("abilities").EnumerateArray().Select((x, i) => (id: abilities[i], value: x.GetInt32())).ToDictionary(p => p.id, p => p.value));
            var donor = new CommanderLearningState(new(Int("donor_rank"), Int("donor_rank_xp")), new(Int("donor_title"), Int("donor_title_xp")), new Dictionary<string, int>());
            var result = CommanderInheritance.Apply(receiver, donor, input.GetProperty("skills").EnumerateArray().Select(x => Ew4Combat.Skill(x.GetInt32())).ToArray(), inheritance);
            Check(new long[] { result.Rank.Level, result.Title.Level, result.Rank.Experience, result.Title.Experience }
                .Concat(abilities.Select(id => (long)result.Abilities[id])).SequenceEqual(expected), "native inheritance experience fractions, advancement and seven teaching abilities");
        }
        var dynamicLearning = new CommanderLearningState(new(0, 0), new(0, 0), new Dictionary<string, int>
            { ["infantry"] = 8, ["above.cap"] = 20 });
        var dynamicInheritance = inheritance with { Rank = new() { Levels = Enumerable.Range(0, 40).Select(i => new ProgressionLevel("r" + i, 1_000_000_000)).Append(new("cap", null)).ToArray() },
            RankPercent = Enumerable.Repeat(100, 41).ToArray(), Abilities = [new("teach.new", "infantry", 2, 12), new("teach.new", "above.cap", 1, 5)] };
        var dynamicResult = CommanderInheritance.Apply(dynamicLearning, dynamicLearning with { Rank = new(30, 5_000_000_000) }, ["teach.new"], dynamicInheritance);
        Check(dynamicResult.Rank.Level == 35 && dynamicResult.Abilities["infantry"] == 10 && dynamicResult.Abilities["above.cap"] == 20,
            "inheritance has dynamic levels/64-bit experience/new skills/caps and never decreases an existing ability");
        Check(dynamicLearning.Abilities["infantry"] == 8, "inheritance owns resulting ability dictionary");
        Reject(() => CommanderInheritance.Apply(dynamicLearning, dynamicLearning with { Rank = new(50, 0) }, [], inheritance), "inheritance.donor");
        var many = strongWorld with { Commanders = Enumerable.Range(0, 100).Select(i => new CommanderRuntime { Id = "general." + i, Owner = unit.Owner }).ToArray(),
            CommanderOffers = Enumerable.Range(0, 100).Select(i => new CommanderDeploymentOffer("offer." + i, "general." + i, unit.Owner, 0,
                i == 99 ? 1 : 10, new(0, 0, 0))).ToArray() };
        var manyRules = rules with { Definitions = many.Commanders.ToDictionary(c => c.Id, c => leader with { Id = c.Id }, StringComparer.Ordinal) };
        var manyCommand = WorldAi.PlanCommander(many, manyRules, scoring);
        Check(manyCommand?.OfferId == "offer.99", "commander roster and city offers exceed original 12/5 slots without truncation");
        var manyDeployed = WorldCommanders.Deploy(many, many.Revision, unit.Id, manyCommand!.OfferId, manyRules);
        Check(manyDeployed.State.Commanders.Count == 100 && manyDeployed.State.CommanderOffers.Count == 99
            && manyDeployed.State.Units.Single(u => u.Id == unit.Id).CommanderId == "general.99", "large commander pool deploys and consumes only selected entry");
        return checks;
    }
}
