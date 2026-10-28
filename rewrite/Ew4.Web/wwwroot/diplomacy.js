'use strict';
(function(root){
  const node=(tag,cls,text)=>{const el=document.createElement(tag);if(cls)el.className=cls;if(text!==undefined)el.textContent=text;return el;};
  const format=(text,values)=>String(text||'').replace(/\{(\w+)\}/g,(match,key)=>values[key]??match);
  class Panel {
    constructor(api){
      this.api=api;this.opened=false;this.serial=0;this.order=[];this.context=null;this.revision=null;
      this.entry=node('button','diplomacy-entry');this.entry.id='open-diplomacy';this.entry.type='button';this.entry.hidden=true;
      this.entry.title=this.entry.ariaLabel='外交';this.entryFace=node('span','diplomacy-entry-face');this.entryFace.append(this.icon('client/ui/diplomacy/handshake.png'));this.entry.append(this.entryFace);this.entry.onclick=()=>this.open();
      this.modal=node('div','modal diplomacy-modal');this.modal.id='diplomacy-modal';this.modal.hidden=true;
      this.dialog=node('section','dialog diplomacy-dialog');this.dialog.setAttribute('role','dialog');this.dialog.setAttribute('aria-modal','true');this.dialog.setAttribute('aria-labelledby','diplomacy-title');
      this.header=node('header','menu-bar');this.title=node('h2','','外交');this.title.id='diplomacy-title';this.closeButton=node('button','close-icon');this.closeButton.type='button';this.closeButton.title=this.closeButton.ariaLabel='关闭';this.closeButton.onclick=()=>this.close();this.header.append(this.title,this.closeButton);
      this.player=node('div','diplomacy-player');this.list=node('div','diplomacy-list');this.list.tabIndex=0;this.list.ariaLabel='国家列表';
      this.confirm=node('section','diplomacy-confirm');this.confirm.hidden=true;this.confirmCopy=node('p','');this.confirmStats=node('p','');
      this.confirmActions=node('div','diplomacy-confirm-actions');this.cancel=node('button','native-command blue','取消');this.submit=node('button','native-command','确认');this.cancel.type=this.submit.type='button';
      this.cancel.onclick=()=>this.cancelConfirmation();this.submit.onclick=()=>this.execute();this.confirmActions.append(this.cancel,this.submit);this.confirm.append(this.confirmCopy,this.confirmStats,this.confirmActions);
      this.result=node('p','diplomacy-result');this.result.hidden=true;this.result.setAttribute('role','status');this.result.setAttribute('aria-live','polite');
      this.dialog.append(this.header,this.player,this.list,this.confirm,this.result);this.modal.append(this.dialog);
      document.getElementById('play').append(this.entry,this.modal);
      this.modal.addEventListener('keydown',e=>{if(e.key==='Escape'){e.preventDefault();e.stopPropagation();this.back();}else if(e.key==='Tab'){
        const controls=[...this.dialog.querySelectorAll('button:not(:disabled),[tabindex="0"]')].filter(el=>el.getClientRects().length),first=controls[0],last=controls[controls.length-1];
        if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}
      }});
      addEventListener('resize',()=>this.fit());addEventListener('ew4uiscale',()=>this.fit());
    }
    path(value){return value?.startsWith('client/')?'/content/'+value.slice(7):value;}
    icon(path){const image=node('img','');image.src=this.path(path);image.alt='';return image;}
    get state(){return this.api.state();}
    sync(){
      const s=this.state,enabled=!!s?.diplomacyEnabled&&s.phase!=='Finished';
      this.entry.hidden=!enabled;this.entry.disabled=!!s?.dialogue||!!s?.scenarioPopup||this.api.blocked()||this.api.busy()&&!this.api.aiRunning();
      const context=s?.battleId+'/'+s?.playerFaction;
      if(this.context!==context){this.reset();this.context=context;}
      const presentation=this.api.presentation();if(!this.snapshot&&presentation)this.style(presentation);
      if(this.opened&&(!enabled||s.dialogue||s.scenarioPopup)){this.close(false);return;}
      this.fit();
    }
    reset(){this.serial++;this.opened=false;this.modal.hidden=true;this.pending=null;this.snapshot=null;this.revision=null;this.order=[];this.confirm.hidden=true;this.list.hidden=false;this.result.hidden=true;}
    async open(){
      if(this.opened||this.entry.disabled||this.entry.hidden)return;
      this.opened=true;this.order=[];this.revision=null;this.result.hidden=true;this.modal.hidden=false;this.dialog.setAttribute('aria-busy','true');this.closeButton.focus();
      try{await this.api.pause();if(!this.opened)return;await this.refresh();}catch(e){this.api.notice(e.message);this.close();}
      finally{this.dialog.removeAttribute('aria-busy');}
    }
    close(resume=true){if(this.submitting)return;this.reset();if(!this.entry.hidden)this.entry.focus();if(resume)this.api.resume();}
    back(){if(!this.opened)return false;if(this.submitting)return true;if(this.pending)this.cancelConfirmation();else this.close();return true;}
    async refresh(){
      const serial=++this.serial,context=this.context;
      const data=await this.api.rpc('diplomacy',{});
      await this.api.prepare(JSON.stringify(data.presentation)+JSON.stringify(this.state.factions.map(f=>f.name)));
      if(serial!==this.serial||!this.opened||context!==this.context)return;
      if(data.revision!==this.state.revision){this.api.notice('战局已变化，请重新打开外交');this.close();return;}
      this.snapshot=data;this.revision=data.revision;this.render();
    }
    style(p){
      this.presentation=p;const style=this.dialog.style;
      for(const key of ['textColor','headerColor','headerTextColor','rowColor','borderColor'])style.setProperty('--diplomacy-'+key.replace(/[A-Z]/g,m=>'-'+m.toLowerCase()),p[key]);
      for(const key of ['fontSize','titleFontSize','gap','rowHeight','buttonSize'])style.setProperty('--diplomacy-'+key.replace(/[A-Z]/g,m=>'-'+m.toLowerCase()),p[key]+'px');
      style.setProperty('--diplomacy-action-hit',Math.max(44,p.buttonSize)+'px');
      for(const key of ['backgroundImage','titleImage','closeImage','patternImage','warBackground','demandBackground'])style.setProperty('--diplomacy-'+key.replace(/[A-Z]/g,m=>'-'+m.toLowerCase()),'url("'+this.path(p[key])+'")');
      style.backgroundImage=p.backgroundImage==='client/ui/image_ui_hd/form_back.png'?'none':'url("'+this.path(p.backgroundImage)+'")';style.backgroundSize='100% 100%';
      this.dialog.dataset.originalFont=p.font;this.entryFace.style.backgroundImage='url("'+this.path(p.entryBackground)+'")';this.entryFace.firstElementChild.src=this.path(p.entryIcon);this.entry.title=this.entry.ariaLabel=p.title;
      this.title.textContent=p.title;this.cancel.textContent=p.cancelText;this.submit.textContent=p.confirmText;
    }
    identity(f){const el=node('div','diplomacy-identity'),flag=this.icon(this.api.flag(f)),name=node('span','diplomacy-name',f.name);flag.alt=f.name;el.append(flag,name);return el;}
    resources(f){const el=node('div','diplomacy-resources');if(f.id!==this.state.playerFaction&&f.relation!=='allied'){el.hidden=true;return el;}for(const [key,label] of [['money','金币'],['industry','工业'],['food','粮食']]){
      const item=node('span','diplomacy-resource'),image=this.icon('client/ui/image_ui_hd/marker_'+key+'.png');item.ariaLabel=label+' '+f.resources[key];item.append(image,node('span','',f.resources[key]));el.append(item);
    }return el;}
    capitalText(data,q){const p=this.presentation;return format(p.capitalText,{name:data?.capitalName||p.noCapitalText,percent:q?.lootPercent??0,used:data?.capital?.treasuryCaptured?p.capturedText:''});}
    showCapitalNotices(s){
      const p=this.presentation||this.api.presentation();if(!p)return;
      for(const capture of s.notices||[]){
        if(capture.kind!=='capital.captured'||capture.target!==s.playerFaction&&capture.subject!==s.playerFaction)continue;
        const values={name:s.factions.find(f=>f.id===capture.subject)?.name||capture.subject,percent:capture.value};
        for(const resource of ['money','industry','food'])values[resource]=s.notices.find(n=>n.kind==='capital.loot.'+resource&&n.target===capture.subject)?.value||0;
        this.api.notice(format(capture.target===s.playerFaction?p.lootedText:p.lostTreasuryText,values));
      }
    }
    render(){
      const p=this.snapshot.presentation,s=this.state,scroll=this.list.scrollTop;this.style(p);
      const player=s.factions.find(f=>f.id===s.playerFaction),playerDetails=node('div','diplomacy-player-details');playerDetails.append(this.resources(player),node('div','diplomacy-capital',this.snapshot.playerCapitalName||p.noCapitalText));this.player.replaceChildren(this.identity(player),playerDetails);
      const quotes=new Map(this.snapshot.factions.map(f=>[f.id,f]));
      const score=f=>f.defeated?3:f.relation==='hostile'?0:f.relation==='neutral'?1:2;
      if(!this.order.length)this.order=s.factions.filter(f=>f.id!==s.playerFaction).map((f,index)=>({f,index})).sort((a,b)=>score(a.f)-score(b.f)||a.index-b.index).map(e=>e.f.id);
      const rows=[];
      for(const id of this.order){
        const f=s.factions.find(f=>f.id===id),data=quotes.get(id),q=data?.quote;if(!f||!q)continue;
        const row=node('div','diplomacy-row');row.dataset.faction=id;row.dataset.relation=f.defeated?'defeated':f.relation;
        const identity=this.identity(f),relation=node('span','diplomacy-relation',p.relations[f.defeated?3:f.relation==='allied'?0:f.relation==='neutral'?1:2]);identity.append(relation);
        const details=node('div','diplomacy-details'),stats=node('div','diplomacy-stats',q.demandBlocked==='legacy'?p.reasons.legacy:format(p.buildingsText,{remaining:q.remaining,initial:q.initial}));
        const progress=node('div','diplomacy-progress',format(p.occupiedText,{count:q.occupied}));
        const reason=node('div','diplomacy-reason');
        const enemy=f.relation==='hostile',block=enemy?q.demandBlocked:q.warBlocked;
        reason.textContent=block?(p.reasons[block]||block):enemy?format(p.chanceText,{chance:q.chance}):'';
        const capital=node('div','diplomacy-capital',this.capitalText(data,q));
        details.append(this.resources(f),stats,progress,capital,reason);
        const action=node('button','diplomacy-action');action.type='button';action.title=action.ariaLabel=enemy?p.demandText:p.warText;action.disabled=!!block||this.submitting;
        const face=node('span','native-command diplomacy-action-face '+(enemy?'':'red'));face.append(this.icon(enemy?p.demandIcon:p.warIcon));
        action.dataset.op=enemy?'demand_surrender':'declare_war';action.dataset.target=id;action.append(face);action.onclick=()=>this.ask(f,q,action.dataset.op);
        row.append(identity,details,action);rows.push(row);
      }
      this.list.replaceChildren(...rows);this.fit();this.list.scrollTop=scroll;
    }
    ask(f,q,op){
      if(this.submitting||this.revision!==this.state.revision)return;
      const p=this.presentation;this.pending={id:f.id,op,revision:this.revision};this.result.hidden=true;
      this.confirmCopy.textContent=format(op==='declare_war'?p.warConfirmation:p.demandConfirmation,{name:f.name})+(op==='declare_war'&&f.relation==='allied'?' '+p.alliedWarning:'');
      this.confirmStats.textContent=op==='declare_war'?'':format(p.buildingsText,{remaining:q.remaining,initial:q.initial})+' · '+format(p.occupiedText,{count:q.occupied})+' · '+format(p.chanceText,{chance:q.chance});
      if(op!=='declare_war')this.confirmStats.textContent+='\n'+format(p.coreText,{count:q.sharedCores,initial:q.initial,bonus:q.coreBonus,pair:q.pairBonus>0?'+'+q.pairBonus:q.pairBonus})+'\n'+this.capitalText(this.snapshot.factions.find(d=>d.id===f.id),q);
      this.confirmStats.hidden=op==='declare_war';this.list.hidden=true;this.confirm.hidden=false;this.cancel.focus();this.fit();
    }
    cancelConfirmation(){if(this.submitting)return;this.pending=null;this.confirm.hidden=true;this.list.hidden=false;this.closeButton.focus();this.fit();}
    async execute(){
      if(this.submitting||!this.pending||this.api.busy())return;
      if(this.pending.revision!==this.state.revision){this.cancelConfirmation();await this.refresh();return;}
      this.submitting=true;this.submit.disabled=this.cancel.disabled=this.closeButton.disabled=true;
      const pending=this.pending,name=this.state.factions.find(f=>f.id===pending.id)?.name||pending.id;
      try{
        await this.api.action(pending.op,{target:pending.id});
        const notice=this.state.notices?.find(n=>['diplomacy.war','diplomacy.accepted','diplomacy.refused'].includes(n.kind));
        this.pending=null;this.confirm.hidden=true;this.list.hidden=false;
        if(this.state.phase==='Finished'){this.reset();return;}
        await this.refresh();
        if(this.opened){this.result.textContent=format(notice?.kind==='diplomacy.war'?this.presentation.declaredText:notice?.kind==='diplomacy.accepted'?this.presentation.acceptedText:this.presentation.refusedText,{name});this.result.hidden=false;this.closeButton.focus();this.fit();}
      }catch(e){this.api.notice(e.message);this.pending=null;this.confirm.hidden=true;this.list.hidden=false;try{await this.refresh();}catch(_){} }
      finally{this.submitting=false;this.submit.disabled=this.cancel.disabled=this.closeButton.disabled=false;if(this.snapshot&&this.opened)this.render();}
    }
    fit(){
      const p=this.presentation,rootStyle=getComputedStyle(document.documentElement),scale=Number(rootStyle.getPropertyValue('--battle-compact-scale'))||1;
      const anchor=['undo-move','skip-ai'].map(id=>document.getElementById(id)).find(el=>!el.hidden),rect=anchor?.getBoundingClientRect();
      const size=Math.min(64,(p?.entrySize||27)*scale),hit=Math.max(44,size),left=rect?.left??(parseFloat(rootStyle.getPropertyValue('--screen-corner-inset'))||2);
      this.entry.style.width=this.entry.style.height=hit+'px';this.entryFace.style.width=this.entryFace.style.height=size+'px';this.entry.style.left=left+'px';
      this.entry.style.bottom=(rect?innerHeight-rect.top+Math.max(4,(p?.entryGap||8)-(hit-size)/2):size+12)+'px';
      if(!this.opened||!p)return;
      const safeWidth=Number.parseFloat(rootStyle.getPropertyValue('--screen-safe-width'))||innerWidth,safeHeight=Number.parseFloat(rootStyle.getPropertyValue('--screen-safe-height'))||innerHeight;
      this.dialog.style.width=Math.min(p.width,Math.max(240,safeWidth-16))+'px';this.dialog.style.maxHeight=Math.max(180,safeHeight-16)+'px';
    }
  }
  root.Ew4Diplomacy={Panel};
})(window);
