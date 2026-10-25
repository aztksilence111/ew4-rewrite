using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ExtensionStatus
{
    public string Id { get; init; } = "";
    public string NameKey { get; init; } = "";
    public string DescriptionKey { get; init; } = "";
    public string? Icon { get; init; }
    public int MaxStacks { get; init; } = 1;
    public string Stacking { get; init; } = "refresh";
    public IReadOnlyList<ExtensionEffect> Effects { get; init; } = Array.Empty<ExtensionEffect>();
}

public sealed record SkillStatus(string Id, int RemainingTurns, int Level = 1, int Stacks = 1,
    string? SourceId = null, string? SourceOwner = null);
public sealed record SkillSummonTemplate(UnitProfile Profile, UnitSupport Support);

public sealed partial class RuleEventBus
{
    public IReadOnlyDictionary<string, SkillSummonTemplate> UnitTemplates { get; private set; }
        = new Dictionary<string, SkillSummonTemplate>();

    public void RegisterUnitTemplates(IEnumerable<SkillSummonTemplate> templates)
    {
        var index = templates.ToDictionary(t => t.Profile.Id, StringComparer.Ordinal);
        foreach (var template in index.Values) template.Profile.Validate();
        foreach (var effect in Skills.Values.SelectMany(s => s.Effects))
            if (effect.Operation == "summon" && (effect.Profile == null || !index.ContainsKey(effect.Profile)))
                throw new RuleViolation("extension.summon_profile", effect.Profile ?? "Missing profile");
        UnitTemplates = index;
    }

    private IEnumerable<(ExtensionSkill Skill, SkillSlot Slot)> StatusBindings(Combatant unit) => unit.Statuses.Select(s =>
    {
        if (!Statuses.TryGetValue(s.Id, out var definition)) throw new RuleViolation("extension.status", "Unknown saved status: " + s.Id);
        return (new ExtensionSkill { Id = s.Id, NameKey = definition.NameKey, DescriptionKey = definition.DescriptionKey,
            Icon = definition.Icon, Effects = definition.Effects }, new SkillSlot(s.Id, "status." + s.Id, s.Level));
    });

    private static double StackedValue(ExtensionEffect effect, SkillStatus? status) => effect.Operation.EndsWith("multiplier", StringComparison.Ordinal)
        ? Math.Pow(effect.Value, status?.Stacks ?? 1) : effect.Value * (status?.Stacks ?? 1);

    private static UnitState?[] EffectTargets(WorldState world, UnitState source, UnitState selected, int? selectedCell, ExtensionEffect effect)
    {
        if (effect.Operation is "summon" or "teleport") return new UnitState?[] { null };
        IEnumerable<UnitState> targets = effect.Scope == "source" ? new[] { world.Units.SingleOrDefault(u => u.Id == source.Id) ?? source }
            : effect.Scope == "area" ? world.Units.Where(u => new HexMap(world.Width, world.Height).Distance(selectedCell ?? selected.Cell, u.Cell) <= effect.Radius)
            : selectedCell.HasValue ? world.Units.Where(u => u.Cell == selectedCell) : new[] { world.Units.SingleOrDefault(u => u.Id == selected.Id) ?? selected };
        return targets.Where(u => (effect.TargetTag == null || u.Combatant.Profile.Traits.Contains(effect.TargetTag))
            && (effect.Relation == "any" || WorldDiplomacy.Allied(world, source.Owner, u.Owner) == (effect.Relation == "ally")))
            .OrderBy(u => u.Id, StringComparer.Ordinal).Cast<UnitState?>().ToArray();
    }

