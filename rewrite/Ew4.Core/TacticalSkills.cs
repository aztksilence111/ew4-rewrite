using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

// Optional Mod rules. No character IDs, country identities or Mod skill IDs belong here.
public sealed record ManeuverRule
{
    public int MaximumOthers { get; init; } = 3;
    public int Movement { get; init; } = 2;
    public int ProtectionTurns { get; init; } = 2;
    public int NetDistance { get; init; } = 2;
    public double AttackPenalty { get; init; } = .15;
    public double Counteroffensive { get; init; } = .20;
    public double Evasion { get; init; } = .15;
    public double EvasionCeiling { get; init; } = .50;
    public double IgnoreRetention { get; init; } = .50;
    public string[] RecipientCategories { get; init; } = new[] { "infantry", "cavalry" };
    public string[] ExcludedAbilities { get; init; } = Array.Empty<string>();
}
public sealed record TerritoryAuraRule
{
    public int Radius { get; init; } = 1;
    public double Attack { get; init; } = .15;
    public double Defense { get; init; } = .15;
}
public sealed record RecoveryNetworkRule
{
    public string[] Buildings { get; init; } = new[] { "city", "industry", "stable", "farmland" };
    public string[] Categories { get; init; } = new[] { "infantry", "cavalry" };
    public string[] ExcludedAbilities { get; init; } = Array.Empty<string>();
    public int FacilityRadius { get; init; } = 1;
    public int PerFacility { get; init; } = 2;
    public int FacilityHealing { get; init; } = 4;
    public int AdjacentRadius { get; init; } = 1;
    public int AdjacentHealing { get; init; } = 6;
    public string FacilityEffect { get; init; } = "";
    public string AdjacentEffect { get; init; } = "";
}
public sealed record ManeuverGrant(string Skill, string Instance, string Source, string Owner, string? SourceCommander,
    string? RecipientCommander, int StartRound, int StartCell, int? EndCell = null, bool CounterReady = false, bool Used = false);
public sealed record TacticalCombatBonus(double Attack = 0, double Defense = 0, double MainAttackPenalty = 0,
    double FirstAttack = 0, double Evasion = 0, double EvasionCeiling = .5, double IgnoreRetention = .5,
    bool GroundMobile = false);

