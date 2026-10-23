using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;

namespace Ew4.Mods;

public sealed partial class ModStore
{
    private readonly object gate = new();
    private readonly SemaphoreSlim importing = new(1, 1);
    private readonly ModBase baseline;
    private readonly string root, saves;
    private string? activeKey;
    private ModManifest? activeManifest;
    private ClientGame? game;
    private ImportProgress progress = new("idle", "");
    public string Root => root;
    public ModBase Base => baseline;
    public ModStore(ModBase baseline, string root, string saves)
    {
        this.baseline = baseline; this.root = Path.GetFullPath(root); this.saves = Path.GetFullPath(saves);
        Directory.CreateDirectory(this.root);
        string selected = Path.Combine(this.root, "active.json");
        if (File.Exists(selected))
        {
            try { Activate(JsonSerializer.Deserialize<string>(File.ReadAllText(selected)), persist: false); }
            catch (Exception e) when (Recoverable(e)) { progress = new("error", "上次Mod不可用，已回到原版: " + e.Message); }
        }
    }
    private static bool Recoverable(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException or Ew4.Core.RuleViolation
        or KeyNotFoundException or NullReferenceException or IndexOutOfRangeException or System.Xml.XmlException;
    private string Folder(string key)
    {
        if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("无效的Mod安装标识");
        return ModFiles.Under(root, key);
    }
    private ModManifest Manifest(string key) => JsonSerializer.Deserialize<ModManifest>(File.ReadAllBytes(Path.Combine(Folder(key), "mod.json")), ModFiles.Json)
        ?? throw new InvalidDataException("缺少Mod清单");
    private byte[] ReadFrom(string? key, ModManifest? manifest, string path) => manifest?.Files.ContainsKey(path) == true
        ? File.ReadAllBytes(ModFiles.Under(Folder(key!), path)) : baseline.Read(path);
    public byte[] Read(string path) { lock (gate) return ReadFrom(activeKey, activeManifest, ModFiles.PathName(path)); }
    public string Dispatch(string path, string body)
    {
        try
        {
            if (path == "/api/mods")
            {
                var request = string.IsNullOrWhiteSpace(body) ? new JsonObject() : ModFiles.Object(System.Text.Encoding.UTF8.GetBytes(body));
                if ((string?)request["op"] == "activate") Activate((string?)request["key"]);
                if ((string?)request["op"] == "delete") Delete((string?)request["key"]);
                if ((string?)request["op"] == "import") ImportPrepared((string?)request["token"], (string?)request["id"], (string?)request["name"]);
                if ((string?)request["op"] == "discard") DiscardPrepared((string?)request["token"]);
                return Status();
            }
            lock (gate)
            {
                game ??= NewGame(activeKey, activeManifest);
                return game.Dispatch(path, body);
            }
        }
        catch (Exception e) when (Recoverable(e)) { return JsonSerializer.Serialize(new { error = e.Message }, ModFiles.Json); }
    }
    private ClientGame NewGame(string? key, ModManifest? manifest) => new(baseline.Content(p => ReadFrom(key, manifest, p), manifest?.Scenarios ?? baseline.Scenarios, showHiddenCampaigns: manifest == null),
        manifest == null ? saves : ModFiles.Under(saves, $"mods/{manifest.Id}/{manifest.LogicRevision}"));
    public string Status()
    {
        lock (gate) return JsonSerializer.Serialize(new { active = activeKey, baseRevision = baseline.Revision, mods = List(), import = progress }, ModFiles.Json);
    }
    public ModInfo[] List()
    {
        lock (gate) return Directory.EnumerateDirectories(root).Where(d => Path.GetFileName(d).Length == 64).Select(d =>
        {
            string key = Path.GetFileName(d); ModManifest m;
            try { m = Manifest(key); }
            catch (Exception e) when (Recoverable(e)) { return new ModInfo(key, "invalid", "损坏的内容包", "", key, "", 0, 0, [e.Message]); }
            if (m.Files == null || m.Warnings == null) return new ModInfo(key, "invalid", "损坏的内容包", "", key, "", 0, 0, ["清单字段缺失"]);
            return new ModInfo(key, m.Id, m.Name, m.Version, key, m.LogicRevision,
                m.Files.Keys.Sum(f => { try { return new FileInfo(ModFiles.Under(d, f)).Length; } catch (IOException) { return 0L; } }), m.Files.Count, m.Warnings, activeKey == key);
        }).ToArray();
    }
    public void Activate(string? key, bool persist = true)
    {
        lock (gate)
        {
            ModManifest? m = key == null ? null : Validate(Folder(key));
            var candidate = NewGame(key, m);
            if (persist)
            {
                string pending = Path.Combine(root, "active.pending");
                File.WriteAllText(pending, JsonSerializer.Serialize(key)); File.Move(pending, Path.Combine(root, "active.json"), true);
            }
            activeKey = key; activeManifest = m; game = candidate;
        }
    }
    public void Delete(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == "builtin") throw new InvalidDataException("不能删除内置原版");
        if (!importing.Wait(0)) throw new InvalidOperationException("导入完成后才能删除Mod");
        try
        {
            lock (gate)
            {
                string folder = Folder(key);
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("此Mod已不存在，请刷新列表");
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("不能删除链接目录");
                if (string.Equals(activeKey, key, StringComparison.OrdinalIgnoreCase)) Activate(null);
                Directory.Delete(folder, recursive: true);
                progress = new("complete", "已删除Mod，存档已保留");
            }
        }
        finally { importing.Release(); }
    }
    public void BeginImport(string path, string id, string name, bool deleteSource = false)
        => BeginImportCore(path, id, name, deleteSource, renamePackage: false);
    private void BeginImportCore(string path, string id, string name, bool deleteSource, bool renamePackage)
    {
        if (!importing.Wait(0)) throw new InvalidOperationException("已有导入任务正在运行");
        lock (gate) progress = new("working", "正在检查文件");
        _ = Task.Run(() =>
        {
            try
            {
                var info = ImportCore(path, id, name, p => { lock (gate) progress = p; }, renamePackage);
                lock (gate) progress = new("complete", "导入并校验完成，请选择启用", Key: info.Key);
            }
            catch (Exception e) { lock (gate) progress = new("error", e.Message); }
            finally { if (deleteSource) File.Delete(path); importing.Release(); }
        });
    }
    public ModInfo Import(string path, string id, string name, Action<ImportProgress>? report = null)
    {
        importing.Wait(); try { return ImportCore(path, id, name, report ?? (_ => { })); } finally { importing.Release(); }
    }
    private ModInfo ImportCore(string path, string id, string name, Action<ImportProgress> report, bool renamePackage = false)
    {
        string stage = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            using var source = new ImportSource(path);
            if (source.Contains("mod.json"))
            {
                var incoming = JsonSerializer.Deserialize<ModManifest>(source.Read("mod.json"), ModFiles.Json) ?? throw new InvalidDataException("清单无效");
                if (renamePackage && (incoming.Id != id || incoming.Name != name))
                    ModFiles.WriteJson(stage, "mod.json", incoming with { Id = ModFiles.Identifier(id), Name = name });
                else ModFiles.Write(stage, "mod.json", source.Read("mod.json"));
                foreach (string f in incoming.Files.Keys)
                {
                    if (!ModFiles.RuntimePath(ModFiles.PathName(f))) throw new InvalidDataException("不支持的资源路径: " + f);
                    ModFiles.Write(stage, f, source.Read(f));
                }
                foreach (string reportName in new[] { "import-report.json", "validation-report.json" })
                    if (source.Contains(reportName)) ModFiles.Write(stage, reportName, source.Read(reportName));
            }
            else
            {
                ModFiles.Identifier(id);
                var importer = new LegacyModImporter(baseline, source, stage, report);
                var (scenarios, warnings) = importer.Convert();
                Seal(stage, id, name, "1.0", scenarios, warnings);
            }
            report(new("working", "验证剧本、规则和资源引用"));
            var diagnostics = new List<object>();
            var manifest = Validate(stage, diagnostics);
            ModFiles.WriteJson(stage, "validation-report.json", new { diagnostics });
            string key = ModFiles.Hash(File.ReadAllBytes(Path.Combine(stage, "mod.json")));
            string target = Folder(key);
            if (Directory.Exists(target)) Directory.Delete(stage, true);
            else for (int attempt = 0; ; attempt++)
            {
                try { Directory.Move(stage, target); break; }
                // Windows readers can briefly hold freshly validated files during the rename.
                catch (IOException e) when (OperatingSystem.IsWindows() && attempt < 5
                    && (e.HResult & 0xffff) is 5 or 32 or 33 && Directory.Exists(stage) && !Directory.Exists(target))
                { Thread.Sleep(50 << attempt); }
            }
            return new ModInfo(key, manifest.Id, manifest.Name, manifest.Version, key, manifest.LogicRevision,
                manifest.Files.Keys.Sum(f => new FileInfo(ModFiles.Under(target, f)).Length), manifest.Files.Count, manifest.Warnings);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public void Seal(string folder, string id, string name, string version, string[]? scenarios = null, string[]? warnings = null)
    {
        ModFiles.Identifier(id);
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).Where(ModFiles.RuntimePath).Order(StringComparer.Ordinal)
            .ToDictionary(f => f, f => ModFiles.Hash(File.ReadAllBytes(ModFiles.Under(folder, f))));
        var manifest = new ModManifest(1, id, name, version, baseline.Revision, scenarios ?? baseline.Scenarios, files, warnings ?? []);
        manifest = manifest with { LogicRevision = LogicRevision(manifest, p => files.ContainsKey(p) ? File.ReadAllBytes(ModFiles.Under(folder, p)) : baseline.Read(p)) };
        ModFiles.WriteJson(folder, "mod.json", manifest);
    }
    private string LogicRevision(ModManifest m, Func<string, byte[]> read)
    {
        var names = baseline.Files.Union(m.Files.Keys).Where(p => p.StartsWith("original/", StringComparison.Ordinal) || p.StartsWith("legacy/", StringComparison.Ordinal)
                || p.StartsWith("rewrite/", StringComparison.Ordinal) && !p.EndsWith(".png", StringComparison.Ordinal)
                || p == "client/countries.json" && m.Files.ContainsKey(p))
            .Where(p => !p.EndsWith(".btl", StringComparison.Ordinal) || m.Scenarios.Contains(Path.GetFileNameWithoutExtension(p))).Order(StringComparer.Ordinal);
        return ModFiles.Hash(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', names.Select(p => p + ":" + ModFiles.Hash(read(p))))));
    }
    public ModManifest Validate(string folder) => Validate(folder, null);
    private ModManifest Validate(string folder, List<object>? diagnostics)
    {
        using var source = new ImportSource(folder);
        var m = JsonSerializer.Deserialize<ModManifest>(source.Read("mod.json"), ModFiles.Json) ?? throw new InvalidDataException("清单无效");
        ModFiles.Identifier(m.Id);
        if (m.Schema != 1 || m.BaseRevision != baseline.Revision) throw new InvalidDataException("Mod格式或基础资源版本不匹配，请使用当前版本重新导入原资源");
        if (m.Scenarios.Length == 0 || m.Scenarios.Length > 1000 || m.Scenarios.Distinct().Count() != m.Scenarios.Length) throw new InvalidDataException("剧本清单无效");
        foreach (string s in m.Scenarios) { ModFiles.PathName(s); if (s.Contains('/') || s.Contains('.')) throw new InvalidDataException("剧本ID无效"); }
        foreach (var (file, hash) in m.Files)
        {
            if (!ModFiles.RuntimePath(ModFiles.PathName(file)) || ModFiles.Hash(source.Read(file)) != hash) throw new InvalidDataException("文件类型或哈希不符: " + file);
            if (file.EndsWith(".xml", StringComparison.Ordinal)) ModFiles.Xml(source.Read(file));
            if (file.EndsWith(".png", StringComparison.Ordinal)) Raster.Decode(source.Read(file));
            if (file.StartsWith("client/animations/", StringComparison.Ordinal) && file.EndsWith(".json", StringComparison.Ordinal)) ResourceValidation.CheckAnimation(ModFiles.Object(source.Read(file)));
        }
        byte[] Read(string p) => m.Files.ContainsKey(p) ? source.Read(p) : baseline.Read(p);
        if (LogicRevision(m, Read) != m.LogicRevision) throw new InvalidDataException("规则版本指纹不符");
        var content = baseline.Content(Read, m.Scenarios);
        var scenarioErrors = new List<string>();
        Exception? firstScenarioError = null;
        foreach (var scenario in content.Scenarios)
        {
            try
            {
                var battle = content.Battle(scenario.Id);
                if (!content.Maps.ContainsKey(battle.MapId)) throw new InvalidDataException("剧本缺少地图");
                if (scenario.Mode != 3)
                {
                    var started = content.Start(scenario.Id, scenario.Mode == 2 ? battle.Factions.First(f => f.Camp != 4).CountryId : null);
                    foreach (var diagnostic in started.MapDiagnostics) diagnostics?.Add(new { scenario = scenario.Id, diagnostic.Code, diagnostic.Cell, diagnostic.Value });
                }
            }
            catch (Exception e) when (Recoverable(e)) { firstScenarioError ??= e; scenarioErrors.Add(scenario.Id + ": " + e.Message); }
        }
        if (scenarioErrors.Count > 0) throw new InvalidDataException($"{scenarioErrors.Count} 个剧本无法启动:\n" + string.Join('\n', scenarioErrors), firstScenarioError);
        ResourceValidation.Check(Read, m.Files.Keys.Union(baseline.Files).ToHashSet(StringComparer.Ordinal), content);
        return m;
    }
    public void Export(string key, string destination)
    {
        var m = Validate(Folder(key));
        if (File.Exists(destination)) throw new IOException("输出文件已存在");
        using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (string f in m.Files.Keys.Concat(["mod.json", "import-report.json", "validation-report.json"]).Distinct())
        {
            string file = ModFiles.Under(Folder(key), f); if (File.Exists(file)) zip.CreateEntryFromFile(file, f, CompressionLevel.Optimal);
        }
    }
}
