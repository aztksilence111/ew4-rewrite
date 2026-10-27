using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record WorldNotice(string Kind, string Subject, long Value = 0)
{
    public string? Target { get; init; }
}
public sealed record WorldMovement(string Unit, IReadOnlyList<int> Path);
public sealed record WorldChange(WorldState State, IReadOnlyList<WorldNotice> Notices)
{
    public IReadOnlyList<WorldMovement> Movements { get; init; } = Array.Empty<WorldMovement>();
}
public sealed record EventContext(TriggerKind Trigger, string? UnitId = null, int? Cell = null,
    string? PreviousOwner = null, string? NewOwner = null, bool HasCommander = false);

public static class WorldEvents
{
    public static WorldChange Process(WorldState state, TriggerKind trigger, string? destroyedUnit = null)
    {
        state.Validate();
        var unit = state.Units.SingleOrDefault(u => u.Id == destroyedUnit);
        return Process(state, new EventContext(trigger, destroyedUnit, unit?.Cell,
            unit == null ? null : state.Cells[unit.Cell].Owner, HasCommander: unit?.CommanderId != null));
    }

    public static WorldChange Process(WorldState state, EventContext context) => ProcessCore(state, context, false);

    internal static WorldChange DuringAction(WorldState state, EventContext context) => ProcessCore(state, context, true);

    private static WorldChange ProcessCore(WorldState state, EventContext context, bool queueDialogues)
    {
        state.Validate();
        if (!Enum.IsDefined(typeof(TriggerKind), context.Trigger)
            || (context.Trigger == TriggerKind.UnitDestroyed && string.IsNullOrWhiteSpace(context.UnitId))
            || (context.Trigger == TriggerKind.CellCaptured && (!context.Cell.HasValue || context.Cell < 0 || context.Cell >= state.Cells.Count
                || context.PreviousOwner == null || context.NewOwner == null || context.PreviousOwner == context.NewOwner))
            || new[] { context.PreviousOwner, context.NewOwner }.Any(id => id != null && !state.Factions.Any(f => f.Id == id)))
            throw new RuleViolation("event.trigger", "Invalid event trigger");
        if (state.PendingDialogue != null && !queueDialogues) throw new RuleViolation("event.dialogue_pending", "Acknowledge dialogue before continuing");
        // A controlled candidate can borrow its inputs until an event actually
        // writes. Public actions still return independently owned snapshots.
        bool deferredCopy = queueDialogues && PlanningScope.IsMovementProjection(state);
        var next = deferredCopy ? state : state.Copy();
        var fired = new HashSet<string>(state.TriggeredEvents, StringComparer.Ordinal);
        var notices = new List<WorldNotice>();
        bool differentCamps = context.PreviousOwner != null && context.NewOwner != null
            && state.Factions.Single(f => f.Id == context.PreviousOwner).Camp != state.Factions.Single(f => f.Id == context.NewOwner).Camp;
        foreach (var ev in state.Events)
        {
            if (fired.Contains(ev.Id)) continue;
            var rule = ev.Rules.FirstOrDefault(r => r.Trigger == context.Trigger && (!r.RequireCommander || context.HasCommander)
                && (!r.RequireDifferentCamps || differentCamps)
                && (context.Trigger == TriggerKind.Round ? r.Round == state.Round
                    : context.Trigger == TriggerKind.UnitDestroyed ? r.UnitId == context.UnitId : r.TriggerCell == context.Cell));
            if (rule == null) continue;
            if (deferredCopy) { next = state.Copy(); deferredCopy = false; }
            string? faction = rule.Target == EventFactionTarget.PreviousOwner ? context.PreviousOwner
                : rule.Target == EventFactionTarget.NewOwner ? context.NewOwner : rule.FactionId;
            if (rule.Effect == EventEffectKind.SetMorale && faction != null)
                next = next with { Units = next.Units.Select(u =>
                {
                    if (u.Owner != faction) return u;
                    var morale = MoraleState.Set(rule.Value, u.Combatant.Has(Ew4Combat.Skill(3)));
                    return u with { Morale = morale, Combatant = u.Combatant with { Morale = morale.Effective } };
                }).ToArray() };
            else if (rule.Effect == EventEffectKind.EnvironmentSpread)
                next = Spread(next, rule.Cell);
            fired.Add(ev.Id);
            notices.Add(new WorldNotice("event.triggered", ev.Id));
            if (ev.DialogueId != null)
            {
                next = next.PendingDialogue == null ? next with { PendingDialogue = ev.DialogueId }
                    : next with { QueuedDialogues = next.QueuedDialogues.Append(ev.DialogueId).ToArray() };
                notices.Add(new WorldNotice("dialogue", ev.DialogueId));
                if (!queueDialogues) break;
            }
        }
        var triggered = state.Events.Where(e => fired.Contains(e.Id)).Select(e => e.Id).ToArray();
        if (deferredCopy && triggered.SequenceEqual(state.TriggeredEvents)) return new(state, notices);
        if (deferredCopy) next = state.Copy();
        next = next with { TriggeredEvents = triggered,
            Revision = notices.Count == 0 ? state.Revision : checked(state.Revision + 1) };
        next.Validate();
        return new WorldChange(next, notices);
    }

