'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),{createRequire}=require('node:module');
const deps=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs');
const {chromium}=deps('playwright');
const base=process.argv[2]||'http://127.0.0.1:57943',out=path.resolve(process.argv[3]||'build/critical-audit-20261102/browser');
(async()=>{
  await fs.mkdir(out,{recursive:true});
  const fixtures=JSON.parse(await fs.readFile(path.resolve(out,'../fixtures.json'),'utf8')).fixtures;
  const report={checks:0,errors:[],missing:[],cases:[]},check=(pass,label)=>{assert.ok(pass,label);report.checks++;};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const [source,renderer,width,height] of [[true,'pixi',1280,720],[false,'canvas',1280,720],[false,'pixi',844,390],[false,'canvas',390,844]]){
      const context=await browser.newContext({viewport:{width,height},hasTouch:width<1000,isMobile:width<1000}),page=await context.newPage();
      const label=[source?'source':'compat',renderer,width].join('-');
      page.on('pageerror',e=>report.errors.push(label+': '+e.message));page.on('response',r=>{if(r.status()>=400&&!r.url().endsWith('/favicon.ico'))report.missing.push(r.url());});
      if(source)await page.route(base+'/',async route=>{const response=await route.fetch();await route.fulfill({response,body:(await response.text()).replace(/((?:src|href)=")compat\//g,'$1')});});
      await page.goto(base+(renderer==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      for(const fixture of fixtures){
        await page.evaluate(async fixture=>{
          settings.speed=1;await load(fixture.slot,1);stageIntroOpen=false;$('detail-modal').hidden=true;
          const target=state.units.find(u=>u.id===fixture.defender),p=point(target.cell);view.x=p.x;view.y=p.y;view.zoom=1.8;clampView();
          window.criticalProbe={hits:[],responses:[]};
          const add=damageNumbers.add.bind(damageNumbers);damageNumbers.add=(amount,critical,...rest)=>{
            add(amount,critical,...rest);const hit=damageNumbers.instances.at(-1);if(!hit)return;
            const data=hit.canvas.getContext('2d').getImageData(0,0,hit.canvas.width,hit.canvas.height).data;let red=0,white=0,outline=0;
            for(let i=0;i<data.length;i+=4){if(data[i+3]<128)continue;if(data[i]>180&&data[i+1]<90&&data[i+2]<90)red++;if(data[i]>220&&data[i+1]>220&&data[i+2]>220)white++;if(data[i]<25&&data[i+1]<25&&data[i+2]<25)outline++;}
            criticalProbe.hits.push({amount,critical,red,white,outline,x:hit.x,y:hit.y});
          };
          const send=rpc;rpc=async(...args)=>{const result=await send(...args);if(result.combat)criticalProbe.responses.push(result.combat);return result;};
          window.criticalAttack=act('attack',{unit:fixture.attacker,target:fixture.defender}).then(()=>{criticalProbe.completed=true;criticalProbe.healthAfter=state.units.find(u=>u.id===fixture.defender)?.health||0;});
        },fixture);
        await page.waitForFunction(()=>criticalProbe.hits.length>0,null,{timeout:15000});
        const visible=await page.evaluate(fixture=>{
          const hit=damageNumbers.instances.find(h=>h.amount===fixture.damage&&h.critical===fixture.critical),draw=ctx.drawImage.bind(ctx);let matching=0;
          ctx.drawImage=(image,...args)=>{if(image===hit?.canvas)matching++;return draw(image,...args);};
          try{renderDirty=true;render(performance.now(),true);}finally{ctx.drawImage=draw;}
          return {matching,sheet:damageNumbers.sheet.src};
        },fixture);
        await page.screenshot({path:path.join(out,label+'-'+fixture.kind+'.png')});
        await page.evaluate(()=>criticalAttack);
        const result=await page.evaluate(()=>criticalProbe),main=result.hits.find(hit=>hit.amount===fixture.damage&&hit.critical===fixture.critical),response=result.responses[0];
        check(response.critical===fixture.critical&&response.damage===fixture.damage,label+' '+fixture.kind+' actual backend response');
        check(!!main,label+' '+fixture.kind+' damage passed to renderer '+JSON.stringify(result.hits));
        check(fixture.critical?main.red>20&&main.white===0:main.white>20,label+' '+fixture.kind+' red/white bitmap pixels');
        check(main.outline>10&&visible.matching>0&&visible.sheet.endsWith('font_fight_num_hd.png'),label+' original glyph outline and actual frame draw');
        check(result.healthAfter===fixture.healthAfter,label+' '+fixture.kind+' exact health deduction');
        report.cases.push({label,kind:fixture.kind,damage:main.amount,critical:main.critical,red:main.red,white:main.white,healthAfter:result.healthAfter,draws:visible.matching});
        // Reload removes interception wrappers before the next real backend action.
        await page.reload();await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      }
      await context.close();
    }
    check(!report.errors.length&&!report.missing.length,'no missing resources or script errors');
  }finally{await fs.writeFile(path.join(out,'report.json'),JSON.stringify(report,null,2)+'\n');await browser.close();}
  console.log(JSON.stringify(report));
})().catch(error=>{console.error(error);process.exitCode=1;});
