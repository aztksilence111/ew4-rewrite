using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ExtensionEffectCondition
{
    public IReadOnlyList<string> SourceCategories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetCategories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceTerrains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetTerrains { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceBuildings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetBuildings { get; init; } = Array.Empty<string>();
    public bool? SourceInBuilding { get; init; }
    public bool? TargetInBuilding { get; init; }
    public int? MinDistance { get; init; }
    public int? MaxDistance { get; init; }

    public bool UsesLocation() => SourceTerrains.Count > 0 || TargetTerrains.Count > 0 || SourceBuildings.Count > 0
        || TargetBuildings.Count > 0 || SourceInBuilding.HasValue || TargetInBuilding.HasValue
        || MinDistance.HasValue || MaxDistance.HasValue;

    public void Validate()
    {
        var lists = new[] { SourceCategories, TargetCategories, SourceTerrains, TargetTerrains, SourceBuildings, TargetBuildings };
        if (lists.Any(list => list == null || list.Count > 64 || list.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 128)
            || list.Distinct(StringComparer.Ordinal).Count() != list.Count)
            || MinDistance < 0 || MaxDistance < 0 || MinDistance > MaxDistance)
            throw new RuleViolation("extension.condition", "Invalid category, terrain, building or distance condition");
    }

    public bool Matches(Combatant source, Combatant? target, int? distance, RuleUnitLocation? targetCell = null)
    {
        bool In(IReadOnlyList<string> values, string? value) => values.Count == 0 || value != null && values.Contains(value);
        bool Building(bool? expected, RuleUnitLocation? location) => !expected.HasValue
            || location != null && expected.Value == (location.Building != null);
        var targetSite = targetCell ?? target?.RuleLocation;
        return In(SourceCategories, source.Profile.Category) && In(TargetCategories, target?.Profile.Category)
            && In(SourceTerrains, source.RuleLocation?.Terrain) && In(TargetTerrains, targetSite?.Terrain)
            && In(SourceBuildings, source.RuleLocation?.Building) && In(TargetBuildings, targetSite?.Building)
            && Building(SourceInBuilding, source.RuleLocation) && Building(TargetInBuilding, targetSite)
            && (!MinDistance.HasValue || distance.HasValue && distance.Value >= MinDistance.Value)
            && (!MaxDistance.HasValue || distance.HasValue && distance.Value <= MaxDistance.Value);
    }
}
