using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record UnitLayerProfile
{
    public string Layer { get; init; } = "surface";
    public IReadOnlyList<string>? Occupies { get; init; }
    public IReadOnlyList<string> AttackLayers { get; init; } = new[] { "surface" };
    public IReadOnlyList<string>? CounterattackLayers { get; init; }
}

public sealed record UnitLayerRules
{
    public bool Enabled { get; init; }
    public int SurfaceCapacity { get; init; } = 1;
    public int AirCapacity { get; init; } = 1;
    public IReadOnlyDictionary<string, UnitLayerProfile> Profiles { get; init; } = new Dictionary<string, UnitLayerProfile>();

    public void Validate()
    {
        static bool Layers(IReadOnlyList<string>? layers) => layers != null && layers.Count <= 2
            && layers.All(l => l is "surface" or "air") && layers.Distinct(StringComparer.Ordinal).Count() == layers.Count;
        if (SurfaceCapacity is < 1 or > 32 || AirCapacity is < 1 or > 32 || Profiles == null
            || Profiles.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || p.Value.Layer is not ("surface" or "air")
                || !Layers(p.Value.AttackLayers) || p.Value.CounterattackLayers != null && !Layers(p.Value.CounterattackLayers)))
            throw new RuleViolation("layers.configuration", "Invalid unit layers, capacities or target layers");
        if (Profiles.Values.Any(p => p.Occupies != null && (!Layers(p.Occupies) || !p.Occupies.Contains(p.Layer))))
            throw new RuleViolation("layers.configuration", "Occupied layers must include the primary layer");
    }

    public UnitLayerRules Copy() => this with { Profiles = Profiles.ToDictionary(p => p.Key, p => p.Value with {
        Occupies = p.Value.Occupies?.ToArray(), AttackLayers = p.Value.AttackLayers.ToArray(), CounterattackLayers = p.Value.CounterattackLayers?.ToArray()
    }, StringComparer.Ordinal) };
}

public static class WorldUnitLayers
{
    public static bool Enabled(WorldState world) => world.UnitLayers?.Enabled == true;
    public static string Layer(WorldState world, UnitProfile profile) => Enabled(world)
        && world.UnitLayers!.Profiles.TryGetValue(profile.Id, out var policy) ? policy.Layer : "surface";
    public static bool Air(WorldState world, UnitState unit) => Air(world, unit.Combatant.Profile);
    public static bool Air(WorldState world, UnitProfile profile) => Layer(world, profile) == "air";
    public static IReadOnlyList<string> Occupies(WorldState world, UnitProfile profile) => Enabled(world)
        && world.UnitLayers!.Profiles.TryGetValue(profile.Id, out var policy) && policy.Occupies != null ? policy.Occupies : new[] { Layer(world, profile) };
    public static bool Composite(WorldState world, UnitProfile profile) => Occupies(world, profile).Count > 1;
    public static bool Same(WorldState world, UnitState first, UnitState second) => Occupies(world, first.Combatant.Profile).Intersect(Occupies(world, second.Combatant.Profile)).Any();
    public static IEnumerable<UnitState> Surface(WorldState world) => world.Units.Where(u => !Air(world, u));
    public static bool ValidTerrain(WorldState world, UnitProfile profile, int cell) => cell >= 0 && cell < world.Cells.Count
        && (Air(world, profile) ? world.Cells[cell].Terrain.Active : world.Cells[cell].Terrain.CanTraverse());
    public static int Capacity(WorldState world, UnitProfile profile) => !Enabled(world) ? world.Limits.UnitsPerCell
        : Air(world, profile) ? world.UnitLayers!.AirCapacity : world.UnitLayers!.SurfaceCapacity;
    public static int Capacity(WorldState world, string layer) => !Enabled(world) ? world.Limits.UnitsPerCell
        : layer == "air" ? world.UnitLayers!.AirCapacity : world.UnitLayers!.SurfaceCapacity;
    public static bool HasSpace(WorldState world, UnitProfile profile, string owner, int cell, string? except = null)
    {
        string camp = world.Factions.Single(f => f.Id == owner).Camp;
        foreach (string layer in Occupies(world, profile))
        {
            var occupants = world.Units.Where(u => u.Cell == cell && u.Id != except && Occupies(world, u.Combatant.Profile).Contains(layer)).ToArray();
            if (occupants.Length >= Capacity(world, layer) || occupants.Any(u => world.Factions.Single(f => f.Id == u.Owner).Camp != camp)) return false;
        }
        return true;
    }
    public static bool CanAttack(WorldState world, UnitState source, UnitState target, bool counterattack = false)
    {
        return !Enabled(world) || AttackLayer(world, source, target, counterattack) != null;
    }
    public static string? AttackLayer(WorldState world, UnitState source, UnitState target, bool counterattack = false)
    {
        var layers = world.UnitLayers?.Profiles.TryGetValue(source.Combatant.Profile.Id, out var policy) == true
            ? counterattack ? policy.CounterattackLayers ?? policy.AttackLayers : policy.AttackLayers : new[] { "surface" };
        bool sourceAtSea = Composite(world, source.Combatant.Profile) && world.Cells[source.Cell].Terrain.Sea;
        bool targetAtSea = Composite(world, target.Combatant.Profile) && world.Cells[target.Cell].Terrain.Sea;
        return layers.FirstOrDefault(layer => Occupies(world, target.Combatant.Profile).Contains(layer)
            && !(layer == "surface" && (sourceAtSea || targetAtSea)));
    }
}
