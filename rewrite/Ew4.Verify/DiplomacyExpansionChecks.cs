using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class DiplomacyExpansionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Diplomacy expansion: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, "reject " + code + ": " + e.Code); return; }
            throw new InvalidOperationException("Diplomacy expansion: expected " + code);
        }
        var example = WorldChecks.Example();
        var world = example with { Width = 5, Height = 5, ScenarioPlayerFaction = "winner", Units = [],
            Factions = example.Factions.Select(f => f with { Resources = f.Id == "loser" ? new(1001, 501, 49) : new(10, 20, 30) }).ToArray(),
            Cells = Enumerable.Range(0, 25).Select(i => new CellState { Owner = i < 5 ? "loser" : "winner",
                BuildingKind = i switch { 0 => "city", 1 => "industry", 2 => "stable", 3 => "port", 4 => "farmland", 6 => "city", _ => null },
                BuildingLevel = i <= 6 ? 1 : 0 }).ToArray(), Demands = new() { Seed = 99 } };
        world = DemandDiplomacy.Initialize(world); world.Validate();
        Check(world.Factions.Single(f => f.Id == "loser").Capital!.Cell == 0 && world.Factions.Single(f => f.Id == "winner").Capital!.Cell == 6, "default capitals prefer owned cities");
        Check(CapitalTreasury.Percent(world, "winner", "loser") == 20, "default treasury percentage is 20");
        WorldState Events(WorldState state, params ScenarioEffect[] effects)
        {
            var pending = state with { ScenarioEvents = state.ScenarioEvents.Append(new ScriptedScenarioEvent {
                Id = "expansion." + state.ScenarioEvents.Count, Effects = effects }).ToArray() };
            ScenarioRules.Validate(pending, true); return ScenarioRules.Evaluate(pending).State;
        }
        var occupied = world with { Cells = world.Cells.Select((c, i) => i == 1 ? c with { Owner = "winner" } : c).ToArray() };
        var claims = Events(occupied, new ScenarioEffect { Type = "core_add", Faction = "winner", Cells = [1, 2] });
        var quote = DemandDiplomacy.Quote(claims, "winner", "loser");
        Check(quote.SharedCores == 2 && quote.Chance == 38 && quote.CoreBonus == 13, "unoccupied overlapping claim raises chance proportionally");
        Check(claims.Cells[2].Owner == "loser" && claims.Cells[1].Owner == "winner", "claims do not transfer territory");
        Check(DemandDiplomacy.CoreCells(claims, "winner").SequenceEqual(new[] {1, 2, 6}), "additive claims preserve own opening core");
        var reduced = Events(claims, new ScenarioEffect { Type = "core_remove", Faction = "winner", Cells = [2] });
        Check(DemandDiplomacy.Quote(reduced, "winner", "loser").Chance == 31, "removing claim reduces chance");
        var restored = Events(reduced, new ScenarioEffect { Type = "faction_core", Faction = "winner", Cells = [6] });
        Check(DemandDiplomacy.Quote(restored, "winner", "loser").Chance == 25, "new core alias replaces list with old behavior");
        Reject(() => Events(world, new ScenarioEffect { Type = "core_add", Faction = "winner", Cells = [4] }), "scenario.homeland");
        var favorable = Events(claims, new ScenarioEffect { Type = "surrender_modifier", Faction = "winner", OtherFaction = "loser", Value = 20 });
        Check(DemandDiplomacy.Quote(favorable, "winner", "loser").Chance == 58, "directional positive modifier");
        Check(DemandDiplomacy.Quote(favorable, "loser", "winner").PairBonus == 0, "reverse direction unaffected");
        var unfavorable = Events(favorable, new ScenarioEffect { Type = "surrender_modifier", Faction = "winner", OtherFaction = "loser", Value = -100 });
        Check(DemandDiplomacy.Quote(unfavorable, "winner", "loser").Chance == 0, "negative modifier clamps at zero");
        var capped = Events(favorable, new ScenarioEffect { Type = "surrender_modifier", Faction = "winner", OtherFaction = "loser", Value = 100 });
        Check(DemandDiplomacy.Quote(capped, "winner", "loser").Chance == 95, "surrender chance retains configured cap");
        Check(CapitalTreasury.Percent(claims, "winner", "loser") == 30, "overlapping cores add loot bonus without occupation requirement");
        var lootBonus = Events(favorable, new ScenarioEffect { Type = "loot_modifier", Faction = "winner", OtherFaction = "loser", Value = 90 });
        Check(CapitalTreasury.Percent(lootBonus, "winner", "loser") == 100
            && DemandDiplomacy.Quote(lootBonus, "winner", "loser").PairBonus == 20, "loot modifier caps at 100 and preserves surrender adjustment");
        var saved = WorldSave.Decode(WorldSave.Encode(lootBonus), lootBonus.ContentRevision);
        Check(CapitalTreasury.Percent(saved, "winner", "loser") == 100 && DemandDiplomacy.Quote(saved, "winner", "loser").Chance == 58, "claims and directional adjustments persist");
        var transferred = world with { Cells = world.Cells.Select((c, i) => i == 0 ? c with { Owner = "winner" } : c).ToArray() };
        var loot = CapitalTreasury.Captured(world, transferred, "winner", [0]);
        Check(loot.State.Factions.Single(f => f.Id == "winner").Resources == new Resources(210, 120, 39), "three treasury resources transferred with floor rounding");
        Check(loot.State.Factions.Single(f => f.Id == "loser").Resources == new Resources(801, 401, 40), "defender loses exact transferred amounts");
        Check(loot.State.Factions.Single(f => f.Id == "loser").Capital!.TreasuryCaptured && loot.Notices.Count == 4, "capture consumes one chance and reports amounts");
        Check(world.Factions.Single(f => f.Id == "loser").Resources == new Resources(1001, 501, 49), "capture leaves input snapshot unchanged");
        for (int percent = 0; percent <= 100; percent++)
        {
            var sample = world with { Demands = world.Demands! with { Rules = new() { CapitalLootPercent = percent } } };
            var outcome = CapitalTreasury.Captured(sample, transferred with { Demands = sample.Demands }, "winner", [0]).State;
            var a = outcome.Factions.Single(f => f.Id == "winner").Resources;
            var b = outcome.Factions.Single(f => f.Id == "loser").Resources;
            Check(a.Money == 10 + 1001L * percent / 100 && a.Money + b.Money == 1011 && a.Industry + b.Industry == 521
                && a.Food + b.Food == 79 && outcome.Factions.Single(f => f.Id == "loser").Capital!.TreasuryCaptured, "all percentages conserve resources " + percent);
        }
        var lostBack = loot.State with { Cells = loot.State.Cells.Select((c, i) => i == 0 ? c with { Owner = "loser" } : c).ToArray() };
        Check(CapitalTreasury.Captured(lostBack, loot.State, "winner", [0]).Notices.Count == 0, "recapture cannot loot twice");
        var movedCapital = Events(lostBack, new ScenarioEffect { Type = "faction_capital", Faction = "loser", Cells = [2] });
        Check(movedCapital.Factions.Single(f => f.Id == "loser").Capital == new FactionCapital(2, true), "moving capital never resets lifetime capture flag");
        var surrendered = WorldEvents.Surrender(loot.State, "loser", "winner").State;
        var revived = Events(surrendered, new ScenarioEffect { Type = "faction_revive", Faction = "loser" });
        Check(revived.Factions.Single(f => f.Id == "loser").Capital!.TreasuryCaptured, "revival preserves captured treasury marker");
        Check(CapitalTreasury.Captured(world with { Demands = null }, transferred with { Demands = null }, "winner", [0]).Notices.Count == 0, "campaign without conquest state never loots");
        var scripted = Events(world, new ScenarioEffect { Type = "cells_owner", Faction = "winner", Cells = [0] });
        Check(scripted.Factions.Single(f => f.Id == "winner").Resources == new Resources(10, 20, 30)
            && !scripted.Factions.Single(f => f.Id == "loser").Capital!.TreasuryCaptured, "scripted transfer does not loot or consume chance");
        var debt = world with { Factions = world.Factions.Select(f => f.Id == "loser" ? f with { Resources = new(-5, 0, 1) } : f).ToArray() };
        Check(CapitalTreasury.Captured(debt, transferred with { Factions = debt.Factions }, "winner", [0]).State.Factions.Single(f => f.Id == "loser").Resources
            == new Resources(-5, 0, 1), "debt and tiny balances never mint resources");
        var large = world with { Factions = world.Factions.Select(f => f with { Resources = f.Id == "loser" ? new(long.MaxValue, 0, 0) : new(0, 0, 0) }).ToArray(),
            Demands = world.Demands! with { Rules = new() { CapitalLootPercent = 100 } } };
        Check(CapitalTreasury.Captured(large, transferred with { Factions = large.Factions, Demands = large.Demands }, "winner", [0]).State
            .Factions.Single(f => f.Id == "winner").Resources.Money == long.MaxValue, "percentage calculation avoids intermediate overflow");
        var mover = world with { Units = example.Units.Select(u => u.Id == "unit.2" ? u with { Cell = 8, RemainingMovement = 6 } : u with { Cell = 0 }).ToArray() };
        mover = CapitalTreasury.SetCapital(mover, "loser", 3);
        var movement = WorldActions.Move(mover, mover.Revision, "unit.2", 3);
        Check(movement.Notices.Any(n => n.Kind == "capital.captured") && movement.State.Factions.Single(f => f.Id == "winner").Resources.Money == 210, "actual military movement invokes treasury capture");
        var history = new MovementHistory(); history.Reset(mover); history.Record(mover, movement.State, "unit.2", movement.Movements[0].Path.ToArray(), movement.Notices);
        Check(history.Available(movement.State) == null, "treasury capture invalidates movement undo");
        var ai = mover with { Factions = mover.Factions.Select(f => f with { Human = false }).ToArray() };
        var aiMoved = WorldActions.Move(ai, ai.Revision, "unit.2", 3);
        Check(aiMoved.Notices.Any(n => n.Kind == "capital.captured") && aiMoved.State.Factions.Single(f => f.Id == "winner").Resources.Money == 210,
            "AI faction uses same military loot path");
        var aiPlan = new AiUnitPlan(ai.Revision, "unit.2", ai.AiRandomState, ai.AiRandomState,
            [new(new AiMoveCommand(ai.Revision, "unit.2", 3), 1)]);
        var aiExecuted = WorldAi.ExecuteUnit(ai, aiPlan, new(), new());
        Check(aiExecuted.Notices.Any(n => n.Kind == "capital.captured")
            && aiExecuted.State.Factions.Single(f => f.Id == "winner").Resources.Money == 210,
            "AI unit execution preserves treasury transfer and notices");
        Reject(() => DemandDiplomacy.Demand(ai, ai.Revision, "winner", "loser"), "diplomacy.turn");
        var scriptedSurrender = Events(ai with { Jurisdictions = TerritorialJurisdictions.Create(ai) },
            new ScenarioEffect { Type = "faction_surrender", Faction = "loser", OtherFaction = "winner" });
        Check(scriptedSurrender.Factions.Single(f => f.Id == "loser").Defeated && scriptedSurrender.Cells[0].Owner == "winner"
            && scriptedSurrender.Factions.Single(f => f.Id == "winner").Resources.Money == 10,
            "events simulate AI surrender without random demand or capital loot");
        Check(scriptedSurrender.Cells.All(c => c.Owner != "loser"), "scripted surrender transfers all facilities with fixed jurisdictions");
        var spawned = Events(loot.State, new ScenarioEffect { Type = "faction_create", Faction = "transformed", Name = "new", Camp = "new", TemplateFaction = "loser",
            Capital = 2, InitialResources = new(300, 200, 100) });
        var created = spawned.Factions.Single(f => f.Id == "transformed");
        Check(created.Resources == new Resources(300, 200, 100) && created.Capital == new FactionCapital(2), "new faction gets explicit fresh treasury and one new capture chance");
        Check(spawned.Factions.Single(f => f.Id == "loser").Capital!.TreasuryCaptured, "creating another faction cannot reset old faction treasury");
        var reloaded = WorldSave.Decode(WorldSave.Encode(loot.State), loot.State.ContentRevision);
        Check(reloaded.Factions.Single(f => f.Id == "loser").Capital!.TreasuryCaptured, "one-shot flag survives save reload");
        Reject(() => new DemandRules { CapitalLootPercent = 101 }.Validate(), "diplomacy.rules");
        Reject(() => Events(world, new ScenarioEffect { Type = "surrender_modifier", Faction = "winner", OtherFaction = "loser", Value = 101 }), "scenario.effect");
        Reject(() => Events(world, new ScenarioEffect { Type = "faction_capital", Faction = "winner", Cells = [4] }), "scenario.homeland");
        byte[] Read(string p) => File.ReadAllBytes(p.StartsWith("data/", StringComparison.Ordinal) ? Path.Combine(root, "rewrite/data/legacy", p[5..]) : Path.Combine(root, "rewrite/data", p));
        var config = DiplomacyConfiguration.Read(Read);
        Check(config.Rules.CapitalLootPercent == 20 && config.Presentation.BuildingsText.StartsWith("核心地区"), "new defaults and UI terminology load");
        var rules = (config with { ScenarioPairs = new() { ["conquest1"] = [new("fra", "rus", 20, 15)] } }).ResolveRules("conquest1",
            new Dictionary<string, string> { ["france"] = "fra", ["russia"] = "rus" }, "france");
        Check(rules.PairModifiers.Single() == new FactionPairModifier("france", "russia", 20, 15), "configuration resolves country selectors to runtime faction IDs");
        return checks;
    }
}
