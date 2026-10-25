'use strict';
window.Ew4Performance=class {
  constructor(){
    this.records=[];this.build='';this.turn=null;this.entry=null;
    try{const saved=JSON.parse(localStorage.getItem('ew4.performance')||'[]');if(Array.isArray(saved))this.records=saved.slice(-32).filter(r=>r&&typeof r==='object');}catch(_){}
    document.addEventListener('visibilitychange',()=>{if(document.hidden&&this.turn)this.turn.interrupted=true;});
  }
  save(record){
    record.build=this.build;record.at=new Date().toISOString();
    this.records.push(record);this.records=this.records.slice(-32);
    try{localStorage.setItem('ew4.performance',JSON.stringify(this.records));}catch(_){}
  }
  async stage(name,action){
    const entry=this.entry,started=performance.now();
    try{return await action();}finally{if(entry)entry.stages[name]=(entry.stages[name]||0)+performance.now()-started;}
  }
  beginEntry(scenario){this.entry={kind:'entry',scenario,started:performance.now(),stages:{}};}
  endEntry(status){if(!this.entry)return;const entry=this.entry;this.entry=null;this.save({...entry,started:undefined,elapsedMs:performance.now()-entry.started,status});}
  beginTurn(world,settings){
    this.turn={kind:'turn',scenario:world.scenario,round:world.round,fast:settings.autoTurnFast,speed:settings.speed,started:performance.now()};
    return this.turn;
  }
  endTurn(status,turn=this.turn){if(!turn)return;if(this.turn===turn)this.turn=null;this.save({...turn,started:undefined,elapsedMs:performance.now()-turn.started,status});}
};
