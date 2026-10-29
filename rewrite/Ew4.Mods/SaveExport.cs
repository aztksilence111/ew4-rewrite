using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ew4.Mods;

public sealed record SaveExportResult(int Files, long Bytes);

public sealed partial class ModStore
{
    public SaveExportResult ExportSaves(Stream output, string applicationVersion, string platform = "unknown")
    {
        // Use the same lock as game actions so autosaves cannot change halfway through the archive.
        lock (gate)
        {
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var records = new List<object>();
            long total = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
            IEnumerable<string> files = Directory.Exists(saves) ? Directory.EnumerateFiles(saves, "*", options).Order(StringComparer.Ordinal) : [];
            byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                foreach (string file in files)
                {
                    string leaf = Path.GetFileName(file);
                    if (leaf.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) leaf = leaf[..^4];
                    if (leaf is not ("account.json" or "session.json") && !(leaf.Length == 11 && leaf.StartsWith("slot-", StringComparison.Ordinal)
                        && leaf[5] is >= '0' and <= '6' && leaf.EndsWith(".json", StringComparison.Ordinal))) continue;
                    if (file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || file.StartsWith(Path.Combine(saves, "startup-assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    string name = "saves/" + Path.GetRelativePath(saves, file).Replace('\\', '/');
                    using var input = File.OpenRead(file);
                    using var target = archive.CreateEntry(name, CompressionLevel.Fastest).Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long bytes = 0; int count;
                    while ((count = input.Read(buffer)) > 0) { target.Write(buffer, 0, count); hash.AppendData(buffer, 0, count); bytes += count; }
                    records.Add(new { path = name, bytes, sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() });
                    total += bytes;
                }
            }
            finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer); }
            using (var info = archive.CreateEntry("export-info.json").Open())
                JsonSerializer.Serialize(info, new { format = "ew4-save-export", version = 1, applicationVersion, createdAt = DateTimeOffset.UtcNow,
                    platform, frontendSource = ExportFrontendSource(), baseRevision = baseline.Revision, saveNamespace = baseline.SaveNamespace,
                    activeMod = activeManifest == null ? null : new { activeManifest.Id, activeManifest.Version, activeManifest.LogicRevision }, files = records }, ModFiles.Json);
            return new(records.Count, total);
        }
    }

    private string? ExportFrontendSource()
    {
        try
        {
            using var info = JsonDocument.Parse(baseline.Read("frontend/build-info.json"));
            return info.RootElement.TryGetProperty("source", out var source) ? source.GetString() : null;
        }
        catch (Exception error) when (Recoverable(error)) { return null; }
    }
}
