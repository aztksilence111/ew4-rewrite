'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs/promises'),path=require('node:path'),vm=require('node:vm'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base=process.argv[2]||'http://127.0.0.1:5547',folder=path.resolve(process.argv[3]||'build/map-views-20261025/browser');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],missing:[],cases:[],scope:'Actual desktop Edge, Canvas/Pixi and phone viewport emulation; no physical Android device.'};
  const check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const sandbox={};vm.runInNewContext(await fs.readFile('rewrite/Ew4.Web/wwwroot/map-views.js','utf8'),sandbox);
  const projection=new sandbox.Ew4MapViews.Projection(),map={},world={scenario:'fixture',factions:[
    {id:'france',country:'fra',camp:'a',color:11,relation:'allied'},
    {id:'usa',country:'usa',camp:'a',color:22,relation:'hostile'},
    {id:'britain',country:'gbr',camp:'b',color:33,relation:'allied'},
    {id:'third',country:'fra',camp:'c',color:44,relation:'hostile'}]};
  projection.sync(world,map);
  check(projection.color({owner:'france'},'camp')===projection.color({owner:'usa'},'camp'),'same camp shares color regardless of bilateral relations');
  check(projection.color({owner:'france'},'camp')!==projection.color({owner:'britain'},'camp'),'different allied camps remain distinct');
  check(projection.color({owner:'usa'},'normal')===22&&projection.color({owner:'usa'},'district')===22,'ordinary and district views preserve actual country colors');
  const first=[...projection.camps].map(([id,c])=>({id,...c}));
  check(new Set(first.map(c=>c.abbreviation)).size===first.length&&first.every(c=>/^[A-Z]+$/.test(c.abbreviation)),'unique alphabetic camp labels');
  projection.sync({...world,factions:[...world.factions].reverse()},map);
  check(JSON.stringify(first)===JSON.stringify([...projection.camps].map(([id,c])=>({id,...c}))),'camp identities survive turn order changes');
  projection.sync({...world,factions:[...world.factions,{id:'new',country:'fra',camp:'d',color:99}]},map);
  check(projection.camps.size===4&&projection.camps.get('a').color===first.find(c=>c.id==='a').color,'dynamic camp added without recoloring existing camps');
  check(projection.color({owner:null},'camp')===0,'unowned cells remain unowned');
  const configured={...world,mapColors:{campPalette:['#12345660','#FEDCBA80'],camps:{a:'#00000000',d:'#ABCDEF'}}};
  check(projection.sync(configured,map),'new color configuration signals ground invalidation');
  check(projection.color({owner:'france'},'camp')===0,'fully transparent black remains transparent');
  check(projection.color({owner:'third'},'camp')===0x60563412,'explicit color does not shift fallback palette slots');
  check(projection.color({owner:'usa'},'normal')===22,'camp configuration preserves normal country color');
  check(!projection.sync({...configured},map),'unchanged configuration preserves ground cache');
  projection.sync({...configured,factions:[...world.factions,{id:'new',country:'fra',camp:'d',color:99}]},map);
  check(projection.color({owner:'new'},'camp')===0xFFEFCDAB,'event-created camp accepts explicit opaque RGB color');
  check(projection.sync(world,map),'removing configuration invalidates ground');
  check(projection.color({owner:'france'},'camp')===first.find(c=>c.id==='a').color,'removing configuration restores original palette and alpha');
  projection.sync({...world,factions:[{id:'prototype',camp:'constructor',color:11}]},{});
  check(projection.color({owner:'prototype'},'camp')===0x60DDAA79,'prototype-like camp ID uses normal fallback palette');
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',args:['--mute-audio']});
  try{
    for(const backend of ['pixi','canvas']){
      const page=await browser.newPage({viewport:{width:1280,height:720}});
      page.on('pageerror',e=>{if(report.errors.length<5)report.errors.push(e.stack||e.message);});page.on('response',r=>{if(r.status()>=400)report.missing.push(r.url());});
      await page.goto(base+(backend==='canvas'?'/?renderer=canvas':''));await page.waitForFunction(()=>startupReady,null,{timeout:90000});
      await page.evaluate(()=>start('conquest1','fra'));
      await page.waitForFunction(()=>state&&ctx&&[...images.values()].every(i=>i.complete));
      await page.evaluate(()=>{stageIntroOpen=false;$('detail-modal').hidden=true;effects.clear();damageNumbers.clear();});
      const snapshot=()=>page.evaluate(async()=>{
        suspendRendering();effects.clear();damageNumbers.clear();render(2000);suspendRendering();
        const pixels=ctx.gpu?ctx.renderer.extract.pixels():ctx.getImageData(0,0,canvas.width,canvas.height).data;
        const digest=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',pixels))).map(v=>v.toString(16).padStart(2,'0')).join('');
        const colors=new Set();for(let i=0;i<pixels.length;i+=4*397)colors.add(pixels[i]+','+pixels[i+1]+','+pixels[i+2]);
        return{revision:state.revision,round:state.round,player:state.playerFaction,owners:state.cells.map(c=>c.owner),camps:state.factions.map(f=>[f.id,f.camp,f.relation]),pixels:digest,colors:colors.size};
      });
      const baseline=await snapshot(),hash=pixels=>pixels,beforeHash=hash(baseline.pixels);
      check(baseline.colors>30,backend+' nonblank battlefield');
      const button=page.locator('#map-mode'),options=page.locator('#map-mode-options');
      check(await button.getAttribute('aria-label')==='普通地图',backend+' defaults to normal');
      await button.click();await page.waitForTimeout(320);
      check(await options.locator('button:visible').count()===2,backend+' two alternatives');
      await page.getByRole('menuitemradio',{name:'阵营地图'}).click();
      await options.waitFor({state:'hidden'});
      const camp=await snapshot();
      check(hash(camp.pixels)!==beforeHash,backend+' camp view changes actual canvas pixels');
      check(camp.revision===baseline.revision&&JSON.stringify(camp.owners)===JSON.stringify(baseline.owners)&&JSON.stringify(camp.camps)===JSON.stringify(baseline.camps),backend+' camp view does not alter gameplay');
      await page.screenshot({path:path.join(folder,backend+'-camp-1280x720.png')});
      const groups=await page.evaluate(()=>{
        const result={};for(const f of state.factions)(result[f.camp]??=[]).push(battlefield.projection.color({owner:f.id},'camp'));
        return result;
      });
      check(Object.values(groups).every(colors=>new Set(colors).size===1),backend+' real camp grouping');
      check(await page.evaluate(()=>battlefield.projection.abbreviation(state.factions.find(f=>f.country==='fra').id))==='F',backend+' French-led original camp labeled F');
      await button.click();await page.getByRole('menuitemradio',{name:'辖区地图'}).click();await options.waitFor({state:'hidden'});
      const district=await snapshot();
      check(hash(district.pixels)!==beforeHash&&hash(district.pixels)!==hash(camp.pixels),backend+' district boundaries change actual pixels');
      check(district.revision===baseline.revision&&JSON.stringify(district.owners)===JSON.stringify(baseline.owners),backend+' district view preserves actual ownership');
      check(await page.evaluate(()=>battlefield.districtCanvases.size>0),backend+' district edges drawn');
      check(await page.evaluate(()=>{
        const owner=state.factions.find(f=>f.country==='fra').id,marker=battlefield.campMark(owner),pixels=marker.getContext('2d').getImageData(0,0,marker.width,marker.height).data;
        const shades=new Set();for(let i=0;i<pixels.length;i+=4)if(pixels[i+3]>200&&pixels[i]>90)shades.add(pixels[i]);
        return shades.size>10&&pixels.some((v,i)=>i%4===3&&v===0);
      }),backend+' camp tag uses shaded silver glyphs on transparent background');
      check(await page.evaluate(()=>{
        const prototype=Object.getPrototypeOf(battlefield),owner=state.playerFaction,unit=state.units.find(u=>u.owner===owner);
        for(const text of ['F','FRA']){
          const scene={projection:{abbreviation:()=>text},draw:{},campMarks:new Map()},mark=prototype.campMark.call(scene,owner),placements=[];
          const draw={save(){},restore(){},globalAlpha:1,drawImage(source,x,y,w,h){if(source===mark)placements.push({x,y,w,h});}};
          prototype.unitBadge.call({draw,state,time:2000,badgeOnly:false,api:{sprite(){}},healthArc:()=>null,campMark:()=>mark},unit,{x:0,y:0},{unitIndex:0},false);
          if(placements.length!==1)return false;
          const {x,y,w,h}=placements[0],pixels=mark.getContext('2d').getImageData(0,0,mark.width,mark.height).data;
          if(x< -12||y<0||x+w>12||y+h>12)return false;
          for(let py=0;py<mark.height;py++)for(let px=0;px<mark.width;px++)if(pixels[(py*mark.width+px)*4+3]>100&&Math.hypot(x+(px+.5)*w/mark.width,y+(py+.5)*h/mark.height)>12)return false;
        }
        return true;
      }),backend+' single and multiple camp letters overlap lower right within badge circle');
      await page.screenshot({path:path.join(folder,backend+'-district-1280x720.png')});
      await button.click();await page.getByRole('menuitemradio',{name:'普通地图'}).click();await options.waitFor({state:'hidden'});
      const restored=await snapshot();
      check(hash(restored.pixels)===beforeHash,backend+' normal restored pixel for pixel after switching');
      for(const name of ['阵营地图','辖区地图','普通地图']){
        if(await button.getAttribute('aria-label')!==name){await button.click();await page.getByRole('menuitemradio',{name}).click();await options.waitFor({state:'hidden'});}
        const selected=await page.evaluate(async()=>{const unit=state.units.find(u=>u.owner===state.playerFaction&&!u.constructionTurns);await selectCell(unit.cell,false,true);return selection?.cell===unit.cell;});
        check(selected,backend+' selection works in '+name);
        const zoomChecks=await page.evaluate(()=>{
          suspendRendering();const origin={...view},originalSprite=battlefield.api.sprite,originalMark=battlefield.campMark,originalArc=battlefield.healthArc;
          const leader=state.units.find(u=>u.commanderImage&&u.health>0),samples=[];
          if(!leader)throw new Error('Commander fixture missing');
          const center=Ew4Battlefield.point(leader.cell,state.width),scale=Math.max(1,Math.min(innerWidth/568,innerHeight/320,3));
          let sample;
          battlefield.api.sprite=function(library,name,...args){if(sample){if(name.startsWith('hpbar_'))sample.badges++;if(name.startsWith('mark_unit_'))sample.units++;if(library==='commandermark'&&name===leader.commanderImage+'.png')sample.portraits++;}return originalSprite.call(this,library,name,...args);};
          battlefield.campMark=function(...args){sample.camps++;return originalMark.apply(this,args);};
          battlefield.healthArc=function(...args){const arc=originalArc.apply(this,args);if(arc)sample.health++;return arc;};
          try{
            for(const [level,relative] of [['near',1],['far',.35]]){
              sample={level,camps:0,badges:0,units:0,portraits:0,health:0};Object.assign(view,center,{zoom:scale*relative});clampView();render(2000);suspendRendering();
              samples.push({...sample,badgeOnly:battlefield.badgeOnly});
            }
          }finally{sample=null;battlefield.api.sprite=originalSprite;battlefield.campMark=originalMark;battlefield.healthArc=originalArc;Object.assign(view,origin);render(2000);suspendRendering();}
          return samples;
        });
        check(!zoomChecks[0].badgeOnly&&zoomChecks[0].camps>0,backend+' near view shows camp letters in '+name);
        check(zoomChecks[1].badgeOnly&&zoomChecks[1].camps===0,backend+' distant view omits camp letters in '+name);
        check(zoomChecks.every(s=>s.badges>0&&s.units>0&&s.health>0&&s.portraits>0),backend+' badge health and commander retained at both distances in '+name);
      }
      await page.evaluate(()=>{const unit=state.units.find(u=>u.commanderImage&&u.health>0);Object.assign(view,Ew4Battlefield.point(unit.cell,state.width),{zoom:Math.max(1,Math.min(innerWidth/568,innerHeight/320,3))*.35});clampView();render(2000);suspendRendering();});
      await page.screenshot({path:path.join(folder,backend+'-far-1280x720.png')});
      await page.evaluate(()=>{selection=undefined;selected=undefined;$('selection').hidden=true;});
      for(const [width,height] of [[1280,720],[844,390],[390,844]]){
        await page.setViewportSize({width,height});await button.click();await page.waitForTimeout(320);
        const geometry=await page.evaluate(()=>{
          const r=e=>{const b=e.getBoundingClientRect();return{x:b.x,y:b.y,w:b.width,h:b.height};};
          return{pause:r($('menu-button')),main:r($('map-mode')),buttons:[...$('map-mode-options').children].map(r)};
        });
        check(Math.abs(geometry.pause.w-geometry.main.w)<.1&&Math.abs(geometry.pause.h-geometry.main.h)<.1,backend+' original pause size '+width);
        check(Math.abs(geometry.pause.x-geometry.main.x-geometry.main.w-6)<.2,backend+' exact six pixel gap '+width);
        check(geometry.buttons.every(r=>r.x>=0&&r.y>=geometry.main.y+geometry.main.h&&r.x+r.w<=width&&r.y+r.h<=height),backend+' expanded buttons in bounds '+width);
        check(geometry.buttons.every(r=>Math.abs(r.x-geometry.main.x)<.1),backend+' dropdown aligned '+width);
        await page.evaluate(()=>Ew4OriginalUI.prepareText('普通地图阵营辖区'));
        for(const control of [button,options.locator('button').nth(0),options.locator('button').nth(1)]){
          await control.hover();await page.waitForTimeout(80);
          const tip=await control.locator('.map-mode-tip').evaluate(e=>{
            const r=e.getBoundingClientRect(),style=getComputedStyle(e),labels=[...e.querySelectorAll('canvas')];
            return{visible:style.visibility==='visible',x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:r.width,height:r.height,labels:labels.map(c=>{
              const b=c.getBoundingClientRect(),pixels=c.getContext('2d').getImageData(0,0,c.width,c.height).data;
              let left=c.width,top=c.height,right=0,bottom=0;
              for(let y=0;y<c.height;y++)for(let x=0;x<c.width;x++)if(pixels[(y*c.width+x)*4+3]>50){left=Math.min(left,x);top=Math.min(top,y);right=Math.max(right,x+1);bottom=Math.max(bottom,y+1);}
              return{inside:b.left>=r.left&&b.top>=r.top&&b.right<=r.right&&b.bottom<=r.bottom,ink:right>left,width:b.width,height:b.height,dx:b.x+(left+right)/2/c.width*b.width-r.x-r.width/2,dy:b.y+(top+bottom)/2/c.height*b.height-r.y-r.height/2};
            })};
          });
          check(tip.visible&&tip.x>=0&&tip.y>=0&&tip.right<=width&&tip.bottom<=height&&tip.labels.length>0&&tip.labels.every(c=>c.inside&&c.ink&&c.width>20&&c.height>=10),backend+' readable unclipped tooltip '+await control.getAttribute('aria-label')+' '+width);
          check(tip.labels.every(c=>Math.abs(c.dx)<=1.5&&Math.abs(c.dy)<=1.5),backend+' tooltip visible glyphs centered '+await control.getAttribute('aria-label')+' '+width+' '+JSON.stringify(tip.labels));
          if(control===button)await page.screenshot({path:path.join(folder,backend+'-tooltip-main-'+width+'x'+height+'.png'),clip:{x:Math.max(0,width-220),y:0,width:Math.min(width,220),height:220}});
        }
        await page.screenshot({path:path.join(folder,backend+'-tooltip-'+width+'x'+height+'.png'),clip:{x:Math.max(0,width-220),y:0,width:Math.min(width,220),height:220}});
        await page.mouse.move(0,height-1);await snapshot();await page.screenshot({path:path.join(folder,backend+'-menu-'+width+'x'+height+'.png')});
        check(await page.evaluate(()=>ew4Back())==='handled',backend+' hardware back handles menu');
        check(await page.locator('#pause').isHidden(),backend+' back closes dropdown before pause');
        await options.waitFor({state:'hidden'});
        await page.evaluate(()=>mapViews.select('camp'));
        const originalColors=await page.evaluate(()=>state.mapColors),beforeColors=await snapshot();
        await page.evaluate(()=>{state={...state,mapColors:{campPalette:['#21DE6080'],camps:Object.fromEntries(state.factions.map(f=>[f.camp,'#DE216080']))}};render(2000);suspendRendering();});
        const customColors=await snapshot();
        check(customColors.pixels!==beforeColors.pixels,backend+' configured colors change actual cached terrain pixels '+width);
        check(customColors.revision===beforeColors.revision&&JSON.stringify(customColors.owners)===JSON.stringify(beforeColors.owners),backend+' recoloring preserves gameplay '+width);
        check(await page.evaluate(()=>state.factions.every(f=>battlefield.projection.color({owner:f.id},'camp')===0x806021DE)),backend+' explicit RGBA reaches every camp '+width);
        await page.screenshot({path:path.join(folder,backend+'-custom-colors-'+width+'x'+height+'.png')});
        const stableGround=await page.evaluate(()=>{const signature=battlefield.groundSignature;state={...state};render(2000);suspendRendering();return signature===battlefield.groundSignature;});
        check(stableGround,backend+' unchanged colors reuse ground cache '+width);
        await page.evaluate(colors=>{state={...state,mapColors:colors};render(2000);suspendRendering();},originalColors);
        check((await snapshot()).pixels===beforeColors.pixels,backend+' removed color overrides restore terrain pixels '+width);
        await page.evaluate(()=>mapViews.select('normal'));
        report.cases.push({backend,width,height,...geometry});
      }
      await button.click();await page.keyboard.press('Escape');await options.waitFor({state:'hidden'});
      check(await button.getAttribute('aria-expanded')==='false',backend+' escape closes');
      await button.click();await page.mouse.click(10,100);await options.waitFor({state:'hidden'});
      check(await button.getAttribute('aria-expanded')==='false',backend+' outside click closes');
      await page.evaluate(()=>{for(let i=0;i<7;i++)$('map-mode').click();});await page.waitForTimeout(320);
      check(await button.getAttribute('aria-expanded')==='true',backend+' rapid toggles settle open');
      await page.getByRole('menuitemradio',{name:'阵营地图'}).click();await button.click();
      check(await options.locator('button').evaluateAll(buttons=>buttons.map(b=>b.getAttribute('aria-label'))).then(names=>names.includes('普通地图')&&!names.includes('阵营地图')),backend+' reopen while closing updates alternatives');
      await page.evaluate(()=>{$('map-mode').click();mapViews.reset();});
      check(await button.getAttribute('aria-label')==='普通地图',backend+' new session reset');
      await page.close();
    }
    check(!report.errors.length&&!report.missing.length,'no browser errors or missing assets');
  }catch(error){report.failure=error.message;throw error;}
  finally{await fs.writeFile(path.join(folder,'verification.json'),JSON.stringify(report,null,2));await browser.close();}
  console.log(JSON.stringify({checks:report.checks,errors:report.errors.length,missing:report.missing.length,cases:report.cases.length}));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
