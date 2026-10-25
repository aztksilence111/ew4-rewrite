using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Ew4.Core;

internal static class FrozenAiTacticalRules
{
    private static readonly ConditionalWeakTable<AiTacticalRules, object> certificates = new();
    internal static bool IsValidated(AiTacticalRules rules) => certificates.TryGetValue(rules, out _);

    internal static AiTacticalRules Copy(AiTacticalRules source)
    {
        if (IsValidated(source)) return source;
        var copy = source with
        {
            Buildings = new ReadOnlyDictionary<string, AiBuildingValue>(source.Buildings.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)),
            Installations = new ReadOnlyDictionary<string, long>(source.Installations.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)),
            Priorities = new ReadOnlyDictionary<int, long>(source.Priorities.ToDictionary(p => p.Key, p => p.Value)),
            Grades = new ReadOnlyDictionary<string, float>(source.Grades.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)),
            Modifiers = Array.AsReadOnly(source.Modifiers.Select(modifier => modifier with
            {
                AttackerCategories = Array.AsReadOnly(modifier.AttackerCategories.ToArray()),
                DefenderCategories = Array.AsReadOnly(modifier.DefenderCategories.ToArray()),
                ExcludedDefenderCategories = Array.AsReadOnly(modifier.ExcludedDefenderCategories.ToArray())
            }).ToArray())
        };
        copy.Validate();
        certificates.Add(copy, new object());
        return copy;
    }
}
