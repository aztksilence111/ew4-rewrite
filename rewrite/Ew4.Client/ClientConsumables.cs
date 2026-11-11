using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private string ItemDescription(Equipment item, string name, UnitState? unit = null, TrainingRules? training = null)
    {
        string original = content.Label("desc_" + name);
        if (!item.Consumable) return original;
        string result = item.Effect switch
        {
            "ew4.item_effect.6" => "士气设为高昂（+1），持续3回合。",
            "ew4.item_effect.7" => "士气恢复正常（0），清除低落状态。",
            "ew4.item_effect.8" => $"恢复{item.Value}点生命，不超过生命上限。",
            "ew4.item_effect.17" => "部队等级提升1级，当前等级经验归零。",
            _ => ""
        };
        if (unit != null && item.Effect == "ew4.item_effect.8")
            result += $"当前部队实际恢复{Math.Min(item.Value, unit.Combatant.MaximumHealth - unit.Combatant.Health)}点。";
        if (unit != null && training != null && item.Effect == "ew4.item_effect.17")
        {
            int level = training.Stages.ToList().FindIndex(s => s.Id == unit.Veterancy);
            if (level >= 0 && level + 1 < training.Stages.Count)
            {
                int healing = training.Stages[level + 1].HealingOnEntry;
                result += $"升级恢复{healing}点生命，当前实际恢复{Math.Min(healing, unit.Combatant.MaximumHealth - unit.Combatant.Health)}点。";
            }
        }
        return result.Length == 0 ? original : original + "\n具体效果：" + result;
    }

    private object Consumables(ClientCommand command)
    {
        var current = Require(); var session = current.Session;
        var unit = session.World.Units.SingleOrDefault(u => u.Id == command.Unit);
        if (unit == null || unit.Owner != session.PlayerFaction)
            throw new RuleViolation("item.target", "请选择己方部队");
        return new { session.Revision, unit = unit.Id, items = content.ItemDefinitions.Select(e =>
        {
            var item = content.Catalogs.Items.Items[(int)e.Attribute("id")!].Equipment;
            string name = (string)e.Attribute("name")!;
            string? reason = ConsumableItems.UnavailableReason(session, unit.Id, item, current.Rules.Training, content.Extensions.Bus);
            return new { id = item.Id, name = content.Label("name_" + name), image = name + ".png",
                description = ItemDescription(item, name, unit, current.Rules.Training), item.Consumable,
                quantity = session.Account.Inventory.GetValueOrDefault(item.Id), available = reason == null, reason };
        }).Where(i => i.Consumable).ToArray() };
    }
}
