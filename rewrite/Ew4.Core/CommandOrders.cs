using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

// Optional, data-defined orders. Character identities and balance values live in the Mod.
public sealed record OrderSelector
{
    public string[] Categories { get; init; } = Array.Empty<string>();
    public string[] Abilities { get; init; } = Array.Empty<string>();
    public string[] Terrains { get; init; } = Array.Empty<string>();
    public string[] Buildings { get; init; } = Array.Empty<string>();
    public string[] Conditions { get; init; } = Array.Empty<string>();
    public double MinHealth { get; init; }
    public double MaxHealth { get; init; } = 1;
    public bool Other { get; init; }
    public bool Own { get; init; } = true;
    public bool Allied { get; init; }
    public int Radius { get; init; } = 1;
    public int Count { get; init; } = 1;
    public string Sort { get; init; } = "health";
}

public sealed record OrderBonus
{
    public double Attack { get; init; }
    public double Defense { get; init; }
    public double CounterAttack { get; init; }
    public double CounterDefense { get; init; }
    public int Movement { get; init; }
    public int MovementPenalty { get; init; }
    public int Heal { get; init; }
    public double HealFraction { get; init; }
    public int? Morale { get; init; }
    public int MoraleChange { get; init; }
    public int? MoraleFloor { get; init; }
    public bool SuppressCounter { get; init; }
    public bool IgnoreEvasion { get; init; }
    public double IgnoreTerrain { get; init; }
    public double IgnoreBuilding { get; init; }
    public string[] Waivers { get; init; } = Array.Empty<string>();
    public double MoneyDiscount { get; init; }
    public double IndustryDiscount { get; init; }
    public double FoodDiscount { get; init; }
    public int LowerAttack { get; init; }
    public int UpperAttack { get; init; }
}

public sealed record CommandOrder
{
    public string Mode { get; init; } = "grant";
    public string Event { get; init; } = "activate";
    public OrderSelector Source { get; init; } = new();
    public OrderSelector Recipients { get; init; } = new();
    public OrderSelector? Enemy { get; init; }
    public OrderSelector? SecondaryEnemy { get; init; }
    public OrderBonus Bonus { get; init; } = new();
    public string[] Conditions { get; init; } = Array.Empty<string>();
    public int Duration { get; init; } = 1;
    public int Interval { get; init; }
    public int Uses { get; init; }
    public int Followup { get; init; }
    public bool IncludesSelf { get; init; }
    public bool RequiresFresh { get; init; }
    public bool SelectSelf { get; init; }
    public string? Resource { get; init; }
    public double Income { get; init; }
    public int IncomeCap { get; init; }
    public string[] Profiles { get; init; } = Array.Empty<string>();
    public double Price { get; init; }
    public int SourceLimit { get; init; }
    public string? Unlock { get; init; }
    public string? Plan { get; init; }
    public bool EnemyOptional { get; init; }
    public bool Intelligence { get; init; }
    public CommandOrder[] Additional { get; init; } = Array.Empty<CommandOrder>();
}

public sealed record OrderGrant(string Skill, string Instance, string Source, string Owner, string? Commander,
    int Round, int Cell, string? Enemy = null, int EnemyCell = -1, int Uses = 0, string? RecipientCommander = null, long Epoch = 0,
    bool Pending = false, string? Auxiliary = null, int Stage = 0, int Branch = -1,
    string[]? Participants = null, int[]? Positions = null, string? Plan = null, int Lifetime = 1, bool MarkerOnly = false);

public sealed record OrderCombatBonus(double Attack = 0, double Defense = 0, double CounterAttack = 0, double CounterDefense = 0,
    double GenericAttack = 0, double GenericDefense = 0, bool SuppressCounter = false, bool IgnoreEvasion = false,
    double IgnoreTerrain = 0, double IgnoreBuilding = 0, int LowerAttack = 0, int UpperAttack = 0, bool Survive = false,
    double GenericCounterAttack = 0, double GenericCounterDefense = 0, int RedirectCapacity = 0);

public sealed record OrderMoraleContribution(string Effect, string Source, int Change, int? Set, long EndEpoch);

