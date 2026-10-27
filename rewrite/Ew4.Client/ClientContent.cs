using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;

namespace Ew4.Client;

public sealed record ScenarioChoice(string Id, string Name, int Mode);
public sealed record CampaignChapter(int Id, string Name, string Start, string End, string[] Countries);
public sealed record ScenarioPresentation(string Id, string Description, string? Commander, string Age,
    int Chapter, string? Alternate, string[] Countries, bool Hidden = false);

public sealed class ClientContent
{
    private readonly Func<string, byte[]> read;
    public LegacyScenarioCatalogs Catalogs { get; }
    public ExtensionLoader Extensions { get; }
    public ArmyCapacityConfiguration ArmyCapacity { get; }
    public CountryCatalog Countries { get; }
    public IReadOnlyDictionary<int, LegacyTerrainMap> Maps { get; }
    public IReadOnlyDictionary<int, string> MapNames { get; }
    public IReadOnlyDictionary<int, string> Cities { get; }
    public IReadOnlyDictionary<string, string> Strings { get; }
    public IReadOnlyList<ScenarioChoice> Scenarios { get; }
    public IReadOnlyList<CampaignChapter> Chapters { get; }
    public IReadOnlyDictionary<string, ScenarioPresentation> Presentations { get; }
    public IReadOnlyList<XElement> ItemDefinitions { get; }
    public IReadOnlyList<string> TechnologyUnitNames { get; }
    public IReadOnlyList<XElement> PrincessDefinitions { get; }
    public bool CorrectOriginalEquipment { get; }
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> scenarioCityNames;

