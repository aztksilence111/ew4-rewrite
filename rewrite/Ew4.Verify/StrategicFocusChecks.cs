using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class StrategicFocusChecks
{
    private static WorldState Basis() => WorldChecks.Example() with {
        Units = WorldChecks.Example().Units.Select(u => u with {
            Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Grade = "0" } }
        }).ToArray()
    };
    private static WorldAiRules Rules() => new() { Tactics = LegacyAiTactics.Rules(),
        ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        var basis = Basis();
        var actor = basis.Units[1] with { Cell = 31, RemainingMovement = 6, Behavior = "1" };
        var rules = Rules();
        foreach (bool transport in new[] { false, true })
        foreach (bool mirror in new[] { false, true })
        {
            int Cell(int index) => mirror ? 62 - index : index;
            var mover = actor with { Cell = Cell(31), Transport = transport,
                Combatant = actor.Combatant with { AtSea = transport } };
            var world = basis with { Width = 21, Height = 3, Units = [mover],
                Cells = Enumerable.Range(0, 63).Select(i => new CellState {
                    Owner = i == Cell(0) || i == Cell(34) ? "loser" : "winner",
                    BuildingKind = i == Cell(0) || i == Cell(34) ? "0" : null,
                    BuildingLevel = i == Cell(0) || i == Cell(34) ? 1 : 0,
                    Terrain = new() { Sea = transport && i != Cell(0) && i != Cell(34) }
                }).ToArray() };
            var bytes = WorldSave.Encode(world);
            var plan = WorldAi.StrategicCandidates(world, mover.Id, rules);
            Check(plan.Candidates.Single().Action.GoalCell == Cell(34), "equal-value nearby goal beats distant map corner, transport=" + transport + " mirror=" + mirror);
            Check(bytes.SequenceEqual(WorldSave.Encode(world)), "target planning preserves input");
            var restored = WorldSave.Decode(bytes, world.ContentRevision);
            Check(plan.Candidates.SequenceEqual(WorldAi.StrategicCandidates(restored, mover.Id, rules).Candidates), "target choice survives save reload");
            var peaceful = world with { Factions = world.Factions.Select(f => f with { Camp = "b" }).ToArray() };
            Check(WorldAi.StrategicCandidates(peaceful, mover.Id, rules).Candidates.Count == 0, "no hostile objective means no corner fallback");
        }
        return checks;
    }

    public static void Benchmark()
    {
        var basis = Basis();
        var actor = basis.Units[1] with { Cell = 3050, RemainingMovement = 6, Behavior = "1" };
        var enemies = Enumerable.Range(0, 1000).Select(i => basis.Units[0] with { Id = "enemy." + i, Cell = i * 6 }).ToArray();
        var world = basis with { Width = 100, Height = 60, Units = enemies.Append(actor).ToArray(),
            UnitLayers = new UnitLayerRules { Enabled = true },
            Cells = Enumerable.Range(0, 6000).Select(i => new CellState { Owner = i == actor.Cell ? "winner" : "loser" }).ToArray() };
        var rules = Rules();
        WorldAi.StrategicCandidates(world, actor.Id, rules);
        var times = new List<double>(); var bytes = new List<long>(); AiStrategyPlan? result = null;
        for (int i = 0; i < 7; i++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            result = WorldAi.StrategicCandidates(world, actor.Id, rules);
            times.Add(timer.Elapsed.TotalMilliseconds); bytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { cells = world.Cells.Count, units = world.Units.Count,
            medianMs = times.Order().ElementAt(3), bytesPerDecision = bytes.Order().ElementAt(3),
            planSha256 = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result))),
            scope = "Synthetic desktop layered strategy decision, seven samples; not a phone or full round" }));
    }
}
