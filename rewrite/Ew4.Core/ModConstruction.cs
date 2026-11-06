using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ModBuildingLevel
{
    public int ConstructionTurns { get; init; }
    public Resources Income { get; init; } = new(0, 0, 0);
    public Resources UpgradeCost { get; init; } = new(0, 0, 0);
    public int Healing { get; init; }
    public int DefensePercent { get; init; }
    public string Image { get; init; } = "";
}
public sealed record ModBuildingRecruit
{
    public IReadOnlyList<string>? BuildingKinds { get; init; }
    public string Id { get; init; } = "";
    public string Profile { get; init; } = "";
    public Resources Cost { get; init; } = new(0, 0, 0);
    public int MinimumLevel { get; init; } = 1;
    public IReadOnlyList<string> Scenarios { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Countries { get; init; } = Array.Empty<string>();
}
public sealed record ModBuildingDefinition
{
    public int RecruitmentPerTurn { get; init; }
    public bool BuilderKeepsMovement { get; init; }
    public string Name { get; init; } = "";
    public Resources Cost { get; init; } = new(0, 0, 0);
    public IReadOnlyList<ModBuildingLevel> Levels { get; init; } = Array.Empty<ModBuildingLevel>();
    public long AiValue { get; init; } = 60;
    public long AiValuePerLevel { get; init; } = 10;
    public long AiUpgradeWeight { get; init; } = 3;
    public IReadOnlyList<ModBuildingRecruit> Recruitment { get; init; } = Array.Empty<ModBuildingRecruit>();
    public IReadOnlyList<string> Scenarios { get; init; } = Array.Empty<string>();
}
public sealed record ModConstructionRules
{
    public IReadOnlyList<ModBuildingRecruit>? Recruitment { get; init; }
    public bool Enabled { get; init; }
    public IReadOnlyList<string> BuilderProfiles { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, ModBuildingDefinition> Buildings { get; init; } = new Dictionary<string, ModBuildingDefinition>();
    public void Validate()
    {
        if (BuilderProfiles == null || Buildings == null || BuilderProfiles.Any(string.IsNullOrWhiteSpace)
            || BuilderProfiles.Distinct(StringComparer.Ordinal).Count() != BuilderProfiles.Count)
            throw new RuleViolation("construction.configuration", "Invalid builder profiles");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var offer in Recruitment ?? Array.Empty<ModBuildingRecruit>())
        {
            if (offer == null || string.IsNullOrWhiteSpace(offer.Id) || !ids.Add(offer.Id) || string.IsNullOrWhiteSpace(offer.Profile)
                || offer.BuildingKinds == null || offer.BuildingKinds.Count == 0 || offer.BuildingKinds.Any(string.IsNullOrWhiteSpace)
                || offer.Cost == null || offer.MinimumLevel < 1 || offer.Countries == null || offer.Scenarios == null
                || offer.Countries.Any(string.IsNullOrWhiteSpace) || offer.Scenarios.Any(string.IsNullOrWhiteSpace))
                throw new RuleViolation("construction.configuration", "Invalid site recruitment");
            offer.Cost.Validate();
        }
        foreach (var pair in Buildings)
        {
            var b = pair.Value;
            if (string.IsNullOrWhiteSpace(pair.Key) || new[] { "city", "industry", "stable", "port", "farmland" }.Contains(pair.Key)
                || b == null || string.IsNullOrWhiteSpace(b.Name) || b.Levels == null || b.Levels.Count is < 1 or > 32
                || b.Recruitment == null || b.Scenarios == null || b.Scenarios.Any(string.IsNullOrWhiteSpace)
                || b.AiValue < 0 || b.AiValuePerLevel < 0 || b.AiUpgradeWeight < 0 || b.RecruitmentPerTurn < 0)
                throw new RuleViolation("construction.configuration", "Invalid custom building definition");
            if (b.Cost == null) throw new RuleViolation("construction.configuration", "Missing construction cost");
            b.Cost.Validate();
            foreach (var level in b.Levels)
            {
                if (level == null || level.Income == null || level.UpgradeCost == null || level.Healing < 0 || level.ConstructionTurns < 0 || level.DefensePercent is < 0 or > 100 || string.IsNullOrWhiteSpace(level.Image))
                    throw new RuleViolation("construction.configuration", "Invalid building level");
                level.Income.Validate(); level.UpgradeCost.Validate();
            }
            foreach (var offer in b.Recruitment)
            {
                if (offer == null || offer.Cost == null || string.IsNullOrWhiteSpace(offer.Id) || !ids.Add(offer.Id) || string.IsNullOrWhiteSpace(offer.Profile)
                    || offer.MinimumLevel < 1 || offer.MinimumLevel > b.Levels.Count || offer.Countries == null || offer.Scenarios == null
                    || offer.Countries.Any(string.IsNullOrWhiteSpace) || offer.Scenarios.Any(string.IsNullOrWhiteSpace))
                    throw new RuleViolation("construction.configuration", "Invalid custom recruitment");
                offer.Cost.Validate();
            }
        }
    }
    public ModConstructionRules Copy() => this with { Recruitment = Recruitment?.Select(r => r with { BuildingKinds = r.BuildingKinds?.ToArray(), Scenarios = r.Scenarios.ToArray(), Countries = r.Countries.ToArray() }).ToArray(), BuilderProfiles = BuilderProfiles.ToArray(), Buildings = Buildings.ToDictionary(p => p.Key,
        p => p.Value with { Scenarios = p.Value.Scenarios.ToArray(), Levels = p.Value.Levels.Select(l => l with { }).ToArray(),
            Recruitment = p.Value.Recruitment.Select(r => r with { BuildingKinds = r.BuildingKinds?.ToArray(), Scenarios = r.Scenarios.ToArray(), Countries = r.Countries.ToArray() }).ToArray() }, StringComparer.Ordinal) };
}

public static class WorldConstruction
{
    public static bool Builder(WorldState world, UnitState unit) => world.Construction?.Enabled != true
        || world.Construction.BuilderProfiles.Contains(unit.Combatant.Profile.Id);
    public static bool Available(WorldState world, ModBuildingDefinition building) => building.Scenarios.Count == 0 || building.Scenarios.Contains(world.ScenarioId);
    public static UnitState? FortBuilder(WorldState world, int cell, string owner) => world.Units
        .Where(u => u.Owner == owner && Builder(world, u) && !WorldUnitLayers.Air(world, u) && !u.Acted && u.Combatant.ConstructionTurns == 0
            && u.Morale.Effective != -3 && !world.Cells[u.Cell].Terrain.Sea && new HexMap(world.Width, world.Height).Distance(u.Cell, cell) == 1)
        .OrderBy(u => u.Id, StringComparer.Ordinal).FirstOrDefault();
    public static bool CanBuild(WorldState world, string unitId, string kind, bool checkResources = true, bool checkAction = true)
    {
        var unit = world.Units.SingleOrDefault(u => u.Id == unitId);
        if (world.Construction?.Enabled != true || !world.Construction.Buildings.TryGetValue(kind, out var building) || !Available(world, building)
            || unit == null || unit.Owner != world.ActiveFaction || !Builder(world, unit) || WorldUnitLayers.Air(world, unit)
            || checkAction && unit.Acted || unit.Combatant.ConstructionTurns > 0 || unit.Morale.Effective == -3
            || world.Phase != TurnPhase.Active || world.PendingDialogue != null) return false;
        var cell = world.Cells[unit.Cell];
        var faction = world.Factions.Single(f => f.Id == unit.Owner);
        return !faction.Defeated && cell.Owner == unit.Owner && cell.Terrain.Active && cell.Terrain.CanTraverse() && !cell.Terrain.Sea
            && cell.BuildingKind == null && cell.InstallationKind == null
            && (!checkResources || faction.Resources.Money >= building.Cost.Money && faction.Resources.Industry >= building.Cost.Industry && faction.Resources.Food >= building.Cost.Food);
    }
    public static WorldChange Build(WorldState world, long revision, string unitId, string kind)
    {
        var faction = WorldActions.Ready(world, revision);
        if (!CanBuild(world, unitId, kind)) throw new RuleViolation("construction.unavailable", "Only configured engineers can construct this building at the selected site");
        var unit = world.Units.Single(u => u.Id == unitId);
        var cost = world.Construction!.Buildings[kind].Cost;
        var next = (world with {
            Factions = world.Factions.Select(f => f.Id == faction.Id ? f with { Resources = new(f.Resources.Money-cost.Money, f.Resources.Industry-cost.Industry, f.Resources.Food-cost.Food) } : f).ToArray(),
            Units = world.Units.Select(u => u.Id == unit.Id ? u with { Acted = true,
                RemainingMovement = world.Construction!.Buildings[kind].BuilderKeepsMovement ? u.RemainingMovement : 0 } : u).ToArray(),
            Cells = world.Cells.Select((c, i) => i == unit.Cell ? c with { BuildingKind = kind, BuildingLevel = 1,
                BuildingConstructionTurns = world.Construction!.Buildings[kind].Levels[0].ConstructionTurns } : c).ToArray(),
            Revision = checked(world.Revision + 1)
        }).Copy();
        next.Validate();
        return new(next, new[] { new WorldNotice("building.constructed", unitId, unit.Cell) });
    }
}
