'use strict';
const $ = id => document.getElementById(id);
const canvas = $('map'), ctx = canvas.getContext('2d', {alpha: false});
let catalog, manifest, cityNames={}, state, selected, selection, busy = false, mode = 2;
let conquestPages, conquestPage=0, conquestEntering=false, conquestRender=0;
let originalTextRenderer, menuArt, imageRevision=0;
let startupReady=false,startupRunning=false,startupError='';
let effects,damageNumbers;
let originalTextQueued=false;
const originalTextChanges=new MutationObserver(()=>{
  if(!originalTextRenderer||originalTextQueued)return;originalTextQueued=true;
  requestAnimationFrame(()=>{originalTextQueued=false;originalTextRenderer.paintTree(document.body);});
});
originalTextChanges.observe(document.body,{childList:true,characterData:true,attributes:true,attributeFilter:['hidden'],subtree:true});
let view = {x: 0, y: 0, zoom: 1}, viewport = {w: 0, h: 0, dpr: 1};
const images = new Map(), libraries = new Map();
let errorTimer, aiRunning = false;
const unitNavigation=new Ew4UnitNavigation.Cycle();
let findingUnit=false;
let sessionTransition = false, aiTask = null, aiStopRequested = false;
let skipAi = false, aiFocus = null, mapBounds, finishPresentation;
const battleDialogues = [];
const presentationWaiters = new Set();
const motions = new Map(), facings = new Map(), arrivals = new Map(), combatMotions = new Map();
const deaths = new Map();
let renderRequest=0,renderDirty=true,lastRenderTime=-Infinity,appVisible=true;
function requestRender(){
  renderDirty=true;
  if(document.hidden||!appVisible)return;
  if(!renderRequest)renderRequest=requestAnimationFrame(render);
}
function suspendRendering(){cancelAnimationFrame(renderRequest);renderRequest=0;}
window.ew4SetAppVisible=visible=>{appVisible=visible;if(visible)requestRender();else suspendRendering();};
document.addEventListener('visibilitychange',()=>{if(document.hidden)suspendRendering();else requestRender();});
new MutationObserver(requestRender).observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['hidden']});
for(const event of ['pointerdown','pointermove','pointerup','wheel','input','change','click'])document.addEventListener(event,requestRender,{passive:true});
const battlefield = new Ew4Battlefield.Renderer({image,sprite,animation,libraries,get imageRevision(){return imageRevision;},get effects(){return effects;},get manifest(){return manifest;},get geographicLanguage(){return settings.geographicLanguage;},cityLabel:(name,cell,rasterScale=1)=>{
  const translated=manifest.maps[state.map].cityNames?.[cell];
  const text=translated?.name===name?translated[settings.cityLanguage]:Ew4MenuArt.cityName(name,settings.cityLanguage,cityNames);
  return originalTextRenderer.element(text,'font_city_labels_hd','#ffffff',undefined,'left',{rasterScale});
}});
const menus = new Ew4Menus.Menus({rpc,button,uiImage,countryImage,labels:labelImage,text:(value,font,color,width,align,options)=>originalTextRenderer?.element(value,font,color,width,align,options)||document.createTextNode(value),prose:(...args)=>originalTextRenderer.prose(...args),detail:detailWindow,start,loadLibrary,image,animation,libraries,notice,main:showMain,
  get manifest(){return manifest;},get catalog(){return catalog;},refreshCatalog:async()=>{catalog=await rpc('catalog');},tutorialUi,popup:popupOpen,load:(slot,mode)=>load(slot,mode),deploy:(unit,commander)=>act('deploy',{unit,commander}),act,
  refresh:async()=>{if(state&&!$('play').hidden){await installState(await rpc('action',{op:'state',revision:state.revision}));}},get menuArt(){return menuArt;},closeDetail});
let chosenScenario, chosenCountry, countryChoices = [], showGrid = false, selectedAt = 0;
const victoryUi = new Ew4VictoryUI.Results({
  ready:next=>!busy&&!skipAi&&!sessionTransition&&$('pause').hidden&&$('loading-screen').hidden&&state===next&&battlefield.state===next&&[...images.values()].every(img=>img.complete),
  get manifest(){return manifest;},detail:detailWindow,button,uiImage,notice,
  text:(...args)=>originalTextRenderer.element(...args),portrait:name=>menus.portrait(name),close:closeDetail,
  title:key=>$('detail-title').replaceChildren(labelImage(key,manifest.strings[key]||key,$('detail-title'))),
  restart:()=>start(state.scenario,catalog.scenarios.find(s=>s.id===state.scenario)?.mode===2?(state.startCountry??state.factions.find(f=>f.id===state.playerFaction).country):null,null),
  continue:async result=>{if(result.nextScenario)await start(result.nextScenario,null,null);else await showMain();},exit:showMain
});
const localMultiplayerUi = new Ew4LocalMultiplayer.LocalMultiplayer({rpc,detail:detailWindow,button,close:closeDetail,main:showMain,
  text:(...args)=>originalTextRenderer.element(...args),ready:()=>!busy&&!aiRunning&&!skipAi&&!sessionTransition&&$('pause').hidden&&$('loading-screen').hidden,
  restart:next=>start(next.scenario,next.startCountry,next.localMultiplayer.humanFactions)});