public static class TacticalSkills
{
    private static bool Fraction(double n) => !double.IsNaN(n) && !double.IsInfinity(n) && n >= 0 && n <= 1;
    private static bool Names(string[]? names) => names != null && names.Length is > 0 and <= 64
        && names.All(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 128) && names.Distinct().Count() == names.Length;
    public static void Validate(ExtensionSkill skill, int apiVersion)
    {
        if (skill.Maneuver == null && skill.TerritoryAura == null && skill.RecoveryNetwork == null) return;
        if (apiVersion < 3 || skill.Effects.Count != 0) throw new RuleViolation("tactical.configuration", "Tactical rules require API 3 and separate effect definitions");
        if (skill.Maneuver is { } m && (skill.Kind != "active" || skill.Target != "self" || skill.EndsAction
            || m.MaximumOthers is < 0 or > 16 || m.Movement is < 0 or > 2 || m.ProtectionTurns != 2 || m.NetDistance is < 1 or > 32
            || !Fraction(m.AttackPenalty) || !Fraction(m.Counteroffensive) || !Fraction(m.Evasion) || !Fraction(m.EvasionCeiling)
            || !Fraction(m.IgnoreRetention) || !Names(m.RecipientCategories) || m.ExcludedAbilities == null || m.ExcludedAbilities.Length > 64
            || m.ExcludedAbilities.Any(string.IsNullOrWhiteSpace)))
            throw new RuleViolation("tactical.configuration", "Invalid maneuver rule");
        if (skill.TerritoryAura is { } a && (skill.Kind != "passive" || a.Radius is < 1 or > 32 || !Fraction(a.Attack) || !Fraction(a.Defense)))
            throw new RuleViolation("tactical.configuration", "Invalid territory aura");
        if (skill.RecoveryNetwork is { } h && (skill.Kind != "passive" || !Names(h.Buildings) || !Names(h.Categories)
            || h.ExcludedAbilities == null || h.ExcludedAbilities.Length > 64 || h.ExcludedAbilities.Any(string.IsNullOrWhiteSpace)
            || h.FacilityRadius is < 0 or > 32 || h.AdjacentRadius is < 1 or > 32 || h.PerFacility is < 1 or > 16
            || h.FacilityHealing is < 0 or > 10000 || h.AdjacentHealing is < 0 or > 10000
            || string.IsNullOrWhiteSpace(h.FacilityEffect) || string.IsNullOrWhiteSpace(h.AdjacentEffect)
            || h.FacilityEffect.Length > 128 || h.AdjacentEffect.Length > 128 || h.FacilityEffect == h.AdjacentEffect))
            throw new RuleViolation("tactical.configuration", "Invalid recovery network");
    }
    public static bool Ground(WorldState w, UnitState u) => !WorldUnitLayers.Air(w, u) && !u.Transport
        && !w.Cells[u.Cell].Terrain.Sea && u.Combatant.Profile.Category != "naval";
    public static bool Mobile(WorldState w, UnitState u) => Ground(w, u) && u.Combatant.Profile.Category != "fort"
        && u.Combatant.Profile.Movement > 0;
    private static bool Fresh(UnitState u) => !u.Acted && !u.MovedThisTurn && !u.AttackedThisTurn;
    public static string CooldownKey(UnitState u, ExtensionSkill skill, string instance) =>
        (skill.Maneuver != null && u.CommanderId != null ? "commander." + u.CommanderId : u.Id) + "/" + instance + "/ready";
    public static bool CanActivate(WorldState w, UnitState source) => Mobile(w, source) && Fresh(source)
        && source.Morale.Effective != -3 && source.Combatant.ConstructionTurns == 0;
    public static bool Eligible(WorldState w, UnitState source, UnitState target, ExtensionSkill skill) =>
        target.Id != source.Id && target.Owner == source.Owner && Ground(w, target) && Fresh(target)
        && target.Combatant.ConstructionTurns == 0 && skill.Maneuver!.RecipientCategories.Contains(target.Combatant.Profile.Category)
        && !skill.Maneuver.ExcludedAbilities.Contains(target.Combatant.Profile.CommanderAbility)
        && new HexMap(w.Width, w.Height).Distance(source.Cell, target.Cell) <= skill.Range;

