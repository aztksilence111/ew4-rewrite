'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),vm=require('node:vm');
const {createRequire}=require('node:module'),{execFileSync}=require('node:child_process');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:56232',out=path.resolve(process.argv[3]||'build/movement-smooth-20261111');
const source=path.resolve(process.argv[4]||'.'),web=path.join(source,'rewrite/Ew4.Web/wwwroot');
const report={checks:0,errors:[],missing:[],cadence:[],moves:[],scope:'Desktop Edge, touch emulation and native parameter comparison; no Android device FPS claim'};
function check(v,label){assert.ok(v,label);report.checks++;}
function cadence(app,hz,active){
  const begin=app.indexOf('  // Idle flags'),end=app.indexOf('  const playVisible',begin);
  const block=app.slice(begin,end),context={navigator:{maxTouchPoints:1},matchMedia:()=>({matches:true}),pointers:{size:0},cameraInputUntil:active?10000:0,aiRunning:false,
    motions:new Map(),arrivals:new Map(),deaths:new Map(),combatMotions:new Map(),effects:{instances:[]},damageNumbers:{instances:[]},requestAnimationFrame:()=>1,
    render(){},renderRequest:0,renderDirty:false,lastRenderTime:-Infinity,forceBattlefield:false};
  const tick=vm.runInNewContext('(function(time){'+block+'return true;})',context);let count=0;
  for(let i=0;i<hz*2;i++)if(tick(i*1000/hz))count++;
  return count/2;
}
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const app=await fs.readFile(path.join(web,'app.js'),'utf8');
  const old=execFileSync('git',['show','0a54b01:rewrite/Ew4.Web/wwwroot/app.js'],{cwd:source,encoding:'utf8'});
  for(const hz of [60,90,120,144])for(const active of [false,true]){
    const before=cadence(old,hz,active),after=cadence(app,hz,active);report.cadence.push({hz,active,before,after});
    check(Math.abs(after-(active?60:30))<=.5,'stable budget at '+hz+' Hz / '+active);
  }
  const context={};vm.runInNewContext(await fs.readFile(path.join(web,'effects.js'),'utf8'),context);
  const {ParticleEmitter,Effects}=context.Ew4Effects,data=JSON.parse(await fs.readFile(path.join(source,'rewrite/data/client/effects.json'),'utf8'));
  const definition=data.definitions['effect_moving1.xml'][0];
  const emitter=new ParticleEmitter(definition,{at:0},7,{continuous:true});emitter.move(100,0,1/60);emitter.advance(1/60);
  check(emitter.emitted===5,'native 300/s rate emits five births in first step');
  check(new Set(emitter.particles.map(p=>p.born)).size===5,'births have distinct substep times');
  const deterministic=hz=>{
    const e=new ParticleEmitter(definition,{at:0},7,{continuous:true});
    for(let i=1;i<=hz;i++){e.move(i/hz*200,0,i/hz);e.advance(i/hz);}
    return e;
  };
  const reference=deterministic(60);
  for(const hz of [30,90,120,144]){
    const e=deterministic(hz);check(e.emitted===reference.emitted&&e.seed===reference.seed,'birth count/seed independent of '+hz+' Hz');
    check(e.particles.length===reference.particles.length&&e.particles.every((p,i)=>Math.abs(p.x-reference.particles[i].x)<1e-6),'trail stays on same straight path at '+hz+' Hz');
  }
  const e=new ParticleEmitter(definition,{at:0},7);e.until=0;e.particles=reference.particles.slice(0,4).map(p=>({...p,age:0,life:1}));
  const ids=e.particles.map(p=>p.born);e.particles[1].life=.001;e.advance(1/60);
  check(e.particles.map(p=>p.born).join(',')===[ids[0],ids[2],ids[3]].join(','),'expiry retains alpha blend order');
  const engine=new Effects({manifest:{atlases:{eff:{'cloud5.png':{w:64,h:64}}}}},data);
  engine.sound=()=>{};engine.texture=()=>({});
  engine.trail('unit',0,0,0,false,0);let positions=[];
  const draw={save(){},restore(){},rotate(){},translate(x,y){positions.push([x,y]);},drawImage(){}};
  engine.frame(draw,100,'ground');const count=engine.trails.get('unit').instance.emitters[0].emitted;
  positions=[];engine.frame(draw,102,'ground');const a=positions;
  positions=[];engine.frame(draw,108,'ground');const b=positions;
  check(a.length===b.length&&a.some((p,i)=>Math.hypot(p[0]-b[i][0],p[1]-b[i][1])>.001),'smoke moves between simulation ticks');
  check(engine.trails.get('unit').instance.emitters[0].emitted===count,'draw prediction does not emit or mutate the simulation clock');
  engine.finishTrails(new Set(),110);engine.frame(draw,2000,'ground');check(engine.instances.length===0&&engine.trails.size===0,'stopped trail fully expires');
  vm.runInNewContext(await fs.readFile(path.join(web,'map-surface.js'),'utf8'),context);
  vm.runInNewContext(await fs.readFile(path.join(web,'battlefield.js'),'utf8'),context);
  const motion={path:[0,1,2],started:0,stepMs:255,pending:true},motions=new Map([['test',motion]]);
  check(context.Ew4Battlefield.pose(motion,3,150).x===0,'queued first frame retains the start cell');
  context.Ew4Battlefield.advanceMotions(motions,150);
  check(motion.started===150&&!motion.pending,'movement clock anchors to first delivered frame');
  context.Ew4Battlefield.advanceMotions(motions,166);
  check(Math.abs(context.Ew4Battlefield.pose(motion,3,166).x-64*16/255)<1e-6,'normal frame retains continuous native-style motion');
  context.Ew4Battlefield.advanceMotions(motions,366);
  check(Math.abs(context.Ew4Battlefield.pose(motion,3,366).x-64*66/255)<1e-6,'long frame advances at most native 50 ms');
  const renderer=new context.Ew4Battlefield.Renderer({effects:{motion:()=>({name:'walk',loop:true,speed:1})}});
  renderer.motions=new Map([['walker',{started:4000}]]);renderer.arrivalSpeed=2;
  const profile={motions:{move:'walk',ready:'stand'}},lib={fps:24,byName:new Map([['walk',{duration:24}]])},unit={id:'walker'};
  check(renderer.motionPose(unit,profile,lib,1,4000,true).tick===0,'gait starts at movement origin');
  check(renderer.motionPose(unit,profile,lib,1,4250,true).tick===12,'gait shares movement speed');
  renderer.api.effects.motion=()=>({name:'walk',loop:true,static:true});
  check(renderer.motionPose(unit,profile,lib,1,4250,true).tick===0,'explicit static Mod motion stays static');

  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const config of [{name:'source-pixi',width:1280,height:720,touch:false,source:true},{name:'compat-pixi',width:844,height:390,touch:true},{name:'compat-canvas',width:390,height:844,touch:true,canvas:true}]){
      const page=await browser.newPage({viewport:{width:config.width,height:config.height},hasTouch:config.touch,deviceScaleFactor:2});
      if(config.source)await page.route(/\/compat\/[^/]+\.js(?:\?|$)/,async route=>route.fulfill({body:await fs.readFile(path.join(web,new URL(route.request().url()).pathname.split('/').pop()),'utf8'),contentType:'text/javascript'}));
      page.on('pageerror',error=>report.errors.push(error.message));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.status()+' '+r.url());});
      await page.goto(base+(config.canvas?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady);
      for(const naval of [false,true]){
        await page.evaluate(naval=>start(naval?'campaign1_02b':'conquest1',naval?null:'fra'),naval);
        const move=await page.evaluate(async naval=>{
          settings.speed=1;const candidates=state.units.filter(u=>u.owner===state.playerFaction&&u.canNavigateMove&&(!naval||u.category==='naval'));
          for(const u of candidates){
            const s=await rpc('action',{op:'select',cell:u.cell,revision:state.revision});
            const dest=s.range.filter(c=>c!==u.cell).sort((a,b)=>Math.hypot(point(b).x-point(u.cell).x,point(b).y-point(u.cell).y)-Math.hypot(point(a).x-point(u.cell).x,point(a).y-point(u.cell).y))[0];
            if(dest==null)continue;
            await selectCell(u.cell,true);const p=point(u.cell);view.x=p.x;view.y=p.y;view.zoom=1.5;
            window.motionSample={frames:[],original:battlefield.frame,trailPeak:0};
            battlefield.frame=function(scene,time){const t=performance.now();const result=motionSample.original.call(this,scene,time);
              const motion=motions.get(u.id);if(motion)motionSample.frames.push({time,started:motion.started,position:Ew4Battlefield.pose(motion,state.width,time),cost:performance.now()-t});
              motionSample.trailPeak=Math.max(motionSample.trailPeak,effects.particles.length);return result;};
            try{
              const next=await rpc('action',{op:'move',unit:u.id,cell:dest,revision:state.revision});
              const movement=next.movements.find(m=>m.unit===u.id);window.movementReview=installState(next);
              return {unit:u.id,from:u.cell,dest,path:movement.path};
            }catch(error){battlefield.frame=motionSample.original;throw error;}
          }
          throw Error('No movable '+(naval?'naval':'land')+' unit');
        },naval);
        await page.waitForFunction(()=>motions.size>0&&motionSample.frames.length>2);
        if(process.env.EW4_MOTION_SCREENSHOTS==='1')await page.screenshot({path:path.join(out,config.name+(naval?'-naval':'-land')+'.png')});
        await page.evaluate(()=>movementReview);
        const sample=await page.evaluate(async()=>{
          battlefield.frame=motionSample.original;const frames=motionSample.frames,dt=frames.slice(1).map((p,i)=>p.time-frames[i].time),cost=frames.map(p=>p.cost).sort((a,b)=>a-b);
          const result={frames:frames.length,trailPeak:motionSample.trailPeak,firstFrameDelayMs:frames[0].time-frames[0].started,medianDrawMs:cost[Math.floor(cost.length/2)],maxDrawMs:cost.at(-1),maxGapMs:Math.max(...dt),moving:motions.size};
          effects.finishTrails(new Set(),performance.now());effects.frame(ctx,performance.now()+2500,'ground');
          return {...result,remainingTrails:effects.trails.size,remainingParticles:effects.particles.length};
        });
        check(sample.frames>=3&&sample.trailPeak>0,'real '+config.name+' '+(naval?'naval':'land')+' motion and particles');
        check(Math.abs(sample.firstFrameDelayMs)<.01,'first delivered frame begins at path origin');
        check(await page.evaluate(m=>state.units.find(u=>u.id===m.unit).cell===m.dest,move),'real move reaches committed endpoint');
        check(sample.moving===0&&sample.remainingTrails===0&&sample.remainingParticles===0,'movement/trail lifecycle completes');
        report.moves.push({config:config.name,naval,...move,...sample});
      }
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing resources');
  }finally{await browser.close();}
  await fs.writeFile(path.join(out,'verification.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify(report));
})().catch(async error=>{report.failure=error.message;await fs.mkdir(out,{recursive:true});await fs.writeFile(path.join(out,'verification.json'),JSON.stringify(report,null,2));console.error(error.message);process.exitCode=1;});
