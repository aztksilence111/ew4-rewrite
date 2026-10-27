'use strict';
(function(root){
  const $=id=>document.getElementById(id);
  const element=(tag,cls,text)=>{const el=document.createElement(tag);el.className=cls;if(text!==undefined)el.textContent=text;return el;};
  const policies=['manual','free','aggressive','cautious','hold','retreat'];
  const kinds=['attack','defend','line_attack','line_defend'];
  const taskColors=['#ffe995','#55d9e8','#cf8cf3','#ffad62','#8ee17b','#f38eb6','#83afff','#e3d36f','#73d5b5','#dba6ff','#f0b69d','#b3d86b','#96c9ed','#e89fe1','#babdf7','#dae7a0'];
  const targetsOf=order=>Array.isArray(order?.goals)?order.goals:Number.isInteger(order?.goal)?[order.goal]:[];
  class Panel{
    constructor(api){
      this.api=api;this.mode=null;this.chosen=new Set();this.targets=[];this.pointers=new Map();
      this.panel=element('aside','ai-control');this.panel.id='ai-control';this.panel.setAttribute('aria-label','AI设定');
      this.title=element('button','ai-control-title','AI设定');this.title.type='button';this.title.onclick=()=>{this.closeMission();$('pause').classList.toggle('ai-sidebar-expanded');this.fit();this.api.fitObjectives();};
      this.body=element('div','ai-control-body');this.status=element('p','ai-control-status');this.buttons=[];
      this.missionPicker=element('div','ai-mission-picker');this.mission=element('button','ai-mission');this.mission.type='button';this.mission.id='ai-mission';this.mission.setAttribute('aria-label','任务类型');
      this.mission.setAttribute('role','combobox');this.mission.setAttribute('aria-haspopup','listbox');this.mission.setAttribute('aria-controls','ai-mission-options');this.mission.setAttribute('aria-expanded','false');
      this.missionText=element('span','ai-mission-text');const missionArrow=element('span','ai-mission-arrow');missionArrow.setAttribute('aria-hidden','true');this.mission.append(this.missionText,missionArrow);
      this.missionOptions=element('div','ai-mission-options');this.missionOptions.id='ai-mission-options';this.missionOptions.hidden=true;this.missionOptions.setAttribute('role','listbox');this.missionOptions.setAttribute('aria-label','任务类型');
      this.missionChoices=kinds.map(kind=>{const option=element('button','ai-mission-option');option.type='button';option.dataset.kind=kind;option.id='ai-mission-'+kind;option.setAttribute('role','option');option.tabIndex=-1;
        option.onclick=event=>{this.closeMission(event.detail===0);if(kind!==(this.draft.kind||'attack'))void this.configure('mission',{kind});};this.missionOptions.append(option);return option;});
      this.mission.onclick=()=>this.setMissionOpen(this.missionOptions.hidden);this.missionPicker.append(this.mission,this.missionOptions);this.body.append(this.missionPicker);
      for(const event of ['pointerdown','pointerup','click'])this.missionPicker.addEventListener(event,e=>e.stopPropagation());
      document.addEventListener('pointerdown',e=>{if(!this.missionPicker.contains(e.target))this.closeMission();},true);
      this.missionPicker.addEventListener('focusout',()=>setTimeout(()=>{if(!this.missionPicker.contains(document.activeElement))this.closeMission();},0));
      document.addEventListener('keydown',e=>{
        if(e.key==='Escape'&&this.closeMission(true)){e.preventDefault();e.stopImmediatePropagation();return;}
        if(!this.missionPicker.contains(e.target)||!['ArrowDown','ArrowUp','Home','End'].includes(e.key)||this.mission.disabled)return;
        this.setMissionOpen(true);const index=this.missionChoices.indexOf(document.activeElement),current=kinds.indexOf(this.mission.value);
        this.missionChoices[e.key==='Home'?0:e.key==='End'?kinds.length-1:index<0?current:(index+(e.key==='ArrowDown'?1:-1)+kinds.length)%kinds.length].focus();e.preventDefault();
      },true);
      this.radiusLabel=element('label','ai-radius');this.radiusText=element('span','','防守范围');this.radius=element('input','');this.radius.type='number';this.radius.min='0';this.radius.max='6';this.radius.step='1';this.radius.id='ai-radius';
      this.radius.onchange=()=>this.configure('radius',{radius:Number(this.radius.value)});this.radiusLabel.append(this.radiusText,this.radius);this.body.append(this.radiusLabel);
      for(let i=0;i<4;i++){const b=element('button','native-command ai-control-command');b.type='button';b.id=['ai-policy','ai-units','ai-goal','ai-execute'][i];this.buttons.push(b);this.body.append(b);}
      this.auto=element('input','');this.auto.type='checkbox';this.auto.id='ai-auto-battle';
      const autoLabel=element('label','ai-control-auto');this.autoLabel=element('span','','自动战斗');autoLabel.append(this.auto,this.autoLabel);this.body.append(autoLabel,this.status);
      this.goalSummary=element('ol','ai-goal-summary');this.body.append(this.goalSummary);
      this.lineButton=element('button','native-command ai-control-command','绘制部署线');this.lineButton.id='ai-line';this.lineButton.onclick=()=>this.pick('line');this.body.insertBefore(this.lineButton,this.buttons[2]);
      this.deployLabel=element('label','ai-control-auto');this.deploy=element('input','');this.deploy.type='checkbox';this.deploy.id='ai-deploy-first';this.deployText=element('span','','部署完成后进攻');this.deployLabel.append(this.deploy,this.deployText);this.deploy.onchange=async()=>{this.pendingDeploy=this.deploy.checked;await this.configure('deploy_first',{deployFirst:this.pendingDeploy});this.pendingDeploy=null;this.sync();};this.body.insertBefore(this.deployLabel,this.buttons[3]);
      this.taskBar=element('nav','ai-task-bar');this.taskBar.setAttribute('aria-label','任务切换');
      const taskButton=(id,symbol,label,action)=>{const b=element('button','native-command ai-task-tool',symbol);b.id=id;b.type='button';b.title=b.ariaLabel=label;b.onclick=action;this.taskBar.append(b);return b;};
      this.previousTask=taskButton('ai-task-prev','\u2190','上一个任务',()=>this.switchTask(-1));
      this.taskLabel=element('span','ai-task-label');this.taskLabel.id='ai-task-label';this.taskNameLabel=element('span','');this.taskIndex=element('span','');this.taskLabel.append(this.taskNameLabel,this.taskIndex);this.taskBar.append(this.taskLabel);
      this.nextTask=taskButton('ai-task-next','\u2192','下一个任务',()=>this.switchTask(1));
      this.addTask=taskButton('ai-task-add','+','新增任务',()=>this.configure('task_add'));
      this.deleteTask=taskButton('ai-task-delete','\u00d7','删除当前任务',()=>this.configure('task_delete'));this.body.append(this.taskBar);
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
      addEventListener('resize',()=>{this.closeMission();if(this.mode)this.resetGesture();this.fit();});addEventListener('blur',()=>{this.closeMission();this.resetGesture();});
      document.addEventListener('keydown',e=>{if(e.key==='Escape'){if(this.mode)this.leavePick();else this.modal.hidden=true;}});
    }
    get state(){return this.api.state();}
    get control(){return this.state?.playerAi||{};}
    get tasks(){return this.control.tasks||[{id:1,colorIndex:0,draft:this.control.draft||{},enabled:!!this.control.enabled}];}
    get task(){return this.tasks.find(t=>t.id===(this.control.activeTask||1))||this.tasks[0];}
    get taskEnabled(){return !!this.task.enabled;}
    taskColor(task){return (this.presentation?.taskColors||taskColors)[task.colorIndex||0]||taskColors[0];}
    taskName(task){return (this.presentation?.taskText||'任务{number}').replace('{number}',task.id);}
    get draft(){return this.control.draft||{};}
    get target(){return this.targets[0]??null;}
    liveUnits(ids){const own=new Set(this.state?.units.filter(u=>u.owner===this.state.playerFaction).map(u=>u.id)||[]);return [...new Set(ids||[])].filter(id=>own.has(id));}
    get selectable(){const s=this.state;return s&&s.phase==='Active'&&s.activeFaction===s.playerFaction&&!s.dialogue&&!s.scenarioPopup&&!this.api.busy();}
    get editable(){return this.selectable&&!this.control.endingTurn&&!this.taskEnabled;}
    setMissionOpen(open){
      open=!!open&&this.editable&&!$('pause').hidden;
      this.missionOptions.hidden=!open;this.mission.setAttribute('aria-expanded',String(open));
      for(const option of this.missionChoices)option.tabIndex=open?0:-1;
      if(open)this.missionOptions.scrollTop=this.missionChoices[kinds.indexOf(this.mission.value)]?.offsetTop||0;
    }
    closeMission(focus=false){const open=!this.missionOptions.hidden;if(open){this.setMissionOpen(false);if(focus&&!this.mission.disabled)this.mission.focus();}return open;}
    switchTask(delta){const index=this.tasks.findIndex(t=>t.id===this.task.id),next=this.tasks[index+delta];if(next)this.configure('task_select',{taskId:next.id});}
    async configure(step,extra={}){
      if(!(step.startsWith('task_')?this.selectable:this.editable))return false;
      const moved=step==='units'?this.tasks.filter(t=>t.id!==this.task.id).reduce((n,t)=>n+(t.draft.units||[]).filter(id=>extra.units.includes(id)).length,0):0;
      try{await this.api.action('player_ai_config',{offer:step,...extra});if(moved)this.api.notice((this.presentation?.transferredText||'已转入{count}支部队').replace('{count}',moved));return true;}catch(e){this.api.notice(e.message);this.sync();return false;}
    }
    sync(){
      const s=this.state;if(!s){this.reset();return;}
      const p=s.aiPresentation||{};this.presentation=p;
      const set=(el,text)=>{if(el.textContent!==text)el.textContent=text;};
      set(this.title,p.title||'AI设定');
      const labels=p.buttons||['指定战斗方针','框选单位','指定行动目标','开始执行','停止执行'];
      const kind=this.draft.kind||'attack',lineMode=kind.startsWith('line_');
      const kindNames=p.taskKinds||['进攻地点','防守地点','沿线进攻','沿线防守'];
      this.mission.value=kind;this.mission.disabled=!this.editable;set(this.missionText,kindNames[kinds.indexOf(kind)]||kindNames[0]);
      this.missionChoices.forEach((option,i)=>{set(option,kindNames[i]);option.setAttribute('aria-selected',String(kinds[i]===kind));});
      if(!this.editable||$('pause').hidden||this.missionTask!==this.task.id)this.closeMission();this.missionTask=this.task.id;
      this.radiusLabel.hidden=kind==='attack';this.radius.value=String(this.draft.radius??2);this.radius.disabled=!this.editable;set(this.radiusText,p.radiusText||'防守范围');
      this.lineButton.hidden=!lineMode;this.lineButton.disabled=!this.editable;set(this.lineButton,p.lineText||'绘制部署线');this.lineButton.dataset.complete=String((this.draft.line||[]).length>=2);
      this.deployLabel.hidden=kind!=='line_attack';this.deploy.checked=this.pendingDeploy??this.draft.deployFirst!==false;this.deploy.disabled=!this.editable;set(this.deployText,p.deployFirstText||'部署完成后进攻');
      const units=this.liveUnits(this.draft.units),goals=targetsOf(this.draft);
      const complete=[this.draft.policyConfirmed,this.draft.unitsConfirmed&&units.length>0,this.draft.goalConfirmed&&(kind==='line_defend'?(this.draft.line||[]).length>=2:goals.length>0)];
      this.buttons.forEach((b,i)=>{const label=i===2&&kind==='defend'?p.defendGoalText||'指定防守地点':i===2&&kind==='line_attack'?p.attackGoalText||'指定进攻目标':labels[i===3&&this.taskEnabled?4:i];set(b,label);b.setAttribute('aria-label',label);b.title=label;b.dataset.complete=String(!!complete[i]);
        b.hidden=i===2&&kind==='line_defend';b.disabled=i===3?this.taskEnabled?this.api.transitioning():!this.editable||!complete.every(Boolean):!this.editable;});
      this.auto.disabled=!this.editable;this.auto.checked=this.pendingAuto??!!this.control.autoBattle;
      const names=p.policies||['玩家指挥','自由行动','积极进攻','消极进攻','原地防守','逃离战场'];
      let summary=this.unitCount(units.length)+' · '+(this.draft.policy?names[policies.indexOf(this.draft.policy)]:p.noPolicyText||'未设方针')+' · '+(goals.length?this.goalCount(goals.length):p.noGoalText||'未设目标');
      if(this.taskEnabled&&kind!=='attack'){
        const assigned=Object.keys(this.task.stations||{}).length,unassigned=units.length-assigned;
        summary=this.unitCount(units.length)+' · '+(kind==='line_attack'?(this.task.deployed||this.draft.deployFirst===false?p.advancingText||'推进中':p.deployingText||'部署中'):p.defendingText||'防守中')+(unassigned?' · '+(p.unassignedText||'{count}支无可用站位').replace('{count}',unassigned):'');
      }
      if(this.status.ariaLabel!==summary){this.status.ariaLabel=summary;this.status.replaceChildren(...summary.split(' · ').map(text=>element('span','',text)));}
      const index=this.tasks.findIndex(t=>t.id===this.task.id),taskLabel=this.taskName(this.task)+' ('+(index+1)+'/'+this.tasks.length+')';set(this.taskNameLabel,this.taskName(this.task));set(this.taskIndex,'('+(index+1)+'/'+this.tasks.length+')');this.taskLabel.ariaLabel=taskLabel;this.taskLabel.style.setProperty('--ai-task-color',this.taskColor(this.task));
      this.previousTask.disabled=!this.selectable||index===0;this.nextTask.disabled=!this.selectable||index===this.tasks.length-1;this.addTask.disabled=!this.selectable||this.control.endingTurn||this.tasks.length>=16;this.deleteTask.disabled=!this.selectable||this.control.endingTurn||this.taskEnabled;
      for(const [b,label] of [[this.previousTask,p.previousTaskText||'上一个任务'],[this.nextTask,p.nextTaskText||'下一个任务'],[this.addTask,p.addTaskText||'新增任务'],[this.deleteTask,p.deleteTaskText||'删除当前任务']])b.title=b.ariaLabel=label;
      this.goalSummary.replaceChildren(...goals.map(cell=>element('li','',this.goalName(cell))));this.goalSummary.hidden=!goals.length;
      for(const el of [this.confirm,this.policyConfirm]){set(el,p.confirmText||'确认');el.title=el.ariaLabel=p.confirmText||'确认';}
      set(this.cancelPick,p.cancelSelectionText||'取消选择');set(this.autoLabel,p.autoBattleText||'自动战斗');set(this.policyTitle,labels[0]);
      this.policyCancel.title=this.policyCancel.ariaLabel=p.cancelText||'取消';
      set(this.clearSelection,p.clearSelectionText||'取消全部选中');set(this.goalHeader,p.goalOrderText||'目标优先顺序');
      for(const host of [this.panel,this.modal,this.tools,this.box,this.goalEditor]){
        const style=host.style;
        style.setProperty('--ai-task-color',this.taskColor(this.task));
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
      this.closeMission();
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
      this.mode=mode;this.chosen=new Set(this.liveUnits(this.draft.units));this.targets=mode==='line'?[...(this.draft.line||[])]:[...targetsOf(this.draft)];
      this.resetGesture();this.api.beginPick();$('play').classList.add('ai-picking');$('pause').hidden=true;this.tools.hidden=false;this.updatePick();
    }
    leavePick(){this.mode=null;this.resetGesture();$('play').classList.remove('ai-picking');this.tools.hidden=true;this.goalEditor.hidden=true;this.api.endPick();this.openPause();this.api.render();}
    async confirmPick(){
      const mode=this.mode;if(!mode)return;
      this.chosen=new Set(this.liveUnits([...this.chosen]));
      const clear=mode==='units'&&!this.chosen.size?'cancel':mode==='goal'&&!this.targets.length?'clear_goal':mode;
      const extra=mode==='units'?{units:[...this.chosen]}:mode==='line'?{line:this.targets}:clear==='goal'?{goals:this.targets,cell:this.target}:{};
      if(await this.configure(clear,extra))this.leavePick();
    }
    unitCount(count){return (this.presentation?.unitCountText||'{count} 支部队').replace('{count}',count);}
    goalName(cell){return (this.presentation?.goalText||'目标 {cell}').replace('{cell}',cell);}
    goalCount(count){return (this.presentation?.goalCountText||'{count} 个目标').replace('{count}',count);}
    updatePick(){
      this.confirm.disabled=this.mode==='line'&&this.targets.length<2;this.count.textContent=this.taskName(this.task)+' · '+(this.mode==='units'?this.unitCount(this.chosen.size):this.goalCount(this.targets.length));
      this.clearSelection.hidden=this.mode!=='units';this.clearSelection.disabled=!this.chosen.size;this.goalEditor.hidden=this.mode==='units';
      this.goalHeader.textContent=this.mode==='line'?this.presentation?.lineText||'绘制部署线':this.presentation?.goalOrderText||'目标优先顺序';
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
          if(g.button>0||this.mode!=='units'){this.api.pan(pos.x-prior.x,pos.y-prior.y);}
          else if(this.dragged){this.box.hidden=false;Object.assign(this.box.style,{left:Math.min(g.x,pos.x)+'px',top:Math.min(g.y,pos.y)+'px',width:Math.abs(pos.x-g.x)+'px',height:Math.abs(pos.y-g.y)+'px'});}
        }
      }else if(type==='up'){
        this.pointers.delete(e.pointerId);
        if(this.pointers.size===0&&this.gesture){const g=this.gesture;
          if(this.mode==='units'&&g.button===0){
            if(this.dragged){for(const unit of this.state.units){if(unit.owner!==this.state.playerFaction)continue;const p=this.api.unitScreen(unit);if(p.x>=Math.min(g.x,pos.x)&&p.x<=Math.max(g.x,pos.x)&&p.y>=Math.min(g.y,pos.y)&&p.y<=Math.max(g.y,pos.y))this.chosen.add(unit.id);}}
            else{const cell=this.api.cellAt(pos.x,pos.y),unit=this.state.units.find(u=>u.owner===this.state.playerFaction&&u.cell===cell);if(unit){if(this.chosen.has(unit.id))this.chosen.delete(unit.id);else this.chosen.add(unit.id);}}
          }else if(this.mode!=='units'&&!this.dragged&&g.button===0){const cell=this.api.cellAt(pos.x,pos.y);if(this.state.cells[cell]?.active){const index=this.targets.indexOf(cell);if(index>=0)this.targets.splice(index,1);else if(this.draft.kind==='defend'&&this.mode==='goal')this.targets=[cell];else if(this.targets.length<32)this.targets.push(cell);else this.api.notice(this.presentation?.targetLimitText||'最多设置32个目标');}}
          this.resetGesture();this.updatePick();
        }
      }else this.resetGesture();
      this.api.render();return true;
    }
    overlay(){
      const all=this.tasks.map(t=>({taskId:t.id,color:this.taskColor(t),units:this.liveUnits(t.draft.units),goals:targetsOf(t.draft),line:t.draft.line||[],kind:t.draft.kind||'attack',radius:t.draft.radius??2,stations:t.stations||{},current:t.id===this.task.id}));
      if(this.mode){const current=all.find(t=>t.current);current.units=[...this.chosen];if(this.mode==='goal')current.goals=this.targets;if(this.mode==='line')current.line=this.targets;}
      if(this.mode||!$('pause').hidden){const current=all.find(t=>t.current);return {tasks:all,mode:this.mode,units:current.units,goal:current.goals[0]??null,goals:current.goals};}
      const selected=this.api.selectedTask?.();return selected?{tasks:all.filter(t=>t.taskId===selected)}:null;
    }
    async execute(){
      if(this.taskEnabled){try{await this.api.stop();}catch(e){this.api.notice(e.message);}return;}
      if(!this.editable)return;try{await this.api.action('player_ai_start');}catch(e){this.api.notice(e.message);}
    }
    reset(){this.closeMission();this.mode=null;this.resetGesture();this.modal.hidden=true;this.tools.hidden=true;this.goalEditor.hidden=true;$('play').classList.remove('ai-picking');$('pause').classList.remove('ai-sidebar-expanded');}
  }
  root.Ew4AiControl={Panel};
})(globalThis);
