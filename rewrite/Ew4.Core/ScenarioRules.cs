using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ScenarioCondition
{
    public string Type { get; init; } = "always";
    public IReadOnlyList<ScenarioCondition> Conditions { get; init; } = Array.Empty<ScenarioCondition>();
    public string Faction { get; init; } = "@player";
    public string? OtherFaction { get; init; }
    public string? Relation { get; init; }
    public string? Camp { get; init; }
    public string? Unit { get; init; }
    public string? Commander { get; init; }
    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
    public string? Key { get; init; }
    public string? Profile { get; init; }
    public string? Category { get; init; }
    public string Compare { get; init; } = "ge";
    public long Value { get; init; } = 1;
    public string? Script { get; init; }
    public string? Function { get; init; }
}

public sealed record ScenarioEffect
{
    public string Type { get; init; } = "state";
    public string Faction { get; init; } = "@player";
    public string? OtherFaction { get; init; }
    public string? Relation { get; init; }
    public string? Camp { get; init; }
    public string? Name { get; init; }
    public string? TemplateFaction { get; init; }
    public string? Country { get; init; }
    public string? UnitVariant { get; init; }
    public IReadOnlyDictionary<string, RecruitmentGrant>? Recruitment { get; init; }
    public string? Flag { get; init; }
    public string? FlagImage { get; init; }
    public string? Color { get; init; }
    public ExtensionVictoryRules? VictoryRules { get; init; }
    public string? Outcome { get; init; }
    public string? Unit { get; init; }
    public IReadOnlyList<int> Cells { get; init; } = Array.Empty<int>();
    public string? Key { get; init; }
    public long Value { get; init; }
    public string? Script { get; init; }
    public string? Function { get; init; }
}

public sealed record ScenarioEventChoice
{
    public string Id { get; init; } = "ok";
    public string Text { get; init; } = "OK";
    public ScenarioCondition? Condition { get; init; }
    public IReadOnlyList<ScenarioEffect> Effects { get; init; } = Array.Empty<ScenarioEffect>();
}

public sealed record ScenarioPopup
{
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public string? Image { get; init; }
    public string ImageAlt { get; init; } = "";
    public IReadOnlyList<ScenarioEventChoice> Choices { get; init; } = new[] { new ScenarioEventChoice() };
}

public sealed record ScenarioEventRepeat
{
    public int EveryRounds { get; init; } = 1;
    public int StartRound { get; init; } = 1;
    public int? EndRound { get; init; }
    public int? MaxOccurrences { get; init; }
}

public sealed record ScenarioEventProgress(int Occurrences, int LastRound);

public sealed record ScriptedScenarioEvent
{
    public string Id { get; init; } = "";
    public ScenarioCondition Condition { get; init; } = new();
    public IReadOnlyList<ScenarioEffect> Effects { get; init; } = Array.Empty<ScenarioEffect>();
    public ScenarioPopup? Popup { get; init; }
    public ScenarioEventRepeat? Repeat { get; init; }
    public IReadOnlyList<ScenarioEffect> InsufficientResourcesEffects { get; init; } = Array.Empty<ScenarioEffect>();
}

// Conditions are queries; effects run on a copy and commit only after the whole transaction validates.
public static class ScenarioRules
{
    private const string PopupPrefix = "scenario.popup/";
    public static string DialogueId(string eventId) => PopupPrefix + eventId;
    public static ScriptedScenarioEvent? Pending(WorldState world) => world.ScenarioEvents
        .FirstOrDefault(e => e.Popup != null && DialogueId(e.Id) == world.PendingDialogue);
    private static string Faction(WorldState world, string selector) => selector == "@player" ? world.ScenarioPlayerFaction!
        : selector == "@active" ? world.ActiveFaction : selector;
    private static bool Owned(WorldState world, int cell, ScenarioCondition c) => world.Cells[cell].Owner is string owner
        && (c.Camp != null ? world.Factions.Single(f => f.Id == owner).Camp == c.Camp : owner == Faction(world, c.Faction));
    private static bool Compare(long actual, string op, long value) => op switch
    {
        "eq" => actual == value, "ne" => actual != value, "gt" => actual > value,
        "ge" => actual >= value, "lt" => actual < value, "le" => actual <= value,
        _ => throw new RuleViolation("scenario.compare", "Unknown comparison: " + op)
    };
    private static RuleScriptResult Script(WorldState world, string file, string function, long value, string ev, IRuleScriptRuntime? scripts)
        => (scripts ?? throw new RuleViolation("scenario.script", "Script runtime is unavailable")).Run(file, function,
            new(ev, null, null, 1, world.Round, value, world.ExtensionState, world,
                world.Factions.Single(f => f.Id == world.ScenarioPlayerFaction).Camp), new Ew4Random(world.RandomState));

