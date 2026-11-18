using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record OrderSelection(string[] Targets, string? Enemy);

public static partial class CommandOrders
{
    // Joint constraints are shared by availability and the UI. Order is retained:
    // the first destination designates the rear hex in a paired swap.
    public static IEnumerable<OrderSelection>? LegalSelections(WorldState w, UnitState source, ExtensionSkill skill)
    {
        var r=skill.Order;
        if(r==null || HasPending(w,source,skill) || r.Mode=="summon") return null;
        if(r.Mode!="swap" && r.Plan is not ("calibration" or "bombard_suppress" or "terrain_sequence" or "combined_shock"
            or "naval_block" or "encircled_target" or "attacker_morale" or "different_type_terrain"
            or "battle_confusion_escape" or "attack_or_kill_shock") && r.Enemy==null) return null;
        return Enumerate();
        IEnumerable<OrderSelection> Enumerate()
        {
            var candidates=Candidates(w,source,skill);
            var enemies=EnemyTargets(w,source,skill).Cast<string?>().ToList();
            if(r.Enemy==null || r.EnemyOptional) enemies.Insert(0,null);
            int min=r.SelectSelf?0:r.Mode=="swap"?r.IncludesSelf?1:2:r.Plan=="terrain_sequence"?2:1;
            int max=r.Plan=="different_type_terrain"?2:min;
            for(int count=min;count<=max;count++)
            foreach(var ids in Selections(count,Array.Empty<string>()))
            foreach(var enemy in enemies)
            {
                var recipients=ids.Select(id=>Unit(w,id)!).ToList();
                if(r.IncludesSelf || r.SelectSelf) recipients.Insert(0,source);
                if(Valid(recipients,enemy)) yield return new(ids,enemy);
            }
            IEnumerable<string[]> Selections(int count,string[] prefix)
            {
                if(count==0) {yield return prefix;yield break;}
                foreach(var id in candidates.Where(id=>!prefix.Contains(id)))
                    foreach(var selection in Selections(count-1,prefix.Append(id).ToArray())) yield return selection;
            }
            bool Valid(List<UnitState> recipients,string? enemy)
            {
                try
                {
                    if(r.Mode=="swap")
                    {
                        var a=recipients[0];var b=recipients[1];
                        if(a.Id==b.Id || Distance(w,a.Cell,b.Cell)!=1
                            || !LegalDestination(w,a,b.Cell,a.Id,b.Id) || !LegalDestination(w,b,a.Cell,a.Id,b.Id)) return false;
                        ValidateSwap(w,source,a,b,r,enemy);
                    }
                    ValidatePlanTargets(w,source,recipients,skill,enemy,null);
                    return true;
                }
                catch(RuleViolation) { return false; }
            }
        }
    }
}
