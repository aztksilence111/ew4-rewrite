'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const folder=process.env.EW4_FONT_REPORT||'E:/Workplace/game/o4mod/rewrite/reports/bundled-fonts-20261020/browser';
const base=process.env.EW4_TEST_URL||'http://127.0.0.1:5185';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1136,height:640}}),report={checks:0,errors:[],missing:[],screenshots:[],countries:[],fontRequests:[],platformFonts:[]};
  const check=(value,label)=>{assert.ok(value,label);report.checks++;};
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  page.on('request',r=>{if(r.resourceType()==='font')report.fontRequests.push(r.url());});
  const shot=async name=>{await page.screenshot({path:folder+'/'+name+'.png'});report.screenshots.push(name);};
  const ready=async()=>page.waitForFunction(()=>typeof originalTextRenderer!=='undefined'&&!!originalTextRenderer&&!!catalog);
  try{
    await page.goto(base+'/');await ready();
    check(await page.evaluate(()=>document.documentElement.dataset.fontsReady==='true'&&document.fonts.check('32px "EW4 UI Kai"','中文城市')),'primary common subset is ready before rendering');
    check(report.fontRequests.length>0&&report.fontRequests.length<7&&report.fontRequests.every(url=>url.startsWith(base+'/content/')&&url.endsWith('.woff2')),'startup requests only local common subsets; rare fonts are deferred');
    const cdp=await page.context().newCDPSession(page);await cdp.send('DOM.enable');await cdp.send('CSS.enable');
    for(const [index,sample] of ['中文城市 LONDON 123','繁體龘靐齉','𠀀𠮷','𰀀𱍐𲎰','Русский Ελληνικά','日本語 한국어','العربية','★→'].entries()){
      await page.evaluate(sample=>Ew4OriginalUI.prepareText(sample),sample);
      await page.evaluate(async({sample,index})=>{const span=document.createElement('span');span.className='original-text';span.id='font-probe-'+index;span.textContent=sample;span.style.cssText='display:block;position:fixed;left:0;top:0;font-size:32px;line-height:1;z-index:99999';document.body.append(span);await new Promise(requestAnimationFrame);span.getBoundingClientRect();},{sample,index});
      const {root}=await cdp.send('DOM.getDocument'),{nodeId}=await cdp.send('DOM.querySelector',{nodeId:root.nodeId,selector:'#font-probe-'+index});
      const {fonts}=await cdp.send('CSS.getPlatformFontsForNode',{nodeId});report.platformFonts.push({sample,fonts});
      check(fonts.length>0&&fonts.every(f=>f.isCustomFont),'all actual glyphs use packaged font faces: '+sample);
      await page.locator('#font-probe-'+index).evaluate(e=>e.remove());
    }
    const rare=await page.evaluate(()=>['龘','𠮷','𰀀','𲎰'].map(char=>{
      const c=originalTextRenderer.element(char,'font_text_4_cn_hd').firstChild,p=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
      return {char,supported:Ew4OriginalUI.hasGlyph(char.codePointAt(0)),ink:p.filter((v,i)=>i%4===3&&v>100).length,image:c.toDataURL()};
    }));
    check(rare.every(g=>g.supported&&g.ink>30)&&new Set(rare.map(g=>g.image)).size===rare.length,'rare Han glyphs render distinct nonblank pixels, including supplementary planes');
    check(await page.evaluate(()=>Ew4OriginalUI.displayText(String.fromCodePoint(0x10ffff))==='\ufffd'&&Ew4OriginalUI.displayText('e\u0301')==='é'),'unsupported text is explicit and NFC normalization is stable');
    await shot('after-main');
    report.renderer=await page.evaluate(()=>{
      const renderer=originalTextRenderer;
      const a=renderer.element('伦敦 LONDON','font_city_labels_hd'),other=new Ew4OriginalUI.TextRenderer({fonts:{font_city_labels_hd:{height:26,glyphs:{}}}});other.measure=renderer.measure;
      const b=other.element('伦敦 LONDON','font_city_labels_hd'),canvas=a.firstChild,p=canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;
      let white=0,black=0;for(let i=0;i<p.length;i+=4)if(p[i+3]>160){if(p[i]>240&&p[i+1]>240&&p[i+2]>240)white++;if(p[i]<30&&p[i+1]<30&&p[i+2]<30)black++;}
      const long=renderer.element('奥斯曼土耳其帝国','font_text_2_cn_hd','#373737',84,'left',{size:11.5,singleLine:true});
      const multi=renderer.layout('第一行\n第二行文字换行测试','font_text_2_cn_hd',50);
      return {white,black,equal:canvas.toDataURL()===b.firstChild.toDataURL(),long:{size:Number(long.dataset.size),width:parseFloat(long.firstChild.style.width),height:parseFloat(long.firstChild.style.height)},multi:multi.lines.length};
    });
    check(report.renderer.white>50&&report.renderer.black>30,'Chinese/English city names retain separate white fill and black outline');
    check(report.renderer.equal,'city names use bundled fonts independently of the original atlas');
    check(report.renderer.long.width===84&&report.renderer.long.size<=11.5&&report.renderer.long.height<18,'long names fit one line without stretching or exceeding the requested width');
    check(report.renderer.multi>2,'descriptions retain explicit and automatic line breaks');
    await page.locator('#main-settings').click();await page.waitForTimeout(260);await shot('after-settings');
    check(await page.locator('.settings-dialog [data-label="name_music"] .original-text').evaluate(e=>e.dataset.color==='rgb(69, 69, 67)'&&Number(e.dataset.size)===12),'settings labels inherit dark foreground and compact CSS size');
    await page.locator('#close-settings').click();
    for(const viewport of [{width:1136,height:640},{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(viewport);await page.evaluate(()=>showMain());
      await page.locator('[data-mode="2"]').click();await page.locator('.scenario-entry[data-scenario]').first().click();await page.waitForTimeout(750);
      await page.waitForFunction(()=>document.querySelectorAll('#country-list .country-entry').length>=6);
      const rows=await page.locator('#country-list .country-entry').evaluateAll(rows=>rows.map(row=>{
        const c=row.querySelector('canvas'),r=c.getBoundingClientRect(),b=row.getBoundingClientRect(),pixels=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
        let dark=0,white=0;for(let i=0;i<pixels.length;i+=4)if(pixels[i+3]>160){if(pixels[i]<90&&pixels[i+1]<90&&pixels[i+2]<90)dark++;if(pixels[i]>230&&pixels[i+1]>230&&pixels[i+2]>230)white++;}
        return {name:row.getAttribute('aria-label'),dark,white,inside:r.left>=b.left&&r.right<=Math.min(b.right,document.getElementById('menu-stage').getBoundingClientRect().right)+1,ratio:(r.width/r.height)/(c.width/c.height),size:Number(c.parentElement.dataset.size)};
      }));
      check(rows.length>=6,'countries populated at '+viewport.width);
      check(rows.every(r=>r.dark>20&&r.white===0),'country labels have dark ink at '+viewport.width);
      check(rows.every(r=>r.inside&&Math.abs(r.ratio-1)<.02),'country labels contained and proportionate at '+viewport.width);
      check(await page.locator('#countries h1 .original-text').evaluate(e=>e.dataset.color==='rgb(255, 255, 255)'&&Number(e.dataset.size)===17),'country header retains light title style at '+viewport.width);
      report.countries.push({viewport,rows});await shot('after-countries-'+viewport.width);
      await page.locator('#country-list .country-entry').nth(1).click();
      check(await page.locator('#country-list .country-entry').nth(1).getAttribute('aria-pressed')==='true','country selection remains interactive at '+viewport.width);
    }
    await page.setViewportSize({width:1136,height:640});await page.evaluate(()=>showMain());await page.locator('#headquarters').click();
    await page.waitForFunction(()=>!!menus.data);await page.waitForTimeout(100);await shot('after-headquarters');
    await page.locator('#hq-shop').click();await page.waitForTimeout(260);await shot('after-shop');
    await page.locator('.native-item:not(:disabled)').first().click();await page.waitForTimeout(260);await shot('after-shop-selected');
    check(await page.locator('[data-native-id="btn_buy"] canvas').evaluate(c=>{
      const p=c.getContext('2d').getImageData(0,0,c.width,c.height).data;let left=c.width,right=0;
      for(let i=0;i<p.length;i+=4)if(p[i+3]>150){const x=i/4%c.width;left=Math.min(left,x);right=Math.max(right,x);}
      return Math.abs((left+right)/2-c.width/2)<3;
    }),'purchase lettering is centered within its button artwork');
    const native=await page.locator('.native-caption').evaluateAll(nodes=>nodes.filter(n=>n.getClientRects().length&&n.querySelector('canvas')).map(n=>{
      const c=n.querySelector('canvas'),r=c.getBoundingClientRect(),b=n.getBoundingClientRect();return {id:n.dataset.nativeId,name:n.getAttribute('aria-label'),width:r.width<=b.width+1,height:r.height<=b.height+1};
    }));report.native=native;
    check(native.length>=5&&native.every(n=>n.width&&n.height),'native shop captions fit original layout control dimensions');
    await page.locator('#close-detail').click();await page.evaluate(()=>showMain());await page.locator('#mod-open').click();await page.waitForTimeout(300);await shot('after-mod-manager');
    check(await page.locator('#mod-manager .original-text').first().evaluate(e=>e.dataset.color===getComputedStyle(e.parentElement).color),'dynamic Mod manager labels use their control foreground');
    check(report.errors.length===0,'no browser exceptions: '+report.errors.join(';'));
    check(report.missing.length===0,'no missing resources: '+JSON.stringify(report.missing));
  }finally{await fs.writeFile(folder+'/results.json',JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors,missing:report.missing,screenshots:report.screenshots.length}));
})().catch(e=>{console.error(e);process.exitCode=1;});
