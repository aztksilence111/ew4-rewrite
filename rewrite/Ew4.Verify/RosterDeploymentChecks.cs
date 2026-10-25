using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class RosterDeploymentChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Roster deployment: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + " actual=" + error.Code); return; }
            throw new InvalidOperationException("Expected roster deployment rejection: " + code);
        }
        string assets = Path.Combine(root, "apk_work/original/assets");
        var buildings = LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml")));
        var combat = LegacyCombatCatalog.Read(File.ReadAllText(Path.Combine(root, "reverse/reports/runtime_rule_tables.json")),
            File.ReadAllText(Path.Combine(assets, "def_terraintype.xml")), File.ReadAllText(Path.Combine(assets, "def_installation.xml")), buildings);
        var runtime = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var accounts = LegacyCommanderAccounts.Read(runtime, combat, File.ReadAllText(Path.Combine(root, "reverse/reports/metagame_rule_tables.json")));
        var rules = accounts.DeploymentRules(combat);
        foreach (var pair in runtime.Commanders)
        {
            var owned = accounts.FromDefinition(pair.Key, "owned." + pair.Key);
            var resolved = rules.Resolve(owned, "winner");
            Check(resolved.Runtime.Id == owned.Id && resolved.Definition.Id == owned.Id && resolved.Runtime.Owner == "winner", "owned instance identity bound to faction");
            Check(resolved.Runtime.Rank == owned.Learning.Rank && resolved.Runtime.Title == owned.Learning.Title
                && resolved.Runtime.RankHealthBonus == pair.Value.HealthBonus && resolved.Runtime.MaximumTrainingLevel == pair.Value.Training
                && resolved.Runtime.RankExperiencePercent == pair.Value.RankExperiencePercent && resolved.Runtime.TitleExperiencePercent == pair.Value.TitleExperiencePercent,
                "all original definitions resolve rank, training and equipped experience modifiers");
        }
        var roster = Enumerable.Range(0, 101).Select(i => accounts.FromDefinition(1, "owned." + i)).ToArray();
        var initial = new GameSession { ContentRevision = "session.roster", PlayerFaction = "winner", World = WorldChecks.Example(),
            Account = new() { Id = "account", ContentRevision = accounts.SourceRevision, Medals = 5000, Roster = roster },
            RosterDeployments = [new("winner", 2, [])] };
        GameSession WithHistory(int used, int? limit) => initial with
        {
            World = initial.World with { Commanders = roster.Take(used).Select(c => rules.Resolve(c, "winner").Runtime).ToArray() },
            RosterDeployments = [new("winner", limit, roster.Take(used).Select(c => c.Id).ToArray())]
        };
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/roster_deployment_vectors.json")));
        int nativeCapacity = 0, fixedBypass = 0;
        foreach (var vector in vectors.RootElement.GetProperty("capacity").EnumerateArray())
        {
            int used = vector.GetProperty("used").GetInt32(), limit = vector.GetProperty("capacity").GetInt32();
            if (used > limit) continue;
            var state = WithHistory(used, limit);
            if (vector.GetProperty("expected").GetBoolean())
            {
                var result = RosterDeployment.Deploy(state, 0, "unit.2", roster[used].Id, rules).State;
                Check(result.RosterDeployments[0].CommanderIds.Count == used + 1, "native capacity accepted and history appended");
            }
            else Reject(() => RosterDeployment.Deploy(state, 0, "unit.2", roster[used].Id, rules), "session.roster_unavailable");
            nativeCapacity++;
        }
        foreach (var vector in vectors.RootElement.GetProperty("dispatch").EnumerateArray())
        {
            int used = vector.GetProperty("used").GetInt32(), limit = vector.GetProperty("capacity").GetInt32();
            if (used > limit || !vector.GetProperty("unit").GetBoolean()) continue;
            var state = WithHistory(used, limit);
            if (used == limit)
            {
                Check(vector.GetProperty("expected").GetProperty("events").GetArrayLength() == 1, "original action attaches even at capacity");
                Reject(() => RosterDeployment.Deploy(state, 0, "unit.2", roster[used].Id, rules), "session.roster_unavailable"); fixedBypass++;
            }
            else Check(RosterDeployment.Deploy(state, 0, "unit.2", roster[used].Id, rules).State.World.Units.Single(u => u.Id == "unit.2").CommanderId == roster[used].Id,
                "native action attachment in valid quota");
        }
        int duplicateHistories = 0;
        foreach (var vector in vectors.RootElement.GetProperty("history").EnumerateArray())
        {
            int[] ids = vector.GetProperty("ids").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            byte[] raw = new byte[428];
            for (int i = 0; i < 12; i++) BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(0xd4 + i * 2), (short)ids[i]);
            var mapping = ids.Where(i => i >= 0).Distinct().ToDictionary(i => i, i => "account." + i);
            if (ids.Where(i => i >= 0).Distinct().Count() != ids.Count(i => i >= 0))
            { Reject(() => LegacyRosterDeployment.Restore(raw, "winner", 12, mapping), "legacy.roster_history"); duplicateHistories++; }
            else
            {
                var restored = LegacyRosterDeployment.Restore(raw, "winner", 12, mapping);
                Check(restored.CommanderIds.SequenceEqual(ids.TakeWhile(i => i != -1).Select(i => mapping[i]))
                    && restored.CommanderIds.Count == vector.GetProperty("expected").GetProperty("used").GetInt32(), "native saved short history restored");
            }
        }
        Check(fixedBypass > 0 && duplicateHistories > 0, "native invalid histories and capacity bypass are exercised");
        var deployed = RosterDeployment.Deploy(initial, 0, "unit.2", roster[0].Id, rules).State;
        Check(deployed.Revision == 1 && deployed.World.Revision == 1 && deployed.Account.Revision == 0
            && deployed.Account.Medals == initial.Account.Medals && deployed.World.Factions.Select(f => f.Resources).SequenceEqual(initial.World.Factions.Select(f => f.Resources)), "free owned deployment touches no wallet or battlefield funds");
        Check(initial.World.Commanders.Count == 0 && initial.RosterDeployments[0].CommanderIds.Count == 0, "deployment input isolation");
        var lethal = deployed.World with { ActiveFaction = "loser", Units = deployed.World.Units.Select(u => u.Id == "unit.2"
            ? u with { Combatant = u.Combatant with { Health = 1, Evasion = 0 } }
            : u with { Cell = 7, Combatant = u.Combatant with { AttackBonus = 10000 } }).ToArray() };
        var killed = WorldBattle.Attack(lethal, lethal.Revision, "unit.1", "unit.2", new WorldBattleRules {
            CheckFactionSurvival = false, ResolveIgnition = false, RecalculateLocalMorale = false,
            InstallationDestructionProbability = 0, KillMoraleProbability = 0, SuppressionProbability = 0, KillDialogueProbability = 0 });
        Check(killed.State.Units.All(u => u.Id != "unit.2") && killed.State.Commanders.Single().Withdrawn,
            "actual combat death withdraws the deployed commander");
        var afterDeath = SessionActions.CommitWorld(deployed, deployed.Revision, killed).State;
        afterDeath = afterDeath with { World = afterDeath.World with { ActiveFaction = "winner", Units = afterDeath.World.Units.Append(initial.World.Units.Single(u => u.Id == "unit.2") with { Id = "replacement" }).ToArray() } };
        Reject(() => RosterDeployment.Deploy(afterDeath, afterDeath.Revision, "replacement", roster[0].Id, rules), "session.roster_unavailable");
        Reject(() => WorldCommanders.Assign(afterDeath.World, afterDeath.World.Revision, "replacement", roster[0].Id, rules.Assignment), "commander.unavailable");
        var deathLoaded = SessionSave.Decode(SessionSave.Encode(afterDeath), new(afterDeath.ContentRevision, afterDeath.World.ContentRevision, afterDeath.Account.ContentRevision));
        Reject(() => RosterDeployment.Deploy(deathLoaded, deathLoaded.Revision, "replacement", roster[0].Id, rules), "session.roster_unavailable");
        Check(RosterDeployment.Deploy(initial, 0, "unit.2", roster[0].Id, rules).State.World.Units.Single(u => u.Id == "unit.2").CommanderId == roster[0].Id,
            "death restriction ends with the scenario and leaves the account commander available for a new game");
        Reject(() => RosterDeployment.Deploy(deployed, 0, "unit.2", roster[1].Id, rules), "session.revision");
        var detached = SessionActions.CommitWorld(deployed, 1, WorldCommanders.Detach(deployed.World, 1, "unit.2", rules.Assignment)).State;
        Check(detached.RosterDeployments[0].CommanderIds.SequenceEqual(new[] { roster[0].Id }), "detach does not refund scenario deployment");
        Reject(() => RosterDeployment.Deploy(detached, 2, "unit.2", roster[0].Id, rules), "session.roster_unavailable");
        var nextDeployment = RosterDeployment.Deploy(detached, 2, "unit.2", roster[1].Id, rules).State;
        Check(nextDeployment.RosterDeployments[0].CommanderIds.Count == 2, "different owned commander uses next quota");
        var withdrawn = detached with { World = detached.World with { Commanders = detached.World.Commanders.Select(c => c with { Withdrawn = true }).ToArray() } };
        Reject(() => RosterDeployment.Deploy(withdrawn, 2, "unit.2", roster[0].Id, rules), "session.roster_unavailable");
        Reject(() => SessionActions.Inherit(detached, 2, roster[0].Id, roster[2].Id, rules.Account), "account.inheritance");
        Reject(() => SessionActions.Equip(detached, 2, roster[0].Id, "ew4.item.0", rules.Account), "session.commander_locked");
        Reject(() => SessionActions.Unequip(detached, 2, roster[0].Id, "ew4.item.0", rules.Account), "session.commander_locked");
        string gearId = runtime.Items.Values.First(i => i.Effect == Ew4Commander.Effect(9) && i.Value > 0 && (i.TargetCategory == null || i.TargetCategory == "infantry")).Id;
        var equipBase = initial with { Account = initial.Account with { Inventory = new Dictionary<string, long> { [gearId] = 1 } } };
        var equipped = SessionActions.Equip(equipBase, 0, roster[0].Id, gearId, rules.Account).State;
        Check(equipped.Account.Roster[0].Items.SequenceEqual(new[] { gearId })
            && SessionActions.Unequip(equipped, 1, roster[0].Id, gearId, rules.Account).State.Account.Roster[0].Items.Count == 0, "uncommitted roster gear changes use session transactions");
        var geared = RosterDeployment.Deploy(equipped, 1, "unit.2", roster[0].Id, rules).State;
        Check(geared.World.Units.Single(u => u.Id == "unit.2").Combatant.Profile.Movement > deployed.World.Units.Single(u => u.Id == "unit.2").Combatant.Profile.Movement,
            "owned equipment changes real battlefield movement");
        var full = WithHistory(100, 101);
        var large = RosterDeployment.Deploy(full, 0, "unit.2", roster[100].Id, rules).State;
        Check(large.World.Commanders.Count == 101 && large.RosterDeployments[0].CommanderIds.Count == 101, "no twelve-entry deployment ceiling");
        Check(RosterDeployment.Deploy(WithHistory(100, null), 0, "unit.2", roster[100].Id, rules).State.RosterDeployments[0].Limit == null, "explicit unlimited scenario policy");
        Reject(() => RosterDeployment.Deploy(initial with { RosterDeployments = [] }, 0, "unit.2", roster[0].Id, rules), "session.roster_unavailable");
        Reject(() => RosterDeployment.Deploy(initial with { RosterDeployments = [new("winner", 0, [])] }, 0, "unit.2", roster[0].Id, rules), "session.roster_unavailable");
        Reject(() => RosterDeployment.Deploy(initial, 0, "unit.1", roster[0].Id, rules), "session.roster_unavailable");
        Reject(() => RosterDeployment.Deploy(initial, 0, "unit.2", roster[0].Id, rules with { Assignment = rules.Assignment with { DeploymentBuilding = BuildingRequirement.Required } }), "session.roster_unavailable");
        Reject(() => RosterDeployment.Deploy(initial, 0, "unit.2", roster[0].Id, rules with { RankHealth = new Dictionary<int, int>() }), "session.roster_definition");
        var binding = new SessionContentBinding(large.ContentRevision, large.World.ContentRevision, large.Account.ContentRevision);
        byte[] bytes = SessionSave.Encode(large);
        Check(bytes.SequenceEqual(SessionSave.Encode(SessionSave.Decode(bytes, binding))), "all 101 histories roundtrip together with world/account");
        var restoredSession = SessionSave.Decode(SessionSave.Encode(detached), binding);
        Reject(() => RosterDeployment.Deploy(restoredSession, 2, "unit.2", roster[0].Id, rules), "session.roster_unavailable");
        byte[] Tamper(Action<JsonNode> action)
        {
            var node = JsonNode.Parse(bytes)!; action(node);
            var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string payload = node["payload"]!.ToJsonString(options);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteString("format", "ew4.rewrite.session"); writer.WriteNumber("version", (int)node["version"]!);
                writer.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
                writer.WritePropertyName("payload"); writer.WriteRawValue(payload); writer.WriteEndObject();
            }
            return stream.ToArray();
        }
        Reject(() => SessionSave.Decode(Tamper(n => n["version"] = 1), binding), "save.version");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!.AsObject().Remove("rosterDeployments")), binding), "save.required_field");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!["rosterDeployments"]![0]!.AsObject().Remove("limit")), binding), "save.required_field");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!["rosterDeployments"]![0]!["limit"] = 12), binding), "session.roster_history");
        Reject(() => SessionSave.Decode(Tamper(n => n["payload"]!["rosterDeployments"]![0]!["commanderIds"]![0] = "missing"), binding), "session.roster_history");
        var copy = large.Copy(); ((string[])copy.RosterDeployments[0].CommanderIds)[0] = "mutated";
        Check(large.RosterDeployments[0].CommanderIds[0] == roster[0].Id, "copy isolates history arrays");
        var firstBattle = LegacyBattle.Read(File.ReadAllBytes(Directory.GetFiles(assets, "*.btl").Order(StringComparer.Ordinal).First()));
        var source = firstBattle.Armies[0] with { Cell = 8, Unit = 1, Grade = 0, Commander = 1, Flag58 = 1, Veterancy = 0,
            Burning = 0, Behavior = 0, RestoreBehavior = 0, BehaviorExpiry = 0, ConstructionTurns = 0, Transport = 0, Acted = 0,
            ForceRecovery = 0, Flag5c = 1, State70 = -1, Morale = 0, MoraleDuration = 0, Movement = 3 };
        for (int i = 0; i < 200; i++)
        {
            var owned = equipped.Account.Roster[0] with { Learning = roster[0].Learning with { Rank = new(i % 15, i * 2), Title = new(i % 10, i),
                Abilities = roster[0].Learning.Abilities.ToDictionary(p => p.Key, p => p.Key == "movement" ? i % 6 : p.Value) }, Training = i % 5 };
            var template = rules.Resolve(owned, "winner");
            var profile = runtime.Unit("fra", 1, 0).Profile;
            int maximum = profile.MaxHealth + template.Runtime.RankHealthBonus;
            var record = source with { MaximumHealth = checked((short)maximum), Health = checked((short)Math.Max(1, maximum / 2)) };
            var assignment = new LegacyOwnedAssignment(1, "winner", owned, rules);
            var imported = LegacyUnitImporter.Import(record, "imported", "winner", "fra", initial.World.Cells[8], runtime, ownedAssignment: assignment);
            Check(imported.Commander == template.Runtime && imported.Definition!.Items.SequenceEqual(template.Definition.Items), "Flag58 resolves owned learning/training/equipment rather than default XML stats");
            Check(imported.Unit.Combatant.Profile.MaxHealth == maximum && imported.Unit.Combatant.Health == record.Health
                && imported.Unit.Support.TitleHealing == rules.Assignment.TitleHealing[i % 10] && imported.Unit.CommanderBase != null,
                "Flag58 restoration preserves learned rank health, title healing and unassigned basis");
            if (i == 0)
            {
                Reject(() => LegacyUnitImporter.Import(record, "x", "winner", "fra", initial.World.Cells[8], runtime, ownedAssignment: assignment with { LegacyId = 2 }), "legacy.roster_assignment");
                Reject(() => LegacyUnitImporter.Import(record, "x", "winner", "fra", initial.World.Cells[8], runtime, ownedAssignment: assignment with { Owner = "loser" }), "legacy.roster_assignment");
                Reject(() => LegacyUnitImporter.Import(record, "x", "winner", "fra", initial.World.Cells[8], runtime, commanderId: "wrong", ownedAssignment: assignment), "legacy.roster_assignment");
            }
        }
        var bare = source with { Commander = -1, MaximumHealth = 100, Health = 50 };
        Check(LegacyUnitImporter.Import(bare, "bare", "winner", "fra", initial.World.Cells[8], runtime).Commander == null, "detached native roster flag allowed without phantom assignment");
        var quotaSource = firstBattle.Factions[0] with { Count172 = 100 };
        Check(LegacyRosterDeployment.Start(quotaSource, "winner").Limit == 100 && LegacyRosterDeployment.Start(quotaSource, "winner", true).Limit == 12,
            "modern quota retains source value; native cap is an explicit compatibility option");
        Reject(() => LegacyRosterDeployment.Start(quotaSource with { Count172 = -1 }, "winner"), "legacy.roster_quota");
        File.WriteAllText(Path.Combine(root, "rewrite/reports/roster_deployment.json"), JsonSerializer.Serialize(new { checks, errors = 0,
            nativeCapacity, fixedBypass, duplicateHistories, ownedDefinitions = runtime.Commanders.Count, restoredOwnedSamples = 200,
            limitation = "Roster deployment/session save and explicit Flag58 component binding verified; full native account/saved-world migration and complete scenario Start/client remain pending." }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
