using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]), source = Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);
byte[] Read(string p)
{
    p = p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p;
    string map = Path.Combine(source, p);
    return File.ReadAllBytes(File.Exists(map) ? map : Path.Combine(root, "rewrite/data", p));
}
var content = new ClientContent(Read, ["conquest9.btl", "conquest1.btl"]);
var field = typeof(ClientGame).GetField("game", BindingFlags.NonPublic | BindingFlags.Instance)!;
var commit = typeof(ClientGame).GetMethod("Commit", BindingFlags.NonPublic | BindingFlags.Instance)!;
LegacyScenarioStart Current(ClientGame client) => (LegacyScenarioStart)field.GetValue(client)!;
JsonElement Call(ClientGame client, string op, bool compact, List<object> timings)
{
    var watch = Stopwatch.StartNew();
    string result = client.Dispatch("/api/action", JsonSerializer.Serialize(new { op, revision = Current(client).Session.Revision, compact }, ClientGame.Json));
    using var doc = JsonDocument.Parse(result);
    if (!doc.RootElement.GetProperty("ok").GetBoolean()) throw new Exception(result[..Math.Min(500, result.Length)]);
    var data = doc.RootElement.GetProperty("data").Clone();
    timings.Add(new { op, ms = watch.Elapsed.TotalMilliseconds, bytes = result.Length,
        steps = data.TryGetProperty("steps", out var steps) ? steps.GetInt32() : 0 });
    return data;
}
var reports = new List<object>();
int factionCount = int.Parse(Environment.GetEnvironmentVariable("EW4_PIPELINE_FACTIONS") ?? "2");
bool contact = bool.Parse(Environment.GetEnvironmentVariable("EW4_PIPELINE_CONTACT") ?? "false");
foreach (int count in (Environment.GetEnvironmentVariable("EW4_PIPELINE_COUNTS") ?? "0,30000").Split(',').Select(int.Parse))
{
    var start = content.Start(count == 0 ? "conquest1" : "conquest9", "fra");
    Console.WriteLine(JsonSerializer.Serialize(new { count, extensions = content.Extensions.Enabled, skills = content.Extensions.Bus.Skills.Count,
        statuses = content.Extensions.Bus.Statuses.Count, scripts = content.Extensions.Bus.Scripts != null }));
    if (count > 0)
    {
        var world = start.Session.World;
        var land = world.Cells.Select((c, i) => (c, i)).Where(p => p.c.Terrain.CanTraverse() && !p.c.Terrain.Sea).Select(p => p.i).ToArray();
        var templates = world.Units.ToArray();
        world = world with { Units = Enumerable.Range(0, count).Select(i => templates[i % templates.Length] with {
            Id = "stress." + i, Cell = land[(int)((long)i * land.Length / count)], CommanderId = null, CommanderBase = null,
            Objective = false, Acted = false, RemainingMovement = templates[i % templates.Length].Combatant.Profile.Movement }).ToArray(),
            Commanders = [], PendingDialogue = null, Story = null,
            Factions = world.Factions.Select(f => f with { Resources = new(10000000, 10000000, 10000000) }).ToArray() };
        start = start with { Session = start.Session with { World = world } };
        if (factionCount > 2)
        {
            var normal = content.Start("conquest1", "fra");
            string player = normal.Session.PlayerFaction;
            var factions = normal.Session.World.Factions.Where(f => f.Id == player || !normal.Rules.Turns.NeutralCamps.Contains(f.Camp))
                .OrderBy(f => f.Id == player ? 0 : 1).Take(factionCount).Select(f => f with {
                    Human = f.Id == player, Defeated = false, PlayerAi = null, HomelandBuildings = null, Capital = null,
                    Resources = new(300, 150, 300), TurnSummary = null }).ToArray();
            if (factions.Length != factionCount) throw new Exception("Not enough active faction templates");
            var templatesMulti = normal.Session.World.Units.Where(u => u.CommanderId == null).ToArray();
            var landMulti = land.Where(i => i % world.Width > world.Width / 3 && i % world.Width < world.Width * 2 / 3
                && i / world.Width > world.Height / 3 && i / world.Width < world.Height * 2 / 3).ToArray();
            if (landMulti.Length < count) landMulti = land;
            int Owner(int cell) => Math.Min(factions.Length - 1, cell % world.Width * factions.Length / world.Width);
            var units = Enumerable.Range(0, count).Select(i => {
                var template = templatesMulti[i % templatesMulti.Length];
                int cell = landMulti[(int)((long)i * landMulti.Length / count)];
                return template with { Id = "multi." + i, Owner = factions[Owner(cell)].Id, Cell = cell,
                    CommanderId = null, CommanderBase = null, PlayerControl = null, Objective = false,
                    Acted = false, RemainingMovement = template.Combatant.Profile.Movement };
            }).ToArray();
            // Spread every country's army into its territory, keeping border contact possible.
            var available = Enumerable.Range(0, factions.Length).Select(owner => new Queue<int>(land.Where(c => Owner(c) == owner)
                .OrderBy(c => Math.Min(c % world.Width - owner * world.Width / factions.Length,
                    (owner + 1) * world.Width / factions.Length - 1 - c % world.Width)).ThenBy(c => c / world.Width))).ToArray();
            for (int i = 0; i < units.Length; i++)
            {
                string owner = factions[i % factions.Length].Id;
                int cell = available[i % factions.Length].Dequeue();
                units[i] = units[i] with { Owner = owner, Cell = cell };
            }
            if (contact)
            {
                var map = new HexMap(world.Width, world.Height);
                var infantry = templatesMulti.First(u => u.Combatant.Profile.Category == "infantry" && u.Combatant.Profile.MaximumRange == 1);
                for (int owner = 0; owner + 1 < factions.Length; owner += 2)
                {
                    var occupiedOther = units.Where((_, i) => i != owner && i != owner + 1).Select(u => u.Cell).ToHashSet();
                    var pair = land.Where(c => Owner(c) == owner && !occupiedOther.Contains(c))
                        .SelectMany(c => Enumerable.Range(0, 6).Select(d => (from: c, to: map.Neighbor(c, d))))
                        .First(p => p.to >= 0 && Owner(p.to) == owner + 1 && world.Cells[p.to].Terrain.CanTraverse()
                            && !world.Cells[p.to].Terrain.Sea && !occupiedOther.Contains(p.to));
                    units[owner] = units[owner] with { Cell = pair.from, Combatant = infantry.Combatant, Morale = infantry.Morale,
                        Transport = false, ExtensionMovementLimit = null, RemainingMovement = infantry.Combatant.Profile.Movement };
                    units[owner + 1] = units[owner + 1] with { Cell = pair.to, Combatant = infantry.Combatant, Morale = infantry.Morale,
                        Transport = false, ExtensionMovementLimit = null, RemainingMovement = infantry.Combatant.Profile.Movement };
                }
            }
            world = world with { ScenarioId = normal.Session.World.ScenarioId, Factions = factions, Units = units,
                Cells = world.Cells.Select((c, i) => c with { Owner = c.Terrain.CanTraverse() ? factions[Owner(i)].Id : null }).ToArray(),
                ActiveFaction = player, ScenarioPlayerFaction = player, Commanders = [], CommanderOffers = [], ScenarioEvents = [], Events = [],
                TriggeredEvents = [], TriggeredScenarioEvents = [], ScenarioChoices = new Dictionary<string, string>(),
                Demands = null, DiplomacyAdjustments = null, Provocations = null, DiplomaticRelations = [], NeutralHostilities = [],
                Jurisdictions = null, ArmyCapacity = null };
            start = start with { Session = start.Session with { World = world, PlayerFaction = player, Markets = [], TavernCandidates = [], RosterDeployments = [] }, Rules = normal.Rules };
            start.Session.Validate();
            Console.WriteLine(JsonSerializer.Serialize(new { factions = factions.Select(f => new { f.Id, f.Camp, units = units.Count(u => u.Owner == f.Id) }) }));
        }
    }
    start = start with { Session = start.Session with { BattleId = "turn-pipeline-fixed", World = start.Session.World with { RandomState = 123456, AiRandomState = 654321,
        Demands = start.Session.World.Demands is { } demands ? demands with { Seed = 123456 } : null } } };
    foreach (bool compact in (Environment.GetEnvironmentVariable("EW4_PIPELINE_COMPACT") ?? "false,true").Split(',').Select(bool.Parse))
    {
        var client = new ClientGame(content, Path.Combine(output, count + "-" + compact));
        field.SetValue(client, start);
        var calls = new List<object>();
        var watch = Stopwatch.StartNew();
        Call(client, "end", compact, calls);
        int steps = 0, requests = 0;
        var countries = new Dictionary<string, (int Steps, double Ms)>(StringComparer.Ordinal);
        var phases = new List<object>();
        while (Current(client).Session.World is { } world && !world.Factions.Single(f => f.Id == world.ActiveFaction).Human
            && world.PendingDialogue == null && world.Phase == TurnPhase.Active)
        {
            string active = world.ActiveFaction;
            var batchWatch = Stopwatch.StartNew();
            var data = Call(client, "ai_batch", compact, calls);
            var previousCountry = countries.GetValueOrDefault(active);
            countries[active] = (previousCountry.Steps + data.GetProperty("steps").GetInt32(), previousCountry.Ms + batchWatch.Elapsed.TotalMilliseconds);
            if (Current(client).Session.World.ActiveFaction != active)
                Console.WriteLine(JsonSerializer.Serialize(new { handoff = active, steps = countries[active].Steps, ms = countries[active].Ms }));
            steps += data.GetProperty("steps").GetInt32(); requests++;
            if (data.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) throw new Exception(error.ToString());
            if (count >= 30000 && steps >= 20 || steps > 5000) break;
        }
        double ms = watch.Elapsed.TotalMilliseconds;
        var current = Current(client);
        var binding = new SessionContentBinding(current.Session.ContentRevision, current.Session.World.ContentRevision, current.Session.Account.ContentRevision);
        var sessionBytes = SessionSave.Encode(current.Session);
        if (!sessionBytes.SequenceEqual(SessionSave.Encode(SessionSave.Decode(sessionBytes, binding).Copy())))
            throw new Exception("Session save/load/copy changed bytes");
        var stable = current.Session.World.Copy(); stable.Validate();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        watch.Restart();
        for (int i = 0; i < 10; i++) { var copied = stable.Copy(); copied.Validate(); }
        var snapshot = new { iterations = 10, ms = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated };
        File.WriteAllBytes(Path.Combine(output, count + "-" + compact + "-world.json"), WorldSave.Encode(current.Session.World));
        var hash = Convert.ToHexStringLower(SHA256.HashData(WorldSave.Encode(current.Session.World)));
        // Measure one actual plan, execution and client commit without generating a view.
        if (count >= 30000)
        for (int i = 0; i < 3; i++)
        {
            current = Current(client);var world = current.Session.World;var rules = current.Rules;var production = rules.Production[world.ActiveFaction];
            watch.Restart();var plan = WorldAi.PlanStep(world, rules.Ai, production, rules.Turns);double planMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();var change = WorldAi.ExecuteStep(world, plan, rules.Ai, production, rules.Turns, rules.Battle);double executeMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();commit.Invoke(client, [change, true]);double commitMs = watch.Elapsed.TotalMilliseconds;
            phases.Add(new { planMs, executeMs, commitMs });
        }
        var reference = start;
        var referenceClient = new ClientGame(content, Path.Combine(output, count + "-reference-" + compact));
        var decisions = new Dictionary<string, int>(StringComparer.Ordinal);
        var handoffPhases = new List<object>();
        var slowPlans = new List<object>();
        field.SetValue(referenceClient, reference);
        var rulesReference = reference.Rules;
        commit.Invoke(referenceClient, [WorldTurns.EndTurn(reference.Session.World, reference.Session.World.Revision, rulesReference.Turns), true]);
        for (int i = 0; i < steps; i++)
        {
            reference = Current(referenceClient);var world = reference.Session.World;var rules = reference.Rules;var production = rules.Production[world.ActiveFaction];
            watch.Restart();
            var plan = WorldAi.PlanStep(world, rules.Ai, production, rules.Turns);
            double planMs = watch.Elapsed.TotalMilliseconds;
            string decision = plan.Production.Command?.GetType().Name ?? (plan.Commander != null ? "commander" : plan.Unit == null ? "handoff"
                : plan.Unit.Candidates[AiPlanning.Best(plan.Unit.Candidates)].Action.GetType().Name);
            decisions[decision] = decisions.GetValueOrDefault(decision) + 1;
            if (planMs > 100) slowPlans.Add(new { faction = world.ActiveFaction, planMs, decision,
                unit = plan.Unit?.UnitId, category = world.Units.FirstOrDefault(u => u.Id == plan.Unit?.UnitId)?.Combatant.Profile.Category });
            watch.Restart();
            var change = WorldAi.ExecuteStep(world, plan, rules.Ai, production, rules.Turns, rules.Battle);
            double executeMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); commit.Invoke(referenceClient, [change, true]);
            if (Current(referenceClient).Session.World.ActiveFaction != world.ActiveFaction)
                handoffPhases.Add(new { faction = world.ActiveFaction, planMs, executeMs, commitMs = watch.Elapsed.TotalMilliseconds });
        }
        var referenceHash = Convert.ToHexStringLower(SHA256.HashData(WorldSave.Encode(Current(referenceClient).Session.World)));
        if (hash != referenceHash) throw new Exception("AI state differs from ordinary plan/execute/commit sequence");
        bool completeRound = current.Session.World.Round == start.Session.World.Round + 1 && current.Session.World.ActiveFaction == start.Session.PlayerFaction;
        if (count < 30000 && !completeRound) throw new Exception("Did not complete an enemy round");
        var countryRows = countries.Select(p => new { faction = p.Key, steps = p.Value.Steps, ms = p.Value.Ms }).ToArray();
        int actualFactionCount = start.Session.World.Factions.Count;
        var row = new { count, factionCount = actualFactionCount, contact, map = start.Map.Id, width = start.Session.World.Width, height = start.Session.World.Height,
            compact, steps, requests, ms, hash, completeRound, round = current.Session.World.Round, calls, phases, handoffPhases, slowPlans, snapshot, countries = countryRows, decisions };
        reports.Add(row);Console.WriteLine(JsonSerializer.Serialize(new { count, factionCount = actualFactionCount, compact, steps, requests, ms, hash, completeRound, snapshot, countries = countryRows, decisions }));
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
}
