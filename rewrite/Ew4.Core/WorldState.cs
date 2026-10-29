using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record WorldLimits
{
    public int? UnitsPerFaction { get; init; }
    public int UnitsPerCell { get; init; } = 1;
    public int? MaximumCells { get; init; }
    public void Validate()
    {
        if (UnitsPerFaction < 1 || UnitsPerCell < 1 || MaximumCells < 1)
            throw new RuleViolation("world.limits", "Configured limits must be positive");
    }
}

public sealed record FactionTurnSummary(int Round, Resources Income, long FoodConsumption);

public sealed record FactionState
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public FactionAppearance? Appearance { get; init; }
    public IReadOnlyList<int>? HomelandBuildings { get; init; }
    public FactionCapital? Capital { get; init; }
    public string Camp { get; init; } = "";
    public bool Human { get; init; }
    public PlayerAiControl? PlayerAi { get; init; }
    public bool Defeated { get; init; }
    public bool DiplomacyWarned { get; init; }
    public Resources Resources { get; init; } = new(0, 0, 0);
    public int Shortage { get; init; }
    public float IncomeMultiplier { get; init; } = 1;
    public float AiEvasionBonus { get; init; }
    public Resources IncomeBonus { get; init; } = new(0, 0, 0);
    public FactionTurnSummary? TurnSummary { get; init; }
    public IReadOnlyDictionary<string, long> Kills { get; init; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, RecruitmentGrant> Recruitment { get; init; } = new Dictionary<string, RecruitmentGrant>();
    public IReadOnlyDictionary<string, int> TurnUses { get; init; } = new Dictionary<string, int>();
    public SurvivalRules Survival { get; init; } = FactionSurvival.Legacy(2);
}

public sealed record RecruitmentGrant(string Veterancy = "0", bool Transport = false);

public sealed record CellState
{
    public MovementCell Terrain { get; init; } = new();
    public string? Owner { get; init; }
    public string? Name { get; init; }
    public string? BuildingKind { get; init; }
    public int BuildingLevel { get; init; }
    public int BuildingUpgradesThisTurn { get; init; }
    public bool Objective { get; init; }
    public bool BlocksTerritoryExpansion { get; init; }
    public int Environment { get; init; }
    public Resources? IncomeOverride { get; init; }
    public string? InstallationKind { get; init; }
    public string FireTerrain { get; init; } = "open";
    public string DefenseTerrain { get; init; } = "land";
}

public sealed record MoraleState(int Baseline = 0, int Duration = 0, int Effective = 0)
{
    public static MoraleState Set(int value, bool leadership) =>
        new(value, 3, leadership && value < 0 ? 0 : value);
    public MoraleState Add(int value, bool leadership) => Set((int)Math.Max(-3L, Math.Min(1L, (long)Baseline + value)), leadership);
    public void Validate()
    {
        if (Baseline < -3 || Baseline > sbyte.MaxValue || Effective < -3 || Effective > sbyte.MaxValue || Duration < 0)
            throw new RuleViolation("world.morale", "Invalid morale state");
    }
}

public sealed record UnitState
{
    public string Id { get; init; } = "";
    public string Owner { get; init; } = "";
    public int Cell { get; init; }
    public Combatant Combatant { get; init; } = new();
    public string? CommanderId { get; init; }
    public UnitCommanderBase? CommanderBase { get; init; }
    public MoraleState Morale { get; init; } = new();
    public int RemainingMovement { get; init; }
    public int? ExtensionMovementLimit { get; init; }
    public bool Acted { get; init; }
    public PlayerUnitControl? PlayerControl { get; init; }
    public bool Objective { get; init; }
    public long Experience { get; init; }
    public string Veterancy { get; init; } = "0";
    public bool Transport { get; init; }
    public int Burning { get; init; }
    public bool ForceRecovery { get; init; }
    public int LocalPenalty { get; init; }
    public string Behavior { get; init; } = "default";
    public string RestoreBehavior { get; init; } = "default";
    public int? BehaviorExpiry { get; init; }
    public UnitSupport Support { get; init; } = new();
}

