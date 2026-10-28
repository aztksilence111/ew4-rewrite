using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class SaveCompatibilityChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string message) { checks++; if (!pass) throw new InvalidOperationException("Save compatibility: " + message); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var start = content.Start("campaign1_06", null);
        var owned = content.Catalogs.Accounts.FromDefinition(content.Catalogs.Runtime.Commanders.Keys.First(), "compatibility.owned") with { Items = [] };
        owned = owned with { Learning = owned.Learning with { Abilities = owned.Learning.Abilities.ToDictionary(p => p.Key, p => p.Key == "commerce" ? 13 : p.Key == "movement" ? 9 : p.Value) } };
        var basis = start.Session with { ContentRevision = "older-session-binding", BattleId = "compatible-save",
            Account = start.Session.Account with { ContentRevision = "older-account-binding", Revision = 100, Roster = [owned] },
            World = start.Session.World with { ContentRevision = "older-world-binding", Phase = TurnPhase.Active, PendingDialogue = null, QueuedDialogues = [] } };
        var binding = new SessionContentBinding(start.Session.ContentRevision, start.Session.World.ContentRevision, start.Session.Account.ContentRevision);
        string folder = Path.Combine(root, "build/save-compatibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            (ClientGame Host, string Profile) Prepare(string name, GameSession session, byte[]? bytes = null)
            {
                string profile = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.Combine(profile, "saves/mode-1"));
                File.WriteAllBytes(Path.Combine(profile, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
                File.WriteAllBytes(Path.Combine(profile, "saves/mode-1/slot-0.json"), JsonSerializer.SerializeToUtf8Bytes(
                    new ClientSlot("campaign1_06", null, bytes ?? SessionSave.Encode(session)), ClientGame.Json));
                return (new(content, profile), profile);
            }
            JsonElement Load(ClientGame host)
            {
                using var response = JsonDocument.Parse(host.Dispatch("/api/load", "{\"mode\":1}"));
                return response.RootElement.Clone();
            }
            var reusable = Prepare("reusable", basis);
            var loaded = Load(reusable.Host);
            Check(loaded.GetProperty("ok").GetBoolean(), "different bindings alone do not block loading");
            var slot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(reusable.Profile, "saves/mode-1/slot-0.json")), ClientGame.Json)!;
            var restored = SessionSave.Decode(slot.Session, binding);
            Check(JsonSerializer.Serialize(restored.World, ClientGame.Json) == JsonSerializer.Serialize(basis.World with { ContentRevision = binding.World }, ClientGame.Json),
                "reusable world snapshot retains units, resolved bonuses, cells, resources, events and random states");
            Check(restored.Account.Medals == basis.Account.Medals && restored.Account.Roster[0].Learning.Abilities["commerce"] == 13
                && restored.Account.Roster[0].Learning.Abilities["movement"] == 9, "account balances and above-five abilities survive");
            Check(Load(new(content, reusable.Profile)).GetProperty("ok").GetBoolean(), "rebound autosave reloads");
            var mover = restored.World.Units.First(u => u.Owner == restored.PlayerFaction && WorldActions.Reachable(restored.World, u.Id, start.Rules.Movement)
                .Keys.Any(c => c != u.Cell && !restored.World.Units.Any(other => other.Cell == c)));
            int destination = WorldActions.Reachable(restored.World, mover.Id, start.Rules.Movement).Keys
                .First(c => c != mover.Cell && !restored.World.Units.Any(other => other.Cell == c));
            using var moved = JsonDocument.Parse(reusable.Host.Dispatch("/api/action", JsonSerializer.Serialize(new {
                op = "move", revision = restored.Revision, unit = mover.Id, cell = destination }, ClientGame.Json)));
            Check(moved.RootElement.GetProperty("ok").GetBoolean() && moved.RootElement.GetProperty("data").GetProperty("units").EnumerateArray()
                .Single(u => u.GetProperty("id").GetString() == mover.Id).GetProperty("cell").GetInt32() == destination,
                "reused save remains playable through the real movement API: " + (moved.RootElement.GetProperty("ok").GetBoolean() ? "unexpected destination" : moved.RootElement.ToString()));

            var missingUnit = basis with { World = basis.World with { Units = basis.World.Units.Select((u, i) => i == 0
                ? u with { Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Id = "missing-unit-definition" } } } : u).ToArray() } };
            var badUnit = Load(Prepare("missing-unit", missingUnit).Host);
            Check(!badUnit.GetProperty("ok").GetBoolean() && badUnit.GetProperty("code").GetString() == "save.missing_definition"
                && badUnit.GetProperty("message").GetString()!.Contains("missing-unit-definition"), "missing unit names the actual incompatible reference");
            var missingCommander = basis with { Account = basis.Account with { Roster = [owned with { DefinitionId = "missing-commander-definition" }] } };
            var badCommander = Load(Prepare("missing-commander", missingCommander).Host);
            Check(!badCommander.GetProperty("ok").GetBoolean() && badCommander.GetProperty("code").GetString() == "save.missing_definition", "missing owned commander is checked before migration");
            var wrongMap = basis with { World = basis.World with { Width = basis.World.Height, Height = basis.World.Width } };
            var badMap = Load(Prepare("wrong-map", wrongMap).Host);
            Check(!badMap.GetProperty("ok").GetBoolean() && badMap.GetProperty("code").GetString() == "save.map_incompatible", "unusable map dimensions have a specific error");
            var corrupt = JsonNode.Parse(SessionSave.Encode(basis))!;
            corrupt["payload"]!["account"]!["medals"] = basis.Account.Medals + 1;
            var badChecksum = Load(Prepare("checksum", basis, System.Text.Encoding.UTF8.GetBytes(corrupt.ToJsonString())).Host);
            Check(!badChecksum.GetProperty("ok").GetBoolean() && badChecksum.GetProperty("code").GetString() == "save.checksum", "reuse still authenticates checksums");

            var legacy = (ClientContent)typeof(ClientContent).GetMethod("WithLegacyCatalogFormatting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(content, null)!;
            var lf = legacy.Start("campaign1_06", null).Session;
            Check(lf.Account.ContentRevision != start.Session.Account.ContentRevision, "historical Android LF binding reproduced");
            Check(Load(Prepare("android-lf", lf).Host).GetProperty("ok").GetBoolean(), "old Android formatting migrates");
        }
        finally { Directory.Delete(folder, true); }
        return checks;
    }
}
