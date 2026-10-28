using System.Text.Json;
using Ew4.Core;
using Ew4.Client;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class DiplomacyHomelandChecks
{
    public static int Run(WorldState basis, string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Homeland: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, label: "reject " + code + " got " + error.Code); return; }
            throw new InvalidOperationException("Homeland: accepted " + code);
        }
        var world = basis with { ScenarioPlayerFaction = "winner", Factions = basis.Factions.Select(f => f with {
            Appearance = new("fra", null, "#112233") }).ToArray() };
        WorldState Event(WorldState state, ScenarioEffect first, params ScenarioEffect[] effects)
        {
            var pending = state with { ScenarioEvents = state.ScenarioEvents.Append(new ScriptedScenarioEvent {
                Id = "homeland." + state.ScenarioEvents.Count, Effects = new[] { first }.Concat(effects).ToArray() }).ToArray() };
            ScenarioRules.Validate(pending, true);
            return ScenarioRules.Evaluate(pending).State;
        }
        var changed = Event(world, new() { Type = "faction_homeland", Faction = "loser", Cells = [1, 2] });
        Check(changed.Demands!.Buildings.Where(b => b.Faction == "loser").Select(b => b.Cell).SequenceEqual(new[] {1, 2}), "event replaces only target homeland");
        Check(DemandDiplomacy.Quote(changed, "winner", "loser").Initial == 2, "quote uses event baseline");
        Check(changed.Factions.Single(f => f.Id == "loser") with { HomelandBuildings = null } == world.Factions.Single(f => f.Id == "loser"), "homeland-only preserves all other faction fields");
        Check(ReferenceEquals(changed.Cells, world.Cells) && ReferenceEquals(changed.Units, world.Units), "homeland never transfers land or units");
        Check(changed.Demands.Buildings.Where(b => b.Faction != "loser").SequenceEqual(world.Demands!.Buildings.Where(b => b.Faction != "loser")), "other homeland bindings preserved");
        foreach (var effect in new[] {
            new ScenarioEffect { Type = "faction_identity", Faction = "loser", Name = "new name" },
            new ScenarioEffect { Type = "faction_identity", Faction = "loser", Flag = "rus" },
            new ScenarioEffect { Type = "faction_identity", Faction = "loser", Color = "#AABBCC" },
            new ScenarioEffect { Type = "faction_identity", Faction = "loser", FlagImage = "rewrite/images/custom.png" }
        })
        {
            var updated = Event(changed, effect);
            var before = changed.Factions.Single(f => f.Id == "loser");
            var after = updated.Factions.Single(f => f.Id == "loser");
            Check(ReferenceEquals(updated.Demands, changed.Demands) && ReferenceEquals(after.HomelandBuildings, before.HomelandBuildings), "identity never recalculates homeland " + effect);
            Check(after with { Name = before.Name, Appearance = before.Appearance } == before, "identity preserves gameplay fields " + effect);
            Check(effect.Name != null ? after.Appearance == before.Appearance
                : after.Name == before.Name && (effect.Color != null ? after.Appearance!.Flag == before.Appearance!.Flag
                    : after.Appearance!.Color == before.Appearance!.Color), "single display edit preserves unspecified display fields");
        }
        var controlled = Event(changed, new() { Type = "player_control", Faction = "loser" });
        Check(controlled.ScenarioPlayerFaction == "loser" && ReferenceEquals(controlled.Demands, changed.Demands), "player switch does not move homeland identity");
        var shared = Event(changed, new() { Type = "faction_homeland", Faction = "ally", Cells = [1] });
        shared.Validate();
        Check(DemandDiplomacy.Quote(shared, "winner", "ally").Initial == 1 && DemandDiplomacy.Quote(shared, "winner", "loser").Initial == 2, "factions can explicitly claim same building independently");
        var noHomes = Event(changed, new() { Type = "faction_homeland", Faction = "loser", Cells = [] });
        Check(DemandDiplomacy.Quote(noHomes, "winner", "loser").DemandBlocked == "no_buildings", "empty array explicitly clears homeland");
        var pendingWorld = world with { Demands = world.Demands! with { Ready = false, Buildings = [], Progress = [] } };
        var pendingChange = Event(pendingWorld, new() { Type = "faction_homeland", Faction = "loser", Cells = [2] });
        var initialized = DemandDiplomacy.Initialize(pendingChange);
        Check(initialized.Demands!.Buildings.Where(b => b.Faction == "loser").Single().Cell == 2, "opening events override before automatic baseline freezes");
        var legacy = Event(world with { Demands = new() { Legacy = true } }, new() { Type = "faction_homeland", Faction = "loser", Cells = [2] });
        Check(legacy.Demands!.Legacy && !legacy.Demands.Ready, "event does not invent missing legacy history");
        var captured = world with { Cells = world.Cells.Select((c, i) => i == 1 ? c with { Owner = "winner" } : c).ToArray() };
        captured = DemandDiplomacy.Captured(world, captured, "winner", [1]);
        captured = captured with { Demands = captured.Demands! with { Rules = new() { MaximumChance = 0 } } };
        var failed = DemandDiplomacy.Demand(captured, captured.Revision, "winner", "loser").State;
        var retargeted = Event(failed, new() { Type = "faction_homeland", Faction = "loser", Cells = [0, 2] },
            new ScenarioEffect { Type = "faction_homeland", Faction = "loser", Cells = [0, 1, 2] });
        Check(retargeted.Demands!.Progress.SequenceEqual(failed.Demands!.Progress) && retargeted.Demands.Seed == failed.Demands.Seed, "editing and restoring list preserves all attempt history");
        Check(DemandDiplomacy.Quote(retargeted, "winner", "loser").DemandBlocked == "progress", "homeland edit does not unlock failed demand");
        var newCredit = retargeted with { Cells = retargeted.Cells.Select((c, i) => i == 2 ? c with { Owner = "winner" } : c).ToArray() };
        newCredit = DemandDiplomacy.Captured(retargeted, newCredit, "winner", [2]);
        Check(DemandDiplomacy.Quote(newCredit, "winner", "loser").DemandBlocked == null, "new military capture after edit unlocks retry");
        var removedCredit = Event(newCredit, new() { Type = "faction_homeland", Faction = "loser", Cells = [1] });
        Check(DemandDiplomacy.Quote(removedCredit, "winner", "loser").DemandBlocked == "progress", "removed homeland cannot supply current retry credit");
        var roundtrip = WorldSave.Decode(WorldSave.Encode(removedCredit), removedCredit.ContentRevision);
        Check(roundtrip.Factions.Single(f => f.Id == "loser").HomelandBuildings!.SequenceEqual(new[] {1}) && roundtrip.Demands!.Progress.Single().Captured.SequenceEqual(new[] {1, 2}), "save preserves overrides and retired historical captures");
        var copied = changed.Copy();
        ((int[])copied.Factions.Single(f => f.Id == "loser").HomelandBuildings!)[0] = 3;
        Check(changed.Factions.Single(f => f.Id == "loser").HomelandBuildings![0] == 1, "snapshot isolates homeland array");
        foreach (int[] invalid in new[] { new[] {4}, new[] {-1}, new[] {world.Cells.Count}, new[] {1, 1} })
            Reject(() => DemandDiplomacy.SetHomeland(world, "loser", invalid), "scenario.homeland");
        Reject(() => Event(world, new() { Type = "faction_homeland", Faction = "loser", Cells = [4] }), "scenario.homeland");
        var eventDefinition = new ScriptedScenarioEvent { Id = "homeland.rollback", Effects = [
            new() { Type = "faction_homeland", Faction = "loser", Cells = [1] },
            new() { Type = "money", Faction = "loser", Value = -long.MaxValue }],
            InsufficientResourcesEffects = [new() { Type = "state", Key = "scenario/shortage", Value = 1 }] };
        var rollback = ScenarioRules.Evaluate(world with { ScenarioEvents = [eventDefinition] });
        Check(ReferenceEquals(rollback.State.Demands, world.Demands) && rollback.State.Factions.All(f => f.HomelandBuildings == null)
            && rollback.Notices.All(n => n.Kind != "faction.homeland"), "failed event rolls back homeland and notice atomically");
        var creation = new ScenarioEffect { Type = "faction_create", Faction = "rebels", Name = "rebels", Camp = "rebels", TemplateFaction = "loser" };
        var created = Event(changed, creation, new ScenarioEffect { Type = "faction_homeland", Faction = "rebels", Cells = [3] });
        Check(created.Factions.Single(f => f.Id == "rebels").HomelandBuildings!.SequenceEqual(new[] {3})
            && created.Factions.Single(f => f.Id == "loser").HomelandBuildings!.SequenceEqual(new[] {1, 2}), "created faction independently binds homeland");
        var unbound = Event(changed, creation);
        Check(unbound.Factions.Single(f => f.Id == "rebels").HomelandBuildings == null
            && unbound.Demands!.Buildings.All(b => b.Faction != "rebels"), "template does not copy homeland to new faction");
        Check(!System.Text.Encoding.UTF8.GetString(WorldSave.Encode(world)).Contains("homelandBuildings"), "omitted override preserves old save payload shape");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var content = new ClientContent(Read, ["conquest1.btl"]);
        var original = content.Start("conquest1", "fra");
        var victim = original.Session.World.Factions.First(f => f.Id != original.Session.PlayerFaction
            && original.Session.World.Cells.Any(c => c.Owner == f.Id && DemandDiplomacy.Counts(c)));
        int building = original.Session.World.Cells.Select((c, i) => (c, i)).First(p => p.c.Owner == victim.Id && DemandDiplomacy.Counts(p.c)).i;
        var overrides = new Dictionary<string, byte[]> {
            ["rewrite/extension.json"] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, apiVersion = 3, scenarioEvents = "rewrite/homeland-events.json" }),
            ["rewrite/homeland-events.json"] = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, ScriptedScenarioEvent[]> {
                ["conquest1"] = [new() { Id = "homeland.choice", Popup = new() { Title = "homeland", Text = "change", Choices = [
                    new() { Id = "only", Text = "homeland only", Effects = [new() { Type = "faction_homeland", Faction = victim.Id, Cells = [building] }] }
                ] } }]
            }, ExtensionLoader.Json)
        };
        content = new ClientContent(p => overrides.GetValueOrDefault(p) ?? Read(p), ["conquest1.btl"]);
        var host = new ClientGame(content, Path.Combine(root, "build/diplomacy-20261028/homeland-client"));
        JsonElement Call(string route, object request)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(request, ClientGame.Json)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), "client " + route);
            return response.RootElement.GetProperty("data").Clone();
        }
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        var factionBefore = view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == victim.Id).GetRawText();
        view = Call("action", new { op = "event_choice", offer = "homeland.choice", target = "only", revision = view.GetProperty("revision").GetInt64() });
        Check(view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == victim.Id).GetRawText() == factionBefore,
            "actual event choice changes homeland without changing faction view");
        var quote = Call("diplomacy", new { }).GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == victim.Id).GetProperty("quote");
        Check(quote.GetProperty("initial").GetInt32() == 1 && quote.GetProperty("remaining").GetInt32() == 1, "actual client quote uses chosen homeland");
        Call("save", new { slot = 1, mode = 2 }); Call("load", new { slot = 1, mode = 2 });
        Check(Call("diplomacy", new { }).GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == victim.Id)
            .GetProperty("quote").GetProperty("initial").GetInt32() == 1, "actual client save reload retains event homeland");
        return checks;
    }
}
