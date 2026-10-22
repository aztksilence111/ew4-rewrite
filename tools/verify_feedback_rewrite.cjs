'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),crypto=require('node:crypto'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5363',folder=path.resolve(process.argv[3]||'build/rewrite-feedback-fix-20261022');
const profile=path.join(folder,'profile'),viewports=[['desktop',1136,640],['landscape',844,390],['portrait',390,844]];
const report={checks:0,errors:[],missing:[],screenshots:[],fixtures:'Isolated profile; synthetic victory saves and 18-card roster. No player profile or device testing.'};
const check=(value,label)=>{assert.ok(value,label);report.checks++;};
const digest=value=>crypto.createHash('sha256').update(JSON.stringify(value)).digest('hex');
async function api(route,args={}){
  const reply=await(await fetch(base+'/api/'+route+'?q='+encodeURIComponent(JSON.stringify(args)),{method:'POST'})).json();
  if(reply.ok===false||reply.error)throw Error(reply.message||reply.error||reply.code);return reply.data??reply;
}
async function main(){
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1136,height:640},hasTouch:true});
  page.on('pageerror',e=>report.errors.push(e.message));page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  const ready=()=>page.waitForFunction(()=>startupReady,null,{timeout:60000});
  const shot=async name=>{await page.waitForTimeout(300);await page.evaluate(async()=>{await Promise.all(document.getAnimations().filter(a=>a.effect.getComputedTiming().iterations!==Infinity).map(a=>a.finished.catch(()=>{})));});await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name);};
  const commerce=()=>api('commerce',{cell:-1});
  const refreshShop=async()=>{const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/api/commerce'&&JSON.parse(new URL(r.url()).searchParams.get('q')||'{}').op==='refresh');await page.locator('.shop-refresh').click();await response;await page.waitForTimeout(80);};
  try{
    await page.goto(base);await ready();
    report.fonts=await page.evaluate(async()=>{
      const renderer=originalTextRenderer;
      const native=['font_text_1_cn_hd','font_text_2_cn_hd','font_text_3_cn_hd','font_text_4_cn_hd','font_char_1_hd','font_char_2_hd','font_char_3_hd','font_char_4_hd'].map(font=>{
        const image=renderer.element('0',font,'#123456').firstChild,expected=document.createElement('canvas');expected.width=image.width;expected.height=image.height;
        const draw=expected.getContext('2d'),[x,y,w,h,dx,dy]=renderer.fonts[font].glyphs[48];
        draw.drawImage(renderer.sheet(font),x,y,w,h,4-dx,4-dy,w,h);draw.globalCompositeOperation='source-in';draw.fillStyle='#123456';draw.fillRect(0,0,expected.width,expected.height);
        const pixels=image.getContext('2d').getImageData(0,0,image.width,image.height).data;
        return {font,exact:image.toDataURL()===expected.toDataURL(),ink:pixels.filter((p,i)=>i%4===3&&p>100).length};
      });
      await Ew4OriginalUI.prepareText('𠮷𰀀𲎰');
      const rare=[...'𠮷𰀀𲎰'].map(char=>{const c=renderer.element(char,'font_text_4_cn_hd').firstChild;return {char,ink:c.getContext('2d').getImageData(0,0,c.width,c.height).data.filter((v,i)=>i%4===3&&v>100).length};});
      const prose=renderer.prose('任务目标：保持己方部队存续。'.repeat(500),'font_text_1_cn_hd','#404040',300);
      return {native,rare,chunks:prose.children.length,maxChunkHeight:Math.max(...[...prose.querySelectorAll('canvas')].map(c=>c.height))};
    });
    for(const f of report.fonts.native)check(f.exact&&f.ink>10,'original glyph pixels and bearings '+f.font);
    check(report.fonts.rare.every(r=>r.ink>20),'rare Mod characters retain bundled fallback');
    check(report.fonts.chunks>10&&report.fonts.maxChunkHeight<200,'long event prose has bounded original-font canvases');

    await page.evaluate(()=>menus.headquarters());await page.locator('#hq-shop').click();
    report.shop=[];
    for(const [name,width,height] of viewports){
      await page.setViewportSize({width,height});await page.waitForTimeout(250);
      const geometry=await page.evaluate(()=>{
        const b=document.querySelector('.shop-refresh'),r=b.getBoundingClientRect(),bar=document.querySelector('#detail-modal .menu-bar').getBoundingClientRect(),dialog=document.querySelector('#detail-modal .dialog').getBoundingClientRect();
        return {belowTitle:r.top>=bar.bottom,inside:r.left>=dialog.left&&r.right<=dialog.right&&r.bottom<=dialog.bottom,hit:document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('button')===b};
      });
      check(geometry.belowTitle&&geometry.inside&&geometry.hit,name+' shop refresh clear and clickable');report.shop.push({name,...geometry});await shot(name+'-shop');
    }
    await page.evaluate(()=>{window.beforeShop={layout:document.getElementById('detail-content').nativeLayout,merchant:document.querySelector('[data-native-id=group_seller]'),buyer:document.querySelector('[data-native-id=grid_buyer]'),items:[...document.querySelector('[data-native-id=grid_buyer]').children]};});
    await refreshShop();
    check(await page.evaluate(()=>beforeShop.layout===document.getElementById('detail-content').nativeLayout&&beforeShop.merchant===document.querySelector('[data-native-id=group_seller]')&&beforeShop.buyer===document.querySelector('[data-native-id=grid_buyer]')&&beforeShop.items.every((item,i)=>item===beforeShop.buyer.children[i])),'refresh preserves merchant, buyer items and Layout identity');
    check(await page.locator('.shop-refresh').evaluate(e=>getComputedStyle(e).borderImageSource.includes('btn_common_blue.png')),'refresh retains visible original blue button chrome');
    check(await page.locator('[data-native-id=grid_seller] .item-quantity .original-text').first().getAttribute('aria-label')==='1','stock of one visible');
    await page.locator('[data-native-id=grid_seller] .native-item').first().click();
    const buy=page.waitForResponse(r=>r.url().includes('/api/commerce')&&JSON.parse(new URL(r.url()).searchParams.get('q')||'{}').op==='buy');
    await page.locator('[data-native-id=btn_buy]').click();await buy;
    await page.waitForFunction(()=>document.querySelector('[data-native-id=grid_seller] .item-quantity .original-text')?.getAttribute('aria-label')==='0');
    check((await commerce()).offers.find(o=>o.slot===0).remaining===0,'UI purchase decrements real stock to zero');await shot('shop-zero-stock');
    await refreshShop();check((await commerce()).offers.find(o=>o.slot===0).remaining===1,'default refresh replenishes stock');
    await page.route('**/api/commerce*',async route=>{
      const args=JSON.parse(new URL(route.request().url()).searchParams.get('q')||'{}');
      if(args.op==='refresh'){const response=await route.fetch();await new Promise(resolve=>setTimeout(resolve,250));await route.fulfill({response});}else await route.continue();
    });
    await page.locator('.shop-refresh').click();await page.locator('#close-detail').click();await page.waitForTimeout(400);
    check(await page.locator('#detail-modal').isHidden(),'late refresh response never reopens closed window');await page.unroute('**/api/commerce*');

    await page.evaluate(async()=>{await menus.headquarters();if(!menus.data.roster.length)await menus.updateAccount({op:'recruit',commander:menus.data.offers[0][0]});await start('conquest1','fra');const unit=state.units.find(u=>u.owner===state.playerFaction&&!u.commanderId&&state.cells[u.cell].owner===u.owner);await menus.deployGeneral(unit.id,state.revision);});
    report.deployment=[];
    for(const [name,width,height] of viewports){
      await page.setViewportSize({width,height});
      await page.evaluate(()=>{const held=menus.data.roster[0];menus.data.roster=menus.data.definitions.slice(0,18).map((d,i)=>({...held,id:'feedback.fixture.'+i,definitionId:d.id,locked:false}));menus.rosterSelected=null;document.getElementById('hq-roster').scrollLeft=0;menus.drawRoster();});
      const g=await page.locator('#hq-roster').evaluate(e=>{const r=e.getBoundingClientRect();return {x:r.left+250*r.width/e.offsetWidth,y:r.top+45*r.width/e.offsetWidth,scale:r.width/e.offsetWidth};});
      await page.mouse.move(g.x,g.y);await page.mouse.down();await page.mouse.move(g.x-180*g.scale,g.y,{steps:12});await page.mouse.up();
      const mouse=await page.locator('#hq-roster').evaluate(e=>e.scrollLeft);check(mouse>100,name+' roster mouse scroll');
      await page.locator('#hq-roster').evaluate(e=>{e.scrollLeft=0;});const cdp=await page.context().newCDPSession(page);
      await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:g.x,y:g.y}]});
      for(let i=1;i<=10;i++)await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:g.x-i*14*g.scale,y:g.y}]});
      await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});await cdp.detach();
      const touch=await page.locator('#hq-roster').evaluate(e=>e.scrollLeft);check(touch>100,name+' roster touch scroll');
      const animation=await page.evaluate(()=>{document.getElementById('hq-roster').scrollLeft=0;menus.rosterSelected=null;menus.drawRoster();const c=document.querySelector('#hq-roster .commander-open');c.click();if(c.closest('.commander-card').getAttribute('aria-pressed')!=='true')c.click();const button=c.closest('.commander-card').querySelector('.deploy-card-confirm');return {name:getComputedStyle(button).animationName,active:button.getAnimations().length};});
      check(animation.name==='deploy-popup'&&animation.active>0,name+' deployment popup animates');report.deployment.push({name,mouse,touch,animation});await shot(name+'-deployment');
    }
    await page.evaluate(async()=>{menus.closeRoster();await showMain();});await page.setViewportSize({width:1136,height:640});
    await page.evaluate(()=>start('campaign1_01',null));
    const opening=await page.evaluate(()=>({intro:stageIntroOpen,dialogue:state.dialogue,revision:state.revision,hidden:document.getElementById('dialogue').hidden}));
    check(opening.intro&&opening.dialogue&&opening.hidden,'campaign opens original briefing before pending dialogue');
    for(const [name,width,height] of viewports){await page.setViewportSize({width,height});await shot(name+'-stage-intro');const r=await page.locator('#detail-modal .dialog').boundingBox();check(r.x>=0&&r.y>=0&&r.x+r.width<=width+1&&r.y+r.height<=height+1,name+' briefing fits viewport');}
    await page.locator('#close-detail').click();
    check(await page.evaluate(o=>!stageIntroOpen&&state.dialogue===o.dialogue&&state.revision===o.revision&&!document.getElementById('dialogue').hidden,opening),'closing briefing preserves dialogue and revision');
    await api('save',{slot:0,mode:1});
    const slot=JSON.parse(await fs.readFile(path.join(profile,'saves/mode-1/slot-0.json'),'utf8'));
    const catalog=await api('catalog'),session=JSON.parse(Buffer.from(slot.session,'base64'));
    const fixtureScores=Object.fromEntries(Object.keys(catalog.presentations).filter(id=>id!=='campaign1_01').map((id,i)=>[id,i%5+1]));
    session.payload.account.campaignStars=fixtureScores;session.payload.account.unlockedCampaigns=Object.keys(catalog.presentations);
    session.payload.account.revision++;session.payload.battleId='feedback-fix-win-'+Date.now();session.payload.battleResult=null;
    Object.assign(session.payload.world.payload,{phase:'Finished',outcome:'Victory',pendingDialogue:null,queuedDialogues:[],round:1});
    session.payload.world.sha256=digest(session.payload.world.payload);session.sha256=digest(session.payload);slot.session=Buffer.from(JSON.stringify(session)).toString('base64');
    await fs.writeFile(path.join(profile,'saves/mode-1/slot-6.json'),JSON.stringify(slot));
    const victory=await api('load',{slot:6,mode:1});check(victory.battleResult.stars===5,'synthetic real settlement awards five stars');
    check((await api('catalog')).campaignStars.campaign1_01===5,'catalog exposes persisted best stars');
    const receipts=Object.keys(JSON.parse(await fs.readFile(path.join(profile,'account.json'),'utf8')).battleResults).length;
    await api('load',{slot:6,mode:1});check(Object.keys(JSON.parse(await fs.readFile(path.join(profile,'account.json'),'utf8')).battleResults).length===receipts,'same victory receipt cannot duplicate recovery progress');
    await page.reload();await ready();report.chapters=[];
    for(const [viewport,width,height] of viewports){
      await page.setViewportSize({width,height});
      for(let chapter=1;chapter<=6;chapter++){
        await page.evaluate(id=>menus.openChapter(catalog.chapters.find(c=>c.id===id)),chapter);
        const rows=await page.evaluate(()=>[...document.querySelectorAll('#campaign-battles .country-entry')].map(row=>({id:row.dataset.campaign,expected:catalog.campaignStars[row.dataset.campaign],count:row.querySelectorAll('.campaign-stars img').length,left:row.querySelector('.campaign-stars img')?.style.left,color:getComputedStyle(row).backgroundImage})));
        check(rows.length>0&&rows.every(r=>r.count===r.expected&&r.left==='4px'),viewport+' chapter '+chapter+' every campaign score in original strip');
        if(viewport==='desktop')report.chapters.push({chapter,rows:rows.length,image:rows[0].color});
        await shot(viewport+'-chapter-'+chapter);
      }
    }
    report.maps=await page.evaluate(()=>{
      const results=[],previous=menus.battle,draw=menus.api.menuArt.drawStrategic.bind(menus.api.menuArt);let args;
      menus.api.menuArt.drawStrategic=(...a)=>{args=a.slice(2);return draw(...a);};
      try{for(const p of Object.values(catalog.presentations)){menus.battle=p;menus.draw(900);const canvas=document.getElementById('campaign-map'),pixels=canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data;results.push({id:p.id,scale:args[0],textureScale:args[3],ink:pixels.filter((v,i)=>i%4===3&&v>0).length,animation:menus.api.libraries.get('strategicmap').byName.has(manifest.strategic[p.id]?.name)});}}
      finally{menus.api.menuArt.drawStrategic=draw;menus.battle=previous;}
      return results;
    });
    for(const map of report.maps)check(map.textureScale===.8&&map.ink>10000&&map.animation,'native backdrop and animation available '+map.id);
    const original=await fs.readFile(path.join(profile,'account.json'));
    const form=new FormData();form.set('file',new Blob([await fs.readFile(path.join(folder,'feedback-config.ew4mod'))]),'feedback-config.ew4mod');
    check((await fetch(base+'/api/mod-import',{method:'POST',body:form})).ok,'author example package uploads');
    let status;
    for(let i=0;i<250;i++){status=await api('mods');if(status.import.status!=='working')break;await new Promise(r=>setTimeout(r,100));}
    const mod=status.mods.find(m=>m.id==='feedback-config');check(!!mod,'example passes actual Mod import');
    await api('mods',{op:'activate',key:mod.key});await page.reload();await ready();
    const hq=await api('headquarters');check(hq.medals===13579&&hq.badges===246&&hq.roster.length===0,'mod initial assets and roster isolated');
    await page.evaluate(()=>start('campaign1_01',null));
    for(const [name,width,height] of viewports){await page.setViewportSize({width,height});await shot(name+'-mod-briefing');check(await page.locator('[data-layout=form_mod_stageintro] [data-native-id=objectives]').getAttribute('aria-label')==='任务目标',name+' objectives container has accessible label');check(await page.locator('[data-native-id=objectives] .original-text').getAttribute('aria-label')==='保持己方存续，坚守至第3回合。',name+' configured actual goal description visible');
      const geometry=await page.evaluate(()=>{const l=menus.introLayout,r=l.body.parentElement.getBoundingClientRect(),base=Number(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale'));return {scale:r.width/l.width,expected:Math.min(base,(innerWidth-24)/l.width,(innerHeight-24)/l.height)};});check(Math.abs(geometry.scale-geometry.expected)<.01,name+' briefing responds to current viewport scale');
      const colors=await page.evaluate(()=>{const pixels=ctx.getImageData(0,0,canvas.width,canvas.height).data,colors=new Set();for(let i=0;i<pixels.length;i+=4096)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);return colors.size;});check(colors>20,name+' battlefield behind briefing is rendered');}
    await page.locator('[data-native-id=continue]').click();check(await page.evaluate(()=>!stageIntroOpen&&!document.getElementById('dialogue').hidden),'custom close binding resumes dialogue');
    await page.evaluate(async()=>{await showMain();await menus.headquarters();await menus.shop();});
    check(await page.locator('.shop-refresh .original-text').getAttribute('aria-label')==='2','finite shop refresh counter visible');await refreshShop();
    check(await page.locator('.shop-refresh .original-text').getAttribute('aria-label')==='1','refresh counter decrements');
    check(await page.locator('[data-native-id=grid_seller] .item-quantity .original-text').allTextContents().then(a=>a.length===2),'configured shop slots render');await shot('mod-limited-stock');
    await page.locator('#close-detail').click();
    await page.evaluate(()=>{menus.recruiting=true;menus.rating=0;menus.drawRoster();});
    const academy=page.waitForResponse(r=>r.url().includes('/api/commander')&&JSON.parse(new URL(r.url()).searchParams.get('q')||'{}').op==='refresh');
    await page.locator('#hq-refresh').click();await academy;
    await page.waitForFunction(()=>document.getElementById('hq-refresh').title==='剩余 0 次');check(true,'academy finite refresh counter updates');
    await api('mods',{op:'activate',key:null});check(original.equals(await fs.readFile(path.join(profile,'account.json'))),'mod operations preserve builtin account bytes');
    await page.reload();await ready();
    check(report.errors.length===0&&report.missing.length===0,'no browser exceptions or missing resources');
  }catch(error){report.failure=error.message;process.exitCode=1;}
  finally{await browser.close();await fs.writeFile(path.join(folder,'browser.json'),JSON.stringify(report,null,2));}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,maps:report.maps?.length,chapters:report.chapters,screenshots:report.screenshots.length,failure:report.failure}));
}
main().catch(error=>{console.error(error);process.exitCode=1;});