    public bool ValidCellTarget(WorldState world, UnitState source, ExtensionSkill skill, int cell)
    {
        if (cell < 0 || cell >= world.Cells.Count || !world.Cells[cell].Terrain.Active
            || new HexMap(world.Width, world.Height).Distance(source.Cell, cell) > skill.Range) return false;
        foreach (var effect in skill.Effects)
        {
            if (effect.Operation == "teleport" && (cell == source.Cell || !CanPlace(world, source.Combatant.Profile, source.Transport, source.Owner, cell))) return false;
            if (effect.Operation == "summon" && (effect.Profile == null || !UnitTemplates.TryGetValue(effect.Profile, out var template)
                || !CanPlace(world, template.Profile, false, source.Owner, cell)
                || world.Units.Count(u => u.Owner == source.Owner) >= world.Limits.UnitsPerFaction)) return false;
        }
        return true;
    }

    private static bool CanPlace(WorldState world, UnitProfile profile, bool transport, string owner, int cell)
    {
        if (cell < 0 || cell >= world.Cells.Count) return false;
        var site = world.Cells[cell];
        return profile.Category != "fort" && site.Terrain.CanTraverse()
            && (profile.Category == "naval" ? site.Terrain.Sea : !site.Terrain.Sea || transport)
            && (site.Owner == null || WorldDiplomacy.Allied(world, owner, site.Owner))
            && world.Units.Count(u => u.Cell == cell) < world.Limits.UnitsPerCell
            && world.Units.Where(u => u.Cell == cell).All(u => WorldDiplomacy.Allied(world, owner, u.Owner));
    }

