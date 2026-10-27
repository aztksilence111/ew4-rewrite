using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record BuildingTurnRule(string Kind, int Level, Resources Income, int Healing = 0, bool ClaimsNeighbors = false);
public sealed record BuildingIncomeOverride(string Kind, long? Money = null, long? Industry = null, long? Food = null);
public enum RoundEventTiming { BeforeFaction, AfterFaction, Disabled }
public sealed record WorldTurnRules
{
    public RuleEventBus? Extensions { get; init; }
    public IReadOnlyList<BuildingTurnRule> Buildings { get; init; } = Array.Empty<BuildingTurnRule>();
    public IReadOnlyDictionary<int, BuildingIncomeOverride> SiteIncomeOverrides { get; init; } = new Dictionary<int, BuildingIncomeOverride>();
    public IReadOnlyDictionary<string, int> VeterancyHealing { get; init; } = new Dictionary<string, int>();
    public SiteEndRules Settlement { get; init; } = new();
    public long? ResourceCap { get; init; }
    public string? PlayerCamp { get; init; }
    public VictoryRules Victory { get; init; } = new();
    public IReadOnlyCollection<string> NeutralCamps { get; init; } = Array.Empty<string>();
    public RoundEventTiming RoundEvents { get; init; } = RoundEventTiming.BeforeFaction;

    public void Validate()
    {
        Settlement.Burning.Validate(); Settlement.Environment.Validate();
        if (ResourceCap < 0 || (PlayerCamp != null && string.IsNullOrWhiteSpace(PlayerCamp)) || Victory.RoundLimit < 1
            || !Enum.IsDefined(typeof(ObjectiveUnitOwnership), Victory.UnitObjectiveOwnership)
            || !Enum.IsDefined(typeof(RoundEventTiming), RoundEvents)
            || NeutralCamps.Any(string.IsNullOrWhiteSpace)
            || Buildings.GroupBy(b => (b.Kind, b.Level)).Any(g => g.Count() > 1)
            || Buildings.Any(b => string.IsNullOrWhiteSpace(b.Kind) || b.Level < 0 || b.Healing < 0)
            || SiteIncomeOverrides.Any(p => p.Key < 0 || string.IsNullOrWhiteSpace(p.Value.Kind)
                || p.Value.Money < 0 || p.Value.Industry < 0 || p.Value.Food < 0)
            || VeterancyHealing.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0))
            throw new RuleViolation("turn.configuration", "Invalid world turn rules");
        foreach (var building in Buildings) building.Income.Validate();
        // Validate every settlement option before any faction is allowed to act.
        TurnSettlement.EndSite(new(), new Ew4Random(0), Settlement);
    }

    internal BuildingTurnRule? Building(CellState cell) => cell.BuildingKind == null ? null
        : Buildings.SingleOrDefault(b => b.Kind == cell.BuildingKind && b.Level == cell.BuildingLevel)
            ?? throw new RuleViolation("turn.building_definition", "Building level is missing from turn rules");

    internal Resources Income(CellState cell, int cellId)
    {
        if (cell.IncomeOverride != null) return cell.IncomeOverride;
        var income = Building(cell)?.Income ?? new Resources(0, 0, 0);
        if (!SiteIncomeOverrides.TryGetValue(cellId, out var specific) || specific.Kind != cell.BuildingKind) return income;
        return new(specific.Money ?? income.Money, specific.Industry ?? income.Industry, specific.Food ?? income.Food);
    }
}

public static class WorldTurns
{
    private static void CheckRevision(WorldState state, long expectedRevision)
    {
        state.Validate();
        if (state.Revision != expectedRevision) throw new RuleViolation("action.stale", "Turn command uses a stale revision");
        if (state.Phase == TurnPhase.Finished) throw new RuleViolation("turn.finished", "Battle has already ended");
    }