public static partial class CommandOrders
{
    public static readonly string[] Modes = { "grant", "heal", "morale", "swap", "reactivate", "assault", "guard", "plan", "summon", "income", "discount", "followup" };
    private static int Distance(WorldState w, int a, int b) => new HexMap(w.Width, w.Height).Distance(a, b);
    private static UnitState? Unit(WorldState w, string? id) => w.Units.FirstOrDefault(u => u.Id == id);
    private static int EnemyCell(WorldState w, string? id) => id?.StartsWith("cell.", StringComparison.Ordinal) == true
        && int.TryParse(id.AsSpan(5), out int cell) ? cell : Unit(w, id)?.Cell ?? -1;
    public static string[] EnemyTargets(WorldState w, UnitState source, ExtensionSkill skill) => skill.Order?.Plan is "op_recruit" or "op_sabotage"
        ? OperationSites(w,source,skill.Order).Select(c=>"cell."+c).ToArray() : skill.Order?.Enemy is not { } s
        ? Array.Empty<string>() : skill.Order.Plan == "city_direction"
            ? Enumerable.Range(0, w.Cells.Count).Where(c => w.Cells[c].BuildingKind == "city" && w.Cells[c].Owner is string owner
                && !WorldDiplomacy.Allied(w, source.Owner, owner) && Distance(w, source.Cell, c) <= s.Radius).Select(c => "cell." + c).ToArray()
            : w.Units.Where(t => Matches(w, source, t, s) && Distance(w, source.Cell, t.Cell) <= s.Radius).Select(t => t.Id).ToArray();
    private static string Identity(UnitState u) => u.CommanderId == null ? "unit." + u.Id : "commander." + u.CommanderId;
    private static string Key(UnitState u, string suffix) => "orders/" + (UnitHistory(suffix)?"unit."+u.Id:Identity(u)) + "/" + suffix;
    private static bool UnitHistory(string suffix) => suffix.StartsWith("heal.",StringComparison.Ordinal)
        || suffix.StartsWith("attacker.",StringComparison.Ordinal) || suffix.StartsWith("guard.target.",StringComparison.Ordinal)
        || suffix is "reactivated" or "followup" or "attacks" or "defenses" or "air_defenses" or "air_spot_used"
            or "last_cell" or "last_round" or "last_moved" or "start_cell" or "received_round" or "air_attack_round"
            or "air_kill_round" or "air_kill_cell" or "naval_attack_round" or "siege_round" or "city_kill_round"
            or "landed_round" or "local_shield" or "redirect" or "warning.used" or "survived" or "river_used" or "last_end" or "received_revision" or "kills"
            or "attack_lock" or "move_lock" or "any_move" or "normal_net" or "staff_swap" or "staff_transfer";
    private static long Get(WorldState w, UnitState u, string suffix) => w.ExtensionState.GetValueOrDefault(Key(u, suffix));
    private static long Epoch(WorldState w, string owner) => w.ExtensionState.GetValueOrDefault("orders/turn/" + owner);
    private static WorldState Set(WorldState w, UnitState u, string suffix, long value)
    {
        var state = w.ExtensionState.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        state[Key(u, suffix)] = value;
        return w with { ExtensionState = state };
    }
    private static WorldState Replace(WorldState w, UnitState u) => w with { Units = w.Units.Select(t => t.Id == u.Id ? u : t).ToArray() };
    private static double Health(UnitState u) => (double)u.Combatant.Health / u.Combatant.MaximumHealth;
    public static void Validate(ExtensionSkill s, int api)
    {
        if (s.Order == null) return;
        var r = s.Order;
        bool Selector(OrderSelector x) => x != null && x.Radius is >= 0 and <= 64 && x.Count is >= 0 and <= 32
            && double.IsFinite(x.MinHealth) && double.IsFinite(x.MaxHealth) && x.MinHealth >= 0 && x.MaxHealth <= 1 && x.MinHealth <= x.MaxHealth
            && new[] { x.Categories, x.Abilities, x.Terrains, x.Buildings, x.Conditions }.All(a => a != null && a.Length <= 64 && a.All(n => !string.IsNullOrWhiteSpace(n)))
            && x.Sort is "health" or "morale" or "training" or "distance";
        var b = r.Bonus;
        if (api < 3 || s.Effects.Count != 0 || s.Maneuver != null || s.TerritoryAura != null || s.RecoveryNetwork != null
            || !Modes.Contains(r.Mode) || !new[] { "activate", "start", "combat", "after_attack", "after_defend", "end", "morale" }.Contains(r.Event)
            || !Selector(r.Source) || !Selector(r.Recipients) || r.Enemy != null && !Selector(r.Enemy)
            || r.Duration is < 1 or > 16 || r.Interval is < 0 or > 100 || r.Uses is < 0 or > 32 || r.Followup is < 0 or > 6
            || r.Profiles == null || r.Profiles.Length > 16 || r.SourceLimit is < 0 or > 64
            || b == null || b.Waivers == null || b.Waivers.Any(n => n is not ("mountain" or "forest" or "river" or "road"))
            || new[] { b.Attack, b.Defense, b.CounterAttack, b.CounterDefense, b.HealFraction, b.MoneyDiscount, b.IndustryDiscount, b.FoodDiscount, b.IgnoreTerrain, b.IgnoreBuilding, r.Price, r.Income }
                .Any(n => !double.IsFinite(n) || n < 0 || n > 1)
            || b.Heal is < 0 or > 10000 || b.Movement is < 0 or > 8 || b.MovementPenalty is < 0 or > 8 || b.Morale is < -3 or > 1 || b.MoraleFloor is < -3 or > 1
            || s.Kind == "active" && r.Event != "activate" || s.Kind == "passive" && r.Event == "activate")
            throw new RuleViolation("orders.configuration", "Invalid data-defined order: " + s.Id);
        ValidateOrderNames(s,r,api);
    }

