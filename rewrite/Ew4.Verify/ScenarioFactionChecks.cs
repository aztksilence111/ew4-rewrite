using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ScenarioFactionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Scenario factions: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + " got " + error.Code); return; }
            throw new Exception("Scenario factions accepted " + code);
        }
        var basis = WorldChecks.Example() with { ScenarioPlayerFaction = "winner",
            Cells = WorldChecks.Example().Cells.Select((c, i) => c with { BuildingKind = i is 0 or 3 or 8 ? "city" : null }).ToArray(),
            Units = WorldChecks.Example().Units.Select(u => u.Id == "unit.1" ? u with { Cell = 3, CommanderId = "general" }
                : u with { Cell = 1, RemainingMovement = 6 }).ToArray(), Commanders = [new() { Id = "general", Owner = "loser" }] };
        var creation = new ScenarioEffect { Type = "faction_create", Faction = "rebel", TemplateFaction = "loser", Name = "叛军", Camp = "rebel" };
        var rebellion = new ScriptedScenarioEvent { Id = "rebellion", Condition = new() { Type = "cells_owned", Cells = [0] }, Effects = [
            creation, new() { Type = "unit_owner", Unit = "unit.1", Faction = "rebel" }, new() { Type = "cells_owner", Cells = [3], Faction = "rebel" },
            new() { Type = "money", Faction = "rebel", Value = 100 }, new() { Type = "relation", Faction = "rebel", OtherFaction = "loser", Relation = "hostile" }] };
        var before = basis with { ScenarioEvents = [rebellion] };
        ScenarioRules.Validate(before, true);
        Check(ScenarioRules.Evaluate(before).State.Factions.Count == 2, "reserved faction is absent before capture");
        foreach (string type in new[] { "faction_exists", "faction_active", "faction_defeated", "money", "faction_camp", "cells_owned", "relation" })
            Check(!ScenarioRules.Match(before, new() { Type = type, Faction = "rebel", OtherFaction = "loser", Cells = [0], Compare = "eq", Value = 0 }), "absent faction query " + type);
        var moved = WorldActions.Move(before, before.Revision, "unit.2", 0, new() { PlayerCamp = "b" }).State;
        Check(moved.Cells[0].Owner == "winner" && moved.Factions.Count == 3, "real capture triggers new faction before victory evaluation");
        Check(moved.Factions[^1].Id == "rebel" && !moved.Factions[^1].Human && moved.ActiveFaction == "winner", "new AI appended without interrupting active turn");
        Check(moved.Units.Single(u => u.Id == "unit.1").Owner == "rebel" && moved.Commanders[0].Owner == "rebel" && moved.Cells[3].Owner == "rebel", "land army and commander transferred together");
        Check(moved.Factions[^1].Resources == new Resources(100, 0, 0) && WorldDiplomacy.Relation(moved, "rebel", "loser") == "hostile", "fresh resources and diplomacy");
        Check(WorldVictory.Facts(moved, []).Any(f => f.Id == "rebel" && !f.Defeated), "new faction included in victory facts");
        var turns = new WorldTurnRules { Buildings = [new("city", 0, new(2, 0, 0))] };
        var rebelTurn = WorldTurns.EndTurn(moved, moved.Revision, turns).State;
        Check(rebelTurn.ActiveFaction == "rebel" && rebelTurn.Round == 1 && rebelTurn.Factions[^1].Resources.Money == 100, "new faction receives turn with original first-round income exemption");
        var nextRebelTurn = rebelTurn;
        for (int i = 0; i < 3; i++) nextRebelTurn = WorldTurns.EndTurn(nextRebelTurn, nextRebelTurn.Revision, turns).State;
        Check(nextRebelTurn.ActiveFaction == "rebel" && nextRebelTurn.Round == 2 && nextRebelTurn.Factions[^1].Resources.Money == 102, "later round pays exactly one new-faction income");
        var saved = WorldSave.Decode(WorldSave.Encode(rebelTurn), rebelTurn.ContentRevision);
        Check(saved.ActiveFaction == "rebel" && saved.Factions.Count == 3 && ScenarioRules.Evaluate(saved).Notices.Count == 0, "save resumes new faction without duplicate creation");
        Check(before.Factions.Count == 2 && before.Units[0].Owner == "loser", "input remains unchanged");
        Reject(() => ScenarioRules.Evaluate(basis with { ScenarioEvents = [rebellion with { Condition = new(), Effects = [creation, creation] }] }), "scenario.faction_exists");
        Reject(() => ScenarioRules.Validate(basis with { ScenarioEvents = [rebellion with { Effects = [creation, creation] }] }, true), "scenario.faction_definition");
        Reject(() => ScenarioRules.Validate(basis with { ScenarioEvents = [rebellion with { Effects = [creation with { Faction = "loser" }] }] }, true), "scenario.faction_definition");
        Reject(() => ScenarioRules.Validate(basis with { ScenarioEvents = [rebellion with { Effects = [creation with { TemplateFaction = "missing" }] }] }, true), "scenario.faction_definition");
        Reject(() => ScenarioRules.Evaluate(basis with { ScenarioEvents = [rebellion with { Condition = new(), Effects = [new() { Type = "money", Faction = "rebel", Value = 1 }, creation] }] }), "scenario.faction_missing");
        var shortage = rebellion with { Condition = new(), Effects = [creation, new() { Type = "money", Faction = "rebel", Value = -1 }],
            InsufficientResourcesEffects = [new() { Type = "state", Key = "scenario/shortage", Value = 1 }] };
        var rollback = ScenarioRules.Evaluate(basis with { ScenarioEvents = [shortage] });
        Check(rollback.State.Factions.Count == 2 && rollback.Notices.All(n => n.Kind != "faction.created"), "failed transaction rolls back new faction and notices");
        var eliminated = WorldEvents.Surrender(basis, "loser", "winner").State;
        var revive = new ScriptedScenarioEvent { Id = "return", Effects = [new() { Type = "faction_revive", Faction = "loser" },
            new() { Type = "cells_owner", Faction = "loser", Cells = [3] }] };
        var revived = ScenarioRules.Evaluate(eliminated with { ScenarioEvents = [revive] }).State;
        Check(!revived.Factions[0].Defeated && revived.Cells[3].Owner == "loser", "defeated country revives and receives territory");
        Check(revived.Units.All(u => u.Owner != "loser") && revived.Commanders[0].Withdrawn, "revival does not resurrect old armies or generals");
        Check(WorldTurns.EndTurn(revived, revived.Revision, turns).State.ActiveFaction == "loser", "revived faction rejoins original turn position");
        Reject(() => ScenarioRules.Evaluate(basis with { ScenarioEvents = [revive] }), "scenario.faction_active");
        var identity = new ScenarioEffect { Type = "faction_identity", Faction = "loser", Name = "新国号", Color = "#237A4D80", FlagImage = "rewrite/images/rebel.png" };
        var renamed = ScenarioRules.Evaluate(basis with { ScenarioEvents = [new() { Id = "identity", Effects = [identity] }] }).State;
        Check(renamed.Factions[0].Name == "新国号" && renamed.Factions[0].Appearance == new FactionAppearance(null, identity.FlagImage, identity.Color), "identity updates all three fields");
        Check(renamed.Factions[0].Camp == basis.Factions[0].Camp && renamed.Factions[0].Resources == basis.Factions[0].Resources && renamed.Units[0].Owner == "loser", "identity preserves political ownership and resources");
        Check(ScenarioFactions.PackedColor("#237A4D80") == unchecked((int)0x804D7A23) && ScenarioFactions.PackedColor("#010203") == unchecked((int)0xFF030201), "RGB and RGBA packing matches map renderer");
        var renamedSaved = WorldSave.Decode(WorldSave.Encode(renamed), renamed.ContentRevision);
        Check(renamedSaved.Factions[0].Appearance == renamed.Factions[0].Appearance && renamedSaved.Factions[0].Name == "新国号", "identity persists through world save");
        var oldSave = JsonNode.Parse(WorldSave.Encode(basis))!;
        foreach (var f in oldSave["payload"]!["factions"]!.AsArray()) f!.AsObject().Remove("appearance");
        byte[] oldPayload = JsonSerializer.SerializeToUtf8Bytes(oldSave["payload"]);
        oldSave["sha256"] = Convert.ToHexStringLower(SHA256.HashData(oldPayload));
        Check(WorldSave.Decode(JsonSerializer.SerializeToUtf8Bytes(oldSave), basis.ContentRevision).Factions.All(f => f.Appearance == null), "older save without appearance fields still loads");
        var flagOnly = new ScriptedScenarioEvent { Id = "flag", Effects = [identity with { Name = null, Color = null, FlagImage = null, Flag = "rus" }] };
        var switched = ScenarioRules.Evaluate(renamed with { ScenarioEvents = [.. renamed.ScenarioEvents, flagOnly] }).State;
        Check(switched.Factions[0].Appearance == new FactionAppearance("rus", null, identity.Color) && switched.Factions[0].Name == "新国号", "partial identity update preserves other fields and switches image to atlas");
        foreach (var bad in new[] { identity with { Color = "red" }, identity with { Color = "#GG0000" }, identity with { FlagImage = "rewrite/../flag.png" },
            identity with { FlagImage = "https://example.com/flag.png" }, identity with { Flag = "../rus" }, identity with { Flag = "rus" } })
            Reject(() => ScenarioRules.ValidateDefinitions([new() { Id = "bad", Effects = [bad] }]), "scenario.appearance");
        Reject(() => ScenarioRules.ValidateDefinitions([new() { Id = "empty", Effects = [new() { Type = "faction_identity" }] }]), "scenario.effect");
        // Real client loading resolves independent unit variants and package PNG flags.
        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string path) => overrides.TryGetValue(path, out var value) ? value : File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        void Put(string path, object value) => overrides[path] = JsonSerializer.SerializeToUtf8Bytes(value, ExtensionLoader.Json);
        Put("rewrite/extension.json", new { schema = 1, apiVersion = 1, scenarioEvents = "rewrite/events.json" });
        Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]>());
        overrides[identity.FlagImage!] = Read("client/ui/image_flag_hd/rus1.png");
        var catalog = Ew4.Legacy.LegacyCatalogFiles.Load(Read).Catalog;
        catalog["variants"]!["rebellion"] = catalog["variants"]!["rus"]!.DeepClone();
        overrides["data/catalog.json"] = JsonSerializer.SerializeToUtf8Bytes(catalog);
        var content = new ClientContent(Read, ["conquest1.btl"]);
        var original = content.Start("conquest1", "fra");
        string victim = original.Session.World.Factions.First(f => f.Id != original.Session.PlayerFaction).Id;
        var army = original.Session.World.Units.First(u => u.Owner == victim && original.Session.World.Cells[u.Cell].BuildingKind != null);
        var independent = creation with { TemplateFaction = null, UnitVariant = "rebellion", FlagImage = identity.FlagImage, Color = "#237A4D" };
        ScenarioEffect[] uprisingEffects = [
            independent, new() { Type = "unit_owner", Faction = "rebel", Unit = army.Id }, new() { Type = "cells_owner", Faction = "rebel", Cells = [army.Cell] },
            new() { Type = "money", Faction = "rebel", Value = 1000 }, new() { Type = "industry", Faction = "rebel", Value = 1000 }];
        var opening = new ScriptedScenarioEvent { Id = "uprising", Popup = new() { Title = "地方起义", Text = "地方驻军请求宣布独立。", Choices = [
            new() { Id = "yes", Text = "承认独立", Effects = uprisingEffects }, new() { Id = "no", Text = "维持原状" }] } };
        Put("rewrite/events.json", new Dictionary<string, ScriptedScenarioEvent[]> { ["conquest1"] = [opening,
            new() { Id = "rename", Condition = new() { Type = "faction_exists", Faction = "rebel" }, Effects = [identity with { Faction = "rebel", Name = "独立共和国" },
                identity with { Faction = "@player", Name = "新法兰西" }] }] });
        content = new ClientContent(Read, ["conquest1.btl"]);
        string folder = Path.Combine(root, "rewrite/reports/scenario-factions-20261021"); Directory.CreateDirectory(folder);
        var host = new ClientGame(content, Path.Combine(folder, "client-" + Guid.NewGuid().ToString("N")));
        JsonElement Call(string route, object body)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(), route + ": " + doc.RootElement.GetRawText()[..Math.Min(240, doc.RootElement.GetRawText().Length)]);
            return doc.RootElement.GetProperty("data").Clone();
        }
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        Check(view.GetProperty("factions").GetArrayLength() == original.Session.World.Factions.Count, "pending choice keeps independent country hidden");
        view = Call("action", new { op = "event_choice", offer = "uprising", target = "no", revision = view.GetProperty("revision").GetInt64() });
        Check(!view.GetProperty("factions").EnumerateArray().Any(f => f.GetProperty("id").GetString() == "rebel"), "declining button creates no faction");
        view = Call("start", new { scenario = "conquest1", country = "fra" });
        Call("save", new { mode = 2, slot = 1 }); view = Call("load", new { mode = 2, slot = 1 });
        view = Call("action", new { op = "event_choice", offer = "uprising", target = "yes", revision = view.GetProperty("revision").GetInt64() });
        var faction = view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == "rebel");
        Check(faction.GetProperty("name").GetString() == "独立共和国" && faction.GetProperty("flagImage").GetString() == identity.FlagImage
            && faction.GetProperty("color").GetInt32() == ScenarioFactions.PackedColor(identity.Color!), "client projects chained name color and custom flag");
        Check(view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == view.GetProperty("playerFaction").GetString())
            .GetProperty("name").GetString() == "新法兰西", "existing player country identity changes through same interface");
        Call("save", new { mode = 2, slot = 1 }); view = Call("load", new { mode = 2, slot = 1 });
        Check(view.GetProperty("factions").EnumerateArray().Count(f => f.GetProperty("id").GetString() == "rebel") == 1, "client load restores exactly one country");
        var configured = content.Start("conquest1", "fra");
        Check(configured.Rules.Production["rebel"].Offers.Count > 0 && configured.Rules.Recruitment["rebel"].Count > 0, "independent unit variant has production policies");
        var chosen = ScenarioRules.Choose(configured.Session.World, configured.Session.World.Revision, "uprising", "yes").State;
        Check(chosen.Factions[^1].Recruitment.Count == configured.Rules.Recruitment["rebel"].Count, "independent variant enables recruitment grants");
        var aiWorld = chosen with { ActiveFaction = "rebel", Phase = TurnPhase.Active };
        var step = WorldAi.PlanStep(aiWorld, configured.Rules.Ai, configured.Rules.Production["rebel"], configured.Rules.Turns);
        var aiResult = WorldAi.ExecuteStep(aiWorld, step, configured.Rules.Ai, configured.Rules.Production["rebel"], configured.Rules.Turns, configured.Rules.Battle);
        Check(aiResult.State.Revision > aiWorld.Revision, "independent faction executes actual AI command");
        string fixture = Path.Combine(folder, "raw-fixture"); Directory.CreateDirectory(fixture);
        foreach (var p in overrides) { string target = Path.Combine(fixture, p.Key.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p.Key[5..] : p.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, p.Value); }
        File.WriteAllText(Path.Combine(folder, "core.json"), JsonSerializer.Serialize(new { checks, errors = 0, fixture, army = army.Id, cell = army.Cell }));
        return checks;
    }
}