    public static WorldChange EndTurn(WorldState state, long expectedRevision, WorldTurnRules rules)
    {
        CheckRevision(state, expectedRevision); rules.Validate();
        if (state.Phase != TurnPhase.Active) throw new RuleViolation("turn.phase", "Faction turn has not started");
        if (state.PendingDialogue != null) throw new RuleViolation("action.dialogue_pending", "A dialogue is waiting");
        if (state.Factions.Single(f => f.Id == state.ActiveFaction).Defeated)
            throw new RuleViolation("action.defeated", "Active faction has been defeated");
        var notices = new List<WorldNotice>();
        var next = EndFaction(state.Copy(), rules, notices);
        next = Continue(next with { Phase = TurnPhase.EndFactionEvents }, rules, notices);
        return Commit(state, next, notices);
    }

    private static WorldState Advance(WorldState state, List<WorldNotice> notices)
    {
        if (state.Factions.All(f => f.Defeated))
        {
            notices.Add(new("battle.finished", BattleOutcome.Defeat.ToString()));
            return state with { Outcome = BattleOutcome.Defeat, Phase = TurnPhase.Finished };
        }
        var next = state;
        if (state.Factions.Single(f => f.Id == state.ActiveFaction).PlayerAi is { EndingTurn: true } control)
            next = next with { Factions = next.Factions.Select(f => f.Id == state.ActiveFaction ? f with {
                PlayerAi = control with { EndingTurn = false, Queue = Array.Empty<string>(), Steps = 0 } } : f).ToArray() };
        int current = next.Factions.ToList().FindIndex(f => f.Id == next.ActiveFaction), selected = current;
        bool wrapped = false;
        for (int step = 1; step <= next.Factions.Count; step++)
        {
            selected = (current + step) % next.Factions.Count;
            wrapped |= selected == 0;
            if (!next.Factions[selected].Defeated) break;
        }
        next = next with { ActiveFaction = next.Factions[selected].Id, Round = wrapped ? checked(next.Round + 1) : next.Round,
            Phase = wrapped ? TurnPhase.RoundEvents : TurnPhase.StartFaction };
        if (wrapped) notices.Add(new("round.started", next.ScenarioId, next.Round));
        return next;
    }

    public static WorldChange Resume(WorldState state, long expectedRevision, WorldTurnRules rules, string? dialogue = null)
    {
        CheckRevision(state, expectedRevision); rules.Validate();
        if (state.Phase == TurnPhase.Active) throw new RuleViolation("turn.phase", "No turn transition is pending");
        var next = state.Copy();
        var notices = new List<WorldNotice>();
        if (next.PendingDialogue != null)
        {
            if (dialogue == null) throw new RuleViolation("action.dialogue_pending", "A dialogue is waiting");
            next = WorldEvents.Acknowledge(next, dialogue);
            notices.Add(new("dialogue.acknowledged", dialogue));
        }
        else if (dialogue != null) throw new RuleViolation("event.dialogue_mismatch", "No dialogue is waiting");
        return Commit(state, Continue(next, rules, notices), notices);
    }

    public static WorldChange RecoverDefeatedFaction(WorldState state, WorldTurnRules rules)
    {
        if (state.Phase != TurnPhase.Active || !state.Factions.Single(f => f.Id == state.ActiveFaction).Defeated)
            return new(state, Array.Empty<WorldNotice>());
        state.Validate(); rules.Validate();
        var notices = new List<WorldNotice>();
        // The action has already settled; do not run this faction's end-turn effects again.
        var next = state.Copy() with { Phase = TurnPhase.EndFactionEvents };
        return Commit(state, Continue(next, rules, notices), notices);
    }

    private static WorldChange Commit(WorldState before, WorldState next, List<WorldNotice> notices)
    {
        next = next with { Revision = checked(before.Revision + 1) };
        next.Validate();
        return new(next, notices);
    }

