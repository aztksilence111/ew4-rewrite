using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record FactionCapital(int Cell, bool TreasuryCaptured = false);

public static class CapitalTreasury
{
    public static WorldState Initialize(WorldState world) => world with { Factions = world.Factions.Select(f => {
        if (f.Capital != null) return f;
        var candidates = world.Cells.Select((c, i) => (c, i)).Where(p => p.c.Owner == f.Id && DemandDiplomacy.Counts(p.c))
            .OrderByDescending(p => p.c.BuildingKind == "city").ThenByDescending(p => p.c.BuildingLevel).ThenBy(p => p.i).ToArray();
        return candidates.Length == 0 ? f : f with { Capital = new(candidates[0].i) };
    }).ToArray() };

    public static WorldState SetCapital(WorldState world, string faction, int cell)
    {
        if (cell < 0 || cell >= world.Cells.Count || !DemandDiplomacy.Counts(world.Cells[cell]))
            throw new RuleViolation("scenario.capital", "Capital requires a city, industry, stable or port");
        return world with { Factions = world.Factions.Select(f => f.Id == faction
            ? f with { Capital = new(cell, f.Capital?.TreasuryCaptured ?? false) } : f).ToArray() };
    }

    public static int Percent(WorldState world, string actor, string target)
    {
        var rules = world.Demands?.Rules ?? new DemandRules();
        var targetCores = world.Demands?.Buildings.Where(b => b.Faction == target).Select(b => b.Cell).ToArray() ?? Array.Empty<int>();
        var actorCores = world.Demands?.Buildings.Where(b => b.Faction == actor).Select(b => b.Cell).ToHashSet() ?? new HashSet<int>();
        double bonus = targetCores.Length == 0 ? 0 : (double)rules.CoreLootBonus * targetCores.Count(actorCores.Contains) / targetCores.Length;
        int pair = world.DiplomacyAdjustments?.SingleOrDefault(p => p.Actor == actor && p.Target == target)?.LootBonus
            ?? rules.PairModifiers.SingleOrDefault(p => p.Actor == actor && p.Target == target)?.LootBonus ?? 0;
        return Math.Max(0, Math.Min(100, (int)Math.Round(rules.CapitalLootPercent + bonus + pair, MidpointRounding.AwayFromZero)));
    }

    // This runs at military ownership change, before automatic defeat or scripted land transfers.
    public static WorldChange Captured(WorldState before, WorldState after, string actor, IReadOnlyList<int> changed)
    {
        if (after.Demands is not { Ready: true }) return new(after, Array.Empty<WorldNotice>());
        var sites = changed.ToHashSet();
        var targets = before.Factions.Where(f => f.Id != actor && !f.Defeated && f.Capital is { TreasuryCaptured: false } capital
            && sites.Contains(capital.Cell) && before.Cells[capital.Cell].Owner == f.Id && after.Cells[capital.Cell].Owner == actor).ToArray();
        var next = after;
        var notices = new List<WorldNotice>();
        foreach (var victim in targets)
        {
            int percent = Percent(before, actor, victim.Id);
            var defender = next.Factions.Single(f => f.Id == victim.Id);
            long Portion(long amount) => amount <= 0 ? 0 : amount / 100 * percent + amount % 100 * percent / 100;
            var loot = new Resources(Portion(defender.Resources.Money), Portion(defender.Resources.Industry), Portion(defender.Resources.Food));
            var winner = next.Factions.Single(f => f.Id == actor);
            Resources gained;
            try { gained = new(checked(winner.Resources.Money + loot.Money), checked(winner.Resources.Industry + loot.Industry), checked(winner.Resources.Food + loot.Food)); }
            catch (OverflowException) { throw new RuleViolation("capital.resources", "Captured treasury exceeds resource range"); }
            next = next with { Factions = next.Factions.Select(f => f.Id == victim.Id ? f with {
                Capital = f.Capital! with { TreasuryCaptured = true },
                Resources = new(f.Resources.Money - loot.Money, f.Resources.Industry - loot.Industry, f.Resources.Food - loot.Food)
            } : f.Id == actor ? f with { Resources = gained } : f).ToArray() };
            notices.Add(new("capital.captured", victim.Id, percent) { Target = actor });
            notices.Add(new("capital.loot.money", actor, loot.Money) { Target = victim.Id });
            notices.Add(new("capital.loot.industry", actor, loot.Industry) { Target = victim.Id });
            notices.Add(new("capital.loot.food", actor, loot.Food) { Target = victim.Id });
        }
        return new(next, notices);
    }
}
