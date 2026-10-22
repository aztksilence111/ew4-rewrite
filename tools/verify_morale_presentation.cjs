'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
require('../rewrite/Ew4.Web/wwwroot/map-surface.js');
require('../rewrite/Ew4.Web/wwwroot/battlefield.js');
const folder=path.resolve(process.argv[2]||'build/morale-restoration-20261022');
const native=JSON.parse(fs.readFileSync(path.join(folder,'native.json'),'utf8'));
let checks=0;
for(const vector of native.vectors){
  const draws=[],renderer=new Ew4Battlefield.Renderer({sprite:(atlas,sprite,x,y)=>draws.push({sprite,x,y})});
  renderer.state={};renderer.factions=new Map();renderer.flag=()=>{};
  renderer.visibleUnitMarkers=[{unit:{id:'unit',morale:vector.morale},p:{x:native.unitPosition[0],y:native.unitPosition[1]}}];
  renderer.unitMarkers();assert.deepEqual(draws,vector.draws);checks++;
}
for(const value of [undefined,null,0,2,3,127,-4,NaN]){assert.equal(Ew4Battlefield.moraleSprite(value),null);checks++;}
console.log(JSON.stringify({checks,errors:0,source:'native ARM64 draw submissions; unsupported Mod morale has no indexed sprite'}));