    private static bool OwnSite(WorldState w, UnitState u) => w.Cells[u.Cell].Owner == u.Owner;
    private static bool Near(WorldState w, UnitState u, Func<UnitState, bool> test, int radius = 1) =>
        w.Units.Any(t => t.Id != u.Id && t.Owner == u.Owner && Distance(w, u.Cell, t.Cell) <= radius && test(t));
    public static bool Matches(WorldState w, UnitState source, UnitState u, OrderSelector s)
    {
        if (s.Other && source.Id == u.Id || s.Allied && !WorldDiplomacy.Allied(w,source.Owner,u.Owner)
            || !s.Allied && (s.Own && source.Owner != u.Owner || !s.Own && WorldDiplomacy.Allied(w, source.Owner, u.Owner))
            || s.Categories.Length > 0 && !s.Categories.Contains(Category(w, u))
            || s.Abilities.Length > 0 && !s.Abilities.Contains(u.Combatant.Profile.CommanderAbility ?? u.Combatant.Profile.Category)
            || s.Terrains.Length > 0 && !s.Terrains.Contains(w.Cells[u.Cell].DefenseTerrain)
            || s.Buildings.Length > 0 && !s.Buildings.Contains(w.Cells[u.Cell].BuildingKind ?? "none")
            || Health(u) < s.MinHealth || Health(u) > s.MaxHealth) return false;
        return s.Conditions.All(c => Condition(w, source, u, c));
    }
    private static string Category(WorldState w, UnitState u) => WorldUnitLayers.Air(w, u) ? "air" : u.Combatant.Profile.Category;
    private static bool Condition(WorldState w, UnitState s, UnitState u, string c) => c switch
    {
        "ground" => TacticalSkills.Ground(w, u), "mobile" => TacticalSkills.Mobile(w, u),
        "self" => s.Id == u.Id,
        "armor" => u.Combatant.Profile.CommanderAbility is "cavalry" or "tyej.armor" && Category(w, u) == "cavalry",
        "not_armor" => u.Combatant.Profile.CommanderAbility != "tyej.armor",
        "infantry_or_armor" => Category(w, u) == "infantry" || u.Combatant.Profile.CommanderAbility == "tyej.armor",
        "engineer" => u.Combatant.Profile.Traits.Contains("engineer"),
        "own" => OwnSite(w, u), "city" => w.Cells[u.Cell].BuildingKind == "city" && OwnSite(w, u),
        "settled" => OwnSite(w, u) && Get(w, u, "last_cell") == u.Cell + 1 && Get(w, u, "last_round") == w.Round - 1,
        "city_near" => Enumerable.Range(0, w.Cells.Count).Any(i => w.Cells[i].Owner == u.Owner && w.Cells[i].BuildingKind == "city" && Distance(w, i, u.Cell) <= 1),
        "city_two" => Enumerable.Range(0, w.Cells.Count).Any(i => w.Cells[i].Owner == u.Owner && w.Cells[i].BuildingKind == "city" && Distance(w, i, u.Cell) <= 2),
        "port_two" => Enumerable.Range(0, w.Cells.Count).Any(i => w.Cells[i].Owner == u.Owner && w.Cells[i].BuildingKind == "port" && Distance(w, i, u.Cell) <= 2),
        "port_near" => Enumerable.Range(0, w.Cells.Count).Any(i => w.Cells[i].Owner == u.Owner && w.Cells[i].BuildingKind == "port" && Distance(w, i, u.Cell) <= 1),
        "airport" => w.Cells[u.Cell].BuildingKind == "airfield" && OwnSite(w, u),
        "no_building" => w.Cells[u.Cell].BuildingKind == null,
        "damaged" => u.Combatant.Health < u.Combatant.MaximumHealth,
        "fresh" => !u.Acted && !u.MovedThisTurn && !u.AttackedThisTurn,
        "not_attacked" => !u.AttackedThisTurn && !u.Acted,
        "spent" => u.MovedThisTurn || u.AttackedThisTurn,
        "stationary" => !u.MovedThisTurn,
        "last_stationary" => Get(w, u, "last_moved") == 0,
        "low_morale" => u.Morale.Effective < 0,
        "normal_morale" => u.Morale.Effective >= 0,
        "vulnerable" => Health(u) <= .5 || u.Morale.Effective < 0,
        "weaker" => Health(u) < Health(s),
        "morale_susceptible" => !u.Combatant.Has(Ew4Combat.Skill(3)),
        "infantry_near" => Near(w, u, t => Category(w, t) == "infantry"),
        "artillery_near" => Near(w, u, t => Category(w, t) == "artillery"),
        "armor_near" => Near(w, u, t => t.Combatant.Profile.CommanderAbility == "cavalry" || t.Combatant.Profile.CommanderAbility == "tyej.armor"),
        "naval_near" => Near(w, u, t => Category(w, t) == "naval"),
        "no_artillery_two" => !Near(w, u, t => Category(w, t) == "artillery", 2),
        "river_near" => w.Cells[u.Cell].Terrain.RiverEdges != 0 || Enumerable.Range(0, 6).Select(d => new HexMap(w.Width, w.Height).Neighbor(u.Cell, d)).Any(i => i >= 0 && w.Cells[i].Terrain.RiverEdges != 0),
        "city_or_river" => Condition(w, s, u, "city") || Condition(w, s, u, "river_near"),
        "city_or_road" => Condition(w, s, u, "city") || w.Cells[u.Cell].DefenseTerrain == "road" && OwnSite(w, u),
        "city_or_forest" => w.Cells[u.Cell].BuildingKind == "city" || w.Cells[u.Cell].DefenseTerrain == "forest",
        "infantry_field_near" => Near(w, u, t => Category(w, t) == "infantry" && w.Cells[t.Cell].BuildingKind == null),
        "two_categories_near" => w.Units.Where(t => t.Id != u.Id && t.Owner == u.Owner && TacticalSkills.Ground(w, t) && Distance(w, u.Cell, t.Cell) <= 2)
            .Select(t => t.Combatant.Profile.CommanderAbility ?? Category(w, t)).Distinct().Count() >= 2,
        "infantry_armor_pair" => Category(w, u) == "infantry" && Condition(w, s, u, "armor_near") || Condition(w, s, u, "armor") && Condition(w, s, u, "infantry_near"),
        "port_or_coast_near" => Condition(w, s, u, "port_near") || Near(w, u, t => t.Combatant.Profile.Traits.Contains("coastal_gun")),
        "enemy_territory" => w.Cells[u.Cell].Owner is string owner && !WorldDiplomacy.Allied(w, u.Owner, owner),
        "air_ground_weapon" => WorldUnitLayers.Air(w, u) && Weapon(w, u, "surface"),
        "fighter" => WorldUnitLayers.Air(w, u) && Weapon(w, u, "air"),
        "own_enemy_adjacent" => w.Units.Any(t => t.Owner == s.Owner && TacticalSkills.Ground(w, t) && Distance(w, t.Cell, u.Cell) == 1),
        "siege_flanker" => w.Units.Any(t => t.Owner == s.Owner && TacticalSkills.Ground(w, t) && Distance(w, t.Cell, u.Cell) == 1
            && (w.Cells[t.Cell].BuildingKind == "city" || t.Combatant.Profile.Category == "fort")),
        "fortified" => w.Cells[u.Cell].BuildingKind == "city" || w.Cells[u.Cell].InstallationKind is "bunker" or "trench",
        "under_construction" => u.Combatant.ConstructionTurns > 0,
        "city_or_mountain" => w.Cells[u.Cell].BuildingKind == "city" || w.Cells[u.Cell].DefenseTerrain == "mountain",
        "infantry_pair_if_cavalry" => Category(w, u) != "cavalry" || Condition(w, s, u, "infantry_near"),
        "fighter_near" => Near(w, u, t => WorldUnitLayers.Air(w, t) && Weapon(w, t, "air")),
        "city_or_port" => OwnSite(w, u) && w.Cells[u.Cell].BuildingKind is "city" or "port",
        "attacked_since_end" => Get(w, u, "received_revision") > Get(w, u, "last_end"),
        "attacked_fort" => Get(w, u, "siege_round") == w.Round,
        "killed_city" => Get(w, u, "city_kill_round") == w.Round,
        "air_attack" => Get(w, u, "air_attack_round") == w.Round,
        "naval_attack" => Get(w, u, "naval_attack_round") == w.Round,
        "landed" => Get(w, u, "landed_round") == w.Round,
        "two_allies" => w.Units.Count(t => t.Owner == s.Owner && t.Id != s.Id && TacticalSkills.Ground(w, t) && Distance(w, t.Cell, u.Cell) == 1) >= 2,
        _ => throw new RuleViolation("orders.condition", c)
    };

    private static IEnumerable<UnitState> Select(WorldState w, UnitState source, OrderSelector s) =>
        w.Units.Where(u => Distance(w, source.Cell, u.Cell) <= s.Radius && Matches(w, source, u, s))
            .OrderBy(u => s.Sort == "morale" ? u.Morale.Effective : s.Sort == "training" ? int.TryParse(u.Veterancy, out int v) ? v : 0
                : s.Sort == "distance" ? Distance(w, source.Cell, u.Cell) : Health(u))
            .ThenBy(u => u.Combatant.Health).ThenBy(u => u.Id, StringComparer.Ordinal).Take(s.Count);

