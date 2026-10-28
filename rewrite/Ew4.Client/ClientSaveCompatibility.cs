using Ew4.Core;
using Ew4.Legacy;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private static void RequireSavedDefinition(bool available, string kind, string id)
    {
        if (!available) throw new RuleViolation("save.missing_definition", "存档所需的" + kind + "不存在：" + id);
    }

    private void ValidateReusableAccount(PlayerAccount account)
    {
        account.Validate();
        var rules = content.Catalogs.Accounts.Rules();
        foreach (var commander in account.Roster)
        {
            RequireSavedDefinition(rules.Definitions.ContainsKey(commander.DefinitionId), "将领", commander.DefinitionId);
            foreach (string item in commander.Items) RequireSavedDefinition(rules.Items.ContainsKey(item), "道具", item);
            AccountCommanders.Resolve(commander, rules);
        }
        foreach (string item in account.Inventory.Keys) RequireSavedDefinition(rules.Items.ContainsKey(item), "道具", item);
        foreach (string commander in account.CommanderOffers.Values.SelectMany(v => v))
            RequireSavedDefinition(rules.Definitions.ContainsKey(commander), "将领", commander);
    }

    private GameSession ReuseSnapshot(GameSession state, LegacyScenarioStart current)
    {
        var world = state.World;
        var initial = current.Session.World;
        if (world.ScenarioId != initial.ScenarioId || world.Width != initial.Width || world.Height != initial.Height)
            throw new RuleViolation("save.map_incompatible", "存档地图与当前剧本的尺寸或标识不一致");
        ValidateReusableAccount(state.Account);
        foreach (var faction in world.Factions)
            RequireSavedDefinition(current.Rules.Production.ContainsKey(faction.Id), "国家规则", faction.Id);
        foreach (var unit in world.Units)
        {
            RequireSavedDefinition(content.Catalogs.Runtime.Profiles.TryGetValue(unit.Combatant.Profile.Id, out var profile)
                && profile.Profile.Category == unit.Combatant.Profile.Category, "兵种", unit.Combatant.Profile.Id);
        }
        foreach (var commander in world.Commanders)
            RequireSavedDefinition(current.Rules.Commanders.Definitions.ContainsKey(commander.Id)
                || state.Account.Roster.Any(c => c.Id == commander.Id), "战场将领", commander.Id);
        foreach (var candidate in state.TavernCandidates)
            RequireSavedDefinition(current.Rules.Tavern.Assignment.Definitions.ContainsKey(candidate.CommanderId), "酒馆将领", candidate.CommanderId);
        foreach (var market in state.Markets)
            foreach (var offer in market.Market.Offers)
                RequireSavedDefinition(current.Rules.Trading.Items.Definitions.ContainsKey(offer.ItemId), "商店道具", offer.ItemId);
        foreach (var cell in world.Cells.Where(c => c.BuildingKind != null))
            RequireSavedDefinition(content.Catalogs.Buildings.Definitions.Values.Any(b => b.Kind == cell.BuildingKind
                && cell.BuildingLevel <= b.Levels.Count), "建筑", cell.BuildingKind!);
        // Keep resolved combat values and all progress; only the binding changes.
        var reused = state with { ContentRevision = current.Session.ContentRevision,
            Account = state.Account with { ContentRevision = current.Session.Account.ContentRevision, Revision = checked(state.Account.Revision + 1) },
            World = world with { ContentRevision = initial.ContentRevision } };
        reused.Validate();
        return reused;
    }
}
