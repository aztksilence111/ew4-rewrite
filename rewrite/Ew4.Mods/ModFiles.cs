using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Ew4.Client;
using Ew4.Legacy;

namespace Ew4.Mods;

public sealed record ModManifest(int Schema, string Id, string Name, string Version, string BaseRevision,
    string[] Scenarios, Dictionary<string, string> Files, string[] Warnings, string LogicRevision = "");
public sealed record ModInfo(string Key, string Id, string Name, string Version, string Revision, string LogicRevision,
    long Bytes, int Files, string[] Warnings, bool Active = false);
public sealed record ImportProgress(string Status, string Message, int Completed = 0, int Total = 0, string? Key = null);

public sealed class ModBase(Func<string, byte[]> read, IEnumerable<string> files, string? saveNamespace = null)
{
    public string? SaveNamespace { get; } = saveNamespace == null ? null : ModFiles.PathName(saveNamespace);
    public string[] Files { get; } = files.Select(ModFiles.PathName).Where(ModFiles.RuntimePath).Order(StringComparer.Ordinal).ToArray();
    public byte[] Read(string path) => read(ModFiles.PathName(path));
    public System.Text.Json.Nodes.JsonObject Catalog() => LegacyCatalogFiles.Load(p => Read(
        p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p)).Catalog;
    public string[] Scenarios => Files.Where(p => p.StartsWith("original/", StringComparison.Ordinal) && p.EndsWith(".btl", StringComparison.Ordinal))
        .Select(Path.GetFileNameWithoutExtension).Select(p => p!).ToArray();
    private string? revision;
    public string Revision => revision ??= ModFiles.Hash(Read("legacy/catalog.json").Concat(Read("client/manifest.json"))
        .Concat(Files.Where(p => p.StartsWith("legacy/", StringComparison.Ordinal) && LegacyCatalogFiles.PartNames.Contains(p[7..]))
            .Order(StringComparer.Ordinal).SelectMany(Read)).ToArray());
    public ClientContent Content(Func<string, byte[]> reader, IEnumerable<string> scenarios, bool showHiddenCampaigns = false) => new(path => reader(
        path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path), scenarios.Select(s => s + ".btl"), showHiddenCampaigns: showHiddenCampaigns);
}

internal static class ModFiles
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal const long MaximumFile = 256L * 1024 * 1024;
    internal const long MaximumTotal = 1024L * 1024 * 1024;
    internal static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    internal static string PathName(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Any(c => c < 32 || "\\:*?\"<>|".Contains(c))
            || path.StartsWith('/') || path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ')
                || System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidDataException("不合法的资源路径: " + path);
        return path;
    }
    internal static string Under(string root, string relative)
    {
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, PathName(relative)));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("资源路径越界");
        return full;
    }
    internal static void Write(string root, string name, byte[] bytes)
    {
        string path = Under(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
    }
    internal static void WriteJson<T>(string root, string name, T value) => Write(root, name, JsonSerializer.SerializeToUtf8Bytes(value, Json));
    internal static XDocument Xml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 16 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    internal static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    internal static JsonObject Object(byte[] bytes) => JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException("缺少JSON对象");
    internal static bool RuntimePath(string path) => path switch
    {
        "legacy/catalog.json" or "legacy/runtime_rule_tables.json" or "legacy/metagame_rule_tables.json" => true,
        _ when path.StartsWith("legacy/", StringComparison.Ordinal) && LegacyCatalogFiles.PartNames.Contains(path[7..]) => true,
        "frontend/conquest-pages.json" or "frontend/city-names.json" => true,
        _ when path.StartsWith("rewrite/", StringComparison.Ordinal) => new[] { ".json", ".lua", ".png" }.Contains(Path.GetExtension(path)),
        _ when path.StartsWith("original/", StringComparison.Ordinal) => new[] { ".btl", ".bin", ".xml", ".ini" }.Contains(Path.GetExtension(path)),
        _ when path.StartsWith("client/", StringComparison.Ordinal) => new[] { ".png", ".json", ".ttf", ".woff", ".woff2", ".mp3", ".wav", ".ogg" }.Contains(Path.GetExtension(path)),
        _ => false
    };
    internal static string Identifier(string value)
    {
        if (value.Length is < 1 or > 64 || value.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.'))
            || value is "." or ".." or "builtin" || value.EndsWith('.')
            || new[] { "con", "prn", "aux", "nul", "com1", "com2", "com3", "lpt1", "lpt2", "lpt3" }.Contains(value.Split('.')[0]))
            throw new InvalidDataException("Mod ID须为1至64位小写字母、数字、点、横线或下划线，且不能使用系统保留名称");
        return value;
    }
}

internal sealed class ImportSource : IDisposable
{
    private readonly ZipArchive? zip;
    private readonly string? directory;
    private readonly Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
    internal IReadOnlyCollection<string> Names { get; }
    internal ImportSource(string path)
    {
        if (Directory.Exists(path))
        {
            directory = Path.GetFullPath(path);
            var names = new List<string>(); long total = 0;
            if ((new DirectoryInfo(directory).Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("导入根目录不能是符号链接");
            var directories = new Stack<DirectoryInfo>(); directories.Push(new DirectoryInfo(directory));
            while (directories.TryPop(out var current)) foreach (var file in current.EnumerateFileSystemInfos())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("导入目录不能包含符号链接");
                if (file is DirectoryInfo child) { directories.Push(child); continue; }
                if (file is not FileInfo info) continue;
                string name = ModFiles.PathName(Path.GetRelativePath(directory, info.FullName).Replace('\\', '/'));
                total = checked(total + info.Length); CheckBudget(info.Length, total, names.Count + 1); names.Add(name);
            }
            Names = names;
        }
        else
        {
            zip = ZipFile.OpenRead(path); long total = 0;
            try
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    string name = ModFiles.PathName(entry.FullName);
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("压缩包不能包含符号链接");
                    total = checked(total + entry.Length); CheckBudget(entry.Length, total, entries.Count + 1);
                    if (!entries.TryAdd(name, entry)) throw new InvalidDataException("压缩包有重复路径: " + name);
                }
                Names = entries.Keys;
            }
            catch { zip.Dispose(); throw; }
        }
        if (Names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Names.Count)
        { zip?.Dispose(); throw new InvalidDataException("资源路径存在大小写冲突"); }
    }
    private static void CheckBudget(long size, long total, int count)
    {
        if (size > ModFiles.MaximumFile || total > ModFiles.MaximumTotal || count > 25000)
            throw new InvalidDataException("资源包超过导入预算（单文件256MiB、总计1GiB、25000文件）");
    }
    internal bool Contains(string name) => Names.Contains(name);
    internal byte[] Read(string name)
    {
        ModFiles.PathName(name);
        if (directory != null) return File.ReadAllBytes(ModFiles.Under(directory, name));
        using var stream = entries[name].Open(); using var output = new MemoryStream();
        byte[] buffer = new byte[65536]; int count;
        while ((count = stream.Read(buffer)) > 0) { if (output.Length + count > entries[name].Length || output.Length + count > ModFiles.MaximumFile) throw new InvalidDataException("资源解压超出声明长度"); output.Write(buffer, 0, count); }
        if (output.Length != entries[name].Length || output.Length > ModFiles.MaximumFile) throw new InvalidDataException("资源解压长度不符");
        return output.ToArray();
    }
    public void Dispose() => zip?.Dispose();
}
