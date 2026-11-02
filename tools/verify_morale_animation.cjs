'use strict';
const assert=require('node:assert/strict');
require('../rewrite/Ew4.Web/wwwroot/map-surface.js');
require('../rewrite/Ew4.Web/wwwroot/battlefield.js');
let checks=0,frames=0,alpha=1;
const calls=[],stack=[],draw={get globalAlpha(){return alpha;},set globalAlpha(value){alpha=value;},save(){stack.push(alpha);},restore(){alpha=stack.pop();}};
const renderer=new Ew4Battlefield.Renderer({requestFrame:()=>frames++,sprite:(atlas,name,x,y)=>calls.push({name,x,y,alpha})});
renderer.state={};renderer.draw=draw;renderer.factions=new Map();renderer.flag=()=>{};
const snapshot=(morale,revision=1)=>({revision,units:[{id:'u',morale}]});
const check=(value,label)=>{assert.ok(value,label);checks++;};
const render=(morale,time,x=100,y=100)=>{calls.length=0;renderer.time=time;renderer.visibleUnitMarkers=[{unit:{id:'u',morale},p:{x,y}}];renderer.unitMarkers();return [...calls];};
for(const [from,to] of [[0,1],[1,0],[0,-1],[-1,0],[-1,-2],[-2,-1],[-3,1],[1,-3]]){
  renderer.resetMotions();renderer.observeMorale(snapshot(from),snapshot(to,2),{time:100});
  const transition=renderer.moraleTransitions.get('u'),direction=to>from?-1:1;
  check(transition.direction===direction,'direction '+from+' -> '+to);
  const start=render(to,100),middle=render(to,425),end=render(to,750);
  check(start.length===(from===0?0:1)&&start.every(c=>c.y===75&&c.alpha===1),'previous status at start');
  check(middle.length>0&&middle.every(c=>c.alpha>0&&c.alpha<1),'intermediate opacity');
  check(middle.filter(c=>c.name===transition.from).every(c=>Math.sign(c.y-75)===direction),'outgoing direction');
  check(middle.filter(c=>c.name===transition.to).every(c=>Math.sign(c.y-75)===-direction),'incoming direction');
  check(end.length===(to===0?0:1)&&end.every(c=>c.y===75&&c.x===115&&c.alpha===1),'stable final marker');
  const moved=render(to,425,130,150);check(moved.every((c,i)=>c.x===middle[i].x+30&&c.y===middle[i].y+50),'follows presented unit');
  const active=renderer.moraleTransitions.get('u');renderer.observeMorale(snapshot(to,2),snapshot(to,2),{time:1000});
  check(renderer.moraleTransitions.get('u')===active,'duplicate snapshot does not restart');
  renderer.unitById=new Map([['u',{morale:to}]]);renderer.time=2000;renderer.syncMorale();check(!renderer.moraleTransitions.size,'expired transition pruned');
}
renderer.observeMorale(snapshot(0),snapshot(1,2),{time:0,speed:4});check(renderer.moraleTransitions.get('u').duration===325,'speed scales duration');
renderer.observeMorale(snapshot(1,2),snapshot(-1,3),{time:20});check(renderer.moraleTransitions.size===1&&renderer.moraleTransitions.get('u').direction===1,'new change replaces previous');
renderer.observeMorale(snapshot(-1,3),{revision:4,units:[]},{time:30});check(!renderer.moraleTransitions.size,'removed unit pruned');
renderer.observeMorale({revision:1,units:[]},snapshot(1,2),{time:0});check(!renderer.moraleTransitions.size,'new unit has no old change');
for(const options of [{animate:false},{}]){renderer.observeMorale(snapshot(0),snapshot(1,2),{time:0});renderer.observeMorale(options.animate===false?snapshot(1,2):null,snapshot(-1,3),options);check(!renderer.moraleTransitions.size,'skip or initial load clears');}
for(const value of [-4,2,127,undefined]){renderer.observeMorale(snapshot(0),snapshot(value,2),{time:0});check(!renderer.moraleTransitions.size,'unsupported Mod state remains static');}
renderer.observeMorale(snapshot(0),snapshot(1,2),{time:0});renderer.resetMotions();check(!renderer.moraleTransitions.size&&frames>0,'reset and rendering wakeup');
console.log(JSON.stringify({checks,errors:0}));
