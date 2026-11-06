using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Mods;

internal static class SaveImportChecks
{
    internal static int Run(string root)
    {
        string run = Path.Combine(root, "build/save-import-checks", Guid.NewGuid().ToString("N"));
        string saves = Path.Combine(run, "saves");
        var baseline = new ModBase(_ => [], [], "builtin/check");
        var store = new ModStore(baseline, Path.Combine(saves, "content-mods"), saves);
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        string PathFor(string name) => Path.Combine(saves, name);
        byte[] Bundle(Dictionary<string, byte[]> files, string? badHash = null, string? extra = null)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var (name, bytes) in files) { using var entry = zip.CreateEntry("saves/" + name).Open(); entry.Write(bytes); }
                if (extra != null) { using var entry = zip.CreateEntry(extra).Open(); entry.WriteByte(0); }
                using var info = zip.CreateEntry("export-info.json").Open();
                JsonSerializer.Serialize(info, new { format = "ew4-save-export", version = 1, files = files.Select(p => new {
                    path = "saves/" + p.Key, bytes = p.Value.Length, sha256 = badHash ?? Convert.ToHexStringLower(SHA256.HashData(p.Value)) }) });
            }
            return output.ToArray();
        }
        void Reject(byte[] bytes, string label)
        {
            try { store.ImportSaves(new MemoryStream(bytes), true); }
            catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException) { Check(true, label); return; }
            throw new Exception("Expected rejection: " + label);
        }
        JsonNode Manager(string op, int slot = 1)
        {
            var result = JsonNode.Parse(store.Dispatch("/api/save-manager", JsonSerializer.Serialize(new { op, slot })))!;
            Check((bool?)result["ok"] == true, "manager " + op + ": " + result);
            return result["data"]!;
        }
        try
        {
            var original = new Dictionary<string, byte[]> {
                ["builtin/check/account.json"] = Encoding.UTF8.GetBytes("{\"medals\":9592,\"roster\":[]}"),
                ["builtin/check/saves/mode-2/slot-1.json"] = [0,255,1,2],
                ["mods/sample/logic/saves/mode-2/slot-1.json.bak"] = [3,4,5] };
            var result = store.ImportSaves(new MemoryStream(Bundle(original)));
            Check(result.Imported && result.Files == 3, "new archive imports without requiring readable save payloads");
            foreach (var (name, bytes) in original) Check(File.ReadAllBytes(PathFor(name)).SequenceEqual(bytes), "raw " + name);
            var changed = new Dictionary<string, byte[]> { ["builtin/check/account.json"] = Encoding.UTF8.GetBytes("{\"medals\":2}") };
            result = store.ImportSaves(new MemoryStream(Bundle(changed)));
            Check(!result.Imported && result.Conflicts == 1, "conflicts require explicit overwrite");
            Check(File.ReadAllBytes(PathFor("builtin/check/account.json")).SequenceEqual(original["builtin/check/account.json"]), "preview does not write");
            Manager("save");
            Check(Manager("list")["slots"]!.AsArray().Count == 8, "eight retention slots");
            result = store.ImportSaves(new MemoryStream(Bundle(changed)), true);
            Check(result.Imported, "confirmed overwrite");
            Check(Directory.EnumerateFiles(PathFor("recovery"), "account.json", SearchOption.AllDirectories)
                .Any(p => File.ReadAllBytes(p).SequenceEqual(original["builtin/check/account.json"])), "overwrite retains original bytes");
            var extra = new Dictionary<string, byte[]> { ["builtin/check/saves/mode-2/slot-2.json"] = [42] };
            store.ImportSaves(new MemoryStream(Bundle(extra)));
            Manager("restore");
            Check(File.ReadAllBytes(PathFor("builtin/check/account.json")).SequenceEqual(original["builtin/check/account.json"]), "retention restores account");
            Check(!File.Exists(PathFor(extra.Keys.Single())), "restore removes slots absent from snapshot");
            Check(File.ReadAllBytes(PathFor("mods/sample/logic/saves/mode-2/slot-1.json.bak")).SequenceEqual(new byte[] {3,4,5}), "other Mod isolated");
            Manager("clear");
            Check(!File.Exists(PathFor("builtin/check/account.json")) && File.Exists(PathFor("builtin/check/retention-1.json")), "clear keeps retention");
            Manager("restore"); Manager("delete");
            Check(!File.Exists(PathFor("builtin/check/retention-1.json")), "delete retention");
            Reject(Bundle(changed, "deadbeef"), "hash mismatch");
            Reject(Bundle(changed, extra: "rogue.txt"), "unlisted file");
            Reject(Bundle(new() { ["../outside.json"] = [1] }), "path traversal");
            Reject(Bundle(new() { ["content-mods/account.json"] = [1] }), "installation write");
            Reject(Bundle(new() { ["startup-assets/account.json"] = [1] }), "cache write");
            Reject(Bundle(new() { ["builtin/check/slot-7.json"] = [1] }), "invalid slot");
            Reject(Bundle(new() { ["builtin/check/account.json"] = [1], ["builtin/check/ACCOUNT.json"] = [2] }), "case duplicate");
            Check(File.ReadAllBytes(PathFor("builtin/check/account.json")).SequenceEqual(original["builtin/check/account.json"]), "rejected archives leave live bytes intact");
            using var exported = new MemoryStream(); store.ExportSaves(exported, "checks"); exported.Position = 0;
            using var zip = new ZipArchive(exported);
            Check(zip.Entries.Any(e => e.FullName.StartsWith("saves/recovery/")), "recovery files exportable");
            string data = Path.Combine(root, "rewrite/data");
            byte[] Read(string name) => File.ReadAllBytes(name.StartsWith("frontend/")
                ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", name[9..]) : Path.Combine(data, name));
            var realBase = new ModBase(Read, Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
            var real = new ModStore(realBase, Path.Combine(run, "real-mods"), Path.Combine(run, "real-saves"));
            var installed = new List<ModInfo>();
            foreach (string id in new[] { "alpha", "beta" })
            {
                string author = Path.Combine(run, id); Directory.CreateDirectory(author);
                real.Seal(author, id, "模组" + id, "1.0", ["conquest1"]);
                installed.Add(real.Import(author, id, id));
            }
            JsonNode Call(string route, object payload)
            {
                var result = JsonNode.Parse(real.Dispatch("/api/" + route, JsonSerializer.Serialize(payload)))!;
                Check((bool?)result["ok"] == true, "real " + route + ": " + (string?)result["message"]);
                return result["data"]!;
            }
            real.Activate(installed[0].Key);
            Call("start", new { scenario = "conquest1", country = "fra" });
            Call("save", new { slot = 1, mode = 2 });
            Check((string?)Call("saves", new { mode = 2 })[1]!["modName"] == "模组alpha", "battle slot displays Mod name");
            real.Activate(installed[1].Key);
            Check((bool?)Call("saves", new { mode = 2 })[1]!["exists"] == false, "other installed Mod does not see alpha slot");
            Call("start", new { scenario = "conquest1", country = "fra" }); Call("save", new { slot = 1, mode = 2 });
            Check((string?)Call("saves", new { mode = 2 })[1]!["modName"] == "模组beta", "beta owns independent slot");
            real.Activate(installed[0].Key);
            Call("load", new { slot = 1, mode = 2 });
            string alphaSlot = Path.Combine(run, "real-saves/mods/alpha", installed[0].LogicRevision, "saves/mode-2/slot-1.json");
            var foreign = JsonNode.Parse(File.ReadAllBytes(alphaSlot))!; foreign["saveIdentity"] = "mods/beta/" + installed[1].LogicRevision;
            File.WriteAllText(alphaSlot, foreign.ToJsonString());
            var rejected = JsonNode.Parse(real.Dispatch("/api/load", "{\"slot\":1,\"mode\":2}"))!;
            Check((string?)rejected["code"] == "save.mod", "foreign identity rejected before session decode");
            return checks;
        }
        finally { if (Directory.Exists(run)) Directory.Delete(run, true); }
    }
}
