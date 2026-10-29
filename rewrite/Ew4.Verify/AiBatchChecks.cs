using System.Text.Json;
using System.Reflection;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class AiBatchChecks
{
    public static int Run(ClientContent content, string folder)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("AI batch: " + label); }
        JsonElement Call(ClientGame host, string route, object command)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(), route + " succeeded");
            return doc.RootElement.GetProperty("data").Clone();
        }
        var initial = content.Start("conquest1", "fra").Session;
        ClientGame Host(string name, GameSession session)
        {
            string path = Path.Combine(folder, "ai-batch", name);
            Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "slot-6.json"), JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
            var host = new ClientGame(content, path);
            Call(host, "load", new { slot = 6 });
            return host;
        }
        var ordinary = Host("ordinary", initial);
        var batched = Host("batched", initial);
        var normal = Call(ordinary, "action", new { op = "end", revision = initial.Revision });
        var fast = Call(batched, "action", new { op = "end", revision = initial.Revision });
        var fastCells = fast.GetProperty("cells").EnumerateArray().Select(c => c.Clone()).ToArray();
        int total = 0, batches = 0, handoffs = 0;
        while (fast.GetProperty("activeFaction").GetString() != fast.GetProperty("playerFaction").GetString()
            && fast.GetProperty("phase").GetString() != "Finished" && fast.GetProperty("dialogue").ValueKind == JsonValueKind.Null)
        {
            var batch = Call(batched, "action", new { op = "ai_batch", revision = fast.GetProperty("revision").GetInt64() });
            int steps = batch.GetProperty("steps").GetInt32();
            Check(steps is > 0 and <= 32 && batch.GetProperty("error").ValueKind == JsonValueKind.Null, "bounded batch progressed");
            string batchFaction = fast.GetProperty("activeFaction").GetString()!;
            for (int i = 0; i < steps; i++)
            {
                Check(normal.GetProperty("activeFaction").GetString() == batchFaction, "batch returns at faction handoff for progress display");
                normal = Call(ordinary, "action", new { op = "ai", revision = normal.GetProperty("revision").GetInt64() });
            }
            fast = batch.GetProperty("state");
            if (fast.GetProperty("activeFaction").GetString() != batchFaction) handoffs++;
            if (fast.GetProperty("cells").ValueKind == JsonValueKind.Null)
                foreach (var update in fast.GetProperty("cellUpdates").EnumerateArray()) fastCells[update.GetProperty("cell").GetInt32()] = update.GetProperty("value").Clone();
            else fastCells = fast.GetProperty("cells").EnumerateArray().Select(c => c.Clone()).ToArray();
            Check(fastCells.Select(c => c.GetRawText()).SequenceEqual(normal.GetProperty("cells").EnumerateArray().Select(c => c.GetRawText())), "cell deltas equal full projection");
            Check(normal.GetProperty("revision").GetInt64() == fast.GetProperty("revision").GetInt64(), "equal step counts");
            // Compare actual durable sessions, including RNG, events, account and result fields.
            Call(ordinary, "save", new { slot = 5 });Call(batched, "save", new { slot = 5 });
            ClientSlot Read(string name) => JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(folder, "ai-batch", name, "slot-5.json")), ClientGame.Json)!;
            Check(Read("ordinary").Session.SequenceEqual(Read("batched").Session), "identical session bytes at batch boundary");
            ClientSlot Auto(string name) => JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(folder, "ai-batch", name, "saves/mode-2/slot-0.json")), ClientGame.Json)!;
            Check(Auto("ordinary").Session.SequenceEqual(Auto("batched").Session), "same automatic recovery boundary");
            total += steps;batches++;
            Check(total < 2000, "turn terminates");
        }
        Check(total > 32 && batches < total, "real original turn batched");
        Check(handoffs > 1, "progress crosses multiple original factions");
        var stale = JsonDocument.Parse(batched.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "ai_batch", revision = -1 })));
        Check(stale.RootElement.GetProperty("code").GetString() == "client.stale", "stale batch rejected");
        stale.Dispose();
        var aiWorld = initial.World with { ActiveFaction = initial.World.Factions.First(f => !f.Human).Id };
        var compactHost = Host("compact", initial with { World = aiWorld });
        var compactReference = Host("compact-reference", initial with { World = aiWorld });
        var gameField = typeof(ClientGame).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pendingField = typeof(ClientGame).GetField("pendingAiStep", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Ew4.Legacy.LegacyScenarioStart Current(ClientGame host) => (Ew4.Legacy.LegacyScenarioStart)gameField.GetValue(host)!;
        WorldAi.StepWork Partial()
        {
            var current = Current(compactHost);
            var work = new WorldAi.StepWork(current.Session.World, current.Rules.Ai,
                current.Rules.Production[current.Session.World.ActiveFaction], current.Rules.Turns);
            pendingField.SetValue(compactHost, work);
            return work;
        }
        foreach (string route in new[] { "action", "catalog" })
        {
            Partial();
            Call(compactHost, route, new { op = "state" });
            Check(pendingField.GetValue(compactHost) == null, "refresh and non-batch routes discard pending planning");
        }
        var compactStart = Current(compactHost);
        var partial = Partial();
        using (PlanningScope.Begin(compactStart.Rules.Ai, compactStart.Rules.Production[aiWorld.ActiveFaction], compactStart.Rules.Turns)) partial.Advance();
        int compactSteps = 0;
        while (Current(compactHost).Session.World.ActiveFaction == aiWorld.ActiveFaction)
        {
            var before = Current(compactHost).Session;
            var response = Call(compactHost, "action", new { op = "ai_batch", revision = before.Revision, compact = true });
            int advanced = response.GetProperty("steps").GetInt32();
            bool progressOnly = response.TryGetProperty("progressOnly", out var progress) && progress.GetBoolean();
            Check(advanced is >= 0 and <= 32, "compact batch has bounded commits");
            if (advanced == 0)
                Check(progressOnly && response.GetProperty("planning").GetBoolean()
                    && response.GetProperty("revision").GetInt64() == before.Revision, "partial planning keeps revision and reports progress");
            for (int i = 0; i < advanced; i++)
                Call(compactReference, "action", new { op = "ai", revision = Current(compactReference).Session.Revision });
            Check(SessionSave.Encode(Current(compactHost).Session).SequenceEqual(SessionSave.Encode(Current(compactReference).Session)),
                "compact continuation matches ordinary decisions and durable RNG");
            if (!progressOnly) Check(response.GetProperty("error").ValueKind == JsonValueKind.Null, "compact boundary has no error");
            compactSteps += advanced;
            Check(compactSteps < 2000, "compact faction terminates");
        }
        Check(pendingField.GetValue(compactHost) == null && compactSteps > 0, "handoff clears pending planning");
        Partial();
        using (var staleCompact = JsonDocument.Parse(compactHost.Dispatch("/api/action", "{\"op\":\"ai_batch\",\"compact\":true,\"revision\":-1}")))
            Check(staleCompact.RootElement.GetProperty("code").GetString() == "client.stale" && pendingField.GetValue(compactHost) == null,
                "stale request discards pending planning");
        var popupEvent = new ScriptedScenarioEvent { Id = "batch-popup", Popup = new ScenarioPopup { Text = "Batch boundary" } };
        var popupWorld = aiWorld with { ScenarioEvents = [popupEvent], ScenarioPlayerFaction = initial.PlayerFaction };
        var popupHost = Host("popup", initial with { World = popupWorld });
        var popupBatch = Call(popupHost, "action", new { op = "ai_batch", revision = initial.Revision });
        Check(popupBatch.GetProperty("steps").GetInt32() == 1 && popupBatch.GetProperty("state").GetProperty("scenarioPopup").ValueKind == JsonValueKind.Object,
            "new choice interrupts batch immediately after its commit");
        var failedWorld = aiWorld with { ScenarioPlayerFaction = initial.PlayerFaction, ScenarioEvents = [new ScriptedScenarioEvent {
            Id = "batch-failure", Effects = [new ScenarioEffect { Type = "money", Value = -long.MaxValue }] }] };
        var failedHost = Host("failure", initial with { World = failedWorld });
        var failedBefore = Call(failedHost, "action", new { op = "state" });
        var failedBatch = Call(failedHost, "action", new { op = "ai_batch", revision = initial.Revision });
        Check(failedBatch.GetProperty("error").GetProperty("code").GetString() == "scenario.resources", "rule error returned with snapshot");
        Check(failedBatch.GetProperty("state").GetRawText() == failedBefore.GetRawText(), "failed commit retains authoritative state");
        foreach (var (name, world) in new[] {
            ("human", initial.World),
            ("dialogue", popupWorld with { PendingDialogue = ScenarioRules.DialogueId(popupEvent.Id), TriggeredScenarioEvents = [popupEvent.Id] }),
            ("victory", initial.World with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory }),
            ("defeat", initial.World with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Defeat }) })
        {
            var host = Host(name, initial with { World = world });
            var before = Call(host, "action", new { op = "state" });
            var batch = Call(host, "action", new { op = "ai_batch", revision = before.GetProperty("revision").GetInt64() });
            Check(batch.GetProperty("steps").GetInt32() == 0, name + " stops without committing");
            Check(batch.GetProperty("state").GetRawText() == before.GetRawText(), name + " retains final state projection");
        }
        return checks;
    }
}
