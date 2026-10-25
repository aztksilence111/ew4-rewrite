using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class GeographyChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Geography: " + label); checks++; }
        void Reject(Action action, string code) {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + ": " + e.Code); return; }
            throw new InvalidOperationException("Geography accepted " + code);
        }
        foreach (bool sea in new[] { false, true })
        foreach (string category in new[] { "infantry", "cavalry", "artillery", "naval" })
        foreach (bool skill in new[] { false, true })
        foreach (bool overdraw in new[] { false, true })
        {
            var ordinary = new MovementCell { Sea = sea, TerrainCost = sea ? 1 : 6 };
            var blocked = ordinary with { Barrier = sea ? TerrainBarrier.Ocean : TerrainBarrier.Mountain };
            var actor = new MovementActor { Category = category, Camp = "a", Geography = skill, Transport = true, FullBudget = 10000, Remaining = 10000 };
            var rules = new MovementRules { AllowOverdrawnEntry = overdraw };
            for (int edge = 0; edge < 6; edge++)
                Check(Movement.EntryCost(ordinary, blocked with { BridgeEdges = 63, RiverEdges = 63 }, edge, actor, rules) == null,
                    "no skill, budget, bridge or overdraw bypass");
            var cells = new[] { ordinary, blocked, ordinary };
            var map = new HexMap(3, 1);
            Check(!Movement.Reachable(map, cells, 0, actor, rules).ContainsKey(2), "no crossing barrier");
            Check(Movement.Reachable(map, cells, 1, actor, rules).Count == 0, "no path from illegal origin");
            Check(!Movement.DistancesTo(map, cells, [2], actor, rules).ContainsKey(0), "reverse distance respects barrier");
            Check(Movement.DistancesTo(map, cells, [1], actor, rules).Count == 0, "barrier is not a distance-field seed");
            Check(Movement.Components(map, cells, actor, rules)[1] == -1, "barrier excluded from connected components");
        }
        var baseCells = Enumerable.Range(0, 9).Select(_ => new CellState()).ToArray();
        baseCells[4] = new() { Terrain = new() { Barrier = TerrainBarrier.Mountain, TerrainCost = 6 }, DefenseTerrain = "mountain" };
        var definition = new WorldMapDefinition("geography.test", "Fixture", 3, 3, baseCells);
        var encoded = MapDocument.Encode(definition);
        Check(JsonNode.Parse(encoded)!["version"]!.GetValue<int>() == 2, "new map declares version 2");
        Check(MapDocument.Decode(encoded).Map.Cells[4].Terrain.Barrier == TerrainBarrier.Mountain, "modern map preserves barrier");
        Reject(() => definition.Edit([new(4, baseCells[4] with { Name = "Bad site" })]), "geography.occupied");
        Reject(() => definition.Edit([new(4, baseCells[4] with { Terrain = baseCells[4].Terrain with { Sea = true } })]), "geography.terrain");
        Reject(() => definition.Edit([new(4, baseCells[4] with { Terrain = baseCells[4].Terrain with { Barrier = (TerrainBarrier)99 } })]), "geography.terrain");
        var example = WorldChecks.Example();
        var cellsWithBarrier = example.Cells.Select((c, i) => i == 4 ? baseCells[4] : c).ToArray();
        var world = example with { Cells = cellsWithBarrier };
        world.Validate();
        var caster = world.Units[0];
        var teleport = new ExtensionSkill { Id = "test.teleport", Target = "cell", Range = 100,
            Effects = [new() { Event = "active", Operation = "teleport", Value = 1 }] };
        var summon = new ExtensionSkill { Id = "test.summon", Target = "cell", Range = 100,
            Effects = [new() { Event = "active", Operation = "summon", Profile = caster.Combatant.Profile.Id, Value = 1 }] };
        var bus = new RuleEventBus(new Dictionary<string, ExtensionSkill>());
        bus.RegisterUnitTemplates([new(caster.Combatant.Profile, caster.Support)]);
        Check(!bus.ValidCellTarget(world, caster, teleport, 4), "teleport cannot land on barrier");
        Check(!bus.ValidCellTarget(world, caster, summon, 4), "summon cannot land on barrier");
        Check(WorldSave.Decode(WorldSave.Encode(world), world.ContentRevision).Cells[4].Terrain.Barrier == TerrainBarrier.Mountain, "save preserves barrier");
        string oldSave = Encoding.UTF8.GetString(WorldSave.Encode(example)).Replace("\"version\":5,", "\"version\":4,", StringComparison.Ordinal);
        Check(WorldSave.Decode(Encoding.UTF8.GetBytes(oldSave), example.ContentRevision).Cells.All(c => c.Terrain.Barrier == TerrainBarrier.None), "version 4 defaults to ordinary terrain");
        var invalidUnit = world.Units[0] with { Cell = 4 };
        Reject(() => (world with { Units = [invalidUnit] }).Validate(), "world.unit");
        Check(!RosterDeployment.IsDeploymentLocation(world, invalidUnit), "roster cannot deploy on barrier");
        Check(!WorldActions.CanRecruit(world, 4, new RecruitmentOffer { Id = "fixture" }), "recruitment refuses barrier");
        var expanded = TerritoryClaims.Expand(new(3,3), cellsWithBarrier, [3], [], "winner");
        Check(expanded[4].Owner == null, "territory expansion does not claim barrier");
        var jurisdictions = TerritorialJurisdictions.Create(world);
        Check(jurisdictions.CellCenters[4] == -1, "jurisdiction does not flood through barrier");

        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string p) => overrides.TryGetValue(p, out var value) ? value : File.ReadAllBytes(p.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", p[5..]) : Path.Combine(root, "rewrite/data", p));
        var builtin = new ClientContent(Read, ["conquest1.btl"]);
        Check(builtin.Extensions.Geography.Maps.Single().Map == "eastasia", "built-in geography loaded");
        var eastasia = LegacyMapDefinitions.Import(builtin.Maps[3], builtin.Catalogs.Terrain, "eastasia", "East Asia");
        eastasia = builtin.Extensions.Geography.Apply("eastasia", eastasia);
        int blockedCount = eastasia.Cells.Count(c => c.Terrain.Barrier == TerrainBarrier.Mountain);
        Check(blockedCount > 80, "Himalayan crest has impassable cells");
        var data = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "maps/eastasia_rewrite/impassable/himalaya-cells.json")))!;
        foreach (int cell in data["passMountainCells"]!.AsArray().Select(n => n!.GetValue<int>()))
        {
            var terrain = eastasia.Cells[cell].Terrain;
            Check(terrain.CanTraverse() && terrain.TerrainCost == 6 && terrain.MinimumEntryCost == 6
                && eastasia.Cells[cell].DefenseTerrain == "mountain", "pass remains difficult mountain terrain");
            foreach (string category in new[] { "infantry", "cavalry", "artillery" })
            foreach (bool skill in new[] { false, true })
                Check(Movement.EntryCost(new(), terrain with { RiverEdges = 0 }, 0,
                    new() { Category = category, Geography = skill, FullBudget = 30, Remaining = 30 }, new()) == 6,
                    "no infantry or geography shortcut through high pass");
        }
        Check(MapDocument.Decode(MapDocument.Encode(eastasia)).Map.Cells.Any(c => c.Terrain.MinimumEntryCost == 6), "map preserves pass entry floor");
        var passSave = world with { Cells = world.Cells.Select((c,i) => i == 4 ? c with { Terrain = new() { TerrainCost = 6, MinimumEntryCost = 6 }, DefenseTerrain = "mountain" } : c).ToArray() };
        Check(WorldSave.Decode(WorldSave.Encode(passSave), passSave.ContentRevision).Cells[4].Terrain.MinimumEntryCost == 6, "save preserves pass difficulty");
        Reject(() => definition.Edit([new(4, baseCells[4] with { Terrain = new() { MinimumEntryCost = -1 } })]), "geography.cost");
        foreach (var pass in data["passes"]!.AsArray())
        {
            var route = pass!["cells"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray();
            Check(route.All(i => eastasia.Cells[i].Terrain.CanTraverse()), "historical pass fully traversable");
            Check(route.Zip(route.Skip(1)).All(p => new HexMap(eastasia.Width,eastasia.Height).Distance(p.First,p.Second) == 1), "continuous pass chain");
        }
        int mapId = 1;
        var original = builtin.Start("conquest1", "fra");
        var free = original.Session.World.Cells.Select((c,i)=>(c,i)).Where(p => p.c.Terrain.Active && p.c.BuildingKind == null
            && p.c.InstallationKind == null && p.c.Name == null && !p.c.Objective && original.Session.World.Units.All(u=>u.Cell!=p.i)).ToArray();
        int mountain = free.First(p=>!p.c.Terrain.Sea).i;
        int ocean = free.First(p=>p.c.Terrain.Sea).i;
        var mapSource = builtin.Maps[mapId];
        var sourceFile = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(Read("original/def_map.xml"))).Root!.Elements()
            .Single(e => (int)e.Attribute("id")! == mapId).Attribute("file")!.Value;
        var config = new GeographyCatalog(1, [new(builtin.MapNames[mapId], mapSource.Width, mapSource.Height,
            Convert.ToHexStringLower(SHA256.HashData(Read("original/"+sourceFile))), [
                new(mountain%mapSource.Width,mountain/mapSource.Width,"impassable_mountain"),
                new(ocean%mapSource.Width,ocean/mapSource.Width,"impassable_sea")])]);
        overrides["rewrite/extension.json"] = Encoding.UTF8.GetBytes("{\"schema\":1,\"apiVersion\":3,\"geography\":\"rewrite/geography.json\"}");
        void Set(GeographyCatalog c) => overrides["rewrite/geography.json"] = JsonSerializer.SerializeToUtf8Bytes(c, ExtensionLoader.Json);
        Set(config);
        var custom = new ClientContent(Read, ["conquest1.btl"]);
        var started = custom.Start("conquest1", "fra");
        Check(started.Session.World.Cells[mountain].Terrain.Barrier == TerrainBarrier.Mountain, "real scenario applies mountain override");
        Check(started.Session.World.Cells[ocean].Terrain.Barrier == TerrainBarrier.Ocean, "same map applies ocean override");
        Check(started.Session.World.Cells[mountain].Owner == null, "scenario territory cannot own barrier");
        Check(started.Session.World.Cells[mountain].Terrain.Active, "barrier remains visible/selectable");
        Check(started.Map.Cells[mountain].Terrain.Barrier == TerrainBarrier.Mountain, "exportable started map contains geography");
        Set(config with { Maps = [config.Maps[0] with { Width = 1 }] });
        Reject(()=>new ClientContent(Read,["conquest1.btl"]), "geography.source");
        Set(config with { Maps = [config.Maps[0] with { BaseTerrainSha256 = new string('0',64) }] });
        Reject(()=>new ClientContent(Read,["conquest1.btl"]), "geography.source");
        Set(config with { Maps = [config.Maps[0] with { TerrainOverrides = [config.Maps[0].TerrainOverrides[0], config.Maps[0].TerrainOverrides[0]] }] });
        Reject(()=>new ClientContent(Read,["conquest1.btl"]), "geography.cells");
        Set(config with { Maps = [config.Maps[0] with { TerrainOverrides = [new(0,0,"unknown")] }] });
        Reject(()=>new ClientContent(Read,["conquest1.btl"]), "geography.cells");
        Set(config with { Maps = [config.Maps[0] with { TerrainOverrides = [config.Maps[0].TerrainOverrides[0] with { Type="impassable_sea" }] }] });
        Reject(()=>new ClientContent(Read,["conquest1.btl"]), "geography.environment");
        Set(config);
        overrides["rewrite/extension.json"] = Encoding.UTF8.GetBytes("{\"schema\":1,\"apiVersion\":2,\"geography\":\"rewrite/geography.json\"}");
        Reject(()=>new ClientContent(Read,["conquest1.btl"]), "extension.version");
        var output = Path.Combine(root,"build/impassable-geography-20261025");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"core-checks.json"), JsonSerializer.Serialize(new { checks, errors=0, blockedCount, mountain, ocean }));
        return checks;
    }
}
