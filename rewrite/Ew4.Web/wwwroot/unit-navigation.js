'use strict';
(function(root){
  function groups(state){
    const result=[[],[]];
    if(!state||state.phase!=='Active'||state.dialogue||state.scenarioPopup||state.activeFaction!==state.playerFaction
      ||!state.factions.some(f=>f.id===state.activeFaction&&f.human&&!f.defeated))return result;
    for(const unit of state.units){
      if(unit.owner!==state.playerFaction||unit.health<=0||unit.morale===-3||unit.constructionTurns>0)continue;
      const move=unit.canNavigateMove===true,attack=unit.canAttack===true&&!unit.acted;
      if(!move&&!attack)continue;
      const untouched=!unit.acted&&(unit.remainingMovement>0||unit.category==='fort');
      result[untouched?0:1].push(unit);
    }
    return result;
  }
  class Cycle{
    constructor(){this.reset();}
    reset(){this.turn=null;this.seen=new Set();}
    next(state){
      const turn=state&&JSON.stringify([state.scenario,state.round,state.activeFaction,state.playerFaction]);
      if(turn!==this.turn){this.reset();this.turn=turn;}
      const candidates=groups(state).flatMap((units,tier)=>units.map(unit=>({unit,key:tier+':'+unit.id})));
      let next=candidates.find(entry=>!this.seen.has(entry.key));
      if(!next){this.seen.clear();next=candidates[0];}
      if(next)this.seen.add(next.key);
      return next?.unit;
    }
  }
  root.Ew4UnitNavigation={groups,Cycle};
})(globalThis);