    public ClientContent(Func<string, byte[]> read, IEnumerable<string> battleFiles, bool correctOriginalEquipment = true, bool showHiddenCampaigns = true)
    {
        var definitionsBundle = LegacyCatalogFiles.Load(read);
        var originalRead = read;
        bool correctItems = correctOriginalEquipment && definitionsBundle.ItemsMatchSource(read);
        if (definitionsBundle.IsSplit)
            read = path => path.StartsWith("original/", StringComparison.Ordinal) && definitionsBundle.Xml.TryGetValue(path[9..], out string? value)
                ? System.Text.Encoding.UTF8.GetBytes(value) : originalRead(path);
        this.read = originalRead;
        string Text(string name)
        {
            using var reader = new StreamReader(new MemoryStream(read("original/" + name)), System.Text.Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        Countries = CountryCatalog.Load(read);
        Extensions = ExtensionLoader.Load(read, Countries);
        ArmyCapacity = ArmyCapacityConfiguration.Read(read);
        var runtime = LegacyRuntimeCatalog.FromJson(Extensions.PatchCatalog(definitionsBundle.Catalog.ToJsonString()),
            correctItems, Extensions.LegacySkills, Extensions.ConfigureItem);
        CorrectOriginalEquipment = correctItems && LegacyEquipmentCorrections.IsOriginal(runtime.SourceHashes["def_item.xml"]);
        Extensions.ConfigureRuntime(runtime);
        runtime.VerifySources(name => originalRead("original/" + name));
        Countries.ValidateRuntime(runtime);
        var buildings = LegacyBuildingCatalog.Read(Text("def_construction.xml"), Text("def_capital.xml"));
        var combat = LegacyCombatCatalog.Read(definitionsBundle.RuntimeRules.ToJsonString(),
            Text("def_terraintype.xml"), Text("def_installation.xml"), buildings, Extensions.LegacySkills);
        var accounts = LegacyCommanderAccounts.Read(runtime, combat,
            definitionsBundle.MetagameRules.ToJsonString());
        Catalogs = new(runtime, LegacyTerrainRules.FromXml(Text("def_mapterrain.xml"), Text("def_terraintype.xml")), buildings,
            LegacyRecruitmentCatalog.Read(Text("def_card.xml")), combat, accounts, LegacyItemTradingCatalog.Read(Text("def_item.xml"), correctItems, runtime.Items),
            LegacyWarzoneTechnology.Read(Text("def_warzonetech.xml")), LegacyDialogueCatalog.Read(Text("def_dialogues.xml"), Text("stringtable_cn.ini")));
        TechnologyUnitNames = XDocument.Parse(Text("def_army.xml")).Root!.Elements("country").First().Elements("army")
            .Where(a => (int?)a.Attribute("grade") == 0).Select(a => (string)a.Attribute("name")!).ToArray();
        var definitions = XDocument.Parse(Text("def_map.xml")).Root!.Elements("map").ToArray();
        Maps = definitions.ToDictionary(e => (int)e.Attribute("id")!, e => LegacyTerrainMap.Read(read("original/" + (string)e.Attribute("file")!)));
        MapNames = definitions.ToDictionary(e => (int)e.Attribute("id")!, e => (string)e.Attribute("name")!);
        Extensions.Geography.Validate(Maps, MapNames, id => read("original/" +
            (string)definitions.Single(e => (int)e.Attribute("id")! == id).Attribute("file")!));
        Cities = XDocument.Parse(Text("def_areaname.xml")).Root!.Elements("areaname")
            .ToDictionary(e => (int)e.Attribute("id")!, e => (string)e.Attribute("name")!);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in Text("stringtable_cn.ini").Split('\n'))
        {
            int separator = line.IndexOf('=');
            if (separator > 0 && !line.TrimStart().StartsWith(';')) strings[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        if (definitionsBundle.IsSplit)
            foreach (var skill in definitionsBundle.Catalog["skills"]!.AsArray())
            {
                if (skill!["legacy_id"] is not { } legacyId) continue;
                string suffix = ((int)legacyId + 1).ToString("00");
                strings["name_skill_" + suffix] = (string)skill["names"]!["zh_CN"]!;
                strings["desc_skill_" + suffix] = (string)skill["legacy_description"]!;
            }
        Strings = strings;
        ItemDefinitions = XDocument.Parse(Text("def_item.xml")).Root!.Elements("item").ToArray();
        PrincessDefinitions = XDocument.Parse(Text("def_princess.xml")).Root!.Elements("Princess").ToArray();
        var battleEntries = XDocument.Parse(Text("def_battlelist.xml")).Root!.Elements("battle").ToArray();
        bool Hidden(string id, bool original)
        {
            string visibility = Extensions.CampaignVisibility.GetValueOrDefault(id)
                ?? Extensions.CampaignVisibility.GetValueOrDefault("*") ?? (showHiddenCampaigns ? "visible" : "original");
            return visibility == "hidden" || visibility == "original" && original;
        }
        var namesByScenario = new Dictionary<string, IReadOnlyDictionary<int, string>>();
        foreach (var entry in battleEntries.Where(e => e.Attribute("citynames") != null))
        {
            string file = (string)entry.Attribute("citynames")!;
            if (file.Contains('/') || file.Contains('\\') || file.Contains(':') || Path.GetExtension(file) != ".xml")
                throw new RuleViolation("client.city_names", "City name files must be sibling XML resources");
            var names = ScenarioCityNames.Read(Text(file), Cities);
            namesByScenario.Add(Path.GetFileNameWithoutExtension((string)entry.Attribute("file")!), names);
            if (entry.Attribute("file2") is XAttribute alternate)
                namesByScenario.Add(Path.GetFileNameWithoutExtension(alternate.Value), names);
        }
        scenarioCityNames = namesByScenario;
        var battleNames = battleEntries
            .ToDictionary(e => Path.GetFileNameWithoutExtension((string)e.Attribute("file")!), e => (string)e.Attribute("name")!);
        Presentations = battleEntries.Select(e =>
        {
            string id = Path.GetFileNameWithoutExtension((string)e.Attribute("file")!);
            string key = (string)e.Attribute("name")!;
            int chapter = id.StartsWith("campaign", StringComparison.Ordinal) ? int.Parse(id[8..id.IndexOf('_')]) : 0;
            return new ScenarioPresentation(id, Label("desc_" + key), (string?)e.Attribute("commander"),
                (string?)e.Attribute("age") ?? "", chapter, e.Attribute("file2") == null ? null : Path.GetFileNameWithoutExtension((string)e.Attribute("file2")!),
                ((string?)e.Attribute("countries") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries), Hidden(id, (string?)e.Attribute("hide") == "1"));
        }).ToDictionary(p => p.Id);
        if (Extensions.CampaignVisibility.Keys.Any(id => id != "*" && (!Presentations.TryGetValue(id, out var p) || p.Chapter == 0)))
            throw new RuleViolation("extension.campaign_visibility", "Visibility requires a campaign entry ID, not an alternate BTL ID");
        Chapters = XDocument.Parse(Text("def_battleline.xml")).Root!.Elements("line").Select((e, i) => new CampaignChapter(i + 1,
            Label((string)e.Attribute("name")!), (string)e.Attribute("start")!, (string)e.Attribute("end")!,
            ((string)e.Attribute("countries")!).Split(','))).ToArray();
        foreach (var presentation in Presentations.Values.Where(p => p.Alternate != null))
            battleNames[presentation.Alternate!] = battleNames[presentation.Id];
        string ScenarioName(string id) => id.StartsWith("conquest", StringComparison.Ordinal) ? Label("text_conquest_age_" + id[8..])
            : id == "tutorials1" ? Label("btn_basic") : id == "tutorials2" ? Label("btn_classic")
            : battleNames.TryGetValue(id, out string? key) ? Label("title_" + key) : id;
        Scenarios = battleFiles.Select(Path.GetFileNameWithoutExtension).Where(n => n != null)
            .Select(n => new ScenarioChoice(n!, ScenarioName(n!), n!.StartsWith("conquest", StringComparison.Ordinal) ? 2
                : n.StartsWith("tutorial", StringComparison.Ordinal) ? 4 : n.StartsWith("multiplay", StringComparison.Ordinal) ? 3 : 1)).OrderBy(s => s.Mode).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (Extensions.Victories.Keys.Any(id => !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("extension.victory_scenario", "Victory rules reference an unknown scenario");
        if (Extensions.VictoryPresentations.Keys.Any(id => id != "*" && !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("extension.victory_presentation", "Victory screen references an unknown scenario");
        if (Extensions.ScenarioEvents.Keys.Any(id => !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("scenario.reference", "Event rules reference an unknown scenario");
        if (Extensions.DeploymentLimits.Keys.Any(id => id != "*" && !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("extension.deployment_limit", "Deployment limits reference an unknown scenario");
        if (Extensions.StageIntroPresentations.Keys.Any(id => id != "*" && !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("extension.stage_intro", "Stage intro references an unknown scenario");
        if (Extensions.PauseObjectivesPresentations.Keys.Any(id => id != "*" && !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("extension.pause_objectives", "Pause objectives references an unknown scenario");
        if (Extensions.MapColors.Keys.Any(id => id != "*" && !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("extension.map_colors", "Map colors reference an unknown scenario");
        if (ArmyCapacity.FactionBases.Keys.Any(id => id != "*" && !Scenarios.Any(s => s.Id == id)))
            throw new RuleViolation("army.capacity_configuration", "Army capacity references an unknown scenario");
        if (Extensions.Metagame.ShopSlots?.Any(s => s.Items?.Any(id => !Catalogs.Items.Rules().Definitions.ContainsKey(id)
                || s.Pool == "consumable" && !Catalogs.Items.Rules().Definitions[id].Equipment.Consumable
                || s.Pool == "equipment" && Catalogs.Items.Rules().Definitions[id].Equipment.Consumable) == true) == true)
            throw new RuleViolation("metagame.item", "Shop slot references an unknown item or mismatched pool");
        foreach (var objective in Extensions.Metagame.Objectives)
        {
            if (objective.Reward.Items.Keys.Any(id => !Catalogs.Items.Rules().Definitions.ContainsKey(id))
                || objective.Reward.Commanders.Any(id => !Catalogs.Accounts.Rules().Definitions.ContainsKey(id)))
                throw new RuleViolation("objectives.reference", "Unknown reward definition: " + objective.Id);
            var scenario = objective.Scenario == null ? null : Scenarios.SingleOrDefault(s => s.Id == objective.Scenario);
            if (objective.Scenario != null && (scenario == null || scenario.Mode is not (1 or 2) || objective.Mode.HasValue && scenario.Mode != objective.Mode)
                || objective.Country != null && (Countries.Get(objective.Country) == null || Countries.Canonical(objective.Country) != objective.Country))
                throw new RuleViolation("objectives.reference", "Unknown scenario or country: " + objective.Id);
            foreach (var condition in objective.Conditions)
            {
                if (condition.Type == "commanderOwned" && condition.Id != null && !Catalogs.Accounts.Rules().Definitions.ContainsKey(condition.Id)
                    || condition.Type == "itemOwned" && condition.Id != null && !Catalogs.Items.Rules().Definitions.ContainsKey(condition.Id)
                    || condition.Type is "campaignUnlocked" or "campaignStars" && condition.Id != null && !Presentations.ContainsKey(condition.Id)
                    || condition.Type == "cellControlled" && condition.Cell >= Maps[Battle(objective.Scenario!).MapId].Cells.Count)
                    throw new RuleViolation("objectives.reference", "Unknown condition reference: " + objective.Id);
            }
        }
    }

    public string Label(string key) => Strings.TryGetValue(key, out string? label) ? label : Strings.TryGetValue("name_" + key, out label) ? label : key;
    public string CountryName(string id) => Countries.Get(id)?.Name ?? Label(id);
    public CountryDefinition? Country(string id) => Countries.Get(id);
    internal ClientContent WithoutEquipmentCorrections() => new(read, Scenarios.Select(s => s.Id + ".btl"), false);
    public LegacyBattle Battle(string id)
    {
        if (!Scenarios.Any(s => s.Id == id)) throw new RuleViolation("client.scenario", "Unknown scenario");
        var battle = LegacyBattle.Read(read("original/" + id + ".btl"));
        if (battle.CountryIds != null && battle.Factions.Any(f => Country(f.CountryId) == null))
            throw new RuleViolation("country.reference", "CNTR references an unregistered global country in " + id);
        return battle;
    }
    public LegacyScenarioStart Start(string id, string? country, PlayerAccount? account = null, IReadOnlyList<string>? localPlayers = null,
        IReadOnlyList<int>? technologySnapshot = null)
    {
        var choice = Scenarios.Single(s => s.Id == id);
        if (choice.Mode == 3) throw new RuleViolation("client.mode", "Multiplayer requires a multiplayer session host");
        account ??= new PlayerAccount { Id = "local", ContentRevision = Catalogs.Accounts.SourceRevision, Medals = 20_000_000 };
        var battle = Battle(id);
        // The UI uses canonical registry IDs while legacy conquest records still
        // carry their fixed-width source IDs.
        string? selectedCountry = country == null ? null
            : battle.Factions.FirstOrDefault(f => Countries.Canonical(f.CountryId) == country)?.CountryId ?? country;
        if (battle.Jurisdictions != null && choice.Mode is not (1 or 2))
            throw new RuleViolation("client.jurisdiction_mode", "手工辖区仅支持战役与征服剧本");
        var baseMap = LegacyMapDefinitions.Import(Maps[battle.MapId], Catalogs.Terrain,
            "legacy.map." + battle.MapId, "Legacy map " + battle.MapId);
        baseMap = Extensions.Geography.Apply(MapNames[battle.MapId], baseMap, Catalogs.Terrain.MovementCosts["mountain"]);
        int warzone = Presentations.Values.FirstOrDefault(p => p.Id == id || p.Alternate == id)?.Chapter is int chapter && chapter > 0 ? chapter - 1 : 0;
        var started = LegacyScenarioHost.Start(battle, baseMap, Catalogs, new LegacyScenarioRequest
        {
            Id = id, Mode = choice.Mode, SelectedCountry = selectedCountry, LocalPlayers = localPlayers,
            CityNames = scenarioCityNames.TryGetValue(id, out var names) ? names : Cities,
            Warzone = warzone,
            TechnologySnapshot = choice.Mode == 1 ? technologySnapshot ?? Catalogs.Technology.Snapshot(account, warzone) : null,
            CountryNames = battle.Factions.Select(f => f.CountryId).Distinct().ToDictionary(c => c, CountryName),
            CountryUnitVariants = battle.Factions.Where(f => Country(f.CountryId)?.UnitVariant is string variant && variant != f.Variant
                && !(variant == "others" && !Catalogs.Runtime.Variants.ContainsKey(f.Variant)))
                .GroupBy(f => f.CountryId).ToDictionary(g => g.Key, g => Country(g.Key)!.UnitVariant!),
            CombatSeed = choice.Mode == 4 ? 100 : (ulong)Random.Shared.NextInt64(1, long.MaxValue), AiSeed = (ulong)Random.Shared.NextInt64(1, long.MaxValue)
        }, account);
        if (Extensions.DeploymentLimits.TryGetValue(id, out int? limit) || Extensions.DeploymentLimits.TryGetValue("*", out limit))
            started = started with { Session = started.Session with {
                RosterDeployments = started.Session.RosterDeployments.Select(q => q with { Limit = limit }).ToArray() } };
        // Apply playable rules after import so existing scenario/save fingerprints stay compatible.
        started = started with { Rules = started.Rules with { Battle = started.Rules.Battle with {
            CollectMedals = choice.Mode is 1 or 2 && localPlayers == null,
            Combat = started.Rules.Battle.Combat with { LethalStrikePreventsCounterattack = true } } } };
        if (choice.Mode is 1 or 2)
        {
            var world = started.Session.World;
            started = started with { Session = started.Session with { World = world with {
                Jurisdictions = battle.Jurisdictions?.Apply(world) ?? TerritorialJurisdictions.Create(world) } } };
        }
        var customVictory = battle.Victory ?? Extensions.Victories.GetValueOrDefault(id);
        if (Extensions.Enabled || customVictory != null)
        {
            var rules = started.Rules;
            var victory = rules.Turns.Victory with { Custom = customVictory, Scripts = Extensions.Bus.Scripts };
            if (customVictory != null)
            {
                ValidateVictoryTargets(customVictory, started.Session.World);
                Extensions.ValidateVictoryScripts(customVictory);
            }
            var movement = rules.Movement with { Extensions = Extensions.Bus, Victory = victory };
            var turns = rules.Turns with { Extensions = Extensions.Bus, Victory = victory };
            var combat = rules.Battle with { Combat = rules.Battle.Combat with { Extensions = Extensions.Bus }, Victory = victory };
            started = started with { Rules = rules with { Movement = movement, Turns = turns, Battle = combat,
                Commanders = rules.Commanders with { Extensions = Extensions.Bus },
                Roster = rules.Roster with { Assignment = rules.Roster.Assignment with { Extensions = Extensions.Bus } },
                Tavern = rules.Tavern with { Assignment = rules.Tavern.Assignment with { Extensions = Extensions.Bus } },
                Production = rules.Production.ToDictionary(p => p.Key, p => p.Value with {
                    Commanders = p.Value.Commanders == null ? null : p.Value.Commanders with { Extensions = Extensions.Bus } }),
                Ai = rules.Ai with { Movement = movement } } };
            if (Extensions.Enabled)
            {
                var world = started.Session.World;
                string revision = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(world.ContentRevision + ":" + Extensions.Revision)));
                world = world with { ContentRevision = revision, ScenarioPlayerFaction = started.Session.PlayerFaction, DiplomacyNeutralCamps = turns.NeutralCamps.ToArray(),
                    Story = Extensions.Stories == null ? null : new StoryState { Library = Extensions.Stories },
                    ScenarioEvents = (Extensions.ScenarioEvents.GetValueOrDefault(id) ?? [])
                        .Select(e => ScenarioFactions.BindTemplates(e, started.Session.PlayerFaction)).ToArray() };
                world = world with { Units = world.Units.Select(unit => {
                    int budget = Extensions.Bus.MovementBudget(unit.Combatant);
                    return unit with { ExtensionMovementLimit = budget, RemainingMovement = unit.RemainingMovement == unit.Combatant.Profile.Movement
                        ? budget : Math.Min(unit.RemainingMovement, budget) };
                }).ToArray() };
                ScenarioRules.Validate(world, initial: true);
                var production = started.Rules.Production.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                var recruitment = started.Rules.Recruitment.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                var grants = new Dictionary<string, IReadOnlyDictionary<string, RecruitmentGrant>>();
                foreach (var creation in ScenarioFactions.Creations(world.ScenarioEvents))
                {
                    string? template = ScenarioFactions.Template(world, creation);
                    WorldProductionRules policy;
                    if (creation.UnitVariant == null && template != null) policy = production[template];
                    else
                    {
                        string variant = creation.UnitVariant ?? "others";
                        if (!Catalogs.Runtime.Variants.ContainsKey(variant)) throw new RuleViolation("scenario.unit_variant", "Unknown unit variant: " + variant);
                        policy = LegacyAiCatalog.Production(variant, Catalogs.Runtime, Catalogs.Cards, Catalogs.Buildings) with { Commanders = started.Rules.Commanders };
                    }
                    production.Add(creation.Faction, policy);
                    var offers = policy.Offers.Concat(policy.Fortresses).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                    recruitment.Add(creation.Faction, offers);
                    var available = creation.Recruitment ?? (creation.UnitVariant == null && template != null
                        ? world.Factions.Single(f => f.Id == template).Recruitment : offers.ToDictionary(p => p.Key, _ => new RecruitmentGrant()));
                    if (available.Keys.Any(key => !offers.ContainsKey(key))) throw new RuleViolation("scenario.recruitment", "Unknown recruitment card for " + creation.Faction);
                    grants.Add(creation.Faction, available);
                }
                ScenarioEffect Prepare(ScenarioEffect effect) => effect.Type == "faction_create" ? effect with { Recruitment = grants[effect.Faction] } : effect;
                world = world with { ScenarioEvents = world.ScenarioEvents.Select(ev => ev with {
                    Effects = ev.Effects.Select(Prepare).ToArray(), InsufficientResourcesEffects = ev.InsufficientResourcesEffects.Select(Prepare).ToArray(),
                    Popup = ev.Popup == null ? null : ev.Popup with { Choices = ev.Popup.Choices.Select(c => c with { Effects = c.Effects.Select(Prepare).ToArray() }).ToArray() }
                }).ToArray() };
                started = started with { Rules = started.Rules with { Production = production, Recruitment = recruitment } };
                foreach (string unit in world.Units.Select(u => u.Id).ToArray())
                    world = Extensions.Bus.Dispatch(world, "scenario.start", unit, new Ew4Random(world.RandomState));
                world.Validate();
                started = started with { Session = started.Session with { World = world } };
            }
            if (Extensions.Stories?.Bindings.GetValueOrDefault(id)?.OpeningStory is { } opening)
            {
                var world = started.Session.World;
                string? pending = world.PendingDialogue;
                var queue = world.QueuedDialogues;
                world = StoryPlayback.Enqueue(world with { PendingDialogue = null, QueuedDialogues = [] }, opening.Key, opening.Scene, "opening");
                world = world with { QueuedDialogues = (pending == null ? Array.Empty<string>() : new[] { pending }).Concat(queue).ToArray() };
                started = started with { Session = started.Session with { World = world } };
            }
            var scenarioChange = ScenarioRules.Evaluate(started.Session.World, Extensions.Bus.Scripts);
            started = started with { Session = SessionActions.SyncPlayer(started.Session with { World = scenarioChange.State }),
                Notices = started.Notices.Concat(scenarioChange.Notices).ToArray() };
            if ((customVictory != null || started.Session.World.ScenarioVictory != null) && started.Session.World.PendingDialogue == null && started.Session.World.Outcome == BattleOutcome.Continue)
            {
                var world = started.Session.World;
                var outcome = WorldVictory.Evaluate(world, turns.PlayerCamp!, victory, turns.NeutralCamps);
                if (outcome != BattleOutcome.Continue)
                    started = started with { Session = started.Session with { World = world with { Outcome = outcome, Phase = TurnPhase.Finished } } };
            }
        }
        if (localPlayers != null)
            started = started with { Session = SessionActions.SyncPlayer(started.Session with {
                World = LocalMultiplayerGame.Resolve(started.Session.World, started.Rules.Turns.Victory) }) };
        // Apply the current capacity policy after content fingerprints, including when upgrading older saves.
        var capacityCountries = battle.Factions.Select((f, i) => new {
            Id = LegacyScenarioInitialization.FactionId(id, i, f), Country = Countries.Canonical(f.CountryId)
        }).ToDictionary(f => f.Id, f => f.Country, StringComparer.Ordinal);
        foreach (var creation in ScenarioFactions.Creations(started.Session.World.ScenarioEvents))
            capacityCountries[creation.Faction] = creation.Country ?? (ScenarioFactions.Template(started.Session.World, creation) is string template
                ? capacityCountries.GetValueOrDefault(template) ?? creation.Faction : creation.Faction);
        var capacities = ArmyCapacity.Resolve(id, capacityCountries);
        var capacityWorld = started.Session.World with { ArmyCapacity = capacities };
        foreach (var cell in capacityWorld.Cells) WorldArmyCapacity.BuildingCapacity(capacityWorld, cell);
        started = started with { Session = started.Session with { World = capacityWorld } };
        return started;
    }
    private static void ValidateVictoryTargets(ExtensionVictoryRules rules, WorldState world)
    {
        void Check(VictoryCondition c)
        {
            if (c.Cells.Any(i => i < 0 || i >= world.Cells.Count || !world.Cells[i].Terrain.Active)
                || c.Factions.Any(id => world.Factions.All(f => f.Id != id)) || c.Units.Any(id => world.Units.All(u => u.Id != id)))
                throw new RuleViolation("extension.victory_reference", "Victory condition references an unknown initial unit/faction/cell");
            foreach (var child in c.Conditions) Check(child);
        }
        Check(rules.Win); if (rules.Lose != null) Check(rules.Lose);
    }
}
