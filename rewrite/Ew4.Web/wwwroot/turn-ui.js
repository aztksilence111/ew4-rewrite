'use strict';
(function(root){
  class Turns {
    constructor(api){this.api=api;this.seen=new Set();this.pending=null;this.open=false;this.generation=0;}
    hasNewTurn(state){
      const summary=state?.factions.find(f=>f.id===state.playerFaction)?.turnSummary;
      return !!summary&&summary.round>1&&summary.round===state.round&&state.activeFaction===state.playerFaction&&state.phase!=='Finished'
        &&!this.seen.has([state.battleId,state.playerFaction,summary.round].join(':'));
    }
    observe(state,reset=false){
      const faction=state.factions.find(f=>f.id===state.playerFaction),summary=faction?.turnSummary;
      const key=summary&&[state.battleId,faction.id,summary.round].join(':');
      if(reset){this.generation++;this.seen.clear();this.pending=null;this.open=false;if(key)this.seen.add(key);return;}
      if(state.phase==='Finished'){this.pending=null;return;}
      if(!summary||summary.round<=1||summary.round!==state.round||this.seen.has(key))return;
      if(state.activeFaction===state.playerFaction&&state.phase!=='Finished')this.pending={key,summary,faction:faction.id};
    }
    confirmEnd(){
      const state=this.api.state();
      if(!state||!this.api.ready()||this.open||state.activeFaction!==state.playerFaction||state.phase!=='Active'||state.dialogue||state.scenarioPopup||state.story)return;
      const revision=state.revision,battle=state.battleId;
      const layout=new Ew4NativeUI.Layout(this.api,'form_messagebox');
      layout.text('text_info',this.api.manifest.strings.msg_endturn);
      layout.action('btn_cancel',this.api.manifest.strings.btn_cancel,()=>this.api.close());
      layout.action('btn_confirm',this.api.manifest.strings.btn_confirm,async()=>{
        const current=this.api.state();this.api.close();
        if(current?.battleId===battle&&current.revision===revision)await this.api.end();
      });
    }
    tick(){
      const state=this.api.state(),pending=this.pending;
      if(!pending||this.open||!this.api.ready()||state?.activeFaction!==pending.faction||state.phase!=='Active'||state.story||state.dialogue||state.scenarioPopup)return;
      this.pending=null;this.seen.add(pending.key);this.open=true;
      const generation=++this.generation;
      const layout=new Ew4NativeUI.Layout(this.api,'form_roundturn',()=>{
        if(generation!==this.generation)return;
        this.open=false;this.api.resume();
      });
      const summary=pending.summary;
      for(const [id,value] of Object.entries({text_money:summary.income.money,text_indus:summary.income.industry,text_roundnum:summary.round,
        text_addfood:summary.income.food,text_delfood:summary.foodConsumption}))layout.text(id,value);
      layout.get('text_roundnum').style.height='32px';layout.text('text_roundnum',summary.round,{size:21});
      const intro=state.stageIntro,campaign=this.api.campaign(state.scenario);
      for(const id of ['image_star','text_roundmin','text_roundmax'])layout.hide(id,!campaign);
      if(campaign){layout.text('text_roundmin',intro?.highestRatingRound??'');layout.text('text_roundmax',intro?.minimumRatingRound??'');}
      else{
        for(const id of ['text_round','text_roundnum'])layout.get(id).style.width='97px';
        layout.text('text_round',this.api.manifest.strings.text_round);
        layout.text('text_roundnum',summary.round);
      }
      const list=layout.get('lbox_general');list.classList.add('round-commanders');
      Object.assign(list.style,{width:'299px',height:'59px',transform:'none',gap:'4px'});
      const populate=roster=>{
        list.replaceChildren();
        const available=roster?.roster.filter(c=>!c.locked)||[],definitions=new Map((roster?.definitions||[]).map(d=>[d.id,d]));
        for(let i=0;i<Math.max(6,available.length);i++){
          const held=available[i],definition=held&&definitions.get(held.definitionId),slot=document.createElement('div');slot.className='round-commander';
          const name=document.createElement('span');name.className='round-commander-name';
          if(definition){
            slot.setAttribute('aria-label',definition.name);slot.append(this.api.portrait(definition.image));
            name.append(this.api.text(definition.name,'font_text_2_cn_hd','#ebe6e1',46.5,'center',{size:6,singleLine:true}));
          }
          else{slot.classList.add('empty');slot.setAttribute('aria-label','空将领槽');}
          slot.append(name);
          list.append(slot);
        }
      };
      populate();
      this.api.rpc('headquarters').then(roster=>{if(generation===this.generation&&layout.current())populate(roster);}).catch(error=>{if(layout.current())this.api.notice(error.message);});
    }
  }
  root.Ew4TurnUI={Turns};
})(globalThis);
