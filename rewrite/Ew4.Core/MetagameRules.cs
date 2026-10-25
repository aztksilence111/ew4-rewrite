using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record RefreshRecovery(long? IntervalSeconds = null, int? Victories = null, int Amount = 1);
public sealed record RefreshPolicy(int? Limit = null, int? Initial = null, RefreshRecovery? Recovery = null)
{
    public void Validate()
    {
        if (Limit < 0 || Initial < 0 || Initial > Limit || !Limit.HasValue && (Initial.HasValue || Recovery != null)
            || Recovery != null && (Recovery.Amount < 1 || Recovery.IntervalSeconds < 1 || Recovery.Victories < 1
                || Recovery.IntervalSeconds.HasValue == Recovery.Victories.HasValue))
            throw new RuleViolation("metagame.refresh", "Invalid refresh limit or recovery condition");
    }
}
public sealed record RefreshBalance(int Remaining, DateTimeOffset UpdatedAt, int Victories);
public sealed record MarketSlot(int Slot, string Pool = "consumable", string[]? Items = null, bool Refresh = true, long? Quantity = 1);
public sealed record MetagameRules
{
    public const int DefaultCommanderSectionCapacity = 20;
    public int Schema { get; init; } = 1;
    public long InitialMedals { get; init; } = 20_000_000;
    public long InitialBadges { get; init; } = 20_000_000;
    public IReadOnlyList<MarketSlot>? ShopSlots { get; init; }
    public IReadOnlyList<MetagameObjective> Objectives { get; init; } = Array.Empty<MetagameObjective>();
    public IReadOnlyDictionary<string, int> CommanderSectionCapacities { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, RefreshPolicy> Refresh { get; init; } = new Dictionary<string, RefreshPolicy>();
    public int CommanderSectionCapacity(int section) => CommanderSectionCapacities.GetValueOrDefault(
        section.ToString(System.Globalization.CultureInfo.InvariantCulture), DefaultCommanderSectionCapacity);
    public void Validate()
    {
        if (Schema != 1 || InitialMedals < 0 || InitialBadges < 0 || Refresh == null
            || CommanderSectionCapacities == null || CommanderSectionCapacities.Any(p => p.Key is not ("0" or "1" or "2") || p.Value < 0)
            || Refresh.Any(p => p.Key is not ("shop" or "academy.0" or "academy.1" or "academy.2") || p.Value == null)
            || ShopSlots != null && (ShopSlots.Any(s => s == null || s.Slot is < 0 or > 13
                || s.Pool is not ("consumable" or "equipment" or "any") || s.Quantity < 0
                || s.Items != null && (s.Items.Length == 0 || s.Items.Any(string.IsNullOrWhiteSpace)
                    || s.Items.Distinct(StringComparer.Ordinal).Count() != s.Items.Length))
                || ShopSlots.Select(s => s.Slot).Distinct().Count() != ShopSlots.Count))
            throw new RuleViolation("metagame.configuration", "Invalid metagame resources, refresh target or shop slots");
        foreach (var policy in Refresh.Values) policy.Validate();
        MetagameObjectives.Validate(Objectives);
    }
}

public static class RefreshUses
{
    public static int Victories(PlayerAccount account) => account.BattleResults.Values.Count(r => r.Outcome == "Victory");
    public static RefreshBalance Recover(RefreshBalance state, RefreshPolicy policy, DateTimeOffset now, int victories)
    {
        policy.Validate();
        int limit = policy.Limit ?? throw new RuleViolation("metagame.refresh", "Unlimited refresh has no balance");
        int remaining = Math.Min(state.Remaining, limit);
        // A full balance cannot bank recovery for a later refresh; a backwards clock grants nothing.
        if (remaining == limit) return new(remaining, now > state.UpdatedAt ? now : state.UpdatedAt, victories);
        if (policy.Recovery is not RefreshRecovery recovery) return state with { Remaining = remaining };
        long periods = recovery.IntervalSeconds is long seconds
            ? Math.Max(0, (now.UtcDateTime.Ticks - state.UpdatedAt.UtcDateTime.Ticks) / TimeSpan.TicksPerSecond) / seconds
            : Math.Max(0, victories - state.Victories) / recovery.Victories!.Value;
        int missing = limit - remaining;
        int earned = (int)Math.Min(missing, periods > missing / recovery.Amount ? missing : periods * recovery.Amount);
        if (remaining + earned == limit) return new(limit, now > state.UpdatedAt ? now : state.UpdatedAt, victories);
        return new(remaining + earned,
            recovery.IntervalSeconds is long interval && periods > 0
                ? state.UpdatedAt.AddTicks(checked(periods * interval * TimeSpan.TicksPerSecond)) : state.UpdatedAt,
            recovery.Victories is int every ? state.Victories + (int)periods * every : state.Victories);
    }
    public static PlayerAccount Consume(PlayerAccount account, string target, RefreshPolicy? policy, DateTimeOffset now)
    {
        if (policy?.Limit == null) return account;
        var state = Recover(account.RefreshBalances[target], policy, now, Victories(account));
        if (state.Remaining == 0) throw new RuleViolation("account.refresh", "刷新次数不足");
        var balances = account.RefreshBalances.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        balances[target] = state with { Remaining = state.Remaining - 1 };
        return account with { RefreshBalances = balances };
    }
}