// Resolved bonuses are part of the snapshot; refresh must not reapply commander attachment.
public sealed record UnitSupport
{
    public long FoodConsumption { get; init; }
    public int FieldHealing { get; init; }
    public int TitleHealing { get; init; }
    public int AdjacentHealing { get; init; }
    public int AdjacentAttack { get; init; }
    public int AdjacentDefense { get; init; }
    public float IncomeMultiplier { get; init; } = 1;
    public bool Logistics { get; init; }
}

public enum TurnPhase { Active, RoundEvents, StartFaction, Finished, EndFactionEvents, StartFactionEvents }

public sealed record CommanderRuntime
{
    public string Id { get; init; } = "";
    public string Owner { get; init; } = "";
    public bool Withdrawn { get; init; }
    public bool ProgressionEnabled { get; init; } = true;
    public ProgressionState Rank { get; init; } = new(0, 0);
    public ProgressionState Title { get; init; } = new(0, 0);
    public int? RankExperiencePercent { get; init; }
    public int? TitleExperiencePercent { get; init; }
    public int RankHealthBonus { get; init; }
    public int MaximumTrainingLevel { get; init; }
}

public enum TriggerKind { Round, UnitDestroyed, CellCaptured }
public enum EventEffectKind { None, SetMorale, EnvironmentSpread }
public enum EventFactionTarget { Explicit, PreviousOwner, NewOwner }
public sealed record ScenarioEventRule
{
    public TriggerKind Trigger { get; init; }
    public int Round { get; init; } = 1;
    public string? UnitId { get; init; }
    public int? TriggerCell { get; init; }
    public bool RequireCommander { get; init; }
    public bool RequireDifferentCamps { get; init; }
    public EventEffectKind Effect { get; init; }
    public string? FactionId { get; init; }
    public EventFactionTarget Target { get; init; }
    public int Value { get; init; }
    public int Cell { get; init; }
}

public sealed record ScenarioEvent
{
    public string Id { get; init; } = "";
    public IReadOnlyList<ScenarioEventRule> Rules { get; init; } = Array.Empty<ScenarioEventRule>();
    public string? DialogueId { get; init; }
}

