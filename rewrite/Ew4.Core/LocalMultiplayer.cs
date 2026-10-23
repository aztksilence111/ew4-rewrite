using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record LocalMultiplayerState(string MainFaction, IReadOnlyList<string> HumanFactions, string? WinnerFaction = null,
    string? InitialObjectiveOwner = null, bool HadObjectives = false, bool ShareAlliedVictory = false);

// Campaign allies share victory; conquest countries retain independent results.
public static class LocalMultiplayerGame
{
    private static bool SharesVictory(WorldState world, string first, string second) => first == second
        || world.LocalMultiplayer?.ShareAlliedVictory == true && WorldDiplomacy.Allied(world, first, second);

    public static bool Won(WorldState world, string faction, string winner) => SharesVictory(world, faction, winner);

    private static BattleOutcome Outcome(WorldState world, string winner) => world.LocalMultiplayer!.HumanFactions
        .Any(id => Won(world, id, winner)) ? BattleOutcome.Victory : BattleOutcome.Defeat;

    public static void Validate(WorldState world)
    {
        var local = world.LocalMultiplayer;
        if (local == null) return;
        if (local.HumanFactions == null || local.HumanFactions.Count == 0
            || local.HumanFactions.Distinct(StringComparer.Ordinal).Count() != local.HumanFactions.Count
            || !local.HumanFactions.Contains(local.MainFaction)
            || local.HumanFactions.Any(id => !world.Factions.Any(f => f.Id == id))
            || local.InitialObjectiveOwner != null && !world.Factions.Any(f => f.Id == local.InitialObjectiveOwner)
            || world.Factions.Any(f => f.Human != local.HumanFactions.Contains(f.Id))
            || !local.HumanFactions.SequenceEqual(world.Factions.Where(f => f.Human).Select(f => f.Id))
            || local.WinnerFaction != null && (!world.Factions.Any(f => f.Id == local.WinnerFaction && !f.Defeated)
                || world.Phase != TurnPhase.Finished
                || world.Outcome != Outcome(world, local.WinnerFaction)))
            throw new RuleViolation("local.state", "Invalid local multiplayer selection or winner");
    }

    public static string? Winner(WorldState world, VictoryRules rules)
    {
        var local = world.LocalMultiplayer;
        if (local == null) return null;
        if (local.WinnerFaction != null) return local.WinnerFaction;
        var custom = world.ScenarioVictory ?? rules.Custom;
        var objectiveOwners = ObjectiveOwners(world, rules.UnitObjectiveOwnership);
        // Iteration uses the saved turn order, including AI countries, for deterministic ties.
        foreach (var candidate in world.Factions.Where(f => !f.Defeated))
        {
            if (custom != null)
            {
                // Project the candidate's winning side without changing battle diplomacy.
                var projected = world with { LocalMultiplayer = null, ScenarioPlayerFaction = candidate.Id,
                    DiplomaticRelations = Array.Empty<DiplomaticRelation>(), NeutralHostilities = Array.Empty<NeutralHostility>(),
                    Factions = world.Factions.Select(f => f with {
                        Camp = SharesVictory(world, candidate.Id, f.Id) ? candidate.Id : f.Id,
                        Human = f.Id == candidate.Id }).ToArray() };
                if (ExtensionVictory.Evaluate(projected, candidate.Id, Array.Empty<string>(),
                    custom with { Lose = null }, rules.Scripts) == BattleOutcome.Victory) return candidate.Id;
            }
            else
            {
                bool soleSurvivor = world.Factions.All(f => SharesVictory(world, candidate.Id, f.Id) || f.Defeated);
                // A defending country can start with every marked objective in a campaign.
                // Initial ownership is not a completed conquest. Resolve removes this guard
                // once an objective changes hands, allowing the defender to reclaim them.
                bool initialOwner = local.InitialObjectiveOwner is string initial && SharesVictory(world, candidate.Id, initial);
                if (soleSurvivor || objectiveOwners.Length > 0 && objectiveOwners.All(owner => owner != null && SharesVictory(world, candidate.Id, owner))
                    && !initialOwner
                    || local.HadObjectives && objectiveOwners.Length == 0 && candidate.Id == world.ActiveFaction
                        && !initialOwner)
                    return candidate.Id;
            }
        }
        return null;
    }

    private static string?[] ObjectiveOwners(WorldState world, ObjectiveUnitOwnership ownership)
    {
        return world.Cells.Where(c => c.Objective).Select(c => c.Owner)
            .Concat(world.Units.Where(u => u.Objective).Select(u => ownership == ObjectiveUnitOwnership.UnitOwner
                ? u.Owner : world.Cells[u.Cell].Owner)).Distinct().ToArray();
    }

    public static string? InitialObjectiveOwner(WorldState world, ObjectiveUnitOwnership ownership = ObjectiveUnitOwnership.UnitOwner)
    {
        var owners = ObjectiveOwners(world, ownership);
        return owners.Length > 0 && owners[0] is string first
            && owners.All(owner => owner != null && SharesVictory(world, first, owner)) ? first : null;
    }

    public static WorldState Finish(WorldState world, string winner)
    {
        var local = world.LocalMultiplayer!;
        if (local.WinnerFaction != null || !world.Factions.Any(f => f.Id == winner && !f.Defeated)) return world;
        return world with { LocalMultiplayer = local with { WinnerFaction = winner }, Phase = TurnPhase.Finished,
            Outcome = Outcome(world, winner) };
    }

    public static WorldState Resolve(WorldState world, VictoryRules rules)
    {
        if (world.LocalMultiplayer == null || world.PendingDialogue != null) return world;
        if (world.LocalMultiplayer.InitialObjectiveOwner is string initial
            && ObjectiveOwners(world, rules.UnitObjectiveOwnership).Any(owner => owner == null || !SharesVictory(world, initial, owner)))
            world = world with { LocalMultiplayer = world.LocalMultiplayer with { InitialObjectiveOwner = null } };
        string? winner = Winner(world, rules);
        return winner == null ? world : Finish(world, winner);
    }

    public static GameSession SyncPlayer(GameSession session)
    {
        var world = session.World;
        if (world.LocalMultiplayer == null) return session;
        var active = world.Factions.Single(f => f.Id == world.ActiveFaction);
        string player = active.Human && !active.Defeated ? active.Id : session.PlayerFaction;
        return session with { PlayerFaction = player, World = world with { ScenarioPlayerFaction = player } };
    }
}
