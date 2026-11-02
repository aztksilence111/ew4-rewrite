using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ew4.Core;

public sealed record ScenarioUnitTemplate(Combatant Combatant, UnitSupport Support, int? MovementBudget = null);
public sealed record ScenarioCommanderTemplate
{
    public Commander Definition { get; init; } = new();
    public CommanderRuntime Runtime { get; init; } = new();
    public IReadOnlyDictionary<string, Dictionary<string, double>> Skills { get; init; } = new Dictionary<string, Dictionary<string, double>>();
    public IReadOnlyDictionary<int, int> TitleHealing { get; init; } = new Dictionary<int, int>();
    public RuleEventBus? Extensions { get; init; }

    public WorldCommanderRules Rules(string instance) => new()
    {
        Definitions = new Dictionary<string, Commander> { [instance] = Definition with { Id = instance } },
        LegacySkills = LegacySkillParameters.FromOverrides(Skills), TitleHealing = TitleHealing, Extensions = Extensions
    };
}

public static class ScenarioArmies
{
    public static bool KnownUnit(WorldState world, string id) => world.Units.Any(u => u.Id == id)
        || Effects(world).Any(e => e.Type == "unit_spawn" && e.Unit != null && Declared(e.Unit, id));
    public static bool KnownCommander(WorldState world, string id) => world.Commanders.Any(c => c.Id == id)
        || Effects(world).Any(e => e.Type is "unit_spawn" or "commander_assign" && e.Commander != null && Declared(e.Commander, id));

    public static void ValidateEffect(ScenarioEffect e)
    {
        bool army = e.Type is "unit_spawn" or "unit_remove" or "commander_assign" or "commander_detach";
        if (army && (string.IsNullOrWhiteSpace(e.Unit) || e.Script != null)
            || army && (!StoryValidation.Id(e.Unit!.Replace("{occurrence}", "1", StringComparison.Ordinal))
                || e.Commander != null && !StoryValidation.Id(e.Commander.Replace("{occurrence}", "1", StringComparison.Ordinal)))
            || e.Type == "unit_spawn" && (e.Cells.Count == 0 || e.Cells.Distinct().Count() != e.Cells.Count || e.Profile == null && e.UnitTemplate == null)
            || e.Type is "commander_assign" or "commander_detach" && string.IsNullOrWhiteSpace(e.Commander)
            || e.Type is "unit_spawn" or "commander_assign" && (e.CommanderDefinition != null && e.Commander == null || e.CommanderDefinition < 0)
            || e.Type is not ("unit_spawn" or "commander_assign" or "commander_detach") && (e.Commander != null || e.CommanderDefinition != null || e.CommanderTemplate != null)
            || e.Type != "unit_spawn" && (e.Profile != null || e.UnitTemplate != null || e.Health != null || e.Experience != 0 || e.Cost != null)
            || e.Type is not ("unit_remove" or "commander_detach") && e.Disposition != null
            || e.Disposition != null && e.Disposition is not ("available" or "withdrawn")
            || e.Health < 1 || e.Experience < 0 || e.ExpectedOwner != null && (string.IsNullOrWhiteSpace(e.ExpectedOwner)
                || e.Type is not ("unit_owner" or "unit_remove" or "commander_assign" or "commander_detach")))
            throw new RuleViolation("scenario.army_effect", "Invalid army effect fields");
        e.Cost?.Validate();
        e.UnitTemplate?.Combatant.Validate();
        if (e.CommanderTemplate is { } template) template.Rules(template.Definition.Id).Validate();
    }

    public static ScenarioEffect Copy(ScenarioEffect e) => e with
    {
        UnitTemplate = e.UnitTemplate == null ? null : e.UnitTemplate with { Combatant = FrozenCombatants.Copy(e.UnitTemplate.Combatant) },
        CommanderTemplate = e.CommanderTemplate == null ? null : e.CommanderTemplate with
        {
            Definition = e.CommanderTemplate.Definition with
            {
                Skills = e.CommanderTemplate.Definition.Skills.ToArray(), SkillSlots = e.CommanderTemplate.Definition.SkillSlots.ToArray(),
                LegacySkillOverrides = e.CommanderTemplate.Definition.LegacySkillOverrides.ToArray(),
                Stars = e.CommanderTemplate.Definition.Stars.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                Items = e.CommanderTemplate.Definition.Items.Select(i => i with { SkillSlots = i.SkillSlots?.ToArray() }).ToArray()
            },
            Skills = e.CommanderTemplate.Skills.ToDictionary(p => p.Key, p => p.Value.ToDictionary(v => v.Key, v => v.Value)),
            TitleHealing = e.CommanderTemplate.TitleHealing.ToDictionary(p => p.Key, p => p.Value)
        }
    };

