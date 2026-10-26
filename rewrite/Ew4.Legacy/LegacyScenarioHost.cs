using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record LegacyScenarioCatalogs(LegacyRuntimeCatalog Runtime, LegacyTerrainRules Terrain,
    LegacyBuildingCatalog Buildings, LegacyRecruitmentCatalog Cards, LegacyCombatCatalog Combat,
    LegacyCommanderAccounts Accounts, LegacyItemTradingCatalog Items, LegacyWarzoneTechnology Technology,
    LegacyDialogueCatalog Dialogues);

public sealed record LegacyScenarioRequest
{
    public string Id { get; init; } = "";
    public int Mode { get; init; }
    public string? SelectedCountry { get; init; }
    public IReadOnlyList<string>? LocalPlayers { get; init; }
    public int Warzone { get; init; }
    public IReadOnlyList<int>? TechnologySnapshot { get; init; }
    public ulong CombatSeed { get; init; }
    public ulong AiSeed { get; init; }
    public WorldLimits Limits { get; init; } = new();
    public IReadOnlyDictionary<int, string> CityNames { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<string, string> CountryNames { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> CountryUnitVariants { get; init; } = new Dictionary<string, string>();
    public RoundEventTiming RoundEvents { get; init; } = RoundEventTiming.AfterFaction;
    public IReadOnlyCollection<int> NeutralCamps { get; init; } = new[] { 4 };
}

public sealed record LegacyScenarioRules(WorldTurnRules Turns, WorldMoveRules Movement, WorldBattleRules Battle,
    WorldAiRules Ai, IReadOnlyDictionary<string, WorldProductionRules> Production,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, RecruitmentOffer>> Recruitment,
    WorldCommanderRules Commanders, RosterDeploymentRules Roster, TrainingRules Training,
    BuildingUpgradeRules Upgrades, IReadOnlyDictionary<string, InstallationBuildRules> Installations,
    TransportPurchaseRules Transport, TavernRules Tavern, SessionTradingRules Trading,
    IReadOnlyDictionary<string, LegacyDialogue> Dialogues, IReadOnlyList<LegacyMissingEvent> MissingEventReferences,
    int HighestRatingRound);

public sealed record LegacyScenarioStart(GameSession Session, LegacyScenarioRules Rules, IReadOnlyList<WorldNotice> Notices, WorldMapDefinition Map)
{
    public IReadOnlyList<LegacyMapDiagnostic> MapDiagnostics { get; init; } = [];
    public IReadOnlyList<int>? TechnologySnapshot { get; init; }
}

public static class LegacyScenarioHost
{
    public static LegacyScenarioStart Start(LegacyBattle battle, IReadOnlyDictionary<int, LegacyTerrainMap> maps,
        LegacyScenarioCatalogs catalogs, LegacyScenarioRequest request, PlayerAccount account)
    {
        if (!maps.TryGetValue(battle.MapId, out var map)) throw new RuleViolation("map.reference", "Scenario map is not registered");
        return Start(battle, LegacyMapDefinitions.Import(map, catalogs.Terrain, "legacy.map." + battle.MapId, "Legacy map " + battle.MapId),
            catalogs, request, account);
    }

    public static LegacyScenarioStart Start(LegacyBattle battle, WorldMapCatalog maps, IReadOnlyDictionary<int, string> legacyMapIds,
        LegacyScenarioCatalogs catalogs, LegacyScenarioRequest request, PlayerAccount account)
    {
        if (!legacyMapIds.TryGetValue(battle.MapId, out string? id)) throw new RuleViolation("map.reference", "Scenario map alias is not registered");
        return Start(battle, maps.Resolve(id), catalogs, request, account);
    }

    public static LegacyScenarioStart Start(LegacyBattle battle, WorldMapDefinition map,
        LegacyScenarioCatalogs catalogs, LegacyScenarioRequest request, PlayerAccount account)
    {
        account.Validate(); request.Limits.Validate();
        if (account.Inventory.Keys.Any(id => !catalogs.Items.Items.Values.Any(item => item.Equipment.Id == id)))
            throw new RuleViolation("legacy.start_account", "Account contains an item absent from the installed catalogs");
        foreach (var owned in account.Roster) AccountCommanders.Resolve(owned, catalogs.Accounts.Rules());
        if (string.IsNullOrWhiteSpace(request.Id) || request.Warzone < 0
            || !Enum.IsDefined(request.RoundEvents))
            throw new RuleViolation("legacy.start_request", "Missing scenario identity/map or invalid startup policy");
        // New scenarios cannot smuggle saved owned instances into a fresh deployment history.
        if (battle.Armies.Any(a => a.Flag58 == 1 && a.Commander >= 0))
            throw new RuleViolation("legacy.start_saved_roster", "Use a saved-world importer for pre-existing owned assignments");
        var initialized = LegacyScenarioInitialization.Prepare(battle, request.Mode, request.SelectedCountry,
            catalogs.Technology, request.Warzone, request.TechnologySnapshot, request.CountryUnitVariants);
        if (request.NeutralCamps.Contains(initialized.Records[initialized.PlayerIndex].Camp))
            throw new RuleViolation("legacy.start_neutral_player", "Selected player camp is configured as noncombatant; choose a combatant or change the explicit camp policy");
        string Camp(int value) => "ew4.camp." + value.ToString(CultureInfo.InvariantCulture);
        string[] ids = initialized.Records.Select((f, i) => LegacyScenarioInitialization.FactionId(request.Id, i, f)).ToArray();
        if (request.LocalPlayers != null && (request.Mode is not (1 or 2) || request.LocalPlayers.Count == 0
            || request.LocalPlayers.Distinct(StringComparer.Ordinal).Count() != request.LocalPlayers.Count
            || !request.LocalPlayers.Contains(ids[initialized.PlayerIndex]) || request.LocalPlayers.Any(id => !ids.Contains(id))))
            throw new RuleViolation("local.selection", "Local players must include the original country and use unique scenario faction IDs");
        var importedMap = LegacyScenarioMap.Import(map, battle, ids, LegacyBuildingCatalog.Kinds,
            LegacyRecruitmentCatalog.InstallationKinds, request.CityNames, catalogs.Buildings);
        var bySource = initialized.Records.Select((f, i) => LegacyFactionRules.Import(f, ids[i],
            request.CountryNames.TryGetValue(f.CountryId, out string? name) ? name : f.CountryId,
            Camp(f.Camp), catalogs.Cards, catalogs.Runtime)).ToArray();
        if (request.LocalPlayers != null)
            bySource = bySource.Select(f => f with { Human = request.LocalPlayers.Contains(f.Id),
                IncomeMultiplier = request.LocalPlayers.Contains(f.Id) ? 1 : f.IncomeMultiplier,
                AiEvasionBonus = request.LocalPlayers.Contains(f.Id) ? 0 : f.AiEvasionBonus }).ToArray();
        var bindings = new List<LegacyUnitBinding>();
        for (int i = 0; i < battle.Armies.Count; i++)
        {
            var source = battle.Armies[i];
            if (source.Cell < 0 || source.Cell >= importedMap.Cells.Count
                || importedMap.Cells[source.Cell].Owner is not string owner)
                throw new RuleViolation("legacy.start_army_owner", "Scenario army needs an active faction-owned cell");
            int index = Array.IndexOf(ids, owner);
            string unit = request.Id + ".unit." + i.ToString(CultureInfo.InvariantCulture);
            bindings.Add(LegacyUnitImporter.Import(source, unit, owner, initialized.Records[index].Variant,
                importedMap.Cells[source.Cell], catalogs.Runtime, initialized.Records[index].State176, unit + ".commander"));
        }
        var events = LegacyScenarioEvents.Import(battle, bindings, importedMap.Cells, ids, catalogs.Dialogues);
        var commerce = LegacyScenarioCommerce.Import(battle, importedMap.Cells, catalogs.Runtime, catalogs.Items,
            catalogs.Accounts, catalogs.Combat, request.Id);
        string player = ids[initialized.PlayerIndex];
        string? playerCamp = request.Mode == 4 ? null : bySource[initialized.PlayerIndex].Camp;
        string[] neutral = request.NeutralCamps.Distinct().Order().Select(Camp).ToArray();
        var turns = catalogs.Combat.TurnRules(catalogs.Buildings.TurnRules(battle)) with
            { PlayerCamp = playerCamp, Victory = initialized.Victory, NeutralCamps = neutral, RoundEvents = request.RoundEvents };
        var movement = new WorldMoveRules { PlayerCamp = playerCamp, Victory = initialized.Victory, NeutralCamps = neutral };
        var combat = catalogs.Combat.BattleRules() with { PlayerCamp = playerCamp, Victory = initialized.Victory, NeutralCamps = neutral };
        var transport = catalogs.Cards.Transport(catalogs.Combat.SkillParameters);
        var ai = LegacyAiCatalog.Rules(catalogs.Combat, movement, transport) with { NeutralCamps = neutral };
        var production = new Dictionary<string, WorldProductionRules>(StringComparer.Ordinal);
        var recruitment = new Dictionary<string, IReadOnlyDictionary<string, RecruitmentOffer>>(StringComparer.Ordinal);
        var variants = new Dictionary<string, WorldProductionRules>(StringComparer.Ordinal);
        var definitions = bindings.Where(b => b.Definition != null).ToDictionary(b => b.Definition!.Id, b => b.Definition!, StringComparer.Ordinal);
        foreach (var definition in commerce.Tavern.Assignment.Definitions) definitions.Add(definition.Key, definition.Value);
        var commanders = new WorldCommanderRules { LegacySkills = catalogs.Combat.SkillParameters, Definitions = new ReadOnlyDictionary<string, Commander>(definitions),
            TitleHealing = commerce.Tavern.Assignment.TitleHealing, DeploymentBuilding = BuildingRequirement.Any };
        for (int i = 0; i < ids.Length; i++)
        {
            string variant = initialized.Records[i].Variant;
            if (!variants.TryGetValue(variant, out var policy))
            {
                policy = LegacyAiCatalog.Production(variant, catalogs.Runtime, catalogs.Cards, catalogs.Buildings) with { Commanders = commanders };
                variants.Add(variant, policy);
            }
            production.Add(ids[i], policy);
            recruitment.Add(ids[i], new ReadOnlyDictionary<string, RecruitmentOffer>(policy.Offers.Concat(policy.Fortresses)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)));
        }
        var rules = new LegacyScenarioRules(turns, movement, combat, ai,
            new ReadOnlyDictionary<string, WorldProductionRules>(production),
            new ReadOnlyDictionary<string, IReadOnlyDictionary<string, RecruitmentOffer>>(recruitment), commanders,
            catalogs.Accounts.DeploymentRules(catalogs.Combat), catalogs.Combat.Training, catalogs.Buildings.UpgradeRules(catalogs.Combat.SkillParameters),
            catalogs.Cards.Installations(catalogs.Combat.SkillParameters), transport, commerce.Tavern, commerce.Trading, events.Dialogues,
            events.MissingReferences, initialized.HighestRatingRound);
        turns.Validate(); combat.Validate(); commanders.Validate(); rules.Training.Validate(); rules.Upgrades.Validate();
        foreach (var policy in production.Values) policy.Validate();
        var initial = new WorldState { ScenarioId = request.Id, ContentRevision = "initializing", Width = map.Width, Height = map.Height,
            LocalMultiplayer = request.LocalPlayers == null ? null : new(player,
                initialized.TurnOrder.Select(i => ids[i]).Where(request.LocalPlayers.Contains).ToArray(), ShareAlliedVictory: request.Mode == 1),
            Factions = initialized.TurnOrder.Select(i => bySource[i]).ToArray(), ActiveFaction = player, Cells = importedMap.Cells,
            Units = bindings.Select(b => b.Unit).ToArray(), Commanders = bindings.Where(b => b.Commander != null).Select(b => b.Commander!).ToArray(),
            Events = events.Events, TriggeredEvents = events.TriggeredEvents, Limits = request.Limits with { },
            RandomState = request.CombatSeed, AiRandomState = request.AiSeed, Phase = TurnPhase.RoundEvents };
        if (initial.LocalMultiplayer != null)
            initial = initial with { LocalMultiplayer = initial.LocalMultiplayer with {
                InitialObjectiveOwner = LocalMultiplayerGame.InitialObjectiveOwner(initial with {
                    LocalMultiplayer = initial.LocalMultiplayer with { ShareAlliedVictory = false } }, turns.Victory.UnitObjectiveOwnership),
                HadObjectives = initial.Cells.Any(c => c.Objective) || initial.Units.Any(u => u.Objective) } };
        string worldRevision = Fingerprint(new { schema = 1, initialized.SourceRevision, map = importedMap.SourceRevision,
            events = events.SourceRevision, commerce = commerce.SourceRevision, rules,
            sourceHashes = catalogs.Runtime.SourceHashes, initial = initial with { RandomState = 0, AiRandomState = 0 } });
        initial = initial with { ContentRevision = worldRevision };
        initial = initial with { DiplomacyNeutralCamps = neutral,
            Provocations = request.Mode is 1 or 2 ? new(Camp(4), new[] { Camp(1), Camp(2), Camp(3) }) : null };
        if (initial.LocalMultiplayer?.ShareAlliedVictory == true)
            initial = initial with { LocalMultiplayer = initial.LocalMultiplayer with {
                InitialObjectiveOwner = LocalMultiplayerGame.InitialObjectiveOwner(initial, turns.Victory.UnitObjectiveOwnership) } };
        initial.Validate();
        var entered = WorldTurns.Resume(initial, 0, turns);
        if (request.LocalPlayers != null) entered = entered with { State = LocalMultiplayerGame.Resolve(entered.State, turns.Victory) };
        string sessionRevision = Fingerprint(new { schema = 1, worldRevision, account = account.ContentRevision, roster = rules.Roster,
            commerce = commerce.SourceRevision });
        var session = new GameSession { ContentRevision = sessionRevision, PlayerFaction = player, World = entered.State,
            Account = account.Copy(), Markets = commerce.Markets, TavernCandidates = commerce.Candidates,
            RosterDeployments = request.LocalPlayers == null ? [new(player, initialized.Records[initialized.PlayerIndex].Count172, Array.Empty<string>())]
                : initialized.TurnOrder.Where(i => request.LocalPlayers.Contains(ids[i]))
                    .Select(i => new RosterDeploymentState(ids[i], initialized.Records[i].Count172, Array.Empty<string>())).ToArray() };
        session.Validate();
        return new(session.Copy(), rules, entered.Notices.ToArray(), map) { MapDiagnostics = importedMap.Diagnostics,
            TechnologySnapshot = request.Mode == 1 ? initialized.Records[initialized.PlayerIndex].Values68.ToArray() : null };
    }

    private static string Fingerprint<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(document.RootElement, writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));

