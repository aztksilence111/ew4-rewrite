using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Ew4.Core;

public sealed record DemandRules
{
    public int LossWeight { get; init; } = 75;
    public int OccupationWeight { get; init; } = 25;
    public int MaximumChance { get; init; } = 95;
    public int CoreWeight { get; init; } = 25;
    public int CapitalLootPercent { get; init; } = 20;
    public int CoreLootBonus { get; init; } = 20;
    public IReadOnlyList<FactionPairModifier> PairModifiers { get; init; } = Array.Empty<FactionPairModifier>();
    public IReadOnlyList<string> LockedFactions { get; init; } = Array.Empty<string>();
    public void Validate()
    {
        if (LossWeight < 0 || LossWeight > 100 || OccupationWeight < 0 || OccupationWeight > 100
            || MaximumChance < 0 || MaximumChance > 100 || CoreWeight < 0 || CoreWeight > 100
            || CapitalLootPercent < 0 || CapitalLootPercent > 100 || CoreLootBonus < 0 || CoreLootBonus > 100
            || PairModifiers == null || PairModifiers.Any(p => p == null || string.IsNullOrWhiteSpace(p.Actor)
                || string.IsNullOrWhiteSpace(p.Target) || p.Actor == p.Target || p.SurrenderBonus < -100 || p.SurrenderBonus > 100 || p.LootBonus < -100 || p.LootBonus > 100)
            || PairModifiers.Select(p => (p.Actor, p.Target)).Distinct().Count() != PairModifiers.Count || LockedFactions == null
            || LockedFactions.Any(string.IsNullOrWhiteSpace) || LockedFactions.Distinct().Count() != LockedFactions.Count)
            throw new RuleViolation("diplomacy.rules", "Invalid diplomacy rules");
    }
}

public sealed record FactionPairModifier(string Actor, string Target, int SurrenderBonus = 0, int LootBonus = 0);
public sealed record FactionPairAdjustment(string Actor, string Target, int? SurrenderBonus = null, int? LootBonus = null);

public sealed record HomelandBuilding(int Cell, string Faction);
public sealed record DemandProgress(string Actor, string Target, int[] Captured, int[] Consumed, int Attempts = 0);
public sealed record DemandState
{
    public bool Ready { get; init; }
    public bool Legacy { get; init; }
    public ulong Seed { get; init; }
    public DemandRules Rules { get; init; } = new();
    public IReadOnlyList<HomelandBuilding> Buildings { get; init; } = Array.Empty<HomelandBuilding>();
    public IReadOnlyList<DemandProgress> Progress { get; init; } = Array.Empty<DemandProgress>();
    public DemandState Copy() => this with {
        Rules = Rules with { LockedFactions = Rules.LockedFactions.ToArray(), PairModifiers = Rules.PairModifiers.ToArray() }, Buildings = Buildings.ToArray(),
        Progress = Progress.Select(p => p with { Captured = p.Captured.ToArray(), Consumed = p.Consumed.ToArray() }).ToArray()
    };
}

public sealed record DiplomacyQuote(int Initial, int Remaining, int Occupied, int Chance, string? WarBlocked, string? DemandBlocked,
    int SharedCores = 0, int CoreBonus = 0, int PairBonus = 0, int LootPercent = 20);

public static class DemandDiplomacy
{
    public static bool Counts(CellState cell) => cell.Terrain.Active && cell.BuildingKind is "city" or "industry" or "stable" or "port";

    public static WorldState Initialize(WorldState world)
    {
        if (world.Demands is not { Ready: false, Legacy: false } pending || world.PendingDialogue != null
            || StoryPlayback.Current(world) != null || world.Phase != TurnPhase.Active) return world;
        world = CapitalTreasury.Initialize(world);
        return world with { Demands = pending with { Ready = true,
            Buildings = world.Factions.SelectMany(f => (f.HomelandBuildings ?? world.Cells.Select((c, i) => (c, i))
                .Where(p => p.c.Owner == f.Id && Counts(p.c)).Select(p => p.i).ToArray())
                .Select(i => new HomelandBuilding(i, f.Id))).ToArray() } };
    }

