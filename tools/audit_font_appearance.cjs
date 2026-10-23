'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),cp=require('node:child_process'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5420',folder=process.argv[3]||'build/font-recheck-20261023';
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720},deviceScaleFactor:2});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  try{
    await page.goto(base);await page.waitForFunction(()=>startupReady);
    const samples=['战役 征服 教程 指挥部 设定','升级 招募 城市 确定 取消','France Britain Russia ATTACK Defense','0123456789 151 135 3600 64-74','Il1 O0 6G 8B 2Z .,:%+-/','ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz','-0123456789 -123'];
    await page.evaluate(async samples=>{
      await Ew4OriginalUI.prepareText(samples.join(''));
      await Ew4OriginalUI.prepareText(samples.join(''),['EW4 Kai']);
      await Ew4OriginalUI.prepareCombatText('-0123456789');
      globalThis.auditFightFonts=await (await fetch('/content/fight-font.json')).json();
      await originalTextRenderer.sheet('font_fight_num_hd').decode();
      await Promise.all(Object.keys(manifest.fonts).filter(n=>manifest.fonts[n].glyphs).map(n=>new Promise((resolve,reject)=>{
        const i=originalTextRenderer.sheet(n);if(i.complete&&i.naturalWidth)resolve();else {i.onload=resolve;i.onerror=reject;}
      })));
    },samples);
    const result=await page.evaluate(samples=>{
      const canvas=document.createElement('canvas');canvas.width=1560;canvas.height=1500;
      const draw=canvas.getContext('2d');draw.fillStyle='#f5f5f3';draw.fillRect(0,0,canvas.width,canvas.height);
      const rows=[['Original PNG / 原版字图',null],['0.2.14 / 文楷用于所有字符','"EW4 Kai"'],['Correction / 楷体 + 粗衬线 + 战斗粗黑',Ew4OriginalUI.fontFamily]];
      const metrics=[];
      for(const [row,[title,family]] of rows.entries()){
        const top=20+row*495;draw.fillStyle='#555555';draw.font='24px '+Ew4OriginalUI.fontFamily;draw.textBaseline='alphabetic';draw.fillText(title,24,top+28);
        draw.strokeStyle='#cccccc';draw.beginPath();draw.moveTo(20,top+475);draw.lineTo(1540,top+475);draw.stroke();
        for(const [line,text] of samples.entries()){
          const y=top+55+line*57;let x=28;
          if(family){const face=row===2&&line===6?Ew4OriginalUI.combatFontFamily:family;draw.fillStyle=line===6?'#ffffff':'#333333';draw.font='42px '+face;draw.textBaseline='top';draw.fontKerning='none';if(line===6){draw.strokeStyle='#000000';draw.lineWidth=3.5;draw.lineJoin='round';draw.strokeText(text,x,y);}draw.fillText(text,x,y);metrics.push({family:face,line,width:draw.measureText(text).width});}
          else{
            const layer=document.createElement('canvas');layer.width=canvas.width;layer.height=canvas.height;const ink=layer.getContext('2d');
            const height=42,font=line===6?'font_fight_num_hd':line<2?'font_text_2_cn_hd':'font_char_2_hd';let missing=[];
            const definitions={...manifest.fonts,...auditFightFonts};
            for(const char of text){
              const name=[font,'font_text_1_cn_hd','font_text_2_cn_hd','font_text_3_cn_hd','font_text_4_cn_hd'].find(n=>definitions[n]?.glyphs?.[char.codePointAt(0)]);
              if(!name){missing.push(char);continue;}
              const def=definitions[name],g=def.glyphs[char.codePointAt(0)],scale=height/def.height,[sx,sy,w,h,dx,dy,advance]=g;
              ink.drawImage(originalTextRenderer.sheet(name),sx,sy,w,h,x-dx*scale,y-dy*scale,w*scale,h*scale);x+=advance*scale;
            }
            if(line!==6){ink.globalCompositeOperation='source-in';ink.fillStyle='#333333';ink.fillRect(0,0,layer.width,layer.height);}draw.drawImage(layer,0,0);
            metrics.push({family:'original',line,width:x-28,missing});
          }
        }
      }
      return {png:canvas.toDataURL('image/png'),metrics};
    },samples);
    await fs.writeFile(path.join(folder,'font-comparison.png'),Buffer.from(result.png.split(',')[1],'base64'));
    const file='rewrite/Ew4.Web/wwwroot/battlefield.js',source=await fs.readFile(file,'utf8'),previous=cp.execFileSync('git',['show','244f92a:'+file],{encoding:'utf8'});
    const mapText=s=>s.slice(s.indexOf('    mapText() {'),s.indexOf('    facilities(farms) {'));
    const normalize=s=>s.replace("Ew4OriginalUI.prepareMapText(label.textZh,!label.sea)","Ew4OriginalUI.prepareText(label.textZh,label.sea?undefined:['EW4 Serif','EW4 Kai','EW4 Han','EW4 Han Plane 2','EW4 Han Plane 3','EW4 Unicode','EW4 Unicode Supplement'])").replace('Ew4OriginalUI.mapFontFamily','Ew4OriginalUI.fontFamily');
    assert.equal(normalize(mapText(source)),mapText(previous),'map artwork and legacy Chinese map faces must be preserved');
    const cdp=await page.context().newCDPSession(page);await cdp.send('DOM.enable');await cdp.send('CSS.enable');
    const platformFonts=[];
    for(const [kind,text,combat,expected] of [['Chinese','战役征服升级',false,'UKai'],['English','ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz',false,'EW4UILatin'],['Digits','0123456789',false,'EW4UILatin'],['Mixed','攻击 Attack 151',false,'EW4UILatin'],['Combat','-0123456789',true,'EW4Combat']]){
      await page.evaluate(async({text,combat})=>{const e=document.createElement('div');e.id='appearance-probe';e.className='original-text';e.textContent=text;e.style.cssText='position:fixed;top:0;left:0;font-size:32px;z-index:99999';e.style.fontFamily=combat?Ew4OriginalUI.combatFontFamily:Ew4OriginalUI.fontFamily;document.body.append(e);await new Promise(requestAnimationFrame);e.getBoundingClientRect();},{text,combat});
      const {root}=await cdp.send('DOM.getDocument'),{nodeId}=await cdp.send('DOM.querySelector',{nodeId:root.nodeId,selector:'#appearance-probe'}),{fonts}=await cdp.send('CSS.getPlatformFontsForNode',{nodeId});
      platformFonts.push({kind,fonts});assert.ok(fonts.length&&fonts.every(f=>f.isCustomFont),'packaged fonts only: '+kind+' '+JSON.stringify(fonts));
      assert.ok(fonts.some(f=>f.postScriptName.includes(expected)),'expected face: '+kind+JSON.stringify(fonts));
      await page.locator('#appearance-probe').evaluate(e=>e.remove());
    }
    const report={samples,metrics:result.metrics,platformFonts,errors,mapLayerRendererPreserved:true,fontsReplaced:true,scope:'Visual comparison; coverage and font selection do not establish identity with original source fonts'};
    await fs.writeFile(path.join(folder,'font-audit.json'),JSON.stringify(report,null,2));
    assert.equal(errors.length,0);console.log(JSON.stringify({rows:3,samples:samples.length,scriptChecks:platformFonts.length,errors:errors.length,mapLayerRestored:true}));
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
