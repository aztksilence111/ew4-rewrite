'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm'),path=require('node:path'),{createRequire}=require('node:module');
const frontendRequire=createRequire(path.join(__dirname,'frontend/package.json'));
const source=fs.readFileSync(path.join(__dirname,'../rewrite/Ew4.Web/wwwroot/commander-workflows.js'),'utf8');
const compatible=frontendRequire('esbuild').transformSync(source,{target:['chrome70','es2018']}).code;
frontendRequire('acorn').parse(compatible,{ecmaVersion:2018});
const context={Ew4Menus:{Menus:function(){}}};
vm.runInNewContext(compatible,context);
const menus=new context.Ew4Menus.Menus();
let checks=0;
const check=(pass,label)=>{assert(pass,label);checks++;};
const items=[
  {id:'ew4.item.0',displayId:'ew4.item.0',image:'War Horse.png',quantity:6,available:0},
  {id:'ew4.item.10001',displayId:'ew4.item.0',image:'War Horse.png',quantity:4,available:2},
  {id:'ew4.item.10002',displayId:'ew4.item.0',image:'War Horse.png',quantity:3,available:3},
  {id:'ew4.item.8',displayId:'ew4.item.8',image:'Army Tentage.png',quantity:1,available:1},
  {id:'ew4.item.10801',displayId:'ew4.item.10801',image:'Army Tentage.png',quantity:2,available:2}
];
const before=JSON.stringify(items),grouped=menus.inventoryItems(items);
check(grouped.length===3,'identical legacy aliases occupy one inventory entry; Mod variants remain separate');
check(grouped[0].quantity===13&&grouped[0].available===5,'counts include all copies and reserve equipped copies');
check(grouped[0].id==='ew4.item.10001','sale/equip commands target an actual spare legacy ID');
check(grouped[0].image==='War Horse.png','grouped copy inherits the original resource name');
check(menus.inventoryItems(items,['ew4.item.10001'])[0].id==='ew4.item.10002','equipment picker chooses a spare ID not already equipped');
check(JSON.stringify(items)===before,'display grouping preserves source IDs and per-ID quantities');
const locked=menus.inventoryItems(items.map(i=>({...i,available:0})));
check(locked[0].quantity===13&&locked[0].available===0,'fully equipped groups remain visible and unavailable for sale');
check(menus.inventoryItems([{id:'old.api',quantity:2,available:2}])[0].id==='old.api','responses without display aliases remain usable');
const offers=[{id:'sold',itemId:'ew4.item.0',available:false,slot:0},
  {id:'legacy',itemId:'ew4.item.10001',available:true,slot:1},
  {id:'duplicate',itemId:'ew4.item.10002',available:true,slot:2},
  {id:'mod',itemId:'ew4.item.10801',available:true,slot:3}];
const displayed=menus.shopOffers({items,offers});
check(displayed.length===2&&displayed[0].id==='legacy','old battle shops hide redundant offers and retain an available legacy copy');
check(displayed[0].slot===1&&displayed[1].id==='mod','source slots, IDs and Mod variants remain intact');
check(menus.shopOffers({items:items.map(i=>({...i,consumable:true})),offers}).length===4,'consumable stock slots remain independent');
console.log(JSON.stringify({itemQuantityFrontendChecks:checks,errors:0}));
