'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
let checks=0;
const check=(value,label)=>{assert.ok(value,label);checks++;};
for(const folder of ['rewrite/Ew4.Web/wwwroot','rewrite/Ew4.Web/wwwroot/compat']){
  const sandbox={};
  for(const file of ['map-surface.js','map-views.js','battlefield.js'])vm.runInNewContext(fs.readFileSync(folder+'/'+file,'utf8'),sandbox);
  const {Projection}=sandbox.Ew4MapViews;
  const factions=[{id:'north',camp:1,color:0x60997744},{id:'south',camp:2,color:0x60447799}];
  const center={active:true,sea:false,owner:'north',jurisdictionCenter:0};
  const mountain={active:true,sea:false,owner:null,geographyType:'impassable_mountain',jurisdictionCenter:null};
  const cells=[center,mountain,{active:true,sea:false,owner:'north'}];
  const state={scenario:'fixture',width:3,height:1,cells,factions};
  const map={territoryDisplay:{fixture:[[1,0,-2]]}};
  const projection=new Projection();
  check(projection.sync(state,map),'first state invalidates ground');
  const display=projection.display(mountain,1);
  check(display.owner==='north'&&display.jurisdictionCenter===-2,'mountain follows center but has an independent visual district');
  check(mountain.owner===null&&mountain.jurisdictionCenter===null,'gameplay mountain remains untouched');
  check(projection.display(center,0)===center,'normal land preserves object and gameplay fields');
  check(!projection.sync({...state},map),'unchanged linked owner preserves cache');
  const captured={...state,cells:[{...center,owner:'south'},mountain,cells[2]],changedCells:[0]};
  check(projection.sync(captured,map),'distant linked mountain invalidates cached ground on capture');
  check(projection.display(mountain,1).owner==='south','capture updates mountain tint');
  for(const mode of ['normal','camp','district'])check(projection.color(projection.display(mountain,1),mode)===projection.color(captured.cells[0],mode),mode+' follows parent color');
  check(projection.sync(state,map)&&projection.display(mountain,1).owner==='north','undo or load restores linked owner');
  const neutral={...state,cells:[{...center,owner:null},mountain,cells[2]]};
  check(projection.sync(neutral,map)&&projection.display(mountain,1).owner===null,'neutralization removes tint and invalidates cache');
  check(projection.sync({...state,scenario:'other'},map)&&projection.display(mountain,1)===mountain,'another scenario on the same map has no effect');
  projection.sync(state,{});
  check(projection.display(mountain,1)===mountain,'unconfigured maps preserve original presentation');
  projection.sync({scenario:'empty',factions},{});
  check(projection.color({owner:null},'normal')===0,'minimal existing projection fixtures remain compatible');
  for(const bad of [[1,-1,-2],[1,1,-2],[2,0,-2],[-1,0,-2],[1,0.5,-2],['1',0,-2],[1,0,1],[1,0,-4],[1,0]]){
    projection.sync(state,{territoryDisplay:{fixture:[bad]}});
    check(projection.display(mountain,1)===mountain,'invalid link is ignored: '+JSON.stringify(bad));
  }
  projection.sync({...state,cells:[center,{...mountain,geographyType:'mountain_pass'},cells[2]]},map);
  check(projection.display(mountain,1)===mountain,'a traversable pass cannot receive a synthetic district');
  projection.sync(captured,map);
  const renderer=new sandbox.Ew4Battlefield.Renderer({manifest:{mapDrawing:{borderTexture:'fixture'}},image:()=>({})});
  renderer.map={};
  renderer.projection=projection;renderer.state=captured;renderer.mapMode='normal';renderer.factions=new Map(factions.map(f=>[f.id,f]));
  renderer.visibleCells=[{site:mountain,cell:1,p:{x:64,y:0}}];
  const fills=[],borders=[];
  renderer.draw={beginPath(){},moveTo(){},lineTo(){},closePath(){},fill(){fills.push(this.fillStyle);},drawImage(){}};
  renderer.ownership();
  check(fills.length===1&&fills[0].startsWith('rgba('),'ownership renders a projected mountain even though actual owner is null');
  check(renderer.neighbors(0)[0].owner==='south','neighbor coast and national border masks use projected ownership');
  renderer.surface.border=(mask)=>{borders.push(mask);return {};};renderer.borders();
  check(borders.length===1,'projected mountain participates in national border rendering');
  let districtStrokes=0;
  sandbox.document={createElement:()=>({getContext:()=>({scale(){},translate(){},beginPath(){},moveTo(){},lineTo(){},setLineDash(){},stroke(){districtStrokes++;}})})};
  renderer.visibleCells.push({site:captured.cells[0],cell:0,p:{x:0,y:0}});
  renderer.mapMode='district';renderer.districtBorders();
  check(districtStrokes===1,'independent mountain district boundary remains visible');
  const untouched=JSON.stringify(captured.cells);
  renderer.ownership();renderer.borders();renderer.districtBorders();
  check(JSON.stringify(captured.cells)===untouched,'all rendering paths preserve actual state');
  const ridgeState={...state,width:4,cells:[center,mountain,{...mountain},center]};
  projection.sync(ridgeState,{territoryDisplay:{fixture:[[1,0,-2],[2,0,-2]]}});
  renderer.projection=projection;renderer.state=ridgeState;
  renderer.visibleCells=[{site:mountain,cell:1,p:{x:64,y:0}}];
  renderer.districtCanvases=new Map();districtStrokes=0;renderer.districtBorders();
  check(projection.display(mountain,1).jurisdictionCenter===projection.display(mountain,2).jurisdictionCenter,'connected mountain cells share the authored district');
  check(districtStrokes===0,'no internal per-hex dashed edge within a continuous mountain district');
  const splitMap={territoryDisplay:{fixture:[[1,0,-2],[2,0,-3]]}};
  check(projection.sync(ridgeState,splitMap),'changing mountain boundary invalidates ground');
  renderer.districtBorders();check(districtStrokes===1,'authored mountain-pass split remains visible');
}
console.log(JSON.stringify({checks,errors:0}));
if(process.argv[2]){
  const audit=JSON.parse(fs.readFileSync(process.argv[2],'utf8')),sandbox={};
  for(const file of ['map-surface.js','map-views.js','battlefield.js'])vm.runInNewContext(fs.readFileSync('rewrite/Ew4.Web/wwwroot/'+file,'utf8'),sandbox);
  const cells=Array.from({length:audit.width*audit.height},()=>({active:false,sea:true,owner:null}));
  for(const c of audit.cells)cells[c.cell]={active:true,sea:false,owner:c.mountain||c.owner<0?null:c.owner===0?'south':'north',jurisdictionCenter:c.center<0?null:c.center,geographyType:c.mountain?'impassable_mountain':'none'};
  const factions=[{id:'north',camp:1,color:11},{id:'south',camp:2,color:22}],projection=new sandbox.Ew4MapViews.Projection();
  const state={scenario:'conquest7',width:audit.width,height:audit.height,cells,factions},map={territoryDisplay:{conquest7:audit.mountainLinks}};
  projection.sync(state,map);
  const regions=new Map(),lookup=new Map(audit.cells.map(c=>[c.cell,c]));
  for(const [cell,center,district]of audit.mountainLinks){
    const displayed=projection.display(cells[cell],cell);
    check(displayed.owner===cells[center].owner&&displayed.jurisdictionCenter===district,'actual mountain '+cell+' follows authored center and district');
    if(!regions.has(district))regions.set(district,new Set());regions.get(district).add(cell);
  }
  for(const group of regions.values()){
    const queue=[group.values().next().value],seen=new Set(queue);
    for(let head=0;head<queue.length;head++)for(let d=0;d<6;d++){
      const n=sandbox.Ew4Battlefield.neighbor(queue[head],d,audit.width,audit.height);
      if(group.has(n)&&!seen.has(n)){seen.add(n);queue.push(n);}
    }
    check(seen.size===group.size,'each actual mountain district is continuous');
  }
  check(audit.mountainLinks.every(([cell,center])=>lookup.get(cell).claimed===lookup.get(center).claimed),'each mountain tint follows a center on the same side of the target boundary');
  for(const reference of audit.northReferences||[]){
    const queue=[reference.cell],seen=new Set(queue);
    for(let head=0;head<queue.length&&!seen.has(reference.northCell);head++)for(const d of [4,5]){
      const next=sandbox.Ew4Battlefield.neighbor(queue[head],d,audit.width,audit.height);
      if(next===reference.northCell)seen.add(next);
      else if(lookup.get(next)?.mountain&&!seen.has(next)){seen.add(next);queue.push(next);}
    }
    check(seen.has(reference.northCell)&&!lookup.get(reference.northCell).mountain&&lookup.get(reference.northCell).center===reference.center,'actual tint source is reached only through northward mountain steps: '+reference.cell);
  }
  const unified={...state,cells:cells.map((c,i)=>({...c,owner:lookup.get(i)?.claimed&&c.geographyType!=='impassable_mountain'?'south':null}))};
  projection.sync(unified,map);
  let displayedTarget=0;
  for(const c of audit.cells){
    const displayed=projection.display(unified.cells[c.cell],c.cell);
    check((displayed.owner==='south')===c.claimed,'unified outline matches target at '+c.cell);
    if(displayed.owner)displayedTarget++;
  }
  check(displayedTarget===audit.claimedLand,'unified projection contains every target land cell and no foreign cell');
  for(const center of new Set(audit.mountainLinks.map(link=>link[1]))){
    const captured={...state,cells:cells.map((c,i)=>i===center?{...c,owner:cells[i].owner==='south'?'north':'south'}:c)};
    projection.sync(captured,map);
    check(audit.mountainLinks.every(([cell,parent])=>projection.display(cells[cell],cell).owner===captured.cells[parent].owner),'every linked mountain updates for actual center '+center);
  }
  check(audit.mountainLinks.every(([cell])=>cells[cell].owner===null),'actual barrier gameplay ownership remains neutral');
  console.log(JSON.stringify({checks,errors:0,target:displayedTarget,mountainDistricts:regions.size}));
}
if(process.argv[3])void(async()=>{
  const base=process.argv[3],audit=JSON.parse(fs.readFileSync(process.argv[2],'utf8')),sandbox={};
  vm.runInNewContext(fs.readFileSync('rewrite/Ew4.Web/wwwroot/map-views.js','utf8'),sandbox);
  const manifest=await (await fetch(base+'/content/manifest.json')).json(),projection=new sandbox.Ew4MapViews.Projection();
  const center=11707,linked=audit.mountainLinks.filter(l=>l[1]===center),cases=[];
  for(const [slot,owner]of [[1,null],[2,'conquest7.faction.1.gbr'],[3,'conquest7.faction.0.fra'],[4,null]]){
    const response=await (await fetch(base+'/api/load',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({slot,mode:2})})).json();
    check(response.ok,'actual client slot '+slot+' loads');
    const state=response.data;
    check(state.cells[center].owner===owner,'actual north district owner at stage '+slot);
    projection.sync(state,manifest.maps[state.map]);
    for(const [cell]of audit.mountainLinks)check(state.cells[cell].owner===null&&state.cells[cell].jurisdictionCenter===null&&state.cells[cell].geographyType==='impassable_mountain','actual centerless impassable cell remains unchanged: '+slot+'/'+cell);
    for(const [cell]of linked)for(const mode of ['normal','camp','district'])check(projection.display(state.cells[cell],cell).owner===owner&&projection.color(projection.display(state.cells[cell],cell),mode)===projection.color(state.cells[center],mode),'actual north capture/reload tint matches at '+slot+'/'+cell+'/'+mode);
    cases.push({slot,owner,linkedMountains:linked.length,color:projection.color(projection.display(state.cells[linked[0][0]],linked[0][0]),'normal')});
  }
  console.log(JSON.stringify({checks,errors:0,actualClientNorthColor:cases}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
