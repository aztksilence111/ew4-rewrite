using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ew4.Core;

// Only synchronous, built-in planning can reuse validated objects. No cache survives
// a public planning call, and script callbacks never enter this trusted scope.
internal sealed class PlanningScope : IDisposable
{
    [ThreadStatic] private static PlanningScope? current;
    private readonly PlanningScope? previous;
    private readonly HashSet<object> validated = new(IdentityComparer.Instance);
    private readonly bool owns;
    private PlanningScope(bool enabled)
    {
        previous = current;
        owns = enabled && current == null;
        if (owns) current = this;
    }
    internal static bool CanTrust(WorldMoveRules rules) => rules.Extensions == null
        || rules.Extensions.Scripts == null && rules.Extensions.Skills.Count == 0 && rules.Extensions.Statuses.Count == 0;
    internal static PlanningScope Begin(WorldAiRules rules) => new(CanTrust(rules.Movement));
    internal static void Validate(object value, Action validate)
    {
        var scope = current;
        if (scope?.validated.Contains(value) == true) return;
        validate();
        scope?.validated.Add(value);
    }
    public void Dispose() { if (owns) current = previous; }
    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        internal static readonly IdentityComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
