using System.Text.Json;
using Ew4.Core;

if (args.ElementAtOrDefault(1) == "--map-colors-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { mapColorChecks = MapColorChecks.Run(Path.GetFullPath(args[0])),
        factionChecks = ScenarioFactionChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--pause-objectives-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { pauseObjectivesChecks = PauseObjectivesChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--objectives-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { objectiveChecks = ObjectiveChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--auto-jurisdiction-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { autoJurisdictionChecks = AutoJurisdictionChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--garrison-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { garrisonChecks = CustomMechanicsChecks.GarrisonChecks(),
        controlledMovementChecks = ControlledMovementChecks.Run(), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--geography-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { geographyChecks = GeographyChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--catalog-files-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { catalogFileChecks = CatalogFileChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--r-feedback-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { rFeedbackChecks = RFeedbackChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--performance-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { performanceContracts = PerformanceContractChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--campaign-only")
{
    string campaignRoot = Path.GetFullPath(args[0]);
    Console.WriteLine(JsonSerializer.Serialize(new { campaignVisibilityChecks = CampaignVisibilityChecks.Run(campaignRoot),
        clientChecks = ClientChecks.Run(campaignRoot), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--turn-performance")
{
    TurnPerformanceChecks.Run(Path.GetFullPath(args[0]), args.ElementAtOrDefault(2) ?? "sample"); return;
}

if (args.ElementAtOrDefault(1) == "--medal-drops-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { medalDropChecks = MedalDropChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--feedback-undo-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { feedbackUndoChecks = FeedbackUndoChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--feedback-configuration-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { feedbackConfigurationChecks = FeedbackConfigurationChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--naval-deployment-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { navalDeploymentChecks = NavalDeploymentChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--deployment-limits-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { deploymentLimitChecks = DeploymentLimitChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--item-extensions-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { itemExtensionChecks = ItemExtensionChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}
if (args.ElementAtOrDefault(1) == "--headquarters-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { headquartersChecks = HeadquartersChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}
if (args.Length is not (1 or 2) || args.Length == 2 && args[1] is not ("--startup-only" or "--client-only" or "--mechanics-only" or "--jurisdiction-only" or "--extension-only" or "--scenario-only" or "--settlement-only" or "--equipment-only" or "--local-only" or "--provocations-only" or "--consumables-only" or "--single-actions-only" or "--legacy-skills-only" or "--dialogues-only"))
    throw new ArgumentException("Usage: Ew4.Verify <project root> [--startup-only|--client-only|--mechanics-only|--jurisdiction-only]");
string root = Path.GetFullPath(args[0]);
if (args.Length == 2 && args[1] == "--dialogues-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { commanderDialogueChecks = CommanderDialogueChecks.Run(root),
        battleCommitChecks = BattleCommitChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--legacy-skills-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { legacySkillParameterChecks = LegacySkillParameterChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--single-actions-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { singleActionChecks = SingleActionChecks.Run(root),
        improvementChecks = ImprovementChecks.Run(root), supportActionChecks = SupportActionChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--consumables-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { consumableChecks = ConsumableChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--local-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { localMultiplayerChecks = LocalMultiplayerChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--provocations-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { provocationChecks = ProvocationChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--equipment-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { equipmentChecks = EquipmentDescriptionChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--settlement-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { settlementChecks = BattleSettlementChecks.Run(root), victoryModChecks = VictoryModChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--scenario-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { scenarioRuleChecks = ScenarioRuleChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--extension-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { extensionChecks = ExtensionChecks.Run(root), commonSkillChecks = CommonSkillChecks.Run(root), commanderDialogueChecks = CommanderDialogueChecks.Run(root), deploymentLimitChecks = DeploymentLimitChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--jurisdiction-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { jurisdictionChecks = JurisdictionExtensionChecks.Run(root),
        autoJurisdictionChecks = AutoJurisdictionChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--mechanics-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { mechanicsChecks = CustomMechanicsChecks.Run(root), errors = 0 })); return;
}
if (args.Length == 2 && args[1] == "--client-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { clientChecks = ClientChecks.Run(root), navalDeploymentChecks = NavalDeploymentChecks.Run(root), errors = 0 }));
    return;
}
if (args.Length == 2)
{
    Console.WriteLine(JsonSerializer.Serialize(new { legacyStartupChecks = LegacyStartupChecks.Run(root),
        worldTurnEntryChecks = WorldTurnEntryChecks.Run(root), mapCatalogChecks = MapCatalogChecks.Run(root), legacyHostChecks = LegacyHostChecks.Run(root), errors = 0 }));
    return;
}
using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/combat_vectors.json")));
float[] multipliers = vectors.RootElement.GetProperty("multipliers").EnumerateArray().Select(v => v.GetSingle()).ToArray();
string[] categories = ["infantry", "cavalry", "artillery", "naval", "fort"];
int[] thresholds = [5, 15, 30, 50, 65, 80];

Combatant Decode(JsonElement data)
{
    int Int(string key) => data.GetProperty(key).GetInt32();
    int unitId = Int("unit_id"), grade = Int("grade"), weapon = Int("weapon");
    var traits = new List<string>();
    if (unitId == 12) traits.Add("siege_gun");
    if (unitId == 13) traits.Add("no_retaliation");
    if (unitId == 21) traits.Add("coastal_gun");
    var combatant = new Combatant
    {
        Profile = new UnitProfile
        {
            Id = "legacy.unit." + unitId, Category = categories[Int("category")], Grade = grade.ToString(),
            MaxHealth = Int("maximum"), MinimumAttack = Int("minimum_attack"), MaximumAttack = Int("maximum_attack"),
            MinimumRange = Int("minimum_range"), MaximumRange = Int("maximum_range"),
            AttackBands = thresholds.Take(grade + 4).Select((t, i) => new AttackBand(t, i + 2)).ToArray(),
            DamageMultipliers = categories.Select((c, i) => (c, multipliers[weapon * 5 + i])).ToDictionary(p => p.c, p => p.Item2),
            Traits = traits
        },
        Health = Int("health"), Morale = Int("morale"), ConstructionTurns = Int("construction_turns"),
        Skills = data.GetProperty("skills").EnumerateArray().Select(v => Ew4Combat.Skill(v.GetInt32())).ToArray(),
        Stars = Int("stars"), AttackBonus = Int("attack_bonus"), DefenseBonus = Int("defense_bonus"), Armor = Int("armor"),
        AtSea = data.GetProperty("sea").GetBoolean(), Evasion = data.GetProperty("evasion").GetSingle(),
        BuildingKind = Int("building") < 0 ? null : Int("building") == 4 ? "farmland" : "city"
    };
    var commander = new Commander
    {
        Id = "fixture.general", Skills = combatant.Skills,
        Stars = new Dictionary<string, int> { [combatant.Profile.Category] = combatant.Stars },
        Items = data.GetProperty("equipment").EnumerateArray().Select((e, i) => new Equipment("item." + i,
            Ew4Commander.Effect(e[0].GetInt32()), e[1].GetInt32(), e[2].GetInt32() < 0 ? null : categories[e[2].GetInt32()])).ToArray()
    };
    return Ew4Commander.Attach(combatant, commander);
}

int cases = 0;
using var transportVectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/transport_combat_vectors.json")));
foreach (var fixture in vectors.RootElement.GetProperty("fixtures").EnumerateArray().Concat(transportVectors.RootElement.GetProperty("fixtures").EnumerateArray()))
{
    var actual = Ew4Combat.Calculate(Decode(fixture.GetProperty("attacker")), Decode(fixture.GetProperty("defender")),
        fixture.GetProperty("distance").GetInt32(), new Ew4Random(fixture.GetProperty("seed").GetUInt64()),
        new CombatRules { LegacySiegeRetaliationBuildingType = true });
    var expected = fixture.GetProperty("expected");
    var wanted = new CombatResult(expected.GetProperty("defender_damage").GetInt32(), expected.GetProperty("attacker_damage").GetInt32(),
        expected.GetProperty("counterattack").GetBoolean(), expected.GetProperty("offensive_proc").GetBoolean(),
        expected.GetProperty("defensive_proc").GetBoolean(), expected.GetProperty("rng_state").GetUInt64());
    if (actual != wanted) throw new InvalidOperationException($"Native combat vector {cases}: {actual} != {wanted}");
    cases++;
}
int contracts = Contracts.Run(root);
int commanderChecks = CommanderChecks.Run(root);
commanderChecks += EquipmentDescriptionChecks.Run(root);
commanderChecks += ConsumableChecks.Run(root);
int animationChecks = AnimationChecks.Run(root);
int catalogChecks = CatalogChecks.Run(root);
int worldChecks = WorldChecks.Run(root);
int progressionChecks = ProgressionChecks.Run(root);
int aiChecks = AiChecks.Run(root);
int lifecycleChecks = LifecycleChecks.Run(root);
int actionChecks = ActionChecks.Run(root);
int movementCommitChecks = MovementCommitChecks.Run(root);
movementCommitChecks += RFeedbackChecks.Run(root);
int turnSettlementChecks = TurnSettlementChecks.Run(root);
int worldTurnChecks = WorldTurnChecks.Run(root);
int battleCommitChecks = BattleCommitChecks.Run(root);
int battleChainChecks = BattleChainChecks.Run(root);
int combatContextChecks = CombatContextChecks.Run(root);
int improvementChecks = ImprovementChecks.Run(root);
int aiTacticalChecks = AiTacticalChecks.Run(root);
int splashChecks = SplashChecks.Run(root);
int supportActionChecks = SupportActionChecks.Run(root);
int aiMovementChecks = AiMovementChecks.Run(root);
int aiStrategyChecks = AiStrategyChecks.Run(root);
int aiStrategySelectionChecks = AiStrategySelectionChecks.Run(root);
int aiUnitChecks = AiUnitChecks.Run(root);
int aiProductionChecks = AiProductionChecks.Run(root);
int aiOfferPoolChecks = AiOfferPoolChecks.Run(root);
int aiConstructionChecks = AiConstructionChecks.Run(root);
int aiWorldProductionChecks = AiWorldProductionChecks.Run(root);
int commanderDeploymentChecks = CommanderDeploymentChecks.Run(root);
int hazardDeathChecks = HazardDeathChecks.Run(root);
int legacyImportChecks = LegacyImportChecks.Run(root);
int legacyArmyImportChecks = LegacyArmyImportChecks.Run(root);
int legacyEventChecks = LegacyEventChecks.Run(root);
int legacyBuildingChecks = LegacyBuildingChecks.Run(root);
int legacyFactionChecks = LegacyFactionChecks.Run(root);
int legacyCombatChecks = LegacyCombatChecks.Run(root);
int legacyActionAiChecks = LegacyActionAiChecks.Run(root);
int legacyCommerceChecks = LegacyCommerceChecks.Run(root);
int sessionAccountChecks = SessionAccountChecks.Run(root);
int rosterDeploymentChecks = RosterDeploymentChecks.Run(root);
int legacyTechnologyChecks = LegacyTechnologyChecks.Run(root);
int legacyStartupChecks = LegacyStartupChecks.Run(root);
int worldTurnEntryChecks = WorldTurnEntryChecks.Run(root);
int mapCatalogChecks = MapCatalogChecks.Run(root);
int legacyHostChecks = LegacyHostChecks.Run(root);
int jurisdictionChecks = JurisdictionExtensionChecks.Run(root);
int autoJurisdictionChecks = AutoJurisdictionChecks.Run(root);
int extensionChecks = ExtensionChecks.Run(root);
extensionChecks += ItemExtensionChecks.Run(root);
extensionChecks += DeploymentLimitChecks.Run(root);
int commanderDialogueChecks = CommanderDialogueChecks.Run(root);
int commonSkillChecks = CommonSkillChecks.Run(root);
int legacySkillParameterChecks = LegacySkillParameterChecks.Run(root);
int scenarioRuleChecks = ScenarioRuleChecks.Run(root);
int mapColorChecks = MapColorChecks.Run(root);
int objectiveChecks = ObjectiveChecks.Run(root);
var report = new { combatVectors = cases, ruleChecks = contracts, commanderChecks, animationChecks, catalogChecks, worldChecks, progressionChecks, aiChecks, lifecycleChecks, actionChecks, movementCommitChecks, turnSettlementChecks, worldTurnChecks, battleCommitChecks, battleChainChecks, combatContextChecks, improvementChecks, aiTacticalChecks, splashChecks, supportActionChecks, aiMovementChecks, aiStrategyChecks, aiStrategySelectionChecks, aiUnitChecks, aiProductionChecks, aiOfferPoolChecks, aiConstructionChecks, aiWorldProductionChecks, commanderDeploymentChecks, hazardDeathChecks, legacyImportChecks, legacyArmyImportChecks, legacyEventChecks, legacyBuildingChecks, legacyFactionChecks, legacyCombatChecks, legacyActionAiChecks, legacyCommerceChecks, sessionAccountChecks, rosterDeploymentChecks, legacyTechnologyChecks, legacyStartupChecks, worldTurnEntryChecks, mapCatalogChecks, legacyHostChecks, jurisdictionChecks, errors = 0,
    objectiveChecks, autoJurisdictionChecks, extensionChecks, commanderDialogueChecks, commonSkillChecks, legacySkillParameterChecks, scenarioRuleChecks, mapColorChecks, limitations = new[] { "Headless scenario/session startup and core rules; rendering and Android require separate integration checks.",
        "Native profile migration, commander purchase/cultivation, session tavern AI, tutorial controller, merge flows, full playthroughs and display/Android integration remain incomplete." } };
Directory.CreateDirectory(Path.Combine(root, "rewrite/reports"));
File.WriteAllText(Path.Combine(root, "rewrite/reports/core_validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(JsonSerializer.Serialize(report));
