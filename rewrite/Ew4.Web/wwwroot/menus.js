'use strict';
(function(root){
  class Menus {
    constructor(api){this.api=api;this.chapter=null;this.rating=null;this.rosterRating=null;this.recruiting=false;this.tagDraft=null;this.tagSaving=false;
      addEventListener('resize',()=>requestAnimationFrame(()=>this.fitStageIntro()));
      document.getElementById('campaign-back').onclick=()=>{document.getElementById('campaign-screen').hidden=true;};
      document.getElementById('hq-back').onclick=()=>{
        if(this.tagSaving)return;
        if(this.tagDraft){this.tagDraft=null;this.drawRoster();return;}
        if(this.deployment){this.closeRoster();this.api.tutorialUi('winbtn_back');return;}
        if(this.recruiting){this.recruiting=false;this.drawRoster();return;}
        this.api.main();
      };
      document.getElementById('hq-recruit').onclick=()=>{this.recruiting=true;this.rating=0;this.offer=null;this.drawRoster();};
      const roster=document.getElementById('hq-roster');let drag=null,dragged=false;
      roster.onpointerdown=e=>{if(this.recruiting||e.button!==0)return;dragged=false;drag={id:e.pointerId,x:e.clientX,scroll:roster.scrollLeft};};
      roster.onpointermove=e=>{
        if(!drag||e.pointerId!==drag.id)return;
        const scale=roster.getBoundingClientRect().width/roster.offsetWidth,dx=(e.clientX-drag.x)/scale;
        if(!dragged&&Math.abs(e.clientX-drag.x)>6){dragged=true;roster.setPointerCapture(e.pointerId);roster.classList.add('dragging');}
        if(dragged){e.preventDefault();roster.scrollLeft=drag.scroll-dx;}
      };
      const endDrag=()=>{drag=null;roster.classList.remove('dragging');};
      roster.onpointerup=endDrag;roster.onpointercancel=endDrag;
      // Touch capture transfers from the card to the roster; ignore the child's loss.
      roster.onlostpointercapture=e=>{if(e.target===roster)endDrag();};
      roster.addEventListener('click',e=>{if(dragged){e.preventDefault();e.stopImmediatePropagation();dragged=false;}},true);
      roster.ondragstart=e=>e.preventDefault();
    }
    get manifest(){return this.api.manifest;}
    get catalog(){return this.api.catalog;}
    countryFlag(id,width,height){return this.api.countryImage?.(id,width,height)||this.api.uiImage(id+'1.png',width,height);}
    text(key){return this.manifest.strings[key]||key;}
    lettering(text,font='font_text_2_cn_hd',color='#404040',width,align,options){return this.api.text(text,font,color,width,align,options);}
    slideInSidebar(list){
      const rows=[...list.children];
      for(const node of [list,...rows])for(const animation of node.getAnimations?.()||[])animation.cancel();
      if(matchMedia('(prefers-reduced-motion: reduce)').matches)return;
      const easing='cubic-bezier(.2,.7,.25,1)';
      // Separate translate from the selected row's transform so both offsets compose.
      list.animate([{translate:'180px 0'},{translate:'0 0'}],{duration:220,easing});
      rows.forEach((row,index)=>row.animate([{translate:'160px 0'},{translate:'0 0'}],
        {duration:300,delay:Math.min(index,5)*60,easing,fill:'backwards'}));
    }
    async open(mode){
      if(mode===4)return this.tutorialMenu();
      if(mode!==1)return false;
      const list=document.getElementById('scenarios');list.replaceChildren();list.className='chapter-list';
      const layout=this.manifest.layouts.form_selcampaign;
      const custom=this.catalog.chapters.filter(chapter=>!layout.children.some(e=>e.attributes.id==='btn_selcamp_'+chapter.id));
      if(custom.length)list.classList.add('extended-chapters');
      for(const chapter of this.catalog.chapters){const placement=layout.children.find(e=>e.attributes.id==='btn_selcamp_'+chapter.id)?.attributes;
        const b=this.api.button('',()=>this.previewChapter(chapter,b),list);b.className='chapter-entry';b.setAttribute('aria-label',chapter.name);
        if(placement){Object.assign(b.style,{left:placement.x+'px',top:placement.y+'px',width:placement.w+'px',height:placement.h+'px'});b.append(this.api.uiImage(placement.frm1,Number(placement.w),Number(placement.h)));}
        else{b.classList.add('custom-chapter');Object.assign(b.style,{left:'356px',top:(14+custom.indexOf(chapter)*48)+'px',width:'185px',height:'40px'});b.append(this.lettering(chapter.name,'font_text_2_cn_hd','#404040',170,'center'));}
        b.title=chapter.name+' '+chapter.start+'–'+chapter.end;
      }
      document.getElementById('scenario-menu').onclick=e=>{if(!e.target.closest('.chapter-entry,.chapter-preview'))document.querySelector('.chapter-preview')?.remove();};
      return true;
    }
    previewChapter(chapter,anchor){
      document.querySelector('.chapter-preview')?.remove();
      const popup=document.createElement('section');popup.className='chapter-preview';popup.setAttribute('aria-label',chapter.name+' '+chapter.start+'-'+chapter.end);
      const title=document.createElement('div');title.className='chapter-preview-title';title.append(this.lettering(chapter.name,'font_text_2_cn_hd','#ebe6e1',139,'center',{singleLine:true}));
      const years=document.createElement('div');years.className='chapter-preview-years';years.append(this.lettering(chapter.start+'-'+chapter.end,'font_char_2_hd','#404040'));
      const flags=document.createElement('div');flags.className='chapter-preview-flags';for(const country of chapter.countries)flags.append(this.countryFlag(country,26,15));
      popup.append(title,years,flags);const confirm=this.api.button('',()=>{popup.remove();return this.openChapter(chapter);},popup);confirm.className='chapter-preview-confirm';confirm.setAttribute('aria-label','进入 '+chapter.name);confirm.title='进入 '+chapter.name;confirm.append(this.api.uiImage('button_confrim.png',31,31.5));
      // The visible marker tip is (25, 54); its texture includes transparent shadow margins.
      const targetX=parseFloat(anchor.style.left)+25,targetY=parseFloat(anchor.style.top)+54;
      const left=Math.max(4,Math.min(425,targetX-69.5));
      popup.style.left=left+'px';popup.style.top=Math.max(24,targetY-69.5)+'px';popup.style.setProperty('--chapter-arrow-left',(targetX-left-16.5)+'px');
      document.getElementById('scenarios').append(popup);confirm.focus({preventScroll:true});
    }
    async openChapter(chapter){
      await this.api.refreshCatalog();
      await this.api.loadLibrary('strategicmap');this.chapter=chapter;
      // Chapter names are not all present in the limited title atlas. Use one complete
      // original font for the entire title instead of mixing different glyph designs.
      document.getElementById('campaign-title').replaceChildren(this.lettering(chapter.name,'font_text_2_cn_hd','#ebe6e1'));
      const list=document.getElementById('campaign-battles');list.replaceChildren();
      const chapterArt=['france','coalition','holyroman','east','usa','uk'][chapter.id-1]||'france';
      list.style.setProperty('--chapter-entry-image','url("/content/ui/image_menu_hd/button_choosestage_'+chapterArt+'.png")');
      for(const presentation of Object.values(this.catalog.presentations).filter(s=>s.chapter===chapter.id)){
        if(presentation.hidden&&!this.catalog.unlockedCampaigns?.includes(presentation.id))continue;
        const scenario=this.catalog.scenarios.find(s=>s.id===presentation.id);if(!scenario)continue;
        const b=this.api.button('',()=>this.chooseBattle(presentation,b),list);b.className='country-entry';b.dataset.campaign=presentation.id;b.setAttribute('aria-label',scenario.name);b.setAttribute('aria-pressed','false');b.append(this.lettering(scenario.name,'font_text_2_cn_hd','#404040',132,'center',{singleLine:true}));
        const stars=Math.max(0,Math.min(5,this.catalog.campaignStars?.[presentation.id]||0));
        if(stars){const strip=document.createElement('span');strip.className='campaign-stars';strip.setAttribute('aria-label',stars+'星');
          // Native 440a4 uses HD points (8,8), (12,22), (16,36), (12,50), (8,64).
          for(let i=0;i<stars;i++){const image=this.api.uiImage('star_campaign.png',7.5,7);image.style.left=[4,6,8,6,4][i]+'px';image.style.top=[4,11,18,25,32][i]+'px';strip.append(image);}b.append(strip);}
      }
      document.getElementById('campaign-screen').hidden=false;list.scrollTop=0;list.firstElementChild?.click();
      this.slideInSidebar(list);
    }
    chooseBattle(presentation,button){
      this.battle=presentation;
      for(const b of document.getElementById('campaign-battles').children)b.setAttribute('aria-pressed',String(b===button));
      document.getElementById('campaign-description').replaceChildren(this.lettering(presentation.description.startsWith('desc_')?'':presentation.description,'font_text_1_cn_hd','#404040',267));
      document.getElementById('campaign-description').scrollTop=0;
      const commander=document.getElementById('campaign-commander');commander.replaceChildren();
      if(presentation.commander){commander.append(this.portrait(presentation.commander+'.png'));const name=document.createElement('span');name.append(this.lettering(this.text('name_'+presentation.commander),'font_text_2_cn_hd','#ebe6e1',70,'center'));commander.append(name);}
      document.getElementById('campaign-confirm').onclick=()=>this.confirmBattle(presentation).catch(e=>this.api.notice(e.message));
    }
    async confirmBattle(presentation){
      if(presentation.alternate&&presentation.countries.length){
        let chosen=0;const body=this.api.detail('country_label',234,150,()=>this.api.start(chosen?presentation.alternate:presentation.id,null));body.className='scenario-countries';
        presentation.countries.forEach((country,index)=>{const name=this.api.catalog?.countries?.[country]?.name||this.text('name_'+country),b=this.api.button('',()=>{chosen=index;for(const c of body.children)c.setAttribute('aria-pressed',String(c===b));},body);b.setAttribute('aria-label',name);b.append(this.countryFlag(country,51,30),this.api.labels('country_'+country,name));if(!index)b.click();});
      }else await this.api.start(presentation.id,null);
    }
    stageIntro(intro,onClose){
      const screen=intro.presentation||{},bindings=screen.bindings||{description:'text_desc',commander:'tcmder',minimumRatingRound:'text_victory',highestRatingRound:'text_bestvic'};
      const api=screen.layout?{...this.api,manifest:{...this.manifest,layouts:{...this.manifest.layouts,form_mod_stageintro:screen.layout}}}:this.api;
      const l=this.introLayout=new Ew4NativeUI.Layout(api,screen.layout?'form_mod_stageintro':'form_stageintro',onClose);
      l.body.classList.add('stage-intro');this.fitStageIntro();
      document.getElementById('detail-title').replaceChildren(this.lettering(intro.title,'font_text_4_cn_hd','#ebe6e1',300,'center',{singleLine:true}));
      if(bindings.commander){
        if(intro.commander){const portrait=document.createElement('div');portrait.className='commander-card';
          const name=document.createElement('span');name.className='commander-name';name.append(this.lettering(this.text('name_'+intro.commander),'font_text_2_cn_hd','#ebe6e1',78,'center',{singleLine:true}));portrait.append(this.portrait(intro.commander+'.png'),name);l.fill(bindings.commander,portrait);
        }else l.hide(bindings.commander);
      }
      const prose=(key,value)=>{if(bindings[key]){const target=l.get(bindings[key]),a=target.nativeAttributes,width=parseFloat(target.style.width)-10;
        target.classList.add('stage-intro-scroll');target.tabIndex=0;target.setAttribute('aria-label',key==='description'?'战役说明':'任务目标');
        const font=(a.font||'font_text_1').replace(/^(font_text_\d)$/, '$1_cn_hd'),color='rgba('+(a.color||a.textcolor||'64,64,64,255').split(',').map((v,i)=>i===3?Number(v)/255:Number(v)).join(',')+')';
        const text=value=>this.api.prose(this.text(value||''),font,color,width);
        target.replaceChildren(text(value));
        if(key==='description')for(const block of screen.content||[]){const node=document.createElement('figure');node.className='stage-intro-block';node.dataset.contentType=block.type;
          if(block.type==='text')node.append(text(block.text));
          else{const image=block.type==='commander'?this.portrait(block.commander+'.png'):block.image.startsWith('rewrite/')?Object.assign(document.createElement('img'),{src:'/content/extensions/'+block.image.slice(8).split('/').map(encodeURIComponent).join('/')}):this.api.uiImage(block.image);
            image.removeAttribute('width');image.removeAttribute('height');image.classList.add('stage-intro-image');image.style.width=Math.min(width,block.width||(block.type==='commander'?78:width))+'px';image.alt=this.text(block.text||(block.type==='commander'?'name_'+block.commander:''));image.draggable=false;node.append(image);
            const caption=block.text||(block.type==='commander'?'name_'+block.commander:'');if(caption)node.append(text(caption));}
          target.append(node);}
        if(key==='description'&&intro.objectives&&!bindings.objectives){const goal=document.createElement('div');goal.className='stage-intro-objectives';goal.append(text(intro.objectives));target.append(goal);}
      }};
      prose('description',intro.description);prose('objectives',intro.objectives);
      for(const [group,key,binding,override,value] of [['group_condwin','text_victory','minimumRatingRound',screen.victoryText,intro.minimumRatingRound],['group_condlost','text_bestvic','highestRatingRound',screen.bestVictoryText,intro.highestRatingRound]]){
        if(!screen.layout){const heading=document.createElement('div');heading.className='stage-condition-title';heading.append(this.lettering(this.text(key),'font_text_2_cn_hd','#ebe6e1',176,'center',{singleLine:true}));l.get(group).append(heading);
          if(bindings[binding])l.get(bindings[binding]).style.height='32px';
          if(override!=null)l.hide(group+'/text_round');}
        if(bindings[binding])l.text(bindings[binding],override==null?value:this.text(override));
      }
      if(bindings.victoryLabel)l.text(bindings.victoryLabel,this.text('text_victory'));
      if(bindings.bestVictoryLabel)l.text(bindings.bestVictoryLabel,this.text('text_bestvic'));
      if(bindings.close)l.action(bindings.close,'关闭',()=>this.api.closeDetail());
    }
    fitStageIntro(){
      const l=this.introLayout;if(!l?.current())return;
      const base=Number(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale'))||1;
      l.body.parentElement.style.setProperty('--ui-scale',Math.min(base,(innerWidth-24)/l.width,(innerHeight-24)/l.height));
    }
    draw(time){
      if(document.getElementById('campaign-screen').hidden||!this.battle)return;
      const surface=document.getElementById('campaign-map'),draw=surface.getContext('2d'),lib=this.api.libraries.get('strategicmap');if(!lib)return;
      const rect=surface.getBoundingClientRect(),ratio=Math.min(devicePixelRatio||1,3),w=Math.round(rect.width*ratio),h=Math.round(rect.height*ratio);
      if(surface.width!==w||surface.height!==h){surface.width=w;surface.height=h;}
      draw.setTransform(w/568,0,0,h/320,0,0);draw.clearRect(0,0,568,320);
      const definition=this.manifest.strategic[this.battle.id]||{},scale=Number(definition.scale||1),textureScale=this.manifest.uiOverrides?.strategic?1:.8;
      const camera=this.api.menuArt.strategicCamera(definition,scale,textureScale);
      draw.save();this.api.menuArt.drawStrategic(draw,definition,scale,camera.x,camera.y,textureScale);
      draw.translate(camera.x,camera.y);draw.scale(scale,scale);if(lib.byName.has(definition.name))this.api.animation(lib,lib.byName.get(definition.name),time/180,0,draw);draw.restore();
    }
    tutorialMenu(){
      const body=this.api.detail('tutorial_label',250,200);body.className='tutorial-menu';
      for(const [title,action] of [['btn_basic',()=>this.beginTutorial('tutorials1')],['btn_classic',()=>this.beginTutorial('tutorials2')],['btn_notice',()=>this.playNotice()]]){
        const b=this.api.button(this.text(title),action,body);b.className='native-command';
      }
      return true;
    }
    async beginTutorial(id){document.getElementById('detail-modal').hidden=true;await this.api.start(id,null);}
    playNotice(){const body=this.api.detail('tutorial_label',400,225);body.className='play-notice';const p=document.createElement('p');
      // The source uses a small markup vocabulary; show its text without executing markup.
      p.textContent=this.text('html_notice').replace(/<[^>]*>/g,'').replace(/\\n/g,'\n');body.append(p);
    }
    portrait(name){
      const img=document.createElement('img');img.alt='';img.width=78;img.height=78;
      const atlas=['princesses_hd','commanders1_hd','commanders2_hd'].find(a=>this.manifest.atlases[a]?.[name]);
      if(atlas)img.src='/content/ui/'+atlas+'/'+name;else{img.src='/content/ui/image_ui_hd/button_add_generals.png';img.title=name;}
      return img;
    }
    async headquarters(deployment=null){
      this.data=await this.api.rpc('headquarters');this.deployment=deployment;this.recruiting=false;this.tagDraft=null;
      if(deployment)document.getElementById('play').hidden=true;
      for(const id of ['main-menu','scenario-menu','countries','campaign-screen'])document.getElementById(id).hidden=true;
      document.getElementById('hq-screen').hidden=false;document.getElementById('menu').hidden=false;
      if(deployment)document.getElementById('hq-roster').scrollLeft=0;
      this.drawRoster();
    }
    async deployGeneral(unit,revision){
      const available=await this.api.rpc('deployment',{unit,revision});
      this.deploymentQuota=available;
      await this.headquarters(unit);
      this.data.roster=this.data.roster.map(c=>({...c,locked:!available.commanders.includes(c.id)}));
      this.drawRoster();
    }
    closeRoster(){document.getElementById('hq-screen').hidden=true;if(this.deployment){document.getElementById('menu').hidden=true;document.getElementById('play').hidden=false;}this.deployment=null;}
    card(definition,held,action){
      const card=document.createElement('div');card.className='commander-card';card.setAttribute('role','group');card.setAttribute('aria-label',definition.name);card.append(this.portrait(definition.image));
      const name=document.createElement('span');name.className='commander-name';name.append(this.lettering(definition.name,'font_text_2_cn_hd','#ebe6e1',74,'center',{singleLine:true}));card.append(name);
      const rank=held?.learning.rank.level??definition.rank,title=held?.learning.title.level??definition.title;
      if(rank>0){const img=this.api.uiImage('rank_'+Math.min(rank,14)+'.png',21,24);img.className='commander-rank';card.append(img);}
      if(title>0){const img=this.api.uiImage('class_'+Math.min(title,9)+'.png',21,24);img.className='commander-title';card.append(img);}
      const open=document.createElement('button');open.className='commander-open';open.setAttribute('aria-label',definition.name);open.onclick=action;card.append(open);
      const info=document.createElement('button');info.className='commander-info';info.setAttribute('aria-label',definition.name+'信息');info.title='将领信息';info.onclick=e=>{e.stopPropagation();this.commanderInfo(definition,held);};info.append(this.api.uiImage('button_generalinfo_blue.png',20,20));card.append(info);
      if(held?.tag){const tag=document.createElement('span');tag.className='commander-tag';card.title='已标记';tag.setAttribute('aria-label','已标记');card.append(tag);}
      if(held?.locked){card.dataset.locked='true';card.title=(card.title?card.title+' · ':'')+'已派遣';}return card;
    }
    drawRoster(){
      const data=this.data,screen=document.getElementById('hq-screen');screen.classList.toggle('recruiting',this.recruiting);screen.classList.toggle('tag-editing',!!this.tagDraft);screen.classList.toggle('deploying',!!this.deployment);
      const heading=screen.querySelector('h1'),headingKey=this.deployment?'title_deploygeneral':'title_headquarters';heading.dataset.label=headingKey;heading.setAttribute('aria-label',this.text(headingKey));heading.replaceChildren(this.api.labels(headingKey,this.text(headingKey),heading));
      document.getElementById('hq-medals').replaceChildren(this.api.uiImage('medals.png',13,17),this.lettering(data.medals,'font_char_2_hd','#ebe6e1'),this.api.uiImage('badges.png',13,17),this.lettering(data.badges,'font_char_2_hd','#ebe6e1'));
      const recruit=document.getElementById('hq-recruit');recruit.replaceChildren(this.lettering(this.text('btn_college'),'font_text_3_cn_hd','#ebe6e1'));recruit.setAttribute('aria-label',this.text('btn_college'));recruit.hidden=!!this.deployment;
      recruit.disabled=!!this.tagDraft;
      document.getElementById('hq-back').disabled=this.tagSaving;
      const definitions=data.definitions,byId=new Map(definitions.map(d=>[d.id,d])),owned=new Map(data.roster.map(c=>[c.definitionId,c]));
      const section=d=>Math.max(0,Math.min(2,Math.floor((d.rating-1)/3)));
      const entries=this.recruiting?(data.offers[this.rating]||[]).map(id=>byId.get(id)).filter(Boolean):data.roster.map(c=>byId.get(c.definitionId)).filter(d=>d&&(this.deployment||this.rosterRating===null||section(d)===this.rosterRating))
        .sort((a,b)=>Number(!!owned.get(b.id)?.tag)-Number(!!owned.get(a.id)?.tag)||section(b)-section(a));
      const filters=document.getElementById('hq-filters');filters.replaceChildren();
      if(this.recruiting)for(const [rating,count] of [6,4,2].entries()){
        const b=this.api.button('',()=>{this.rating=rating;this.offer=null;this.drawRoster();},filters);b.setAttribute('role','tab');b.setAttribute('aria-label',['一星招募区','二星招募区','三星招募区'][rating]);b.setAttribute('aria-selected',String(rating===this.rating));b.append(this.api.uiImage('button_general_'+count+'.png',152,55));
      }
      else if(!this.deployment)for(const rating of [null,0,1,2]){
        const label=rating===null?'全选':['一星区','二星区','三星区'][rating];
        const b=this.api.button('',()=>{this.rosterRating=rating;document.getElementById('hq-roster').scrollLeft=0;this.drawRoster();},filters);
        b.setAttribute('role','tab');b.setAttribute('aria-label',label);b.title=label;b.setAttribute('aria-selected',String(rating===this.rosterRating));
        if(rating===null){b.className='native-command blue';b.append(this.lettering(label,'font_text_3_cn_hd','#ebe6e1'));}
        else b.append(this.api.uiImage('button_general_'+[6,4,2][rating]+'.png',110,40));
      }
      const roster=document.getElementById('hq-roster'),scroll=roster.scrollLeft;roster.replaceChildren();
      roster.dataset.count=this.recruiting?[6,4,2][this.rating]:entries.length;
      for(const definition of entries){
        const held=owned.get(definition.id),shown=this.tagDraft&&held?{...held,tag:this.tagDraft.has(held.id)?'置顶':''}:held,b=this.card(definition,shown,()=>{
          if(this.tagDraft){if(!this.tagSaving){if(this.tagDraft.has(held.id))this.tagDraft.delete(held.id);else this.tagDraft.add(held.id);this.drawRoster();}return;}
          if(this.recruiting){this.offer=definition;this.drawPurchases();for(const c of roster.children)c.setAttribute('aria-pressed',String(c===b));}
          else {this.rosterSelected=definition.id;for(const c of roster.children)c.setAttribute('aria-pressed',String(c===b));if(!this.deployment)this.commanderInfo(definition,held);}
        });roster.append(b);if(held)b.dataset.commander=held.id;
        if(this.deployment){
          b.querySelector('.commander-info').onclick=e=>{e.stopPropagation();this.commanderInfo(definition,held,()=>this.drawRoster());};
          const deploy=this.api.button('',async()=>{await this.api.deploy(this.deployment,held.id);this.closeRoster();},b);deploy.className='deploy-card-confirm';deploy.setAttribute('aria-label','任命 '+definition.name);deploy.disabled=held.locked;deploy.append(this.lettering(this.text('btn_deploy'),'font_text_3_cn_hd','#ebe6e1'));
        }
        if(this.tagDraft)b.querySelector('.commander-open').setAttribute('aria-pressed',String(this.tagDraft.has(held.id)));
        if(!this.recruiting&&!this.deployment)this.promotionShortcut(b,held,()=>this.drawRoster());
        b.setAttribute('aria-pressed',String(definition.id===(this.recruiting?this.offer?.id:this.rosterSelected)));
      }
      const capacity=data.sectionCapacity??20,slots=this.recruiting?[6,4,2][this.rating]:this.deployment?Math.max(12,entries.length):Math.max(this.rosterRating===null?capacity*3:capacity,entries.length);
      for(let i=entries.length;i<slots;i++){const b=this.api.button('',()=>{if(!this.deployment){this.recruiting=true;this.rating=this.rosterRating??0;this.drawRoster();}},roster);b.className='commander-card empty-commander';b.setAttribute('aria-label','空将领位');b.append(this.api.uiImage('button_add_generals.png',78,78));const name=document.createElement('span');name.className='commander-name';b.append(name);b.disabled=this.recruiting||!!this.deployment||!!this.tagDraft;}
      if(!this.recruiting)for(const [i,card] of [...roster.children].entries()){card.style.gridRow=i%2+1;card.style.gridColumn=Math.floor(i/2)+1;}
      for(const id of ['hq-recruit-band','hq-refresh','hq-buy-buttons'])document.getElementById(id)?.remove();
      if(this.recruiting){const band=document.createElement('div');band.id='hq-recruit-band';screen.insertBefore(band,roster);
        const refresh=this.api.button('',async()=>{this.offer=null;await this.transact({op:'refresh',group:this.rating});},screen);refresh.id='hq-refresh';refresh.className='native-command blue';refresh.setAttribute('aria-label','刷新将领');for(let i=0;i<3;i++)refresh.append(this.api.uiImage('button_refresh.png',13,16));
        const remaining=data.refresh?.[this.rating]?.remaining;if(remaining!=null){refresh.append(this.lettering(remaining,'font_char_2_hd','#ebe6e1'));refresh.title='剩余 '+remaining+' 次';}this.drawPurchases();}
      roster.scrollLeft=scroll;this.headquartersControls();
      const count=document.getElementById('hq-count');
      count.hidden=this.recruiting;count.replaceChildren(this.lettering(entries.length+' / '+slots,'font_char_2_hd','#404040'));
      document.getElementById('hq-deployment-quota')?.remove();
      if(this.deployment){const quota=document.createElement('div');quota.id='hq-deployment-quota';quota.append(this.api.uiImage('generalnumber.png',26,19),this.lettering((this.deploymentQuota?.used??0)+' / '+(this.deploymentQuota?.limit??'∞'),'font_char_4_hd','#404040'));screen.append(quota);}
    }
    drawPurchases(){
      document.getElementById('hq-buy-buttons')?.remove();if(!this.offer)return;
      const row=document.createElement('div');row.id='hq-buy-buttons';document.getElementById('hq-screen').append(row);
      for(const [currency,op,price,balance] of [['medals','recruit',this.offer.price,this.data.medals],['badges','recruit_badge',this.offer.badgePrice,this.data.badges]]){
        if(price<=0)continue;const b=this.api.button('',async()=>{const definition=this.offer;this.offer=null;await this.transact({op,commander:definition.id,group:this.rating});},row);b.className='native-command'+(currency==='badges'?' red':'');b.setAttribute('aria-label',(currency==='badges'?'徽章':'勋章')+'招募 '+price);b.disabled=balance<price;b.append(this.api.uiImage(currency+'.png',13,17),this.lettering(price,'font_text_3_cn_hd','#ebe6e1'));
      }
      this.api.popup(row);
    }
    async transact(args){this.data=await this.api.rpc('commander',{revision:this.data.revision,...args});this.drawRoster();}
    async saveMenu(saving,mode,tutorial){
      const slots=await this.api.rpc('saves',{mode}),layout=this.manifest.layouts.form_save;
      const body=this.api.detail(saving?'title_savegame':'title_loadgame',352,235);body.className='save-slots';
      const auto=document.createElement('div');auto.className='autosave-band';
      auto.append(this.lettering(this.text('text_autosave'),'font_text_2_cn_hd','#646464',100,'center'),this.api.uiImage('pattern_stage_intro.png',36,12));body.append(auto);
      for(const slot of slots){
        const attrs=layout.children.find(e=>e.attributes.id==='group_save_'+slot.slot).attributes;
        const panel=document.createElement('section');panel.className='save-slot';
        Object.assign(panel.style,{left:attrs.x+'px',top:(Number(attrs.y)-27)+'px',width:attrs.w+'px',height:attrs.h+'px'});
        const title=document.createElement('div');title.className='save-name';title.append(this.lettering(slot.corrupt?'存档损坏':slot.exists?slot.name:this.text('text_empty'),'font_text_2_cn_hd','#ebe6e1',110,'center'));panel.append(title);
        const date=document.createElement('div');date.className='save-date';
        if(slot.savedAt){const d=new Date(slot.savedAt),pad=n=>String(n).padStart(2,'0');date.append(this.lettering(d.getFullYear()+'/'+pad(d.getMonth()+1)+'/'+pad(d.getDate())+'\n'+pad(d.getHours())+':'+pad(d.getMinutes()),'font_char_1_hd','#404040',110,'center'));}panel.append(date);
        if(slot.country){const flag=this.countryFlag(slot.country,25.5,15);flag.className='save-country';panel.append(flag);}
        const titleText=(saving?'保存到':'载入')+(slot.slot===0?'自动存档':'存档 '+slot.slot)+(slot.exists?' '+slot.name+' 回合 '+slot.round:' 空');
        const command=this.api.button('',async()=>{
          if(saving){await this.api.rpc('save',{slot:slot.slot,mode,tutorial});document.getElementById('detail-modal').hidden=true;this.api.notice('已保存');}
          else await this.api.load(slot.slot,mode);
        },panel);
        command.className='save-slot-confirm';command.setAttribute('aria-label',titleText);command.title=titleText;
        command.disabled=saving?slot.slot===0:!slot.exists||slot.corrupt;
        command.append(this.api.uiImage('button_ok_gray_noshadow.png',30));body.append(panel);
      }
    }
    commanderInfo(definition,held,onClose){
      this.commanderReturn=onClose;
      const body=this.api.detail('title_unitinfo',440,217,null,onClose||(()=>{this.drawRoster();}));body.className='commander-detail';
      const card=document.createElement('div');card.className='commander-detail-card';const portrait=this.card(definition,held,()=>{});portrait.querySelector('.commander-info').remove();portrait.querySelector('.commander-tag')?.remove();delete portrait.dataset.locked;card.append(portrait);body.append(card);
      const equipment=document.createElement('div');equipment.className='commander-equipment';const title=document.createElement('div');title.className='commander-equipment-title';title.append(this.lettering(this.text('text_equipitem')));equipment.append(title);
      const items=document.createElement('div');items.className='commander-equipment-slots';for(let i=0;i<(this.data.equipmentCapacity??2);i++){const marker=this.data.items.find(m=>m.id===held?.items[i]);items.append(this.api.uiImage(marker?.image||'item_blank.png',45));}equipment.append(items);body.append(equipment);
      const skills=document.createElement('div');skills.className='commander-skills';
      for(let i=0;i<Math.max(this.data.skillLayout?0:4,definition.skills.length);i++){
        const row=document.createElement('div');row.className='skill-row';const skill=definition.skills[i];
        if(skill){
          row.append(Ew4SkillUI.icon(skill,19,this.api.uiImage),this.lettering(skill.name));
          const info=this.api.button('',()=>this.skillTip(row,skill,true),row);info.className='skill-info';info.setAttribute('aria-label',skill.name+'说明');info.append(this.api.uiImage('button_generalinfo_blue.png',18));
          row.onpointerenter=()=>this.skillTip(row,skill);row.onpointerleave=()=>{if(!this.skillPinned)this.dismissSkillTip();};
          info.onfocus=()=>this.skillTip(row,skill);info.onblur=()=>{if(!this.skillPinned)this.dismissSkillTip();};
        }skills.append(row);
      }Ew4SkillUI.layout(skills,this.data.skillLayout,definition.skills);body.append(skills);
      const ranks=document.createElement('div');ranks.className='commander-ranks';
      for(const [track,effect,value] of [['rank','hp',held?.healthBonus??definition.healthBonus],['title','recover',held?.healing??definition.healing]]){const row=document.createElement('div');row.append(this.progressionBadge(track,held?.learning[track]??{level:definition[track],experience:0}));const icon=this.api.uiImage('marker_'+effect+'_dark.png',17);icon.className='rank-effect';row.append(icon,this.lettering(value,'font_char_2_hd'));ranks.append(row);}body.append(ranks);
      const abilities=document.createElement('div');abilities.className='commander-abilities';const values=held?.learning.abilities||definition.abilities;
      for(const [key,icon] of [['infantry','upgrade_tag_infantry'],['cavalry','upgrade_tag_cavalry'],['artillery','upgrade_tag_artillery'],['naval','upgrade_tag_ship'],['fort','upgrade_tag_fortress'],['commerce','generalsinfo_trade'],['movement','generalsinfo_move'],['training','generalsinfo_training']]){
        const row=document.createElement('div');row.className='commander-ability';row.setAttribute('aria-label',key+' '+(key==='training'?(held?.training??definition.training):values[key]));row.append(this.api.uiImage(icon+'.png',22,21.5));const stars=document.createElement('div');stars.className='ability-stars';for(let i=0;i<(key==='training'?(held?.training??definition.training):values[key]);i++)stars.append(this.api.uiImage('star_middle.png',13.5,12.5));row.append(stars);abilities.append(row);
      }body.append(abilities);
      if(held&&!this.deployment&&!held.battle){const regroup=this.api.button('',()=>this.regroup(held.id),equipment);regroup.className='native-command orange regroup';regroup.disabled=held.locked;regroup.setAttribute('aria-label',this.text('btn_regroup'));regroup.append(this.lettering(this.text('btn_regroup'),'font_text_3_cn_hd','#ebe6e1'));}
      const item=this.api.button('',()=>held?.battle?this.battleEquipment(held,()=>this.commanderInfo(definition,held,onClose)):this.equipment(held.id),equipment);item.className='native-command blue equip';item.disabled=!held||!!this.deployment||!!held.locked&&!held.battle;item.setAttribute('aria-label',this.text('btn_item'));item.append(this.lettering(this.text('btn_item'),'font_text_3_cn_hd','#ebe6e1'));
    }
  }
  root.Ew4Menus={Menus};
})(globalThis);
