using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record BuildingDefenseRule(string Kind, int Level, int Percent);
public sealed record CombatAura(int Attack, int Defense);
public sealed record CombatContextRules
{
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> TerrainDefense { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, int>>();
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> InstallationDefense { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, int>>();
    public IReadOnlyList<BuildingDefenseRule> Buildings { get; init; } = Array.Empty<BuildingDefenseRule>();
    public IReadOnlyDictionary<string, int> VeterancyArmor { get; init; } = new Dictionary<string, int>();
    public bool UseResolvedFallback { get; init; } = true;
    public void Validate()
    {
        foreach (var table in new[] { TerrainDefense, InstallationDefense })
            if (table.Any(row => string.IsNullOrWhiteSpace(row.Key) || row.Value.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0 || p.Value > 100)))
                throw new RuleViolation("context.configuration", "Invalid contextual defense table");
        if (Buildings.Any(b => string.IsNullOrWhiteSpace(b.Kind) || b.Level < 0 || b.Percent < 0 || b.Percent > 100)
            || Buildings.GroupBy(b => (b.Kind, b.Level)).Any(g => g.Count() > 1)
            || VeterancyArmor.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0))
            throw new RuleViolation("context.configuration", "Invalid building defense or armor configuration");
    }
}

public static class CombatContext
{
    public static CombatAura Auras(WorldState state, HexMap map, UnitState unit)
    {
        if (WorldUnitLayers.Air(state, unit)) return new(0, 0);
        var allies = new HashSet<string>(state.Factions.Where(f => WorldDiplomacy.Allied(state, f.Id, unit.Owner)).Select(f => f.Id), StringComparer.Ordinal);
        var adjacent = state.Units.Where(u => !WorldUnitLayers.Air(state, u) && u.CommanderId != null && allies.Contains(u.Owner) && state.Cells[u.Cell].Terrain.Active
            && map.Distance(unit.Cell, u.Cell) == 1).ToArray();
        return new(checked((int)adjacent.Sum(u => (long)u.Support.AdjacentAttack)), checked((int)adjacent.Sum(u => (long)u.Support.AdjacentDefense)));
    }

    public static Combatant Resolve(WorldState state, UnitState unit, string opponentCategory, CombatContextRules rules, bool includeAiBonus = true)
    {
        rules.Validate();
        var cell = state.Cells[unit.Cell];
        var owner = state.Factions.Single(f => f.Id == unit.Owner);
        float basis = unit.Combatant.Evasion;
        float withoutBuilding = unit.Combatant.EvasionIgnoringBuilding;
        bool air = WorldUnitLayers.Air(state, unit);
        float Percent(IReadOnlyDictionary<string, int> row) => row.TryGetValue(opponentCategory, out int amount) ? amount / 100f
            : throw new RuleViolation("context.category", "Defense configuration has no entry for this attacker category");
        if (!air && cell.BuildingKind != null)
        {
            var building = rules.Buildings.SingleOrDefault(b => b.Kind == cell.BuildingKind && b.Level == cell.BuildingLevel);
            if (building != null) basis = building.Percent / 100f;
            else if (!rules.UseResolvedFallback) throw new RuleViolation("context.building", "Missing building defense configuration");
        }
        else if (!air)
        {
            if (rules.TerrainDefense.TryGetValue(cell.DefenseTerrain, out var terrain)) basis = Percent(terrain);
            else if (!rules.UseResolvedFallback) throw new RuleViolation("context.terrain", "Missing terrain defense configuration");
            if (cell.InstallationKind != null)
            {
                if (!rules.InstallationDefense.TryGetValue(cell.InstallationKind, out var installation))
                    throw new RuleViolation("context.installation", "Missing installation defense configuration");
                basis = Math.Max(basis, Percent(installation));
            }
            withoutBuilding = basis;
        }
        float bonus = includeAiBonus && !owner.Human ? owner.AiEvasionBonus : 0;
        var aura = Auras(state, new(state.Width, state.Height), unit);
        return unit.Combatant with
        {
            RuleLocation = new(unit.Cell, cell.DefenseTerrain, cell.BuildingKind, cell.BuildingLevel, cell.InstallationKind),
            AtSea = !air && cell.Terrain.Sea, BuildingKind = air ? null : cell.BuildingKind, Morale = unit.Morale.Effective,
            Evasion = Math.Min(1f, basis + bonus), EvasionIgnoringBuilding = Math.Min(1f, withoutBuilding + bonus),
            Armor = rules.VeterancyArmor.TryGetValue(unit.Veterancy, out int armor) ? armor : unit.Combatant.Armor,
            AttackBonus = checked(unit.Combatant.AttackBonus + aura.Attack), DefenseBonus = checked(unit.Combatant.DefenseBonus + aura.Defense)
        };
    }
}
