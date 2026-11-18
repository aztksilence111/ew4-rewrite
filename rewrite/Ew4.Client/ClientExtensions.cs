using Ew4.Core;

namespace Ew4.Client;

public sealed record ClientSkillView(string Id, string InstanceId, int Level, int Order, float? X, float? Y,
    string Name, string Description, string? Icon, string Kind, string Target, int Cooldown, Resources Cost);
public sealed partial class ClientGame
{
    private object? OrderReactionView(WorldState world)
    {
        var choice = world.PendingOrderAction?.Choices?.FirstOrDefault(c => c.Accept == null);
        if (choice == null) return null;
        var skill = content.Extensions.Bus.Skills[choice.Skill];
        var source = world.Units.Single(u => u.Id == choice.Source);
        var target = world.Units.SingleOrDefault(u => u.Id == choice.Target);
        return new { choice.Source, choice.Target, name = content.Label(skill.NameKey), description = content.Label(skill.DescriptionKey),
            owner = source.Owner, cell = target?.Cell ?? source.Cell };
    }
    private ClientSkillView SkillView(SkillSlot slot)
    {
        if (content.Extensions.Bus.Skills.TryGetValue(slot.Id, out var skill))
            return new(slot.Id, slot.InstanceId, slot.Level, slot.Order, slot.X, slot.Y,
                content.Label(string.IsNullOrEmpty(skill.NameKey) ? skill.Id : skill.NameKey), content.Label(skill.DescriptionKey),
                skill.Icon == null ? null : "extensions/" + skill.Icon[8..], skill.Kind, skill.Target, skill.Cooldown, skill.Cost);
        int number = slot.Id.StartsWith("ew4.skill.", StringComparison.Ordinal) && int.TryParse(slot.Id[10..], out int id) ? id + 1 : 0;
        return new(slot.Id, slot.InstanceId, slot.Level, slot.Order, slot.X, slot.Y,
            content.Label(number > 0 ? "name_skill_" + number.ToString("00") : slot.Id),
            number > 0 ? LegacySkillDescription(number) : "",
            number > 0 ? "skill_" + number.ToString("00") + ".png" : null, "passive", "self", 0, new(0, 0, 0));
    }
    private ClientSkillView[] SkillViews(Combatant unit) => RuleEventBus.Slots(unit).OrderBy(s => s.Order).Select(SkillView).ToArray();
    private ClientSkillView[] StatusViews(Combatant unit) => unit.Statuses.Select(saved =>
    {
        var status = content.Extensions.Bus.Statuses[saved.Id];
        return new ClientSkillView(status.Id, "status." + status.Id, saved.Level, 0, null, null,
            content.Label(string.IsNullOrEmpty(status.NameKey) ? status.Id : status.NameKey) + " (" + saved.Stacks + "/" + saved.RemainingTurns + ")",
            content.Label(status.DescriptionKey), status.Icon == null ? null : "extensions/" + status.Icon[8..],
            "status", "self", 0, new(0, 0, 0));
    }).ToArray();
    private object[] ActiveSkills(WorldState world, UnitState unit)
    {
        var faction = world.Factions.Single(f => f.Id == unit.Owner);
        return SkillViews(unit.Combatant).Where(s => s.Kind == "active" || content.Extensions.Bus.Skills.TryGetValue(s.Id, out var pending)
            && pending.Order != null && CommandOrders.HasPending(world, unit, pending)).Select(s =>
        {
            var def = content.Extensions.Bus.Skills[s.Id];
            var cells = def.Target != "cell" ? [] : Enumerable.Range(0, world.Cells.Count)
                .Where(cell => content.Extensions.Bus.ValidCellTarget(world, unit, def, cell)).ToArray();
            long ready = world.ExtensionState.GetValueOrDefault(TacticalSkills.CooldownKey(unit, def, s.InstanceId));
            var targets = def.Target == "cell" ? [] : world.Units.Where(t => new HexMap(world.Width, world.Height).Distance(unit.Cell, t.Cell) <= def.Range
                && (def.Target == "self" ? t.Id == unit.Id : def.Target == "ally"
                    ? WorldDiplomacy.Allied(world, t.Owner, faction.Id)
                    : !WorldDiplomacy.Allied(world, t.Owner, faction.Id))).Select(t => t.Id).ToArray();
            if (def.Maneuver != null) targets = world.Units.Where(t => TacticalSkills.Eligible(world, unit, t, def)).Select(t => t.Id).ToArray();
            if (def.Order != null) targets = CommandOrders.Candidates(world, unit, def, content.Extensions.Bus);
            bool pendingOrder = def.Order != null && CommandOrders.HasPending(world, unit, def);
            bool available = !unit.Acted && unit.Morale.Effective != -3 && ready <= world.Round && (targets.Length > 0 || cells.Length > 0 || def.Maneuver != null)
                && (def.Cost.Money == 0 || faction.Resources.Money >= def.Cost.Money)
                && (def.Cost.Industry == 0 || faction.Resources.Industry >= def.Cost.Industry) && (def.Cost.Food == 0 || faction.Resources.Food >= def.Cost.Food);
            if (def.Maneuver != null) available &= TacticalSkills.CanActivate(world, unit);
            if (def.Order != null) available = unit.Morale.Effective != -3 && (pendingOrder || ready <= world.Round)
                && CommandOrders.CanActivate(world, unit, def, content.Extensions.Bus);
            var enemies = CommandOrders.EnemyTargets(world, unit, def);
            var legalSelections=CommandOrders.LegalSelections(world,unit,def)?.ToArray();
            if (def.Order?.Enemy != null && !def.Order.EnemyOptional && enemies.Length == 0) available = false;
            var secondaryEnemies = def.Order?.SecondaryEnemy == null ? Array.Empty<string>() : world.Units.Where(t =>
                CommandOrders.Matches(world, unit, t, def.Order.SecondaryEnemy) && new HexMap(world.Width, world.Height).Distance(unit.Cell, t.Cell) <= def.Order.SecondaryEnemy.Radius).Select(t => t.Id).ToArray();
            var secondaryByEnemy=enemies.ToDictionary(e=>e,e=>secondaryEnemies.Where(id=>
                CommandOrders.SecondaryAvailable(world,unit,def,e,id)).ToArray());
            int minimum = def.Order == null ? 0 : pendingOrder ? 1 : def.Order.Mode == "summon" ? def.Order.Profiles.Length
                : def.Order.Mode == "swap" ? def.Order.IncludesSelf ? 1 : 2 : def.Order.SelectSelf || def.Order.IncludesSelf ? 0 : 1;
            var shown = def.Order?.Mode == "summon" && CommandOrders.SummonCost(world, unit, def, content.Extensions.Bus) is { } price ? s with { Cost = price } : s;
            return (object)new { skill = shown, available, remainingCooldown = Math.Max(0, ready - world.Round), targets, cells,
                multiple = def.Maneuver != null || def.Order != null, maximumTargets = def.Maneuver?.MaximumOthers ?? (pendingOrder ? 1 : def.Order?.Recipients.Count ?? 1),
                includesSelf = def.Maneuver != null || def.Order?.IncludesSelf == true || def.Order?.SelectSelf == true,
                minimumTargets = minimum, enemyTargets = enemies, requiresEnemy = !pendingOrder && def.Order?.Enemy != null,
                enemyOptional = def.Order?.EnemyOptional ?? false, secondaryEnemies,secondaryByEnemy,
                secondaryOwn=def.Order?.SecondaryEnemy?.Own==true,siteTarget=def.Order?.Plan=="op_recruit", pendingOrder, legalSelections };
        }).ToArray();
    }
    private ClientSkillView[] ManeuverViews(WorldState world, UnitState unit) => unit.Maneuvers
        .Where(g => content.Extensions.Bus.Skills.ContainsKey(g.Skill) && world.Units.Any(s => s.Id == g.Source
            && s.CommanderId == g.SourceCommander && s.Owner == g.Owner && RuleEventBus.Slots(s.Combatant).Any(slot => slot.Id == g.Skill && slot.InstanceId == g.Instance))
            && unit.CommanderId == g.RecipientCommander)
        .Select(g =>
        {
            var skill = content.Extensions.Bus.Skills[g.Skill];
            string stage = world.Round == g.StartRound ? "转进阶段" : g.CounterReady && !g.Used ? "反攻待命" : "回避保护";
            return new ClientSkillView(g.Skill, "maneuver." + g.Source + "." + g.Instance, 1, 0, null, null,
                content.Label(skill.NameKey) + " · " + stage, content.Label(skill.DescriptionKey),
                skill.Icon == null ? null : "extensions/" + skill.Icon[8..], "status", "self", 0, new(0, 0, 0));
        }).ToArray();
}
