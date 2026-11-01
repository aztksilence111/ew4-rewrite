using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record UnitCommanderBase(Combatant Combatant, UnitSupport Support);
public sealed record CommanderDeploymentOffer(string Id, string CommanderId, string Owner, int Cell, int AvailableRound,
    Resources Cost, string TurnGroup = "commander.deployment", int? PerTurnLimit = null);
public sealed record WorldCommanderRules
{
    public LegacySkillParameters? LegacySkills { get; init; }
    public RuleEventBus? Extensions { get; init; }
    public IReadOnlyDictionary<string, Commander> Definitions { get; init; } = new Dictionary<string, Commander>();
    public IReadOnlyDictionary<int, int> TitleHealing { get; init; } = new Dictionary<int, int>();
    public IReadOnlyCollection<string> ImmobileCategories { get; init; } = new[] { "fort" };
    public BuildingRequirement DeploymentBuilding { get; init; } = BuildingRequirement.Required;
    public bool AllowReplacement { get; init; }
    public bool RefillAvailableMovement { get; init; }
    public void Validate()
    {
        if (!Enum.IsDefined(typeof(BuildingRequirement), DeploymentBuilding) || ImmobileCategories.Any(string.IsNullOrWhiteSpace)
            || TitleHealing.Any(p => p.Key < 0 || p.Value < 0)
            || Definitions.Any(p => p.Key != p.Value.Id || string.IsNullOrWhiteSpace(p.Key) || p.Value.Movement < 0
                || p.Value.Stars.Any(s => string.IsNullOrWhiteSpace(s.Key) || s.Value < 0) || p.Value.Skills.Any(string.IsNullOrWhiteSpace)))
            throw new RuleViolation("commander.configuration", "Invalid commander definitions or assignment policy");
    }
}

public static class CommanderAttachment
{
    public static int ResizeHealth(int health, int oldMaximum, int baseMaximum, int newMaximum, bool legacyDoubleRounding = false)
    {
        if (health < 0 || health > oldMaximum || oldMaximum < 1 || baseMaximum < 1 || newMaximum < 1)
            throw new RuleViolation("commander.health", "Invalid health conversion");
        long result = legacyDoubleRounding ? (long)health * baseMaximum / oldMaximum * newMaximum / baseMaximum
            : (long)health * newMaximum / oldMaximum;
        return (int)Math.Min(newMaximum, legacyDoubleRounding || health == 0 ? result : Math.Max(1, result));
    }

    public static int RebaseMovement(int remaining, int oldBudget, int newBudget, bool refill = false)
    {
        if (remaining < 0 || remaining > oldBudget || oldBudget < 0 || newBudget < 0)
            throw new RuleViolation("commander.movement", "Invalid movement conversion");
        return remaining == 0 ? 0 : refill ? newBudget : Math.Max(0, newBudget - (oldBudget - remaining));
    }
}

public static class WorldCommanders
{
    private static Combatant Unassigned(UnitState unit)
    {
        if (unit.CommanderId == null) return unit.Combatant;
        var original = unit.CommanderBase?.Combatant
            ?? throw new RuleViolation("commander.base_missing", "Assignment base must be imported before replacing this commander");
        return unit.Combatant with
        {
            Skills = original.Skills, SkillSlots = original.SkillSlots, Stars = original.Stars, AttackBonus = original.AttackBonus, DefenseBonus = original.DefenseBonus,
            FullAttackRolls = original.FullAttackRolls, SeaImmunity = original.SeaImmunity, Geography = original.Geography, FireImmune = original.FireImmune,
            Profile = unit.Combatant.Profile with { MaxHealth = original.Profile.MaxHealth, Movement = original.Profile.Movement }
        };
    }

    public static WorldChange Assign(WorldState state, long expectedRevision, string unitId, string commanderId, WorldCommanderRules rules)
    {
        var faction = WorldActions.Ready(state, expectedRevision);
        return AssignForOwner(state, unitId, commanderId, faction.Id, rules);
    }

