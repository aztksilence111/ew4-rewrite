namespace Ew4.Core;

public static class CampaignTechnologyStars
{
    // Accounts written before technology restoration have earned, but never spent, these stars.
    public static int Balance(PlayerAccount account) => account.TechnologyStars
        ?? (int)System.Math.Min(999L, System.Linq.Enumerable.Sum(account.CampaignStars.Values, s => (long)s));
    public static int Award(PlayerAccount account, int stars, int previousBest) =>
        System.Math.Min(999, Balance(account) + System.Math.Max(0, stars - previousBest));
}
