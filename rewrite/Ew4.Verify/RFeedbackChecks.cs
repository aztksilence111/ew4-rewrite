using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class RFeedbackChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/transport_budget_vectors.json")));
        foreach (var v in vectors.RootElement.EnumerateArray())
        {
            int budget = v.GetProperty("override").GetInt32();
            if (budget == 0) budget = v.GetProperty("budget").GetInt32();
            var actor = Movement.AtOrigin(new() { Transport = v.GetProperty("transport").GetBoolean(), FullBudget = budget, Remaining = budget },
                new() { Sea = v.GetProperty("sea").GetBoolean() });
            Check(actor.FullBudget == v.GetProperty("expected").GetInt32() && actor.Remaining == actor.FullBudget, "native transport budget");
        }
        var fixture = WorldChecks.Example();
        var mover = fixture.Units[1] with { Cell = 0, Transport = true, RemainingMovement = 25,
            Combatant = fixture.Units[1].Combatant with { AtSea = true, Profile = fixture.Units[1].Combatant.Profile with { Movement = 25 } } };
        var sea = fixture with { Width = 8, Height = 1, Units = [mover, fixture.Units[0] with { Cell = 7 }],
            Cells = Enumerable.Range(0, 8).Select(i => new CellState { Owner = i == 7 ? "loser" : "winner", Terrain = new() { Sea = true } }).ToArray() };
        foreach (var moveRules in new[] { new WorldMoveRules(), new WorldMoveRules { Extensions = new RuleEventBus(new Dictionary<string, ExtensionSkill>()) } })
        {
            var range = WorldActions.Reachable(sea, mover.Id, moveRules);
            Check(range.ContainsKey(2) && !range.ContainsKey(3), "high movement transport sails at most two cells");
            var sailed = WorldActions.Move(sea, sea.Revision, mover.Id, 2, moveRules).State;
            Check(sailed.Units.Single(u => u.Id == mover.Id).RemainingMovement == 0, "transport move spends turn budget");
            try { WorldActions.Move(sea, sea.Revision, mover.Id, 3, moveRules); throw new InvalidOperationException("unlimited transport"); }
            catch (RuleViolation e) { Check(e.Code == "move.unreachable", "server rejects distant transport destination"); }
        }
        var navy = sea with { Units = [mover with { Transport = false, Combatant = mover.Combatant with { Profile = mover.Combatant.Profile with { Category = "naval" } } }, sea.Units[1]] };
        Check(WorldActions.Reachable(navy, mover.Id).ContainsKey(6), "naval ships retain own movement");
        var coast = sea with { Cells = sea.Cells.Select((c, i) => i == 1 ? c with { Terrain = c.Terrain with { Sea = false } } : c).ToArray() };
        Check(WorldActions.Reachable(coast, mover.Id)[1].Remaining == 0 && !WorldActions.Reachable(coast, mover.Id).ContainsKey(2), "landing consumes transport budget");
        var spent = sea with { Units = [mover with { RemainingMovement = 0 }, sea.Units[1]] };
        Check(WorldActions.Reachable(spent, mover.Id).Count == 0, "spent transport cannot regain movement");

        var definition = new Commander { Id = "economist", Skills = [Ew4Combat.Skill(24)] };
        var economy = fixture with { Round = 2, Commanders = [new() { Id = definition.Id, Owner = "winner" }],
            Cells = fixture.Cells.Select((c, i) => c with { BuildingKind = i == 8 ? "city" : null, BuildingLevel = i == 8 ? 1 : 0 }).ToArray() };
        var turns = new WorldTurnRules { Buildings = [new("city", 1, new(100, 20, 40))] };
        economy = WorldCommanders.Assign(economy, economy.Revision, "unit.2", definition.Id,
            new() { Definitions = new Dictionary<string, Commander> { [definition.Id] = definition } }).State;
        var economist = economy.Units.Single(u => u.Id == "unit.2");
        Check(WorldTurns.SiteIncome(economy.Cells[8], 8, turns, economist) == new Resources(180, 36, 72), "master panel income");
        var before = economy.Factions.Single(f => f.Id == "winner").Resources;
        var enemyTurn = WorldTurns.EndTurn(economy, economy.Revision, turns).State;
        var playerTurn = WorldTurns.EndTurn(enemyTurn, enemyTurn.Revision, turns).State;
        var after = playerTurn.Factions.Single(f => f.Id == "winner").Resources;
        Check(after == new Resources(before.Money + 180, before.Industry + 36, before.Food + 72), "real turn deposits master income exactly once");
        Check(WorldSave.Encode(WorldSave.Decode(WorldSave.Encode(playerTurn), "fixture.1")).SequenceEqual(WorldSave.Encode(playerTurn)), "economic save roundtrip");

        string assets = Path.Combine(root, "rewrite/data/original");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("original/", StringComparison.Ordinal)
            ? Path.Combine(assets, path[9..]) : Path.Combine(root, "rewrite/data/legacy", path[5..]));
        var content = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        var start = content.Start("conquest1", "fra");
        var unit = start.Session.World.Units.First(u => u.Owner == start.Session.PlayerFaction && u.CommanderId != null && !u.Combatant.AtSea && u.Combatant.Profile.Movement > 0);
        unit = unit with { RemainingMovement = unit.Combatant.Profile.Movement, Support = unit.Support with { IncomeMultiplier = 1.8f } };
        var world = start.Session.World with { Units = start.Session.World.Units.Select(u => u.Id == unit.Id ? unit : u).ToArray(),
            Cells = start.Session.World.Cells.Select((c, i) => i == unit.Cell ? c with { IncomeOverride = new(100, 20, 40) } : c).ToArray() };
        string folder = Path.Combine(root, "build/r-feedback-20261023/clients", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var session = start.Session with { World = world };
        File.WriteAllBytes(Path.Combine(folder, "session.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object args)
        {
            using var parsed = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(args, ClientGame.Json)));
            Check(parsed.RootElement.GetProperty("ok").GetBoolean(), route + " success: " + parsed.RootElement.ToString()[..Math.Min(180, parsed.RootElement.ToString().Length)]);
            return parsed.RootElement.GetProperty("data").Clone();
        }
        var loaded = Call("load", new { });
        Check(loaded.GetProperty("cells")[unit.Cell].GetProperty("tileInfo").GetProperty("income").Deserialize<Resources>(ClientGame.Json) == new Resources(180, 36, 72), "client panel includes master");
        int destination = WorldActions.Reachable(world, unit.Id, start.Rules.Movement).Keys.First(i => i != unit.Cell && world.Cells[i].Owner == unit.Owner
            && !world.Units.Any(u => u.Cell == i));
        var moved = Call("action", new { op = "move", unit = unit.Id, cell = destination, revision = loaded.GetProperty("revision").GetInt64(), delta = true });
        var update = moved.GetProperty("cellUpdates").EnumerateArray().Single(p => p.GetProperty("cell").GetInt32() == unit.Cell).GetProperty("value");
        Check(update.GetProperty("tileInfo").GetProperty("income").Deserialize<Resources>(ClientGame.Json) == new Resources(100, 20, 40), "delta clears departed commander's bonus without terrain changes");
        return checks;
    }
}
