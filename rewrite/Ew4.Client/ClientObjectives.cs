using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private ObjectiveContext ObjectiveContext(GameSession session, WorldState? previous = null)
    {
        int mode = content.Scenarios.Single(s => s.Id == session.World.ScenarioId).Mode;
        string countryId = FactionCountries(session.World).GetValueOrDefault(session.PlayerFaction) ?? session.PlayerFaction;
        return new(session, mode, countryId, previous);
    }
    private PlayerAccount ObserveObjectives(PlayerAccount account)
    {
        if (content.Extensions.Metagame.Objectives.Count == 0) return account;
        var context = game == null ? null : ObjectiveContext(game.Session with { Account = account });
        var next = MetagameObjectives.Observe(account, content.Extensions.Metagame.Objectives, context);
        if (!ReferenceEquals(next, account))
        {
            StoreAccount(next);
            if (game != null) game = game with { Session = game.Session with { Account = next, Revision = checked(game.Session.Revision + 1) } };
        }
        return next;
    }
    private object Objectives(ClientCommand command)
    {
        var account = ObserveObjectives(Account());
        if (command.Op == "claim")
        {
            var objective = content.Extensions.Metagame.Objectives.SingleOrDefault(o => o.Id == command.Offer)
                ?? throw new RuleViolation("objectives.unknown", "Unknown task or achievement");
            var commanders = objective.Reward.Commanders.Select(id => {
                int legacyId = content.Catalogs.Runtime.Commanders.Single(p => p.Value.Definition.Id == id).Key;
                return content.Catalogs.Accounts.FromDefinition(legacyId, "local.commander." + legacyId + "." + Guid.NewGuid().ToString("N"));
            }).ToArray();
            var next = MetagameObjectives.Claim(account, command.Revision, objective,
                content.Catalogs.Items.Rules(), content.Catalogs.Accounts.Rules(), commanders);
            foreach (int section in commanders.Select(c => CommanderSection(c.DefinitionId)).Distinct())
                if (next.Roster.Count(c => CommanderSection(c.DefinitionId) == section) > content.Extensions.Metagame.CommanderSectionCapacity(section))
                    throw new RuleViolation("account.capacity", "奖励将领所属星级区已满，暂时无法领取");
            StoreAccount(next);
            if (game != null) game = game with { Session = game.Session with { Account = next, Revision = checked(game.Session.Revision + 1) } };
            account = next;
        }
        else if (command.Op is not ("" or "list")) throw new RuleViolation("objectives.command", "Unknown objective command");
        var context = game == null || content.Extensions.Metagame.Objectives.Count == 0 ? null : ObjectiveContext(game.Session);
        return new { account.Revision, account.Medals, account.Badges,
            entries = content.Extensions.Metagame.Objectives.Where(o => !o.Hidden || account.CompletedObjectives.Contains(o.Id)
                || account.ClaimedRewards.Contains("objective." + o.Id)).Select(o => new {
                o.Id, o.Kind, name = content.Label(o.Name), description = content.Label(o.Description), o.Reward,
                completed = account.CompletedObjectives.Contains(o.Id), claimed = account.ClaimedRewards.Contains("objective." + o.Id),
                conditions = o.Conditions.Select(c => MetagameObjectives.Progress(account, o, c, context)) }) };
    }
}
