using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Legacy;

namespace Ew4.Mods;

public sealed record StartupAssetResult(ModBase Content, string Fingerprint, bool CacheHit, bool Rebuilt, int CheckedFiles);

public static class StartupAssets
{
    private const int Schema = 1;
    private static readonly object Gate = new();
    private sealed record Receipt(int Schema, string Fingerprint, bool Rebuilt, Dictionary<string, string> Files, string? SaveNamespace = null);

    public static StartupAssetResult Prepare(ModBase baseline, string cacheRoot, Action<ImportProgress>? report = null)
    {
        lock (Gate) return PrepareCore(baseline, Path.GetFullPath(cacheRoot), report ?? (_ => { }));
    }

    private static StartupAssetResult PrepareCore(ModBase baseline, string cacheRoot, Action<ImportProgress> report)
    {
        report(new("working", "检查游戏资源"));
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in baseline.Files)
        {
            byte[] bytes = baseline.Read(path);
            hashes.Add(path, ModFiles.Hash(bytes));
            if (path.StartsWith("original/", StringComparison.Ordinal)) originals.Add(path, bytes);
        }
        JsonObject? reference = null;
        try
        {
            byte[] bytes = baseline.Read("runtime-content.json");
            hashes.Add("runtime-content.json", ModFiles.Hash(bytes));
            reference = ModFiles.Object(bytes);
        }
        catch (FileNotFoundException) { }
        var expectedOriginals = reference?["files"]?.AsArray().Where(p => (string?)p?["path"] is string path
                && path.StartsWith("original/", StringComparison.Ordinal) && ModFiles.RuntimePath(path))
            .ToDictionary(p => (string)p!["path"]!, p => (string)p!["sha256"]!, StringComparer.Ordinal);
        bool originalChanges = expectedOriginals == null || originals.Keys.Any(p => !expectedOriginals.TryGetValue(p, out string? hash) || hash != hashes[p])
            || expectedOriginals.Keys.Any(p => !originals.ContainsKey(p));
        bool LogicPath(string path) => path != ArmyCapacityConfiguration.Path && (path.StartsWith("original/", StringComparison.Ordinal) || path.StartsWith("legacy/", StringComparison.Ordinal)
            || path.StartsWith("rewrite/", StringComparison.Ordinal) && !path.EndsWith(".png", StringComparison.Ordinal));
        var expectedLogic = reference?["files"]?.AsArray().Where(p => (string?)p?["path"] is string path && LogicPath(path) && ModFiles.RuntimePath(path))
            .ToDictionary(p => (string)p!["path"]!, p => (string)p!["sha256"]!, StringComparer.Ordinal);
        var logic = hashes.Where(p => LogicPath(p.Key)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        bool logicChanges = expectedLogic != null && (logic.Any(p => !expectedLogic.TryGetValue(p.Key, out string? hash) || hash != p.Value)
            || expectedLogic.Keys.Any(p => !logic.ContainsKey(p)));
        string logicRevision = ModFiles.Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(logic)));
        string engine = string.Join(':', new[] { typeof(StartupAssets), typeof(Client.ClientContent), typeof(LegacyRuntimeCatalog), typeof(Core.UnitProfile) }
            .Select(t => t.Assembly.ManifestModule.ModuleVersionId));
        string assetRevision = ModFiles.Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hashes)));
        string fingerprint = ModFiles.Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Schema, engine, assetRevision })));
        string target = ModFiles.Under(cacheRoot, fingerprint);
        if (TryCached(baseline, target, fingerprint) is { } cached)
        {
            report(new("complete", "资源检查完成"));
            return new(cached.Content, fingerprint, true, cached.Rebuilt, originals.Count);
        }

        var generated = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[] Read(string path) => generated.TryGetValue(path, out var bytes) ? bytes
            : originals.TryGetValue(path, out bytes) ? bytes : baseline.Read(path);
        XElement Xml(string name) => ModFiles.Xml(Read("original/" + name)).Root ?? throw new InvalidDataException(name + ": 缺少XML根节点");
        foreach (var (path, bytes) in originals)
            InFile(path, () =>
            {
                switch (Path.GetExtension(path))
                {
                    case ".xml":
                        byte[] normalized = LegacyXml.Normalize(bytes, out bool changed);
                        ModFiles.Xml(normalized);
                        if (changed) generated[path] = normalized;
                        break;
                    case ".btl": LegacyBattle.Read(bytes); break;
                    case ".json": using (JsonDocument.Parse(bytes)) { } break;
                }
            });
        var mapFiles = Xml("def_map.xml").Elements("map").Select(e => "original/" + (string)e.Attribute("file")!).ToHashSet(StringComparer.Ordinal);
        foreach (var path in mapFiles)
            InFile(path, () => LegacyTerrainMap.Read(Read(path)));
        foreach (var path in originals.Keys.Where(p => p.EndsWith(".bin", StringComparison.Ordinal) && !mapFiles.Contains(p)))
            InFile(path, () => LegacyBinary.Animation(Read(path)));

        var bundle = LegacyCatalogFiles.Load(p => Read(p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p));
        var changedSources = bundle.Catalog["source_sha256"]!.AsObject()
            .Where(p => !string.Equals((string?)p.Value, ModFiles.Hash(Read("original/" + p.Key)), StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Key).ToArray();
        if (changedSources.Length > 0)
        {
            report(new("working", "重建游戏规则"));
            JsonObject catalog = null!;
            InFile(string.Join(", ", changedSources.Select(n => "original/" + n)), () =>
            {
                catalog = LegacyCatalogConverter.Convert(name => Read("original/" + name), bundle.Catalog);
                LegacyRuntimeCatalog.FromJson(catalog.ToJsonString()).VerifySources(name => Read("original/" + name));
            });
            foreach (var (name, bytes) in LegacyCatalogFiles.Split(catalog, bundle.RuntimeRules, bundle.MetagameRules))
                generated["legacy/" + name] = bytes;
        }
        if (originalChanges || generated.Count > 0)
        {
            RefreshPresentation(baseline, Read, generated, expectedOriginals);
            var manifest = ModFiles.Object(Read("client/manifest.json"));
            manifest["startupAssetsRevision"] = assetRevision;
            generated["client/manifest.json"] = LegacyCatalogFiles.Encode(manifest);
        }
        report(new("working", "验证地图、剧本和规则"));
        // Validate every cell, including terrain outside the playable scenario rectangles.
        LegacyTerrainRules terrain = null!;
        InFile("original/def_terraintype.xml / def_mapterrain.xml", () => terrain = LegacyTerrainRules.FromXml(
            ModFiles.Text(Read("original/def_mapterrain.xml")), ModFiles.Text(Read("original/def_terraintype.xml"))));
        foreach (string path in mapFiles)
            InFile(path, () =>
            {
                var map = LegacyTerrainMap.Read(Read(path));
                foreach (var cell in map.Cells) terrain.Resolve(cell);
            });
        ContentValidation.Check(baseline, Read, baseline.Scenarios, baseline.Files.Union(generated.Keys).ToHashSet(StringComparer.Ordinal),
            startScenarios: originalChanges || logicChanges || generated.Count > 0);

        // A source directory can change while a desktop startup is validating it.
        foreach (var (path, hash) in hashes)
            if (ModFiles.Hash(baseline.Read(path)) != hash)
                throw new InvalidDataException("资源在启动检查期间发生变化，请重试: " + path);
        Directory.CreateDirectory(cacheRoot);
        string stage = ModFiles.Under(cacheRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var (path, bytes) in generated) ModFiles.Write(stage, path, bytes);
            var receipt = new Receipt(Schema, fingerprint, generated.Count > 0,
                generated.ToDictionary(p => p.Key, p => ModFiles.Hash(p.Value), StringComparer.Ordinal),
                logicChanges || expectedLogic == null && changedSources.Length > 0 ? "builtin/" + logicRevision : null);
            ModFiles.WriteJson(stage, "receipt.json", receipt);
            // An unusable cache is replaced only after the replacement has passed validation.
            if (Directory.Exists(target)) Directory.Delete(target, true);
            for (int attempt = 0; ; attempt++)
                try { Directory.Move(stage, target); break; }
                catch (IOException error) when (OperatingSystem.IsWindows() && attempt < 5
                    && (error.HResult & 0xffff) is 5 or 32 or 33 && Directory.Exists(stage) && !Directory.Exists(target))
                { Thread.Sleep(50 << attempt); }
            report(new("complete", "资源检查完成"));
            return new(Overlay(baseline, target, receipt), fingerprint, false, receipt.Rebuilt, originals.Count);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static (ModBase Content, bool Rebuilt)? TryCached(ModBase baseline, string target, string fingerprint)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(Path.Combine(target, "receipt.json")), ModFiles.Json);
            if (receipt == null || receipt.Schema != Schema || receipt.Fingerprint != fingerprint || receipt.Files == null) return null;
            foreach (var (path, hash) in receipt.Files)
                if (!ModFiles.RuntimePath(path) || ModFiles.Hash(File.ReadAllBytes(ModFiles.Under(target, path))) != hash) return null;
            return (Overlay(baseline, target, receipt), receipt.Rebuilt);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return null; }
    }

    private static ModBase Overlay(ModBase baseline, string target, Receipt receipt) => receipt.Files.Count == 0 && receipt.SaveNamespace == null ? baseline
        : new(path => receipt.Files.ContainsKey(path) ? File.ReadAllBytes(ModFiles.Under(target, path)) : baseline.Read(path),
            baseline.Files.Union(receipt.Files.Keys), receipt.SaveNamespace);

    private static void InFile(string path, Action action)
    {
        try { action(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new InvalidDataException(path + ": " + error.Message, error); }
    }

    private static void RefreshPresentation(ModBase baseline, Func<string, byte[]> read, Dictionary<string, byte[]> generated,
        Dictionary<string, string>? expectedOriginals)
    {
        XElement Xml(string name) => ModFiles.Xml(read("original/" + name)).Root!;
        var before = ModFiles.Object(baseline.Read("client/manifest.json"));
        var manifest = (JsonObject)before.DeepClone();
        var strings = LegacyCatalogConverter.Strings(read("original/stringtable_cn.ini"));
        manifest["strings"] = JsonSerializer.SerializeToNode(strings);
        manifest["commanders"] = new JsonObject(Xml("def_commander.xml").Elements().Select(e =>
            KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("id")!, JsonValue.Create((string)e.Attribute("name")!))));
        manifest["buildings"] = new JsonObject(Xml("def_construction.xml").Elements().Select(e =>
            KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("type")!, new JsonArray(e.Elements().Select(x => (JsonNode)LegacyCatalogConverter.Attr(x)).ToArray()))));
        manifest["capitals"] = new JsonObject(Xml("def_capital.xml").Elements().Select(e =>
            KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("id")!, LegacyCatalogConverter.Attr(e))));
        foreach (var army in Xml("def_army.xml").Elements().SelectMany(e => e.Elements("army")))
        {
            string kind = (string)army.Attribute("name")!, country = (string)army.Parent!.Attribute("name")!, grade = (string)army.Attribute("grade")!;
            string id = $"ew4.unit.{country}.{LegacyCatalogConverter.Slug(kind)}.{grade}";
            if (manifest["profiles"]?[id] is not JsonObject profile)
                throw new InvalidDataException("original/def_army.xml: 兵种缺少显示配置: " + id);
            profile["stats"] = LegacyCatalogConverter.Attr(army);
            profile["name"] = strings.GetValueOrDefault("name_" + kind, kind);
            profile["description"] = strings.GetValueOrDefault("desc_" + kind, "");
        }
        bool Changed(string name) => baseline.Files.Contains("original/" + name) && expectedOriginals != null && (!expectedOriginals.TryGetValue("original/" + name, out string? hash)
            || hash != ModFiles.Hash(read("original/" + name)));
        JsonObject? geography = null;
        foreach (var map in Xml("def_map.xml").Elements("map"))
        {
            string name = (string)map.Attribute("name")!, file = (string)map.Attribute("file")!;
            if (manifest["maps"]?[name] is not JsonObject metadata)
                throw new InvalidDataException("original/def_map.xml: 地图缺少显示资源: " + name);
            if ((string?)map.Attribute("textpos") is string text && Changed(text))
                metadata["labels"] = new JsonArray(Xml(text).Elements().Select(e => (JsonNode)LegacyCatalogConverter.Attr(e)).ToArray());
            if (!Changed(file)) continue;
            var terrain = LegacyTerrainMap.Read(read("original/" + file));
            var definitions = Xml("def_mapterrain.xml").Elements().ToArray();
            var overlays = new JsonArray();
            for (int cell = 0; cell < terrain.Cells.Count; cell++)
            {
                var row = terrain.Cells[cell];
                for (int layer = 0; layer < 2; layer++)
                {
                    int kind = layer == 0 ? row.BackgroundKind : row.ForegroundKind, sprite = layer == 0 ? row.BackgroundIndex : row.ForegroundIndex;
                    if (kind is 0 or 11 || layer == 0 && kind == 1 || sprite == 255) continue;
                    if (kind >= definitions.Length) throw new InvalidDataException(file + ": 未知地形贴图分类");
                    var entry = definitions[kind].Elements().SingleOrDefault(e => (int?)e.Attribute("idx") == sprite)
                        ?? throw new InvalidDataException(file + ": 缺少地形贴图索引");
                    overlays.Add(new JsonObject { ["cell"] = cell, ["layer"] = layer, ["seaForeground"] = kind == 1,
                        ["image"] = (string)entry.Attribute("image")!,
                        ["x"] = cell % terrain.Width * 64 + cell / terrain.Width % 2 * 32 + (layer == 0 ? row.BackgroundX : row.ForegroundX),
                        ["y"] = cell / terrain.Width * 54 - 18 + (layer == 0 ? row.BackgroundY : row.ForegroundY) });
                }
            }
            metadata["overlays"] = overlays;
            if (baseline.Files.Contains("rewrite/geography.json"))
            {
                geography ??= ModFiles.Object(read("rewrite/geography.json"));
                foreach (var binding in geography["maps"]!.AsArray().Where(m => (string?)m?["map"] == name))
                {
                    if ((int?)binding!["width"] != terrain.Width || (int?)binding["height"] != terrain.Height)
                        throw new InvalidDataException(file + ": 地图尺寸变化，需要配套更新geography配置");
                    binding["baseTerrainSha256"] = ModFiles.Hash(read("original/" + file));
                }
            }
        }
        if (geography != null) generated["rewrite/geography.json"] = LegacyCatalogFiles.Encode(geography);
        if (!JsonNode.DeepEquals(before, manifest)) generated["client/manifest.json"] = LegacyCatalogFiles.Encode(manifest);
        var pages = ModFiles.Object(read("frontend/conquest-pages.json"));
        var registered = pages["pages"]!.AsArray().SelectMany(p => p!["slots"]!.AsArray()).Where(s => s != null)
            .Select(s => (string)s!["scenario"]!).ToHashSet(StringComparer.Ordinal);
        string[] additional = baseline.Scenarios.Where(s => s.StartsWith("conquest", StringComparison.Ordinal) && !registered.Contains(s)).ToArray();
        for (int offset = 0; offset < additional.Length; offset += 6)
            pages["pages"]!.AsArray().Add(new JsonObject { ["id"] = "startup-" + offset / 6,
                ["slots"] = new JsonArray(Enumerable.Range(0, 6).Select(i => offset + i < additional.Length
                    ? (JsonNode)new JsonObject { ["scenario"] = additional[offset + i] } : null).ToArray()) });
        if (additional.Length > 0) generated["frontend/conquest-pages.json"] = LegacyCatalogFiles.Encode(pages);
        // Country definitions are still validated against the regenerated runtime below.
        var countries = ModFiles.Object(baseline.Read("client/countries.json"));
        if (countries["sourceSha256"] is JsonObject sources)
        {
            bool changed = false;
            foreach (string name in sources.Select(p => p.Key).ToArray())
            {
                string hash = ModFiles.Hash(read("original/" + name));
                if ((string?)sources[name] == hash) continue;
                sources[name] = hash; changed = true;
            }
            if (changed) generated["client/countries.json"] = LegacyCatalogFiles.Encode(countries);
        }
    }
}
