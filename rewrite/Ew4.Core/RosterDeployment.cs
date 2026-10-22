using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record RosterDeploymentState(string Faction, int? Limit, IReadOnlyList<string> CommanderIds);
public sealed record OwnedDeploymentTemplate(Commander Definition, CommanderRuntime Runtime);
public sealed record RosterDeploymentRules
{
    public AccountCommanderRules Account { get; init; } = new();
    public WorldCommanderRules Assignment { get; init; } = new();
    public IReadOnlyDictionary<int, int> RankHealth { get; init; } = new Dictionary<int, int>();

    public OwnedDeploymentTemplate Resolve(OwnedCommander source, string owner)
    {
        Account.Validate(); Assignment.Validate();
        var definition = AccountCommanders.Resolve(source, Account);
        if (string.IsNullOrWhiteSpace(owner) || RankHealth.Any(p => p.Key < 0 || p.Value < 0)
            || !RankHealth.TryGetValue(source.Learning.Rank.Level, out int health)
            || !Assignment.TitleHealing.ContainsKey(source.Learning.Title.Level))
            throw new RuleViolation("session.roster_definition", "Missing rank or title stage for owned commander");
        int? Percent(int effect) => definition.Items.Where(i => i.Effect == Ew4Commander.Effect(effect)).Select(i => (int?)i.Value).FirstOrDefault();
        return new(definition, new() { Id = source.Id, Owner = owner, Rank = source.Learning.Rank, Title = source.Learning.Title,
            RankHealthBonus = health, MaximumTrainingLevel = source.Training,
            RankExperiencePercent = Percent(1), TitleExperiencePercent = Percent(2) });
    }
}

public static class RosterDeployment
{
    public static bool IsDeploymentLocation(WorldState world, UnitState unit)
    {
        var cell = world.Cells[unit.Cell];
        return cell.Terrain.Active && (cell.Owner == unit.Owner
            || cell.Terrain.Sea && unit.Combatant.Profile.Category == "naval");
    }

    public static bool CanDeploy(GameSession state, string unitId, string commanderId, RosterDeploymentRules rules)
    {
        var world = state.World;
        var unit = world.Units.SingleOrDefault(u => u.Id == unitId);
        var quota = state.RosterDeployments.SingleOrDefault(q => q.Faction == world.ActiveFaction);
        return world.Phase == TurnPhase.Active && world.PendingDialogue == null && world.ActiveFaction == state.PlayerFaction
            && world.Factions.Any(f => f.Id == world.ActiveFaction && f.Human && !f.Defeated)
            && unit != null && unit.Owner == world.ActiveFaction && unit.CommanderId == null
            && IsDeploymentLocation(world, unit)
            && quota != null && !(quota.CommanderIds.Count >= quota.Limit)
            && state.Account.Roster.Any(c => c.Id == commanderId)
            && world.Commanders.All(c => c.Id != commanderId) && state.TavernCandidates.All(c => c.CommanderId != commanderId)
            && state.RosterDeployments.All(q => !q.CommanderIds.Contains(commanderId))
            && (rules.Assignment.DeploymentBuilding != BuildingRequirement.Required || world.Cells[unit.Cell].BuildingKind != null)
            && (rules.Assignment.DeploymentBuilding != BuildingRequirement.Forbidden || world.Cells[unit.Cell].BuildingKind == null);
    }

    public static SessionChange Deploy(GameSession state, long expectedRevision, string unitId, string commanderId, RosterDeploymentRules rules)
    {
        state.Validate();
        if (state.Revision != expectedRevision) throw new RuleViolation("session.revision", "Stale session command");
        var faction = WorldActions.Ready(state.World, state.World.Revision);
        if (!CanDeploy(state, unitId, commanderId, rules))
            throw new RuleViolation("session.roster_unavailable", "Owned commander or deployment quota/location is unavailable");
        var owned = state.Account.Roster.Single(c => c.Id == commanderId);
        var unit = state.World.Units.Single(u => u.Id == unitId);
        var template = rules.Resolve(owned, faction.Id);
        var definitions = rules.Assignment.Definitions.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (!definitions.TryAdd(commanderId, template.Definition))
            throw new RuleViolation("session.roster_definition", "Owned instance collides with an existing assignment definition");
        var world = state.World with { Commanders = state.World.Commanders.Concat(new[] { template.Runtime }).ToArray() };
        var assigned = WorldCommanders.Assign(world, world.Revision, unitId, commanderId, rules.Assignment with { Definitions = definitions });
        var next = (state with { Revision = checked(state.Revision + 1), World = assigned.State,
            RosterDeployments = state.RosterDeployments.Select(q => q.Faction != faction.Id ? q
                : q with { CommanderIds = q.CommanderIds.Concat(new[] { commanderId }).ToArray() }).ToArray() }).Copy();
        next.Validate();
        return new(next, assigned.Notices.Concat(new[] { new WorldNotice("roster.deployed", commanderId, unit.Cell) }).ToArray());
    }
}