    public static string[] Candidates(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus? bus = null) => skill.Order == null ? Array.Empty<string>()
        : HasPending(w, source, skill) ? PendingCandidates(w, source, skill, bus)
        : skill.Order.Mode == "summon" ? SummonCells(w, source, skill).Select(c => "cell." + c).ToArray()
        : w.Units.Where(u => Distance(w, source.Cell, u.Cell) <= skill.Order.Recipients.Radius
            && Matches(w, source, u, skill.Order.Recipients)
            && OperationCandidate(w,source,u,skill.Order)
            && (skill.Order.Mode != "reactivate" || skill.Order.Plan is "different_type_terrain" or "landing_guard" || CanReactivate(w, u)))
            .OrderBy(u => Health(u)).ThenBy(u => u.Id, StringComparer.Ordinal).Select(u => u.Id).ToArray();
    public static bool CanActivate(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus? bus = null)
    {
        var r = skill.Order!;
        if (HasPending(w, source, skill)) return Candidates(w, source, skill, bus).Length > 0;
        return Matches(w, source, source, r.Source) && (!r.RequiresFresh || Condition(w, source, source, "fresh"))
            && source.Combatant.ConstructionTurns == 0 && source.Morale.Effective != -3
            && (r.Unlock == null || w.TriggeredScenarioEvents.Contains(r.Unlock))
            && (r.Mode != "summon" || bus != null && SummonAvailable(w, source, skill, bus))
            && PlanAvailable(w, source, skill, bus)
            && OperationAvailable(w,source,skill)
            && (LegalSelections(w,source,skill) is not { } choices || choices.Any())
            && (r.SelectSelf || r.Mode is "assault" or "guard" or "income" || Candidates(w, source, skill).Length >= (r.Mode == "swap" && !r.IncludesSelf ? 2 : 1));
    }
    private static bool CanReactivate(WorldState w, UnitState u) => (u.MovedThisTurn || u.AttackedThisTurn)
        && u.Combatant.ConstructionTurns == 0 && u.Morale.Effective != -3 && Get(w, u, "reactivated") != Epoch(w, u.Owner) + 1;
    public static WorldState Activate(WorldState w, UnitState source, ExtensionSkill skill, SkillSlot slot,
        IReadOnlyList<string>? selected, string? enemy, RuleEventBus bus, string? secondary = null)
    {
        var r = skill.Order!;
        if (HasPending(w, source, skill) && skill.Order!.Plan is "op_relocate" or "op_cavalry" or "op_retreat") return CompleteOperationMove(w,source,skill,selected,bus);
        if (HasPending(w, source, skill)) return source.Orders.Any(g => g.Pending && g.Skill == skill.Id && g.Stage < 0)
            ? CompleteChoice(w, source, skill, selected, bus) : CompleteFollowup(w, source, skill, selected, bus);
        if (!CanActivate(w, source, skill, bus)) throw new RuleViolation("orders.unavailable", "Order conditions are not met");
        var ids = selected ?? Array.Empty<string>();
        if (ids.Distinct().Count() != ids.Count || ids.Count > r.Recipients.Count || ids.Any(id => !Candidates(w, source, skill).Contains(id)))
            throw new RuleViolation("orders.target", "Invalid order recipients");
        if (r.Mode == "summon") return Summon(w, source, skill, slot, ids, bus);
        if (r.SecondaryEnemy != null && secondary != null && (Unit(w, secondary) is not { } second || !Matches(w, source, second, r.SecondaryEnemy)
            || Distance(w, source.Cell, second.Cell) > r.SecondaryEnemy.Radius)) throw new RuleViolation("orders.target", "Invalid secondary enemy");
        var recipients = ids.Select(id => Unit(w, id)!).ToList();
        if (r.SelectSelf || r.IncludesSelf) recipients.Insert(0, source);
        if (recipients.Count == 0 && r.Mode is not ("assault" or "guard" or "income")) throw new RuleViolation("orders.target", "Choose recipients");
        if (r.Enemy != null && !(r.EnemyOptional && enemy == null) && !EnemyTargets(w, source, skill).Contains(enemy))
            throw new RuleViolation("orders.target", "Choose a legal enemy");
        if (r.Mode == "swap")
        {
            if (recipients.Count != 2 || recipients[0].Id == recipients[1].Id || Distance(w, recipients[0].Cell, recipients[1].Cell) != 1)
                throw new RuleViolation("orders.swap", "Choose two adjacent recipients");
            var a = recipients[0]; var b = recipients[1];
            ValidateSwap(w, source, a, b, r, enemy);
            if (!LegalDestination(w, a, b.Cell, a.Id, b.Id) || !LegalDestination(w, b, a.Cell, a.Id, b.Id))
                throw new RuleViolation("orders.swap", "Both destinations must be legal");
            w = Replace(Replace(w, Relocate(w, a, b.Cell)), Relocate(w, b, a.Cell));
            w=Set(Set(w,a,"any_move",Epoch(w,a.Owner)+1),b,"any_move",Epoch(w,b.Owner)+1);
        }
        ValidatePlanTargets(w, source, recipients, skill, enemy, secondary);
        foreach (var old in recipients.DefaultIfEmpty(source))
        {
            var u = Unit(w, old.Id)!;
            if (r.Mode == "reactivate" && (r.Plan != "different_type_terrain" || u.Id == recipients[0].Id)
                && (r.Plan != "landing_guard" || u.Id == source.Id))
            {
                if (!CanReactivate(w, u)) throw new RuleViolation("orders.reactivate", "Only one full reactivation per owner turn");
                w = Set(w, u, "reactivated", Epoch(w, u.Owner) + 1);
                u = u with { Acted = false, RemainingMovement = u.ExtensionMovementLimit ?? bus.MovementBudget(u.Combatant) };
            }
            if (ReceivesBonus(w, source, u, recipients, r)) w = ApplyBonus(w, source, u, skill, slot, r, enemy, bus);
        }
        return ActivatePlan(w, source, skill, slot, recipients, enemy, secondary, bus);
    }

    public static bool LegalDestination(WorldState w, UnitState u, int cell, params string[] excluding) => cell >= 0 && cell < w.Cells.Count
        && WorldUnitLayers.ValidTerrain(w, u.Combatant.Profile, cell)
        && (WorldUnitLayers.Air(w, u) || w.Cells[cell].Terrain.Sea == w.Cells[u.Cell].Terrain.Sea)
        && WorldUnitLayers.HasSpace(w with { Units = w.Units.Where(t => !excluding.Contains(t.Id)).ToArray() }, u.Combatant.Profile, u.Owner, cell)
        && !w.Units.Any(t => t.Cell == cell && !excluding.Contains(t.Id) && WorldUnitLayers.Layer(w, t.Combatant.Profile) == WorldUnitLayers.Layer(w, u.Combatant.Profile)
            && !WorldDiplomacy.Allied(w, u.Owner, t.Owner));
    private static UnitState Relocate(WorldState w, UnitState u, int cell) => u with { Cell = cell, Combatant = u.Combatant with {
        BuildingKind = WorldUnitLayers.Air(w, u) ? null : w.Cells[cell].BuildingKind, AtSea = !WorldUnitLayers.Air(w, u) && w.Cells[cell].Terrain.Sea } };

