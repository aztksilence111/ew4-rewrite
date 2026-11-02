using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class ScenarioEconomyChecks
{
    internal static int Run(string root)
    {
        int checks=0;
        void Check(bool pass,string label){checks++;if(!pass)throw new Exception("Scenario economy: "+label);}
        void Reject(Action action,string code){try{action();}catch(RuleViolation e){Check(e.Code==code,code+" got "+e.Code);return;}throw new Exception("Accepted "+code);}
        var initial=WorldChecks.Example() with {ScenarioPlayerFaction="winner",Round=2,
            Cells=WorldChecks.Example().Cells.Select((c,i)=>c with {BuildingKind=i==7?"farmland":i==8?"city":null,BuildingLevel=i>=7?1:0}).ToArray()};
        var rules=new WorldTurnRules{Buildings=[new("city",1,new(20,2,0)),new("city",2,new(30,3,0)),new("farmland",1,new(0,0,30))]};
        WorldState Execute(WorldState w,string id,params ScenarioEffect[] effects){var ready=w with {ScenarioEvents=w.ScenarioEvents.Append(new(){Id=id,Effects=effects}).ToArray()};ScenarioRules.Validate(ready,true);return ScenarioRules.Evaluate(ready).State;}
        Check(!ScenarioEconomy.Enabled(initial),"baseline has no economy extension");
        var configured=Execute(initial,"food",new(){Type="faction_income",Faction="winner",Income=new(Food:10)},
            new(){Type="site_income",Cells=[8],Income=new(Food:60)});
        Check(initial.Cells[8].IncomeAdjustment==null&&initial.Factions[1].IncomeBonus.Food==0,"original state untouched");
        var site=WorldTurns.SiteIncome(configured.Cells[8],8,rules,null);
        Check(site==new Resources(20,2,60),"food-only patch preserves tax and industry");
        var upgraded=configured.Cells[8] with {BuildingLevel=2};
        Check(WorldTurns.SiteIncome(upgraded,8,rules,null)==new Resources(30,3,60),"upgrade changes unpatched income");
        var nation=configured.Factions.Single(f=>f.Id=="winner");
        var view=ScenarioEconomy.Food(configured,nation,rules);
        Check(view.Total==100&&view.Base==10&&view.Farmland==30&&view.Settlements==60,"source breakdown reconciles");
        var paid=WorldTurns.Resume(configured with {Phase=TurnPhase.StartFaction},configured.Revision,rules).State;
        Check(paid.Factions.Single(f=>f.Id=="winner").TurnSummary!.Income.Food==view.Total,"actual turn settlement matches food source view");
        var taken=Execute(configured,"take",new(){Type="cells_owner",Faction="loser",Cells=[7]},
            new(){Type="unit_owner",Faction="loser",Unit="unit.2"},new(){Type="cells_owner",Faction="loser",Cells=[8]});
        Check(ScenarioEconomy.Food(taken,taken.Factions.Single(f=>f.Id=="winner"),rules).Total==10,"occupation cuts former owner income");
        Check(ScenarioEconomy.Food(taken,taken.Factions.Single(f=>f.Id=="loser"),rules).Total==90,"grain follows current owner once");
        var shortage=TurnEconomy.Start(2,new(0,0,0),new(0,0,10),90);
        Check(shortage.Shortage==4&&TurnEconomy.Morale(0,0,0,shortage.Shortage,false,false)==-3,"base cannot defeat starvation tactic");
        var restored=Execute(configured,"restore",new ScenarioEffect(){Type="site_income_default",Cells=[8]});
        Check(WorldTurns.SiteIncome(restored.Cells[8],8,rules,null)==new Resources(20,2,0),"reset restores building rules");
        var saved=WorldSave.Decode(WorldSave.Encode(configured),configured.ContentRevision);
        Check(saved.Cells[8].IncomeAdjustment?.Food==60&&saved.Factions[1].IncomeBonus.Food==10,"save retains explicit economy");
        Check(ScenarioRules.Evaluate(saved).Notices.Count==0,"save does not duplicate income setup");
        Check(!System.Text.Encoding.UTF8.GetString(WorldSave.Encode(initial)).Contains("incomeAdjustment"),"unconfigured save omits new cell field");
        var updated=Execute(configured,"partial",new ScenarioEffect(){Type="site_income",Cells=[8],Income=new(Money:55)});
        Check(WorldTurns.SiteIncome(updated.Cells[8],8,rules,null)==new Resources(55,2,60),"partial update preserves prior food");
        Reject(()=>Execute(initial,"bad",new ScenarioEffect(){Type="site_income",Cells=[8],Income=new(Food:-1)}),"scenario.income");
        Reject(()=>Execute(initial,"empty",new ScenarioEffect(){Type="faction_income",Income=new()}),"scenario.income");
        Reject(()=>Execute(initial,"field",new ScenarioEffect(){Type="site_income",Cells=[6],Income=new(Food:5)}),"scenario.income_site");
        var child=Execute(configured,"child",new ScenarioEffect(){Type="faction_create",Faction="child",Name="Child",Camp="c",TemplateFaction="winner",Income=new(Food:2)});
        Check(child.Factions.Single(f=>f.Id=="child").IncomeBonus.Food==2,"new faction overrides template bonus");
        var bad=initial.Cells[8] with {IncomeAdjustment=new("city",Food:-1)};
        Reject(()=>(initial with {Cells=initial.Cells.Select((c,i)=>i==8?bad:c).ToArray()}).Validate(),"world.cell_income");
        Check(WorldTurns.SiteIncome(configured.Cells[8] with {BuildingKind=null},8,rules,null)==new Resources(0,0,0),"removed building stops site income");
        var demolished=configured with {Cells=configured.Cells.Select((c,i)=>i==8?c with {BuildingKind=null,BuildingLevel=0}:c).ToArray()};
        Check(WorldSave.Decode(WorldSave.Encode(demolished),demolished.ContentRevision).Cells[8].BuildingKind==null,"demolished income site remains saveable");
        var farmRules=ScenarioEconomy.BuildingRules(rules,[new("farmland",1,new(Food:48))]);
        Check(WorldTurns.SiteIncome(configured.Cells[7],7,farmRules,null).Food==48&&WorldTurns.SiteIncome(configured.Cells[7],7,rules,null).Food==30,"building rules isolated from original");
        Check(farmRules.Buildings[0]==rules.Buildings[0],"nonfarm rules unchanged");
        Reject(()=>ScenarioEconomy.BuildingRules(rules,[new("farmland",9,new(Food:2))]),"scenario.building_income");
        Reject(()=>ScenarioEconomy.BuildingRules(rules,[new("farmland",1,new(Food:2)),new("farmland",1,new(Food:3))]),"scenario.building_income");
        var different=configured with {Cells=configured.Cells.Select((c,i)=>i==8?c with {BuildingKind="farmland"}:c).ToArray()};
        var replaced=Execute(different,"replace",new ScenarioEffect(){Type="site_income",Cells=[8],Income=new(Money:1)});
        Check(WorldTurns.SiteIncome(replaced.Cells[8],8,rules,null)==new Resources(1,0,30),"replacement building cannot inherit stale patch");
        var rollback=configured with {ScenarioEvents=configured.ScenarioEvents.Append(new ScriptedScenarioEvent{Id="rollback",Effects=[
            new(){Type="faction_income",Faction="winner",Income=new(Food:999)},new(){Type="food",Faction="winner",Value=-999999}],
            InsufficientResourcesEffects=[new(){Type="state",Key="scenario/fallback",Value=1}]}).ToArray()};
        var rolled=ScenarioRules.Evaluate(rollback).State;
        Check(rolled.Factions[1].IncomeBonus.Food==10&&rolled.ExtensionState["scenario/fallback"]==1,"failed event rolls back economy atomically");
        var data=Path.Combine(root,"rewrite/data");byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(data,p.StartsWith("data/")?"legacy/"+p[5..]:p));
        var content=new ClientContent(Read,["conquest1"]);var start=content.Start("conquest1","fra");
        Check(!ScenarioEconomy.Enabled(start.Session.World),"real original conquest unchanged");
        var roundtrip=SessionSave.Decode(SessionSave.Encode(start.Session),new(start.Session.ContentRevision,start.Session.World.ContentRevision,start.Session.Account.ContentRevision));
        Check(roundtrip.World.Factions.Select(f=>f.IncomeBonus).SequenceEqual(start.Session.World.Factions.Select(f=>f.IncomeBonus)),"baseline session preserves economy");
        var manifest=System.Text.Json.Nodes.JsonNode.Parse(Read("rewrite/extension.json"))!;
        manifest["apiVersion"]=3;manifest["economy"]="rewrite/test-economy.json";
        byte[] configuredManifest=JsonSerializer.SerializeToUtf8Bytes(manifest);
        string settings="{\"schema\":1,\"scenarios\":{\"conquest1\":[{\"kind\":\"farmland\",\"level\":1,\"income\":{\"food\":77}}]}}";
        byte[] ModRead(string path)=>path=="rewrite/extension.json"?configuredManifest:path=="rewrite/test-economy.json"?System.Text.Encoding.UTF8.GetBytes(settings):Read(path);
        var scoped=new ClientContent(ModRead,["conquest1","conquest2"]);
        Check(scoped.Start("conquest1","fra").Rules.Turns.Buildings.Single(b=>b.Kind=="farmland"&&b.Level==1).Income.Food==77,"loader applies named scenario configuration");
        Check(scoped.Start("conquest2","fra").Rules.Turns.Buildings.Single(b=>b.Kind=="farmland"&&b.Level==1).Income.Food==5,"other scenario in same mod unchanged");
        Check(content.Start("conquest1","fra").Rules.Turns.Buildings.Single(b=>b.Kind=="farmland"&&b.Level==1).Income.Food==5,"new mod leaves original content unchanged");
        settings=settings.Replace("conquest1","missing");
        Reject(()=>new ClientContent(ModRead,["conquest1"]),"extension.economy");
        settings="{\"schema\":1,\"scenarios\":{\"*\":[]}}";
        Reject(()=>new ClientContent(ModRead,["conquest1"]),"extension.economy");
        return checks;
    }
}
