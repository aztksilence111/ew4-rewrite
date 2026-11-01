using System.Text.Json;
using Ew4.Core;

internal static class Contracts
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(label);
    }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (RuleViolation error) { Check(error.Code == code, code); return; }
        throw new InvalidOperationException("Expected rejection: " + code);
    }
    public static int Run(string root)
    {
        checks = 0;
        using var victory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/victory_vectors.json")));
        foreach (var vector in victory.RootElement.EnumerateArray())
        {
            var facts = vector.GetProperty("countries").EnumerateArray().Select((v, i) => new FactionFacts(i.ToString(),
                v.GetProperty("camp").GetInt32().ToString(), !v.GetProperty("ai").GetBoolean(), v.GetProperty("defeated").GetBoolean(),
                v.GetProperty("objectives").GetInt32())).ToArray();
            var rules = new VictoryRules(vector.GetProperty("round_limit").GetInt32(), vector.GetProperty("objectives_enabled").GetBoolean());
            Check(Victory.Evaluate(vector.GetProperty("camp").GetInt32().ToString(), vector.GetProperty("round").GetInt32(), facts, rules)
                .ToString().ToLowerInvariant() == vector.GetProperty("expected").GetString(), "native victory");
        }
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/movement_economy_vectors.json")));
        string[] categories = ["infantry", "cavalry", "artillery", "naval", "fort"];
        foreach (var vector in data.RootElement.GetProperty("movement").EnumerateArray())
        {
            bool Flag(string key) => vector.GetProperty(key).GetBoolean();
            var cell = new MovementCell { Sea = Flag("sea"), TerrainCost = 6,
                BlockedSeaEdges = Flag("blocked") ? (byte)4 : (byte)0, OccupantCamp = Flag("enemy") ? "b" : "a" };
            var actor = new MovementActor { Category = categories[vector.GetProperty("category").GetInt32()], Camp = "a",
                Transport = Flag("transport"), Geography = Flag("geography"), FullBudget = 6, Remaining = 6 };
            Check((Movement.EntryCost(cell, cell, 2, actor, new MovementRules { BlockEnemyOccupied = Flag("block_occupied") }) ?? -1)
                == vector.GetProperty("expected").GetInt32(), "native movement cost");
        }
        foreach (var vector in data.RootElement.GetProperty("supply").EnumerateArray())
        {
            var actual = TurnEconomy.Start(2, new Resources(0, 0, vector.GetProperty("food").GetInt64()), new Resources(0, 0, 0),
                vector.GetProperty("consumption").GetInt64());
            Check(actual.Resources.Food == vector.GetProperty("expected")[0].GetInt64()
                && actual.Shortage == vector.GetProperty("expected")[1].GetInt32(), "native food consumption");
        }
        foreach (var vector in data.RootElement.GetProperty("morale").EnumerateArray())
        {
            var a = vector.GetProperty("inputs");
            Check(TurnEconomy.Morale(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32(), a[4].GetBoolean(), a[5].GetBoolean())
                == vector.GetProperty("expected").GetInt32(), "native morale");
        }
        var saved = new Resources(10000, 12, 8);
        Check(TurnEconomy.Start(1, saved, new Resources(1, 1, 100), 50).Resources == saved, "round1 does not add/consume");
        Check(TurnEconomy.Start(2, saved, new Resources(100, 0, 0), 0).Resources.Money == 10100, "no hidden9999cap");
        Check(TurnEconomy.Start(2, new Resources(0, 0, 0), new Resources(0, 0, 10), 10).Shortage == 0, "income before consumption");

        var registry = new Registry<string>();
        for (int i = 0; i < 1000; i++) registry.Add("faction." + i, "势力" + i);
        Check(registry.Count == 1000 && registry.Get("faction.999") == "势力999", "dynamic Unicode registry");
        Reject(() => registry.Add("faction.999", "duplicate"), "registry.duplicate_or_invalid");
        Reject(() => registry.Get("unknown"), "registry.unknown");
        var camps = Enumerable.Range(0, 200).Select(i => new FactionFacts("f." + i, "camp." + i, i == 0, false, 1)).ToArray();
        Check(Victory.Evaluate("camp.0", 1, camps, new VictoryRules()) == BattleOutcome.Continue, "more than3camps");

        var corridor = new HexMap(5005, 1);
        var corridorCells = Enumerable.Range(0, corridor.Count).Select(_ => new MovementCell()).ToArray();
        var walker = new MovementActor { FullBudget = 20000, Remaining = 20000 };
        var reachable = Movement.Reachable(corridor, corridorCells, 0, walker);
        Check(reachable.Count == 5005 && reachable[5004].Path.Count == 5005 && reachable[5004].Remaining == 4988,
            "search exceeds4000records");
        Check(new HexMap(40000, 2).Index(39999, 1) == 79999, "cellID exceeds signed16bit");
        var pair = new HexMap(2, 1);
        var river = new MovementCell { RiverEdges = 8 };
        var limited = new MovementActor { Category = "cavalry", FullBudget = 6, Remaining = 3 };
        Check(Movement.Reachable(pair, [new(), river], 0, limited)[1].Remaining == -3, "native first-step overdraft");
        Check(!Movement.Reachable(pair, [new(), river], 0, limited, new MovementRules { AllowFirstStepOverdraw = false }).ContainsKey(1),
            "explicit strict budget");
        Check(Movement.Reachable(pair, [new(), river], 0, limited, new MovementRules { AllowOverdrawnEntry = true })[1].Remaining == -3,
            "explicit legacy overdraft");
        Check(Movement.Reachable(pair, [new(), river with { BridgeEdges = 8, TerrainCost = 3 }], 0, limited)[1].Remaining == 0, "bridge");
        Check(!Movement.Reachable(pair, [new(), new() { Sea = true }], 0, limited).ContainsKey(1), "land cannot enter sea");
        var transport = limited with { Transport = true, Remaining = 6 };
        Check(Movement.Reachable(pair, [new(), new() { Sea = true }], 0, transport)[1].Remaining == 0, "transport transition");
        Reject(() => Movement.Reachable(pair, [new()], 0, limited), "movement.state");

        // Independent Bellman-Ford oracle verifies weighted reachability, including detours.
        var random = new Random(1926);
        var small = new HexMap(7, 6);
        for (int trial = 0; trial < 50; trial++)
        {
            var cells = Enumerable.Range(0, small.Count).Select(_ => new MovementCell { TerrainCost = random.Next(1, 7), Active = random.Next(4) != 0 }).ToArray();
            int start = random.Next(small.Count);
            cells[start] = cells[start] with { Active = true };
            int[] distance = Enumerable.Repeat(int.MaxValue, small.Count).ToArray();
            distance[start] = 0;
            for (int pass = 0; pass < small.Count; pass++)
                for (int cell = 0; cell < small.Count; cell++)
                    if (distance[cell] != int.MaxValue)
                        for (int direction = 0; direction < 6; direction++)
                        {
                            int next = small.Neighbor(cell, direction);
                            if (next >= 0 && cells[next].Active) distance[next] = Math.Min(distance[next], distance[cell] + cells[next].TerrainCost);
                        }
            var actual = Movement.Reachable(small, cells, start, new MovementActor { Category = "cavalry", FullBudget = 15, Remaining = 15 });
            for (int cell = 0; cell < small.Count; cell++)
            {
                Check(actual.ContainsKey(cell) == (distance[cell] <= 15), "weighted reachability");
                if (actual.ContainsKey(cell)) Check(actual[cell].Remaining == 15 - distance[cell], "weighted remaining");
            }
        }
        return checks;
    }
}
