'use strict';
(function(root){
  class LocalMultiplayer {
    constructor(api){this.api=api;this.panel=null;this.resultId=null;this.pending=0;this.state=null;addEventListener('resize',()=>this.fit());}
    text(value){return this.api.text(value,'font_text_2_cn_hd','#404040');}
    fit(){
      if(!this.panel?.isConnected||document.getElementById('detail-modal').hidden)return;
      this.panel.closest('.dialog').style.setProperty('--ui-scale',Math.min(1.5,(innerWidth-24)/380,(innerHeight-24)/300));
    }
    panelFor(title,onClose,confirm){
      const body=this.api.detail(title,380,300,confirm,()=>{body.parentElement.style.removeProperty('--ui-scale');onClose?.();});
      body.className='local-multiplayer-content';
      const panel=document.createElement('div');panel.className='local-multiplayer-panel';body.append(panel);this.panel=panel;
      this.fit();return panel;
    }
    async choose(scenario,country){
      const factions=await this.api.rpc('local-players',{scenario,country});
      return new Promise(resolve=>{
        const selected=new Set(factions.filter(f=>f.locked).map(f=>f.id));
        const done=value=>{this.panel?.closest('.dialog')?.style.removeProperty('--ui-scale');resolve(value);};
        const panel=this.panelFor('选择本局势力',()=>done(null),()=>done(factions.filter(f=>selected.has(f.id)).map(f=>f.id)));
        const toolbar=document.createElement('div');toolbar.className='local-selection-toolbar';
        const inputs=[];
        for(const [label,select] of [['全选',true],['全不选',false]]){
          const button=this.api.button('',()=>{for(const {input,faction} of inputs){input.checked=select||faction.locked;if(input.checked)selected.add(faction.id);else selected.delete(faction.id);}},toolbar);
          button.setAttribute('aria-label',label);button.append(this.text(label));
        }
        panel.append(toolbar);
        const list=document.createElement('div');list.className='local-faction-list';list.setAttribute('role','group');list.setAttribute('aria-label','本局玩家势力');
        for(const faction of factions){
          const label=document.createElement('label');label.className='local-faction-row';label.dataset.faction=faction.id;
          const input=document.createElement('input');input.type='checkbox';input.checked=faction.locked;input.disabled=faction.locked;
          input.setAttribute('aria-label',faction.name);input.onchange=()=>{if(input.checked)selected.add(faction.id);else selected.delete(faction.id);};
          inputs.push({input,faction});
          const flag=document.createElement('img');flag.src=faction.flagImage?'/content/extensions/'+faction.flagImage.slice('rewrite/'.length).split('/').map(encodeURIComponent).join('/'):'/content/ui/image_flag_hd/'+encodeURIComponent(faction.flag)+'1.png';flag.alt='';
          const name=document.createElement('span');name.className='local-faction-name';name.append(this.text(faction.name));
          label.append(input,flag,name);list.append(label);
        }
        panel.append(list);
      });
    }
    sync(state){
      this.state=state;
      const local=state?.localMultiplayer,result=state?.battleResult;
      if(!local||!result){cancelAnimationFrame(this.pending);this.pending=0;this.resultId=null;return;}
      if(this.resultId===result.id||this.pending)return;
      if(state.dialogue||state.scenarioPopup||!this.api.ready()){
        this.pending=requestAnimationFrame(()=>{this.pending=0;this.sync(this.state);});return;
      }
      this.resultId=result.id;
      const winner=state.factions.find(f=>f.id===local.winnerFaction);
      const panel=this.panelFor('本局结果');
      const winners=new Set((local.results||[]).filter(f=>f.outcome==='Victory').map(f=>f.id));
      const title=document.createElement('div');title.className='local-winner';title.append(this.text(winner?winner.name+(local.shareAlliedVictory?'及盟友 胜利':' 胜利'):'本局结束'));panel.append(title);
      const list=document.createElement('div');list.className='local-faction-list';list.setAttribute('aria-label','各势力结果');
      for(const faction of state.factions){
        const row=document.createElement('div');row.className='local-faction-row local-result-row';row.dataset.faction=faction.id;
        const name=document.createElement('span');name.className='local-faction-name';name.append(this.text(faction.name));
        const won=winners.has(faction.id);
        const outcome=document.createElement('span');outcome.className=won?'local-won':'local-lost';
        outcome.append(this.text(won?'胜利':'失败'));row.append(name,outcome);list.append(row);
      }
      panel.append(list);
      const actions=document.createElement('div');actions.className='local-result-actions';
      for(const [label,action] of [['重新开始',()=>this.api.restart(state)],['退出',()=>this.api.main()]]){
        const button=this.api.button('',async()=>{this.api.close();await action();},actions);button.setAttribute('aria-label',label);button.append(this.text(label));
      }
      panel.append(actions);
    }
  }
  root.Ew4LocalMultiplayer={LocalMultiplayer};
})(globalThis);