    public static bool Match(WorldState world, ScenarioCondition c, IRuleScriptRuntime? scripts = null)
    {
        // Declared reinforcements need not exist until their creation event fires.
        if (c.Type is "money" or "industry" or "food" or "city_count" or "objective_count" or "territory_count"
            or "unit_count" or "kill_count" or "cells_owned" or "faction_defeated" or "faction_camp" or "active_faction"
            or "relation" or "faction_active"
            && (!world.Factions.Any(f => f.Id == Faction(world, c.Faction))
                || c.Type == "relation" && !world.Factions.Any(f => f.Id == Faction(world, c.OtherFaction!)))) return false;
        long Number()
        {
            var faction = world.Factions.Single(f => f.Id == Faction(world, c.Faction));
            switch (c.Type)
            {
                case "round": return world.Round;
                case "money": return faction.Resources.Money;
                case "industry": return faction.Resources.Industry;
                case "food": return faction.Resources.Food;
                case "city_count": return world.Cells.Select((cell, i) => (cell, i)).Count(p => p.cell.BuildingKind == "city" && Owned(world, p.i, c));
                case "objective_count": return world.Cells.Select((cell, i) => (cell, i)).Count(p => p.cell.Objective && Owned(world, p.i, c));
                case "territory_count": return Enumerable.Range(0, world.Cells.Count).Count(i => world.Cells[i].Terrain.CanTraverse() && Owned(world, i, c));
                case "unit_count": return world.Units.Count(u => (c.Camp == null ? u.Owner == faction.Id : world.Factions.Single(f => f.Id == u.Owner).Camp == c.Camp)
                    && (c.Profile == null || c.Profile == u.Combatant.Profile.Id) && (c.Category == null || c.Category == u.Combatant.Profile.Category));
                case "kill_count": return c.Key == null ? faction.Kills.Values.Sum() : faction.Kills.TryGetValue(c.Key, out long kills) ? kills : 0;
                case "unit_health": return world.Units.SingleOrDefault(u => u.Id == c.Unit)?.Combatant.Health ?? 0;
                case "building_level": return world.Cells[c.Cells[0]].BuildingLevel;
                case "state": return world.ExtensionState.TryGetValue(c.Key!, out long value) ? value : 0;
                default: throw new RuleViolation("scenario.condition", "Unknown condition: " + c.Type);
            }
        }
        switch (c.Type)
        {
            case "always": return true;
            case "faction_exists": return world.Factions.Any(f => f.Id == Faction(world, c.Faction));
            case "faction_active": return world.Factions.Any(f => f.Id == Faction(world, c.Faction) && !f.Defeated);
            case "all": return c.Conditions.All(v => Match(world, v, scripts));
            case "any": return c.Conditions.Any(v => Match(world, v, scripts));
            case "not": return !Match(world, c.Conditions[0], scripts);
            case "cells_owned": return c.Cells.All(i => Owned(world, i, c));
            case "unit_at": return world.Units.Any(u => u.Id == c.Unit && c.Cells.Contains(u.Cell));
            case "unit_alive": return world.Units.Any(u => u.Id == c.Unit);
            case "unit_destroyed": return world.DestroyedScenarioUnits.Contains(c.Unit!);
            case "commander_alive": return world.Units.Any(u => u.CommanderId == c.Commander);
            case "commander_dead": return world.DeadScenarioCommanders.Contains(c.Commander!);
            case "faction_defeated": return world.Factions.Single(f => f.Id == Faction(world, c.Faction)).Defeated;
            case "faction_camp": return world.Factions.Single(f => f.Id == Faction(world, c.Faction)).Camp == c.Camp;
            case "active_faction": return world.ActiveFaction == Faction(world, c.Faction);
            case "player_faction": return world.ScenarioPlayerFaction == Faction(world, c.Faction);
            case "relation": return WorldDiplomacy.Relation(world, Faction(world, c.Faction), Faction(world, c.OtherFaction!)) == c.Relation;
            case "event_count": return Compare(world.ScenarioEventProgress.TryGetValue(c.Key!, out var progress) ? progress.Occurrences
                : world.TriggeredScenarioEvents.Contains(c.Key!) ? 1 : 0, c.Compare, c.Value);
            case "event_completed": return world.TriggeredScenarioEvents.Contains(c.Key!) && (world.ScenarioEvents.Single(e => e.Id == c.Key).Popup == null
                || world.ScenarioChoices.ContainsKey(c.Key!));
            case "script": return Script(world, c.Script!, c.Function!, c.Value, "scenario.condition", scripts).Value != 0;
            default: return Compare(Number(), c.Compare, c.Value);
        }
    }

