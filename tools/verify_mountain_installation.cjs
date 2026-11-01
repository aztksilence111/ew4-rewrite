'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:57941';
const folder=path.resolve(process.argv[3]||'build/mountain-installation-20261101');
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(folder,'client/fixture.json'),'utf8'));
  const report={checks:0,errors:[],missing:[],screenshots:[]};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  try{
    const page=await browser.newPage();
    page.on('pageerror',e=>report.errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
    await page.goto(base);
    await page.waitForFunction(()=>typeof catalog!=='undefined'&&!!catalog&&!!originalTextRenderer);
    await page.evaluate(async()=>{await installState(await rpc('load',{}),true);});
    const expected=[['-10%','-25%'],['-20%'],['-10%','-30%'],['-10%','-25%','-10%'],['-10%','-10%','-30%']];
    for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(size);
      for(let i=0;i<5;i++){
        const result=await page.evaluate(async cell=>{
          await selectCell(cell,true);view.x=point(cell).x;view.y=point(cell).y;requestRender();
          const panel=$('terrain-info'),p=panel.getBoundingClientRect();
          return {hidden:panel.hidden,text:[...panel.querySelectorAll('.original-text')].map(e=>e.getAttribute('aria-label')),
            contained:[...panel.querySelectorAll('.terrain-row')].every(e=>{const r=e.getBoundingClientRect();return r.left>=p.left-1&&r.right<=p.right+1&&r.top>=p.top-1&&r.bottom<=p.bottom+1;})};
        },fixture.installations[i]);
        check(!result.hidden&&JSON.stringify(result.text)===JSON.stringify(expected[i]),'correct installation rows '+i+' / '+size.width);
        check(result.contained,'rows fit panel '+i+' / '+size.width);
        if(i===0){const name='fence-'+size.width+'.png';await page.screenshot({path:path.join(folder,name)});report.screenshots.push(name);}
      }
      const selection=await page.evaluate(async cell=>{await selectCell(cell,true);view.x=point(cell).x;view.y=point(cell).y;requestRender();return selection.range;},fixture.origin);
      check(selection.includes(fixture.destination),'mountain move circle included '+size.width);
    }
    const moved=await page.evaluate(async fixture=>{
      const next=await rpc('action',{op:'move',unit:fixture.unit,cell:fixture.destination,revision:state.revision});
      await installState(next);return state.units.find(u=>u.id===fixture.unit).cell;
    },fixture);
    check(moved===fixture.destination,'real browser command commits heavy artillery mountain move');
    check(report.errors.length===0&&report.missing.length===0,'no script or asset errors');
    console.log(JSON.stringify(report));
  }finally{await fs.writeFile(path.join(folder,'browser.json'),JSON.stringify(report,null,2));await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
