using System.Text.Json;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class LegacyArmyImportChecks
{
    private static int checks;
    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new InvalidOperationException("Army import: " + label); }
    private static void Reject(Action action, string code)
    {
        try { action(); }
        catch (RuleViolation error) { Check(error.Code == code, code + " actual=" + error.Code); return; }
        throw new InvalidOperationException("Expected army import rejection: " + code);
    }
    public static int Run(string root)
    {
        checks = 0;
        string assets = Path.Combine(root, "apk_work/original/assets");
        var catalog = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        catalog.VerifySources(name => File.ReadAllBytes(Path.Combine(assets, name)));
        Check(catalog.Profiles.Count == 369 && catalog.Variants.Count == 33 && catalog.Commanders.Count == 588 && catalog.Items.Count == 228, "runtime catalog inventory");
        Reject(() => catalog.VerifySources(_ => []), "legacy.source_changed");
        Check(catalog.Unit("pie", 0, 0) == catalog.Unit("others", 0, 0), "original country fallback");
        Reject(() => catalog.Unit("fra", 22, 0), "legacy.unit_definition");
        Reject(() => catalog.Unit("fra", 0, 3), "legacy.unit_definition");
        Check(catalog.SerializedUnit("fra", 11, 3) == catalog.Unit("fra", 12, 0), "serialized grade uses original flat three-slot table");
        Reject(() => catalog.SerializedUnit("fra", 21, 3), "legacy.unit_definition");
        var overHealth = new LegacyArmy(1399, 4, 2, 171, 6, 0, 3, 7, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 1, -1, 0, 0, 0, 0);
        var restoredOverHealth = LegacyArmyRestoration.Calculate(overHealth, 160, 8, 150, 0, 0, true, false, false);
        Check(restoredOverHealth.Health == 310 && restoredOverHealth.MaximumHealth == 310, "native load rebases excessive serialized health then caps to new maximum");
        Check(LegacyArmyRestoration.Calculate(overHealth with { Health = 6, Morale = 3 }, 160, 8, 0, 0, 6, false, false, false).Morale == 0,
            "unusual baseline morale and shortage follow native penalty formula");
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/world_import_vectors.json")));
        int rescued = 0, preservedMovement = 0;
        foreach (var vector in vectors.RootElement.GetProperty("army").EnumerateArray())
        {
            var source = LegacyArmy.Read(vector.GetProperty("raw").EnumerateArray().Select(v => v.GetByte()).ToArray());
            var input = vector.GetProperty("inputs"); var wanted = vector.GetProperty("expected");
            int Int(string key) => input.GetProperty(key).GetInt32();
            bool Bool(string key) => input.GetProperty(key).GetBoolean();
            var compatibility = LegacyArmyRestoration.Calculate(source, Int("base_health"), Int("base_movement"), Int("health_bonus"), Int("movement_bonus"),
                Int("shortage"), wanted.GetProperty("attached").GetBoolean(), Bool("leadership"), Bool("logistics"), legacy: true);
            Check(compatibility.Health == wanted.GetProperty("health").GetInt32() && compatibility.MaximumHealth == wanted.GetProperty("maximum").GetInt32()
                && compatibility.Movement == wanted.GetProperty("movement").GetInt32() && compatibility.Morale == wanted.GetProperty("morale").GetInt32(), "native load/attach/morale chain");
            Check(source.Behavior == wanted.GetProperty("behavior").GetByte() && source.BehaviorExpiry == wanted.GetProperty("expiry").GetInt16()
                && source.RestoreBehavior == wanted.GetProperty("restore_behavior").GetByte() && source.State70 == wanted.GetProperty("state70").GetSByte()
                && source.Burning == wanted.GetProperty("burning").GetSByte() && (source.Acted != 0) == wanted.GetProperty("acted").GetBoolean()
                && (source.Transport != 0) == wanted.GetProperty("transport").GetBoolean(), "native unit field decode");
            var modern = LegacyArmyRestoration.Calculate(source, Int("base_health"), Int("base_movement"), Int("health_bonus"), Int("movement_bonus"),
                Int("shortage"), wanted.GetProperty("attached").GetBoolean(), Bool("leadership"), Bool("logistics"), immobile: Int("category") == 4);
            Check(modern.Health > 0 && modern.Health <= modern.MaximumHealth && modern.Movement <= modern.MaximumMovement
                && modern.Movement <= Math.Max(0, (int)source.Movement), "modern loading preserves spent movement and living health");
            if (compatibility.Health == 0) { Check(modern.Health >= 1, "original zero-health load corrected"); rescued++; }
            if (modern.Movement < compatibility.Movement) preservedMovement++;
        }
        Check(rescued > 0 && preservedMovement > 0, "modernization has exercised positive cases");
        var terrainRules = LegacyTerrainRules.FromXml(File.ReadAllText(Path.Combine(assets, "def_mapterrain.xml")), File.ReadAllText(Path.Combine(assets, "def_terraintype.xml")));
        var maps = new Dictionary<int, LegacyTerrainMap> { [1] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "europe.bin"))),
            [2] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "america.bin"))) };
        var buildings = LegacyBuildingCatalog.Kinds;
        var buildingRules = LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml")));
        var installations = new Dictionary<int, string> { [0] = "trench", [1] = "fence", [2] = "bunker" };
        var names = XDocument.Load(Path.Combine(assets, "def_areaname.xml")).Root!.Elements("areaname").ToDictionary(e => (int)e.Attribute("id")!, e => (string)e.Attribute("name")!);
        int files = 0, units = 0, assigned = 0;
        var pending = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<object>();
        WorldState? sample = null; LegacyUnitBinding? assignedSample = null; LegacyArmy? sourceSample = null; string? sampleVariant = null;
        foreach (string file in Directory.GetFiles(assets, "*.btl").Order(StringComparer.Ordinal))
        {
            var battle = LegacyBattle.Read(File.ReadAllBytes(file));
            var ids = battle.Factions.Select((f, i) => $"legacy.{i}.{f.CountryId}").ToArray();
            var importedMap = LegacyScenarioMap.Import(maps[battle.MapId], battle, terrainRules, ids, buildings, installations, names, buildingRules);
            var bindings = new List<LegacyUnitBinding>();
            foreach (var pair in battle.Armies.Select((a, i) => (a, i)))
            {
                try
                {
                    string owner = importedMap.Cells[pair.a.Cell].Owner!;
                    int faction = Array.IndexOf(ids, owner);
                    var binding = LegacyUnitImporter.Import(pair.a, "legacy.unit." + pair.i, owner, battle.Factions[faction].Variant,
                        importedMap.Cells[pair.a.Cell], catalog, commanderId: $"legacy.unit.{pair.i}.commander.{pair.a.Commander}");
                    bindings.Add(binding); units++;
                    Check(binding.Unit.Combatant.Profile.Id == catalog.Unit(battle.Factions[faction].Variant, pair.a.Unit, pair.a.Grade).Profile.Id,
                        "unit profile resolved through occupying faction variant");
                    Check(binding.Unit.Experience == pair.a.Experience && binding.Unit.Veterancy == pair.a.Veterancy.ToString()
                        && binding.Unit.Acted == (pair.a.Acted != 0) && binding.Unit.Cell == pair.a.Cell, "army state retained");
                    foreach (string field in binding.UnmappedGameplay) pending[field] = pending.GetValueOrDefault(field) + 1;
                    if (binding.Commander != null)
                    {
                        assigned++;
                        Check(binding.Unit.CommanderBase != null && binding.Unit.CommanderId == binding.Commander.Id && binding.Definition?.Id == binding.Commander.Id, "assigned unit base and runtime reference");
                    }
                }
                catch (RuleViolation error) { failures.Add(new { file = Path.GetFileName(file), unit = pair.i, error.Code }); }
            }
            var factions = battle.Factions.Select((f, i) => new FactionState { Id = ids[i], Name = f.CountryId, Camp = f.Camp.ToString(), Human = i == 0,
                Resources = f.Resources, IncomeMultiplier = f.IncomeMultiplier, AiEvasionBonus = f.AiEvasionBonus, Survival = FactionSurvival.Legacy(f.SurvivalMode) }).ToArray();
            var state = new WorldState { ScenarioId = Path.GetFileNameWithoutExtension(file), ContentRevision = "structural-check." + importedMap.SourceRevision,
                Width = maps[battle.MapId].Width, Height = maps[battle.MapId].Height, ActiveFaction = ids[0], Factions = factions, Cells = importedMap.Cells,
                Units = bindings.Select(b => b.Unit).ToArray(), Commanders = bindings.Where(b => b.Commander != null).Select(b => b.Commander!).ToArray() };
            state.Validate(); Check(state.Units.Count == battle.Armies.Count, "all armies fit dynamic structural snapshot " + JsonSerializer.Serialize(failures));
            if (sample == null && bindings.Any(b => b.Commander != null))
            {
                sample = state; assignedSample = bindings.First(b => b.Commander != null); sourceSample = assignedSample.Source;
                sampleVariant = battle.Factions[Array.IndexOf(ids, assignedSample.Unit.Owner)].Variant;
            }
            files++;
        }
        Check(files == 95 && units == 7289 && failures.Count == 0, "all original army records imported");
        Check(sample != null && assignedSample != null && sourceSample != null, "assigned sample available");
        var initial = sample! with { ActiveFaction = assignedSample!.Unit.Owner };
        var decoded = WorldSave.Decode(WorldSave.Encode(initial), initial.ContentRevision);
        var restoredUnit = decoded.Units.Single(u => u.Id == assignedSample.Unit.Id);
        Check(restoredUnit.CommanderBase != null && restoredUnit.CommanderBase.Combatant.Profile.MaxHealth == assignedSample.Unit.CommanderBase!.Combatant.Profile.MaxHealth
            && restoredUnit.CommanderId == assignedSample.Commander!.Id, "imported assignment survives modern save; structural fixture only");
        var detach = WorldCommanders.Detach(decoded, decoded.Revision, restoredUnit.Id, new());
        var detached = detach.State.Units.Single(u => u.Id == restoredUnit.Id);
        Check(detached.CommanderId == null && detached.CommanderBase == null && detached.Combatant.Profile.MaxHealth == restoredUnit.CommanderBase!.Combatant.Profile.MaxHealth
            && detached.Combatant.Stars == 0 && detached.Combatant.Skills.Count == 0, "real detach uses imported unassigned base");
        Check(decoded.Units.Single(u => u.Id == restoredUnit.Id).CommanderId != null, "detach leaves imported snapshot unchanged");
        var sourceRow = sourceSample!;
        var sampleCell = initial.Cells[sourceRow.Cell];
        string variantName = sampleVariant!;
        Reject(() => LegacyUnitImporter.Import(sourceRow with { Flag58 = 1 }, "x", initial.ActiveFaction, variantName, sampleCell, catalog), "legacy.army_state");
        Reject(() => LegacyUnitImporter.Import(sourceRow with { Commander = short.MaxValue }, "x", initial.ActiveFaction, variantName, sampleCell, catalog), "legacy.commander_definition");
        Reject(() => LegacyUnitImporter.Import(sourceRow with { Health = 0 }, "x", initial.ActiveFaction, variantName, sampleCell, catalog), "legacy.army_state");
        Reject(() => LegacyUnitImporter.Import(sourceRow with { Behavior = 255 }, "x", initial.ActiveFaction, variantName, sampleCell, catalog), "legacy.army_state");
        var report = new { files, importedUnits = units, assignedCommanders = assigned, failures, unmappedGameplay = pending, rescuedNativeZeroHealth = rescued,
            preservedSpentMovement = preservedMovement, checks, limitations = new[] { "Structural snapshots and unit components only; no complete legacy scenario Start or rule host.",
                "Death references require the separate LegacyScenarioEvents component; full tavern/AI/faction/site rules and metagame integration are still required." } };
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_army_import.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
