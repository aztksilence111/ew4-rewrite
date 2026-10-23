using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class PerformanceContractChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        var fixture = WorldChecks.Example();
        var rules = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        // An unbound extension forces the fully validated/deep-copy simulation path.
        var untrusted = rules with { Movement = rules.Movement with { Extensions = new RuleEventBus(new Dictionary<string, ExtensionSkill> { ["unused"] = new() { Id = "unused" } }) } };
        for (int seed = 0; seed < 24; seed++)
        {
            var actor = fixture.Units[1] with { Cell = 0, RemainingMovement = 6, Transport = seed % 2 == 0,
                Combatant = fixture.Units[1].Combatant with { Profile = fixture.Units[1].Combatant.Profile with { Grade = "0" } } };
            var enemy = fixture.Units[0] with { Cell = 19, Combatant = fixture.Units[0].Combatant with { Profile = fixture.Units[0].Combatant.Profile with { Grade = "0" } } };
            var cells = Enumerable.Range(0, 20).Select(i => new CellState { Owner = i < 8 ? actor.Owner : enemy.Owner,
                Terrain = new() { TerrainCost = 1 + (i + seed) % 4, Sea = i / 5 == 2,
                    RiverEdges = (byte)((i + seed) % 64), BridgeEdges = (byte)((i * 3 + seed) % 64), BlockedSeaEdges = (byte)((i * 5 + seed) % 64) } }).ToArray();
            var world = fixture with { Width = 5, Height = 4, Units = [actor, enemy], Cells = cells, AiRandomState = (ulong)(seed + 1) };
            byte[] before = WorldSave.Encode(world);
            var fast = WorldAi.PlanUnit(world, actor.Id, rules);
            var full = WorldAi.PlanUnit(world, actor.Id, untrusted);
            Check(JsonSerializer.Serialize(fast) == JsonSerializer.Serialize(full), "Projected candidates/order/RNG differ from full simulation");
            Check(before.SequenceEqual(WorldSave.Encode(world)), "Planning mutated the source world");
            if (fast.Candidates.FirstOrDefault(c => c.Action is AiMoveCommand) is { Action: AiMoveCommand move })
            {
                var change = WorldAi.ExecuteMove(world, move, rules, new() { ResolveIgnition = false, RecalculateLocalMorale = false });
                Check(change.Movements.Count == 1 && change.Movements[0].Path[0] == actor.Cell
                    && change.Movements[0].Path[^1] == move.Destination, "Execution must carry its actual movement route");
            }
            // Reusing the same WorldState identity after a caller mutates its array must revalidate.
            cells[1] = cells[1] with { Owner = "missing" };
            bool rejected = false;
            try { WorldAi.PlanUnit(world, actor.Id, rules); } catch (RuleViolation) { rejected = true; }
            Check(rejected, "A planning cache escaped its call scope");
        }
        var runtime = new MovementProbe();
        var scriptBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { ["probe"] = new() { Id = "probe",
            Effects = [new() { Event = "movement.modify", Script = "probe.lua", Function = "budget" }] } }, runtime);
        var scriptedActor = fixture.Units[1] with { Cell = 0, RemainingMovement = 6,
            Combatant = fixture.Units[1].Combatant with { SkillSlots = [new("probe", "probe")], Profile = fixture.Units[1].Combatant.Profile with { Grade = "0" } } };
        var scriptedWorld = fixture with { Units = [scriptedActor, fixture.Units[0] with { Cell = 8,
            Combatant = fixture.Units[0].Combatant with { Profile = fixture.Units[0].Combatant.Profile with { Grade = "0" } } }] };
        var scriptedRules = rules with { Movement = rules.Movement with { Extensions = scriptBus } };
        var range = WorldActions.Reachable(scriptedWorld, scriptedActor.Id, scriptedRules.Movement);
        int expectedCalls = 1 + range.Keys.Count(cell => cell != scriptedActor.Cell && !scriptedWorld.Units.Any(u => u.Cell == cell));
        runtime.Calls = 0;
        WorldAi.TacticalMoveCandidates(scriptedWorld, scriptedActor.Id, scriptedRules);
        Check(runtime.Calls == expectedCalls, "Scripted movement must retain every simulation callback");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, ["conquest1.btl"]);
        string folder = Path.Combine(root, "build/pixi-performance-20261023/contracts");
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object command)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), "Incremental command failed");
            return response.RootElement.GetProperty("data").Clone();
        }
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        var cellViews = view.GetProperty("cells").EnumerateArray().ToArray();
        var unitViews = view.GetProperty("units").EnumerateArray().ToDictionary(u => u.GetProperty("id").GetString()!);
        for (int step = 0; step < 18; step++)
        {
            long revision = view.GetProperty("revision").GetInt64();
            var delta = Call("action", new { op = step == 0 ? "end" : "ai", revision, delta = true });
            Check(delta.GetProperty("baseRevision").GetInt64() == revision && delta.GetProperty("cells").ValueKind == JsonValueKind.Null
                && delta.GetProperty("units").ValueKind == JsonValueKind.Null, "Ordinary actions must use the incremental protocol");
            foreach (var update in delta.GetProperty("cellUpdates").EnumerateArray()) cellViews[update.GetProperty("cell").GetInt32()] = update.GetProperty("value");
            foreach (var removed in delta.GetProperty("removedUnits").EnumerateArray()) unitViews.Remove(removed.GetString()!);
            foreach (var updated in delta.GetProperty("unitUpdates").EnumerateArray()) unitViews[updated.GetProperty("id").GetString()!] = updated;
            view = Call("action", new { op = "state" });
            Check(cellViews.Zip(view.GetProperty("cells").EnumerateArray()).All(p => JsonElement.DeepEquals(p.First, p.Second)), "Cell delta differs from full resync");
            var ordered = delta.GetProperty("unitOrder").EnumerateArray().Select(id => unitViews[id.GetString()!]);
            Check(ordered.Zip(view.GetProperty("units").EnumerateArray()).All(p => JsonElement.DeepEquals(p.First, p.Second))
                && unitViews.Count == view.GetProperty("units").GetArrayLength(), "Unit delta/order differs from full resync");
        }
        var resync = Call("action", new { op = "ai", revision = view.GetProperty("revision").GetInt64(), delta = false });
        Check(resync.GetProperty("cells").ValueKind == JsonValueKind.Array && resync.GetProperty("units").ValueKind == JsonValueKind.Array, "Full protocol compatibility lost");
        return checks;
    }
    private sealed class MovementProbe : IRuleScriptRuntime
    {
        internal int Calls;
        public RuleScriptResult Run(string file, string function, RuleScriptContext context, Ew4Random random)
        { Calls++; return new(0); }
    }
}
