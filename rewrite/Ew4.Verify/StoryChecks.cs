using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class StoryChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Story: " + label); }
        void Reject(Action action, string code)
        { try { action(); } catch (RuleViolation e) { Check(e.Code == code, label: code + " got " + e.Code); return; } throw new Exception("Story accepted " + code); }
        var library = new StoryLibrary { Scenes = new Dictionary<string, StoryScene> {
            ["scene"] = new("scene", [new() { Id = "intro", Text = "Opening" }, new() { Id = "choose", Text = "Decide", Choices = [
                new() { Id = "attack", Text = "Attack", Next = "attack", Condition = new() { Type = "money", Value = 20 }, Effects = [new() { Type = "money", Value = -20 }, new() { Type = "cells_owner", Faction = "player", Cells = [1] }] },
                new() { Id = "defend", Text = "Defend", Next = "defend" }] },
                new() { Id = "attack", End = true, Result = "attack" }, new() { Id = "defend", End = true, Result = "defend" }]),
            ["ending"] = new("ending", [new() { Id = "last", Text = "Ending", End = true }]) },
            Bindings = new Dictionary<string, StoryBinding> { ["fixture"] = new(EndingStories: [new("last", "ending", "victory")]) } };
        StoryValidation.Library(library); checks++;
        var basis = new WorldState { ScenarioId = "fixture", ContentRevision = "fixture", Width = 2, Height = 1,
            ActiveFaction = "player", ScenarioPlayerFaction = "player", Factions = [new() { Id = "player", Camp = "a", Human = true, Resources = new(100, 0, 0) },
                new() { Id = "enemy", Camp = "b" }], Cells = [new() { Owner = "player" }, new() { Owner = "enemy" }], Story = new() { Library = library } };
        var ev = new ScriptedScenarioEvent { Id = "council", Effects = [new() { Type = "money", Value = -10 },
            new() { Type = "play_story", Key = "call", Scene = "scene" }, new() { Type = "food", Value = 15 }] };
        var w = ScenarioRules.Evaluate(basis with { ScenarioEvents = [ev] }).State;
        Check(w.Factions[0].Resources.Money == 90 && w.Factions[0].Resources.Food == 0, "event suspends tail after story");
        Check(!ScenarioRules.Match(w, new() { Type = "event_completed", Key = "council" }), "calling event is not prematurely complete");
        var run = StoryPlayback.Current(w)!;
        var rng = w.RandomState;
        w = StoryPlayback.Advance(w, run.Id, "intro", null, true).State;
        Check(StoryPlayback.Current(w)!.Path.Last() == "choose" && w.RandomState == rng, "skip stops at choice without combat RNG");
        Reject(() => StoryPlayback.Advance(w, run.Id, "choose", null, false), "story.choice_required");
        Reject(() => WorldEvents.Acknowledge(w, w.PendingDialogue!), "story.command_required");
        Reject(() => StoryPlayback.Advance(w, "wrong", "choose", "attack", false), "story.stale");
        var poor = w with { Factions = w.Factions.Select(f => f.Id == "player" ? f with { Resources = new(0, 0, 0) } : f).ToArray() };
        Reject(() => StoryPlayback.Advance(poor, run.Id, "choose", "attack", false), "story.choice");
        w = WorldSave.Decode(WorldSave.Encode(w), w.ContentRevision); checks++;
        w = StoryPlayback.Advance(w, run.Id, "choose", "attack", false).State;
        Check(w.Factions[0].Resources.Money == 90 && w.Cells[1].Owner == "enemy" && StoryPlayback.Match(w, new() { Type = "story_choice", Key = "call", Node = "choose", Choice = "attack" }), "choice records intent without changing resources or map");
        Check(StoryPlayback.DecisionWorld(w, StoryPlayback.Current(w)!).Factions[0].Resources.Money == 70, "later conditions include reserved resource costs");
        w = WorldSave.Decode(WorldSave.Encode(w), w.ContentRevision); checks++;
        Check(w.Factions[0].Resources.Money == 90 && StoryPlayback.Current(w)!.PendingEffects.Count == 2, "manual mid-story save preserves unapplied effects");
        Reject(() => StoryPlayback.Advance(w, run.Id, "choose", "attack", false), "story.stale");
        w = StoryPlayback.Advance(w, run.Id, "attack", null, true).State;
        Check(w.PendingDialogue == null && w.Factions[0].Resources.Money == 70 && w.Cells[1].Owner == "player" && w.Factions[0].Resources.Food == 15
            && StoryPlayback.Match(w, new() { Type = "story_result", Key = "call", Result = "attack" }), "completion applies deferred map effects and resumes tail exactly once");
        Check(ScenarioRules.Match(w, new() { Type = "event_completed", Key = "council" }), "event complete after resumed tail");
        w = StoryPlayback.Enqueue(w, "call", "scene");
        Check(!StoryPlayback.Match(w, new() { Type = "story_completed", Key = "call" })
            && StoryPlayback.Match(w, new() { Type = "story_completed", Key = "call", Scope = "any" }), "repeat uses current instance with optional history");
        var terminal = StoryPlayback.BindEnding(w with { Outcome = BattleOutcome.Victory, Phase = TurnPhase.Finished });
        Check(StoryPlayback.BlocksSettlement(terminal) && terminal.QueuedDialogues.Count == 1, "ending queues after current story and blocks settlement");
        terminal = StoryPlayback.Advance(terminal, StoryPlayback.Current(terminal)!.Id, "intro", null, true).State;
        terminal = StoryPlayback.Advance(terminal, StoryPlayback.Current(terminal)!.Id, "choose", "defend", false).State;
        terminal = StoryPlayback.Advance(terminal, StoryPlayback.Current(terminal)!.Id, "defend", null, true).State;
        Check(StoryPlayback.Current(terminal)!.Kind == "ending" && terminal.Outcome == BattleOutcome.Victory, "finished world can advance into ending");
        terminal = WorldSave.Decode(WorldSave.Encode(terminal), terminal.ContentRevision); checks++;
        terminal = StoryPlayback.Advance(terminal, StoryPlayback.Current(terminal)!.Id, "last", null, true).State;
        Check(!StoryPlayback.BlocksSettlement(terminal) && terminal.PendingDialogue == null, "ending completion releases settlement");
        Check(StoryPlayback.BindEnding(terminal).Story!.Runs.Count == terminal.Story!.Runs.Count, "ending binding is idempotent");
        Reject(() => StoryValidation.Library(library with { Scenes = new Dictionary<string, StoryScene> { ["cycle"] = new("cycle", [new() { Id = "loop", Next = "loop" }]) } }), "story.definition");
        var calls = library with { Scenes = new Dictionary<string, StoryScene> {
            ["scene"] = new("scene", [new() { Id = "a", Choices = [new() { Id = "again", Text = "Again", Next = "b",
                Effects = [new() { Type = "play_story", Key = "again", Scene = "scene" }] }] }, new() { Id = "b", End = true }]) } };
        Reject(() => StoryValidation.Library(calls), "story.definition");
        var routing = library with { Bindings = new Dictionary<string, StoryBinding> { ["fixture"] = new(EndingStories: [
            new("fallback", "ending", "victory"), new("precise", "ending", "victory", "victory.special"),
            new("failure", "ending", "defeat", "failure.commander_lost")]) } };
        var routed = basis with { Story = new() { Library = routing }, Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory, ScenarioEnding = "victory.special" };
        Check(StoryPlayback.Current(StoryPlayback.BindEnding(routed))!.Key == "precise", "specific victory wins over fallback even when listed later");
        Check(StoryPlayback.Current(StoryPlayback.BindEnding(routed with { ScenarioEnding = "other" }))!.Key == "fallback", "unmatched victory uses default");
        Check(StoryPlayback.Current(StoryPlayback.BindEnding(routed with { Outcome = BattleOutcome.Defeat, ScenarioEnding = "failure.commander_lost" }))!.Key == "failure", "special defeat uses matching ending");
        Check(!StoryPlayback.BlocksSettlement(StoryPlayback.BindEnding(routed with { Outcome = BattleOutcome.Defeat, ScenarioEnding = "other" })), "unconfigured ending has no extra settlement barrier");
        Check(!StoryPlayback.BlocksSettlement(basis with { Story = null }), "original worlds keep original settlement behavior");
        var failedEffects = new[] { new ScenarioEffect { Type = "money", Value = -5 }, new ScenarioEffect { Type = "money", Value = -500 } };
        var rollbackLibrary = library with { Scenes = library.Scenes.ToDictionary(p => p.Key, p => p.Key != "scene" ? p.Value : p.Value with {
            Steps = p.Value.Steps!.Select(s => s.Id != "choose" ? s : s with { Choices = s.Choices.Select(c => c.Id != "attack" ? c : c with { Effects = failedEffects }).ToArray() }).ToArray() }) };
        var rollback = StoryPlayback.Enqueue(basis with { Story = new() { Library = rollbackLibrary } }, "rollback", "scene");
        rollback = StoryPlayback.Advance(rollback, "run1", "intro", null, true).State;
        byte[] beforeRollback = WorldSave.Encode(rollback);
        try { StoryPlayback.Advance(rollback, "run1", "choose", "attack", false); throw new Exception("Story accepted unpaid choice"); }
        catch (RuleViolation) { Check(WorldSave.Encode(rollback).SequenceEqual(beforeRollback), "failed multi-effect choice preserves balance and selection"); }
        var chainLibrary = library with { Scenes = library.Scenes.Concat(new Dictionary<string, StoryScene> { ["chain"] = new("chain", [
            new() { Id = "one", Choices = [new() { Id = "pay", Text = "Pay", Next = "two", Effects = [new() { Type = "money", Value = -60 }] }] },
            new() { Id = "two", Choices = [
                new() { Id = "pay", Text = "Pay twice", Next = "done", Condition = new() { Type = "money", Value = 60 }, Effects = [new() { Type = "money", Value = -60 }] },
                new() { Id = "free", Text = "Free", Next = "done" }] }, new() { Id = "done", End = true }]) }).ToDictionary(p => p.Key, p => p.Value) };
        var chain = StoryPlayback.Enqueue(basis with { Story = new() { Library = chainLibrary } }, "chain", "chain");
        chain = StoryPlayback.Advance(chain, "run1", "one", "pay", false).State;
        Check(chain.Factions[0].Resources.Money == 100, "multi-choice story keeps battle resources frozen");
        Reject(() => StoryPlayback.Advance(chain, "run1", "two", "pay", false), "story.choice");
        chain = StoryPlayback.Advance(chain, "run1", "two", "free", false).State;
        chain = WorldSave.Decode(WorldSave.Encode(chain), chain.ContentRevision);
        chain = StoryPlayback.Advance(chain, "run1", "done", null, true).State;
        Check(chain.Factions[0].Resources.Money == 40 && chain.Story!.Runs[0].PendingEffects.Count == 0, "reserved effects commit once after final step and reload");

        string example = Path.Combine(root, "rewrite/mod-kit/examples/story-presentation");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal) ? Path.Combine(root, "rewrite/data/legacy", path[5..])
            : Path.Combine(path.StartsWith("rewrite/", StringComparison.Ordinal) ? example : Path.Combine(root, "rewrite/data"), path));
        var content = new ClientContent(Read, ["conquest1.btl"]);
        string folder = Path.Combine(root, "build/story-checks-20261027");
        Directory.CreateDirectory(folder);
        try
        {
            var host = new ClientGame(content, folder);
            JsonElement Call(string route, object command, bool ok = true)
            {
                using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
                Check(doc.RootElement.GetProperty("ok").GetBoolean() == ok, route + " " + doc.RootElement);
                return doc.RootElement.GetProperty(ok ? "data" : "code").Clone();
            }
            JsonElement view = Call("start", new { scenario = "conquest1", country = "fra" });
            string automatic = Path.Combine(folder, "saves/mode-2/slot-0.json");
            Check(!File.Exists(automatic), "opening playback does not autosave");
            JsonElement Act(string op, string? choice = null)
            {
                var story = view.GetProperty("story");
                var response = Call("action", new { op, offer = story.GetProperty("id").GetString(), tag = story.GetProperty("node").GetString(), target = choice,
                    revision = view.GetProperty("revision").GetInt64() });
                if (response.TryGetProperty("presentationOnly", out var partial) && partial.GetBoolean())
                {
                    Check(!response.TryGetProperty("units", out _) && !response.TryGetProperty("cells", out _), "intermediate response contains no battle projection");
                    var merged = view.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
                    foreach (var p in response.EnumerateObject()) merged[p.Name] = p.Value.Clone();
                    view = JsonSerializer.SerializeToElement(merged);
                }
                else view = response;
                return view;
            }
            Check(view.GetProperty("story").GetProperty("kind").GetString() == "opening", "real client opening first");
            Check(Call("action", new { op = "end", revision = view.GetProperty("revision").GetInt64() }, false).GetString() == "story.pending", "backend rejects battle action during story");
            Call("save", new { slot = 0, mode = 2 }); byte[] beforeLine = File.ReadAllBytes(automatic);
            Act("story_next");
            Check(File.ReadAllBytes(automatic).SequenceEqual(beforeLine), "ordinary dialogue advancement performs no automatic write");
            Act("story_skip");
            Check(view.GetProperty("story").ValueKind == JsonValueKind.Null
                ? File.ReadAllBytes(automatic + ".bak").SequenceEqual(beforeLine) : File.ReadAllBytes(automatic).SequenceEqual(beforeLine),
                "continuous story playback defers saving; a released scene saves only once");
            for (int i = 0; i < 100 && view.GetProperty("story").ValueKind == JsonValueKind.Null; i++)
                view = Call("action", new { op = "dialogue", revision = view.GetProperty("revision").GetInt64() });
            Check(view.GetProperty("story").GetProperty("scene").GetString() == "sample.council", "completion condition invokes event story");
            Act("story_skip");
            Check(view.GetProperty("story").GetProperty("node").GetString() == "decision", "real client skip preserves decision");
            Call("save", new { slot = 1, mode = 2 }); view = Call("load", new { slot = 1, mode = 2 });
            Check(view.GetProperty("story").GetProperty("node").GetString() == "decision", "real session resumes pending choice");
            Act("story_choice", "negotiate"); Act("story_skip");
            Check(view.GetProperty("phase").GetString() == "Finished" && view.GetProperty("ending").GetString() == "victory.negotiated"
                && view.GetProperty("story").GetProperty("kind").GetString() == "ending" && view.GetProperty("battleResult").ValueKind == JsonValueKind.Null,
                "locked victory plays ending before result");
            Call("save", new { slot = 2, mode = 2 }); view = Call("load", new { slot = 2, mode = 2 });
            Check(view.GetProperty("battleResult").ValueKind == JsonValueKind.Null && view.GetProperty("story").GetProperty("kind").GetString() == "ending", "ending load cannot settle early");
            byte[] beforeEnding = File.ReadAllBytes(automatic);
            Act("story_skip");
            Check(File.ReadAllBytes(automatic + ".bak").SequenceEqual(beforeEnding), "final ending writes exactly one automatic snapshot");
            Check(view.GetProperty("battleResult").GetProperty("outcome").GetString() == "Victory" && view.GetProperty("story").ValueKind == JsonValueKind.Null, "result exists only after ending");
            long awards = view.GetProperty("battleResult").GetProperty("awardMedals").GetInt64();
            Call("save", new { slot = 3, mode = 2 }); view = Call("load", new { slot = 3, mode = 2 });
            Check(view.GetProperty("story").ValueKind == JsonValueKind.Null && view.GetProperty("battleResult").GetProperty("awardMedals").GetInt64() == awards, "settled load neither replays nor rewards twice");
            var failureEvents = JsonSerializer.Deserialize<Dictionary<string, ScriptedScenarioEvent[]>>(Read("rewrite/story/events.json"), ClientGame.Json)!;
            failureEvents["conquest1"] = failureEvents["conquest1"].Select(e => e.Id != "sample.negotiated" ? e : e with {
                Effects = [new() { Type = "ending", Key = "failure.commander_lost", Outcome = "defeat" }] }).ToArray();
            byte[] FailureRead(string path) => path == "rewrite/story/events.json" ? JsonSerializer.SerializeToUtf8Bytes(failureEvents, ClientGame.Json) : Read(path);
            host = new ClientGame(new ClientContent(FailureRead, ["conquest1.btl"]), Path.Combine(folder, "failure"));
            view = Call("start", new { scenario = "conquest1", country = "fra" }); Act("story_skip");
            for (int i = 0; i < 100 && view.GetProperty("story").ValueKind == JsonValueKind.Null; i++)
                view = Call("action", new { op = "dialogue", revision = view.GetProperty("revision").GetInt64() });
            Act("story_skip"); Act("story_choice", "negotiate"); Act("story_skip");
            Check(view.GetProperty("story").GetProperty("key").GetString() == "ending.special"
                && view.GetProperty("battleResult").ValueKind == JsonValueKind.Null, "real special failure plays matching story first");
            Act("story_skip");
            Check(view.GetProperty("battleResult").GetProperty("outcome").GetString() == "Defeat"
                && view.GetProperty("battleResult").GetProperty("failureDescription").ValueKind == JsonValueKind.Null,
                "custom defeat ending replaces generic failure dialogue");
        }
        finally { Directory.Delete(folder, true); }
        return checks;
    }
}