    private static WorldState ApplyBonus(WorldState w, UnitState source, UnitState u, ExtensionSkill skill, SkillSlot slot, CommandOrder r, string? enemy, RuleEventBus bus, int branch = -1)
    {
        var b = r.Bonus;
        int healing = Math.Max(b.Heal, (int)Math.Ceiling(u.Combatant.MaximumHealth * b.HealFraction));
        if (healing > 0 && Get(w, u, "heal." + skill.Id) != w.Round)
        {
            u = u with { Combatant = u.Combatant with { Health = Math.Min(u.Combatant.MaximumHealth, checked(u.Combatant.Health + healing)) } };
            w = Set(w, u, "heal." + skill.Id, w.Round);
        }
        w = Replace(w, u);
        if (r.Mode is not ("assault" or "guard" or "plan") && (b.Morale.HasValue || b.MoraleChange != 0 || b.MoraleFloor.HasValue))
            w = Morale(w, source, u, b, skill.Id, bus);
        u = Unit(w, u.Id)!;
        if (b.Movement > 0 && !u.Acted && !u.MovedThisTurn && r.Plan!="op_contact")
        {
            int limit = Math.Max(u.ExtensionMovementLimit ?? bus.MovementBudget(u.Combatant), bus.MovementBudget(u.Combatant) + b.Movement);
            u = u with { RemainingMovement = u.RemainingMovement + limit - (u.ExtensionMovementLimit ?? bus.MovementBudget(u.Combatant)), ExtensionMovementLimit = limit };
        }
        var grant = new OrderGrant(skill.Id, slot.InstanceId, source.Id, source.Owner, source.CommanderId, w.Round, r.Plan=="op_entrench"?u.Cell:source.Cell,
            enemy, EnemyCell(w, enemy), RecipientCommander: u.CommanderId, Epoch: Epoch(w, source.Owner), Branch: branch, Plan: r.Plan, Lifetime: r.Duration);
        u = u with { Orders = u.Orders.Where(g => g.Skill != skill.Id || g.Source != source.Id || g.Branch != branch).Append(grant).ToArray() };
        return Replace(w, u);
    }

    private static WorldState Morale(WorldState w, UnitState source, UnitState u, OrderBonus b, string skill, RuleEventBus? bus = null)
    {
        if (bus != null)
        {
            var protectedEffect = ProtectSkillMorale(w, source, u, b, skill, bus); w = protectedEffect.World; b = protectedEffect.Bonus;
            if (!b.Morale.HasValue && b.MoraleChange == 0 && !b.MoraleFloor.HasValue) return w;
        }
        bool leadership = u.Combatant.Has(Ew4Combat.Skill(3));
        bool negative = b.Morale is < 0 || b.MoraleChange < 0;
        if (leadership && negative) return w;
        if (negative)
        {
            var contribution = new OrderMoraleContribution(skill, source.Id, b.MoraleChange, b.Morale, Epoch(w, u.Owner) + 1);
            u = u with { OrderMorale = u.OrderMorale.Where(m => m.Effect != skill || m.Source != source.Id).Append(contribution).ToArray() };
        }
        else
        {
            int value = b.Morale ?? Math.Clamp(u.Morale.Baseline + b.MoraleChange, -3, 1);
            if (b.MoraleFloor is int floor) value = Math.Max(value, floor);
            u = u with { OrderMorale = u.OrderMorale.Where(m => m.Change >= 0 && m.Set is not < 0).ToArray(),
                Morale = MoraleState.Set(value, leadership) };
        }
        w = Replace(w, RefreshOrderMorale(w, u));
        return negative && bus != null ? AfterSkillMorale(w, source, Unit(w, u.Id)!, skill, bus) : w;
    }

    public static UnitState RefreshOrderMorale(WorldState w, UnitState u)
    {
        int value = u.Morale.Baseline;
        foreach (var m in u.OrderMorale) value = Math.Clamp(m.Set ?? value + m.Change, -3, 1);
        int effective = TurnEconomy.Morale(value, u.Morale.Duration, LocalPenalty(w, u, u.LocalPenalty),
            w.Factions.Single(f => f.Id == u.Owner).Shortage, u.Support.Logistics || u.Combatant.FoodExempt || u.Combatant.Has(Ew4Combat.Skill(22)),
            u.Combatant.Has(Ew4Combat.Skill(3)));
        return u with { Morale = u.Morale with { Effective = effective }, Combatant = u.Combatant with { Morale = effective } };
    }

    private static bool Live(WorldState w, UnitState u, OrderGrant g, RuleEventBus bus) => g.Plan is "op_contact" or "op_delay" ? TokenLive(w,u,g,bus) : g.Plan == "payload_protection"
        ? g.Owner==u.Owner && g.RecipientCommander==u.CommanderId && Epoch(w,g.Owner)<g.Epoch+g.Lifetime && bus.Skills.ContainsKey(g.Skill)
        : g.Owner == u.Owner
        && g.RecipientCommander == u.CommanderId && Unit(w, g.Source) is { } source && source.Owner == g.Owner && source.CommanderId == g.Commander
        && bus.Bound(source.Combatant).Any(p => p.Skill.Id == g.Skill && p.Slot.InstanceId == g.Instance)
        && bus.Skills.TryGetValue(g.Skill, out var s) && s.Order != null && Epoch(w, g.Owner) < g.Epoch + g.Lifetime
        && (g.Plan!="op_entrench" || g.Cell==u.Cell)
        && (g.Pending || g.Enemy == null || g.Plan == "city_direction" && EnemyCell(w, g.Enemy) == g.EnemyCell
            || Unit(w, g.Enemy) is { } enemy
            && (g.Plan == "naval_block" ? Distance(w, g.Positions is {Length:>0}?g.Positions[0]:g.Cell, enemy.Cell) <= 3 : enemy.Cell == g.EnemyCell));

    public static WorldState Prune(WorldState w, RuleEventBus bus) => w with { Units = w.Units.Select(u =>
        u.Orders.Count == 0 ? u : u with { Orders = u.Orders.Where(g => Live(w, u, g, bus)).ToArray() }).ToArray() };

