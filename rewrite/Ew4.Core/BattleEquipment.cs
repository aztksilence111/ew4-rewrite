using System;
using System.Linq;

namespace Ew4.Core;

public static class BattleEquipment
{
    public static GameSession SynchronizeAccount(GameSession state, PlayerAccount account, RosterDeploymentRules rules)
    {
        var world = state.World;
        foreach (var owned in account.Roster)
        {
            var saved = state.Account.Roster.FirstOrDefault(c => c.Id == owned.Id);
            var runtime = world.Commanders.FirstOrDefault(c => c.Id == owned.Id);
            if (saved == null || runtime == null || saved.Items.SequenceEqual(owned.Items)) continue;
            var definition = AccountCommanders.Resolve(saved with { Items = owned.Items }, rules.Account);
            int? Percent(int effect) => definition.Items.Where(i => i.Effect == Ew4Commander.Effect(effect)).Select(i => (int?)i.Value).FirstOrDefault();
            world = world with { Commanders = world.Commanders.Select(c => c.Id != owned.Id ? c
                : c with { RankExperiencePercent = Percent(1), TitleExperiencePercent = Percent(2) }).ToArray() };
            var unit = world.Units.FirstOrDefault(u => u.CommanderId == owned.Id);
            if (unit == null || runtime.Withdrawn) continue;
            var definitions = rules.Assignment.Definitions.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            definitions[owned.Id] = definition;
            world = WorldCommanders.AssignForOwner(world, unit.Id, owned.Id, unit.Owner,
                rules.Assignment with { Definitions = definitions, RefillAvailableMovement = false }, refreshEquipment: true).State;
        }
        var next = (state with { Account = account, World = world }).Copy();
        next.Validate();
        return next;
    }

    public static SessionChange Change(GameSession state, long revision, string unitId, string commanderId,
        string itemId, bool equip, RosterDeploymentRules rules)
    {
        state.Validate();
        if (state.Revision != revision) throw new RuleViolation("session.revision", "装备状态已变化，请重新选择");
        var faction = WorldActions.Ready(state.World, state.World.Revision);
        var unit = state.World.Units.SingleOrDefault(u => u.Id == unitId);
        var runtime = state.World.Commanders.SingleOrDefault(c => c.Id == commanderId);
        if (state.World.Outcome != BattleOutcome.Continue || faction.Id != state.PlayerFaction || !faction.Human || unit == null || unit.Owner != faction.Id
            || unit.CommanderId != commanderId || runtime == null || runtime.Owner != faction.Id || runtime.Withdrawn
            || !state.Account.Roster.Any(c => c.Id == commanderId))
            throw new RuleViolation("session.equipment_target", "只能更换己方自带将领的装备");
        var account = equip ? AccountCommanders.Equip(state.Account, state.Account.Revision, commanderId, itemId, rules.Account)
            : AccountCommanders.Unequip(state.Account, state.Account.Revision, commanderId, itemId, rules.Account);
        var definition = AccountCommanders.Resolve(account.Roster.Single(c => c.Id == commanderId), rules.Account);
        int? Percent(int effect) => definition.Items.Where(i => i.Effect == Ew4Commander.Effect(effect)).Select(i => (int?)i.Value).FirstOrDefault();
        var world = state.World with { Commanders = state.World.Commanders.Select(c => c.Id != commanderId ? c
            : c with { RankExperiencePercent = Percent(1), TitleExperiencePercent = Percent(2) }).ToArray() };
        var definitions = rules.Assignment.Definitions.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        definitions[commanderId] = definition;
        var change = WorldCommanders.AssignForOwner(world, unitId, commanderId, faction.Id,
            rules.Assignment with { Definitions = definitions, RefillAvailableMovement = false }, refreshEquipment: true);
        var next = (state with { Revision = checked(state.Revision + 1), Account = account, World = change.State }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice(equip ? "account.item_equipped" : "account.item_unequipped", commanderId, unit.Cell) });
    }
}
