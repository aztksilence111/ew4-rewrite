using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Ew4.Core;

// Only synchronous, built-in operations can reuse validated objects. No dynamic
// proof survives the call, and untrusted nested calls suspend the outer scope.
internal sealed class PlanningScope : IDisposable
{
    [ThreadStatic] private static PlanningScope? current;
    private readonly PlanningScope? previous;
    private readonly HashSet<object>? validated;
    private readonly ConditionalWeakTable<WorldState, PlanningQueries>? queries;
    private readonly HashSet<object>? projections;
    private readonly bool snapshots;
    private PlanningScope(bool enabled, bool snapshots = false)
    {
        previous = current;
        validated = enabled && (previous == null || previous.validated != null)
            ? previous?.validated ?? new(IdentityComparer.Instance) : null;
        queries = validated == null ? null : previous?.queries ?? new();
        projections = validated == null ? null : previous?.projections ?? new(IdentityComparer.Instance);
        this.snapshots = validated != null && (snapshots || previous?.snapshots == true);
        // A disabled subtree may invoke caller code that mutates shared input.
        // Invalidate ancestor proofs before entry, including exceptional exits.
        if (validated == null)
            for (var ancestor = previous; ancestor != null; ancestor = ancestor.previous)
            { ancestor.validated?.Clear(); ancestor.queries?.Clear(); ancestor.projections?.Clear(); }
        current = this;
    }
    private static bool CanTrust(RuleEventBus? extensions) => extensions == null
        || extensions.Scripts == null && extensions.Skills.Count == 0 && extensions.Statuses.Count == 0;
    internal static bool CanTrust(WorldMoveRules rules) => CanTrust(rules.Extensions) && rules.Victory.Scripts == null;
    internal static PlanningScope Begin(WorldAiRules rules) => new(CanTrust(rules.Movement));
    internal static PlanningScope Suspend() => new(false);
    internal static PlanningQueries? Queries(WorldState state) => current?.validated?.Contains(state) == true
        ? current.queries?.GetValue(state, value => new(value)) : null;
    internal static bool IsMovementProjection(WorldState state) => current?.projections?.Contains(state) == true;
    internal static bool FrozenSnapshots => current?.snapshots == true;
    internal static bool IsValidated(object value) => current?.validated?.Contains(value) == true;
    internal static T PreserveCopy<T>(T source, T copy) where T : class
    {
        if (IsValidated(source)) current!.validated!.Add(copy);
        return copy;
    }
    internal static PlanningScope Begin(WorldAiRules ai, WorldProductionRules production, WorldTurnRules turns,
        WorldBattleRules? battle = null, bool snapshots = false) => new(CanTrustStep(ai, production, turns, battle), snapshots);
    internal static bool CanTrustStep(WorldAiRules ai, WorldProductionRules production, WorldTurnRules turns,
        WorldBattleRules? battle = null) => CanTrust(ai.Movement) && CanTrust(production.Commanders?.Extensions)
            && CanTrust(turns.Extensions) && turns.Victory.Scripts == null
            && (battle == null || CanTrust(battle.Combat.Extensions) && battle.Victory.Scripts == null);

    internal static WorldState WithAiRandomState(WorldState state, ulong randomState)
    {
        if (state.AiRandomState == randomState) return state;
        var next = state with { AiRandomState = randomState };
        // No validated constraint depends on this unrestricted ulong. All other
        // fields are inherited here, rather than supplied by a caller.
        if (current?.validated?.Contains(state) == true)
        {
            current.validated.Add(next);
            // Spatial and scoring queries contain no RNG or command state.
            if (current.queries!.TryGetValue(state, out var queries)) current.queries.Add(next, queries);
        }
        return next;
    }
    internal static WorldState WithRevision(WorldState state, long revision)
    {
        if (revision == state.Revision) return state;
        var next = state with { Revision = revision };
        if (revision >= 0 && current?.validated?.Contains(state) == true) current.validated.Add(next);
        if (revision >= 0 && IsMovementProjection(state)) current!.projections!.Add(next);
        return next;
    }

    // Only this constructor can grant the movement proof. No arbitrary next-world
    // or caller-provided mutation list can bypass the full validator.
    internal static WorldState ProjectMovement(WorldState state, UnitState unit, int destination, int remaining,
        IReadOnlyList<CellState> cells, MovementOwnership? ownership = null)
    {
        if (destination < 0 || destination >= cells.Count)
            throw new RuleViolation("world.unit", "Invalid unit state, commander assignment or owner");
        var moved = unit with { Cell = destination, RemainingMovement = remaining,
            Combatant = unit.Combatant with { AtSea = cells[destination].Terrain.Sea, BuildingKind = cells[destination].BuildingKind } };
        var next = state with { Cells = cells, Units = Array.AsReadOnly(state.Units.Select(u => ReferenceEquals(u, unit) ? moved : u).ToArray()),
            Revision = checked(state.Revision + 1) };
        var proofs = current?.validated;
        if (proofs == null || !proofs.Contains(state) || !state.Units.Any(u => ReferenceEquals(u, unit))
            || cells.Count != state.Cells.Count)
        { next.Validate(); return next; }

        bool controlled = ownership != null && ReferenceEquals(ownership.Source, state) && ReferenceEquals(ownership.Cells, cells);
        foreach (int i in controlled ? ownership!.Changed : Enumerable.Range(0, cells.Count))
        {
            var old = state.Cells[i]; var cell = cells[i];
            if (ReferenceEquals(cell, old)) continue;
            // Capture code may change only ownership. Any future new write falls
            // back to all constraints until its dependencies are explicitly covered.
            if (cell == null || !controlled && cell with { Owner = old.Owner } != old)
            { next.Validate(); return next; }
            if (cell.Owner != null && !state.Factions.Any(f => f.Id == cell.Owner))
                throw new RuleViolation("world.cell", "Invalid cell state or owner");
        }
        if (destination < 0 || destination >= cells.Count || !cells[destination].Terrain.CanTraverse()
            || remaining < 0 || remaining > (unit.ExtensionMovementLimit ?? unit.Combatant.Profile.Movement))
            throw new RuleViolation("world.unit", "Invalid unit state, commander assignment or owner");
        var occupants = state.Units.Where(u => !ReferenceEquals(u, unit) && u.Cell == destination).ToArray();
        if (occupants.Length + 1 > state.Limits.UnitsPerCell)
            throw new RuleViolation("world.capacity", "Configured unit capacity exceeded");
        string camp = state.Factions.Single(f => f.Id == unit.Owner).Camp;
        if (occupants.Any(u => state.Factions.Single(f => f.Id == u.Owner).Camp != camp))
            throw new RuleViolation("world.occupants", "Opposing camps cannot share a cell");
        if (VerifyMovementProjections) next.ValidateUncached();
        proofs.Add(next);
        if (controlled) current!.projections!.Add(next);
        return next;
    }
    [ThreadStatic] internal static bool VerifyMovementProjections;
    internal static void Validate(object value, Action validate)
    {
        var scope = current;
        if (scope?.validated?.Contains(value) == true) return;
        validate();
        scope?.validated?.Add(value);
    }
    public void Dispose() { current = previous; }
    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        internal static readonly IdentityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