let tutorial, tutorialRect;
let settings={music:50,sound:50,speed:2,hd:true,screen:100,eventSize:100,cityLanguage:'en',geographicLanguage:'en',localMultiplayer:false,skipTurnFast:false,followAi:false};
try { settings={...settings,...JSON.parse(localStorage.getItem('ew4.settings')||'{}')}; } catch (_) {}
settings.eventSize=Number.isFinite(settings.eventSize)?Math.max(60,Math.min(180,settings.eventSize)):100;
for(const key of ['skipTurnFast','followAi'])settings[key]=settings[key]===true;
try { showGrid = localStorage.getItem('ew4.showGrid') === 'true'; } catch (_) { /* Storage can be unavailable in restricted WebViews. */ }
function sizeUi() {
  const reserve=startupReady&&!$('startup-status').hidden?Math.ceil($('startup-status').getBoundingClientRect().height)+24:0;
  document.documentElement.style.setProperty('--startup-reserve',reserve+'px');
  const scale = Math.min(innerWidth / 568, Math.max(1,innerHeight-reserve) / 320)*settings.screen/100;
  document.documentElement.style.setProperty('--ui-scale', scale);
  const battleScale=Math.min(Math.max(scale,48/27,innerHeight/300),innerWidth/420,innerHeight/180);
  document.documentElement.style.setProperty('--battle-scale', battleScale);
  const baseEventScale = Math.min(1.5, innerWidth*.84/360, innerHeight*.74/264)*settings.screen/100;
  const eventHeight=innerHeight>=innerWidth?Math.min(400,innerHeight*.74/baseEventScale):264;
  // Bound the slider itself so each step still enlarges the popup on small screens.
  const maxEventSize=Math.min(180,Math.floor(Math.min(innerWidth*.96/360,innerHeight*.92/eventHeight)/baseEventScale*100));
  const eventSize=Math.min(settings.eventSize,maxEventSize);
  const eventScale=baseEventScale*eventSize/100;
  document.documentElement.style.setProperty('--event-scale', eventScale);
  document.documentElement.style.setProperty('--event-available-height', eventHeight+'px');
  document.documentElement.style.setProperty('--settings-scale',Math.min(scale,(innerWidth-16)/362,(innerHeight-16)/340));
  $('event-size').max=maxEventSize;$('event-size').value=eventSize;
  $('event-size-value').textContent=eventSize+'%';
  clampView();
}
function labelImage(key, text, host) {
  if(!host&&key.startsWith('country_'))return originalTextRenderer.element(text,'font_text_2_cn_hd','#373737',84,'left',{size:11.5,singleLine:true});
  return host?originalTextRenderer.forElement(host,text):originalTextRenderer.element(text,'font_text_4_cn_hd','#ffffff');
}
function originalLabels() {
  for (const element of document.querySelectorAll('[data-label]')) {
    const text = element.textContent; element.setAttribute('aria-label', text);
    element.replaceChildren(labelImage(element.dataset.label, text, element));
  }
}
async function showMain() {
  if(sessionTransition)return;
  sessionTransition=true;
  const wasBusy=busy&&!aiRunning;
  try {
    await stopAi();
    busy=true;
    if(state)await rpc('leave',{revision:state.revision});
    state=undefined;selected=undefined;selection=undefined;tutorial=null;tutorialRect=null;
    battleDialogues.length=0;
    detailReturn=null;victoryUi.reset();localMultiplayerUi.sync(null);
    closeSingleAction();menus.dismissSkillTip();menus.deployment=null;
    motions.clear();arrivals.clear();deaths.clear();combatMotions.clear();
    effects?.clear();damageNumbers?.clear();finishPresentation?.();
    $('detail-modal').hidden=true;$('pause').hidden=true;$('selection').hidden=true;
    $('play').hidden = true; $('menu').hidden = false; $('main-menu').hidden = false;
    $('scenario-menu').hidden = true; $('countries').hidden = true;
    $('campaign-screen').hidden=true;$('hq-screen').hidden=true;
    $('tutorial-talk').hidden=true;$('tutorial-focus').hidden=true;
  } catch(error) { notice(error.message); }
  finally { busy=wasBusy;sessionTransition=false; }
}
function notice(text) { $('notice').textContent = text; $('notice').hidden = false; clearTimeout(errorTimer); errorTimer = setTimeout(() => $('notice').hidden = true, 5500); }
async function rpc(route, args = {}) {
  const response = await fetch('/api/' + route + '?q=' + encodeURIComponent(JSON.stringify(args)), {method: 'POST', cache: 'no-store'});
  if (!response.ok) throw Error('连接失败 (' + response.status + ')');
  const result = await response.json();
  if (!result.ok) throw Error(failure(result.code, result.message));
  return result.data;
}
function failure(code, message) {
  const labels = {'client.player_turn':'请等待己方回合','client.stale':'状态已更新，请重新选择','client.ai_turn':'当前并非电脑回合',
    'action.dialogue_pending':'请先确认当前对白','action.phase':'回合尚未就绪','turn.finished':'本局已经结束',
    'training.unit':'请先选择已任职将领的己方部队','client.no_session':'请先开始游戏',
    'training.resources':'培训所需金币或粮食不足','training.unavailable':'该部队当前无法培训','training.limit':'已达到培训上限',
    'upgrade.resources':'建筑升级所需资源不足','upgrade.used':'该建筑本回合已升级','upgrade.limit':'建筑已达到升级上限',
    'purchase.resources':'改装所需资源不足','transport.unavailable':'该部队无法改装运输船',
    'client.undo_move':'该部队目前没有可以撤回的移动：已攻击、原位被占用或相关地块已发生变化'};
  return labels[code] || message;
}
function image(path) {
  if (!images.has(path)) { const img = new Image(); img.addEventListener('load',()=>{imageRevision++;requestRender();});img.src = '/content/' + path; images.set(path, img); }
  return images.get(path);
}
async function loadImage(path) {
  const img = image(path);
  if (img.complete && img.naturalWidth) return img;
  await img.decode(); return img;
}
function button(text, action, parent) {
  const result = document.createElement('button'); result.textContent = text;
  result.onclick = async () => {if(result.disabled)return;result.disabled=true;try{await action();await tutorialUi(result.dataset.tutorialId,Number(result.dataset.tutorialRow??-1));}catch(error){notice(error.message);}finally{result.disabled=false;}};
  parent.append(result); return result;
}
function countryImage(id,width,height,definition){
  const country=definition||(catalog?.countries&&Object.prototype.hasOwnProperty.call(catalog.countries,id)?catalog.countries[id]:null),flag=document.createElement('img');flag.alt='';flag.width=width;flag.height=height;
  flag.src=country?.flagImage?'/content/extensions/'+country.flagImage.slice('rewrite/'.length).split('/').map(encodeURIComponent).join('/'):'/content/ui/image_flag_hd/'+encodeURIComponent(country?.flag||id)+'1.png';return flag;
}
function scenarioList() {
  const list=$('scenarios'),render=++conquestRender;list.replaceChildren();list.className='scenario-list';
  $('conquest-pagination').hidden=mode!==2;
  if(mode!==2){
    for(const scenario of catalog.scenarios.filter(s=>s.mode===mode)){
      const row=button(scenario.name,()=>chooseScenario(scenario),list);row.className='scenario-entry';
    }
    return;
  }
  conquestPage=Math.min(conquestPage,conquestPages.pages.length-1);
  const slots=conquestPages.pages[conquestPage].slots;
  for(let index=0;index<6;index++){
    const slot=slots[index],scenario=slot&&catalog.scenarios.find(s=>s.mode===2&&s.id===slot.scenario);
    const row=scenario?button('',()=>chooseScenario(scenario),list):document.createElement('div');
    row.className='scenario-entry';
    row.style.setProperty('--entry-offset',index%2?'568px':'-568px');
    row.style.setProperty('--entry-delay',Math.floor(index/2)*60+'ms');
    if(!scenario){row.classList.add('scenario-placeholder');row.setAttribute('aria-hidden','true');list.append(row);continue;}
    row.setAttribute('aria-label',scenario.name);row.dataset.scenario=scenario.id;
    if(slot.art){const art=document.createElement('img');art.alt='';art.className='scenario-art';art.src='/content/'+slot.art;row.append(art);}
    else row.classList.add('scenario-fallback');
    const label = document.createElement('span'); label.className = 'conquest-caption';
    for (const name of slot.labels||[]) {
      const s=manifest.atlases.image_menu_hd['conquest_text_'+name+'.png'];if(!s)continue;
      const text = document.createElement('img'); text.alt = ''; text.src = '/content/ui/image_menu_hd/conquest_text_' + name + '.png';
      text.width=s.w/2;text.height=s.h/2;label.append(text);
    }
    if(!label.childElementCount)label.append(originalTextRenderer.element(scenario.name,'font_text_2_cn_hd','#ebe6e1'));
    const line = document.createElement('span'); line.className = 'conquest-line'; row.append(label,line);
  }
  const animations=list.getAnimations?.({subtree:true})||[];conquestEntering=animations.length>0;updateConquestPagination();
  Promise.all(animations.map(a=>a.finished.catch(()=>{}))).then(()=>{
    if(render!==conquestRender)return;conquestEntering=false;updateConquestPagination();
  });
}
function updateConquestPagination(){
  const count=conquestPages.pages.length;
  $('conquest-page-number').replaceChildren(originalTextRenderer.element((conquestPage+1)+' / '+count,'font_char_2_hd','#ebe6e1',42,'center'));
  $('conquest-page-number').setAttribute('aria-label','第'+(conquestPage+1)+'页，共'+count+'页');
  $('conquest-prev').disabled=conquestEntering||conquestPage===0;
  $('conquest-next').disabled=conquestEntering||conquestPage===count-1;
}
function turnConquestPage(offset){
  const next=conquestPage+offset;
  if(mode!==2||conquestEntering||next<0||next>=conquestPages.pages.length)return;
  conquestPage=next;scenarioList();
}
async function chooseScenario(scenario) {
  if (busy) return;
  if (scenario.mode !== 2) return start(scenario.id, null);
  busy = true;
  try {
    const countries = await rpc('countries', {scenario:scenario.id});
    countryChoices = countries;
    await loadLibrary('strategicmap');
    chosenScenario = scenario.id; chosenCountry = undefined; $('confirm-country').disabled = true;
    $('countries').hidden = false; $('country-list').replaceChildren(); $('country-list').scrollTop = 0;
    for (const country of countries) {
      const row = button('', () => {
        chosenCountry = country.id;
        for (const entry of $('country-list').children) entry.setAttribute('aria-pressed', String(entry === row));
        $('confirm-country').disabled = false;
        drawStrategicMap();
      }, $('country-list'));
      row.className = 'country-entry'; row.setAttribute('aria-label', country.name); row.setAttribute('aria-pressed', 'false');
      const flag = countryImage(country.id,51,30,country); flag.className = 'country-flag';
      row.append(flag, labelImage('country_' + country.id, country.name));
    }
    $('country-list').firstElementChild?.click();
    menus.slideInSidebar($('country-list'));
  } finally { busy = false; }
}
async function loadLibrary(name) {
  if (libraries.has(name)) return libraries.get(name);
  const data = await (await fetch('/content/animations/' + name + '.json')).json();
  data.byName = new Map(data.items.map(item => [item.name, item]));
  data.sprites = new Map(data.atlas.sprites.map(sprite => [sprite.name, sprite]));
  data.texture = await loadImage('images/' + data.atlas.textures[0].name.replace(/\.(?:pkm|pvr)$/i, '.png'));
  libraries.set(name, data); return data;
}
function visualProfile(unit,world=state){return unit&&(effects?.profile(unit.profile,world?.factions.find(f=>f.id===unit.owner))||manifest.profiles[unit.profile]);}
async function installState(next, center = false) {
  closeSingleAction();
  const resetPause=center;
  const previous=state;
  center=center||!!(state&&(state.map!==next.map||state.scenario!==next.scenario||state.width!==next.width||state.height!==next.height||state.playerFaction!==next.playerFaction));
  if(center){effects?.clear();damageNumbers?.clear();deaths.clear();combatMotions.clear();battlefield.resetMotions();battleDialogues.length=0;unitNavigation.reset();}
  const keepUnit=center?null:selected?.id,keepCell=center?null:selection?.cell;
  const used = [...new Set([...next.units,...(state?.units||[])].map(u => visualProfile(u,next)?.library).filter(Boolean))];
  await Promise.all([...used, 'choose', 'maptext', 'anim_fire', 'anim_upgrade', 'anim_smoke'].map(loadLibrary));
  if(!center)focusAiAction(next);
  if(!center&&next.combat&&!(aiRunning&&skipAi)){
    selection=undefined;$('selection').hidden=true;
    // A combined AI command must arrive at its firing position while both
    // combatants still exist in the presentation scene.
    const moves=next.combat.movements?.length?next.combat.movements:next.movements||[];
    if(moves.length){
      state={...state,units:state.units.map(u=>{const move=moves.find(m=>m.unit===u.id);return move?{...u,cell:move.path.at(-1)}:u;})};
      await playMovements(moves);
      playOccupationSound(next,moves);
    }
    await playCombat(next.combat,new Set(state.units.filter(u=>!next.units.some(v=>v.id===u.id)).map(u=>u.id)));
  }
  if(!center&&!(aiRunning&&skipAi)){
    const destroyed=(next.notices||[]).filter(n=>n.kind==='unit.destroyed').map(n=>n.subject);
    const victims=state.units.filter(u=>!next.units.some(v=>v.id===u.id)&&(next.combat||destroyed.includes(u.id)));
    if(victims.length){for(const unit of victims)beginDeath(unit.id);await presentationDelay(Math.max(0,...victims.map(u=>{const d=deaths.get(u.id);return d.started+d.duration-performance.now();})));}
  }
  deaths.clear();
  if(!center&&!(aiRunning&&skipAi)&&state&&next.revision>state.revision){
    const units=[...next.units,...state.units],handled=new Set();
    for(const notice of next.notices||[]){
      if(notice.kind==='battle.medal_dropped'){
        const unit=units.find(u=>u.id===notice.target);
        if(unit){const p=point(unit.cell);effects?.timeline('getmedal',p.x,p.y,{speed:Math.sqrt(settings.speed)});effects?.sound('sfx_lvup.wav');}
        continue;
      }
      if(notice.kind==='battle.kill_context'){
        const source=units.find(u=>u.id===notice.subject),victim=state.units.find(u=>u.id===notice.target);
        const speaker=source&&!next.units.some(u=>u.id===source.id)?{...source,health:0}:source;
        if(!speaker?.commanderImage||!victim)continue;
        const rule=catalog.commanderDialogues?.[speaker.commanderDefinitionId]?.killRules?.find(rule=>killDialogueMatches(rule,next,speaker,victim)
          && rule.chance>0&&(rule.chance===100||Math.random()*100<rule.chance));
        if(rule){
          queueBattleDialogue(speaker,rule.lines[rule.selection==='first'?0:Math.floor(Math.random()*rule.lines.length)]);
          handled.add(notice.target);
        }
        continue;
      }
      if(notice.kind!=='battle.kill_dialogue'||handled.has(notice.target))continue;
      const speaker=units.find(u=>u.commanderId===notice.subject&&u.commanderImage);
      if(!speaker)continue;
      const dedicated=catalog.commanderDialogues?.[speaker.commanderDefinitionId]?.kill;
      const choices=dedicated?.length?dedicated:Object.entries(manifest.strings).filter(([key,text])=>/^desc_destroy enemy [1-6]$/.test(key)&&text).map(([,text])=>text);
      if(!choices.length)continue;
      queueBattleDialogue(speaker,choices[Math.floor(Math.random()*choices.length)]);
    }
  }
  state = next; selected = undefined; selection = undefined; $('selection').hidden = true;
  if(!center&&!skipAi&&previous&&next.revision>previous.revision)playAttachedEffects(previous,next);
  presentBattleDialogue();
  if (center || !mapBounds) mapBounds = Ew4Battlefield.cameraBounds(state, manifest.maps[state.map]);
  $('menu').hidden = true; $('play').hidden = false;
  if(resetPause)$('pause').hidden = true;
  if (center) centerPlayer(); else clampView();
  refreshHud();
  motions.clear(); arrivals.clear();
  const now=performance.now(), stepMs=movementStepMs();
  if (!center && !fastSkipping()) {
    const moves=skipAi&&next.combat?.movements?.length?next.combat.movements:next.movements||[];
    if(!next.combat||skipAi)for(const move of moves)if(!move.instant&&move.path.length>1){combatMotions.delete(move.unit);motions.set(move.unit,{path:move.path,started:now,stepMs});const a=point(move.path.at(-2)),b=point(move.path.at(-1));facings.set(move.unit,b.x<a.x?-1:1);if(!skipAi)playMovementSound(move);}
    if(!skipAi)for(const id of next.recruited || []){const unit=state.units.find(u=>u.id===id);if(!unit||unit.category==='fort'||!next.notices?.some(n=>n.kind==='unit.recruited'&&n.subject===id))continue;arrivals.set(id,now);const p=point(unit.cell);effects?.sound('sfx_draft.wav');effects?.effect('effect_recruit.xml',p.x,p.y,{speed:Math.sqrt(settings.speed)});}
    const duration=Math.max(...[...motions.values()].map(m=>(m.path.length-1)*stepMs),...([...arrivals.keys()].map(id=>{
      const unit=state.units.find(u=>u.id===id),p=visualProfile(unit),lib=libraries.get(p?.library),item=lib?.byName.get(p?.motions.ready);
      return Math.max(Ew4Battlefield.recruitDuration(Math.sqrt(settings.speed)),(item?.duration||1)/(lib?.fps||24)*1000);
    })),0);
    updateAiCamera(now);
    if(duration)await presentationDelay(duration);
    if(!next.combat&&!skipAi)playOccupationSound(next,moves);
  }
  motions.clear();arrivals.clear();
  requestRender();
  if(!center&&state.activeFaction===state.playerFaction&&!state.dialogue){const unit=state.units.find(u=>u.id===keepUnit);const cell=unit?.cell??(keepUnit?null:keepCell);if(cell!=null)await selectCell(cell,true);}
}
function playAttachedEffects(previous,next){
  const oldUnits=new Map(previous.units.map(u=>[u.id,u])),units=new Map(next.units.map(u=>[u.id,u])),upgraded=new Set(),healed=new Set(),commanders=new Set(),speed=Math.sqrt(settings.speed);
  for(const notice of next.notices||[]){
    if(['battle.experience','unit.trained','unit.item_used'].includes(notice.kind)){
      const unit=units.get(notice.subject),old=oldUnits.get(notice.subject);
      if(unit&&old&&unit.veterancy!==old.veterancy&&!upgraded.has(unit.id)){
        upgraded.add(unit.id);const p=point(unit.cell);effects?.effect('effect_upgrade.xml',p.x,p.y,{speed});
        if(notice.kind==='battle.experience')effects?.sound('sfx_lvup.wav');
      }
    }
    if(notice.kind==='unit.healed')healed.add(notice.subject);
    if(notice.kind==='commander.rank_advanced'||notice.kind==='commander.title_advanced'){
      const key=notice.kind+':'+notice.subject,unit=next.units.find(u=>u.commanderId===notice.subject);
      if(unit&&!commanders.has(key)){
        commanders.add(key);const p=point(unit.cell);effects?.timeline(notice.kind==='commander.rank_advanced'?'upgraderank':'upgradenobility',p.x,p.y,{speed});
      }
    }
  }
  if(next.suppliedUnit)healed.add(next.suppliedUnit);
  for(const id of healed){const unit=units.get(id),old=oldUnits.get(id);
    if(unit&&old&&unit.health>old.health&&!upgraded.has(id)){
      const p=point(unit.cell);effects?.sound('sfx_supply.wav');effects?.effect('effect_recover.xml',p.x,p.y,{speed});
    }
  }
}
function killDialogueMatches(rule,next,speaker,victim){
  const when=rule.when;
  return (!when.victimCommanders.length||when.victimCommanders.includes(victim.commanderDefinitionId))
    &&(!when.victimCategories.length||when.victimCategories.includes(victim.category))
    &&(when.victimHasCommander==null||when.victimHasCommander===!!victim.commanderId)
    &&(!when.scenarios.length||when.scenarios.includes(next.scenario))
    &&next.round>=when.minRound&&next.round<=when.maxRound
    &&speaker.health*100<=speaker.maxHealth*when.killerHealthPercentAtMost;
}
function queueBattleDialogue(speaker,text){
  const name=speaker.commanderImage;
  battleDialogues.push({text,left:true,portrait:name+'.png',name:manifest.strings[name]||manifest.strings['name_'+name]||name});
}
function presentBattleDialogue(){
  // Shouts reuse the panel but have no pending dialogue in the backend.
  if(!state.dialogue&&battleDialogues.length){
    const talk=battleDialogues[0];
    state={...state,dialogue:talk.text,dialoguePresentation:talk,localBattleDialogue:true};
  }
}
function fastSkipping(){return aiRunning&&skipAi&&settings.skipTurnFast;}
function movementStepMs(){return aiRunning&&skipAi?65:255/Math.sqrt(settings.speed);}
function clearAiPresentation(){
  motions.clear();arrivals.clear();deaths.clear();combatMotions.clear();battlefield.resetMotions();effects?.clear();damageNumbers?.clear();finishPresentation?.();
}
function followingAi(){return aiRunning&&settings.followAi&&!fastSkipping()&&aiFocus&&!state?.factions.find(f=>f.id===state.activeFaction)?.human;}
function focusAiAction(next){
  if(!aiRunning||!settings.followAi||fastSkipping()||next.factions.find(f=>f.id===next.activeFaction)?.human){aiFocus=null;return;}
  const moves=next.combat?.movements?.length?next.combat.movements:next.movements||[];
  const move=moves.find(m=>!m.instant&&m.path.length>1),id=move?.unit||next.combat?.attacker||next.recruited?.[0];
  const unit=state.units.find(u=>u.id===id)||next.units.find(u=>u.id===id);
  if(unit){aiFocus={unit:id,cell:move?.path[0]??unit.cell};updateAiCamera(performance.now());}
}
function updateAiCamera(time){
  if(!followingAi())return;
  const motion=motions.get(aiFocus.unit),unit=state.units.find(u=>u.id===aiFocus.unit);
  const p=motion?Ew4Battlefield.pose(motion,state.width,time):point(unit?.cell??aiFocus.cell);
  view.x=p.x;view.y=p.y;clampView();
}
function presentationDelay(duration){requestRender();return new Promise(resolve=>{const timer=setTimeout(done,duration);function done(){clearTimeout(timer);presentationWaiters.delete(done);if(!presentationWaiters.size)finishPresentation=null;requestRender();resolve();}presentationWaiters.add(done);finishPresentation=()=>{for(const finish of [...presentationWaiters])finish();};});}
function beginDeath(id){if(!deaths.has(id))deaths.set(id,{started:performance.now(),duration:500/Math.sqrt(settings.speed)});}
function playMovementSound(move){const unit=state.units.find(u=>u.id===move.unit);if(unit)effects?.moveSound(visualProfile(unit),unit.category,state.cells[move.path.at(-1)]?.sea);}
function playOccupationSound(next,moves){
  for(const move of moves)if(!move.instant&&move.path.length>1){const cell=move.path.at(-1),site=next.cells[cell];
    if(site.buildingKind&&site.buildingKind!=='farmland'&&next.notices?.some(n=>n.kind==='cell.owner_changed'&&Number(n.subject)===cell))effects?.sound('sfx_occupy.wav');}
}
async function playMovements(moves){
  const started=performance.now(),stepMs=movementStepMs();
  for(const move of moves)if(!move.instant&&move.path.length>1){combatMotions.delete(move.unit);motions.set(move.unit,{path:move.path,started,stepMs});playMovementSound(move);}
  await presentationDelay(Math.max(0,...moves.filter(m=>!m.instant).map(m=>(m.path.length-1)*stepMs)));motions.clear();
}
async function playCombat(combat,destroyed=new Set()){
  selection=undefined;$('selection').hidden=true;requestRender();
  // The backend has already resolved both hits. Animation timing cannot change the outcome.
  const pairs=[[combat.attacker,combat.defender,combat.damage,combat.critical]];
  if(combat.counterattack)pairs.push([combat.defender,combat.attacker,combat.counterDamage,combat.counterCritical]);
  if(aiRunning&&skipAi)return;
  const speed=Math.sqrt(settings.speed),plans=[];
  for(const [id,targetId,damage,critical] of pairs){
    const unit=state.units.find(u=>u.id===id),target=state.units.find(u=>u.id===targetId);
    if(!unit||!target)continue;
    const profile=visualProfile(unit),lib=libraries.get(profile?.library);if(!lib)continue;
    const facing=point(target.cell).x<point(unit.cell).x?-1:1;
    const plan=effects.attackPlan(profile,lib,unit.category,facing,speed);if(!plan.duration)continue;
    plans.push({id,targetId,damage,critical,unit,target,profile,facing,...plan});
  }
  // Source windups differ (artillery 1.7s, fortress 0s). Align the actual
  // salvo and hit timing. Body clips continue independently without extra input waits.
  const firstShot=Math.max(0,...plans.map(p=>p.firstShot));
  for(const plan of plans)plan.delay=firstShot-plan.firstShot;
  const impactAt=Math.max(0,...plans.map(p=>p.delay+p.hitAt));
  const started=performance.now();
  await Promise.all(plans.map(async plan=>{
    const {id,targetId,damage,critical,unit,target,profile,facing,timeline,segments,delay}=plan;
    const hold=impactAt-delay-plan.attackBasis;
    const duration=delay+plan.duration+hold;
    facings.set(id,facing);
    const p=point(unit.cell),q=point(target.cell);
    effects?.timeline(timeline,p.x+facing*profile.x,p.y+profile.y,{speed,scale:profile.scale,started:started+delay});
    combatMotions.set(id,{started,delay,speed,segments});
    await presentationDelay(Math.max(0,started+impactAt-performance.now()));
    if(!(aiRunning&&skipAi)){
      damageNumbers?.add(damage,critical,q.x,q.y,speed);
      if(destroyed.has(targetId))beginDeath(targetId);
      playImpact(profile,target,damage,facing,speed);
      if(id===combat.attacker&&combat.splashTarget){const splash=state.units.find(u=>u.id===combat.splashTarget);if(splash){const hit=point(splash.cell);damageNumbers?.add(combat.splashDamage,false,hit.x,hit.y,speed);playImpact(profile,splash,combat.splashDamage,facing,speed);if(destroyed.has(splash.id))beginDeath(splash.id);}}
    }
    if(!(aiRunning&&skipAi))await presentationDelay(Math.max(0,started+duration-performance.now()));
    // The renderer retires any remaining recovery frames without locking input.
  }));
}
function playImpact(profile,target,damage,facing,speed){
  if(damage<=0)return;
  const p=point(target.cell),rocket=profile.kind==='Rocket';
  effects?.timeline(rocket?'rocketstrike':Ew4Effects.impact(profile.stats.weapon,target.category,damage,state.cells[target.cell].sea,facing),p.x+(rocket?30*facing:0),p.y,{speed});
}
async function start(scenario, country, localPlayers) {
  if(busy)return;
  busy = true; $('countries').querySelectorAll('button').forEach(b => b.disabled = true);
  try {
    const scenarioMode=catalog.scenarios.find(s=>s.id===scenario)?.mode;
    if(localPlayers===undefined&&settings.localMultiplayer&&[1,2].includes(scenarioMode)){
      localPlayers=await localMultiplayerUi.choose(scenario,country);
      if(localPlayers===null)return;
    }else if(Array.isArray(localPlayers)&&[1,2].includes(scenarioMode)){
      const available=new Set((await rpc('local-players',{scenario,country})).map(f=>f.id));
      localPlayers=localPlayers.filter(id=>available.has(id));
    }
    await withLoading(async()=>{tutorial=null;stageIntroOpen=false;await installState(await rpc('start', {scenario, country,localPlayers}), true);showStageIntro();await beginTutorial();});
  }
  finally { busy = false; $('countries').querySelectorAll('button').forEach(b => b.disabled = false); if(state)refreshHud(); }
}
async function withLoading(action){
  const started=performance.now(),tips=Object.entries(manifest.strings).filter(([key])=>/^text_tips_\d+$/.test(key));
  const tip=tips[Math.floor(Math.random()*tips.length)]?.[1]||'';
  $('loading-tip').replaceChildren(originalTextRenderer.element(tip,'font_text_2_cn_hd','#ebe6e1',527));
  $('loading-word').replaceChildren(originalTextRenderer.element('LOADING...','font_char_3_hd','#ebe6e1'));
  $('loading-screen').hidden=false;
  try{await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));await action();await new Promise(resolve=>setTimeout(resolve,Math.max(0,700-(performance.now()-started))));}
  finally{$('loading-screen').hidden=true;}
}
function flagSource(faction){
  return faction.flagImage?'/content/extensions/'+faction.flagImage.slice('rewrite/'.length).split('/').map(encodeURIComponent).join('/')
    :'/content/ui/image_flag_hd/'+faction.flag+'1.png';
}
function refreshAiProgress(){
  const active=state.factions.find(f=>f.id===state.activeFaction);
  $('ai-action').hidden=active.human||state.phase==='Finished'||!!state.dialogue;
  const flag=$('ai-flag'),source=flagSource(active);
  if(flag.getAttribute('src')!==source)flag.src=source;
  flag.alt=flag.title=active.name;
}
async function presentAiProgress(){
  if(document.hidden||!appVisible)return;
  // Give short AI turns a paint opportunity without waiting for battlefield animation.
  await new Promise(resolve=>{
    const timeout=setTimeout(resolve,100);
    const paint=()=>requestAnimationFrame(()=>requestAnimationFrame(()=>{clearTimeout(timeout);resolve();}));
    const flag=$('ai-flag');
    if(flag.complete)paint();else flag.decode().catch(()=>{}).then(paint);
  });
}
function refreshHud() {
  const active = state.factions.find(f => f.id === state.activeFaction);
  const player = state.factions.find(f => f.id === state.playerFaction);
  $('turn').replaceChildren(originalTextRenderer.element('回合  ' + state.round,'font_text_2_cn_hd','#404040'));
  $('player-flag').src = flagSource(player);
  $('player-flag').alt = $('player-flag').title = player.name;
  $('resources').replaceChildren();
  for (const [label, icon, value] of [['金钱','money', player.resources.money], ['工业','industry',player.resources.industry], ['粮食','food',player.resources.food]]) {
    const span = document.createElement('span'); span.className='resource-item';span.setAttribute('aria-label', label + ' ' + value);
    const marker = document.createElement('img'); marker.src = '/content/ui/image_ui_hd/marker_' + icon + '.png'; marker.alt = '';
    span.append(marker, originalTextRenderer.element(value,'font_char_2_hd','#ffffff')); $('resources').append(span);
  }
  $('end-turn').disabled = busy || !active.human || state.phase !== 'Active' || !!state.dialogue;
  $('undo-move').hidden = !active.human;
  $('undo-move').disabled = busy || !selected || !selection?.undo;
  $('next-unit').hidden = !active.human;
  $('next-unit').disabled = busy || findingUnit || !Ew4UnitNavigation.groups(state).some(units=>units.length);
  $('skip-ai').hidden = active.human || state.phase === 'Finished' || !!state.dialogue;
  $('skip-ai').disabled = skipAi;
  refreshAiProgress();
  $('dialogue').hidden = !state.dialogue || !!state.scenarioPopup || stageIntroOpen;
  $('scenario-event').hidden = !state.scenarioPopup || stageIntroOpen;
  if(state.scenarioPopup){
    const popup=state.scenarioPopup;
    const content=$('scenario-event-content'), choices=$('scenario-event-choices'), event=$('scenario-event');
    const sameEvent=event.dataset.event===popup.id;
    const contentScroll=sameEvent?content.scrollTop:0, choicesScroll=sameEvent?choices.scrollTop:0;
    event.dataset.event=popup.id;
    content.classList.toggle('image-only',!!popup.image&&!popup.text);
    $('scenario-event-title').textContent=popup.title;
    const picture=$('scenario-event-image');
    picture.hidden=!popup.image;
    if(popup.image){
      const source='/content/extensions/'+popup.image.slice('rewrite/'.length).split('/').map(encodeURIComponent).join('/');
      if(picture.getAttribute('src')!==source)picture.src=source;
      picture.alt=popup.imageAlt||popup.title;
    }else picture.removeAttribute('src');
    const eventText=$('scenario-event-text');
    const proseWidth=Math.max(40,eventText.clientWidth||300);
    if(eventText.ew4Text!==popup.text||eventText.ew4Width!==proseWidth){
      eventText.replaceChildren(originalTextRenderer.prose(popup.text,'font_text_1_cn_hd','#404040',proseWidth));
      eventText.ew4Text=popup.text;eventText.ew4Width=proseWidth;
    }
    $('scenario-event-text').hidden=!popup.text;
    choices.replaceChildren();
    for(const choice of popup.choices){
      const b=button(choice.text,()=>act('event_choice',{offer:popup.id,target:choice.id}),$('scenario-event-choices'));
      b.dataset.choice=choice.id;b.disabled=busy||!choice.available;
    }
    content.scrollTop=contentScroll;choices.scrollTop=choicesScroll;
  }else delete $('scenario-event').dataset.event;
  if(state.dialogue&&!state.scenarioPopup){
    const talk=state.dialoguePresentation;
    $('dialogue-ok').classList.toggle('speaker-right',talk?.left===false);
    $('dialogue-text').replaceChildren(originalTextRenderer.element(state.dialogue,'font_text_1_cn_hd','#404040',216));
    $('dialogue-speaker').replaceChildren(originalTextRenderer.element(talk?.name||'','font_text_2_cn_hd','#646464',216,'center'));
    $('dialogue-portrait').replaceWith(Object.assign(talk?.portrait?menus.portrait(talk.portrait):document.createElement('img'),{id:'dialogue-portrait',alt:talk?.name||'',hidden:!talk?.portrait}));
    $('dialogue-text').scrollTop=0;
  }
  $('status').textContent = state.phase === 'Finished' ? (state.outcome === 'Victory' ? '胜利' : '本局结束') : '';
  victoryUi.sync(state.localMultiplayer?null:state);
  localMultiplayerUi.sync(state);
}
async function act(op, extra = {}) {
  if (busy || !state || stageIntroOpen) return;
  if(state.localBattleDialogue){
    if(op!=='dialogue')return;
    battleDialogues.shift();
    state={...state,dialogue:null,dialoguePresentation:null,localBattleDialogue:false};
    presentBattleDialogue();refreshHud();requestRender();
    await runAi();return;
  }
  closeSingleAction();
  busy = true; refreshHud();
  try {
    const next = await rpc('action', {op, revision:state.revision, unit:selected?.id, ...extra});
    await installState(next);
  } finally { busy = false; refreshHud(); }
  if (op === 'end' || op === 'dialogue' || op === 'event_choice') await runAi();
}
function runAi() {
  if(aiTask)return aiTask;
  if(sessionTransition||!$('pause').hidden)return Promise.resolve();
  aiTask=runAiLoop().finally(()=>{aiTask=null;});
  return aiTask;
}
async function stopAi(){
  if(!aiTask)return;
  aiStopRequested=true;skipAi=true;clearAiPresentation();
  // Join the in-flight command before replacing its session or using its revision to leave.
  await aiTask;
}
async function runAiLoop() {
  if (aiRunning) return;
  aiStopRequested=false;
  aiRunning = true; skipAi = false;
  try {
    busy=true;refreshHud();
    while (aiCanContinue(state)&&$('pause').hidden&&!aiStopRequested) {
      if(fastSkipping()){
        const batch=await rpc('action',{op:'ai_batch',revision:state.revision});
        if(batch.baseRevision!==state.revision)throw Error('回合状态不同步');
        const next=batch.state;
        if(!next.cells){next.cells=state.cells.slice();for(const update of next.cellUpdates)next.cells[update.cell]=update.value;}
        delete next.cellUpdates;
        if(batch.error||!aiCanContinue(next))await installState(next);
        else {
          const handoff=next.activeFaction!==state.activeFaction;
          state=next;refreshAiProgress();
          if(handoff)await presentAiProgress();
        }
        if(batch.error)throw Error(failure(batch.error.code,batch.error.message));
      }else await installState(await rpc('action', {op:'ai', revision:state.revision}));
      if(!skipAi&&$('pause').hidden)await new Promise(resolve => setTimeout(resolve, 180/settings.speed));
    }
  } catch (error) {
    try{await installState(await rpc('action',{op:'state'}));}catch(_){}
    notice(error.message);
  }
  finally { busy = false; aiRunning = false; skipAi = false; aiStopRequested=false; aiFocus=null; if(state)refreshHud();requestRender(); }
}
function aiCanContinue(next){return !stageIntroOpen&&next&&!next.dialogue&&!next.scenarioPopup&&next.phase!=='Finished'&&!next.factions.find(f=>f.id===next.activeFaction).human;}
async function selectCell(cell,restore=false,selectOnly=false) {
  if ((!restore&&busy) || !state || cell < 0 || cell >= state.cells.length || !state.cells[cell].active) return;
  closeSingleAction();
  if(!restore&&tutorial?.wait?.name==='wait area'&&Number(tutorial.wait.id)!==cell)return;
  const target = state.units.find(u => u.cell === cell);
  if (!restore && !selectOnly && selected && selection && state.activeFaction === state.playerFaction && !state.dialogue) {
    if (target && selection.targets?.includes(cell))
      return act('attack', {target:target.id}).then(()=>tutorial?.notify({type:'area',cell}));
    if (!target && selection.range.includes(cell)) return act('move', {cell}).then(()=>tutorial?.notify({type:'area',cell}));
  }
  selection = await rpc('action', {op:'select', cell, revision:state.revision});
  selected = target;if(!restore)selectedAt = performance.now(); const site = state.cells[cell];
  $('selection').hidden = false;
  const own = target?.owner === state.playerFaction;
  $('actions').replaceChildren(); $('terrain-info').replaceChildren();
  const terrainRows=Ew4Battlefield.terrainRows(site);
  $('terrain-info').hidden=!terrainRows.length;
  const incomeRows=terrainRows.filter(r=>!r.defense),defenseRows=terrainRows.filter(r=>r.defense);
  $('terrain-info').classList.toggle('with-defense',defenseRows.length>0&&incomeRows.length>0);
  for(const rows of [incomeRows,defenseRows]){
    if(!rows.length)continue;
    const column=document.createElement('div');column.className='terrain-column';
    for(const row of rows){
      const entry=document.createElement('div');entry.className='terrain-row';
      entry.append(uiImage(row.icon,18),originalTextRenderer.element(row.text,'font_char_2_hd','#eeeeee'));column.append(entry);
    }
    $('terrain-info').append(column);
  }
  if(selection.deploy) toolButton('general','任命将领',()=>menus.deployGeneral(target.id,state.revision));
  if(selection.actionQuotes?.upgrade) singleActionButton('buildupgrade','升级建筑','upgrade',selection.actionQuotes.upgrade,{cell});
  if(selection.installations.length) toolButton('builddefense','防御',()=>openDefense(cell));
  if(selection.actionQuotes?.train) singleActionButton('training','培训','train',selection.actionQuotes.train);
  if(selection.items) toolButton('items','使用物品',()=>menus.useItems(target.id));
  if(selection.offers.length) toolButton(({city:'city',industry:'factory',stable:'stable',port:'dock'})[site.buildingKind]||'buildfortress',site.buildingKind?'雇佣':'建造要塞',()=>openRecruitment(cell));
  if(selection.actionQuotes?.transport) singleActionButton('buyship','改装运输船','transport',selection.actionQuotes.transport);
  if(selection.tavern)toolButton('bar','酒馆',()=>menus.tavern(cell));
  if(selection.shop)toolButton('shop','商店',()=>menus.shop(cell));
  if(selection.market)toolButton('trade','市场',()=>menus.exchange(cell));
  if(target)toolButton('info','部队信息',()=>openUnitInfo(target));
  if(own&&selection.disband){const b=toolButton('disband','解散部队',()=>confirmDisband(target,selection.disband,b));b.dataset.action='disband';}
  for(const entry of selection.skills||[]){
    const skill=entry.skill,b=toolButton('info',skill.name,()=>openSkillAction(target,entry));
    b.classList.add('active-skill-button');b.disabled=!entry.available;
    b.title=skill.name+(entry.remainingCooldown?' ('+entry.remainingCooldown+')':'');
    b.replaceChildren(Ew4SkillUI.icon(skill,23,uiImage));
  }
  $('actions').hidden=!$('actions').childElementCount;
  refreshHud();
  if(!restore)await tutorial?.notify({type:'area',cell});
}
async function findNextUnit(){
  if(busy||findingUnit||!state||$('play').hidden||['pause','settings','detail-modal','loading-screen'].some(id=>!$(id).hidden))return;
  const unit=unitNavigation.next(state);
  if(!unit)return;
  findingUnit=true;busy=true;refreshHud();
  try{
    // Selecting a different friendly cell must never execute the previous unit's move/attack.
    await selectCell(unit.cell,true,true);
    selectedAt=performance.now();
    const p=point(unit.cell);view.x=p.x;view.y=p.y;clampView();requestRender();
  }catch(error){unitNavigation.reset();throw error;}
  finally{findingUnit=false;busy=false;refreshHud();}
}
function uiImage(name,width,height=width){
  const atlas=['image_recruit_hd','image_recruit_hd2','image_recruit_hd3','image_ui_hd','image_menu_hd','image_item_hd','image_newgame_hd','princesses_hd','princesses_sketch_hd','commanders1_hd','commanders2_hd','image_ui'].find(a=>manifest.atlases[a]?.[name]);
  const img=document.createElement('img');img.alt='';img.width=width;img.height=height;
  if(atlas)img.src='/content/ui/'+atlas+'/'+name;else console.warn('Missing original UI image',name);return img;
}
function toolButton(icon,title,action){const b=button('',action,$('actions'));b.title=title;b.setAttribute('aria-label',title);
  if(icon==='disband'){const img=new Image(33,33);img.alt='';img.src='/disband.png';b.append(img);}
  else b.append(uiImage('button_'+(icon==='general'?'generals':icon)+'.png',33));
  b.dataset.tutorialId=({general:'btn_general',items:'btn_item',city:'btn_city',factory:'btn_factory',stable:'btn_stable',dock:'btn_dock',buildfortress:'btn_fortress',builddefense:'btn_defense',buyship:'btn_ship',training:'btn_training',buildupgrade:'btn_upgrade'})[icon]||'';return b;}