    private static bool CombatCondition(WorldState w, UnitState source, UnitState u, UnitState enemy, string c, bool main) => c switch
    {
        "main" => main, "counter" => !main,
        "first" => Get(w, u, main ? "attacks" : "defenses") == 0,
        "first_attack" => Get(w, u, "attacks") == 0,
        "first_defense" => Get(w, u, "defenses") == 0,
        "first_air_defense" => Get(w, u, "air_defenses") == 0,
        "adjacent" => Distance(w, u.Cell, enemy.Cell) == 1,
        "distant" => Distance(w, u.Cell, enemy.Cell) >= 2,
        "enemy_ground" => TacticalSkills.Ground(w, enemy),
        "enemy_mobile" => TacticalSkills.Mobile(w, enemy),
        "enemy_infantry" => Category(w, enemy) == "infantry",
        "enemy_naval" => Category(w, enemy) == "naval",
        "enemy_air" => Category(w, enemy) == "air",
        "enemy_cavalry" => Category(w, enemy) == "cavalry" && enemy.Combatant.Profile.CommanderAbility != "tyej.armor",
        "enemy_armor" => enemy.Combatant.Profile.CommanderAbility == "tyej.armor",
        "enemy_fort" => Category(w, enemy) == "fort" && !WorldUnitLayers.Air(w, enemy),
        "enemy_city_nonfort" => w.Cells[enemy.Cell].BuildingKind == "city" && Category(w, enemy) != "fort" && !WorldUnitLayers.Air(w, enemy),
        "enemy_siege" => w.Cells[enemy.Cell].BuildingKind == "city" || Category(w, enemy) == "fort",
        "enemy_building" => w.Cells[enemy.Cell].BuildingKind != null,
        "enemy_field" => w.Cells[enemy.Cell].BuildingKind == null,
        "enemy_nonarmor_field" => w.Cells[enemy.Cell].BuildingKind == null && (Category(w, enemy) == "infantry"
            || Category(w, enemy) == "cavalry" && enemy.Combatant.Profile.CommanderAbility != "tyej.armor"),
        "enemy_low" => Health(enemy) < .5,
        "enemy_healthy" => Health(enemy) >= .5,
        "enemy_low_morale" => enemy.Morale.Effective < 0,
        "enemy_isolated" => !w.Units.Any(t => t.Id != enemy.Id && t.Owner == enemy.Owner && TacticalSkills.Ground(w, t) && Distance(w, t.Cell, enemy.Cell) <= 1),
        "enemy_mountain_forest" => w.Cells[enemy.Cell].DefenseTerrain is "mountain" or "forest",
        "enemy_forest_fort" => w.Cells[enemy.Cell].DefenseTerrain == "forest" || Category(w, enemy) == "fort",
        "flanking" => w.Units.Any(t => t.Id != u.Id && t.Owner == u.Owner && TacticalSkills.Ground(w, t) && Distance(w, t.Cell, enemy.Cell) == 1),
        "two_flanking" => w.Units.Count(t => t.Id != u.Id && t.Owner == u.Owner && TacticalSkills.Mobile(w, t) && Distance(w, t.Cell, enemy.Cell) == 1) >= 2,
        "infantry_flanking" => w.Units.Any(t => t.Id != u.Id && t.Owner == u.Owner && Category(w, t) == "infantry" && Distance(w, t.Cell, enemy.Cell) == 1),
        "other_type_flanking" => w.Units.Any(t => t.Id != u.Id && t.Owner == u.Owner && Category(w, t) != Category(w, u) && Category(w, t) is "infantry" or "artillery" && Distance(w, t.Cell, enemy.Cell) == 1),
        "first_against_target" => !w.ExtensionState.ContainsKey("orders/hit/" + u.Owner + "/" + enemy.Id + "/infantry/" + w.Round),
        "other_type_hit" => w.ExtensionState.ContainsKey("orders/hit/" + u.Owner + "/" + enemy.Id + "/" + (Category(w, u) == "artillery" ? "infantry" : "artillery") + "/" + w.Round),
        "infantry_hit" => w.ExtensionState.ContainsKey("orders/hit/" + u.Owner + "/" + enemy.Id + "/infantry/" + w.Round),
        _ => Condition(w, source, u, c)
    };

    public static OrderCombatBonus Resolve(WorldState w, UnitState u, UnitState enemy, bool main, RuleEventBus bus)
    {
        var bonuses = new List<(ExtensionSkill Skill, CommandOrder Rule)>();
        foreach (var source in w.Units.Where(t => t.Owner == u.Owner))
            foreach (var (skill, _, r, _) in BoundRules(source, bus).Where(p => p.Rule.Event == "combat"))
            {
                if (!Matches(w, source, source, r.Source) || !Matches(w, source, u, r.Recipients)
                    || Distance(w, source.Cell, u.Cell) > r.Recipients.Radius || !r.Conditions.All(c => CombatCondition(w, source, u, enemy, c, main))) continue;
                bonuses.Add((skill, r));
            }
        foreach (var g in u.Orders.Where(g => !g.MarkerOnly && Live(w, u, g, bus)))
        {
            if (g.Pending) continue;
            var skill = bus.Skills[g.Skill]; var r = DynamicGrant(w,u,g,GrantRule(skill,g),bus);
            if(r.Plan=="op_entrench" && !TacticalSkills.Ground(w,enemy))continue;
            if(r.Plan=="op_air_ground")continue;
            if (g.Enemy != null && g.Enemy != enemy.Id || r.Uses > 0 && g.Uses >= r.Uses
                || r.Plan == "air_close_support" && Distance(w, Unit(w,g.Source)!.Cell,u.Cell)>1
                || r.Plan == "intelligence_warning" && Get(w, u, "warning.used") == Epoch(w, u.Owner) + 1
                || r.Mode == "assault" && !main || r.Mode == "guard" && main
                || r.Mode == "plan" || !r.Conditions.All(c => CombatCondition(w, Unit(w, g.Source)!, u, enemy, c, main))) continue;
            if (r.Event == "start" && Distance(w, u.Cell, Unit(w, g.Source)!.Cell) > (r.Plan == "intelligence_warning"
                ? WarningRange(w, Unit(w, g.Source)!, bus, true) : r.Recipients.Radius)) continue;
            bonuses.Add((skill, r));
        }
        var unique = bonuses.GroupBy(p => p.Skill.Id).Select(g => g.OrderByDescending(p=>p.Rule.Bonus.Attack+p.Rule.Bonus.Defense+p.Rule.Bonus.CounterAttack+p.Rule.Bonus.CounterDefense).First()).ToArray();
        double Sum(Func<OrderBonus, double> f, bool common) => unique.Where(p => p.Skill.Id.Contains(".common.") == common).Sum(p => f(p.Rule.Bonus));
        double Strongest(Func<OrderBonus,double> f)=>unique.Where(p=>!p.Skill.Id.Contains(".common.")).Select(p=>f(p.Rule.Bonus)).DefaultIfEmpty().Max();
        var resolved = new OrderCombatBonus(Math.Min(.35, Sum(b => b.Attack, false)), Math.Min(.25, Strongest(b=>b.Defense)),
            Math.Min(.35, Sum(b => b.CounterAttack, false)), Math.Min(.25, Strongest(b=>b.CounterDefense)),
            Math.Min(.3,Sum(b => b.Attack, true)), Math.Min(.25,Sum(b => b.Defense, true)), unique.Any(p => p.Rule.Bonus.SuppressCounter),
            unique.Any(p => p.Rule.Bonus.IgnoreEvasion), unique.Select(p => p.Rule.Bonus.IgnoreTerrain).DefaultIfEmpty().Max(),
            unique.Select(p => p.Rule.Bonus.IgnoreBuilding).DefaultIfEmpty().Max(), unique.Sum(p => p.Rule.Bonus.LowerAttack),
            unique.Sum(p => p.Rule.Bonus.UpperAttack), !main && unique.Any(p => p.Rule.Plan == "survive"),
            Math.Min(.3,Sum(b => b.CounterAttack, true)), Math.Min(.25,Sum(b => b.CounterDefense, true)));
        return FireBonus(w, u, enemy, main, bus, ResolvePlans(w, u, enemy, main, bus, resolved));
    }

