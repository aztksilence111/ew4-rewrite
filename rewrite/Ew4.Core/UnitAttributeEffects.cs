using System;
using System.Linq;

namespace Ew4.Core;

public sealed record UnitAttributeModifiers(int Health = 0, int Range = 0, bool FoodExempt = false, bool IgnoreTerrain = false);

public sealed partial class RuleEventBus
{
    public Combatant RefreshAttributes(Combatant unit)
    {
        double health = 0, range = 0;
        bool food = false, terrain = false;
        foreach (var (skill, slot) in Bound(unit).Where(p => p.Skill.Kind == "passive"))
            foreach (var effect in skill.Effects.Where(e => e.Event == "unit.attributes"
                && (e.TargetTag == null || unit.Profile.Traits.Contains(e.TargetTag))
                && (e.When == null || e.When.Matches(unit, unit, null))))
            {
                var status = unit.Statuses.SingleOrDefault(s => "status." + s.Id == slot.InstanceId);
                double amount = StackedValue(effect, status);
                switch (effect.Operation)
                {
                    case "max_health_add": health += amount; break;
                    case "range_add": range += amount; break;
                    case "food_exempt": food |= amount > 0; break;
                    case "ignore_terrain": terrain |= amount > 0; break;
                    default: throw new RuleViolation("extension.attribute", effect.Operation);
                }
            }
        if (!double.IsFinite(health) || health < 1L - unit.Profile.MaxHealth || health > int.MaxValue - (long)unit.Profile.MaxHealth
            || !double.IsFinite(range) || range < unit.Profile.MinimumRange - (long)unit.Profile.MaximumRange
            || range > int.MaxValue - (long)unit.Profile.MaximumRange)
            throw new RuleViolation("extension.attribute", "Invalid effective health or range");
        var modifiers = new UnitAttributeModifiers((int)health, (int)range, food, terrain);
        var effective = modifiers == new UnitAttributeModifiers() ? null : modifiers;
        if (effective == unit.AttributeModifiers) return unit;
        var updated = unit with { AttributeModifiers = effective };
        // Preserve missing HP when capacity changes; repeated refreshes cannot heal.
        int hp = (int)Math.Clamp((long)unit.Health + updated.MaximumHealth - unit.MaximumHealth, 1, updated.MaximumHealth);
        return updated with { Health = hp };
    }
}
