using System.Diagnostics;
using System.Buffers.Binary;
using System.Text.Json;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class LegacyHostChecks
{
    public static LegacyScenarioCatalogs Catalogs(string root)
    {
        string assets = Path.Combine(root, "apk_work/original/assets");
        string Text(string name) => File.ReadAllText(Path.Combine(assets, name));
        var runtime = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        runtime.VerifySources(name => File.ReadAllBytes(Path.Combine(assets, name)));
        var buildings = LegacyBuildingCatalog.Read(Text("def_construction.xml"), Text("def_capital.xml"));
        var combat = LegacyCombatCatalog.Read(File.ReadAllText(Path.Combine(root, "reverse/reports/runtime_rule_tables.json")),
            Text("def_terraintype.xml"), Text("def_installation.xml"), buildings);
        var accounts = LegacyCommanderAccounts.Read(runtime, combat, File.ReadAllText(Path.Combine(root, "reverse/reports/metagame_rule_tables.json")));
        return new(runtime, LegacyTerrainRules.FromXml(Text("def_mapterrain.xml"), Text("def_terraintype.xml")), buildings,
            LegacyRecruitmentCatalog.Read(Text("def_card.xml")), combat, accounts, LegacyItemTradingCatalog.Read(Text("def_item.xml")),
            LegacyWarzoneTechnology.Read(Text("def_warzonetech.xml")), LegacyDialogueCatalog.Read(Text("def_dialogues.xml"), Text("stringtable_cn.ini")));
    }

    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Scenario host: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + " actual=" + error.Code); return; }
            throw new InvalidOperationException("Expected host rejection: " + code);
        }
        string assets = Path.Combine(root, "apk_work/original/assets");
        var catalogs = Catalogs(root);
        var maps = new Dictionary<int, LegacyTerrainMap> { [1] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "europe.bin"))),
            [2] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "america.bin"))) };
        var names = XDocument.Load(Path.Combine(assets, "def_areaname.xml")).Root!.Elements("areaname")
            .ToDictionary(e => (int)e.Attribute("id")!, e => (string)e.Attribute("name")!);
        var account = new PlayerAccount { Id = "local", ContentRevision = catalogs.Accounts.SourceRevision, Medals = 20_000_000 };
        byte[] accountBefore = JsonSerializer.SerializeToUtf8Bytes(account);
        int scenarios = 0, starts = 0, choices = 0, paused = 0, finished = 0, units = 0, explicitNeutralPlayers = 0;
        var clock = Stopwatch.StartNew();
        LegacyScenarioStart? sample = null; LegacyScenarioRequest? sampleRequest = null; LegacyBattle? sampleBattle = null;
        foreach (string path in Directory.GetFiles(assets, "*.btl").Order(StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            var battle = LegacyBattle.Read(File.ReadAllBytes(path));
            byte[] source = battle.ToBytes();
            int mode = id.StartsWith("conquest", StringComparison.Ordinal) ? 2 : id.StartsWith("tutorial", StringComparison.Ordinal) ? 4 : 1;
            var countries = mode == 2 ? battle.Factions.Select(f => f.CountryId).ToArray() : new string?[] { null };
            foreach (string? country in countries)
            {
                var request = new LegacyScenarioRequest { Id = id, Mode = mode, SelectedCountry = country, CityNames = names, CombatSeed = 1930, AiSeed = 1946 };
                if (mode == 2 && battle.Factions.Single(f => f.CountryId == country).Camp == 4)
                {
                    Reject(() => LegacyScenarioHost.Start(battle, maps, catalogs, request, account), "legacy.start_neutral_player");
                    request = request with { NeutralCamps = Array.Empty<int>() };
                    explicitNeutralPlayers++;
                }
                var start = LegacyScenarioHost.Start(battle, maps, catalogs, request, account);
                start.Session.Validate();
                var world = start.Session.World;
                var binding = new SessionContentBinding(start.Session.ContentRevision, world.ContentRevision, account.ContentRevision);
                var restored = SessionSave.Decode(SessionSave.Encode(start.Session), binding);
                Check(SessionSave.Encode(start.Session).SequenceEqual(SessionSave.Encode(restored)), "complete session round trip " + id);
                Check(world.Units.Count == battle.Armies.Count && world.Factions.Count == battle.Factions.Count, "complete map and armies " + id);
                Check(world.ActiveFaction == start.Session.PlayerFaction && world.Factions[0].Id == start.Session.PlayerFaction, "stable player-first order " + id);
                Check(world.Factions.Select(f => f.Resources).SequenceEqual(world.Factions.Select(f => battle.Factions.Single(s =>
                    LegacyScenarioInitialization.FactionId(id, battle.Factions.ToList().IndexOf(s), s) == f.Id).Resources)), "no first-round income " + id);
                Check(start.Rules.Production.Keys.ToHashSet().SetEquals(world.Factions.Select(f => f.Id))
                    && start.Rules.Recruitment.Keys.ToHashSet().SetEquals(world.Factions.Select(f => f.Id)), "every faction has its own rules " + id);
                Check(start.Rules.Battle.PlayerCamp == start.Rules.Movement.PlayerCamp && start.Rules.Turns.Victory == start.Rules.Battle.Victory
                    && start.Rules.Ai.Movement == start.Rules.Movement && start.Rules.Ai.NeutralCamps.SequenceEqual(start.Rules.Battle.NeutralCamps), "shared victory, neutral and movement policy " + id);
                Check(world.Phase != TurnPhase.Finished, "new scenario does not finish before first action " + id);
                if (world.PendingDialogue != null) paused++;
                if (world.Phase == TurnPhase.Finished) finished++;
                if (sample == null && mode == 2 && world.Phase == TurnPhase.Active)
                { sample = start; sampleRequest = request; sampleBattle = battle; }
                starts++; units += world.Units.Count;
                if (mode == 2) choices++;
            }
            Check(source.SequenceEqual(battle.ToBytes()), "startup leaves binary source unchanged " + id);
            scenarios++;
        }
        Check(JsonSerializer.SerializeToUtf8Bytes(account).SequenceEqual(accountBefore), "all starts preserve supplied local account");
        Check(sample != null, "actual playable conquest entry exists");
        var repeated = LegacyScenarioHost.Start(sampleBattle!, maps, catalogs, sampleRequest!, account);
        Check(SessionSave.Encode(sample!.Session).SequenceEqual(SessionSave.Encode(repeated.Session)), "whole start is deterministic");
        var differentSeed = LegacyScenarioHost.Start(sampleBattle!, maps, catalogs, sampleRequest! with { AiSeed = 1924 }, account);
        Check(differentSeed.Session.World.ContentRevision == sample.Session.World.ContentRevision
            && differentSeed.Session.World.AiRandomState != sample.Session.World.AiRandomState, "random seed is state rather than content identity");
        var policyChange = LegacyScenarioHost.Start(sampleBattle!, maps, catalogs, sampleRequest! with { Limits = new() { UnitsPerCell = 2 } }, account);
        Check(policyChange.Session.World.ContentRevision != sample.Session.World.ContentRevision, "capacity policy binds saves");
        var labels = names.OrderByDescending(p => p.Key).ToDictionary(p => p.Key, p => p.Value);
        var reordered = LegacyScenarioHost.Start(sampleBattle!, maps, catalogs, sampleRequest! with { CityNames = labels }, account);
        Check(reordered.Session.ContentRevision == sample.Session.ContentRevision, "dictionary insertion order does not change content identity");
        var session = sample.Session;
        var end = WorldTurns.EndTurn(session.World, session.World.Revision, sample.Rules.Turns);
        var after = SessionActions.CommitWorld(session, session.Revision, end).State;
        Check(after.World.ActiveFaction != session.World.ActiveFaction && after.World.Round == 1, "actual conquest advances to next faction");
        var bindings = new SessionContentBinding(after.ContentRevision, after.World.ContentRevision, after.Account.ContentRevision);
        Check(SessionSave.Decode(SessionSave.Encode(after), bindings).World.ActiveFaction == after.World.ActiveFaction, "post-turn actual session restores");
        Check(after.Account.Medals == account.Medals, "turn transition never touches account medals");
        var modernMap = LegacyMapDefinitions.Import(maps[sampleBattle!.MapId], catalogs.Terrain, "verification.map.37", "第三张地图验证");
        int editableCell = Enumerable.Range(0, modernMap.Cells.Count).First(i => sample.Session.World.Cells[i].Terrain.Active
            && !sampleBattle.Sites.Any(s => s.Cell == i) && !sampleBattle.Armies.Any(a => a.Cell == i));
        var modifiedMap = modernMap.Edit([new(editableCell, modernMap.Cells[editableCell] with { Name = "西安 Xi'an",
            Terrain = modernMap.Cells[editableCell].Terrain with { TerrainCost = modernMap.Cells[editableCell].Terrain.TerrainCost + 1 } })]);
        var mapCatalog = new WorldMapCatalog([MapDocument.Decode(MapDocument.Encode(modifiedMap)).Map]);
        byte[] thirdRaw = sampleBattle.ToBytes(); BinaryPrimitives.WriteInt32LittleEndian(thirdRaw.AsSpan(4), 37);
        var thirdBattle = LegacyBattle.Read(thirdRaw);
        var thirdRequest = sampleRequest! with { Id = "verification.third-map" };
        var third = LegacyScenarioHost.Start(thirdBattle, mapCatalog, new Dictionary<int, string> { [37] = modifiedMap.Id }, catalogs, thirdRequest, account);
        Check(third.Session.World.Cells[editableCell].Name == "西安 Xi'an"
            && third.Session.World.Cells[editableCell].Terrain.TerrainCost == modifiedMap.Cells[editableCell].Terrain.TerrainCost, "third map JSON drives actual startup geography");
        var thirdBinding = new SessionContentBinding(third.Session.ContentRevision, third.Session.World.ContentRevision, account.ContentRevision);
        byte[] thirdSave = SessionSave.Encode(third.Session);
        Check(SessionSave.Decode(thirdSave, thirdBinding).World.ScenarioId == thirdRequest.Id, "third map session saves and restores");
        var oldMapStart = LegacyScenarioHost.Start(thirdBattle, new WorldMapCatalog([modernMap]), new Dictionary<int, string> { [37] = modernMap.Id }, catalogs, thirdRequest, account);
        Check(oldMapStart.Session.World.ContentRevision != third.Session.World.ContentRevision, "map change binds actual world saves");
        Reject(() => SessionSave.Decode(thirdSave, new(oldMapStart.Session.ContentRevision, oldMapStart.Session.World.ContentRevision, account.ContentRevision)), "save.content_revision");
        Reject(() => LegacyScenarioHost.Start(thirdBattle, mapCatalog, new Dictionary<int, string>(), catalogs, thirdRequest, account), "map.reference");
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_host.json"), JsonSerializer.Serialize(new { checks, errors = 0,
            scenarios, starts, conquestChoices = choices, explicitNeutralPlayers, importedUnits = units, paused, finished, elapsedMilliseconds = clock.ElapsedMilliseconds,
            scope = "Headless new-scenario session composition/start/save and actual conquest turn transition. No renderer, Android APK, original-profile migration or complete-playthrough claim." },
            new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
