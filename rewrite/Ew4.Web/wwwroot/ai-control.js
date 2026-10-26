'use strict';
(function(root){
  const $=id=>document.getElementById(id);
  const element=(tag,cls,text)=>{const el=document.createElement(tag);el.className=cls;if(text!==undefined)el.textContent=text;return el;};
  const policies=['manual','free','aggressive','cautious','hold','retreat'];
  const targetsOf=order=>Array.isArray(order?.goals)?order.goals:Number.isInteger(order?.goal)?[order.goal]:[];
  class Panel{
    constructor(api){
      this.api=api;this.mode=null;this.chosen=new Set();this.targets=[];this.pointers=new Map();
      this.panel=element('aside','ai-control');this.panel.id='ai-control';this.panel.setAttribute('aria-label','AI设定');
      this.title=element('button','ai-control-title','AI设定');this.title.type='button';this.title.onclick=()=>{$('pause').classList.toggle('ai-sidebar-expanded');this.fit();this.api.fitObjectives();};
      this.body=element('div','ai-control-body');this.status=element('p','ai-control-status');this.buttons=[];
      for(let i=0;i<4;i++){const b=element('button','native-command ai-control-command');b.type='button';b.id=['ai-policy','ai-units','ai-goal','ai-execute'][i];this.buttons.push(b);this.body.append(b);}
      this.auto=element('input','');this.auto.type='checkbox';this.auto.id='ai-auto-battle';
      const autoLabel=element('label','ai-control-auto');this.autoLabel=element('span','','自动战斗');autoLabel.append(this.auto,this.autoLabel);this.body.append(autoLabel,this.status);
      this.goalSummary=element('ol','ai-goal-summary');this.body.append(this.goalSummary);
      this.panel.append(this.title,this.body);$('pause').append(this.panel);
      this.buttons[0].onclick=()=>this.openPolicy();this.buttons[1].onclick=()=>this.pick('units');this.buttons[2].onclick=()=>this.pick('goal');
      this.buttons[3].onclick=()=>this.execute();this.auto.onchange=async()=>{this.pendingAuto=this.auto.checked;await this.configure('auto',{autoBattle:this.pendingAuto});this.pendingAuto=null;this.sync();};
      this.modal=element('div','modal ai-policy-modal');this.modal.id='ai-policy-modal';this.modal.hidden=true;
      const dialog=element('section','dialog ai-policy-dialog');dialog.setAttribute('role','dialog');dialog.setAttribute('aria-modal','true');dialog.setAttribute('aria-label','指定战斗方针');
      const header=element('header','menu-bar');this.policyTitle=element('h2','','指定战斗方针');header.append(this.policyTitle);
      const close=element('button','close-icon');this.policyCancel=close;close.title=close.ariaLabel='取消';close.onclick=()=>{this.modal.hidden=true;};header.append(close);
      this.policyList=element('div','ai-policy-list');this.policyList.setAttribute('role','radiogroup');
      this.policyConfirm=element('button','native-command ai-policy-confirm','确认');this.policyConfirm.id='ai-policy-confirm';this.policyConfirm.title=this.policyConfirm.ariaLabel='确认';
      this.policyConfirm.onclick=async()=>{if(await this.configure('policy',{policy:this.policy}))this.modal.hidden=true;};
      dialog.append(header,this.policyList,this.policyConfirm);this.modal.append(dialog);$('play').append(this.modal);
      this.tools=element('div','ai-map-tools');this.tools.id='ai-map-tools';this.tools.hidden=true;
      this.count=element('span','ai-selection-count');this.confirm=element('button','native-command ai-map-confirm','确认');this.confirm.id='ai-map-confirm';this.confirm.title=this.confirm.ariaLabel='确认';
      this.cancelPick=element('button','native-command ai-map-cancel','取消选择');this.cancelPick.id='ai-map-cancel';this.cancelPick.onclick=()=>this.leavePick();
      this.clearSelection=element('button','native-command ai-clear-selection','取消全部选中');this.clearSelection.id='ai-clear-selection';
      this.clearSelection.onclick=()=>{this.chosen.clear();this.updatePick();};
      this.confirm.onclick=()=>this.confirmPick();this.tools.append(this.count,this.clearSelection,this.cancelPick,this.confirm);$('play').append(this.tools);
      this.goalEditor=element('section','ai-goal-editor');this.goalEditor.id='ai-goal-editor';this.goalEditor.hidden=true;
      this.goalHeader=element('h3','','目标优先顺序');this.goalList=element('ol','ai-goal-list');this.goalEditor.append(this.goalHeader,this.goalList);$('play').append(this.goalEditor);
      this.box=element('div','ai-pick-box');this.box.id='ai-pick-box';this.box.hidden=true;$('play').append(this.box);
      addEventListener('resize',()=>{if(this.mode)this.resetGesture();this.fit();});addEventListener('blur',()=>this.resetGesture());
      document.addEventListener('keydown',e=>{if(e.key==='Escape'){if(this.mode)this.leavePick();else this.modal.hidden=true;}});
    }
    get state(){return this.api.state();}
    get control(){return this.state?.playerAi||{};}
    get draft(){return this.control.draft||{};}
    get target(){return this.targets[0]??null;}
    liveUnits(ids){const own=new Set(this.state?.units.filter(u=>u.owner===this.state.playerFaction).map(u=>u.id)||[]);return [...new Set(ids||[])].filter(id=>own.has(id));}
    get editable(){const s=this.state;return s&&s.phase==='Active'&&s.activeFaction===s.playerFaction&&!s.dialogue&&!s.scenarioPopup&&!this.api.busy()&&!this.control.enabled;}
    async configure(step,extra={}){
      if(!this.editable)return false;
      try{await this.api.action('player_ai_config',{offer:step,...extra});return true;}catch(e){this.api.notice(e.message);return false;}
    }
    sync(){
      const s=this.state;if(!s){this.reset();return;}
      const p=s.aiPresentation||{};this.presentation=p;
      const set=(el,text)=>{if(el.textContent!==text)el.textContent=text;};
      set(this.title,p.title||'AI设定');
      const labels=p.buttons||['指定战斗方针','框选单位','指定行动目标','开始执行','停止执行'];
      const units=this.liveUnits(this.draft.units),goals=targetsOf(this.draft);
      const complete=[this.draft.policyConfirmed,this.draft.unitsConfirmed&&units.length>0,this.draft.goalConfirmed&&goals.length>0];
      this.buttons.forEach((b,i)=>{const label=labels[i===3&&this.control.enabled?4:i];set(b,label);b.setAttribute('aria-label',label);b.title=label;b.dataset.complete=String(!!complete[i]);
        b.disabled=i===3?this.control.enabled?this.api.transitioning():!this.editable||!complete.every(Boolean):!this.editable;});
      this.auto.disabled=!this.editable;this.auto.checked=this.pendingAuto??!!this.control.autoBattle;
      const names=p.policies||['玩家指挥','自由行动','积极进攻','消极进攻','原地防守','逃离战场'];
      set(this.status,this.unitCount(units.length)+' · '+(this.draft.policy?names[policies.indexOf(this.draft.policy)]:p.noPolicyText||'未设方针')+' · '+(goals.length?this.goalCount(goals.length):p.noGoalText||'未设目标'));
      this.goalSummary.replaceChildren(...goals.map(cell=>element('li','',this.goalName(cell))));this.goalSummary.hidden=!goals.length;
      for(const el of [this.confirm,this.policyConfirm]){set(el,p.confirmText||'确认');el.title=el.ariaLabel=p.confirmText||'确认';}
      set(this.cancelPick,p.cancelSelectionText||'取消选择');set(this.autoLabel,p.autoBattleText||'自动战斗');set(this.policyTitle,labels[0]);
      this.policyCancel.title=this.policyCancel.ariaLabel=p.cancelText||'取消';
      set(this.clearSelection,p.clearSelectionText||'取消全部选中');set(this.goalHeader,p.goalOrderText||'目标优先顺序');
      for(const host of [this.panel,this.modal,this.tools,this.box,this.goalEditor]){
        const style=host.style;
        for(const [key,value] of Object.entries({width:p.width||224,'button-height':p.buttonHeight||40,'font-size':p.fontSize||16,'title-font-size':p.titleFontSize||20,gap:p.gap??10,'target-list-height':p.targetListHeight||180,
          'confirm-width':p.confirmWidth||112,'confirm-height':p.confirmHeight||46,'cancel-width':p.cancelWidth||100,'policy-width':p.policyWidth||236,'policy-button-height':p.policyButtonHeight||36,'offset-right':p.offsetRight??0,'offset-y':p.offsetY??0}))style.setProperty('--ai-'+key,value+'px');
        for(const [key,value] of Object.entries({'panel-color':p.panelColor,'header-color':p.headerColor,'text-color':p.textColor,'button-text-color':p.buttonTextColor,'selection-color':p.selectionColor}))if(value)style.setProperty('--ai-'+key,value);
        for(const [key,value] of Object.entries({'panel-image':p.panelImage,'button-image':p.buttonImage,'pressed-image':p.pressedButtonImage||p.buttonImage,'confirm-image':p.confirmImage||p.buttonImage}))style.setProperty('--ai-'+key,value?'url("/content/'+value.replace(/^client\//,'')+'")':'none');
      }
      if(this.mode){this.chosen=new Set(this.liveUnits([...this.chosen]));this.updatePick();}
      this.fit();
    }
    fit(){if(!this.state)return;const menu=document.querySelector('#pause>.pause-dialog');const compact=innerWidth<760||innerWidth-menu.getBoundingClientRect().right<160;$('pause').classList.toggle('ai-compact',compact);}
    openPause(){
      const pause=$('pause');pause.classList.remove('pause-sidebars-enter');pause.classList.add('pause-sidebars-pending');pause.hidden=false;
      this.sync();void this.panel.offsetWidth;this.api.pauseSync();
    }
    revealPause(snapshot){
      const pause=$('pause');if(pause.hidden||!pause.classList.contains('pause-sidebars-pending'))return;
      pause.style.setProperty('--pause-sidebars-time',(snapshot?.presentation?.slideMilliseconds??280)+'ms');
      pause.classList.remove('pause-sidebars-pending');pause.classList.add('pause-sidebars-enter');
    }
    openPolicy(){
      if(!this.editable)return;this.policy=this.draft.policy||null;this.policyList.replaceChildren();
      const names=this.presentation?.policies||['玩家指挥','0 自由行动','1 积极进攻','2 消极进攻','3 原地防守','4 逃离战场'];
      this.policyConfirm.disabled=!this.policy;
      for(let i=0;i<policies.length;i++){const b=element('button','native-command ai-policy-option',names[i]);b.type='button';b.dataset.policy=policies[i];b.setAttribute('role','radio');b.setAttribute('aria-checked',String(this.policy===policies[i]));
        b.onclick=()=>{this.policy=policies[i];this.policyConfirm.disabled=false;for(const option of this.policyList.children)option.setAttribute('aria-checked',String(option===b));};this.policyList.append(b);}
      this.modal.hidden=false;
    }
    pick(mode){
      if(!this.editable)return;if(mode==='goal'&&(!this.draft.unitsConfirmed||!this.liveUnits(this.draft.units).length)){this.api.notice('请先框选并确认单位');return;}
      this.mode=mode;this.chosen=new Set(this.liveUnits(this.draft.units));this.targets=[...targetsOf(this.draft)];
      this.resetGesture();this.api.beginPick();$('play').classList.add('ai-picking');$('pause').hidden=true;this.tools.hidden=false;this.updatePick();
    }
    leavePick(){this.mode=null;this.resetGesture();$('play').classList.remove('ai-picking');this.tools.hidden=true;this.goalEditor.hidden=true;this.api.endPick();this.openPause();this.api.render();}
    async confirmPick(){
      const mode=this.mode;if(!mode)return;
      this.chosen=new Set(this.liveUnits([...this.chosen]));
      const clear=mode==='units'&&!this.chosen.size?'cancel':mode==='goal'&&!this.targets.length?'clear_goal':mode;
      const extra=mode==='units'?{units:[...this.chosen]}:clear==='goal'?{goals:this.targets,cell:this.target}:{};
      if(await this.configure(clear,extra))this.leavePick();
    }
    unitCount(count){return (this.presentation?.unitCountText||'{count} 支部队').replace('{count}',count);}
    goalName(cell){return (this.presentation?.goalText||'目标 {cell}').replace('{cell}',cell);}
    goalCount(count){return (this.presentation?.goalCountText||'{count} 个目标').replace('{count}',count);}
    updatePick(){
      this.confirm.disabled=false;this.count.textContent=this.mode==='units'?this.unitCount(this.chosen.size):this.goalCount(this.targets.length);
      this.clearSelection.hidden=this.mode!=='units';this.clearSelection.disabled=!this.chosen.size;this.goalEditor.hidden=this.mode!=='goal';
      this.goalList.replaceChildren(...this.targets.map((cell,index)=>{
        const row=element('li','ai-goal-row');row.dataset.cell=String(cell);row.append(element('span','ai-goal-name',this.goalName(cell)));
        for(const [key,symbol,label,disabled] of [['up','\u2191',this.presentation?.raisePriorityText||'提高优先级',index===0],['down','\u2193',this.presentation?.lowerPriorityText||'降低优先级',index===this.targets.length-1],['remove','\u00d7',this.presentation?.removeTargetText||'移除目标',false]]){
          const button=element('button','ai-goal-tool',symbol);button.type='button';button.title=button.ariaLabel=label;button.dataset.action=key;button.disabled=disabled;
          button.onclick=()=>{if(key==='remove')this.targets.splice(index,1);else{const next=index+(key==='up'?-1:1);[this.targets[index],this.targets[next]]=[this.targets[next],this.targets[index]];}this.updatePick();};row.append(button);
        }
        return row;
      }));this.api.render();
    }
    resetGesture(){this.pointers.clear();this.box.hidden=true;this.gesture=null;this.dragged=false;}
    input(type,e){
      if(!this.mode)return false;
      const pos={x:e.clientX,y:e.clientY};
      if(type==='down'){
        this.pointers.set(e.pointerId,pos);this.dragged=false;
        if(this.pointers.size===1){this.gesture={...pos,button:e.button,origin:pos};this.box.hidden=true;}
        else{this.box.hidden=true;this.gesture=null;this.dragged=true;}
      }else if(type==='move'){
        const prior=this.pointers.get(e.pointerId);if(!prior)return true;
        const old=[...this.pointers.values()];this.pointers.set(e.pointerId,pos);
        if(this.pointers.size>1){const now=[...this.pointers.values()],a=old[0],b=old[1],c=now[0],d=now[1];const distance=Math.hypot(a.x-b.x,a.y-b.y),next=Math.hypot(c.x-d.x,c.y-d.y);
          this.api.pan((c.x+d.x-a.x-b.x)/2,(c.y+d.y-a.y-b.y)/2);if(distance>0)this.api.zoom(next/distance,(c.x+d.x)/2,(c.y+d.y)/2);this.dragged=true;
        }else if(this.gesture){const g=this.gesture;if(Math.hypot(pos.x-g.x,pos.y-g.y)>5)this.dragged=true;
          if(g.button>0||this.mode==='goal'){this.api.pan(pos.x-prior.x,pos.y-prior.y);}
          else if(this.dragged){this.box.hidden=false;Object.assign(this.box.style,{left:Math.min(g.x,pos.x)+'px',top:Math.min(g.y,pos.y)+'px',width:Math.abs(pos.x-g.x)+'px',height:Math.abs(pos.y-g.y)+'px'});}
        }
      }else if(type==='up'){
        this.pointers.delete(e.pointerId);
        if(this.pointers.size===0&&this.gesture){const g=this.gesture;
          if(this.mode==='units'&&g.button===0){
            if(this.dragged){for(const unit of this.state.units){if(unit.owner!==this.state.playerFaction)continue;const p=this.api.unitScreen(unit);if(p.x>=Math.min(g.x,pos.x)&&p.x<=Math.max(g.x,pos.x)&&p.y>=Math.min(g.y,pos.y)&&p.y<=Math.max(g.y,pos.y))this.chosen.add(unit.id);}}
            else{const cell=this.api.cellAt(pos.x,pos.y),unit=this.state.units.find(u=>u.owner===this.state.playerFaction&&u.cell===cell);if(unit){if(this.chosen.has(unit.id))this.chosen.delete(unit.id);else this.chosen.add(unit.id);}}
          }else if(this.mode==='goal'&&!this.dragged&&g.button===0){const cell=this.api.cellAt(pos.x,pos.y);if(this.state.cells[cell]?.active){const index=this.targets.indexOf(cell);if(index>=0)this.targets.splice(index,1);else if(this.targets.length<32)this.targets.push(cell);else this.api.notice(this.presentation?.targetLimitText||'最多设置32个目标');}}
          this.resetGesture();this.updatePick();
        }
      }else this.resetGesture();
      this.api.render();return true;
    }
    overlay(){return this.mode?{mode:this.mode,units:[...this.chosen],goal:this.mode==='goal'?this.target:null,goals:this.mode==='goal'?this.targets:[]}:null;}
    async execute(){
      if(this.control.enabled){try{await this.api.stop();}catch(e){this.api.notice(e.message);}return;}
      if(!this.editable)return;try{await this.api.action('player_ai_start');}catch(e){this.api.notice(e.message);}
    }
    reset(){this.mode=null;this.resetGesture();this.modal.hidden=true;this.tools.hidden=true;this.goalEditor.hidden=true;$('play').classList.remove('ai-picking');$('pause').classList.remove('ai-sidebar-expanded');}
  }
  root.Ew4AiControl={Panel};
})(globalThis);
