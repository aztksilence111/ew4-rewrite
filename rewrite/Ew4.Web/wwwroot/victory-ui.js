'use strict';
(function(root){
  class Results {
    constructor(api){
      this.api=api;this.id=null;this.dismissed=null;this.layout=null;this.pending=0;
      addEventListener('ew4uiscale',()=>this.fit());
    }
    fit(){
      if(!this.layout?.current())return;
      const base=Number(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale'))||1;
      const fit=Math.min((innerWidth-24)/this.layout.width,(innerHeight-24)/this.layout.height);
      this.layout.body.parentElement.style.setProperty('--ui-scale',Math.min(fit,Math.max(base,Math.min(1.15,fit))));
    }
    reset(){
      cancelAnimationFrame(this.pending);this.pending=0;
      if(this.layout?.current())this.api.close();
      this.id=null;this.dismissed=null;this.layout=null;this.failureTalk=null;
    }
    sync(state){
      this.state=state;
      const result=state?.battleResult;
      if(!result){this.reset();return;}
      if(this.id!==result.id){this.reset();this.id=result.id;}
      if(state.dialogue||state.scenarioPopup||this.dismissed===result.id||this.layout?.current()||this.pending)return;
      let readyFrames=0;
      const present=()=>{
        this.pending=0;
        if(this.id!==result.id||this.dismissed===result.id)return;
        if(this.state.dialogue||this.state.scenarioPopup)return;
        readyFrames=this.api.ready(this.state)?readyFrames+1:0;
        if(readyFrames>=2){this.open(this.state.battleResult);return;}
        this.pending=requestAnimationFrame(present);
      };
      this.pending=requestAnimationFrame(present);
    }
    open(result){
      const api=this.api,won=result.outcome==='Victory';
      if(!won&&!result.presentation?.layout&&!result.presentation?.defeatLayout&&result.failureDescription&&this.failureTalk!==result.id){
        this.failureTalk=result.id;api.failureDialogue(result);return;
      }
      const screen=result.presentation||{},bindings=screen.bindings||{
        name:'text_battle',round:'text_roundval',awardMedals:'text_award_val',collectedMedals:'text_gain_val',
        minimumRatingRound:'text_star',highestRatingRound:'text_star2',thresholds:'group_starlevel',
        stars:'lbox_stars',generals:'lbox_general',continue:'btn_continue',restart:'btn_restart',exit:'btn_exit'
      };
      const definition=(!won&&screen.defeatLayout)||screen.layout;
      const layoutApi=definition?{...api,manifest:{...api.manifest,layouts:{...api.manifest.layouts,form_mod_victory:definition}}}:api;
      const dismiss=()=>{this.dismissed=result.id;};
      const layout=this.layout=new Ew4NativeUI.Layout(layoutApi,definition?'form_mod_victory':'form_victory',dismiss);
      layout.body.classList.add('victory-result');
      this.fit();
      layout.body.dataset.result=result.id;
      api.title(won?(screen.victoryTitle||'title_victory'):(screen.defeatTitle||'title_failure'));
      if(bindings.name)layout.text(bindings.name,result.name);
      if(!definition){
        if(!won){
          const width=layout.width-parseFloat(layout.get('scale_line_ver').style.left);
          for(const id of ['text_round','text_roundval'])layout.get(id).style.width=width+'px';
        }
        for(const [label,value] of [['text_award','text_award_val'],['text_gain','text_gain_val']]){
          const el=layout.get(label),a=el.nativeAttributes;
          el.replaceChildren(api.text(api.manifest.strings[a.text]||a.text,'font_text_2_cn_hd','#404040',parseFloat(el.style.width),a.align,
            {singleLine:true,maxHeight:parseFloat(el.style.height),inkCenter:true}));
          layout.get(value).style.top=el.style.top;
        }
      }
      for(const key of ['round','awardMedals','collectedMedals','minimumRatingRound','highestRatingRound'])if(bindings[key]){
        const el=layout.get(bindings[key]),a=el.nativeAttributes,threshold=key==='minimumRatingRound'||key==='highestRatingRound';
        const parentWidth=parseFloat(el.parentElement.style.width)||layout.width;
        const width=Math.max(1,Math.min(parseFloat(el.style.width),parentWidth-parseFloat(el.style.left)-2));
        el.style.width=width+'px';
        const font=(a.font||'font_char_2').replace(/^(font_(?:text|char)_\d)$/,(_,name)=>name+(name.startsWith('font_text')?'_cn_hd':'_hd'));
        const rgba=(a.color||a.textcolor||'64,64,64,255').split(',').map(Number);
        const color=threshold&&!a.color&&!a.textcolor?'#ffffff':`rgba(${rgba[0]},${rgba[1]},${rgba[2]},${rgba[3]/255})`;
        el.replaceChildren(api.text(result[key],font,color,width,a.align||'left',{
          singleLine:true,maxHeight:parseFloat(el.style.height),numeric:true,inkCenter:!definition
        }));
      }
      if(bindings.thresholds)layout.hide(bindings.thresholds,!won||result.stars===0);
      if(bindings.stars){
        layout.fill(bindings.stars,...Array.from({length:5},(_,i)=>api.uiImage(i<result.stars?(screen.starImage||'star_middle.png'):(screen.emptyStarImage||'diffcult_1.png'),13,13)));
        layout.get(bindings.stars).setAttribute('aria-label',result.stars+'星');
      }
      if(bindings.generals){
        const generals=layout.get(bindings.generals),count=Math.max(6,result.generals.length);
        const height=Math.min(98,parseFloat(generals.style.height)),width=height*78/98;
        generals.classList.add('result-generals');
        Object.assign(generals.style,{transform:'none',display:'flex',gap:'7px',
          justifyContent:count*width+(count-1)*7<=parseFloat(generals.style.width)?'space-between':'flex-start'});
        generals.style.setProperty('--result-card-width',width+'px');
        generals.style.setProperty('--result-card-height',height+'px');
        const slots=Array.from({length:count},(_,i)=>{
          const general=result.generals[i],slot=document.createElement('div');slot.className='result-general';
          if(general){
            slot.setAttribute('aria-label',general.name+(general.withdrawn?' 已退场':''));slot.title=general.name;
            slot.append(api.portrait(general.image));
            for(const [value,name] of [[general.rank,'rank_'],[general.title,'class_']])if(value>0){
              const badge=api.uiImage(name+Math.min(value,name==='rank_'?14:9)+'.png',21,24);badge.className=name==='rank_'?'result-rank':'result-title';slot.append(badge);
            }
            if(general.withdrawn)slot.classList.add('withdrawn');
          }else{slot.append(api.uiImage('button_add_generals.png',78,78));slot.setAttribute('aria-label','空将领位');}
          return slot;
        });
        layout.fill(bindings.generals,...slots);
      }
      layout.hide(bindings.continue,!won);layout.hide(bindings.restart,won);
      layout.action(bindings.continue,'继续',async()=>{await api.continue(result);dismiss();api.close();});
      layout.action(bindings.restart,'重新开始',async()=>{await api.restart();dismiss();api.close();});
      layout.action(bindings.exit,'退出',()=>{dismiss();api.close();api.exit();});
    }
  }
  root.Ew4VictoryUI={Results};
})(globalThis);