    public static WorldChange Evaluate(WorldState state, IRuleScriptRuntime? scripts = null)
    {
        if (state.ScenarioEvents.Count == 0 || state.PendingDialogue != null || state.Phase == TurnPhase.Finished) return new(state, Array.Empty<WorldNotice>());
        var next = state;
        var notices = new List<WorldNotice>();
        // Recheck chains, but a recurring event can only fire once in each eligible whole round.
        for (int step = 0; step < state.ScenarioEvents.Count; step++)
        {
            var ev = next.ScenarioEvents.FirstOrDefault(e => Due(next, e) && Match(next, e.Condition, scripts));
            if (ev == null) break;
            if (!next.TriggeredScenarioEvents.Contains(ev.Id))
                next = next with { TriggeredScenarioEvents = next.TriggeredScenarioEvents.Append(ev.Id).ToArray() };
            if (ev.Repeat != null)
            {
                var progress = next.ScenarioEventProgress.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                progress[ev.Id] = new(checked((progress.TryGetValue(ev.Id, out var old) ? old.Occurrences : 0) + 1), next.Round);
                next = next with { ScenarioEventProgress = progress };
            }
            notices.Add(new("scenario.triggered", ev.Id));
            if (ev.Popup != null)
            {
                var choices = next.ScenarioChoices.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                choices.Remove(ev.Id);
                next = next with { ScenarioChoices = choices };
                if (!ev.Popup.Choices.Any(c => c.Condition == null || Match(next, c.Condition, scripts)))
                    throw new RuleViolation("scenario.choices", "Popup has no available choice: " + ev.Id);
                next = next with { PendingDialogue = DialogueId(ev.Id) };
                break;
            }
            next = ApplyEvent(next, ev, ev.Effects, scripts, notices);
            if (next.PendingDialogue != null || next.Phase == TurnPhase.Finished) break;
        }
        if (notices.Count > 0) next = next with { Revision = checked(next.Revision + 1) };
        next.Validate();
        return new(next, notices);
    }

    public static WorldChange Choose(WorldState state, long revision, string eventId, string choiceId, IRuleScriptRuntime? scripts = null)
    {
        state.Validate();
        if (state.Revision != revision) throw new RuleViolation("action.stale", "Stale event choice");
        var ev = Pending(state);
        if (ev == null || ev.Id != eventId || state.ScenarioChoices.ContainsKey(eventId))
            throw new RuleViolation("scenario.choice", "No matching pending event");
        var choice = ev.Popup!.Choices.SingleOrDefault(c => c.Id == choiceId);
        if (choice == null || choice.Condition != null && !Match(state, choice.Condition, scripts))
            throw new RuleViolation("scenario.choice", "Unknown or unavailable choice");
        var choices = state.ScenarioChoices.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        choices.Add(eventId, choiceId);
        var next = state.Copy() with { PendingDialogue = state.QueuedDialogues.FirstOrDefault(), QueuedDialogues = state.QueuedDialogues.Skip(1).ToArray(),
            ScenarioChoices = choices, Revision = checked(state.Revision + 1) };
        var notices = new List<WorldNotice> { new("scenario.choice", eventId + "/" + choiceId) };
        next = ApplyEvent(next, ev, ev.Effects.Concat(choice.Effects), scripts, notices);
        var chained = Evaluate(next, scripts);
        return new(chained.State, notices.Concat(chained.Notices).ToArray());
    }

    private static bool Due(WorldState world, ScriptedScenarioEvent ev)
    {
        if (ev.Repeat is not ScenarioEventRepeat repeat) return !world.TriggeredScenarioEvents.Contains(ev.Id);
        return world.Round >= repeat.StartRound && (!repeat.EndRound.HasValue || world.Round <= repeat.EndRound.Value)
            && (world.Round - repeat.StartRound) % repeat.EveryRounds == 0
            && (!world.ScenarioEventProgress.TryGetValue(ev.Id, out var progress)
                || progress.LastRound < world.Round && (!repeat.MaxOccurrences.HasValue || progress.Occurrences < repeat.MaxOccurrences.Value));
    }

    private static WorldState ApplyEvent(WorldState state, ScriptedScenarioEvent ev, IEnumerable<ScenarioEffect> effects,
        IRuleScriptRuntime? scripts, List<WorldNotice> notices)
    {
        var committed = new List<WorldNotice>();
        WorldState next;
        try { next = Apply(state, effects, scripts, committed); }
        catch (RuleViolation error) when (error.Code == "scenario.resources" && ev.InsufficientResourcesEffects.Count > 0)
        {
            committed.Clear();
            next = Apply(state, ev.InsufficientResourcesEffects, scripts, committed);
            committed.Add(new("scenario.insufficient_resources", ev.Id));
        }
        notices.AddRange(committed);
        return next;
    }