let detailReturn,detailRevision=0,stageIntroOpen=false;
function showStageIntro(){
  if(!state?.stageIntro||state.phase==='Finished')return;
  stageIntroOpen=true;
  menus.stageIntro(state.stageIntro,()=>{stageIntroOpen=false;refreshHud();void runAi();});
  refreshHud();
}
function confirmDisband(unit,refund,anchor){openSingleAction('disband','解散部队',{cost:refund,affordable:true,warning:selection.disbandWarning},anchor,{unit:unit.id},true);}
function popupOpen(element){element.classList.remove('popup-opening');void element.offsetWidth;element.classList.add('popup-opening');}
function closeDetail(){const back=detailReturn;detailReturn=null;menus.dismissSkillTip();$('detail-modal').hidden=true;if(back)back();}
function detailWindow(title,width,height,confirm,onClose){
  closeSingleAction();
  const generation=++detailRevision;
  menus.dismissSkillTip();detailReturn=onClose;
  const dialog=$('detail-modal').firstElementChild;dialog.style.width=width+'px';dialog.style.height=height+'px';
  dialog.classList.remove('native-dialog');
  dialog.style.removeProperty('--ui-scale');
  const titleText=({headquarters_label:'指挥部',tutorial_label:'教程',country_label:'选择国家',recruit_label:'招募',info_label:'信息',defense_label:'防御'})[title]||manifest.strings[title]||title;
  $('detail-title').replaceChildren(labelImage(title,titleText,$('detail-title')));$('detail-content').replaceChildren();$('detail-content').nativeLayout=null;$('detail-content').className='';$('detail-modal').hidden=false;
  $('detail-confirm').dataset.tutorialId='winbtn_ok';$('detail-confirm').hidden=!confirm;$('detail-confirm').onclick=async()=>{try{await confirm();if(generation===detailRevision)$('detail-modal').hidden=true;await tutorialUi($('detail-confirm').dataset.tutorialId);}catch(e){notice(e.message);}};
  $('detail-confirm').disabled=false;
  popupOpen(dialog);return $('detail-content');
}
const formationColors=['#646464','#326478','#40803b'];
function tintedUi(name,color,width,height){
  const source=uiImage(name,width,height),result=document.createElement('canvas');result.width=width*2;result.height=height*2;result.style.width=width+'px';result.style.height=height+'px';
  function paint(){const draw=result.getContext('2d');draw.clearRect(0,0,result.width,result.height);draw.globalCompositeOperation='source-over';draw.drawImage(source,0,0,result.width,result.height);draw.globalCompositeOperation='multiply';draw.fillStyle=color;draw.fillRect(0,0,result.width,result.height);draw.globalCompositeOperation='destination-in';draw.drawImage(source,0,0,result.width,result.height);draw.globalCompositeOperation='source-over';}
  if(source.complete&&source.naturalWidth)paint();else source.onload=paint;return result;
}
function unitCard(profile,cost){
  const p=manifest.profiles[profile];const card=document.createElement('div');card.className='unit-card';
  card.style.setProperty('--formation-color',formationColors[p.grade]||formationColors[0]);
  card.append(uiImage(p.card,72,54));const marker=uiImage(p.cardMarker,19,19);marker.className='card-marker';card.append(marker);
  const strip=document.createElement('span');strip.className='cost-strip';
  const balance=state?.factions.find(f=>f.id===state.playerFaction)?.resources;
  for(const [kind,icon] of [['money','money'],['industry','industry']]){strip.append(uiImage('marker_'+icon+'.png',10));const value=document.createElement('span');value.textContent=cost?.[kind]??0;if(balance&&balance[kind]<(cost?.[kind]??0))value.style.color='#ff3200';strip.append(value);}
  card.append(strip);const frame=tintedUi('build_frame.png',formationColors[p.grade]||formationColors[0],72,65);frame.className='recruit-frame';card.append(frame);return card;
}
function abilityGrid(profile,unit){
  const p=manifest.profiles[profile],s=p.stats,grid=document.createElement('div');grid.className='ability-grid';
  const health=Number(s.strength)===-999?'一次性':unit?.health??s.strength;
  for(const [icon,value] of [['attack',s.minatk+'-'+s.maxatk],['soldiernumber',p.grade+1],['hp',health],['food',s.consumption],['range',s.maxatkrange],['move',unit?.remainingMovement??s.movement]]){
    const marker=document.createElement('span');marker.className='ability-icon';marker.append(uiImage('infomarker_'+icon+'.png',21));const number=document.createElement('span');number.textContent=value;grid.append(marker,number);
    if(icon==='soldiernumber'){number.replaceChildren();number.className='formation-count';for(let i=0;i<3;i++)number.append(tintedUi('infomarker_soldiernumber'+(i<value?'':'_empty')+'.png','#404040',14,18));}
  }return grid;
}
function descriptionBox(parent,name,text){const desc=document.createElement('section');desc.className='unit-description';const h=document.createElement('h3');h.textContent=name;const p=document.createElement('p');p.textContent=text;desc.append(h,p);parent.append(desc);}
async function openUnitInfo(unit){
  const p=manifest.profiles[unit.profile],entries=[...(unit.traits||[]),...(unit.skills||[]),...(unit.statuses||[])];
  const l=menus.native('form_unitinfo');l.body.classList.add('unit-detail');
  // Keep the original 330 x 187 contents intact; reserve a separate band below.
  l.body.parentElement.style.height='255px';
  const card=unitCard(unit.profile);card.classList.add('info-unit-card');l.fill('group_unit',card);
  card.querySelector('.card-marker').remove();
  for(let i=0;i<=p.grade;i++){const marker=document.createElement('span');marker.className='info-formation-marker';marker.style.left=(i*4)+'px';marker.append(tintedUi('buildmaker.png',formationColors[p.grade]||formationColors[0],19,20),uiImage(p.cardMarker,19,19));card.append(marker);}
  const grid=abilityGrid(unit.profile,unit);grid.classList.add('info-abilities');l.fill('grid_ability',grid);
  l.text('intitle',p.name);l.text('text_desc',p.description);l.hide('group_info');
  const tags=document.createElement('section');tags.className='unit-effect-region';tags.setAttribute('aria-label','技能与效果');
  const title=document.createElement('div');title.className='unit-effect-title';title.append(originalTextRenderer.element('技能 / 效果','font_text_1_cn_hd','#646464'));tags.append(title);l.body.append(tags);
  const list=document.createElement('div');list.className='unit-skill-list';tags.append(list);
  if(entries.length){
    for(const skill of entries){const row=button('',()=>openSkillDescription(skill,()=>openUnitInfo(state.units.find(u=>u.id===unit.id)||unit)),list);row.className='unit-skill-row';row.title=skill.description;
      row.append(Ew4SkillUI.icon(skill,19,uiImage),originalTextRenderer.element(skill.name,'font_text_2_cn_hd','#404040'));}
    Ew4SkillUI.layout(list,catalog.extensionLayouts?.unit,entries);
  }
  const revision=state.revision;
  const info=await rpc('unit-info',{unit:unit.id,revision});
  if(!l.current()||revision!==state.revision)return;
  card.querySelector('.cost-strip').replaceChildren(...['money','industry'].flatMap(key=>info.cost?[uiImage('marker_'+key+'.png',10),originalTextRenderer.element(info.cost[key],'font_char_2_hd','#ebe6e1')]:[]));
  if(!info.commander)return;
  menus.data=await rpc('headquarters');
  if(!l.current()||revision!==state.revision)return;
  const definition=menus.data.definitions.find(d=>d.id===info.commander.definitionId);
  if(!definition)return;
  const portrait=menus.card(definition,info.commander,()=>{});portrait.querySelector('.commander-info').remove();delete portrait.dataset.locked;
  l.fill('tcmder',portrait);l.hide('group_info',false);
  l.action('btn_info','查看'+definition.name+'将领信息',()=>menus.commanderInfo(definition,info.commander,()=>openUnitInfo(state.units.find(u=>u.id===unit.id)||unit)));
}
function openSkillDescription(skill,onClose){
  const body=detailWindow('info_label',300,180);
  const box=document.createElement('div');box.className='skill-action-description';
  box.append(originalTextRenderer.element(skill.name,'font_text_2_cn_hd','#404040'),originalTextRenderer.element(skill.description,'font_text_2_cn_hd','#404040',270));body.append(box);
  detailReturn=onClose||(()=>{if(selected)openUnitInfo(selected);});
}
function openSkillAction(unit,entry){
  const skill=entry.skill,isCell=skill.target==='cell',choices=isCell?(entry.cells||[]):entry.targets;let target=choices[0];
  const body=detailWindow('info_label',300,220,()=>act('skill',{offer:skill.instanceId,...(isCell?{cell:target}:{target})}));
  $('detail-confirm').disabled=!entry.available||target===undefined;
  const box=document.createElement('div');box.className='skill-action-description';
  box.append(originalTextRenderer.element(skill.name,'font_text_2_cn_hd','#404040'),originalTextRenderer.element(skill.description,'font_text_2_cn_hd','#404040',270));
  const cost=document.createElement('div');cost.className='skill-action-cost';
  for(const [key,icon] of [['money','money'],['industry','industry'],['food','food']]){const span=document.createElement('span');span.append(uiImage('marker_'+icon+'.png',16),originalTextRenderer.element(skill.cost[key]||0,'font_char_2_hd','#404040'));cost.append(span);}box.append(cost);
  const targets=document.createElement('div');targets.className='skill-targets';
  for(const id of choices){const t=isCell?null:state.units.find(u=>u.id===id),cell=isCell?id:t.cell,b=button('',()=>{target=id;for(const n of targets.children)n.setAttribute('aria-pressed',String(n===b));},targets);
    b.setAttribute('aria-pressed',String(id===target));b.append(originalTextRenderer.element((isCell?'':manifest.profiles[t.profile].name+' ')+'('+(cell%state.width)+','+Math.floor(cell/state.width)+')','font_text_2_cn_hd','#404040'));}
  box.append(targets);body.append(box);
}
let pendingAction=null;
function closeSingleAction(){pendingAction=null;$('single-action').hidden=true;}
function singleActionButton(icon,title,op,quote,extra={}){
  const b=toolButton(icon,title,()=>confirmUnitAction(op,title,extra,b));
  b.dataset.action=op;b.dataset.affordable=String(quote.affordable);
}
function confirmUnitAction(op,title,extra={},anchor){
  const quote=selection?.actionQuotes?.[op];
  if(quote)openSingleAction(op,title,quote,anchor,extra);
}
function openSingleAction(op,title,quote,anchor,extra={},refund=false){
  if(busy||!anchor||!selection)return;
  pendingAction={op,title,quote,anchor,extra,refund,revision:state.revision,unit:selected?.id,cell:selection.cell};
  const host=$('single-action'),panel=$('single-action-cost'),confirm=$('single-action-confirm');
  host.dataset.action=op;panel.replaceChildren();
  const funds=state.factions.find(f=>f.id===state.playerFaction).resources;
  const keys=op==='train'?['money','food']:['money','industry'];
  for(const key of ['money','industry','food'])if(quote.cost[key]&&!keys.includes(key))keys.push(key);
  const labels={money:'金币',industry:'工业',food:'粮食'};
  panel.setAttribute('aria-label',(refund?'返还':'费用')+'：'+keys.map(k=>labels[k]+quote.cost[k]).join('，'));
  panel.style.height=(56+Math.max(0,keys.length-2)*21)+'px';
  for(const key of keys){
    const row=document.createElement('div');row.className='action-cost-row';row.dataset.resource=key;row.dataset.value=quote.cost[key];
    const color=refund?'#28733b':funds[key]<quote.cost[key]?'#ff3200':'#404040';
    row.append(uiImage('marker_'+key+'.png',19.5),originalTextRenderer.element((refund?'+':'')+quote.cost[key],'font_text_4',color,50,'center',{singleLine:true}));
    panel.append(row);
  }
  confirm.disabled=!quote.affordable;confirm.title='确认'+title;confirm.setAttribute('aria-label','确认'+title);
  host.hidden=false;positionSingleAction();
}
function positionSingleAction(){
  if(!pendingAction)return;
  const {anchor,revision,cell}=pendingAction;
  if(!anchor.isConnected||revision!==state?.revision||cell!==selection?.cell){closeSingleAction();return;}
  const host=$('single-action'),panel=$('single-action-cost'),parent=$('selection').getBoundingClientRect(),rect=anchor.getBoundingClientRect();
  if(!rect.width){closeSingleAction();return;}
  const scale=rect.width/33,left=rect.left-parent.left;
  host.style.left=left+'px';host.style.top=(rect.top-parent.top)+'px';
  const panelLeft=Math.max(2,Math.min(parent.width-84*scale-2,left-25.5*scale));
  panel.style.left=(panelLeft-left)/scale+'px';
}
async function submitSingleAction(pending){
  if(!pending||busy||!pending.quote.affordable)return;
  if(pending.revision!==state.revision||pending.cell!==selection?.cell||pending.unit!==selected?.id){closeSingleAction();notice('状态已更新，请重新选择');return;}
  try{await act(pending.op,{...pending.extra,unit:pending.unit,revision:pending.revision});await tutorialUi('btn_done');}
  catch(error){notice(error.message);}
}
$('single-action-confirm').onclick=async()=>{
  const pending=pendingAction;
  if(!pending||busy||!pending.quote.affordable)return;
  $('single-action-confirm').disabled=true;
  if(pending.quote.warning){
    const body=detailWindow('解散要塞',300,160,async()=>{
      if($('detail-confirm').disabled)return;
      $('detail-confirm').disabled=true;
      await submitSingleAction(pending);
    });
    body.className='fort-disband-warning';
    body.append(originalTextRenderer.element(pending.quote.warning,'font_text_2_cn_hd','#404040',260,'center'));
    return;
  }
  await submitSingleAction(pending);
};
document.addEventListener('pointerdown',event=>{if(pendingAction&&!$('single-action').contains(event.target))closeSingleAction();},{capture:true});
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&pendingAction){closeSingleAction();event.preventDefault();}});
function openRecruitment(cell){
  const offers=selection.offers;let chosen=offers[0];
  if(!chosen)return;
  const body=detailWindow('recruit_label',441,185,()=>act('recruit',{cell,offer:chosen.id}));
  const row=document.createElement('div');row.className='recruit-cards';body.append(row);const details=document.createElement('div');details.className='recruit-description';body.append(details);
  function choose(offer,b){chosen=offer;for(const c of row.children)c.setAttribute('aria-pressed',String(c===b));details.replaceChildren(abilityGrid(offer.profile));descriptionBox(details,manifest.profiles[offer.profile].name,manifest.profiles[offer.profile].description);const funds=state.factions.find(f=>f.id===state.playerFaction).resources;$('detail-confirm').disabled=['money','industry','food'].some(key=>(offer.cost[key]||0)>funds[key]);}
  const groups=new Map();for(const offer of offers){const key=manifest.profiles[offer.profile].unitIndex;if(!groups.has(key))groups.set(key,[]);groups.get(key).push(offer);}
  [...groups.values()].forEach((group,index)=>{group.sort((a,b)=>manifest.profiles[a.profile].grade-manifest.profiles[b.profile].grade);let grade=0;
    function render(b,animate){
      const outgoing=animate?b.querySelector('.unit-card'):null;
      b.replaceChildren(unitCard(group[grade].profile,group[grade].cost));
      for(let layer=1;layer<group.length;layer++){const next=manifest.profiles[group[(grade+layer)%group.length].profile];const side=tintedUi('build_side.png',formationColors[next.grade],72-layer*2,3);side.className='formation-layer';side.style.left=layer*2+'px';side.style.top=-layer*3+'px';b.append(side);}
      if(outgoing){
        // Only the old portrait layer lifts off; costs and formation markers update in place.
        outgoing.querySelectorAll('.cost-strip,.card-marker').forEach(n=>n.remove());
        outgoing.classList.add('formation-outgoing');outgoing.setAttribute('aria-hidden','true');b.append(outgoing);
        outgoing.animate([{transform:'translateY(0)',opacity:1},{transform:'translateY(-39px)',opacity:0}],{duration:240,easing:'ease-out'}).finished.then(()=>outgoing.remove());
      }
    }
    const b=button('',()=>{const cycle=chosen===group[grade]&&group.length>1;if(cycle)grade=(grade+1)%group.length;chosen=group[grade];render(b,cycle);choose(chosen,b);},row);
    b.dataset.tutorialId='lbox_unit';b.dataset.tutorialRow=index;b.setAttribute('aria-label',manifest.profiles[group[0].profile].name);render(b,false);});
  while(row.children.length<6){const empty=document.createElement('div');empty.className='empty-recruit-card';row.append(empty);}
  choose(chosen,row.firstElementChild);
}
function openDefense(){
  let chosen=selection.installations[0];const body=detailWindow('defense_label',300,185,()=>act('installation',{offer:chosen}));const row=document.createElement('div');row.className='defense-cards';body.append(row);
  for(const [index,id] of selection.installations.entries()){const b=button('',()=>{chosen=id;for(const c of row.children)c.setAttribute('aria-pressed',String(c===b));},row);b.dataset.tutorialId='lbox_defense';b.dataset.tutorialRow=index;b.setAttribute('aria-label',id);b.append(uiImage('build_'+({trench:'moat',fence:'fences',bunker:'bunker'})[id]+'.png',94,64));}
}
function centerPlayer() {
  const unit = state.units.find(u => u.owner === state.playerFaction);
  const cell = unit?.cell ?? state.cells.findIndex(c => c.owner === state.playerFaction);
  const p = point(Math.max(0, cell)); view.x = p.x; view.y = p.y; view.zoom = Math.min(innerWidth / 568, innerHeight / 320); clampView();
}
function clampView() {
  if (!state || !manifest) return;
  const bounds = mapBounds || manifest.maps[state.map];
  view = Ew4Camera.constrain(view, {w:canvas.clientWidth || innerWidth, h:canvas.clientHeight || innerHeight}, bounds);
}
function point(cell) { return Ew4Battlefield.point(cell,state.width); }
function screenToWorld(x, y) { return {x:(x - viewport.w/2)/view.zoom + view.x, y:(y - viewport.h/2)/view.zoom + view.y}; }
function cellAt(x, y) {
  const p = screenToWorld(x, y); const approximateRow = Math.round((p.y+18) / 54); let best = -1, distance = Infinity;
  for (let r = approximateRow - 1; r <= approximateRow + 1; r++) {
    if (r < 0 || r >= state.height) continue;
    const approximateCol = Math.round((p.x - (r%2)*32)/64);
    for (let col = approximateCol - 1; col <= approximateCol + 1; col++) {
      if (col < 0 || col >= state.width) continue;
      const cell = r*state.width + col, q = point(cell), d = (q.x-p.x)**2+(q.y-p.y)**2;
      if (d < distance) { best=cell; distance=d; }
    }
  }
  return best;
}
function hex(x, y) { ctx.beginPath(); for(const [dx,dy] of [[0,-36],[32,-18],[32,18],[0,36],[-32,18],[-32,-18]]) { if(dx===0&&dy===-36)ctx.moveTo(x+dx,y+dy);else ctx.lineTo(x+dx,y+dy); } ctx.closePath(); }
function sprite(atlas, name, x, y, scale=1, draw=ctx) {
  let ratio=1;if(manifest.atlases[atlas+'_hd']?.[name]){atlas+='_hd';ratio=2;}
  const s=manifest.atlases[atlas]?.[name], texture=image('images/'+atlas+'.png');
  if(s && texture.complete && texture.naturalWidth) draw.drawImage(texture,s.x,s.y,s.w,s.h,x-s.refx*scale/ratio,y-s.refy*scale/ratio,s.w*scale/ratio,s.h*scale/ratio);
}
const unresolved = new Set();
function animation(lib, item, tick, depth=0, draw=ctx, frameFor=null) {
  if(!item || depth>20)return;
  draw.save();
  if(depth===0){const ratio=lib.pixelRatio||1;draw.scale(1/ratio,1/ratio);}
  if(item.kind) {
    const s=lib.sprites.get(item.name+'.png');
    if(s)draw.drawImage(lib.texture,+s.x,+s.y,+s.w,+s.h,item.pivot[0],item.pivot[1],+s.w,+s.h);
    else if(!unresolved.has(item.name)){unresolved.add(item.name);console.warn('Unresolved original sprite',item.name);}
  } else {
    const frame=frameFor?.(item) ?? Math.floor(tick)%item.duration;
    for(const layer of [...item.layers].reverse()) {
      let entry=layer[0];for(const candidate of layer){if(candidate.key<=frame)entry=candidate;else break;}
      for(const [elementId, childId] of entry.indices) {
        const e=lib.elements[elementId], child=lib.items[childId];if(!child)continue;
        const localTick=e.playback_mode===2?e.start_frame:e.playback_mode===1?Math.min(e.start_frame+frame-entry.key, child.duration-1):e.start_frame+frame-entry.key;
        draw.save();draw.transform(...e.transform);draw.globalAlpha*=e.alpha;animation(lib,child,localTick,depth+1,draw,frameFor);draw.restore();
      }
    }
  }
  draw.restore();
}
function drawStrategicMap(time=performance.now()) {
  const surface=$('strategic-map'), draw=surface.getContext('2d'), lib=libraries.get('strategicmap');
  if(!lib || !chosenScenario)return;
  const definition=manifest.strategic[chosenScenario]||{}, scale=Number(definition.scale||1)*.92;
  const textureScale=manifest.uiOverrides?.strategic?1:.8;
  const mapLeft=205-Number(definition.centerx||410)*scale;
  const mapRight=mapLeft+menuArt.strategic.image.naturalWidth/menuArt.strategic.ratio*textureScale*scale;
  const centerX=205+(manifest.uiOverrides?.strategic?0:mapLeft>0?-mapLeft:Math.max(0,410-mapRight));
  const rect=surface.getBoundingClientRect(),ratio=settings.hd?Math.min(devicePixelRatio||1,3):1;
  const width=Math.max(1,Math.round(rect.width*ratio)),height=Math.max(1,Math.round(rect.height*ratio));
  if(surface.width!==width||surface.height!==height){surface.width=width;surface.height=height;}
  draw.setTransform(surface.width/568,0,0,surface.height/320,0,0);draw.clearRect(0,0,568,320);draw.save();
  // Conquest overlays use an 80% backdrop; scenario centers remain in overlay coordinates.
  menuArt.drawStrategic(draw,definition,scale,centerX,160,textureScale);
  draw.translate(centerX,160);draw.scale(scale,scale);
  const strategic=lib.byName.get(definition.name||('conquest '+chosenScenario.slice(8)));if(strategic)animation(lib,strategic,time/180,0,draw);draw.restore();
}
function render(time) {
  renderRequest=0;
  if(document.hidden||!appVisible)return;
  if(!renderDirty&&time-lastRenderTime<1000/30-.5){renderRequest=requestAnimationFrame(render);return;}
  renderDirty=false;lastRenderTime=time;
  const playVisible=state&&!$('play').hidden;
  const menuVisible=!$('menu').hidden;
  const covered=['pause','settings','detail-modal','mod-manager','loading-screen'].some(id=>!$(id).hidden);
  const animated=menuVisible&&(!$('campaign-screen').hidden||!$('countries').hidden)||playVisible&&!covered&&!fastSkipping()&&
    (battlefield.animated||motions.size||arrivals.size||deaths.size||combatMotions.size||effects?.instances.length||damageNumbers?.instances.length||selection);
  if(animated)renderRequest=requestAnimationFrame(render);
  if(manifest&&menuVisible)menus.draw(time);
  positionSingleAction();
  if(tutorial&&!$('play').hidden)positionTutorial();
  if(tutorial?.wait?.name==='wait action'&&!busy&&!aiRunning)tutorial.notify({type:'action'}).catch(e=>notice(e.message));
  if(!$('countries').hidden&&!$('menu').hidden)drawStrategicMap(time);
  if(!playVisible||covered&&!stageIntroOpen||fastSkipping())return;
  const w=canvas.clientWidth,h=canvas.clientHeight;
  const dpr=settings.hd?Math.max(1,Math.min(window.devicePixelRatio||1,3,Math.sqrt(6*1024*1024/Math.max(1,w*h)))):1;
  if(canvas.width!==Math.round(w*dpr)||canvas.height!==Math.round(h*dpr)){canvas.width=Math.round(w*dpr);canvas.height=Math.round(h*dpr);}
  viewport={w,h,dpr};updateAiCamera(time);clampView();ctx.setTransform(dpr,0,0,dpr,0,0);ctx.fillStyle='#688e91';ctx.fillRect(0,0,w,h);
  ctx.translate(w/2,h/2);ctx.scale(view.zoom,view.zoom);ctx.translate(-view.x,-view.y);
  const min=screenToWorld(0,0),max=screenToWorld(w,h);
  battlefield.frame({draw:ctx,state,map:manifest.maps[state.map],visibleRect:{left:min.x,top:min.y,width:max.x-min.x,height:max.y-min.y},
    zoom:view.zoom,showGrid,selection,selected,selectedAt,motions,facings,arrivals,arrivalSpeed:Math.sqrt(settings.speed),deaths,combatMotions},time);
  effects?.frame(ctx,time,'foreground',battlefield.visibleRect);
  damageNumbers?.frame(ctx,time,battlefield.visibleRect);
  if(!animated&&battlefield.animated)requestRender();
}
async function tutorialUi(id,row=-1){if(id)await tutorial?.notify({type:'ui',id,row});}
async function beginTutorial(checkpoint){
  $('tutorial-talk').hidden=true;$('tutorial-focus').hidden=true;tutorialRect=null;
  const source=manifest.tutorials?.[state.scenario];if(!source){tutorial=null;return;}
  tutorial=new Ew4Tutorial.Runner(source,async command=>{
    const cell=Number(command.id);
    switch(command.name){
      case 'rand seed':break;
      case 'show text':$('tutorial-text').replaceChildren(originalTextRenderer.element(manifest.strings['desc_tutorials_word_'+command.id]||'','font_text_1_cn_hd','#404040',216));$('tutorial-speaker').replaceChildren(originalTextRenderer.element(manifest.strings.name_Napoleon||'拿破仑','font_text_2_cn_hd','#646464'));$('tutorial-portrait').src=menus.portrait('Napoleon.png').src;$('tutorial-talk').hidden=false;break;
      case 'hide text':$('tutorial-talk').hidden=true;break;
      case 'moveto area':{const p=point(cell);view.x=p.x;view.y=p.y;clampView();break;}
      case 'sel area':await selectCell(cell);break;
      case 'unsel area':selected=undefined;selection=undefined;$('selection').hidden=true;break;
      case 'draw rect':case 'draw ui rect':tutorialRect=command;positionTutorial();break;
      case 'clear rect':tutorialRect=null;$('tutorial-focus').hidden=true;break;
      case 'exit':await showMain();break;
      default:throw Error('Unknown tutorial command '+command.name);
    }
  });
  await tutorial.begin(checkpoint);
}
function positionTutorial(){
  if(!tutorialRect||!state){$('tutorial-focus').hidden=true;return;}
  const command=tutorialRect;let rect;
  if(command.name==='draw rect'){
    const p=point(Number(command.id));rect={left:(p.x-view.x+Number(command.x||0))*view.zoom+viewport.w/2,top:(p.y-view.y+Number(command.y||0))*view.zoom+viewport.h/2,width:Number(command.w)*view.zoom,height:Number(command.h)*view.zoom};
  }else{
    const controls={group_res:'#resources',group_incom:'#terrain-info',btn_next:'#end-turn',btn_undo:'#undo-move',winbtn_close:'#close-detail',winbtn_ok:'#detail-confirm',btn_done:pendingAction?'#single-action-confirm':'#detail-confirm',winbtn_back:'#hq-back',grid_general:'#hq-roster',lbox_unit:'.recruit-cards>button',lbox_defense:'.defense-cards>button'};
    const selector=controls[command.string]||'[data-tutorial-id="'+command.string+'"]';const candidates=[...document.querySelectorAll(selector)].filter(e=>e.getClientRects().length);
    const target=candidates[command.string==='grid_general'?0:command.row===undefined?0:Number(command.row)];
    if(target){rect=target.getBoundingClientRect();
      if(command.string==='grid_general'&&command.row!==undefined)rect={left:rect.left,top:rect.top+Number(command.row)*rect.height/2,width:rect.width,height:rect.height/2};
    }
  }
  $('tutorial-focus').hidden=!rect;if(!rect)return;
  Object.assign($('tutorial-focus').style,{left:rect.left+'px',top:rect.top+'px',width:Math.max(1,rect.width)+'px',height:Math.max(1,rect.height)+'px'});
}
let tutorialTouchTime=-Infinity;
document.addEventListener('pointerup',event=>{
  if(!tutorial||$('play').hidden||!$('pause').hidden||tutorial.wait?.name!=='wait touch')return;
  tutorialTouchTime=performance.now();event.preventDefault();event.stopPropagation();tutorial.notify({type:'touch'}).catch(e=>notice(e.message));
  pointers.clear();dragged=true;
},true);
document.addEventListener('click',event=>{if(performance.now()-tutorialTouchTime<350){event.preventDefault();event.stopPropagation();}},true);
let pointers=new Map(),dragged=false,lastDistance=0;
canvas.addEventListener('pointerdown',e=>{if(followingAi())return;canvas.setPointerCapture(e.pointerId);pointers.set(e.pointerId,{x:e.clientX,y:e.clientY,startX:e.clientX,startY:e.clientY});if(pointers.size===1)dragged=false;else{dragged=true;lastDistance=0;}});
canvas.addEventListener('pointermove',e=>{
  if(followingAi()){pointers.clear();return;}
  const last=pointers.get(e.pointerId);if(!last)return;
  const dx=e.clientX-last.x,dy=e.clientY-last.y;pointers.set(e.pointerId,{...last,x:e.clientX,y:e.clientY});
  if(pointers.size===1){view.x-=dx/view.zoom;view.y-=dy/view.zoom;clampView();if(Math.hypot(e.clientX-last.startX,e.clientY-last.startY)>4)dragged=true;}
  else{const[a,b]=[...pointers.values()],distance=Math.hypot(a.x-b.x,a.y-b.y);if(lastDistance)zoom(distance/lastDistance,(a.x+b.x)/2,(a.y+b.y)/2);lastDistance=distance;}
});
canvas.addEventListener('pointerup',e=>{pointers.delete(e.pointerId);if(!dragged&&pointers.size===0)selectCell(cellAt(e.clientX,e.clientY)).catch(error=>notice(error.message));});
canvas.addEventListener('pointercancel',e=>{pointers.delete(e.pointerId);dragged=true;});
function zoom(factor,x=viewport.w/2,y=viewport.h/2){const before=screenToWorld(x,y);view.zoom*=factor;clampView();const after=screenToWorld(x,y);view.x+=before.x-after.x;view.y+=before.y-after.y;clampView();}
canvas.addEventListener('wheel',e=>{e.preventDefault();if(!followingAi())zoom(Math.exp(-e.deltaY*.001),e.clientX,e.clientY);},{passive:false});
document.querySelectorAll('[data-mode]').forEach(b=>b.onclick=async()=>{if(!startupReady)return;mode=Number(b.dataset.mode);if(mode===4){menus.tutorialMenu();return;}$('main-menu').hidden=true;$('scenario-menu').hidden=false;$('countries').hidden=true;$('conquest-pagination').hidden=mode!==2;$('scenario-menu').classList.toggle('campaign-mode',mode===1);menuArt.apply(mode);$('mode-title').replaceChildren(labelImage(b.dataset.label,b.getAttribute('aria-label')));if(!await menus.open(mode))scenarioList();});
$('conquest-prev').onclick=()=>turnConquestPage(-1);
$('conquest-next').onclick=()=>turnConquestPage(1);
$('cancel-country').onclick=()=>$('countries').hidden=true;
$('confirm-country').onclick=()=>{if(chosenScenario&&chosenCountry&&!busy)start(chosenScenario,chosenCountry).catch(e=>notice(e.message));};
$('menu-back').onclick=showMain;
$('headquarters').onclick=()=>{if(startupReady)menus.headquarters().catch(e=>notice(e.message));};
const languageControls=[['city-language','cityLanguage','城市名称'],['geographic-language','geographicLanguage','地理名称']];
for(const [id,key,title] of languageControls){
  const row=document.createElement('div');row.className='setting-row language-row '+id;
  const caption=document.createElement('span');caption.textContent=title;row.append(caption);
  const choice=button('',()=>{settings[key]=settings[key]==='zh'?'en':'zh';syncLanguages();
    if(key==='cityLanguage')battlefield.cityLabels.clear();else battlefield.mapLabels.clear();persistSettings();},row);
  choice.id=id;choice.title=title+'语言';$('settings-content').append(row);
}
function syncLanguages(){for(const [id,key,title] of languageControls){const choice=$(id),text=settings[key]==='zh'?'中文':'English';
  choice.setAttribute('aria-label',title+'：'+text);choice.dataset.language=settings[key];
  if(originalTextRenderer)choice.replaceChildren(originalTextRenderer.element(text,'font_text_2_cn_hd','#404040'));
  else choice.textContent=text;
}}
function openSettings(){for(const [id,key] of [['music-volume','music'],['sound-volume','sound'],['game-speed','speed'],['screen-size','screen']])$(id).value=settings[key];syncLanguages();$('hd-mode').checked=settings.hd;$('show-grid').checked=showGrid;
  $('skip-turn-fast').checked=settings.skipTurnFast;$('follow-ai').checked=settings.followAi;
  const local=$('local-multiplayer-mode'),inGame=!$('play').hidden;
  local.disabled=inGame;local.checked=inGame?!!state?.localMultiplayer:!!settings.localMultiplayer;
  $('settings').hidden=false;}
