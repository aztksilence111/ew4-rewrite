using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private object? StoryView(WorldState world)
    {
        var run = StoryPlayback.Current(world);
        if (run == null) return null;
        var library = world.Story!.Library;
        var scene = library.Scenes[run.Scene];
        var step = StoryPlayback.Step(world.Story, run);
        var decision = step.Choices.Any(c => c.Condition != null) ? StoryPlayback.DecisionWorld(world, run, content.Extensions.Bus.Scripts) : world;
        var actors = new Dictionary<string, StoryActor>();
        var names = new Dictionary<string, string>();
        string? background = null;
        foreach (string id in run.Path)
        {
            var frame = scene.Steps!.Single(s => s.Id == id);
            background = frame.Background ?? background;
            if (frame.ClearActors) actors.Clear();
            foreach (var a in frame.Actors.Concat(frame.UpdateActors))
            {
                var old = actors.GetValueOrDefault(a.Id);
                actors[a.Id] = old == null ? a : old with { Character = a.Character ?? old.Character, Slot = a.Slot ?? old.Slot,
                    Pose = a.Pose ?? old.Pose, Expression = a.Expression ?? (a.Pose != null && a.Pose != old.Pose ? null : old.Expression),
                    X = a.X ?? old.X, Y = a.Y ?? old.Y, Scale = a.Scale ?? old.Scale, Mirror = a.Mirror ?? old.Mirror,
                    Order = a.Order ?? old.Order, Hidden = a.Hidden, Parts = a.Parts ?? (a.Pose != null ? null : old.Parts) };
            }
            names[id] = frame.Name ?? (frame.Speaker != null && actors.TryGetValue(frame.Speaker, out var speaker)
                && speaker.Character != null ? library.Characters[speaker.Character].Name : "");
        }
        string Text(StoryStep s) => s.TextKey == null ? s.Text : library.Texts[s.TextKey];
        string Name(StoryStep s) => names[s.Id];
        string Media(string path) => path.StartsWith("client/", StringComparison.Ordinal) ? "/content/" + path[7..]
            : "/content/extensions/" + path[8..];
        var cast = actors.Values.Where(a => !a.Hidden).Select(a =>
        {
            if (a.Character == null || !library.Characters.TryGetValue(a.Character, out var c))
                throw new RuleViolation("story.actor", "Unknown character for " + a.Id);
            string poseName = a.Pose ?? c.Poses.Keys.First();
            if (!c.Poses.TryGetValue(poseName, out var p)) throw new RuleViolation("story.pose", a.Character + "/" + poseName);
            var parts = p.Layers.ToDictionary(l => l.Key, l => l.Value.Default);
            if (a.Expression != null)
            {
                if (!p.Expressions.TryGetValue(a.Expression, out var expression)) throw new RuleViolation("story.expression", a.Character + "/" + a.Expression);
                foreach (var part in expression) parts[part.Key] = part.Value;
            }
            foreach (var part in a.Parts ?? new Dictionary<string, string>()) parts[part.Key] = part.Value;
            return new { a.Id, name = c.Name, a.Slot, x = a.X ?? a.Slot switch { "left" => .25, "midLeft" => .35, "midRight" => .65, "right" => .75, _ => .5 },
                y = a.Y ?? 1, scale = a.Scale ?? 1, mirror = a.Mirror ?? false, order = a.Order ?? 0, p.Width, p.Height,
                image = p.Image == null ? null : Media(p.Image),
                layers = p.Layers.OrderBy(l => l.Value.Order).Select(l => new { id = l.Key, image = Media(l.Value.Parts[parts[l.Key]]),
                    l.Value.X, l.Value.Y, width = l.Value.Width ?? p.Width, height = l.Value.Height ?? p.Height }).ToArray() };
        }).ToArray();
        var history = run.Path.Select(id => scene.Steps!.Single(s => s.Id == id)).Where(s => Text(s).Length > 0 || run.Choices.ContainsKey(s.Id))
            .Select(s => new { s.Id, name = Name(s), text = Text(s),
                choice = run.Choices.TryGetValue(s.Id, out string? selected) ? s.Choices.Single(c => c.Id == selected).Text : null }).ToArray();
        return new { run.Id, run.Key, run.Scene, run.Kind, restored = storyRestored, node = step.Id, text = Text(step), name = Name(step), step.Speaker,
            step.WaitMs, background = background == null ? null : Media(library.Backgrounds[background]), actors = cast,
            step.Animations, theme = library.Theme, history,
            choices = step.Choices.Select(c => new { c.Id, c.Text, available = c.Condition == null || ScenarioRules.Match(decision, c.Condition, content.Extensions.Bus.Scripts) }).ToArray() };
    }
}
