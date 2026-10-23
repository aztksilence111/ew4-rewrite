using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class LegacySkillParameterChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Original skill parameters: " + label); }
        void Reject(Action action, string label)
        {
            try { action(); } catch (Exception e) when (e is RuleViolation or JsonException) { checks++; return; }
            throw new InvalidOperationException("Original skill parameters accepted " + label);
        }
        string example = Path.Combine(root, "rewrite/mod-kit/examples/legacy-skill-parameters");
        const string parameterPath = "rewrite/definitions/legacy-skill-parameters.json";
        var files = Directory.EnumerateFiles(Path.Combine(example, "rewrite"), "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(example, p).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
        byte[] Read(string path) => files.TryGetValue(path, out var data) ? data : throw new FileNotFoundException(path);
        var defaults = ExtensionLoader.Load(Read);
        Check(defaults.LegacySkills!.Skills.Count == 29 && defaults.LegacySkills.Skills.Sum(p => p.Value.Count) == 36, "complete author template");
        foreach (var skill in LegacySkillParameters.Default.Skills)
            foreach (var parameter in skill.Value)
                Check(defaults.LegacySkills.Skills[skill.Key][parameter.Key] == parameter.Value, "template preserves " + skill.Key + "." + parameter.Key);
        var absent = ExtensionLoader.Load(_ => throw new FileNotFoundException());
        Check(!absent.Enabled && absent.LegacySkills == null, "absent extension keeps old fingerprint path");
        byte[] originalParameters = files[parameterPath];
        void Parameters(string json) => files[parameterPath] = Encoding.UTF8.GetBytes(json);
        foreach (string invalid in new[] {
            "{\"schema\":2,\"skills\":{}}", "{\"schema\":1,\"skills\":null}", "{\"schema\":1}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.0\":{}}}", "{\"schema\":1,\"skills\":{\"ew4.skill.40\":{\"bonus\":1}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.04\":{\"multiplier\":2}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":{\"percent\":10}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":{\"multiplier\":-1}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":{\"multiplier\":101}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":{\"multiplier\":null}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":null}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":{\"Multiplier\":2}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.31\":{\"percent\":101}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.31\":{\"percent\":1.5}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.6\":{\"bonus\":1001}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.33\":{\"gain\":-1}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.33\":{\"maximum\":101}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":{\"multiplier\":1,\"multiplier\":2}}}",
            "{\"schema\":1,\"skills\":{\"ew4.skill.4\":{},\"ew4.skill.4\":{}}}",
            "{\"schema\":1,\"skills\":{},\"typo\":1}" })
        {
            Parameters(invalid); Reject(() => ExtensionLoader.Load(Read), invalid);
        }
        var manifest = files["rewrite/extension.json"];
        files["rewrite/extension.json"] = Encoding.UTF8.GetBytes("{\"schema\":1,\"apiVersion\":1,\"legacySkillParameters\":\"" + parameterPath + "\"}");
        Reject(() => ExtensionLoader.Load(Read), "API 1");
        files["rewrite/extension.json"] = manifest;
        files.Remove(parameterPath); Reject(() => { try { ExtensionLoader.Load(Read); } catch (FileNotFoundException) { throw new RuleViolation("missing", "missing"); } }, "missing registered file");
        Parameters("{\"schema\":1,\"skills\":{\"ew4.skill.33\":{\"gain\":2}}}");
        var partial = ExtensionLoader.Load(Read).LegacySkills!;
        Check(partial.Gain(33) == 2 && partial.Maximum(33) == 5 && partial.Multiplier(4) == 1.5f, "partial fields and absent skills inherit defaults");
        var edits = LegacySkillParameters.Default.Skills.ToDictionary(p => p.Key, p => p.Value.ToDictionary(v => v.Key,
            v => v.Key switch { "multiplier" => 2.25, "bonus" => 20d, "percent" => 25d, "gain" => 2d, _ => 7d }));
        files[parameterPath] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, skills = edits });
        var tuned = ExtensionLoader.Load(Read);
        var parameters = tuned.LegacySkills!;
        Check(tuned.Revision != defaults.Revision, "parameter file contributes to rule revision");
        edits["ew4.skill.4"]["multiplier"] = 99;
        Check(parameters.Multiplier(4) == 2.25f && LegacySkillParameters.Default.Multiplier(4) == 1.5f, "immutable parameters and no global leakage");
        Check(defaults.LegacySkills.Multiplier(4) == 1.5f, "separate loaded mods remain isolated");

        Combatant Actor(string category = "infantry", params int[] skills) => new() { Health = 1000,
            Skills = skills.Select(Ew4Combat.Skill).ToArray(), Profile = new() { Id = "test." + category, Category = category,
                MaxHealth = 1000, Movement = 6, MinimumAttack = 10, MaximumAttack = 10, AttackBands = [new(0, 1)],
                DamageMultipliers = new Dictionary<string, float> { ["infantry"] = 1, ["cavalry"] = 1, ["artillery"] = 1, ["naval"] = 1, ["fort"] = 1 } } };
        CombatResult Battle(Combatant a, Combatant d, LegacySkillParameters? configuration = null, ulong seed = 123) =>
            Ew4Combat.Calculate(a, d, 2, new(seed), new() { LegacySkills = configuration });
        Check(Battle(Actor("infantry", 4), Actor("fort"), parameters).DefenderDamage == 22, "spy multiplication and native truncation");
        Check(Battle(Actor(), Actor("naval", 16), parameters).DefenderDamage == 22, "incoming helmsman multiplier");
        foreach (var (id, category, lower) in new[] { (7, "artillery", true), (8, "artillery", false), (10, "cavalry", false),
            (11, "cavalry", true), (12, "infantry", false), (13, "infantry", true) })
        {
            var maximum = LegacySkillParameters.FromOverrides(new Dictionary<string, Dictionary<string, double>> { [Ew4Combat.Skill(id)] = new() { ["bonus"] = 20 },
                [Ew4Combat.Skill(31)] = new() { ["percent"] = 100 } });
            Check(Battle(Actor(category, id, 31), Actor(), maximum).DefenderDamage == 30, "attack bounds in actual damage " + id);
            var effect = Ew4Commander.Resolve(new() { Skills = [Ew4Combat.Skill(id)] }, category, parameters);
            Check(lower ? effect.MinimumAttackBonus == 20 && effect.MaximumAttackBonus == 0
                : effect.MaximumAttackBonus == 20 && effect.MinimumAttackBonus == 0, "commander displayed attack bound " + id);
        }
        for (ulong seed = 0; seed < 64; seed++)
        {
            var random = new Ew4Random(seed);
            bool offensive = random.Next(100) < 25, defensive = random.Next(100) < 25;
            var actual = Battle(Actor("infantry", 31), Actor("infantry", 32), parameters, seed);
            Check(actual.OffensiveProc == offensive && actual.DefensiveProc == defensive && actual.RandomState == random.State
                && actual.DefenderDamage == (defensive ? 1 : 10), "probability threshold and RNG order " + seed);
        }
        foreach (int percent in new[] { 0, 100 })
        {
            var boundary = LegacySkillParameters.FromOverrides(new Dictionary<string, Dictionary<string, double>> { [Ew4Combat.Skill(31)] = new() { ["percent"] = percent },
                [Ew4Combat.Skill(32)] = new() { ["percent"] = percent } });
            var result = Battle(Actor("infantry", 31), Actor("infantry", 32), boundary);
            Check(result.OffensiveProc == (percent == 100) && result.DefensiveProc == (percent == 100), "probability boundary " + percent);
        }
        foreach (int id in new[] { 6, 18 })
        {
            string category = id == 6 ? "artillery" : "naval";
            var commander = new Commander { Movement = 3, Skills = [Ew4Combat.Skill(id)] };
            Check(Ew4Commander.Attach(Actor(category), commander, parameters).Profile.Movement == (id == 6 ? 29 : 26), "movement skill " + id);
            Check(Ew4Commander.Attach(Actor("infantry"), commander, parameters).Profile.Movement == 9, "movement category gate " + id);
        }
        foreach (int id in new[] { 19, 20, 21 })
        {
            Check(Ew4Experience.CommanderGain(101, id == 19, [Ew4Combat.Skill(id)], skillParameters: parameters) == 227, "experience multiplier " + id);
            Check(Ew4Experience.CommanderGain(101, id == 19, [Ew4Combat.Skill(id)], 150, skillParameters: parameters) == 151, "equipment precedence " + id);
            Check(Ew4Experience.CommanderGain(101, id == 19, [Ew4Combat.Skill(id)], training: true, skillParameters: parameters) == 101, "training bypass " + id);
        }

        byte[] BaseRead(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        byte[] ConfiguredRead(string path) => files.TryGetValue(path, out var data) ? data : BaseRead(path);
        var original = new ClientContent(BaseRead, ["conquest1.btl"]);
        var content = new ClientContent(ConfiguredRead, ["conquest1.btl"]);
        Dictionary<string, string> Descriptions(ClientContent source)
        {
            var host = new ClientGame(source, Path.Combine(root, "rewrite/reports/skill-descriptions-20261023", Guid.NewGuid().ToString("N")));
            JsonElement Call(string route, object? command = null)
            {
                using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
                Check(response.RootElement.GetProperty("ok").GetBoolean(), "description API " + route);
                return response.RootElement.GetProperty("data").Clone();
            }
            var skills = Call("headquarters").GetProperty("definitions").EnumerateArray().SelectMany(d => d.GetProperty("skills").EnumerateArray())
                .GroupBy(s => s.GetProperty("id").GetString()!).ToDictionary(g => g.Key, g => g.First().GetProperty("description").GetString()!);
            var battle = Call("start", new { scenario = "conquest1", country = "fra" });
            Check(battle.GetProperty("units").EnumerateArray().SelectMany(u => u.GetProperty("skills").EnumerateArray())
                .All(s => skills[s.GetProperty("id").GetString()!] == s.GetProperty("description").GetString()), "battle and headquarters descriptions agree");
            return skills;
        }
        var defaultDescriptions = Descriptions(original);
        var configuredDescriptions = Descriptions(content);
        Check(defaultDescriptions.Count == 40 && defaultDescriptions.Values.All(s => !string.IsNullOrWhiteSpace(s)), "all original skills described");
        Check(defaultDescriptions[Ew4Combat.Skill(0)] == original.Label("desc_skill_01"), "flag qualification retained");
        Check(Enumerable.Range(1, 39).All(id => defaultDescriptions[Ew4Combat.Skill(id)] != original.Label("desc_skill_" + (id + 1).ToString("00"))), "remaining descriptions explain numeric effects or mechanism");
        foreach (string id in parameters.Skills.Keys)
            Check(defaultDescriptions[id] != configuredDescriptions[id], "description follows configured parameters " + id);
        Check(defaultDescriptions[Ew4Combat.Skill(14)].Contains("全部回避减伤") && defaultDescriptions[Ew4Combat.Skill(29)].Contains("建筑提供的回避减伤"), "horn and siege explain different evasion scopes");
        Check(defaultDescriptions[Ew4Combat.Skill(31)].Contains("10%") && configuredDescriptions[Ew4Combat.Skill(31)].Contains("25%"), "displayed probability follows configuration");
        Check(defaultDescriptions[Ew4Combat.Skill(4)].Contains("50%") && configuredDescriptions[Ew4Combat.Skill(4)].Contains("125%"), "displayed multiplier follows configuration");
        Check(defaultDescriptions[Ew4Combat.Skill(30)].Contains("40%") && configuredDescriptions[Ew4Combat.Skill(30)].Contains("75%"), "displayed savings use the complement of cost percent");
        Check(configuredDescriptions[Ew4Combat.Skill(33)].Contains("提高2级") && configuredDescriptions[Ew4Combat.Skill(33)].Contains("至7级"), "displayed teaching gain and cap follow configuration");
        var customStrings = (IDictionary<string, string>)content.Strings;
        string oldHorn = customStrings["desc_skill_15"];
        customStrings["desc_skill_15"] = "Mod自定义军号说明";
        Check(Descriptions(content)[Ew4Combat.Skill(14)] == "Mod自定义军号说明", "custom legacy wording retains priority");
        customStrings["desc_skill_15"] = oldHorn;
        var start = content.Start("conquest1", "fra");
        Check(start.Rules.Battle.Combat.LegacySkills == content.Extensions.LegacySkills && start.Rules.Commanders.LegacySkills == content.Extensions.LegacySkills
            && start.Rules.Roster.Assignment.LegacySkills == content.Extensions.LegacySkills && start.Rules.Tavern.Assignment.LegacySkills == content.Extensions.LegacySkills,
            "scenario battle, assignment, roster and tavern receive the same parameters");
        Check(start.Rules.Production.Values.All(p => p.Transport!.Purchase.MoneyPercentWithSkill == 25 && p.BuildingUpgrades!.DiscountPercent == 25
            && p.Installations.Values.All(i => i.Purchase.MoneyPercentWithSkill == 25)), "AI uses configured purchase rules");
        var originalStart = original.Start("conquest1", "fra");
        Check(start.Session.ContentRevision != originalStart.Session.ContentRevision && start.Session.Account.ContentRevision != originalStart.Session.Account.ContentRevision,
            "custom values isolate sessions and accounts");
        var encoded = SessionSave.Encode(start.Session);
        var restarted = new ClientContent(ConfiguredRead, ["conquest1.btl"]).Start("conquest1", "fra");
        var restored = SessionSave.Decode(encoded, new(restarted.Session.ContentRevision, restarted.Session.World.ContentRevision, restarted.Session.Account.ContentRevision));
        Check(SessionSave.Encode(restored).SequenceEqual(encoded), "custom session exact save/reload");
        Reject(() => SessionSave.Decode(encoded, new(originalStart.Session.ContentRevision, originalStart.Session.World.ContentRevision, originalStart.Session.Account.ContentRevision)), "loading custom save under default rules");

        var world = WorldChecks.Example();
        var commanderDefinition = new Commander { Id = "test.commander", Skills = [Ew4Combat.Skill(6), Ew4Combat.Skill(23), Ew4Combat.Skill(24)], Movement = 3 };
        world = world with { Commanders = [new() { Id = commanderDefinition.Id, Owner = "winner" }],
            Units = world.Units.Select(u => u.Id == "unit.2" ? u with { Combatant = Actor("artillery"), RemainingMovement = 6 } : u).ToArray(),
            Factions = world.Factions.Select(f => f with { Resources = new(10000, 10000, 10000) }).ToArray() };
        var assignment = start.Rules.Commanders with { Definitions = new Dictionary<string, Commander> { [commanderDefinition.Id] = commanderDefinition }, AllowReplacement = true };
        var assigned = WorldCommanders.Assign(world, world.Revision, "unit.2", commanderDefinition.Id, assignment).State;
        var unit = assigned.Units.Single(u => u.Id == "unit.2");
        Check(unit.Combatant.Profile.Movement == 29 && unit.Support.IncomeMultiplier == 2.25f, "actual assignment applies movement and economy");
        var detached = WorldCommanders.Detach(assigned, assigned.Revision, unit.Id, assignment).State;
        var reassigned = WorldCommanders.Assign(detached, detached.Revision, unit.Id, commanderDefinition.Id, assignment).State.Units.Single(u => u.Id == unit.Id);
        Check(reassigned.Combatant.Profile.Movement == 29 && reassigned.Support.IncomeMultiplier == 2.25f, "detach and reassign do not stack parameters");
        foreach (int id in new[] { 23, 24 })
        {
            var economic = Ew4Commander.Resolve(new() { Skills = [Ew4Combat.Skill(id)] }, "infantry", parameters);
            Check(EconomyProduction.Calculate([new(new(100, 20, 40), economic.EconomicMultiplier)], 1, new(0, 0, 0)) == new Resources(225, 45, 90), "actual income calculation " + id);
        }
        foreach (var installation in start.Rules.Installations.Values)
        {
            unit = unit with { Acted = false, RemainingMovement = 1,
                CommanderBase = unit.CommanderBase! with { Combatant = unit.CommanderBase.Combatant with {
                    Profile = unit.CommanderBase.Combatant.Profile with { Category = "infantry" } } },
                Combatant = unit.Combatant with { Profile = unit.Combatant.Profile with { Category = "infantry" }, Skills = [installation.Purchase.DiscountSkill!] } };
            var before = assigned with { Units = assigned.Units.Select(u => u.Id == unit.Id ? unit : u).ToArray() };
            var after = WorldSupportActions.BuildInstallation(before, before.Revision, unit.Id, installation).State;
            Check(before.Factions.Single(f => f.Id == unit.Owner).Resources.Money - after.Factions.Single(f => f.Id == unit.Owner).Resources.Money
                == installation.Purchase.Cost.Money * 25 / 100, "installation actual payment " + installation.Kind);
        }
        unit = unit with { Transport = false, Combatant = unit.Combatant with { Skills = [Ew4Combat.Skill(26)] } };
        var transportWorld = assigned with { Units = assigned.Units.Select(u => u.Id == unit.Id ? unit : u).ToArray() };
        Check(WorldSupportActions.TransportCost(transportWorld, unit.Id, start.Rules.Transport).Money == start.Rules.Transport.Purchase.Cost.Money * 25 / 100, "transport quote");
        var transported = WorldSupportActions.BuyTransport(transportWorld, transportWorld.Revision, unit.Id, start.Rules.Transport).State;
        Check(transported.Units.Single(u => u.Id == unit.Id).Transport && transported.Factions.Single(f => f.Id == unit.Owner).Resources.Money
            == 10000 - start.Rules.Transport.Purchase.Cost.Money * 25 / 100, "transport actual payment");
        var step = start.Rules.Upgrades.Steps.First(s => s.Kind == "industry");
        unit = unit with { Combatant = unit.Combatant with { Skills = [Ew4Combat.Skill(30)] } };
        var upgradeWorld = assigned with { Units = assigned.Units.Select(u => u.Id == unit.Id ? unit : u).ToArray(),
            Cells = assigned.Cells.Select((c, i) => i == unit.Cell ? c with { BuildingKind = step.Kind, BuildingLevel = step.FromLevel } : c).ToArray() };
        var quote = WorldImprovements.BuildingUpgradeCost(upgradeWorld, unit.Cell, start.Rules.Upgrades);
        Check(quote == new Resources(step.Cost.Money * 25 / 100, step.Cost.Industry * 25 / 100, 0), "architecture quote discounts money and industry");
        var upgraded = WorldImprovements.UpgradeBuilding(upgradeWorld, upgradeWorld.Revision, unit.Cell, start.Rules.Upgrades).State;
        Check(upgraded.Factions.Single(f => f.Id == unit.Owner).Resources == new Resources(10000 - quote.Money, 10000 - quote.Industry, 10000), "architecture actual payment");
        var teaching = content.Catalogs.Accounts.Inheritance;
        for (int id = 33; id <= 39; id++)
        {
            string ability = LegacyCommanderAccounts.Abilities[id - 33];
            var receiver = new CommanderLearningState(new(0, 0), new(0, 0), new Dictionary<string, int> { [ability] = 6 });
            var result = CommanderInheritance.Apply(receiver, receiver, [Ew4Combat.Skill(id)], teaching);
            Check(result.Abilities[ability] == 7, "teaching gain and cap " + id);
            receiver = receiver with { Abilities = new Dictionary<string, int> { [ability] = 3 } };
            Check(CommanderInheritance.Apply(receiver, receiver, [Ew4Combat.Skill(id)], teaching).Abilities[ability] == 5, "teaching applies the configured increment " + id);
            receiver = receiver with { Abilities = new Dictionary<string, int> { [ability] = 9 } };
            Check(CommanderInheritance.Apply(receiver, receiver, [Ew4Combat.Skill(id)], teaching).Abilities[ability] == 9, "teaching never lowers existing ability " + id);
        }

        files["rewrite/extension.json"] = Encoding.UTF8.GetBytes("{\"schema\":1,\"apiVersion\":2,\"legacySkillParameters\":\"" + parameterPath
            + "\",\"skills\":[\"rewrite/replacements.json\"]}");
        files["rewrite/replacements.json"] = JsonSerializer.SerializeToUtf8Bytes(new[] { new ExtensionSkill { Id = Ew4Combat.Skill(4),
            Effects = [new() { Event = "combat.modify_damage", Operation = "damage_multiplier", Value = 3 }] },
            new ExtensionSkill { Id = Ew4Combat.Skill(6) }, new ExtensionSkill { Id = Ew4Combat.Skill(19) }, new ExtensionSkill { Id = Ew4Combat.Skill(33) } }, ExtensionLoader.Json);
        var replacement = new ClientContent(ConfiguredRead, ["conquest1.btl"]);
        Check(Descriptions(replacement)[Ew4Combat.Skill(4)] == "", "full replacement uses its own description without original numeric text");
        var overridden = replacement.Catalogs.Runtime.Profiles.Values.First().Profile.LegacySkillOverrides;
        var a = Actor("infantry", 4) with { Profile = Actor().Profile with { LegacySkillOverrides = overridden } };
        Check(Ew4Combat.Calculate(a, Actor("fort"), 2, new(123), new() { LegacySkills = parameters, Extensions = replacement.Extensions.Bus }).DefenderDamage == 30,
            "same-ID replacement uses extension only, without spy multiplier");
        Check(Ew4Commander.Resolve(commanderDefinition with { LegacySkillOverrides = overridden }, "artillery", parameters).MovementBonus == 3, "replacement suppresses original movement bonus");
        var noExperienceSkill = Ew4Commander.Attach(Actor(), new() { Skills = [Ew4Combat.Skill(19)], LegacySkillOverrides = overridden }, parameters);
        Check(Ew4Experience.CommanderGain(101, true, noExperienceSkill.Skills, skillParameters: parameters) == 101, "replacement suppresses original experience multiplier");
        var accountRules = replacement.Catalogs.Accounts.Rules();
        var donor = replacement.Catalogs.Accounts.FromDefinition(1, "donor");
        var receiverOwned = replacement.Catalogs.Accounts.FromDefinition(2, "receiver");
        var definitions = accountRules.Definitions.ToDictionary(p => p.Key, p => p.Value);
        definitions[donor.DefinitionId] = definitions[donor.DefinitionId] with { Skills = [Ew4Combat.Skill(33)] };
        var account = new PlayerAccount { Id = "test", ContentRevision = "test", Roster = [donor, receiverOwned] };
        var inherited = AccountCommanders.Inherit(account, account.Revision, donor.Id, receiverOwned.Id, [], accountRules with { Definitions = definitions });
        Check(inherited.Roster.Single().Learning.Abilities["infantry"] == receiverOwned.Learning.Abilities["infantry"], "replacement suppresses original teaching gain");

        files[parameterPath] = originalParameters;
        string report = Path.Combine(root, "rewrite/reports/legacy-skill-parameters-20261021"); Directory.CreateDirectory(report);
        File.WriteAllText(Path.Combine(report, "checks.json"), JsonSerializer.Serialize(new { checks, errors = 0,
            sessionHash = Convert.ToHexStringLower(SHA256.HashData(encoded)), parameters = 36, skills = 29 }));
        return checks;
    }
}