    public static WorldState EndTurn(WorldState w, string owner, RuleEventBus bus)
    {
        if (!bus.OrdersEnabled) return w;
        foreach (var u in w.Units.Where(t => t.Owner == owner))
        {
            w = Set(w, u, "last_cell", u.Cell + 1); w = Set(w, u, "last_round", w.Round);
            w = Set(w, u, "last_moved", u.MovedThisTurn ? 1 : 0);
            w = Set(w, u, "last_end", w.Revision + 1);
            w = EndPlans(w, u, bus);
            var latest = Unit(w, u.Id)!;
            if (latest.OrderMorale.Any(m => m.EndEpoch <= Epoch(w, owner)))
                w = Replace(w, RefreshOrderMorale(w, latest with { OrderMorale = latest.OrderMorale.Where(m => m.EndEpoch > Epoch(w, owner)).ToArray() }));
        }
        return w;
    }
    public static WorldState StartTurn(WorldState w, string owner, RuleEventBus bus)
    {
        if (!bus.OrdersEnabled) return w;
        if (w.ExtensionState.GetValueOrDefault("orders/started/" + owner) == w.Round) return w;
        var epochs = w.ExtensionState.ToDictionary(p => p.Key, p => p.Value);
        epochs["orders/turn/" + owner] = Epoch(w, owner) + 1;
        epochs["orders/started/" + owner] = w.Round;
        w = w with { ExtensionState = epochs };
        w = Prune(w, bus);
        var snapshot = w;
        foreach (var old in snapshot.Units.Where(t => t.Owner == owner))
        {
            w = Set(w, old, "attacks", 0); w = Set(w, old, "defenses", 0);
            w=Set(w,old,"air_defenses",0); w=Set(w,old,"kills",0);
            w = StartPlans(w, old, bus);
            foreach (var (skill, slot, r, branch) in BoundRules(old, bus).Where(p => p.Rule.Event == "start"))
            {
                if (!Matches(snapshot, old, old, r.Source) || Get(w, old, "start." + skill.Id + "." + branch) == w.Round) continue;
                var selectionRule = r.Plan == "intelligence_warning" ? r.Recipients with { Radius = WarningRange(snapshot, old, bus, false) } : r.Recipients;
                var selected = r.SelectSelf ? new[] { old } : Select(snapshot, old, selectionRule).ToArray();
                foreach (var target in selected) w = ApplyBonus(w, old, Unit(w, target.Id)!, skill, slot, r, null, bus, branch);
                if (r.Plan == "intelligence_warning" && Condition(snapshot, old, old, "city"))
                    foreach (var (terrain, terrainSlot, terrainRule, terrainBranch) in BoundRules(old, bus).Where(p => p.Rule.Plan == "intel_terrain"))
                        foreach (var target in selected) w = ApplyBonus(w, old, Unit(w, target.Id)!, terrain, terrainSlot,
                            terrainRule with { Bonus = new() { Waivers = new[] { "mountain", "forest" } } }, null, bus, terrainBranch);
                w = Set(w, old, "start." + skill.Id + "." + branch, w.Round);
            }
        }
        w=OperationStart(w,owner,bus);
        return w with { Units = w.Units.Select(u => u.OrderMorale.Count == 0 ? u : RefreshOrderMorale(w, u)).ToArray() };
    }

    public static WorldState AfterBattle(WorldState before, WorldState w, string attacker, string defender, RuleEventBus bus, bool counterattack = false)
    {
        var a = Unit(before, attacker)!; var d = Unit(before, defender)!;
        var counters = w.ExtensionState.ToDictionary(p => p.Key, p => p.Value);
        counters["orders/hit/" + a.Owner + "/" + d.Id + "/" + Category(before, a) + "/" + w.Round] = 1;
        w = w with { ExtensionState = counters };
        if (Unit(w, a.Id) is { } sa)
        {
            w = Set(w, sa, "attacks", Get(before, a, "attacks") + 1);
            if (Unit(w,d.Id)==null) w=Set(w,sa,"kills",Get(before,a,"kills")+1);
            if (Category(before, d) == "air") w = Set(w, sa, "air_attack_round", w.Round);
            if (Category(before, d) == "air" && Unit(w, d.Id) == null) { w = Set(w, sa, "air_kill_round", w.Round); w = Set(w, sa, "air_kill_cell", d.Cell + 1); }
            if (Category(before, d) == "naval") w = Set(w, sa, "naval_attack_round", w.Round);
            if (Unit(w, d.Id) != null && (Category(before, d) == "fort" || before.Cells[d.Cell].BuildingKind == "city")) w = Set(w, sa, "siege_round", w.Round);
            if (Unit(w, d.Id) == null && before.Cells[d.Cell].BuildingKind == "city") w = Set(w, sa, "city_kill_round", w.Round);
        }
        if (Unit(w, d.Id) is { } sd)
        {
            w = Set(w, sd, "defenses", Get(before, d, "defenses") + 1);
            w = Set(w, sd, "received_round", w.Round);
            w = Set(w, sd, "received_revision", w.Revision + 1);
            if (WorldUnitLayers.Air(before, a)) w = Set(w, sd, "air_defenses", Get(before, d, "air_defenses") + 1);
            if (TacticalSkills.Mobile(before, a)) w = Set(w, sd, "attacker." + a.Id, w.Revision + 1);
        }
        foreach (var old in new[] { a, d })
        {
            if (Unit(w, old.Id) is not { } u) continue;
            bool main = old.Id == attacker; var enemy = main ? d : a;
            foreach (var g in u.Orders.Where(g => Live(before, old, g, bus)).ToArray())
            {
                var r = GrantRule(bus.Skills[g.Skill], g);
                if(Operation(r) && r.Plan is not ("op_artillery" or "op_armor"))continue;
                if (g.MarkerOnly || g.Pending || r.Mode is "plan" or "income" or "discount" || r.Mode == "assault" && !main || r.Mode == "guard" && main
                    || g.Enemy != null && g.Enemy != enemy.Id
                    || !r.Conditions.All(c => CombatCondition(before, Unit(before, g.Source)!, old, enemy, c, main))) continue;
                u = u with { Orders = u.Orders.Select(x => x == g ? x with { Uses = x.Uses + 1 } : x).ToArray() };
                w = Replace(w, u);
                if (main && r.Plan=="air_spot_action" && Distance(before,Unit(before,g.Source)!.Cell,old.Cell)<=2)
                    w=Set(w,u,"air_spot_used",Epoch(w,u.Owner)+1);
                if (r.Plan == "intelligence_warning") w = Set(w, u, "warning.used", Epoch(w, u.Owner) + 1);
            }
            foreach (var (skill, slot, r, branch) in BoundRules(old, bus).Where(p => p.Rule.Event == (main ? "after_attack" : "after_defend")))
            {
                if (r.Mode == "followup" || r.Plan == "choose" || r.Plan == "counter_heal") continue;
                if (!Matches(before, old, old, r.Source) || !r.Conditions.All(c => c == "kill" ? Unit(w, enemy.Id) == null : CombatCondition(before, old, old, enemy, c, main))) continue;
                foreach (var target in r.SelectSelf ? new[] { u } : Select(w, u, r.Recipients).ToArray())
                    w = ApplyBonus(w, u, Unit(w, target.Id)!, skill, slot, r, null, bus, branch);
            }
        }
        w=OperationBattle(before,AfterChoiceBattle(before, AfterFirePlans(before, AfterAdvanced(before, AfterPlans(before, w, a, d, bus), a, d, bus, counterattack), a, d, bus), a, d, bus),a,d,bus);
        foreach(var id in new[]{a.Id,d.Id}) if(Unit(w,id) is {} survivor) w=Set(w,survivor,"redirect",0);
        return w;
    }

