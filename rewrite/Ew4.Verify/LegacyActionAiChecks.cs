using System.Globalization;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class LegacyActionAiChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Legacy action/AI: " + label); }
        string assets = Path.Combine(root, "apk_work/original/assets");
        var buildings = LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml")));
        var combat = LegacyCombatCatalog.Read(File.ReadAllText(Path.Combine(root, "reverse/reports/runtime_rule_tables.json")),
            File.ReadAllText(Path.Combine(assets, "def_terraintype.xml")), File.ReadAllText(Path.Combine(assets, "def_installation.xml")), buildings);
        var runtime = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var cards = LegacyRecruitmentCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_card.xml")));
        var installations = cards.Installations(); var transport = cards.Transport();
        var ai = LegacyAiCatalog.Rules(combat, transport: transport);
        int namedTacticalChecks = AiTacticalChecks.Run(root, ai.Tactics); checks += namedTacticalChecks;
        Check(ai.Tactics.Buildings.Keys.ToHashSet().SetEquals(LegacyBuildingCatalog.Kinds.Values)
            && ai.Tactics.Installations.Keys.ToHashSet().SetEquals(LegacyRecruitmentCatalog.InstallationKinds.Values), "all tactical names match imported map");
        Check(ai.Tactics.Modifiers.All(m => m.SourceInstallation == null || LegacyRecruitmentCatalog.InstallationKinds.Values.Contains(m.SourceInstallation))
            && ai.Tactics.Modifiers.All(m => m.TargetInstallation == null || LegacyRecruitmentCatalog.InstallationKinds.Values.Contains(m.TargetInstallation)), "modifier names mapped too");
        foreach (string kind in new[] { "city", "industry", "stable", "port" })
            Check(AiStrategy.SelectDestination([new(1, 1, 1, Allied: true, BuildingKind: kind)], false, ai.Strategy.Landing,
                _ => throw new InvalidOperationException("Reserved allied building must not draw")) == -1, "strategic landing reserves actual " + kind);
        Check(AiStrategy.SelectDestination([new(1, 1, 1, Allied: true, BuildingKind: "farmland")], false, ai.Strategy.Landing, _ => 5) == 0
            && AiStrategy.SelectDestination([new(1, 1, 1, Allied: true, BuildingKind: "farmland")], false, ai.Strategy.Landing, _ => 6) == -1, "farmland landing actual probability boundary");
        Check(ai.Strategy.Transport == transport, "strategic transport uses actual action card");
        int variants = 0;
        foreach (string variant in runtime.Variants.Keys)
        {
            var production = LegacyAiCatalog.Production(variant, runtime, cards, buildings);
            Check(production.Offers.Count == 37 && production.Fortresses.Count == 4 && production.Installations.Count == 3 && production.Groups.Count == 18, "all production actions mapped for " + variant);
            Check(production.Offers.Keys.Concat(production.Fortresses.Keys).ToHashSet().SetEquals(cards.Cards.Values.Where(c => c.Recruits).Select(c => c.Id)), "every recruitment card in production");
            Check(production.Groups.SelectMany(g => g.HighestFirst).ToHashSet().SetEquals(production.Offers.Keys), "all ordinary actions in unique production groups");
            Check(production.Groups.Count(g => g.Naval) == 4 && production.Upgrade.BuildingWeights.Keys.ToHashSet().SetEquals(buildings.Definitions.Values.Select(b => b.Kind)), "naval categories and named upgrade weights");
            Check(production.Pool.Admission.ContainsKey("ew4.card.33") && production.InstallationScoring!.PreferenceOrder.SequenceEqual(new[] { "ew4.card.41", "ew4.card.43", "ew4.card.42" })
                && production.FortressScoring!.Recipes.All(r => production.Fortresses.ContainsKey(r.Id)), "recipe preference and card identities mapped consistently");
            variants++;
        }
        var fixture = WorldChecks.Example();
        var profile = runtime.Unit("fra", 1, 0);
        int[] skills = [27, 28, 25];
        foreach (int kind in Enumerable.Range(0, 3))
            foreach (bool skilled in new[] { false, true })
            {
                var installation = installations["ew4.card." + (41 + kind)];
                var unit = fixture.Units[1] with { CommanderId = "general", RemainingMovement = 6,
                    Combatant = new() { Profile = profile.Profile, Health = profile.Profile.MaxHealth,
                        Skills = skilled ? [Ew4Combat.Skill(skills[kind])] : [] }, Support = new() { FoodConsumption = profile.FoodConsumption } };
                var world = fixture with { Units = [unit], Commanders = [new() { Id = "general", Owner = "winner" }],
                    Factions = fixture.Factions.Select(f => f.Id == "winner" ? f with { Resources = new(1000, 1000, 1000) } : f).ToArray() };
                byte[] before = WorldSave.Encode(world);
                var next = WorldSupportActions.BuildInstallation(world, world.Revision, unit.Id, installation).State;
                var funds = next.Factions.Single(f => f.Id == "winner").Resources;
                var expected = cards.Cards[41 + kind].Cost;
                Check(next.Cells[8].InstallationKind == LegacyRecruitmentCatalog.InstallationKinds[kind]
                    && funds == new Resources(1000 - (skilled ? expected.Money / 2 : expected.Money), 1000 - expected.Industry, 1000), "actual installation card price and skill discount");
                Check(next.Units.Single().Acted && next.Units.Single().RemainingMovement == 0 && before.SequenceEqual(WorldSave.Encode(world)), "installation commits action without mutating input");
                Check(CombatContext.Resolve(next, next.Units.Single(), "infantry", combat.Context).Evasion > 0, "installed named object immediately feeds imported defense");
                var sailed = WorldSupportActions.BuyTransport(next, next.Revision, unit.Id, transport).State;
                Check(sailed.Units.Single().Transport && sailed.Units.Single().Acted
                    && sailed.Factions.Single(f => f.Id == "winner").Resources.Money == funds.Money - 40, "actual transport card works after construction exhausted action");
            }
        for (int level = 0; level < 5; level++)
        {
            var commander = new CommanderRuntime { Id = "trainer", Owner = "winner", MaximumTrainingLevel = 5 };
            var unit = fixture.Units[1] with { CommanderId = commander.Id, Veterancy = level.ToString(CultureInfo.InvariantCulture), Experience = 17,
                Combatant = new() { Profile = profile.Profile, Health = 1 }, Support = new() { FoodConsumption = profile.FoodConsumption } };
            var world = fixture with { Units = [unit], Commanders = [commander], Factions = fixture.Factions.Select(f => f.Id == "winner" ? f with { Resources = new(1000, 1000, 1000) } : f).ToArray() };
            var next = WorldImprovements.Train(world, world.Revision, unit.Id, combat.Training).State;
            Check(next.Units.Single().Veterancy == (level + 1).ToString(CultureInfo.InvariantCulture) && next.Units.Single().Experience == 17
                && next.Units.Single().Combatant.Health == 1 + combat.Veterancy[level + 1].PromotionHealing, "actual table training preserves XP and applies destination healing");
            Check(next.Factions.Single(f => f.Id == "winner").Resources == new Resources(1000 - combat.Training.Stages[level].MoneyToNext!.Value, 1000,
                1000 - profile.FoodConsumption * 3), "actual table training price and unit upkeep food cost");
        }
        var source = LegacyBattle.Read(File.ReadAllBytes(Path.Combine(assets, "campaign1_01.btl"))).Factions[0] with
            { Variant = "fra", Values68 = new int[26], Resources = new(300, 200, 300), State176 = 0, SurvivalMode = 2, Ai = true };
        var actor = LegacyFactionRules.Import(source, "winner", "test", "b", cards, runtime);
        var playable = fixture with { Factions = [fixture.Factions[0], actor],
            Cells = fixture.Cells.Select((c, i) => c with { BuildingKind = i == 8 ? "city" : null, BuildingLevel = i == 8 ? 4 : 0 }).ToArray(),
            Units = fixture.Units.Select(u => u with { RemainingMovement = profile.Profile.Movement,
                Combatant = new() { Profile = profile.Profile, Health = profile.Profile.MaxHealth }, Support = new() { FoodConsumption = profile.FoodConsumption } }).ToArray() };
        var productionRules = LegacyAiCatalog.Production("fra", runtime, cards, buildings);
        var turnRules = combat.TurnRules(buildings.TurnRules(LegacyBattle.Read(File.ReadAllBytes(Path.Combine(assets, "campaign1_01.btl"))))) with
            { SiteIncomeOverrides = new Dictionary<int, BuildingIncomeOverride>() };
        int steps = 0;
        for (; steps < 100 && playable.ActiveFaction == "winner" && playable.Phase != TurnPhase.Finished; steps++)
        {
            byte[] before = WorldSave.Encode(playable);
            var plan = WorldAi.PlanStep(playable, ai, productionRules, turnRules);
            Check(before.SequenceEqual(WorldSave.Encode(playable)), "integrated named-rule AI planning preserves snapshot");
            var next = WorldAi.ExecuteStep(playable, plan, ai, productionRules, turnRules, combat.BattleRules()).State;
            Check(next.Revision == playable.Revision + 1, "integrated imported AI step has one revision");
            playable = WorldSave.Decode(WorldSave.Encode(next), next.ContentRevision);
        }
        Check(steps > 0 && steps < 100, "bounded imported-rule AI faction turn reaches handoff");
        var report = new { variants, namedTacticalChecks, integratedFactionTurnSteps = steps, checks,
            limitations = new[] { "Named rule/card adapters and one small synthetic AI faction turn; not full imported scenario startup or performance acceptance.",
                "Tavern/metagame/technology/deployment capacity, scenario AI records and complete rule fingerprint remain unbound." } };
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_actions_ai.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
