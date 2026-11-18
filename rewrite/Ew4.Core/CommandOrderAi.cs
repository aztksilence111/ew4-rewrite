using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public static partial class CommandOrders
{
    public static WorldChange? AiAction(WorldState w,RuleEventBus bus,WorldMoveRules move,WorldBattleRules? battle=null)
    {
        if (!bus.OrdersEnabled || w.PendingOrderAction!=null || w.PendingDialogue!=null) return null;
        foreach(var source in w.Units.Where(u=>u.Owner==w.ActiveFaction).OrderBy(u=>u.Id,StringComparer.Ordinal))
        foreach(var (skill,slot) in bus.Bound(source.Combatant).Where(p=>p.Skill.Order!=null))
        {
            var r=skill.Order!; bool pending=HasPending(w,source,skill);
            if ((!pending && skill.Kind!="active") || !pending && w.ExtensionState.GetValueOrDefault(TacticalSkills.CooldownKey(source,skill,slot.InstanceId))>w.Round
                || !CanActivate(w,source,skill,bus)) continue;
            if (!pending && r.Mode is not ("heal" or "income" or "summon") && r.Income==0 && r.Plan!="payload_action"
                && !w.Units.Any(e=>!WorldDiplomacy.Allied(w,source.Owner,e.Owner) && Distance(w,source.Cell,e.Cell)<=6)) continue;
            if(LegalSelections(w,source,skill) is {} legal)
            {
                foreach(var choice in legal)
                    try { return WorldSkillActions.Execute(w,w.Revision,source.Id,slot.InstanceId,choice.Enemy,bus,move,targets:choice.Targets,battle:battle); }
                    catch(RuleViolation) { }
                continue;
            }
            var candidates=Candidates(w,source,skill,bus).Take(10).ToArray();
            var enemies=EnemyTargets(w,source,skill).Take(10).Cast<string?>().ToList();
            if(r.Enemy==null || r.EnemyOptional || pending) enemies.Insert(0,null);
            var secondary=r.SecondaryEnemy==null?null:w.Units.Where(t=>Matches(w,source,t,r.SecondaryEnemy)
                && Distance(w,source.Cell,t.Cell)<=r.SecondaryEnemy.Radius).OrderBy(t=>t.Combatant.Health).FirstOrDefault()?.Id;
            int count=pending?1:r.Mode=="summon"?r.Profiles.Length:r.Mode=="swap"?r.IncludesSelf?1:2:r.SelectSelf?0:Math.Min(r.Recipients.Count,candidates.Length);
            foreach(var selection in Choices(candidates,count))
            foreach(var enemy in enemies)
            {
                try { return WorldSkillActions.Execute(w,w.Revision,source.Id,slot.InstanceId,enemy,bus,move,targets:selection,secondaryTarget:secondary,battle:battle); }
                catch(RuleViolation) { /* A combination may fail a plan's joint weapon/terrain constraints. */ }
            }
        }
        return null;
        static IEnumerable<string[]> Choices(string[] candidates,int count)
        {
            if(count==0) { yield return Array.Empty<string>();yield break; }
            if(count==1) { foreach(var c in candidates) yield return new[]{c};yield break; }
            if(count>candidates.Length) yield break;
            // Order matters for mixed-type reactivation and paired swaps.
            foreach(var first in candidates)
                foreach(var rest in Choices(candidates.Where(c=>c!=first).ToArray(),count-1))
                    yield return new[]{first}.Concat(rest).ToArray();
        }
    }
}
