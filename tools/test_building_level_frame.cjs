'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),{test}=require('node:test');
for(const folder of ['','compat']){
  const context=vm.createContext({});
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../rewrite/Ew4.Web/wwwroot',folder,'battlefield.js'),'utf8'),context);
  test(`${folder||'source'}: frame follows upgrade ceiling including initial-only tiers`,()=>{
    const calls=[],manifest={buildings:{city:Array(7),industry:Array(4),stable:Array(3),port:Array(4),farmland:Array(3)},buildingUpgradeLimits:{city:6,industry:4,stable:3,port:4,farmland:3}};
    const renderer=Object.create(context.Ew4Battlefield.Renderer.prototype);
    renderer.api={manifest,sprite:(atlas,name)=>calls.push(name)};
    renderer.draw={save(){},restore(){}};
    for(const [kind,maximum] of Object.entries(manifest.buildingUpgradeLimits))for(let level=1;level<=7;level++){
      calls.length=0;renderer.visibleCells=[{site:{active:true,buildingKind:kind,buildingLevel:level},p:{x:100,y:100}}];
      renderer.facilities(kind==='farmland');
      assert.equal(calls.includes('buildinglevel_max.png'),kind!=='farmland'&&level>=maximum,kind+' '+level);
    }
    for(const maximum of [5,8])for(const level of [5,6,7,8]){
      calls.length=0;manifest.buildingUpgradeLimits.city=maximum;
      renderer.visibleCells=[{site:{active:true,buildingKind:'city',buildingLevel:level},p:{x:100,y:100}}];renderer.facilities(false);
      assert.equal(calls.includes('buildinglevel_max.png'),level>=maximum,'Mod '+maximum+' / '+level);
    }
    delete manifest.buildingUpgradeLimits;calls.length=0;renderer.facilities(false);
    assert.ok(calls.includes('buildinglevel_max.png'),'standalone old manifest falls back to row count');
  });
}
