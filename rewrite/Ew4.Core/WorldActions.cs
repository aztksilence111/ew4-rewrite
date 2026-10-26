using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public enum BuildingRequirement { Any, Required, Forbidden }
public sealed record RecruitmentOffer
{
    public string Id { get; init; } = "";
    public Combatant? Template { get; init; }
    public Resources Cost { get; init; } = new(0, 0, 0);
    public BuildingRequirement Building { get; init; } = BuildingRequirement.Required;
    public IReadOnlyCollection<string> BuildingKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, IReadOnlyCollection<int>>? AllowedBuildingLevels { get; init; }
    public int MinimumBuildingLevel { get; init; }
    public bool AllowSea { get; init; } = true;
    public string TurnGroup { get; init; } = "recruitment";
    public int? PerTurnLimit { get; init; }
    public int ConstructionTurns { get; init; }
    public UnitSupport Support { get; init; } = new();
    public void Validate()
    {
        Cost.Validate();
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(TurnGroup) || PerTurnLimit < 1
            || MinimumBuildingLevel < 0 || ConstructionTurns < 0 || !Enum.IsDefined(typeof(BuildingRequirement), Building)
            || BuildingKinds.Any(string.IsNullOrWhiteSpace)
            || AllowedBuildingLevels != null && AllowedBuildingLevels.Any(p => string.IsNullOrWhiteSpace(p.Key)
                || p.Value == null || p.Value.Count == 0 || p.Value.Any(l => l < 0)))
            throw new RuleViolation("recruit.configuration", "Invalid recruitment configuration");
    }
}

public enum AttackBlock { None, MissingAttacker, MissingDefender, UnderConstruction, AlreadyActed, Stunned, OutOfRange }
public sealed record WorldMoveRules
{
    public RuleEventBus? Extensions { get; init; }
    public MovementRules Search { get; init; } = new();
    public TerritoryMoveRules Territory { get; init; } = new();
    public bool SpendAllMovement { get; init; } = true;
    public string? PlayerCamp { get; init; }
    public VictoryRules Victory { get; init; } = new();
    public IReadOnlyCollection<string> NeutralCamps { get; init; } = Array.Empty<string>();
}
public static class AttackLegality
{
    public static AttackBlock Check(bool attackerPresent, bool defenderPresent, int construction, bool acted,
        int morale, int distance, int minimumRange, int maximumRange)
    {
        if (!attackerPresent) return AttackBlock.MissingAttacker;
        if (!defenderPresent) return AttackBlock.MissingDefender;
        if (construction > 0) return AttackBlock.UnderConstruction;
        if (acted) return AttackBlock.AlreadyActed;
        if (morale == -3) return AttackBlock.Stunned;
        return distance < minimumRange || distance > maximumRange ? AttackBlock.OutOfRange : AttackBlock.None;
    }
}

public static class WorldActions
{
    internal static FactionState Ready(WorldState state, long expectedRevision)
    {
        state.Validate();
        if (expectedRevision != state.Revision) throw new RuleViolation("action.stale", "Command was planned against another state revision");
        if (state.Phase != TurnPhase.Active) throw new RuleViolation("action.phase", "Faction turn is not active");
        if (state.PendingDialogue != null) throw new RuleViolation("action.dialogue_pending", "A dialogue is waiting");
        var faction = state.Factions.Single(f => f.Id == state.ActiveFaction);
        if (faction.Defeated) throw new RuleViolation("action.defeated", "Active faction has been defeated");
        return faction;
    }

    public static bool CanRecruit(WorldState state, int cell, RecruitmentOffer offer)
    {
        state.Validate(); offer.Validate();
        var faction = state.Factions.Single(f => f.Id == state.ActiveFaction);
        return CanRecruitValidated(state, faction, cell, offer);
    }

