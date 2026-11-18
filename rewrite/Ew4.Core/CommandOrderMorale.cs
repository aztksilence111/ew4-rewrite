using System;
using System.Linq;

namespace Ew4.Core;

public static partial class CommandOrders
{
    public static int LocalPenalty(WorldState w, UnitState u, int penalty)
    {
        if (u.Orders.Any(g => !g.MarkerOnly && g.Plan == "survive" && g.Uses == 0 && Epoch(w, u.Owner) < g.Epoch + g.Lifetime
            && Unit(w, g.Source) is { } s && s.Owner == u.Owner && s.CommanderId == g.Commander)) return 0;
        return penalty == 1 && Get(w, u, "local_shield") == w.Revision + 1 ? 0 : penalty;
    }

    public static WorldState BattleMorale(WorldState w, string target, int value, bool additive, RuleEventBus bus)
    {
        if (Unit(w, target) is not { } u) return w;
        int next = additive ? Math.Clamp(u.Morale.Baseline + value, -3, 1) : value;
        bool lower = next < u.Morale.Baseline;
        if (lower)
        {
            var redirect = w.Units.FirstOrDefault(t => t.Owner == u.Owner && t.Cell + 1 == Get(w, u, "redirect") && t.Id != u.Id);
            if (redirect != null)
            {
                w = Set(w, u, "redirect", 0);
                return BattleMorale(w, redirect.Id, value, additive, bus);
            }
            foreach (var g in u.Orders.Where(g => !g.MarkerOnly && Live(w, u, g, bus) && !g.Pending).ToArray())
            {
                var r = GrantRule(bus.Skills[g.Skill], g);
                if (r.Plan is not ("morale_guard" or "mutual_guard" or "rear_guard")
                    || Get(w, u, "morale_guard." + g.Skill) == Epoch(w, u.Owner) + 1) continue;
                if (r.Mode == "swap")
                {
                    var partner = g.Source == u.Id ? (g.Participants ?? Array.Empty<string>()).Select(id => Unit(w, id)).FirstOrDefault(t => t != null && t.Id != u.Id)
                        : Unit(w, g.Source);
                    if (partner == null || Distance(w, partner.Cell, u.Cell) > 1) continue;
                }
                return Set(w, u, "morale_guard." + g.Skill, Epoch(w, u.Owner) + 1);
            }
        }
        bool leader = u.Combatant.Has(Ew4Combat.Skill(3));
        var morale = additive ? u.Morale.Add(value, leader) : MoraleState.Set(value, leader);
        return Replace(w, RefreshOrderMorale(w, u with { Morale = morale }));
    }
}
