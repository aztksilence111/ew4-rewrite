using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class EquipmentDescriptionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Equipment: " + label); }
        string assets = Path.Combine(root, "rewrite/data/original");
        byte[] Read(string name) => File.ReadAllBytes(name.StartsWith("original/", StringComparison.Ordinal) ? Path.Combine(assets, name[9..])
            : Path.Combine(root, "rewrite/data/legacy", name[5..]));
        var files = Directory.GetFiles(assets, "*.btl");
        var content = new ClientContent(Read, files);
        var prior = new ClientContent(Read, files, false);
        Check(content.CorrectOriginalEquipment && !prior.CorrectOriginalEquipment, "base correction is explicit and raw compatibility remains available");
        Check(content.Catalogs.Accounts.SourceRevision == prior.Catalogs.Accounts.SourceRevision, "existing account binding is preserved");
        int numeric = 0, changed = 0;
        foreach (var node in content.ItemDefinitions)
        {
            int id = (int)node.Attribute("id")!;
            var item = content.Catalogs.Runtime.Items[id];
            Check(item == content.Catalogs.Items.Items[id].Equipment, "combat and trading catalogs agree for " + id);
            if (item.Value != prior.Catalogs.Runtime.Items[id].Value) changed++;
            var match = Regex.Match(content.Label("desc_" + (string)node.Attribute("name")!), "(攻击|防御|移动|补给)\\+(\\d+)");
            if (!match.Success || item.Consumable) continue;
            var effects = Ew4Commander.Resolve(new Commander { Id = "audit", Items = [item] }, item.TargetCategory ?? "infantry");
            int? actual = item.Effect switch
            {
                "ew4.item_effect.9" => effects.MovementBonus, "ew4.item_effect.10" => effects.DefenseEquipment,
                "ew4.item_effect.11" => effects.AttackEquipment, "ew4.item_effect.5" => effects.FieldHealing,
                "ew4.item_effect.13" => effects.AdjacentHealing, "ew4.item_effect.14" => effects.AdjacentAttack,
                "ew4.item_effect.15" => effects.AdjacentDefense, _ => null
            };
            if (!actual.HasValue) continue;
            Check(actual == int.Parse(match.Groups[2].Value), "numeric description agrees for every item alias " + id);
            numeric++;
        }
        Check(numeric > 38 && changed > 32, "duplicate IDs receive the same corrections");
        foreach (int id in new[] { 1, 2, 3, 4 })
        {
            var owned = content.Catalogs.Accounts.FromDefinition(1, "xp.audit") with { Items = ["ew4.item." + id] };
            var runtime = content.Catalogs.Accounts.DeploymentRules(content.Catalogs.Combat).Resolve(owned, "audit.owner").Runtime;
            int value = content.Catalogs.Runtime.Items[id].Value;
            Check(runtime.RankExperiencePercent == (id >= 3 ? value : null) && runtime.TitleExperiencePercent == (id < 3 ? value : null), "correct experience track " + id);
            Check(Ew4Experience.CommanderGain(100, id < 3, [], id < 3 ? runtime.TitleExperiencePercent : runtime.RankExperiencePercent) == value,
                "correct experience amount " + id);
        }
        var profile = new UnitProfile { Id = "audit.infantry", Category = "infantry", MaxHealth = 100, MinimumAttack = 10, MaximumAttack = 10,
            AttackBands = [new(-1, 1)], DamageMultipliers = new Dictionary<string, float> { ["infantry"] = 1 } };
        var plain = new Combatant { Profile = profile, Health = 100 };
        var armed = Ew4Commander.Attach(plain, new Commander { Id = "audit", Items = [content.Catalogs.Runtime.Items[20]] });
        int damage = Ew4Combat.Calculate(armed, plain, 1, new Ew4Random(1)).DefenderDamage;
        Check(damage == 18, "rifle adds eight actual damage");
        int healing = Ew4Commander.Resolve(new Commander { Id = "audit", Items = [content.Catalogs.Runtime.Items[8]] }, "infantry").FieldHealing;
        var healed = TurnSettlement.EndSite(new SiteEndInput { Units = [new UnitEndInput { Id = "audit", Health = 50, MaximumHealth = 100,
            HasCommander = true, FieldHealing = healing }] }, new Ew4Random(1));
        Check(healed.Units[0].Health == 54, "tent adds four actual health at turn settlement");

        string xml = File.ReadAllText(Path.Combine(assets, "def_item.xml"));
        string modXml = xml.Replace("value=\"6\" target=\"infantry\"", "value=\"99\" target=\"infantry\"", StringComparison.Ordinal);
        var modItems = LegacyItemTradingCatalog.Read(modXml, true);
        Check(modItems.Items[20].Equipment.Value == 99 && modItems.Items[8].Equipment.Value == 2
            && modItems.Items[1].Equipment.Effect == Ew4Commander.Effect(1), "custom item tables retain author values and effects");
        var modCatalog = JsonNode.Parse(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")))!;
        modCatalog["source_sha256"]!["def_item.xml"] = new string('a', 64);
        var customRuntime = LegacyRuntimeCatalog.FromJson(modCatalog.ToJsonString(), true);
        Check(customRuntime.Items[20].Value == 6 && customRuntime.Items[1].Effect == Ew4Commander.Effect(1), "custom runtime catalog is not normalized by base IDs");

        foreach (int[] equipment in new[] { new[] { 20, 1 }, new[] { 8, 18 }, new[] { 47, 48 } })
        {
            var owned = prior.Catalogs.Accounts.FromDefinition(1, "audit.owned") with { Items = equipment.Select(id => "ew4.item." + id).ToArray() };
            var account = new PlayerAccount { Id = "local", ContentRevision = prior.Catalogs.Accounts.SourceRevision, Roster = [owned],
                Inventory = owned.Items.ToDictionary(id => id, _ => 1L), Medals = 12345, Badges = 23456 };
            var oldStart = prior.Start("conquest1", "fra", account);
            var receiving = oldStart.Session.World.Units.First(u => u.Owner == oldStart.Session.PlayerFaction && u.CommanderId == null
                && u.Combatant.Profile.Category == "infantry" && oldStart.Session.World.Cells[u.Cell].Owner == u.Owner);
            var session = oldStart.Session with { RosterDeployments = oldStart.Session.RosterDeployments.Select(q => q with { Limit = 10 }).ToArray() };
            session = RosterDeployment.Deploy(session, session.Revision, receiving.Id, owned.Id, oldStart.Rules.Roster).State;
            var attached = session.World.Units.Single(u => u.Id == receiving.Id);
            session = session with { World = session.World with { Units = session.World.Units.Select(u => u.Id != attached.Id ? u
                : u with { RemainingMovement = 0, Acted = true, Combatant = u.Combatant with { Health = u.Combatant.Health - 1 } }).ToArray() } };
            string folder = Path.Combine(root, "rewrite/reports/equipment-fix-20261021/saves", Guid.NewGuid().ToString("N"));
            string save = Path.Combine(folder, "saves/mode-2/slot-0.json");
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientGame.Json));
            void Write(GameSession value) => File.WriteAllBytes(save, JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot("conquest1", "fra", SessionSave.Encode(value)), ClientGame.Json));
            Write(session);
            var host = new ClientGame(content, folder);
            JsonElement Load()
            {
                using var response = JsonDocument.Parse(host.Dispatch("/api/load", "{\"mode\":2}"));
                return response.RootElement.Clone();
            }
            Check(Load().GetProperty("ok").GetBoolean(), "authentic old save loads and migrates");
            var currentStart = content.Start("conquest1", "fra", account);
            GameSession Saved()
            {
                var slot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(save), ClientGame.Json)!;
                return SessionSave.Decode(slot.Session, new(currentStart.Session.ContentRevision, currentStart.Session.World.ContentRevision, account.ContentRevision));
            }
            var migrated = Saved();
            var unit = migrated.World.Units.Single(u => u.Id == receiving.Id);
            var definition = content.Catalogs.Accounts.DeploymentRules(content.Catalogs.Combat).Resolve(owned, receiving.Owner);
            var expected = Ew4Commander.Attach(unit.CommanderBase!.Combatant, definition.Definition);
            Check(unit.Combatant.AttackBonus == expected.AttackBonus && unit.Combatant.DefenseBonus == expected.DefenseBonus, "old save receives corrected attack and defense");
            var support = Ew4Commander.Resolve(definition.Definition, unit.Combatant.Profile.Category);
            Check(unit.Support.FieldHealing == unit.CommanderBase.Support.FieldHealing + support.FieldHealing
                && unit.Support.AdjacentAttack == unit.CommanderBase.Support.AdjacentAttack + support.AdjacentAttack
                && unit.Support.AdjacentHealing == unit.CommanderBase.Support.AdjacentHealing + support.AdjacentHealing, "old save receives corrected healing and auras");
            Check(unit.RemainingMovement == 0 && unit.Acted && unit.Combatant.Health == attached.Combatant.Health - 1, "migration does not restore movement, actions or health");
            Check(migrated.World.RandomState == session.World.RandomState && migrated.World.AiRandomState == session.World.AiRandomState
                && migrated.World.Round == session.World.Round && migrated.Account.Medals == account.Medals
                && migrated.World.Cells.Select(c => c.Owner).SequenceEqual(session.World.Cells.Select(c => c.Owner)), "migration preserves RNG, round, wallet and territory");
            var commander = migrated.World.Commanders.Single(c => c.Id == owned.Id);
            Check(commander.RankExperiencePercent == definition.Runtime.RankExperiencePercent
                && commander.TitleExperiencePercent == definition.Runtime.TitleExperiencePercent, "old save receives corrected experience routing");
            Check(Load().GetProperty("ok").GetBoolean(), "migrated save reloads");
            Check(JsonSerializer.Serialize(Saved(), ClientGame.Json) == JsonSerializer.Serialize(migrated, ClientGame.Json), "second load never stacks equipment corrections");
            Write(session with { ContentRevision = "foreign.content" });
            var rejected = Load();
            Check(!rejected.GetProperty("ok").GetBoolean() && rejected.GetProperty("code").GetString() == "save.content_revision", "unrelated content fingerprints remain rejected");
        }
        return checks;
    }
}
