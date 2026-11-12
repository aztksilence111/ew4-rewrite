using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class SeaInstallationChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            checks++;
            if (!value) throw new InvalidOperationException("Sea installation: " + label);
        }
        void Reject(Action action)
        {
            try { action(); }
            catch (RuleViolation error) { Check(error.Code == "purchase.site", "sea site rejection"); return; }
            throw new InvalidOperationException("Sea installation: expected purchase.site");
        }
        var cards = LegacyRecruitmentCatalog.Read(File.ReadAllText(Path.Combine(root, "apk_work/original/assets/def_card.xml")));
        var installations = cards.Installations();
        var fixture = WorldChecks.Example();
        var actor = fixture.Units[1] with { Cell = 4, RemainingMovement = 6,
            Combatant = fixture.Units[1].Combatant with { Profile = fixture.Units[1].Combatant.Profile with { Grade = "0" } } };
        var enemy = fixture.Units[0] with { Cell = 5, Combatant = actor.Combatant };
        var ai = new WorldAiRules { Tactics = new() { Grades = new Dictionary<string, float> { ["0"] = 1 } },
            ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var turns = new WorldTurnRules { VeterancyHealing = new Dictionary<string, int> { ["0"] = 0 } };
        foreach (var entry in installations)
        foreach (bool transport in new[] { false, true })
        foreach (bool sea in new[] { false, true })
        {
            var rules = entry.Value;
            var world = fixture with { Units = [actor with { Transport = transport }, enemy],
                Cells = fixture.Cells.Select((c, i) => c with { Owner = i == enemy.Cell ? enemy.Owner : actor.Owner,
                    Terrain = c.Terrain with { Sea = sea && i == actor.Cell } }).ToArray(),
                Factions = fixture.Factions.Select(f => f with { Resources = new(1000, 1000, 1000) }).ToArray() };
            byte[] before = WorldSave.Encode(world);
            Check(WorldSupportActions.CanBuildInstallation(world, actor.Id, rules) == !sea, entry.Key + " eligibility");
            if (sea)
            {
                Reject(() => WorldSupportActions.InstallationCost(world, actor.Id, rules));
                Reject(() => WorldSupportActions.BuildInstallation(world, world.Revision, actor.Id, rules));
                // A previously selected AI command must also fail when executed on a sea site.
                var stalePlan = new AiProductionPlan(world.Revision, world.AiRandomState, world.AiRandomState,
                    "installation", new AiInstallationCommand(world.Revision, actor.Id, entry.Key));
                Reject(() => WorldAi.ExecuteProduction(world, stalePlan, new() { Installations = installations }, ai));
            }
            else
            {
                Check(WorldSupportActions.InstallationCost(world, actor.Id, rules) == rules.Purchase.Cost, "land quote");
                var built = WorldSupportActions.BuildInstallation(world, world.Revision, actor.Id, rules).State;
                Check(built.Cells[actor.Cell].InstallationKind == rules.Kind && built.Units.Single(u => u.Id == actor.Id).Acted
                    && built.Factions.Single(f => f.Id == actor.Owner).Resources == new Resources(1000 - rules.Purchase.Cost.Money,
                        1000 - rules.Purchase.Cost.Industry, 1000 - rules.Purchase.Cost.Food), "land construction and cost");
            }
            var production = new WorldProductionRules { Tasks = [new("installation", 1)],
                Installations = new Dictionary<string, InstallationBuildRules> { [entry.Key] = rules },
                InstallationScoring = new() { Recipes = [new(entry.Key, [new("infantry", 3)])], PreferenceOrder = [entry.Key],
                    PreferredBase = 0, Admission = new(1, 1, 1) } };
            int commands = 0;
            for (ulong seed = 0; seed < 8; seed++)
            {
                var sample = world with { AiRandomState = seed };
                var plan = WorldAi.PlanProduction(sample, ai, production, turns);
                using (PlanningScope.Begin(ai, production, turns))
                    Check(plan == WorldAi.PlanProduction(sample, ai, production, turns), "indexed AI agrees with normal planning");
                if (sea) Check(plan.Command == null && plan.AttemptedTask == null, "AI excludes sea construction");
                else if (plan.Command is AiInstallationCommand command)
                {
                    commands++;
                    Check(command.UnitId == actor.Id && command.RecipeId == entry.Key, "AI selects land builder");
                    Check(WorldAi.ExecuteProduction(sample, plan, production, ai).State.Cells[actor.Cell].InstallationKind == rules.Kind,
                        "AI builds land installation");
                }
            }
            if (!sea) Check(commands > 0, "land AI construction remains available");
            Check(before.SequenceEqual(WorldSave.Encode(world)), "planning and rejected commands preserve original world");
        }
        string?[] kinds = ["trench", "fence", "bunker", "trench", "bunker", "floating.custom", "unknown.custom", null, null];
        var damaged = fixture with { Cells = fixture.Cells.Select((c, i) => c with
            { Terrain = c.Terrain with { Sea = i < 3 || i >= 5 }, InstallationKind = kinds[i],
                BuildingKind = i == 1 ? "port" : null, BuildingLevel = i == 1 ? 1 : 0 }).ToArray() };
        byte[] damagedBytes = WorldSave.Encode(damaged);
        var repairRules = installations.Values.Concat([new InstallationBuildRules { Kind = "floating.custom",
            Purchase = new() { AllowSea = true } }]).ToArray();
        var repair = WorldSupportActions.RemoveIllegalSeaInstallations(damaged, repairRules);
        Check(repair.State.Cells.Take(3).All(c => c.InstallationKind == null), "repair removes all three illegal sea fieldworks");
        Check(repair.State.Cells.Skip(3).Select(c => c.InstallationKind).SequenceEqual(kinds.Skip(3)),
            "repair preserves land, explicitly permitted sea and unknown custom installations");
        Check(repair.State.Cells[1].BuildingKind == "port" && repair.State.Cells[1].BuildingLevel == 1, "repair preserves sea building");
        Check(repair.Notices.Select(n => n.Value).SequenceEqual(new long[] { 0, 1, 2 }), "repair reports exact cells");
        Check(WorldSave.Encode(damaged with { Cells = repair.State.Cells, Revision = damaged.Revision + 1 })
            .SequenceEqual(WorldSave.Encode(repair.State)), "repair changes only installations and one world revision");
        Check(damagedBytes.SequenceEqual(WorldSave.Encode(damaged)), "repair leaves original snapshot intact");
        Check(ReferenceEquals(WorldSupportActions.RemoveIllegalSeaInstallations(repair.State, repairRules).State, repair.State),
            "repair is idempotent");
        var permitted = WorldSupportActions.RemoveIllegalSeaInstallations(damaged, repairRules.Concat([
            new InstallationBuildRules { Kind = "bunker", Purchase = new() { AllowSea = true } }]));
        Check(permitted.State.Cells[2].InstallationKind == "bunker", "explicit sea permission for the same kind takes precedence");

        var sailing = fixture with { Units = [actor], Cells = fixture.Cells.Select(c => c with
            { Owner = actor.Owner, Terrain = c.Terrain with { Sea = true } }).ToArray(),
            Factions = fixture.Factions.Select(f => f with { Resources = new(1000, 1000, 1000) }).ToArray() };
        Check(WorldSupportActions.CanBuyTransport(sailing, actor.Id, cards.Transport()), "transport purchase still allows sea sites");
        Check(WorldSupportActions.BuyTransport(sailing, sailing.Revision, actor.Id, cards.Transport()).State.Units.Single().Transport,
            "transport purchase still succeeds");
        return checks;
    }
}