    internal static bool CanRecruitValidated(WorldState state, FactionState faction, int cell, RecruitmentOffer offer,
        int? occupants = null, int? armySize = null, int? armyLimit = null)
    {
        if (cell < 0 || cell >= state.Cells.Count || state.PendingDialogue != null || state.Phase != TurnPhase.Active) return false;
        var site = state.Cells[cell];
        if (faction.Defeated || !site.Terrain.CanTraverse() || site.Owner != faction.Id || (!offer.AllowSea && site.Terrain.Sea)
            || offer.Template?.Profile.Category == "fort" && !JurisdictionClaims.CanBuildFort(state, cell, faction.Id)
            || !faction.Recruitment.ContainsKey(offer.Id)
            || (offer.Building == BuildingRequirement.Required && site.BuildingKind == null)
            || (offer.Building == BuildingRequirement.Forbidden && site.BuildingKind != null)
            || site.BuildingLevel < offer.MinimumBuildingLevel
            || (offer.BuildingKinds.Count > 0 && (site.BuildingKind == null || !offer.BuildingKinds.Contains(site.BuildingKind)))
            || (offer.AllowedBuildingLevels != null && (site.BuildingKind == null
                || !offer.AllowedBuildingLevels.TryGetValue(site.BuildingKind, out var levels) || !levels.Contains(site.BuildingLevel)))
            || (faction.TurnUses.TryGetValue(offer.TurnGroup, out int used) && used >= offer.PerTurnLimit)) return false;
        if ((occupants ?? state.Units.Count(u => u.Cell == cell)) >= state.Limits.UnitsPerCell
            || !WorldArmyCapacity.CanAdd(state, faction.Id, armySize, armyLimit)) return false;
        return true;
    }

