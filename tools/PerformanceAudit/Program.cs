using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var contentInputs = new Dictionary<string, string>(StringComparer.Ordinal);
var setup = Stopwatch.StartNew();
byte[] Read(string path)
{
    string relative = "rewrite/data/" + (path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path);
    byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative));
    string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
    if (contentInputs.TryGetValue(relative, out string? previous) && previous != hash) throw new Exception("Content changed: " + relative);
    contentInputs[relative] = hash;
    return bytes;
}
var scenarios = (Environment.GetEnvironmentVariable("EW4_AUDIT_SCENARIOS") ?? "conquest1,conquest4").Split(',');
var content = new ClientContent(Read, Enumerable.Range(1, 6).Select(i => $"conquest{i}.btl")
    .Concat(scenarios.Select(s => s + ".btl")).Distinct().ToArray());
double contentSetupMs = setup.Elapsed.TotalMilliseconds;
var gameField = typeof(ClientGame).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!;
GameSession Session(ClientGame host) => ((LegacyScenarioStart)gameField.GetValue(host)!).Session;
JsonElement Call(ClientGame host, string route, object command, List<double>? durations = null, List<int>? sizes = null)
{
    string body = JsonSerializer.Serialize(command, ClientGame.Json);
    var clock = Stopwatch.StartNew();
    string result = host.Dispatch("/api/" + route, body);
    durations?.Add(clock.Elapsed.TotalMilliseconds); sizes?.Add(Encoding.UTF8.GetByteCount(result));
    using var doc = JsonDocument.Parse(result);
    if (!doc.RootElement.GetProperty("ok").GetBoolean()) throw new Exception(result);
    return doc.RootElement.GetProperty("data").Clone();
}
var reports = new List<object>();
bool delta = Environment.GetEnvironmentVariable("EW4_AUDIT_DELTA") == "1";
bool trace = Environment.GetEnvironmentVariable("EW4_AUDIT_TRACE") == "1";
int rounds = int.Parse(Environment.GetEnvironmentVariable("EW4_AUDIT_ROUNDS") ?? "1");
if (rounds < 1 || rounds > 20) throw new ArgumentOutOfRangeException(nameof(rounds));
bool retainedMemory = Environment.GetEnvironmentVariable("EW4_AUDIT_RETAINED_MEMORY") == "1";
string? startSlot = Environment.GetEnvironmentVariable("EW4_AUDIT_START_SLOT");
if (Environment.GetEnvironmentVariable("EW4_AUDIT_DUAL_VALIDATION") == "1")
    typeof(WorldState).Assembly.GetType("Ew4.Core.PlanningScope")!.GetField("VerifyMovementProjections", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
foreach (string scenario in scenarios)
{
    var started = content.Start(scenario, "fra");
    var initial = started.Session with { BattleId = "performance-audit-fixed-" + scenario,
        World = started.Session.World with { RandomState = 481960042392081575, AiRandomState = 3493881671538411339 } };
    var inputSlot = startSlot == null ? new ClientSlot(scenario, "fra", SessionSave.Encode(initial))
        : JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(startSlot), ClientGame.Json)!;
    if (inputSlot.Scenario != scenario) throw new Exception("Start slot scenario differs");
    var expectedHashes = new Dictionary<int, string>();
    foreach (string mode in (Environment.GetEnvironmentVariable("EW4_AUDIT_MODES") ?? "batch,ordinary").Split(','))
    for (int sample = 0; sample < int.Parse(Environment.GetEnvironmentVariable("EW4_AUDIT_SAMPLES") ?? "3"); sample++)
    {
        string folder = Path.Combine(output, scenario + "-" + mode + "-" + sample);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "slot-6.json"), JsonSerializer.SerializeToUtf8Bytes(inputSlot, ClientGame.Json));
        var host = new ClientGame(content, folder);
        var loadClock = Stopwatch.StartNew();
        Call(host, "load", new { slot = 6 });
        double loadMs = loadClock.Elapsed.TotalMilliseconds;
        for (int roundIndex = 0; roundIndex < rounds; roundIndex++)
        {
            var roundInitial = Session(host);
            if (!roundInitial.World.Factions.Single(f => f.Id == roundInitial.World.ActiveFaction).Human
                || roundInitial.World.PendingDialogue != null || roundInitial.World.Phase == TurnPhase.Finished)
                throw new Exception("Round must start at an actionable human turn");
            byte[] initialBytes = SessionSave.Encode(roundInitial);
            File.WriteAllBytes(Path.Combine(folder, $"round-{roundIndex + 1}-start.json"),
                JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(scenario, "fra", initialBytes), ClientGame.Json));
            PerfProbe.Reset(); PerfProbe.Active = true;
            var calls = new List<double>(); var sizes = new List<int>();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            var clock = Stopwatch.StartNew();
            using var sequence = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Trace()
            {
                if (!trace) return;
                sequence.AppendData(WorldSave.Encode(Session(host).World));
                sequence.AppendData(JsonSerializer.SerializeToUtf8Bytes(((LegacyScenarioStart)gameField.GetValue(host)!).Notices, ClientGame.Json));
            }
            var state = Call(host, "action", new { op = "end", revision = Session(host).Revision, delta }, calls, sizes);
            Trace();
            int steps = 0;
            while (true)
            {
                var world = Session(host).World;
                if (world.Factions.Single(f => f.Id == world.ActiveFaction).Human || world.PendingDialogue != null || world.Phase == TurnPhase.Finished) break;
                var next = Call(host, "action", new { op = mode == "batch" ? "ai_batch" : "ai", revision = Session(host).Revision, delta }, calls, sizes);
                Trace();
                if (mode == "batch")
                {
                    if (next.GetProperty("error").ValueKind != JsonValueKind.Null) throw new Exception(next.GetRawText());
                    int count = next.GetProperty("steps").GetInt32();
                    if (count == 0) throw new Exception("AI did not progress");
                    steps += count; state = next.GetProperty("state");
                }
                else { steps++; state = next; }
                if (steps > 3000) throw new Exception("AI step limit");
            }
            double elapsedMs = clock.Elapsed.TotalMilliseconds;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            int[] turnGc = [GC.CollectionCount(0)-gc0, GC.CollectionCount(1)-gc1, GC.CollectionCount(2)-gc2];
            PerfProbe.Active = false;
            var final = Session(host);
            string hash = Convert.ToHexStringLower(SHA256.HashData(SessionSave.Encode(final)));
            if (!expectedHashes.TryGetValue(roundIndex, out string? expectedHash)) expectedHashes.Add(roundIndex, hash);
            else if (hash != expectedHash) throw new Exception("Final session differs between modes/repetitions");
            object History(WorldState w) => new { destroyed = w.DestroyedScenarioUnits.Count, deadCommanders = w.DeadScenarioCommanders.Count,
                triggered = w.TriggeredScenarioEvents.Count, choices = w.ScenarioChoices.Count, progress = w.ScenarioEventProgress.Count,
                units = w.Units.Count, factions = w.Factions.Count(f => !f.Defeated), w.Round, w.ActiveFaction };
            // Forced collection is an opt-in diagnostic, outside the measured turn.
            long? retainedManagedBytes = retainedMemory ? GC.GetTotalMemory(true) : null;
            var report = new { scenario, mode, sample, roundIndex, warmup = sample == 0, contentSetupMs, loadMs,
                initialSha256 = Convert.ToHexStringLower(SHA256.HashData(initialBytes)), cells = roundInitial.World.Cells.Count,
                units = roundInitial.World.Units.Count, initialHistory = History(roundInitial.World), finalHistory = History(final.World), retainedManagedBytes,
                steps, delta, sequenceSha256 = trace ? Convert.ToHexStringLower(sequence.GetHashAndReset()) : null, requests = calls.Count, elapsedMs, dispatchMs = calls.Sum(),
                responseBytes = sizes.Sum(x => (long)x), allocatedBytes = bytes, gc = turnGc,
                requestP95Ms = calls.Order().ElementAt((int)((calls.Count-1)*.95)), requestMaxMs = calls.Max(),
                final.World.Round, final.World.RandomState, final.World.AiRandomState, finalSha256 = hash,
                stopReason = final.World.PendingDialogue != null ? "dialogue" : final.World.Phase == TurnPhase.Finished ? "finished" : "human",
                worldValidation = new { calls = PerfProbe.WorldValidationCalls, snapshots = PerfProbe.WorldSnapshots, cellVisits = PerfProbe.CellValidationVisits, unitVisits = PerfProbe.UnitValidationVisits }, counters = PerfProbe.Counters(), distanceKeySamples = PerfProbe.DistanceSamples(), metrics = PerfProbe.Snapshot() };
            // GameSession exposes the round through World; serialized state remains the correctness oracle.
            reports.Add(report);
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { scenario, mode, sample, roundIndex, steps, ms = Math.Round(elapsedMs), mb = Math.Round(bytes/1048576.0), hash = hash[..12] }));
            if (final.World.PendingDialogue != null || final.World.Phase == TurnPhase.Finished) break;
        }
    }
}
foreach (var input in contentInputs)
    if (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, input.Key)))) != input.Value)
        throw new Exception("Content changed after measurement: " + input.Key);
File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(new {
    content = contentInputs, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    configuration = "Release; net10.0; desktop; no AOT; no trimming",
    rng = new ulong[] { 481960042392081575, 3493881671538411339 }, errors = 0
}, new JsonSerializerOptions { WriteIndented = true }));