    private static WorldState Apply(WorldState state, IEnumerable<ScenarioEffect> effects, IRuleScriptRuntime? scripts, List<WorldNotice> notices)
    {
        var next = state;
        foreach (var e in effects)
        {
            string owner = Faction(next, e.Faction);
            if (e.Type != "faction_create" && !next.Factions.Any(f => f.Id == owner))
                throw new RuleViolation("scenario.faction_missing", "Create the faction before applying effects: " + owner);
            long value = e.Value;
            if (e.Script != null)
            {
                double raw = Script(next, e.Script, e.Function!, e.Value, "scenario.effect", scripts).Value;
                if (double.IsNaN(raw) || double.IsInfinity(raw) || raw != Math.Truncate(raw) || Math.Abs(raw) > 9_007_199_254_740_991d)
                    throw new RuleViolation("scenario.value", "Effect script must return an exact integer");
                value = checked((long)raw);
            }
            switch (e.Type)
            {
                case "faction_create": next = ScenarioFactions.Create(next, e); notices.Add(new("faction.created", owner)); break;
                case "faction_revive": next = ScenarioFactions.Revive(next, owner); notices.Add(new("faction.revived", owner)); break;
                case "faction_identity": next = ScenarioFactions.Identity(next, owner, e); notices.Add(new("faction.identity", owner)); break;
                case "player_control":
                    next = ScenarioFactions.Control(next, owner); notices.Add(new("scenario.player_changed", owner)); break;
                case "victory_rules": next = next with { ScenarioVictory = ExtensionVictory.Copy(e.VictoryRules!) }; break;
                case "victory_default": next = next with { ScenarioVictory = null }; break;
                case "ending":
                    if (next.LocalMultiplayer != null)
                    {
                        if (e.Outcome == "victory")
                        {
                            next = LocalMultiplayerGame.Finish(next, owner);
                            if (next.LocalMultiplayer!.WinnerFaction == owner) next = next with { ScenarioEnding = e.Key };
                        }
                        break;
                    }
                    next = next with { ScenarioEnding = e.Key, Outcome = e.Outcome == "victory" ? BattleOutcome.Victory : BattleOutcome.Defeat,
                        Phase = TurnPhase.Finished }; break;
                case "relation": next = WorldDiplomacy.Set(next, owner, Faction(next, e.OtherFaction!), e.Relation!); break;
                case "money": case "industry": case "food": case "set_money": case "set_industry": case "set_food":
                    next = next with { Factions = next.Factions.Select(f =>
                    {
                        if (f.Id != owner) return f;
                        long Change(string key, long old) => e.Type == "set_" + key ? value : e.Type == key ? checked(old + value) : old;
                        var resources = new Resources(Change("money", f.Resources.Money), Change("industry", f.Resources.Industry), Change("food", f.Resources.Food));
                        if (resources.Money < 0 || resources.Industry < 0 || resources.Food < 0) throw new RuleViolation("scenario.resources", "Event would overdraw resources");
                        return f with { Resources = resources };
                    }).ToArray() }; break;
                case "camp":
                    next = next with { Factions = next.Factions.Select(f => f.Id == owner ? f with { Camp = e.Camp! } : f).ToArray() }; break;
                case "cells_owner":
                    if (next.Units.Any(u => e.Cells.Contains(u.Cell) && !WorldDiplomacy.Allied(next, u.Owner, owner)))
                        throw new RuleViolation("scenario.occupants", "Transfer occupants first before changing hostile territory");
                    next = next with { Cells = next.Cells.Select((c, i) => e.Cells.Contains(i) ? c with { Owner = owner } : c).ToArray() }; break;
                case "unit_owner":
                    var unit = next.Units.Single(u => u.Id == e.Unit);
                    next = next with { Units = next.Units.Select(u => u.Id == e.Unit ? u with { Owner = owner } : u).ToArray(),
                        Commanders = next.Commanders.Select(c => c.Id == unit.CommanderId ? c with { Owner = owner } : c).ToArray() }; break;
                case "morale":
                    var amount = checked((int)value);
                    if (amount < -3 || amount > 1) throw new RuleViolation("scenario.value", "Morale must be between -3 and 1");
                    next = next with { Units = next.Units.Select(u => u.Owner != owner ? u : u with {
                        Morale = MoraleState.Set(amount, u.Combatant.Has(Ew4Combat.Skill(3))),
                        Combatant = u.Combatant with { Morale = MoraleState.Set(amount, u.Combatant.Has(Ew4Combat.Skill(3))).Effective } }).ToArray() }; break;
                case "environment":
                    if (value < 0 || value > int.MaxValue) throw new RuleViolation("scenario.value", "Environment must be a nonnegative integer");
                    next = next with { Cells = next.Cells.Select((c, i) => e.Cells.Contains(i) ? c with { Environment = checked((int)value) } : c).ToArray() }; break;
                case "environment_spread": foreach (int cell in e.Cells) next = WorldEvents.Spread(next, cell); break;
                case "heal":
                    if (value < 0) throw new RuleViolation("scenario.value", "Healing must be nonnegative");
                    next = next with { Units = next.Units.Select(u => u.Id == e.Unit ? u with { Combatant = u.Combatant with {
                        Health = checked((int)Math.Min(u.Combatant.Profile.MaxHealth, checked(u.Combatant.Health + value))) } } : u).ToArray() }; break;
                case "destroy_unit":
                    if (next.Units.Any(u => u.Id == e.Unit))
                    { var death = WorldDeaths.Remove(next, e.Unit!); next = death.State; notices.AddRange(death.Notices); } break;
                case "state": case "add_state":
                    var counters = next.ExtensionState.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                    counters[e.Key!] = e.Type == "state" ? value : checked((counters.TryGetValue(e.Key!, out long old) ? old : 0) + value);
                    next = next with { ExtensionState = counters }; break;
                case "victory": case "defeat":
                    if (next.LocalMultiplayer != null)
                    {
                        if (e.Type == "victory") next = LocalMultiplayerGame.Finish(next, owner);
                        break;
                    }
                    next = next with { Outcome = e.Type == "victory" ? BattleOutcome.Victory : BattleOutcome.Defeat, Phase = TurnPhase.Finished }; break;
                default: throw new RuleViolation("scenario.effect", "Unknown effect: " + e.Type);
            }
        }
        next.Validate();
        return next;
    }

