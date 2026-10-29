using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Ew4.Mods;

internal static class SaveExportChecks
{
    internal static int Run(string root)
    {
        string run = Path.Combine(root, "build/save-export-checks", Guid.NewGuid().ToString("N"));
        string saves = Path.Combine(run, "saves");
        var store = new ModStore(new ModBase(p => p == "frontend/build-info.json" ? Encoding.UTF8.GetBytes("{\"source\":\"frontend-test-sha\"}") : [], []), Path.Combine(saves, "content-mods"), saves);
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        byte[] Archive()
        {
            using var output = new MemoryStream(); store.ExportSaves(output, "0.2.23-save-export-test", "web");
            Check(output.CanWrite, "export leaves caller stream open"); return output.ToArray();
        }
        try
        {
            using (var empty = new ZipArchive(new MemoryStream(Archive()))) Check(empty.Entries.Count == 1, "empty save directory still exports metadata");
            var fixtures = new Dictionary<string, byte[]> {
                ["account.json"] = Encoding.UTF8.GetBytes("{\"medals\":17}"),
                ["account.json.bak"] = Encoding.UTF8.GetBytes("{\"medals\":16}"),
                ["session.json"] = Encoding.UTF8.GetBytes("legacy session"),
                ["session.json.bak"] = Encoding.UTF8.GetBytes("legacy backup"),
                ["slot-6.json"] = Encoding.UTF8.GetBytes("legacy manual slot"),
                ["saves/mode-2/slot-1.json"] = Encoding.UTF8.GetBytes("malformed save must remain exportable"),
                ["saves/mode-2/slot-1.json.bak"] = [0, 1, 255, 13, 10],
                ["builtin/revision/saves/mode-2/slot-0.json"] = Encoding.UTF8.GetBytes("{\"scenario\":\"conquest1\"}"),
                ["mods/sample/logic/account.json"] = Encoding.UTF8.GetBytes("{}") };
            foreach (var (path, bytes) in fixtures) { string file = Path.Combine(saves, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, bytes); }
            File.WriteAllText(Path.Combine(saves, "unfinished.json.tmp"), "incomplete");
            File.WriteAllText(Path.Combine(run, "outside.json"), "private");
            foreach (string name in new[] { "startup-assets/account.json", "startup-assets/cache.json", "content-mods/active.json", "content-mods/hash/account.json", "notes.json", "slot-7.json" })
            {
                string file = Path.Combine(saves, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "excluded");
            }
            using var archive = new ZipArchive(new MemoryStream(Archive()));
            Check(archive.Entries.Count == fixtures.Count + 1, "only saves and backups included");
            using var info = new StreamReader(archive.GetEntry("export-info.json")!.Open());
            var manifest = JsonNode.Parse(info.ReadToEnd())!;
            Check((string?)manifest["format"] == "ew4-save-export" && (string?)manifest["applicationVersion"] == "0.2.23-save-export-test", "archive identity");
            Check(manifest["files"]!.AsArray().Count == fixtures.Count, "complete manifest");
            Check((string?)manifest["platform"] == "web" && (string?)manifest["frontendSource"] == "frontend-test-sha", "feedback includes platform and exact frontend source");
            foreach (var (path, bytes) in fixtures)
            {
                string name = "saves/" + path;
                using var entry = archive.GetEntry(name)!.Open(); using var copy = new MemoryStream(); entry.CopyTo(copy);
                Check(copy.ToArray().SequenceEqual(bytes), "raw bytes preserved: " + path);
                Check(File.ReadAllBytes(Path.Combine(saves, path)).SequenceEqual(bytes), "original unchanged: " + path);
                var record = manifest["files"]!.AsArray().Single(n => (string?)n!["path"] == name)!;
                Check((string?)record["sha256"] == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() && (long?)record["bytes"] == bytes.Length, "file hash and size: " + path);
            }
            Check(File.ReadAllText(Path.Combine(saves, "unfinished.json.tmp")) == "incomplete", "temporary file unchanged");
            return checks;
        }
        finally { if (Directory.Exists(run)) Directory.Delete(run, true); }
    }
}
