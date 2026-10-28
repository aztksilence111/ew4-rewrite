using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class DiplomacyChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Diplomacy: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + " got " + e.Code); return; }
            throw new InvalidOperationException("Diplomacy: expected " + code);
        }
        var basis = WorldChecks.Example();
        var initial = basis with { Width = 5, Height = 5, Units = [],
            Factions = basis.Factions.Concat(new FactionState[] {
                new() { Id = "ally", Name = "盟友", Camp = "b" }, new() { Id = "neutral", Name = "中立", Camp = "n" } }).ToArray(),
            DiplomacyNeutralCamps = ["n"], Cells = Enumerable.Range(0,25).Select(i => new CellState {
                Owner = i < 5 ? "loser" : i == 5 ? "ally" : i == 6 ? "neutral" : "winner",
                BuildingKind = i switch {0=>"city",1=>"industry",2=>"stable",3=>"port",4=>"farmland",_=>null}, BuildingLevel = i<5?1:0
            }).ToArray(), Demands = new() { Seed = 1946 } };
        initial = DemandDiplomacy.Initialize(initial); initial.Validate();
        checks += DiplomacyHomelandChecks.Run(initial, root);
        checks += DiplomacyExpansionChecks.Run(root);
        Check(initial.Demands!.Buildings.Count == 4 && initial.Demands.Buildings.All(b => b.Cell != 4), "farmland excluded from baseline");
        Check(initial.Demands.Buildings.Select(b=>b.Cell).SequenceEqual(new[]{0,1,2,3}), "four eligible kinds");
        Check(DemandDiplomacy.Quote(initial,"winner","loser").DemandBlocked=="occupation", "must occupy an eligible building");
        var farm = initial with { Cells = initial.Cells.Select((c,i)=>i==4?c with { Owner="winner" }:c).ToArray() };
        farm = DemandDiplomacy.Captured(initial, farm, "winner", [4]);
        Check(farm.Demands!.Progress.Count==0 && DemandDiplomacy.Quote(farm,"winner","loser").DemandBlocked=="occupation", "farm capture grants neither eligibility nor credit");
        var war = DemandDiplomacy.DeclareWar(initial, initial.Revision, "winner", "ally").State;
        Check(WorldDiplomacy.Hostile(war,"winner","ally",["n"]) && WorldDiplomacy.Relation(war,"winner","neutral")=="neutral", "allied declaration is bilateral");
        Check(war.Factions.Select(f=>f.Camp).SequenceEqual(initial.Factions.Select(f=>f.Camp)), "declaration preserves camps");
        Reject(()=>DemandDiplomacy.DeclareWar(war, war.Revision,"winner","ally"),"diplomacy.war");
        war = DemandDiplomacy.DeclareWar(war, war.Revision,"winner","neutral").State;
        Check(WorldDiplomacy.Hostile(war,"neutral","winner",["n"])&&!WorldDiplomacy.Hostile(war,"neutral","ally",["n"]), "neutral declaration symmetric without third-party war");
        Reject(()=>DemandDiplomacy.DeclareWar(war,0,"winner","neutral"),"world.stale");
        Reject(()=>DemandDiplomacy.DeclareWar(initial,0,"winner","missing"),"diplomacy.target");
        Reject(()=>DemandDiplomacy.DeclareWar(initial,0,"winner","winner"),"diplomacy.target");
        var legacy = initial with { Demands = null };
        Check(DemandDiplomacy.Quote(legacy,"winner","loser").DemandBlocked=="legacy", "old world never fabricates baseline");
        Check(DemandDiplomacy.Quote(legacy,"winner","ally").WarBlocked==null, "old world can declare war");
        Check(!DemandDiplomacy.Initialize(initial with { Demands = new() { Legacy = true } }).Demands!.Ready, "legacy stays unavailable after initialization");
        var first = initial with { Cells = initial.Cells.Select((c,i)=>i==1?c with { Owner="winner" }:c).ToArray() };
        first = DemandDiplomacy.Captured(initial,first,"winner",[1]);
        Check(DemandDiplomacy.Quote(first,"winner","loser").Chance==25, "direct occupation contributes both weights");
        var third = first with { Cells = first.Cells.Select((c,i)=>i==2?c with { Owner="ally" }:c).ToArray() };
        Check(DemandDiplomacy.Quote(third,"winner","loser").Chance==44, "third-party loss contributes only loss weight and rounds away from zero");
        var failedBase = first with { Demands = first.Demands! with { Rules = new() { MaximumChance=0 } } };
        var failed = DemandDiplomacy.Demand(failedBase,failedBase.Revision,"winner","loser");
        Check(failed.Notices.Last().Kind=="diplomacy.refused" && failed.State.Demands!.Progress.Single().Attempts==1, "failure saved");
        Check(DemandDiplomacy.Quote(failed.State,"winner","loser").DemandBlocked=="progress", "failure locks retry");
        Reject(()=>DemandDiplomacy.Demand(failed.State,failed.State.Revision,"winner","loser"),"diplomacy.progress");
        var waiting = failed.State with { Round=2 };
        Check(DemandDiplomacy.Quote(waiting,"winner","loser").DemandBlocked=="progress", "waiting does not unlock");
        var lost = failed.State with { Cells = failed.State.Cells.Select((c,i)=>i==1?c with { Owner="loser" }:c).ToArray() };
        var retaken = DemandDiplomacy.Captured(lost,failed.State,"winner",[1]);
        Check(DemandDiplomacy.Quote(retaken,"winner","loser").DemandBlocked=="progress", "recapturing same city cannot buy retry");
        var next = failed.State with { Cells = failed.State.Cells.Select((c,i)=>i==2?c with { Owner="winner" }:c).ToArray() };
        Check(DemandDiplomacy.Quote(next,"winner","loser").DemandBlocked=="progress", "scripted transfer alone cannot unlock retry");
        next = DemandDiplomacy.Captured(failed.State,next,"winner",[2]);
        Check(DemandDiplomacy.Quote(next,"winner","loser").DemandBlocked==null, "new military capture unlocks retry");
        var noLongerHeld = next with { Cells=next.Cells.Select((c,i)=>i==2?c with { Owner="loser" }:c).ToArray() };
        Check(DemandDiplomacy.Quote(noLongerHeld,"winner","loser").DemandBlocked=="progress", "must still hold newly gained building");
        var second = DemandDiplomacy.Demand(next,next.Revision,"winner","loser").State;
        Check(second.Demands!.Progress.Single().Attempts==2 && DemandDiplomacy.Quote(second,"winner","loser").DemandBlocked=="progress", "each attempt consumes all new captures");
        Check(WorldSave.Encode(second).SequenceEqual(WorldSave.Encode(WorldSave.Decode(WorldSave.Encode(second),second.ContentRevision))), "all diplomacy state roundtrips");
        for(ulong seed=1;seed<=100;seed++)
        {
            var sample=first with { Demands=first.Demands! with { Seed=seed } };
            var a=DemandDiplomacy.Demand(sample,sample.Revision,"winner","loser");
            var reloaded=WorldSave.Decode(WorldSave.Encode(sample),sample.ContentRevision) with { RandomState=seed*177, AiRandomState=seed*991 };
            var b=DemandDiplomacy.Demand(reloaded,reloaded.Revision,"winner","loser");
            Check(a.Notices.Last().Kind==b.Notices.Last().Kind, "reload and unrelated RNG cannot redraw attempt "+seed);
        }
        var success = first with { Demands=first.Demands! with { Rules=new() { LossWeight=100,OccupationWeight=100,MaximumChance=100 } },
            Cells=first.Cells.Select((c,i)=>i==2?c with { Owner="winner" }:c).ToArray(),
            Units=basis.Units.Select(u=>u.Owner=="loser"?u:u with { Owner="ally" }).ToArray() };
        var accepted=DemandDiplomacy.Demand(success,success.Revision,"winner","loser");
        Check(accepted.Notices.Last().Kind=="diplomacy.accepted" && accepted.State.Factions.Single(f=>f.Id=="loser").Defeated,"success defeats target");
        Check(accepted.State.Units.All(u=>u.Owner!="loser")&&accepted.State.Units.Any(u=>u.Owner=="ally"),"only target army removed");
        Check(accepted.State.Cells[0].Owner=="winner"&&accepted.State.Cells[4].Owner=="winner","remaining assets including farms transfer normally");
        Check(accepted.State.Factions.Single(f=>f.Id=="winner").Resources==success.Factions.Single(f=>f.Id=="winner").Resources,"no resource stockpile inherited");
        var mover=initial with { Units=basis.Units.Select(u=>u.Owner=="winner"?u with { Cell=8,RemainingMovement=6 }:u).ToArray() };
        var moved=WorldActions.Move(mover,mover.Revision,"unit.2",3).State;
        Check(moved.Demands!.Progress.Any(p=>p.Actor=="winner"&&p.Captured.Contains(3)), "real military movement records captures");
        var protectedWorld=initial with { Demands=initial.Demands! with { Rules=new() { LockedFactions=["loser"] } } };
        Check(DemandDiplomacy.Quote(protectedWorld,"winner","loser").DemandBlocked=="protected", "scenario locks checked server-side");
        Reject(()=>DemandDiplomacy.Demand(initial with { ActiveFaction="loser" },0,"winner","loser"),"diplomacy.turn");
        Reject(()=>(initial with { Demands=initial.Demands! with { Buildings=[new(1000,"loser")] } }).Validate(),"diplomacy.state");
        var clone=second.Copy();clone.Demands!.Progress.Single().Captured[0]=3;
        Check(second.Demands!.Progress.Single().Captured[0]==1,"snapshot isolates capture arrays");

        byte[] Read(string path)=>File.ReadAllBytes(path.StartsWith("data/",StringComparison.Ordinal)?Path.Combine(root,"rewrite/data/legacy",path[5..]):Path.Combine(root,"rewrite/data",path));
        var content=new ClientContent(Read,Directory.EnumerateFiles(Path.Combine(root,"rewrite/data/original"),"*.btl"));
        var started=content.Start("conquest1","fra");
        Check(started.Session.World.Demands!=null,"conquest initializes diplomacy");
        foreach(var choice in content.Scenarios.Where(s=>s.Mode==1).Take(3))
            Check(content.Start(choice.Id,null).Session.World.Demands==null,"campaign has no diplomacy state "+choice.Id);
        var defaults=DiplomacyConfiguration.Read(Read);
        Check(defaults.Rules.MaximumChance==95&&defaults.Presentation.Title=="外交","default configuration loads");
        byte[] custom=Encoding.UTF8.GetBytes("{\"schema\":1,\"presentation\":{\"title\":\"使节\",\"width\":400,\"reasons\":{\"progress\":\"继续攻城\"}}}");
        var mod=DiplomacyConfiguration.Read(p=>p==DiplomacyConfiguration.Path?custom:Read(p));
        Check(mod.Presentation.Title=="使节"&&mod.Presentation.Width==400&&mod.Presentation.Reasons["progress"]=="继续攻城"&&mod.Presentation.Reasons.ContainsKey("legacy"),"partial Mod configuration retains defaults");
        Reject(()=>DiplomacyConfiguration.Read(p=>p==DiplomacyConfiguration.Path?Encoding.UTF8.GetBytes("{\"presentation\":{\"entryIcon\":\"client/../bad.png\"}}"):Read(p)),"diplomacy.presentation");
        var profile=Path.Combine(root,"build/diplomacy-20261028/check-profile");Directory.CreateDirectory(profile);
        var host=new ClientGame(content,profile);
        JsonElement Call(string route,object command,bool ok=true)
        {
            using var doc=JsonDocument.Parse(host.Dispatch("/api/"+route,JsonSerializer.Serialize(command,ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean()==ok,route+": "+doc.RootElement.GetRawText()[..Math.Min(180,doc.RootElement.GetRawText().Length)]);
            return doc.RootElement.GetProperty(ok?"data":"code").Clone();
        }
        var campaign=content.Scenarios.First(s=>s.Mode==1);
        var campaignView=Call("start",new {scenario=campaign.Id});
        Check(!campaignView.GetProperty("diplomacyEnabled").GetBoolean(),"campaign hides UI");
        Check(Call("diplomacy",new{},false).GetString()=="diplomacy.mode","campaign read API blocked");
        var campaignTarget=campaignView.GetProperty("factions").EnumerateArray().First(f=>f.GetProperty("id").GetString()!=campaignView.GetProperty("playerFaction").GetString()).GetProperty("id").GetString();
        Check(Call("action",new {op="declare_war",revision=campaignView.GetProperty("revision").GetInt64(),target=campaignTarget},false).GetString()=="diplomacy.mode","campaign action API blocked");
        var live=started.Session.World with { ActiveFaction=started.Session.PlayerFaction, Phase=TurnPhase.Active, Outcome=BattleOutcome.Continue,
            PendingDialogue=null, QueuedDialogues=[], Story=null, ScenarioEvents=[], Events=[], Demands=new() { Seed=1946 } };
        live=DemandDiplomacy.Initialize(live);
        var enemy=live.Factions.First(f=>!f.Human&&!f.Defeated&&WorldDiplomacy.Relation(live,started.Session.PlayerFaction,f.Id)=="hostile"
            && live.Demands!.Buildings.Count(b=>b.Faction==f.Id)>=3);
        var home=live.Demands!.Buildings.Where(b=>b.Faction==enemy.Id).Select(b=>b.Cell).ToArray();
        WorldState Capture(WorldState before,int[] cells)
        {
            var after=before with { Cells=before.Cells.Select((c,i)=>cells.Contains(i)?c with { Owner=started.Session.PlayerFaction }:c).ToArray() };
            return DemandDiplomacy.Captured(before,after,started.Session.PlayerFaction,cells);
        }
        var failureWorld=Capture(live,[home[0]]);
        failureWorld=failureWorld with { Demands=failureWorld.Demands! with { Rules=new() { MaximumChance=0 } } };
        var successWorld=Capture(live,home.Take((home.Length+1)/2).ToArray());
        successWorld=successWorld with { Demands=successWorld.Demands! with { Rules=new() { LossWeight=100,OccupationWeight=100,MaximumChance=100 } } };
        void Fixture(string directory,int slot,WorldState world)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory,"slot-"+slot+".json"),JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot("conquest1","fra",SessionSave.Encode(started.Session with { World=world })),ClientGame.Json));
        }
        Fixture(profile,2,failureWorld);Fixture(profile,3,successWorld);Fixture(profile,4,live with { Demands=null });
        var loaded=Call("load",new{slot=2});
        var failedView=Call("action",new{op="demand_surrender",revision=loaded.GetProperty("revision").GetInt64(),target=enemy.Id});
        Check(failedView.GetProperty("notices").EnumerateArray().Any(n=>n.GetProperty("kind").GetString()=="diplomacy.refused"),"client demand failure reported");
        Call("load",new{slot=0,mode=2});
        var lockedQuote=Call("diplomacy",new{}).GetProperty("factions").EnumerateArray().Single(f=>f.GetProperty("id").GetString()==enemy.Id).GetProperty("quote");
        Check(lockedQuote.GetProperty("demandBlocked").GetString()=="progress","autosave and reload retain failure lock");
        loaded=Call("load",new{slot=3});
        var successView=Call("action",new{op="demand_surrender",revision=loaded.GetProperty("revision").GetInt64(),target=enemy.Id});
        Check(successView.GetProperty("factions").EnumerateArray().Single(f=>f.GetProperty("id").GetString()==enemy.Id).GetProperty("defeated").GetBoolean(),"client surrender commits defeat");
        Check(successView.GetProperty("units").EnumerateArray().All(u=>u.GetProperty("owner").GetString()!=enemy.Id),"client surrender removes target units");
        Call("load",new{slot=4});
        Check(Call("diplomacy",new{}).GetProperty("factions").EnumerateArray().Single(f=>f.GetProperty("id").GetString()==enemy.Id).GetProperty("quote").GetProperty("demandBlocked").GetString()=="legacy","old client slot has no invented occupation history");
        string browserFixtures=Path.Combine(root,"build/diplomacy-20261028/browser-fixtures");
        Fixture(browserFixtures,2,failureWorld);Fixture(browserFixtures,3,successWorld);Fixture(browserFixtures,4,live with { Demands=null });
        return checks;
    }
}
