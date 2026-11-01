using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public static class CampaignTechnologyStars
{
    private const string MedalItem = "ew4.item.53";
    // Accounts written before technology restoration have earned, but never spent, these stars.
    private static int EarnedBalance(PlayerAccount account) => account.TechnologyStars
        ?? (int)Math.Min(999L, account.CampaignStars.Values.Sum(s => (long)s));

    public static long Balance(PlayerAccount account)
    {
        int earned = EarnedBalance(account);
        return earned + Math.Min(long.MaxValue - earned, account.Inventory.GetValueOrDefault(MedalItem));
    }

    public static int Award(PlayerAccount account, int stars, int previousBest) =>
        Math.Min(999, EarnedBalance(account) + Math.Max(0, stars - previousBest));

    public static PlayerAccount Spend(PlayerAccount account, int stars)
    {
        if (stars < 0 || Balance(account) < stars) throw new RuleViolation("technology.stars", "金星奖章不足");
        int earned = EarnedBalance(account), fromEarned = Math.Min(earned, stars);
        var inventory = account.Inventory.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        int fromInventory = stars - fromEarned;
        if (fromInventory > 0)
        {
            long remaining = inventory[MedalItem] - fromInventory;
            if (remaining == 0) inventory.Remove(MedalItem); else inventory[MedalItem] = remaining;
        }
        return account with { TechnologyStars = earned - fromEarned, Inventory = inventory };
    }
}