    private static WorldState Continue(WorldState state, WorldTurnRules rules, List<WorldNotice> notices)
    {
        WorldState Events(WorldState world)
        {
            var events = ScenarioRules.Evaluate(world, rules.Extensions?.Scripts);
            notices.AddRange(events.Notices); return events.State;
        }
        state = Events(state);
        if (state.Phase == TurnPhase.Active && state.Factions.Single(f => f.Id == state.ActiveFaction).Defeated)
            state = state with { Phase = TurnPhase.EndFactionEvents };
        if (state.PendingDialogue != null || state.Phase == TurnPhase.Finished) return state;
        if (state.Phase == TurnPhase.EndFactionEvents)
        {
            if (rules.PlayerCamp != null && state.Factions.Single(f => f.Id == state.ActiveFaction).Defeated)
            {
                state = WorldVictory.Resolve(state, rules.PlayerCamp, rules.Victory, rules.NeutralCamps);
                if (state.Outcome != BattleOutcome.Continue)
                {
                    notices.Add(new("battle.finished", state.Outcome.ToString()));
                    return state;
                }
            }
            state = Advance(state, notices);
            if (state.Phase == TurnPhase.Finished) return state;
            state = Events(state);
            if (state.PendingDialogue != null || state.Phase == TurnPhase.Finished) return state;
        }
        if (state.Phase == TurnPhase.RoundEvents)
        {
            if (rules.RoundEvents == RoundEventTiming.BeforeFaction)
            {
                var events = WorldEvents.Process(state, TriggerKind.Round);
                state = events.State; notices.AddRange(events.Notices);
                if (state.PendingDialogue != null) return state;
            }
            state = state with { Phase = TurnPhase.StartFaction };
        }
        if (rules.PlayerCamp != null)
        {
            state = WorldVictory.Resolve(state, rules.PlayerCamp, rules.Victory, rules.NeutralCamps);
            if (state.Outcome != BattleOutcome.Continue)
            {
                notices.Add(new("battle.finished", state.Outcome.ToString()));
                return state;
            }
        }
        if (state.Phase == TurnPhase.StartFaction)
        {
            state = StartFaction(state, rules, notices);
            if (rules.RoundEvents == RoundEventTiming.AfterFaction)
                state = state with { Phase = TurnPhase.StartFactionEvents };
        }
        if (state.Phase == TurnPhase.StartFactionEvents)
        {
            var events = WorldEvents.Process(state, TriggerKind.Round);
            state = events.State; notices.AddRange(events.Notices);
            if (state.PendingDialogue != null) return state;
            state = state with { Phase = TurnPhase.Active };
        }
        state = Events(state);
        if (state.Phase == TurnPhase.Active && state.Factions.Single(f => f.Id == state.ActiveFaction).Defeated)
            state = state with { Phase = TurnPhase.EndFactionEvents };
        if (state.PendingDialogue != null || state.Phase == TurnPhase.Finished) return state;
        if (rules.PlayerCamp != null)
        {
            state = WorldVictory.Resolve(state, rules.PlayerCamp, rules.Victory, rules.NeutralCamps);
            if (state.Outcome != BattleOutcome.Continue)
            {
                notices.Add(new("battle.finished", state.Outcome.ToString()));
                return state;
            }
        }
        if (state.Phase == TurnPhase.EndFactionEvents && state.Factions.Single(f => f.Id == state.ActiveFaction).Defeated)
        {
            state = Advance(state, notices);
            return state.Phase == TurnPhase.Finished ? state : Continue(state, rules, notices);
        }
        return state;
    }

