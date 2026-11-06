using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ew4.Mods;

public sealed partial class ModStore
{
    private sealed record RetainedSave(string Identity, string ModName, DateTimeOffset SavedAt, long Medals,
        int Battles, int Commanders, Dictionary<string, byte[]> Files);

    private Dictionary<string, byte[]> CurrentSaveFiles()
    {
        string directory = SaveDirectory(activeManifest);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory)) return files;
        long total = 0;
        var folders = new[] { directory }.Concat(new[] { 0, 1, 2, 4 }.Select(mode => Path.Combine(directory, "saves", "mode-" + mode)));
        foreach (string folder in folders.Where(Directory.Exists))
        foreach (string path in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (!IsProfileFile(relative)) continue;
            if (new FileInfo(path).Length > SaveFileLimit || (total += new FileInfo(path).Length) > SaveTotalLimit / 2)
                throw new InvalidDataException("存档超出备份大小限制");
            files[relative] = File.ReadAllBytes(CheckedSavePath(Path.GetRelativePath(saves, path).Replace('\\', '/')));
        }
        return files;
    }
    private static bool IsProfileFile(string name) => IsSaveFile(name) && !name.StartsWith("mods/") && !name.StartsWith("builtin/")
        && !name.StartsWith("recovery/") && !name.StartsWith("retention-");
    private string RetentionPath(int slot) => Path.Combine(SaveDirectory(activeManifest), $"retention-{slot}.json");
    private RetainedSave ReadRetention(int slot)
    {
        string path = RetentionPath(slot);
        if (!File.Exists(path) || new FileInfo(path).Length > SaveFileLimit) throw new InvalidDataException("备份为空或过大");
        var saved = JsonSerializer.Deserialize<RetainedSave>(File.ReadAllBytes(path), ModFiles.Json) ?? throw new InvalidDataException("备份损坏");
        if (saved.Identity != SaveIdentity(activeManifest) || saved.Files == null || saved.Files.Count == 0
            || saved.Files.Any(p => !IsProfileFile(p.Key) || p.Value == null) || saved.Files.Values.Sum(b => (long)b.Length) > SaveTotalLimit / 2)
            throw new InvalidDataException("备份身份不符或数据损坏");
        return saved;
    }
    private object ManageSaves(string body)
    {
        var request = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body)!;
        string op = (string?)request["op"] ?? "list";
        int slot = (int?)request["slot"] ?? 0;
        if (op != "list" && op != "clear" && slot is < 1 or > 8) throw new InvalidDataException("无效备份槽位");
        if (op == "save")
        {
            var files = CurrentSaveFiles();
            if (files.Count == 0) throw new InvalidDataException("当前Mod没有可备份的存档");
            long medals = 0; int battles = 0, commanders = 0;
            try
            {
                if (files.TryGetValue("account.json", out var bytes))
                {
                    using var account = JsonDocument.Parse(bytes); var value = account.RootElement;
                    medals = value.TryGetProperty("medals", out var m) ? m.GetInt64() : 0;
                    commanders = value.TryGetProperty("roster", out var r) ? r.GetArrayLength() : 0;
                    battles = value.TryGetProperty("battleResults", out var b) && b.ValueKind == JsonValueKind.Object ? b.EnumerateObject().Count() : 0;
                }
            }
            catch (Exception e) when (Recoverable(e)) { }
            byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(new RetainedSave(SaveIdentity(activeManifest), activeManifest?.Name ?? "原版",
                DateTimeOffset.Now, medals, battles, commanders, files), ModFiles.Json);
            if (snapshot.Length > SaveFileLimit) throw new InvalidDataException("备份超过64MiB");
            string name = Path.GetRelativePath(saves, RetentionPath(slot)).Replace('\\', '/');
            ApplySaveFiles(new(StringComparer.OrdinalIgnoreCase) { [name] = snapshot });
        }
        else if (op is "restore" or "clear")
        {
            var current = CurrentSaveFiles();
            var restored = op == "restore" ? ReadRetention(slot).Files : new Dictionary<string, byte[]>();
            string prefix = SaveDirectory(activeManifest) == saves ? "" : SaveIdentity(activeManifest) + "/";
            ApplySaveFiles(restored.ToDictionary(p => prefix + p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
                current.Keys.Select(p => prefix + p));
            game = null;
        }
        else if (op == "delete")
        {
            string name = Path.GetRelativePath(saves, RetentionPath(slot)).Replace('\\', '/');
            ApplySaveFiles(new(StringComparer.OrdinalIgnoreCase), [name]);
        }
        else if (op != "list") throw new InvalidDataException("未知存档操作");
        return new { modName = activeManifest?.Name ?? "原版", identity = SaveIdentity(activeManifest), slots = Enumerable.Range(1, 8).Select(index =>
        {
            RetainedSave? saved = null; bool corrupt = false;
            try { if (File.Exists(RetentionPath(index))) saved = ReadRetention(index); }
            catch (Exception e) when (Recoverable(e)) { corrupt = true; }
            return new { slot = index, exists = saved != null || corrupt, corrupt, modName = saved?.ModName ?? activeManifest?.Name ?? "原版",
                savedAt = saved?.SavedAt, medals = saved?.Medals, battles = saved?.Battles, commanders = saved?.Commanders };
        }).ToArray() };
    }
}
