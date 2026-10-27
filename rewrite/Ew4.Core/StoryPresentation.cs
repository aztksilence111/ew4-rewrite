using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record StoryLibrary
{
    public int Schema { get; init; } = 1;
    public IReadOnlyList<string> SceneFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Backgrounds { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, StoryCharacter> Characters { get; init; } = new Dictionary<string, StoryCharacter>();
    public IReadOnlyDictionary<string, StoryScene> Scenes { get; init; } = new Dictionary<string, StoryScene>();
    public IReadOnlyDictionary<string, StoryBinding> Bindings { get; init; } = new Dictionary<string, StoryBinding>();
    public IReadOnlyDictionary<string, string> Texts { get; init; } = new Dictionary<string, string>();
    public StoryTheme Theme { get; init; } = new();
}
public sealed record StoryTheme(string Panel = "#0b1013", string Text = "#f2f1e9", string Accent = "#dac690",
    double Opacity = .88, int DialoguePercent = 26, int FontSize = 24);
public sealed record StoryCharacter
{
    public string Name { get; init; } = "";
    public IReadOnlyDictionary<string, StoryPose> Poses { get; init; } = new Dictionary<string, StoryPose>();
}
public sealed record StoryPose
{
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 1200;
    public string? Image { get; init; }
    public IReadOnlyDictionary<string, StoryLayer> Layers { get; init; } = new Dictionary<string, StoryLayer>();
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Expressions { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();
}
public sealed record StoryLayer
{
    public string Default { get; init; } = "";
    public int Order { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public IReadOnlyDictionary<string, string> Parts { get; init; } = new Dictionary<string, string>();
}
public sealed record StoryActor
{
    public string Id { get; init; } = "";
    public string? Character { get; init; }
    public string? Slot { get; init; }
    public string? Pose { get; init; }
    public string? Expression { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Scale { get; init; }
    public bool? Mirror { get; init; }
    public int? Order { get; init; }
    public bool Hidden { get; init; }
    public IReadOnlyDictionary<string, string>? Parts { get; init; }
}
public sealed record StoryAnimation(string Actor = "", string Type = "hop", int DurationMs = 300,
    double Amplitude = 12, double X = 0, double Y = 0, bool Wait = true);
public sealed record StoryChoice
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public string? Next { get; init; }
    public ScenarioCondition? Condition { get; init; }
    public IReadOnlyList<ScenarioEffect> Effects { get; init; } = Array.Empty<ScenarioEffect>();
}
public sealed record StoryStep
{
    public string Id { get; init; } = "";
    public string? Background { get; init; }
    public string? Speaker { get; init; }
    public string? Name { get; init; }
    public string Text { get; init; } = "";
    public string? TextKey { get; init; }
    public bool ClearActors { get; init; }
    public IReadOnlyList<StoryActor> Actors { get; init; } = Array.Empty<StoryActor>();
    public IReadOnlyList<StoryActor> UpdateActors { get; init; } = Array.Empty<StoryActor>();
    public IReadOnlyList<StoryAnimation> Animations { get; init; } = Array.Empty<StoryAnimation>();
    public IReadOnlyList<StoryChoice> Choices { get; init; } = Array.Empty<StoryChoice>();
    public string? Next { get; init; }
    public bool End { get; init; }
    public string? Result { get; init; }
    public int WaitMs { get; init; }
}
public sealed record StoryScene(string Id = "", IReadOnlyList<StoryStep>? Steps = null, int Schema = 1);
public sealed record StoryCall(string Key = "", string Scene = "");
public sealed record StoryEnding(string Key = "", string Scene = "", string Outcome = "victory", string? Ending = null);
public sealed record StoryBinding(StoryCall? OpeningStory = null, IReadOnlyList<StoryEnding>? EndingStories = null);
public sealed record StoryRun
{
    public string Id { get; init; } = "";
    public string Key { get; init; } = "";
    public string Scene { get; init; } = "";
    public string Kind { get; init; } = "event";
    public string? SourceEvent { get; init; }
    public int Occurrence { get; init; }
    public bool Started { get; init; }
    public bool Completed { get; init; }
    public string? Result { get; init; }
    public IReadOnlyList<string> Path { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Choices { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<ScenarioEffect> Continuation { get; init; } = Array.Empty<ScenarioEffect>();
    public IReadOnlyList<ScenarioEffect> PendingEffects { get; init; } = Array.Empty<ScenarioEffect>();
}
public sealed record StoryState
{
    public StoryLibrary Library { get; init; } = new();
    public IReadOnlyList<StoryRun> Runs { get; init; } = Array.Empty<StoryRun>();
    public bool EndingBound { get; init; }
}

public static class StoryPlayback
{
    public static string DialogueId(string id) => "story/" + id;
    public static StoryRun? Current(WorldState w) => w.Story?.Runs.FirstOrDefault(r => !r.Completed && DialogueId(r.Id) == w.PendingDialogue);
    public static bool Known(WorldState w, string id) => w.Story?.Runs.Any(r => !r.Completed && DialogueId(r.Id) == id) == true;
    public static bool EventPending(WorldState w, string id) => w.Story?.Runs.Any(r => !r.Completed && r.SourceEvent == id) == true;
    public static StoryStep Step(StoryState state, StoryRun run) => state.Library.Scenes[run.Scene].Steps!.Single(s => s.Id == run.Path.Last());
    private static WorldState Replace(WorldState w, StoryRun run) => w with { Story = w.Story! with {
        Runs = w.Story!.Runs.Select(r => r.Id == run.Id ? run : r).ToArray() } };
    public static WorldState Synchronize(WorldState w)
    {
        var run = Current(w);
        return run != null && !run.Started ? Replace(w, run with { Started = true }) : w;
    }
    public static WorldState Enqueue(WorldState w, string key, string scene, string kind = "event", string? sourceEvent = null,
        IReadOnlyList<ScenarioEffect>? continuation = null)
    {
        if (w.Story == null || !w.Story.Library.Scenes.ContainsKey(scene) || string.IsNullOrWhiteSpace(key))
            throw new RuleViolation("story.reference", "Unknown story scene or call key: " + scene);
        if (w.Story.Runs.Count >= 2048) throw new RuleViolation("story.limit", "Story call budget exceeded");
        int occurrence = sourceEvent == null ? 0 : w.ScenarioEventProgress.TryGetValue(sourceEvent, out var p) ? p.Occurrences : 1;
        var run = new StoryRun { Id = "run" + (w.Story.Runs.Count + 1), Key = key, Scene = scene, Kind = kind,
            SourceEvent = sourceEvent, Occurrence = occurrence, Path = new[] { w.Story.Library.Scenes[scene].Steps![0].Id },
            Started = w.PendingDialogue == null, Continuation = continuation ?? Array.Empty<ScenarioEffect>() };
        string id = DialogueId(run.Id);
        return w with { Story = w.Story with { Runs = w.Story.Runs.Append(run).ToArray() },
            PendingDialogue = w.PendingDialogue ?? id,
            QueuedDialogues = w.PendingDialogue == null ? w.QueuedDialogues : w.QueuedDialogues.Append(id).ToArray() };
    }
    public static WorldState BindEnding(WorldState w)
    {
        if (w.Story == null || w.Outcome == BattleOutcome.Continue || w.Story.EndingBound) return Synchronize(w);
        var binding = w.Story.Library.Bindings.TryGetValue(w.ScenarioId, out var b) ? b : null;
        var endings = binding?.EndingStories ?? Array.Empty<StoryEnding>();
        string outcome = w.Outcome == BattleOutcome.Victory ? "victory" : "defeat";
        var ending = endings.FirstOrDefault(e => e.Outcome == outcome && e.Ending != null && e.Ending == w.ScenarioEnding)
            ?? endings.FirstOrDefault(e => e.Outcome == outcome && e.Ending == null);
        // An active scene finishes its selected route; ordinary queued events cannot interrupt the ending.
        var current = Current(w);
        var keep = current == null ? Array.Empty<StoryRun>() : new[] { current };
        w = w with { Story = w.Story with { EndingBound = true, Runs = w.Story.Runs.Select(r => r.Completed || keep.Contains(r)
            ? r : r with { Completed = true, Result = "cancelled", Continuation = Array.Empty<ScenarioEffect>(), PendingEffects = Array.Empty<ScenarioEffect>() }).ToArray() },
            PendingDialogue = current == null ? w.PendingDialogue : DialogueId(current.Id),
            QueuedDialogues = w.QueuedDialogues.Where(id => !Known(w, id)).ToArray() };
        return ending == null ? w : Enqueue(w, ending.Key, ending.Scene, "ending");
    }
    public static bool BlocksSettlement(WorldState w) => w.Story != null && (!w.Story.EndingBound
        || w.Story.Runs.Any(r => !r.Completed));
    public static bool Match(WorldState w, ScenarioCondition c)
    {
        var all = w.Story?.Runs.Where(r => r.Key == c.Key).ToArray() ?? Array.Empty<StoryRun>();
        if (c.Type == "story_count") return ScenarioRules.Compare(all.Count(r => r.Completed && r.Result != "cancelled"), c.Compare, c.Value);
        var candidates = c.Scope == "any" ? all : all.TakeLast(1);
        return candidates.Any(r => c.Type switch {
            "story_started" => r.Started || DialogueId(r.Id) == w.PendingDialogue,
            "story_completed" => r.Completed && r.Result != "cancelled",
            "story_choice" => r.Choices.TryGetValue(c.Node!, out var selected) && selected == c.Choice,
            "story_result" => r.Completed && r.Result == c.Result,
            _ => false });
    }
    public static WorldState DecisionWorld(WorldState w, StoryRun run, IRuleScriptRuntime? scripts = null) => run.PendingEffects.Count == 0 ? w
        : ScenarioRules.ApplyStoryEffects(w, run.PendingEffects.Where(e => e.Type != "play_story"), scripts, new List<WorldNotice>(), run.SourceEvent);
    public static WorldChange Advance(WorldState w, string instance, string node, string? choice, bool skip, IRuleScriptRuntime? scripts = null)
    {
        var run = Current(w);
        if (run == null || run.Id != instance || run.Path.Last() != node)
            throw new RuleViolation("story.stale", "Story instance or step changed");
        var notices = new List<WorldNotice>();
        for (int budget = 0; budget < 2048; budget++)
        {
            run = Current(w)!;
            var scene = w.Story!.Library.Scenes[run.Scene];
            var step = Step(w.Story, run);
            string? next = step.Next;
            if (step.Choices.Count > 0)
            {
                if (choice == null)
                {
                    if (skip) break;
                    throw new RuleViolation("story.choice_required", "Select a story choice");
                }
                var option = step.Choices.SingleOrDefault(c => c.Id == choice);
                var decision = DecisionWorld(w, run, scripts);
                if (option == null || option.Condition != null && !ScenarioRules.Match(decision, option.Condition, scripts))
                    throw new RuleViolation("story.choice", "Unknown or unavailable story choice");
                if (decision.Outcome != BattleOutcome.Continue && option.Effects.Any(e => e.Type is not ("state" or "add_state" or "play_story")))
                    throw new RuleViolation("story.ending_locked", "Ending choices cannot change the locked battle result");
                run = run with { Choices = run.Choices.Concat(new[] { new KeyValuePair<string, string>(step.Id, choice) }).ToDictionary(p => p.Key, p => p.Value),
                    PendingEffects = run.PendingEffects.Concat(option.Effects).Select(ScenarioRules.CopyEffect).ToArray() };
                w = Replace(w, run);
                // Validate against an isolated projection; the battle remains unchanged until the scene ends.
                _ = DecisionWorld(w, run, scripts);
                next = option.Next;
                choice = null;
                notices.Add(new("story.choice", run.Key + "/" + step.Id + "/" + option.Id));
            }
            else if (choice != null) throw new RuleViolation("story.choice", "No choice is waiting");
            if (next == null && !step.End)
            {
                int index = scene.Steps!.ToList().FindIndex(s => s.Id == step.Id);
                next = scene.Steps.ElementAtOrDefault(index + 1)?.Id;
            }
            if (step.End || next == null)
            {
                var effects = run.PendingEffects;
                var remaining = run.Continuation;
                run = run with { Completed = true, Result = step.Result ?? "completed", Continuation = Array.Empty<ScenarioEffect>(), PendingEffects = Array.Empty<ScenarioEffect>() };
                w = Replace(w, run) with { PendingDialogue = w.QueuedDialogues.FirstOrDefault(), QueuedDialogues = w.QueuedDialogues.Skip(1).ToArray() };
                w = ScenarioRules.ApplyStoryEffects(w, effects, scripts, notices, run.SourceEvent);
                if (w.Outcome == BattleOutcome.Continue) w = ScenarioRules.ApplyStoryEffects(w, remaining, scripts, notices, run.SourceEvent);
                notices.Add(new("story.completed", run.Key));
                break;
            }
            run = run with { Path = run.Path.Append(next).ToArray() };
            w = Replace(w, run);
            if (!skip) break;
        }
        w = Synchronize(w) with { Revision = checked(w.Revision + 1) };
        return new(w, notices);
    }
}
