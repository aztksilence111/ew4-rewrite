'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs');
require('../rewrite/Ew4.Web/wwwroot/map-surface.js');
require('../rewrite/Ew4.Web/wwwroot/battlefield.js');
require('../rewrite/Ew4.Web/wwwroot/effects.js');
const vectors=JSON.parse(fs.readFileSync('reverse/reports/frontend_draw_vectors.json','utf8'));
const manifest=JSON.parse(fs.readFileSync('rewrite/data/client/manifest.json','utf8'));
let checks=0;
const markers=JSON.parse(fs.readFileSync('reverse/reports/objective_marker_draws.json','utf8'));
for(const fixture of markers.objectives){
  const selected=Ew4Battlefield.objectiveMarker({objectiveMarker:fixture.site},{objectiveMarker:fixture.unit});
  assert.equal(selected===1||selected===2,fixture.draws.length>0);checks++;
}
assert.deepEqual(Ew4Battlefield.flagPosition({x:320,y:540}),{x:320+markers.flagOffset[0],y:540+markers.flagOffset[1]});checks++;
for(const [time,frame] of [[0,1],[99,1],[100,2],[200,3],[300,4],[400,1],[501,2]]){assert.equal(Ew4Battlefield.flagFrame(time),frame);checks++;}
const relationState={playerFaction:'player',factions:[{id:'player',camp:10},{id:'ally',camp:10},{id:'enemy',camp:20},{id:'third',camp:30}],cells:[{owner:'ally'},{owner:'enemy'},{owner:'third'},{owner:null}],units:[]};
for(const [cell,expected] of [[0,false],[1,true],[2,true],[3,false]]){assert.equal(Ew4Battlefield.selectionHostile(relationState,cell),expected);checks++;}
relationState.units=[{cell:0,owner:'enemy'},{cell:1,owner:'ally'}];
assert.equal(Ew4Battlefield.selectionHostile(relationState,0),true);assert.equal(Ew4Battlefield.selectionHostile(relationState,1),false);checks+=2;
relationState.factions.push({id:'neutral',camp:40,neutral:true});
for(const [owner,color] of [['player','blue'],['ally','blue'],['enemy','red'],['third','red'],['neutral','black']]){assert.equal(Ew4Battlefield.badgeColor(relationState,owner),color);checks++;}
relationState.cells.push({owner:'neutral'});assert.equal(Ew4Battlefield.selectionHostile(relationState,4),true);checks++;
assert.equal(Ew4Battlefield.recruitOffset(0),-60);assert.equal(Ew4Battlefield.recruitOffset(Ew4Battlefield.recruitDuration()),0);
assert.equal(Ew4Battlefield.recruitOffset(Ew4Battlefield.recruitDuration()/2),-30);checks+=3;
for(const test of vectors.coastCases){assert.deepEqual(Ew4MapSurface.coastTriangles(test.neighbors),test.triangles);checks++;}
for(let mask=0;mask<64;mask++){
  const neighbors=Array.from({length:6},(_,d)=>({active:true,sea:false,owner:mask&(1<<d)?'other':'self'}));
  assert.equal(Ew4MapSurface.borderMask({owner:'self'},neighbors),mask);
  const expected=vectors.borders['2'][mask].map(q=>q.map(v=>[v[0]/2,v[1]/2,...v.slice(2)]));
  assert.deepEqual(manifest.mapDrawing.borders[mask],expected);checks+=2;
}
assert.deepEqual(Ew4MapSurface.rgba(0xaabbccdd),[221,204,187,170]);
const pixels=Ew4MapSurface.rasterize(vectors.ownedFill);
// Shared triangle edges must not produce pinholes or double alpha blending.
for(let y=0;y<pixels.height;y++)for(let x=0;x<pixels.width;x++){
  const px=(x+.5)/2-32,py=(y+.5)/2-36;
  if(Math.abs(px)<30&&Math.abs(py)<34-Math.abs(px)*.5625){assert.equal(pixels.data[(y*pixels.width+x)*4+3],170);checks++;}
}
assert.equal(Ew4MapSurface.borderMask({owner:'a'},[null,{active:true,sea:true,owner:'b'},{active:false,sea:false,owner:'b'}]),0);
assert.deepEqual(Ew4Battlefield.terrainRows({tileInfo:{income:{money:10,food:0},penalties:{infantry:12,cavalry:30,artillery:5}}}).map(r=>r.text),['+10','-12%','-30%','-5%']);
assert.deepEqual(Ew4Battlefield.terrainRows({tileInfo:{income:{money:10},penalties:{infantry:12,cavalry:30,artillery:5}}}).map(r=>!!r.defense),[false,true,true,true]);checks++;
const services=JSON.parse(fs.readFileSync('reverse/reports/frontend_service_contracts.json','utf8'));
for(const v of services.impacts){assert.equal(Ew4Effects.impact(v.weapon,v.category,v.damage,v.sea,v.facing),v.name,JSON.stringify(v));checks++;}
const effects=JSON.parse(fs.readFileSync('rewrite/data/client/effects.json','utf8'));
const engine=new Ew4Effects.Effects({},effects);
for(const [name,definitions] of Object.entries(effects.definitions)){
  const instance={seed:1,emitters:[]};engine.emit(instance,{type:'effect',name,at:0});
  for(const emitter of instance.emitters){emitter.advance(.1);
    assert.ok(emitter.particles.every(p=>['x','y','vx','vy','life','scale','rotation'].every(k=>Number.isFinite(p[k]))),name);checks++;
    assert.ok(emitter.peak<=100,'original emitter capacity '+name);checks++;
  }
}
const report={checks,errors:0,scope:'256 native mixed-coast submissions, 64 HD border masks, original packed color and interpolation coverage; not Android pixel acceptance.'};
fs.writeFileSync('rewrite/reports/frontend_native.json',JSON.stringify(report,null,2));console.log(JSON.stringify(report));