    private static WorldState EndFaction(WorldState state, WorldTurnRules rules, List<WorldNotice> notices)
    {
        string faction = state.ActiveFaction;
        var random = new Ew4Random(state.RandomState);
        var cells = state.Cells.ToArray();
        state = state with { Cells = cells };
        var occupied = new HashSet<int>(state.Units.Where(u => u.Owner == faction).Select(u => u.Cell));
        var selectedCells = Enumerable.Range(0, state.Cells.Count).Where(i => state.Cells[i].Owner == faction || occupied.Contains(i)).ToArray();
        var sites = new HexMap(state.Width, state.Height);
        bool deaths = false;
        foreach (int cellId in selectedCells)
        {
            if (state.Factions.Single(f => f.Id == faction).Defeated) break;
            var cell = state.Cells[cellId];
            bool ownsLand = cell.Owner == faction;
            var occupants = state.Units.Where(u => u.Owner == faction && u.Cell == cellId).ToArray();
            if (!ownsLand && occupants.Length == 0) continue;
            var building = rules.Building(cell);
            var result = TurnSettlement.EndSite(new()
            {
                BuildingKind = cell.BuildingKind, BuildingLevel = cell.BuildingLevel, BuildingHealing = building?.Healing ?? 0,
                Environment = cell.Environment, DegradeBuilding = ownsLand, DecayEnvironment = ownsLand,
                Units = occupants.Select(u => new UnitEndInput
                {
                    Id = u.Id, Health = u.Combatant.Health, MaximumHealth = u.Combatant.Profile.MaxHealth,
                    ConstructionTurns = u.Combatant.ConstructionTurns, Acted = u.Acted, ForceRecovery = u.ForceRecovery,
                    Shortage = state.Factions.Single(f => f.Id == faction).Shortage, HasCommander = u.CommanderId != null,
                    VeterancyHealing = rules.VeterancyHealing.TryGetValue(u.Veterancy, out int heal) ? heal : 0,
                    FieldHealing = u.Support.FieldHealing, AdjacentHealing = AdjacentHealing(state, sites, u),
                    TitleHealing = u.Support.TitleHealing, Burning = u.Burning
                }).ToArray()
            }, random, rules.Settlement);
            cells[cellId] = cell with { Environment = result.Environment, BuildingLevel = result.BuildingLevel };
            state = state with { RandomState = random.State };
            foreach (var end in result.Units)
            {
                var unit = state.Units.SingleOrDefault(u => u.Id == end.Id);
                if (unit == null) continue;
                if (end.Health == 0)
                {
                    notices.Add(new("hazard.unit_lost", unit.Id, unit.Cell));
                    var death = WorldDeaths.Remove(state, unit.Id);
                    state = death.State; notices.AddRange(death.Notices);
                    cells = state.Cells.ToArray();
                    state = state with { Cells = cells };
                    random = new Ew4Random(state.RandomState);
                    deaths = true;
                    continue;
                }
                int penalty = LocalPenalty(state, sites, cellId, unit.Owner, rules.NeutralCamps);
                int effective = TurnEconomy.Morale(unit.Morale.Baseline, unit.Morale.Duration, penalty,
                    state.Factions.Single(f => f.Id == faction).Shortage,
                    unit.Support.Logistics || unit.Combatant.Has(Ew4Combat.Skill(22)), unit.Combatant.Has(Ew4Combat.Skill(3)));
                var updated = unit with { Burning = end.Burning, ForceRecovery = end.ForceRecovery,
                    Acted = true, RemainingMovement = 0, LocalPenalty = penalty, Morale = unit.Morale with { Effective = effective },
                    Combatant = unit.Combatant with { Health = end.Health, Morale = effective } };
                state = state with { Units = state.Units.Select(u => u.Id == updated.Id ? updated : u).ToArray() };
            }
        }
        if (deaths) state = WorldDeaths.RefreshMorale(state, rules.NeutralCamps);
        notices.Add(new("faction.ended", faction));
        state = state with { RandomState = random.State };
        if (rules.Extensions != null)
        {
            var before = state;
            foreach (string id in state.Units.Where(u => u.Owner == faction).Select(u => u.Id).ToArray())
                state = rules.Extensions.Dispatch(state, "turn.end", id, new Ew4Random(state.RandomState));
            state = rules.Extensions.ExpireStatuses(state, faction);
            notices.AddRange(RuleEventBus.ChangeNotices(before, state));
        }
        return state;
    }

