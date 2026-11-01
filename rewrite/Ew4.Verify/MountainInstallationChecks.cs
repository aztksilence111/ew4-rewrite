using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class MountainInstallationChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Mountain/installation: " + label); }
        var map = new HexMap(3, 1);
        var actor = new MovementActor { Category = "artillery", Camp = "self", FullBudget = 5, Remaining = 5 };
        MovementCell[] mountains = [new(), new() { TerrainCost = 6 }, new() { TerrainCost = 6 }];
        var range = Movement.Reachable(map, mountains, 0, actor);
        Check(range[1].Remaining == -1 && range[1].Path.SequenceEqual(new[] { 0, 1 }), "native 5-point heavy artillery enters 6-point mountain");
        Check(!range.ContainsKey(2), "overdraw ends the route");
        Check(Movement.Reachable(map, mountains, 0, actor with { Remaining = 1 })[1].Remaining == -5, "positive partial budget permits first step");
        Check(Movement.Reachable(map, mountains, 0, actor with { Remaining = 0 }).Count == 1, "zero budget cannot move");
        Check(!Movement.Reachable(map, mountains, 0, actor, new() { AllowFirstStepOverdraw = false }).ContainsKey(1), "strict opt-out");
        MovementCell[] later = [new(), new() { TerrainCost = 3 }, new() { TerrainCost = 4 }];
        Check(!Movement.Reachable(map, later, 0, actor).ContainsKey(2), "later step cannot overdraw");
        Check(Movement.Reachable(map, later, 0, actor, new() { AllowOverdrawnEntry = true })[2].Remaining == -2, "explicit all-step overdraw remains available");
        foreach (var blocked in new[] { mountains[1] with { Barrier = TerrainBarrier.Mountain }, mountains[1] with { Active = false },
            mountains[1] with { OccupantCamp = "enemy" }, mountains[1] with { Sea = true } })
            Check(!Movement.Reachable(map, [mountains[0], blocked, mountains[2]], 0, actor).ContainsKey(1), "overdraw cannot bypass entry restrictions");
        Check(Movement.DistancesTo(map, mountains, [2], actor)[0] == 12, "AI reverse route crosses mountains over multiple turns");
        Check(Movement.DistancesFrom(map, mountains, 0, actor, new())[2] == 12, "AI forward route agrees");
        Check(Movement.DistancesForAdvance(map, mountains, [2], actor, new(), 0, [1])[0] == 12, "bounded AI route agrees");
        Check(Movement.Components(map, mountains, actor, new()).Distinct().Count() == 1, "AI components do not discard mountain route");

        var fixture = WorldChecks.Example();
        var mover = fixture.Units[1] with { Cell = 0, RemainingMovement = 5, Combatant = fixture.Units[1].Combatant with
            { Profile = fixture.Units[1].Combatant.Profile with { Category = "artillery", Movement = 5, Grade = "0" } } };
        var enemy = fixture.Units[0] with { Cell = 4, Combatant = fixture.Units[0].Combatant with
            { Profile = fixture.Units[0].Combatant.Profile with { Grade = "0" } } };
        var world = fixture with { Width = 5, Height = 1, Units = [mover, enemy],
            Cells = Enumerable.Range(0, 5).Select(i => new CellState { Owner = i == 4 ? "loser" : "winner",
                Terrain = new() { TerrainCost = 6 }, DefenseTerrain = "mountain" }).ToArray() };
        Check(WorldActions.MovableUnits(world, mover.Owner, new()).Contains(mover.Id), "unit navigation includes enclosed heavy artillery");
        var moved = WorldActions.Move(world, world.Revision, mover.Id, 1).State;
        Check(moved.Units.Single(u => u.Id == mover.Id).RemainingMovement == 0, "committed overdraw clamps movement to zero");
        Check(WorldSave.Decode(WorldSave.Encode(moved), moved.ContentRevision).Units.Single(u => u.Id == mover.Id).Cell == 1, "overdraw survives save/load");
        Check(world.Units.Single(u => u.Id == mover.Id).Cell == 0, "move preserves input world");
        var aiWorld = world with { Units = world.Units.Select(u => u.Id == mover.Id ? u with { Behavior = "1" } : u).ToArray(),
            Cells = world.Cells.Select((c, i) => i == 4 ? c with { BuildingKind = "0", BuildingLevel = 1 } : c).ToArray() };
        var aiRules = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var plan = WorldAi.StrategicCandidates(aiWorld, mover.Id, aiRules);
        Check(plan.Candidates.Count == 1 && plan.Candidates[0].Action.Destination == 1, "AI selects first mountain step toward distant enemy");
        Check(WorldAi.ExecuteStrategic(aiWorld, plan, 0, aiRules).State.Units.Single(u => u.Id == mover.Id).Cell == 1, "AI executes mountain step");

        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var content = new ClientContent(Read, ["campaign1_02.btl"]);
        var start = content.Start("campaign1_02", null);
        for (int i = 0; i < 32 && (start.Session.World.PendingDialogue != null || start.Session.World.Phase != TurnPhase.Active); i++)
        {
            var opening = start.Session.World;
            start = start with { Session = start.Session with { World = WorldTurns.Resume(opening, opening.Revision,
                start.Rules.Turns, opening.PendingDialogue).State } };
        }
        Check(start.Session.World.PendingDialogue == null && start.Session.World.Phase == TurnPhase.Active, "French campaign opening completes normally");
        var cases = new[] { ("land", "fence"), ("land", "trench"), ("land", "bunker"), ("forest", "fence"),
            ("mountain", "bunker"), ("land", "fence") };
        var free = start.Session.World.Cells.Select((c, i) => (c, i)).Where(p => p.c.Terrain.CanTraverse() && !p.c.Terrain.Sea
            && p.c.BuildingKind == null && start.Session.World.Units.All(u => u.Cell != p.i)).Take(cases.Length).Select(p => p.i).ToArray();
        Check(free.Length == cases.Length, "French second campaign has fixture locations");
        var cells = start.Session.World.Cells.ToArray();
        for (int i = 0; i < cases.Length; i++) cells[free[i]] = cells[free[i]] with { DefenseTerrain = cases[i].Item1,
            InstallationKind = cases[i].Item2, BuildingKind = i == cases.Length - 1 ? "city" : null,
            BuildingLevel = i == cases.Length - 1 ? 1 : 0 };
        var frenchMap = new HexMap(start.Session.World.Width, start.Session.World.Height);
        bool Empty(int i) => i >= 0 && !free.Contains(i) && cells[i].Terrain.CanTraverse() && !cells[i].Terrain.Sea
            && cells[i].BuildingKind == null && start.Session.World.Units.All(u => u.Cell != i);
        var landing = Enumerable.Range(0, cells.Length).Where(i => Empty(i) && cells[i].Terrain.TerrainCost == 6)
            .SelectMany(i => Enumerable.Range(0, 6).Select(d => (destination: i, origin: frenchMap.Neighbor(i, d), edge: d)))
            .First(p => Empty(p.origin) && (cells[p.destination].Terrain.RiverEdges & (1 << p.edge)) == 0);
        var originalUnit = start.Session.World.Units.First(u => u.Owner == start.Session.PlayerFaction && u.CommanderId == null && !u.Combatant.AtSea);
        var heavy = content.Catalogs.Runtime.Unit("fra", 11, 0).Profile;
        var frenchHeavy = originalUnit with { Cell = landing.origin, RemainingMovement = 5, Transport = false,
            Combatant = originalUnit.Combatant with { Profile = heavy, Health = heavy.MaxHealth, Skills = [], Geography = false } };
        cells[landing.origin] = cells[landing.origin] with { Owner = frenchHeavy.Owner };
        var session = start.Session with { World = start.Session.World with { Cells = cells,
            Units = start.Session.World.Units.Select(u => u.Id == frenchHeavy.Id ? frenchHeavy : u).ToArray() } };
        string folder = Path.Combine(root, "build/mountain-installation-20261101/client");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "session.json"), JsonSerializer.SerializeToUtf8Bytes(
            new ClientSlot("campaign1_02", null, SessionSave.Encode(session)), ClientGame.Json));
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
        var host = new ClientGame(content, folder);
        using var response = JsonDocument.Parse(host.Dispatch("/api/load", "{}"));
        Check(response.RootElement.GetProperty("ok").GetBoolean(), "real client loads French campaign fixture");
        var view = response.RootElement.GetProperty("data");
        var probe = session.World.Units[0];
        for (int i = 0; i < cases.Length; i++)
        {
            var info = view.GetProperty("cells")[free[i]].GetProperty("tileInfo");
            if (cells[free[i]].BuildingKind != null)
            {
                Check(info.GetProperty("defense").GetInt32() > 0, "building retains independent all-category defense");
                Check(info.GetProperty("penalties").GetProperty("cavalry").GetInt32() == 0, "building does not acquire fence penalty");
                continue;
            }
            foreach (string category in new[] { "infantry", "cavalry", "artillery" })
            {
                int shown = info.GetProperty("penalties").GetProperty(category).GetInt32();
                var resolved = CombatContext.Resolve(session.World, probe with { Cell = free[i] }, category, content.Catalogs.Combat.Context, false);
                Check(Math.Abs(resolved.Evasion * 100 - shown) < .001f, "display matches actual defense: " + cases[i] + "/" + category);
            }
        }
        Check(view.GetProperty("cells")[free[0]].GetProperty("tileInfo").GetProperty("penalties").GetProperty("cavalry").GetInt32() == 25,
            "fence cavalry reduction is visible");
        Check(view.GetProperty("cells")[free[3]].GetProperty("tileInfo").GetProperty("penalties").GetProperty("cavalry").GetInt32() == 25,
            "forest and fence take maximum, not sum");
        using var selected = JsonDocument.Parse(host.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "select", cell = landing.origin,
            revision = view.GetProperty("revision").GetInt64() }, ClientGame.Json)));
        Check(selected.RootElement.GetProperty("ok").GetBoolean(), "French campaign selection: " + selected.RootElement);
        Check(selected.RootElement.GetProperty("data").GetProperty("range").EnumerateArray().Any(v => v.GetInt32() == landing.destination),
            "French campaign client exposes actual adjacent mountain as legal destination");
        using var committed = JsonDocument.Parse(host.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "move", unit = frenchHeavy.Id,
            cell = landing.destination, revision = view.GetProperty("revision").GetInt64() }, ClientGame.Json)));
        Check(committed.RootElement.GetProperty("ok").GetBoolean(), "French campaign API commits actual heavy artillery mountain entry");
        File.WriteAllBytes(Path.Combine(folder, "session.json"), JsonSerializer.SerializeToUtf8Bytes(
            new ClientSlot("campaign1_02", null, SessionSave.Encode(session)), ClientGame.Json));
        File.WriteAllText(Path.Combine(folder, "fixture.json"), JsonSerializer.Serialize(new { landing.origin, landing.destination,
            unit = frenchHeavy.Id, installations = free }, ClientGame.Json));
        return checks;
    }
}
