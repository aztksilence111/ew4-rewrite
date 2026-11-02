using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;
using Ew4.Persistence;

internal static class CommanderAbilityModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Commander abilities: " + label); }
        string run = Path.Combine(root, "build/commander-abilities-20261102"), data = Path.Combine(root, "rewrite/data");
        string example = Path.Combine(root, "rewrite/mod-kit/examples/commander-abilities"), author = Path.Combine(run, "author");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(example, "rewrite"), "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(author, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, true);
        }
        string configuration = Path.Combine(author, "rewrite/commander-abilities.json");
        var config = ExtensionLoader.ParseJson<CommanderAbilityConfiguration>(File.ReadAllBytes(configuration));
        var definitions = config.Abilities.Concat(Enumerable.Range(1, 31).Select(i =>
            new CommanderAbilityDefinition("demo.extra." + i, "Ability " + i, "upgrade_tag_infantry.png", i == 31 ? int.MaxValue : i))).ToArray();
        Directory.CreateDirectory(Path.Combine(author, "rewrite/icons"));
        File.Copy(Path.Combine(data, "client/ui/image_ui_hd/upgrade_tag_ship.png"), Path.Combine(author, "rewrite/icons/air.png"), true);
        definitions[0] = definitions[0] with { Icon = "rewrite/icons/air.png" };
        config = config with { Abilities = definitions, Commanders = new() { ["ew4.commander.1"] = new() { ["demo.air"] = 12 },
            ["ew4.commander.2"] = new() { ["demo.air"] = 2 } }, Order = ["demo.air", "movement", "infantry"],
            Presentation = new() { ["infantry"] = new("步兵指挥", "upgrade_tag_cavalry.png") } };
        config.Validate();
        (config with { Abilities = [], Commanders = new(), Units = new(), Inheritance = [], Order = ["movement", "infantry"], Presentation = new() }).Validate();
        Check(true, "original-only reordering is supported");
        void Reject(CommanderAbilityConfiguration invalid, string label)
        {
            try { invalid.Validate(); throw new InvalidOperationException("Accepted " + label); }
            catch (RuleViolation e) { Check(e.Code == "extension.commander_abilities", label); }
        }
        Reject(config with { Order = ["demo.air", "demo.air"] }, "duplicate order entry");
        Reject(config with { Order = ["unknown.air"] }, "unknown order entry");
        Reject(config with { Presentation = new() { ["unknown.air"] = new("Bad") } }, "unknown presentation entry");
        Reject(config with { Abilities = definitions.Append(definitions[0]).ToArray() }, "too many rows");
        Reject(config with { Abilities = [definitions[0], definitions[0]] }, "duplicate ID");
        Reject(config with { Abilities = [definitions[0] with { Id = "movement" }] }, "reserved ID");
        Reject(config with { Abilities = [definitions[0] with { DefaultValue = -1 }] }, "negative default");
        Reject(config with { Abilities = [definitions[0] with { Icon = "../unsafe.png" }] }, "unsafe icon");
        Reject(config with { Commanders = new() { ["ew4.commander.1"] = new() { ["demo.air"] = -1 } } }, "negative value");
        Reject(config with { Units = new() { ["ew4.unit.fra.light_infantry.0"] = "unknown.air" } }, "unknown ability");
        Reject(config with { Inheritance = [new("demo.teach.air", "demo.air", -1)] }, "negative teaching");
        File.WriteAllBytes(configuration, JsonSerializer.SerializeToUtf8Bytes(config, ExtensionLoader.Json));
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        store.Seal(author, "commander-abilities", "Commander abilities", "1.0", ["conquest1"]);
        var installed = store.Import(author, "ignored", "ignored"); store.Activate(installed.Key);
        var content = baseline.Content(store.Read, ["conquest1"]);
        void RejectReference(CommanderAbilityConfiguration invalid, string label, int api = 3)
        {
            try
            {
                var extension = ExtensionLoader.Load(p => p == "rewrite/extension.json"
                    ? JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, apiVersion = api, commanderAbilities = "rewrite/abilities.json" })
                    : p == "rewrite/abilities.json" ? JsonSerializer.SerializeToUtf8Bytes(invalid, ExtensionLoader.Json) : store.Read(p), content.Countries);
                extension.PatchCatalog("{\"skills\":[{\"id\":\"demo.teach.air\"}],\"commanders\":[{\"id\":\"ew4.commander.1\"},{\"id\":\"ew4.commander.2\"}],\"profiles\":[{\"id\":\"ew4.unit.fra.light_infantry.0\"}]}");
                throw new InvalidOperationException("Accepted " + label);
            }
            catch (RuleViolation e) { Check(e.Code is "extension.commander_abilities" or "extension.version", label); }
        }
        RejectReference(config with { Commanders = new() { ["unknown.commander"] = new() { ["demo.air"] = 1 } } }, "unknown commander reference");
        RejectReference(config with { Units = new() { ["unknown.unit"] = "demo.air" } }, "unknown profile reference");
        RejectReference(config with { Inheritance = [new("unknown.teach", "demo.air")] }, "unknown teaching skill");
        RejectReference(config, "API2 rejects new fields", 2);
        var donor = content.Catalogs.Accounts.FromDefinition(1, "ability.donor");
        var receiver = content.Catalogs.Accounts.FromDefinition(2, "ability.receiver");
        Check(donor.Learning.Abilities["demo.air"] == 12 && receiver.Learning.Abilities["demo.air"] == 2, "commander overrides");
        Check(donor.Learning.Abilities["demo.extra.31"] == int.MaxValue, "all custom defaults retained");
        var raw = new byte[60];
        BitConverter.TryWriteBytes(raw.AsSpan(0), 1);
        for (int i = 0; i < 7; i++) BitConverter.TryWriteBytes(raw.AsSpan(20 + i * 4), i + 1);
        BitConverter.TryWriteBytes(raw.AsSpan(52), -1); BitConverter.TryWriteBytes(raw.AsSpan(56), -1);
        var importedRoster = content.Catalogs.Accounts.Import(LegacyRosterCommander.Read(raw), "legacy.import");
        Check(importedRoster.Learning.Abilities["demo.air"] == 12 && importedRoster.Learning.Abilities["movement"] == 7, "binary import merges custom defaults and original values");
        var resolved = AccountCommanders.Resolve(donor, content.Catalogs.Accounts.Rules());
        var profile = content.Catalogs.Runtime.Profiles["ew4.unit.fra.light_infantry.0"].Profile;
        var unit = new Combatant { Profile = profile, Health = profile.MaxHealth };
        Check(profile.CommanderAbility == "demo.air" && Ew4Commander.Attach(unit, resolved).Stars == 12, "custom combat binding");
        Check(Ew4Commander.Attach(unit with { Profile = profile with { CommanderAbility = null } }, resolved).Stars == resolved.Stars[profile.Category], "unmapped original category");
        var target = unit with { Profile = profile with { MinimumAttack = 10, MaximumAttack = 10, AttackBands = [new(0, 5)] } };
        int Damage(int stars) => Ew4Combat.Calculate(target with { Stars = stars }, target, 1, new(123)).DefenderDamage;
        Check(Damage(12) - Damage(11) == 5, "custom twelfth star affects combat");
        var account = new PlayerAccount { Id = "local", ContentRevision = content.Catalogs.Accounts.SourceRevision, Medals = 100000, Roster = [donor, receiver] };
        string saves = Path.Combine(run, "saves/mods", installed.Id, installed.LogicRevision);
        Directory.CreateDirectory(saves); File.WriteAllBytes(Path.Combine(saves, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientJson()));
        JsonElement Call(string route, object body)
        {
            using var reply = JsonDocument.Parse(store.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(reply.RootElement.GetProperty("ok").GetBoolean(), route + ": " + reply.RootElement.GetRawText()[..Math.Min(300, reply.RootElement.GetRawText().Length)]);
            return reply.RootElement.GetProperty("data").Clone();
        }
        var hq = Call("headquarters", new { });
        Check(hq.GetProperty("commanderAbilities").GetArrayLength() == 32, "headquarters exposes all rows");
        var preview = Call("commander", new { op = "preview_inherit", revision = hq.GetProperty("revision").GetInt64(), commander = receiver.Id, target = donor.Id });
        Check(preview.GetProperty("learning").GetProperty("abilities").GetProperty("demo.air").GetInt32() == 4, "actual inheritance preview");
        File.WriteAllText(Path.Combine(run, "headquarters.json"), hq.GetRawText());
        var start = content.Start("conquest1", "fra", account);
        var first = start.Session.World.Units.First(u => u.Owner == start.Session.PlayerFaction);
        var battleUnit = first with { Combatant = Ew4Commander.Attach(unit, resolved), RemainingMovement = 0 };
        var session = start.Session with { World = start.Session.World with { Units = start.Session.World.Units.Select(u => u.Id == first.Id ? battleUnit : u).ToArray() } };
        var restored = SessionSave.Decode(SessionSave.Encode(session), new(session.ContentRevision, session.World.ContentRevision, account.ContentRevision));
        Check(restored.World.Units.Single(u => u.Id == first.Id).Combatant.Profile.CommanderAbility == "demo.air", "battle saves unit binding");
        Check(restored.World.Units.Single(u => u.Id == first.Id).Combatant.Stars == 12 && restored.Account.Roster[0].Learning.Abilities["demo.air"] == 12, "battle saves star values");
        hq = Call("commander", new { op = "inherit", revision = hq.GetProperty("revision").GetInt64(), commander = receiver.Id, target = donor.Id });
        Check(hq.GetProperty("roster").GetArrayLength() == 1 && hq.GetProperty("roster")[0].GetProperty("learning").GetProperty("abilities").GetProperty("demo.air").GetInt32() == 4, "actual teaching transaction");
        var restarted = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        using var loaded = JsonDocument.Parse(restarted.Dispatch("/api/headquarters", "{}"));
        Check(loaded.RootElement.GetProperty("data").GetProperty("roster")[0].GetProperty("learning").GetProperty("abilities").GetProperty("demo.extra.31").GetInt32() == int.MaxValue, "account restart retains custom data");
        string archive = Path.Combine(run, "abilities.ew4mod"); if (File.Exists(archive)) File.Delete(archive); store.Export(installed.Key, archive);
        var offline = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        Check(offline.Import(archive, "ignored", "ignored").LogicRevision == installed.LogicRevision, "archive roundtrip");
        File.WriteAllBytes(Path.Combine(saves, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientJson()));
        File.WriteAllText(Path.Combine(run, "fixture.json"), JsonSerializer.Serialize(new { key = installed.Key, mods = Path.Combine(run, "mods"), saves = Path.Combine(run, "saves"), receiver = receiver.Id, donor = donor.Id }));
        return checks;
    }
    private static JsonSerializerOptions ClientJson() => new(JsonSerializerDefaults.Web);
}
