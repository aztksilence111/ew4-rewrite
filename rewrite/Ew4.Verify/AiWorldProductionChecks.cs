using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class AiWorldProductionChecks
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
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/ai_production_dispatch_vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("stock").EnumerateArray())
        {
            var input = vector.GetProperty("inputs");
            long Int(string name) => input.GetProperty(name).GetInt64();
            bool Flag(string name) => input.GetProperty(name).GetBoolean();
            long? actual = Flag("exists") && Flag("affordable") ? AiProduction.AffordableCopies(new(Int("money"), Int("industry"), 0),
                new(Int("price"), Int("industry_price"), 0), new(127, true)) : 0;
            Check(actual == vector.GetProperty("expected").GetInt64(), "native affordability vector");
        }
        Check(AiProduction.AffordableCopies(new(1000, 2000, 17), new(1, 2, 4)) == 4, "modern food cost limits actual affordable stock");
        Check(AiProduction.AffordableCopies(new(long.MaxValue, 0, 0), new(1, 0, 0)) == long.MaxValue, "modern stock has no byte cap");
        Check(AiProduction.AffordableCopies(new(0, 0, 0), new(0, 0, 0)) == null, "free offer has no resource limit");
        Check(AiProduction.AffordableCopies(new(0, 99, 0), new(0, 3, 0)) == 33, "industry only offer works without money cost");
        Check(AiProduction.AffordableCopies(new(0, 0, 0), new(0, 0, 0), new(5)) == 5, "configured free offer cap");
        Reject(() => AiProduction.AffordableCopies(new(0, 0, 0), new(0, 0, 0), new(-1)), "ai.affordability_configuration");
        foreach (long scale in new long[] { 1, 7, 100, 1000, 1_000_000_000 })
        {
            var weights = new AiTaskWeight[] { new("a", scale * 2), new("disabled", 0), new("b", scale * 3), new("c", scale * 5) };
            foreach (long draw in new long[] { 0, scale * 2 - 1, scale * 2, scale * 5 - 1, scale * 5, scale * 10 - 1 })
            {
                long observed = 0;
                var result = AiProduction.SelectTask(weights, bound => { observed = bound; return draw; });
                Check(result == (draw < scale * 2 ? "a" : draw < scale * 5 ? "b" : "c") && observed == scale * 10,
                    "task sampling uses actual weights at any total");
            }
        }
        Check(AiProduction.SelectTask([new("a", 0)], _ => throw new InvalidOperationException()) == null, "no task draws for all disabled weights");
        Reject(() => AiProduction.SelectTask([new("a", 1), new("a", 2)], _ => 0), "ai.production_tasks");
        Reject(() => AiProduction.SelectTask([new("a", 1)], _ => 1), "ai.production_random");
        var neighborhoodMap = new HexMap(15, 11);
        foreach (int cell in Enumerable.Range(0, neighborhoodMap.Count))
            foreach (int radius in new[] { 0, 1, 2, 4, 15, int.MaxValue })
                Check(neighborhoodMap.Within(cell, radius).SequenceEqual(Enumerable.Range(0, neighborhoodMap.Count)
                    .Where(i => i != cell && neighborhoodMap.Distance(i, cell) <= radius)), "bounded hex neighborhood matches full search at edges and large radii");
        Reject(() => neighborhoodMap.Within(0, -1).ToArray(), "map.radius");

        var fixture = WorldChecks.Example();
        var actor = fixture.Units[1] with { Cell = 0, RemainingMovement = 6, Acted = false };
        var enemy = fixture.Units[0] with { Cell = 8, RemainingMovement = 0, Acted = true };
        var template = actor.Combatant with { Profile = actor.Combatant.Profile with { Grade = "0" } };
        actor = actor with { Combatant = template };
        enemy = enemy with { Combatant = template };
        var world = fixture with
        {
            Width = 9, Height = 1, Units = [actor, enemy],
            Cells = Enumerable.Range(0, 9).Select(i => new CellState { Owner = i == 8 ? enemy.Owner : actor.Owner,
                BuildingKind = i == 0 ? "city" : null, BuildingLevel = i == 0 ? 1 : 0 }).ToArray(),
            Factions = fixture.Factions.Select(f => f with { Resources = new(1000, 1000, 1000),
                Recruitment = new Dictionary<string, RecruitmentGrant> { ["recruit"] = new("0"), ["fort"] = new("0") },
                Survival = new(true, true, []) }).ToArray()
        };
        var tactics = new AiTacticalRules { Grades = new Dictionary<string, float> { ["0"] = 1 },
            Buildings = new Dictionary<string, AiBuildingValue> { ["city"] = new(1000, 10) },
            Installations = new Dictionary<string, long> { ["trench"] = 10 } };
        var ai = new WorldAiRules { Tactics = tactics, ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var turns = new WorldTurnRules { Buildings = [new("city", 1, new(0, 0, 100)), new("city", 2, new(0, 0, 200))],
            VeterancyHealing = new Dictionary<string, int> { ["0"] = 0 } };
        var offer = new RecruitmentOffer { Id = "recruit", Template = template, Cost = new(30, 5, 10),
            BuildingKinds = ["city"], PerTurnLimit = 1 };
        var recruit = new WorldProductionRules { Offers = new Dictionary<string, RecruitmentOffer> { [offer.Id] = offer },
            Groups = [new("infantry", [offer.Id])], Tasks = [new("recruit", 1)],
            Recruit = new() { Pressure = [new(0, 0)], LowValueSkipChance = 0 } };
        WorldChange Direct(WorldState source, WorldProductionCommand command, WorldProductionRules rules) => command switch
        {
            AiRecruitCommand c when c.RelocateUnitId != null => WorldActions.RelocateAndRecruit(source, source.Revision, c.RelocateUnitId,
                c.Relocation!.Value, c.NewUnitId, c.Fortress ? rules.Fortresses[c.OfferId] : rules.Offers[c.OfferId], ai.Movement),
            AiRecruitCommand c => WorldActions.Recruit(source, source.Revision, c.Cell, c.NewUnitId, c.Fortress ? rules.Fortresses[c.OfferId] : rules.Offers[c.OfferId]),
            AiInstallationCommand c => WorldSupportActions.BuildInstallation(source, source.Revision, c.UnitId, rules.Installations[c.RecipeId]),
            AiUpgradeCommand c => WorldImprovements.UpgradeBuilding(source, source.Revision, c.Cell, rules.BuildingUpgrades!),
            AiTransportCommand c => WorldSupportActions.BuyTransport(source, source.Revision, c.UnitId, rules.Transport!),
            _ => throw new InvalidOperationException()
        };
        int Verify(WorldState source, WorldProductionRules rules, Func<WorldProductionCommand, bool> expected, int attempts = 60)
        {
            int successes = 0;
            for (ulong seed = 0; seed < (ulong)attempts; seed++)
            {
                var sample = source with { AiRandomState = seed };
                byte[] before = WorldSave.Encode(sample);
                var plan = WorldAi.PlanProduction(sample, ai, rules, turns);
                Check(before.SequenceEqual(WorldSave.Encode(sample)), "production planning is read only");
                var restored = WorldSave.Decode(before, sample.ContentRevision);
                var replayPlan = WorldAi.PlanProduction(restored, ai, rules, turns);
                Check(plan == replayPlan, "production command and draws survive a save round trip");
                if (plan.Command == null) continue;
                successes++;
                Check(expected(plan.Command), "expected production action family and destination");
                var change = WorldAi.ExecuteProduction(sample, plan, rules, ai);
                var direct = Direct(sample, plan.Command, rules);
                Check(WorldSave.Encode(change.State).SequenceEqual(WorldSave.Encode(direct.State with { AiRandomState = plan.FinalRandomState }))
                    && change.Notices.SequenceEqual(direct.Notices), "AI production equals public action including costs and notices");
                Check(change.State.Revision == sample.Revision + 1 && change.State.RandomState == sample.RandomState
                    && change.State.AiRandomState == plan.FinalRandomState, "production commits one revision and only its private RNG");
                Check(WorldSave.Encode(change.State).SequenceEqual(WorldSave.Encode(WorldAi.ExecuteProduction(restored, replayPlan, rules, ai).State)),
                    "restored production executes identically");
                Check(before.SequenceEqual(WorldSave.Encode(sample)), "successful production preserves input world");
            }
            Check(successes > 0, "production fixture has reachable successful commands");
            return successes;
        }
        Verify(world, recruit, c => c is AiRecruitCommand { Cell: 0, Relocation: 1, Fortress: false });
        Verify(world with { Units = [enemy] }, recruit, c => c is AiRecruitCommand { Cell: 0, Relocation: null });
        var stacked = world with { Limits = new() { UnitsPerCell = 2 } };
        Verify(stacked, recruit, c => c is AiRecruitCommand { Cell: 0, Relocation: null });
        Verify(stacked with { Units = [actor, actor with { Id = "second" }, enemy] }, recruit,
            c => c is AiRecruitCommand { Cell: 0, Relocation: 1 });
        var navalWorld = world with { Units = [enemy], Cells = world.Cells.Select((c, i) => i == 0 ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray() };
        Verify(navalWorld, recruit with { Groups = [new("navy", [offer.Id], true)], Tasks = [new("naval", 1)] }, c => c is AiRecruitCommand { Cell: 0 });

        var buildWorld = world with { Units = [actor with { Cell = 3 }, enemy with { Cell = 4 }],
            Cells = world.Cells.Select((c, i) => i == 0 ? c with { BuildingKind = null, BuildingLevel = 0 } : c).ToArray() };
        var installation = new WorldProductionRules { Tasks = [new("installation", 1)],
            Installations = new Dictionary<string, InstallationBuildRules> { ["trench.recipe"] = new() { Kind = "trench", Purchase = new() { Cost = new(10, 5, 1) } } },
            InstallationScoring = new() { Recipes = [new("trench.recipe", [new("infantry", 3)])], PreferenceOrder = ["trench.recipe"],
                PreferredBase = 0, Admission = new(1, 1, 1) } };
        Verify(buildWorld, installation, c => c is AiInstallationCommand { UnitId: "unit.2", RecipeId: "trench.recipe" });
        var fortress = new WorldProductionRules { Tasks = [new("fortress", 1)],
            Fortresses = new Dictionary<string, RecruitmentOffer> { ["fort.recipe"] = new() { Id = "fort", Template = template with
                { Profile = template.Profile with { Id = "fort", Category = "fort" } }, Cost = new(10, 5, 0), Building = BuildingRequirement.Forbidden, PerTurnLimit = 1 } },
            FortressScoring = new() { Recipes = [new("fort.recipe", 1, false)], MinimumScore = 0, Admission = new(1, 1, 1) } };
        Verify(world with { Units = [enemy] }, fortress, c => c is AiRecruitCommand { Fortress: true, OfferId: "fort.recipe" });

        var supplyWorld = world with { Units = [actor with { Support = new() { FoodConsumption = 100 } }, enemy] };
        WorldState Supply(long food, int shortage = 0) => supplyWorld with
        {
            Factions = supplyWorld.Factions.Select(f => f.Id == actor.Owner
                ? f with { Resources = new(1000, 1000, food), Shortage = shortage } : f).ToArray()
        };
        WorldProductionRules RecruitSupply(RecruitmentOffer candidate, bool naval = false) => recruit with
        {
            Offers = new Dictionary<string, RecruitmentOffer> { [candidate.Id] = candidate },
            Groups = [new("supply", [candidate.Id], naval)], Tasks = [new(naval ? "naval" : "recruit", 1)],
            Recruit = recruit.Recruit with { TierAdmissionChance = [100], Economy = [new(long.MinValue, 1, 0)] }
        };
        var consumingOffer = offer with { Support = new() { FoodConsumption = 20 }, PerTurnLimit = null };
        var consumingRecruit = RecruitSupply(consumingOffer);
        var consumingFort = fortress.Fortresses["fort.recipe"] with { Support = new() { FoodConsumption = 20 } };
        var consumingFortress = fortress with { Fortresses = new Dictionary<string, RecruitmentOffer> { ["fort.recipe"] = consumingFort } };
        void BlockSupply(WorldState source, WorldProductionRules rules, WorldTurnRules economy, string label)
        {
            for (ulong seed = 0; seed < 12; seed++)
            {
                var plan = WorldAi.PlanProduction(source with { AiRandomState = seed }, ai, rules, economy);
                Check(plan is { Command: null, AttemptedTask: null } && plan.FinalRandomState == seed, label);
            }
        }
        BlockSupply(Supply(29), consumingRecruit, turns, "recruit cost plus new upkeep cannot cause next-turn starvation");
        BlockSupply(Supply(19), consumingFortress, turns, "fortress also reserves its new upkeep");
        BlockSupply(Supply(0), fortress, turns with { Buildings = [new("city", 1, new(0, 0, 99))] },
            "even a zero-upkeep fortress is blocked when the existing army cannot be fed");
        foreach (int shortage in new[] { 1, 2, 3, 4 })
        {
            BlockSupply(Supply(1000, shortage), consumingRecruit, turns, "recorded shortage blocks land recruitment");
            BlockSupply(Supply(1000, shortage), consumingFortress, turns, "recorded shortage blocks fortress construction");
        }
        var navalSupply = Supply(29) with { Cells = navalWorld.Cells };
        BlockSupply(navalSupply, RecruitSupply(consumingOffer, true), turns, "naval recruitment cannot bypass supply gate");
        BlockSupply(Supply(1000), consumingRecruit, turns with { ResourceCap = 119 }, "next-turn food cap applies before upkeep");
        Verify(Supply(30), consumingRecruit, c => c is AiRecruitCommand { Fortress: false });
        Verify(Supply(20), consumingFortress, c => c is AiRecruitCommand { Fortress: true });
        foreach (var exempt in new[] { consumingOffer with { Support = consumingOffer.Support with { Logistics = true } },
            consumingOffer with { Template = consumingOffer.Template! with { Skills = [Ew4Combat.Skill(22)] } } })
            Verify(Supply(10), RecruitSupply(exempt), c => c is AiRecruitCommand { Fortress: false });
        Verify(Supply(10) with { Units = [actor with { Support = new() { FoodConsumption = 1000, Logistics = true } }, enemy] },
            consumingRecruit, c => c is AiRecruitCommand { Fortress: false });
        var afterSupplyRecruit = WorldAi.ExecuteProduction(Supply(30), WorldAi.PlanProduction(Supply(30), ai, consumingRecruit, turns),
            consumingRecruit, ai).State;
        BlockSupply(afterSupplyRecruit, consumingRecruit, turns, "successive production recalculates remaining supply");
        var mixed = consumingRecruit with { Offers = new Dictionary<string, RecruitmentOffer>
            { ["recruit"] = consumingOffer, ["fort"] = consumingOffer with { Id = "fort", Support = new() } },
            Groups = [new("supply", ["recruit", "fort"])] };
        Verify(Supply(10), mixed, c => c is AiRecruitCommand { OfferId: "fort" });

        var upgrade = new WorldProductionRules { Tasks = [new("upgrade", 1)],
            BuildingUpgrades = new() { Steps = [new("city", 1, 2, new(50, 40, 5))] },
            Upgrade = new() { BuildingWeights = new Dictionary<string, long> { ["city"] = 3 }, Chances = [new(0, 100)] } };
        Verify(world with { Units = [actor with { Cell = 1 }, enemy] }, upgrade, c => c is AiUpgradeCommand { Cell: 0 });
        var transport = new WorldProductionRules { Tasks = [new("transport", 1)], Transport = new() { Purchase = new() { Cost = new(25, 10, 2) } } };
        var coastal = world with { Cells = world.Cells.Select((c, i) => i == 1 ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray() };
        Verify(coastal, transport, c => c is AiTransportCommand { UnitId: "unit.2" });

        var stalePlan = WorldAi.PlanProduction(world, ai, recruit, turns);
        Check(stalePlan.Command != null, "stale command fixture");
        Reject(() => WorldAi.ExecuteProduction(world with { Revision = 1 }, stalePlan, recruit, ai), "action.stale");
        Reject(() => WorldAi.ExecuteProduction(world with { AiRandomState = 1 }, stalePlan, recruit, ai), "ai.production_stale_random");
        var broke = world with { Factions = world.Factions.Select(f => f.Id == actor.Owner ? f with { Resources = new(0, 0, 0) } : f).ToArray() };
        byte[] brokeBefore = WorldSave.Encode(broke);
        Reject(() => WorldAi.ExecuteProduction(broke, stalePlan, recruit, ai), "recruit.resources");
        Check(brokeBefore.SequenceEqual(WorldSave.Encode(broke)), "failed AI production rolls back relocation, cost and random");
        Check(WorldAi.PlanProduction(world with { Limits = new() { UnitsPerFaction = 1 } }, ai, recruit, turns).Command == null,
            "AI obeys scenario faction army cap");
        Check(WorldAi.PlanProduction(world with { Units = [actor with { Behavior = "3" }, enemy] }, ai, recruit, turns).Command == null,
            "AI does not relocate hold-position garrison to recruit");
        var noFoodOffer = offer with { Cost = new(0, 0, 2000) };
        var noFood = recruit with { Offers = new Dictionary<string, RecruitmentOffer> { [offer.Id] = noFoodOffer } };
        Check(WorldAi.PlanProduction(world, ai, noFood, turns) is { Command: null, AttemptedTask: null, FinalRandomState: 1926 },
            "unaffordable task including food does not enter sampling or consume draws");
        Reject(() => (recruit with { Groups = [new("a", ["recruit"]), new("b", ["recruit"])] }).Validate(), "ai.production_configuration");
        var legacyCollisionOffers = new Dictionary<string, RecruitmentOffer>();
        var legacyCollisionGroups = new List<ProductionOfferGroup>();
        foreach (string id in new[] { "9", "17", "23", "a.new.category" })
        {
            legacyCollisionOffers[id] = offer with { Id = id };
            legacyCollisionGroups.Add(new(id, [id]));
        }
        (recruit with { Offers = legacyCollisionOffers, Groups = legacyCollisionGroups }).Validate();
        Check(legacyCollisionOffers.Count == 4, "new mappings preserve distinct legacy alias and new categories");

        var battle = new WorldBattleRules();
        var failedProduction = recruit with { Recruit = recruit.Recruit with { TierAdmissionChance = [0],
            Economy = [new(long.MinValue, int.MaxValue, 0)] } };
        var failed = WorldAi.PlanStep(world, ai, failedProduction, turns);
        Check(failed.Production is { Command: null, AttemptedTask: "recruit" } && failed.Production.FinalRandomState != world.AiRandomState
            && failed.Unit != null && failed.Unit.InitialRandomState == failed.Production.FinalRandomState,
            "failed production draws feed the fallback unit plan");
        var step = WorldAi.ExecuteStep(world, failed, ai, failedProduction, turns, battle);
        var directUnit = WorldAi.ExecuteUnit(world with { AiRandomState = failed.Production.FinalRandomState }, failed.Unit!, ai, battle);
        Check(WorldSave.Encode(step.State).SequenceEqual(WorldSave.Encode(directUnit.State)), "fallback commits failed production RNG with actual unit action");
        var exhausted = world with { Units = world.Units.Select(u => u with { RemainingMovement = 0, Acted = true }).ToArray() };
        var endPlan = WorldAi.PlanStep(exhausted, ai, failedProduction, turns);
        Check(endPlan.Unit == null && endPlan.Production.Command == null && endPlan.Production.FinalRandomState != exhausted.AiRandomState,
            "no remaining unit actions falls through to end turn");
        var ended = WorldAi.ExecuteStep(exhausted, endPlan, ai, failedProduction, turns, battle);
        var directEnd = WorldTurns.EndTurn(exhausted with { AiRandomState = endPlan.Production.FinalRandomState }, exhausted.Revision, turns);
        Check(WorldSave.Encode(ended.State).SequenceEqual(WorldSave.Encode(directEnd.State)), "turn fallback commits consumed production draws");
        var running = world;
        for (int i = 0; i < 40 && running.Phase == TurnPhase.Active && running.PendingDialogue == null; i++)
        {
            var saved = WorldSave.Decode(WorldSave.Encode(running), running.ContentRevision);
            var next = WorldAi.ExecuteStep(running, WorldAi.PlanStep(running, ai, recruit, turns), ai, recruit, turns, battle);
            var replay = WorldAi.ExecuteStep(saved, WorldAi.PlanStep(saved, ai, recruit, turns), ai, recruit, turns, battle);
            Check(next.State.Revision == running.Revision + 1 && next.Notices.SequenceEqual(replay.Notices)
                && WorldSave.Encode(next.State).SequenceEqual(WorldSave.Encode(replay.State)), "continuous production/unit/turn control agrees after every saved step");
            running = next.State;
        }
        var perfOffers = Enumerable.Range(0, 128).ToDictionary(i => "offer." + i,
            i => offer with { Id = "offer." + i, Template = template with { Profile = template.Profile with { Id = "profile." + i } } }, StringComparer.Ordinal);
        var largeWorld = world with
        {
            Width = 80, Height = 60, Units = [enemy with { Cell = 4799 }],
            Cells = Enumerable.Range(0, 4800).Select(i => new CellState { Owner = i == 4799 ? enemy.Owner : actor.Owner,
                BuildingKind = i % 40 == 0 ? "city" : null, BuildingLevel = i % 40 == 0 ? 1 : 0 }).ToArray(),
            Factions = world.Factions.Select(f => f.Id != actor.Owner ? f : f with
                { Recruitment = perfOffers.Keys.ToDictionary(id => id, _ => new RecruitmentGrant("0"), StringComparer.Ordinal) }).ToArray()
        };
        var largeRules = recruit with { Offers = perfOffers,
            Groups = Enumerable.Range(0, 32).Select(i => new ProductionOfferGroup("group." + i,
                Enumerable.Range(i * 4, 4).Reverse().Select(j => "offer." + j).ToArray())).ToArray() };
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var largePlan = WorldAi.PlanProduction(largeWorld, ai, largeRules, turns);
        stopwatch.Stop();
        Check(largePlan.Command is AiRecruitCommand && largeWorld.Cells.Count > 4000, "large world production resolves dynamic categories across all city sites");
        var largeChange = WorldAi.ExecuteProduction(largeWorld, largePlan, largeRules, ai);
        Check(largeChange.State.Units.Count == 2, "large world production command executes against public rules");
        long recruitMilliseconds = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();
        var fortressPlan = WorldAi.PlanProduction(largeWorld with { Factions = largeWorld.Factions.Select(f => f.Id != actor.Owner ? f : f with
            { Recruitment = new Dictionary<string, RecruitmentGrant> { ["fort"] = new("0") } }).ToArray() }, ai, fortress, turns);
        stopwatch.Stop();
        Check(fortressPlan.Command is AiRecruitCommand { Fortress: true }, "large world fortress uses bounded neighborhoods");
        File.WriteAllText(Path.Combine(root, "rewrite/reports/ai_production_performance.json"), JsonSerializer.Serialize(new
        {
            cells = 4800, cities = 120, recruitOffers = 128, recruitGroups = 32, recruitMilliseconds,
            fortressMilliseconds = stopwatch.ElapsedMilliseconds,
            scope = "Single production planning sample on desktop; no rendering, Android runtime, or complete AI turn benchmark."
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