        static void Write(JsonElement element, Utf8JsonWriter writer)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    // Optional runtime districts must not change the pre-existing source-content binding.
                    if (property.Name == nameof(LocalMultiplayerState.ShareAlliedVictory)) continue;
                    if (property.Name == nameof(WorldState.Jurisdictions) && property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Name == nameof(WorldState.ArmyCapacity) && property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Name == nameof(FactionState.Appearance) && property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Name == nameof(FactionState.DiplomacyWarned) && property.Value.ValueKind == JsonValueKind.False) continue;
                    if (property.Name == nameof(WorldState.Provocations) && property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Name is nameof(WorldState.LocalMultiplayer) or nameof(WorldState.ScenarioVictory) or nameof(WorldState.ScenarioEnding)
                        && property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Name == nameof(WorldState.NeutralHostilities) && property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() == 0) continue;
                    if (property.Name == nameof(CombatRules.LethalStrikePreventsCounterattack) && property.Value.ValueKind == JsonValueKind.False) continue;
                    if (property.Name == nameof(CombatRules.LegacySkills) && property.Value.ValueKind == JsonValueKind.Null) continue;
                    if (property.Name is nameof(Equipment.SkillSlots) or nameof(Equipment.UseSkill) && property.Value.ValueKind == JsonValueKind.Null) continue;
                    writer.WritePropertyName(property.Name); Write(property.Value, writer);
                }
                writer.WriteEndObject();
            }
            else if (element.ValueKind == JsonValueKind.Array)
            { writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); }
            else element.WriteTo(writer);
        }
    }
}
