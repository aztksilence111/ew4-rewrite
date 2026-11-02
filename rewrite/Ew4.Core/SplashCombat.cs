using System;
using System.Linq;

namespace Ew4.Core;

public sealed record SplashRules
{
    public string RequiredTrait { get; init; } = "line_splash";
    public int Numerator { get; init; } = 60;
    public int Denominator { get; init; } = 100;
    public int MinimumPositiveDamage { get; init; } = 1;
    public int RightDirection { get; init; }
    public int LeftDirection { get; init; } = 3;
    public bool AllowFriendlyFire { get; init; } = true;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RequiredTrait) || Numerator < 0 || Denominator < 1 || MinimumPositiveDamage < 0
            || RightDirection < 0 || RightDirection >= 6 || LeftDirection < 0 || LeftDirection >= 6)
            throw new RuleViolation("splash.configuration", "Invalid splash selection or damage configuration");
    }
}

public sealed record CombatExchange(Combatant Attacker, Combatant Defender, int Distance);
public sealed record SplashCombatResult(CombatResult Main, int SplashDamage);

public static class SplashCombat
{
    public static SplashCombatResult Calculate(CombatExchange primary, CombatExchange? secondary, Ew4Random callerRandom,
        CombatRules combat, SplashRules splash)
    {
        splash.Validate();
        var random = new Ew4Random(callerRandom.State);
        int splashDamage = 0;
        if (secondary != null && primary.Attacker.Profile.HasTrait(splash.RequiredTrait))
        {
            var extra = Ew4Combat.Calculate(secondary.Attacker, secondary.Defender, secondary.Distance, random, combat);
            splashDamage = checked((int)((long)extra.DefenderDamage * splash.Numerator / splash.Denominator));
            if (extra.DefenderDamage > 0) splashDamage = Math.Max(splash.MinimumPositiveDamage, splashDamage);
        }
        var main = Ew4Combat.Calculate(primary.Attacker, primary.Defender, primary.Distance, random, combat);
        callerRandom.Commit(random.State);
        return new(main, splashDamage);
    }

    public static bool FacesRight(HexMap map, int from, int to)
    {
        map.Check(from); map.Check(to);
        int fromRow = from / map.Width, toRow = to / map.Width;
        long fromX = 2L * (from % map.Width) + fromRow % 2, toX = 2L * (to % map.Width) + toRow % 2;
        return fromX < toX || (fromX == toX && fromRow >= toRow);
    }

    public static UnitState? Target(WorldState state, UnitState attacker, UnitState defender, SplashRules rules)
    {
        rules.Validate();
        if (!attacker.Combatant.Profile.HasTrait(rules.RequiredTrait)) return null;
        var map = new HexMap(state.Width, state.Height);
        int direction = FacesRight(map, attacker.Cell, defender.Cell) ? rules.RightDirection : rules.LeftDirection;
        int cell = map.Neighbor(defender.Cell, direction);
        if (cell < 0) return null;
        var target = state.Units.FirstOrDefault(u => u.Cell == cell && u.Id != attacker.Id && u.Id != defender.Id
            && (!WorldUnitLayers.Enabled(state) || WorldUnitLayers.Occupies(state, u.Combatant.Profile).Contains(WorldUnitLayers.AttackLayer(state, attacker, defender)!))
            && WorldUnitLayers.CanAttack(state, attacker, u));
        if (target != null && !rules.AllowFriendlyFire
            && WorldDiplomacy.Allied(state, attacker.Owner, target.Owner)) return null;
        return target;
    }
}
