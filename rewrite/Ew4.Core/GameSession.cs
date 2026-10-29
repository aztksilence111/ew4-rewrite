using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record SessionMarket(int? Cell, ItemMarket Market);
public sealed record TavernCandidate(string Id, string CommanderId, int Cell, int AvailableRound, Resources Cost, long Medals);
public sealed record TavernRules
{
    public WorldCommanderRules Assignment { get; init; } = new();
    public IReadOnlyDictionary<string, CommanderRuntime> Templates { get; init; } = new Dictionary<string, CommanderRuntime>();
    public bool FreeForAi { get; init; } = true;
}
public sealed record SessionTradingRules
{
    public ItemTradingRules Items { get; init; } = new();
    public IReadOnlyDictionary<int, int> TitleCommerce { get; init; } = new Dictionary<int, int>();
    public IReadOnlyDictionary<string, int> CommanderCommerce { get; init; } = new Dictionary<string, int>();
}

// The session is the transaction boundary for battlefield resources and persistent account medals.
public sealed record GameSession
{
    public string BattleId { get; init; } = "";
    public BattleResult? BattleResult { get; init; }
    public long CollectedMedals { get; init; }
    public string ContentRevision { get; init; } = "";
    public long Revision { get; init; }
    public string PlayerFaction { get; init; } = "";
    public WorldState World { get; init; } = new();
    public PlayerAccount Account { get; init; } = new();
    public IReadOnlyList<SessionMarket> Markets { get; init; } = Array.Empty<SessionMarket>();
    public IReadOnlyList<TavernCandidate> TavernCandidates { get; init; } = Array.Empty<TavernCandidate>();
    public IReadOnlyList<RosterDeploymentState> RosterDeployments { get; init; } = Array.Empty<RosterDeploymentState>();
    public void Validate() => PlanningScope.Validate(this, ValidateCore);
    private void ValidateCore()
    {
        World.Validate(); Account.Validate();
        if (CollectedMedals < 0 || BattleResult != null && (BattleResult.Id != BattleId
            || BattleResult.Scenario != World.ScenarioId || World.Phase != TurnPhase.Finished
            || BattleResult.Outcome != World.Outcome.ToString() || BattleResult.Round != World.Round))
            throw new RuleViolation("session.result", "Invalid battle result binding");
        if (string.IsNullOrWhiteSpace(ContentRevision) || Revision < 0 || !World.Factions.Any(f => f.Id == PlayerFaction)
            || World.ScenarioPlayerFaction != null && World.ScenarioPlayerFaction != PlayerFaction
            || Markets.Select(m => m.Market.Id).Distinct(StringComparer.Ordinal).Count() != Markets.Count
            || TavernCandidates.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != TavernCandidates.Count
            || TavernCandidates.Select(c => c.CommanderId).Distinct(StringComparer.Ordinal).Count() != TavernCandidates.Count)
            throw new RuleViolation("session.state", "Invalid session identity, player or duplicate service entry");
        foreach (var market in Markets)
            if (market.Cell < 0 || market.Cell >= World.Cells.Count || string.IsNullOrWhiteSpace(market.Market.Id)
                || market.Market.Offers.Any(o => string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.ItemId) || o.Remaining < 0)
                || market.Market.Offers.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != market.Market.Offers.Count)
                throw new RuleViolation("session.market", "Invalid market location, identity or stock");
        foreach (var candidate in TavernCandidates)
        {
            candidate.Cost.Validate();
            if (string.IsNullOrWhiteSpace(candidate.Id) || string.IsNullOrWhiteSpace(candidate.CommanderId)
                || candidate.Cell < 0 || candidate.Cell >= World.Cells.Count || candidate.AvailableRound < 1 || candidate.Medals < 0
                || World.Commanders.Any(c => c.Id == candidate.CommanderId) || Account.Roster.Any(c => c.Id == candidate.CommanderId))
                throw new RuleViolation("session.tavern", "Invalid candidate location, cost or instance identity");
        }
        if (RosterDeployments.Select(q => q.Faction).Distinct(StringComparer.Ordinal).Count() != RosterDeployments.Count
            || RosterDeployments.SelectMany(q => q.CommanderIds).Distinct(StringComparer.Ordinal).Count() != RosterDeployments.Sum(q => q.CommanderIds.Count))
            throw new RuleViolation("session.roster_history", "Duplicate deployment faction or owned commander history");
        // History belongs to the deploying country even if an event later transfers the commander.
        foreach (var quota in RosterDeployments)
            if (!World.Factions.Any(f => f.Id == quota.Faction) || quota.Limit < 0 || quota.CommanderIds.Count > quota.Limit
                || quota.CommanderIds.Any(id => !Account.Roster.Any(c => c.Id == id) || !World.Commanders.Any(c => c.Id == id)))
                throw new RuleViolation("session.roster_history", "Invalid deployment quota or historical commander reference");
    }
    public GameSession Copy() => this with { World = World.Copy(), Account = Account.Copy(),
        Markets = Markets.Select(m => new SessionMarket(m.Cell, new(m.Market.Id, m.Market.Offers.Select(o => o with { }).ToArray()))).ToArray(),
        TavernCandidates = TavernCandidates.Select(c => c with { }).ToArray(),
        RosterDeployments = RosterDeployments.Select(q => q with { CommanderIds = q.CommanderIds.ToArray() }).ToArray() };
}

