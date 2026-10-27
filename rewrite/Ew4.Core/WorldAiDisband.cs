using System;
using System.Linq;

namespace Ew4.Core;

public static partial class WorldAi
{
    private const string EmergencyDisbandUse = "ai.emergency_disband";

    private static DisbandRules DisbandConfiguration(WorldState state, WorldProductionRules production) => new()
    {
        Enabled = production.EmergencyDisband, PlayerFaction = state.ActiveFaction,
        RecruitmentCosts = production.Offers.Values.Concat(production.Fortresses.Values)
            .Where(o => o.Template != null).GroupBy(o => o.Template!.Profile.Id)
            .ToDictionary(g => g.Key, g => g.First().Cost, StringComparer.Ordinal)
    };

    private static UnitState? EmergencyDisbandCandidate(WorldState state, WorldAiRules ai, WorldProductionRules production)
    {
        var faction = state.Factions.Single(f => f.Id == state.ActiveFaction);
        if (!production.EmergencyDisband || faction.Human || faction.Shortage < 2
            || faction.TurnUses.ContainsKey(EmergencyDisbandUse) || !WorldArmyCapacity.IsOverLimit(state, faction.Id)
            || WorldDiplomacy.Dormant(state, faction.Id, ai.NeutralCamps)) return null;
        var army = state.Units.Where(u => u.Owner == faction.Id).ToArray();
        // Keep a fighting force even when all territory capacity has been lost.
        if (army.Length <= 8 || !army.Any(u => u.Morale.Effective < 0)) return null;
        var enemies = state.Factions.Where(f => !f.Defeated && WorldDiplomacy.Hostile(state, faction.Id, f.Id, ai.NeutralCamps))
            .Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var front = state.Cells.Select((cell, index) => (cell, index))
            .Where(p => p.cell.Terrain.Active && p.cell.Owner != null && enemies.Contains(p.cell.Owner))
            .Select(p => p.index).Concat(state.Units.Where(u => enemies.Contains(u.Owner)).Select(u => u.Cell)).Distinct().ToArray();
        if (front.Length == 0) return null;
        var costs = DisbandConfiguration(state, production).RecruitmentCosts;
        var map = new HexMap(state.Width, state.Height);
        return army.Where(u => u.CommanderId == null && !u.Objective && !u.Acted && u.Combatant.ConstructionTurns == 0
                && u.Combatant.Profile.Category != "fort" && !production.HoldBehaviors.Contains(u.Behavior)
                && !u.Support.Logistics && !u.Combatant.Has(Ew4Combat.Skill(22)) && u.Support.FoodConsumption > 0
                && costs.ContainsKey(u.Combatant.Profile.Id) && !state.Cells[u.Cell].Objective
                && state.Cells[u.Cell].BuildingKind == null && state.Cells[u.Cell].Owner == faction.Id)
            .Select(u => (Unit: u, Distance: front.Min(cell => map.Distance(u.Cell, cell))))
            .Where(p => p.Distance >= 6).OrderByDescending(p => p.Distance)
            .ThenBy(p => (decimal)p.Unit.Combatant.Health / p.Unit.Combatant.Profile.MaxHealth)
            .ThenByDescending(p => p.Unit.Support.FoodConsumption).ThenBy(p => p.Unit.Id, StringComparer.Ordinal)
            .Select(p => p.Unit).FirstOrDefault();
    }
}
