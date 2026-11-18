using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

// Optional author-owned watches. Tokens are explicit, stable author identifiers,
// never faction array offsets (opening handoffs can reorder that array).
public static class ScenarioBattleFacts
{
    public const string Prefix = "scenario/battle_facts/";
    private static long Token(WorldState world, string? faction) => faction != null
        && world.ExtensionState.TryGetValue(Prefix + "faction/" + faction, out long token) && token > 0 ? token : 0;

    public static WorldState Captured(WorldState before, WorldState after, string actor, IReadOnlyList<int> changed)
    {
        Dictionary<string, long>? facts = null;
        foreach (int cell in changed)
        {
            if (!before.ExtensionState.TryGetValue(Prefix + "watch/cell/" + cell, out long watched) || watched != 1
                || before.Cells[cell].Owner == after.Cells[cell].Owner || after.Cells[cell].Owner != actor) continue;
            facts ??= after.ExtensionState.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            string key = Prefix + "capture/" + cell + "/";
            facts[key + "from"] = Token(before, before.Cells[cell].Owner);
            facts[key + "to"] = Token(after, actor);
            facts[key + "round"] = after.Round;
            facts[key + "count"] = checked((facts.TryGetValue(key + "count", out long count) ? count : 0) + 1);
        }
        return facts == null ? after : after with { ExtensionState = facts };
    }

    internal static WorldState Destroyed(WorldState world, UnitState unit)
    {
        if (!world.ExtensionState.TryGetValue(Prefix + "watch/unit/" + unit.Id, out long watched) || watched != 1) return world;
        string key = Prefix + "destroyed/" + unit.Id + "/";
        if (world.ExtensionState.ContainsKey(key + "round")) return world;
        var facts = world.ExtensionState.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        facts[key + "owner"] = Token(world, unit.Owner);
        facts[key + "round"] = world.Round;
        facts[key + "cell"] = unit.Cell;
        return world with { ExtensionState = facts };
    }
}
