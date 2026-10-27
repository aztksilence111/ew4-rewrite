using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record BattleResult(string Id, string Scenario, string Outcome, int Round, int Stars,
    int MinimumRatingRound, int HighestRatingRound, long AwardMedals, long CollectedMedals);

public static class BattleSettlement
{
    private static readonly int[] CampaignAwards = [0, 25, 50, 75, 100, 150];
    private static readonly int[] ConquestThresholds = [91, 87, 83, 79, 75, 71, 67, 63, 59, 55, 51, 47, 43, 39, 35, 31, 27, 23, 16, 1];

    // Original ARM64 7eeb8: upper boundary is inclusive, lower boundary exclusive.
    public static int Stars(int round, int minimumRatingRound, int highestRatingRound)
    {
        if (round < 1 || minimumRatingRound < 1 || highestRatingRound < 0)
            throw new RuleViolation("settlement.rounds", "Invalid settlement round thresholds");
        if (round <= highestRatingRound) return 5;
        return round >= minimumRatingRound ? 1
            : Math.Max(2, (int)((long)(minimumRatingRound - round) * 4 / (minimumRatingRound - highestRatingRound)) + 1);
    }

    public static long CampaignAward(int stars, int previousStars) => Math.Max(0, CampaignAwards[stars] - CampaignAwards[previousStars]);

    // Original 7ef38(..., false), used by 7f300 for conquest results, including defeat.
    public static int ConquestAward(GameSession session)
    {
        long generals = session.Account.Roster.Take(12).Sum(c =>
            10L * (c.Learning.Rank.Level + 1) * (c.Learning.Rank.Level + 2)
            + 25L * (c.Learning.Title.Level + 1) * (c.Learning.Title.Level + 2));
        var resources = session.World.Factions.Single(f => f.Id == session.PlayerFaction).Resources;
        decimal supplies = decimal.Truncate((resources.Money * 2m + resources.Industry * 4m + resources.Food) / 10);
        int round = session.World.Round;
        int band = round < 46 ? 4 : round <= 55 ? 3 : round <= 65 ? 2 : round <= 75 ? 1 : 0;
        long time = round <= 31 ? 23330 : round > 99 ? 0 : (long)((100 - round) * 111 * (1 + band * .5f));
        long progress = Math.Min(2333L, session.Account.CampaignStars.Values.Sum(s => (long)s) * 10);
        int score = (int)Math.Max(1m, Math.Min(100m, decimal.Truncate((time + Math.Min(6999, generals) + supplies + progress) * 100 / 46660)));
        int index = Array.FindIndex(ConquestThresholds, threshold => score >= threshold);
        return index < 0 ? 0 : 1000 - 50 * index;
    }

    public static GameSession Settle(GameSession session, int mode, string campaignKey, int minimumRound, int highestRound)
    {
        var world = session.World;
        if (world.Phase != TurnPhase.Finished || world.Outcome == BattleOutcome.Continue || world.PendingDialogue != null
            || StoryPlayback.BlocksSettlement(world)) return session;
        if (string.IsNullOrWhiteSpace(session.BattleId)) throw new RuleViolation("settlement.identity", "Battle identity is missing");
        bool won = world.Outcome == BattleOutcome.Victory;
        int stars = won && mode != 4 ? Stars(world.Round, minimumRound, highestRound) : 0;
        if (session.Account.BattleResults.TryGetValue(session.BattleId, out var receipt))
            return session with { BattleResult = receipt.Round == world.Round && receipt.Outcome == world.Outcome.ToString()
                ? receipt : new(session.BattleId, world.ScenarioId, world.Outcome.ToString(), world.Round, stars,
                    minimumRound, highestRound, 0, session.CollectedMedals) };
        ProgressionState Latest(ProgressionState current, ProgressionState battle) => battle.Level > current.Level
            || battle.Level == current.Level && battle.Experience > current.Experience ? battle : current;
        var participating = session.World.Commanders.ToDictionary(c => c.Id);
        session = session with { Account = session.Account with { Roster = session.Account.Roster.Select(c =>
            participating.TryGetValue(c.Id, out var runtime) ? c with { Learning = c.Learning with {
                Rank = Latest(c.Learning.Rank, runtime.Rank), Title = Latest(c.Learning.Title, runtime.Title) } } : c).ToArray() } };
        int best = session.Account.CampaignStars.GetValueOrDefault(campaignKey);
        long award = mode == 1 && won ? CampaignAward(stars, best) : mode == 2 ? ConquestAward(session) : 0;
        var result = new BattleResult(session.BattleId, world.ScenarioId, world.Outcome.ToString(), world.Round,
            stars, minimumRound, highestRound, award, session.CollectedMedals);
        var scores = session.Account.CampaignStars.ToDictionary(p => p.Key, p => p.Value);
        if (mode == 1 && won) scores[campaignKey] = Math.Max(best, stars);
        var receipts = session.Account.BattleResults.ToDictionary(p => p.Key, p => p.Value);
        receipts.Add(result.Id, result);
        return session with { BattleResult = result, Account = session.Account with {
            Revision = checked(session.Account.Revision + 1), Medals = checked(session.Account.Medals + award),
            TechnologyStars = mode == 1 && won ? CampaignTechnologyStars.Award(session.Account, stars, best) : session.Account.TechnologyStars,
            CampaignStars = scores, BattleResults = receipts } };
    }
}
