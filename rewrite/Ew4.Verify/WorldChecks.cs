using System.Text;
using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class WorldChecks
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        checks++;
        if (!value) throw new InvalidOperationException(label);
    }
    private static void Reject(Action action, string code)
    {
        try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + ": " + error.Code); return; }
        throw new InvalidOperationException("Expected rejection: " + code);
    }
    internal static WorldState Example() => new()
    {
        ScenarioId = "verification", ContentRevision = "fixture.1", Width = 3, Height = 3, RandomState = 1946, AiRandomState = 1926,
        ActiveFaction = "winner", Factions = [new() { Id = "loser", Name = "测试甲", Camp = "a" }, new() { Id = "winner", Name = "测试乙", Camp = "b", Human = true }],
        Cells = Enumerable.Range(0, 9).Select(i => new CellState { Owner = i < 5 ? "loser" : "winner", Name = i == 0 ? "西安 Xi'an" : null }).ToArray(),
        Units = [new() { Id = "unit.1", Owner = "loser", Cell = 0, Combatant = Actor() },
            new() { Id = "unit.2", Owner = "winner", Cell = 8, Combatant = Actor() }]
    };
    private static Combatant Actor() => new()
    {
        Health = 100, Profile = new() { Id = "fixture.infantry", Category = "infantry", MaxHealth = 100, Movement = 6,
            MinimumAttack = 2, MaximumAttack = 6, AttackBands = [new(0, 5)],
            DamageMultipliers = new Dictionary<string, float> { ["infantry"] = 1 } }
    };
    public static int Run(string root)
    {
        checks = 0;
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/event_vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("morale").EnumerateArray())
        {
            bool leadership = vector.GetProperty("leadership").GetBoolean();
            int value = vector.GetProperty("value").GetInt32();
            var initial = new MoraleState(vector.GetProperty("baseline").GetInt32());
            var actual = vector.GetProperty("operation").GetString() == "set" ? MoraleState.Set(value, leadership) : initial.Add(value, leadership);
            var wanted = vector.GetProperty("expected");
            Check(actual == new MoraleState(wanted[0].GetInt32(), wanted[1].GetInt32(), wanted[2].GetInt32()), "native morale mutation");
        }
        foreach (var vector in vectors.RootElement.GetProperty("spread").EnumerateArray())
        {
            int center = vector.GetProperty("center").GetInt32();
            var before = Example() with
            {
                RandomState = vector.GetProperty("seed").GetUInt64(),
                Cells = Enumerable.Range(0, 9).Select(i => new CellState { Terrain = new() { Sea = vector.GetProperty("sea")[i].GetBoolean() },
                    Environment = vector.GetProperty("before")[i].GetInt32() }).ToArray(),
                Events = [new() { Id = "spread", Rules = [new() { Effect = EventEffectKind.EnvironmentSpread, Cell = center }] }]
            };
            var after = WorldEvents.Process(before, TriggerKind.Round).State;
            Check(after.Cells.Select(c => c.Environment).SequenceEqual(vector.GetProperty("expected").EnumerateArray().Select(e => e.GetInt32()))
                && after.RandomState == vector.GetProperty("rng_state").GetUInt64(), "native environmental spread and RNG");
            Check(before.TriggeredEvents.Count == 0 && after.TriggeredEvents.Count == 1, "event state is transactional");
        }
        foreach (var vector in vectors.RootElement.GetProperty("round_events").EnumerateArray())
        {
            var definitions = vector.GetProperty("events").EnumerateArray().ToArray();
            var dialogues = vector.GetProperty("dialogues").EnumerateArray().Select(v => v.GetInt32()).ToHashSet();
            var before = Example() with
            {
                Round = vector.GetProperty("round").GetInt32(),
                Events = definitions.Select((e, i) =>
                {
                    int effect = e.GetProperty("effect").GetInt32();
                    return new ScenarioEvent { Id = "event." + i, DialogueId = dialogues.Contains(i) ? "dialogue." + i : null,
                        Rules = [new() { Trigger = e.GetProperty("condition").GetInt32() == 2 ? TriggerKind.Round : TriggerKind.UnitDestroyed,
                        UnitId = "unrelated", Round = e.GetProperty("round").GetInt32(),
                        Effect = effect < 3 ? EventEffectKind.SetMorale : effect == 5 ? EventEffectKind.EnvironmentSpread : EventEffectKind.None,
                        Value = effect < 3 ? new[] { 1, -1, -2 }[effect] : 0,
                        FactionId = e.GetProperty("country").GetInt32() == 1 ? "loser" : null,
                        Cell = e.GetProperty("cell").GetInt32() }] };
                }).ToArray(),
                TriggeredEvents = definitions.Select((e, i) => (e, i)).Where(p => p.e.GetProperty("triggered").GetBoolean()).Select(p => "event." + p.i).ToArray()
            };
            var after = WorldEvents.Process(before, TriggerKind.Round).State;
            Check(Enumerable.Range(0, definitions.Length).Select(i => after.TriggeredEvents.Contains("event." + i))
                .SequenceEqual(vector.GetProperty("expected_triggered").EnumerateArray().Select(v => v.GetBoolean())), "native round event order");
            Check(WorldSave.Decode(WorldSave.Encode(after), "fixture.1").TriggeredEvents.SequenceEqual(after.TriggeredEvents), "fired events survive save");
        }
        foreach (var vector in vectors.RootElement.GetProperty("surrender").EnumerateArray())
        {
            if (vector.GetProperty("successor").GetString() == "self") continue;
            var facts = vector.GetProperty("tiles").EnumerateArray().ToArray();
            var before = Example() with { Units = [], Cells = Enumerable.Range(0, 9).Select(i => new CellState
            {
                Owner = i < facts.Length ? "loser" : null,
                Terrain = new() { Sea = i < facts.Length && facts[i].GetProperty("sea").GetBoolean() },
                BuildingKind = i < facts.Length && facts[i].GetProperty("building").GetBoolean() ? "city" : null
            }).ToArray() };
            var after = WorldEvents.Surrender(before, "loser", vector.GetProperty("successor").GetString() == "none" ? null : "winner").State;
            Check(after.Cells.Take(facts.Length).Select(c => c.Owner ?? "none")
                .SequenceEqual(vector.GetProperty("expected_owners").EnumerateArray().Select(v => v.GetString())), "native surrender owners");
        }
        var example = Example();
        var eventState = example with { Events = [new() { Id = "death", DialogueId = "dialogue.1", Rules = [new()
            { Trigger = TriggerKind.UnitDestroyed, UnitId = "unit.1", Effect = EventEffectKind.SetMorale, FactionId = "winner", Value = -3 }] }] };
        var paused = WorldEvents.Process(eventState, TriggerKind.UnitDestroyed, "unit.1").State;
        Check(paused.Units.Single(u => u.Owner == "winner").Morale == new MoraleState(-3, 3, -3), "death event morale");
        Reject(() => WorldEvents.Process(paused, TriggerKind.Round), "event.dialogue_pending");
        Reject(() => WorldEvents.Acknowledge(paused, "wrong"), "event.dialogue_mismatch");
        var acknowledged = WorldEvents.Acknowledge(paused, "dialogue.1");
        Check(WorldEvents.Process(acknowledged, TriggerKind.UnitDestroyed, "unit.1").Notices.Count == 0, "event only once");
        var alliedOccupant = example with { Units = [example.Units[1] with { Cell = 1 }] };
        Check(WorldEvents.Surrender(alliedOccupant, "loser", "winner").State.Units.Count == 1, "modern surrender preserves other factions");
        Check(WorldEvents.Surrender(alliedOccupant, "loser", "winner", true).State.Units.Count == 0, "explicit native removal policy");
        Reject(() => WorldEvents.Surrender(example, "loser", "loser"), "surrender.faction");
        var stacked = example with { Factions = example.Factions.Select(f => f with { Camp = "a" }).ToArray(),
            Units = [example.Units[0], example.Units[1] with { Cell = 0 }] };
        Reject(stacked.Validate, "world.capacity");
        (stacked with { Limits = new() { UnitsPerCell = 2 } }).Validate();
        Check(true, "stack capacity is configured");
        Reject((example with { Limits = new() { MaximumCells = 8 } }).Validate, "world.header");
        var cloned = example.Copy();
        ((Dictionary<string, float>)example.Units[0].Combatant.Profile.DamageMultipliers)["infantry"] = 3;
        Check(cloned.Units[0].Combatant.Profile.DamageMultipliers["infantry"] == 1, "snapshot owns nested collections");
        ((Dictionary<string, float>)example.Units[0].Combatant.Profile.DamageMultipliers)["infantry"] = 1;

        byte[] bytes = WorldSave.Encode(paused);
        var restored = WorldSave.Decode(bytes, "fixture.1");
        Check(bytes.SequenceEqual(WorldSave.Encode(restored)), "deterministic save roundtrip including RNG and dialogue");
        Check(restored.AiRandomState == 1926 && restored.RandomState == paused.RandomState, "AI and combat RNG states independent");
        Check(restored.Cells[0].Name == "西安 Xi'an" && restored.Factions[0].Name == "测试甲", "Unicode and apostrophe retained");
        Reject(() => WorldSave.Decode(bytes, "wrong"), "save.content_revision");
        Reject(() => WorldSave.Decode(bytes, "fixture.1", new(20)), "save.size");
        string json = Encoding.UTF8.GetString(bytes);
        string versionField = "\"version\":" + WorldSave.Version;
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace(versionField, "\"version\":99")), "fixture.1"), "save.version");
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace(versionField, "\"version\":1")), "fixture.1"), "save.version");
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace("\"round\":1", "\"round\":2")), "fixture.1"), "save.checksum");
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace(versionField, versionField + "," + versionField)), "fixture.1"), "save.duplicate_property");
        Reject(() => WorldSave.Decode(bytes[..^4], "fixture.1"), "save.malformed");
        Reject(() => WorldSave.Decode(Encoding.UTF8.GetBytes(json.Replace("\"aiRandomState\":1926,", "")), "fixture.1"), "save.required_field");
        string testDirectory = Path.Combine(root, "rewrite/reports/save-check");
        Directory.CreateDirectory(testDirectory);
        string save = Path.Combine(testDirectory, "世界状态.json");
        WorldSave.WriteAtomic(save, example);
        WorldSave.WriteAtomic(save, paused);
        Check(WorldSave.Read(save, "fixture.1").PendingDialogue == "dialogue.1", "atomic replace load");
        Check(WorldSave.Read(save + ".bak", "fixture.1").PendingDialogue == null, "previous save preserved");
        Reject(() => WorldSave.WriteAtomic(save, example with { Round = 0 }), "world.header");
        Check(WorldSave.Read(save, "fixture.1").PendingDialogue == "dialogue.1", "failed save leaves old file intact");
        Check(Directory.GetFiles(testDirectory, "*.tmp").Length == 0, "no temporary save remains");
        return checks;
    }
}
