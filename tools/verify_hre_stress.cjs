'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const count=Number(process.env.EW4_STRESS_COUNT||30000),root=path.resolve(process.env.EW4_STRESS_ROOT||'build/hre-stress-30k-20261028'),base=process.argv[2]||'http://127.0.0.1:5597';
const variants=process.argv.includes('--before-only')?['before']:process.argv.includes('--after-only')?['after']:['before','after'];
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const rows=[],errors=[];
  try{
    const payload=JSON.parse(await fs.readFile(path.join(root,'before/view-'+count+'.json'),'utf8')).data;
    for(const variant of variants)for(const backend of process.argv.includes('--quick')?['pixi']:['pixi','canvas'])for(const [width,height] of process.argv.includes('--quick')||process.argv.includes('--desktop-only')?[[1280,720]]:[[1280,720],[844,390]]){
      const page=await browser.newPage({viewport:{width,height}});
      page.on('pageerror',e=>errors.push(e.message));
      await page.route(/\/(app|battlefield|pixi-battle)\.js(?:\?|$)/,async route=>route.fulfill({contentType:'text/javascript',body:await fs.readFile(path.join(variant==='before'?path.join(root,'before'):'rewrite/Ew4.Web/wwwroot',new URL(route.request().url()).pathname.split('/').pop()),'utf8')}));
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));
      await page.waitForFunction(()=>startupReady,null,{timeout:180000});
      let transformMax=null;
      if(variant==='after'&&backend==='pixi'){
        transformMax=await page.evaluate(source=>{
          const scope={};new Function('globalThis',source)(scope);
          const original=new scope.Ew4Pixi.Context(document.createElement('canvas')),current=new Ew4Pixi.Context(document.createElement('canvas'));
          const texture=document.createElement('canvas');texture.width=24;texture.height=20;
          const pen=texture.getContext('2d');for(let y=0;y<20;y++)for(let x=0;x<24;x++){pen.fillStyle=`rgb(${x*10},${y*12},${(x+y)*5})`;pen.fillRect(x,y,1,1);}
          let max=0;
          try{for(let frame=0;frame<3;frame++){
            for(const draw of [original,current]){
              draw.begin(256,256);
              for(let i=0;i<12;i++){
                const angle=frame===1?.3:0,a=i%3===0?-1:1,d=i%4===0?-1:1;
                draw.setTransform(a*Math.cos(angle),a*Math.sin(angle),-d*Math.sin(angle),d*Math.cos(angle),25+i%4*58,28+Math.floor(i/4)*70);
                draw.globalAlpha=.3+i/20;draw.globalCompositeOperation=i%2?'source-over':'lighter';
                draw.drawImage(texture,2,3,18,14,-12,-10,24+i,20+i);
              }
              draw.end();
            }
            const a=original.renderer.extract.pixels(),b=current.renderer.extract.pixels();
            for(let i=0;i<a.length;i++)max=Math.max(max,Math.abs(a[i]-b[i]));
          }}finally{original.renderer.destroy(true);current.renderer.destroy(true);}
          return max;
        },await fs.readFile(path.join(root,'before/pixi-battle.js'),'utf8'));
        assert.ok(transformMax<=1,'axis/reflection/rotation pool reuse preserves pixels: '+transformMax);
      }
      await page.evaluate(async data=>{await installState(data,true);stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();damageNumbers.clear();suspendRendering();},payload);
      await page.waitForFunction(()=>[...images.values()].every(i=>i.complete));
      const result=await page.evaluate(async variant=>{
        const percentile=(a,p)=>[...a].sort((x,y)=>x-y)[Math.min(a.length-1,Math.floor(a.length*p))];
        const installs=[];
        for(let i=0;i<5;i++){
          const next={...state,revision:state.revision+1,units:[...state.units],movements:[],notices:[],combat:null,recruited:[]};
          const begin=performance.now();await installState(next);suspendRendering();installs.push(performance.now()-begin);
        }
        const origin=point(state.units[Math.floor(state.units.length/2)].cell),samples=[];
        for(const zoom of [1,.3,.09]){
          view.zoom=zoom;view.x=origin.x;view.y=origin.y;clampView();
          for(let i=0;i<16;i++){
            await new Promise(requestAnimationFrame);view.x+=i%2?32:-32;renderDirty=true;
            const begin=performance.now();render(2000+i*16,true);suspendRendering();
            if(i>=4)samples.push({zoom,ms:performance.now()-begin,visible:battlefield.visibleUnits.length});
          }
        }
        const phases={},methods=['units','staticGround','labels','unitMarkers'],originals=new Map();
        for(const name of methods){const fn=battlefield[name];originals.set(name,fn);battlefield[name]=function(...args){const begin=performance.now();try{return fn.apply(this,args);}finally{phases[name]=(phases[name]||0)+performance.now()-begin;}};}
        const finish=ctx.end;ctx.end=function(...args){const begin=performance.now();try{return finish.apply(this,args);}finally{phases.submit=(phases.submit||0)+performance.now()-begin;}};
        try{render(3000,true);suspendRendering();}finally{for(const [name,fn] of originals)battlefield[name]=fn;ctx.end=finish;}
        const data=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data,colors=new Set();
        for(let i=0;i<data.length;i+=388)colors.add(data[i]+','+data[i+1]+','+data[i+2]);
        let badgeDiff=null,cacheStress=null;
        if(variant==='after'){
          const read=()=>ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
          const cached=new Uint8Array(read()),method=battlefield.farBadge;battlefield.farBadge=()=>null;
          render(3000,true);suspendRendering();const direct=read();let sum=0;for(let i=0;i<direct.length;i++)sum+=Math.abs(direct[i]-cached[i]);
          badgeDiff=sum/direct.length;battlefield.farBadge=method;
          render(3000,true);suspendRendering();
          const unit=state.units[Math.floor(state.units.length/2)],profile=manifest.profiles[unit.profile];
          battlefield.farBadgeUses=new Map();battlefield.farBadgeBuilds=0;battlefield.farBadgeBuildPixels=0;
          for(let i=1;i<=1000;i++){const changed={...unit,health:i,maxHealth:2000};battlefield.farBadge(changed,profile,'blue');battlefield.farBadge(changed,profile,'blue');}
          cacheStress={builds:battlefield.farBadgeBuilds,pixels:battlefield.farBadgeBuildPixels};
        }
        let hitChecks=0;
        for(let i=0;i<state.units.length;i+=100){const u=state.units[i],p=point(u.cell);if(cellAt((p.x-view.x)*view.zoom+viewport.w/2,(p.y-view.y)*view.zoom+viewport.h/2)===u.cell)hitChecks++;}
        return {count:state.units.length,gpu:!!ctx.gpu,colors:colors.size,badgeDiff,cacheStress,phases,hitChecks,markerPixels:24*battlefield.markerScale*view.zoom,cacheEntries:battlefield.farBadges?.size||0,cachePixels:battlefield.farBadgePixels||0,installMedian:percentile(installs,.5),installs,
          frames:[1,.3,.09].map(zoom=>{const a=samples.filter(s=>s.zoom===zoom);return {zoom,median:percentile(a.map(s=>s.ms),.5),p95:percentile(a.map(s=>s.ms),.95),visible:a.at(-1).visible};})};
      },variant);
      assert.equal(result.count,count);assert.equal(result.gpu,backend==='pixi');assert.ok(result.colors>20,'nonblank map');
      assert.equal(result.hitChecks,Math.ceil(count/100),'all sampled unit cells remain individually selectable');
      if(variant==='after'){assert.ok(result.badgeDiff<3,'badge pixel equivalence '+result.badgeDiff);assert.ok(result.cacheEntries<=128&&result.cachePixels<=2*1024*1024,'bounded badge cache');assert.ok(result.cacheStress.builds<=8&&result.cacheStress.pixels<=256*1024,'diverse health cannot flood cache uploads');}
      await page.screenshot({path:path.join(root,`${variant}-${backend}-${width}.png`)});
      await page.evaluate(()=>{view.zoom=1;const p=point(state.units[Math.floor(state.units.length/2)].cell);view.x=p.x;view.y=p.y;clampView();requestRender();});
      const before=await page.evaluate(()=>({...view}));
      await page.mouse.move(width/2,height/2);await page.mouse.down();await page.mouse.move(width/2+80,height/2+40,{steps:4});await page.mouse.up();
      await page.mouse.wheel(0,-120);await page.waitForTimeout(150);
      const after=await page.evaluate(()=>({...view}));
      assert.ok(after.x!==before.x||after.y!==before.y,'drag moves camera');assert.ok(after.zoom>before.zoom,'wheel zooms map');
      const row={variant,backend,width,height,transformMax,...result};rows.push(row);console.log(JSON.stringify(row));await page.close();
    }
    assert.deepEqual(errors,[]);
  }finally{await fs.writeFile(path.join(root,'browser-'+variants.join('-')+'.json'),JSON.stringify({rows,errors},null,2));await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