    public static int[] CoreCells(WorldState world, string faction) => world.Demands is { Ready: true } state
        ? state.Buildings.Where(b => b.Faction == faction).Select(b => b.Cell).ToArray()
        : world.Factions.Single(f => f.Id == faction).HomelandBuildings?.ToArray()
            ?? world.Cells.Select((c, i) => (c, i)).Where(p => p.c.Owner == faction && Counts(p.c)).Select(p => p.i).ToArray();

    public static WorldState ChangeCores(WorldState world, string faction, IReadOnlyList<int> cells, string operation)
    {
        if (cells.Distinct().Count() != cells.Count || cells.Any(i => i < 0 || i >= world.Cells.Count || !Counts(world.Cells[i])))
            throw new RuleViolation("scenario.homeland", "Core regions require valid distinct building cells");
        var old = CoreCells(world, faction);
        return SetHomeland(world, faction, (operation == "core_add" ? old.Union(cells) : old.Except(cells)).ToArray());
    }

    public static WorldState SetPair(WorldState world, string actor, string target, int value, bool loot)
    {
        if (actor == target || !world.Factions.Any(f => f.Id == actor) || !world.Factions.Any(f => f.Id == target)
            || value < -100 || value > 100) throw new RuleViolation("diplomacy.pair", "Invalid directional diplomacy modifier");
        var prior = world.DiplomacyAdjustments?.SingleOrDefault(p => p.Actor == actor && p.Target == target) ?? new(actor, target);
        var updated = loot ? prior with { LootBonus = value } : prior with { SurrenderBonus = value };
        return world with { DiplomacyAdjustments = (world.DiplomacyAdjustments ?? Array.Empty<FactionPairAdjustment>())
            .Where(p => p.Actor != actor || p.Target != target).Append(updated).ToArray() };
    }

    public static WorldState SetHomeland(WorldState world, string faction, IReadOnlyList<int> cells)
    {
        if (!world.Factions.Any(f => f.Id == faction) || cells.Count > world.Cells.Count || cells.Distinct().Count() != cells.Count
            || cells.Any(i => i < 0 || i >= world.Cells.Count || !Counts(world.Cells[i])))
            throw new RuleViolation("scenario.homeland", "Homeland requires distinct city, industry, stable or port cells");
        var next = world with { Factions = world.Factions.Select(f => f.Id == faction
            ? f with { HomelandBuildings = cells.OrderBy(i => i).ToArray() } : f).ToArray() };
        if (world.Demands is { Ready: true } state)
            next = next with { Demands = state with { Buildings = state.Buildings.Where(b => b.Faction != faction)
                .Concat(cells.OrderBy(i => i).Select(i => new HomelandBuilding(i, faction))).ToArray() } };
        return next;
    }