    public static WorldState Activate(WorldState w, UnitState source, ExtensionSkill skill, SkillSlot slot,
        IReadOnlyList<string>? others, RuleEventBus bus)
    {
        var ids = others ?? Array.Empty<string>();
        if (!CanActivate(w, source) || ids.Count > skill.Maneuver!.MaximumOthers || ids.Distinct().Count() != ids.Count
            || ids.Any(id => !w.Units.Any(t => t.Id == id && Eligible(w, source, t, skill))))
            throw new RuleViolation("skill.target", "Choose only fresh own ground recipients, excluding the caster");
        var selected = new HashSet<string>(ids, StringComparer.Ordinal) { source.Id };
        return w with { Units = w.Units.Select(u =>
        {
            if (!selected.Contains(u.Id)) return u;
            if (u.Maneuvers.Count >= 32) throw new RuleViolation("skill.target", "Too many concurrent maneuvers");
            // New movement shares one +2 budget with existing optional movement effects.
            int oldLimit = u.ExtensionMovementLimit ?? bus.MovementBudget(u.Combatant);
            int limit = Math.Max(oldLimit, checked(u.Combatant.Profile.Movement + skill.Maneuver.Movement));
            return u with { Maneuvers = u.Maneuvers.Append(new ManeuverGrant(skill.Id, slot.InstanceId, source.Id,
                source.Owner, source.CommanderId, u.CommanderId, w.Round, u.Cell)).ToArray(),
                RemainingMovement = checked(u.RemainingMovement + limit - oldLimit), ExtensionMovementLimit = limit };
        }).ToArray() };
    }
    private static UnitState? Source(WorldState w, UnitState recipient, ManeuverGrant grant, RuleEventBus bus)
    {
        if (recipient.Owner != grant.Owner || recipient.CommanderId != grant.RecipientCommander
            || !bus.Skills.TryGetValue(grant.Skill, out var skill) || skill.Maneuver == null) return null;
        return w.Units.FirstOrDefault(s => s.Id == grant.Source && s.Owner == grant.Owner && s.CommanderId == grant.SourceCommander
            && RuleEventBus.Slots(s.Combatant).Any(slot => slot.Id == grant.Skill && slot.InstanceId == grant.Instance));
    }
    public static WorldState Prune(WorldState w, RuleEventBus? bus)
    {
        if (bus == null || !w.Units.Any(u => u.Maneuvers.Count > 0)) return w;
        return w with { Units = w.Units.Select(u => u with { Maneuvers = u.Maneuvers.Where(g => Source(w, u, g, bus) != null).ToArray() }).ToArray() };
    }
    public static WorldState EndTurn(WorldState w, string owner, RuleEventBus bus)
    {
        if (!bus.TacticalEnabled) return w;
        w = Prune(w, bus);
        return w with { Units = w.Units.Select(u => u.Owner != owner ? u : u with { Maneuvers = u.Maneuvers.Select(g =>
            g.StartRound == w.Round ? g with { EndCell = u.Cell } : g).ToArray() }).ToArray() };
    }
    public static WorldState StartTurn(WorldState w, string owner, RuleEventBus bus)
    {
        if (!bus.TacticalEnabled) return w;
        w = Prune(w, bus);
        var map = new HexMap(w.Width, w.Height);
        w = w with { Units = w.Units.Select(u => u.Owner != owner ? u : u with {
            Maneuvers = u.Maneuvers.Where(g => w.Round < g.StartRound + 2).Select(g =>
            {
                var skill = bus.Skills[g.Skill]; var source = Source(w, u, g, bus)!;
                bool ready = w.Round == g.StartRound + 1 && g.EndCell.HasValue
                    && map.Distance(g.StartCell, g.EndCell.Value) >= skill.Maneuver!.NetDistance
                    && map.Distance(source.Cell, u.Cell) <= skill.Range;
                return g with { CounterReady = ready };
            }).ToArray() }).ToArray() };
        return Heal(w, owner, bus);
    }
    private static bool InRange(WorldState w, UnitState recipient, ManeuverGrant grant, RuleEventBus bus) =>
        Source(w, recipient, grant, bus) is { } source && Mobile(w, source) && Ground(w, recipient)
        && (recipient.Id == source.Id ? Mobile(w, recipient) : bus.Skills[grant.Skill].Maneuver!.RecipientCategories.Contains(recipient.Combatant.Profile.Category))
        && (recipient.Id == source.Id || !bus.Skills[grant.Skill].Maneuver!.ExcludedAbilities.Contains(recipient.Combatant.Profile.CommanderAbility))
        && new HexMap(w.Width, w.Height).Distance(source.Cell, recipient.Cell) <= bus.Skills[grant.Skill].Range;
    public static WorldState ConsumeAttack(WorldState w, string unitId, RuleEventBus bus) => w with {
        Units = w.Units.Select(u => u.Id != unitId ? u : u with { Maneuvers = u.Maneuvers.Select(g =>
            g.CounterReady && !g.Used && InRange(w, u, g, bus) ? g with { Used = true } : g).ToArray() }).ToArray() };
    public static TacticalCombatBonus? Resolve(WorldState w, UnitState unit, RuleEventBus bus)
    {
        if (!bus.TacticalEnabled || !Ground(w, unit)) return null;
        double attack = 0, defense = 0, penalty = 0, first = 0, evade = 0, ceiling = .5, retain = .5;
        var map = new HexMap(w.Width, w.Height);
        foreach (var source in w.Units.Where(s => s.Owner == unit.Owner && Ground(w, s)))
            foreach (var (skill, _) in bus.Bound(source.Combatant).Where(p => p.Skill.Kind == "passive" && p.Skill.TerritoryAura != null))
            {
                var aura = skill.TerritoryAura!;
                int distance = map.Distance(source.Cell, unit.Cell);
                if (distance > aura.Radius || distance == 0 && source.Id != unit.Id) continue;
                string? controller = w.Cells[source.Cell].Owner;
                if (controller == null && w.Jurisdictions is { } jurisdictions && jurisdictions.CellCenters[source.Cell] >= 0)
                    controller = w.Cells[jurisdictions.CellCenters[source.Cell]].Owner;
                if (controller == null || WorldDiplomacy.Allied(w, source.Owner, controller)) continue;
                attack = Math.Max(attack, aura.Attack); defense = Math.Max(defense, aura.Defense);
            }
        foreach (var grant in unit.Maneuvers)
        {
            if (Source(w, unit, grant, bus) == null) continue;
            var m = bus.Skills[grant.Skill].Maneuver!;
            if (w.ActiveFaction == unit.Owner && w.Round == grant.StartRound) penalty = Math.Max(penalty, m.AttackPenalty);
            if (!InRange(w, unit, grant, bus)) continue;
            if (grant.CounterReady && !grant.Used && w.ActiveFaction == unit.Owner && w.Round == grant.StartRound + 1)
                first = Math.Max(first, m.Counteroffensive);
            if (m.Evasion > evade) { evade = m.Evasion; ceiling = m.EvasionCeiling; retain = m.IgnoreRetention; }
        }
        return attack == 0 && defense == 0 && penalty == 0 && first == 0 && evade == 0 ? null
            : new(attack, defense, penalty, first, evade, ceiling, retain, Mobile(w, unit));
    }
    public static WorldState Heal(WorldState w, string owner, RuleEventBus bus)
    {
        if (!bus.TacticalEnabled) return w;
        var effects = new Dictionary<(string Unit, string Effect), int>();
        var map = new HexMap(w.Width, w.Height);
        var units = w.Units.Where(u => u.Owner == owner && Ground(w, u) && u.Combatant.Health < u.Combatant.MaximumHealth).ToArray();
        var counters = w.ExtensionState.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        bool hasRules = false;
        foreach (var source in w.Units.Where(u => u.Owner == owner && Ground(w, u)))
            foreach (var (skill, slot) in bus.Bound(source.Combatant).Where(p => p.Skill.Kind == "passive" && p.Skill.RecoveryNetwork != null))
            {
                hasRules = true;
                var h = skill.RecoveryNetwork!;
                string Key(string effect) => owner + "/recovery-network/" + effect + "/round";
                bool Done(string effect) => w.ExtensionState.TryGetValue(Key(effect), out long round) && round == w.Round;
                void Offer(UnitState u, string effect, int amount) { if (!Done(effect)) effects[(u.Id, effect)] = Math.Max(effects.GetValueOrDefault((u.Id, effect)), amount); }
                counters[Key(h.FacilityEffect)] = w.Round; counters[Key(h.AdjacentEffect)] = w.Round;
                foreach (int cell in Enumerable.Range(0, w.Cells.Count).Where(c => w.Cells[c].Owner == owner
                    && w.Cells[c].Terrain.Active && w.Cells[c].BuildingLevel > 0 && h.Buildings.Contains(w.Cells[c].BuildingKind)))
                    foreach (var u in units.Where(u => h.Categories.Contains(u.Combatant.Profile.Category)
                        && !h.ExcludedAbilities.Contains(u.Combatant.Profile.CommanderAbility) && map.Distance(u.Cell, cell) <= h.FacilityRadius)
                        .OrderBy(u => (decimal)u.Combatant.Health / u.Combatant.MaximumHealth)
                        .ThenBy(u => map.Distance(u.Cell, cell)).ThenBy(u => u.Id, StringComparer.Ordinal).Take(h.PerFacility))
                        Offer(u, h.FacilityEffect, h.FacilityHealing);
                foreach (var u in units.Where(u => u.Id != source.Id && map.Distance(source.Cell, u.Cell) is var distance && distance > 0 && distance <= h.AdjacentRadius))
                    Offer(u, h.AdjacentEffect, h.AdjacentHealing);
            }
        if (!hasRules) return w;
        var healing = effects.GroupBy(p => p.Key.Unit).ToDictionary(g => g.Key, g => g.Sum(p => (long)p.Value));
        return w with { ExtensionState = counters, Units = w.Units.Select(u => !healing.TryGetValue(u.Id, out long amount) ? u : u with {
            Combatant = u.Combatant with { Health = (int)Math.Min(u.Combatant.MaximumHealth, u.Combatant.Health + amount) } }).ToArray() };
    }
}
