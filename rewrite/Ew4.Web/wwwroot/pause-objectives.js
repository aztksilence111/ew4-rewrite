'use strict';
(function(root){
  const node=(tag,cls)=>{const el=document.createElement(tag);el.className=cls;return el;};
  class Panel {
    constructor(api){
      this.api=api;this.element=document.getElementById('pause-objectives');this.version=null;this.context=null;
      this.serial=0;this.refreshes=0;this.rowUpdates=0;this.rows=new Map();this.ratings=new Map();
      this.header=node('h2','pause-objectives-bar');this.scenario=node('div','pause-objectives-scenario');
      this.scenarioName=node('span','');this.country=node('span','pause-objectives-country');this.scenario.append(this.scenarioName,this.country);
      this.scroll=node('div','pause-objectives-scroll');this.scroll.tabIndex=0;this.scroll.setAttribute('aria-label','战局目标列表');
      this.description=node('p','pause-objectives-description');this.scroll.append(this.description);
      this.sections={};for(const key of ['win','limits','ratings']){const section=node('section','pause-objectives-section'),title=node('h3',''),body=node('div','');section.append(title,body);this.sections[key]={section,title,body};this.scroll.append(section);}
      this.note=node('p','pause-objectives-foot');this.element.replaceChildren(this.header,this.scenario,this.scroll,this.note);
      addEventListener('resize',()=>this.fit());addEventListener('ew4uiscale',()=>this.fit());
    }
    reset(){this.serial++;this.version=null;this.context=null;this.snapshot=null;this.element.hidden=true;this.rows.clear();this.ratings.clear();for(const part of Object.values(this.sections))part.body.replaceChildren();this.scroll.scrollTop=0;document.getElementById('pause').classList.remove('pause-objectives-active');}
    async sync(){
      const state=this.api.state();if(!state||document.getElementById('pause').hidden)return;
      const context=state.battleId+'/'+state.playerFaction;
      if(this.context!==context){this.reset();this.context=context;}
      if(this.snapshot){this.fit();this.api.ready?.(this.snapshot);}
      const serial=++this.serial;
      try{
        const data=await this.api.rpc('pause-objectives',{knownVersion:this.version});
        if(serial!==this.serial||document.getElementById('pause').hidden||this.api.state()?.battleId+'/'+this.api.state()?.playerFaction!==context)return;
        if(data.context!==context)return;
        // An in-flight action may be committed on the host before its response reaches the map.
        if(data.revision!==this.api.state().revision&&this.snapshot)return;
        if(data.version===this.version){this.fit();this.api.ready?.(this.snapshot);return;}
        const snapshot=data.snapshot;if(!snapshot)return;
        const text=JSON.stringify(snapshot);await root.Ew4OriginalUI.prepareText(text);
        if(serial!==this.serial||document.getElementById('pause').hidden)return;
        this.version=data.revision===this.api.state()?.revision?data.version:null;
        this.snapshot=snapshot;this.refreshes++;this.render(snapshot);
      }catch(error){if(serial===this.serial){this.api.notice(error.message);this.element.hidden=true;document.getElementById('pause').classList.remove('pause-objectives-active');this.api.ready?.(null);}}
    }
    write(el,text){if(el.textContent!==String(text??''))el.textContent=text??'';}
    render(snapshot){
      const {model,presentation:p}=snapshot,scrollTop=this.scroll.scrollTop,wasActive=document.getElementById('pause').classList.contains('pause-objectives-active');
      this.element.hidden=!snapshot.visible;document.getElementById('pause').classList.toggle('pause-objectives-active',snapshot.visible);
      if(!wasActive&&snapshot.visible)this.api.repaintMenu();
      for(const [key,value] of Object.entries({background:p.background,'header-background':p.headerBackground,color:p.textColor,accent:p.accentColor,'font-size':p.fontSize+'px','row-gap':p.rowGap+'px','slide-time':p.slideMilliseconds+'ms'}))this.element.style.setProperty('--objective-'+key,value);
      this.element.style.backgroundImage=p.backgroundImage?'url("'+this.api.imagePath(p.backgroundImage)+'")':'';
      this.write(this.header,snapshot.title);this.write(this.scenarioName,snapshot.scenario);this.write(this.country,snapshot.country);
      this.write(this.description,model.description);this.description.hidden=!model.description;this.write(this.note,model.note);
      const titles={win:p.winTitle,limits:p.limitsTitle,ratings:p.ratingsTitle};
      if(this.sectionOrder!==p.sections.join(',')){for(const key of p.sections)this.scroll.append(this.sections[key].section);this.sectionOrder=p.sections.join(',');}
      for(const key of p.sections)this.write(this.sections[key].title,this.api.label(titles[key]));
      const used=new Set();
      const mount=(row,parent,failure=false)=>{
        used.add(row.id);let entry=this.rows.get(row.id);
        if(!entry){const el=node('div','pause-objective-row'),line=node('div','pause-objective-line'),marker=node('i','pause-objective-marker'),copy=node('div','pause-objective-copy'),text=node('p',''),detail=node('p','pause-objective-detail'),children=node('div','pause-objective-group');copy.append(text,detail);line.append(marker,copy);el.append(line,children);entry={el,text,detail,children,signature:null};this.rows.set(row.id,entry);}
        if(entry.el.parentElement!==parent)parent.append(entry.el);
        const text=this.api.label(p.conditionText[row.id]??row.text),signature=JSON.stringify([text,row.detail,row.met,row.relation,failure]);
        if(entry.signature!==signature){this.rowUpdates++;entry.signature=signature;this.write(entry.text,text);this.write(entry.detail,row.detail);entry.detail.hidden=!row.detail;entry.el.dataset.met=row.met===null?'unknown':String(row.met);entry.el.dataset.failure=String(failure);entry.el.dataset.group=String(!!row.children?.length);}
        entry.children.hidden=!row.children?.length;
        for(const child of row.children||[])mount(child,entry.children,failure||row.relation==='failure');
      };
      mount(model.win,this.sections.win.body);for(const limit of model.limits)mount(limit,this.sections.limits.body);
      for(const [id,entry] of this.rows)if(!used.has(id)){entry.el.remove();this.rows.delete(id);}
      this.sections.limits.section.hidden=model.limits.length===0;this.sections.ratings.section.hidden=model.ratings.length===0;
      const ratingUsed=new Set();
      for(const rating of model.ratings){
        ratingUsed.add(rating.stars);let entry=this.ratings.get(rating.stars);
        if(!entry){const el=node('div','pause-rating-row'),stars=node('span','pause-rating-stars'),range=node('span',''),status=node('span','pause-rating-status');el.append(stars,range,status);entry={el,stars,range,status};this.ratings.set(rating.stars,entry);}
        if(entry.el.parentElement!==this.sections.ratings.body)this.sections.ratings.body.append(entry.el);
        const signature=JSON.stringify([rating,p.starImage,p.emptyStarImage]);if(entry.signature!==signature){this.rowUpdates++;entry.signature=signature;entry.el.classList.toggle('expired',!rating.available);entry.stars.replaceChildren(...Array.from({length:5},(_,i)=>this.api.image(i<rating.stars?p.starImage:p.emptyStarImage)));entry.stars.setAttribute('aria-label',rating.stars+'星');this.write(entry.range,rating.start===1&&rating.end!==null?'第 '+rating.end+' 回合及以前':rating.end===null?'第 '+rating.start+' 回合起':rating.start===rating.end?'第 '+rating.start+' 回合':'第 '+rating.start+'–'+rating.end+' 回合');this.write(entry.status,rating.available?'可达成':'已不可达');}
      }
      for(const [stars,entry] of this.ratings)if(!ratingUsed.has(stars)){entry.el.remove();this.ratings.delete(stars);}
      this.fit();this.scroll.scrollTop=scrollTop;this.api.ready?.(snapshot);
    }
    fit(){
      if(!this.snapshot||this.element.hidden)return;
      const p=this.snapshot.presentation,style=getComputedStyle(document.documentElement);
      const width=innerWidth,height=Number(style.getPropertyValue('--screen-height').replace('px',''))||innerHeight;
      const menu=document.querySelector('#pause>.pause-dialog');
      menu.style.left=width/2+'px';menu.style.top=height/2+'px';menu.style.bottom='auto';
      const menuRect=menu.getBoundingClientRect(),sideWidth=menuRect.left-16;
      this.element.style.paddingLeft='0px';
      this.element.classList.toggle('pause-objectives-narrow',sideWidth>=160&&sideWidth<240);
      if(sideWidth>=160){
        this.element.style.width=Math.min(p.width,sideWidth)+'px';
        this.element.style.height=Math.min(p.maxHeight,height-40)+'px';
        this.element.style.top=height/2+'px';this.element.style.transform='translateY(-50%)';this.element.style.setProperty('--objective-slide-y','-50%');
      }else{
        const expanded=document.getElementById('pause').classList.contains('ai-compact')&&document.getElementById('pause').classList.contains('ai-sidebar-expanded');
        this.element.style.width=Math.min(p.width,expanded?width/2-16:width-18)+'px';
        this.element.style.top='12px';this.element.style.height=Math.max(0,menuRect.top-24)+'px';this.element.style.transform='none';this.element.style.setProperty('--objective-slide-y','0px');
      }
    }
  }
  root.Ew4PauseObjectives={Panel};
})(globalThis);
