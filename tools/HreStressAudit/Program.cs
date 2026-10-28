using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

if (args.Length is < 3 or > 4) throw new ArgumentException("Usage: HreStressAudit <repository> <output> <review-map-source> [unit-count]");
string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
string mapSource = Path.GetFullPath(args[2]);
byte[] Read(string p)
{
    p = p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p;
    string source = Path.Combine(mapSource, p);
    return File.ReadAllBytes(File.Exists(source) ? source : Path.Combine(root, "rewrite/data", p));
}
var content = new ClientContent(Read, ["conquest9.btl"]);
var rows = new List<object>();
foreach (int count in args.Length == 4 ? new[] { int.Parse(args[3]) } : new[] { 1000, 10000, 30000 })
{
    var start = content.Start("conquest9", "fra");
    var original = start.Session.World;
    var cells = original.Cells.Select((c, i) => (c, i)).Where(p => p.c.Terrain.CanTraverse() && !p.c.Terrain.Sea).Select(p => p.i).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new { count, landCells = cells.Length }));
    if (count <= 0 || cells.Length < count) throw new InvalidDataException("Insufficient distinct land cells");
    var templates = original.Units.ToArray();
    var units = Enumerable.Range(0, count).Select(i => templates[i % templates.Length] with {
        Id = "stress." + i, Cell = cells[(int)((long)i * cells.Length / count)], CommanderId = null,
        CommanderBase = null, Objective = false, Acted = false,
        RemainingMovement = templates[i % templates.Length].Combatant.Profile.Movement
    }).ToArray();
    var world = original with { Units = units, Commanders = [], PendingDialogue = null, Story = null, RandomState = 123456, AiRandomState = 654321,
        Demands = original.Demands is { } demand ? demand with { Seed = 123456 } : null,
        Factions = original.Factions.Select(f => f with { Resources = new(10000000, 10000000, 10000000) }).ToArray() };
    world.Validate();
    var navigationMs = new List<double>();
    var navigationAllocations = new List<long>();
    for (int sample = 0; sample < 4; sample++)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        var navigationTimer = Stopwatch.StartNew();
        var movable = WorldActions.MovableUnits(world, start.Session.PlayerFaction, start.Rules.Movement);
        navigationMs.Add(navigationTimer.Elapsed.TotalMilliseconds);
        navigationAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
        if (sample == 0) File.WriteAllText(Path.Combine(output, "movable-" + count + ".json"), JsonSerializer.Serialize(movable.Order(StringComparer.Ordinal)));
    }
    start = start with { Session = start.Session with { World = world } };
    var client = new ClientGame(content, Path.Combine(output, "profile-" + count));
    typeof(ClientGame).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, start);
    typeof(ClientGame).GetField("country", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client, "fra");
    var timer = Stopwatch.StartNew();
    string response = client.Dispatch("/api/action", "{\"op\":\"state\"}");
    double viewMs = timer.Elapsed.TotalMilliseconds;
    using var parsed = JsonDocument.Parse(response);
    if (!parsed.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidDataException(response);
    File.WriteAllText(Path.Combine(output, "view-" + count + ".json"), response);
    timer.Restart();
    var turn = WorldTurns.EndTurn(world, world.Revision, start.Rules.Turns);
    double turnMs = timer.Elapsed.TotalMilliseconds;
    if (world.Units.Any(u => u.Acted)) throw new InvalidDataException("Input state mutated");
    byte[] turnBytes = WorldSave.Encode(turn.State);
    File.WriteAllBytes(Path.Combine(output, "turn-" + count + ".json"), turnBytes);
    var digest = Convert.ToHexStringLower(SHA256.HashData(turnBytes));
    timer.Restart();
    byte[] sessionBytes = SessionSave.Encode(start.Session with { World = turn.State });
    var restored = SessionSave.Decode(sessionBytes, new(start.Session.ContentRevision, world.ContentRevision, start.Session.Account.ContentRevision));
    if (!SessionSave.Encode(restored).SequenceEqual(sessionBytes)) throw new InvalidDataException("Session save/load changed authenticated bytes");
    double saveLoadMs = timer.Elapsed.TotalMilliseconds;
    timer.Restart();
    var end = client.Dispatch("/api/action", JsonSerializer.Serialize(new ClientCommand("end", start.Session.Revision, Delta: true), ClientGame.Json));
    double commandMs = timer.Elapsed.TotalMilliseconds;
    using var endDoc = JsonDocument.Parse(end);
    if (!endDoc.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidDataException(end);
    var aiTimes = new List<double>();
    var aiWorld = turn.State;
    for (int step = 0; step < 20 && aiWorld.Phase == TurnPhase.Active && aiWorld.PendingDialogue == null; step++)
    {
        timer.Restart();
        var plan = WorldAi.PlanNextUnit(aiWorld, start.Rules.Ai);
        if (plan == null) break;
        aiWorld = WorldAi.ExecuteUnit(aiWorld, plan, start.Rules.Ai, start.Rules.Battle).State;
        aiTimes.Add(timer.Elapsed.TotalMilliseconds);
    }
    string aiDigest = Convert.ToHexStringLower(SHA256.HashData(WorldSave.Encode(aiWorld)));
    var row = new { count, cells = world.Cells.Count, viewMs, turnMs, commandMs, responseBytes = response.Length,
        deltaBytes = end.Length, digest, aiTimes, aiDigest, navigationMs, navigationAllocations, saveBytes = sessionBytes.Length, saveLoadMs,
        notices = turn.Notices.Count, remaining = turn.State.Units.Count };
    rows.Add(row); Console.WriteLine(JsonSerializer.Serialize(row));
}
File.WriteAllText(Path.Combine(output, "core.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
