using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

internal sealed record WorldDeathResult(WorldState State, IReadOnlyList<WorldNotice> Notices, bool FactionDefeated);

internal static class WorldDeaths
{
    internal static WorldDeathResult Remove(WorldState state, string unitId, string? successor = null, bool checkSurvival = true)
    {
        var unit = state.Units.Single(u => u.Id == unitId);
        var notices = new List<WorldNotice> { new("unit.destroyed", unitId) };
        var next = state with
        {
            DestroyedScenarioUnits = state.DestroyedScenarioUnits.Append(unit.Id).Distinct(StringComparer.Ordinal).ToArray(),
            DeadScenarioCommanders = unit.CommanderId == null ? state.DeadScenarioCommanders : state.DeadScenarioCommanders.Append(unit.CommanderId).Distinct(StringComparer.Ordinal).ToArray(),
            Units = state.Units.Where(u => u.Id != unitId).ToArray(),
            Cells = unit.Combatant.Profile.Category == "fort" && successor != null && successor != unit.Owner
                ? JurisdictionClaims.DestroyFort(state, unit.Cell, successor) : state.Cells,
            Commanders = state.Commanders.Select(c => c.Id == unit.CommanderId ? c with { Withdrawn = true } : c).ToArray()
        };
        if (unit.CommanderId != null) notices.Add(new("commander.withdrawn", unit.CommanderId));
        var events = WorldEvents.DuringAction(next, new(TriggerKind.UnitDestroyed, unitId, unit.Cell,
            state.Cells[unit.Cell].Owner, HasCommander: unit.CommanderId != null));
        next = events.State; notices.AddRange(events.Notices);
        bool defeated = false;
        var faction = next.Factions.Single(f => f.Id == unit.Owner);
        if (checkSurvival && !faction.Defeated)
        {
            var armyCells = new HashSet<int>(next.Units.Where(u => u.Owner == unit.Owner).Select(u => u.Cell));
            var assets = next.Cells.Select((c, i) => (c, i)).Where(p => p.c.Owner == unit.Owner)
                .Select(p => new SurvivalAsset(armyCells.Contains(p.i), p.c.BuildingKind)).Append(new SurvivalAsset(armyCells.Count > 0, null));
            if (FactionSurvival.Defeated(assets, faction.Survival))
            {
                string? recipient = successor != null && next.Factions.Any(f => f.Id == successor && !f.Defeated) ? successor : null;
                var transfer = WorldEvents.Surrender(next, unit.Owner, recipient);
                next = transfer.State; notices.AddRange(transfer.Notices); defeated = true;
            }
        }
        var site = next.Cells[unit.Cell];
        if (site.Terrain.Sea && site.BuildingKind == null && !next.Units.Any(u => u.Cell == unit.Cell))
            next = next with { Cells = next.Cells.Select((c, i) => i == unit.Cell ? c with { Owner = null } : c).ToArray() };
        return new(next, notices, defeated);
    }

    internal static WorldState RefreshMorale(WorldState state, IReadOnlyCollection<string> neutralCamps)
    {
        var map = new HexMap(state.Width, state.Height);
        var factions = state.Factions.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var occupied = new HashSet<int>(state.Units.Select(u => u.Cell));
        return state with { Units = state.Units.Select(unit =>
        {
            int local = WorldTurns.LocalPenalty(state, map, unit.Cell, unit.Owner, neutralCamps, occupied);
            int morale = TurnEconomy.Morale(unit.Morale.Baseline, unit.Morale.Duration, local, factions[unit.Owner].Shortage,
                unit.Support.Logistics || unit.Combatant.Has(Ew4Combat.Skill(22)), unit.Combatant.Has(Ew4Combat.Skill(3)));
            return unit with { LocalPenalty = local, Morale = unit.Morale with { Effective = morale }, Combatant = unit.Combatant with { Morale = morale } };
        }).ToArray() };
    }
}
