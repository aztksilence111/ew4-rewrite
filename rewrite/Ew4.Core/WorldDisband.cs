using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record DisbandRules
{
    public bool Enabled { get; init; }
    public string PlayerFaction { get; init; } = "";
    public IReadOnlyDictionary<string, Resources> RecruitmentCosts { get; init; } = new Dictionary<string, Resources>();
}

public static class WorldDisband
{
    public static Resources Refund(UnitState unit, Resources recruitmentCost)
    {
        recruitmentCost.Validate();
        int maximum = unit.Combatant.Profile.MaxHealth, health = unit.Combatant.Health;
        if (maximum <= 0 || health <= 0 || health > maximum) throw new RuleViolation("disband.health", "Invalid remaining health");
        long Portion(long value) => checked((long)decimal.Floor((decimal)value * health / maximum / 2));
        return new(Portion(recruitmentCost.Money), Portion(recruitmentCost.Industry), Portion(recruitmentCost.Food));
    }
    public static bool CanDisband(WorldState state, string unitId, DisbandRules rules) => CanDisband(state, unitId, rules, true);

    private static bool CanDisband(WorldState state, string unitId, DisbandRules rules, bool human) => rules.Enabled
        && state.Phase == TurnPhase.Active && state.PendingDialogue == null && state.ActiveFaction == rules.PlayerFaction
        && state.Factions.Any(f => f.Id == rules.PlayerFaction && f.Human == human && !f.Defeated)
        && state.Units.Any(u => u.Id == unitId && u.Owner == rules.PlayerFaction && u.CommanderId == null
            && rules.RecruitmentCosts.ContainsKey(u.Combatant.Profile.Id));

    public static WorldChange Execute(WorldState state, long revision, string unitId, DisbandRules rules)
        => ExecuteCore(state, revision, unitId, rules, true);

    internal static WorldChange ExecuteAi(WorldState state, long revision, string unitId, DisbandRules rules)
        => ExecuteCore(state, revision, unitId, rules, false);

    private static WorldChange ExecuteCore(WorldState state, long revision, string unitId, DisbandRules rules, bool human)
    {
        var faction = WorldActions.Ready(state, revision);
        if (!CanDisband(state, unitId, rules, human)) throw new RuleViolation("disband.unavailable", "Only eligible conquest units without a commander may be disbanded");
        var unit = state.Units.Single(u => u.Id == unitId);
        var refund = Refund(unit, rules.RecruitmentCosts[unit.Combatant.Profile.Id]);
        var next = (state with
        {
            Revision = checked(state.Revision + 1),
            Units = state.Units.Where(u => u.Id != unitId).ToArray(),
            Factions = state.Factions.Select(f => f.Id == faction.Id ? f with { Resources = new(checked(f.Resources.Money + refund.Money),
                checked(f.Resources.Industry + refund.Industry), checked(f.Resources.Food + refund.Food)) } : f).ToArray()
        }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice("unit.disbanded", unitId) });
    }
}
