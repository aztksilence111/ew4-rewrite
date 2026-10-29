'use strict';
(function(){
  const proto=Ew4Menus.Menus.prototype;
  proto.clearHeadquartersPresentation=function(){
    if(!this.hqPresentation)return;
    const {layout,placeholder,roster,style}=this.hqPresentation;
    if(placeholder){placeholder.replaceWith(roster);if(style===null)roster.removeAttribute('style');else roster.setAttribute('style',style);}
    roster?.classList.remove('hq-custom-roster');
    layout.body.remove();const host=document.getElementById('hq-screen');host.classList.remove('hq-custom');host.style.removeProperty('--hq-custom-background');this.hqPresentation=null;
  };
  proto.renderHeadquartersPresentation=function(){
    const screens=this.catalog.headquartersPresentation?.screens;
    const key=this.deployment?'deployment':this.recruiting?'academy':this.hqPage||'roster',screen=screens?.[key];
    if(!screen)return;
    this.closeHeadquartersFilter?.(true);
    const host=document.getElementById('hq-screen'),body=document.createElement('div');body.className='hq-custom-surface';
    body.style.backgroundColor=screen.background;
    if(screen.backgroundImage)body.style.backgroundImage='url("'+Ew4NativeUI.resource(this.manifest,screen.backgroundImage).file+'")';
    host.append(body);host.classList.add('hq-custom');host.style.setProperty('--hq-custom-background',screen.background);
    const layout=new Ew4NativeUI.Layout(this.api,'headquarters-'+key,null,{body,definition:screen.layout,current:()=>!host.hidden,afterAction:()=>this.syncHeadquartersPresentation()});
    const state=this.hqPresentation={layout,screen,key};
    if(screen.bindings.roster){
      const target=layout.get(screen.bindings.roster),roster=document.getElementById('hq-roster'),placeholder=document.createComment('headquarters roster');
      Object.assign(state,{roster,placeholder,style:roster.getAttribute('style')});roster.replaceWith(placeholder);target.replaceChildren(roster);roster.classList.add('hq-custom-roster');
    }
    for(const [target,action] of Object.entries(screen.actions)){
      const button=layout.get(target),label=this.text(button.nativeAttributes.text||button.nativeAttributes.string||button.nativeAttributes.title||action.command);
      layout.action(target,label,()=>this.headquartersCommand(action));
    }
    this.syncHeadquartersPresentation();this.fitHeadquartersPresentation();
    if(!this.hqPresentationResize){
      this.hqPresentationResize=true;
      for(const name of ['resize','ew4uiscale'])addEventListener(name,()=>this.fitHeadquartersPresentation());
    }
  };
  proto.fitHeadquartersPresentation=function(){
    const state=this.hqPresentation;if(!state)return;
    const host=document.getElementById('hq-screen'),{layout}=state;
    const factor=Math.min(host.clientWidth/layout.width,host.clientHeight/layout.height);
    Object.assign(layout.body.style,{width:layout.width+'px',height:layout.height+'px',transform:'translate(-50%,-50%) scale('+factor+')'});
  };
  proto.syncHeadquartersPresentation=function(){
    const state=this.hqPresentation;if(!state)return;
    const {layout,screen,roster}=state,data=this.data,capacities=data.sectionCapacities??Array(3).fill(data.sectionCapacity??20);
    const section=d=>Math.max(0,Math.min(2,Math.floor((d.rating-1)/3)));
    const definitions=new Map(data.definitions.map(d=>[d.id,d]));
    const count=data.roster.filter(c=>this.deployment||this.rosterRating===null||section(definitions.get(c.definitionId)||{rating:1})===this.rosterRating).length;
    const values={title:this.text(this.deployment?'title_deploygeneral':this.recruiting?'title_buygeneral':'title_headquarters'),
      medals:data.medals,badges:data.badges,count,capacity:this.deployment||this.rosterRating===null?capacities.reduce((a,b)=>a+b,0):capacities[this.rosterRating],
      refreshRemaining:data.refresh?.[this.rating]?.remaining??'∞',offerName:this.offer?.name??'',medalPrice:this.offer?.price??'',badgePrice:this.offer?.badgePrice??''};
    for(const [name,target] of Object.entries(screen.bindings))if(name!=='roster')layout.text(target,String(values[name]));
    if(roster){
      const settings=screen.roster;
      roster.style.setProperty('--hq-card-scale',settings.cardScale);roster.style.setProperty('--hq-card-width',78*settings.cardScale+'px');
      roster.style.setProperty('--hq-card-height',98*settings.cardScale+'px');roster.style.setProperty('--hq-card-gap',settings.gap+'px');
      roster.style.setProperty('--hq-card-rows',settings.rows);
      let index=0;
      for(const card of roster.children){
        const visible=settings.showEmpty||!card.classList.contains('empty-commander');card.hidden=!visible;
        if(!visible)continue;
        card.style.gridRow=index%settings.rows+1;card.style.gridColumn=Math.floor(index/settings.rows)+1;index++;
        const info=card.querySelector('.commander-info'),promotion=card.querySelector('.promote-commander');
        if(info)info.hidden=!settings.showInfo;if(promotion)promotion.hidden=!settings.showPromotion;
      }
    }
    const busy=!!(this.refreshPending||this.recruitPending),editing=!!this.tagDraft;
    for(const [target,action] of Object.entries(screen.actions)){
      const command=action.command,button=layout.get(target);
      const purchase=command==='recruitMedals'||command==='recruitBadges',badge=command==='recruitBadges',price=badge?this.offer?.badgePrice:this.offer?.price;
      const unavailable=busy||this.tagSaving||editing&&!['back','tags'].includes(command)
        ||purchase&&(!this.recruiting||!this.offer||!(price>0)||(badge?data.badges:data.medals)<price)
        ||command==='refresh'&&(!this.recruiting||data.refresh?.[this.rating]?.remaining===0)
        ||command==='info'&&!this.rosterSelected||['tags','shop','princesses','tasks','achievements'].includes(command)&&!!this.deployment;
      button.disabled=!!unavailable;
      if(command==='filter'||command==='academy')button.setAttribute('aria-pressed',String((command==='filter'?this.rosterRating:this.rating)===(action.section??(command==='filter'?null:0))));
      if(command==='tags')button.setAttribute('aria-pressed',String(editing));
      if(command==='tags'&&action.editingText){
        const label=this.text(editing?action.editingText:button.nativeAttributes.text||button.nativeAttributes.string||'标签');
        layout.text(target,label);button.setAttribute('aria-label',label);button.title=label;
      }
    }
  };
  proto.headquartersCommand=async function(action){
    const command=action.command;
    if(command==='back'){document.getElementById('hq-back').click();return;}
    if(command==='main'){this.api.main();return;}
    if(command==='roster'){this.hqPage=null;this.recruiting=false;this.drawRoster();return;}
    if(command==='page'){this.hqPage=action.page;this.recruiting=false;this.drawRoster();return;}
    if(command==='academy'){this.hqPage=null;this.recruiting=true;this.rating=action.section??0;this.offer=null;this.drawRoster();return;}
    if(command==='filter'){this.rosterRating=action.section??null;document.getElementById('hq-roster').scrollLeft=0;this.drawRoster();return;}
    if(command==='shop')return this.shop();
    if(command==='princesses')return this.princesses();
    if(command==='tags')return this.editCommanderTags();
    if(command==='tasks'||command==='achievements')return this.openObjectives(command==='tasks'?'task':'achievement');
    if(command==='refresh')return this.transact({op:'refresh',group:this.rating});
    if(command==='info'){
      const definition=this.data.definitions.find(d=>d.id===this.rosterSelected),held=this.data.roster.find(c=>c.definitionId===definition?.id);
      if(definition)return this.commanderInfo(definition,held,()=>this.drawRoster());return;
    }
    if(this.recruitPending||this.refreshPending||!this.offer||!this.recruiting)return;
    this.recruitPending=true;this.syncRecruitmentBusy();
    try{await this.transact({op:command==='recruitBadges'?'recruit_badge':'recruit',commander:this.offer.id,group:this.rating});}
    finally{this.recruitPending=false;this.syncRecruitmentBusy();}
  };
})();