    internal static WorldChange AssignForOwner(WorldState state, string unitId, string commanderId, string owner, WorldCommanderRules rules)
    {
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        var commander = state.Commanders.SingleOrDefault(c => c.Id == commanderId);
        if (unit == null || unit.Owner != owner || commander == null || commander.Owner != owner || commander.Withdrawn
            || state.Units.Any(u => u.CommanderId == commanderId))
            throw new RuleViolation("commander.unavailable", "Commander or receiving unit is unavailable");
        if (unit.CommanderId != null && !rules.AllowReplacement)
            throw new RuleViolation("commander.occupied", "Commander replacement is disabled");
        if (!rules.Definitions.TryGetValue(commanderId, out var definition)) throw new RuleViolation("commander.definition", "Missing commander definition");
        var original = Unassigned(unit);
        var support = unit.CommanderBase?.Support ?? unit.Support;
        var effects = Ew4Commander.Resolve(definition, original.Profile.Category, rules.LegacySkills);
        var attached = Ew4Commander.Attach(original, definition, rules.LegacySkills);
        int maximum = checked(original.Profile.MaxHealth + commander.RankHealthBonus);
        int movement = rules.ImmobileCategories.Contains(original.Profile.Category) ? 0 : attached.Profile.Movement;
        int health = CommanderAttachment.ResizeHealth(unit.Combatant.Health, unit.Combatant.Profile.MaxHealth, original.Profile.MaxHealth, maximum);
        attached = attached with { Health = health, Profile = attached.Profile with { MaxHealth = maximum, Movement = movement } };
        int oldBudget = unit.ExtensionMovementLimit ?? unit.Combatant.Profile.Movement;
        int budget = rules.Extensions?.MovementBudget(attached) ?? Math.Max(0, checked(movement + oldBudget - unit.Combatant.Profile.Movement));
        var updated = unit with
        {
            CommanderId = commanderId, CommanderBase = new(original with { Health = original.Profile.MaxHealth }, support),
            RemainingMovement = CommanderAttachment.RebaseMovement(unit.RemainingMovement, oldBudget, budget, rules.RefillAvailableMovement),
            ExtensionMovementLimit = unit.ExtensionMovementLimit.HasValue || rules.Extensions != null ? budget : null,
            Combatant = attached,
            Support = support with
            {
                FieldHealing = checked(support.FieldHealing + effects.FieldHealing),
                TitleHealing = checked(support.TitleHealing + (rules.TitleHealing.TryGetValue(commander.Title.Level, out int heal) ? heal : 0)),
                AdjacentHealing = checked(support.AdjacentHealing + effects.AdjacentHealing),
                AdjacentAttack = checked(support.AdjacentAttack + effects.AdjacentAttack), AdjacentDefense = checked(support.AdjacentDefense + effects.AdjacentDefense),
                Logistics = support.Logistics || effects.FoodExempt, IncomeMultiplier = support.IncomeMultiplier * effects.EconomicMultiplier
            }
        };
        updated = RefreshMorale(state, updated);
        var next = (state with { Units = state.Units.Select(u => u.Id == unitId ? updated : u).ToArray(), Revision = checked(state.Revision + 1) }).Copy();
        next.Validate();
        var notices = new List<WorldNotice>();
        if (unit.CommanderId != null) notices.Add(new("commander.detached", unit.CommanderId, unit.Cell));
        notices.Add(new("commander.assigned", commanderId, unit.Cell));
        return new(next, notices);
    }

    public static WorldChange Detach(WorldState state, long expectedRevision, string unitId, WorldCommanderRules rules)
    {
        var faction = WorldActions.Ready(state, expectedRevision);
        return DetachForOwner(state, unitId, faction.Id, rules);
    }

