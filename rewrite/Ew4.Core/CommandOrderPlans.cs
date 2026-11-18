using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public static partial class CommandOrders
{
    public static bool HasPending(WorldState w, UnitState u, ExtensionSkill s) => u.Orders.Any(g => g.Pending && g.Skill == s.Id
        && g.Source == u.Id && g.Commander == u.CommanderId && Epoch(w, u.Owner) == g.Epoch);

    private static bool ConnectedOwn(WorldState w, UnitState a, UnitState b)
    {
        if (!OwnSite(w, a) || !OwnSite(w, b)) return false;
        var reached = new HashSet<int> { a.Cell }; var pending = new Queue<int>(); pending.Enqueue(a.Cell);
        var map = new HexMap(w.Width, w.Height);
        while (pending.TryDequeue(out int cell))
        {
            if (cell == b.Cell) return true;
            for (int d = 0; d < 6; d++)
            {
                int next = map.Neighbor(cell, d);
                if (next >= 0 && w.Cells[next].Owner == a.Owner && w.Cells[next].Terrain.CanTraverse() && !w.Cells[next].Terrain.Sea && reached.Add(next)) pending.Enqueue(next);
            }
        }
        return false;
    }

    private static void ValidateSwap(WorldState w, UnitState source, UnitState a, UnitState b, CommandOrder r, string? enemy)
    {
        if(MoveLocked(w,a) || MoveLocked(w,b))throw new RuleViolation("orders.move_locked","Displacement is locked for this owner turn");
        bool valid = r.Plan switch {
            null or "road_recipient" or "own_road" or "morale_guard" or "mutual_guard" or "forest_ambush" => true,
            "vulnerable_pair" => (Condition(w, source, a, "vulnerable") || Condition(w, source, b, "vulnerable")) && ConnectedOwn(w, a, b),
            "weaker_guard" or "extract_weaker" => Health(b) < Health(a),
            "extract_vulnerable" => Condition(w, source, b, "vulnerable"),
            "rear_city_road" => OwnSite(w, a) && (Condition(w, source, a, "city_or_road") || Condition(w, source, b, "city_or_road")),
            "infantry_artillery" => new[] { Category(w, a), Category(w, b) }.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(new[] { "artillery", "infantry" }),
            "rear_guard" => (Category(w, a) == "infantry" || Category(w, b) == "infantry") && OwnSite(w, a)
                && !w.Units.Any(t => !WorldDiplomacy.Allied(w, source.Owner, t.Owner) && TacticalSkills.Ground(w, t) && Distance(w, a.Cell, t.Cell) == 1),
            "swap_flank" => enemy == null || Unit(w, enemy) is { } foe && TacticalSkills.Ground(w, foe)
                && Distance(w, a.Cell, foe.Cell) == 1 && Distance(w, b.Cell, foe.Cell) == 1,
            "naval_swap" => Unit(w, enemy) is { } foe && Category(w, foe) == "naval"
                && Distance(w, a.Cell, foe.Cell) <= 2 && Distance(w, b.Cell, foe.Cell) <= 2,
            "op_swap" => true,
            _ => false
        };
        if (!valid) throw new RuleViolation("orders.swap", "The selected pair does not meet the swap conditions");
    }

    private static bool ReceivesBonus(WorldState w, UnitState source, UnitState u, IReadOnlyList<UnitState> selected, CommandOrder r) => r.Plan switch {
        "road_recipient" => u.Id == selected[0].Id,
        // The first selected unit identifies the rear destination before the swap.
        "rear_city_road" or "rear_guard" => u.Id == selected[1].Id,
        "own_road" or "morale_guard" or "forest_ambush" => u.Id == source.Id,
        "extract_vulnerable" or "extract_weaker" or "weaker_guard" => u.Id != source.Id,
        "infantry_artillery" => Category(w, u) == "artillery",
        "vulnerable_pair" => u.Id == selected.OrderBy(t => t.Morale.Effective).ThenBy(t => t.Id, StringComparer.Ordinal).First().Id,
        _ => true
    };

    private static int[] SummonCells(WorldState w, UnitState source, ExtensionSkill skill) => Enumerable.Range(0, w.Cells.Count)
        .Where(c => Distance(w, source.Cell, c) <= skill.Order!.Recipients.Radius && w.Cells[c].Owner == source.Owner
            && w.Cells[c].Terrain.CanTraverse() && !w.Cells[c].Terrain.Sea && !WorldUnitLayers.Surface(w).Any(u => u.Cell == c)).ToArray();

    private static int[] FollowupCells(WorldState w, UnitState u, ExtensionSkill skill)
        => FollowupRange(w, u, skill).Keys.OrderBy(c => c).ToArray();

    private static IReadOnlyDictionary<int, ReachableCell> FollowupRange(WorldState w, UnitState u, ExtensionSkill skill)
    {
        var pending = u.Orders.FirstOrDefault(g => g.Pending && g.Skill == skill.Id && g.Commander == u.CommanderId);
        if (pending == null || skill.Order!.Followup == 0) return new Dictionary<int, ReachableCell>();
        var r = skill.Order;
        var move = WorldActions.MovementContext(w, u);
        bool air = WorldUnitLayers.Air(w, u);
        var cells = move.Cells.Select((c,i) => !air && c.Sea != w.Cells[u.Cell].Terrain.Sea ? c with { Active = false } : c).ToArray();
        if (r.Plan is "forest_escape" or "battle_confusion_escape") cells = cells.Select((c,i) => w.Cells[i].DefenseTerrain is "forest" or "mountain"
            ? c with { TerrainCost = 3 } : c).ToArray();
        int budget = Category(w, u) == "naval" ? r.Followup : r.Followup * 3;
        var actor = move.Actor with { Remaining = budget, FullBudget = budget };
        var map = new HexMap(w.Width, w.Height);
        var paths = new Dictionary<int, ReachableCell>();
        void Walk(int from, int remaining, int[] path)
        {
            if (path.Length > r.Followup) return;
            for (int d = 0; d < 6; d++)
            {
                int next = map.Neighbor(from,d);
                if (next < 0 || path.Contains(next) || !LegalDestination(w,u,next,u.Id)
                    || w.Units.Any(t => t.Cell == next && t.Id != u.Id && WorldUnitLayers.Same(w,t,u))) continue;
                int? cost = Movement.EntryCost(cells[from],cells[next],(d+3)%6,actor,new());
                if (cost == null || cost > remaining) continue;
                if (r.Plan is "advance_two_stage" or "advance" && path.Length == 1 && next != pending.EnemyCell) continue;
                if (r.Plan == "advance_inspire" && next != pending.EnemyCell) continue;
                var route = path.Append(next).ToArray();
                if ((r.Plan != "lateral" || Distance(w,pending.Cell,next)==1 && Distance(w,pending.EnemyCell,next)==1)
                    && (r.Plan != "forest_escape" || w.Cells[next].DefenseTerrain=="forest")
                    && (!paths.TryGetValue(next,out var old) || old.Remaining < remaining-cost.Value))
                    paths[next] = new(next,remaining-cost.Value,route);
                Walk(next,remaining-cost.Value,route);
            }
        }
        Walk(u.Cell,budget,new[]{u.Cell});
        return paths;
    }

    private static WorldState CompleteFollowup(WorldState w, UnitState source, ExtensionSkill skill, IReadOnlyList<string>? selected, RuleEventBus bus)
    {
        if (selected == null || selected.Count != 1 || !selected[0].StartsWith("cell.", StringComparison.Ordinal)
            || !int.TryParse(selected[0].AsSpan(5), out int cell) || !FollowupCells(w, source, skill).Contains(cell))
            throw new RuleViolation("orders.followup", "Choose one legal post-battle destination");
        var range = FollowupRange(w, source, skill);
        w = WorldActions.OrderMove(w, source, cell, range, new() { Extensions = bus });
        var result = Unit(w, source.Id)!;
        w = Replace(w, result with { Orders = result.Orders.Where(g => !g.Pending || g.Stage < 0).ToArray() });
        w = Set(w, source, "followup", w.Round);
        if (skill.Order!.Plan=="advance_inspire") w=Morale(w,source,Unit(w,source.Id)!,new(){MoraleChange=1},skill.Id,bus);
        return WorldDeaths.RefreshMorale(w, w.DiplomacyNeutralCamps);
    }

    private static OrderCombatBonus ResolvePlans(WorldState w, UnitState u, UnitState enemy, bool main, RuleEventBus bus, OrderCombatBonus bonus)
    {
        foreach (var (skill, _) in bus.Bound(u.Combatant).Where(p => p.Skill.Order?.Plan != null))
        {
            var r = skill.Order!;
        if (!Matches(w, u, u, r.Source)) continue;
            if (r.Plan is "next_attack_on_kill" or "next_attack_on_defend" or "net_movement_attack"
                && main && Get(w, u, "next_attack." + skill.Id) == w.Round && Get(w, u, "attacks") == 0)
                bonus = bonus with { Attack = Math.Min(.35, bonus.Attack + .15) };
        }
        foreach (var g in u.Orders.Where(g => !g.MarkerOnly && Live(w, u, g, bus) && !g.Pending))
        {
            var r = GrantRule(bus.Skills[g.Skill], g);
            if (r.Uses > 0 && g.Uses >= r.Uses || g.Enemy != null && g.Enemy != enemy.Id || !CombatApplies(w, Unit(w, g.Source)!, u, enemy, r, main)) continue;
            if (main)
            {
                if (r.Plan == "artillery_handoff" && g.Uses == 0 && Unit(w,g.Source) is { } observer && Distance(w,observer.Cell,u.Cell)<=1)
                    bonus = bonus with { Attack = Math.Min(.35,bonus.Attack+.1) };
                bool suppress = r.Plan switch {
                    "pre_morale" or "encircled_target" => enemy.Morale.Effective < 0,
                    "assault_suppress" => true,
                    "combined_breach" => CombatCondition(w, u, u, enemy, "other_type_flanking", true),
                    "siege_followup" => CombatCondition(w, u, u, enemy, "infantry_hit", true),
                    "forest_ambush" => w.Cells[u.Cell].DefenseTerrain is "mountain" or "forest" && Distance(w, u.Cell, enemy.Cell) == 1 && TacticalSkills.Mobile(w, enemy),
                    "battle_confusion_escape" => !enemy.Combatant.Has(Ew4Combat.Skill(3)),
                    _ => false
                };
                if (r.Plan == "battle_confusion_escape" && enemy.Combatant.Has(Ew4Combat.Skill(3))) bonus = bonus with { SuppressCounter = false };
                else bonus = bonus with { SuppressCounter = bonus.SuppressCounter || suppress };
            }
        }
        if (!main && WorldUnitLayers.Air(w, enemy) && Get(w, u, "air_defenses") == 0
            && BoundRules(u, bus).Any(p => p.Rule.Plan == "air_evasion")) bonus = bonus with { GenericCounterAttack = bonus.GenericCounterAttack - .2 };
        if (main && Get(w, u, "cover." + enemy.Id) == w.Revision + 1) bonus = bonus with { SuppressCounter = true };
        if (!main)
        {
            var redirect = u.Orders.FirstOrDefault(g => Live(w, u, g, bus) && !g.Pending && g.Uses == 0
                && GrantRule(bus.Skills[g.Skill], g).Plan == "damage_redirect" && Unit(w, g.Source) is { } p
                && p.Id != u.Id && Distance(w, p.Cell, u.Cell) == 1);
            if (redirect != null) bonus = bonus with { RedirectCapacity = Math.Min(40, Unit(w, redirect.Source)!.Combatant.Health - 1) };
        }
        return bonus;
    }

    private static int WarningRange(WorldState w, UnitState source, RuleEventBus bus, bool receiving) =>
        BoundRules(source, bus).Any(p => p.Rule.Plan == "intel_range" || p.Rule.Plan == "intel_range_city" && Condition(w, source, source, "city"))
            ? receiving ? 3 : 2 : receiving ? 2 : 1;

    private static WorldState EndPlans(WorldState w, UnitState u, RuleEventBus bus)
    {
        foreach (var (skill, _) in bus.Bound(u.Combatant).Where(p => p.Skill.Order?.Plan == "net_movement_attack"))
            if (!u.AttackedThisTurn && Get(w, u, "start_cell") is long cell && cell > 0 && Distance(w, (int)cell - 1, u.Cell) >= 2)
                w = Set(w, u, "next_attack." + skill.Id, w.Round + 1);
        foreach (var g in u.Orders.Where(g => Live(w, u, g, bus) && GrantRule(bus.Skills[g.Skill], g).Plan == "city_direction"))
            if (!u.AttackedThisTurn && g.EnemyCell >= 0 && Distance(w, u.Cell, g.EnemyCell) < Distance(w, g.Cell, g.EnemyCell))
                w = Set(w, u, "next_morale", Epoch(w, u.Owner) + 1);
        return w;
    }

    private static WorldState StartPlans(WorldState w, UnitState u, RuleEventBus bus)
    {
        w = Set(w, u, "start_cell", u.Cell + 1);
        if (Get(w, u, "next_morale") == Epoch(w, u.Owner)) w = Morale(w, u, Unit(w, u.Id)!, new() { MoraleChange = 1 }, "order.direction");
        foreach (var (skill, slot) in bus.Bound(u.Combatant).Where(p => p.Skill.Order?.Plan != null))
        {
            var r = skill.Order!;
            if (!Matches(w, u, u, r.Source)) continue;
            if (r.Plan == "next_movement_on_defend" && Get(w, u, "next_movement." + skill.Id) == w.Round)
                w = ApplyBonus(w, u, Unit(w, u.Id)!, skill, slot, r with { Bonus = new() { Movement = 1 } }, null, bus);
            if (r.Plan == "next_ally_movement_on_kill" && Get(w, u, "next_ally_movement") == w.Round)
                foreach (var target in Select(w, u, new() { Categories = new[] { "infantry" }, Other = true }))
                    w = ApplyBonus(w, u, target, skill, slot, r with { Bonus = new() { Movement = 1 } }, null, bus);
            if (r.Plan == "landing_recovery" && Get(w, u, "landed_round") == w.Round - 1 && Category(w, u) == "infantry" && Condition(w, u, u, "port_two"))
                w = ApplyBonus(w, u, Unit(w, u.Id)!, skill, slot, r with { Bonus = new() { Heal = 20 } }, null, bus);
            if (r.Plan?.StartsWith("choose_", StringComparison.Ordinal) == true && ReactionAvailable(w, u, skill)
                && r.Plan is "choose_morale_city" or "choose_infantry_mark" or "choose_artillery_mark")
                w = OfferChoice(w, u, skill, slot);
            if (r.Plan is "choose_swap_on_defend" or "choose_swap_on_shock" && Get(w, u, "swap_next." + skill.Id) == w.Round)
                w = OfferChoice(w, u, skill, slot, w.Units.FirstOrDefault(t => Get(w, u, "swap_target." + skill.Id + "." + t.Id) == w.Round)?.Id);
            if (r.Plan == "construction_finish" && Get(w, u, "construction." + skill.Id) is long fortCell && fortCell > 0)
            {
                var fort = w.Units.FirstOrDefault(t => t.Cell == fortCell - 1 && t.Owner == u.Owner && Category(w, t) == "fort" && t.Combatant.ConstructionTurns > 0);
                if (fort != null) w = Replace(w, fort with { Combatant = fort.Combatant with { ConstructionTurns = Math.Max(0, fort.Combatant.ConstructionTurns - 1) } });
                w = Set(w, u, "construction." + skill.Id, 0);
            }
        }
        return w;
    }

    private static WorldState AfterPlans(WorldState before, WorldState w, UnitState a, UnitState d, RuleEventBus bus)
    {
        foreach (var old in new[] { a, d })
        {
            if (Unit(w, old.Id) is not { } u) continue;
            bool main = old.Id == a.Id;
            foreach (var (skill, slot) in bus.Bound(old.Combatant).Where(p => p.Skill.Order != null))
            {
                var r = skill.Order!;
                if (!Matches(before, old, old, r.Source)) continue;
                if (r.Plan == "next_attack_on_kill" && main && Unit(w, d.Id) == null
                    || r.Plan == "next_attack_on_defend" && !main)
                    w = Set(w, u, "next_attack." + skill.Id, w.Round + 1);
                if (r.Plan == "next_movement_on_defend" && !main)
                    w = Set(w, u, "next_movement." + skill.Id, w.Round + 1);
                if (r.Plan == "next_ally_movement_on_kill" && main && Unit(w, d.Id) == null && old.Combatant.Profile.CommanderAbility == "tyej.armor")
                    w = Set(w, u, "next_ally_movement", w.Round + 1);
                if (r.Plan == "air_morale" && main && WorldUnitLayers.Air(before, d) && Unit(w, d.Id) is { } aircraft
                    && Get(before, old, "attacks") == 0 && Get(w, aircraft, "air_shock." + skill.Id) != Epoch(w, old.Owner) + 1)
                {
                    w = Morale(w, old, aircraft, new() { MoraleChange = -1 }, skill.Id, bus);
                    w = Set(w, aircraft, "air_shock." + skill.Id, Epoch(w, old.Owner) + 1);
                }
                if (r.Plan == "choose" && main && Unit(w, d.Id) == null && Get(before, old, "kills") == 0
                    && Get(w, u, "choice." + skill.Id) != Epoch(w, u.Owner) + 1) w = OfferChoice(w, u, skill, slot);
                if (r.Plan == "artillery_handoff" && main && Category(before,old)=="infantry" && Get(before,old,"attacks")==0
                    && Category(before,d)=="fort" && Unit(w,d.Id)!=null)
                {
                    var artillery = w.Units.Where(t => t.Owner==u.Owner && Category(w,t)=="artillery" && TacticalSkills.Mobile(w,t)
                        && !t.AttackedThisTurn && !t.Acted && Distance(w,u.Cell,t.Cell)<=1)
                        .OrderBy(t=>Distance(w,t.Cell,d.Cell)).ThenBy(t=>t.Id,StringComparer.Ordinal).FirstOrDefault();
                    if (artillery!=null) w=ApplyBonus(w,u,artillery,skill,slot,r,d.Id,bus);
                }
                if (r.Plan == "discipline" && Health(u) >= .7 && u.Morale.Effective < 0 && Get(w, u, "discipline." + skill.Id) != Epoch(w,u.Owner)+1)
                {
                    w = Morale(w, u, u, new() { MoraleFloor = 0 }, skill.Id);
                    w = Set(w, u, "discipline." + skill.Id, Epoch(w,u.Owner)+1);
                }
                if (r.Mode == "followup" && main && Get(before, old, r.Conditions.Contains("kill")?"kills":"attacks") == 0 && Get(w, u, "followup") != w.Round
                    && r.Conditions.All(c => c == "kill" ? Unit(w, d.Id) == null : CombatCondition(before, old, old, d, c, true)))
                {
                    var pending = new OrderGrant(skill.Id, slot.InstanceId, u.Id, u.Owner, u.CommanderId, w.Round,
                        old.Cell, d.Id, d.Cell, RecipientCommander: u.CommanderId, Epoch: Epoch(w, u.Owner), Pending: true);
                    w = Replace(w, Unit(w, u.Id)! with { Orders = Unit(w, u.Id)!.Orders.Append(pending).ToArray() });
                }
            }
        }
        return w;
    }
}
