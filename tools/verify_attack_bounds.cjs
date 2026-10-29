'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2],out=path.resolve(process.argv[3]);
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  const report={checks:0,errors:[],views:[]},check=(value,label)=>{assert.ok(value,label);report.checks++;};
  try{
    const page=await browser.newPage();
    if(process.argv.includes('--compat'))await page.route(/\/(?:compat\/)?app\.js(?:\?|$)/,route=>route.fulfill({contentType:'text/javascript',path:path.join(__dirname,'../rewrite/Ew4.Web/wwwroot/compat/app.js')}));
    page.on('pageerror',error=>report.errors.push(error.message));
    await page.goto(base);await page.waitForFunction(()=>startupReady,null,{timeout:90000});
    await page.evaluate(()=>start('conquest1','fra'));
    await page.waitForFunction(()=>!busy&&!stageIntroOpen);
    for(const [width,height] of [[1280,720],[844,390],[390,844]]){
      await page.setViewportSize({width,height});
      const result=await page.evaluate(async()=>{
        closeDetail();
        const unit=state.units.find(u=>u.commanderId&&u.attack&&(u.attack.minimum!==Number(manifest.profiles[u.profile].stats.minatk)||u.attack.maximum!==Number(manifest.profiles[u.profile].stats.maxatk)));
        if(!unit)throw Error('No boosted scenario commander');
        await openUnitInfo(unit);
        const grid=document.querySelector('.info-abilities'),value=grid.children[1],rect=value.getBoundingClientRect();
        const p=manifest.profiles[unit.profile],gridText=mock=>abilityGrid(unit.profile,mock).children[1].textContent;
        return {expected:unit.attack.minimum+'-'+unit.attack.maximum,actual:value.querySelector('[aria-label]')?.getAttribute('aria-label')||value.textContent,
          base:gridText(),baseExpected:p.stats.minatk+'-'+p.stats.maxatk,
          example:gridText({...unit,attack:{minimum:5,maximum:7}}),zero:gridText({...unit,attack:{minimum:0,maximum:0}}),
          visible:rect.width>0&&rect.height>0&&rect.x>=0&&rect.right<=innerWidth&&rect.y>=0&&rect.bottom<=innerHeight};
      });
      await page.screenshot({path:path.join(out,'attack-'+width+'.png')});
      check(result.actual===result.expected,'actual commander attack '+width+' '+JSON.stringify(result));
      check(result.base===result.baseExpected,'recruit base values '+width);
      check(result.example==='5-7','requested example '+width);check(result.zero==='0-0','zero bounds '+width);
      check(result.visible,'attack text within viewport '+width);
      report.views.push({width,height,...result});
    }
    check(report.errors.length===0,'no browser errors');
    await fs.writeFile(path.join(out,'browser.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify(report));
  }finally{await browser.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});
