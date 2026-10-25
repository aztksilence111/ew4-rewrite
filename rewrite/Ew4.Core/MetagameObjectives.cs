using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ObjectiveReward(long Medals = 0, long Badges = 0)
{
    public IReadOnlyDictionary<string, long> Items { get; init; } = new Dictionary<string, long>();
    public IReadOnlyList<string> Commanders { get; init; } = Array.Empty<string>();
}
public sealed record ObjectiveCondition(string Type, long Count = 1, string? Id = null, int? Cell = null, string Scope = "battle");
public sealed record MetagameObjective(string Id, string Kind, string Name, string Description,
    IReadOnlyList<ObjectiveCondition> Conditions, ObjectiveReward Reward, bool Any = false,
    int? Mode = null, string? Scenario = null, string? Country = null, bool Hidden = false);
public sealed record ObjectiveBattleProgress(string Scenario, int Mode, string Country, long UnitsDestroyed,
    IReadOnlyList<int> CapturedCities, string BattleId = "", string Faction = "", bool Won = false);
public sealed record ObjectiveContext(GameSession Session, int Mode, string Country, WorldState? Previous = null);
public sealed record ObjectiveConditionProgress(string Type, long Current, long Required, bool Complete);

public static class MetagameObjectives
{
    private static readonly string[] AccountTypes = ["commanderOwned", "itemOwned", "campaignUnlocked", "campaignStars"];
    private static readonly string[] BattleTypes = ["cellControlled", "citiesControlled", "citiesCaptured", "unitsDestroyed", "battleWon", "starsAtLeast", "roundAtMost", "eventTriggered"];
    public static void Validate(IReadOnlyList<MetagameObjective> objectives)
    {
        if (objectives == null || objectives.Count > 256 || objectives.Any(o => o == null)
            || objectives.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != objectives.Count)
            throw new RuleViolation("objectives.configuration", "Invalid or duplicate objectives");
        foreach (var o in objectives)
        {
            if (string.IsNullOrWhiteSpace(o.Id) || o.Id.Length > 96 || o.Id.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_'))
                || o.Kind is not ("task" or "achievement") || string.IsNullOrWhiteSpace(o.Name) || o.Name.Length > 120
                || o.Hidden && o.Kind != "achievement"
                || o.Description == null || o.Description.Length > 2000 || o.Conditions == null || o.Conditions.Count is < 1 or > 32
                || o.Reward == null || o.Reward.Medals < 0 || o.Reward.Badges < 0 || o.Mode.HasValue && o.Mode is not (1 or 2)
                || o.Scenario != null && string.IsNullOrWhiteSpace(o.Scenario) || o.Country != null && string.IsNullOrWhiteSpace(o.Country))
                throw new RuleViolation("objectives.configuration", "Invalid objective: " + o.Id);
            if (o.Reward.Items == null || o.Reward.Items.Count > 256 || o.Reward.Items.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 1)
                || o.Reward.Commanders == null || o.Reward.Commanders.Count > 64 || o.Reward.Commanders.Any(string.IsNullOrWhiteSpace)
                || o.Reward.Commanders.Distinct(StringComparer.Ordinal).Count() != o.Reward.Commanders.Count)
                throw new RuleViolation("objectives.reward", "Invalid reward in " + o.Id);
            foreach (var c in o.Conditions)
                if (c == null || !AccountTypes.Contains(c.Type) && !BattleTypes.Contains(c.Type) || c.Count < 1
                    || c.Scope is not ("battle" or "lifetime") || c.Scope == "lifetime" && c.Type is not ("citiesCaptured" or "unitsDestroyed" or "battleWon")
                    || c.Type == "cellControlled" && (!c.Cell.HasValue || c.Cell < 0 || o.Scenario == null)
                    || c.Type != "cellControlled" && c.Cell.HasValue
                    || c.Type is "campaignUnlocked" or "eventTriggered" && string.IsNullOrWhiteSpace(c.Id)
                    || c.Id != null && string.IsNullOrWhiteSpace(c.Id)
                    || c.Id != null && c.Type is not ("commanderOwned" or "itemOwned" or "campaignUnlocked" or "campaignStars" or "eventTriggered")
                    || c.Type is "cellControlled" or "campaignUnlocked" or "eventTriggered" or "battleWon" && c.Count != 1 && c.Scope != "lifetime"
                    || c.Type == "starsAtLeast" && c.Count > 5)
                    throw new RuleViolation("objectives.condition", "Invalid condition in " + o.Id);
        }
    }
    private static bool Matches(MetagameObjective o, ObjectiveBattleProgress b) => (!o.Mode.HasValue || o.Mode == b.Mode)
        && (o.Scenario == null || o.Scenario == b.Scenario) && (o.Country == null || o.Country == b.Country);
    private static string BattleKey(GameSession session) => session.BattleId + ":" + session.PlayerFaction;
    public static ObjectiveConditionProgress Progress(PlayerAccount account, MetagameObjective objective, ObjectiveCondition c, ObjectiveContext? context)
    {
        var session = context?.Session;
        bool live = context != null && context.Mode is 1 or 2 && session!.World.LocalMultiplayer == null
            && Matches(objective, new(session.World.ScenarioId, context.Mode, context.Country, 0, []));
        var world = live ? session!.World : null;
        string? player = live ? session!.PlayerFaction : null;
        IEnumerable<KeyValuePair<string, ObjectiveBattleProgress>> battles = account.ObjectiveBattles.Where(p => Matches(objective, p.Value));
        var current = live ? account.ObjectiveBattles.GetValueOrDefault(BattleKey(session!)) : null;
        long value = c.Type switch
        {
            "commanderOwned" => account.Roster.LongCount(g => c.Id == null || g.DefinitionId == c.Id),
            "itemOwned" => c.Id == null ? account.Inventory.Values.Sum() : account.Inventory.GetValueOrDefault(c.Id),
            "campaignUnlocked" => account.UnlockedCampaigns.Contains(c.Id!) ? 1 : 0,
            "campaignStars" => c.Id == null ? account.CampaignStars.Values.Sum(s => (long)s) : account.CampaignStars.GetValueOrDefault(c.Id),
            "cellControlled" => world != null && c.Cell < world.Cells.Count && world.Cells[c.Cell!.Value].Owner == player ? 1 : 0,
            "citiesControlled" => world?.Cells.LongCount(s => s.Owner == player && s.BuildingKind == "city") ?? 0,
            "citiesCaptured" => c.Scope == "lifetime" ? battles.SelectMany(p => p.Value.CapturedCities.Select(cell => (p.Value.BattleId, cell))).Distinct().LongCount() : current?.CapturedCities.Count ?? 0,
            "unitsDestroyed" => c.Scope == "lifetime" ? battles.Sum(p => p.Value.UnitsDestroyed) : current?.UnitsDestroyed ?? 0,
            "battleWon" => c.Scope == "lifetime" ? battles.Where(p => p.Value.Won).Select(p => p.Value.BattleId).Distinct(StringComparer.Ordinal).LongCount()
                : live && session!.BattleResult?.Outcome == "Victory" ? 1 : 0,
            "starsAtLeast" => live ? session!.BattleResult?.Stars ?? 0 : 0,
            "roundAtMost" => world != null && world.Round <= c.Count ? c.Count : 0,
            "eventTriggered" => world?.TriggeredScenarioEvents.Contains(c.Id!) == true ? 1 : 0,
            _ => 0
        };
        bool requiresContext = c.Scope != "lifetime" && (BattleTypes.Contains(c.Type)
            || objective.Mode.HasValue || objective.Scenario != null || objective.Country != null);
        return new(c.Type, value, c.Count, value >= c.Count && (!requiresContext || live));
    }
    public static PlayerAccount Observe(PlayerAccount account, IReadOnlyList<MetagameObjective> objectives, ObjectiveContext? context = null)
    {
        if (objectives.Count == 0) return account;
        var next = account;
        bool trackCaptures = objectives.Any(o => o.Conditions.Any(c => c.Type == "citiesCaptured"));
        bool trackBattles = trackCaptures || objectives.Any(o => o.Conditions.Any(c => c.Type == "unitsDestroyed" || c.Type == "battleWon" && c.Scope == "lifetime"));
        if (trackBattles && context is { Mode: 1 or 2 } && context.Session.World.LocalMultiplayer == null && !string.IsNullOrEmpty(context.Session.BattleId))
        {
            var session = context.Session; var world = session.World;
            string key = BattleKey(session);
            var prior = account.ObjectiveBattles.GetValueOrDefault(key);
            var captured = (prior?.CapturedCities ?? []).ToHashSet();
            if (trackCaptures && context.Previous is { } before && before.ScenarioId == world.ScenarioId)
                for (int i = 0; i < Math.Min(before.Cells.Count, world.Cells.Count); i++)
                    if (world.Cells[i].BuildingKind == "city" && world.Cells[i].Owner == session.PlayerFaction && before.Cells[i].Owner != session.PlayerFaction)
                        captured.Add(i);
            long kills = Math.Max(prior?.UnitsDestroyed ?? 0, world.Factions.Single(f => f.Id == session.PlayerFaction).Kills.Values.Sum());
            bool won = prior?.Won == true || session.BattleResult?.Outcome == "Victory";
            var progress = new ObjectiveBattleProgress(world.ScenarioId, context.Mode, context.Country, kills, captured.OrderBy(c => c).ToArray(), session.BattleId, session.PlayerFaction, won);
            if (prior == null || kills != prior.UnitsDestroyed || captured.Count != prior.CapturedCities.Count || won != prior.Won)
            {
                var ledger = next.ObjectiveBattles.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                ledger[key] = progress;
                next = next with { ObjectiveBattles = ledger };
            }
        }
        var completed = next.CompletedObjectives.ToHashSet(StringComparer.Ordinal);
        foreach (var objective in objectives.Where(o => !completed.Contains(o.Id)))
        {
            var conditions = objective.Conditions.Select(c => Progress(next, objective, c, context));
            if (objective.Any ? conditions.Any(c => c.Complete) : conditions.All(c => c.Complete)) completed.Add(objective.Id);
        }
        if (completed.Count != next.CompletedObjectives.Count) next = next with { CompletedObjectives = completed.OrderBy(c => c, StringComparer.Ordinal).ToArray() };
        return ReferenceEquals(account, next) ? account : next with { Revision = checked(account.Revision + 1) };
    }
    public static PlayerAccount Claim(PlayerAccount account, long revision, MetagameObjective objective,
        ItemTradingRules? itemRules = null, AccountCommanderRules? commanderRules = null, IReadOnlyList<OwnedCommander>? commanders = null)
    {
        account.Validate(); Validate(new[] { objective });
        if (account.Revision != revision) throw new RuleViolation("account.revision", "Account changed; refresh this command");
        if (!account.CompletedObjectives.Contains(objective.Id)) throw new RuleViolation("objectives.incomplete", "条件尚未达成");
        string receipt = "objective." + objective.Id;
        if (account.ClaimedRewards.Contains(receipt)) throw new RuleViolation("objectives.claimed", "奖励已领取");
        commanders ??= Array.Empty<OwnedCommander>();
        if (!commanders.Select(c => c.DefinitionId).SequenceEqual(objective.Reward.Commanders)
            || commanders.Count > 0 && commanderRules == null)
            throw new RuleViolation("objectives.reward", "Reward commanders do not match configured definitions");
        if (commanders.Any(c => account.Roster.Any(held => held.DefinitionId == c.DefinitionId)))
            throw new RuleViolation("objectives.commander_owned", "已拥有奖励中的将领，暂时无法领取");
        if (commanderRules != null)
        {
            if (account.Roster.Count + commanders.Count > commanderRules.MaximumRoster)
                throw new RuleViolation("account.capacity", "将领名册已满，暂时无法领取");
            foreach (var commander in commanders) AccountCommanders.Resolve(commander, commanderRules);
        }
        var inventory = account.Inventory.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var reward in objective.Reward.Items)
            inventory[reward.Key] = checked(inventory.GetValueOrDefault(reward.Key) + reward.Value);
        // Definition equipment is owned inventory too, just as when recruiting a commander.
        foreach (string item in commanders.SelectMany(c => c.Items)) inventory[item] = checked(inventory.GetValueOrDefault(item) + 1);
        if (objective.Reward.Items.Count > 0 || commanders.Any(c => c.Items.Count > 0))
        {
            if (itemRules == null) throw new RuleViolation("objectives.reward", "Reward item rules are required");
            itemRules.Validate();
            if (inventory.Count > itemRules.MaximumDistinctItems)
                throw new RuleViolation("trading.capacity", "物品库存已满，暂时无法领取");
            foreach (var held in inventory)
                if (!itemRules.Definitions.TryGetValue(held.Key, out var definition))
                    throw new RuleViolation("objectives.reference", "Unknown reward item: " + held.Key);
                else if (definition.Unique && held.Value > 1 || held.Value > definition.MaximumOwned)
                    throw new RuleViolation("objectives.item_capacity", "奖励物品已达持有上限，暂时无法领取");
        }
        var granted = commanders.Select(c => c.DefinitionId).ToHashSet(StringComparer.Ordinal);
        var next = account with { Revision = checked(account.Revision + 1), Medals = checked(account.Medals + objective.Reward.Medals),
            Badges = checked(account.Badges + objective.Reward.Badges), Inventory = inventory,
            Roster = account.Roster.Concat(commanders).Select(c => c.Copy()).ToArray(),
            CommanderOffers = account.CommanderOffers.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value.Where(id => !granted.Contains(id)).ToArray()),
            ClaimedRewards = account.ClaimedRewards.Append(receipt).ToArray() };
        next.Validate(); return next;
    }
}
