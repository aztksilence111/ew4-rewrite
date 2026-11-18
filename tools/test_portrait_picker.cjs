'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
let checks=0;
const check=(yes,label)=>{assert.ok(yes,label);checks++;};
class Element{
  constructor(){this.children=[];this.attrs={};this.value='';this.hidden=false;this.dataset={};this.style={};this.classList={toggle(){}};}
  append(...nodes){for(const node of nodes){node.parent=this;this.children.push(node);}}
  replaceChildren(...nodes){this.children=nodes;}
  add(node){this.append(node);}
  addEventListener(){}
  setAttribute(key,value){this.attrs[key]=value;}
  querySelector(){return new Element();}
  querySelectorAll(){return [];}
  focus(){}
  remove(){if(this.parent)this.parent.children=this.parent.children.filter(n=>n!==this);}
  get offsetWidth(){return 260;}
  setPointerCapture(id){this.pointer=id;}
  hasPointerCapture(id){return this.pointer===id;}
  releasePointerCapture(){this.pointer=null;}
}
const nodes=new Map(),element=id=>{if(!nodes.has(id))nodes.set(id,new Element());return nodes.get(id);};
const document={getElementById:element,createElement:()=>new Element(),querySelectorAll:()=>[],activeElement:new Element()};
let now=0,frameId=0;const frames=new Map(),tick=ms=>{now+=ms;const pending=[...frames.values()];frames.clear();pending.forEach(fn=>fn(now));},finish=()=>{for(let i=0;frames.size&&i<60;i++)tick(16);check(!frames.size,'animation finishes within one second');};
const context=vm.createContext({document,Option:class extends Element{constructor(text,value){super();this.textContent=text;this.value=value;}},window:{},setTimeout,clearTimeout,performance:{now:()=>now},requestAnimationFrame:fn=>{frames.set(++frameId,fn);return frameId;},cancelAnimationFrame:id=>frames.delete(id)});
for(const file of ['portrait-gallery.js','menus.js'])vm.runInContext(fs.readFileSync('rewrite/Ew4.Web/wwwroot/'+file,'utf8'),context);
const row={id:'general',name:'将领',image:'general.png',country:'fra',rating:7,owned:true,custom:true,selected:'custom',
  skins:[{id:'low',name:'常服',image:'rewrite/skins/low.png',unlocked:true,automatic:true,priority:1},{id:'unlocked',name:'远征服',image:'rewrite/skins/one.png',unlocked:true,automatic:true,priority:20},{id:'locked',name:'纪念服',unlocked:false,automatic:true,priority:999,unlockDescription:'完成任务后解锁'}]};
