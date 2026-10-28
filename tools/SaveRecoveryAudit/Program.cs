using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Client;
using Ew4.Legacy;
using Ew4.Core;
using Ew4.Persistence;

if (args.Length is not (3 or 4)) throw new ArgumentException("Usage: SaveRecoveryAudit <root> <export.zip> <isolated-output> [recovered-export.zip]");
string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a new isolated output directory");
Directory.CreateDirectory(output);
byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
var bundle = LegacyCatalogFiles.Load(Read);
var lineEndings = new List<object>();
foreach (string newline in new[] { "\n", "\r\n" })
{
    string Xml(string name) => bundle.Xml.TryGetValue(name, out var xml)
        ? xml.Replace("\r\n", "\n").Replace("\n", newline) : System.Text.Encoding.UTF8.GetString(Read("original/" + name)).TrimStart('\uFEFF');
    var buildings = LegacyBuildingCatalog.Read(Xml("def_construction.xml"), Xml("def_capital.xml"));
    var combat = LegacyCombatCatalog.Read(bundle.RuntimeRules.ToJsonString(), Xml("def_terraintype.xml"), Xml("def_installation.xml"), buildings, content.Extensions.LegacySkills);
    var accounts = LegacyCommanderAccounts.Read(content.Catalogs.Runtime, combat, bundle.MetagameRules.ToJsonString());
    lineEndings.Add(new { newline, account = accounts.SourceRevision, combat = combat.SourceRevision });
}
using var archive = ZipFile.OpenRead(args[1]);
byte[] Bytes(ZipArchiveEntry entry) { using var stream = entry.Open(); using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray(); }
using var info = JsonDocument.Parse(Bytes(archive.GetEntry("export-info.json")!));
int checks = 0;
foreach (var file in info.RootElement.GetProperty("files").EnumerateArray())
{
    byte[] bytes = Bytes(archive.GetEntry(file.GetProperty("path").GetString()!)!);
    if (bytes.Length != file.GetProperty("bytes").GetInt32()
        || Convert.ToHexStringLower(SHA256.HashData(bytes)) != file.GetProperty("sha256").GetString()) throw new IOException("Export checksum mismatch");
    checks++;
}
var results = new List<object>();
foreach (int mode in new[] { 1, 2 })
{
    byte[] bytes = Bytes(archive.GetEntry($"saves/saves/mode-{mode}/slot-0.json")!);
    var slot = JsonSerializer.Deserialize<ClientSlot>(bytes, ClientGame.Json)!;
    var start = content.Start(slot.Scenario, slot.Country, localPlayers: slot.LocalPlayers, technologySnapshot: slot.TechnologySnapshot);
    var legacyContent = typeof(ClientContent).GetMethod("WithLegacyCatalogFormatting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(content, null) as ClientContent;
    var legacyStart = legacyContent?.Start(slot.Scenario, slot.Country, localPlayers: slot.LocalPlayers, technologySnapshot: slot.TechnologySnapshot);
    var legacyUncorrected = typeof(ClientContent).GetMethod("WithoutEquipmentCorrections", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(legacyContent ?? content, null) as ClientContent;
    var legacyUncorrectedStart = legacyUncorrected?.Start(slot.Scenario, slot.Country, localPlayers: slot.LocalPlayers, technologySnapshot: slot.TechnologySnapshot);
    using var session = JsonDocument.Parse(slot.Session);
    var payload = session.RootElement.GetProperty("payload");
    if (mode == 1)
    {
        string browserProfile = Path.Combine(output, "browser-profile");
        string saves = Path.Combine(browserProfile, "saves/mode-1");
        Directory.CreateDirectory(saves);
        File.WriteAllBytes(Path.Combine(browserProfile, "account.json"), Bytes(archive.GetEntry("saves/account.json")!));
        File.WriteAllBytes(Path.Combine(saves, "slot-0.json"), bytes);
        var original = SessionSave.Decode(slot.Session, new(payload.GetProperty("contentRevision").GetString()!,
            payload.GetProperty("world").GetProperty("payload").GetProperty("contentRevision").GetString()!,
            payload.GetProperty("account").GetProperty("contentRevision").GetString()!));
        var defeat = original with { BattleId = "save-recovery-defeat", BattleResult = null, World = original.World with { Outcome = BattleOutcome.Defeat } };
        File.WriteAllBytes(Path.Combine(saves, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(slot with { Session = SessionSave.Encode(defeat) }, ClientGame.Json));
    }
    string profile = Path.Combine(output, "profile-" + mode);
    Directory.CreateDirectory(Path.Combine(profile, "saves", "mode-" + mode));
    File.WriteAllBytes(Path.Combine(profile, "account.json"), Bytes(archive.GetEntry("saves/account.json")!));
    File.WriteAllBytes(Path.Combine(profile, "saves", "mode-" + mode, "slot-0.json"), bytes);
    var host = new ClientGame(content, profile);
    JsonElement Call(string route, object request)
    {
        using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(request, ClientGame.Json)));
        return doc.RootElement.Clone();
    }
    var loaded = Call("load", new { mode });
    var actions = new List<JsonElement>();
    var state = loaded;
    for (int i = 0; state.GetProperty("ok").GetBoolean() && i < 10; i++)
    {
        var data = state.GetProperty("data");
        if (data.GetProperty("phase").GetString() != "Finished" || data.GetProperty("dialogue").ValueKind == JsonValueKind.Null) break;
        state = Call("action", new { op = "dialogue", revision = data.GetProperty("revision").GetInt64() });
        actions.Add(state);
    }
    JsonElement Summary(JsonElement response)
    {
        if (!response.GetProperty("ok").GetBoolean()) return response;
        var data = response.GetProperty("data");
        return JsonSerializer.SerializeToElement(new { ok = true, revision = data.GetProperty("revision"), phase = data.GetProperty("phase"),
            outcome = data.GetProperty("outcome"), dialogue = data.GetProperty("dialogue"), result = data.GetProperty("battleResult"), medals = data.GetProperty("medals") });
    }
    var final = Summary(state);
    host = new ClientGame(content, profile);
    results.Add(new { mode, slot.Scenario, slot.Round, slot.TechnologySnapshot,
        stored = new { session = payload.GetProperty("contentRevision").GetString(), world = payload.GetProperty("world").GetProperty("payload").GetProperty("contentRevision").GetString(), account = payload.GetProperty("account").GetProperty("contentRevision").GetString() },
        expected = new { session = start.Session.ContentRevision, world = start.Session.World.ContentRevision, account = start.Session.Account.ContentRevision },
        legacyExpected = legacyStart == null ? null : new { session = legacyStart.Session.ContentRevision, world = legacyStart.Session.World.ContentRevision, account = legacyStart.Session.Account.ContentRevision },
        legacyUncorrectedExpected = legacyUncorrectedStart == null ? null : new { session = legacyUncorrectedStart.Session.ContentRevision, world = legacyUncorrectedStart.Session.World.ContentRevision, account = legacyUncorrectedStart.Session.Account.ContentRevision },
        loaded = Summary(loaded), actions = actions.Select(Summary).ToArray(), final, restored = Summary(Call("load", new { mode })) });
}
string report = JsonSerializer.Serialize(new { checks, lineEndings, results }, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(output, "checks.json"), report);
if (args.Length == 4)
{
    GameSession Decode(ClientSlot slot)
    {
        using var doc = JsonDocument.Parse(slot.Session);
        var p = doc.RootElement.GetProperty("payload");
        return SessionSave.Decode(slot.Session, new(p.GetProperty("contentRevision").GetString()!,
            p.GetProperty("world").GetProperty("payload").GetProperty("contentRevision").GetString()!, p.GetProperty("account").GetProperty("contentRevision").GetString()!));
    }
    var originalSlot = JsonSerializer.Deserialize<ClientSlot>(Bytes(archive.GetEntry("saves/saves/mode-1/slot-0.json")!), ClientGame.Json)!;
    var original = Decode(originalSlot);
    var finishedSlot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(output, "profile-1/saves/mode-1/slot-0.json")), ClientGame.Json)!;
    var finished = Decode(finishedSlot);
    if (finished.World.PendingDialogue != null || finished.BattleResult?.Outcome != "Victory" || finished.World.Round != original.World.Round)
        throw new InvalidOperationException("Recovery did not complete the original victory");
    var recovered = finished with { ContentRevision = original.ContentRevision,
        Account = finished.Account with { ContentRevision = original.Account.ContentRevision },
        World = finished.World with { ContentRevision = original.World.ContentRevision } };
    var recoveredSlot = originalSlot with { Session = SessionSave.Encode(recovered), SavedAt = DateTimeOffset.UtcNow };
    var verified = Decode(recoveredSlot);
    if (verified.BattleResult?.Id != original.BattleId || verified.Account.Medals != original.Account.Medals + verified.BattleResult.AwardMedals)
        throw new InvalidOperationException("Recovery identity or award mismatch");
    byte[] saved = JsonSerializer.SerializeToUtf8Bytes(recoveredSlot, ClientGame.Json);
    byte[] account = JsonSerializer.SerializeToUtf8Bytes(recovered.Account, ClientGame.Json);
    var files = archive.Entries.Where(e => e.FullName.StartsWith("saves/")).ToDictionary(e => e.FullName, Bytes);
    files["saves/saves/mode-1/slot-0.json"] = saved; files["saves/saves/mode-1/slot-0.json.bak"] = saved;
    files["saves/account.json"] = account; files["saves/account.json.bak"] = account;
    var metadata = System.Text.Json.Nodes.JsonNode.Parse(info.RootElement.GetRawText())!;
    metadata["createdAt"] = DateTimeOffset.UtcNow;
    foreach (var entry in metadata["files"]!.AsArray())
    {
        byte[] data = files[entry!["path"]!.GetValue<string>()];
        entry["bytes"] = data.Length; entry["sha256"] = Convert.ToHexStringLower(SHA256.HashData(data));
    }
    string destination = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    using var zip = ZipFile.Open(destination, ZipArchiveMode.Create);
    foreach (var (name, data) in files)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); stream.Write(data);
    }
    using var manifestStream = zip.CreateEntry("export-info.json", CompressionLevel.Optimal).Open();
    manifestStream.Write(System.Text.Encoding.UTF8.GetBytes(metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
}
Console.WriteLine(JsonSerializer.Serialize(new { checks, report = Path.Combine(output, "checks.json") }));
