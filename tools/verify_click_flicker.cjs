'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path');
const {promisify}=require('node:util'),execFile=promisify(require('node:child_process').execFile);
const {createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root=path.resolve(__dirname,'..'),base=process.argv[2]||'http://127.0.0.1:5417';
const folder=path.join(root,'rewrite/reports/click-flicker-20261023');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:[],errors:[],missing:[],screenshots:[],scope:'Desktop Edge and mobile-sized Chromium WebView simulation; no physical Android device'};
  const check=(pass,label)=>{assert.ok(pass,label);report.checks.push(label);console.log(label);};
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('response',r=>{if(r.status()>=400)report.missing.push([r.status(),r.url()]);});
  const ready=()=>page.waitForFunction(()=>startupReady,null,{timeout:60000});
  const shot=async name=>{await page.screenshot({path:path.join(folder,name+'.png')});report.screenshots.push(name+'.png');};
  const api=async(route,data={})=>(await page.request.post(base+'/api/'+route,{data})).json();
  const activate=async key=>{const r=await api('mods',{op:'activate',key});assert.equal(r.active,key);await page.goto(base);await ready();};
  const watch=()=>page.evaluate(()=>{
    window.hudProbe?.observer.disconnect();
    const rows=[...$('resources').children];
    window.hudProbe={rows,icons:rows.map(r=>r.querySelector('img')),numbers:rows.map(r=>r.querySelector('canvas')),turn:$('turn').firstChild,mutations:[],writes:0};
    hudProbe.observer=new MutationObserver(records=>hudProbe.mutations.push(...records.map(r=>({type:r.type,attr:r.attributeName,icon:r.target.tagName==='IMG'}))));
    hudProbe.observer.observe($('resources'),{subtree:true,childList:true,attributes:true,characterData:true});
    if(!window.hudCanvasWatch){
      window.hudCanvasWatch=true;
      for(const property of ['width','height']){
        const descriptor=Object.getOwnPropertyDescriptor(HTMLCanvasElement.prototype,property);
        Object.defineProperty(HTMLCanvasElement.prototype,property,{...descriptor,set(value){if(this.closest('#resources')&&window.hudProbe)hudProbe.writes++;descriptor.set.call(this,value);}});
      }
    }
  });
  const inspect=()=>page.evaluate(()=>({
    sameRows:hudProbe.rows.every((r,i)=>r===$('resources').children[i]),
    sameIcons:hudProbe.icons.every((r,i)=>r===$('resources').children[i].querySelector('img')),
    changed:hudProbe.numbers.map((r,i)=>r!==$('resources').children[i].querySelector('canvas')),
    sameTurn:hudProbe.turn===$('turn').firstChild,mutations:hudProbe.mutations,writes:hudProbe.writes,
    values:[...document.querySelectorAll('#resources .original-text')].map(r=>r.getAttribute('aria-label')),
    authoritative:['money','industry','food'].map(k=>String(state.factions.find(f=>f.id===state.playerFaction).resources[k]))
  }));
  const unchanged=async label=>{const r=await inspect();check(r.sameRows&&r.sameIcons&&r.sameTurn&&!r.changed.some(Boolean)&&r.writes===0&&r.mutations.length===0,label);};
  const changed=async(mask,label)=>{const r=await inspect();check(r.sameRows&&r.sameIcons&&r.changed.join()===mask.join()&&r.values.join()===r.authoritative.join()&&!r.mutations.some(m=>m.icon),label);};
  try{
    await activate(null);await page.evaluate(()=>start('conquest1','fra'));await watch();
    await page.evaluate(()=>{for(let i=0;i<40;i++){busy=!busy;refreshHud();}busy=false;});
    await unchanged('40 repeated refreshes and busy transitions: zero resource DOM mutations or canvas resizes');
    await page.evaluate(async()=>{
      const cells=[...new Set([...state.units.filter(u=>u.owner===state.playerFaction).slice(0,8).map(u=>u.cell),state.cells.findIndex(c=>c.active)])];
      for(const cell of cells)await selectCell(cell,false,true);
      await installState(await rpc('action',{op:'state'}));
      window.ew4SetAppVisible(false);window.ew4SetAppVisible(true);
    });
    await unchanged('different unit/terrain selections, repeated authoritative state and foreground restore preserve resource nodes');
    await page.mouse.move(700,320);await page.mouse.down();await page.mouse.move(800,380,{steps:5});await page.mouse.up();await page.mouse.wheel(0,150);
    await page.waitForTimeout(100);await unchanged('pointer drag and zoom preserve icons and unchanged number canvases');
    await page.evaluate(async()=>{try{await act('invalid_hud_test_action');}catch(_){} });
    await unchanged('failed backend command causes no optimistic deduction or resource DOM update');
    await page.evaluate(async()=>{await rpc('save',{slot:1,mode:2});await installState(await rpc('load',{slot:1,mode:2}),true);});
    await unchanged('save/reload with identical resources preserves number canvases');
    await page.evaluate(()=>{window.hudUpgrade=null;});
    const upgrade=await page.evaluate(async()=>{
      for(const [cell,c] of state.cells.entries()){
        if(!c.active||c.owner!==state.playerFaction||!c.buildingKind)continue;
        const s=await rpc('action',{op:'select',cell,revision:state.revision});
        if(s.actionQuotes?.upgrade?.affordable){await selectCell(cell,true,true);return {cell,before:{...state.factions.find(f=>f.id===state.playerFaction).resources},cost:s.actionQuotes.upgrade.cost};}
      }
      return null;
    });
    check(upgrade,'real affordable building upgrade fixture');await watch();
    await page.evaluate(cell=>act('upgrade',{cell}),upgrade.cell);
    await changed(['money','industry','food'].map(k=>upgrade.cost[k]!==0),'real building upgrade updates only resources in the committed cost');
    await watch();await page.evaluate(()=>refreshHud());await unchanged('post-commit repeated refresh leaves all resources untouched');
    await page.evaluate(()=>Ew4OriginalUI.prepareText('龘靐齉𠮷'));
    await unchanged('loading unrelated rare-character font subsets does not redraw resource digits');
    await page.evaluate(()=>window.dispatchEvent(new Event('ew4uiscale')));
    await unchanged('same UI scale event does not redraw identical resource canvases');
    await page.evaluate(()=>{document.documentElement.style.setProperty('--ui-scale','0.5');window.dispatchEvent(new Event('ew4uiscale'));});
    const density=await inspect();check(density.sameIcons&&!density.changed.some(Boolean)&&density.writes===6,'actual density change repaints each existing numeric canvas once');
    await page.evaluate(()=>sizeUi());
    for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
      await page.setViewportSize(size);await page.waitForTimeout(150);
      check(await page.evaluate(()=>{
        const bar=document.querySelector('.battle-resources').getBoundingClientRect();
        return bar.top>=0&&bar.left>=0&&bar.right<innerWidth-25&&[...$('resources').children].every(row=>{
          const a=row.querySelector('img').getBoundingClientRect(),b=row.querySelector('canvas').getBoundingClientRect();return b.left>a.right&&b.right<=bar.right+1&&b.bottom<=bar.bottom+1;
        });
      }),'resource values fit '+size.width+'x'+size.height);
      await shot('hud-'+size.width);
    }
    await page.setViewportSize({width:1280,height:720});
    for(const id of ['pause','settings','detail-modal','loading-screen']){
      check(await page.evaluate(id=>{
        $(id).hidden=false;
        const bar=document.querySelector('.battle-resources').getBoundingClientRect(),hit=document.elementFromPoint(bar.left+10,bar.top+10);
        const covered=hit=== $(id)||$(id).contains(hit);$(id).hidden=true;return covered;
      },id),'HUD remains below '+id+' overlay');
    }
    await watch();await page.evaluate(()=>start('conquest1','fra'));
    await changed(['money','industry','food'].map(k=>upgrade.cost[k]!==0),'new session restores authoritative resources without replacing icons');
    await watch();await page.evaluate(()=>start('conquest1','fra'));
    await unchanged('restarting with identical resource values preserves all numeric canvases');
    if(process.argv.includes('--ui-only')){
      check(report.errors.length===0&&report.missing.length===0,'final UI check has no browser exceptions or missing assets');
      console.log(JSON.stringify({checks:report.checks.length,errors:report.errors,missing:report.missing}));return;
    }
    const builtin=await page.locator('#resources img').evaluateAll(nodes=>nodes.map(img=>{
      const c=document.createElement('canvas');c.width=img.naturalWidth;c.height=img.naturalHeight;c.getContext('2d').drawImage(img,0,0);return c.toDataURL();
    }));
    const events={conquest1:[
      {id:'hud.one',condition:{type:'always'},popup:{title:'HUD single resource',choices:[{id:'apply',text:'Apply',effects:[{type:'money',value:333}]}]}},
      {id:'hud.many',condition:{type:'event_completed',key:'hud.one'},popup:{title:'HUD multiple resources',choices:[{id:'apply',text:'Apply',effects:[{type:'set_money',value:999999999},{type:'set_industry',value:123456789},{type:'set_food',value:987654321}]}]}},
      {id:'hud.role',condition:{type:'event_completed',key:'hud.many'},popup:{title:'HUD controlled faction',choices:[{id:'apply',text:'Apply',effects:[{type:'faction_create',faction:'hud.rebels',name:'HUD Rebels',camp:'hud.rebels',unitVariant:'rus',flag:'rus',color:'#237A4D'},{type:'money',faction:'hud.rebels',value:123},{type:'industry',faction:'hud.rebels',value:456},{type:'food',faction:'hud.rebels',value:789},{type:'player_control',faction:'hud.rebels'}]}]}}
    ]};
    const modKeys=[];
    for(let version=1;version<=2;version++){
      const fixture=path.join(root,'build','click-flicker-fixture-'+version+'-'+Date.now());
      await fs.mkdir(path.join(fixture,'rewrite'),{recursive:true});await fs.mkdir(path.join(fixture,'client/ui/image_ui_hd'),{recursive:true});
      await fs.writeFile(path.join(fixture,'rewrite/extension.json'),JSON.stringify({schema:1,apiVersion:1,scenarioEvents:'rewrite/events.json'}));
      await fs.writeFile(path.join(fixture,'rewrite/events.json'),JSON.stringify(events));
      const icons=await page.evaluate(version=>[...document.querySelectorAll('#resources img')].map((img,i)=>{
        const c=document.createElement('canvas');c.width=img.naturalWidth;c.height=img.naturalHeight;
        c.getContext('2d').fillStyle=(version===1?['#ff0055','#00dd55','#00aaff']:['#ffff00','#ff00ff','#00ffff'])[i];c.getContext('2d').fillRect(0,0,c.width,c.height);return c.toDataURL().split(',')[1];
      }),version);
      for(const [i,key] of ['money','industry','food'].entries())await fs.writeFile(path.join(fixture,'client/ui/image_ui_hd/marker_'+key+'.png'),Buffer.from(icons[i],'base64'));
      await execFile('dotnet',[path.join(root,'build/click-flicker-tool/Ew4.ModTool.dll'),'pack',fixture,'--base='+path.join(root,'rewrite/data'),'--id=click-flicker-test','--name=HUD Fixture','--version='+version],{maxBuffer:1024*1024});
      const zip=fixture+'.zip';
      await execFile('powershell',['-NoProfile','-Command',"Compress-Archive -Path '"+fixture.replaceAll("'","''")+"/*' -DestinationPath '"+zip.replaceAll("'","''")+"'"],{maxBuffer:1024*1024});
      const response=await page.request.post(base+'/api/mod-import',{multipart:{id:'click-flicker-test',name:'HUD Fixture',file:{name:'fixture.zip',mimeType:'application/zip',buffer:await fs.readFile(zip)}}});assert(response.ok());
      let status;
      for(let i=0;i<240;i++){status=await (await page.request.get(base+'/api/mods')).json();assert.notEqual(status.import.status,'error',status.import.message);if(status.import.status==='complete')break;await page.waitForTimeout(250);}
      assert.equal(status.import.status,'complete');modKeys.push(status.import.key);
      await activate(status.import.key);await page.evaluate(()=>start('conquest1','fra'));
      const rendered=await page.locator('#resources img').evaluateAll(nodes=>nodes.map(img=>{
        const c=document.createElement('canvas');c.width=img.naturalWidth;c.height=img.naturalHeight;c.getContext('2d').drawImage(img,0,0);return c.toDataURL().split(',')[1];
      }));
      check(rendered.join()===icons.join()&&await page.locator('#resources img').first().getAttribute('src').then(src=>src.includes('/r-'+status.import.key+'/')),'Mod '+version+' uses matching versioned icon pixels, including same-name replacement');
      if(version===1){
        await watch();
        await page.evaluate(()=>act('event_choice',{offer:state.scenarioPopup.id,target:'apply'}));
        await changed([true,false,false],'backend Mod event changes only money with no action whitelist');
        await watch();
        await page.evaluate(()=>act('event_choice',{offer:state.scenarioPopup.id,target:'apply'}));
        await changed([true,true,true],'backend Mod event changes three resources to nine-digit values');
        for(const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]){
          await page.setViewportSize(size);await page.waitForTimeout(100);
          check(await page.evaluate(()=>{const entries=[...$('resources').children];return entries.every((e,i)=>{const r=e.getBoundingClientRect();return r.right<innerWidth-25&&(!i||r.left>=entries[i-1].getBoundingClientRect().right);});}),'nine-digit values fit '+size.width+'x'+size.height);
          await shot('long-values-'+size.width);
        }
        await page.setViewportSize({width:1280,height:720});await watch();
        await page.evaluate(async()=>{busy=true;try{await installState(await rpc('action',{op:'event_choice',offer:state.scenarioPopup.id,target:'apply',revision:state.revision}));}finally{busy=false;refreshHud();}});
        await changed([true,true,true],'committed controlled-faction switch displays new faction resources');
        check(await page.evaluate(()=>state.playerFaction==='hud.rebels'&&$('player-flag').title==='HUD Rebels'),'faction identity updated');
        await page.evaluate(async()=>{await rpc('save',{slot:2,mode:2});await installState(await rpc('load',{slot:2,mode:2}),true);});
        check((await inspect()).values.join()==='123,456,789','faction resources survive save/reload');
      }
    }
    check(modKeys[0]!==modKeys[1],'same Mod identity and image filenames receive a new content revision');
    await activate(null);await page.evaluate(()=>start('conquest1','fra'));
    check(await page.locator('#resources img').evaluateAll((nodes,builtin)=>nodes.every((img,i)=>{const c=document.createElement('canvas');c.width=img.naturalWidth;c.height=img.naturalHeight;c.getContext('2d').drawImage(img,0,0);return c.toDataURL()===builtin[i];}),builtin),'return to builtin restores original icon pixels');
    check(report.errors.length===0&&report.missing.length===0,'no browser exceptions or missing assets');
  }catch(error){report.failure=error.message;await shot('failure').catch(()=>{});throw error;}
  finally{await fs.writeFile(path.join(folder,process.argv.includes('--ui-only')?'ui-final.json':'browser.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks.length,errors:report.errors,missing:report.missing,screenshots:report.screenshots.length}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