    private WorldState ApplyEffect(WorldState world, UnitState source, UnitState? target, int cell,
        ExtensionEffect effect, int amount, int level, string creditOwner, WorldMoveRules? movement)
    {
        if (effect.Operation == "summon")
        {
            if (amount != 1 || effect.Profile == null || !UnitTemplates.TryGetValue(effect.Profile, out var template)
                || !CanPlace(world, template.Profile, false, source.Owner, cell)
                || world.Units.Count(u => u.Owner == source.Owner) >= world.Limits.UnitsPerFaction)
                throw new RuleViolation("skill.summon", "Summoning requires one valid unit and a legal free cell");
            string id = "summon." + world.Revision + "." + cell;
            int serial = 0;
            while (world.Units.Any(u => u.Id == id) || world.DestroyedScenarioUnits.Contains(id))
                id = "summon." + world.Revision + "." + cell + "." + ++serial;
            var summoned = new UnitState { Id = id, Owner = source.Owner, Cell = cell, Support = template.Support,
                Combatant = new Combatant { Profile = template.Profile, Health = template.Profile.MaxHealth,
                    AtSea = world.Cells[cell].Terrain.Sea, BuildingKind = world.Cells[cell].BuildingKind }, Acted = true };
            return world with { Units = world.Units.Append(summoned).ToArray() };
        }
        if (effect.Operation == "teleport")
        {
            var caster = world.Units.SingleOrDefault(u => u.Id == source.Id);
            if (caster == null || cell == caster.Cell || !CanPlace(world, caster.Combatant.Profile, caster.Transport, caster.Owner, cell))
                throw new RuleViolation("skill.teleport", "Invalid teleport destination");
            var next = world with { Units = world.Units.Select(u => u.Id != caster.Id ? u : u with {
                Cell = cell, RemainingMovement = 0, Combatant = u.Combatant with {
                    AtSea = world.Cells[cell].Terrain.Sea, BuildingKind = world.Cells[cell].BuildingKind } }).ToArray() };
            // Teleportation does not traverse or claim territory; only neutral or friendly destinations are allowed.
            return WorldDeaths.RefreshMorale(next, movement?.NeutralCamps ?? world.DiplomacyNeutralCamps);
        }
        if (effect.Operation is "money" or "industry" or "food")
            return world with { Factions = world.Factions.Select(f => f.Id != source.Owner ? f : f with {
                Resources = new Resources(checked(f.Resources.Money + (effect.Operation == "money" ? amount : 0)),
                    checked(f.Resources.Industry + (effect.Operation == "industry" ? amount : 0)),
                    checked(f.Resources.Food + (effect.Operation == "food" ? amount : 0))) }).ToArray() };
        target = target == null ? null : world.Units.SingleOrDefault(u => u.Id == target.Id);
        if (target == null) return world;
        if (effect.Operation == "damage")
        {
            if (amount > 0 && target.Owner != creditOwner) world = WorldDiplomacy.Attacked(world, creditOwner, target.Owner, world.DiplomacyNeutralCamps);
            int health = (int)Math.Max(0, (long)target.Combatant.Health - amount);
            return health > 0 ? world with { Units = world.Units.Select(u => u.Id != target.Id ? u : u with {
                Combatant = u.Combatant with { Health = health } }).ToArray() }
                : WorldDeaths.RefreshMorale(WorldDeaths.Remove(world, target.Id, creditOwner).State, world.DiplomacyNeutralCamps);
        }
        if (effect.Operation == "apply_status")
        {
            if (effect.Status == null || !Statuses.TryGetValue(effect.Status, out var definition) || effect.Duration < 1
                || amount < 1 || amount > definition.MaxStacks) throw new RuleViolation("skill.status", "Invalid status application");
            var old = target.Combatant.Statuses.SingleOrDefault(s => s.Id == definition.Id);
            int stacks = old == null || definition.Stacking == "replace" ? amount
                : definition.Stacking == "stack" ? Math.Min(definition.MaxStacks, old.Stacks + amount) : old.Stacks;
            var applied = new SkillStatus(definition.Id, old == null || definition.Stacking == "replace" ? effect.Duration
                : Math.Max(old.RemainingTurns, effect.Duration), level, stacks, source.Id, source.Owner);
            var statuses = target.Combatant.Statuses.Where(s => s.Id != applied.Id).Append(applied).ToArray();
            if (statuses.Length > 64) throw new RuleViolation("skill.status", "Too many statuses");
            if (!WorldDiplomacy.Allied(world, source.Owner, target.Owner))
                world = WorldDiplomacy.Attacked(world, source.Owner, target.Owner, world.DiplomacyNeutralCamps);
            return world with { Units = world.Units.Select(u => u.Id != target.Id ? u : u with {
                Combatant = u.Combatant with { Statuses = statuses }, ExtensionMovementLimit = MovementBudget(u.Combatant with { Statuses = statuses }),
                RemainingMovement = Math.Min(u.RemainingMovement, MovementBudget(u.Combatant with { Statuses = statuses })) }).ToArray(),
                ExtensionState = old == null || definition.Stacking == "replace" ? ClearStatusState(world, target.Id, definition.Id) : world.ExtensionState };
        }
        if (effect.Operation == "remove_status")
            return world with { Units = world.Units.Select(u => {
                if (u.Id != target.Id) return u;
                var combatant = u.Combatant with { Statuses = u.Combatant.Statuses.Where(s => s.Id != effect.Status).ToArray() };
                return u with { Combatant = combatant, ExtensionMovementLimit = MovementBudget(combatant),
                    RemainingMovement = Math.Min(u.RemainingMovement, MovementBudget(combatant)) };
            }).ToArray(),
                ExtensionState = ClearStatusState(world, target.Id, effect.Status!) };
        return world with { Units = world.Units.Select(u => u.Id != target.Id ? u : effect.Operation == "heal"
            ? u with { Combatant = u.Combatant with { Health = (int)Math.Min(u.Combatant.Profile.MaxHealth, (long)u.Combatant.Health + amount) } }
            : effect.Operation == "movement" ? u with { ExtensionMovementLimit = MovementBudget(u.Combatant),
                RemainingMovement = (int)Math.Max(0, Math.Min(MovementBudget(u.Combatant), (long)u.RemainingMovement + amount)) }
            : throw new RuleViolation("extension.operation", effect.Operation)).ToArray() };
    }

