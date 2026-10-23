'use strict';
const fs=require('node:fs/promises'),path=require('node:path'),assert=require('node:assert/strict'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..'),output=path.resolve(process.argv[4]||path.join(root,'build/campaign-capture-20261023'));
const url=process.argv[2]||'http://127.0.0.1:5438',runtime=path.resolve(process.argv[3]||path.join(output,'runtime'));
(async()=>{
  const fixture=JSON.parse(await fs.readFile(path.join(root,'rewrite/reports/local-multiplayer-20261021/backend.json'),'utf8'));
  await fs.mkdir(output,{recursive:true});
  await fs.mkdir(runtime,{recursive:true});
  for(const [name,slot]of[['campaign-finished',5],['campaign-defeat',6],['single-defeat',4],['single-timeout',3]])
    await fs.copyFile(path.join(fixture.folder,name,'slot-1.json'),path.join(runtime,`slot-${slot}.json`));
  const report={checks:0,errors:[],missing:[],screenshots:[]};
  const check=(pass,label)=>{assert(pass,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1136,height:640}});page.setDefaultTimeout(30000);
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
  try{
    await page.goto(url);
    await page.waitForFunction(()=>catalog&&startupReady&&document.documentElement.dataset.fontsReady==='true',null,{timeout:90000});
    for(const [slot,name]of[[5,'allied-victory'],[6,'allied-defeat']]){
      await page.evaluate(slot=>load(slot,0),slot);
      await page.locator('.local-result-actions').waitFor();
      await page.waitForFunction(()=>battlefield.state===state&&[...images.values()].every(img=>img.complete));
      await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
      const results=await page.evaluate(()=>state.localMultiplayer.results);
      check(results.filter(f=>f.outcome==='Victory').length===await page.locator('.local-won').count(),name+' winner rows match server');
      check(results.filter(f=>f.outcome==='Defeat').length===await page.locator('.local-lost').count(),name+' losing rows match server');
      check(await page.evaluate(()=>state.localMultiplayer.shareAlliedVictory),name+' uses campaign policy');
      if(slot===5)check(await page.locator('.local-won').count()>1,'multiple allies visibly win');
      else check(await page.evaluate(()=>state.battleResult.outcome==='Defeat'&&state.localMultiplayer.humanFactions.every(id=>state.localMultiplayer.results.find(r=>r.id===id).outcome==='Defeat')),'all human allies visibly lose');
      check(await page.evaluate(()=>{const pixels=ctx.getImageData(0,0,canvas.width,canvas.height).data,colors=new Set();
        for(let i=0;i<pixels.length;i+=400)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);return colors.size>20;
      }),name+' battlefield background renders');
      for(const [width,height]of[[1136,640],[844,390],[390,844]]){
        await page.setViewportSize({width,height});
        await page.waitForTimeout(100);
        check(await page.evaluate(()=>{
          const r=document.querySelector('.local-multiplayer-panel').closest('.dialog').getBoundingClientRect();
          return r.left>=-1&&r.top>=-1&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1;
        }),name+' dialog fits '+width+'x'+height);
        const file=name+'-'+width+'x'+height+'.png';await page.screenshot({path:path.join(output,file)});report.screenshots.push(file);
      }
      await page.getByRole('button',{name:'退出',exact:true}).click();
      await page.locator('#main-menu').waitFor({state:'visible'});
    }
    await page.setViewportSize({width:1136,height:640});
    await page.evaluate(()=>load(4,0));
    await page.locator('#dialogue:not([hidden])').waitFor();
    check(await page.evaluate(()=>state.battleResult.outcome==='Defeat'&&!state.localMultiplayer),'single player failure result reaches renderer');
    check(await page.evaluate(()=>state.dialogue===state.battleResult.failureDescription&&state.battleResult.failureDescriptionKey==='desc_failure 1'),'ordinary failure description uses dialogue');
    check(await page.locator('.victory-result:visible').count()===0,'dialogue precedes settlement');
    check(await page.locator('#dialogue-portrait').evaluate(img=>img.complete&&img.naturalWidth>0),'failure commander portrait rendered');
    for(const [width,height]of[[1136,640],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(250);
      const file='single-defeat-talk-'+width+'x'+height+'.png';await page.screenshot({path:path.join(output,file)});report.screenshots.push(file);
    }
    await page.getByRole('button',{name:'继续对白',exact:true}).click();
    await page.locator('[data-layout="form_victory"]:visible').waitFor();
    check(await page.locator('[data-native-id="btn_restart"]:visible').count()===1,'failure restart button connected');
    check(await page.locator('[data-native-id="btn_exit"]:visible').count()===1,'failure exit button connected');
    check(await page.locator('[data-native-id="btn_continue"]:visible').count()===0,'failure hides continue');
    check(await page.locator('[data-layout="form_failure"]').count()===0,'nation elimination notification is not used as a battle settlement');
    check(await page.locator('.result-general').count()===6,'original general row restored');
    report.failureLayout='form_talk then form_victory';
    for(const [width,height]of[[1136,640],[844,390],[390,844]]){
      await page.setViewportSize({width,height});await page.waitForTimeout(250);
      check(await page.evaluate(()=>{const body=document.querySelector('[data-layout="form_victory"]'),r=body.closest('.dialog').getBoundingClientRect();
        return r.left>=-1&&r.top>=-1&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1;
      }),'single failure layout fits '+width+'x'+height);
      check(await page.evaluate(()=>{
        const body=document.querySelector('[data-layout="form_victory"]'),base=Number(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale'));
        const fit=Math.min((innerWidth-24)/346,(innerHeight-24)/210),expected=Math.min(fit,Math.max(base,Math.min(1.15,fit)));
        return Math.abs(body.parentElement.getBoundingClientRect().width-346*expected)<2;
      }),'result follows updated UI scale '+width+'x'+height);
      check(await page.evaluate(()=>Math.abs(canvas.width/canvas.height-canvas.clientWidth/canvas.clientHeight)<.005
        &&viewport.w===canvas.clientWidth&&viewport.h===canvas.clientHeight),'covered battlefield redraws at the new aspect ratio '+width+'x'+height);
      const alignment=await page.evaluate(()=>{
        const ink=id=>{const c=document.querySelector('[data-native-id="'+id+'"] canvas'),r=c.getBoundingClientRect(),data=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
          let top=c.height,bottom=-1;for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++)if(data[(y*c.width+x)*4+3]>64){top=Math.min(top,y);bottom=Math.max(bottom,y);}
          return r.top+(top+bottom+1)/2*r.height/c.height;};
        return [['text_award','text_award_val'],['text_gain','text_gain_val']].map(([label,value])=>Math.abs(ink(label)-ink(value)));
      });
      check(alignment.every(delta=>delta<1),'Chinese labels and numeric ink centers align '+width+'x'+height);
      const file='single-defeat-'+width+'x'+height+'.png';await page.screenshot({path:path.join(output,file)});report.screenshots.push(file);
    }
    await page.evaluate(()=>{closeDetail();refreshHud();refreshHud();});
    check(await page.locator('#detail-modal').isHidden()&&await page.locator('#dialogue').isHidden(),'dismissed result and dialogue do not reopen on refresh');
    await page.evaluate(async()=>{await showMain();await load(4,0);});await page.locator('#dialogue:not([hidden])').waitFor();
    await page.getByRole('button',{name:'继续对白',exact:true}).click();
    await page.locator('[data-layout="form_victory"]:visible').waitFor();
    await page.getByRole('button',{name:'重新开始',exact:true}).click();
    await page.waitForFunction(()=>state&&state.phase!=='Finished'&&!busy&&!sessionTransition);
    check(await page.evaluate(()=>state.scenario==='campaign2_03'&&!state.localMultiplayer),'failure restart starts the same single player campaign');
    await page.evaluate(()=>load(3,0));await page.locator('#dialogue:not([hidden])').waitFor();
    check(await page.evaluate(()=>state.dialogue===state.battleResult.failureDescription&&state.battleResult.failureDescriptionKey==='desc_failure 2'),'timeout description connected');
    await page.screenshot({path:path.join(output,'single-timeout-talk-390x844.png')});report.screenshots.push('single-timeout-talk-390x844.png');
    await page.getByRole('button',{name:'继续对白',exact:true}).click();
    await page.locator('[data-layout="form_victory"]:visible').waitFor();
    await page.waitForTimeout(250);
    await page.screenshot({path:path.join(output,'single-timeout-390x844.png')});report.screenshots.push('single-timeout-390x844.png');
    await page.getByRole('button',{name:'退出',exact:true}).click();await page.locator('#main-menu').waitFor({state:'visible'});
    check(await page.locator('#main-menu').isVisible(),'single failure exit returns to menu');
    check(report.errors.length===0&&report.missing.length===0,'no browser errors or missing assets');
    await fs.writeFile(path.join(output,'browser.json'),JSON.stringify(report,null,2)+'\n');
    console.log(JSON.stringify(report));
  }catch(error){
    await page.screenshot({path:path.join(output,'browser-failure.png')});
    const diagnostic=await page.evaluate(()=>({phase:state?.phase,result:state?.battleResult,
      dialogue:state?.dialogue,scenarioPopup:state?.scenarioPopup,busy,skipAi,sessionTransition,
      loading:!document.getElementById('loading-screen').hidden,pause:!document.getElementById('pause').hidden,
      sameBattlefield:battlefield.state===state,images:[...images].filter(([,img])=>!img.complete).map(([key])=>key),
      resultId:victoryUi.id,resultPending:victoryUi.pending})).catch(()=>null);
    console.log(JSON.stringify(diagnostic));
    throw error;
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
