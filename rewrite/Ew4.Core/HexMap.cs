using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed class HexMap
{
    public int Width { get; }
    public int Height { get; }
    public int Count { get; }
    public HexMap(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new RuleViolation("map.dimensions", "Map dimensions must be positive");
        Width = width; Height = height; Count = checked(width * height);
    }
    public int Index(int column, int row) =>
        column >= 0 && column < Width && row >= 0 && row < Height ? column + row * Width : -1;
    public void Check(int cell)
    {
        if (cell < 0 || cell >= Count) throw new RuleViolation("map.cell", $"Cell outside map: {cell}");
    }
    public int Neighbor(int cell, int direction)
    {
        Check(cell);
        if (direction < 0 || direction >= 6) throw new ArgumentOutOfRangeException(nameof(direction));
        int x = cell % Width, y = cell / Width, odd = y % 2;
        return direction switch
        {
            0 => Index(x + 1, y), 1 => Index(x + odd, y + 1), 2 => Index(x + odd - 1, y + 1),
            3 => Index(x - 1, y), 4 => Index(x + odd - 1, y - 1), _ => Index(x + odd, y - 1)
        };
    }
    public int Distance(int from, int to)
    {
        Check(from); Check(to);
        int r1 = from / Width, r2 = to / Width;
        int q1 = from % Width - (r1 - r1 % 2) / 2, q2 = to % Width - (r2 - r2 % 2) / 2;
        int dq = q2 - q1, dr = r2 - r1;
        return Math.Max(Math.Max(Math.Abs(dq), Math.Abs(dr)), Math.Abs(dq + dr));
    }

    public IEnumerable<int> Within(int center, int radius, bool includeCenter = false)
    {
        Check(center);
        if (radius < 0) throw new RuleViolation("map.radius", "Neighborhood radius cannot be negative");
        int x = center % Width, y = center / Width;
        int left = (int)Math.Max(0L, (long)x - radius), right = (int)Math.Min(Width - 1L, (long)x + radius);
        int top = (int)Math.Max(0L, (long)y - radius), bottom = (int)Math.Min(Height - 1L, (long)y + radius);
        for (int row = top; row <= bottom; row++)
            for (int column = left; column <= right; column++)
            {
                int cell = Index(column, row);
                if ((includeCenter || cell != center) && Distance(center, cell) <= radius) yield return cell;
            }
    }
}

public sealed record MovementCell
{
    public bool Active { get; init; } = true;
    public bool Sea { get; init; }
    public int TerrainCost { get; init; } = 3;
    public byte BlockedSeaEdges { get; init; }
    public byte RiverEdges { get; init; }
    public byte BridgeEdges { get; init; }
    public string? OccupantCamp { get; init; }
}

public sealed record MovementActor
{
    public string Category { get; init; } = "infantry";
    public string Camp { get; init; } = "";
    public bool Transport { get; init; }
    public bool Geography { get; init; }
    public int FullBudget { get; init; }
    public int Remaining { get; init; }
}

public sealed record MovementRules
{
    public int BaseLandCost { get; init; } = 3;
    public int SeaCost { get; init; } = 1;
    public bool BlockEnemyOccupied { get; init; } = true;
    public bool AllowOverdrawnEntry { get; init; }
}

public sealed record ReachableCell(int Cell, int Remaining, IReadOnlyList<int> Path);

