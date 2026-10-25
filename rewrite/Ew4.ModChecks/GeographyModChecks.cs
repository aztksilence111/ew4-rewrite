using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;

internal static class GeographyModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidDataException("Geography Mod: " + label); checks++; }
        string data = Path.Combine(root, "rewrite/data");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        string evidence = Path.Combine(root, "build/impassable-geography-20261025");
        string run = Path.Combine(evidence, "mod-" + Guid.NewGuid().ToString("N"));
        string author = Path.Combine(run, "author");
        Directory.CreateDirectory(Path.Combine(author, "rewrite"));
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        string[] scenarios = ["conquest1", "conquest7"];
        Directory.CreateDirectory(Path.Combine(author, "original"));
        foreach (string file in new[] { "def_battlelist.xml", "conquest7.btl", "conquest7.names.xml" })
            File.Copy(Path.Combine(root, "rewrite/content/eastasia/original", file), Path.Combine(author, "original", file), true);
        byte[] AuthorRead(string p) => p.StartsWith("original/", StringComparison.Ordinal) && File.Exists(Path.Combine(author, p))
            ? File.ReadAllBytes(Path.Combine(author, p)) : baseline.Read(p);
        var content = baseline.Content(AuthorRead, scenarios);
        var maps = new List<GeographyMap>();
        var tests = new List<(string Scenario, int Mountain, int Ocean, int Pass)>();
        foreach (string scenario in scenarios)
        {
            var started = content.Start(scenario, "fra");
            var world = started.Session.World;
            var free = world.Cells.Select((c, i) => (c, i)).Where(p => p.c.Terrain.CanTraverse()
                && p.c.Name == null && p.c.BuildingKind == null && p.c.InstallationKind == null && !p.c.Objective
                && world.Units.All(u => u.Cell != p.i)).ToArray();
            int mountain = free.First(p => !p.c.Terrain.Sea).i, ocean = free.First(p => p.c.Terrain.Sea).i;
            int pass = free.First(p => !p.c.Terrain.Sea && p.i != mountain).i;
            string name = content.MapNames[int.Parse(started.Map.Id["legacy.map.".Length..], System.Globalization.CultureInfo.InvariantCulture)];
            var entry = XDocument.Parse(System.Text.Encoding.UTF8.GetString(baseline.Read("original/def_map.xml")))
                .Root!.Elements().Single(e => (string?)e.Attribute("name") == name);
            maps.Add(new(name, world.Width, world.Height,
                Convert.ToHexStringLower(SHA256.HashData(baseline.Read("original/" + (string)entry.Attribute("file")!))),
                [new(mountain % world.Width, mountain / world.Width, "impassable_mountain"),
                 new(ocean % world.Width, ocean / world.Width, "impassable_sea"),
                 new(pass % world.Width, pass / world.Width, "mountain_pass")]));
            tests.Add((scenario, mountain, ocean, pass));
        }
        var config = new GeographyCatalog(1, maps.ToArray());
        void Write(GeographyCatalog value) => File.WriteAllBytes(Path.Combine(author, "rewrite/geography.json"),
            JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(ExtensionLoader.Json) { WriteIndented = true }));
        File.WriteAllText(Path.Combine(author, "rewrite/extension.json"), "{\"schema\":1,\"apiVersion\":3,\"geography\":\"rewrite/geography.json\"}\n");
        Write(config);
        store.Seal(author, "impassable-geography", "通用禁行地形示例", "1.0.0", scenarios);
        var first = store.Validate(author);
        var info = store.Import(author, "ignored", "ignored");
        store.Activate(info.Key);
        var installed = baseline.Content(store.Read, scenarios);
        foreach (var test in tests)
        {
            var started = installed.Start(test.Scenario, "fra");
            Check(started.Session.World.Cells[test.Mountain].Terrain.Barrier == TerrainBarrier.Mountain, "mountain applies on " + test.Scenario);
            Check(started.Session.World.Cells[test.Ocean].Terrain.Barrier == TerrainBarrier.Ocean, "ocean applies on " + test.Scenario);
            Check(started.Session.World.Cells[test.Pass].Terrain.MinimumEntryCost == 6
                && started.Session.World.Cells[test.Pass].Terrain.CanTraverse(), "difficult mountain pass applies on " + test.Scenario);
            Check(started.Session.World.Cells[test.Mountain].Owner == null, "barrier remains neutral");
            var response = JsonNode.Parse(store.Dispatch("/api/start", JsonSerializer.Serialize(new { scenario = test.Scenario, country = "fra" })))!;
            Check((bool?)response["ok"] == true, "installed Mod starts through real API");
        }
        string archive = Path.Combine(run, "impassable-geography.ew4mod");
        store.Export(info.Key, archive);
        Check(store.Import(archive, "ignored", "ignored").Key == info.Key, "export/reimport preserves identity");
        Write(config with { Maps = [maps[0] with { TerrainOverrides = [maps[0].TerrainOverrides[0]] }, maps[1]] });
        store.Seal(author, "impassable-geography", "通用禁行地形示例", "1.0.1", scenarios);
        Check(store.Validate(author).LogicRevision != first.LogicRevision, "geography edit changes save fingerprint");
        Write(config with { Visuals = new() { ["impassable_sea"] = new(["missing-geography.png"], "Bad") } });
        bool rejected = false;
        try {
            store.Seal(author, "impassable-geography", "通用禁行地形示例", "1.0.0", scenarios);
            store.Validate(author);
        }
        catch (InvalidDataException e) when (e.Message.Contains("Geography sprite missing", StringComparison.Ordinal)) { rejected = true; }
        Check(rejected, "missing custom sprite rejected before import");
        Write(config);
        store.Seal(author, "impassable-geography", "通用禁行地形示例", "1.0.0", scenarios);
        File.WriteAllText(Path.Combine(evidence, "mod-checks.json"), JsonSerializer.Serialize(new { checks, errors = 0, scenarios, package = archive }));
        return checks;
    }
}
