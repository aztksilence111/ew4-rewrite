using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;

internal static class LegacyCombatChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Legacy combat: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + ": " + e.Code); return; }
            throw new InvalidOperationException("Expected combat rejection: " + code);
        }
        string assets = Path.Combine(root, "apk_work/original/assets");
        string tables = File.ReadAllText(Path.Combine(root, "reverse/reports/runtime_rule_tables.json"));
        string terrain = File.ReadAllText(Path.Combine(assets, "def_terraintype.xml"));
        string installations = File.ReadAllText(Path.Combine(assets, "def_installation.xml"));
        var buildings = LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml")));
        var rules = LegacyCombatCatalog.Read(tables, terrain, installations, buildings);
        string runtimeJson = Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json"));
        var runtime = LegacyRuntimeCatalog.FromJson(runtimeJson);
        rules.VerifyNativeSource(File.ReadAllBytes(Path.Combine(root, "apk_work/original/lib/arm64-v8a/lib64ew4_Sagiri.so")));
        Check(rules.NativeSourceHash.Length == 64, "native rules bind preserved source hash");
        Reject(() => rules.VerifyNativeSource([1, 2, 3]), "legacy.native_source_changed");
        Check(rules.Veterancy.Count == 6 && rules.RankProgression.Levels.Count == 15 && rules.TitleProgression.Levels.Count == 10, "actual native track lengths");
        var tableRoot = JsonNode.Parse(tables)!;
        tableRoot["schema_version"] = 99;
        Reject(() => LegacyCombatCatalog.Read(tableRoot.ToJsonString(), terrain, installations, buildings), "legacy.combat_catalog");
        tableRoot["schema_version"] = 1; tableRoot["unit_veterancy_rows"]![1]![3] = 0;
        Reject(() => LegacyCombatCatalog.Read(tableRoot.ToJsonString(), terrain, installations, buildings), "legacy.combat_catalog");
        var oldRuntime = JsonNode.Parse(runtimeJson)!; oldRuntime["schema_version"] = 1;
        Reject(() => LegacyRuntimeCatalog.FromJson(oldRuntime.ToJsonString()), "legacy.catalog_version");
        Check(rules.SourceRevision == LegacyCombatCatalog.Read(tables, terrain, installations, buildings).SourceRevision
            && rules.SourceRevision != LegacyCombatCatalog.Read(tables, terrain.Replace("penalty_cavalry=\"20\"", "penalty_cavalry=\"21\""), installations, buildings).SourceRevision,
            "combat table and defense source digests deterministic and sensitive");

        foreach (var country in XDocument.Load(Path.Combine(assets, "def_army.xml")).Root!.Elements("country"))
            foreach (var row in country.Elements("army"))
            {
                int id = LegacyRuntimeCatalog.UnitIndex((string)row.Attribute("name")!), grade = (int)row.Attribute("grade")!;
                var profile = runtime.Unit((string)country.Attribute("name")!, id, grade).Profile;
                string weapon = (string)row.Attribute("weapon")!;
                Check(profile.KillExperience == grade + 1, "all imported profile kill rewards use actual grade");
                Check(profile.IgnitionNumerator == (weapon == "cannon" ? 1 : weapon == "rocket" ? 5 : 0)
                    && profile.IgnitionDenominator == (weapon == "rocket" ? 2 : 1), "actual imported weapon fire scale");
            }
        foreach (string key in new[] { "commander_rank_health_bonus", "commander_nobility_healing" })
        {
            var actual = key.Contains("health", StringComparison.Ordinal) ? rules.RankHealth : rules.TitleHealing;
            Check(actual.OrderBy(p => int.Parse(p.Key, CultureInfo.InvariantCulture)).Select(p => p.Value)
                .SequenceEqual(runtime.NativeTables.GetProperty(key).EnumerateArray().Select(v => v.GetInt32())), "runtime attachment tables match native combat progression");
        }
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/runtime_catalog_vectors.json")));
        foreach (var v in vectors.RootElement.GetProperty("xp").EnumerateArray())
        {
            var track = rules.UnitProgression;
            if (v.GetProperty("attached").GetBoolean() || v.GetProperty("naval").GetBoolean())
                track = track with { Levels = track.Levels.Select(l => l.ExperienceToNext is not long cost ? l : l with
                    { ExperienceToNext = (v.GetProperty("attached").GetBoolean() ? cost * 3 / 2 : cost) * (v.GetProperty("naval").GetBoolean() ? 2 : 1) }).ToArray() };
            var actual = Progression.Award(new(v.GetProperty("level").GetInt32(), v.GetProperty("experience").GetInt64()), v.GetProperty("gain").GetInt64(), track);
            var expected = v.GetProperty("expected");
            Check(actual.State.Level == expected[0].GetInt32() && actual.State.Experience == expected[1].GetInt64()
                && Math.Min(v.GetProperty("maximum").GetInt64(), v.GetProperty("health").GetInt64() + actual.Healing) == expected[2].GetInt64()
                && actual.LevelsGained == expected[3].GetInt32(), "actual native progression and healing table chain");
        }
        foreach (var v in vectors.RootElement.GetProperty("worth").EnumerateArray())
        {
            var profile = runtime.Unit("fra", 0, v.GetProperty("grade").GetInt32()).Profile;
            Check(profile.KillExperience * (v.GetProperty("attached").GetBoolean() ? 2 : 1) == v.GetProperty("expected").GetInt32(), "original worth query applied to imported profile");
        }
        var fixture = WorldChecks.Example();
        var imported = runtime.Unit("fra", 1, 2).Profile;
        foreach (var row in XDocument.Parse(terrain).Root!.Elements("terraintype"))
            foreach (string category in new[] { "infantry", "cavalry", "artillery", "naval", "fort" })
            {
                string kind = (string)row.Attribute("type")!;
                var world = fixture with { Cells = fixture.Cells.Select((c, i) => i == 8 ? c with { DefenseTerrain = kind } : c).ToArray() };
                var context = CombatContext.Resolve(world, world.Units[1], category, rules.Context);
                string column = category is "naval" or "fort" ? "artillery" : category;
                Check(context.Evasion == (int)row.Attribute("penalty_" + column)! / 100f, "actual terrain defense category lookup");
            }
        foreach (var row in XDocument.Parse(installations).Root!.Elements("installation"))
            foreach (string category in new[] { "infantry", "cavalry", "artillery", "naval", "fort" })
            {
                var world = fixture with { Cells = fixture.Cells.Select((c, i) => i == 8 ? c with { InstallationKind = (string)row.Attribute("type")! } : c).ToArray() };
                var context = CombatContext.Resolve(world, world.Units[1], category, rules.Context);
                string column = category is "naval" or "fort" ? "artillery" : category;
                Check(context.Evasion == (int)row.Attribute("penalty_" + column)! / 100f, "actual installation defense category lookup");
            }
        for (int level = 0; level < rules.Veterancy.Count; level++)
        {
            var unit = fixture.Units[1] with { Veterancy = level.ToString(CultureInfo.InvariantCulture), Acted = false,
                Combatant = new() { Profile = imported, Health = 1 } };
            var world = fixture with { Units = [unit] };
            Check(CombatContext.Resolve(world, unit, "infantry", rules.Context).Armor == rules.Veterancy[level].Armor, "actual native armor applies to imported unit");
            var turns = rules.TurnRules(new()); turns.Validate();
            var ended = WorldTurns.EndTurn(world, world.Revision, turns).State;
            Check(ended.Units.Single().Combatant.Health == 1 + rules.Veterancy[level].TurnHealing, "actual end-turn healing applies to imported unit");
        }
        var battleRules = rules.BattleRules(); battleRules.Validate();
        int nativeChainChecks = BattleChainChecks.Run(root, importedCatalog: true);
        checks += nativeChainChecks;
        int unitStates = 0;
        foreach (var file in Directory.GetFiles(assets, "*.btl"))
            foreach (var army in LegacyBattle.Read(File.ReadAllBytes(file)).Armies)
            {
                Check(army.Veterancy >= 0 && army.Veterancy < rules.Veterancy.Count, "every real scenario unit has a valid veterancy row");
                unitStates++;
            }
        var report = new { profiles = runtime.Profiles.Count, unitStates, nativeChainChecks, checks,
            nativeSourceHash = rules.NativeSourceHash, rules.SourceRevision,
            limitations = new[] { "Combat/progression and defense components; no complete Start/client/APK.",
                "Native joint samples use supplied damage and generated commanders; eligibility, survival/map and display services are injected; not whole-scenario play tests." } };
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_combat.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
