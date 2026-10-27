const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..');
const dir=path.join(root,'maps/settlement_plan_20261026');
const screenshotDir=path.join(root,'build/japan-hre-plan-qa-20261026');
const summary=JSON.parse(fs.readFileSync(path.join(dir,'verification.json'),'utf8'));
const results=[];
let stage='startup';
(async()=>{
  fs.mkdirSync(screenshotDir,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio','--allow-file-access-from-files']});
  try{
    for(const [width,height] of [[1440,1000],[900,700],[390,844],[844,390],[360,640]]){
      const context=await browser.newContext({viewport:{width,height}});
      const page=await context.newPage();
      const errors=[];
      page.on('pageerror',error=>errors.push(error.message));
      page.on('requestfailed',request=>errors.push(request.url()));
      await page.goto(pathToFileURL(path.join(dir,'index.html')).href);
      for(const key of ['japan','hre']){
        stage=`${key}/${width}x${height}`;
        await page.locator('#'+key+'-tab').click();
        await page.waitForFunction(()=>image.complete&&image.naturalWidth>0);
        assert.equal(await page.locator('.point-row').count(),summary.maps[key].total);
        assert.equal(await page.locator('#'+key+'-tab').getAttribute('aria-selected'),'true');
        const pixels=await page.evaluate(()=>{
          const p=ctx.getImageData(0,0,canvas.width,canvas.height).data;
          let land=0,ink=0,water=0;
          for(let i=0;i<p.length;i+=4){
            if(p[i]>225&&p[i+1]>225&&p[i+2]<246&&p[i+2]>215)land++;
            if(p[i]<200&&p[i+1]<200&&p[i+2]<200)ink++;
            if(p[i]===220&&p[i+1]===235&&p[i+2]===241)water++;
          }
          return {land,ink,water,total:p.length/4};
        });
        assert(pixels.land>2000&&pixels.ink>100&&pixels.water>1000,JSON.stringify(pixels));
        const layout=await page.evaluate(()=>({overflowX:document.documentElement.scrollWidth>innerWidth,
          overflowY:document.documentElement.scrollHeight>innerHeight+2,
          canvas:canvas.getBoundingClientRect().toJSON(),aside:document.querySelector('aside').getBoundingClientRect().toJSON(),
          clippedControls:[...document.querySelectorAll('header button,.filters input,.filters select')].filter(e=>{const r=e.getBoundingClientRect();return r.left<0||r.right>innerWidth||r.top<0||r.bottom>innerHeight;}).length}));
        assert(!layout.overflowX&&!layout.overflowY&&!layout.clippedControls,JSON.stringify(layout));
        assert(layout.canvas.width>150&&layout.canvas.height>110);
        if(width===1440||width===390)await page.screenshot({path:path.join(screenshotDir,`${key}-${width}x${height}.png`)});
        const originalZoom=Number(await page.locator('#zoom').inputValue());
        await page.locator('#plus').click();
        assert(Number(await page.locator('#zoom').inputValue())>originalZoom);
        await page.locator('#fit').click();assert.equal(await page.locator('#zoom').inputValue(),'100');
        for(const kind of ['city','stable','port'])await page.locator(`.kinds input[value=${kind}]`).uncheck();
        assert.equal(await page.locator('.point-row').count(),summary.maps[key].counts.industry);
        for(const kind of ['city','stable','port'])await page.locator(`.kinds input[value=${kind}]`).check();
        const query=key==='japan'?'京都':'须德海';
        await page.locator('#search').fill(query);
        assert.equal(await page.locator('.point-row').count(),1);
        await page.locator('.point-row').click();
        assert.equal(await page.locator('#selected').isVisible(),true);
        assert.equal(await page.evaluate(()=>document.documentElement.scrollHeight>innerHeight+2),false);
        if(key==='hre')assert.match(await page.locator('#selected').innerText(),/历史海湾/);
        await page.locator('#search').fill('不存在的测试地点');
        assert.equal(await page.locator('.point-row').count(),0);
        assert.equal(await page.locator('#selected').isVisible(),false);
        await page.locator('#search').fill('');
        const firstGroup=await page.locator('#region option').nth(1).getAttribute('value');
        await page.locator('#region').selectOption(firstGroup);
        const expected=JSON.parse(fs.readFileSync(path.join(dir,key+'_points.json'),'utf8')).records.filter(r=>r.group===firstGroup).length;
        assert.equal(await page.locator('.point-row').count(),expected);
        if(key==='japan'){
          await page.locator('#region').selectOption('朝鲜半岛');
          assert.equal(await page.locator('.point-row').count(),23);
          assert.match(await page.locator('#downloads').innerText(),/朝鲜半岛/);
          assert.equal(await page.locator('.point-row i.city').count(),23);
          await page.locator('#region').selectOption('');
          await page.locator('#search').fill('海参崴');
          assert.equal(await page.locator('.point-row').count(),1);
          await page.locator('.point-row').click();
          assert.match(await page.locator('#selected').innerText(),/符拉迪沃斯托克/);
          await page.locator('#search').fill('');
        }
        await page.locator('#region').selectOption('');
        results.push({map:key,width,height,points:summary.maps[key].total,pixels,errors:errors.length});
      }
      assert.deepEqual(errors,[]);
      await context.close();
    }
  }finally{await browser.close();}
  console.log(JSON.stringify({views:results.length,errors:0,results}));
})().catch(error=>{console.error(stage+': '+error.message);process.exitCode=1;});