    internal static WorldState Settle(WorldState world, IReadOnlyCollection<string> owners, List<WorldNotice> notices)
    {
        foreach (string owner in owners)
        {
            var faction = world.Factions.Single(f => f.Id == owner);
            if (faction.Defeated) continue;
            var armyCells = new HashSet<int>(world.Units.Where(u => u.Owner == owner).Select(u => u.Cell));
            var assets = world.Cells.Select((c, i) => (c, i)).Where(p => p.c.Owner == owner)
                .Select(p => new SurvivalAsset(armyCells.Contains(p.i), p.c.BuildingKind)).Append(new SurvivalAsset(armyCells.Count > 0, null));
            if (!FactionSurvival.Defeated(assets, faction.Survival)) continue;
            var surrendered = WorldEvents.Surrender(world, owner, null);
            world = surrendered.State; notices.AddRange(surrendered.Notices);
        }
        return world;
    }

    public static void ValidateState(WorldState world)
    {
        foreach (var ids in new[] { world.RetiredScenarioUnits, world.SpawnedScenarioUnits })
            if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new RuleViolation("scenario.army_state", "Invalid army identity history");
        if (world.RetiredScenarioUnits.Any(id => world.Units.Any(u => u.Id == id)))
            throw new RuleViolation("scenario.army_state", "A retired army cannot remain on the map");
        var effects = Effects(world).ToArray();
        foreach (var effect in effects.Where(e => e.Type is "unit_spawn" or "unit_remove" or "commander_assign" or "commander_detach"))
        {
            ValidateEffect(effect);
            if (effect.Cells.Any(i => !ValidLocation(world, effect, i)))
                throw new RuleViolation("scenario.reference", "Invalid army event location");
        }
        var declarations = effects.Where(e => e.Type == "unit_spawn").ToArray();
        if (declarations.Any(e => string.IsNullOrWhiteSpace(e.Unit))
            || declarations.Select(e => e.Unit).Distinct(StringComparer.Ordinal).Count() != declarations.Length)
            throw new RuleViolation("scenario.army_identity", "Each reinforcement needs one unique declaration");
        if (declarations.Any(e => world.Units.Any(u => Declared(e.Unit!, u.Id) && !world.SpawnedScenarioUnits.Contains(u.Id))))
            throw new RuleViolation("scenario.army_identity", "A reinforcement declaration collides with an initial army");
    }

    public static IEnumerable<ScenarioEffect> Effects(WorldState world) => world.ScenarioEvents.SelectMany(ScenarioRules.Effects)
        .Concat(world.Story?.Library.Scenes.Values.SelectMany(s => s.Steps!).SelectMany(s => s.Choices).SelectMany(c => c.Effects)
            ?? Enumerable.Empty<ScenarioEffect>());

    internal static bool ValidLocation(WorldState world, ScenarioEffect effect, int cell) =>
        effect.Type == "unit_spawn" && effect.UnitTemplate != null
            ? WorldUnitLayers.ValidTerrain(world, effect.UnitTemplate.Combatant.Profile, cell)
            : cell >= 0 && cell < world.Cells.Count && world.Cells[cell].Terrain.CanTraverse();

    public static bool Declared(string pattern, string id) => pattern == id || pattern.Contains("{occurrence}", StringComparison.Ordinal)
        && Regex.IsMatch(id, "^" + Regex.Escape(pattern).Replace("\\{occurrence}", "[1-9][0-9]*", StringComparison.Ordinal) + "$");

