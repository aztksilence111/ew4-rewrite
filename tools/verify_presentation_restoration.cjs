'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const root=path.resolve(__dirname,'..'),folder=path.join(root,'build/presentation-restoration-20261022');
fs.mkdirSync(folder,{recursive:true});
const report={checks:0,errors:[],missing:[],samples:{}};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
const read=p=>fs.readFileSync(path.join(root,p),'utf8'),json=p=>JSON.parse(read(p));
const data=json('rewrite/data/client/effects.json'),manifest=json('rewrite/data/client/manifest.json');
let now=0;const played=[];
class AudioStub {
  constructor(src){this.src=src;this.currentTime=0;}
  play(){played.push(this.src);return Promise.resolve();}
  pause(){this.paused=true;}
  cloneNode(){return new AudioStub(this.src);}
}
const scope={performance:{now:()=>now},Audio:AudioStub};
vm.runInNewContext(read('rewrite/Ew4.Web/wwwroot/effects.js'),scope);
vm.runInNewContext(read('rewrite/Ew4.Web/wwwroot/battlefield.js'),scope);
const {Effects,ParticleEmitter}=scope.Ew4Effects;
function definition(rate=60,life=1){return {life,parameters:{settings:{mode:'once',type:'point'},image:{file:'ray0000.png',width:30,height:8},
  life:{min:2,max:2},speed:{min:10,max:10},angle:{min:0,max:0},timetrack:[{time:0,quantity:rate}],lifetrack:[{life:0,speed:1,a:1},{life:1,speed:0,a:1}]}};}
