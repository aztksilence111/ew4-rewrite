using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

if (args.Length < 2) throw new ArgumentException("Usage: Hre30FactionAudit <repository> <output> [max-steps] [resume-session]");
string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
int maxSteps = args.Length > 2 ? int.Parse(args[2]) : int.MaxValue;
if (!output.StartsWith(Path.Combine(root, "build") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Test output must be inside the repository build directory");
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new ArgumentException("Use a new test output directory");
Directory.CreateDirectory(output);
using var archive = ZipFile.OpenRead(Path.Combine(root, "rewrite/content/three-map-review.ew4mod"));
byte[] Read(string path)
{
    path = path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path;
    if (archive.GetEntry(path) is { } entry)
    {
        using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
    return File.ReadAllBytes(Path.Combine(root, "rewrite/data", path));
}
var content = new ClientContent(Read, ["conquest1.btl", "conquest9.btl"]);
var normal = content.Start("conquest1", "fra");
string player = normal.Session.PlayerFaction;
var start = content.Start("conquest9", "fra");
var original = start.Session.World;
var templates = normal.Session.World.Factions.Where(f => !normal.Rules.Turns.NeutralCamps.Contains(f.Camp))
    .OrderBy(f => f.Id == player ? 0 : 1).ToArray();
var sources = content.Battle("conquest1").Factions.Select((f, i) => (f, i)).ToDictionary(
    p => LegacyScenarioInitialization.FactionId(normal.Session.World.ScenarioId, p.i, p.f), p => p.f);
FactionAppearance Appearance(int i)
{
    var source = sources[templates[i % templates.Length].Id];
    var country = content.Country(source.CountryId);
    return new(Flag: country?.Flag ?? source.Variant, Color: country?.Color ?? "#708090");
}
var factions = Enumerable.Range(0, 30).Select(i => templates[i % templates.Length] with {
    Id = i == 0 ? player : "stress.f" + i.ToString("00"), Human = i == 0, Defeated = false, PlayerAi = null,
    HomelandBuildings = null, Capital = null, Resources = new(300, 150, 300), TurnSummary = null, Appearance = Appearance(i) }).ToArray();
var land = original.Cells.Select((c, i) => (c, i)).Where(p => p.c.Terrain.CanTraverse() && !p.c.Terrain.Sea)
    .Select(p => p.i).OrderBy(i => i % original.Width).ThenBy(i => i / original.Width).ToArray();
if (land.Length < 15000) throw new InvalidDataException("Not enough distinct land cells");
var owners = new int[original.Cells.Count];
for (int i = 0; i < land.Length; i++) owners[land[i]] = i * 30 / land.Length;
var unitTemplates = normal.Session.World.Units.Where(u => u.CommanderId == null && u.Combatant.Profile.Category != "naval").ToArray();
var units = Enumerable.Range(0, 30).SelectMany(f => {
    var cells = land.Where(c => owners[c] == f).ToArray();
    return Enumerable.Range(0, 500).Select(i => {
        var unit = unitTemplates[(f * 500 + i) % unitTemplates.Length];
        return unit with { Id = $"stress.{f:D2}.{i:D3}", Owner = factions[f].Id, Cell = cells[i * cells.Length / 500],
            CommanderId = null, CommanderBase = null, PlayerControl = null, Objective = false, Acted = false,
            RemainingMovement = unit.Combatant.Profile.Movement };
    });
}).ToArray();
var rules = normal.Rules with {
    Production = factions.Select((f, i) => (f, i)).ToDictionary(p => p.f.Id, p => normal.Rules.Production[templates[p.i % templates.Length].Id]),
    Recruitment = factions.Select((f, i) => (f, i)).ToDictionary(p => p.f.Id, p => normal.Rules.Recruitment[templates[p.i % templates.Length].Id]) };
var world = original with { ScenarioId = normal.Session.World.ScenarioId, Factions = factions, Units = units,
    Cells = original.Cells.Select((c, i) => c with { Owner = c.Terrain.CanTraverse() && !c.Terrain.Sea ? factions[owners[i]].Id : null }).ToArray(),
    ActiveFaction = player, ScenarioPlayerFaction = player, Commanders = [], CommanderOffers = [], ScenarioEvents = [], Events = [],
    TriggeredEvents = [], TriggeredScenarioEvents = [], ScenarioChoices = new Dictionary<string, string>(),
    Demands = null, DiplomacyAdjustments = null, Provocations = null, DiplomaticRelations = [], NeutralHostilities = [],
    Jurisdictions = null, ArmyCapacity = null, PendingDialogue = null, Story = null, RandomState = 123456, AiRandomState = 654321 };
start = start with { Rules = rules, Session = start.Session with { World = world, PlayerFaction = player, Markets = [], TavernCandidates = [], RosterDeployments = [], BattleId = "hre30-fixed" } };
start.Session.Validate();
if (world.Units.Count != 15000 || world.Units.Select(u => u.Cell).Distinct().Count() != 15000
    || factions.Any(f => world.Units.Count(u => u.Owner == f.Id) != 500)) throw new InvalidDataException("Invalid fixture distribution");
var binding = new SessionContentBinding(start.Session.ContentRevision, world.ContentRevision, start.Session.Account.ContentRevision);
if (args.Length > 3)
{
    var resumed = SessionSave.Decode(File.ReadAllBytes(args[3]), binding);
    start = start with { Session = resumed with { World = resumed.World with {
        Factions = resumed.World.Factions.Select((f, i) => f with { Appearance = Appearance(i) }).ToArray() } } };
}
var field = typeof(ClientGame).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic)!;
var commit = typeof(ClientGame).GetMethod("Commit", BindingFlags.Instance | BindingFlags.NonPublic)!;
var client = new ClientGame(content, Path.Combine(output, "profile")); field.SetValue(client, start);
LegacyScenarioStart Current() => (LegacyScenarioStart)field.GetValue(client)!;
void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
using (var initial = JsonDocument.Parse(client.Dispatch("/api/action", "{\"op\":\"state\"}")))
    if (!initial.RootElement.GetProperty("ok").GetBoolean()) throw new Exception("Fixture full view failed: " + initial.RootElement);
Write("fixture.json", new { cells = world.Cells.Count, units = world.Units.Count, factions = factions.Select(f => new { f.Id, f.Camp, units = 500 }),
    fixtureHash = Convert.ToHexStringLower(SHA256.HashData(WorldSave.Encode(world))) });
var handoffs = new List<object>();
var handoffWorld = world;
for (int i = 0; i < 30; i++)
{
    var timer = Stopwatch.StartNew(); var change = WorldTurns.EndTurn(handoffWorld, handoffWorld.Revision, rules.Turns);
    handoffs.Add(new { from = handoffWorld.ActiveFaction, to = change.State.ActiveFaction, ms = timer.Elapsed.TotalMilliseconds });
    handoffWorld = change.State;
}
if (handoffWorld.Round != world.Round + 1 || handoffWorld.ActiveFaction != player) throw new Exception("Handoff cycle incomplete");
Write("handoffs.json", handoffs);
if (args.Length <= 3) commit.Invoke(client, [WorldTurns.EndTurn(world, world.Revision, rules.Turns), true]);
var wall = Stopwatch.StartNew();
var rows = new List<object>(); var decisions = new Dictionary<string, int>();
double planTotal = 0, executeTotal = 0, commitTotal = 0; int steps = 0;
long allocated = GC.GetTotalAllocatedBytes();
bool batch = Environment.GetEnvironmentVariable("EW4_HRE30_BATCH") == "1";
double actionMs = 0; int requests = 0;
int completedFactions = 0;
int stopFactions = int.Parse(Environment.GetEnvironmentVariable("EW4_HRE30_STOP_FACTIONS") ?? "30");
while (Current().Session.World is { } state && state.ActiveFaction != player && state.Phase == TurnPhase.Active && state.PendingDialogue == null)
{
    if (steps >= maxSteps || completedFactions >= stopFactions || wall.Elapsed.TotalHours >= 6) break;
    if (batch)
    {
        var timerBatch = Stopwatch.StartNew();
        string response = client.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "ai_batch", revision = Current().Session.Revision, compact = true }, ClientGame.Json));
        double elapsed = timerBatch.Elapsed.TotalMilliseconds; actionMs += elapsed; requests++;
        using var doc = JsonDocument.Parse(response);
        if (!doc.RootElement.GetProperty("ok").GetBoolean()) throw new Exception(response[..Math.Min(1000, response.Length)]);
        var data = doc.RootElement.GetProperty("data");
        if (data.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) throw new Exception(error.ToString());
        int advanced = data.GetProperty("steps").GetInt32();
        if (advanced == 0 && !(data.TryGetProperty("planning", out var planning) && planning.GetBoolean()))
            throw new Exception("AI batch made no progress");
        steps += advanced;
        if (state.ActiveFaction != Current().Session.World.ActiveFaction) completedFactions++;
        rows.Add(new { step = steps, faction = state.ActiveFaction, steps = advanced, ms = elapsed, bytes = response.Length, next = Current().Session.World.ActiveFaction });
        if (steps / 500 != (steps - advanced) / 500 || state.ActiveFaction != Current().Session.World.ActiveFaction)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { steps, seconds = wall.Elapsed.TotalSeconds, actionMs, faction = state.ActiveFaction, next = Current().Session.World.ActiveFaction }));
            Write("steps.json", rows); File.WriteAllBytes(Path.Combine(output, "checkpoint.json"), SessionSave.Encode(Current().Session));
        }
        continue;
    }
    var production = rules.Production[state.ActiveFaction];
    var timer = Stopwatch.StartNew(); var plan = WorldAi.PlanStep(state, rules.Ai, production, rules.Turns); double planMs = timer.Elapsed.TotalMilliseconds;
    string decision = plan.Production.Command?.GetType().Name ?? (plan.Commander != null ? "commander" : plan.Unit == null ? "handoff" : plan.Unit.Candidates[AiPlanning.Best(plan.Unit.Candidates)].Action.GetType().Name);
    timer.Restart(); var change = WorldAi.ExecuteStep(state, plan, rules.Ai, production, rules.Turns, rules.Battle); double executeMs = timer.Elapsed.TotalMilliseconds;
    timer.Restart(); commit.Invoke(client, [change, true]); double commitMs = timer.Elapsed.TotalMilliseconds;
    steps++; planTotal += planMs; executeTotal += executeMs; commitTotal += commitMs; decisions[decision] = decisions.GetValueOrDefault(decision) + 1;
    actionMs += planMs + executeMs + commitMs;
    rows.Add(new { step = steps, faction = state.ActiveFaction, decision, unit = plan.Unit?.UnitId, planMs, executeMs, commitMs, next = Current().Session.World.ActiveFaction });
    if (steps % 50 == 0 || state.ActiveFaction != Current().Session.World.ActiveFaction || steps == 1)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { steps, seconds = wall.Elapsed.TotalSeconds, faction = state.ActiveFaction, next = Current().Session.World.ActiveFaction, planTotal, executeTotal, commitTotal }));
        Write("steps.json", rows);
        File.WriteAllBytes(Path.Combine(output, "checkpoint.json"), SessionSave.Encode(Current().Session));
    }
}
var final = Current().Session;
var bytes = SessionSave.Encode(final);
if (!bytes.SequenceEqual(SessionSave.Encode(SessionSave.Decode(bytes, binding).Copy()))) throw new Exception("Save/load/copy changed bytes");
final.Validate();
bool complete = final.World.ActiveFaction == player && final.World.Round == world.Round + 1;
bool? replayMatches = null;
if (Environment.GetEnvironmentVariable("EW4_HRE30_COMPARE_SLOT") is string comparePath)
{
    var slot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(comparePath), ClientGame.Json)!;
    var reference = SessionSave.Decode(slot.Session, binding);
    byte[] Normalize(GameSession session) => SessionSave.Encode(session with { World = session.World with {
        Factions = session.World.Factions.Select(f => f with { Appearance = null }).ToArray() } });
    replayMatches = Normalize(reference).SequenceEqual(Normalize(final));
    if (replayMatches != true) throw new Exception("Resumed AI session differs from the original automatic save beyond presentation");
}
var report = new { steps, batch, requests, actionMs, ms = wall.Elapsed.TotalMilliseconds, planTotal, executeTotal, commitTotal, complete, round = final.World.Round,
    units = final.World.Units.Count, replayMatches, allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, decisions,
    hash = Convert.ToHexStringLower(SHA256.HashData(WorldSave.Encode(final.World))), saveBytes = bytes.Length };
Write("steps.json", rows); Write("results.json", report);
File.WriteAllBytes(Path.Combine(output, "checkpoint.json"), bytes);
Console.WriteLine(JsonSerializer.Serialize(report));
if (maxSteps == int.MaxValue && stopFactions >= 29 && !complete) throw new Exception("Full AI round did not return to the player");