public static class Movement
{
    // A necessary (not sufficient) reachability filter. Union either legal edge
    // direction so one-way coasts can never cause a reachable target to be rejected.
    internal static int[] Components(HexMap map, IReadOnlyList<MovementCell> cells, MovementActor actor, MovementRules rules)
    {
        var labels = Enumerable.Repeat(-1, map.Count).ToArray();
        var queue = new Queue<int>();
        int component = 0;
        for (int start = 0; start < map.Count; start++)
        {
            if (labels[start] >= 0 || !cells[start].Active) continue;
            labels[start] = component; queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int from = queue.Dequeue();
                for (int direction = 0; direction < 6; direction++)
                {
                    int to = map.Neighbor(from, direction);
                    if (to < 0 || !cells[to].Active || labels[to] >= 0) continue;
                    int? forward = EntryCost(cells[from], cells[to], (direction + 3) % 6, actor, rules);
                    int? reverse = EntryCost(cells[to], cells[from], direction, actor, rules);
                    bool Legal(int? cost) => cost.HasValue && (cost.Value <= actor.FullBudget || rules.AllowOverdrawnEntry);
                    if (!Legal(forward) && !Legal(reverse)) continue;
                    labels[to] = component; queue.Enqueue(to);
                }
            }
            component++;
        }
        return labels;
    }
    // Reverse Dijkstra measures long routes without replacing the actor's per-turn budget.
    public static IReadOnlyDictionary<int, long> DistancesTo(HexMap map, IReadOnlyList<MovementCell> cells,
        IReadOnlyCollection<int> destinations, MovementActor actor, MovementRules? configuration = null)
    {
        if (cells.Count != map.Count || actor.FullBudget < 1 || actor.Remaining < 0 || actor.Remaining > actor.FullBudget)
            throw new RuleViolation("movement.state", "Map/actor movement state is inconsistent");
        var rules = configuration ?? new();
        var distances = new Dictionary<int, long>();
        var queue = new SortedSet<(long Cost, int Cell)>();
        foreach (int cell in destinations)
        {
            map.Check(cell);
            if (!cells[cell].Active) continue;
            distances[cell] = 0;
            queue.Add((0, cell));
        }
        while (queue.Count > 0)
        {
            var current = queue.Min;
            queue.Remove(current);
            if (distances[current.Cell] != current.Cost) continue;
            for (int direction = 0; direction < 6; direction++)
            {
                int previous = map.Neighbor(current.Cell, direction);
                if (previous < 0 || !cells[previous].Active) continue;
                int? cost = EntryCost(cells[previous], cells[current.Cell], direction, actor, rules);
                if (!cost.HasValue || cost.Value > actor.FullBudget && !rules.AllowOverdrawnEntry) continue;
                long total = checked(current.Cost + cost.Value);
                if (distances.TryGetValue(previous, out long old) && old <= total) continue;
                distances[previous] = total;
                queue.Add((total, previous));
            }
        }
        return distances;
    }

    public static int? EntryCost(MovementCell from, MovementCell to, int enteringEdge, MovementActor actor, MovementRules rules)
    {
        if (enteringEdge < 0 || enteringEdge >= 6 || rules.BaseLandCost <= 0 || rules.SeaCost <= 0 || to.TerrainCost <= 0)
            throw new RuleViolation("movement.configuration", "Invalid edge or movement cost");
        if (!to.Active || (rules.BlockEnemyOccupied && to.OccupantCamp != null && to.OccupantCamp != actor.Camp)) return null;
        int cost;
        if (to.Sea)
        {
            if ((to.BlockedSeaEdges & (1 << enteringEdge)) != 0 || (actor.Category != "naval" && !actor.Transport)) return null;
            cost = rules.SeaCost;
        }
        else
        {
            if (actor.Category == "naval") return null;
            cost = actor.Category == "infantry" || actor.Geography ? rules.BaseLandCost : to.TerrainCost;
        }
        if ((actor.Transport && from.Sea != to.Sea)
            || ((to.RiverEdges & (1 << enteringEdge)) != 0 && (to.BridgeEdges & (1 << enteringEdge)) == 0))
            cost = actor.FullBudget;
        return cost;
    }

    public static IReadOnlyDictionary<int, ReachableCell> Reachable(HexMap map, IReadOnlyList<MovementCell> cells,
        int origin, MovementActor actor, MovementRules? configuration = null)
    {
        map.Check(origin);
        if (cells.Count != map.Count || actor.FullBudget < 0 || actor.Remaining < 0 || actor.Remaining > actor.FullBudget)
            throw new RuleViolation("movement.state", "Map/actor movement state is inconsistent");
        var rules = configuration ?? new MovementRules();
        var remaining = new Dictionary<int, int> { [origin] = actor.Remaining };
        var parents = new Dictionary<int, int>();
        var queue = new SortedSet<(int NegativeRemaining, int Cell)> { (-actor.Remaining, origin) };
        while (queue.Count > 0)
        {
            var current = queue.Min;
            queue.Remove(current);
            int budget = -current.NegativeRemaining;
            if (remaining[current.Cell] != budget || budget <= 0) continue;
            for (int direction = 0; direction < 6; direction++)
            {
                int next = map.Neighbor(current.Cell, direction);
                if (next < 0) continue;
                int? cost = EntryCost(cells[current.Cell], cells[next], (direction + 3) % 6, actor, rules);
                if (!cost.HasValue || (cost.Value > budget && !rules.AllowOverdrawnEntry)) continue;
                int rest = checked(budget - cost.Value);
                if (remaining.TryGetValue(next, out int previous) && previous >= rest) continue;
                remaining[next] = rest;
                parents[next] = current.Cell;
                queue.Add((-rest, next));
            }
        }
        // Store predecessor links during search. Materialize paths only on request.
        return new Reachability(remaining, parents, origin);
    }

    private sealed class Reachability : IReadOnlyDictionary<int, ReachableCell>
    {
        private readonly Dictionary<int, int> remaining;
        private readonly Dictionary<int, int> parents;
        private readonly int origin;
        public Reachability(Dictionary<int, int> remaining, Dictionary<int, int> parents, int origin)
        { this.remaining = remaining; this.parents = parents; this.origin = origin; }
        public int Count => remaining.Count;
        public IEnumerable<int> Keys => remaining.Keys;
        public IEnumerable<ReachableCell> Values => Keys.Select(key => this[key]);
        public ReachableCell this[int key]
        {
            get
            {
                int budget = remaining[key];
                var path = new List<int> { key };
                while (key != origin) { key = parents[key]; path.Add(key); }
                path.Reverse();
                return new ReachableCell(path[path.Count - 1], budget, path);
            }
        }
        public bool ContainsKey(int key) => remaining.ContainsKey(key);
        public bool TryGetValue(int key, out ReachableCell value)
        {
            if (!remaining.ContainsKey(key)) { value = null!; return false; }
            value = this[key]; return true;
        }
        public IEnumerator<KeyValuePair<int, ReachableCell>> GetEnumerator() =>
            Keys.Select(key => new KeyValuePair<int, ReachableCell>(key, this[key])).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
