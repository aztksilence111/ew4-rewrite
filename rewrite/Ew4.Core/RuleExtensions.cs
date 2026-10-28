using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record SkillSlot(string Id, string InstanceId = "", int Level = 1, int Order = 0, float? X = null, float? Y = null)
{
    public static void Validate(IEnumerable<SkillSlot> slots)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in slots)
            if (slot == null || string.IsNullOrWhiteSpace(slot.Id) || string.IsNullOrWhiteSpace(slot.InstanceId) || slot.InstanceId.Length > 64
                || slot.InstanceId.Contains('/') || slot.Level < 1 || !ids.Add(slot.InstanceId) || slot.X.HasValue != slot.Y.HasValue
                || float.IsNaN(slot.X ?? 0) || float.IsInfinity(slot.X ?? 0) || float.IsNaN(slot.Y ?? 0) || float.IsInfinity(slot.Y ?? 0))
                throw new RuleViolation("extension.slot", "Invalid or duplicate skill instance");
    }
}
public sealed record ExtensionEffect
{
    public string Event { get; init; } = "combat.modify_damage";
    public string Operation { get; init; } = "damage_multiplier";
    public double Value { get; init; } = 1;
    public string? TargetTag { get; init; }
    public string? Script { get; init; }
    public string? Function { get; init; }
    public string Scope { get; init; } = "target";
    public int Radius { get; init; }
    public string Relation { get; init; } = "any";
    public string? Profile { get; init; }
    public string? Status { get; init; }
    public int Duration { get; init; }
}
public sealed record ExtensionSkill
{
    public string Id { get; init; } = "";
    public string NameKey { get; init; } = "";
    public string DescriptionKey { get; init; } = "";
    public string? Icon { get; init; }
    public string Kind { get; init; } = "passive";
    public string Target { get; init; } = "self";
    public int Range { get; init; } = 1;
    public int Cooldown { get; init; }
    public bool EndsAction { get; init; } = true;
    public Resources Cost { get; init; } = new(0, 0, 0);
    public IReadOnlyList<ExtensionEffect> Effects { get; init; } = Array.Empty<ExtensionEffect>();
}
public sealed record RuleScriptContext(string Event, Combatant? Source, Combatant? Target, int Level,
    int Round, double Value, IReadOnlyDictionary<string, long> State, WorldState? World = null, string? PlayerCamp = null,
    string? SourceId = null, string? TargetId = null, int? TargetCell = null, int Stacks = 1, int RemainingTurns = 0,
    string? SkillId = null, string? InstanceId = null, string? ItemId = null);
public sealed record RuleScriptResult(double Value, IReadOnlyDictionary<string, long>? State = null);
public interface IRuleScriptRuntime
{
    RuleScriptResult Run(string file, string function, RuleScriptContext context, Ew4Random random);
}

public sealed partial class RuleEventBus
{
    public IReadOnlyDictionary<string, ExtensionSkill> Skills { get; }
    public IRuleScriptRuntime? Scripts { get; }
    public IReadOnlyDictionary<string, ExtensionStatus> Statuses { get; }
    public RuleEventBus(IReadOnlyDictionary<string, ExtensionSkill> skills, IRuleScriptRuntime? scripts = null,
        IReadOnlyDictionary<string, ExtensionStatus>? statuses = null)
    { Skills = skills; Scripts = scripts; Statuses = statuses ?? new Dictionary<string, ExtensionStatus>(); }

    public static IReadOnlyList<SkillSlot> Slots(Combatant unit) => unit.Profile.SkillSlots.Concat(unit.SkillSlots.Count > 0
        ? unit.SkillSlots : unit.Skills.Select((id, i) => new SkillSlot(id, "legacy." + i))).ToArray();

    public IEnumerable<(ExtensionSkill Skill, SkillSlot Slot)> Bound(Combatant unit) => Slots(unit)
        .Concat(unit.Profile.Traits.Select(id => new SkillSlot(id, "trait." + id)))
        .Where(s => Skills.ContainsKey(s.Id)).Select(s => (Skills[s.Id], s)).Concat(StatusBindings(unit));

    private static string? ItemId(SkillSlot slot)
    {
        if (!slot.InstanceId.StartsWith("item.", StringComparison.Ordinal)) return null;
        int end = slot.InstanceId.IndexOf('.', 5);
        return end > 5 ? "ew4.item." + slot.InstanceId.Substring(5, end - 5) : null;
    }

