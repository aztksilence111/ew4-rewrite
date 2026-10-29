using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class TavernPortraitChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Tavern portrait: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var started = content.Start("conquest1", "fra");
        var initial = started.Session;
        var battle = content.Battle(initial.World.ScenarioId);
        var sources = battle.Taverns.SelectMany(t => t.Candidates).ToArray();
        var output = Path.Combine(root, "build/tavern-portrait-check-20261029");
        Directory.CreateDirectory(output);
        foreach (int index in new[] { 0, sources.Length - 1 }.Distinct())
        {
            var candidate = initial.TavernCandidates[index];
            var definition = content.Catalogs.Runtime.Commanders[sources[index].CommanderId].Definition;
            var plain = initial.World.Units.First(u => u.Owner == initial.PlayerFaction && u.CommanderId == null && !initial.World.Cells[u.Cell].Terrain.Sea);
            var world = initial.World with
            {
                Round = Math.Max(initial.World.Round, candidate.AvailableRound), ActiveFaction = initial.PlayerFaction,
                Phase = TurnPhase.Active, PendingDialogue = null, QueuedDialogues = [], Story = null, Events = [], ScenarioEvents = [],
                Units = initial.World.Units.Select(u => u.Id == plain.Id ? u with { Cell = candidate.Cell }
                    : u.Cell == candidate.Cell ? u with { Cell = plain.Cell } : u).ToArray(),
                Cells = initial.World.Cells.Select((c, i) => i == candidate.Cell ? c with { Owner = initial.PlayerFaction } : c).ToArray(),
                Factions = initial.World.Factions.Select(f => f.Id == initial.PlayerFaction ? f with { Resources = new(1000000, 1000000, 1000000) } : f).ToArray()
            };
            var session = initial with { World = world, Account = initial.Account with { Medals = 1000000 } };
            var profile = Path.Combine(output, "case-" + index);
            Directory.CreateDirectory(Path.Combine(profile, "saves/mode-2"));
            File.WriteAllBytes(Path.Combine(profile, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
            File.WriteAllBytes(Path.Combine(profile, "saves/mode-2/slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
            var host = new ClientGame(content, profile);
            JsonElement Call(string route, object command)
            {
                using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
                Check(response.RootElement.GetProperty("ok").GetBoolean(), route + ": " + response.RootElement.GetRawText()[..Math.Min(200, response.RootElement.GetRawText().Length)]);
                return response.RootElement.GetProperty("data").Clone();
            }
            JsonElement Unit(JsonElement view, string id) => view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == id);
            void CheckPortrait(JsonElement view, string stage)
            {
                var unit = Unit(view, plain.Id);
                Check(unit.GetProperty("commanderId").GetString() == candidate.CommanderId, stage + " keeps tavern instance");
                Check(unit.GetProperty("commanderImage").GetString() == definition.DisplayName, stage + " displays tavern map portrait");
                Check(unit.GetProperty("commanderDefinitionId").GetString() == definition.Id, stage + " exposes stable definition identity");
                var detail = Call("unit-info", new { unit = plain.Id, revision = view.GetProperty("revision").GetInt64() }).GetProperty("commander");
                Check(detail.GetProperty("definitionId").GetString() == definition.Id, stage + " agrees with detail portrait");
            }
            var before = Call("load", new { slot = 1, mode = 2 });
            Check(Unit(before, plain.Id).GetProperty("commanderImage").ValueKind == JsonValueKind.Null, "plain unit has no portrait");
            var initialGeneral = before.GetProperty("units").EnumerateArray().First(u => u.GetProperty("commanderId").ValueKind != JsonValueKind.Null);
            string generalId = initialGeneral.GetProperty("id").GetString()!;
            var tavern = Call("commerce", new { cell = candidate.Cell, op = "tavern" });
            Check(tavern.GetProperty("candidates").EnumerateArray().Single(c => c.GetProperty("id").GetString() == candidate.Id).GetProperty("available").GetBoolean(), "real tavern candidate available");
            var hired = Call("action", new { op = "hire", unit = plain.Id, offer = candidate.Id, revision = before.GetProperty("revision").GetInt64() });
            CheckPortrait(hired, "hire " + index);
            Check(Unit(hired, generalId).GetProperty("commanderImage").GetRawText() == initialGeneral.GetProperty("commanderImage").GetRawText(), "initial general portrait unchanged");
            Check(!Call("commerce", new { cell = candidate.Cell, op = "tavern" }).GetProperty("candidates").EnumerateArray().Any(c => c.GetProperty("id").GetString() == candidate.Id), "hired candidate removed from remaining offers");
            CheckPortrait(Call("load", new { slot = 0, mode = 2 }), "autosave reload " + index);
            host = new ClientGame(content, profile);
            CheckPortrait(Call("load", new { slot = 0, mode = 2 }), "fresh host reload " + index);
            Check(Call("headquarters", new { }).GetProperty("roster").GetArrayLength() == session.Account.Roster.Count, "temporary general does not enter permanent roster");
            if (index == 0)
                File.WriteAllText(Path.Combine(output, "fixture.json"), JsonSerializer.Serialize(new { profile, unit = plain.Id, cell = candidate.Cell,
                    offer = candidate.Id, commander = candidate.CommanderId, definition = definition.Id, image = definition.DisplayName }, ClientGame.Json));
        }
        return checks;
    }
}
