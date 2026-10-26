using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Mods;
using Ew4.Core;

internal static class StartupAssetChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("Startup assets: " + label); checks++; }
        string data = Path.Combine(root, "rewrite/data"), run = Path.Combine(root, "build/startup-assets-20261026/checks", Guid.NewGuid().ToString("N"));
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", path[9..]) : Path.Combine(data, path));
        string[] files = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(data, p).Replace('\\', '/'))
            .Concat(["frontend/conquest-pages.json", "frontend/city-names.json"]).ToArray();
        var baseline = new ModBase(Read, files);
        string cache = Path.Combine(run, "cache");
        var first = StartupAssets.Prepare(baseline, cache);
        Check(!first.CacheHit && !first.Rebuilt, "valid stock assets need validation once and no generated rules");
        Check(first.Content.Revision == baseline.Revision, "unmodified stock keeps existing Mod identity and accounts");
        Check(first.Content.SaveNamespace == null, "unmodified stock retains the original save directory");
        Check(first.CheckedFiles == baseline.Files.Count(p => p.StartsWith("original/", StringComparison.Ordinal)), "all original files are scanned");
        var phases = new List<string>();
        var warm = StartupAssets.Prepare(baseline, cache, p => phases.Add(p.Message));
        Check(warm.CacheHit && !warm.Rebuilt && warm.Fingerprint == first.Fingerprint, "unchanged startup reuses validation receipt");
        Check(!phases.Any(p => p.Contains("验证地图") || p.Contains("重建")), "cache hit skips conversion and scenario validation");

        var overlay = new Dictionary<string, byte[]>();
        var modified = new ModBase(path => overlay.TryGetValue(path, out var bytes) ? bytes : Read(path), files);
        var army = ModFiles.Xml(Read("original/def_army.xml"));
        var unit = army.Descendants("army").First();
        int oldHealth = (int)unit.Attribute("strength")!;
        string unitId = "ew4.unit." + (string)unit.Parent!.Attribute("name")! + "." + LegacyCatalogConverter.Slug((string)unit.Attribute("name")!) + "." + (string)unit.Attribute("grade")!;
        unit.SetAttributeValue("strength", oldHealth + 7);
        overlay["original/def_army.xml"] = Encoding.UTF8.GetBytes(army.ToString());
        var rebuilt = StartupAssets.Prepare(modified, cache);
        var content = rebuilt.Content.Content(rebuilt.Content.Read, rebuilt.Content.Scenarios);
        Check(rebuilt.Rebuilt && !rebuilt.CacheHit, "changed source automatically rebuilds");
        Check(content.Catalogs.Runtime.Profiles[unitId].Profile.MaxHealth == oldHealth + 7, "gameplay receives edited XML health");
        var manifest = JsonNode.Parse(rebuilt.Content.Read("client/manifest.json"))!;
        Check((string?)manifest["profiles"]?[unitId]?["stats"]?["strength"] == (oldHealth + 7).ToString(), "UI stats receive the same edited XML health");
        Check(rebuilt.Content.Revision != baseline.Revision, "rebuilt content changes frontend and Mod identity");
        Check(rebuilt.Content.SaveNamespace?.StartsWith("builtin/", StringComparison.Ordinal) == true, "edited gameplay resources use an isolated profile");
        string saves = Path.Combine(run, "saves");
        Directory.CreateDirectory(saves);
        byte[] account = JsonSerializer.SerializeToUtf8Bytes(new PlayerAccount { Id = "local", Medals = 12345,
            ContentRevision = first.Content.Content(first.Content.Read, first.Content.Scenarios).Catalogs.Accounts.SourceRevision }, Ew4.Client.ClientGame.Json);
        File.WriteAllBytes(Path.Combine(saves, "account.json"), account);
        var stockStore = new ModStore(first.Content, Path.Combine(run, "mods"), saves);
        Check((bool?)JsonNode.Parse(stockStore.Dispatch("/api/headquarters", "{}"))?["ok"] == true, "stock profile initializes");
        var editedStore = new ModStore(rebuilt.Content, Path.Combine(run, "mods"), saves);
        Check((bool?)JsonNode.Parse(editedStore.Dispatch("/api/headquarters", "{}"))?["ok"] == true, "edited assets enter game despite an existing stock profile");
        Check(File.ReadAllBytes(Path.Combine(saves, "account.json")).SequenceEqual(account), "existing stock profile remains unchanged");
        Check(StartupAssets.Prepare(modified, cache).CacheHit, "generated rules survive process-style reopening");
        string generated = Path.Combine(cache, rebuilt.Fingerprint, "legacy/units.json");
        File.WriteAllText(generated, "{}");
        var repaired = StartupAssets.Prepare(modified, cache);
        Check(!repaired.CacheHit && repaired.Content.Content(repaired.Content.Read, repaired.Content.Scenarios)
            .Catalogs.Runtime.Profiles[unitId].Profile.MaxHealth == oldHealth + 7, "corrupt generated cache rebuilds from original sources");
        byte[] receipt = File.ReadAllBytes(Path.Combine(cache, repaired.Fingerprint, "receipt.json"));

        void Reject(string path, byte[] bytes, string expected)
        {
            overlay.Clear(); overlay[path] = bytes;
            try { StartupAssets.Prepare(modified, cache); }
            catch (InvalidDataException error)
            {
                Check(error.Message.Contains(expected, StringComparison.Ordinal), "invalid input names its file: " + expected);
                Check(File.ReadAllBytes(Path.Combine(cache, repaired.Fingerprint, "receipt.json")).SequenceEqual(receipt), "failed candidate preserves prior validated cache");
                return;
            }
            throw new InvalidOperationException("Invalid startup input accepted: " + path);
        }
        Reject("original/def_army.xml", "<armies>"u8.ToArray(), "def_army.xml");
        Reject("original/conquest1.btl", [1, 2, 3], "conquest1.btl");
        Reject("original/europe.bin", [1, 2, 3], "europe.bin");
        var terrains = ModFiles.Xml(Read("original/def_terraintype.xml"));
        terrains.Root!.Elements().Single(e => (string?)e.Attribute("type") == "sea").SetAttributeValue("movementcost", 0);
        Reject("original/def_terraintype.xml", Encoding.UTF8.GetBytes(terrains.ToString()), "def_terraintype.xml");
        overlay.Clear();
        var battle = Ew4.Legacy.LegacyBattle.Read(Read("original/conquest1.btl"));
        byte[] battleBytes = battle.ToBytes();
        var extra = new ModBase(path => path == "original/conquest99.btl" ? battleBytes : Read(path), files.Append("original/conquest99.btl"));
        var added = StartupAssets.Prepare(extra, Path.Combine(run, "added"));
        Check(added.Content.Scenarios.Contains("conquest99"), "new valid BTL is discovered at startup");
        Check(added.Content.Revision != baseline.Revision, "BTL-only change updates content identity");
        overlay.Clear();
        byte[] mapBytes = Read("original/eastasia.bin");
        mapBytes[24] ^= 1;
        overlay["original/eastasia.bin"] = mapBytes;
        var changedMap = StartupAssets.Prepare(modified, Path.Combine(run, "map"));
        Check(changedMap.Rebuilt && changedMap.Content.Content(changedMap.Content.Read, changedMap.Content.Scenarios)
            .Maps[3].ToBytes().SequenceEqual(mapBytes), "valid BIN change regenerates overlays and geography binding");
        Check(StartupAssets.Prepare(baseline, cache).Content.Revision == baseline.Revision, "restoring source assets does not retain generated overlays");
        return checks;
    }
}