    public double Modify(string ev, string operation, double value, Combatant source, Combatant target, Ew4Random random)
    {
        foreach (var (skill, slot) in Bound(source).Where(p => p.Skill.Kind == "passive"))
            foreach (var effect in skill.Effects.Where(e => e.Event == ev && e.Operation == operation
                && (e.TargetTag == null || target.Profile.Traits.Contains(e.TargetTag))))
            {
                var status = source.Statuses.SingleOrDefault(s => "status." + s.Id == slot.InstanceId);
                double amount = effect.Script == null ? StackedValue(effect, status) : Scripts!.Run(effect.Script, effect.Function!,
                    new(ev, source, target, slot.Level, 0, value, new Dictionary<string, long>(),
                        Stacks: status?.Stacks ?? 1, RemainingTurns: status?.RemainingTurns ?? 0,
                        SkillId: skill.Id, InstanceId: slot.InstanceId, ItemId: ItemId(slot)), random).Value;
                value = operation.EndsWith("multiplier", StringComparison.Ordinal) ? value * amount
                    : operation == "counterattack" ? Math.Min(value, amount) : value + amount;
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > int.MaxValue)
                    throw new RuleViolation("extension.value", "Invalid rule result: " + skill.Id);
            }
        return value;
    }

    internal WorldState DispatchFactionTurn(WorldState world, string faction, string ev)
    {
        var ids = world.Units.Where(u => u.Owner == faction).Select(u => u.Id).ToArray();
        var units = world.Units.ToDictionary(u => u.Id, StringComparer.Ordinal);
        foreach (string id in ids)
        {
            if (!units.TryGetValue(id, out var unit) || !Bound(unit.Combatant)
                .Any(p => p.Skill.Kind == "passive" && p.Skill.Effects.Any(e => e.Event == ev))) continue;
            var previous = world.Units;
            world = Dispatch(world, ev, id, new Ew4Random(world.RandomState));
            if (!ReferenceEquals(previous, world.Units)) units = world.Units.ToDictionary(u => u.Id, StringComparer.Ordinal);
        }
        return world;
    }

    public WorldState Dispatch(WorldState world, string ev, string unitId, Ew4Random random, string? onlyInstance = null, string? targetId = null,
        UnitState? eventSource = null, int? targetCell = null, WorldMoveRules? movement = null, SkillSlot? grantedSlot = null)
    {
        var source = world.Units.SingleOrDefault(u => u.Id == unitId) ?? eventSource;
        if (source == null) return world;
        var target = targetId == null ? source : world.Units.Single(u => u.Id == targetId);
        var bindings = grantedSlot == null ? Bound(source.Combatant).Where(p => onlyInstance != null
            ? p.Slot.InstanceId == onlyInstance : p.Skill.Kind == "passive")
            : new[] { (Skill: Skills[grantedSlot.Id], Slot: grantedSlot) };
        foreach (var (skill, slot) in bindings)
            foreach (var effect in skill.Effects.Where(e => e.Event == ev))
            {
                if (ev == "skill.activate" && !world.Units.Any(u => u.Id == unitId))
                    return world.RandomState == random.State ? world : world with { RandomState = random.State };
                var status = source.Combatant.Statuses.SingleOrDefault(s => "status." + s.Id == slot.InstanceId);
                foreach (var affected in EffectTargets(world, source, target, targetCell, effect))
                {
                    var counters = world.ExtensionState.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                    string prefix = unitId + "/" + slot.InstanceId + "/";
                    var local = counters.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal))
                        .ToDictionary(p => p.Key[prefix.Length..], p => p.Value, StringComparer.Ordinal);
                    var currentSource = world.Units.SingleOrDefault(u => u.Id == source.Id) ?? source;
                    var result = effect.Script == null ? new RuleScriptResult(StackedValue(effect, status)) : Scripts!.Run(effect.Script, effect.Function!,
                        new(ev, currentSource.Combatant, affected?.Combatant, slot.Level, world.Round, effect.Value, local, world,
                            SourceId: source.Id, TargetId: affected?.Id, TargetCell: targetCell ?? target.Cell,
                            Stacks: status?.Stacks ?? 1, RemainingTurns: status?.RemainingTurns ?? 0,
                            SkillId: skill.Id, InstanceId: slot.InstanceId, ItemId: ItemId(slot)), random);
                    if (result.State != null) foreach (var pair in result.State)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 64 || pair.Key.Contains('/'))
                            throw new RuleViolation("extension.state", "Invalid script state key");
                        counters[prefix + pair.Key] = pair.Value;
                    }
                    if (double.IsNaN(result.Value) || double.IsInfinity(result.Value) || result.Value < (effect.Operation == "movement" ? int.MinValue : 0) || result.Value > int.MaxValue)
                        throw new RuleViolation("extension.value", "Invalid event result: " + skill.Id);
                    if (effect.Operation == "summon" && result.Value != 1)
                        throw new RuleViolation("skill.summon", "A summon effect must return exactly one unit");
                    int amount = checked((int)result.Value);
                    world = world with { ExtensionState = counters, RandomState = random.State };
                    world = ApplyEffect(world, currentSource, affected, targetCell ?? target.Cell, effect, amount, slot.Level,
                        status?.SourceOwner ?? source.Owner, movement);
                    random.Commit(world.RandomState);
                }
            }
        return world.RandomState == random.State ? world : world with { RandomState = random.State };
    }
}

