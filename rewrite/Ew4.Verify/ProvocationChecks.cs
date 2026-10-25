using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ProvocationChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Provocation: " + label); }
        var seeds = new Dictionary<(int, int), ulong>();
        ulong Seed(int first, int second)
        {
            if (seeds.TryGetValue((first, second), out ulong found)) return found;
            for (ulong seed = 0; seed < 1000000; seed++)
            {
                var random = new Ew4Random(seed);
                if (random.Next(100) == first && random.Next(100) == second) return seeds[(first, second)] = seed;
            }
            throw new InvalidOperationException("No matching test seed");
        }
        WorldState Example(int sourceCamp, int oldCamp, bool warned, ulong seed, int mode = 1) => WorldChecks.Example() with {
            RandomState = seed, DiplomacyNeutralCamps = ["4"], Provocations = mode == 3 ? null : new("4", ["1", "2", "3"]),
            Factions = WorldChecks.Example().Factions.Select(f => f with { Camp = (f.Human ? sourceCamp : oldCamp).ToString(),
                DiplomacyWarned = !f.Human && warned }).ToArray() };
        using (var vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "reverse/reports/provocation_capture_vectors.json"))))
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            int N(string name) => vector.GetProperty(name).GetInt32();
            var before = Example(N("sourceCamp"), N("oldCamp"), vector.GetProperty("warned").GetBoolean(), Seed(N("draw"), N("choice")), N("mode"));
            before = before with { Cells = before.Cells.Select((c, i) => i == 1 ? c with { BuildingKind = N("building") == 4 ? "farmland" : "city" } : c).ToArray() };
            var after = before with { Cells = before.Cells.Select((c, i) => i == 1 ? c with { Owner = "winner" } : c).ToArray() };
            var change = WorldProvocations.Captured(before, after, 1);
            var victim = change.State.Factions.Single(f => f.Id == "loser");
            var result = vector.GetProperty("result");
            Check(victim.Camp == result[0].GetInt32().ToString() && victim.DiplomacyWarned == result[1].GetBoolean(), "native capture camp and warning");
            var random = new Ew4Random(before.RandomState);
            for (int i = 0; i < N("draws"); i++) random.Next(100);
            Check(change.State.RandomState == random.State, "native capture RNG consumption");
            Check(result[2].GetArrayLength() == 0 ? change.Notices.Count == 0
                : change.Notices[0].Kind == "diplomacy." + new[] { "alliance_protest", "alliance_neutral", "neutral_protest", "neutral_declarewar" }[result[2][0].GetInt32()], "native capture notice");
        }
        using (var vectors = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "reverse/reports/provocation_attack_vectors.json"))))
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            int N(string name) => vector.GetProperty(name).GetInt32();
            var before = Example(N("sourceCamp"), 4, vector.GetProperty("warned").GetBoolean(), Seed(N("draw"), N("choice")), N("mode"));
            // Tutorial mode has no original provocation policy, so there is no modern neutral hostility fallback in this probe.
            var change = N("mode") == 3 ? new WorldChange(before, []) : WorldProvocations.Attacked(before, "winner", "loser", ["4"]);
            var victim = change.State.Factions.Single(f => f.Id == "loser");
            var result = vector.GetProperty("result");
            Check(victim.Camp == result[0].GetInt32().ToString() && victim.DiplomacyWarned == result[1].GetBoolean(), "native attack camp and warning");
            var random = new Ew4Random(before.RandomState);
            for (int i = 0; i < N("draws"); i++) random.Next(100);
            Check(change.State.RandomState == random.State, "native attack RNG consumption");
            Check(N("mode") == 3 || !WorldDiplomacy.Provoked(change.State, "winner", "loser"), "protest does not force premature bilateral war");
        }

        var baseWorld = Example(1, 1, false, Seed(0, 0));
        baseWorld = baseWorld with { Cells = baseWorld.Cells.Select((c, i) => i == 1 ? c with { BuildingKind = "city" } : c).ToArray(),
            Units = baseWorld.Units.Select(u => u.Id == "unit.2" ? u with { Cell = 2, RemainingMovement = 6 } : u).ToArray() };
        var moved = WorldActions.Move(baseWorld, baseWorld.Revision, "unit.2", 1).State;
        Check(moved.Factions.Single(f => f.Id == "loser").DiplomacyWarned, "real movement triggers first allied building warning");
        Check(baseWorld.Factions.All(f => !f.DiplomacyWarned), "capture does not mutate its input");
        var eventBefore = baseWorld with { Events = [new() { Id = "capture", Rules = [new() { Trigger = TriggerKind.CellCaptured, TriggerCell = 1 }] }] };
        var eventAfter = eventBefore with { Cells = moved.Cells, TriggeredEvents = ["capture"] };
        Check(!WorldProvocations.Captured(eventBefore, eventAfter, 1).State.Factions.Single(f => f.Id == "loser").DiplomacyWarned,
            "native capture event suppresses first warning");
        eventBefore = eventBefore with { Factions = moved.Factions, RandomState = Seed(29, 0) };
        eventAfter = eventBefore with { Cells = moved.Cells, TriggeredEvents = ["capture"] };
        var scripted = WorldProvocations.Captured(eventBefore, eventAfter, 1);
        Check(scripted.State.Factions.Single(f => f.Id == "loser").Camp == "4" && scripted.Notices.Count == 0,
            "native event suppresses speech but preserves escalation of existing warning");
        var aiCapture = baseWorld with { Factions = baseWorld.Factions.Select(f => f with { Human = false }).ToArray() };
        Check(ReferenceEquals(WorldProvocations.Captured(aiCapture, aiCapture with { Cells = moved.Cells }, 1).State.Factions, aiCapture.Factions),
            "AI captures do not provoke player diplomacy");
        var restored = WorldSave.Decode(WorldSave.Encode(moved), moved.ContentRevision);
        Check(restored.Factions.Single(f => f.Id == "loser").DiplomacyWarned && restored.Provocations?.NeutralCamp == moved.Provocations!.NeutralCamp
            && restored.Provocations!.CombatantCamps.SequenceEqual(moved.Provocations.CombatantCamps), "warning and provocation policy persist in save");
        var secondBefore = baseWorld with { Factions = moved.Factions, RandomState = Seed(29, 0) };
        var secondAfter = secondBefore with { Cells = moved.Cells };
        var neutralized = WorldProvocations.Captured(secondBefore, secondAfter, 1).State;
        Check(neutralized.Factions.Single(f => f.Id == "loser") is { Camp: "4", DiplomacyWarned: false }, "second allied incident can break alliance and clear warning");
        var protest = WorldProvocations.Attacked(neutralized, "winner", "loser", ["4"]).State;
        Check(protest.Factions.Single(f => f.Id == "loser") is { Camp: "4", DiplomacyWarned: true }, "newly neutral country protests before declaring war");
        var war = WorldProvocations.Attacked(protest with { RandomState = Seed(49, 0) }, "winner", "loser", ["4"]).State;
        Check(WorldDiplomacy.Hostile(war, "winner", "loser", ["4"]), "repeated neutral provocation changes camp and starts war");
        var combat = Example(1, 4, false, 0) with { Units = baseWorld.Units.Select(u => u with { Cell = u.Owner == "winner" ? 1 : 0 }).ToArray() };
        var battle = WorldBattle.Attack(combat, combat.Revision, "unit.2", "unit.1", new() { NeutralCamps = ["4"] }).State;
        Check(battle.Factions.Single(f => f.Id == "loser").DiplomacyWarned && !WorldDiplomacy.Hostile(battle, "winner", "loser", ["4"]),
            "real attack records protest without declaring war on first hit");

        var campaign = Example(1, 4, false, 0) with { LocalMultiplayer = new("winner", ["loser", "winner"], ShareAlliedVictory: true),
            Factions = Example(1, 4, false, 0).Factions.Select(f => f with { Human = true })
                .Append(new() { Id = "enemy", Name = "Enemy", Camp = "2" }).ToArray() };
        var localAllied = campaign with { RandomState = Seed(29, 0),
            Cells = baseWorld.Cells, Factions = campaign.Factions.Select(f => f.Id == "loser" ? f with { Camp = "1", DiplomacyWarned = true } : f).ToArray() };
        var localNeutralized = WorldProvocations.Captured(localAllied, localAllied with { Cells = moved.Cells }, 1).State;
        Check(localNeutralized.Factions.Single(f => f.Id == "loser") is { Camp: "4", DiplomacyWarned: false },
            "repeated theft can neutralize a human ally in local campaign");
        var localRejoined = WorldProvocations.Attacked(localNeutralized, "winner", "loser", ["4"]).State;
        Check(localRejoined.Factions.Single(f => f.Id == "loser").Camp == "2", "neutralized human ally joins opponent on first subsequent attack");
        var victory = new VictoryRules(999, true);
        var neutralGoals = campaign with { Cells = campaign.Cells.Select((c, i) => c with { Objective = i == 0, Owner = "loser" }).ToArray() };
        Check(LocalMultiplayerGame.Winner(neutralGoals, victory) == null, "neutral holding all objectives cannot win");
        Check(LocalMultiplayerGame.Finish(neutralGoals, "loser").Outcome == BattleOutcome.Continue, "scripted neutral victory is blocked");
        Check(!LocalMultiplayerGame.Won(neutralGoals, "loser", "loser"), "neutral cannot appear as a winning faction");
        var custom = neutralGoals with { ScenarioVictory = new() { Win = new() { Type = "resources", Resources = new(1, 0, 0) } },
            Factions = neutralGoals.Factions.Select(f => f with { Resources = new(f.Id == "loser" ? 10 : 0, 0, 0) }).ToArray() };
        Check(LocalMultiplayerGame.Winner(custom, victory) == null, "custom goals do not let neutrals win");
        foreach (string opponent in new[] { "winner", "enemy" })
        foreach (bool initiates in new[] { false, true })
        {
            var joined = LocalMultiplayerGame.Engaged(campaign, initiates ? "loser" : opponent, initiates ? opponent : "loser", ["4"]);
            string side = opponent == "winner" ? "enemy" : "winner";
            Check(WorldDiplomacy.Allied(joined, "loser", side) && WorldDiplomacy.Hostile(joined, "loser", opponent, ["4"]), "first attack in either direction chooses opposing side");
            Check(ReferenceEquals(LocalMultiplayerGame.Engaged(joined, side, "loser", ["4"]), joined), "subsequent combat cannot choose a second side");
            var completed = joined with { Cells = neutralGoals.Cells };
            Check(LocalMultiplayerGame.Winner(completed, victory) != null && LocalMultiplayerGame.Won(completed, "loser", LocalMultiplayerGame.Winner(completed, victory)!),
                "joined neutral can share alliance victory");
            var saved = WorldSave.Decode(WorldSave.Encode(joined), joined.ContentRevision);
            Check(saved.Factions.Single(f => f.Id == "loser").Camp == joined.Factions.Single(f => f.Id == "loser").Camp, "joined camp persists in save");
        }
        var defended = campaign with { ActiveFaction = "winner", Units = combat.Units };
        WorldBattle.Preview(defended, defended.Revision, "unit.2", "unit.1", new() { NeutralCamps = ["4"] });
        Check(defended.Factions.Single(f => f.Id == "loser").Camp == "4", "attack preview does not choose a side");
        Check(WorldBattle.Attack(defended, defended.Revision, "unit.2", "unit.1", new() { NeutralCamps = ["4"] }).State
            .Factions.Single(f => f.Id == "loser").Camp == "2", "real attack against neutral human joins opposing camp");
        var initiating = defended with { ActiveFaction = "loser" };
        Check(WorldBattle.Attack(initiating, initiating.Revision, "unit.1", "unit.2", new() { NeutralCamps = ["4"] }).State
            .Factions.Single(f => f.Id == "loser").Camp == "2", "real attack by neutral human joins opposing camp");
        Check(ReferenceEquals(LocalMultiplayerGame.Engaged(campaign with { LocalMultiplayer = campaign.LocalMultiplayer! with { ShareAlliedVictory = false } },
            "loser", "winner", ["4"]).Factions, campaign.Factions), "conquest does not use campaign joining");
        checks += Client(root);
        return checks;
    }

    private static int Client(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Provocation client: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var start = content.Start("conquest1", "fra");
        var world = start.Session.World with { PendingDialogue = null, QueuedDialogues = [] };
        string victim = world.Units.First(u => u.Owner != start.Session.PlayerFaction && u.CommanderId != null).Owner;
        string player = start.Session.PlayerFaction;
        world = world with { Factions = world.Factions.Select(f => f.Id == victim
            ? f with { Camp = world.Factions.Single(p => p.Id == player).Camp } : f).ToArray() };
        int city = world.Cells.Select((c, i) => (c, i)).First(p => p.c.Owner == victim && p.c.BuildingKind is not (null or "farmland")).i;
        var stolen = world with { Cells = world.Cells.Select((c, i) => i == city ? c with { Owner = player } : c).ToArray() };
        var protest = WorldProvocations.Captured(world, stolen, city).State;
        string folder = Path.Combine(root, "build/provocations-20261025/fixtures");
        Directory.CreateDirectory(folder);
        void Save(WorldState state, int slot) => File.WriteAllBytes(Path.Combine(folder, $"slot-{slot}.json"),
            JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(start.Session with { World = state })), ClientGame.Json));
        Save(protest, 2);
        Check(protest.PendingDialogue != null && WorldProvocations.Dialogue(protest, protest.PendingDialogue)?.Kind == "alliance protest", "real country queues original allied protest");
        var acknowledged = WorldEvents.Acknowledge(protest, protest.PendingDialogue!);
        var secondBefore = world with { Factions = acknowledged.Factions, RandomState = 0 };
        ulong FindSeed(int limit)
        {
            for (ulong seed = 0; ; seed++) if (new Ew4Random(seed).Next(100) < limit) return seed;
        }
        secondBefore = secondBefore with { RandomState = FindSeed(30) };
        var neutral = WorldProvocations.Captured(secondBefore, secondBefore with { Cells = stolen.Cells }, city).State;
        Save(neutral, 3);
        var warned = WorldProvocations.Attacked(WorldEvents.Acknowledge(neutral, neutral.PendingDialogue!), player, victim, start.Rules.Battle.NeutralCamps).State;
        Save(warned, 4);
        var declared = WorldProvocations.Attacked(WorldEvents.Acknowledge(warned, warned.PendingDialogue!) with { RandomState = FindSeed(50) },
            player, victim, start.Rules.Battle.NeutralCamps).State;
        Save(declared, 5);
        Save(world with { Provocations = null, Factions = world.Factions.Select(f => f with { DiplomacyWarned = false }).ToArray() }, 6);
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object args)
        {
            using var document = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(args, ClientGame.Json)));
            Check(document.RootElement.GetProperty("ok").GetBoolean(), route + ": " + document.RootElement.GetRawText()[..Math.Min(240, document.RootElement.GetRawText().Length)]);
            return document.RootElement.GetProperty("data").Clone();
        }
        for (int slot = 2; slot <= 5; slot++)
        {
            var view = Call("load", new { slot });
            string text = view.GetProperty("dialogue").GetString()!;
            Check(!text.StartsWith("diplomacy.", StringComparison.Ordinal) && text.Length > 5, "original localized text reaches client");
            var presentation = view.GetProperty("dialoguePresentation");
            Check(presentation.GetProperty("portrait").GetString()!.EndsWith(".png", StringComparison.Ordinal)
                && !presentation.GetProperty("name").GetString()!.StartsWith("name_", StringComparison.Ordinal), "original commander portrait and localized name");
            Call("save", new { slot = 1 });
            var restored = Call("load", new { slot = 1 });
            Check(restored.GetProperty("dialogue").GetString() == text, "pending diplomatic dialogue persists across reload");
            var after = Call("action", new { op = "dialogue", revision = restored.GetProperty("revision").GetInt64() });
            Check(after.GetProperty("dialogue").ValueKind == JsonValueKind.Null, "dialogue can be acknowledged");
        }
        Call("load", new { slot = 6 });
        Call("save", new { slot = 1 });
        var migrated = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(folder, "slot-1.json")), ClientGame.Json)!;
        var session = SessionSave.Decode(migrated.Session, new(start.Session.ContentRevision, start.Session.World.ContentRevision, start.Session.Account.ContentRevision));
        Check(session.World.Provocations != null && session.World.Factions.All(f => !f.DiplomacyWarned), "old unfinished save gains rules without inventing prior warnings");
        File.WriteAllText(Path.Combine(folder, "../client-fixtures.json"), JsonSerializer.Serialize(new { folder, victim, checks }, ClientGame.Json));
        return checks;
    }
}