    public static void Validate(WorldState world)
    {
        if (world.DiplomacyAdjustments is { } adjustments && (adjustments.Any(p => p == null || p.Actor == p.Target
            || !world.Factions.Any(f => f.Id == p.Actor) || !world.Factions.Any(f => f.Id == p.Target)
            || p.SurrenderBonus < -100 || p.SurrenderBonus > 100 || p.LootBonus < -100 || p.LootBonus > 100)
            || adjustments.Select(p => (p.Actor, p.Target)).Distinct().Count() != adjustments.Count))
            throw new RuleViolation("diplomacy.pair", "Invalid event diplomacy modifiers");
        if (world.Demands is not { } state) return;
        state.Rules.Validate();
        var factions = world.Factions.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        if (state.Buildings == null || state.Progress == null || state.Buildings.Count > (long)world.Cells.Count * factions.Count
            || state.Buildings.Any(b => b == null || b.Cell < 0 || b.Cell >= world.Cells.Count || !factions.Contains(b.Faction))
            || state.Buildings.Select(b => (b.Cell, b.Faction)).Distinct().Count() != state.Buildings.Count
            || state.Legacy && state.Ready || !state.Ready && (state.Buildings.Count > 0 || state.Progress.Count > 0)
            || state.Progress.Count > (long)factions.Count * factions.Count)
            throw new RuleViolation("diplomacy.state", "Invalid homeland records");
        var pairs = new HashSet<(string, string)>();
        foreach (var p in state.Progress)
        {
            if (p == null || p.Actor == p.Target || !factions.Contains(p.Actor) || !factions.Contains(p.Target)
                || !pairs.Add((p.Actor, p.Target)) || p.Attempts < 0 || p.Captured == null || p.Consumed == null
                || p.Captured.Length > world.Cells.Count || p.Captured.Distinct().Count() != p.Captured.Length
                || p.Captured.Any(c => c < 0 || c >= world.Cells.Count)
                || p.Consumed.Distinct().Count() != p.Consumed.Length || p.Consumed.Except(p.Captured).Any())
                throw new RuleViolation("diplomacy.state", "Invalid surrender attempt records");
        }
    }

    // Called at the military ownership change, before scripts or automatic surrender transfer land.
    public static WorldState Captured(WorldState before, WorldState after, string actor, IReadOnlyList<int> changed)
    {
        if (after.Demands is not { Ready: true } state || !after.Factions.Any(f => f.Id == actor && f.Human)) return after;
        var sites = changed.ToHashSet();
        var gains = state.Buildings.Where(b => sites.Contains(b.Cell) && b.Faction != actor
            && before.Cells[b.Cell].Owner == b.Faction && after.Cells[b.Cell].Owner == actor).GroupBy(b => b.Faction).ToArray();
        if (gains.Length == 0) return after;
        var progress = state.Progress.ToList();
        foreach (var group in gains)
        {
            int index = progress.FindIndex(p => p.Actor == actor && p.Target == group.Key);
            var prior = index < 0 ? new DemandProgress(actor, group.Key, Array.Empty<int>(), Array.Empty<int>()) : progress[index];
            var next = prior with { Captured = prior.Captured.Concat(group.Select(b => b.Cell)).Distinct().OrderBy(c => c).ToArray() };
            if (index < 0) progress.Add(next); else progress[index] = next;
        }
        return after with { Demands = state with { Progress = progress.ToArray() } };
    }

    private static string? CommonBlock(WorldState world, string actor, string target)
    {
        var own = world.Factions.SingleOrDefault(f => f.Id == actor);
        var other = world.Factions.SingleOrDefault(f => f.Id == target);
        if (own == null || other == null || actor == target) return "target";
        if (own.Defeated || other.Defeated) return "defeated";
        if (world.Outcome != BattleOutcome.Continue || world.Phase != TurnPhase.Active || world.PendingDialogue != null
            || StoryPlayback.Current(world) != null || !own.Human || world.ActiveFaction != actor) return "turn";
        if (PlayerAi.Pending(world)) return "ai";
        if (world.Demands?.Rules.LockedFactions.Contains(target) == true) return "protected";
        return null;
    }

