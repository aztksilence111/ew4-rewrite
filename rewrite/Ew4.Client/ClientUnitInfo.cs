using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    // Read the current battle instance, including scenario/enemy/tavern commanders.
    // The account roster alone cannot describe these commanders or battle experience.
    private object UnitInfo(ClientCommand command)
    {
        var current = Require(); var session = current.Session; var world = session.World;
        if (command.Revision != session.Revision) throw new RuleViolation("client.stale", "State changed; refresh the command");
        var unit = world.Units.SingleOrDefault(u => u.Id == command.Unit)
            ?? throw new RuleViolation("client.unit", "Unit is unavailable");
        var offer = current.Rules.Recruitment.Values.SelectMany(v => v.Values)
            .FirstOrDefault(o => o.Template?.Profile.Id == unit.Combatant.Profile.Id);
        return new { session.Revision, unit = unit.Id, cost = offer?.Cost, commander = BattleCommanderView(unit.CommanderId) };
    }

    private object? BattleCommanderView(string? id)
    {
        var current = Require(); var session = current.Session; var world = session.World;
        if (id != null)
        {
            var runtime = world.Commanders.Single(c => c.Id == id);
            var owned = session.Account.Roster.FirstOrDefault(c => c.Id == id);
            var resolved = owned != null ? AccountCommanders.Resolve(owned, current.Rules.Roster.Account)
                : current.Rules.Commanders.Definitions[id];
            var battle = content.Battle(world.ScenarioId);
            int sourceId = owned != null ? content.Catalogs.Runtime.Commanders.Single(c => c.Value.Definition.Id == owned.DefinitionId).Key
                : battle.Armies.Select((a, i) => new { a.Commander, Id = world.ScenarioId + ".unit." + i + ".commander" })
                    .FirstOrDefault(a => a.Id == id)?.Commander
                    ?? battle.Taverns.SelectMany(t => t.Candidates).Select((c, i) => new { c.CommanderId, Id = world.ScenarioId + ".tavern." + i + ".commander" })
                    .Single(c => c.Id == id).CommanderId;
            var source = content.Catalogs.Runtime.Commanders[sourceId];
            var abilities = content.Catalogs.Accounts.FromDefinition(sourceId, "preview").Learning.Abilities.ToDictionary(p => p.Key, p => p.Value);
            if (owned != null) foreach (var value in owned.Learning.Abilities) abilities[value.Key] = value.Value;
            foreach (var value in resolved.Stars) abilities[value.Key] = value.Value;
            abilities["movement"] = resolved.Movement;
            return new { id, definitionId = source.Definition.Id, locked = true, battle = true,
                learning = new CommanderLearningState(runtime.Rank, runtime.Title, abilities),
                training = runtime.MaximumTrainingLevel, items = resolved.Items.Select(i => i.Id).ToArray(),
                healthBonus = runtime.RankHealthBonus, healing = current.Rules.Commanders.TitleHealing.GetValueOrDefault(runtime.Title.Level) };
        }
        return null;
    }
}