public static class WorldSkillActions
{
    public static WorldChange Execute(WorldState state, long revision, string unitId, string instanceId, string? targetId,
        RuleEventBus bus, WorldMoveRules movement, int? targetCell = null)
    {
        state.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == unitId);
        if (state.Revision != revision || state.Phase != TurnPhase.Active || state.PendingDialogue != null
            || unit == null || unit.Owner != state.ActiveFaction || unit.Acted || unit.Morale.Effective == -3)
            throw new RuleViolation("skill.unavailable", "Unit cannot use a skill now");
        var binding = bus.Bound(unit.Combatant).SingleOrDefault(p => p.Slot.InstanceId == instanceId);
        var skill = binding.Skill ?? throw new RuleViolation("skill.unknown", "Unknown skill instance");
        var target = targetId == null ? unit : state.Units.SingleOrDefault(u => u.Id == targetId);
        string key = unitId + "/" + instanceId + "/ready";
        if (skill.Kind != "active" || target == null || state.ExtensionState.TryGetValue(key, out long ready) && ready > state.Round)
            throw new RuleViolation("skill.unavailable", "Skill unavailable or cooling down");
        if (skill.Target == "cell" && (targetId != null || targetCell == null || !bus.ValidCellTarget(state, unit, skill, targetCell.Value))
            || skill.Target != "cell" && targetCell != null)
            throw new RuleViolation("skill.target", "Invalid skill cell target");
        bool allied = WorldDiplomacy.Allied(state, unit.Owner, target.Owner);
        bool hostile = !allied;
        if (skill.Target == "self" && target.Id != unit.Id || skill.Target == "ally" && !allied || skill.Target == "enemy" && !hostile
            || new HexMap(state.Width, state.Height).Distance(unit.Cell, targetCell ?? target.Cell) > skill.Range)
            throw new RuleViolation("skill.target", "Invalid skill target");
        var faction = state.Factions.Single(f => f.Id == unit.Owner);
        if (faction.Resources.Money < skill.Cost.Money || faction.Resources.Industry < skill.Cost.Industry || faction.Resources.Food < skill.Cost.Food)
            throw new RuleViolation("skill.resources", "Insufficient resources");
        var next = state.Copy() with { Factions = state.Factions.Select(f => f.Id != unit.Owner ? f : f with { Resources = new(
            checked(f.Resources.Money - skill.Cost.Money), checked(f.Resources.Industry - skill.Cost.Industry), checked(f.Resources.Food - skill.Cost.Food)) }).ToArray() };
        if (skill.Effects.Any(e => e.Operation == "damage") && target.Owner != unit.Owner)
            next = WorldDiplomacy.Attacked(next, unit.Owner, target.Owner, movement.NeutralCamps);
        next = bus.Dispatch(next, "skill.activate", unitId, new Ew4Random(state.RandomState), instanceId,
            skill.Target == "cell" ? null : target.Id, targetCell: targetCell, movement: movement);
        if (next.Units.SingleOrDefault(u => u.Id == unitId) is UnitState relocated && relocated.Cell != unit.Cell)
            next = bus.Dispatch(next, "unit.move_end", unitId, new Ew4Random(next.RandomState));
        var counters = next.ExtensionState.ToDictionary(p => p.Key, p => p.Value);
        counters[key] = checked(state.Round + Math.Max(1, skill.Cooldown));
        next = next with { ExtensionState = counters, Revision = checked(revision + 1), Units = next.Units.Select(u => u.Id == unitId && skill.EndsAction
            ? u with { Acted = true, RemainingMovement = 0 } : u).ToArray() };
        var events = ScenarioRules.Evaluate(next, bus.Scripts);
        next = events.State;
        if (movement.PlayerCamp != null && next.Outcome == BattleOutcome.Continue && next.PendingDialogue == null)
        {
            next = WorldVictory.Resolve(next, movement.PlayerCamp, movement.Victory, movement.NeutralCamps);
        }
        next.Validate();
        var notices = new List<WorldNotice> { new("skill.used", unitId) };
        notices.AddRange(RuleEventBus.ChangeNotices(state, next));
        notices.AddRange(events.Notices);
        return new(next, notices);
    }
}