    internal static ScenarioEffect Resolve(WorldState world, ScenarioEffect effect, string? sourceEvent)
    {
        string ResolveId(string id)
        {
            if (!id.Contains("{occurrence}", StringComparison.Ordinal)) return id;
            if (sourceEvent == null || !world.ScenarioEventProgress.TryGetValue(sourceEvent, out var progress))
                throw new RuleViolation("scenario.army_identity", "Occurrence IDs require a recurring source event");
            return id.Replace("{occurrence}", progress.Occurrences.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        return effect with { Unit = effect.Unit == null ? null : ResolveId(effect.Unit),
            Commander = effect.Commander == null ? null : ResolveId(effect.Commander) };
    }

    internal static WorldState Apply(WorldState world, ScenarioEffect effect, string owner, List<WorldNotice> notices)
    {
        if (!world.Factions.Any(f => f.Id == owner && !f.Defeated))
            throw new RuleViolation("scenario.army_faction", "Army effects require a live receiving faction");
        if (effect.Type == "unit_spawn") return Spawn(world, effect, owner, notices);
        var unit = world.Units.SingleOrDefault(u => u.Id == effect.Unit);
        if (unit == null || unit.Owner != owner || effect.ExpectedOwner != null && unit.Owner != effect.ExpectedOwner)
            throw new RuleViolation("scenario.army_owner", "Army target is missing or no longer belongs to the expected faction");
        if (effect.Type == "commander_assign") return Assign(world, effect, owner, notices);
        if (effect.Type == "commander_detach")
        {
            if (unit.CommanderId == null || effect.Commander != null && effect.Commander != unit.CommanderId)
                throw new RuleViolation("scenario.army_commander", "The expected commander is not attached");
            var detached = WorldCommanders.DetachForOwner(world, unit.Id, owner, effect.CommanderTemplate?.Rules(unit.CommanderId) ?? new());
            notices.AddRange(detached.Notices);
            return effect.Disposition == "withdrawn" ? detached.State with
            { Commanders = detached.State.Commanders.Select(c => c.Id == unit.CommanderId ? c with { Withdrawn = true } : c).ToArray() } : detached.State;
        }
        if (unit.CommanderId != null && effect.Disposition == null)
            throw new RuleViolation("scenario.army_disposition", "Removing a commanded army requires an explicit commander disposition");
        var next = world with
        {
            Units = world.Units.Where(u => u.Id != unit.Id).ToArray(),
            RetiredScenarioUnits = world.RetiredScenarioUnits.Append(unit.Id).Distinct(StringComparer.Ordinal).ToArray(),
            Commanders = world.Commanders.Select(c => c.Id == unit.CommanderId && effect.Disposition == "withdrawn" ? c with { Withdrawn = true } : c).ToArray()
        };
        if (!WorldUnitLayers.Air(world, unit) && world.Cells[unit.Cell].Terrain.Sea && world.Cells[unit.Cell].BuildingKind == null
            && WorldUnitLayers.Surface(next).All(u => u.Cell != unit.Cell))
            next = next with { Cells = next.Cells.Select((c, i) => i == unit.Cell ? c with { Owner = null } : c).ToArray() };
        notices.Add(new("unit.removed", unit.Id, unit.Cell));
        return next;
    }

    private static WorldState Assign(WorldState world, ScenarioEffect effect, string owner, List<WorldNotice> notices)
    {
        string id = effect.Commander!;
        var runtime = world.Commanders.SingleOrDefault(c => c.Id == id);
        var template = effect.CommanderTemplate ?? throw new RuleViolation("scenario.army_definition", "Missing resolved commander definition");
        if (runtime?.DefinitionId != null && runtime.DefinitionId != template.Definition.Id)
            throw new RuleViolation("scenario.army_definition", "An existing commander cannot change identity");
        if (runtime == null)
        {
            if (world.DeadScenarioCommanders.Contains(id) || world.Commanders.Any(c => c.DefinitionId == template.Definition.Id))
                throw new RuleViolation("scenario.army_commander", "A scenario person cannot be instantiated twice");
            runtime = template.Runtime with { Id = id, Owner = owner, DefinitionId = template.Definition.Id, DisplayName = template.Definition.DisplayName };
            world = world with { Commanders = world.Commanders.Append(runtime).ToArray() };
        }
        var assigned = WorldCommanders.AssignForOwner(world, effect.Unit!, id, owner, template.Rules(id));
        notices.AddRange(assigned.Notices);
        return assigned.State;
    }

    private static WorldState Spawn(WorldState world, ScenarioEffect effect, string owner, List<WorldNotice> notices)
    {
        if (world.Units.Any(u => u.Id == effect.Unit) || world.SpawnedScenarioUnits.Contains(effect.Unit!)
            || world.DestroyedScenarioUnits.Contains(effect.Unit!) || world.RetiredScenarioUnits.Contains(effect.Unit!))
            throw new RuleViolation("scenario.army_identity", "Army instance IDs cannot be reused");
        var template = effect.UnitTemplate ?? throw new RuleViolation("scenario.army_definition", "Missing resolved army profile");
        var category = template.Combatant.Profile.Category;
        bool Site(int cell)
        {
            var site = world.Cells[cell];
            var occupants = world.Units.Where(u => u.Cell == cell && WorldUnitLayers.Layer(world, u.Combatant.Profile)
                == WorldUnitLayers.Layer(world, template.Combatant.Profile)).ToArray();
            bool air = WorldUnitLayers.Air(world, template.Combatant.Profile);
            return WorldUnitLayers.ValidTerrain(world, template.Combatant.Profile, cell) && (air || (category == "naval" ? site.Terrain.Sea : !site.Terrain.Sea))
                && (site.Owner == owner || (air || category == "naval") && site.Owner == null)
                && occupants.Length < WorldUnitLayers.Capacity(world, template.Combatant.Profile) && occupants.All(u => u.Owner == owner)
                && (air || category != "fort" || JurisdictionClaims.CanBuildFort(world, cell, owner));
        }
        int cell = effect.Cells.Where(Site).DefaultIfEmpty(-1).First();
        if (cell < 0 || !WorldArmyCapacity.CanAdd(world, owner))
            throw new RuleViolation("scenario.army_blocked", "No legal reinforcement site or army capacity; event remains pending");
        var unit = new UnitState
        {
            Id = effect.Unit!, Owner = owner, Cell = cell, Combatant = template.Combatant with
            { Health = template.Combatant.MaximumHealth, AtSea = !WorldUnitLayers.Air(world, template.Combatant.Profile) && world.Cells[cell].Terrain.Sea,
                BuildingKind = WorldUnitLayers.Air(world, template.Combatant.Profile) ? null : world.Cells[cell].BuildingKind },
            Support = template.Support, Experience = effect.Experience, Acted = true, RemainingMovement = 0,
            ExtensionMovementLimit = template.MovementBudget
        };
        var next = world with { Units = world.Units.Append(unit).ToArray(), SpawnedScenarioUnits = world.SpawnedScenarioUnits.Append(unit.Id).ToArray() };
        if (effect.Commander != null) next = Assign(next, effect, owner, notices);
        var spawned = next.Units.Single(u => u.Id == unit.Id);
        int health = effect.Health ?? spawned.Combatant.MaximumHealth;
        if (health < 1 || health > spawned.Combatant.MaximumHealth)
            throw new RuleViolation("scenario.army_health", "Initial health exceeds resolved maximum");
        next = next with { Units = next.Units.Select(u => u.Id == unit.Id ? u with { Combatant = u.Combatant with { Health = health } } : u).ToArray() };
        if (effect.Cost is { } cost)
        {
            var faction = next.Factions.Single(f => f.Id == owner);
            if (faction.Resources.Money < cost.Money || faction.Resources.Industry < cost.Industry || faction.Resources.Food < cost.Food)
                throw new RuleViolation("scenario.resources", "Reinforcement cost exceeds available resources");
            next = next with { Factions = next.Factions.Select(f => f.Id == owner ? f with { Resources = new(f.Resources.Money - cost.Money,
                f.Resources.Industry - cost.Industry, f.Resources.Food - cost.Food) } : f).ToArray() };
        }
        notices.Add(new("unit.spawned", unit.Id, cell));
        return next;
    }
}