    public static DiplomacyQuote Quote(WorldState world, string actor, string target)
    {
        var state = world.Demands;
        var homes = state?.Buildings.Where(b => b.Faction == target).ToArray() ?? Array.Empty<HomelandBuilding>();
        int n = homes.Length, r = homes.Count(b => world.Cells[b.Cell].Owner == target), k = homes.Count(b => world.Cells[b.Cell].Owner == actor);
        var rules = state?.Rules ?? new DemandRules();
        var ownCores = state?.Buildings.Where(b => b.Faction == actor).Select(b => b.Cell).ToHashSet() ?? new HashSet<int>();
        int shared = homes.Count(b => ownCores.Contains(b.Cell));
        int coreBonus = n == 0 ? 0 : (int)Math.Round((double)rules.CoreWeight * shared / n, MidpointRounding.AwayFromZero);
        int pairBonus = world.DiplomacyAdjustments?.SingleOrDefault(p => p.Actor == actor && p.Target == target)?.SurrenderBonus
            ?? rules.PairModifiers.SingleOrDefault(p => p.Actor == actor && p.Target == target)?.SurrenderBonus ?? 0;
        int chance = n == 0 ? 0 : Math.Max(0, Math.Min(rules.MaximumChance,
            (int)Math.Round(((double)rules.LossWeight * (n - r) + (double)rules.OccupationWeight * k + (double)rules.CoreWeight * shared) / n
                + pairBonus, MidpointRounding.AwayFromZero)));
        string? common = CommonBlock(world, actor, target);
        bool enemy = common == "target" ? false : WorldDiplomacy.Relation(world, actor, target) == "hostile";
        string? demand = common;
        if (demand == null)
        {
            var progress = state?.Progress.SingleOrDefault(p => p.Actor == actor && p.Target == target);
            demand = !enemy ? "peace" : world.Factions.Single(f => f.Id == target).Human ? "human"
                : state is not { Ready: true } ? "legacy" : n == 0 ? "no_buildings" : k == 0 ? "occupation"
                : progress is { Attempts: > 0 } && !progress.Captured.Except(progress.Consumed)
                    .Any(c => homes.Any(b => b.Cell == c) && world.Cells[c].Owner == actor) ? "progress" : null;
        }
        return new(n, r, k, chance, common ?? (enemy ? "war" : null), demand, shared, coreBonus, pairBonus,
            CapitalTreasury.Percent(world, actor, target));
    }

    public static WorldChange DeclareWar(WorldState world, long revision, string actor, string target)
    {
        Ready(world, revision, actor, target, false);
        var next = WorldDiplomacy.Set(world, actor, target, "hostile") with { Revision = checked(world.Revision + 1) };
        next.Validate();
        return new(next, new[] { new WorldNotice("diplomacy.war", target) });
    }

    public static WorldChange Demand(WorldState world, long revision, string actor, string target)
    {
        var quote = Ready(world, revision, actor, target, true);
        var state = world.Demands!;
        var prior = state.Progress.SingleOrDefault(p => p.Actor == actor && p.Target == target)
            ?? new DemandProgress(actor, target, Array.Empty<int>(), Array.Empty<int>());
        var progress = prior with { Attempts = checked(prior.Attempts + 1), Consumed = prior.Captured.ToArray() };
        // A separate seed makes an attempt stable even when unrelated combat consumes RNG after loading.
        bool accepted = Roll(state.Seed, actor, target, progress.Attempts) < quote.Chance;
        var next = world with { Demands = state with { Progress = state.Progress.Where(p => p.Actor != actor || p.Target != target).Append(progress).ToArray() } };
        var notices = new List<WorldNotice>();
        if (accepted)
        {
            var surrender = WorldEvents.Surrender(next, target, actor);
            next = surrender.State; notices.AddRange(surrender.Notices);
        }
        next = next with { Revision = checked(world.Revision + 1) };
        notices.Add(new(accepted ? "diplomacy.accepted" : "diplomacy.refused", target, quote.Chance));
        next.Validate();
        return new(next, notices);
    }

    private static DiplomacyQuote Ready(WorldState world, long revision, string actor, string target, bool demand)
    {
        world.Validate();
        if (world.Revision != revision) throw new RuleViolation("world.stale", "Diplomacy state changed");
        var quote = Quote(world, actor, target);
        if ((demand ? quote.DemandBlocked : quote.WarBlocked) is { } reason)
            throw new RuleViolation("diplomacy." + reason, reason);
        return quote;
    }

    private static int Roll(ulong seed, string actor, string target, int attempt)
    {
        using var hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ":" + actor.Length + ":" + actor + ":" + target.Length + ":" + target + ":" + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        uint value = (uint)bytes[0] | (uint)bytes[1] << 8 | (uint)bytes[2] << 16 | (uint)bytes[3] << 24;
        return (int)(value % 100);
    }
}
