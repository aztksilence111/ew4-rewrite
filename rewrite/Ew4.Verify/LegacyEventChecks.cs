using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class LegacyEventChecks
{
    private static int checks;
    private static void Check(bool value, string label)
    { checks++; if (!value) throw new InvalidOperationException("Legacy events: " + label); }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + ": " + error.Code); return; }
        throw new InvalidOperationException("Expected legacy event rejection: " + code);
    }
    private static void Set(byte[] raw, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(offset), value);
    private static LegacyBattle Battle(int condition, int effect, string country = "missing", int round = 1, int dialogue = -1,
        int[]? deathReferences = null, bool triggered = false)
    {
        deathReferences ??= [];
        var raw = new byte[72 + 360 + 9 + 16 + deathReferences.Length * 32 + 44];
        int[] header = [2, 1, 0, 0, 3, 3, 2, 1, deathReferences.Length, 1, 0, 0, 0, 0, 0, 0, 0, 0];
        for (int i = 0; i < header.Length; i++) Set(raw, i * 4, header[i]);
        for (int i = 0; i < 2; i++)
        {
            Set(raw, 72 + i * 180 + 56, BitConverter.SingleToInt32Bits(1));
            Encoding.UTF8.GetBytes(i == 0 ? "old" : "new").CopyTo(raw, 72 + i * 180 + 24);
            Encoding.UTF8.GetBytes("others").CopyTo(raw, 72 + i * 180 + 40);
        }
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(441), 7);
        BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(443), -1);
        raw[445] = 255; raw[450] = 255; raw[452] = 7;
        for (int i = 0; i < deathReferences.Length; i++)
        {
            int offset = 457 + i * 32;
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(offset), (short)i);
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(offset + 4), 100);
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(offset + 6), 100);
            BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(offset + 16), -1);
            raw[offset + 25] = 1; raw[offset + 26] = unchecked((byte)deathReferences[i]);
        }
        int cursor = raw.Length - 44;
        int[] record = [7, condition, effect, round, dialogue, 4];
        for (int i = 0; i < record.Length; i++) Set(raw, cursor + i * 4, record[i]);
        Encoding.UTF8.GetBytes(country).CopyTo(raw, cursor + 24);
        raw[cursor + 40] = (byte)(triggered ? 1 : 0);
        Array.Fill(raw, (byte)204, cursor + 41, 3);
        return LegacyBattle.Read(raw);
    }
    private static LegacyUnitBinding[] Bindings(LegacyBattle battle, WorldState fixture) => battle.Armies.Select((a, i) =>
        new LegacyUnitBinding(fixture.Units[0] with { Id = "source." + i, Cell = a.Cell }, null, null, a, [])).ToArray();
    private static WorldState WithEvents(WorldState state, LegacyEventImport imported) => state with
        { Events = imported.Events, TriggeredEvents = imported.TriggeredEvents };

    public static int Run(string root)
    {
        checks = 0;
        string assets = Path.Combine(root, "apk_work/original/assets");
        var dialogueCatalog = LegacyDialogueCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_dialogues.xml")),
            File.ReadAllText(Path.Combine(assets, "stringtable_cn.ini")));
        var fixture = WorldChecks.Example();
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/objective_event_vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("objectives").EnumerateArray())
        {
            var markers = vector.GetProperty("markers").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var goals = vector.GetProperty("unit_objectives").EnumerateArray().ToArray();
            var world = fixture with { Cells = Enumerable.Range(0, 9).Select(i => new CellState { Owner = "loser",
                Objective = i < markers.Length && markers[i] == 1, BlocksTerritoryExpansion = i < markers.Length && markers[i] != 0 }).ToArray(),
                Units = goals.Select((g, i) => (g, i)).Where(p => p.g.ValueKind != JsonValueKind.Null).Select(p => fixture.Units[0] with
                    { Id = "goal." + p.i, Cell = p.i, Objective = p.g.GetInt32() == 1 }).ToArray() };
            world.Validate();
            Check(WorldVictory.Facts(world, [], ObjectiveUnitOwnership.TerritoryOwner).Single(f => f.Id == "loser").Objectives
                == vector.GetProperty("expected").GetInt32(), "native double target count and marker2 exclusion");
        }
        var compatibility = fixture with { Units = [fixture.Units[0] with { Owner = "winner", Objective = true }] };
        Check(WorldVictory.Facts(compatibility, []).Single(f => f.Id == "winner").Objectives == 1
            && WorldVictory.Facts(compatibility, [], ObjectiveUnitOwnership.TerritoryOwner).Single(f => f.Id == "loser").Objectives == 1,
            "unit objectives use explicit owner policy; modern default follows army");
        Reject(() => WorldVictory.Facts(compatibility, [], (ObjectiveUnitOwnership)99), "victory.configuration");

        foreach (var vector in vectors.RootElement.GetProperty("capture").EnumerateArray())
        {
            bool Bool(string key) => vector.GetProperty(key).GetBoolean();
            int effect = vector.GetProperty("effect").GetInt32();
            var battle = Battle(vector.GetProperty("condition").GetInt32(), effect, triggered: Bool("already"));
            var imported = LegacyScenarioEvents.Import(battle, [], fixture.Cells, ["loser", "winner"], dialogueCatalog);
            var before = WithEvents(fixture, imported);
            if (!Bool("opposing_camps")) before = before with { Factions = before.Factions.Select(f => f with { Camp = "a" }).ToArray() };
            bool eligible = Bool("had_owner") && !Bool("won");
            if (!Bool("exists")) before = before with { Events = [], TriggeredEvents = [] };
            var after = eligible ? WorldEvents.Process(before, new EventContext(TriggerKind.CellCaptured, Cell: 7,
                PreviousOwner: "loser", NewOwner: "winner")).State : before;
            bool fired = after.TriggeredEvents.Contains("legacy.event.7");
            Check(fired == (Bool("exists") && vector.GetProperty("expected").GetProperty("fired").GetBoolean()), "native capture eligibility/shared identity");
            bool triggeredNow = Bool("exists") && !Bool("already") && eligible && Bool("opposing_camps");
            foreach (var unit in after.Units)
            {
                int expected = triggeredNow && effect < 4 && unit.Owner == (effect == 0 ? "winner" : "loser") ? new[] { 1, -1, -2, -3 }[effect] : 0;
                Check(unit.Morale.Effective == expected, "native capture morale targets capturing/previous faction");
            }
            Check(after.Cells[4].Environment == (triggeredNow && effect == 5 ? 3 : 0), "native capture environment effect");
        }
        foreach (var vector in vectors.RootElement.GetProperty("death").EnumerateArray())
        {
            bool general = vector.GetProperty("general").GetBoolean(), explicitCountry = vector.GetProperty("country_exists").GetBoolean();
            int effect = vector.GetProperty("effect").GetInt32();
            var battle = Battle(vector.GetProperty("condition").GetInt32(), effect, explicitCountry ? "new" : "missing", deathReferences: [7]);
            var imported = LegacyScenarioEvents.Import(battle, Bindings(battle, fixture), fixture.Cells, ["loser", "winner"], dialogueCatalog);
            var before = WithEvents(fixture, imported);
            var after = WorldEvents.Process(before, new EventContext(TriggerKind.UnitDestroyed, "source.0", 0, "loser", HasCommander: general)).State;
            Check(after.TriggeredEvents.Contains("legacy.event.7") == general, "death references require commander but ignore raw condition");
            foreach (var unit in after.Units)
            {
                int expected = general && effect < 4 && unit.Owner == (explicitCountry ? "winner" : "loser") ? new[] { 1, -1, -2, -3 }[effect] : 0;
                Check(unit.Morale.Effective == expected, "native death explicit country or territory owner fallback");
            }
        }

        var textCatalog = LegacyDialogueCatalog.Read("<dialogues><dialogue id='3' commander='24' textid='1' left='1'/></dialogues>",
            "desc_dialogue 1=西安 Xi'an=前进\n");
        Check(textCatalog.Definitions[3].Text == "西安 Xi'an=前进" && textCatalog.Definitions[3].Left, "Unicode and literal equals in dialogue");
        Reject(() => LegacyDialogueCatalog.Read("<dialogues/>", "desc_dialogue 1=1\ndesc_dialogue 1=2"), "legacy.stringtable");
        Reject(() => LegacyDialogueCatalog.Read("<dialogues><dialogue id='3' commander='1' textid='8'/></dialogues>", "x=1"), "legacy.dialogues");
        var sharedBattle = Battle(2, 3, deathReferences: [7, 7], dialogue: 3);
        var shared = LegacyScenarioEvents.Import(sharedBattle, Bindings(sharedBattle, fixture), fixture.Cells, ["loser", "winner"], textCatalog);
        Check(shared.Events.Count == 1 && shared.Events[0].Rules.Count == 4 && shared.DeathBindings == 2 && shared.CaptureBindings == 1,
            "one event identity has round, capture and multiple death references");
        var round = WorldEvents.Process(WithEvents(fixture, shared), TriggerKind.Round).State;
        Check(round.Units.All(u => u.Morale.Effective == 0) && round.PendingDialogue == "legacy.dialogue.3", "round effect3 remains no-op but dialogue fires");
        var restored = WorldSave.Decode(WorldSave.Encode(round), fixture.ContentRevision);
        var again = WorldEvents.Process(WorldEvents.Acknowledge(restored, restored.PendingDialogue!),
            new EventContext(TriggerKind.UnitDestroyed, "source.1", 0, "loser", HasCommander: true));
        Check(again.Notices.Count == 0 && again.State.PendingDialogue == null, "shared event does not refire after save and alternate trigger");
        var dead = WorldEvents.Process(WithEvents(fixture, shared), new EventContext(TriggerKind.UnitDestroyed, "source.0", 0, "loser", HasCommander: true)).State;
        Check(dead.Units[0].Morale.Effective == -3, "same event effect3 works on death entry point");
        var danglingBattle = Battle(0, 4, deathReferences: [0]);
        var dangling = LegacyScenarioEvents.Import(danglingBattle, Bindings(danglingBattle, fixture), fixture.Cells, ["loser", "winner"], textCatalog);
        Check(dangling.MissingReferences.Single().EventId == 0 && dangling.DeathBindings == 0, "dangling ID0 is reported, not invented");
        Check(shared.SourceRevision == LegacyScenarioEvents.Import(sharedBattle, Bindings(sharedBattle, fixture), fixture.Cells,
            ["loser", "winner"], textCatalog).SourceRevision, "event component digest deterministic");
        var changedText = LegacyDialogueCatalog.Read("<dialogues><dialogue id='3' commander='24' textid='1' left='1'/></dialogues>", "desc_dialogue 1=新命令\n");
        Check(shared.SourceRevision != LegacyScenarioEvents.Import(sharedBattle, Bindings(sharedBattle, fixture), fixture.Cells,
            ["loser", "winner"], changedText).SourceRevision, "event digest includes actual localized dialogue source");
        var rawEvent = sharedBattle.SectionBytes("opaque44");
        Check(LegacyEvent.Read(rawEvent).Padding41 == 204 && LegacyEvent.Read(rawEvent).Padding43 == 204, "padding remains raw data, not gameplay flags");
        Reject(() => LegacyEvent.Read(rawEvent.AsSpan(0, 43)), "legacy.event_record");
        rawEvent[40] = 2; Reject(() => LegacyEvent.Read(rawEvent), "legacy.event_record");
        var badRound = Battle(2, 4, round: 0);
        var dormant = LegacyScenarioEvents.Import(badRound, [], fixture.Cells, ["loser", "winner"], textCatalog);
        Check(WorldEvents.Process(WithEvents(fixture, dormant), TriggerKind.Round).State.TriggeredEvents.Count == 0,
            "round zero remains an unreachable event in games beginning at round one");
        var unknown = LegacyScenarioEvents.Import(Battle(2, 8, dialogue: 3), [], fixture.Cells, ["loser", "winner"], textCatalog);
        var unknownRound = WorldEvents.Process(WithEvents(fixture, unknown), TriggerKind.Round).State;
        Check(unknownRound.PendingDialogue == "legacy.dialogue.3" && unknownRound.Units.All(u => u.Morale.Effective == 0)
            && unknownRound.Cells.All(c => c.Environment == 0), "native unknown effect preserves dialogue without inventing numerical effects");
        var repeatedCountryBytes = Battle(2, 0, "old").ToBytes();
        Array.Copy(repeatedCountryBytes, 72 + 24, repeatedCountryBytes, 72 + 180 + 24, 16);
        var repeatedCountry = LegacyScenarioEvents.Import(LegacyBattle.Read(repeatedCountryBytes), [], fixture.Cells, ["loser", "winner"], textCatalog);
        Check(repeatedCountry.Events.Single().Rules.Single(r => r.Trigger == TriggerKind.Round).FactionId == "loser",
            "native event country lookup selects first name without merging indexed factions");
        var badEffect = Battle(0, -1);
        Reject(() => LegacyScenarioEvents.Import(badEffect, [], fixture.Cells, ["loser", "winner"], textCatalog), "legacy.event_definition");
        Reject(() => LegacyScenarioEvents.Import(sharedBattle, [], fixture.Cells, ["loser", "winner"], textCatalog), "legacy.event_context");
        var duplicates = new byte[sharedBattle.ToBytes().Length + 44];
        sharedBattle.ToBytes().CopyTo(duplicates, 0); sharedBattle.SectionBytes("opaque44").CopyTo(duplicates, duplicates.Length - 44);
        Set(duplicates, 9 * 4, 2);
        int firstEvent = sharedBattle.ToBytes().Length - 44, lastEvent = duplicates.Length - 44;
        duplicates[firstEvent + 40] = 1;
        Set(duplicates, lastEvent + 4, 2); Set(duplicates, lastEvent + 8, 5);
        Set(duplicates, lastEvent + 12, 7); Set(duplicates, lastEvent + 20, 8);
        duplicates[lastEvent + 40] = 0;
        var duplicateBattle = LegacyBattle.Read(duplicates);
        var mergedEvents = LegacyScenarioEvents.Import(duplicateBattle, Bindings(duplicateBattle, fixture), fixture.Cells,
            ["loser", "winner"], textCatalog);
        Check(mergedEvents.Events.Count == 1 && mergedEvents.TriggeredEvents.Count == 1
            && mergedEvents.Events.Single().Rules.Single(r => r.Trigger == TriggerKind.Round).Round == 7
            && mergedEvents.Events.Single().Rules.All(r => r.Cell == sharedBattle.Events.Single().Cell && r.Effect == EventEffectKind.EnvironmentSpread),
            "native duplicate event replaces payload but retains original cell and triggered state");

        Integration(fixture, shared, textCatalog);
        int files = 0, records = 0, deathBindings = 0, captureBindings = 0, objectiveUnits = 0, preservedMarker2 = 0;
        var missingReferences = new List<object>();
        var catalog = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var terrainRules = LegacyTerrainRules.FromXml(File.ReadAllText(Path.Combine(assets, "def_mapterrain.xml")), File.ReadAllText(Path.Combine(assets, "def_terraintype.xml")));
        var maps = new Dictionary<int, LegacyTerrainMap> { [1] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "europe.bin"))),
            [2] = LegacyTerrainMap.Read(File.ReadAllBytes(Path.Combine(assets, "america.bin"))) };
        var names = XDocument.Load(Path.Combine(assets, "def_areaname.xml")).Root!.Elements("areaname").ToDictionary(e => (int)e.Attribute("id")!, e => (string)e.Attribute("name")!);
        var buildings = LegacyBuildingCatalog.Kinds;
        var buildingRules = LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml")));
        var installations = new Dictionary<int, string> { [0] = "trench", [1] = "fence", [2] = "bunker" };
        foreach (string file in Directory.GetFiles(assets, "*.btl").Order(StringComparer.Ordinal))
        {
            var battle = LegacyBattle.Read(File.ReadAllBytes(file));
            var ids = battle.Factions.Select((f, i) => "legacy.faction." + i).ToArray();
            var map = LegacyScenarioMap.Import(maps[battle.MapId], battle, terrainRules, ids, buildings, installations, names, buildingRules);
            var units = battle.Armies.Select((a, i) =>
            {
                string owner = map.Cells[a.Cell].Owner!;
                return LegacyUnitImporter.Import(a, "legacy.unit." + i, owner, battle.Factions[Array.IndexOf(ids, owner)].Variant,
                    map.Cells[a.Cell], catalog, commanderId: "legacy.commander." + i);
            }).ToArray();
            var imported = LegacyScenarioEvents.Import(battle, units, map.Cells, ids, dialogueCatalog);
            var state = new WorldState { ScenarioId = Path.GetFileNameWithoutExtension(file), ContentRevision = "event-component." + imported.SourceRevision,
                Width = maps[battle.MapId].Width, Height = maps[battle.MapId].Height, ActiveFaction = ids[0], Cells = map.Cells,
                Factions = battle.Factions.Select((f, i) => new FactionState { Id = ids[i], Camp = f.Camp.ToString(), Human = i == 0 }).ToArray(),
                Units = units.Select(u => u.Unit).ToArray(), Commanders = units.Where(u => u.Commander != null).Select(u => u.Commander!).ToArray(),
                Events = imported.Events, TriggeredEvents = imported.TriggeredEvents };
            state.Validate();
            var saved = WorldSave.Decode(WorldSave.Encode(state), state.ContentRevision);
            Check(saved.Events.Count == battle.Events.Count && saved.Events.Select(e => e.Rules.Count).SequenceEqual(imported.Events.Select(e => e.Rules.Count)),
                "real event components survive save " + state.ScenarioId);
            foreach (var unit in units) Check(unit.Unit.Objective == (unit.Source.Objective == 1), "real unit objective exact marker");
            foreach (var site in battle.Sites)
            {
                Check(map.Cells[site.Cell].Objective == (site.Marker == 1) && map.Cells[site.Cell].BlocksTerritoryExpansion == (site.Marker != 0), "real site target/expansion split");
                if (site.Marker == 2) preservedMarker2++;
            }
            foreach (var missing in imported.MissingReferences) missingReferences.Add(new { file = Path.GetFileName(file), missing.Kind, missing.Subject, missing.EventId });
            objectiveUnits += units.Count(u => u.Unit.Objective); records += battle.Events.Count;
            deathBindings += imported.DeathBindings; captureBindings += imported.CaptureBindings; files++;
        }
        Check(files == 95 && records == 376, "all original event components imported");
        var report = new { files, records, dialogueDefinitions = dialogueCatalog.Definitions.Count, deathBindings, captureBindings,
            objectiveUnits, preservedMarker2, missingReferences, checks,
            limitations = new[] { "Original scenario event/objective components only; no complete legacy world Start or Android client.",
                "Missing original references are reported and inert. Faction auxiliary tables, complete building/recruitment rules and metagame remain incomplete." } };
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_events.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }

    private static void Integration(WorldState fixture, LegacyEventImport shared, LegacyDialogueCatalog catalog)
    {
        var mover = fixture.Units[1] with { RemainingMovement = 6 };
        var world = fixture with { Units = [fixture.Units[0], mover],
            Factions = fixture.Factions.Select(f => f with { Survival = new(true, false, []) }).ToArray(),
            Cells = fixture.Cells.Select((c, i) => i == 7 ? c with { Owner = "loser" } : c).ToArray() };
        var import = LegacyScenarioEvents.Import(Battle(1, 0, dialogue: 3), [], world.Cells, ["loser", "winner"], catalog);
        world = WithEvents(world, import);
        byte[] before = WorldSave.Encode(world);
        var move = WorldActions.Move(world, world.Revision, mover.Id, 7);
        Check(move.State.PendingDialogue == "legacy.dialogue.3" && move.State.TriggeredEvents.Count == 1
            && move.State.Units.Single(u => u.Id == mover.Id).Morale.Effective == 1 && move.State.Revision == world.Revision + 1,
            "public movement captures, applies event and pauses in one commit");
        Check(before.SequenceEqual(WorldSave.Encode(world)), "capture leaves input snapshot unchanged");
        Reject(() => WorldActions.Move(move.State, move.State.Revision, mover.Id, 6), "action.dialogue_pending");
        var saved = WorldSave.Decode(WorldSave.Encode(move.State), world.ContentRevision);
        var acknowledged = WorldEvents.Acknowledge(saved, saved.PendingDialogue!);
        Check(acknowledged.TriggeredEvents.Count == 1 && acknowledged.PendingDialogue == null, "capture resumes after save");
        var friendly = world with { Factions = world.Factions.Select(f => f with { Camp = "a" }).ToArray() };
        var alliedMove = WorldActions.Move(friendly, friendly.Revision, mover.Id, 7);
        Check(alliedMove.State.Cells[7].Owner == "loser" && alliedMove.State.TriggeredEvents.Count == 0 && alliedMove.State.PendingDialogue == null,
            "same-camp movement preserves ownership and does not trigger capture events");
        var finalMove = WorldActions.Move(world, world.Revision, mover.Id, 7, new() { PlayerCamp = "b", Victory = new(1, false) });
        Check(finalMove.State.Outcome == BattleOutcome.Continue, "movement victory policy configured without spurious finish");
        var winning = world with { Cells = world.Cells.Select((c, i) => c with { Objective = i == 7 || i == 8 }).ToArray() };
        var won = WorldActions.Move(winning, winning.Revision, mover.Id, 7, new() { PlayerCamp = "b", Victory = new(ObjectivesEnabled: true) });
        Check(won.State.Outcome == BattleOutcome.Victory && won.State.PendingDialogue == null && won.State.TriggeredEvents.Count == 0,
            "capture victory ends before native reference event");
        var targetOnly = fixture with { Units = fixture.Units.Select(u => u with { Objective = true }).ToArray() };
        Check(WorldVictory.Evaluate(targetOnly, "b", new(ObjectivesEnabled: true), []) == BattleOutcome.Continue, "both armies remain objectives without site targets");
        var ended = WorldTurns.EndTurn(targetOnly, targetOnly.Revision, new() { PlayerCamp = "b", Victory = new(ObjectivesEnabled: true) });
        Check(ended.State.Outcome == BattleOutcome.Continue, "turn victory counts army targets");
        var battleRules = new WorldBattleRules { CheckFactionSurvival = false, ResolveIgnition = false,
            RecalculateLocalMorale = false, KillDialogueProbability = 0, PlayerCamp = "b", Victory = new(ObjectivesEnabled: true) };
        var wounded = WorldBattle.CommitResolved(targetOnly, new("unit.2", "unit.1", 0, 1, false, 1946), battleRules);
        Check(wounded.State.Outcome == BattleOutcome.Continue, "battle victory counts surviving army targets");
        var killed = WorldBattle.CommitResolved(targetOnly, new("unit.2", "unit.1", 0, 100, false, 1946), battleRules);
        Check(killed.State.Outcome == BattleOutcome.Victory, "destroying last enemy army target wins");
        var commander = new CommanderRuntime { Id = "general", Owner = "loser" };
        var bound = WithEvents(fixture with { Commanders = [commander], Units = [fixture.Units[0] with { Id = "source.0", CommanderId = commander.Id },
            fixture.Units[1]] }, shared);
        var result = WorldBattle.CommitResolved(bound, new("unit.2", "source.0", 0, 100, false, 1946), battleRules with { PlayerCamp = null });
        Check(result.State.PendingDialogue == "legacy.dialogue.3" && result.State.TriggeredEvents.Count == 1 && result.State.Commanders[0].Withdrawn,
            "actual death dispatch passes commander and territory context to shared imported event");
        var expanded = TerritoryClaims.Expand(new(3, 3), fixture.Cells.Select((c, i) => i == 1 ? c with { Objective = false, BlocksTerritoryExpansion = true } : c).ToArray(), [0], [], "winner");
        Check(expanded[1].Owner == "loser" && !expanded[1].Objective, "marker2 remains protected without counting as victory target");
        var copy = bound.Copy();
        ((ScenarioEventRule[])copy.Events[0].Rules)[0] = copy.Events[0].Rules[0] with { Round = 999 };
        Check(bound.Events[0].Rules[0].Round == 1, "event rules copied independently");
        string json = Encoding.UTF8.GetString(WorldSave.Encode(bound));
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace("\"objective\":false,", "")), bound.ContentRevision), "save.required_field");
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace("\"rules\":", "\"oldRules\":")), bound.ContentRevision), "save.required_field");
        foreach (string field in new[] { "\"trigger\":\"Round\",", "\"requireCommander\":false,", "\"requireDifferentCamps\":false,",
            "\"target\":\"Explicit\",", "\"blocksTerritoryExpansion\":false," })
        {
            Check(json.Contains(field, StringComparison.Ordinal), "required field test actually removes " + field);
            Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace(field, "")), bound.ContentRevision), "save.required_field");
        }
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace("\"version\":" + WorldSave.Version, "\"version\":3")), bound.ContentRevision), "save.version");
    }
}
