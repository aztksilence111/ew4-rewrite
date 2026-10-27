using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Ew4.Core;

public static class StoryValidation
{
    private static readonly ConditionalWeakTable<StoryLibrary, object> validated = new();
    public static bool Id(string? id) => id != null && id.Length is > 0 and <= 128 && id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_');
    public static bool Media(string? path) => path != null && path.Length < 240 && (path.StartsWith("rewrite/", StringComparison.Ordinal)
        || path.StartsWith("client/", StringComparison.Ordinal)) && path.EndsWith(".png", StringComparison.Ordinal)
        && path.Split('/').All(s => s.Length > 0 && s is not ("." or "..")) && !path.Contains('\\') && !path.Contains(':') && path.All(c => c >= 32);
    private static void Check([DoesNotReturnIf(false)] bool ok, string location)
    { if (!ok) throw new RuleViolation("story.definition", "Invalid story definition: " + location); }
    public static void Library(StoryLibrary l)
    {
        Check(l.Schema == 1 && l.Scenes != null && l.Characters != null && l.Backgrounds != null && l.Bindings != null && l.Texts != null
            && l.Theme != null && l.SceneFiles != null, "catalog");
        Check(l.Scenes.Count is > 0 and <= 128 && l.Characters.Count <= 256 && l.Backgrounds.Count <= 256, "catalog size");
        Check(l.Theme.DialoguePercent is >= 22 and <= 40 && l.Theme.FontSize is >= 16 and <= 36 && l.Theme.Opacity is >= .5 and <= 1
            && new[] { l.Theme.Panel, l.Theme.Text, l.Theme.Accent }.All(c => c != null && Regex.IsMatch(c, "^#[0-9a-fA-F]{6}$")), "theme");
        foreach (var bg in l.Backgrounds) Check(Id(bg.Key) && Media(bg.Value), "background " + bg.Key);
        foreach (var text in l.Texts) Check(Id(text.Key) && text.Value != null && text.Value.Length <= 16000, "text " + text.Key);
        foreach (var c in l.Characters)
        {
            Check(Id(c.Key) && c.Value != null && c.Value.Name != null && c.Value.Name.Length <= 128 && c.Value.Poses != null && c.Value.Poses.Count > 0, "character " + c.Key);
            foreach (var p in c.Value.Poses)
            {
                var pose = p.Value;
                Check(Id(p.Key) && pose != null && pose.Width is > 0 and <= 4096 && pose.Height is > 0 and <= 4096 && pose.Layers != null
                    && pose.Expressions != null && (pose.Image != null || pose.Layers.Count > 0) && (pose.Image == null || Media(pose.Image)) && pose.Layers.Count <= 16, c.Key + "/" + p.Key);
                foreach (var layer in pose.Layers)
                    Check(Id(layer.Key) && layer.Value != null && layer.Value.Parts != null && layer.Value.Parts.Count is > 0 and <= 128
                        && layer.Value.Parts.ContainsKey(layer.Value.Default) && layer.Value.Parts.All(part => Id(part.Key) && Media(part.Value))
                        && layer.Value.Width is not (<= 0 or > 4096) && layer.Value.Height is not (<= 0 or > 4096), "layer " + layer.Key);
                foreach (var expression in pose.Expressions)
                    Check(Id(expression.Key) && expression.Value != null && expression.Value.All(part => pose.Layers.TryGetValue(part.Key, out var layer)
                        && layer.Parts.ContainsKey(part.Value)), "expression " + expression.Key);
            }
        }
        foreach (var pair in l.Scenes)
        {
            var scene = pair.Value;
            Check(Id(pair.Key) && scene != null && scene.Schema == 1 && scene.Id == pair.Key && scene.Steps != null && scene.Steps.Count is > 0 and <= 1000, "scene " + pair.Key);
            Check(scene.Steps.All(s => s != null && Id(s.Id)) && scene.Steps.Select(s => s.Id).Distinct().Count() == scene.Steps.Count, scene.Id + " step IDs");
            var byId = scene.Steps.ToDictionary(s => s.Id);
            foreach (var step in scene.Steps)
            {
                Check(step.Text != null && step.Text.Length <= 16000 && (step.TextKey == null || l.Texts.ContainsKey(step.TextKey))
                    && step.Actors != null && step.UpdateActors != null && step.Animations != null && step.Choices != null && step.WaitMs is >= 0 and <= 60000
                    && (step.Background == null || l.Backgrounds.ContainsKey(step.Background)) && (step.Next == null || byId.ContainsKey(step.Next))
                    && (step.Result == null || step.End && Id(step.Result)) && (!step.End || step.Next == null && step.Choices.Count == 0), scene.Id + "/" + step.Id);
                Check(step.Actors.Count + step.UpdateActors.Count <= 16 && step.Animations.Count <= 32 && step.Choices.Count <= 32, step.Id + " limits");
                foreach (var actor in step.Actors.Concat(step.UpdateActors))
                {
                    Check(actor != null && Id(actor.Id) && (actor.Character == null || l.Characters.ContainsKey(actor.Character))
                        && (actor.Slot == null || new[] { "left", "midLeft", "center", "midRight", "right" }.Contains(actor.Slot))
                        && (actor.X == null || actor.X is >= 0 and <= 1) && (actor.Y == null || actor.Y is >= 0 and <= 1)
                        && (actor.Scale == null || actor.Scale is > 0 and <= 3), step.Id + " actor");
                    if (actor.Character != null)
                    {
                        var character = l.Characters[actor.Character];
                        string poseName = actor.Pose ?? character.Poses.Keys.First();
                        Check(character.Poses.TryGetValue(poseName, out var pose)
                            && (actor.Expression == null || pose.Expressions.ContainsKey(actor.Expression))
                            && (actor.Parts == null || actor.Parts.All(part => pose.Layers.TryGetValue(part.Key, out var layer) && layer.Parts.ContainsKey(part.Value))), step.Id + " pose/parts");
                    }
                }
                foreach (var a in step.Animations)
                    Check(a != null && Id(a.Actor) && a.Type is "hop" or "shake" or "move" or "fade" && a.DurationMs is >= 0 and <= 10000
                        && Math.Abs(a.Amplitude) <= 200 && Math.Abs(a.X) <= 2000 && Math.Abs(a.Y) <= 2000, step.Id + " animation");
                Check(step.Choices.Select(c => c.Id).Distinct().Count() == step.Choices.Count, step.Id + " choice IDs");
                foreach (var c in step.Choices)
                    Check(c != null && Id(c.Id) && !string.IsNullOrWhiteSpace(c.Text) && c.Text.Length <= 2000 && c.Next != null && byId.ContainsKey(c.Next)
                        && c.Effects != null && c.Effects.Count <= 64 && c.Effects.Select((e, i) => e.Type != "play_story" || i == c.Effects.Count - 1).All(v => v), step.Id + " choice");
            }
            var visiting = new HashSet<string>(); var visited = new HashSet<string>();
            void Visit(string id)
            {
                Check(!visiting.Contains(id), scene.Id + " cycle at " + id);
                if (!visited.Add(id)) return;
                visiting.Add(id);
                var s = byId[id];
                if (s.Choices.Count > 0) foreach (var c in s.Choices) Visit(c.Next!);
                else if (!s.End)
                {
                    string? next = s.Next ?? scene.Steps.ElementAtOrDefault(scene.Steps.ToList().FindIndex(x => x.Id == id) + 1)?.Id;
                    if (next != null) Visit(next);
                }
                visiting.Remove(id);
            }
            foreach (var step in scene.Steps) Visit(step.Id);
        }
        var activeScenes = new HashSet<string>(); var checkedScenes = new HashSet<string>();
        void VisitScene(string id)
        {
            Check(!activeScenes.Contains(id), "scene call cycle at " + id);
            if (!checkedScenes.Add(id)) return;
            activeScenes.Add(id);
            foreach (var effect in l.Scenes[id].Steps!.SelectMany(s => s.Choices).SelectMany(c => c.Effects).Where(e => e.Type == "play_story"))
            {
                Check(effect.Scene != null && l.Scenes.ContainsKey(effect.Scene), "called scene " + effect.Scene);
                VisitScene(effect.Scene);
            }
            activeScenes.Remove(id);
        }
        foreach (string id in l.Scenes.Keys) VisitScene(id);
        foreach (var b in l.Bindings)
        {
            Check(!string.IsNullOrWhiteSpace(b.Key) && b.Value != null, "binding");
            if (b.Value.OpeningStory is { } opening) Check(Id(opening.Key) && l.Scenes.ContainsKey(opening.Scene), "opening " + b.Key);
            var endings = b.Value.EndingStories ?? Array.Empty<StoryEnding>();
            Check(endings.All(e => e != null && Id(e.Key) && l.Scenes.ContainsKey(e.Scene) && e.Outcome is "victory" or "defeat"
                && (e.Ending == null || Id(e.Ending))) && endings.Select(e => (e.Outcome, e.Ending)).Distinct().Count() == endings.Count, "endings " + b.Key);
        }
    }
    public static void State(WorldState w)
    {
        if (w.Story == null) return;
        var s = w.Story;
        Check(s.Library != null && s.Runs != null && s.Runs.Count <= 2048 && s.Runs.Select(r => r.Id).Distinct().Count() == s.Runs.Count, "save header");
        validated.GetValue(s.Library, l => { Library(l); return new object(); });
        foreach (var r in s.Runs)
        {
            Check(r != null && Id(r.Id) && Id(r.Key) && s.Library.Scenes.ContainsKey(r.Scene) && r.Path != null && r.Path.Count is > 0 and <= 1000
                && r.Choices != null && r.Continuation != null && r.PendingEffects != null && r.PendingEffects.Count <= 64000
                && (!r.Completed || r.PendingEffects.Count == 0) && r.Kind is "opening" or "event" or "ending", "save call");
            if (r.PendingEffects.Count > 0) ScenarioRules.ValidateDefinitions(new[] { new ScriptedScenarioEvent { Id = "pending", Effects = r.PendingEffects } });
            var scene = s.Library.Scenes[r.Scene];
            Check(r.Path[0] == scene.Steps![0].Id && r.Path.All(id => scene.Steps.Any(n => n.Id == id)) && r.Path.Distinct().Count() == r.Path.Count
                && r.Choices.All(c => scene.Steps!.Any(n => n.Id == c.Key && n.Choices.Any(v => v.Id == c.Value)))
                && (r.Completed || w.PendingDialogue == StoryPlayback.DialogueId(r.Id) || w.QueuedDialogues.Contains(StoryPlayback.DialogueId(r.Id))), "save path " + r.Id);
            for (int i = 0; i < r.Path.Count - 1; i++)
            {
                var step = scene.Steps.Single(n => n.Id == r.Path[i]);
                string? next = step.Choices.Count > 0 && r.Choices.TryGetValue(step.Id, out var choice) ? step.Choices.Single(c => c.Id == choice).Next
                    : step.Choices.Count > 0 ? null : step.End ? null : step.Next ?? scene.Steps.ElementAtOrDefault(scene.Steps.ToList().FindIndex(n => n.Id == step.Id) + 1)?.Id;
                Check(next == r.Path[i + 1], "save branch " + r.Id);
            }
        }
    }
}
