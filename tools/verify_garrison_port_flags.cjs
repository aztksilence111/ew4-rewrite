'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5535',folder=path.resolve(process.argv[3]||'build/r-issues-fix-20261025/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],cases:[],screenshots:[],scope:'Desktop Chromium with actual flag sprite pixels and mobile viewport emulation; no physical Android execution'};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    for(const mode of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:1280,height:720}});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(mode==='canvas'?'?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:60000});
      await page.evaluate(()=>start('conquest1','fra'));
      await page.evaluate(()=>Promise.all(['buildings','buildings_hd'].map(name=>image('images/'+name+'.png').decode())));
      const samples=await page.evaluate(()=>{
        const current=Ew4Battlefield,results=[];
        for(const player of ['a','b'])for(const owner of ['a','b','ally','neutral'])for(const kind of ['port','city']){
          const surface=document.createElement('canvas');surface.width=640;surface.height=420;
          const draw=surface.getContext('2d',{willReadFrequently:true}),calls=[];
          const factions=['a','b','ally','neutral'].map(id=>({id,flag:id==='a'?'fra':'pru',flagImage:null}));
          const cells=Array.from({length:64},()=>({active:true,sea:true,owner:null,buildingKind:null}));
          cells[27]={...cells[27],owner,buildingKind:kind};
          const fixture={...state,width:8,height:8,units:[],cells,factions,playerFaction:player};
          const api={...battlefield.api,sprite:(atlas,name,x,y,scale=1,context=draw)=>{calls.push(name);return sprite(atlas,name,x,y,scale,context);}};
          const renderer=new current.Renderer(api);
          for(const method of ['staticGround','grid','objectives','units','unitMarkers','labels','highlights','scheduleCityLabels','syncMotions'])renderer[method]=()=>{};
          const scene={draw,state:fixture,map:{overlays:[],tiles:[],labels:[]},visibleRect:{left:0,top:0,width:640,height:420},zoom:1,
            motions:new Map(),deaths:new Map(),facings:new Map(),arrivals:new Map(),combatMotions:new Map()};
          const paint=time=>{
            draw.clearRect(0,0,640,420);calls.length=0;renderer.frame(scene,time);
            return {pixels:draw.getImageData(0,0,640,420).data,flags:calls.filter(name=>/^(fra|pru)[1-4]\.png$/.test(name)),animated:renderer.animated};
          };
          const first=paint(0),second=paint(100);let changed=0,opaque=0;
          for(let i=0;i<first.pixels.length;i+=4){if(first.pixels[i+3])opaque++;if([0,1,2,3].some(c=>first.pixels[i+c]!==second.pixels[i+c]))changed++;}
          results.push({player,owner,kind,first:first.flags,second:second.flags,animated:second.animated,changed,opaque});
          const unit={owner,remainingMovement:6,morale:0,constructionTurns:0,acted:false,canAttack:true};
          results.at(-1).unitAnimated=current.flagAnimated(unit,fixture);
          fixture.units=[{...state.units[0],id:'stationed',owner,cell:27,remainingMovement:0,acted:true,canAttack:false,
            canNavigateMove:false,burning:0,objective:false,objectiveMarker:0}];
          scene.state={...fixture};
          const occupied=paint(100);results.at(-1).occupiedFlags=occupied.flags;
        }
        return results;
      });
      for(const sample of samples){
        report.cases.push({mode,...sample});
        const animated=sample.kind==='port'&&sample.owner===sample.player,label=mode+' '+sample.player+'/'+sample.owner+'/'+sample.kind;
        check(sample.opaque>50,label+' flag asset visible');
        check(sample.first.length===1&&sample.second.length===1,label+' one facility flag');
        check((sample.changed>0)===animated,label+' actual pixels animate only the player port');
        check(sample.animated===animated,label+' idle scheduling agrees with painted animation');
        check(sample.unitAnimated===(sample.owner===sample.player),label+' unit flag ownership unchanged');
        check(sample.occupiedFlags.length===0,label+' garrison suppresses duplicate facility flag');
      }
      for(const [width,height]of [[1280,720],[844,390],[390,844]]){
        await page.setViewportSize({width,height});
        await page.evaluate(()=>{const cell=state.cells.findIndex((c,i)=>c.buildingKind==='port'&&c.owner===state.playerFaction&&!state.units.some(u=>u.cell===i));
          const p=point(cell);view.x=p.x;view.y=p.y;view.zoom=2;clampView();requestRender();});
        await page.waitForTimeout(250);
        check(await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>{render(performance.now());const proof=document.createElement('canvas');proof.width=canvas.width;proof.height=canvas.height;
          const draw=proof.getContext('2d');draw.drawImage(canvas,0,0);const s=new Set(),d=draw.getImageData(0,0,proof.width,proof.height).data;
          for(let i=0;i<d.length;i+=4*397)s.add(d[i]+','+d[i+1]+','+d[i+2]);resolve(s.size>20);}))),mode+' nonblank '+width);
        const filename=mode+'-'+width+'x'+height+'.png';await page.screenshot({path:path.join(folder,filename)});report.screenshots.push(filename);
      }
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing assets');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,cases:report.cases.length,errors:report.errors,missing:report.missing,screenshots:report.screenshots}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
