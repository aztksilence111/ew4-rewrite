using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class AdvancedUnitChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label) { checks++; if (!condition) throw new Exception("Advanced units: " + label); }
        void Reject(Action action, string code) { try { action(); } catch (RuleViolation e) { Check(e.Code == code, label: code + ": " + e.Code); return; } throw new Exception("Accepted " + code); }
        var initial = WorldChecks.Example();
        var engineer = initial.Units[1] with { Combatant = initial.Units[1].Combatant with { Profile = initial.Units[1].Combatant.Profile with { Id = "fixture.engineer" } }, RemainingMovement = 6 };
        var ordinary = engineer with { Combatant = engineer.Combatant with { Profile = engineer.Combatant.Profile with { Id = "fixture.rifle" } } };
        var config = new ModConstructionRules { Enabled = true, BuilderProfiles = ["fixture.engineer"], Buildings = new Dictionary<string, ModBuildingDefinition> {
            ["airfield"] = new() { Name = "Airport", Cost = new(100, 40, 0), Levels = [new() { Image = "stable_west_lv1.png" },
                new() { Image = "stable_west_lv2.png", UpgradeCost = new(50, 20, 0), Income = new(3, 0, 0), Healing = 5, DefensePercent = 10 }] }
        } };
        var world = initial with { Construction = config, Units = [initial.Units[0], engineer],
            Factions = initial.Factions.Select(f => f with { Resources = new(1000, 1000, 1000) }).ToArray() };
        world.Validate(); Check(world.Cells.All(c => c.BuildingKind == null), "new custom buildings are not preplaced");
        Check(WorldConstruction.CanBuild(world, engineer.Id, "airfield"), "engineer may build airport");
        var rifleWorld = world with { Units = [initial.Units[0], ordinary] };
        Check(!WorldConstruction.CanBuild(rifleWorld, ordinary.Id, "airfield"), "ordinary infantry cannot build airport");
        Reject(() => WorldConstruction.Build(rifleWorld, world.Revision, ordinary.Id, "airfield"), "construction.unavailable");
        var trench = new InstallationBuildRules { Kind = "trench" };
        Check(!WorldSupportActions.CanBuildInstallation(rifleWorld, ordinary.Id, trench), "ordinary infantry cannot build original installation");
        Reject(() => WorldSupportActions.BuildInstallation(rifleWorld, world.Revision, ordinary.Id, trench), "installation.unavailable");
        Check(WorldSupportActions.CanBuildInstallation(world, engineer.Id, trench), "engineer retains original installations");
        Check(WorldSupportActions.BuildInstallation(world, world.Revision, engineer.Id, trench).State.Cells[engineer.Cell].InstallationKind == "trench", "engineer builds trench");
        Check(WorldSupportActions.CanBuildInstallation(rifleWorld with { Construction = null }, ordinary.Id, trench), "builtin infantry construction unchanged");
        var fortProfile = ordinary.Combatant.Profile with { Id = "fixture.fort", Category = "fort", Movement = 0 };
        var fortOffer = new RecruitmentOffer { Id = "fixture.fort.card", Template = new() { Profile = fortProfile, Health = fortProfile.MaxHealth }, Building = BuildingRequirement.Forbidden };
        var fortWorld = world with { Factions = world.Factions.Select(f => f.Id == world.ActiveFaction ? f with { Recruitment = new Dictionary<string, RecruitmentGrant> { [fortOffer.Id] = new() } } : f).ToArray() };
        Check(WorldActions.CanRecruit(fortWorld, 7, fortOffer), "adjacent engineer may construct fixed fort unit");
        Check(!WorldActions.CanRecruit(fortWorld with { Units = [initial.Units[0], ordinary] }, 7, fortOffer), "ordinary infantry cannot construct fixed fort unit");
        var fortified = WorldActions.Recruit(fortWorld, fortWorld.Revision, 7, "new.fort", fortOffer).State;
        Check(fortified.Units.Single(u => u.Id == engineer.Id).Acted, "fort construction consumes engineer action");
        Check(!WorldConstruction.CanBuild(world with { Construction = null }, engineer.Id, "airfield"), "custom construction defaults off");
        Check(!WorldConstruction.CanBuild(world with { Units = [initial.Units[0], engineer with { Acted = true }] }, engineer.Id, "airfield"), "acted engineer cannot build");
        Check(WorldConstruction.CanBuild(world with { Units = [initial.Units[0], engineer with { Acted = true }] }, engineer.Id, "airfield", false, checkAction: false),
            "acted engineer still has a construction card at a legal site");
        Check(!WorldConstruction.CanBuild(rifleWorld, ordinary.Id, "airfield", false, checkAction: false), "preview cards do not bypass engineer qualification");
        Check(!WorldConstruction.CanBuild(world with { Cells = world.Cells.Select((c, i) => i == engineer.Cell ? c with { Owner = "loser" } : c).ToArray() }, engineer.Id, "airfield"), "enemy land cannot be built on");
        Check(!WorldConstruction.CanBuild(world with { Cells = world.Cells.Select((c, i) => i == engineer.Cell ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray() }, engineer.Id, "airfield"), "airport cannot be built at sea");
        Check(!WorldConstruction.CanBuild(world with { Factions = world.Factions.Select(f => f with { Resources = new(0, 0, 0) }).ToArray() }, engineer.Id, "airfield"), "building must be affordable");
        var built = WorldConstruction.Build(world, world.Revision, engineer.Id, "airfield").State;
        Check(built.Cells[engineer.Cell].BuildingKind == "airfield" && built.Cells[engineer.Cell].BuildingLevel == 1, "airport begins at level one");
        Check(built.Units.Single(u => u.Id == engineer.Id).Acted && built.Units.Single(u => u.Id == engineer.Id).RemainingMovement == 0, "building spends engineer action");
        Check(built.Factions.Single(f => f.Id == engineer.Owner).Resources == new Resources(900, 960, 1000), "construction charges exactly once");
        Check(!WorldConstruction.CanBuild(built, engineer.Id, "airfield"), "occupied building cannot be overwritten");
        var upgrades = new BuildingUpgradeRules { Steps = [new("airfield", 1, 2, new(50, 20, 0))] };
        var empty = built with { Units = [initial.Units[0]] };
        Check(WorldImprovements.CanUpgradeBuilding(empty, engineer.Cell, upgrades), "airport upgrade needs no engineer or garrison");
        var upgraded = WorldImprovements.UpgradeBuilding(empty, empty.Revision, engineer.Cell, upgrades).State;
        Check(upgraded.Cells[engineer.Cell].BuildingLevel == 2, "airport upgrade applies");
        Check(!WorldImprovements.CanUpgradeBuilding(upgraded, engineer.Cell, upgrades), "maximum level enforced");
        var save = WorldSave.Decode(WorldSave.Encode(upgraded), upgraded.ContentRevision);
        Check(save.Cells[engineer.Cell].BuildingKind == "airfield" && save.Construction!.BuilderProfiles.Single() == "fixture.engineer", "construction and building persist");
        Check(JsonSerializer.Serialize(config) == JsonSerializer.Serialize(config.Copy()), "construction copy preserves data");
        var timedConfig = config with { Buildings = new Dictionary<string, ModBuildingDefinition> {
            ["airfield"] = config.Buildings["airfield"] with { RecruitmentPerTurn = 1, BuilderKeepsMovement = true,
                Levels = [new() { Image = "stable_west_lv1.png", ConstructionTurns = 2 },
                    new() { Image = "stable_west_lv2.png", ConstructionTurns = 1 }] }
        } };
        var timed = WorldConstruction.Build(world with { Construction = timedConfig }, world.Revision, engineer.Id, "airfield").State;
        Check(timed.Cells[8].BuildingConstructionTurns == 2, "airport independently starts two own-turn construction");
        Check(timed.Units.Single(u => u.Id == engineer.Id).Combatant.ConstructionTurns == 0, "engineer is not bound to airport construction");
        Check(timed.Units.Single(u => u.Id == engineer.Id).RemainingMovement == engineer.RemainingMovement, "airport builder retains remaining movement");
        var immediateDeparture = WorldActions.Move(timed, timed.Revision, engineer.Id, 7).State;
        Check(immediateDeparture.Units.Single(u => u.Id == engineer.Id).Cell == 7 && immediateDeparture.Cells[8].BuildingConstructionTurns == 2,
            "engineer may depart immediately without altering airport countdown");
        Check(!WorldConstruction.CanBuild(immediateDeparture, engineer.Id, "airfield"), "departed engineer cannot build twice this turn");
        Check(!WorldImprovements.CanUpgradeBuilding(timed, 8, upgrades), "construction blocks upgrade");
        Reject(() => WorldImprovements.UpgradeBuilding(timed, timed.Revision, 8, upgrades), "upgrade.construction");
        var turnRules = new WorldTurnRules { Buildings = [new("airfield", 1, new(0, 0, 0)), new("airfield", 2, new(0, 0, 0))] };
        WorldState End(WorldState w) => WorldTurns.EndTurn(w, w.Revision, turnRules).State;
        var opposingTurn = End(timed);
        Check(opposingTurn.ActiveFaction == "loser" && opposingTurn.Cells[8].BuildingConstructionTurns == 2, "other faction turn does not advance construction");
        var ownTurn = End(opposingTurn);
        Check(ownTurn.ActiveFaction == "winner" && ownTurn.Cells[8].BuildingConstructionTurns == 1, "first owner turn advances construction once");
        ownTurn = WorldActions.Move(ownTurn, ownTurn.Revision, engineer.Id, 7).State;
        Check(ownTurn.Units.Single(u => u.Id == engineer.Id).Cell == 7 && ownTurn.Cells[8].BuildingConstructionTurns == 1, "engineer can leave while airport builds");
        ownTurn = WorldSave.Decode(WorldSave.Encode(ownTurn), ownTurn.ContentRevision);
        Check(ownTurn.Cells[8].BuildingConstructionTurns == 1, "construction survives save reload");
        ownTurn = End(End(ownTurn));
        Check(ownTurn.Cells[8].BuildingConstructionTurns == 0, "second owner turn finishes without engineer");
        var planeProfile = engineer.Combatant.Profile with { Id = "fixture.aircraft" };
        var planeOffer = new RecruitmentOffer { Id = "fixture.aircraft.card", BuildingKinds = ["airfield"],
            Template = new() { Profile = planeProfile, Health = planeProfile.MaxHealth } };
        var readyAirfield = ownTurn with { UnitLayers = new() { Enabled = true, Profiles = new Dictionary<string, UnitLayerProfile> {
            [planeProfile.Id] = new() { Layer = "air", AttackLayers = ["air"] } } },
            Factions = ownTurn.Factions.Select(f => f.Id != ownTurn.ActiveFaction ? f : f with {
                Recruitment = new Dictionary<string, RecruitmentGrant> { [planeOffer.Id] = new() } }).ToArray() };
        Check(!WorldActions.CanRecruit(readyAirfield with { Cells = timed.Cells }, 8, planeOffer), "airport under construction cannot recruit");
        Check(WorldActions.CanRecruit(readyAirfield, 8, planeOffer), "completed airport recruits aircraft");
        var recruited = WorldActions.Recruit(readyAirfield, readyAirfield.Revision, 8, "new.plane", planeOffer).State;
        Check(recruited.Cells[8].BuildingRecruitsThisTurn == 1, "airport records site-local recruitment");
        var vacated = recruited with { Units = recruited.Units.Where(u => u.Id != "new.plane").ToArray() };
        Check(!WorldActions.CanRecruit(vacated, 8, planeOffer), "vacating air slot cannot bypass airport turn limit");
        var loadedRecruit = WorldSave.Decode(WorldSave.Encode(vacated), vacated.ContentRevision);
        Check(!WorldActions.CanRecruit(loadedRecruit, 8, planeOffer), "recruitment limit survives reload");
        loadedRecruit = End(End(loadedRecruit));
        Check(WorldActions.CanRecruit(loadedRecruit, 8, planeOffer), "airport recruitment renews next owner turn");
        var heli = engineer with { Id = "heli", Cell = 7, Combatant = engineer.Combatant with { Profile = engineer.Combatant.Profile with {
            Id = "fixture.heli", MinimumRange = 0, MaximumRange = 3 } } };
        var layers = new UnitLayerRules { Enabled = true, Profiles = new Dictionary<string, UnitLayerProfile> {
            ["fixture.heli"] = new() { Layer = "air", Occupies = ["air", "surface"], AttackLayers = ["air", "surface"] },
            ["fixture.plane"] = new() { Layer = "air", AttackLayers = ["air"] }
        } };
        var hybrid = world with { UnitLayers = layers, Units = [initial.Units[0], heli] };
        hybrid.Validate();
        Check(WorldUnitLayers.Occupies(hybrid, heli.Combatant.Profile).Count == 2, "single instance reserves two layers");
        Check(!WorldUnitLayers.HasSpace(hybrid, ordinary.Combatant.Profile, heli.Owner, 7), "composite blocks ground capacity");
        var plane = heli with { Id = "plane", Combatant = heli.Combatant with { Profile = heli.Combatant.Profile with { Id = "fixture.plane" } } };
        Check(!WorldUnitLayers.HasSpace(hybrid, plane.Combatant.Profile, heli.Owner, 7), "composite blocks air capacity");
        Reject((hybrid with { Units = hybrid.Units.Append(ordinary with { Cell = 7 }).ToArray() }).Validate, "world.capacity");
        Reject((hybrid with { Units = hybrid.Units.Append(plane).ToArray() }).Validate, "world.capacity");
        (hybrid with { Limits = new() { UnitsPerFaction = 1 } }).Validate(); Check(true, "composite counts once against army limit");
        var groundEnemy = initial.Units[0];
        Check(WorldUnitLayers.CanAttack(hybrid, groundEnemy, heli), "ground weapons can target composite ground component");
        Check(WorldUnitLayers.CanAttack(hybrid, plane, heli), "air weapons can target composite air component");
        Check(WorldUnitLayers.AttackLayer(hybrid, heli, plane) == "air" && WorldUnitLayers.AttackLayer(hybrid, heli, groundEnemy) == "surface", "attack channel follows target");
        var sea = hybrid with { Cells = hybrid.Cells.Select((c, i) => i == heli.Cell ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray() };
        sea.Validate();
        Check(!WorldUnitLayers.CanAttack(sea, heli, groundEnemy), "hidden ground component cannot fire over sea");
        Check(!WorldUnitLayers.CanAttack(sea, groundEnemy, heli) && WorldUnitLayers.CanAttack(sea, plane, heli), "sea composite exposes only helicopter as attack target");
        Check(!WorldUnitLayers.HasSpace(sea, ordinary.Combatant.Profile, heli.Owner, heli.Cell), "sea retains both reserved layers");
        Check(WorldActions.Reachable(hybrid, heli.Id).ContainsKey(6), "composite uses airborne movement");
        var blocked = hybrid with { Cells = hybrid.Cells.Select((c, i) => i == 6 ? c with { Owner = null, Terrain = c.Terrain with { Barrier = TerrainBarrier.Mountain } } : c).ToArray() };
        Check(WorldActions.Reachable(blocked, heli.Id).ContainsKey(6), "composite ignores mountain barrier");
        foreach (string building in new[] { "city", "factory", "farmland", "airfield" })
        {
            var target = hybrid with { Cells = hybrid.Cells.Select((c, i) => i == 4 ? c with { Owner = "loser", BuildingKind = building, BuildingLevel = 1 } : c).ToArray() };
            var landing = WorldActions.Move(target, target.Revision, heli.Id, 4);
            Check(landing.State.Cells[4].Owner == heli.Owner && landing.State.Units.Single(u => u.Id == heli.Id).Combatant.BuildingKind == building,
                "composite landing captures " + building);
            Check(landing.State.Cells.Where((c, i) => i != 4).Select(c => c.Owner).SequenceEqual(target.Cells.Where((c, i) => i != 4).Select(c => c.Owner)),
                "airborne route does not paint territory for " + building);
            Check(WorldUnitLayers.Surface(landing.State).Any(u => u.Id == heli.Id), "landed composite is a surface garrison");
            var persisted = WorldSave.Decode(WorldSave.Encode(landing.State), landing.State.ContentRevision);
            Check(persisted.Cells[4].Owner == heli.Owner && persisted.Units.Single(u => u.Id == heli.Id).Combatant.BuildingKind == building,
                "composite captured building survives reload");
        }
        var seaLanding = hybrid with { Cells = hybrid.Cells.Select((c, i) => i == 6 ? c with { Owner = null, Terrain = c.Terrain with { Sea = true } } : c).ToArray() };
        var crossedSea = WorldActions.Move(seaLanding, seaLanding.Revision, heli.Id, 6).State;
        Check(crossedSea.Cells[6].Owner == null && !crossedSea.Units.Single(u => u.Id == heli.Id).Combatant.AtSea
            && WorldUnitLayers.Surface(crossedSea).All(u => u.Id != heli.Id), "composite over sea remains airborne and cannot occupy water");
        var mountainLanding = WorldActions.Move(blocked, blocked.Revision, heli.Id, 6).State;
        Check(mountainLanding.Cells[6].Owner == null, "airborne composite cannot occupy impassable mountain");
        Reject((layers with { Profiles = new Dictionary<string, UnitLayerProfile> { ["bad"] = new() { Layer = "air", Occupies = ["surface"] } } }).Validate, "layers.configuration");
        Check(WorldSave.Decode(WorldSave.Encode(hybrid), hybrid.ContentRevision).UnitLayers!.Profiles["fixture.heli"].Occupies!.Count == 2, "dual footprint persists");
        var overlap = hybrid with { Units = [heli, ordinary with { Id = "ally", Cell = 6 }, plane with { Cell = 6, Owner = "loser" }] };
        overlap.Validate();
        Check(!WorldActions.Reachable(overlap, heli.Id).ContainsKey(6), "enemy layer blocks dual unit even when ally is first at cell");
        var aiPlan = WorldAi.PlanProduction(world, new WorldAiRules(), new WorldProductionRules(), new WorldTurnRules());
        Check(aiPlan.Command == null, "AI does not build unused custom production building");
        return checks;
    }
}
