'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5492',folder=path.resolve(process.argv[3]||'build/mobile-feedback-20261023/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:process.env.EW4_BROWSER||'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const report={checks:0,errors:[],missing:[],menus:[],battle:[],scope:'Browser viewport, real assets and renderer instrumentation; no physical Android execution'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  let stage='startup';
  try{
    for(const [name,width,height] of [['phone',844,390],['user-image',1188,540],['desktop',1280,720],['tablet',1024,768],['portrait',390,844]]){
      const page=await browser.newPage({viewport:{width,height},hasTouch:true,deviceScaleFactor:2});
      page.on('pageerror',e=>report.errors.push({stage,message:e.message}));
      page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      stage=name+' startup';await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:60000});
      const bounds=async selector=>page.locator(selector).evaluate(el=>{
        const r=el.getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};
      });
      if(!process.argv.includes('--battle-only')){
      for(const screen of [80,100,150]){
        await page.evaluate(screen=>{settings.screen=screen;sizeUi();},screen);
        const r=await bounds('#menu-stage');
        check(Math.abs(r.x)<.1&&Math.abs(r.y)<.1&&Math.abs(r.width-width)<.1&&Math.abs(r.height-height)<.1,name+' full menu '+screen);
        const nav=await bounds('#main-menu nav');
        check(nav.x>=0&&nav.y>=0&&nav.right<=width+.1&&nav.bottom<=height+.1,name+' main buttons fit '+screen);
      }
      await page.evaluate(()=>{settings.screen=100;sizeUi();});
      await page.screenshot({path:path.join(folder,name+'-main.png')});
      stage=name+' conquest';await page.locator('[data-mode="2"]').tap();
      await page.waitForFunction(()=>!conquestEntering&&$('scenarios').children.length>0);
      await page.screenshot({path:path.join(folder,name+'-conquest.png')});
      await page.evaluate(()=>chooseScenario(catalog.scenarios.find(s=>s.id==='conquest1')));
      await page.waitForFunction(()=>$('country-list').getAnimations({subtree:true}).every(a=>a.playState==='finished'));
      const sidebar=await bounds('#country-list'),confirm=await bounds('#confirm-country');
      check(sidebar.x>=0&&sidebar.right<=width+.1&&sidebar.bottom<=height+.1,name+' countries fit');
      check(confirm.x>=0&&confirm.right<=width+.1&&confirm.bottom<=height+.1,name+' country confirm fits');
      await page.screenshot({path:path.join(folder,name+'-countries.png')});
      stage=name+' headquarters';await page.evaluate(async()=>{await showMain();await menus.headquarters();});
      check((await bounds('#hq-roster')).right<=width+.1,name+' roster fits');
      await page.screenshot({path:path.join(folder,name+'-headquarters.png')});
      report.menus.push({name,width,height,sidebar,confirm});
      }
      stage=name+' battle';await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>ctx?.stats?.frames>0&&[...images.values()].every(i=>i.complete));
      const modes=name==='phone'?['gpu','canvas']:['gpu'];
      for(const backend of modes){
        if(backend==='canvas')await page.evaluate(()=>{
          suspendRendering();ctx.renderer.destroy(false);const next=canvas.cloneNode();canvas.replaceWith(next);canvas=next;
          history.replaceState(null,'','?renderer=canvas');ctx=Ew4Pixi.createContext(canvas);bindMapInput();battlefield.invalidateGround();
          battlefield.unitFrames.clear();battlefield.unitFramePixels=0;
        });
        for(const relativeZoom of [1,.48,.28,.57,.66]){
          stage=name+' '+backend+' '+relativeZoom;
          const result=await page.evaluate(relativeZoom=>{
            suspendRendering();const reference=Math.max(1,Math.min(innerWidth/568,innerHeight/320,3));
            const originalState=state;
            state={...state,units:state.units.map((u,i)=>({...u,morale:[-3,-2,-1,1][i%4],transport:true,veterancy:'2'}))};
            view.zoom=reference*relativeZoom;const p=point(state.units.find(u=>u.commanderImage&&u.owner===state.playerFaction)?.cell||state.units[0].cell);view.x=p.x;view.y=p.y;clampView();
            const calls={body:0,pose:0,flag:0,trail:0,damage:0,profile:0,morale:0,transport:0,rank:0,health:0,badges:[],labels:[],portraits:[]};
            const wrap=(host,key,fn)=>{const old=host[key];host[key]=function(...args){fn.apply(this,args);return old.apply(this,args);};return ()=>{host[key]=old;};};
            const undo=[wrap(battlefield,'unitAnimation',()=>calls.body++),wrap(battlefield,'motionPose',()=>calls.pose++),wrap(battlefield,'flag',()=>calls.flag++),
              wrap(effects,'trail',()=>calls.trail++),wrap(effects,'element',()=>calls.damage++),wrap(effects,'profile',()=>calls.profile++),
              wrap(battlefield,'healthArc',()=>calls.health++),
              wrap(battlefield,'unitBadge',(unit,p,profile,death,scale=1)=>{
                const m=ctx.getTransform();calls.badges.push({x:m.a*p.x+m.e,y:m.d*p.y+m.f,size:Math.abs(m.a)*scale/viewport.dpr});
              }),
              wrap(battlefield.api,'sprite',(atlas,name,x,y,scale,target)=>{
                if(target&&target!==ctx)return;
                if(name.startsWith('morale_'))calls.morale++;
                if(name==='ship_mark.png')calls.transport++;
                if(name.startsWith('rank_'))calls.rank++;
                if(!/^board_smallgenerals/.test(name))return;
                const m=ctx.getTransform(),entry={x:m.a*x+m.e,y:m.d*y+m.f,size:Math.abs(m.a)/viewport.dpr};
                calls.portraits.push(entry);
              }),wrap(ctx,'drawImage',(source,...args)=>{
                if(![...battlefield.cityLabels.values()].includes(source))return;
                const m=ctx.getTransform();calls.labels.push({width:args[2]*m.a/viewport.dpr,height:args[3]*m.d/viewport.dpr});
              })];
            try{render(performance.now(),true);suspendRendering();}finally{undo.reverse().forEach(f=>f());state=originalState;}
            const pixels=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
            const colors=new Set();for(let i=0;i<pixels.length;i+=4*101)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);
            return {relativeZoom,zoom:view.zoom,reference,badgeOnly:battlefield.badgeOnly,calls,colors:colors.size,
              trails:effects.trails.size,bodyFrames:battlefield.unitFrames.size,cityLabels:battlefield.cityLabels.size};
          },relativeZoom);
          check(result.colors>40,stage+' rendered pixels');
          if(relativeZoom<.52||relativeZoom===.57){
            check(result.badgeOnly,stage+' badge mode');
            for(const key of ['body','pose','flag','trail','damage','profile','morale','transport','rank'])check(result.calls[key]===0,stage+' skips '+key);
            check(result.calls.badges.length>0&&result.calls.badges.every(b=>Math.abs(b.size-result.reference)<.01),stage+' fixed badge size');
            check(result.calls.health>0&&result.calls.portraits.length>0,stage+' health and commander portraits remain');
            check(result.calls.labels.length>0&&result.calls.labels.every(l=>l.height>=10),stage+' readable city names');
            check(result.trails===0,stage+' no hidden trail emitters');
          }else {
            check(!result.badgeOnly&&result.calls.body>0&&result.calls.flag>0,stage+' near models restored');
            check(result.calls.morale>0&&result.calls.transport>0&&result.calls.rank>0,stage+' near status markers restored');
          }
          report.battle.push({name,backend,...result});
          if(relativeZoom===.48||relativeZoom===1)await page.screenshot({path:path.join(folder,name+'-'+backend+'-'+relativeZoom+'.png')});
        }
        if(name==='phone'&&backend==='gpu'){
          stage='movement and idle rendering';
          const behavior=await page.evaluate(async()=>{
            suspendRendering();const reference=Math.max(1,Math.min(innerWidth/568,innerHeight/320,3));
            view.zoom=reference*.48;
            const unit=state.units.find(u=>u.owner===state.playerFaction&&u.canNavigateMove);
            const selected=await rpc('action',{op:'select',cell:unit.cell,revision:state.revision});
            const destination=selected.range.find(c=>c!==unit.cell&&!state.units.some(u=>u.cell===c));
            const next=await rpc('action',{op:'move',unit:unit.id,cell:destination,revision:state.revision});
            const route=next.movements.find(m=>m.unit===unit.id),origin=point(route.path[0]);view.x=origin.x;view.y=origin.y;clampView();
            state=next;selection=undefined;effects.clear();combatMotions.clear();arrivals.clear();
            const started=performance.now(),stepMs=1000;motions.set(unit.id,{path:route.path,started,stepMs});
            const samples=[],old=battlefield.unitBadge;
            battlefield.unitBadge=function(u,p,...args){if(u.id===unit.id){const m=ctx.getTransform();samples.push({x:(m.a*p.x+m.e)/viewport.dpr,y:(m.d*p.y+m.f)/viewport.dpr});}return old.call(this,u,p,...args);};
            try{
              for(const elapsed of [0,500,1000]){render(started+elapsed,true);suspendRendering();}
            }finally{battlefield.unitBadge=old;motions.clear();}
            const from=point(route.path[0]),to=point(route.path[1]);
            const expected={dx:(to.x-from.x)*view.zoom,dy:(to.y-from.y)*view.zoom};
            const realState=state;
            // Keep only hidden animation sources; no visible pulse, objectives or active actions.
            state={...state,units:state.units.map(u=>({...u,owner:'hidden-fixture',canMove:false,canAttack:false,burning:3})),
              cells:state.cells.map(c=>({...c,objective:false,objectiveMarker:0}))};
            battlefield.unitMotions=new Map(state.units.map(u=>[u.id,{playing:true,started,action:'ready'}]));
            let bodyCalls=0;const body=battlefield.unitAnimation; battlefield.unitAnimation=()=>{bodyCalls++;};
            let idle;
            try{
              requestRender();await new Promise(r=>setTimeout(r,80));const frames=ctx.stats.frames;
              await new Promise(r=>setTimeout(r,160));idle={frames:ctx.stats.frames-frames,animated:battlefield.animated,bodyCalls};
            }finally{battlefield.unitAnimation=body;state=realState;battlefield.resetMotions();suspendRendering();}
            const arc=battlefield.healthArc(100,100),pixels=arc.getContext('2d').getImageData(0,0,96,96).data;
            let smoothEdges=0;for(let i=3;i<pixels.length;i+=4)if(pixels[i]>0&&pixels[i]<255)smoothEdges++;
            const sameArc=battlefield.healthArc(100,100)===arc;
            return {route:route.path,samples,expected,idle,smoothEdges,sameArc};
          });
          report.behavior=behavior;
          check(behavior.samples.length===3,'moving badge sampled at three exact times');
          const [a,b,c]=behavior.samples;
          check(Math.abs(c.x-a.x-behavior.expected.dx)<.01&&Math.abs(c.y-a.y-behavior.expected.dy)<.01,'badge follows the actual first path segment');
          check(Math.abs(b.x-(a.x+c.x)/2)<.01&&Math.abs(b.y-(a.y+c.y)/2)<.01,'badge interpolates at half movement');
          check(behavior.idle.frames===0&&!behavior.idle.animated&&behavior.idle.bodyCalls===0,'hidden body animations cannot keep the render loop alive');
          check(behavior.smoothEdges>40&&behavior.sameArc,'health arc has antialiased alpha edges and reuses its texture');
        }
      }
      await page.close();
    }
    check(report.errors.length===0&&report.missing.length===0,'no browser errors or missing resources');
  }catch(e){report.failure={stage,message:e.message};throw e;}
  finally{await fs.writeFile(path.join(folder,'results.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify({checks:report.checks,menus:report.menus.length,battle:report.battle.length,errors:report.errors.length,missing:report.missing.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
