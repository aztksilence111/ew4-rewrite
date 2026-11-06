using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ew4.Mods;

public sealed record SaveImportResult(int Files, long Bytes, int Conflicts, bool Imported);

public sealed partial class ModStore
{
    private const long SaveFileLimit = 64L * 1024 * 1024, SaveTotalLimit = 256L * 1024 * 1024;
    private static bool IsSaveFile(string path)
    {
        try { ModFiles.PathName(path); }
        catch (InvalidDataException) { return false; }
        return Regex.IsMatch(path, @"^(?:recovery/[a-f0-9]{32}/)?(?:(?:mods/[a-z0-9_.-]+/[a-zA-Z0-9_.-]+|builtin/[a-zA-Z0-9_.-]+)/)?(?:(?:saves/mode-[0124]/)?(?:account\.json|session\.json|slot-[0-6]\.json)(?:\.bak)?|retention-[1-8]\.json)$");
    }

    private string CheckedSavePath(string relative)
    {
        string path = ModFiles.Under(saves, relative);
        if (Directory.Exists(path)) throw new InvalidDataException("存档目标是目录");
        for (string? part = path; part != null && part.Length >= saves.Length; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("存档路径不能经过链接目录");
        if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("存档不能写入Mod安装目录");
        return path;
    }

    public SaveImportResult ImportSaves(Stream input, bool overwrite = false)
    {
        lock (gate)
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > 4097) throw new InvalidDataException("存档包文件过多");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
                if (!entries.TryAdd(entry.FullName, entry)) throw new InvalidDataException("存档包有重复路径");
            if (!entries.TryGetValue("export-info.json", out var info) || info.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("请选择由存档导出功能生成的ZIP");
            using var metadata = JsonDocument.Parse(ReadBounded(info, 4 * 1024 * 1024));
            var manifest = metadata.RootElement;
            if (manifest.GetProperty("format").GetString() != "ew4-save-export" || manifest.GetProperty("version").GetInt32() != 1)
                throw new InvalidDataException("不支持此存档包格式");
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var record in manifest.GetProperty("files").EnumerateArray())
            {
                string name = record.GetProperty("path").GetString() ?? "";
                if (!name.StartsWith("saves/", StringComparison.Ordinal) || !IsSaveFile(name[6..]) || !entries.TryGetValue(name, out var entry))
                    throw new InvalidDataException("存档包包含无效路径或缺少文件");
                CheckedSavePath(name[6..]);
                long size = record.GetProperty("bytes").GetInt64();
                if (size < 0 || size > SaveFileLimit || entry.Length != size || (total += size) > SaveTotalLimit)
                    throw new InvalidDataException("存档包超出大小限制");
                byte[] bytes = ReadBounded(entry, size);
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), record.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("存档文件校验失败：" + name);
                if (!files.TryAdd(name[6..], bytes)) throw new InvalidDataException("存档清单包含重复路径");
            }
            if (entries.Count != files.Count + 1 || files.Count == 0) throw new InvalidDataException("存档包有未登记文件或没有存档");
            int conflicts = files.Count(f => File.Exists(CheckedSavePath(f.Key)));
            if (conflicts > 0 && !overwrite) return new(files.Count, total, conflicts, false);
            ApplySaveFiles(files);
            game = null;
            return new(files.Count, total, conflicts, true);
        }
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, long maximum)
    {
        if (entry.Length > maximum) throw new InvalidDataException("存档文件过大");
        using var input = entry.Open(); using var output = new MemoryStream();
        byte[] buffer = new byte[65536]; long total = 0; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if ((total += count) > maximum) throw new InvalidDataException("存档解压数据超出限制");
            output.Write(buffer, 0, count);
        }
        if (total != entry.Length) throw new InvalidDataException("存档长度不符");
        return output.ToArray();
    }

    private static void WriteSaveFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // All input is checked before changing live files; retain overwritten bytes and roll back on I/O failure.
    private void ApplySaveFiles(Dictionary<string, byte[]> files, IEnumerable<string>? remove = null)
    {
        var names = files.Keys.Concat(remove ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var originals = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (string name in names)
        {
            string path = CheckedSavePath(name);
            if (File.Exists(path) && (new FileInfo(path).Length > SaveFileLimit || (total += new FileInfo(path).Length) > SaveTotalLimit))
                throw new InvalidDataException("原存档过大，无法建立恢复副本");
            byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (original != null && files.TryGetValue(name, out var incoming) && original.SequenceEqual(incoming)) continue;
            originals[name] = original;
        }
        foreach (var (name, bytes) in originals)
            if (bytes != null) WriteSaveFile(CheckedSavePath("recovery/" + Guid.NewGuid().ToString("N") + "/"
                + Regex.Replace(name, @"^(?:recovery/[a-f0-9]{32}/)+", "")), bytes);
        try
        {
            foreach (var (name, bytes) in files.Where(p => originals.ContainsKey(p.Key))) WriteSaveFile(CheckedSavePath(name), bytes);
            foreach (string name in originals.Keys.Except(files.Keys, StringComparer.OrdinalIgnoreCase)) File.Delete(CheckedSavePath(name));
        }
        catch
        {
            foreach (var (name, bytes) in originals)
                if (bytes == null) File.Delete(CheckedSavePath(name)); else WriteSaveFile(CheckedSavePath(name), bytes);
            throw;
        }
    }
}