const draws=[],draw={save(){},restore(){},translate(){},scale(){},rotate(){},drawImage(...args){draws.push(args);}};
const api={manifest,libraries:new Map(),volume:()=>50,image:()=>({complete:true,naturalWidth:1})};
const fx=new Effects(api,data);
fx.texture=()=>({});
const emitter=new ParticleEmitter(definition(60,.1),{},1);
emitter.advance(0);check(emitter.emitted===0,'rate does not burst at creation');
emitter.advance(.1);check(emitter.emitted===6,'60 per second emits six in 100ms');
emitter.advance(.2);check(emitter.emitted===6&&emitter.particles.length>0,'once stops births and keeps tails');
const capped=new ParticleEmitter(definition(60000,10),{},1);capped.advance(1);
check(capped.peak===100&&capped.particles.length===100,'100 alive cap enforced per emitter');
const a=new ParticleEmitter(definition(),{},123),b=new ParticleEmitter(definition(),{},123);
a.advance(.5);for(let i=1;i<=30;i++)b.advance(i/60);
check(JSON.stringify(a.particles)===JSON.stringify(b.particles),'same simulation for large frame and 30 small frames');
const particle=a.particles[0],x=particle.x;a.stop(.5);a.advance(1.5);
check(a.particles.includes(particle)&&particle.x>x,'slowing particle retains accumulated displacement');
a.advance(3);check(!a.alive&&!a.particles.length,'stopped emitter releases its tail');
fx.effect('effect_gunfire.xml',0,0,{started:0});fx.frame(draw,0);
check(fx.particles.length===0,'real gunfire no longer creates 220 particles instantly');
fx.frame(draw,100);const gunfire=fx.instances[0];
report.samples.gunfire={bornAt100ms:gunfire.emitters.reduce((n,e)=>n+e.emitted,0),aliveAt100ms:fx.particles.length};
check(report.samples.gunfire.bornAt100ms>0&&report.samples.gunfire.bornAt100ms<25,'real gunfire follows rate curve');
const count=gunfire.emitters.reduce((n,e)=>n+e.emitted,0);fx.frame(draw,100);
check(gunfire.emitters.reduce((n,e)=>n+e.emitted,0)===count,'same frame time cannot emit twice');
const pivot=new Effects(api,{...data,definitions:{one:[definition(60,.02)]}});pivot.texture=()=>({});
pivot.effect('one',0,0,{started:0});draws.length=0;pivot.frame(draw,20);
check(draws.some(d=>d[1]===-3&&Math.abs(d[2]+18/38*8)<1e-9),'particle drawing respects original ray reference point');
const sounds=new Effects(api,{...data,timelines:{test:[{type:'effect',sound:'sfx_gunfire.wav',at:.1},{type:'effect',sound:'sfx_exp.wav',at:.2}]}});
sounds.timeline('test',0,0,{started:0});sounds.frame(draw,50);const before=played.length;
sounds.frame(draw,250);sounds.frame(draw,250);
check(played.length===before+2,'sound-only events cross thresholds once');
sounds.clear();sounds.sound('sfx_gunfire.wav');check(played.at(-1).endsWith('sfx_gunfire.wav'),'pooled audio remains usable after clear');
fx.clear();fx.trail('cavalry',0,0,0,false,0);fx.frame(draw,100,'ground');
check(fx.trails.get('cavalry').kind===1,'grade zero cavalry uses moving1');
const trail=fx.trails.get('cavalry').instance;
fx.trail('cavalry',20,0,0,false,100);fx.frame(draw,100,'ground');
check(fx.trails.get('cavalry').instance===trail,'movement retains emitter identity');
fx.finishTrails(new Set(),100);fx.frame(draw,120,'ground');
check(!fx.trails.size&&fx.particles.length>0,'movement stops births without dropping live tail');
fx.frame(draw,2000,'ground');check(!fx.instances.length,'movement tail expires');
const impacts=json('reverse/reports/frontend_service_contracts.json').impacts;
for(const v of impacts)assert.equal(scope.Ew4Effects.impact(v.weapon,v.category,v.damage,v.sea,v.facing),v.name);
report.nativeImpactCases=impacts.length;
const libraries=new Map();let completePlans=0,restoredPlans=0;
for(const [id,base] of Object.entries(manifest.profiles)){
  const profile=fx.profile(id);
  if(!libraries.has(profile.library)){
    const lib=json('rewrite/data/client/animations/'+profile.library+'.json');
    lib.byName=new Map(lib.items.map(i=>[i.name,i]));libraries.set(profile.library,lib);
  }
  const lib=libraries.get(profile.library);
  for(const facing of [-1,1])for(const speed of [1,Math.sqrt(5)]){
    const category=base.stats.type,plan=fx.attackPlan(profile,lib,category,facing,speed),old=[];
    let duration=0,full=0;
    const last=fx.lastEvent(plan.timeline);
    for(const action of ['attack','reload','finish']){
      const motion=fx.motion(profile,action,facing),item=lib.byName.get(motion?.name||profile.motions[action]);if(!item)continue;
      const length=item.duration/lib.fps*1000/(speed*Number(motion?.speed||1));
      const clipped=action==='attack'&&last!=null?Math.min(length,(last+.2+(category==='artillery'?.2:0))*1000/speed):length;
      old.push({end:duration+clipped});duration+=clipped;full+=length;
    }
    if(!old.length)continue;
    assert.ok(Math.abs(plan.visualDuration-full)<1e-8,'complete body frames '+id);
    assert.ok(Math.abs(plan.duration-duration)<1e-8,'unchanged blocking basis '+id);
    assert.equal(plan.attackBasis,old[0].end);
    assert.equal(plan.hitAt,last==null?old[0].end:Math.max(old[0].end,(last+.2)*1000/speed));
    if(full>duration+1e-8)restoredPlans++;completePlans++;
  }
}
report.samples.attackPlans={checked:completePlans,restored:restoredPlans};report.checks+=completePlans*4;
const Renderer=scope.Ew4Battlefield.Renderer,renderer=Object.create(Renderer.prototype);
renderer.resetMotions();renderer.api={effects:fx};renderer.unitIds=new Set(['own','enemy']);renderer.combatMotions=new Map();
renderer.state={playerFaction:'p',activeFaction:'p'};renderer.time=0;renderer.selectedAt=0;renderer.selection={};
const profile=fx.profile(Object.keys(manifest.profiles)[0]),lib=libraries.get(profile.library),unit={id:'own',owner:'p'};
renderer.selected=unit;renderer.syncMotions();
check(renderer.unitMotions.get('own').action==='ready'&&renderer.unitMotions.get('own').playing,'selection enters ready once');
renderer.motionPose(unit,profile,lib,1,100000);
check(renderer.unitMotions.get('own').action==='attack'&&!renderer.unitMotions.get('own').playing,'ready ends at static attack frame');
renderer.selection=null;renderer.time=100001;renderer.syncMotions();
check(renderer.unitMotions.get('own').action==='undoready','cancel enters undoready');
renderer.motionPose(unit,profile,lib,1,200000);
check(renderer.unitMotions.get('own').action==='ready'&&!renderer.unitMotions.get('own').playing,'cancel returns to static ready frame');
renderer.selection={};renderer.selected={id:'enemy',owner:'e'};renderer.syncMotions();
check(!renderer.unitMotions.has('enemy'),'enemy selection does not animate ready');
fx.clear();
for(const [grade,sea,kind] of [[0,false,1],[1,false,2],[2,false,3],[0,true,4]]){
  fx.trail('t',0,0,grade,sea,0);check(fx.trails.get('t').kind===kind,'trail grade/sea branch '+kind);fx.clear();
}
fx.trail('t',0,0,1,false,0);fx.frame(draw,100,'ground');const moving=fx.trails.get('t').instance,oldParticle=moving.emitters[0].particles[0],oldX=oldParticle.x;
fx.trail('t',1000,0,1,false,100);fx.frame(draw,100,'ground');
check(oldParticle.x===oldX,'moving emitter does not move previously born particles');
fx.trail('t',1200,0,1,false,10000,false);fx.frame(draw,10000,'ground');
check(!fx.particles.length,'invisible long gap discards expired particles');
fx.trail('t',1300,0,1,false,10000,true);fx.frame(draw,10000,'ground');
check(!fx.particles.length,'return to screen cannot replay offscreen births');
fx.clear();const soundStart=played.length;
for(const [p,c,s] of [[{},'infantry',false],[{},'cavalry',false],[{unitIndex:9},'cavalry',false],[{},'infantry',true]])fx.moveSound(p,c,s);
check(played.slice(soundStart).map(s=>s.split('/').at(-1)).join(',')==='sfx_leg.wav,sfx_cavalrymove.wav,sfx_armourmove.wav,sfx_naval.wav','movement sound branches');
check(scope.Ew4Battlefield.recruitOffset(0)===-60&&scope.Ew4Battlefield.recruitOffset(scope.Ew4Battlefield.recruitDuration()/2)===-30,'recruit starts at -60 and moves linearly');
check(data.elements['damaged 3'].map(e=>[Number(e.x||0),Number(e.y||0)].join(',')).join(';')==='-12,-11;0,0;10,-5','native damaged fort offsets retained');
fs.writeFileSync(path.join(folder,'pure.json'),JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify({checks:report.checks,nativeImpactCases:impacts.length,samples:report.samples,errors:0}));
