using Ew4.Core;

namespace Ew4.Client;

public sealed record MovementOwnerChange(int Cell, string? Before, string? After);
public sealed record MovementCheckpoint(string Unit, int Origin, int Destination, int Remaining, int[] Path,
    MovementOwnerChange[] Owners, UnitState[] Removed, string[] Defeated, string[] Withdrawn, int PlayerActionRound = 0);
public sealed record MovementHistoryState(int Round, string Faction, MovementCheckpoint[] Moves, string[] Attackers);

// Only movement is reversible. Purchases, training, combat and RNG are never restored from a world snapshot.
public sealed class MovementHistory
{
    private readonly List<MovementCheckpoint> moves = [];
    private readonly HashSet<string> attackers = new(StringComparer.Ordinal);
    private int round;
    private string faction = "";
    public void Clear() { moves.Clear(); attackers.Clear(); round = 0; faction = ""; }
    public void Reset(WorldState world) { moves.Clear(); attackers.Clear(); round = world.Round; faction = world.ActiveFaction; }
    public void Observe(WorldState world) { if (world.Round != round || world.ActiveFaction != faction) Reset(world); }
    public void Invalidate(WorldState world) { Observe(world); moves.Clear(); }
    public void Attack(string unit) => attackers.Add(unit);
    public void Forget(string unit) { moves.RemoveAll(m => m.Unit == unit || m.Removed.Any(u => u.Id == unit)); attackers.Remove(unit); }
    public MovementHistoryState Save() => new(round, faction, moves.ToArray(), attackers.ToArray());
    public void Load(MovementHistoryState? saved, WorldState world)
    {
        Reset(world);
        if (saved == null || saved.Round != round || saved.Faction != faction) return;
        moves.AddRange(saved.Moves); attackers.UnionWith(saved.Attackers);
    }
    public void Record(WorldState before, WorldState after, string unitId, int[] path, IReadOnlyList<WorldNotice>? notices = null)
    {
        Observe(before);
        if (EventTriggered(before, after, notices)) { Invalidate(after); return; }
        if (before.Cells.Where((c, i) => c.Owner != after.Cells[i].Owner && after.Cells[i].Owner == before.ActiveFaction)
            .Any(c => c.Objective || c.BlocksTerritoryExpansion))
        {
            moves.RemoveAll(m => m.Unit == unitId);
            return;
        }
        var unit = before.Units.Single(u => u.Id == unitId);
        var moved = after.Units.Single(u => u.Id == unitId);
        moves.Add(new(unitId, unit.Cell, moved.Cell, unit.RemainingMovement, path,
            before.Cells.Select((c,i) => new MovementOwnerChange(i,c.Owner,after.Cells[i].Owner)).Where(c => c.Before != c.After).ToArray(),
            before.Units.Where(u => !after.Units.Any(n => n.Id == u.Id)).ToArray(),
            before.Factions.Where(f => !f.Defeated && after.Factions.Single(n => n.Id == f.Id).Defeated).Select(f => f.Id).ToArray(),
            before.Commanders.Where(c => !c.Withdrawn && after.Commanders.Single(n => n.Id == c.Id).Withdrawn).Select(c => c.Id).ToArray(), PlayerAi.Order(unit).ActionRound));
    }
    public MovementCheckpoint? Available(WorldState world, string? unit = null)
    {
        if (world.Round != round || world.ActiveFaction != faction || world.PendingDialogue != null || world.Phase != TurnPhase.Active) return null;
        return moves.LastOrDefault(m => (unit == null || m.Unit == unit) && !attackers.Contains(m.Unit)
            && world.Units.Any(u => u.Id == m.Unit && u.Owner == faction && u.Cell == m.Destination)
            && world.Units.Count(u => u.Cell == m.Origin) < world.Limits.UnitsPerCell
            && m.Removed.All(old => !world.Units.Any(u => u.Id == old.Id || u.Cell == old.Cell))
            && m.Owners.All(c => c.Cell >= 0 && c.Cell < world.Cells.Count && world.Cells[c.Cell].Owner == c.After
                && !(c.After == faction && (world.Cells[c.Cell].Objective || world.Cells[c.Cell].BlocksTerritoryExpansion))
                && !world.Units.Any(u => u.Id != m.Unit && u.Cell == c.Cell && u.Owner != c.Before)));
    }
    internal static bool EventTriggered(WorldState before, WorldState after, IReadOnlyList<WorldNotice>? notices)
        => notices?.Any(n => n.Kind is "event.triggered" or "scenario.triggered" or "scenario.choice" or "capital.captured") == true
            || !before.TriggeredEvents.SequenceEqual(after.TriggeredEvents)
            || !before.TriggeredScenarioEvents.SequenceEqual(after.TriggeredScenarioEvents)
            || before.ScenarioEventProgress.Count != after.ScenarioEventProgress.Count
            || before.ScenarioEventProgress.Any(p => !after.ScenarioEventProgress.TryGetValue(p.Key, out var progress) || progress != p.Value);
    public (WorldChange Change, int[] Path) Undo(WorldState world, string? unit = null)
    {
        var entry = Available(world, unit) ?? throw new RuleViolation("client.undo_move", "This unit has no available movement to undo");
        var owners = entry.Owners.ToDictionary(c => c.Cell);
        var next = world with
        {
            Revision = checked(world.Revision + 1),
            Units = world.Units.Select(u => u.Id != entry.Unit ? u : u with
            {
                Cell = entry.Origin, RemainingMovement = entry.Remaining,
                PlayerControl = PlayerAi.Order(u) with { ActionRound = entry.PlayerActionRound },
                Combatant = u.Combatant with { AtSea = world.Cells[entry.Origin].Terrain.Sea, BuildingKind = world.Cells[entry.Origin].BuildingKind }
            }).Concat(entry.Removed).ToArray(),
            Cells = world.Cells.Select((c,i) => owners.TryGetValue(i,out var owner) ? c with { Owner = owner.Before } : c).ToArray(),
            Factions = world.Factions.Select(f => entry.Defeated.Contains(f.Id) ? f with { Defeated = false } : f).ToArray(),
            Commanders = world.Commanders.Select(c => entry.Withdrawn.Contains(c.Id) ? c with { Withdrawn = false } : c).ToArray(),
            Phase = world.Phase == TurnPhase.Finished ? TurnPhase.Active : world.Phase,
            Outcome = world.Phase == TurnPhase.Finished ? BattleOutcome.Continue : world.Outcome
        };
        next.Validate();
        moves.Remove(entry);
        return (new(next, new[] { new WorldNotice("unit.move_undone", entry.Unit, entry.Origin) }), entry.Path.Reverse().ToArray());
    }
}