let requests=0,updates=0;
const library=new context.Ew4PortraitGallery.Library({countryName:id=>id,original:name=>'/original/'+name,changed(){},updated(){updates++;}});
library.refresh=async()=>{library.entries=[row];return {};};
library.request=async command=>{requests++;row.selected=command.offer==='original'?'':command.offer==='automatic'?null:command.offer;return {};};
const labels=()=>element('portrait-choices').children.map(n=>n.children.find(c=>c.textContent)?.textContent);
const browse=id=>{for(let i=0;library.skinDraft!==id&&i<library.skinCandidates.length;i++)library.moveSkin(1);finish();check(library.skinDraft===id,'can browse '+id);};
(async()=>{
  await library.openSkins(row.id);
  check(library.hasSkins(row.id)&&!library.hasSkins('vanilla'),'only configured commanders have skins');
  check(element('portrait-gallery-title').textContent==='将领皮肤','detail opens skin picker');
  check(element('portrait-requirements').hidden&&element('portrait-upload-actions').hidden&&element('portrait-remove').hidden,'skin picker hides all upload controls');
  check(element('portrait-choices').hidden&&library.skinCandidates.some(s=>s.id==='original')&&!library.skinCandidates.some(s=>s.id==='custom'),'skin carousel excludes uploaded avatar and old vertical list');
  check(element('skin-track').children.length===5,'virtual strip preloads both adjacent skins for seamless wrapping');
  const caption=element('skin-track').children.find(n=>n.className==='skin-card skin-current').children[1];
  check(caption.children[0].className==='skin-nameplate-art'&&caption.children[1].className==='skin-nameplate-text','nameplate image and name text are independent layers');
  browse('unlocked');check(row.selected==='custom'&&requests===0,'browsing does not apply skin or replace uploaded avatar');
  library.close();await library.openSkins(row.id);check(library.skinDraft==='original'&&requests===0,'closing without confirmation discards draft');
  browse('locked');check(element('skin-confirm').disabled&&element('portrait-skin-status').textContent==='完成任务后解锁','locked skin explains unlock condition and disables confirmation');
  check(await library.confirmSkin()===false&&requests===0,'locked skin cannot be confirmed');
  check(!element('skin-next').disabled&&!element('skin-prev').disabled,'locked preview can still be browsed past');
  const unreadable=new Proxy({}, {get(){throw Error('skin picker read an upload');}});
  await library.readFile(unreadable);
  for(const command of [{op:'upload',commander:row.id},{op:'remove',commander:row.id},{op:'select',commander:row.id,offer:'custom'},{op:'select',commander:row.id,offer:'locked'},{op:'select',commander:'other',offer:'unlocked'}])check(await library.mutate(command)===false,'skin picker rejects unrelated avatar operation');
  check(requests===0,'late upload callbacks and invalid skin operations never reach backend');
  check(await library.restoreDefault()===false&&requests===0,'gallery restore cannot be called from Mod skin picker');
  browse('unlocked');check(await library.confirmSkin(),'confirmation applies browsed skin');
  check(row.selected==='unlocked'&&requests===1&&element('skin-confirm').textContent==='当前使用','one explicit confirmation applies exactly one choice');
  check(await library.confirmSkin()===false&&requests===1,'current skin is not saved twice');
  const automatic=library.skinCandidates.find(s=>s.id==='automatic');check(automatic.src.endsWith('/skins/one.png'),'automatic preview uses highest unlocked priority, excluding locked skins');
  const strip=element('skin-window'),pointer=(x,y)=>({button:0,pointerId:1,clientX:x,clientY:y,preventDefault(){}}),beforeSwipe=library.skinDraft;
  const originalPosition=library.skinPosition,originalCard=library.skinCards.get(Math.round(originalPosition));
  strip.onpointerdown(pointer(160,100));tick(80);strip.onpointermove(pointer(80,103));tick(16);
  check(Math.abs(library.skinPosition-originalPosition-80/232)<.001,'drag tracks pointer displacement one to one');
  check(library.skinCards.get(Math.round(originalPosition))===originalCard&&Number(originalCard.style.opacity)<1,'drag retains image nodes and continuously fades outgoing card');
  check(element('skin-confirm').disabled&&await library.confirmSkin()===false,'cannot save a card mid drag');
  strip.onpointerup(pointer(80,103));const releasePosition=library.skinPosition;tick(16);
  check(library.skinPosition>releasePosition&&library.skinPosition<library.skinTarget,'release eases forward instead of jumping');finish();
  check(library.skinDraft!==beforeSwipe&&requests===1,'horizontal swipe changes preview without saving');
  const afterSwipe=library.skinDraft;strip.onpointerdown(pointer(160,100));tick(16);strip.onpointermove(pointer(157,180));strip.onpointermove(pointer(30,190));strip.onpointerup(pointer(30,190));finish();
  check(library.skinDraft===afterSwipe,'vertical gestures do not change skin');
  strip.onpointerdown(pointer(160,100));tick(16);strip.onpointermove(pointer(80,100));strip.onpointercancel(pointer(80,100));finish();check(library.skinDraft===afterSwipe,'cancelled swipe leaves draft unchanged');
  const stationary=library.skinPosition;
  strip.onpointerdown(pointer(160,100));tick(200);strip.onpointermove(pointer(140,100));tick(16);strip.onpointerup(pointer(140,100));finish();check(library.skinPosition===stationary,'short slow drag springs back');
  strip.onpointerdown(pointer(160,100));tick(20);strip.onpointermove(pointer(140,100));strip.onpointerup(pointer(140,100));finish();check(library.skinPosition===stationary+1,'short quick flick advances with velocity');
  const fastStart=library.skinPosition;strip.onpointerdown(pointer(250,100));tick(16);strip.onpointermove(pointer(30,100));strip.onpointerup(pointer(30,100));finish();check(library.skinPosition===fastStart+1,'quick swipe within one card does not skip a second skin');
  const flicked=library.skinPosition;strip.onpointerdown(pointer(160,100));tick(20);strip.onpointermove(pointer(140,100));tick(200);strip.onpointerup(pointer(140,100));finish();check(library.skinPosition===flicked,'holding after short flick discards stale velocity');
  library.moveSkin(1);tick(32);const interrupted=library.skinPosition;strip.onpointerdown(pointer(160,100));check(library.skinPosition===interrupted&&!library.skinMotion,'new touch interrupts easing without jumping');strip.onpointercancel(pointer(160,100));finish();
  const sequence=library.skinTarget;library.moveSkin(1);tick(16);library.moveSkin(1);library.moveSkin(-1);finish();check(library.skinPosition===sequence+1&&element('skin-track').children.length===5,'rapid arrow reversals settle and keep bounded image nodes');
  library.moveSkin(1);tick(16);library.close();check(!frames.size&&!library.skinMotion,'closing cancels pending animation');await library.openSkins(row.id);browse(afterSwipe);
  library.working=true;library.setBusy(true);library.moveSkin(1);check(library.skinDraft===afterSwipe&&element('skin-next').disabled&&await library.confirmSkin()===false,'saving blocks further navigation and duplicate confirmation');library.working=false;library.setBusy(false);
  for(const offer of ['original','automatic']){browse(offer);check(await library.confirmSkin(),'original/automatic selection confirms successfully');}
  check(row.selected==null&&updates===0&&element('portrait-message').textContent.includes('皮肤已切换'),'skin selection refreshes appearance without rebuilding the owning view');
  library.close();await library.readFile(unreadable);await library.open();
  check(!element('portrait-requirements').hidden&&!element('portrait-upload-actions').hidden&&!element('portrait-remove').hidden,'settings gallery restores upload tools');
  check(labels().includes('我的上传')&&labels().includes('原始头像'),'uploaded avatar stays available in settings');
  check(await library.mutate({op:'select',commander:row.id,offer:'custom'})&&updates===1,'settings gallery still refreshes its owning view');
  check(!element('portrait-restore').disabled&&await library.restoreDefault(),'gallery provides restore for custom and manual skin overrides');
  check(row.selected==null&&row.custom&&element('portrait-restore').disabled,'restore removes override, follows Mod defaults and retains upload');
  const restoredRequests=requests;check(await library.restoreDefault()===false&&requests===restoredRequests,'already default appearance is not saved again');
  row.selected='unlocked';await library.refresh();library.render();check(await library.restoreDefault()&&row.selected==null,'manually selected Mod skin can also return to default automatic rule');
  check(['PNG','JPG','WebP','10 MiB','4096','512'].every(s=>element('portrait-requirements').textContent.includes(s)),'upload format and dimensions retained');
  check(!element('portrait-requirements').textContent.includes('合法')&&!element('portrait-upload').title.includes('合法'),'removed sentence is absent from text and tooltip');
  const menu=Object.create(context.Ew4Menus.Menus.prototype),buttons=[];
  menu.api={portraits:library,detail:()=>new Element(),button:(label,callback,parent)=>{const n=new Element();n.textContent=label;n.onclick=callback;parent.append(n);buttons.push(n);return n;},uiImage:()=>new Element()};
  menu.presentedCommander=d=>d;menu.abilityDefinitions=()=>[];menu.card=()=>new Element();menu.lettering=()=>new Element();menu.progressionBadge=()=>new Element();menu.text=key=>key;
  menu.data={items:[]};context.Ew4SkillUI={layout(){}};
  menu.commanderInfo({...row,skills:[]},null);
  check(buttons.some(n=>n.className==='commander-skin-button'),'configured Mod commander gets skin button');
  buttons.length=0;menu.commanderInfo({...row,id:'vanilla',skills:[]},null);
  check(!buttons.some(n=>n.className==='commander-skin-button'),'vanilla and unconfigured Mod commanders hide skin button');
  context.matchMedia=()=>({matches:true});library.moveSkin(1);check(!frames.size&&!library.skinMotion&&library.skinPosition===library.skinTarget,'reduced motion uses immediate selection without a queued animation');
  row.skins=[];await library.openSkins(row.id);
  check(!library.skinCandidates.length&&element('skin-confirm').disabled&&element('portrait-skin-status').textContent.includes('暂无'),'skin list removed during update gives clear empty state');
  console.log(JSON.stringify({checks,errors:0}));
})().catch(e=>{console.error(e);process.exitCode=1;});
