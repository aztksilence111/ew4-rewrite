using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

// Optional operations expressed by Mod data; no person or faction identities.
public static partial class CommandOrders
{
    private static bool Operation(CommandOrder r) => r.Plan?.StartsWith("op_", StringComparison.Ordinal) == true;
    public static bool SecondaryAvailable(WorldState w,UnitState source,ExtensionSkill skill,string enemy,string secondary)
    {
        if(Unit(w,secondary) is not {} extra)return false;
        if(skill.Order?.Plan=="op_sabotage")return Distance(w,extra.Cell,EnemyCell(w,enemy))==1 && !AttackLocked(w,extra);
        return Unit(w,enemy) is {} target && Distance(w,target.Cell,extra.Cell)<=(skill.Order?.Plan=="kill_shock_advance"?2:1);
    }
    public static bool AttackLocked(WorldState w, UnitState u) => Get(w,u,"attack_lock")==Epoch(w,u.Owner)+1;
    private static bool MoveLocked(WorldState w, UnitState u) => Get(w,u,"move_lock")==Epoch(w,u.Owner)+1;
    private static bool TokenLive(WorldState w, UnitState u, OrderGrant g, RuleEventBus bus)
    {
        if (!bus.Skills.TryGetValue(g.Skill,out var skill) || skill.Order is not {} r) return false;
        if (r.Plan=="op_delay") return u.Owner==g.Auxiliary && Epoch(w,u.Owner)<=g.Stage
            && Epoch(w,g.Owner)<g.Epoch+1;
        if (r.Plan=="op_contact") return u.CommanderId==g.RecipientCommander && Epoch(w,u.Owner)<=g.Stage
            && Unit(w,g.Source) is {} s && s.CommanderId==g.Commander && s.Owner==g.Owner
            && WorldDiplomacy.Allied(w,s.Owner,u.Owner) && bus.Bound(s.Combatant).Any(b=>b.Skill.Id==g.Skill && b.Slot.InstanceId==g.Instance);
        return false;
    }
    private static bool LandForce(WorldState w, UnitState u) => TacticalSkills.Mobile(w,u) && !u.Transport;
    private static bool Tank(UnitState u) => u.Combatant.Profile.CommanderAbility=="tyej.armor"
        && !u.Combatant.Profile.Traits.Contains("armored_car");
    private static bool OperationCandidate(WorldState w,UnitState source,UnitState u,CommandOrder r)
    {
        bool pending(string plan)=>u.Orders.Any(g=>g.Plan==plan && g.Uses==0 && (plan!="op_contact" || Epoch(w,u.Owner)<=g.Stage));
        return r.Plan switch {
            "op_contact" => LandForce(w,u) && !pending(r.Plan),
            "op_delay" => LandForce(w,u) && !pending(r.Plan) && w.Units.Any(a=>a.Owner==source.Owner && Category(w,a)=="infantry"
                && LandForce(w,a) && Distance(w,source.Cell,a.Cell)<=2 && Distance(w,a.Cell,u.Cell)==1),
            "op_swap" => LandForce(w,u) && Get(w,u,"staff_swap")!=Epoch(w,u.Owner)+1,
            "op_entrench" => LandForce(w,u) && !u.MovedThisTurn && !MoveLocked(w,u)
                && (w.Cells[u.Cell].BuildingKind!=null || w.Cells[u.Cell].DefenseTerrain is "plain" or "land" or "hill"),
            "op_recover" => LandForce(w,u) && !u.MovedThisTurn && !MoveLocked(w,u),
            "op_mountain" => LandForce(w,u) && !u.MovedThisTurn && !MoveLocked(w,u)
                && WorldActions.Reachable(w,u.Id).Keys.Any(c=>w.Cells[c].DefenseTerrain is "forest" or "mountain"),
            "op_relocate" => LandForce(w,u) && u.MovedThisTurn && !u.AttackedThisTurn && !AttackLocked(w,u)
                && (Tank(u) || Category(w,u)=="artillery" && !u.Combatant.Profile.Traits.Contains("rocket"))
                && Get(w,u,"staff_transfer")!=Epoch(w,u.Owner)+1 && OperationRange(w,u,r).Count>0,
            "op_retreat" => LandForce(w,u) && !u.AttackedThisTurn && !AttackLocked(w,u)
                && Get(w,u,"staff_transfer")!=Epoch(w,u.Owner)+1 && !pending(r.Plan),
            _ => true
        };
    }
    private static bool OperationAvailable(WorldState w, UnitState s, ExtensionSkill skill)
    {
        var r=skill.Order!;
        return r.Plan switch {
            "op_artillery" => LandForce(w,s) && Category(w,s)=="artillery" && !s.MovedThisTurn && Get(w,s,"any_move")!=Epoch(w,s.Owner)+1,
            "op_air_ground" => WorldUnitLayers.Air(w,s) && Weapon(w,s,"surface"),
            "op_armor" => LandForce(w,s) && Tank(s) && Get(w,s,"normal_net")>=2,
            "op_cavalry" => LandForce(w,s) && Category(w,s)=="cavalry" && !Tank(s) && s.MovedThisTurn
                && s.RemainingMovement==0 && !s.AttackedThisTurn && !AttackLocked(w,s) && !MoveLocked(w,s)
                && Get(w,s,"staff_transfer")!=Epoch(w,s.Owner)+1 && OperationRange(w,s,r).Count>0,
            "op_recruit" or "op_sabotage" => OperationSites(w,s,r).Any(),
            _ => true
        };
    }
    private static IEnumerable<int> OperationSites(WorldState w,UnitState s,CommandOrder r) => Enumerable.Range(0,w.Cells.Count).Where(c=>
        Distance(w,s.Cell,c)<=r.Recipients.Radius && w.Cells[c].BuildingKind is "city" or "industry" && w.Cells[c].BuildingConstructionTurns==0
        && w.Cells[c].Owner is {} owner && (r.Plan=="op_recruit"?owner==s.Owner:!WorldDiplomacy.Allied(w,s.Owner,owner))
        && !SiteTokens(w,c,r.Plan!).Any() && (r.Plan!="op_sabotage" || !w.ExtensionState.TryGetValue($"orders/site/{c}/{w.Cells[c].Owner}/{w.Cells[c].BuildingLevel}/{SiteCode(w.Cells[c].BuildingKind)}",out long expiry) || expiry<Epoch(w,w.Cells[c].Owner!))
        && (r.Plan!="op_sabotage" || w.Units.Any(u=>u.Owner==s.Owner && Category(w,u)=="infantry" && LandForce(w,u)
            && u.Morale.Effective!=-3 && !u.Acted && !u.AttackedThisTurn && !AttackLocked(w,u)
            && Distance(w,s.Cell,u.Cell)<=2 && Distance(w,u.Cell,c)==1))
        && w.Factions.Single(f=>f.Id==owner).Recruitment.Keys.Any(id=>w.Construction==null || w.Construction.Buildings.SelectMany(b=>b.Value.Recruitment)
            .Concat(w.Construction.Recruitment??Array.Empty<ModBuildingRecruit>()).Any(o=>o.Id==id && (o.BuildingKinds==null || o.BuildingKinds.Contains(w.Cells[c].BuildingKind!)))));

