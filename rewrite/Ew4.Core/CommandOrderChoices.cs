using System;
using System.Linq;
using System.Collections.Generic;

namespace Ew4.Core;

public static partial class CommandOrders
{
    private static WorldState OfferChoice(WorldState w, UnitState source, ExtensionSkill skill, SkillSlot slot, string? target = null)
    {
        if (!ReactionAvailable(w, source, skill) || HasPending(w, source, skill)) return w;
        var u = Unit(w, source.Id)!;
        var g = new OrderGrant(skill.Id, slot.InstanceId, source.Id, source.Owner, source.CommanderId, w.Round, source.Cell,
            RecipientCommander: source.CommanderId, Epoch: Epoch(w, source.Owner), Pending: true, Auxiliary: target, Stage: -1);
        return Replace(w, u with { Orders = u.Orders.Append(g).ToArray() });
    }
    private static string[] PendingCandidates(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus? bus = null)
    {
        var g = source.Orders.First(t => t.Pending && t.Skill == skill.Id && t.Epoch == Epoch(w, source.Owner));
        var r = skill.Order!;
        if(r.Plan is "op_relocate" or "op_cavalry" or "op_retreat")return (Unit(w,g.Auxiliary) is {} actor
            ? OperationRange(w,actor,r,bus).Keys.Select(c=>"cell."+c):Enumerable.Empty<string>()).Append("stay").ToArray();
        if (g.Stage >= 0) return FollowupCells(w, source, skill).Select(c => "cell." + c).ToArray();
        if (r.Plan is "choose_swap_on_defend" or "choose_swap_on_shock")
        {
            var target = Unit(w, g.Auxiliary);
            return target == null || Distance(w, source.Cell, target.Cell) > r.Recipients.Radius ? Array.Empty<string>()
                : w.Units.Where(u => u.Id != target.Id && u.Owner == source.Owner && Category(w, u) == "infantry"
                    && Distance(w, u.Cell, target.Cell) == 1 && LegalDestination(w, u, target.Cell, u.Id, target.Id)
                    && LegalDestination(w, target, u.Cell, u.Id, target.Id)).Select(u => u.Id).ToArray();
        }
        return w.Units.Where(u => Distance(w, source.Cell, u.Cell) <= r.Recipients.Radius && Matches(w, source, u, r.Recipients)
            && (g.Auxiliary == null || u.Id == g.Auxiliary)
            && (r.Plan != "choose_reactivate_intel_kill" || CanReactivate(w, u))).Select(u => u.Id).ToArray();
    }
    private static WorldState CompleteChoice(WorldState w, UnitState source, ExtensionSkill skill, IReadOnlyList<string>? selected, RuleEventBus bus)
    {
        if (selected == null || selected.Count != 1 || !PendingCandidates(w, source, skill).Contains(selected[0]))
            throw new RuleViolation("orders.choice", "Choose one eligible recipient");
        var r = skill.Order!; var target = Unit(w, selected[0])!;
        var pending = source.Orders.First(g => g.Pending && g.Skill == skill.Id);
        if (r.Plan is "choose_swap_on_defend" or "choose_swap_on_shock")
        {
            var other = Unit(w, pending.Auxiliary)!;
            w = Replace(Replace(w, Relocate(w, target, other.Cell)), Relocate(w, other, target.Cell));
        }
        if (r.Plan is "choose" or "choose_morale_city" or "choose_morale_chain")
        {
            var bonus = r.Plan == "choose" ? new OrderBonus { MoraleFloor = 0 }
                : new() { Morale = Math.Max(r.Plan == "choose_morale_city" ? -2 : -3, target.Morale.Effective - 1) };
            w = Morale(w, source, target, bonus, skill.Id + ".chain", bus);
        }
        if (r.Plan == "choose_reactivate_intel_kill")
        {
            w = Set(w, target, "reactivated", Epoch(w, target.Owner) + 1);
            w = Replace(w, target with { Acted = false, RemainingMovement = target.ExtensionMovementLimit ?? bus.MovementBudget(target.Combatant) });
        }
        var u = Unit(w, source.Id)!;
        var orders = u.Orders.Where(g => g != pending).ToArray();
        if (r.Plan is "choose_infantry_mark" or "choose_artillery_mark")
            orders = orders.Append(pending with { Pending = false, Enemy = target.Id, EnemyCell = target.Cell, Uses = 0, Stage = 0 }).ToArray();
        w = Replace(w, u with { Orders = orders });
        w = Set(w, source, "reaction." + skill.Id, Epoch(w, source.Owner) + Math.Max(1, r.Interval));
        return Set(w, source, "choice." + skill.Id, Epoch(w, source.Owner) + 1);
    }
    private static WorldState AfterSkillMorale(WorldState w, UnitState enemySource, UnitState target, string effect, RuleEventBus bus)
    {
        if (effect.EndsWith(".chain", StringComparison.Ordinal)) return w;
        foreach (var source in w.Units.Where(u => u.Id != target.Id).ToArray())
            foreach (var (skill, slot, r, _) in BoundRules(source, bus))
            {
                if (!Matches(w, source, source, r.Source) || !ReactionAvailable(w, source, skill)) continue;
                if (r.Plan == "choose_morale_chain" && source.Owner == enemySource.Owner && source.Id != enemySource.Id && target.Morale.Effective < 0
                    && Matches(w, source, target, r.Recipients) && Distance(w, source.Cell, target.Cell) <= r.Recipients.Radius)
                    w = OfferChoice(w, source, skill, slot, target.Id);
                if (r.Plan == "choose_swap_on_shock" && source.Owner == target.Owner && enemySource.Owner != target.Owner && !effect.Contains(".common.")
                    && Matches(w, source, target, r.Recipients) && Distance(w, source.Cell, target.Cell) <= r.Recipients.Radius)
                {
                    w = Set(w, source, "swap_next." + skill.Id, w.Round + 1);
                    w = Set(w, source, "swap_target." + skill.Id + "." + target.Id, w.Round + 1);
                }
            }
        return w;
    }
    private static WorldState AfterChoiceBattle(WorldState before, WorldState w, UnitState a, UnitState d, RuleEventBus bus)
    {
        foreach (var source in before.Units.ToArray())
            foreach (var (skill, slot, r, _) in BoundRules(source, bus))
            {
                if (Unit(w, source.Id) == null || !Matches(before, source, source, r.Source) || !ReactionAvailable(w, source, skill)) continue;
                if (r.Plan == "choose_swap_on_defend" && Unit(w, d.Id) is { } alive && source.Owner == d.Owner
                    && Matches(w, source, alive, r.Recipients) && Distance(w, source.Cell, alive.Cell) <= r.Recipients.Radius)
                {
                    w = Set(w, source, "swap_next." + skill.Id, w.Round + 1);
                    w = Set(w, source, "swap_target." + skill.Id + "." + d.Id, w.Round + 1);
                }
                if (r.Plan == "choose_reactivate_intel_kill" && source.Owner == a.Owner && Unit(w, a.Id) is { } survivor && Unit(w, d.Id) == null
                    && Matches(w, source, survivor, r.Recipients) && Distance(w, source.Cell, survivor.Cell) <= r.Recipients.Radius
                    && BoundRules(d, bus).Any(p => p.Rule.Intelligence) && CanReactivate(w, survivor))
                    w = OfferChoice(w, source, skill, slot, a.Id);
            }
        return w;
    }
}