    public static WorldState Acknowledge(WorldState state, string dialogue)
    {
        state.Validate();
        if (StoryPlayback.Current(state) != null) throw new RuleViolation("story.command_required", "Advance the story through its own command");
        if (ScenarioRules.Pending(state) != null) throw new RuleViolation("scenario.choice_required", "Select an event choice");
        if (state.PendingDialogue == null || dialogue != state.PendingDialogue)
            throw new RuleViolation("event.dialogue_mismatch", "Dialogue acknowledgement does not match");
        return state.Copy() with { PendingDialogue = state.QueuedDialogues.FirstOrDefault(), QueuedDialogues = state.QueuedDialogues.Skip(1).ToArray(),
            Revision = checked(state.Revision + 1) };
    }

    internal static WorldState Spread(WorldState state, int center)
    {
        var map = new HexMap(state.Width, state.Height);
        map.Check(center);
        var random = new Ew4Random(state.RandomState);
        var cells = state.Cells.ToArray();
        void Raise(int cell, int level)
        {
            if (!cells[cell].Terrain.Sea) cells[cell] = cells[cell] with { Environment = Math.Max(cells[cell].Environment, level) };
        }
        Raise(center, 3);
        for (int direction = 0; direction < 6; direction++)
        {
            int neighbor = map.Neighbor(center, direction);
            if (neighbor < 0) continue;
            int roll = random.Next(100);
            Raise(neighbor, roll < 30 ? 2 : roll < 60 ? 1 : 0);
        }
        return state with { Cells = cells, RandomState = random.State };
    }

    public static WorldChange Surrender(WorldState state, string loser, string? successor,
        bool legacyRemoveAllOccupants = false)
    {
        state.Validate();
        if (!state.Factions.Any(f => f.Id == loser && !f.Defeated)
            || successor == loser || (successor != null && !state.Factions.Any(f => f.Id == successor && !f.Defeated)))
            throw new RuleViolation("surrender.faction", "Invalid defeated or receiving faction");
        var removed = state.Units.Where(u => u.Owner == loser || (legacyRemoveAllOccupants && state.Cells[u.Cell].Owner == loser)).ToArray();
        var removedIds = new HashSet<string>(removed.Select(u => u.Id), StringComparer.Ordinal);
        var copy = state.Copy();
        var transfer = copy.Cells.Select(c => c.Owner != loser ? c : c with
            { Owner = c.Terrain.Sea && c.BuildingKind == null ? null : successor }).ToArray();
        var next = copy with
        {
            Cells = successor == null ? transfer : JurisdictionClaims.Filter(state, transfer, successor),
            Units = copy.Units.Where(u => !removedIds.Contains(u.Id)).ToArray(),
            Commanders = copy.Commanders.Select(c => c.Owner == loser || removed.Any(u => u.CommanderId == c.Id) ? c with { Withdrawn = true } : c).ToArray(),
            Factions = state.Factions.Select(f => f.Id == loser ? f with { Defeated = true } : f).ToArray(),
            Revision = checked(state.Revision + 1)
        };
        next.Validate();
        return new WorldChange(next, removed.Select(u => new WorldNotice("unit.removed", u.Id))
            .Concat(new[] { new WorldNotice("faction.defeated", loser) }).ToArray());
    }
}