    public static WorldChange Recruit(WorldState state, long expectedRevision, int cell, string unitId, RecruitmentOffer offer)
    {
        var faction = Ready(state, expectedRevision);
        if (string.IsNullOrWhiteSpace(unitId) || state.Units.Any(u => u.Id == unitId))
            throw new RuleViolation("recruit.identity", "Recruitment requires a new unit ID");
        if (state.ArmyCapacity != null && !WorldArmyCapacity.CanAdd(state, faction.Id))
            throw new RuleViolation("recruit.capacity", "Faction army capacity has been reached");
        if (!CanRecruit(state, cell, offer)) throw new RuleViolation("recruit.site", "Recruitment is not permitted at this site");
        if (faction.Resources.Money < offer.Cost.Money || faction.Resources.Industry < offer.Cost.Industry || faction.Resources.Food < offer.Cost.Food)
            throw new RuleViolation("recruit.resources", "Insufficient recruitment resources");
        if (offer.Template == null) throw new RuleViolation("recruit.profile", "Recruitment unit definition is missing");
        offer.Template.Validate();
        var grant = faction.Recruitment[offer.Id];
        var unit = new UnitState
        {
            Id = unitId, Owner = faction.Id, Cell = cell, Veterancy = grant.Veterancy, Transport = grant.Transport,
            Combatant = offer.Template with { Health = offer.Template.Profile.MaxHealth, Morale = 0, ConstructionTurns = offer.ConstructionTurns,
                AtSea = state.Cells[cell].Terrain.Sea, BuildingKind = state.Cells[cell].BuildingKind },
            RemainingMovement = 0, Acted = true, Support = offer.Support
        };
        var uses = faction.TurnUses.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        uses[offer.TurnGroup] = checked((uses.TryGetValue(offer.TurnGroup, out int previous) ? previous : 0) + 1);
        var updated = faction with
        {
            Resources = new(faction.Resources.Money - offer.Cost.Money, faction.Resources.Industry - offer.Cost.Industry, faction.Resources.Food - offer.Cost.Food),
            TurnUses = uses
        };
        var next = (state with
        {
            Factions = state.Factions.Select(f => f.Id == faction.Id ? updated : f).ToArray(),
            Units = state.Units.Append(unit).ToArray(), Revision = checked(state.Revision + 1)
        }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice("unit.recruited", unitId) });
    }

    public static WorldChange Move(WorldState state, long expectedRevision, string unitId, int destination, WorldMoveRules? configuration = null)
        => MoveCore(state, expectedRevision, unitId, destination, configuration);

    internal static WorldChange ProjectMove(WorldState state, string unitId, int destination, WorldMoveRules rules,
        IReadOnlyDictionary<int, ReachableCell> range)
        => PlanningScope.CanTrust(rules) ? MoveCore(state, state.Revision, unitId, destination, rules, range, true)
            : Move(state, state.Revision, unitId, destination, rules);

    private static WorldChange MoveCore(WorldState state, long expectedRevision, string unitId, int destination,
        WorldMoveRules? configuration, IReadOnlyDictionary<int, ReachableCell>? range = null, bool projection = false)
    {
        var faction = Ready(state, expectedRevision);
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        var rules = configuration ?? new();
        if (rules.PlayerCamp != null && string.IsNullOrWhiteSpace(rules.PlayerCamp)
            || rules.Victory.RoundLimit < 1 || !Enum.IsDefined(typeof(ObjectiveUnitOwnership), rules.Victory.UnitObjectiveOwnership)
            || rules.NeutralCamps.Any(string.IsNullOrWhiteSpace))
            throw new RuleViolation("move.configuration", "Invalid movement victory configuration");
        if (unit == null || unit.Owner != faction.Id || unit.RemainingMovement < 1 || unit.Morale.Effective == -3)
            throw new RuleViolation("move.unit", "Unit is unavailable for movement");
        var map = new HexMap(state.Width, state.Height);
        map.Check(destination);
        if (destination == unit.Cell || state.Units.Count(u => u.Cell == destination) >= state.Limits.UnitsPerCell)
            throw new RuleViolation("move.destination", "Destination is occupied or unchanged");
        if (!(range ?? MovementRange(state, unit, rules)).TryGetValue(destination, out var reachable))
            throw new RuleViolation("move.unreachable", "No affordable legal route to destination");
        var otherUnits = state.Units.Where(u => u.Id != unitId).ToArray();
        var ownership = MovementOwnership.Move(state, map, otherUnits.Select(u => u.Cell).Distinct().ToArray(),
            reachable.Path, faction.Id, rules.Territory);
        var claimed = ownership.Cells;
        var moved = unit with
        {
            Cell = destination, RemainingMovement = rules.SpendAllMovement ? 0 : Math.Max(0, reachable.Remaining),
            Combatant = unit.Combatant with { AtSea = claimed[destination].Terrain.Sea, BuildingKind = claimed[destination].BuildingKind }
        };
        var notices = new List<WorldNotice> { new("unit.moved", unitId, destination) };
        foreach (int i in ownership.Changed) notices.Add(new("cell.owner_changed", i.ToString()));
        var next = projection ? PlanningScope.ProjectMovement(state, unit, destination, moved.RemainingMovement, claimed, ownership)
            : state with { Cells = claimed, Units = state.Units.Select(u => u.Id == unitId ? moved : u).ToArray(),
                Revision = checked(state.Revision + 1) };
        if (!projection) next = next.Copy();
        next.Validate();
        var previousOwners = ownership.Changed
            .Select(i => state.Cells[i].Owner).Where(id => id != null && id != faction.Id).Distinct().ToArray();
        foreach (string? owner in previousOwners)
        {
            var victim = next.Factions.Single(f => f.Id == owner);
            if (victim.Defeated) continue;
            var armyCells = new HashSet<int>(next.Units.Where(u => u.Owner == owner).Select(u => u.Cell));
            var assets = next.Cells.Select((c, i) => (c, i)).Where(p => p.c.Owner == owner)
                .Select(p => new SurvivalAsset(armyCells.Contains(p.i), p.c.BuildingKind))
                .Concat(new[] { new SurvivalAsset(armyCells.Count > 0, null) });
            if (!FactionSurvival.Defeated(assets, victim.Survival)) continue;
            var surrendered = WorldEvents.Surrender(next, victim.Id, faction.Id);
            next = surrendered.State;
            notices.AddRange(surrendered.Notices);
        }
        if (rules.Extensions != null)
        {
            next = rules.Extensions.Dispatch(next, "unit.move_end", unitId, new Ew4Random(next.RandomState));
            if (state.Cells[destination].Owner != next.Cells[destination].Owner)
                next = rules.Extensions.Dispatch(next, "unit.captured", unitId, new Ew4Random(next.RandomState));
        }
        var scenarioEvents = ScenarioRules.Evaluate(next, rules.Extensions?.Scripts);
        next = scenarioEvents.State; notices.AddRange(scenarioEvents.Notices);
        if (rules.PlayerCamp != null && next.Outcome == BattleOutcome.Continue && (next.ScenarioEvents.Count == 0 || next.PendingDialogue == null))
        {
            next = WorldVictory.Resolve(next, rules.PlayerCamp, rules.Victory, rules.NeutralCamps);
            if (next.Outcome != BattleOutcome.Continue)
            {
                notices.Add(new("battle.finished", next.Outcome.ToString()));
            }
        }
        string? previous = state.Cells[destination].Owner, current = next.Cells[destination].Owner;
        if (next.Phase != TurnPhase.Finished && previous != null && current != null && previous != current)
        {
            var events = WorldEvents.DuringAction(next, new(TriggerKind.CellCaptured, Cell: destination, PreviousOwner: previous, NewOwner: current));
            next = events.State; notices.AddRange(events.Notices);
        }
        var provocation = WorldProvocations.Captured(state, next, destination);
        next = provocation.State; notices.AddRange(provocation.Notices);
        next = projection ? PlanningScope.WithRevision(next, checked(state.Revision + 1))
            : next with { Revision = checked(state.Revision + 1) };
        next.Validate();
        return new(next, notices) { Movements = new[] { new WorldMovement(unitId, reachable.Path) } };
    }

    public static WorldChange RelocateAndRecruit(WorldState state, long expectedRevision, string unitId, int destination,
        string recruitedUnitId, RecruitmentOffer offer, WorldMoveRules? rules = null)
    {
        var faction = Ready(state, expectedRevision);
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != faction.Id) throw new RuleViolation("recruit.relocation_unit", "Recruitment relocation requires an owned unit");
        int origin = unit.Cell;
        var moved = Move(state, expectedRevision, unitId, destination, rules);
        var recruited = Recruit(moved.State, moved.State.Revision, origin, recruitedUnitId, offer);
        var next = recruited.State with { Revision = checked(state.Revision + 1) };
        next.Validate();
        return new(next, moved.Notices.Concat(recruited.Notices).ToArray()) { Movements = moved.Movements };
    }

    public static IReadOnlyDictionary<int, ReachableCell> Reachable(WorldState state, string unitId, WorldMoveRules? configuration = null)
    {
        state.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId)
            ?? throw new RuleViolation("movement.unit", "Unknown unit");
        if (unit.RemainingMovement < 1 || unit.Morale.Effective == -3)
            return new Dictionary<int, ReachableCell>();
        return MovementRange(state, unit, configuration ?? new());
    }

    internal static IReadOnlyDictionary<int, ReachableCell> MovementRange(WorldState state, UnitState unit, WorldMoveRules rules)
    {
        var context = MovementContext(state, unit, rules);
        return Movement.Reachable(new(state.Width, state.Height), context.Cells, unit.Cell, context.Actor, rules.Search);
    }

    public static IReadOnlyCollection<string> MovableUnits(WorldState state, string owner, WorldMoveRules configuration)
    {
        var units = state.Units.Where(u => u.Owner == owner && u.Combatant.Health > 0
            && u.Combatant.Profile.Category != "fort" && u.RemainingMovement > 0
            && u.Morale.Effective != -3 && u.Combatant.ConstructionTurns == 0).ToArray();
        if (units.Length == 0) return Array.Empty<string>();
        var context = MovementContext(state, units[0]);
        var occupied = state.Units.Select(u => u.Cell).ToHashSet();
        var map = new HexMap(state.Width, state.Height);
        var result = new List<string>();
        foreach (var unit in units)
        {
            var actor = MovementActorFor(unit, context.Actor.Camp);
            if (configuration.Extensions != null)
            {
                int budget = configuration.Extensions.MovementBudget(unit.Combatant);
                actor = actor with { FullBudget = budget, Remaining = Math.Min(unit.RemainingMovement, budget) };
            }
            actor = Movement.AtOrigin(actor, context.Cells[unit.Cell]);
            bool open = false, throughOccupied = false;
            if (actor.Remaining <= 0) continue;
            for (int direction = 0; direction < 6; direction++)
            {
                int next = map.Neighbor(unit.Cell, direction);
                if (next < 0) continue;
                int? cost = Movement.EntryCost(context.Cells[unit.Cell], context.Cells[next], (direction + 3) % 6, actor, configuration.Search);
                if (!cost.HasValue || cost.Value > actor.Remaining && !configuration.Search.AllowOverdrawnEntry) continue;
                if (!occupied.Contains(next)) { open = true; break; }
                throughOccupied = true;
            }
            // Usually a single legal step proves availability. Search only when allies may block the first landing.
            if (open || throughOccupied && Movement.Reachable(map, context.Cells, unit.Cell, actor, configuration.Search)
                .Keys.Any(cell => !occupied.Contains(cell))) result.Add(unit.Id);
        }
        return result;
    }

    private static MovementActor MovementActorFor(UnitState unit, string camp) => new()
    {
        Category = unit.Combatant.Profile.Category, Camp = camp, Transport = unit.Transport,
        Geography = unit.Combatant.Geography || unit.Combatant.Has(Ew4Combat.Skill(1)),
        FullBudget = Math.Max(unit.ExtensionMovementLimit ?? unit.Combatant.Profile.Movement, unit.RemainingMovement), Remaining = unit.RemainingMovement
    };

    internal static (MovementCell[] Cells, MovementActor Actor) MovementContext(WorldState state, UnitState unit, WorldMoveRules? rules = null)
    {
        if (PlanningScope.Queries(state) is { } queries)
        {
            var prepared = queries.Movement(unit.Owner);
            var mover = MovementActorFor(unit, prepared.Camp);
            if (rules?.Extensions != null)
            {
                int budget = rules.Extensions.MovementBudget(unit.Combatant);
                mover = mover with { FullBudget = budget, Remaining = Math.Min(unit.RemainingMovement, budget) };
            }
            return (prepared.Cells, mover);
        }
        var campIndex = state.Factions.ToDictionary(f => f.Id, f => f.Camp, StringComparer.Ordinal);
        if (state.DiplomaticRelations.Count > 0)
            campIndex = state.Factions.ToDictionary(f => f.Id, f => WorldDiplomacy.Allied(state, unit.Owner, f.Id) ? "allied" : "other", StringComparer.Ordinal);
        var occupied = state.Units.GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => campIndex[g.First().Owner]);
        var cells = state.Cells.Select((c, i) => c.Terrain with { OccupantCamp = occupied.TryGetValue(i, out string camp) ? camp : null }).ToArray();
        var actor = MovementActorFor(unit, campIndex[unit.Owner]);
        if (rules?.Extensions != null)
        {
            int budget = rules.Extensions.MovementBudget(unit.Combatant);
            actor = actor with { FullBudget = budget, Remaining = Math.Min(unit.RemainingMovement, budget) };
        }
        // Reachable applies the sea-origin cap for this turn. Keep the land budget
        // here so strategic routes can still include inland steps after landing.
        return (cells, actor);
    }
}
