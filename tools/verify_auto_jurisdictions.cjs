'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5554',folder=path.resolve(process.argv[3]||'build/auto-jurisdictions-connected-20261025/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],districts:[],screenshots:[],scope:'Actual Edge with Canvas/Pixi, desktop and mobile viewport emulation; no physical Android run.'};
  const check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const backend of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:1280,height:900}});
      page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>state&&ctx&&[...images.values()].every(i=>i.complete));
      await page.evaluate(()=>{stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();damageNumbers.clear();});
      const districts=await page.evaluate(()=>[2158,2551,2713,2980,3023,3027,3028].map(cell=>({cell,kind:state.cells[cell].buildingKind,
        level:state.cells[cell].buildingLevel,size:state.cells.filter(c=>c.jurisdictionCenter===cell).length})));
      report.districts.push({backend,districts});
      const size=cell=>districts.find(d=>d.cell===cell).size;
      check(size(3023)===23,backend+' image 2 independent city has 23 cells');
      check(size(2551)===27&&size(2713)===25,backend+' Clermont and Lyon retain surrounding districts');
      check(size(3027)===6&&size(3028)===9,backend+' image 3 facilities have compact multi-cell districts');
      check(size(2158)>=20&&size(2158)<40&&size(2980)>=30,backend+' Paris does not swallow remote cities');
      const connectivity=await page.evaluate(()=>{
        const {cells,width,height}=state,roots=cells.map((c,i)=>c.jurisdictionCenter===i?i:-1).filter(i=>i>=0);
        const flood=district=>{
          const reached=new Set(roots),queue=[...roots];
          for(let p=0;p<queue.length;p++){
            const cell=queue[p];
            for(let d=0;d<6;d++){
              const next=Ew4Battlefield.neighbor(cell,d,width,height),site=cells[next];
              if(!site?.active||site.sea||site.buildingKind==='port'||reached.has(next)||site.owner!==cells[cell].owner
                ||district&&site.jurisdictionCenter!==cells[cell].jurisdictionCenter)continue;
              reached.add(next);queue.push(next);
            }
          }
          return reached;
        };
        const eligible=flood(false),connected=flood(true);
        return{orphaned:[...eligible].filter(i=>Number.isInteger(cells[i].jurisdictionCenter)&&!connected.has(i)),
          border:[3436,3515,3593].map(cell=>({cell,center:cells[cell].jurisdictionCenter}))};
      });
      check(connectivity.orphaned.length===0,backend+' every initial mainland district connects to its center');
      check(connectivity.border.every(c=>c.center===3595),backend+' Rome/Naples isolated strip joins its reachable factory');
      report.districts.at(-1).connectivity=connectivity;
      await page.locator('#map-mode').click();await page.getByRole('menuitemradio',{name:'辖区地图'}).click();
      await page.locator('#map-mode-options').waitFor({state:'hidden'});
      for(const [width,height]of [[1280,900],[844,390],[390,844]]){
        await page.setViewportSize({width,height});
        for(const [name,cell,zoom]of [['france',2785,.8],['separate-city',3023,1.5],['marseille',3027,1.5],['rome-border',3515,1.5]]){
          const proof=await page.evaluate(({cell,zoom})=>{
            selection=undefined;selected=undefined;$('selection').hidden=true;
            Object.assign(view,Ew4Battlefield.point(cell,state.width),{zoom});clampView();suspendRendering();render(2000);suspendRendering();
            const pixels=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
            const colors=new Set();for(let i=0;i<pixels.length;i+=4*397)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);
            return{colors:colors.size,edges:battlefield.districtCanvases.size,center:state.cells[cell].jurisdictionCenter};
          },{cell,zoom});
          check(proof.colors>30&&proof.edges>0&&(['france','rome-border'].includes(name)||proof.center===cell),backend+' nonblank district edges '+name+' '+width);
          const filename=backend+'-'+name+'-'+width+'x'+height+'.png';
          await page.screenshot({path:path.join(folder,filename)});report.screenshots.push(filename);
        }
      }
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no page errors or missing assets');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,screenshots:report.screenshots.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
