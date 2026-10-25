'use strict';
(function(){
  const proto=Ew4Menus.Menus.prototype;
  proto.closeHeadquartersFilter=function(immediate=false){return this.hqFilter?.setOpen(false,immediate)||false;};
  proto.renderHeadquartersFilters=function(){
    const host=document.getElementById('hq-filters'),menus=this;
    if(!host.querySelector('#hq-filter-main')){
      host.replaceChildren();host.setAttribute('role','group');host.setAttribute('aria-label','指挥部工具');
      let open=false,timer,shieldPressed=false;
      const command=(label,action,parent=host)=>{
        const button=menus.api.button('',action,parent);button.className='native-command blue';button.type='button';
        button.title=label;button.setAttribute('aria-label',label);button.append(menus.lettering(label,'font_text_3_cn_hd','#ebe6e1'));return button;
      };
      const main=command('筛选',()=>setOpen(!open));main.id='hq-filter-main';main.setAttribute('aria-haspopup','menu');main.setAttribute('aria-expanded','false');main.setAttribute('aria-controls','hq-filter-options');
      const arrow=document.createElement('span');arrow.className='hq-filter-arrow';arrow.setAttribute('aria-hidden','true');main.append(arrow);
      for(const [id,label,kind] of [['hq-tasks','任务','task'],['hq-achievements','成就','achievement']]){
        const b=command(label,()=>menus.openObjectives(kind));b.id=id;
      }
      document.getElementById('hq-filter-shield')?.remove();
      const shield=document.createElement('div');shield.id='hq-filter-shield';shield.hidden=true;shield.setAttribute('aria-hidden','true');
      document.getElementById('hq-screen').append(shield);
      const options=document.createElement('div');options.id='hq-filter-options';options.setAttribute('role','menu');options.setAttribute('aria-label','将领分类');options.hidden=true;host.append(options);
      for(const [index,rating] of [null,0,1,2].entries()){
        const label=rating===null?'全选':['一星区','二星区','三星区'][rating];
        const b=command(label,()=>{
          setOpen(false);menus.rosterRating=rating;document.getElementById('hq-roster').scrollLeft=0;menus.drawRoster();main.focus();
        },options);
        b.dataset.rating=String(rating);b.setAttribute('role','menuitemradio');b.style.setProperty('--hq-option-index',index+1);
        if(rating!==null){b.className='hq-star-filter';b.replaceChildren(menus.api.uiImage('button_general_'+[6,4,2][rating]+'.png',94,94*40/110));}
      }
      function setOpen(value,immediate=false){
        const changed=open!==value;clearTimeout(timer);open=value;
        if(value){options.hidden=false;shield.hidden=false;getComputedStyle(options.firstElementChild).transform;}
        host.classList.toggle('filter-open',value);main.setAttribute('aria-expanded',String(value));options.setAttribute('aria-hidden',String(!value));
        for(const id of ['hq-tasks','hq-achievements']){const command=document.getElementById(id);if(command)command.disabled=value||!!menus.tagDraft;}
        for(const button of options.children)button.tabIndex=value?0:-1;
        const finish=()=>{options.hidden=true;if(!shieldPressed)shield.hidden=true;};
        if(!value){if(immediate)finish();else timer=setTimeout(finish,280);}
        return changed;
      }
      // Keep the closing shield through pointerup/click so one gesture cannot hit a newly exposed command.
      for(const type of ['pointerdown','pointerup','click','pointercancel','wheel'])shield.addEventListener(type,e=>{
        e.preventDefault();e.stopPropagation();
        if(type==='pointerdown'){shieldPressed=true;setOpen(false);}
        if(type==='pointerup'||type==='pointercancel'){shieldPressed=false;setTimeout(()=>{if(!open&&options.hidden)shield.hidden=true;},0);}
      },{passive:false});
      host.onkeydown=e=>{
        if(e.key==='Escape'&&open){setOpen(false);main.focus();e.preventDefault();e.stopPropagation();return;}
        const keys=['ArrowRight','ArrowLeft','Home','End'];
        if(!keys.includes(e.key))return;
        const buttons=[...options.children];
        if(e.target===main){if(e.key==='ArrowRight'){setOpen(true);buttons[0].focus();}else return;}
        else if(open&&options.contains(e.target)){
          const index=buttons.indexOf(e.target);buttons[e.key==='Home'?0:e.key==='End'?3:(index+(e.key==='ArrowRight'?1:-1)+4)%4].focus();
        }else return;
        e.preventDefault();e.stopPropagation();
      };
      host.onfocusout=()=>setTimeout(()=>{if(open&&!host.contains(document.activeElement))setOpen(false);},0);
      this.hqFilter={setOpen,host};setOpen(false,true);
      if(!this.hqFilterResize){this.hqFilterResize=true;addEventListener('resize',()=>requestAnimationFrame(()=>this.fitHeadquartersFilters()));}
    }
    for(const button of host.querySelectorAll('[role=menuitemradio]'))button.setAttribute('aria-checked',String(button.dataset.rating===String(this.rosterRating)));
    for(const id of ['hq-tasks','hq-achievements'])document.getElementById(id).disabled=!!this.tagDraft||host.classList.contains('filter-open');
    this.fitHeadquartersFilters();
  };
  proto.fitHeadquartersFilters=function(){
    if(this.recruiting||this.deployment)return;
    const host=document.getElementById('hq-filters'),recruit=document.getElementById('hq-recruit');if(!host.querySelector('#hq-filter-main'))return;
    const layout=this.catalog.extensionLayouts?.headquarters||{},width=Math.max(40,Math.min(90,layout.width??70)),height=Math.max(28,Math.min(40,layout.height??40));
    const left=Math.min(layout.left??30,Math.max(0,recruit.offsetLeft-300)),top=Math.max(28,Math.min(32,layout.top??28)),gap=Math.max(2,Math.min(8,layout.gap??5));
    const available=Math.max(270,recruit.offsetLeft-left-8),factor=Math.min(1,available/(width*(2+3*94/70)+4*gap));
    host.style.left=left+'px';host.style.top=top+'px';host.style.setProperty('--hq-button-width',width*factor+'px');host.style.setProperty('--hq-star-width',width*94/70*factor+'px');
    host.style.setProperty('--hq-gap',gap*factor+'px');host.style.setProperty('--hq-button-height',height+'px');
  };
  proto.openObjectives=async function(kind){
    this.closeHeadquartersFilter(true);
    const title=kind==='task'?'任务':'成就',key=kind==='task'?'tasks':'achievements',layout=this.catalog.extensionLayouts?.[key]||{};
    const generation=this.hqObjectiveGeneration=(this.hqObjectiveGeneration||0)+1;
    const body=this.api.detail(title,Math.max(280,layout.width??420),Math.max(140,layout.height??240),undefined,async()=>{
      this.data=await this.api.rpc('headquarters');this.drawRoster();
    });body.className='hq-objectives';body.dataset.kind=kind;
    const status=document.createElement('div');status.className='hq-objective-empty';status.append(this.lettering('加载中','font_text_2_cn_hd','#404040'));body.append(status);
    let pending=false;
    const current=()=>this.hqObjectiveGeneration===generation&&!document.getElementById('detail-modal').hidden&&document.getElementById('detail-content')===body&&body.dataset.kind===kind;
    const render=data=>{
      if(!current())return;body.replaceChildren();
      const entries=data.entries.filter(e=>e.kind===kind);
      const list=document.createElement('div');list.className='hq-objective-list';list.style.gap=(layout.gap??4)+'px';list.tabIndex=0;list.setAttribute('role','region');list.setAttribute('aria-label',title+'列表');body.append(list);
      if(!entries.length){const empty=document.createElement('div');empty.className='hq-objective-empty';empty.append(this.lettering(kind==='task'?'暂无任务':'暂无成就','font_text_2_cn_hd','#77736f'));list.append(empty);return;}
      for(const entry of entries){
        const row=document.createElement('div');row.className='hq-objective-row';row.dataset.objective=entry.id;list.append(row);
        const name=document.createElement('div');name.className='hq-objective-name';name.append(this.lettering(entry.name,'font_text_2_cn_hd','#404040'));row.append(name);
        const description=document.createElement('div');description.className='hq-objective-description';description.textContent=entry.description;row.append(description);
        const rewards=document.createElement('div');rewards.className='hq-objective-rewards';row.append(rewards);
        for(const [currency,image] of [['medals','medals.png'],['badges','badges.png']])if(entry.reward[currency]>0)rewards.append(this.api.uiImage(image,13,17),this.lettering(entry.reward[currency],'font_char_2_hd','#404040'));
        for(const [id,count] of Object.entries(entry.reward.items||{})){
          const item=this.data.items.find(i=>i.id===id),reward=document.createElement('span');reward.className='hq-objective-resource';reward.dataset.item=id;
          const label=(item?.name||id)+' × '+count;reward.title=label;reward.setAttribute('aria-label',label);
          reward.append(this.api.uiImage(item?.image||'item_blank.png',22,22),document.createTextNode(label));rewards.append(reward);
        }
        for(const id of entry.reward.commanders||[]){
          const definition=this.data.definitions.find(d=>d.id===id),reward=document.createElement('span');reward.className='hq-objective-resource';reward.dataset.commander=id;
          const label=definition?.name||id;reward.title=label;reward.setAttribute('aria-label',label);
          const portrait=this.portrait(definition?.image||'');portrait.width=22;portrait.height=22;reward.append(portrait,document.createTextNode(label));rewards.append(reward);
        }
        const state=entry.claimed?'已领取':entry.completed?'领取':'未达成';
        const claim=this.api.button('',async()=>{
          if(pending)return;pending=true;for(const b of body.querySelectorAll('button'))b.disabled=true;
          try{render(await this.api.rpc('objectives',{op:'claim',offer:entry.id,revision:data.revision}));}
          catch(error){if(current()){this.api.notice(error.message);render(await this.api.rpc('objectives'));}}
          finally{pending=false;}
        },row);claim.className='native-command '+(entry.completed&&!entry.claimed?'orange':'blue');claim.disabled=entry.claimed||!entry.completed;claim.setAttribute('aria-label',state+' '+entry.name);claim.append(this.lettering(state,'font_text_3_cn_hd','#ebe6e1'));
        if(!entry.completed){const progress=document.createElement('div');progress.className='hq-objective-progress';progress.textContent=entry.conditions.map(c=>Math.min(c.current,c.required)+' / '+c.required).join(' · ');row.append(progress);}
      }
    };
    try{render(await this.api.rpc('objectives'));}catch(error){if(current()){body.replaceChildren();const retry=this.api.button('重试',()=>this.openObjectives(kind),body);retry.className='native-command blue';retry.setAttribute('aria-label','重试');this.api.notice(error.message);}}
  };
})();