    public static (MovementCell[] Cells, MovementActor Actor) MovementContext(WorldState w, UnitState u,
        (MovementCell[] Cells, MovementActor Actor) context, RuleEventBus? bus)
    {
        if (bus?.OrdersEnabled != true || !TacticalSkills.Mobile(w, u)) return context;
        var grants = u.Orders.Where(g => !g.MarkerOnly && Live(w, u, g, bus) && !g.Pending).Select(g => (Grant: g, Rule: GrantRule(bus.Skills[g.Skill], g)))
            .Select(p => p.Rule.Plan == "different_type_terrain" && p.Grant.Stage == 1 ? (p.Grant, Rule: p.Rule with { Bonus = new() { Waivers = new[] { "mountain", "forest" } } }) : p).ToArray();
        bool Applicable(CommandOrder r, int cell) => r.Plan switch {
            "own_road" => w.Cells[cell].Owner == u.Owner && (w.Cells[cell].BuildingKind == "city" || w.Cells[cell].DefenseTerrain == "road"),
            "road_recipient" or "intel_terrain" => w.Cells[cell].Owner == u.Owner,
            "enemy_terrain" => w.Cells[cell].Owner is string owner && !WorldDiplomacy.Allied(w, u.Owner, owner),
            _ => true
        };
        var cells = context.Cells.Select((cell, i) => {
            var waivers = grants.Where(p => Applicable(p.Rule, i)).SelectMany(p => p.Rule.Bonus.Waivers).ToHashSet();
            int cost=waivers.Contains(w.Cells[i].DefenseTerrain)?Math.Min(cell.TerrainCost,3):cell.TerrainCost;
            return cell with { TerrainCost = cost,
                LandCostCap=grants.Any(p=>p.Rule.Plan=="op_mountain") && w.Cells[i].DefenseTerrain is "mountain" or "forest"
                    ? Math.Max(1,(context.Actor.Category=="infantry"?3:cell.TerrainCost)-1):cell.LandCostCap,
                RiverEdges = waivers.Contains("river") && !cell.Sea && !w.Cells[u.Cell].Terrain.Sea ? (byte)0 : cell.RiverEdges };
        }).ToArray();
        bool first = BoundRules(u, bus).Any(p => p.Rule.Plan == "first_river") && Get(w, u, "river_used") != Epoch(w, u.Owner) + 1;
        return (cells, context.Actor with { FreeRiverCrossings = first ? 1 : 0 });
    }

    private static WorldState Summon(WorldState w, UnitState source, ExtensionSkill skill, SkillSlot slot, IReadOnlyList<string> selected, RuleEventBus bus)
    {
        var r = skill.Order!;
        if (selected.Count != r.Profiles.Length || selected.Distinct().Count() != selected.Count
            || r.SourceLimit > 0 && w.Units.Count(u => u.OrderOrigin == new OrderSummonOrigin(skill.Id, Identity(source))) + selected.Count > r.SourceLimit
            || !WorldArmyCapacity.CanAdd(w, source.Owner, w.Units.Count(u => u.Owner == source.Owner) + selected.Count - 1))
            throw new RuleViolation("orders.summon", "Incomplete batch or source limit exceeded");
        var faction = w.Factions.Single(f => f.Id == source.Owner);
        var templates = SummonOffers(w, source, skill, bus) ?? throw new RuleViolation("orders.summon", "Unit template, base price or recruitment source unavailable");
        var cells = selected.Select(id => int.TryParse(id.AsSpan("cell.".Length), out int c) ? c : -1).ToArray();
        if (cells.Where((c, i) => c < 0 || c >= w.Cells.Count || w.Cells[c].Owner != source.Owner || Distance(w, source.Cell, c) > skill.Range
            || !LegalDestination(w, new UnitState { Owner = source.Owner, Cell = source.Cell, Combatant = templates[i].Template! }, c)).Any())
            throw new RuleViolation("orders.summon", "Every deployment cell must be legal");
        var cost = SummonCost(w, source, skill, bus)!;
        if (faction.Resources.Money < cost.Money || faction.Resources.Industry < cost.Industry || faction.Resources.Food < cost.Food)
            throw new RuleViolation("skill.resources", "Insufficient resources for the complete batch");
        var units = w.Units.ToList();
        for (int i = 0; i < cells.Length; i++)
        {
            var template = templates[i]; string id = "orders.summon." + w.Revision + "." + cells[i];
            units.Add(new() { Id = id, Owner = source.Owner, Cell = cells[i], Acted = true, Support = template.Support,
                Combatant = template.Template! with { Health = template.Template!.MaximumHealth, BuildingKind = w.Cells[cells[i]].BuildingKind },
                OrderOrigin = new(skill.Id, Identity(source)) });
        }
        return w with { Units = units.ToArray(), Factions = w.Factions.Select(f => f.Id != source.Owner ? f : f with {
            Resources = new(f.Resources.Money - cost.Money, f.Resources.Industry - cost.Industry, f.Resources.Food - cost.Food) }).ToArray() };
    }
}