    public static IEnumerable<ScenarioCondition> Conditions(ScriptedScenarioEvent ev) => Walk(ev.Condition)
        .Concat(ev.Popup?.Choices.Where(c => c.Condition != null).SelectMany(c => Walk(c.Condition!)) ?? Enumerable.Empty<ScenarioCondition>());
    private static IEnumerable<ScenarioCondition> Walk(ScenarioCondition root)
    {
        var pending = new Stack<(ScenarioCondition Item, int Depth)>(); pending.Push((root, 0));
        int count = 0;
        while (pending.Count > 0)
        {
            var (item, depth) = pending.Pop();
            if (item == null || item.Conditions == null || depth > 12 || ++count > 1024) throw new RuleViolation("scenario.condition", "Invalid or excessive condition tree");
            yield return item;
            foreach (var child in item.Conditions) pending.Push((child, depth + 1));
        }
    }
    public static IEnumerable<ScenarioEffect> Effects(ScriptedScenarioEvent ev) => ev.Effects.Concat(ev.InsufficientResourcesEffects)
        .Concat(ev.Popup?.Choices.SelectMany(c => c.Effects) ?? Enumerable.Empty<ScenarioEffect>());
    public static void ValidateDefinitions(IReadOnlyList<ScriptedScenarioEvent> events)
    {
        if (events == null || events.Count > 1024 || events.Any(e => e == null || string.IsNullOrWhiteSpace(e.Id) || e.Id.Length > 128
            || e.Effects == null || e.InsufficientResourcesEffects == null
            || e.Repeat != null && (e.Repeat.EveryRounds < 1 || e.Repeat.StartRound < 1 || e.Repeat.EndRound < e.Repeat.StartRound || e.Repeat.MaxOccurrences < 1)
            || e.Popup != null && (e.Popup.Text == null || e.Popup.Title == null || e.Popup.ImageAlt == null || e.Popup.Choices == null
                || e.Popup.Image != null && (!e.Popup.Image.StartsWith("rewrite/", StringComparison.Ordinal) || !e.Popup.Image.EndsWith(".png", StringComparison.Ordinal)
                    || e.Popup.Image.Length >= 240 || e.Popup.Image.Contains('\\') || e.Popup.Image.Contains(':') || e.Popup.Image.Any(c => c < 32)
                    || e.Popup.Image.Split('/').Any(p => p.Length == 0 || p is "." or ".."))
                || e.Popup.Choices.Count == 0 || e.Popup.Choices.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Text) || c.Effects == null)
                || e.Popup.Choices.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != e.Popup.Choices.Count))
            || events.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != events.Count)
            throw new RuleViolation("scenario.definition", "Invalid event identity, effects or popup choices");
        foreach (var ev in events)
        {
            foreach (var c in Conditions(ev))
            {
                if (c.Type is not ("always" or "all" or "any" or "not" or "round" or "money" or "industry" or "food" or "city_count" or "objective_count"
                    or "territory_count" or "unit_count" or "kill_count" or "unit_health" or "building_level" or "state" or "cells_owned" or "unit_at"
                    or "unit_alive" or "unit_destroyed" or "commander_alive" or "commander_dead" or "faction_defeated" or "faction_exists" or "faction_active" or "faction_camp" or "active_faction" or "player_faction" or "event_completed" or "event_count" or "relation" or "script")
                    || c.Cells == null || c.Compare is not ("eq" or "ne" or "gt" or "ge" or "lt" or "le") || string.IsNullOrWhiteSpace(c.Faction)
                    || c.Type is "all" or "any" && c.Conditions.Count == 0 || c.Type == "not" && c.Conditions.Count != 1
                    || c.Type is "cells_owned" or "unit_at" or "building_level" && c.Cells.Count == 0
                    || c.Type is "unit_at" or "unit_alive" or "unit_destroyed" or "unit_health" && string.IsNullOrWhiteSpace(c.Unit)
                    || c.Type is "commander_alive" or "commander_dead" && string.IsNullOrWhiteSpace(c.Commander)
                    || c.Type is "state" or "event_completed" or "event_count" && string.IsNullOrWhiteSpace(c.Key)
                    || c.Type is "event_completed" or "event_count" && events.All(e => e.Id != c.Key)
                    || c.Type == "relation" && (string.IsNullOrWhiteSpace(c.OtherFaction) || c.Relation is not ("allied" or "neutral" or "hostile"))
                    || c.Camp != null && string.IsNullOrWhiteSpace(c.Camp) || c.Type == "faction_camp" && c.Camp == null
                    || c.Type != "script" && (c.Script != null || c.Function != null))
                    throw new RuleViolation("scenario.condition", "Invalid condition in " + ev.Id);
                Handler(c.Script, c.Function, c.Type == "script");
            }
            foreach (var e in Effects(ev))
            {
                if (e == null || e.Cells == null || string.IsNullOrWhiteSpace(e.Faction)
                    || e.Type is not ("money" or "industry" or "food" or "set_money" or "set_industry" or "set_food" or "camp" or "cells_owner"
                        or "unit_owner" or "morale" or "environment" or "environment_spread" or "heal" or "destroy_unit" or "state" or "add_state" or "victory" or "defeat" or "relation" or "faction_create" or "faction_revive" or "faction_identity"
                        or "player_control" or "victory_rules" or "victory_default" or "ending")
                    || (e.Type == "victory_rules") != (e.VictoryRules != null)
                    || e.Type is "player_control" or "victory_rules" or "victory_default" or "ending" && e.Script != null
                    || e.Type == "ending" && (string.IsNullOrWhiteSpace(e.Key) || e.Key.Length > 128 || e.Key.Any(char.IsControl)
                        || e.Outcome is not ("victory" or "defeat"))
                    || e.Type != "ending" && e.Outcome != null
                    || e.Type == "faction_create" && (e.Faction.StartsWith("@", StringComparison.Ordinal) || e.Faction.Length > 128
                        || !ScenarioFactions.Code(e.Faction)
                        || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 128 || string.IsNullOrWhiteSpace(e.Camp) || e.Camp.Length > 128
                        || e.TemplateFaction != null && (string.IsNullOrWhiteSpace(e.TemplateFaction) || e.TemplateFaction == "@active")
                        || e.TemplateFaction == null && (e.Flag == null && e.FlagImage == null || e.Color == null) || e.Script != null)
                    || e.Type == "faction_identity" && (e.Name == null && e.Flag == null && e.FlagImage == null && e.Color == null || e.Script != null)
                    || e.Type is not ("faction_create" or "faction_identity") && (e.Name != null || e.Flag != null || e.FlagImage != null || e.Color != null)
                    || e.Name != null && (string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 128 || e.Name.Any(char.IsControl))
                    || e.Type != "faction_create" && (e.TemplateFaction != null || e.Country != null || e.UnitVariant != null || e.Recruitment != null)
                    || e.Country != null && !ScenarioFactions.CountryId(e.Country)
                    || e.UnitVariant != null && !ScenarioFactions.Code(e.UnitVariant)
                    || e.Recruitment != null && e.Recruitment.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || string.IsNullOrWhiteSpace(p.Value.Veterancy))
                    || e.Type == "relation" && (string.IsNullOrWhiteSpace(e.OtherFaction) || e.Relation is not ("allied" or "neutral" or "hostile" or "default"))
                    || e.Type == "camp" && string.IsNullOrWhiteSpace(e.Camp)
                    || e.Type is "cells_owner" or "environment" or "environment_spread" && e.Cells.Count == 0
                    || e.Type is "unit_owner" or "heal" or "destroy_unit" && string.IsNullOrWhiteSpace(e.Unit)
                    || e.Type is "state" or "add_state" && (string.IsNullOrWhiteSpace(e.Key) || !e.Key.StartsWith("scenario/", StringComparison.Ordinal) || e.Key.Length > 512)
                    || e.Type == "morale" && e.Script == null && (e.Value < -3 || e.Value > 1)
                    || e.Type is "environment" or "heal" && e.Script == null && e.Value < 0)
                    throw new RuleViolation("scenario.effect", "Invalid effect in " + ev.Id);
                ScenarioFactions.ValidateAppearance(new(e.Flag, e.FlagImage, e.Color));
                if (e.VictoryRules != null) ExtensionVictory.Validate(e.VictoryRules);
                Handler(e.Script, e.Function, false);
            }
        }
    }
    private static void Handler(string? script, string? function, bool required)
    {
        if (required && script == null || (script == null) != (function == null) || script != null && (!script.StartsWith("rewrite/", StringComparison.Ordinal)
            || !script.EndsWith(".lua", StringComparison.Ordinal) || script.Contains('\\') || script.Contains(':') || script.Split('/').Any(p => p == ".." || p == "." || p.Length == 0)
            || string.IsNullOrWhiteSpace(function) || !function!.All(c => c == '_' || char.IsLetterOrDigit(c))))
            throw new RuleViolation("scenario.script", "Invalid scenario script handler");
    }

    public static void Validate(WorldState world, bool initial = false)
    {
        if (world.ScenarioEvents == null || world.TriggeredScenarioEvents == null || world.ScenarioChoices == null || world.ScenarioEventProgress == null
            || world.DestroyedScenarioUnits == null || world.DeadScenarioCommanders == null)
            throw new RuleViolation("scenario.state", "Null scenario state collection");
        if (world.ScenarioVictory != null)
        {
            ExtensionVictory.Validate(world.ScenarioVictory);
            ExtensionVictory.ValidateTargets(world.ScenarioVictory, world, false,
                id => world.Factions.Any(f => f.Id == id) || ScenarioFactions.Creations(world.ScenarioEvents).Any(e => e.Faction == id));
        }
        if (world.ScenarioEnding != null && (string.IsNullOrWhiteSpace(world.ScenarioEnding) || world.ScenarioEnding.Length > 128
            || world.ScenarioEnding.Any(char.IsControl) || world.Phase != TurnPhase.Finished || world.Outcome == BattleOutcome.Continue))
            throw new RuleViolation("scenario.ending", "An ending requires a finished battle and a valid branch identifier");
        if (world.DestroyedScenarioUnits.Any(string.IsNullOrWhiteSpace) || world.DestroyedScenarioUnits.Distinct().Count() != world.DestroyedScenarioUnits.Count
            || world.DeadScenarioCommanders.Any(string.IsNullOrWhiteSpace) || world.DeadScenarioCommanders.Distinct().Count() != world.DeadScenarioCommanders.Count)
            throw new RuleViolation("scenario.state", "Invalid death history");
        if (world.ScenarioEvents.Count == 0)
        {
            if (world.TriggeredScenarioEvents.Count != 0 || world.ScenarioChoices.Count != 0 || world.ScenarioEventProgress.Count != 0)
                throw new RuleViolation("scenario.state", "Event history has no definitions");
            return;
        }
        ValidateDefinitions(world.ScenarioEvents);
        var creations = ScenarioFactions.Creations(world.ScenarioEvents).ToArray();
        if (creations.Select(e => e.Faction).Distinct(StringComparer.Ordinal).Count() != creations.Length
            || creations.Any(e => initial && world.Factions.Any(f => f.Id == e.Faction)
                || creations.Any(other => other.Faction == e.TemplateFaction)
                || e.TemplateFaction != null && !world.Factions.Any(f => f.Id == Faction(world, e.TemplateFaction))))
            throw new RuleViolation("scenario.faction_definition", "New factions need unique IDs and an existing initial template");
        foreach (var p in world.ScenarioEventProgress)
        {
            var repeat = world.ScenarioEvents.SingleOrDefault(e => e.Id == p.Key)?.Repeat;
            if (repeat == null || p.Value == null || !world.TriggeredScenarioEvents.Contains(p.Key) || p.Value.Occurrences < 1
                || p.Value.LastRound < repeat.StartRound || p.Value.LastRound > world.Round || p.Value.LastRound > repeat.EndRound
                || (p.Value.LastRound - repeat.StartRound) % repeat.EveryRounds != 0 || p.Value.Occurrences > repeat.MaxOccurrences
                || p.Value.Occurrences > (p.Value.LastRound - repeat.StartRound) / repeat.EveryRounds + 1)
                throw new RuleViolation("scenario.state", "Invalid recurring event progress");
        }
        bool KnownFaction(string selector) => selector is "@player" or "@active" || world.Factions.Any(f => f.Id == selector)
            || creations.Any(e => e.Faction == selector);
        if (!world.Factions.Any(f => f.Id == world.ScenarioPlayerFaction) || world.TriggeredScenarioEvents.Distinct().Count() != world.TriggeredScenarioEvents.Count
            || world.TriggeredScenarioEvents.Any(id => world.ScenarioEvents.All(e => e.Id != id))
            || world.ScenarioChoices.Any(p => !world.TriggeredScenarioEvents.Contains(p.Key)
                || !world.ScenarioEvents.Any(e => e.Id == p.Key && e.Popup != null && e.Popup.Choices.Any(c => c.Id == p.Value))))
            throw new RuleViolation("scenario.state", "Invalid scenario event state");
        foreach (var ev in world.ScenarioEvents)
        {
            if (ev.Repeat != null && world.TriggeredScenarioEvents.Contains(ev.Id) && !world.ScenarioEventProgress.ContainsKey(ev.Id))
                throw new RuleViolation("scenario.state", "Recurring event is missing progress");
            foreach (var c in Conditions(ev))
                if (!KnownFaction(c.Faction) || c.OtherFaction != null && !KnownFaction(c.OtherFaction)
                    || c.Cells.Any(i => i < 0 || i >= world.Cells.Count || !world.Cells[i].Terrain.CanTraverse())
                    || initial && (c.Unit != null && world.Units.All(u => u.Id != c.Unit) || c.Commander != null && world.Commanders.All(k => k.Id != c.Commander)))
                    throw new RuleViolation("scenario.reference", "Unknown condition target in " + ev.Id);
            foreach (var e in Effects(ev))
            {
                if (!KnownFaction(e.Faction) || e.OtherFaction != null && (!KnownFaction(e.OtherFaction) || e.Faction == e.OtherFaction
                        || initial && e.Faction != "@active" && e.OtherFaction != "@active" && Faction(world, e.Faction) == Faction(world, e.OtherFaction))
                    || e.Cells.Any(i => i < 0 || i >= world.Cells.Count || !world.Cells[i].Terrain.CanTraverse())
                    || initial && e.Unit != null && world.Units.All(u => u.Id != e.Unit))
                    throw new RuleViolation("scenario.reference", "Unknown effect target in " + ev.Id);
                if (e.VictoryRules != null) ExtensionVictory.ValidateTargets(e.VictoryRules, world, initial, id => !id.StartsWith("@", StringComparison.Ordinal) && KnownFaction(id));
            }
            if (ev.Popup != null && world.TriggeredScenarioEvents.Contains(ev.Id) && !world.ScenarioChoices.ContainsKey(ev.Id)
                && world.PendingDialogue != DialogueId(ev.Id) && !world.QueuedDialogues.Contains(DialogueId(ev.Id)))
                throw new RuleViolation("scenario.state", "Unresolved event popup is missing");
        }
    }

    public static ScriptedScenarioEvent Copy(ScriptedScenarioEvent e) => e with { Condition = Copy(e.Condition), Effects = e.Effects.Select(Copy).ToArray(),
        InsufficientResourcesEffects = e.InsufficientResourcesEffects.Select(Copy).ToArray(), Repeat = e.Repeat == null ? null : e.Repeat with { },
        Popup = e.Popup == null ? null : e.Popup with { Choices = e.Popup.Choices.Select(c => c with {
            Condition = c.Condition == null ? null : Copy(c.Condition), Effects = c.Effects.Select(Copy).ToArray() }).ToArray() } };
    private static ScenarioCondition Copy(ScenarioCondition c) => c with { Cells = c.Cells.ToArray(), Conditions = c.Conditions.Select(Copy).ToArray() };
    private static ScenarioEffect Copy(ScenarioEffect e) => e with { Cells = e.Cells.ToArray(),
        VictoryRules = e.VictoryRules == null ? null : ExtensionVictory.Copy(e.VictoryRules),
        Recruitment = e.Recruitment?.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) };
}