    private static IEnumerable<(UnitState Source,OrderGrant Grant)> SiteTokens(WorldState w,int cell,string plan) => w.Units
        .SelectMany(u=>u.Orders.Where(g=>g.Plan==plan && g.EnemyCell==cell && g.Uses==0 && g.Positions?.Length==2
            && w.Cells[cell].Owner==g.Auxiliary && w.Cells[cell].BuildingLevel==g.Positions[0]
            && SiteCode(w.Cells[cell].BuildingKind)==g.Positions[1]
            && (plan=="op_sabotage"?Epoch(w,g.Auxiliary!)<=g.Stage:Epoch(w,g.Owner)==g.Epoch && u.CommanderId==g.Commander))
            .Select(g=>(u,g)));
    private static int SiteCode(string? kind)=>kind=="city"?1:kind=="industry"?2:0;
    internal static WorldState SitesChanged(WorldState before,WorldState w)
    {
        if(!w.ExtensionState.Keys.Any(k=>k.StartsWith("orders/site/",StringComparison.Ordinal))
            && !w.Units.Any(u=>u.Orders.Any(g=>g.Plan is "op_recruit" or "op_sabotage")))return w;
        var changed=Enumerable.Range(0,w.Cells.Count).Where(i=>before.Cells[i].Owner!=w.Cells[i].Owner
            || before.Cells[i].BuildingKind!=w.Cells[i].BuildingKind || before.Cells[i].BuildingLevel!=w.Cells[i].BuildingLevel).ToHashSet();
        if(changed.Count==0)return w;
        var prefixes=changed.Select(i=>$"orders/site/{i}/").ToArray();
        return w with{ExtensionState=w.ExtensionState.Where(p=>!prefixes.Any(prefix=>p.Key.StartsWith(prefix,StringComparison.Ordinal))).ToDictionary(p=>p.Key,p=>p.Value),
            Units=w.Units.Select(u=>u with{Orders=u.Orders.Where(g=>g.Plan is not ("op_recruit" or "op_sabotage") || !changed.Contains(g.EnemyCell)).ToArray()}).ToArray()};
    }
    public static bool RecruitmentBlocked(WorldState w,int cell) => w.ExtensionState.TryGetValue($"orders/site/{cell}/{w.Cells[cell].Owner}/{w.Cells[cell].BuildingLevel}/{SiteCode(w.Cells[cell].BuildingKind)}",out long turn)
        && Epoch(w,w.Cells[cell].Owner!)==turn;
    public static Resources RecruitmentPrice(WorldState w,int cell,RecruitmentOffer offer,Resources native,RuleEventBus bus)
    {
        if(offer.Template is not {} t || t.Profile.Category is not ("infantry" or "artillery")) return native;
        var discounts=SiteTokens(w,cell,"op_recruit").Where(p=>bus.Bound(p.Source.Combatant).Any(b=>b.Skill.Id==p.Grant.Skill && b.Slot.InstanceId==p.Grant.Instance))
            .Select(p=>bus.Skills[p.Grant.Skill].Order!.Bonus).ToArray();
        if(discounts.Length==0)return native;
        return new(Math.Min(native.Money,(long)Math.Ceiling(offer.Cost.Money*(1-discounts.Max(b=>b.MoneyDiscount)))),
            Math.Min(native.Industry,(long)Math.Ceiling(offer.Cost.Industry*(1-discounts.Max(b=>b.IndustryDiscount)))),native.Food);
    }
    public static WorldState AfterRecruitment(WorldState w,int cell,RecruitmentOffer offer)
    {
        if(offer.Template?.Profile.Category is not ("infantry" or "artillery"))return w;
        foreach(var (s,g) in SiteTokens(w,cell,"op_recruit").ToArray()) w=UpdateGrant(w,s.Id,g,g with{Uses=1});
        return w;
    }
    private static void ValidateOperation(WorldState w,UnitState s,IReadOnlyList<UnitState> targets,ExtensionSkill skill,string? enemy,string? secondary)
    {
        var r=skill.Order!;var foe=Unit(w,enemy);
        bool valid=r.Plan switch {
            "op_artillery" => foe!=null && LegalAttack(w,s,foe) && !AttackLocked(w,s)
                && TacticalSkills.Ground(w,foe) && (Category(w,foe)=="fort" || w.Cells[foe.Cell].BuildingKind=="city"),
            "op_air_ground" or "op_armor" => foe!=null && LegalAttack(w,s,foe) && !AttackLocked(w,s) && LandForce(w,foe)
                && (r.Plan!="op_armor" || w.Cells[foe.Cell].BuildingKind==null)
                && (r.Plan!="op_air_ground" || Get(w,s,"coordinated."+foe.Id)!=Epoch(w,s.Owner)+1),
            "op_swap" => targets.Count==2 && targets.Select(u=>Category(w,u)).OrderBy(x=>x).SequenceEqual(new[]{"artillery","infantry"}),
            "op_sabotage" or "op_recruit" => enemy!=null && enemy.StartsWith("cell.",StringComparison.Ordinal)
                && OperationSites(w,s,r).Contains(EnemyCell(w,enemy))
                && (r.Plan!="op_sabotage" || secondary==null || Unit(w,secondary) is {} a && a.Owner==s.Owner && Category(w,a)=="infantry"
                    && LandForce(w,a) && !a.Acted && !a.AttackedThisTurn && !AttackLocked(w,a) && a.Morale.Effective!=-3
                    && Distance(w,s.Cell,a.Cell)<=2 && Distance(w,a.Cell,EnemyCell(w,enemy))==1),
            _ => true
        };
        if(!valid)throw new RuleViolation("orders.operation","No legal ordinary action or operation target");
    }
    private static WorldState ActivateOperation(WorldState w,UnitState s,ExtensionSkill skill,SkillSlot slot,IReadOnlyList<UnitState> targets,string? enemy,string? secondary)
    {
        var r=skill.Order!;
        foreach(var old in targets)
        {
            var u=Unit(w,old.Id)!;
            if(r.Plan=="op_swap")w=Set(w,u,"staff_swap",Epoch(w,u.Owner)+1);
            if(r.Plan is "op_entrench" or "op_recover")
            {w=Set(w,u,"move_lock",Epoch(w,u.Owner)+1);w=Replace(w,Unit(w,u.Id)! with{RemainingMovement=0});}
            if(r.Plan is "op_contact" or "op_delay")
            {
                var g=Unit(w,u.Id)!.Orders.Last(x=>x.Skill==skill.Id && x.Source==s.Id);
                long turn=Epoch(w,u.Owner)+(r.Plan=="op_delay" || u.Owner!=s.Owner || u.Acted || u.RemainingMovement==0?1:0);
                w=UpdateGrant(w,u.Id,g,g with{Stage=(int)turn,Auxiliary=u.Owner});
                if(r.Plan=="op_contact" && turn==Epoch(w,u.Owner))
                {
                    var latest=Unit(w,u.Id)!;int oldLimit=latest.ExtensionMovementLimit??latest.Combatant.Profile.Movement;
                    int limit=Math.Max(oldLimit,latest.Combatant.Profile.Movement+r.Bonus.Movement);
                    w=Replace(w,latest with{RemainingMovement=latest.RemainingMovement+limit-oldLimit,ExtensionMovementLimit=limit});
                }
            }
        }
        if(r.Plan=="op_artillery")w=Set(w,s,"move_lock",Epoch(w,s.Owner)+1);
        if(r.Plan is "op_recruit" or "op_sabotage")
        {
            int c=EnemyCell(w,enemy);var u=Unit(w,s.Id)!;var g=u.Orders.Last(x=>x.Skill==skill.Id && x.Source==s.Id);
            w=UpdateGrant(w,s.Id,g,g with{Enemy=null,EnemyCell=c,Auxiliary=w.Cells[c].Owner,
                Stage=(int)Epoch(w,w.Cells[c].Owner!)+(r.Plan=="op_sabotage"?1:0),Positions=new[]{w.Cells[c].BuildingLevel,SiteCode(w.Cells[c].BuildingKind)}});
            if(r.Plan=="op_sabotage")
            {
                var actor=secondary==null?w.Units.First(a=>a.Owner==s.Owner && Category(w,a)=="infantry" && LandForce(w,a) && !a.Acted
                    && !a.AttackedThisTurn && !AttackLocked(w,a) && a.Morale.Effective!=-3 && Distance(w,s.Cell,a.Cell)<=2 && Distance(w,a.Cell,c)==1):Unit(w,secondary)!;
                w=Set(w,actor,"attack_lock",Epoch(w,s.Owner)+1);
                var state=w.ExtensionState.ToDictionary(p=>p.Key,p=>p.Value);
                state[$"orders/site/{c}/{w.Cells[c].Owner}/{w.Cells[c].BuildingLevel}/{SiteCode(w.Cells[c].BuildingKind)}"]=Epoch(w,w.Cells[c].Owner!)+1;
                w=w with{ExtensionState=state};
            }
        }
        if(r.Plan is "op_relocate" or "op_cavalry")
        {
            var actors=r.Plan=="op_cavalry"?new[]{s}:targets.ToArray();
            if(r.Plan=="op_cavalry")w=Set(w,s,"attack_lock",Epoch(w,s.Owner)+1);
            var u=Unit(w,s.Id)!;
            var g=u.Orders.Last(x=>x.Skill==skill.Id && x.Source==s.Id);
            w=UpdateGrant(w,s.Id,g,g with{Pending=true,Stage=0,Participants=actors.Select(a=>a.Id).ToArray(),Auxiliary=actors[0].Id});
        }
        return w;
    }
    private static IReadOnlyDictionary<int,ReachableCell> OperationRange(WorldState w,UnitState u,CommandOrder r,RuleEventBus? bus=null)
    {
        if(MoveLocked(w,u))return new Dictionary<int,ReachableCell>();
        var context=WorldActions.MovementContext(w,u,new(){Extensions=bus});
        bool plain(int c)=>w.Cells[c].Owner==u.Owner && w.Cells[c].DefenseTerrain is "plain" or "land" && !w.Cells[c].Terrain.Sea;
        if(r.Plan=="op_relocate" && !plain(u.Cell))return new Dictionary<int,ReachableCell>();
        int budget=r.Plan=="op_cavalry"?r.Followup:3;
        var cells=context.Cells.Select((c,i)=>c with{Active=c.Active && !c.Sea && (r.Plan!="op_relocate" || plain(i))}).ToArray();
        // Relocation cannot cross rivers, including a first-step overdraw.
        if(r.Plan=="op_relocate")cells=context.Cells.Select((c,i)=>c with{Active=c.Active && plain(i)}).ToArray();
        var range=Movement.Reachable(new(w.Width,w.Height),cells,u.Cell,context.Actor with{Remaining=budget,FullBudget=budget},new(){AllowFirstStepOverdraw=false,AllowOverdrawnEntry=false});
        return range.Where(p=>p.Key!=u.Cell && LegalDestination(w,u,p.Key,u.Id) && (r.Plan!="op_retreat" || p.Value.Path.Count==2)
            && (r.Plan!="op_relocate" || p.Value.Path.Count==2 && !RiverEdge(w,u.Cell,p.Key))).ToDictionary(p=>p.Key,p=>p.Value);
    }
    private static bool RiverEdge(WorldState w,int from,int to)
    {
        var map=new HexMap(w.Width,w.Height);int d=Enumerable.Range(0,6).First(n=>map.Neighbor(from,n)==to);
        return (w.Cells[to].Terrain.RiverEdges&(1<<((d+3)%6)))!=0;
    }
    private static WorldState CompleteOperationMove(WorldState w,UnitState s,ExtensionSkill skill,IReadOnlyList<string>? selected,RuleEventBus bus)
    {
        var g=s.Orders.First(x=>x.Skill==skill.Id && x.Pending);var actor=Unit(w,g.Auxiliary);
        if(selected?.Count!=1)throw new RuleViolation("orders.path","Choose a destination or stay in place");
        if(selected[0]!="stay")
        {
        var range=actor==null?new Dictionary<int,ReachableCell>():OperationRange(w,actor,skill.Order!,bus);
        if(actor==null || !int.TryParse(selected[0].Replace("cell.","",StringComparison.Ordinal),out int cell) || !range.ContainsKey(cell))
            throw new RuleViolation("orders.path","Choose a legal extra movement destination");
        w=Set(w,actor,"staff_transfer",Epoch(w,actor.Owner)+1);
        w=WorldActions.OrderMove(w,actor,cell,range,new(){Extensions=bus});
        }
        int next=g.Stage+1;var actors=g.Participants!;
        w=UpdateGrant(w,s.Id,g,g with{Pending=next<actors.Length,Stage=next,Auxiliary=next<actors.Length?actors[next]:null,Uses=1});
        return w;
    }
    public static WorldState ExecuteStrike(WorldState w,UnitState old,ExtensionSkill skill,string? enemy,WorldBattleRules? battle)
    {
        if(skill.Order?.Plan is not ("op_artillery" or "op_air_ground" or "op_armor"))return w;
        if(battle==null)throw new RuleViolation("orders.battle","Ordinary battle rules are required");
        var change=WorldBattle.Attack(w,w.Revision,old.Id,enemy!,battle);
        return change.State with{Revision=w.Revision};
    }
    private static WorldState OperationMovement(WorldState before,WorldState w,UnitState old)
    {
        if(Unit(w,old.Id) is not {} u)return w;
        w=Set(w,u,"any_move",Epoch(w,u.Owner)+1);
        w=Set(w,u,"normal_net",Distance(w,old.Cell,u.Cell));
        foreach(var g in u.Orders.Where(g=>g.Plan=="op_contact" && g.Stage==Epoch(w,u.Owner) && g.Uses==0).ToArray())w=UpdateGrant(w,u.Id,g,g with{Uses=1});
        return w;
    }
    private static WorldState OperationStart(WorldState w,string owner,RuleEventBus bus)
    {
        foreach(var u in w.Units.Where(t=>t.Owner==owner).ToArray())
        {
            w=Set(w,u,"normal_net",0);
            foreach(var g in u.Orders.Where(g=>g.Plan is "op_contact" or "op_delay" && TokenLive(w,u,g,bus) && g.Stage==Epoch(w,owner) && g.Uses==0).ToArray())
            {
                var latest=Unit(w,u.Id)!;
                if(g.Plan=="op_delay") {w=Replace(w,latest with{RemainingMovement=Math.Min(latest.RemainingMovement,Math.Max(1,latest.RemainingMovement-bus.Skills[g.Skill].Order!.Bonus.MovementPenalty))});w=UpdateGrant(w,u.Id,g,g with{Uses=1});}
                else if(!latest.Acted && !latest.MovedThisTurn)
                {
                    int oldLimit=latest.ExtensionMovementLimit??bus.MovementBudget(latest.Combatant),limit=Math.Max(oldLimit,latest.Combatant.Profile.Movement+bus.Skills[g.Skill].Order!.Bonus.Movement);
                    w=Replace(w,latest with{RemainingMovement=latest.RemainingMovement+limit-oldLimit,ExtensionMovementLimit=limit});
                }
            }
        }
        return w;
    }
    internal static WorldState RestoreOrdinaryMovement(WorldState before,WorldState w,UnitState old)
    {
        var u=Unit(w,old.Id);if(u==null)return w;
        w=Set(w,u,"normal_net",Get(before,old,"normal_net"));
        return Replace(w,u with{Orders=u.Orders.Select(g=>g.Plan=="op_contact" && old.Orders.FirstOrDefault(x=>x.Skill==g.Skill && x.Source==g.Source) is {} prior?g with{Uses=prior.Uses}:g).ToArray()});
    }
    private static WorldState OperationBattle(WorldState before,WorldState w,UnitState a,UnitState d,RuleEventBus bus)
    {
        foreach(var g in a.Orders.Where(g=>g.Plan=="op_retreat" && g.Uses==0 && Live(before,a,g,bus)).ToArray())
        {
            if(Unit(w,a.Id)==null || Get(w,a,"staff_transfer")==Epoch(w,a.Owner)+1 || Unit(w,g.Source) is not {} source)continue;
            var own=source.Orders.FirstOrDefault(x=>x.Skill==g.Skill && x.Source==source.Id);
            if(own!=null)
                w=UpdateGrant(w,source.Id,own,own.Pending
                    ? own with{Participants=(own.Participants??Array.Empty<string>()).Append(a.Id).Distinct().ToArray()}
                    : own with{Pending=true,Stage=0,Participants=new[]{a.Id},Auxiliary=a.Id,Uses=1});
            if(source.Id!=a.Id)w=UpdateGrant(w,a.Id,g,g with{Uses=1});
        }
        return w;
    }
}
