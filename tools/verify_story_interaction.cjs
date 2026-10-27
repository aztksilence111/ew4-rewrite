'use strict';
const assert=require('node:assert/strict'),path=require('node:path'),{createRequire}=require('node:module');
const {chromium}=createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
  let checks=0;const errors=[];
  const check=(value,label)=>{assert.ok(value,label);checks++;};
  try{
    for(const source of ['story-player.js','compat/story-player.js']){
      for(const [width,height] of [[1280,720],[844,390]]){
        const page=await browser.newPage({viewport:{width,height}});page.on('pageerror',e=>errors.push(e.message));
        await page.route('**/story-icons/*.svg',route=>route.fulfill({status:200,contentType:'image/svg+xml',body:'<svg xmlns="http://www.w3.org/2000/svg"/>'}));
        await page.goto('about:blank');await page.setContent('<base href="http://story.test/"><style>[hidden]{display:none!important}p{margin:0}</style>');
        await page.addStyleTag({path:path.resolve('rewrite/Ew4.Web/wwwroot/story-player.css')});
        await page.addScriptTag({path:path.resolve('rewrite/Ew4.Web/wwwroot',source)});
        await page.evaluate(()=>{
          window.calls=[];window.notices=[];window.busy=false;
          window.story={id:'run1',node:'decision',kind:'event',text:'请选择行动。',name:'参谋',actors:[],animations:[],background:null,history:[],waitMs:0,
            theme:{panel:'#0b1013',text:'#f2f1e9',accent:'#dac690',opacity:.88,dialoguePercent:26,fontSize:24},
            choices:[{id:'attack',text:'拨款进攻',available:false},{id:'defend',text:'巩固防线',available:true},{id:'negotiate',text:'派遣使者',available:true}]};
          window.player=new Ew4StoryUI.Player({prepare:()=>new Promise(resolve=>{window.finishPrepare=resolve;}),notice:message=>notices.push(message),
            act:async(op,args)=>{calls.push({op,...args});await new Promise(resolve=>{window.finishAction=resolve;});}});
          window.preparation=player.sync(story,false,true);
          player.sync(story,false,false);
        });
        await page.evaluate(async()=>{finishPrepare();await preparation;});
        const disabled=()=>page.locator('.story-choice').evaluateAll(buttons=>buttons.map(b=>b.disabled));
        check(JSON.stringify(await disabled())==='[true,false,false]',source+' '+width+' unlocks choices after delayed preparation');
        await page.locator('[data-choice="defend"]').click();
        check(await page.evaluate(()=>calls.length===1&&calls[0].target==='defend'),source+' submits available choice');
        check((await disabled()).every(Boolean),source+' locks buttons during pending submission');
        await page.evaluate(()=>player.sync(story,false,false));
        await page.evaluate(()=>player.command('story_choice','negotiate'));
        check(await page.evaluate(()=>calls.length===1),source+' sync cannot unlock duplicate submission');
        await page.evaluate(()=>finishAction());
        await page.waitForFunction(()=>!player.commandPending&&!player.busy);
        check(JSON.stringify(await disabled())==='[true,false,false]',source+' releases controls after submission');
        await page.evaluate(async()=>{story={...story,choices:story.choices.map(c=>({...c,available:c.id==='negotiate'}))};await player.sync(story,false,false);});
        check(JSON.stringify(await disabled())==='[true,true,false]',source+' same-node eligibility updates');
        const layout=await page.evaluate(()=>{
          let reads=0;const rect=player.text.getBoundingClientRect.bind(player.text);player.text.getBoundingClientRect=()=>{reads++;return rect();};
          const original=player.text.textContent,metrics=player.metrics;
          for(let i=0;i<30;i++)player.paginate('预先固定的对话区域 '+i);
          const noDomWrites=player.text.textContent===original,cachedGeometry=player.metrics===metrics;
          const stableReads=reads;
          let fits=true,overflow=null;
          for(const value of ['长台词。'.repeat(800),'English words and longer sentences. '.repeat(100),'\n\n首段\n\n末段。','emoji 😀 🚀 '.repeat(100)]){
            player.paginate(value);
            for(const text of player.pages){player.text.textContent=text;if(player.text.scrollHeight>player.text.clientHeight+1){fits=false;overflow={text,height:player.text.clientHeight,scroll:player.text.scrollHeight,metrics:player.metrics,font:getComputedStyle(player.text).font,widths:text.split('\n').map(s=>player.measure.measureText(s).width)};break;}}
          }
          return {stableReads,noDomWrites,cachedGeometry,fits,overflow};
        });
        check(layout.stableReads===0&&layout.noDomWrites&&layout.cachedGeometry,source+' dialogue pagination does not write or measure live layout');
        check(layout.fits,source+' fixed geometry fits Chinese, English, empty lines and Unicode pages: '+JSON.stringify(layout.overflow));
        check(await page.evaluate(()=>notices.length===0),source+' no failed actions');
        await page.close();
      }
    }
    check(!errors.length,'no browser exceptions: '+errors.join('; '));console.log(JSON.stringify({interactionChecks:checks,errors:errors.length,viewports:2,sources:2}));
  }finally{await browser.close();}
})().catch(error=>{console.error(error.message);process.exitCode=1;});
