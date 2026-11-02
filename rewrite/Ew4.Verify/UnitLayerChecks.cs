using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class UnitLayerChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Unit layers: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + " got " + e.Code); return; }
            throw new Exception("Unit layers accepted " + code);
        }
        var basis = WorldChecks.Example() with { ScenarioPlayerFaction = "winner" };
        basis = basis with { Units = basis.Units.Select(u => u with { Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Grade = "0" } } }).ToArray() };
        var ground = basis.Units[1] with { RemainingMovement = 6 };
        var plane = ground with { Id = "plane", Combatant = ground.Combatant with {
            Profile = ground.Combatant.Profile with { Id = "fixture.air", MinimumRange = 0, MaximumRange = 3 } } };
        var policies = new Dictionary<string, UnitLayerProfile> { ["fixture.air"] = new() { Layer = "air", AttackLayers = ["surface"] } };
        var layers = new UnitLayerRules { Enabled = true, Profiles = policies };
        var world = basis with { UnitLayers = layers, Units = [basis.Units[0], ground, plane] };
        world.Validate(); Check(true, "one surface and one air share a cell");
        Reject((world with { UnitLayers = null }).Validate, "world.capacity");
        Reject((world with { UnitLayers = layers with { Enabled = false } }).Validate, "world.capacity");
        (world with { Units = [basis.Units[0], ground, plane with { Owner = "loser" }] }).Validate();
        Check(true, "opponents coexist across layers");
        Reject((world with { Units = world.Units.Append(plane with { Id = "plane.2" }).ToArray() }).Validate, "world.capacity");
        Reject((world with { UnitLayers = layers with { AirCapacity = 2 }, Units = world.Units.Append(plane with { Id = "plane.2", Owner = "loser" }).ToArray() }).Validate,
            "world.occupants");
        foreach (var invalid in new[] { layers with { AirCapacity = 0 }, layers with { SurfaceCapacity = 33 },
            layers with { Profiles = new Dictionary<string, UnitLayerProfile> { ["a"] = new() { Layer = "space" } } },
            layers with { Profiles = new Dictionary<string, UnitLayerProfile> { ["a"] = new() { AttackLayers = ["air", "air"] } } } })
            Reject(invalid.Validate, "layers.configuration");
        var copy = world.Copy();
        policies["fixture.air"] = policies["fixture.air"] with { AttackLayers = [] };
        Check(WorldUnitLayers.CanAttack(copy, plane, ground) && !WorldUnitLayers.CanAttack(world, plane, ground), "snapshots own layer policies");
        policies["fixture.air"] = new() { Layer = "air", AttackLayers = ["surface"] };
        var save = WorldSave.Encode(world);
        var restored = WorldSave.Decode(save, world.ContentRevision);
        Check(save.SequenceEqual(WorldSave.Encode(restored)) && WorldUnitLayers.Air(restored, restored.Units.Single(u => u.Id == plane.Id)), "layered save roundtrip");
        Check(!Encoding.UTF8.GetString(WorldSave.Encode(basis)).Contains("unitLayers"), "builtin save omits optional layer policy");

        var terrain = world with { Cells = world.Cells.Select((c, i) => c with { Owner = i == 4 ? null : c.Owner, Terrain = c.Terrain with {
            Sea = i == 7, Barrier = i == 4 ? TerrainBarrier.Mountain : TerrainBarrier.None, Active = i != 2,
            TerrainCost = 9, RiverEdges = 63, BlockedSeaEdges = 63 } }).ToArray() };
        var range = WorldActions.Reachable(terrain, plane.Id);
        Check(range.ContainsKey(7) && range.ContainsKey(4) && !range.ContainsKey(2), "air crosses sea, mountains and rivers but not inactive cells");
        var flown = WorldActions.Move(terrain, terrain.Revision, plane.Id, 4).State;
        Check(flown.Cells.Select(c => c.Owner).SequenceEqual(terrain.Cells.Select(c => c.Owner)), "flight never captures territory");
        Check(!flown.Units.Single(u => u.Id == plane.Id).Combatant.AtSea, "air never embarks");
        Reject(() => WorldActions.Move(world, world.Revision, ground.Id, 0), "move.destination");
        var below = world with { Units = [basis.Units[0], ground, plane with { Cell = 7, Owner = "loser" }] };
        var entered = WorldActions.Move(below, below.Revision, ground.Id, 7).State;
        Check(entered.Units.Count(u => u.Cell == 7) == 2, "surface moves below hostile air");
        var groundRange = WorldActions.Reachable(below with { UnitLayers = null }, ground.Id);
        Check(!groundRange.ContainsKey(7), "disabled rules keep hostile occupancy blocking");
        var undo = new MovementHistory();
        var movement = WorldActions.Move(below, below.Revision, ground.Id, 7);
        undo.Record(below, movement.State, ground.Id, movement.Movements[0].Path.ToArray());
        Check(undo.Available(movement.State, ground.Id) != null && undo.Undo(movement.State, ground.Id).Change.State.Units.Single(u => u.Id == ground.Id).Cell == 8,
            "ground undo ignores hostile air in territory changes");
        var airUndo = new MovementHistory();
        var flight = WorldActions.Move(world, world.Revision, plane.Id, 7);
        airUndo.Record(world, flight.State, plane.Id, flight.Movements[0].Path.ToArray());
        Check(airUndo.Undo(flight.State, plane.Id).Change.State.Units.Count(u => u.Cell == 8) == 2, "air undo returns over occupied surface");

        var offer = new RecruitmentOffer { Id = "test.air", Template = plane.Combatant, Building = BuildingRequirement.Any };
        var recruit = world with { Units = [basis.Units[0], ground], Factions = world.Factions.Select(f => f with {
            Recruitment = new Dictionary<string, RecruitmentGrant> { [offer.Id] = new() } }).ToArray() };
        Check(WorldActions.CanRecruit(recruit, 8, offer), "surface does not block aerial recruitment");
        var recruited = WorldActions.Recruit(recruit, recruit.Revision, 8, "new.air", offer).State;
        Check(recruited.Units.Count(u => u.Cell == 8) == 2 && !WorldActions.CanRecruit(recruited, 8, offer), "recruit checks capacity in its own layer");
        var spawn = new ScriptedScenarioEvent { Id = "spawn.air", Effects = [new() { Type = "unit_spawn", Faction = ground.Owner,
            Unit = "event.air", Cells = [8], UnitTemplate = new(plane.Combatant, new()) }] };
        var spawned = ScenarioRules.Evaluate(recruit with { ScenarioEvents = [spawn] }).State;
        Check(spawned.Units.Any(u => u.Id == "event.air" && u.Cell == 8), "scenario reinforcements use layer capacity");
        var blocked = ScenarioRules.Evaluate(world with { ScenarioEvents = [spawn] }).State;
        Check(blocked.Units.Count == world.Units.Count && blocked.TriggeredScenarioEvents.Count == 0, "blocked reinforcement is atomic");
        var mountainSpawn = spawn with { Effects = [spawn.Effects[0] with { Cells = [4] }] };
        Check(ScenarioRules.Evaluate(terrain with { ScenarioEvents = [mountainSpawn] }).State.Units.Any(u => u.Id == "event.air" && u.Cell == 4), "air event deployment permits active neutral mountain");

        var sea = world with { Cells = world.Cells.Select((c, i) => i == 8 ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray(),
            Units = [basis.Units[0], ground with { Combatant = ground.Combatant with { Profile = ground.Combatant.Profile with { Category = "naval" }, AtSea = true } }, plane] };
        sea.Validate(); Check(true, "naval and air share sea cell");
        var removeAir = new ScriptedScenarioEvent { Id = "remove.air", Effects = [new() { Type = "unit_remove", Faction = plane.Owner, Unit = plane.Id }] };
        Check(ScenarioRules.Evaluate(sea with { Units = [basis.Units[0], ground with { Cell = 5 }, plane], ScenarioEvents = [removeAir] }).State.Cells[8].Owner == plane.Owner,
            "removing air never clears sea ownership");
        var removeShip = new ScriptedScenarioEvent { Id = "remove.ship", Effects = [new() { Type = "unit_remove", Faction = ground.Owner, Unit = ground.Id }] };
        var shipGone = ScenarioRules.Evaluate(sea with { ScenarioEvents = [removeShip] }).State;
        Check(shipGone.Cells[8].Owner == null && shipGone.Units.Any(u => u.Id == plane.Id), "air cannot retain sea territory after surface removal");

        var summon = new ExtensionSkill { Id = "test.summon", Kind = "active", Target = "cell", Range = 0,
            Effects = [new() { Event = "skill.activate", Operation = "summon", Profile = ground.Combatant.Profile.Id, Value = 1 }] };
        var bus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [summon.Id] = summon });
        bus.RegisterUnitTemplates([new(ground.Combatant.Profile, new())]);
        var carrier = plane with { Combatant = plane.Combatant with { SkillSlots = [new(summon.Id, "cargo")] } };
        var drop = world with { Units = [basis.Units[0], carrier] };
        var landed = WorldSkillActions.Execute(drop, drop.Revision, carrier.Id, "cargo", null, bus, new(), 8).State;
        Check(landed.Units.Count(u => u.Cell == 8) == 2 && landed.Units.Single(u => u.Id.StartsWith("summon.")).Acted, "same-cell summon lands on free surface and cannot act");
        Check(!bus.ValidCellTarget(world, carrier, summon, 8), "transport cannot summon into occupied surface");

        var enemyGround = basis.Units[0] with { Cell = 7 };
        var enemyAir = plane with { Id = "enemy.air", Owner = "loser", Cell = 7 };
        var battle = world with { Units = [ground, plane, enemyGround, enemyAir] };
        var battleRules = new WorldBattleRules { CheckFactionSurvival = false, ResolveIgnition = false };
        Reject(() => WorldBattle.Preview(battle, 0, ground.Id, enemyAir.Id, battleRules), "attack.target");
        Reject(() => WorldBattle.Preview(battle, 0, plane.Id, enemyAir.Id, battleRules), "attack.target");
        Check(!WorldBattle.Preview(battle, 0, plane.Id, enemyGround.Id, battleRules).Main.Counterattack, "surface cannot retaliate against bomber by default");
        var fighterRules = layers with { Profiles = new Dictionary<string, UnitLayerProfile> {
            ["fixture.air"] = new() { Layer = "air", AttackLayers = ["air"], CounterattackLayers = [] } } };
        var fighters = battle with { UnitLayers = fighterRules };
        Check(!WorldBattle.Preview(fighters, 0, plane.Id, enemyAir.Id, battleRules).Main.Counterattack, "explicit empty counterattack layers disable retaliation");
        var sameCell = battle with { Units = [plane, enemyGround with { Cell = plane.Cell }] };
        Check(WorldBattle.Preview(sameCell, 0, plane.Id, enemyGround.Id, battleRules).Main.DefenderDamage > 0, "zero minimum range permits cross-layer attack at same cell");
        var artillery = plane with { Combatant = plane.Combatant with { Profile = plane.Combatant.Profile with { Traits = ["splash"] } } };
        var splashRules = new SplashRules { RequiredTrait = "splash" };
        var hex = new HexMap(world.Width, world.Height);
        int splashCell = hex.Neighbor(enemyGround.Cell, SplashCombat.FacesRight(hex, artillery.Cell, enemyGround.Cell) ? splashRules.RightDirection : splashRules.LeftDirection);
        Check(splashCell >= 0, "splash fixture neighbor");
        var splashWorld = battle with { Units = [artillery, enemyGround, enemyAir with { Cell = splashCell }] };
        Check(SplashCombat.Target(splashWorld, artillery, enemyGround, splashRules) == null, "surface splash never hits overlapping air");
        var secondGround = enemyGround with { Id = "second.ground", Cell = splashCell };
        splashWorld = splashWorld with { Units = splashWorld.Units.Append(secondGround).ToArray() };
        Check(SplashCombat.Target(splashWorld, artillery, enemyGround, splashRules)?.Id == secondGround.Id, "splash locates same-layer secondary even if air listed first");
        var ai = new WorldAiRules { ExperienceLevels = new Dictionary<string, int> { [ground.Veterancy] = 0 },
            Tactics = new() { Grades = new Dictionary<string, float> { ["0"] = 1 } } };
        Check(WorldAi.StrikeCandidates(battle, plane.Id, ai).All(c => c.Action.TargetId == enemyGround.Id), "AI strikes only permitted layers");
        foreach (var candidate in WorldAi.TacticalMoveCandidates(battle, plane.Id, ai))
        {
            WorldAi.ExecuteMove(battle, candidate.Action, ai, battleRules).State.Validate();
            Check(true, "AI move/strike commits valid occupancy");
        }
        var strategy = WorldAi.StrategicCandidates(terrain, plane.Id, ai);
        foreach (var candidate in strategy.Candidates)
            Check(WorldUnitLayers.HasSpace(terrain, plane.Combatant.Profile, plane.Owner, candidate.Action.Destination, plane.Id), "AI strategic landing uses air capacity");
        var site = TurnSettlement.EndSite(new() { BuildingKind = "city", BuildingHealing = 20, Environment = 1,
            Units = [new() { Id = "air", Health = 50, MaximumHealth = 100, IgnoreSiteEffects = true },
                new() { Id = "surface", Health = 50, MaximumHealth = 100 }] }, new Ew4Random(1));
        Check(site.Units[0].Health == 50 && site.Units[1].Health != 50, "air ignores ground healing and environmental damage");
        checks += ClientChecks(root);
        return checks;
    }

    private static int ClientChecks(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Unit layer client: " + label); }
        var files = new Dictionary<string, byte[]>();
        byte[] Read(string path) => files.TryGetValue(path, out var value) ? value : File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var baseline = new ClientContent(Read, ["conquest1.btl"]);
        var initial = baseline.Start("conquest1", "fra");
        Check(initial.Session.World.UnitLayers == null, "builtin remains disabled");
        var ground = initial.Session.World.Units.First(u => u.Owner == initial.Session.PlayerFaction && u.Combatant.Profile.Category == "infantry");
        var other = initial.Session.World.Units.First(u => u.Owner == ground.Owner && u.Combatant.Profile.Id != ground.Combatant.Profile.Id);
        var rules = new UnitLayerRules { Enabled = true, Profiles = new Dictionary<string, UnitLayerProfile> {
            [other.Combatant.Profile.Id] = new() { Layer = "air", AttackLayers = ["air"] } } };
        void Manifest(int api = 3) => files["rewrite/extension.json"] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, apiVersion = api, unitLayers = "rewrite/unit-layers.json" });
        void Config(UnitLayerRules value) => files["rewrite/unit-layers.json"] = JsonSerializer.SerializeToUtf8Bytes(value, ExtensionLoader.Json);
        Manifest(); Config(rules);
        var content = new ClientContent(Read, ["conquest1.btl"]);
        var start = content.Start("conquest1", "fra");
        var w = start.Session.World;
        Check(w.Units.Where(u => WorldUnitLayers.Air(w, u)).All(u => !u.Combatant.AtSea && u.Combatant.BuildingKind == null), "imported air flags do not inherit surface site");
        var stacked = start.Session with { World = w with { Units = w.Units.Select(u => u.Id == other.Id ? u with { Cell = ground.Cell } : u).ToArray() } };
        stacked.Validate();
        string folder = Path.Combine(root, "build/unit-layers-20261102/profile");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(stacked)), ClientGame.Json));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object body)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), route + " " + response.RootElement.GetRawText()[..Math.Min(300, response.RootElement.GetRawText().Length)]);
            return response.RootElement.GetProperty("data").Clone();
        }
        var view = Call("load", new { slot = 1 });
        Check(view.GetProperty("unitLayers").GetBoolean(), "mod opt-in reaches client view");
        long revision = view.GetProperty("revision").GetInt64();
        var selected = Call("action", new { op = "select", cell = ground.Cell, unit = other.Id, revision });
        Check(selected.GetProperty("unit").GetString() == other.Id && selected.GetProperty("occupants").GetArrayLength() == 2, "select air by ID at shared cell");
        Check(Call("unit-info", new { unit = other.Id, revision }).GetProperty("layer").GetString() == "air", "unit properties expose configured layer");
        Check(Call("action", new { op = "select", cell = ground.Cell, revision }).GetProperty("unit").GetString() == ground.Id, "surface selected by default");
        using (var invalid = JsonDocument.Parse(host.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "select", cell = ground.Cell, unit = "absent", revision }))))
            Check(!invalid.RootElement.GetProperty("ok").GetBoolean() && invalid.RootElement.GetProperty("code").GetString() == "selection.unit", "reject selecting unit from wrong cell");
        Call("save", new { slot = 1 });
        Check(Call("load", new { slot = 1 }).GetProperty("units").EnumerateArray().Count(u => u.GetProperty("cell").GetInt32() == ground.Cell) == 2, "client save restores both layers");
        var incompatible = start.Session with { World = start.Session.World with { UnitLayers = null } };
        File.WriteAllBytes(Path.Combine(folder, "slot-2.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(incompatible)), ClientGame.Json));
        using (var mismatch = JsonDocument.Parse(host.Dispatch("/api/load", "{\"slot\":2}")))
            Check(!mismatch.RootElement.GetProperty("ok").GetBoolean() && mismatch.RootElement.GetProperty("code").GetString() == "layers.save", "save with missing layer policy cannot reinterpret Mod state");
        Manifest(2);
        try { ExtensionLoader.Load(Read); throw new Exception("API2 accepted layers"); } catch (RuleViolation e) { Check(e.Code == "extension.version", "requires API3"); }
        Manifest(); Config(rules with { Profiles = new Dictionary<string, UnitLayerProfile> { ["unknown"] = new() { Layer = "air" } } });
        try { _ = new ClientContent(Read, ["conquest1.btl"]); throw new Exception("unknown profile accepted"); } catch (RuleViolation e) { Check(e.Code == "layers.profile", "unknown profiles rejected"); }
        Config(rules with { Enabled = false });
        Check(new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra").Session.World.UnitLayers == null, "explicit false is disabled");
        files.Clear();
        var after = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra");
        Check(after.Session.World.UnitLayers == null && after.Session.World.ContentRevision == initial.Session.World.ContentRevision, "returning to builtin restores original rules and content binding");
        return checks;
    }
}