    private static IReadOnlyDictionary<string, long> ClearStatusState(WorldState world, string unit, string status)
    {
        string prefix = unit + "/status." + status + "/";
        return world.ExtensionState.Where(p => !p.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }

    public WorldState ExpireStatuses(WorldState world, string faction)
    {
        foreach (var unit in world.Units.Where(u => u.Owner == faction).ToArray())
        {
            foreach (var status in unit.Combatant.Statuses.Where(s => s.RemainingTurns == 1))
                world = world with { ExtensionState = ClearStatusState(world, unit.Id, status.Id) };
            world = world with { Units = world.Units.Select(u => {
                if (u.Id != unit.Id) return u;
                var combatant = u.Combatant with { Statuses = u.Combatant.Statuses.Where(s => s.RemainingTurns > 1)
                    .Select(s => s with { RemainingTurns = s.RemainingTurns - 1 }).ToArray() };
                int limit = MovementBudget(combatant);
                return u with { Combatant = combatant, ExtensionMovementLimit = limit, RemainingMovement = Math.Min(u.RemainingMovement, limit) };
            }).ToArray() };
        }
        return world;
    }

    public int MovementBudget(Combatant unit)
    {
        double budget = unit.Profile.Movement;
        foreach (var (skill, slot) in Bound(unit).Where(p => p.Skill.Kind == "passive"))
            foreach (var effect in skill.Effects.Where(e => e.Event == "movement.modify"
                && (e.TargetTag == null || unit.Profile.Traits.Contains(e.TargetTag))))
            {
                var status = unit.Statuses.SingleOrDefault(s => "status." + s.Id == slot.InstanceId);
                double amount = effect.Script == null ? StackedValue(effect, status) : Scripts!.Run(effect.Script, effect.Function!,
                    new("movement.modify", unit, unit, slot.Level, 0, budget, new Dictionary<string, long>(),
                        Stacks: status?.Stacks ?? 1, RemainingTurns: status?.RemainingTurns ?? 0,
                        SkillId: skill.Id, InstanceId: slot.InstanceId, ItemId: ItemId(slot)), new Ew4Random(1)).Value;
                budget += amount;
                if (double.IsNaN(budget) || double.IsInfinity(budget) || budget > int.MaxValue || budget < int.MinValue)
                    throw new RuleViolation("extension.value", "Invalid movement modifier");
            }
        return (int)Math.Max(0, budget);
    }

    public static IReadOnlyList<WorldNotice> ChangeNotices(WorldState before, WorldState after)
    {
        var notices = new List<WorldNotice>();
        foreach (var unit in before.Units)
        {
            var next = after.Units.SingleOrDefault(u => u.Id == unit.Id);
            int damage = unit.Combatant.Health - (next?.Combatant.Health ?? 0);
            if (damage > 0) notices.Add(new("battle.damage", unit.Id, damage));
            else if (damage < 0) notices.Add(new("unit.healed", unit.Id, -(long)damage));
            if (next == null) notices.Add(new("unit.destroyed", unit.Id));
            else if (next.Cell != unit.Cell) notices.Add(new("unit.moved", unit.Id, next.Cell));
        }
        foreach (var unit in after.Units.Where(u => !before.Units.Any(old => old.Id == u.Id))) notices.Add(new("unit.recruited", unit.Id));
        foreach (var commander in after.Commanders.Where(c => c.Withdrawn && before.Commanders.Any(old => old.Id == c.Id && !old.Withdrawn)))
            notices.Add(new("commander.withdrawn", commander.Id));
        foreach (var faction in after.Factions.Where(f => f.Defeated && before.Factions.Any(old => old.Id == f.Id && !old.Defeated)))
            notices.Add(new("faction.defeated", faction.Id));
        for (int i = 0; i < before.Cells.Count; i++) if (before.Cells[i].Owner != after.Cells[i].Owner) notices.Add(new("cell.owner_changed", i.ToString()));
        return notices;
    }
}
