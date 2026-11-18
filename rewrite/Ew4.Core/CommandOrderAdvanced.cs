using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public static partial class CommandOrders
{
    private static bool Weapon(WorldState w, UnitState u, string layer) => w.UnitLayers?.Profiles.TryGetValue(u.Combatant.Profile.Id, out var p) == true
        && p.AttackLayers.Contains(layer);
    private static bool LegalAttack(WorldState w, UnitState a, UnitState d, bool requireUnspent = true) =>
        !WorldDiplomacy.Allied(w, a.Owner, d.Owner) && WorldUnitLayers.CanAttack(w, a, d)
        && !AttackLocked(w,a)
        && a.Combatant.ConstructionTurns == 0 && a.Morale.Effective != -3 && (!requireUnspent || !a.Acted && !a.AttackedThisTurn)
        && Distance(w, a.Cell, d.Cell) >= a.Combatant.Profile.MinimumRange && Distance(w, a.Cell, d.Cell) <= a.Combatant.MaximumRange;
    private static bool PlanAvailable(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus? bus) => skill.Order!.Plan switch {
        "air_spot_action" => !source.Acted,
        "payload_action" => PayloadAvailable(w,source,skill.Order!,bus),
        "attacker_morale" => w.Units.Any(t => !WorldDiplomacy.Allied(w, source.Owner, t.Owner) && Distance(w, source.Cell, t.Cell) <= 2
            && Get(w,source,"attacker."+t.Id)>Get(w,source,"last_end")),
        _ => true
    };
    private static void ValidatePlanTargets(WorldState w, UnitState source, IReadOnlyList<UnitState> recipients,
        ExtensionSkill skill, string? enemy, string? secondary)
    {
        var r = skill.Order!; var foe = Unit(w, enemy);
        ValidateOperation(w,source,recipients,skill,enemy,secondary);
        bool valid = r.Plan switch {
            "battle_confusion_escape" or "attack_or_kill_shock" => foe != null && LegalAttack(w,source,foe),
            "calibration" => foe != null && recipients.Count == 1 && LegalAttack(w, recipients[0], foe),
            "bombard_suppress" => foe != null && recipients.Count == 1 && LegalAttack(w, recipients[0], foe) && (w.Cells[foe.Cell].BuildingKind == "city" || Category(w, foe) == "fort"),
            "terrain_sequence" => foe != null && recipients.Count == 2 && recipients.Select(t => Category(w, t)).Distinct().Count() == 2
                && recipients.All(t => LegalAttack(w, t, foe)) && (w.Cells[foe.Cell].DefenseTerrain is "mountain" or "forest" || Category(w, foe) == "fort"),
            "combined_shock" => foe != null && (w.Cells[foe.Cell].BuildingKind == "city" || Category(w, foe) == "fort"),
            "naval_block" => foe != null && LegalAttack(w, source, foe) && Condition(w, source, source, "port_near"),
            "encircled_target" => recipients.Count == 1 && LegalAttack(w, source, recipients[0]),
            "attacker_morale" => foe != null && Distance(w, source.Cell, foe.Cell) <= 2 && Get(w,source,"attacker."+foe.Id)>Get(w,source,"last_end"),
            "different_type_terrain" => recipients.Count is 1 or 2 && CanReactivate(w, recipients[0])
                && (recipients.Count == 1 || Category(w, recipients[0]) != Category(w, recipients[1])),
            _ => true
        };
        if (secondary != null && foe != null && Unit(w, secondary) is { } extra
            && Distance(w, foe.Cell, extra.Cell) > (r.Plan == "kill_shock_advance" ? 2 : 1)) valid = false;
        if (!valid) throw new RuleViolation("orders.plan_target", "Selected units do not satisfy the order's weapon, position or history requirements");
    }

    private static WorldState ActivatePlan(WorldState w, UnitState source, ExtensionSkill skill, SkillSlot slot,
        IReadOnlyList<UnitState> recipients, string? enemy, string? secondary, RuleEventBus bus)
    {
        var r = skill.Order!;
        if (r.Plan=="payload_action") return DeployPayload(w,source,skill,bus);
        if (r.Plan is "industry_income" or "food_income") w=ImmediateIncome(w,source,r,bus);
        // The source stores the complete plan; grants on recipients are effects, not extra attacks.
        var current = Unit(w, source.Id)!;
        var marker = new OrderGrant(skill.Id, slot.InstanceId, source.Id, source.Owner, source.CommanderId, w.Round,
            source.Cell, enemy, EnemyCell(w, enemy), RecipientCommander: current.CommanderId, Epoch: Epoch(w, source.Owner),
            Auxiliary: secondary, Participants: recipients.Select(t => t.Id).ToArray(), Positions: recipients.Select(t => t.Cell).ToArray(), Plan: r.Plan, Lifetime: r.Duration,
            MarkerOnly: !recipients.Any(t=>t.Id==source.Id) || !ReceivesBonus(w,source,source,recipients,r));
        w = Replace(w, current with { Orders = current.Orders.Where(g => g.Skill != skill.Id || g.Source != source.Id).Append(marker).ToArray() });
        if(r.Plan=="naval_block")
        {
            int port=Enumerable.Range(0,w.Cells.Count).Where(c=>w.Cells[c].Owner==source.Owner && w.Cells[c].BuildingKind=="port" && Distance(w,c,source.Cell)<=1)
                .OrderBy(c=>Distance(w,c,source.Cell)).ThenBy(c=>c).First();
            w=UpdateGrant(w,source.Id,marker,marker with{Positions=new[]{port}});
        }
        if(Operation(r))return ActivateOperation(w,source,skill,slot,recipients,enemy,secondary);
        if (r.Plan == "encircled_target")
        {
            marker = marker with { Enemy = recipients[0].Id, EnemyCell = recipients[0].Cell };
            w = Replace(w, Unit(w, source.Id)! with { Orders = Unit(w, source.Id)!.Orders.Select(g => g.Skill == skill.Id ? marker : g).ToArray() });
        }
        if (r.Plan is "attacker_morale" or "naval_block" && Unit(w, enemy) is { } foe)
            w = Morale(w, source, foe, new() { MoraleChange = -1 }, skill.Id, bus);
        if (r.Plan == "construction_finish")
        {
            // Selected fort is stored at activation and rechecked at the next owner turn.
            var fort = recipients.FirstOrDefault(t => t.Id != source.Id);
            if (fort != null) w = Set(w, source, "construction." + skill.Id, fort.Cell + 1);
        }
        if (r.Plan == "different_type_terrain" && recipients.Count > 1)
        {
            var u = Unit(w, recipients[1].Id)!;
            var grant = u.Orders.Last(g => g.Skill == skill.Id && g.Source == source.Id);
            w = UpdateGrant(w, u.Id, grant, grant with { Stage = 1 });
        }
        if (r.Plan == "city_rally" && recipients.Count > 0 && Condition(w, source, recipients[0], "city_near"))
            w = Morale(w, source, Unit(w, recipients[0].Id)!, new() { MoraleFloor = 0 }, skill.Id, bus);
        if (r.Plan == "air_kill_shock" && Get(w, source, "air_kill_round") == w.Round && secondary != null && Unit(w, secondary) is { } aircraft
            && Get(w, source, "air_kill_cell") > 0 && Distance(w, (int)Get(w, source, "air_kill_cell") - 1, aircraft.Cell) <= 1)
            w = Morale(w, source, aircraft, new() { MoraleChange = -1 }, skill.Id, bus);
        return w;
    }

    private static IEnumerable<(UnitState Source, OrderGrant Grant, ExtensionSkill Skill, CommandOrder Rule)> ActivePlans(WorldState w, string owner, RuleEventBus bus)
    {
        foreach (var source in w.Units.Where(t => t.Owner == owner))
            foreach (var grant in source.Orders.Where(g => g.Source == source.Id && !g.Pending && Live(w, source, g, bus)))
            {
                var s = bus.Skills[grant.Skill];
                yield return (source, grant, s, GrantRule(s, grant));
            }
    }
    private static CommandOrder GrantRule(ExtensionSkill s, OrderGrant g) => g.Branch < 0 ? s.Order! : s.Order!.Additional[g.Branch];
    private static IEnumerable<(ExtensionSkill Skill, SkillSlot Slot, CommandOrder Rule, int Branch)> BoundRules(UnitState u, RuleEventBus bus)
    {
        foreach (var (skill, slot) in bus.Bound(u.Combatant).Where(p => p.Skill.Order != null))
        {
            yield return (skill, slot, skill.Order!, -1);
            for (int i = 0; i < skill.Order!.Additional.Length; i++) yield return (skill, slot, skill.Order.Additional[i], i);
        }
    }
    private static bool CombatApplies(WorldState w, UnitState source, UnitState u, UnitState enemy, CommandOrder r, bool main) =>
        r.Conditions.All(c => CombatCondition(w, source, u, enemy, c, main));

    private static OrderCombatBonus FireBonus(WorldState w, UnitState u, UnitState enemy, bool main, RuleEventBus bus, OrderCombatBonus bonus)
    {
        if (!main) return bonus;
        foreach (var (source, g, _, r) in ActivePlans(w, u.Owner, bus))
        {
            if (g.Enemy != enemy.Id || r.Plan != "naval_block" && g.EnemyCell != enemy.Cell || r.Uses > 0 && g.Uses >= r.Uses) continue;
            bool participant = g.Participants?.Contains(u.Id) == true;
            bool used = (g.Auxiliary ?? "").Split('|').Contains(u.Id);
            if (r.Plan == "calibration" && g.Stage > 0 && !participant && !used && Category(w, u) == "artillery"
                && g.Positions?.Contains(u.Cell) != true)
                bonus = bonus with { IgnoreTerrain = Math.Max(.5, bonus.IgnoreTerrain), IgnoreBuilding = Math.Max(.5, bonus.IgnoreBuilding) };
            if (r.Plan == "bombard_suppress" && g.Stage > 0 && Category(w, u) == "infantry" && Get(w, u, "attacks") == 0)
                bonus = bonus with { SuppressCounter = true };
            if (r.Plan == "terrain_sequence" && g.Stage > 0 && participant && !used)
                bonus = bonus with { IgnoreTerrain = 1 };
            if (r.Plan == "naval_block" && (Category(w, u) == "naval" || u.Combatant.Profile.Traits.Contains("coastal_gun")) && !used
                && g.Positions?.Length>0 && Distance(w, g.Positions[0], enemy.Cell) <= 3)
                bonus = bonus with { IgnoreTerrain = Math.Max(.5, bonus.IgnoreTerrain), SuppressCounter = bonus.SuppressCounter || enemy.Morale.Effective < 0 };
            if (r.Plan is "swap_flank" or "naval_swap" && participant && g.Uses == 0)
                bonus = bonus with { SuppressCounter = true };
            if (r.Plan == "choose_infantry_mark" && Category(w, u) == "infantry" && enemy.Morale.Effective < 0 && g.Uses == 0)
                bonus = bonus with { SuppressCounter = true };
            if(r.Plan=="op_air_ground" && g.Stage==1 && g.Uses<2 && source.Id!=u.Id && LandForce(w,u)
                && (Category(w,u) is "infantry" or "artillery" || Tank(u)))bonus=bonus with{Attack=Math.Min(.35,bonus.Attack+r.Bonus.Attack)};
        }
        return bonus;
    }

    public static WorldState BeforeBattle(WorldState w, string attacker, string defender, RuleEventBus bus)
    {
        if (!bus.OrdersEnabled) return w;
        var a = Unit(w, attacker)!; var d = Unit(w, defender)!;
        var cover = AttackReaction(w, a, d, bus); w = cover.World;
        if (cover.Suppress) w = Set(w, a, "cover." + d.Id, w.Revision + 1);
        foreach (var g in a.Orders.Where(g => !g.MarkerOnly && Live(w, a, g, bus) && !g.Pending).ToArray())
        {
            var r = GrantRule(bus.Skills[g.Skill], g);
            if (r.Mode != "assault" || g.Uses > 0 || !CombatApplies(w, Unit(w, g.Source)!, a, d, r, true)) continue;
            if (r.Plan == "pre_morale") w = Morale(w, a, d, new() { MoraleChange = -1 }, g.Skill, bus);
            if (r.Plan == "battle_confusion_escape") w = Morale(w, a, d, new() { Morale = -3 }, g.Skill + ".battle", bus);
        }
        foreach (var g in d.Orders.Where(g => !g.MarkerOnly && Live(w, d, g, bus) && !g.Pending).ToArray())
        {
            var r = GrantRule(bus.Skills[g.Skill], g);
            if (r.Plan == "weaker_guard" && g.Uses == 0 && Unit(w, g.Source) is { } guarding && Distance(w, guarding.Cell, d.Cell) <= 2)
                w = Set(w, d, "local_shield", w.Revision + 1);
            if (r.Plan == "counter_resolve" && g.Uses < r.Uses && TacticalSkills.Ground(w, a) && Distance(w, a.Cell, d.Cell) == 1 && d.Morale.Effective < 0)
                w = Morale(w, d, Unit(w, d.Id)!, new() { MoraleFloor = 0 }, g.Skill, bus);
        }
        d = Unit(w, defender)!;
        if (LocalPenalty(w, d, d.LocalPenalty) != d.LocalPenalty) w = Replace(w, RefreshOrderMorale(w, d));
        return w;
    }

    // Damage transfers are calculated after all ordinary mitigation. Transferred
    // damage bypasses mitigation, and the protector always retains at least 1 HP.
    public static (WorldState State, int Damage) Incoming(WorldState w, string target, string attacker, int damage, RuleEventBus bus, bool mainAttack = true)
    {
        var u = Unit(w, target)!;
        foreach (var g in u.Orders.Where(g => !g.MarkerOnly && Live(w, u, g, bus) && !g.Pending).ToArray())
        {
            var r = GrantRule(bus.Skills[g.Skill], g);
            if (mainAttack && r.Plan == "damage_redirect" && g.Uses == 0 && Unit(w, g.Source) is { } protector && protector.Id != u.Id && Distance(w, protector.Cell, u.Cell) == 1)
            {
                int transferred = Math.Min(Math.Min(damage / 2, 40), protector.Combatant.Health - 1);
                w = Replace(w, protector with { Combatant = protector.Combatant with { Health = protector.Combatant.Health - transferred } });
                w = Set(w, u, "redirect", protector.Cell + 1);
                damage -= transferred;
                w = UpdateGrant(w, u.Id, g, g with { Uses = 1 });
                break;
            }
        }
        u = Unit(w, u.Id)!;
        if (mainAttack && damage >= u.Combatant.Health && Get(w, u, "survived") != Epoch(w, u.Owner) + 1)
            foreach (var g in u.Orders.Where(g => !g.MarkerOnly && Live(w, u, g, bus) && GrantRule(bus.Skills[g.Skill], g).Plan == "survive" && g.Uses == 0).ToArray())
            {
                damage = u.Combatant.Health - 1; w = Set(w, u, "survived", Epoch(w, u.Owner) + 1);
                w = UpdateGrant(w, u.Id, g, g with { Uses = 1 });
                foreach (var friend in w.Units.Where(t => t.Owner == u.Owner && Category(w, t) == "infantry" && Distance(w, t.Cell, u.Cell) <= 1).ToArray())
                    w = Morale(w, u, friend, new() { MoraleFloor = 0 }, g.Skill, bus);
                break;
            }
        return (w, damage);
    }
    private static WorldState UpdateGrant(WorldState w, string unit, OrderGrant old, OrderGrant replacement) => Unit(w, unit) is not { } u ? w
        : Replace(w, u with { Orders = u.Orders.Select(g => g == old ? replacement : g).ToArray() });

    private static WorldState AfterFirePlans(WorldState before, WorldState w, UnitState a, UnitState d, RuleEventBus bus)
    {
        foreach (var (source, g, skill, r) in ActivePlans(before, a.Owner, bus).ToArray())
        {
            if (g.Enemy != d.Id || r.Plan != "naval_block" && g.EnemyCell != d.Cell || r.Uses > 0 && g.Uses >= r.Uses || Unit(w, source.Id) == null) continue;
            bool participant = g.Participants?.Contains(a.Id) == true;
            bool used = (g.Auxiliary ?? "").Split('|').Contains(a.Id) || Get(before, source, "plan.used." + skill.Id + "." + a.Id) == before.Round;
            var next = g;
            if(r.Plan=="op_air_ground")
            {
                if(source.Id==a.Id && g.Stage==0)
                {
                    bool hit=Unit(w,d.Id) is {} surviving && surviving.Combatant.Health<d.Combatant.Health;
                    next=g with{Stage=hit?1:2,Uses=hit?0:2};
                    w=Set(w,source,"coordinated."+d.Id,Epoch(w,source.Owner)+1);
                }
                else if(g.Stage==1 && source.Id!=a.Id && LandForce(before,a) && (Category(before,a) is "infantry" or "artillery" || Tank(a)))next=g with{Uses=2};
            }
            if (r.Plan == "calibration")
            {
                if (g.Stage == 0 && participant) next = g with { Stage = 1, Uses = 0, Auxiliary = a.Id, Positions = new[] { a.Cell } };
                else if (g.Stage > 0 && !participant && !used && Category(before, a) == "artillery" && g.Positions?.Contains(a.Cell) != true)
                    next = g with { Uses = g.Uses + 1, Auxiliary = g.Auxiliary + "|" + a.Id, Positions = (g.Positions ?? Array.Empty<int>()).Append(a.Cell).ToArray() };
            }
            if (r.Plan == "bombard_suppress")
            {
                if (g.Stage == 0 && participant && Unit(w, d.Id) != null) next = g with { Stage = 1 };
                else if (g.Stage > 0 && Category(before, a) == "infantry" && Get(before, a, "attacks") == 0) next = g with { Uses = r.Uses };
            }
            if (r.Plan == "terrain_sequence" && participant && !used) next = g with { Stage = g.Stage + 1, Auxiliary = (g.Auxiliary ?? "") + "|" + a.Id, Uses = g.Stage > 0 ? 1 : 0 };
            if (r.Plan == "combined_shock" && !used && Category(before, a) is "infantry" or "artillery" && Unit(w, d.Id) is { } alive)
            {
                w = Morale(w, source, alive, new() { MoraleChange = -1 }, skill.Id + "." + a.Id, bus);
                if (g.Stage > 0 && g.Stage != (Category(before, a) == "infantry" ? 1 : 2) && g.Auxiliary is string extra && Unit(w, extra) is { } other && Distance(w, d.Cell, other.Cell) == 1)
                {
                    w = Morale(w, source, other, new() { MoraleChange = -1 }, skill.Id + ".handoff", bus);
                }
                next = g with { Uses = g.Uses + 1, Stage = g.Stage < 0 || g.Stage > 0 && g.Stage != (Category(before, a) == "infantry" ? 1 : 2)
                    ? -1 : Category(before, a) == "infantry" ? 1 : 2, Positions = (g.Positions ?? Array.Empty<int>()).Append(a.Cell).ToArray() };
                w = Set(w, source, "plan.used." + skill.Id + "." + a.Id, w.Round);
            }
            if (r.Plan == "naval_block" && !used && (Category(before, a) == "naval" || a.Combatant.Profile.Traits.Contains("coastal_gun")))
                next = g with { Uses = g.Uses + 1, Auxiliary = (g.Auxiliary ?? "") + "|" + a.Id };
            if (r.Plan is "swap_flank" or "naval_swap" && participant) next = g with { Uses = 1 };
            if (r.Plan == "choose_infantry_mark" && Category(before, a) == "infantry" && g.Uses == 0) next = g with { Uses = 1 };
            if (r.Plan == "choose_artillery_mark" && Category(before, a) == "artillery" && g.Uses == 0 && Unit(w, d.Id) is { } marked)
            {
                w = Morale(w, source, marked, new() { MoraleChange = -1 }, skill.Id, bus);
                next = g with { Uses = 1 };
            }
            if (next != g) w = UpdateGrant(w, source.Id, g, next);
        }
        return w;
    }

    private static WorldState AfterAdvanced(WorldState before, WorldState w, UnitState a, UnitState d, RuleEventBus bus, bool counterattack)
    {
        bool killed = Unit(w, d.Id) == null;
        foreach (var old in new[] { a, d })
        {
            bool main = old.Id == a.Id; var foe = main ? d : a;
            if (Unit(w, old.Id) is not { } alive) continue;
            foreach (var g in old.Orders.Where(g => !g.MarkerOnly && !g.Pending && Live(before, old, g, bus)).ToArray())
            {
                var skill = bus.Skills[g.Skill]; var r = GrantRule(skill, g);
                if (r.Uses > 0 && g.Uses >= r.Uses || g.Enemy != null && g.Enemy != foe.Id
                    || !CombatApplies(before, Unit(before, g.Source)!, old, foe, r, main)) continue;
                if (main && r.Mode is "assault" or "swap" or "morale")
                {
                    if (Unit(w, foe.Id) is { } target)
                    {
                        OrderBonus? morale = r.Plan switch {
                            "confuse_half" when Health(target) <= .5 => new() { Morale = -3 },
                            "forest_shock" when before.Cells[old.Cell].DefenseTerrain is "mountain" or "forest" => new() { MoraleChange = -1 },
                            "attack_or_kill_shock" => new() { MoraleChange = -2 },
                            "bombard_confusion" => foe.Morale.Effective < 0 ? new() { Morale = -3 } : new() { MoraleChange = -2 },
                            "net_movement_shock" when Distance(before, g.Cell, old.Cell) >= 2
                                && (Category(before, old) != "cavalry" || Condition(before, old, old, "infantry_near")) => new() { MoraleChange = -2 },
                            _ => null
                        };
                        if (morale != null) w = Morale(w, old, target, morale, skill.Id, bus);
                    }
                    else if (r.Plan is "attack_or_kill_shock" or "kill_shock_advance" && g.Auxiliary is string extra
                        && Unit(w, extra) is { } target2 && Distance(w, foe.Cell, target2.Cell) <= (r.Plan == "kill_shock_advance" ? 2 : 1))
                        w = Morale(w, old, target2, new() { MoraleChange = r.Plan == "kill_shock_advance" ? -2 : -1 }, skill.Id, bus);
                    if (r.Plan == "battle_confusion_escape" && Unit(w, foe.Id) is { } transient)
                    {
                        transient = transient with { OrderMorale = transient.OrderMorale.Where(m => m.Effect != skill.Id + ".battle").ToArray() };
                        w = Replace(w, RefreshOrderMorale(w, transient));
                    }
                    bool canFollow = r.Followup > 0 && Get(w, alive, "followup") != w.Round && (r.Plan == "forest_escape"
                        ? before.Cells[old.Cell].DefenseTerrain == "forest" && TacticalSkills.Mobile(before, foe) && Distance(before, old.Cell, foe.Cell) == 1
                        : killed && (r.Plan != "advance_two_stage" || Get(before, old, "attacks") == 0));
                    if (canFollow)
                    {
                        var pending = g with { Pending = true, Enemy = foe.Id, EnemyCell = foe.Cell, Cell = old.Cell, Uses = 0 };
                        w = Replace(w, Unit(w, old.Id)! with { Orders = Unit(w, old.Id)!.Orders.Where(t => !t.Pending).Append(pending).ToArray() });
                    }
                }
                if (!main && r.Mode == "guard" && TacticalSkills.Mobile(before, foe) && Distance(before, old.Cell, foe.Cell) == 1
                    && r.Plan == "counter_shock" && Unit(w, foe.Id) is { } attacker
                    && Get(w, old, "guard.target." + skill.Id + "." + foe.Id) != Epoch(w, old.Owner) + 1)
                {
                    w = Morale(w, old, attacker, new() { MoraleChange = -1 }, skill.Id, bus);
                    w = Set(w, old, "guard.target." + skill.Id + "." + foe.Id, Epoch(w, old.Owner) + 1);
                }
            }
        }
        // A surviving defender's completed, legal counterattack can trigger
        // nearby recovery sources. It never recovers a dead unit.
        if (counterattack && Unit(w, d.Id) is { } defender)
            foreach (var source in before.Units.Where(t => t.Owner == d.Owner))
                foreach (var (skill, slot, r, branch) in BoundRules(source, bus).Where(p => p.Rule.Plan == "counter_heal"))
                    if (Matches(before, source, d, r.Recipients) && Distance(before, source.Cell, d.Cell) <= r.Recipients.Radius)
                        w = ApplyBonus(w, source, Unit(w, defender.Id)!, skill, slot, r, null, bus, branch);
        return w;
    }

    public static IReadOnlyDictionary<int, ReachableCell> FilterMovement(WorldState w, UnitState u,
        IReadOnlyDictionary<int, ReachableCell> range, RuleEventBus bus)
    {
        if(MoveLocked(w,u))return new Dictionary<int,ReachableCell>();
        var direction = u.Orders.FirstOrDefault(g => Live(w, u, g, bus) && GrantRule(bus.Skills[g.Skill], g).Plan == "city_direction");
        if (direction == null || direction.EnemyCell < 0) return range;
        var normal = WorldActions.MovementContext(w, u, new());
        var ordinary = Movement.Reachable(new(w.Width, w.Height), normal.Cells, u.Cell, normal.Actor);
        return range.Keys.Select(c => Distance(w, c, direction.EnemyCell) < Distance(w, u.Cell, direction.EnemyCell)
            ? range[c] : ordinary.TryGetValue(c, out var result) ? result : null).Where(r => r != null).ToDictionary(r => r!.Cell, r => r!);
    }

    public static WorldState AfterMovement(WorldState before, WorldState w, UnitState old, IReadOnlyList<int> path, RuleEventBus bus)
    {
        var map = new HexMap(w.Width, w.Height);
        if (BoundRules(old, bus).Any(p => p.Rule.Plan == "first_river"))
            for (int i = 1; i < path.Count; i++)
            {
                int d = Enumerable.Range(0, 6).First(n => map.Neighbor(path[i - 1], n) == path[i]);
                var from = before.Cells[path[i - 1]].Terrain; var to = before.Cells[path[i]].Terrain; int edge = (d + 3) % 6;
                if (!from.Sea && !to.Sea && (to.RiverEdges & (1 << edge)) != 0 && (to.BridgeEdges & (1 << edge)) == 0)
                { w = Set(w, old, "river_used", Epoch(w, old.Owner) + 1); break; }
            }
        if (path.Any(c => before.Cells[c].Terrain.Sea) && !w.Cells[Unit(w, old.Id)!.Cell].Terrain.Sea && old.Transport)
            w = Set(w, old, "landed_round", w.Round);
        return OperationMovement(before,w,old);
    }
}