public sealed record WorldState
{
    public string ScenarioId { get; init; } = "";
    public string ContentRevision { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int Round { get; init; } = 1;
    public string ActiveFaction { get; init; } = "";
    public long Revision { get; init; }
    public ulong RandomState { get; init; }
    public ulong AiRandomState { get; init; }
    public IReadOnlyDictionary<string, long> ExtensionState { get; init; } = new Dictionary<string, long>();
    public WorldLimits Limits { get; init; } = new();
    public ArmyCapacityRules? ArmyCapacity { get; init; }
    public IReadOnlyList<FactionState> Factions { get; init; } = Array.Empty<FactionState>();
    public IReadOnlyList<CellState> Cells { get; init; } = Array.Empty<CellState>();
    public TerritorialJurisdictions? Jurisdictions { get; init; }
    public IReadOnlyList<NeutralHostility> NeutralHostilities { get; init; } = Array.Empty<NeutralHostility>();
    public IReadOnlyList<DiplomaticRelation> DiplomaticRelations { get; init; } = Array.Empty<DiplomaticRelation>();
    public IReadOnlyList<string> DiplomacyNeutralCamps { get; init; } = Array.Empty<string>();
    public DemandState? Demands { get; init; }
    public IReadOnlyList<FactionPairAdjustment>? DiplomacyAdjustments { get; init; }
    public ProvocationRules? Provocations { get; init; }
    public IReadOnlyList<UnitState> Units { get; init; } = Array.Empty<UnitState>();
    public IReadOnlyList<CommanderRuntime> Commanders { get; init; } = Array.Empty<CommanderRuntime>();
    public IReadOnlyList<CommanderDeploymentOffer> CommanderOffers { get; init; } = Array.Empty<CommanderDeploymentOffer>();
    public IReadOnlyList<ScenarioEvent> Events { get; init; } = Array.Empty<ScenarioEvent>();
    public string? ScenarioPlayerFaction { get; init; }
    public LocalMultiplayerState? LocalMultiplayer { get; init; }
    public ExtensionVictoryRules? ScenarioVictory { get; init; }
    public string? ScenarioEnding { get; init; }
    public StoryState? Story { get; init; }
    public IReadOnlyList<ScriptedScenarioEvent> ScenarioEvents { get; init; } = Array.Empty<ScriptedScenarioEvent>();
    public IReadOnlyList<string> TriggeredScenarioEvents { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> ScenarioChoices { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, ScenarioEventProgress> ScenarioEventProgress { get; init; } = new Dictionary<string, ScenarioEventProgress>();
    public IReadOnlyList<string> DestroyedScenarioUnits { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DeadScenarioCommanders { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TriggeredEvents { get; init; } = Array.Empty<string>();
    public string? PendingDialogue { get; init; }
    public IReadOnlyList<string> QueuedDialogues { get; init; } = Array.Empty<string>();
    public TurnPhase Phase { get; init; } = TurnPhase.Active;
    public BattleOutcome Outcome { get; init; } = BattleOutcome.Continue;

    public void Validate() => PlanningScope.Validate(this, ValidateCore);
    internal void ValidateUncached() => ValidateCore();
    private void ValidateCore()
    {
        Limits.Validate();
        ArmyCapacity?.Validate();
        if (ExtensionState.Count > 100_000 || ExtensionState.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 512))
            throw new RuleViolation("extension.state", "Invalid extension save state");
        Jurisdictions?.Validate(this);
        if (string.IsNullOrWhiteSpace(ScenarioId) || string.IsNullOrWhiteSpace(ContentRevision) || Round < 1 || Revision < 0
            || Width <= 0 || Height <= 0 || (long)Width * Height > int.MaxValue
            || (long)Width * Height != Cells.Count || Cells.Count > Limits.MaximumCells)
            throw new RuleViolation("world.header", "Invalid scenario, revision or dimensions");
        static HashSet<string> Ids(IEnumerable<string> source)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in source)
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new RuleViolation("world.identity", "Empty or duplicate identity");
            return ids;
        }
        var factionIds = Ids(Factions.Select(f => f.Id));
        DemandDiplomacy.Validate(this);
        if (Provocations is { } provocations && (string.IsNullOrWhiteSpace(provocations.NeutralCamp)
            || provocations.CombatantCamps == null || provocations.CombatantCamps.Count == 0
            || provocations.CombatantCamps.Any(string.IsNullOrWhiteSpace)
            || provocations.CombatantCamps.Contains(provocations.NeutralCamp)
            || provocations.CombatantCamps.Distinct().Count() != provocations.CombatantCamps.Count))
            throw new RuleViolation("world.diplomacy", "Invalid provocation policy");
        LocalMultiplayerGame.Validate(this);
        PlayerAi.Validate(this);
        var factionIndex = Factions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        if (DiplomaticRelations == null || DiplomacyNeutralCamps == null
            || DiplomacyNeutralCamps.Any(string.IsNullOrWhiteSpace) || DiplomacyNeutralCamps.Distinct().Count() != DiplomacyNeutralCamps.Count
            || DiplomaticRelations.Any(p => p == null || !factionIds.Contains(p.First) || !factionIds.Contains(p.Second)
                || string.CompareOrdinal(p.First, p.Second) >= 0 || p.Relation is not ("allied" or "neutral" or "hostile"))
            || DiplomaticRelations.Select(p => (p.First, p.Second)).Distinct().Count() != DiplomaticRelations.Count)
            throw new RuleViolation("world.diplomacy", "Invalid bilateral relations");
        if (NeutralHostilities.Any(h => !factionIds.Contains(h.First) || !factionIds.Contains(h.Second) || string.CompareOrdinal(h.First, h.Second) >= 0)
            || NeutralHostilities.Distinct().Count() != NeutralHostilities.Count)
            throw new RuleViolation("world.diplomacy", "Invalid neutral hostility pairs");
        Ids(Units.Select(u => u.Id));
        var eventIds = Ids(Events.Select(e => e.Id));
        var commanderIds = Ids(Commanders.Select(c => c.Id));
        var commanderIndex = Commanders.ToDictionary(c => c.Id, StringComparer.Ordinal);
        Ids(CommanderOffers.Select(o => o.Id));
        foreach (var offer in CommanderOffers)
        {
            offer.Cost.Validate();
            if (!commanderIds.Contains(offer.CommanderId) || !factionIds.Contains(offer.Owner) || offer.Cell < 0 || offer.Cell >= Cells.Count
                || offer.AvailableRound < 1 || string.IsNullOrWhiteSpace(offer.TurnGroup) || offer.PerTurnLimit < 1)
                throw new RuleViolation("world.commander_offer", "Invalid commander deployment offer");
        }
        var fired = Ids(TriggeredEvents);
        if (!factionIds.Contains(ActiveFaction) || fired.Any(id => !eventIds.Contains(id)))
            throw new RuleViolation("world.reference", "Invalid active faction or event reference");
        if (!Enum.IsDefined(typeof(TurnPhase), Phase) || !Enum.IsDefined(typeof(BattleOutcome), Outcome)
            || (Phase == TurnPhase.Finished) != (Outcome != BattleOutcome.Continue)
            || (Phase == TurnPhase.StartFaction && PendingDialogue != null))
            throw new RuleViolation("world.phase", "Invalid turn phase or outcome");
        foreach (var faction in Factions)
        {
            ScenarioFactions.ValidateAppearance(faction.Appearance);
            if (faction.Capital is { } capital && (capital.Cell < 0 || capital.Cell >= Cells.Count || !DemandDiplomacy.Counts(Cells[capital.Cell])))
                throw new RuleViolation("scenario.capital", "Invalid faction capital");
            if (faction.HomelandBuildings is { } homeland && (homeland.Count > Cells.Count || homeland.Distinct().Count() != homeland.Count
                || homeland.Any(i => i < 0 || i >= Cells.Count || !DemandDiplomacy.Counts(Cells[i]))))
                throw new RuleViolation("scenario.homeland", "Invalid faction homeland building list");
            if (faction.Resources.Money < int.MinValue || faction.Resources.Industry < int.MinValue || faction.Resources.Food < int.MinValue)
                throw new RuleViolation("world.faction", "Opening debt exceeds the serialized signed balance range");
            faction.IncomeBonus.Validate();
            if (faction.TurnSummary is { } summary)
            {
                summary.Income.Validate();
                if (summary.Round < 1 || summary.Round > Round || summary.FoodConsumption < 0)
                    throw new RuleViolation("world.turn_summary", "Invalid faction turn summary");
            }
            if (string.IsNullOrWhiteSpace(faction.Camp) || faction.Shortage < 0
                || float.IsNaN(faction.IncomeMultiplier) || float.IsInfinity(faction.IncomeMultiplier) || faction.IncomeMultiplier < 0
                || float.IsNaN(faction.AiEvasionBonus) || faction.AiEvasionBonus < 0 || faction.AiEvasionBonus > 1)
                throw new RuleViolation("world.faction", "Invalid faction state");
            if (faction.Recruitment.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || string.IsNullOrWhiteSpace(p.Value.Veterancy))
                || faction.TurnUses.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)
                || faction.Kills.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)
                || faction.Survival.ExcludedBuildings.Any(string.IsNullOrWhiteSpace))
                throw new RuleViolation("world.faction", "Invalid recruitment grants or turn counters");
        }
        if (Cells is FrozenWorldCells frozenCells) frozenCells.Validate(this, factionIds);
        else foreach (var cell in Cells)
        {
            if (ArmyCapacity != null) WorldArmyCapacity.BuildingCapacity(this, cell);
            ValidateCellValue(cell);
            if (cell.Owner != null && !factionIds.Contains(cell.Owner))
                throw new RuleViolation("world.cell", "Invalid cell state or owner");
        }
        var commanders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in Units)
        {
            unit.Combatant.Validate(); unit.Morale.Validate();
            if (unit.CommanderBase != null)
            {
                unit.CommanderBase.Combatant.Validate();
                if (unit.CommanderId == null || unit.CommanderBase.Combatant.Profile.Id != unit.Combatant.Profile.Id
                    || unit.CommanderBase.Combatant.Profile.Category != unit.Combatant.Profile.Category)
                    throw new RuleViolation("world.commander_base", "Invalid unassigned unit base");
                ValidateSupport(unit.CommanderBase.Support);
            }
            if (!factionIds.Contains(unit.Owner) || factionIndex[unit.Owner].Defeated
                || unit.Cell < 0 || unit.Cell >= Cells.Count || !Cells[unit.Cell].Terrain.CanTraverse() || unit.Experience < 0
                || unit.ExtensionMovementLimit < 0 || unit.RemainingMovement < 0
                || unit.RemainingMovement > (unit.ExtensionMovementLimit ?? unit.Combatant.Profile.Movement)
                || unit.Combatant.Statuses.Any(s => s.SourceOwner != null && !factionIds.Contains(s.SourceOwner))
                || unit.Morale.Effective != unit.Combatant.Morale
                || unit.Burning < 0 || unit.LocalPenalty < 0 || unit.LocalPenalty > 2 || unit.BehaviorExpiry < 1
                || string.IsNullOrWhiteSpace(unit.Veterancy) || string.IsNullOrWhiteSpace(unit.Behavior) || string.IsNullOrWhiteSpace(unit.RestoreBehavior)
                || unit.Support.FoodConsumption < 0 || unit.Support.FieldHealing < 0 || unit.Support.TitleHealing < 0
                || unit.Support.AdjacentHealing < 0 || unit.Support.AdjacentAttack < 0 || unit.Support.AdjacentDefense < 0 || float.IsNaN(unit.Support.IncomeMultiplier)
                || float.IsInfinity(unit.Support.IncomeMultiplier) || unit.Support.IncomeMultiplier < 0
                || (unit.CommanderId != null && (!commanderIds.Contains(unit.CommanderId) || !commanders.Add(unit.CommanderId)
                    || commanderIndex[unit.CommanderId].Owner != unit.Owner || commanderIndex[unit.CommanderId].Withdrawn)))
                throw new RuleViolation("world.unit", "Invalid unit state, commander assignment or owner");
        }
        foreach (var commander in Commanders)
            if (!factionIds.Contains(commander.Owner) || commander.Rank.Level < 0 || commander.Title.Level < 0
                || commander.Rank.Experience < 0 || commander.Title.Experience < 0 || commander.RankHealthBonus < 0
                || commander.RankExperiencePercent < 0 || commander.TitleExperiencePercent < 0 || commander.MaximumTrainingLevel < 0)
                throw new RuleViolation("world.commander", "Invalid commander runtime state");
        var occupants = new Dictionary<int, (int Count, string Camp)>();
        var factionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool overCapacity = false, opposingCamps = false;
        foreach (var unit in Units)
        {
            string camp = factionIndex[unit.Owner].Camp;
            occupants.TryGetValue(unit.Cell, out var occupied);
            opposingCamps |= occupied.Count > 0 && !StringComparer.Ordinal.Equals(occupied.Camp, camp);
            int cellCount = occupied.Count + 1;
            occupants[unit.Cell] = (cellCount, camp);
            overCapacity |= cellCount > Limits.UnitsPerCell;
            if (Limits.UnitsPerFaction.HasValue)
            {
                factionCounts.TryGetValue(unit.Owner, out int count);
                factionCounts[unit.Owner] = count + 1;
                overCapacity |= count + 1 > Limits.UnitsPerFaction.Value;
            }
        }
        // Keep capacity errors ahead of camp conflicts, as in the full validator.
        if (overCapacity)
            throw new RuleViolation("world.capacity", "Configured unit capacity exceeded");
        if (opposingCamps)
            throw new RuleViolation("world.occupants", "Opposing camps cannot share a cell");
        foreach (var ev in Events)
        {
            if (ev.DialogueId != null && string.IsNullOrWhiteSpace(ev.DialogueId))
                throw new RuleViolation("world.event", "Invalid scenario dialogue");
            foreach (var rule in ev.Rules)
                if (!Enum.IsDefined(typeof(TriggerKind), rule.Trigger) || !Enum.IsDefined(typeof(EventEffectKind), rule.Effect)
                    || !Enum.IsDefined(typeof(EventFactionTarget), rule.Target)
                    || (rule.Trigger == TriggerKind.Round && rule.Round < 0)
                    || (rule.Trigger == TriggerKind.UnitDestroyed && string.IsNullOrWhiteSpace(rule.UnitId))
                    || (rule.Trigger == TriggerKind.CellCaptured && (!rule.TriggerCell.HasValue || rule.TriggerCell < 0 || rule.TriggerCell >= Cells.Count))
                    || (rule.RequireCommander && rule.Trigger != TriggerKind.UnitDestroyed)
                    || (rule.RequireDifferentCamps && rule.Trigger != TriggerKind.CellCaptured)
                    || (rule.Target != EventFactionTarget.Explicit && rule.Trigger == TriggerKind.Round)
                    || (rule.Effect == EventEffectKind.SetMorale && (rule.Value < -3 || rule.Value > 1))
                    || (rule.FactionId != null && !factionIds.Contains(rule.FactionId))
                    || (rule.Target != EventFactionTarget.Explicit && rule.FactionId != null)
                    || (rule.Effect == EventEffectKind.EnvironmentSpread && (rule.Cell < 0 || rule.Cell >= Cells.Count)))
                    throw new RuleViolation("world.event", "Invalid scenario event rule");
        }
        ScenarioRules.Validate(this);
        StoryValidation.State(this);
        bool KnownDialogue(string id) => StoryPlayback.Known(this, id) || Events.Any(e => fired.Contains(e.Id) && e.DialogueId == id)
            || WorldProvocations.Dialogue(this, id) != null
            || ScenarioEvents.Any(e => e.Popup != null && TriggeredScenarioEvents.Contains(e.Id) && !ScenarioChoices.ContainsKey(e.Id) && ScenarioRules.DialogueId(e.Id) == id);
        if (PendingDialogue != null && !KnownDialogue(PendingDialogue))
            throw new RuleViolation("world.dialogue", "Dialogue does not belong to a fired event");
        if ((QueuedDialogues.Count > 0 && PendingDialogue == null) || QueuedDialogues.Any(d => !KnownDialogue(d)))
            throw new RuleViolation("world.dialogue", "Invalid pending dialogue queue");
    }

    // A snapshot must not expose caller-owned arrays through nested combat profiles.
    private static void ValidateSupport(UnitSupport support)
    {
        if (support.FoodConsumption < 0 || support.FieldHealing < 0 || support.TitleHealing < 0 || support.AdjacentHealing < 0
            || support.AdjacentAttack < 0 || support.AdjacentDefense < 0 || float.IsNaN(support.IncomeMultiplier)
            || float.IsInfinity(support.IncomeMultiplier) || support.IncomeMultiplier < 0)
            throw new RuleViolation("world.commander_base", "Invalid unassigned support base");
    }

    private static UnitState CopyUnit(UnitState value)
    {
        var combatant = FrozenCombatants.Copy(value.Combatant);
        var control = value.PlayerControl?.Goals == null ? value.PlayerControl
            : value.PlayerControl with { Goals = value.PlayerControl.Goals.ToArray() };
        var commanderBase = value.CommanderBase;
        if (commanderBase != null)
        {
            var original = FrozenCombatants.Copy(commanderBase.Combatant);
            if (!ReferenceEquals(original, commanderBase.Combatant)) commanderBase = commanderBase with { Combatant = original };
        }
        // All other UnitState fields are values or sealed immutable records.
        return ReferenceEquals(combatant, value.Combatant) && ReferenceEquals(control, value.PlayerControl)
            && ReferenceEquals(commanderBase, value.CommanderBase) ? value
            : value with { Combatant = combatant, PlayerControl = control, CommanderBase = commanderBase };
    }

    internal static void ValidateCellValue(CellState cell)
    {
        if (cell.Terrain.Barrier != TerrainBarrier.None || cell.Terrain.MinimumEntryCost != 0) Geography.Validate(cell);
        cell.IncomeOverride?.Validate();
        if (cell.Environment < 0 || cell.Terrain.TerrainCost <= 0 || cell.BuildingLevel < 0 || cell.BuildingUpgradesThisTurn < 0
            || cell.InstallationKind != null && string.IsNullOrWhiteSpace(cell.InstallationKind)
            || string.IsNullOrWhiteSpace(cell.FireTerrain) || string.IsNullOrWhiteSpace(cell.DefenseTerrain))
            throw new RuleViolation("world.cell", "Invalid cell state or owner");
    }
    public WorldState Copy() => PlanningScope.PreserveCopy(this, this with
    {
        ArmyCapacity = ArmyCapacity?.Copy(),
        DiplomacyAdjustments = DiplomacyAdjustments?.ToArray(),
        ExtensionState = ExtensionState.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        Factions = Factions.Select(f => f with
        {
            HomelandBuildings = f.HomelandBuildings?.ToArray(),
            Recruitment = f.Recruitment.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            TurnUses = f.TurnUses.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            PlayerAi = f.PlayerAi?.Copy(),
            Kills = f.Kills.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            Survival = f.Survival with { ExcludedBuildings = f.Survival.ExcludedBuildings.ToArray() }
        }).ToArray(),
        // CellState, MovementCell and Resources are sealed records containing only
        // immutable values. Only the caller-owned list needs snapshot isolation.
        Cells = PlanningScope.FrozenSnapshots ? FrozenWorldCells.Copy(Cells) : Cells.ToArray(),
        Jurisdictions = Jurisdictions?.Copy(),
        Provocations = Provocations == null ? null : Provocations with { CombatantCamps = Provocations.CombatantCamps.ToArray() },
        NeutralHostilities = NeutralHostilities.ToArray(),
        DiplomaticRelations = DiplomaticRelations.ToArray(), DiplomacyNeutralCamps = DiplomacyNeutralCamps.ToArray(),
        Demands = Demands?.Copy(),
        Units = Units.Select(CopyUnit).ToArray(),
        Commanders = Commanders.ToArray(), CommanderOffers = CommanderOffers.ToArray(),
        Events = Events.Select(e => e with { Rules = e.Rules.ToArray() }).ToArray(),
        LocalMultiplayer = LocalMultiplayer == null ? null : LocalMultiplayer with { HumanFactions = LocalMultiplayer.HumanFactions.ToArray() },
        ScenarioVictory = ScenarioVictory == null ? null : ExtensionVictory.Copy(ScenarioVictory),
        ScenarioEvents = ScenarioEvents.Select(ScenarioRules.Copy).ToArray(), TriggeredScenarioEvents = TriggeredScenarioEvents.ToArray(),
        Story = Story == null ? null : Story with { Runs = Story.Runs.Select(r => r with { Path = r.Path.ToArray(),
            Choices = r.Choices.ToDictionary(p => p.Key, p => p.Value), Continuation = r.Continuation.Select(ScenarioRules.CopyEffect).ToArray(),
            PendingEffects = r.PendingEffects.Select(ScenarioRules.CopyEffect).ToArray() }).ToArray() },
        ScenarioChoices = ScenarioChoices.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        ScenarioEventProgress = ScenarioEventProgress.ToDictionary(p => p.Key, p => p.Value with { }, StringComparer.Ordinal),
        DestroyedScenarioUnits = DestroyedScenarioUnits.ToArray(), DeadScenarioCommanders = DeadScenarioCommanders.ToArray(),
        TriggeredEvents = TriggeredEvents.ToArray(), QueuedDialogues = QueuedDialogues.ToArray()
    });
}
