using System.Text.Json;
using Ew4.Core;
using Ew4.Client;
using Ew4.Persistence;

internal static class CustomMechanicsChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Mechanics: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + ": " + e.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        var fixture = WorldChecks.Example();
        var combatRules = new CombatRules { LethalStrikePreventsCounterattack = true };
        var striker = fixture.Units[0].Combatant with { Profile = fixture.Units[0].Combatant.Profile with {
            MinimumAttack=10, MaximumAttack=10, AttackBands=[] } };
        var responder = striker with { Profile=striker.Profile with { MinimumAttack=5, MaximumAttack=15 } };
        foreach (int health in new[] { 9, 10, 11 })
        {
            var rng = new Ew4Random(7);
            var result = Ew4Combat.Calculate(striker,responder with { Health=health },1,rng,combatRules);
            Check(result.DefenderDamage==10 && result.Counterattack==(health>10)
                && (health>10?result.AttackerDamage>0:result.AttackerDamage==0), "lethal and exact-health strikes suppress retaliation: " + health);
            Check(health>10?rng.State!=7:rng.State==7, "lethal strikes consume no retaliation randomness: " + health);
        }
        ulong defensiveSeed=0;
        while(new Ew4Random(defensiveSeed).Next(100)>=10)defensiveSeed++;
        var savedByDefense=Ew4Combat.Calculate(striker,responder with { Health=2, Skills=[Ew4Combat.Skill(32)] },1,new(defensiveSeed),combatRules);
        Check(savedByDefense.DefensiveProc && savedByDefense.DefenderDamage==1 && savedByDefense.Counterattack,
            "defensive proc reduces final damage before lethal retaliation decision");
        var killedDespiteDefense=Ew4Combat.Calculate(striker,responder with { Health=1, Skills=[Ew4Combat.Skill(32)] },1,new(defensiveSeed),combatRules);
        Check(killedDespiteDefense.DefensiveProc && !killedDespiteDefense.Counterattack && killedDespiteDefense.AttackerDamage==0,
            "minimum damage that kills still suppresses retaliation");
        Check(!Ew4Combat.Calculate(striker,responder with { Morale=-3 },1,new(7),combatRules).Counterattack,
            "nonlethal hit preserves existing counterattack restrictions");
        var exchangeWorld=fixture with { Units=[fixture.Units[0] with { Cell=0, Combatant=responder with { Health=10 } },
            fixture.Units[1] with { Cell=1, Combatant=striker }] };
        var exchangeRules=new WorldBattleRules { Combat=combatRules };
        var exchangePreview=WorldBattle.Preview(exchangeWorld,exchangeWorld.Revision,"unit.2","unit.1",exchangeRules);
        var exchangeResult=WorldBattle.Attack(exchangeWorld,exchangeWorld.Revision,"unit.2","unit.1",exchangeRules).State;
        Check(!exchangePreview.Main.Counterattack && exchangeResult.Units.All(u=>u.Id!="unit.1")
            && exchangeResult.Units.Single(u=>u.Id=="unit.2").Combatant.Health==100,
            "backend preview and commit agree: exact-health kill cannot retaliate");
        var neutralRules = new WorldAiRules { NeutralCamps=["neutral"], ExperienceLevels=new Dictionary<string,int> { ["0"]=0 } };
        var neutralWorld = fixture with { ActiveFaction="third", Factions=fixture.Factions.Select(f=>f.Id=="loser"?f with { Camp="neutral" }:f)
            .Append(fixture.Factions[0] with { Id="third", Camp="third" }).ToArray(),
            Units=[fixture.Units[0],fixture.Units[1] with { Cell=3 },fixture.Units[1] with { Id="third.unit", Owner="third", Cell=1 }] };
        neutralWorld = neutralWorld with { Units=neutralWorld.Units.Select(u=>u with { Combatant=u.Combatant with { Profile=u.Combatant.Profile with { Grade="0" } } }).ToArray() };
        var dormant = neutralWorld with { ActiveFaction="loser" };
        var dormantPlan = WorldAi.PlanStep(dormant,neutralRules,new(),new());
        Check(dormantPlan.Unit==null && dormantPlan.Production.Command==null && dormantPlan.Commander==null
            && dormantPlan.Production.FinalRandomState==dormant.AiRandomState, "unprovoked neutral performs no movement, attack or production");
        var diplomacyBattle = new WorldBattleRules { NeutralCamps=["neutral"], CheckFactionSurvival=false, ResolveIgnition=false,
            RecalculateLocalMorale=false, InstallationDestructionProbability=0, KillMoraleProbability=0, SuppressionProbability=0, KillDialogueProbability=0 };
        var aiProvoked = WorldBattle.Attack(neutralWorld,neutralWorld.Revision,"third.unit",fixture.Units[0].Id,diplomacyBattle).State;
        Check(!WorldDiplomacy.Dormant(aiProvoked,"loser",["neutral"]) && WorldDiplomacy.Hostile(aiProvoked,"loser","third",["neutral"]), "actual AI attack awakens neutral against attacker");
        Check(WorldDiplomacy.NeutralTo(aiProvoked,"loser","winner",["neutral"]) && !WorldDiplomacy.Hostile(aiProvoked,"loser","winner",["neutral"]), "AI attack leaves neutral black and peaceful toward player");
        var retaliation = WorldAi.StrikeCandidates(aiProvoked with { ActiveFaction="loser" },fixture.Units[0].Id,neutralRules);
        Check(retaliation.Any(c=>c.Action.TargetId=="third.unit") && retaliation.All(c=>c.Action.TargetId!=fixture.Units[1].Id), "neutral AI only attacks provoking country, not adjacent player");
        var playerProvoked = WorldBattle.Attack(aiProvoked with { ActiveFaction="winner" },aiProvoked.Revision,fixture.Units[1].Id,fixture.Units[0].Id,diplomacyBattle).State;
        Check(!WorldDiplomacy.NeutralTo(playerProvoked,"loser","winner",["neutral"]) && WorldDiplomacy.Hostile(playerProvoked,"loser","winner",["neutral"]), "actual player attack activates hostility toward player");
        Check(WorldSave.Decode(WorldSave.Encode(playerProvoked),playerProvoked.ContentRevision).NeutralHostilities.SequenceEqual(playerProvoked.NeutralHostilities), "bilateral neutral hostility persists across save/load");
        Check(WorldDiplomacy.Attacked(playerProvoked,"winner","loser",["neutral"]).NeutralHostilities.Count==2, "repeat attack does not duplicate hostility pair");
        var own = fixture.Units.Single(u => u.Owner == "winner");
        var refundRules = new DisbandRules { Enabled = true, PlayerFaction = "winner",
            RecruitmentCosts = new Dictionary<string, Resources> { [own.Combatant.Profile.Id] = new(101, 31, 7) } };
        Check(WorldDisband.Refund(own, new(101, 31, 7)) == new Resources(50, 15, 3), "full health price independent of purchase history");
        Check(WorldDisband.Refund(own with { Combatant = own.Combatant with { Health = 37 } }, new(101, 31, 7)) == new Resources(18, 5, 1), "wounded refund floors each resource once");
        Check(WorldDisband.Refund(own, new(long.MaxValue, 0, 0)).Money == long.MaxValue / 2, "refund multiplication does not overflow");
        var before = WorldSave.Encode(fixture);
        var dismissed = WorldDisband.Execute(fixture, fixture.Revision, own.Id, refundRules).State;
        Check(dismissed.Units.Count == fixture.Units.Count - 1 && dismissed.Factions.Single(f => f.Id == "winner").Resources == new Resources(50, 15, 3), "remove initial army and credit exact refund");
        Check(before.SequenceEqual(WorldSave.Encode(fixture)), "disband leaves old snapshot intact");
        Reject(() => WorldDisband.Execute(fixture, -1, own.Id, refundRules), "action.stale");
        Reject(() => WorldDisband.Execute(fixture, 0, fixture.Units[0].Id, refundRules), "disband.unavailable");
        Reject(() => WorldDisband.Execute(fixture, 0, own.Id, refundRules with { Enabled = false }), "disband.unavailable");
        Reject(() => WorldDisband.Execute(dismissed, dismissed.Revision, own.Id, refundRules), "disband.unavailable");
        Check(!WorldDisband.CanDisband(fixture with { PendingDialogue = "pause" }, own.Id, refundRules), "dialogue blocks disband availability");
        var commanded = fixture with { Units = fixture.Units.Select(u => u.Id == own.Id ? u with { CommanderId = "general" } : u).ToArray(),
            Commanders = [new() { Id = "general", Owner = "winner" }] };
        Check(!WorldDisband.CanDisband(commanded, own.Id, refundRules), "commanded unit has no disband quote");
        Reject(() => WorldDisband.Execute(commanded, 0, own.Id, refundRules), "disband.unavailable");
        var wounded = fixture with { Units = fixture.Units.Select(u => u.Id == own.Id ? u with { Combatant = u.Combatant with { Health = 37 } } : u).ToArray() };
        Check(WorldDisband.Execute(wounded, 0, own.Id, refundRules).State.Factions.Single(f => f.Id == "winner").Resources == new Resources(18, 5, 1), "wounded execution credits the quoted refund");

        var cells = Enumerable.Range(0, 45).Select(i => new CellState { Owner = i % 9 < 3 ? "winner" : "loser",
            BuildingKind = i is 19 or 22 or 25 ? "city" : null, BuildingLevel = i is 19 or 22 or 25 ? 1 : 0 }).ToArray();
        var land = fixture with { Width = 9, Height = 5, Cells = cells, Units = [] };
        land = land with { Jurisdictions = TerritorialJurisdictions.Create(land) }; land.Validate();
        var proposed = land.Cells.Select(c => c with { Owner = "winner" }).ToArray();
        var claims = JurisdictionClaims.Filter(land, proposed, "winner");
        Check(claims[23].Owner == "winner" && claims[24].Owner == "winner" && claims[22].Owner == "loser", "ordinary claims do not require a frontier but facilities still require actual occupation");
        Check(JurisdictionClaims.Filter(land,proposed,"winner",requireFrontier:true)[24].Owner=="loser",
            "farm-style expansion explicitly retains the nonadjacent district restriction");
        Check(land.Jurisdictions!.Borders.Count > 1, "multiple border districts represented without a single nearest-city shortcut");
        var allied = land with { Factions = land.Factions.Select(f => f with { Camp = "allied" }).ToArray() };
        Check(JurisdictionClaims.Filter(allied, proposed, "winner").SequenceEqual(allied.Cells), "allied countries keep territory during expansion");
        var alliedMarch = WorldActions.Move(allied with { Units = [own with { Cell = 21, RemainingMovement = 6 }] }, 0, own.Id, 22).State;
        Check(alliedMarch.Units.Single().Cell == 22 && alliedMarch.Cells.Select((c,i)=>(c,i)).All(p=>p.c.Owner ==
            (allied.Jurisdictions!.CellCenters[p.i] == 22 ? "winner" : allied.Cells[p.i].Owner)), "occupying allied city transfers its district only");
        var alliedReturn = WorldActions.Move(allied with { ActiveFaction = "loser", Units = [own with { Owner = "loser", Cell = 20, RemainingMovement = 6 }] }, 0, own.Id, 19).State;
        Check(alliedReturn.Cells[19].Owner == "loser", "allied centers can be occupied in both directions");
        Check(JurisdictionClaims.Move(allied with { Jurisdictions = null }, proposed, [20, 21], "winner").SequenceEqual(allied.Cells), "ordinary allied ground remains protected without district metadata");
        foreach (string kind in new[] { "city", "industry", "stable" })
        {
            var territory = land with { Cells = land.Cells.Select((c,i)=>i==22?c with { BuildingKind = kind }:c).ToArray(), Units = [own with { Cell=21, RemainingMovement=6 }] };
            var capture = WorldActions.Move(territory, 0, own.Id, 22).State;
            Check(capture.Cells.Select((c,i)=>(c,i)).Where(p=>territory.Jurisdictions!.CellCenters[p.i]==22).All(p=>p.c.Owner=="winner"), kind+" capture transfers whole fixed district");
            Check(capture.Cells[25].Owner=="loser", kind+" capture does not automatically transfer next district");
            Check(capture.Jurisdictions!.CellCenters.SequenceEqual(territory.Jurisdictions!.CellCenters), kind+" capture preserves geometry");
        }
        var portWorld = land with { Cells = land.Cells.Select((c,i)=>i==22?c with { BuildingKind="port" }:c).ToArray(), Jurisdictions=null };
        portWorld = portWorld with { Jurisdictions=TerritorialJurisdictions.Create(portWorld) };
        Check(portWorld.Jurisdictions.CellCenters[22]==-1 && !portWorld.Jurisdictions.CellCenters.Contains(22), "port is an independent tile, never a district center");
        var portCapture = WorldActions.Move(portWorld with { Units=[own with { Cell=21, RemainingMovement=6 }] }, 0, own.Id, 22).State;
        Check(portCapture.Cells[22].Owner=="winner" && portCapture.Cells[23].Owner=="loser", "port occupation transfers only port ownership");
        var alliedPortCapture = WorldActions.Move(portWorld with { Factions=allied.Factions, Units=[own with { Cell=21, RemainingMovement=6 }] },0,own.Id,22).State;
        Check(alliedPortCapture.Cells[22].Owner=="winner" && alliedPortCapture.Cells[23].Owner=="loser", "friendly port is occupiable without transferring surrounding land");
        var legacyPortGeometry = portWorld with { Jurisdictions=land.Jurisdictions! with { CenterCaptureVersion=0 } };
        var migratedPortGeometry = JurisdictionClaims.UpgradeCapturedDistricts(legacyPortGeometry,portWorld);
        Check(migratedPortGeometry.Jurisdictions!.CellCenters[22]==-1&&!migratedPortGeometry.Jurisdictions.CellCenters.Contains(22), "old save removes port-centered jurisdiction using original scenario geometry");
        var legacyCapture = land with { Cells = land.Cells.Select((c,i)=>i==22?c with { Owner="winner" }:c).ToArray(), Jurisdictions=land.Jurisdictions! with { CenterCaptureVersion=0 } };
        var upgradedCapture = JurisdictionClaims.UpgradeCapturedDistricts(legacyCapture,land);
        Check(upgradedCapture.Cells.Select((c,i)=>(c,i)).Where(p=>land.Jurisdictions!.CellCenters[p.i]==22).All(p=>p.c.Owner=="winner"), "old captured-center save gains missing territory");
        Check(JurisdictionClaims.UpgradeCapturedDistricts(upgradedCapture,land)==upgradedCapture, "center migration runs once only");
        var taken = land with { Cells = land.Cells.Select((c, i) => i == 22 ? c with { Owner = "winner" } : c).ToArray() };
        Check(JurisdictionClaims.Filter(taken, proposed, "winner")[24].Owner == "winner", "capturing frontier center opens adjacent ordinary ground");
        Check(land.Jurisdictions.CellCenters.SequenceEqual(taken.Jurisdictions!.CellCenters), "district geometry stays fixed after capture");
        var stranded = land with { Cells = land.Cells.Select((c, i) => i % 9 == 3 ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray(), Jurisdictions = null };
        stranded = stranded with { Jurisdictions = TerritorialJurisdictions.Create(stranded) };
        Check(JurisdictionClaims.Filter(stranded, proposed, "winner")[22].Owner == "loser", "water corridor never establishes a land frontier");
        var island = stranded with { Cells = stranded.Cells.Select((c, i) => i is 22 or 25 ? c with { BuildingKind = null, BuildingLevel = 0 } : c).ToArray(), Jurisdictions = null };
        island = island with { Jurisdictions = TerritorialJurisdictions.Create(island) }; island.Validate();
        Check(island.Jurisdictions!.CellCenters[22] >= 0 && island.Jurisdictions.DetachedCells.Contains(22), "centerless island has an administrative label without a land connection");
        Check(JurisdictionClaims.Filter(island, proposed, "winner",requireFrontier:true)[22].Owner == "loser" && island.Jurisdictions.Borders.Count == 0, "island label does not permit farm-style painting across the sea");
        var islandSaved = WorldSave.Decode(WorldSave.Encode(island), island.ContentRevision);
        Check(islandSaved.Jurisdictions!.DetachedCells.SequenceEqual(island.Jurisdictions.DetachedCells), "disconnected land restrictions survive save/load");
        foreach (string kind in new[] { "city", "industry", "stable" })
        {
            var alternate = land with { Cells = land.Cells.Select((c, i) => i == 19 ? c with { BuildingKind = kind } : c).ToArray(), Jurisdictions = null };
            Check(TerritorialJurisdictions.Create(alternate).CellCenters[19] == 19, kind + " counts as a district center");
        }
        int farm = 31;
        var farmland = land with { Cells = land.Cells.Select((c, i) => i == farm ? c with { BuildingKind = "farmland", BuildingLevel = 1 } : c).ToArray() };
        var grid = new HexMap(9, 5); var ring = Enumerable.Range(0, 6).Select(d => grid.Neighbor(farm, d)).Append(farm).Where(i => i >= 0).ToArray();
        var guarded = JurisdictionClaims.Filter(farmland, proposed, "winner");
        Check(ring.All(i => guarded[i].Owner == farmland.Cells[i].Owner), "enemy farm protects its cell and six neighbors from passive/path claims");
        var occupiedFarm = JurisdictionClaims.Move(farmland, proposed, [30, farm], "winner");
        var alliedFarm = farmland with { Factions = allied.Factions };
        Check(JurisdictionClaims.Move(alliedFarm, proposed, [30, farm], "winner").SequenceEqual(alliedFarm.Cells), "allied farmland and its ring keep their owner");
        Check(ring.Where(i => land.Jurisdictions.CellCenters[i] is 19 or 22 && (i == farm || farmland.Cells[i].BuildingKind == null)).All(i => occupiedFarm[i].Owner == "winner"), "occupying eligible farm releases ordinary ring cells and its own tile");
        Check(occupiedFarm[22].Owner == "loser", "farm ring never captures its neighboring city");
        var deepFarm = land with { Cells = land.Cells.Select((c, i) => i == 34 ? c with { BuildingKind = "farmland", BuildingLevel = 1 } : c).ToArray() };
        var deepFarmCapture = JurisdictionClaims.Move(deepFarm, deepFarm.Cells.Select((c,i)=>i==34?c with { Owner="winner" }:c).ToArray(), [33, 34], "winner");
        Check(deepFarmCapture[34].Owner == "winner" && deepFarmCapture[35].Owner == "loser", "direct deep farm occupation transfers its tile but cannot expand its ring beyond the frontier");
        var boundaryFarm = land with { Cells = land.Cells.Select((c, i) => i == 23 ? c with { BuildingKind = "farmland", BuildingLevel = 1 } : c).ToArray() };
        var boundaryCapture = WorldActions.Move(boundaryFarm with { Units = [own with { Cell = 22, RemainingMovement = 6 }] }, 0, own.Id, 23).State;
        Check(boundaryCapture.Cells[23].Owner == "winner" && boundaryCapture.Cells[24].Owner == "loser", "actual farm occupation transfers eligible ring but preserves the deeper district");
        var fort = fixture.Units[0] with { Cell = farm, Combatant = fixture.Units[0].Combatant with { Health = 1,
            Profile = fixture.Units[0].Combatant.Profile with { Category = "fort" } } };
        var fortified = land with { Units = [fort] };
        Check(ring.Where(i=>land.Jurisdictions.CellCenters[i]==land.Jurisdictions.CellCenters[farm]).All(i => JurisdictionClaims.Filter(fortified, proposed, "winner")[i].Owner == fortified.Cells[i].Owner), "living fort protects ring clipped to its fixed district");
        var fallen = JurisdictionClaims.DestroyFort(fortified, farm, "winner");
        Check(ring.Where(i => land.Jurisdictions.CellCenters[i]==land.Jurisdictions.CellCenters[farm] && land.Cells[i].BuildingKind == null).All(i => fallen[i].Owner == "winner"), "eligible destroyed fort transfers ordinary ring cells in its fixed district");
        Check(ring.Where(i=>land.Jurisdictions.CellCenters[i]!=land.Jurisdictions.CellCenters[farm]).All(i=>fallen[i].Owner==fortified.Cells[i].Owner),
            "fort destruction never paints outside its opening district");
        Check(fallen[22].Owner == "loser", "fort destruction never captures its neighboring city");
        foreach (string kind in new[] { "city", "industry", "stable", "port", "farmland" })
        {
            var protectedSite = farmland with { Cells = farmland.Cells.Select((c,i)=>i==22?c with { BuildingKind=kind }:c).ToArray() };
            var farmClaims = JurisdictionClaims.Move(protectedSite, protectedSite.Cells.Select(c=>c with { Owner="winner" }).ToArray(), [30,farm], "winner");
            Check(farmClaims[22].Owner=="loser", "farm occupation cannot capture neighboring "+kind);
            var fortClaims = JurisdictionClaims.DestroyFort(protectedSite with { Units=[fort] },farm,"winner");
            Check(fortClaims[22].Owner=="loser", "fort destruction cannot capture neighboring "+kind);
        }
        var overlappingGuards = fortified with { Cells=fortified.Cells.Select((c,i)=>i==30?c with { BuildingKind="farmland", BuildingLevel=1 }:c).ToArray() };
        var releasedFort = JurisdictionClaims.DestroyFort(overlappingGuards,farm,"winner");
        Check(releasedFort[farm].Owner=="winner" && releasedFort[30].Owner=="loser" && releasedFort[22].Owner=="loser",
            "destroyed fort overrides farm ring but preserves farm and city facilities");
        var centerOverGuards = WorldActions.Move(overlappingGuards with { Units=[fort,own with { Cell=21, RemainingMovement=6 }] },0,own.Id,22).State;
        Check(centerOverGuards.Cells[22].Owner=="winner" && centerOverGuards.Cells[farm].Owner=="loser" && centerOverGuards.Cells[30].Owner=="loser"
            && centerOverGuards.Units.Single(u=>u.Id==fort.Id).Owner=="loser", "center occupation leaves surviving fort's original ordinary-ground ring intact");
        var heldRing=JurisdictionClaims.Filter(centerOverGuards,centerOverGuards.Cells.Select(c=>c with { Owner="winner" }).ToArray(),"winner");
        Check(heldRing[farm].Owner=="loser", "fort protection persists after center ownership changes");
        var reloadedRing=WorldSave.Decode(WorldSave.Encode(centerOverGuards),centerOverGuards.ContentRevision);
        Check(JurisdictionClaims.Filter(reloadedRing,reloadedRing.Cells.Select(c=>c with { Owner="winner" }).ToArray(),"winner")[farm].Owner=="loser",
            "fixed fort scope survives center capture and save/load");
        var edgeFort = fortified with { Units=[fort with { Cell=21 }], Cells=fortified.Cells.Select((c,i)=>i==20?c with { Owner="loser" }:c).ToArray() };
        Check(JurisdictionClaims.Filter(edgeFort,proposed,"winner")[20].Owner=="winner",
            "living fort cannot protect an adjacent tile assigned to a different opening district");
        Check(JurisdictionClaims.DestroyFort(edgeFort,21,"winner")[20].Owner=="loser",
            "destroying a border fort cannot repaint an adjacent foreign district even when it is otherwise reachable");
        var blockedFarmRing = JurisdictionClaims.Move(overlappingGuards,proposed,[29,30],"winner");
        Check(blockedFarmRing[farm].Owner=="loser", "living fort has priority over farm expansion");
        var fortOffer = new RecruitmentOffer { Id="new.fort", Template=fort.Combatant, Building=BuildingRequirement.Forbidden, AllowSea=false };
        foreach (string builder in new[] { "winner", "loser" })
        {
            int site = builder=="winner"?23:20, center = builder=="winner"?22:19;
            var construction = land with { ActiveFaction=builder,
                Factions=land.Factions.Select(f=>f with { Recruitment=new Dictionary<string,RecruitmentGrant> { [fortOffer.Id]=new() } }).ToArray(),
                Cells=land.Cells.Select((c,i)=>i==site?c with { Owner=builder }:c).ToArray() };
            Check(!WorldActions.CanRecruit(construction,site,fortOffer), "fort unavailable on painted ground before center capture: "+builder);
            Reject(()=>WorldActions.Recruit(construction,0,site,"built.fort",fortOffer),"recruit.site");
            var controlled = construction with { Cells=construction.Cells.Select((c,i)=>i==center?c with { Owner=builder }:c).ToArray() };
            Check(WorldActions.CanRecruit(controlled,site,fortOffer), "fort available after district center captured: "+builder);
            Check(WorldActions.Recruit(controlled,0,site,"built.fort",fortOffer).State.Units.Single().Owner==builder,
                "actual fort construction obeys district ownership: "+builder);
        }
        var deepFort = land with { Units = [fort with { Cell = 34 }] };
        Check(JurisdictionClaims.DestroyFort(deepFort, 34, "winner")[34].Owner == "loser", "destroyed deep fort does not bypass city frontier");
        var mover = own with { Cell = 24, RemainingMovement = 6 };
        var marching = land with { Units = [mover] };
        var marched = WorldActions.Move(marching, marching.Revision, mover.Id, 25).State;
        Check(marched.Units.Single().Cell == 25 && marched.Cells[25].Owner == "winner", "real movement captures a nonadjacent enemy center");
        var departed = WorldActions.Move(marched with { Units = marched.Units.Select(u=>u with { RemainingMovement=6 }).ToArray() }, marched.Revision, mover.Id, 24).State;
        Check(departed.Cells[25].Owner == "winner", "captured deep city retains its owner after troops leave");
        foreach (string kind in new[] { "city", "industry", "stable" })
        {
            var overseas = stranded with { Cells = stranded.Cells.Select((c,i)=>i==25?c with { BuildingKind=kind }:c).ToArray(), Units=[mover] };
            var landed = WorldActions.Move(overseas, overseas.Revision, mover.Id, 25).State;
            Check(landed.Cells[25].Owner == "winner" && landed.Cells[24].Owner == "winner", "overseas occupation transfers " + kind + " and its district without a land frontier");
            Check(WorldSave.Decode(WorldSave.Encode(landed), landed.ContentRevision).Cells[25].Owner == "winner", "overseas " + kind + " occupation survives save/load");
        }
        var invader = own with { Cell = 30, Combatant = own.Combatant with { Profile = own.Combatant.Profile with {
            DamageMultipliers = new Dictionary<string, float> { ["infantry"] = 1, ["fort"] = 1 } } } };
        var battleRules = new WorldBattleRules { CheckFactionSurvival = false, ResolveIgnition = false, RecalculateLocalMorale = false,
            InstallationDestructionProbability = 0, KillMoraleProbability = 0, SuppressionProbability = 0, KillDialogueProbability = 0 };
        var fortBattle = land with { Units = [invader, fort] };
        var defeatedFort = WorldBattle.Attack(fortBattle, 0, invader.Id, fort.Id, battleRules).State;
        Check(!defeatedFort.Units.Any(u => u.Id == fort.Id) && defeatedFort.Cells[farm].Owner == "winner", "real zero-health fort removal transfers its eligible territory");
        var deepBattle = land with { Units = [invader with { Cell = 33 }, fort with { Cell = 34 }] };
        var deepKilled = WorldBattle.Attack(deepBattle, 0, invader.Id, fort.Id, battleRules).State;
        Check(!deepKilled.Units.Any(u => u.Id == fort.Id) && deepKilled.Cells[34].Owner == "loser", "actual deep fort destruction cannot paint its jurisdiction");
        var surrendered = WorldEvents.Surrender(farmland, "loser", "winner").State;
        Check(surrendered.Cells[25].Owner == "loser" && ring.All(i => surrendered.Cells[i].Owner == farmland.Cells[i].Owner), "surrender cannot bypass deep districts or farm protection");
        Check(JurisdictionClaims.Filter(land, land.Cells.Select(c => c with { Owner = "loser" }).ToArray(), "loser")[20].Owner == "loser", "AI faction uses the same adjacent-jurisdiction policy");
        var nextTurn = WorldTurns.EndTurn(farmland with { ActiveFaction = "loser" }, 0, new WorldTurnRules {
            Buildings = [new("city", 1, new(0, 0, 0), ClaimsNeighbors: true), new("farmland", 1, new(0, 0, 0), ClaimsNeighbors: true)] }).State;
        Check(ring.All(i => nextTurn.Cells[i].Owner == farmland.Cells[i].Owner), "start-turn expansion cannot steal a protected farm ring");
        var restored = WorldSave.Decode(WorldSave.Encode(farmland), farmland.ContentRevision);
        Check(restored.Jurisdictions!.CellCenters.SequenceEqual(land.Jurisdictions.CellCenters)
            && restored.Jurisdictions.Borders.SequenceEqual(land.Jurisdictions.Borders), "fixed districts survive checksummed save/load");
        Check(JurisdictionClaims.Filter(restored, proposed, "winner")[25].Owner == "loser", "loaded district enforcement remains active");
        var copied = land.Copy(); ((int[])copied.Jurisdictions!.CellCenters)[0] = -1;
        Check(land.Jurisdictions.CellCenters[0] >= 0, "snapshot copies detach district arrays");
        var otherScenario = land with { ScenarioId="same-map-other-scenario", Jurisdictions=null,
            Cells=land.Cells.Select((c,i)=>i==22?c with { BuildingKind=null,BuildingLevel=0 }:i==23?c with { BuildingKind="city",BuildingLevel=1 }:c).ToArray() };
        Check(!TerritorialJurisdictions.Create(otherScenario).CellCenters.SequenceEqual(land.Jurisdictions.CellCenters), "same base map with different scenario centers has independent jurisdiction geometry");
        Reject(() => (land with { Jurisdictions = land.Jurisdictions with { CellCenters = [-1] } }).Validate(), "territory.jurisdictions");

        string assets = Path.Combine(root, "rewrite/data/original");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("original/", StringComparison.Ordinal) ? Path.Combine(assets, path[9..])
            : Path.Combine(root, "rewrite/data/legacy", path[5..]));
        var content = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        Check(content.Start("campaign1_07", null).Session.World.ContentRevision == "16ce79f438c920dd0cac73d0c1187082cb874040311a498d049e908fabe46ddd",
            "original display-release content fingerprint remains compatible before jurisdiction extension");
        var districts = new List<object>();
        foreach (var scenario in content.Scenarios.Where(s => s.Mode is 1 or 2))
        {
            string? country = scenario.Mode == 2 ? content.Battle(scenario.Id).Factions.First(f => f.Camp != 4).CountryId : null;
            var original = content.Start(scenario.Id, country);
            original.Session.Validate(); var world = original.Session.World;
            var areas = world.Jurisdictions!;
            Check(areas.CellCenters.Count == world.Cells.Count && (areas.CellCenters.Any(c => c >= 0)
                || !world.Cells.Any(c=>TerritorialJurisdictions.IsCenter(c)&&c.Owner!=null&&c.Terrain.Active)), "valid original district map " + scenario.Id);
            Check(!areas.CellCenters.Any(c=>c>=0) || world.Cells.Select((c, i) => (c, i)).All(p => !p.c.Terrain.Active || p.c.Terrain.Sea || p.c.BuildingKind=="port" || areas.CellCenters[p.i] >= 0), "land districts cover land when scenario has centers; ports remain independent " + scenario.Id);
            if (scenario.Mode == 2)
            {
                var profiles = original.Rules.Recruitment[world.ActiveFaction].Values.Where(o => o.Template != null).Select(o => o.Template!.Profile.Id).ToHashSet();
                Check(world.Units.Where(u => u.Owner == world.ActiveFaction).All(u => profiles.Contains(u.Combatant.Profile.Id)), "all starting unit grades have a base refund price " + scenario.Id);
            }
            districts.Add(new { scenario.Id, centers = areas.CellCenters.Distinct().Count(c => c >= 0), borders = areas.Borders.Count,
                land = world.Cells.Count(c => c.Terrain.Active && !c.Terrain.Sea),
                detachedLand = areas.DetachedCells.Count,
                unassignedLand = world.Cells.Select((c, i) => (c, i)).Count(p => p.c.Terrain.Active && !p.c.Terrain.Sea && areas.CellCenters[p.i] < 0) });
        }
        string folder = Path.Combine(root, "rewrite/reports/custom-mechanics/client-profile"); Directory.CreateDirectory(folder);
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object command, bool success = true)
        {
            using var parsed = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            var response = parsed.RootElement;
            Check(response.GetProperty("ok").GetBoolean() == success, route + " " + response.GetRawText()[..Math.Min(response.GetRawText().Length, 160)]);
            return response.GetProperty(success ? "data" : "code").Clone();
        }
        foreach (string id in new[] { "conquest1", "campaign1_01", "tutorials1", "tutorials2" })
        {
            var start = content.Start(id, id == "conquest1" ? "fra" : null);
            Check((start.Session.World.Jurisdictions != null) == !id.StartsWith("tutorial"), "mode-specific jurisdiction policy " + id);
            if (start.Session.World.Jurisdictions != null) start.Session.Validate();
            var view = Call("start", new { scenario = id, country = id == "conquest1" ? "fra" : null });
            while (view.GetProperty("dialogue").ValueKind != JsonValueKind.Null)
                view = Call("action", new { op = "dialogue", revision = view.GetProperty("revision").GetInt64() });
            var unit = view.GetProperty("units").EnumerateArray().First(u => u.GetProperty("owner").GetString() == view.GetProperty("playerFaction").GetString());
            var selection = Call("action", new { op = "select", revision = view.GetProperty("revision").GetInt64(), cell = unit.GetProperty("cell").GetInt32() });
            Check((selection.GetProperty("disband").ValueKind != JsonValueKind.Null) == (id == "conquest1"), "disband only appears in conquest " + id);
            if (id != "conquest1")
            {
                Check(Call("action", new { op = "disband", revision = view.GetProperty("revision").GetInt64(), unit = unit.GetProperty("id").GetString() }, false).GetString() == "disband.unavailable", "server rejects disband in " + id); continue;
            }
            var generalUnit = view.GetProperty("units").EnumerateArray().First(u => u.GetProperty("owner").GetString() == view.GetProperty("playerFaction").GetString()
                && u.GetProperty("commanderId").ValueKind != JsonValueKind.Null);
            var commandedSelection = Call("action", new { op = "select", revision = view.GetProperty("revision").GetInt64(), cell = generalUnit.GetProperty("cell").GetInt32() });
            Check(commandedSelection.GetProperty("disband").ValueKind == JsonValueKind.Null, "commanded conquest unit has no refund quote in API");
            Check(Call("action", new { op = "disband", revision = view.GetProperty("revision").GetInt64(), unit = generalUnit.GetProperty("id").GetString() }, false).GetString() == "disband.unavailable", "direct API cannot disband commanded units");
            var player = view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == view.GetProperty("playerFaction").GetString());
            var after = Call("action", new { op = "disband", revision = view.GetProperty("revision").GetInt64(), unit = unit.GetProperty("id").GetString() });
            Check(after.GetProperty("units").GetArrayLength() == view.GetProperty("units").GetArrayLength() - 1, "API removes initial conquest unit");
            var recipient = after.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == view.GetProperty("playerFaction").GetString());
            foreach (string resource in new[] { "money", "industry", "food" })
                Check(recipient.GetProperty("resources").GetProperty(resource).GetInt64() - player.GetProperty("resources").GetProperty(resource).GetInt64()
                    == selection.GetProperty("disband").GetProperty(resource).GetInt64(), "API refunds quoted " + resource);
            Check(Call("action", new { op = "disband", revision = view.GetProperty("revision").GetInt64(), unit = unit.GetProperty("id").GetString() }, false).GetString() == "client.stale", "repeated confirmation cannot duplicate refund");
            var loaded = Call("load", new { slot = 0, mode = 2 });
            Check(loaded.GetProperty("units").GetArrayLength() == after.GetProperty("units").GetArrayLength(), "disband autosave survives host load");
            var legacySession = start.Session with { World = start.Session.World with { Jurisdictions = null } };
            string legacyPath = Path.Combine(folder, "saves/mode-2/slot-6.json");
            File.WriteAllBytes(legacyPath, JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(id, "fra", SessionSave.Encode(legacySession)), ClientGame.Json));
            var migrated = Call("load", new { slot = 6, mode = 2 });
            Check(migrated.GetProperty("cells").EnumerateArray().Select((c, i) => (c, i)).All(p =>
                p.c.GetProperty("jurisdictionCenter").ValueKind == JsonValueKind.Null ? start.Session.World.Jurisdictions!.CellCenters[p.i] < 0
                    : p.c.GetProperty("jurisdictionCenter").GetInt32() == start.Session.World.Jurisdictions!.CellCenters[p.i]), "older save gains the original fixed jurisdiction geometry");
            Call("save", new { slot = 6, mode = 2 });
            var migratedSlot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(legacyPath), ClientGame.Json)!;
            var persisted = SessionSave.Decode(migratedSlot.Session, new(start.Session.ContentRevision, start.Session.World.ContentRevision, start.Session.Account.ContentRevision));
            Check(persisted.World.Jurisdictions!.DetachedCells.SequenceEqual(start.Session.World.Jurisdictions!.DetachedCells), "migrated disconnected land remains persisted");
        }
        var naval = content.Start("campaign1_02b",null);
        Check(naval.Rules.Battle.Combat.LethalStrikePreventsCounterattack, "playable combat enables backend lethal rule");
        var shooter = naval.Session.World.Units.Single(u=>u.Cell==2544);
        var navalMap = new HexMap(naval.Session.World.Width,naval.Session.World.Height);
        var victim = naval.Session.World.Units.First(u=>u.Owner!=shooter.Owner && naval.Session.World.Factions.Single(f=>f.Id==u.Owner).Camp!=naval.Session.World.Factions.Single(f=>f.Id==shooter.Owner).Camp
            && navalMap.Distance(shooter.Cell,u.Cell)>=shooter.Combatant.Profile.MinimumRange && navalMap.Distance(shooter.Cell,u.Cell)<=shooter.Combatant.Profile.MaximumRange);
        var lethal = naval.Session.World with { Units=naval.Session.World.Units.Select(u=>u.Id==shooter.Id?u with { Combatant=u.Combatant with { Skills=u.Combatant.Skills.Append(Ew4Combat.Skill(31)).Distinct().ToArray() } }
            :u.Id==victim.Id?u with { Combatant=u.Combatant with { Health=1 } }:u).ToArray() };
        BattlePreview lethalPreview;
        do { lethal=lethal with { RandomState=lethal.RandomState+1 };lethalPreview=WorldBattle.Preview(lethal,lethal.Revision,shooter.Id,victim.Id,naval.Rules.Battle); }
        while(!lethalPreview.Main.OffensiveProc||lethalPreview.Main.DefensiveProc);
        string feedback=Path.Combine(root,"rewrite/reports/presentation_feedback_20261019/test-profile/saves/mode-1");Directory.CreateDirectory(feedback);
        var lethalSlot = new ClientSlot("campaign1_02b",null,SessionSave.Encode(naval.Session with { World=lethal }));
        File.WriteAllBytes(Path.Combine(feedback,"slot-5.json"),JsonSerializer.SerializeToUtf8Bytes(lethalSlot,ClientGame.Json));
        Directory.CreateDirectory(Path.Combine(folder,"saves/mode-1"));
        File.WriteAllBytes(Path.Combine(folder,"saves/mode-1/slot-5.json"),JsonSerializer.SerializeToUtf8Bytes(lethalSlot,ClientGame.Json));
        var lethalView=Call("load",new { slot=5,mode=1 });
        var lethalResult=Call("action",new { op="attack",revision=lethalView.GetProperty("revision").GetInt64(),unit=shooter.Id,target=victim.Id });
        var combatView=lethalResult.GetProperty("combat");
        Check(!combatView.GetProperty("counterattack").GetBoolean() && combatView.GetProperty("counterDamage").GetInt32()==0,
            "lethal API combat explicitly forbids counterattack and damage");
        Check(combatView.GetProperty("critical").GetBoolean() && !combatView.GetProperty("counterCritical").GetBoolean()
            && combatView.GetProperty("damage").GetInt32()==lethalPreview.Main.DefenderDamage, "client sends authoritative offensive proc and exact damage");
        Check(lethalResult.GetProperty("notices").EnumerateArray().Any(n=>n.GetProperty("kind").GetString()=="unit.destroyed"), "lethal API result retains death presentation notice");
        File.WriteAllText(Path.Combine(root, "rewrite/reports/custom-mechanics/core.json"), JsonSerializer.Serialize(new { checks, errors = 0, districts }));
        return checks;
    }
}
