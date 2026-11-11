using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class BattleEquipmentChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Battle equipment: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("original/", StringComparison.Ordinal) ? path : "legacy/" + path[5..]));
        var content = new ClientContent(Read, Directory.GetFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var owned = content.Catalogs.Accounts.FromDefinition(1, "audit.equipment");
        var account = new PlayerAccount { Id = "local", ContentRevision = content.Catalogs.Accounts.SourceRevision,
            Roster = [owned with { Items = [] }], Inventory = content.Catalogs.Items.Items.Values.ToDictionary(i => i.Equipment.Id, _ => 3L) };
        var start = content.Start("conquest1", "fra", account);
        var target = start.Session.World.Units.First(u => u.Owner == start.Session.PlayerFaction && u.CommanderId == null
            && u.Combatant.Profile.Category == "infantry" && start.Session.World.Cells[u.Cell].Owner == u.Owner);
        var session = start.Session with { RosterDeployments = start.Session.RosterDeployments.Select(q => q with { Limit = 10 }).ToArray() };
        session = RosterDeployment.Deploy(session, session.Revision, target.Id, owned.Id, start.Rules.Roster).State;
        target = session.World.Units.Single(u => u.Id == target.Id);
        session = session with { World = session.World with { Units = session.World.Units.Select(u => u.Id != target.Id ? u
            : u with { Acted = true, RemainingMovement = 0, Combatant = u.Combatant with { Health = u.Combatant.Health - 1 } }).ToArray() } };
        target = session.World.Units.Single(u => u.Id == target.Id);
        foreach (var item in content.Catalogs.Items.Items.Values.Select(i => i.Equipment).Where(i => !i.Consumable))
        {
            var next = BattleEquipment.Change(session, session.Revision, target.Id, owned.Id, item.Id, true, start.Rules.Roster).State;
            var unit = next.World.Units.Single(u => u.Id == target.Id);
            var effect = Ew4Commander.Resolve(AccountCommanders.Resolve(next.Account.Roster.Single(), start.Rules.Roster.Account), "infantry", start.Rules.Commanders.LegacySkills);
            Check(next.Account.Roster.Single().Items.Contains(item.Id) && next.Account.Inventory[item.Id] == 3, "equip preserves owned copies " + item.Id);
            Check(unit.Acted && unit.RemainingMovement == 0 && unit.Combatant.Health == target.Combatant.Health, "no action or health refill " + item.Id);
            Check(unit.Combatant.AttackBonus == target.CommanderBase!.Combatant.AttackBonus + effect.AttackEquipment
                && unit.Combatant.DefenseBonus == target.CommanderBase.Combatant.DefenseBonus + effect.DefenseEquipment, "combat stats " + item.Id);
            Check(unit.Support.FieldHealing == target.CommanderBase.Support.FieldHealing + effect.FieldHealing
                && unit.Combatant.Geography == effect.Geography && unit.Combatant.FireImmune == (AccountCommanders.Resolve(next.Account.Roster.Single(), start.Rules.Roster.Account).HasSkill(2) || item.Effect == Ew4Commander.Effect(16)), "support and flags " + item.Id);
            var restored = BattleEquipment.Change(next, next.Revision, target.Id, owned.Id, item.Id, false, start.Rules.Roster).State;
            Check(JsonSerializer.Serialize(restored.World.Units.Single(u => u.Id == target.Id), ClientGame.Json)
                == JsonSerializer.Serialize(target, ClientGame.Json), "unequip restores exact unit " + item.Id);
            Check(restored.World.RandomState == session.World.RandomState && restored.World.Round == session.World.Round
                && restored.Account.Medals == session.Account.Medals, "no turn RNG or medal cost " + item.Id);
        }
        void Reject(GameSession state, string unit, string commander, string item, long? revision = null)
        {
            string before = JsonSerializer.Serialize(state, ClientGame.Json); bool rejected = false;
            try { BattleEquipment.Change(state, revision ?? state.Revision, unit, commander, item, true, start.Rules.Roster); } catch (RuleViolation) { rejected = true; }
            Check(rejected && JsonSerializer.Serialize(state, ClientGame.Json) == before, "invalid transaction atomic");
        }
        Reject(session, target.Id, owned.Id, "ew4.item.20", session.Revision + 1);
        Reject(session, target.Id, owned.Id, "ew4.item.13");
        var enemy = session.World.Units.First(u => u.Owner != session.PlayerFaction && u.CommanderId != null);
        Reject(session, enemy.Id, enemy.CommanderId!, "ew4.item.20");
        Reject(session, "missing", owned.Id, "ew4.item.20");
        Reject(session with { World = session.World with { PendingDialogue = "audit" } }, target.Id, owned.Id, "ew4.item.20");
        var moving = session with { World = session.World with { Units = session.World.Units.Select(u => u.Id == target.Id
            ? u with { Acted = false, RemainingMovement = Math.Max(1, u.Combatant.Profile.Movement - 2) } : u).ToArray() } };
        for (int repeat = 0; repeat < 3; repeat++)
        {
            moving = BattleEquipment.Change(moving, moving.Revision, target.Id, owned.Id, "ew4.item.18", true, start.Rules.Roster).State;
            moving = BattleEquipment.Change(moving, moving.Revision, target.Id, owned.Id, "ew4.item.18", false, start.Rules.Roster).State;
        }
        Check(moving.World.Units.Single(u => u.Id == target.Id).RemainingMovement == Math.Max(1, target.Combatant.Profile.Movement - 2), "repeated movement equipment cannot refill spent movement");
        var equipped = BattleEquipment.Change(session, session.Revision, target.Id, owned.Id, "ew4.item.20", true, start.Rules.Roster).State;
        var oldSlot = BattleEquipment.SynchronizeAccount(session, equipped.Account, start.Rules.Roster);
        Check(oldSlot.World.Units.Single(u => u.Id == target.Id).Combatant.AttackBonus == equipped.World.Units.Single(u => u.Id == target.Id).Combatant.AttackBonus,
            "older manual slot applies current account equipment to actual stats");
        var sameSlot = BattleEquipment.SynchronizeAccount(oldSlot, equipped.Account, start.Rules.Roster);
        Check(JsonSerializer.Serialize(oldSlot, ClientGame.Json) == JsonSerializer.Serialize(sameSlot, ClientGame.Json), "account synchronization is idempotent");
        var decoded = SessionSave.Decode(SessionSave.Encode(equipped), new(equipped.ContentRevision, equipped.World.ContentRevision, equipped.Account.ContentRevision));
        Check(JsonSerializer.Serialize(decoded, ClientGame.Json) == JsonSerializer.Serialize(equipped, ClientGame.Json), "save roundtrip keeps equipment and stats");
        string folder = Path.Combine(root, "build/battle-equipment-20261111/profile");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
        File.WriteAllBytes(Path.Combine(folder, "saves-fixture.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
        Directory.CreateDirectory(Path.Combine(folder, "saves/mode-2"));
        File.Copy(Path.Combine(folder, "saves-fixture.json"), Path.Combine(folder, "saves/mode-2/slot-0.json"), true);
        File.Copy(Path.Combine(folder, "saves-fixture.json"), Path.Combine(folder, "saves/mode-2/slot-1.json"), true);
        var client = new ClientGame(content, folder);
        JsonElement Call(string route, object command)
        {
            using var result = JsonDocument.Parse(client.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(result.RootElement.GetProperty("ok").GetBoolean(), route + " succeeds");
            return result.RootElement.GetProperty("data").Clone();
        }
        var view = Call("load", new { mode = 2 });
        var info = Call("unit-info", new { unit = target.Id, revision = view.GetProperty("revision").GetInt64() });
        Check(info.GetProperty("commander").GetProperty("editableEquipment").GetBoolean(), "owned battle commander editable");
        view = Call("action", new { op = "equip", unit = target.Id, commander = owned.Id, item = "ew4.item.20", revision = view.GetProperty("revision").GetInt64() });
        client = new ClientGame(content, folder); view = Call("load", new { mode = 2 });
        info = Call("unit-info", new { unit = target.Id, revision = view.GetProperty("revision").GetInt64() });
        Check(info.GetProperty("commander").GetProperty("items").EnumerateArray().Any(i => i.GetString() == "ew4.item.20"), "client autosave reload keeps equipment");
        var attack = view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == target.Id).GetProperty("attack").GetRawText();
        view = Call("load", new { mode = 2, slot = 1 });
        Check(view.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == target.Id).GetProperty("attack").GetRawText() == attack,
            "older manual slot and account equipment agree after client load");
        var hq = Call("headquarters", new { });
        foreach (var item in hq.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("consumable").GetBoolean()))
        {
            string id = item.GetProperty("id").GetString()!;
            var node = content.ItemDefinitions.Single(e => "ew4.item." + (int)e.Attribute("id")! == id);
            string original = content.Label("desc_" + (string)node.Attribute("name")!);
            Check(item.GetProperty("description").GetString()!.StartsWith(original + "\n具体效果：", StringComparison.Ordinal), "original prefix kept " + id);
            var definition = content.Catalogs.Items.Rules().Definitions[id].Equipment;
            if (definition.Effect == Ew4Commander.Effect(8))
                Check(item.GetProperty("description").GetString()!.Contains($"恢复{definition.Value}点生命", StringComparison.Ordinal), "healing description uses actual rule " + id);
        }
        var consumables = Call("items", new { unit = target.Id });
        Check(consumables.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("id").GetString() is "ew4.item.13" or "ew4.item.14" or "ew4.item.15")
            .All(i => i.GetProperty("description").GetString()!.Contains("当前部队实际恢复1点", StringComparison.Ordinal)), "context healing obeys missing health cap");
        File.WriteAllText(Path.Combine(root, "build/battle-equipment-20261111/fixture.json"), JsonSerializer.Serialize(new { target.Id, target.Cell, commander = owned.Id, checks }, ClientGame.Json));
        return checks;
    }
}
