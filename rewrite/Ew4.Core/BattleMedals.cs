using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public static class BattleMedals
{
    // Original a90fc rolls even below the damage threshold; building is the defender's tile.
    public static bool Drops(int damage, string? buildingKind, int buildingLevel, int roll)
    {
        if (damage < 0 || buildingLevel < 0 || roll is < 0 or >= 100)
            throw new RuleViolation("battle.medal_input", "Invalid medal drop input");
        long adjusted = roll + (long)buildingLevel * (buildingKind == "industry" ? 3 : buildingKind is "city" or "port" ? 2 : 0);
        return damage >= 20 && adjusted > (damage < 25 ? 95 : damage < 30 ? 91 : damage < 35 ? 87 : 82);
    }

    public static GameSession Collect(GameSession session, IReadOnlyList<WorldNotice> notices)
    {
        if (session.World.LocalMultiplayer != null) return session;
        long count = notices.Where(n => n.Kind == "battle.medal_dropped").Sum(n => n.Value);
        if (count == 0) return session;
        if (count < 0 || string.IsNullOrWhiteSpace(session.BattleId))
            throw new RuleViolation("battle.medal_identity", "Medal collection needs a battle identity");
        long total = checked(session.CollectedMedals + count);
        // A replayed earlier slot can only advance beyond the account's paid total for this battle.
        long paid = session.Account.CollectedMedalReceipts.GetValueOrDefault(session.BattleId);
        var receipts = session.Account.CollectedMedalReceipts.ToDictionary(p => p.Key, p => p.Value);
        receipts[session.BattleId] = Math.Max(paid, total);
        long grant = Math.Max(0, total - paid);
        return session with { CollectedMedals = total, Account = session.Account with {
            Revision = checked(session.Account.Revision + (grant > 0 ? 1 : 0)),
            Medals = checked(session.Account.Medals + grant), CollectedMedalReceipts = receipts } };
    }
}
