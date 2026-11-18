using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record OrderReactionChoice(string Source, string Skill, string Target, string Effect, bool? Accept = null);
public sealed record OrderActionRequest(string Kind, string Actor, string? Target = null, string? Instance = null,
    string[]? Recipients = null, string? Secondary = null, IReadOnlyList<OrderReactionChoice>? Choices = null, int? TargetCell = null);

public static partial class CommandOrders
{
    public static bool NeedsReactionProbe(RuleEventBus bus, WorldState? world = null) => world == null
        ? bus.Skills.Values.Any(s => s.Order?.Plan is "choose_confusion_protection" or "choose_intel_protection" or "choose_low_morale_cover")
        : world.Units.Any(u => BoundRules(u, bus).Any(p => p.Rule.Plan is "choose_confusion_protection" or "choose_intel_protection" or "choose_low_morale_cover"));
    private static bool ReactionAvailable(WorldState w, UnitState source, ExtensionSkill skill) =>
        Get(w, source, "reaction." + skill.Id) <= Epoch(w, source.Owner);

    private static (WorldState World, bool Accept) Reaction(WorldState w, UnitState source, ExtensionSkill skill, string target, string effect)
    {
        if (!ReactionAvailable(w, source, skill)) return (w, false);
        if (!w.Factions.Single(f => f.Id == source.Owner).Human)
            return (Set(w, source, "reaction." + skill.Id, Epoch(w, source.Owner) + Math.Max(1, skill.Order!.Interval)), true);
        var choice = new OrderReactionChoice(source.Id, skill.Id, target, effect);
        if (w.OrderProbe)
        {
            if (!w.OrderProposals.Any(p => p.Source == source.Id && p.Skill == skill.Id && p.Target == target && p.Effect == effect))
                w = w with { OrderProposals = w.OrderProposals.Append(choice).ToArray() };
            return (w, false);
        }
        bool accepted = w.OrderChoices.Any(p => p.Source == source.Id && p.Skill == skill.Id && p.Target == target && p.Effect == effect && p.Accept == true);
        if (accepted) w = Set(w, source, "reaction." + skill.Id, Epoch(w, source.Owner) + Math.Max(1, skill.Order!.Interval));
        return (w, accepted);
    }

    public static WorldState QueueProbedAction(WorldState original, WorldState probe, OrderActionRequest request) =>
        probe.OrderProposals.Count == 0 ? original : original with { Revision = checked(original.Revision + 1),
            PendingOrderAction = request with { Choices = probe.OrderProposals.ToArray() } };

    public static WorldChange Respond(WorldState w, long revision, bool accept, RuleEventBus bus, WorldMoveRules movement, WorldBattleRules battle)
    {
        if (revision != w.Revision || w.PendingOrderAction is not { } action || action.Choices == null)
            throw new RuleViolation("orders.reaction", "No current reaction is waiting");
        int index = action.Choices.ToList().FindIndex(c => c.Accept == null);
        if (index < 0) throw new RuleViolation("orders.reaction", "Reaction already resolved");
        var choices = action.Choices.Select((c, i) => i == index ? c with { Accept = accept } : c).ToArray();
        if (choices.Any(c => c.Accept == null)) return new(w with { PendingOrderAction = action with { Choices = choices }, Revision = checked(w.Revision + 1) }, Array.Empty<WorldNotice>());
        var ready = w with { PendingOrderAction = null, OrderChoices = choices };
        var changed = action.Kind == "attack" ? WorldBattle.Attack(ready, ready.Revision, action.Actor, action.Target!, battle)
            : WorldSkillActions.Execute(ready, ready.Revision, action.Actor, action.Instance!, action.Target, bus, movement,
                targetCell: action.TargetCell, targets: action.Recipients, secondaryTarget: action.Secondary,battle:battle);
        return changed with { State = changed.State with { OrderChoices = Array.Empty<OrderReactionChoice>() } };
    }

    private static (WorldState World, OrderBonus Bonus) ProtectSkillMorale(WorldState w, UnitState enemy, UnitState target, OrderBonus bonus, string effect, RuleEventBus bus)
    {
        if (enemy.Owner == target.Owner || !bonus.Morale.HasValue && bonus.MoraleChange >= 0 || effect.EndsWith(".chain", StringComparison.Ordinal)) return (w, bonus);
        bool original = !effect.Contains(".common.", StringComparison.Ordinal);
        bool intelligence = BoundRules(enemy, bus).Any(p => p.Rule.Intelligence);
        foreach (var source in w.Units.Where(u => u.Owner == target.Owner && u.Id != target.Id).OrderBy(u => u.Id, StringComparer.Ordinal).ToArray())
            foreach (var (skill, _, r, _) in BoundRules(source, bus))
            {
                if (r.Plan is not ("choose_confusion_protection" or "choose_intel_protection") || !Matches(w, source, source, r.Source)
                    || !Matches(w, source, target, r.Recipients) || Distance(w, source.Cell, target.Cell) > r.Recipients.Radius
                    || r.Plan == "choose_confusion_protection" && (!original || bonus.Morale != -3)
                    || r.Plan == "choose_intel_protection" && !intelligence) continue;
                var reaction = Reaction(w, source, skill, target.Id, effect); w = reaction.World;
                if (reaction.Accept) return (w, r.Plan == "choose_confusion_protection" ? bonus with { Morale = -2 } : new());
            }
        return (w, bonus);
    }

    private static (WorldState World, bool Suppress) AttackReaction(WorldState w, UnitState attacker, UnitState defender, RuleEventBus bus)
    {
        bool suppress = false;
        if (defender.Morale.Effective >= 0 || !TacticalSkills.Mobile(w, defender) || Distance(w, attacker.Cell, defender.Cell) != 1) return (w, false);
        foreach (var source in w.Units.Where(u => u.Owner == attacker.Owner && u.Id != attacker.Id).ToArray())
            foreach (var (skill, _, r, _) in BoundRules(source, bus).Where(p => p.Rule.Plan == "choose_low_morale_cover"))
                if (Matches(w, source, source, r.Source) && Matches(w, source, attacker, r.Recipients) && Distance(w, source.Cell, attacker.Cell) <= r.Recipients.Radius)
                {
                    var reaction = Reaction(w, source, skill, attacker.Id, defender.Id); w = reaction.World; suppress |= reaction.Accept;
                }
        return (w, suppress);
    }
}
