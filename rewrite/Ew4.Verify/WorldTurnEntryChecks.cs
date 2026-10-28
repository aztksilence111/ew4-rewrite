using Ew4.Core;
using Ew4.Persistence;

internal static class WorldTurnEntryChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Turn entry: " + label); }
        var basis = WorldChecks.Example();
        var initial = basis with { Phase = TurnPhase.RoundEvents, ActiveFaction = basis.Factions[0].Id, Round = 1,
            Events = [new() { Id = "opening", DialogueId = "hello", Rules = [new() { Trigger = TriggerKind.Round, Round = 1,
                Effect = EventEffectKind.SetMorale, FactionId = basis.Units[0].Owner, Value = -1 }] },
                new() { Id = "orders", DialogueId = "orders", Rules = [new() { Trigger = TriggerKind.Round, Round = 1 }] }],
            Units = basis.Units.Select(u => u with { Acted = true, RemainingMovement = 0 }).ToArray() };
        var rules = new WorldTurnRules { RoundEvents = RoundEventTiming.AfterFaction };
        byte[] before = WorldSave.Encode(initial);
        var entry = WorldTurns.Resume(initial, initial.Revision, rules);
        Check(entry.State.Phase == TurnPhase.StartFactionEvents && entry.State.PendingDialogue == "hello", "pause after faction refresh");
        Check(entry.State.Factions.Select(f => f.Resources).SequenceEqual(initial.Factions.Select(f => f.Resources)), "round one preserves scenario funds");
        Check(entry.State.Units.Where(u => u.Owner == initial.ActiveFaction).All(u => !u.Acted && u.RemainingMovement == u.Combatant.Profile.Movement), "first faction refreshed before dialogue");
        Check(entry.State.Units.Single(u => u.Id == basis.Units[0].Id).Morale.Duration == 3, "opening morale retains full duration after refresh");
        Check(entry.Notices.Select(n => n.Kind).Take(2).SequenceEqual(new[] { "faction.started", "event.triggered" }), "observable refresh precedes event");
        Check(before.SequenceEqual(WorldSave.Encode(initial)), "entry does not mutate source");
        var restored = WorldSave.Decode(WorldSave.Encode(entry.State), entry.State.ContentRevision);
        var next = WorldTurns.Resume(restored, restored.Revision, rules, "hello");
        Check(next.State.PendingDialogue == "orders" && next.State.Phase == TurnPhase.StartFactionEvents, "resume next ordered event");
        Check(!next.Notices.Any(n => n.Kind == "faction.started"), "resume does not refresh faction twice");
        var done = WorldTurns.Resume(next.State, next.State.Revision, rules, "orders");
        Check(done.State.Phase == TurnPhase.Active && done.State.PendingDialogue == null && done.State.TriggeredEvents.Count == 2, "all opening dialogues complete");
        Check(!done.Notices.Any(n => n.Kind == "faction.started"), "last acknowledgement does not refresh twice");
        var disabled = WorldTurns.Resume(initial, initial.Revision, rules with { RoundEvents = RoundEventTiming.Disabled });
        Check(disabled.State.Phase == TurnPhase.Active && disabled.State.TriggeredEvents.Count == 0, "disabled policy skips round events");
        var early = WorldTurns.Resume(initial, initial.Revision, rules with { RoundEvents = RoundEventTiming.BeforeFaction });
        Check(early.State.Phase == TurnPhase.RoundEvents && early.State.Units.All(u => u.Acted), "before-faction policy still pauses before refresh");
        var secondRound = initial with { Round = 2,
            Cells = initial.Cells.Select((c, i) => i == 0 ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray(),
            Events = initial.Events.Select(e => e with
            { Rules = e.Rules.Select(r => r with { Round = 2 }).ToArray() }).ToArray() };
        var economic = rules with { Buildings = [new("city", 1, new(11, 7, 5))] };
        var paid = WorldTurns.Resume(secondRound, secondRound.Revision, economic);
        var funds = paid.State.Factions.Single(f => f.Id == secondRound.ActiveFaction).Resources;
        Check(funds != secondRound.Factions.Single(f => f.Id == secondRound.ActiveFaction).Resources, "later-round income occurs before after-faction dialogue");
        var summary = paid.State.Factions.Single(f => f.Id == secondRound.ActiveFaction).TurnSummary!;
        Check(summary.Round == 2 && summary.Income == new Resources(11, 7, 5), "summary records turn production rather than stock");
        Check(entry.State.Factions.Single(f => f.Id == initial.ActiveFaction).TurnSummary!.Income == new Resources(0, 0, 0), "opening round does not claim an income payment");
        var paidRestore = WorldSave.Decode(WorldSave.Encode(paid.State), paid.State.ContentRevision);
        var paidAgain = WorldTurns.Resume(paidRestore, paidRestore.Revision, economic, paidRestore.PendingDialogue!);
        Check(paidAgain.State.Factions.Single(f => f.Id == secondRound.ActiveFaction).Resources == funds, "save/resume never pays income twice");
        Check(paidAgain.State.Factions.Single(f => f.Id == secondRound.ActiveFaction).TurnSummary == summary, "save/resume retains original production summary");
        var capped = WorldTurns.Resume(secondRound, secondRound.Revision, economic with { ResourceCap = 9 });
        var cappedFaction = capped.State.Factions.Single(f => f.Id == secondRound.ActiveFaction);
        Check(cappedFaction.TurnSummary!.Income.Money == 11 && cappedFaction.Resources.Money == 9, "summary is production before storage cap");
        var overLimit = secondRound with { ArmyCapacity = new() { BaseCapacity = 0, Buildings = new Dictionary<string, int[]>() },
            Units = secondRound.Units.Select(u => u.Owner == secondRound.ActiveFaction ? u with { Support = u.Support with { FoodConsumption = 7 } } : u).ToArray() };
        var overPaid = WorldTurns.Resume(overLimit, overLimit.Revision, economic);
        var overSummary = overPaid.State.Factions.Single(f => f.Id == secondRound.ActiveFaction).TurnSummary!;
        Check(overSummary.Income == new Resources(5, 3, 2) && overSummary.FoodConsumption == 14, "summary includes overflow income penalty and doubled upkeep");
        Check(overLimit.Factions.All(f => f.TurnSummary == null), "settlement summary does not mutate input");
        Check(!System.Text.Encoding.UTF8.GetString(WorldSave.Encode(basis)).Contains("turnSummary"), "legacy state omits absent summary");
        try { WorldTurns.Resume(initial, initial.Revision, rules with { RoundEvents = (RoundEventTiming)99 }); }
        catch (RuleViolation error) { Check(error.Code == "turn.configuration", "invalid timing rejected"); return checks; }
        throw new InvalidOperationException("Invalid timing was accepted");
    }
}