    internal static WorldChange DetachForOwner(WorldState state, string unitId, string owner, WorldCommanderRules rules)
    {
        rules.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (unit == null || unit.Owner != owner || unit.CommanderId == null)
            throw new RuleViolation("commander.unavailable", "No active faction commander to detach");
        var original = Unassigned(unit);
        int oldBudget = unit.ExtensionMovementLimit ?? unit.Combatant.Profile.Movement;
        int budget = rules.Extensions?.MovementBudget(original)
            ?? Math.Max(0, checked(original.Profile.Movement + oldBudget - unit.Combatant.Profile.Movement));
        var detached = unit with
        {
            Combatant = original with { Health = CommanderAttachment.ResizeHealth(unit.Combatant.Health, unit.Combatant.Profile.MaxHealth,
                original.Profile.MaxHealth, original.Profile.MaxHealth) },
            Support = unit.CommanderBase!.Support, CommanderId = null, CommanderBase = null,
            RemainingMovement = CommanderAttachment.RebaseMovement(unit.RemainingMovement, oldBudget, budget, rules.RefillAvailableMovement),
            ExtensionMovementLimit = unit.ExtensionMovementLimit.HasValue || rules.Extensions != null ? budget : null
        };
        detached = RefreshMorale(state, detached);
        var next = (state with { Units = state.Units.Select(u => u.Id == unitId ? detached : u).ToArray(), Revision = checked(state.Revision + 1) }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice("commander.detached", unit.CommanderId, unit.Cell) });
    }

    private static UnitState RefreshMorale(WorldState state, UnitState unit)
    {
        int morale = TurnEconomy.Morale(unit.Morale.Baseline, unit.Morale.Duration, unit.LocalPenalty,
            state.Factions.Single(f => f.Id == unit.Owner).Shortage, unit.Support.Logistics || unit.Combatant.Has(Ew4Combat.Skill(22)), unit.Combatant.Has(Ew4Combat.Skill(3)));
        return unit with { Morale = unit.Morale with { Effective = morale }, Combatant = unit.Combatant with { Morale = morale } };
    }

    public static bool CanDeploy(WorldState state, string unitId, string offerId, WorldCommanderRules rules)
    {
        var faction = WorldActions.Ready(state, state.Revision); rules.Validate();
        var offer = state.CommanderOffers.SingleOrDefault(o => o.Id == offerId);
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        return offer != null && unit != null && CanDeployValidated(state, faction, unit, offer, rules);
    }

    internal static bool CanDeployValidated(WorldState state, FactionState faction, UnitState unit, CommanderDeploymentOffer offer, WorldCommanderRules rules)
    {
        var cell = state.Cells[unit.Cell];
        var commander = state.Commanders.Single(c => c.Id == offer.CommanderId);
        return offer.Owner == faction.Id && unit.Owner == faction.Id && offer.Cell == unit.Cell && offer.AvailableRound <= state.Round
            && cell.Owner == faction.Id && cell.Terrain.Active && !commander.Withdrawn && commander.Owner == faction.Id
            && rules.Definitions.ContainsKey(commander.Id) && (unit.CommanderId == null || rules.AllowReplacement && unit.CommanderBase != null)
            && !state.Units.Any(u => u.CommanderId == commander.Id)
            && (rules.DeploymentBuilding != BuildingRequirement.Required || cell.BuildingKind != null)
            && (rules.DeploymentBuilding != BuildingRequirement.Forbidden || cell.BuildingKind == null)
            && (offer.PerTurnLimit == null || !faction.TurnUses.TryGetValue(offer.TurnGroup, out int used) || used < offer.PerTurnLimit)
            && AiProduction.AffordableCopies(faction.Resources, offer.Cost) is not 0;
    }

    public static WorldChange Deploy(WorldState state, long expectedRevision, string unitId, string offerId, WorldCommanderRules rules)
    {
        var faction = WorldActions.Ready(state, expectedRevision); rules.Validate();
        var offer = state.CommanderOffers.SingleOrDefault(o => o.Id == offerId);
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (offer == null || unit == null || !CanDeployValidated(state, faction, unit, offer, rules))
            throw new RuleViolation("commander.deployment", "Commander offer is not available at this unit and round");
        var assigned = Assign(state, state.Revision, unitId, offer.CommanderId, rules);
        var uses = faction.TurnUses.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        uses[offer.TurnGroup] = checked((uses.TryGetValue(offer.TurnGroup, out int previous) ? previous : 0) + 1);
        var funds = faction.Resources;
        var next = assigned.State with
        {
            Factions = assigned.State.Factions.Select(f => f.Id != faction.Id ? f : f with
                { Resources = new(funds.Money - offer.Cost.Money, funds.Industry - offer.Cost.Industry, funds.Food - offer.Cost.Food), TurnUses = uses }).ToArray(),
            CommanderOffers = assigned.State.CommanderOffers.Where(o => o.CommanderId != offer.CommanderId).ToArray()
        };
        next.Validate();
        return new(next, assigned.Notices.Concat(new[] { new WorldNotice("commander.deployed", offer.CommanderId, unit.Cell) }).ToArray());
    }
}
