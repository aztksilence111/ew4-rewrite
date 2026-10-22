using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class ConsumableChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Consumables: " + label); }
        string assets = Path.Combine(root, "rewrite/data/original");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("original/", StringComparison.Ordinal) ? Path.Combine(assets, path[9..])
            : Path.Combine(root, "rewrite/data/legacy", path[5..]));
        var content = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        var ids = new[] { 11, 12, 13, 14, 15, 53 };
        string Key(int id) => "ew4.item." + id;
        var account = new PlayerAccount { Id = "local", ContentRevision = content.Catalogs.Accounts.SourceRevision,
            Medals = 12345, Inventory = ids.ToDictionary(Key, _ => 3L) };
        var start = content.Start("conquest1", "fra", account);
        var target = start.Session.World.Units.First(u => u.Owner == start.Session.PlayerFaction && u.CommanderId == null
            && u.Combatant.Profile.Category == "infantry" && u.Combatant.ConstructionTurns == 0);
        var morale = MoraleState.Set(-2, false);
        target = target with { RemainingMovement = 0, Acted = true, Experience = 13, Veterancy = start.Rules.Training.Stages[0].Id,
            Morale = morale, Combatant = target.Combatant with { Health = 1, Morale = morale.Effective } };
        GameSession Replace(GameSession state, UnitState unit) => state with { World = state.World with {
            Units = state.World.Units.Select(u => u.Id == unit.Id ? unit : u).ToArray() } };
        var initial = Replace(start.Session, target);
        initial.Validate();
        var trading = content.Catalogs.Items.Rules(); var training = start.Rules.Training;
        SessionChange Use(GameSession state, int id, string? unit = null, long? revision = null) => ConsumableItems.Use(state,
            revision ?? state.Revision, unit ?? target.Id, Key(id), trading, training);
        foreach (int id in ids)
        {
            var change = Use(initial, id); var next = change.State; var unit = next.World.Units.Single(u => u.Id == target.Id);
            Check(next.Account.Inventory[Key(id)] == 2 && next.Account.Inventory.Where(p => p.Key != Key(id))
                .All(p => initial.Account.Inventory[p.Key] == p.Value), "exactly one copy consumed " + id);
            Check(next.Revision == initial.Revision + 1 && next.World.Revision == initial.World.Revision + 1
                && next.Account.Revision == initial.Account.Revision + 1, "all revisions advance " + id);
            Check(unit.Acted && unit.RemainingMovement == 0, "spent actions and movement stay spent " + id);
            Check(next.World.RandomState == initial.World.RandomState && next.World.Round == initial.World.Round
                && next.Account.Medals == initial.Account.Medals && next.World.Factions.Select(f => (f.Id, f.Resources))
                    .SequenceEqual(initial.World.Factions.Select(f => (f.Id, f.Resources))), "no resource, RNG or turn cost " + id);
            if (id is 11 or 12)
                Check(unit.Morale.Baseline == (id == 11 ? 0 : 1) && unit.Morale.Effective == unit.Combatant.Morale
                    && unit.Morale.Duration == 3 && unit.Combatant.Health == target.Combatant.Health, "morale effect " + id);
            else if (id is 13 or 14 or 15)
                Check(unit.Combatant.Health == Math.Min(unit.Combatant.Profile.MaxHealth, 1 + trading.Definitions[Key(id)].Equipment.Value), "healing effect " + id);
            else Check(unit.Veterancy == training.Stages[1].Id && unit.Experience == 0
                && unit.Combatant.Health == Math.Min(unit.Combatant.Profile.MaxHealth, 1 + training.Stages[1].HealingOnEntry), "star promotes one level with promotion healing");
        }
        foreach (int id in new[] { 13, 14, 15 })
        {
            var large = target with { Combatant = target.Combatant with { Profile = target.Combatant.Profile with { MaxHealth = 500 } } };
            var next = Use(Replace(initial, large), id).State;
            Check(next.World.Units.Single(u => u.Id == target.Id).Combatant.Health == 1 + trading.Definitions[Key(id)].Equipment.Value,
                "full uncapped healing amount " + id);
        }
        void Reject(GameSession state, int id, string label, string? unit = null, long? revision = null)
        {
            string before = JsonSerializer.Serialize(state, ClientGame.Json);
            bool rejected = false;
            try { Use(state, id, unit, revision); } catch (RuleViolation) { rejected = true; }
            Check(rejected, label + " rejected");
            Check(before == JsonSerializer.Serialize(state, ClientGame.Json), label + " leaves inventory and world unchanged");
        }
        Reject(initial, 13, "stale request", revision: initial.Revision + 1);
        Reject(initial, 20, "permanent equipment");
        Reject(initial, 9999, "unknown item");
        Reject(initial, 13, "enemy target", initial.World.Units.First(u => u.Owner != initial.PlayerFaction).Id);
        Reject(initial, 13, "missing target", "missing.unit");
        Reject(initial with { Account = account with { Inventory = new Dictionary<string, long>() } }, 13, "empty inventory");
        Reject(Replace(initial, target with { Combatant = target.Combatant with { Health = target.Combatant.Profile.MaxHealth } }), 13, "full health");
        var normal = new MoraleState();
        Reject(Replace(initial, target with { Morale = normal, Combatant = target.Combatant with { Morale = 0 } }), 11, "normal morale wine");
        var high = MoraleState.Set(1, false);
        Reject(Replace(initial, target with { Morale = high, Combatant = target.Combatant with { Morale = 1 } }), 12, "high morale spirit");
        Reject(Replace(initial, target with { Veterancy = training.Stages[^1].Id }), 53, "maximum level");
        Reject(Replace(initial, target with { Combatant = target.Combatant with { ConstructionTurns = 1 } }), 13, "construction");
        Reject(initial with { World = initial.World with { PendingDialogue = "audit.pending" } }, 13, "pending dialogue");
        Reject(initial with { World = initial.World with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory } }, 13, "finished battle");
        var last = initial with { Account = account with { Inventory = new Dictionary<string, long> { [Key(13)] = 1 } } };
        Check(!Use(last, 13).State.Account.Inventory.ContainsKey(Key(13)), "last copy removes inventory key");
        var enemyFaction = initial.World.Factions.First(f => f.Id != initial.PlayerFaction && !f.Defeated);
        Reject(initial with { World = initial.World with { ActiveFaction = enemyFaction.Id } }, 13, "other faction turn");

        string folder = Path.Combine(root, "rewrite/reports/consumables-20261021");
        Directory.CreateDirectory(folder);
        var fixture = new ClientSlot("conquest1", "fra", SessionSave.Encode(initial));
        File.WriteAllBytes(Path.Combine(folder, "fixture-slot.json"), JsonSerializer.SerializeToUtf8Bytes(fixture, ClientGame.Json));
        File.WriteAllBytes(Path.Combine(folder, "fixture-account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientGame.Json));
        string clientFolder = Path.Combine(folder, "client", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(clientFolder);
        File.WriteAllBytes(Path.Combine(clientFolder, "session.json"), JsonSerializer.SerializeToUtf8Bytes(fixture, ClientGame.Json));
        File.WriteAllBytes(Path.Combine(clientFolder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientGame.Json));
        var client = new ClientGame(content, clientFolder);
        JsonElement Call(string route, object command, bool success = true)
        {
            using var parsed = JsonDocument.Parse(client.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(parsed.RootElement.GetProperty("ok").GetBoolean() == success, route + " result " + parsed.RootElement.GetRawText()[..Math.Min(150, parsed.RootElement.GetRawText().Length)]);
            return parsed.RootElement.GetProperty(success ? "data" : "code").Clone();
        }
        var view = Call("load", new { });
        Check(Call("action", new { op = "select", revision = view.GetProperty("revision").GetInt64(), cell = target.Cell }).GetProperty("items").GetBoolean(), "chest exposed for owned unit without commander");
        var menu = Call("items", new { unit = target.Id });
        Check(menu.GetProperty("items").GetArrayLength() == 6, "all six consumables exposed");
        long rev = menu.GetProperty("revision").GetInt64();
        view = Call("action", new { op = "use_item", revision = rev, unit = target.Id, item = Key(12) });
        Check(view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == target.Id).GetProperty("morale").GetInt32() == 1, "client installs actual morale effect");
        Check(view.GetProperty("suppliedUnit").ValueKind == JsonValueKind.Null, "morale item does not request healing presentation");
        Call("action", new { op = "use_item", revision = rev, unit = target.Id, item = Key(12) }, false);
        menu = Call("items", new { unit = target.Id });
        Check(menu.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == Key(12)).GetProperty("quantity").GetInt64() == 2, "repeat click never consumes twice");
        var persisted = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Path.Combine(clientFolder, "account.json")), ClientGame.Json)!;
        Check(persisted.Inventory[Key(12)] == 2, "account inventory persisted immediately");
        view = Call("action", new { op = "use_item", revision = view.GetProperty("revision").GetInt64(), unit = target.Id, item = Key(13) });
        Check(view.GetProperty("suppliedUnit").GetString() == target.Id, "healing item requests supply presentation once");
        client = new ClientGame(content, clientFolder);
        view = Call("load", new { mode = 2 });
        Check(view.GetProperty("suppliedUnit").ValueKind == JsonValueKind.Null, "load does not replay supply presentation");
        Check(view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == target.Id).GetProperty("morale").GetInt32() == 1, "auto save survives fresh client load");
        Check(Call("items", new { unit = target.Id }).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == Key(12)).GetProperty("quantity").GetInt64() == 2, "reload preserves consumed inventory");
        File.WriteAllText(Path.Combine(folder, "backend.json"), JsonSerializer.Serialize(new { checks, errors = 0, target = new { target.Id, target.Cell }, clientFolder }, ClientGame.Json));
        return checks;
    }
}
