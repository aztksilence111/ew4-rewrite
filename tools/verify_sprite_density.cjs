'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
require('../rewrite/Ew4.Web/wwwroot/battlefield.js');
const source=fs.readFileSync(require.resolve('../rewrite/Ew4.Web/wwwroot/app.js'),'utf8');
const code=source.slice(source.indexOf('function sprite(atlas,'),source.indexOf('\nconst unresolved',source.indexOf('function sprite(atlas,')));
const texture={complete:true,naturalWidth:4096},calls=[];
const context={Ew4Battlefield,manifest:{atlases:{}},image:()=>texture,ctx:{drawImage:(...args)=>calls.push(args)}};
vm.createContext(context);vm.runInContext(code,context);
let checks=0;
const check=(value,label)=>{assert.ok(value,label);checks++;};
for(const hd of [false,true])for(const ratio of [undefined,8,16,36])for(const scale of [.5,1,3]){
  const density=ratio??(hd?2:1),name='test.png',entry={x:5,y:7,w:26*density,h:16*density,refx:3*density,refy:4*density};
  if(ratio!==undefined)entry.pixelRatio=ratio;
  context.manifest.atlases={buildings:hd?{}:{[name]:entry},buildings_hd:hd?{[name]:entry}:{}};
  calls.length=0;context.sprite('buildings',name,100,200,scale);
  const args=calls[0];
  check(args[3]===26*density&&args[4]===16*density,'source pixels preserved');
  check(args[5]===100-3*scale&&args[6]===200-4*scale,'anchor independent of density');
  check(args[7]===26*scale&&args[8]===16*scale,'display dimensions independent of density');
}
for(const value of [0,-1,NaN,Infinity,null,'invalid',undefined])check(Ew4Battlefield.spritePixelRatio({pixelRatio:value},2)===2,'invalid density falls back');
check(Ew4Battlefield.spritePixelRatio({pixelRatio:'16'},2)===16,'numeric JSON value accepted');
context.manifest.atlases={};calls.length=0;context.sprite('buildings','missing',0,0);check(calls.length===0,'missing sprite remains skipped');
const reductions=[];
global.document={createElement:()=>({width:0,height:0,getContext:()=>({drawImage:(...args)=>reductions.push(args)})})};
const hdEntry={x:420,y:260,w:416,h:256},hdTexture={};
for(const density of [.5,1,3,8,16,32]){
  const sampled=Ew4Battlefield.spriteSample(hdTexture,hdEntry,16,{getTransform:()=>({a:density,b:0,c:0,d:density})});
  const level=Math.min(6,Math.max(0,Math.floor(Math.log2(16/density))));
  check(sampled[3]===416/2**level&&sampled[4]===256/2**level,'mip follows actual screen density');
  if(level)check(sampled[1]===0&&sampled[2]===0,'mip is isolated from the atlas');
  else check(sampled[0]===hdTexture&&sampled[1]===420,'magnification keeps original detail');
}
check(reductions.length===5,'mips generated once then reused across zooms');
check(reductions[0][1]===420&&reductions[0][2]===260&&reductions[0][3]===416&&reductions[0][4]===256,'first reduction crops only the sprite');
check(Ew4Battlefield.spriteSample(hdTexture,hdEntry,2,{getTransform:()=>({a:1,b:0,c:0,d:1})})[0]===hdTexture,'native density remains unchanged');
delete global.document;
console.log(JSON.stringify({checks,errors:0}));