$('main-settings').onclick=()=>{if(startupReady)openSettings();};$('pause-settings').onclick=openSettings;
$('close-settings').onclick=()=>$('settings').hidden=true;
$('confirm-settings').onclick=()=>$('settings').hidden=true;
for(const [id,key] of [['music-volume','music'],['sound-volume','sound'],['game-speed','speed'],['screen-size','screen'],['event-size','eventSize']])$(id).oninput=e=>{settings[key]=Number(e.target.value);persistSettings();sizeUi();};
$('hd-mode').onchange=e=>{settings.hd=e.target.checked;persistSettings();};
for(const [id,key] of [['skip-turn-fast','skipTurnFast'],['follow-ai','followAi']])$(id).onchange=e=>{
  settings[key]=e.target.checked;persistSettings();
  if(fastSkipping()){clearAiPresentation();aiFocus=null;}
  if(key==='followAi'){aiFocus=null;pointers.clear();}
  requestRender();
};
$('local-multiplayer-mode').onchange=e=>{if(!$('play').hidden){e.target.checked=!!state?.localMultiplayer;return;}settings.localMultiplayer=e.target.checked;persistSettings();};
function persistSettings(){try{localStorage.setItem('ew4.settings',JSON.stringify(settings));}catch(_){} }
$('close-detail').onclick=async()=>{closeDetail();await tutorialUi('winbtn_close');};
$('show-grid').onchange=e=>{showGrid=e.target.checked;try{localStorage.setItem('ew4.showGrid',String(showGrid));}catch(_){notice('无法保存设定');}};
$('menu-button').onclick=()=>$('pause').hidden=false;
$('resume').onclick=()=>{$('pause').hidden=true;if(!busy)runAi().catch(e=>notice(e.message));};
$('end-turn').onclick=()=>act('end').catch(e=>notice(e.message));
$('undo-move').onclick=()=>{if(!selected||!selection?.undo||$('undo-move').disabled)return;return act('undo',{unit:selected.id}).then(()=>tutorialUi('btn_undo')).catch(e=>notice(e.message));};
$('next-unit').onclick=()=>findNextUnit().catch(e=>notice(e.message));
$('skip-ai').onclick=()=>{skipAi=true;clearAiPresentation();if(fastSkipping())aiFocus=null;refreshHud();requestRender();};
$('dialogue').onclick=()=>{
  if($('dialogue').hidden||!$('pause').hidden||!$('settings').hidden||state?.scenarioPopup)return;
  act('dialogue').catch(e=>notice(e.message));
};
$('save').onclick=()=>{
  if(busy||aiRunning||sessionTransition){notice('请等待当前行动完成后再保存');return;}
  if(state)return menus.saveMenu(true,catalog.scenarios.find(s=>s.id===state.scenario).mode,tutorial?.checkpoint()).catch(e=>notice(e.message));
};
async function load(slot=0,saveMode=mode){if(busy)return;busy=true;try{await withLoading(async()=>{tutorial=null;stageIntroOpen=false;await installState(await rpc('load',{slot,mode:saveMode}),true);$('detail-modal').hidden=true;showStageIntro();await beginTutorial(state.tutorialCheckpoint);});}catch(e){notice(e.message);}finally{busy=false;if(state){refreshHud();await runAi();}}}
$('continue').onclick=()=>menus.saveMenu(false,mode).catch(e=>notice(e.message));
$('restart').onclick=async()=>{
  if(!state||sessionTransition)return;
  if(busy&&!aiRunning){notice('请等待当前行动完成');return;}
  sessionTransition=true;
  const scenario=state.scenario,country=catalog.scenarios.find(s=>s.id===scenario)?.mode===2?(state.startCountry??state.factions.find(f=>f.id===state.playerFaction).country):null;
  const players=state.localMultiplayer?.humanFactions??null;
  try{await stopAi();await start(scenario,country,players);}catch(e){notice(e.message);}
  finally{sessionTransition=false;}
};
$('return-menu').onclick=()=>{if(busy&&!aiRunning){notice('请等待当前行动完成');return;}return showMain();};
window.ew4Back=()=>{
  if(pendingAction){closeSingleAction();return 'handled';}
  const visible=id=>!$(id).hidden;
  const modal=document.querySelector('dialog[open]');
  if(modal){modal.close();return 'handled';}
  for(const [panel,close] of [['settings','close-settings'],['detail-modal','close-detail'],['mod-manager','mod-close']]){
    if(visible(panel)){$(close).click();return 'handled';}
  }
  if(visible('loading-screen'))return 'handled';
  if(visible('hq-screen')){$('hq-back').click();return 'handled';}
  if(visible('play')){$(visible('pause')?'resume':'menu-button').click();return 'handled';}
  if(visible('campaign-screen')){$('campaign-back').click();return 'handled';}
  if(visible('countries')){$('cancel-country').click();return 'handled';}
  const preview=document.querySelector('.chapter-preview');
  if(preview){preview.remove();return 'handled';}
  if(visible('scenario-menu')){$('menu-back').click();return 'handled';}
  return visible('main-menu')?'exit':'handled';
};
window.addEventListener('resize',()=>{sizeUi();requestRender();});sizeUi();
function startupStatus(){
  const failures=Ew4OriginalUI.fontStatus.failures;
  $('startup-status').hidden=startupReady&&!failures.length&&!startupRunning;
  $('startup-message').textContent=startupRunning?'正在载入…':startupError||
    (failures.length?'字体载入失败，已使用内置备用字体：\n'+failures.map(face=>face.file+'：'+face.message).join('\n'):'');
  $('startup-retry').hidden=startupRunning||!startupError&&!failures.length;
  sizeUi();
}
async function startupJson(url){
  const response=await fetch(url,{cache:'no-store'});
  if(!response.ok)throw Error('资源载入失败：'+url+' ('+response.status+')');
  return response.json();
}
async function initializeClient(){
  if(startupRunning)return;
  startupRunning=true;startupError='';startupStatus();
  document.querySelectorAll('.main-entry').forEach(button=>button.disabled=true);$('continue').disabled=true;
  try{
    Ew4Startup.pending('游戏目录与资源索引');
    const [c,m,names,fx,pages,visuals,presentation]=await Promise.all([rpc('catalog'),startupJson('/content/manifest.json'),startupJson('city-names.json'),startupJson('/content/effects.json'),startupJson('conquest-pages.json'),startupJson('/content/unit-visuals.json'),startupJson('/content/presentation.json')]);
    Ew4Startup.pending('内置字库');
    const text=new Ew4OriginalUI.TextRenderer(m);await text.prepare();
    Ew4Startup.pending('菜单图像');
    const art=new Ew4MenuArt.MenuArt({manifest:m,image});await art.prepare();
    catalog=c;manifest=m;cityNames=names;conquestPages=pages;menuArt=art;
    fx.elements??=presentation.elements;fx.sounds=[...new Set([...fx.sounds,...presentation.sounds])];
    effects=new Ew4Effects.Effects({image,sprite,animation,libraries,get manifest(){return manifest;},volume:()=>settings.sound},fx,visuals);
    effects.preloadSounds(presentation.sounds);
    originalTextRenderer=text;
    Ew4Startup.pending('战斗图像与界面');
    const fightFonts=await startupJson('/content/fight-font.json');
    const fightFont='font_fight_num_hd';
    const [fightSheet]=await Promise.all([loadImage('images/'+fightFont+'.png'),loadImage('images/eff.png')]);
    const fightMetrics=manifest.fonts[fightFont]||fightFonts[fightFont];
    damageNumbers=new Ew4Effects.DamageNumbers(fightMetrics,fightSheet);originalLabels();showMain();
    startupReady=true;
    Ew4Startup.ready();
    document.querySelectorAll('.main-entry').forEach(button=>button.disabled=false);$('continue').disabled=false;
  }catch(error){startupError=error.message||String(error);}
  finally{Ew4Startup.stop();startupRunning=false;startupStatus();}
}
$('startup-retry').onclick=async()=>{
  if(startupRunning)return;
  startupRunning=true;startupError='';startupStatus();
  try{await Ew4OriginalUI.retryFonts();}
  catch(error){startupError=error.message||String(error);}
  finally{startupRunning=false;startupStatus();}
  if(!startupReady&&!startupError){for(const [key,img] of images)if(img.complete&&!img.naturalWidth)images.delete(key);await initializeClient();}
};
window.addEventListener('ew4fontstatus',startupStatus);
window.addEventListener('ew4fontsloaded',()=>{battlefield.mapLabels.clear();battlefield.cityLabels.clear();requestRender();});
void initializeClient();
requestRender();
