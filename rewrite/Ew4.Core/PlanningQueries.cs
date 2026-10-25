using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Ew4.Core;

// Each context belongs to an exact validated world, inside one synchronous scope.
internal sealed class PlanningQueries(WorldState state)
{
    private Dictionary<string, FactionState>? factions;
    private Dictionary<int, UnitState[]>? occupants;
    private Dictionary<string, UnitState>? units;
    private Dictionary<string, int[]>? targets;
    private JurisdictionQueries? jurisdictions;
    private readonly Dictionary<string, (MovementCell[] Cells, string Camp)> movement = new(StringComparer.Ordinal);
    private readonly Dictionary<WorldAiRules, AiQueries> ai = new(ReferenceComparer<WorldAiRules>.Instance);
    internal Dictionary<string, FactionState> Factions => factions ??= state.Factions.ToDictionary(f => f.Id, StringComparer.Ordinal);
    internal Dictionary<int, UnitState[]> Occupants => occupants ??= state.Units.GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.ToArray());
    internal Dictionary<string, UnitState> Units => units ??= state.Units.ToDictionary(u => u.Id, StringComparer.Ordinal);
    internal JurisdictionQueries Jurisdictions => jurisdictions ??= new(state);
    internal Dictionary<string, int[]> Targets
    {
        get
        {
            if (targets != null) return targets;
            var cells = state.Factions.ToDictionary(f => f.Id, _ => new HashSet<int>(), StringComparer.Ordinal);
            for (int i = 0; i < state.Cells.Count; i++)
                if (state.Cells[i].Owner is string owner) cells[owner].Add(i);
            foreach (var unit in state.Units) cells[unit.Owner].Add(unit.Cell);
            return targets = cells.ToDictionary(p => p.Key, p => p.Value.OrderBy(i => i).ToArray(), StringComparer.Ordinal);
        }
    }
    internal AiQueries Ai(WorldAiRules rules)
    {
        if (!ai.TryGetValue(rules, out var result)) ai.Add(rules, result = new());
        return result;
    }
    internal (MovementCell[] Cells, string Camp) Movement(string owner)
    {
        string key = state.DiplomaticRelations.Count == 0 ? "" : owner;
        if (!movement.TryGetValue(key, out var result))
        {
            var camps = state.Factions.ToDictionary(f => f.Id,
                f => state.DiplomaticRelations.Count == 0 ? f.Camp : WorldDiplomacy.Allied(state, owner, f.Id) ? "allied" : "other", StringComparer.Ordinal);
            var cells = state.Cells.Select((c, i) => {
                string? camp = Occupants.TryGetValue(i, out var units) ? camps[units[0].Owner] : null;
                return c.Terrain.OccupantCamp == camp ? c.Terrain : c.Terrain with { OccupantCamp = camp };
            }).ToArray();
            movement.Add(key, result = (cells, camps[owner]));
        }
        return (result.Cells, state.DiplomaticRelations.Count == 0 ? Factions[owner].Camp : result.Camp);
    }
    internal sealed class AiQueries
    {
        internal readonly Dictionary<UnitState, AiUnitFacts> Units = new(ReferenceComparer<UnitState>.Instance);
        internal readonly Dictionary<int, AiSiteFacts> Sites = new();
        internal readonly Dictionary<int, AiStrategyGoal> Goals = new();
    }
    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

internal sealed class JurisdictionQueries(WorldState state)
{
    private readonly Dictionary<int, int[]> cells = state.Jurisdictions!.CellCenters
        .Select((center, cell) => (center, cell)).Where(p => p.center >= 0)
        .GroupBy(p => p.center).ToDictionary(g => g.Key, g => g.Select(p => p.cell).ToArray());
    internal readonly HashSet<int> Detached = new(state.Jurisdictions!.DetachedCells);
    private readonly Dictionary<string, int[]> allowed = new(StringComparer.Ordinal);

    internal IReadOnlyList<int> Cells(int center) => cells.TryGetValue(center, out var result) ? result : Array.Empty<int>();
    internal IReadOnlyList<int> Allowed(string owner)
    {
        if (allowed.TryGetValue(owner, out var result)) return result;
        var centers = new HashSet<int>(cells.Keys.Where(c => state.Cells[c].Owner == owner));
        foreach (var edge in state.Jurisdictions!.Borders)
        {
            if (state.Cells[edge.First].Owner == owner) centers.Add(edge.Second);
            if (state.Cells[edge.Second].Owner == owner) centers.Add(edge.First);
        }
        allowed.Add(owner, result = centers.ToArray());
        return result;
    }
}