    public static int AdjacentHealing(WorldState state, HexMap map, UnitState unit)
    {
        var allies = new HashSet<string>(state.Factions.Where(f => WorldDiplomacy.Allied(state, f.Id, unit.Owner)).Select(f => f.Id), StringComparer.Ordinal);
        long healing = state.Units.Where(u => allies.Contains(u.Owner) && state.Cells[u.Cell].Terrain.Active
            && u.CommanderId != null && map.Distance(unit.Cell, u.Cell) == 1)
            .Sum(u => (long)u.Support.AdjacentHealing);
        return checked((int)healing);
    }

    public static Resources SiteIncome(CellState cell, int cellId, WorldTurnRules rules, UnitState? commander)
    {
        var income = rules.Income(cell, cellId);
        return commander?.CommanderId != null ? EconomyProduction.Scale(income, commander.Support.IncomeMultiplier) : income;
    }

    internal static Resources FactionIncome(WorldState state, FactionState faction, WorldTurnRules rules)
    {
        var siteUnits = state.Units.Where(u => u.CommanderId != null).GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.First());
        var production = state.Cells.Select((c, i) => (c, i)).Where(p => p.c.Owner == faction.Id).Select(p =>
            new ProductionSite(SiteIncome(p.c, p.i, rules, siteUnits.GetValueOrDefault(p.i)))).ToArray();
        var income = EconomyProduction.Calculate(production, faction.IncomeMultiplier, faction.IncomeBonus);
        return WorldArmyCapacity.IsOverLimit(state, faction.Id)
            ? new(income.Money / 2, income.Industry / 2, income.Food / 2) : income;
    }

    internal static long FactionUpkeep(WorldState state, string factionId)
    {
        long upkeep = state.Units.Where(u => u.Owner == factionId
            && !u.Support.Logistics && !u.Combatant.Has(Ew4Combat.Skill(22))).Sum(u => u.Support.FoodConsumption);
        return WorldArmyCapacity.IsOverLimit(state, factionId) ? checked(upkeep * 2) : upkeep;
    }

    private static WorldState StartFaction(WorldState state, WorldTurnRules rules, List<WorldNotice> notices)
    {
        var faction = state.Factions.Single(f => f.Id == state.ActiveFaction);
        if (faction.Defeated) throw new RuleViolation("turn.defeated", "Cannot start a defeated faction turn");
        long upkeep = FactionUpkeep(state, faction.Id);
        var income = FactionIncome(state, faction, rules);
        bool openingDebt = faction.Resources.Money < 0 || faction.Resources.Industry < 0 || faction.Resources.Food < 0;
        var economy = TurnEconomy.Start(state.Round, faction.Resources, income, upkeep, rules.ResourceCap, faction.Shortage, openingDebt);
        faction = faction with { Resources = economy.Resources, Shortage = economy.Shortage, TurnUses = new Dictionary<string, int>() };
        var map = new HexMap(state.Width, state.Height);
        // Income uses pre-expansion territory, matching the native start-turn ordering.
        var expansion = Enumerable.Range(0, state.Cells.Count).Where(i => state.Cells[i].Owner == faction.Id && rules.Building(state.Cells[i])?.ClaimsNeighbors == true).ToArray();
        var cells = JurisdictionClaims.Filter(state,
            TerritoryClaims.Expand(map, state.Cells, expansion.Where(i=>state.Cells[i].BuildingKind!="farmland").ToArray(),
                state.Units.Select(u => u.Cell).ToArray(), faction.Id), faction.Id);
        var farms = expansion.Where(i=>state.Cells[i].BuildingKind=="farmland").ToArray();
        if (farms.Length > 0)
            cells = JurisdictionClaims.Filter(state with { Cells=cells },
                TerritoryClaims.Expand(map, cells, farms, state.Units.Select(u=>u.Cell).ToArray(), faction.Id), faction.Id, requireFrontier: true);
        cells = cells.Select(c => c.Owner == faction.Id ? c with { BuildingUpgradesThisTurn = 0 } : c).ToArray();
        var expanded = state with { Cells = cells };
        var occupied = new HashSet<int>(expanded.Units.Select(u => u.Cell));
        var units = expanded.Units.Select(unit =>
        {
            if (unit.Owner != faction.Id) return unit;
            int local = LocalPenalty(expanded, map, unit.Cell, unit.Owner, rules.NeutralCamps, occupied);
            var refreshed = UnitLifecycle.Refresh(new()
            {
                Health = unit.Combatant.Health, MaximumHealth = unit.Combatant.Profile.MaxHealth, BaseHealth = unit.Combatant.Profile.MaxHealth,
                BaseMovement = rules.Extensions?.MovementBudget(unit.Combatant) ?? unit.Combatant.Profile.Movement,
                Morale = unit.Morale, LocalPenalty = local, Shortage = faction.Shortage,
                Logistics = unit.Support.Logistics || unit.Combatant.Has(Ew4Combat.Skill(22)), Leadership = unit.Combatant.Has(Ew4Combat.Skill(3)),
                ConstructionTurns = unit.Combatant.ConstructionTurns, Behavior = unit.Behavior, RestoreBehavior = unit.RestoreBehavior,
                BehaviorExpiry = unit.BehaviorExpiry, Round = state.Round
            });
            return unit with
            {
                RemainingMovement = refreshed.Movement, ExtensionMovementLimit = rules.Extensions == null ? null : refreshed.Movement,
                Acted = refreshed.Acted, Morale = refreshed.Morale, LocalPenalty = local,
                PlayerControl = unit.PlayerControl == null ? null : PlayerAi.Order(unit) with { ActionRound = 0 },
                Behavior = refreshed.Behavior, BehaviorExpiry = refreshed.BehaviorExpiry,
                Combatant = unit.Combatant with { Health = refreshed.Health, Morale = refreshed.Morale.Effective,
                    ConstructionTurns = refreshed.ConstructionTurns, AtSea = cells[unit.Cell].Terrain.Sea, BuildingKind = cells[unit.Cell].BuildingKind }
            };
        }).ToArray();
        notices.Add(new("faction.started", faction.Id));
        state = expanded with { Factions = state.Factions.Select(f => f.Id == faction.Id ? faction : f).ToArray(), Units = units, Phase = TurnPhase.Active };
        if (rules.Extensions != null)
        {
            var before = state;
            foreach (string id in state.Units.Where(u => u.Owner == faction.Id).Select(u => u.Id).ToArray())
                state = rules.Extensions.Dispatch(state, "turn.start", id, new Ew4Random(state.RandomState));
            notices.AddRange(RuleEventBus.ChangeNotices(before, state));
        }
        return state;
    }

    public static int LocalPenalty(WorldState state, HexMap map, int center, string owner, IReadOnlyCollection<string>? neutralCamps = null)
        => LocalPenalty(state, map, center, owner, neutralCamps, new HashSet<int>(state.Units.Select(u => u.Cell)));

    internal static int LocalPenalty(WorldState state, HexMap map, int center, string owner, IReadOnlyCollection<string>? neutralCamps,
        HashSet<int> occupied)
    {
        map.Check(center);
        var neighbors = Enumerable.Range(0, 6).Select(direction => map.Neighbor(center, direction)).ToArray();
        bool hostile(int cell) => cell >= 0 && state.Cells[cell].Owner is string faction && !WorldDiplomacy.Allied(state, faction, owner);
        var active = neighbors.Where(i => i >= 0 && state.Cells[i].Terrain.Active).ToArray();
        if (active.All(i => hostile(i) && (neutralCamps == null || !WorldDiplomacy.NeutralTo(state, state.Cells[i].Owner!, owner, neutralCamps)) && occupied.Contains(i))) return 2;
        for (int direction = 0; direction < 3; direction++)
            if (hostile(neighbors[direction]) && hostile(neighbors[direction + 3])
                && occupied.Contains(neighbors[direction]) && occupied.Contains(neighbors[direction + 3])) return 1;
        return 0;
    }
}