public sealed record SessionChange(GameSession State, IReadOnlyList<WorldNotice> Notices);

public static class SessionActions
{
    public static GameSession SyncPlayer(GameSession state)
    {
        if (state.World.LocalMultiplayer != null) return LocalMultiplayerGame.SyncPlayer(state);
        string player = state.World.ScenarioPlayerFaction ?? state.PlayerFaction;
        if (player == state.PlayerFaction) return state;
        var quotas = state.RosterDeployments;
        if (quotas.All(q => q.Faction != player))
        {
            int? limit = quotas.SingleOrDefault(q => q.Faction == state.PlayerFaction)?.Limit;
            quotas = quotas.Append(new RosterDeploymentState(player, limit, Array.Empty<string>())).ToArray();
        }
        return state with { PlayerFaction = player, RosterDeployments = quotas };
    }

    private static void Revision(GameSession state, long expected)
    {
        state.Validate();
        if (state.Revision != expected) throw new RuleViolation("session.revision", "Stale session command");
    }
    public static SessionChange DeployTavern(GameSession state, long expectedRevision, string unitId, string candidateId, TavernRules rules)
    {
        Revision(state, expectedRevision);
        var faction = WorldActions.Ready(state.World, state.World.Revision);
        rules.Assignment.Validate();
        var candidate = state.TavernCandidates.SingleOrDefault(c => c.Id == candidateId);
        var unit = state.World.Units.SingleOrDefault(u => u.Id == unitId);
        if (candidate == null || unit == null || unit.Owner != faction.Id || unit.CommanderId != null || candidate.Cell != unit.Cell
            || candidate.AvailableRound > state.World.Round || state.World.Cells[unit.Cell].Owner != faction.Id
            || !state.World.Cells[unit.Cell].Terrain.Active
            || (rules.Assignment.DeploymentBuilding == BuildingRequirement.Required && state.World.Cells[unit.Cell].BuildingKind == null)
            || (rules.Assignment.DeploymentBuilding == BuildingRequirement.Forbidden && state.World.Cells[unit.Cell].BuildingKind != null)
            || !rules.Templates.TryGetValue(candidate.CommanderId, out var template)
            || template.Id != candidate.CommanderId || template.Withdrawn || !rules.Assignment.Definitions.ContainsKey(candidate.CommanderId))
            throw new RuleViolation("session.tavern_unavailable", "Candidate cannot join this unit at this round and location");
        bool free = !faction.Human && rules.FreeForAi;
        if (!free && faction.Id != state.PlayerFaction)
            throw new RuleViolation("session.account_owner", "Another faction cannot spend this player's account");
        if (!free && (AiProduction.AffordableCopies(faction.Resources, candidate.Cost) == 0 || state.Account.Medals < candidate.Medals))
            throw new RuleViolation("session.tavern_funds", "Insufficient battlefield resources or account medals");
        var world = state.World with { Commanders = state.World.Commanders.Concat(new[] { template with { Owner = faction.Id } }).ToArray() };
        var assigned = WorldCommanders.Assign(world, world.Revision, unit.Id, candidate.CommanderId, rules.Assignment);
        var cost = free ? new Resources(0, 0, 0) : candidate.Cost;
        long medals = free ? 0 : candidate.Medals;
        var next = state with { Revision = checked(state.Revision + 1), World = assigned.State with
            { Factions = assigned.State.Factions.Select(f => f.Id != faction.Id ? f : f with { Resources = new(f.Resources.Money - cost.Money, f.Resources.Industry - cost.Industry, f.Resources.Food - cost.Food) }).ToArray() },
            Account = state.Account.Copy() with { Medals = state.Account.Medals - medals, Revision = checked(state.Account.Revision + (medals == 0 ? 0 : 1)) },
            TavernCandidates = state.TavernCandidates.Where(c => c.Id != candidate.Id).ToArray() };
        next = next.Copy(); next.Validate();
        return new(next, assigned.Notices.Concat(new[] { new WorldNotice("tavern.hired", candidate.CommanderId, unit.Cell) }).ToArray());
    }
    private static SessionMarket Market(GameSession state, string marketId)
    {
        var faction = WorldActions.Ready(state.World, state.World.Revision);
        var market = state.Markets.SingleOrDefault(m => m.Market.Id == marketId);
        if (market == null || faction.Id != state.PlayerFaction || !faction.Human || market.Cell is int cell
            && (!state.World.Cells[cell].Terrain.Active || state.World.Cells[cell].Owner != faction.Id || state.World.Cells[cell].BuildingKind == null))
            throw new RuleViolation("session.market_access", "Market is not available to this player");
        return market;
    }
    public static int CommerceLevel(GameSession state, SessionMarket market, SessionTradingRules rules)
    {
        if (rules.TitleCommerce.Any(p => p.Key < 0 || p.Value < 0) || rules.CommanderCommerce.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0))
            throw new RuleViolation("session.commerce_rules", "Invalid commander commerce lookup");
        if (market.Cell == null) return 0;
        int highest = 0;
        foreach (var unit in state.World.Units.Where(u => u.Cell == market.Cell && u.Owner == state.PlayerFaction && u.CommanderId != null))
        {
            var commander = state.World.Commanders.Single(c => c.Id == unit.CommanderId);
            var owned = state.Account.Roster.SingleOrDefault(c => c.Id == commander.Id);
            int ability = owned != null && owned.Learning.Abilities.TryGetValue("commerce", out int value) ? value
                : rules.CommanderCommerce.TryGetValue(commander.Id, out int business) ? business
                : throw new RuleViolation("session.commerce_definition", "Missing commerce ability for market commander");
            if (!rules.TitleCommerce.TryGetValue(commander.Title.Level, out int title))
                throw new RuleViolation("session.commerce_definition", "Missing title commerce stage");
            highest = Math.Max(highest, Math.Max(ability, title));
        }
        return highest;
    }
    public static SessionChange BuyItem(GameSession state, long expectedRevision, string marketId, string offerId, SessionTradingRules rules)
    {
        Revision(state, expectedRevision); var market = Market(state, marketId);
        var change = ItemTrading.Buy(state.Account, state.Account.Revision, market.Market, offerId, CommerceLevel(state, market, rules), rules.Items);
        var next = state with { Revision = checked(state.Revision + 1), Account = change.Account,
            Markets = state.Markets.Select(m => m.Market.Id == marketId ? m with { Market = change.Market } : m).ToArray() };
        next = next.Copy(); next.Validate();
        return new(next, new[] { new WorldNotice("account.item_bought", offerId, market.Cell ?? -1) });
    }
    public static SessionChange SellItem(GameSession state, long expectedRevision, string marketId, string itemId, SessionTradingRules rules)
    {
        Revision(state, expectedRevision); var market = Market(state, marketId);
        var change = ItemTrading.Sell(state.Account, state.Account.Revision, market.Market, itemId, CommerceLevel(state, market, rules),
            state.Account.Roster.SelectMany(c => c.Items).ToArray(), rules.Items);
        var next = (state with { Revision = checked(state.Revision + 1), Account = change.Account }).Copy(); next.Validate();
        return new(next, new[] { new WorldNotice("account.item_sold", itemId, market.Cell ?? -1) });
    }
    public static SessionChange Inherit(GameSession state, long expectedRevision, string donorId, string receiverId, AccountCommanderRules rules)
    {
        Revision(state, expectedRevision);
        var account = AccountCommanders.Inherit(state.Account, state.Account.Revision, donorId, receiverId, state.World.Commanders.Select(c => c.Id).ToArray(), rules);
        var next = (state with { Revision = checked(state.Revision + 1), Account = account }).Copy(); next.Validate();
        return new(next, new[] { new WorldNotice("account.commander_inherited", receiverId) });
    }
    public static SessionChange Equip(GameSession state, long expectedRevision, string commanderId, string itemId, AccountCommanderRules rules)
        => ChangeEquipment(state, expectedRevision, commanderId, itemId, rules, true);
    public static SessionChange Unequip(GameSession state, long expectedRevision, string commanderId, string itemId, AccountCommanderRules rules)
        => ChangeEquipment(state, expectedRevision, commanderId, itemId, rules, false);
    private static SessionChange ChangeEquipment(GameSession state, long expectedRevision, string commanderId, string itemId, AccountCommanderRules rules, bool equip)
    {
        Revision(state, expectedRevision);
        if (state.World.Commanders.Any(c => c.Id == commanderId))
            throw new RuleViolation("session.commander_locked", "A participating commander's equipment cannot change during this scenario");
        var account = equip ? AccountCommanders.Equip(state.Account, state.Account.Revision, commanderId, itemId, rules)
            : AccountCommanders.Unequip(state.Account, state.Account.Revision, commanderId, itemId, rules);
        var next = (state with { Revision = checked(state.Revision + 1), Account = account }).Copy(); next.Validate();
        return new(next, new[] { new WorldNotice(equip ? "account.item_equipped" : "account.item_unequipped", commanderId) });
    }
    public static SessionChange CommitWorld(GameSession state, long expectedRevision, WorldChange change)
    {
        Revision(state, expectedRevision);
        if (change.State.Revision != checked(state.World.Revision + 1) || change.State.ScenarioId != state.World.ScenarioId
            || change.State.ContentRevision != state.World.ContentRevision)
            throw new RuleViolation("session.world_revision", "World transaction must advance this scenario once");
        var next = BattleMedals.Collect(SyncPlayer(state with { Revision = checked(state.Revision + 1), World = change.State }), change.Notices).Copy(); next.Validate();
        return new(next, change.Notices.ToArray());
    }
}
