'use strict';
(function(root){
  class Player {
    constructor(api){
      this.api=api;this.key=null;this.version=0;this.animations=[];this.media=new Map();this.pageCache=new Map();this.measure=document.createElement('canvas').getContext('2d');this.preferences={auto:false,speed:1,font:'medium',typewriter:false,skipOpening:false};
      try{Object.assign(this.preferences,JSON.parse(localStorage.getItem('ew4.story')||'{}'));}catch(_){}
      if(![1,2,4,8].includes(this.preferences.speed))this.preferences.speed=1;
      this.host=document.createElement('section');this.host.id='story-player';this.host.hidden=true;this.host.setAttribute('role','dialog');
      this.host.setAttribute('aria-modal','true');this.host.setAttribute('aria-label','剧情');
      this.background=document.createElement('img');this.background.className='story-background';this.background.alt='';
      this.cast=document.createElement('div');this.cast.className='story-cast';
      const left=document.createElement('nav'),right=document.createElement('nav');
      left.className='story-toolbar story-tools-left';left.setAttribute('aria-label','剧情工具');right.className='story-toolbar story-tools-right';right.setAttribute('aria-label','播放控制');
      this.tool(left,'settings','剧情设置',()=>this.settings());this.tool(left,'history','对话历史',()=>this.history());
      this.tool(left,'eye-off','隐藏界面',()=>{this.host.classList.add('story-text-hidden');this.schedule();});
      this.speed=this.tool(right,null,'播放倍速',()=>{const speeds=[1,2,4,8];this.preferences.speed=speeds[(speeds.indexOf(this.preferences.speed)+1)%4];this.persist();this.controls();this.schedule();});
      this.auto=this.tool(right,'play','自动播放',()=>{if(this.resumePaused){this.resumePaused=false;this.preferences.auto=true;}else this.preferences.auto=!this.preferences.auto;this.persist();this.controls();this.schedule();});
      this.auto.className='story-auto';this.auto.append(document.createTextNode('自动'));
      this.skip=this.tool(right,'skip-forward','跳过剧情',()=>this.command('story_skip'));
      this.dialogue=document.createElement('div');this.dialogue.className='story-dialogue';
      this.speaker=document.createElement('div');this.speaker.className='story-speaker';
      this.text=document.createElement('p');this.text.className='story-text';this.text.setAttribute('aria-live','polite');
      this.next=document.createElement('span');this.next.className='story-continue';this.next.setAttribute('aria-hidden','true');
      this.dialogue.append(this.speaker,this.text,this.next);
      this.choices=document.createElement('div');this.choices.className='story-choices';this.choices.setAttribute('role','group');this.choices.setAttribute('aria-label','对话选项');
      this.choiceList=document.createElement('div');this.choiceList.className='story-choice-list';this.choices.append(this.choiceList);
      this.loading=document.createElement('div');this.loading.className='story-loading';this.loading.textContent='载入中…';this.loading.hidden=true;
      this.host.append(this.background,this.cast,left,right,this.dialogue,this.choices,this.loading);document.body.append(this.host);
      this.host.addEventListener('click',e=>{if(!e.target.closest('button,select,input,.story-overlay'))this.advance();});
      this.host.addEventListener('pointerdown',e=>e.stopPropagation());
      this.host.addEventListener('wheel',e=>e.stopPropagation());
      document.addEventListener('visibilitychange',()=>this.setVisible(!document.hidden));
      document.addEventListener('keydown',e=>{
        if(this.host.hidden)return;
        if(e.key==='Escape'){e.preventDefault();e.stopImmediatePropagation();this.back();}
        else if((e.key===' '||e.key==='Enter')&&!e.target.closest('button,input,select')){e.preventDefault();this.advance();}
      },true);
      addEventListener('resize',()=>{clearTimeout(this.resizeTimer);this.resizeTimer=setTimeout(()=>{if(!this.host.hidden){
        this.finishTyping();this.paginate(this.story.text);this.page=0;this.display();
        for(const actor of this.story.actors){const el=Array.from(this.cast.children).find(c=>c.dataset.actor===actor.id);if(el)this.actorSize(el,actor);}
      }},100);});
      this.controls();
    }
    tool(parent,icon,label,action){
      const b=document.createElement('button');b.type='button';b.title=label;b.setAttribute('aria-label',label);
      if(icon){const img=document.createElement('img');img.src='/story-icons/'+icon+'.svg';img.alt='';b.append(img);}
      b.onclick=e=>{e.stopPropagation();action();};parent.append(b);return b;
    }
    persist(){try{localStorage.setItem('ew4.story',JSON.stringify(this.preferences));}catch(_){} }
    controls(){this.speed.textContent=this.preferences.speed+'×';this.auto.setAttribute('aria-pressed',String(this.preferences.auto&&!this.resumePaused));}
    interactions(){const locked=this.busy||this.commandPending||this.preparing;
      for(const b of this.choiceList.children){const option=this.story?.choices.find(c=>c.id===b.dataset.choice);b.disabled=!!locked||!option?.available;}
      this.skip.disabled=!!locked;}
    setVisible(visible){this.visible=visible;for(const a of this.animations){if(visible&&a.playState==='paused')a.play();else if(!visible&&a.playState==='running')a.pause();}this.schedule();}
    paused(){return document.hidden||this.visible===false||!!this.overlay||this.host.classList.contains('story-text-hidden')||this.preparing||this.loading.hidden===false||this.blocked;}
    preload(source){
      if(this.media.has(source))return this.media.get(source);
      const pending=new Promise(resolve=>{const img=new Image();img.onload=()=>resolve(img);img.onerror=()=>resolve(img);img.src=source;});
      this.media.set(source,pending);if(this.media.size>32)this.media.delete(this.media.keys().next().value);return pending;
    }
    async sync(story,blocked=false,busy=false){
      this.blocked=blocked;this.busy=busy;this.host.hidden=!story||blocked;
      if(!story){this.reset();return;}
      this.story=story;if(blocked){this.schedule();return;}this.choices.hidden=!story.choices.length;
      const key=story.id+'/'+story.node;
      this.interactions();
      if(key===this.key){this.schedule();return;}
      const first=!this.key||this.key.split('/')[0]!==story.id;
      if(story.restored&&first)this.resumePaused=true;
      else if(first)this.resumePaused=false;
      this.controls();
      this.key=key;const version=++this.version;this.preparing=true;this.loading.hidden=!first;this.stop();this.closeOverlay();this.host.classList.remove('story-text-hidden');
      const sources=[story.background,...story.actors.flatMap(a=>[a.image,...a.layers.map(l=>l.image)])].filter(Boolean);
      await Promise.all([Promise.all(sources.map(source=>this.preload(source))),this.api.prepare(story.text+story.name+story.choices.map(c=>c.text).join('')).catch(error=>this.api.notice(error.message))]);
      if(version!==this.version)return;
      this.choiceList.replaceChildren();
      const theme=story.theme;
      this.host.style.setProperty('--story-accent',theme.accent);this.host.style.setProperty('--story-text',theme.text);
      const rgb=theme.panel.slice(1).match(/../g).map(v=>parseInt(v,16));
      this.host.style.setProperty('--story-panel','rgba('+rgb.join(',')+','+theme.opacity+')');
      this.host.style.setProperty('--story-panel-height',theme.dialoguePercent+'%');
      this.font();this.speaker.textContent=story.name;this.speaker.title=story.name;this.dialogue.classList.toggle('narration',!story.name);
      if(story.background){this.background.hidden=false;if(this.background.getAttribute('src')!==story.background)this.background.src=story.background;}else{this.background.removeAttribute('src');this.background.hidden=true;}
      const retained=new Set();
      for(const actor of story.actors){
        let container=Array.from(this.cast.children).find(el=>el.dataset.actor===actor.id);
        if(!container){container=document.createElement('div');const motion=document.createElement('div'),art=document.createElement('div');
          motion.className='story-actor-motion';art.className='story-actor-art';motion.append(art);container.append(motion);this.cast.append(container);}
        retained.add(container);container.className='story-actor'+(!story.speaker||actor.id===story.speaker?' speaking':'');
        container.dataset.actor=actor.id;container.style.left=actor.x*100+'%';container.style.bottom=(1-actor.y)*100+'%';
        this.actorSize(container,actor);container.style.zIndex=actor.order;
        const art=container.firstElementChild.firstElementChild;
        art.style.transform=actor.mirror?'scaleX(-1)':'';
        const layers=(actor.image?[{image:actor.image}]:[]).concat(actor.layers);
        const signature=JSON.stringify(layers);
        if(art.dataset.images===signature)continue;
        art.dataset.images=signature;art.replaceChildren();
        for(const layer of layers){const img=document.createElement('img');img.alt='';img.src=layer.image;img.onerror=()=>{img.hidden=true;};
          if(layer.id){img.className='story-layer';Object.assign(img.style,{left:layer.x/actor.width*100+'%',top:layer.y/actor.height*100+'%',width:layer.width/actor.width*100+'%',height:layer.height/actor.height*100+'%'});}
          art.append(img);}
      }
      for(const container of Array.from(this.cast.children))if(!retained.has(container))container.remove();
      for(const option of story.choices){const b=document.createElement('button');b.type='button';b.className='story-choice';b.textContent=option.text;
        b.dataset.choice=option.id;
        b.onclick=e=>{e.stopPropagation();this.command('story_choice',option.id);};this.choiceList.append(b);}
      this.preparing=false;this.loading.hidden=true;this.interactions();
      this.paginate(story.text);this.page=0;this.display();
      const waits=[];
      for(const animation of story.animations){const c=Array.from(this.cast.children).find(el=>el.dataset.actor===animation.actor);if(!c)continue;
        const el=c.firstElementChild,duration=Math.max(1,animation.durationMs/this.preferences.speed);
        const frames=animation.type==='hop'?[{transform:'translateY(0)'},{transform:'translateY('+(-animation.amplitude)+'px)'},{transform:'translateY(0)'}]
          :animation.type==='shake'?[{transform:'translateX(0)'},{transform:'translateX('+animation.amplitude+'px)'},{transform:'translateX('+(-animation.amplitude)+'px)'},{transform:'translateX(0)'}]
          :animation.type==='move'?[{transform:'translate('+animation.x+'px,'+animation.y+'px)'},{transform:'translate(0,0)'}]
          :[{opacity:0},{opacity:1}];
        if(animation.type==='move')for(const f of frames)f.transform='translateX(-50%) '+f.transform;
        const a=(animation.type==='move'?c:el).animate(frames,{duration,easing:'ease-in-out'});this.animations.push(a);if(animation.wait)waits.push(a.finished.catch(()=>{}));}
      this.waiting=waits.length>0;
      await Promise.all(waits);if(version!==this.version)return;this.waiting=false;
      this.schedule();
    }
    actorSize(element,actor){const width=Math.min(innerHeight*.92*actor.scale*actor.width/actor.height,innerWidth*.96/Math.min(this.story.actors.length,3));
      element.style.width=width+'px';element.style.height=(width*actor.height/actor.width)+'px';}
    font(){const sizes={small:18,medium:this.story?.theme.fontSize||24,large:30};const mobile=innerHeight<=540||innerWidth<=600;
      this.host.style.setProperty('--story-font',(mobile?{small:16,medium:18,large:22}[this.preferences.font]||18:sizes[this.preferences.font]||24)+'px');}
    paginate(value){
      this.font();
      const layoutKey=JSON.stringify([innerWidth,innerHeight,this.preferences.font,this.story.theme]);
      if(this.layoutKey!==layoutKey){
        const style=getComputedStyle(this.text),rect=this.text.getBoundingClientRect();
        this.metrics={width:Math.max(1,rect.width-2),lines:Math.max(1,Math.floor(rect.height/parseFloat(style.lineHeight))),font:style.fontSize+' '+style.fontFamily};
        this.layoutKey=layoutKey;this.pageCache.clear();
      }
      const cached=this.pageCache.get(value);if(cached){this.pages=cached;return;}
      // Measure glyphs offscreen; the dialogue DOM keeps its fixed geometry throughout playback.
      this.measure.font=this.metrics.font;this.pages=[];let page='',line='',lines=1;
      for(const char of Array.from(value||'')){
        const newline=char==='\n',wrap=!newline&&line&&this.measure.measureText(line+char).width>this.metrics.width;
        if(newline||wrap){
          if(lines===this.metrics.lines){this.pages.push(page);page='';lines=1;}else{if(wrap)page+='\n';lines++;}
          line='';
          if(newline){if(lines>1)page+=char;continue;}
        }
        page+=char;line+=char;
      }
      if(page||!this.pages.length)this.pages.push(page);
      this.pageCache.set(value,this.pages);if(this.pageCache.size>128)this.pageCache.delete(this.pageCache.keys().next().value);
    }
    display(){this.finishTyping();this.currentText=this.pages[this.page]||'';this.text.textContent=this.currentText;
      if(this.preferences.typewriter&&!this.resumePaused&&this.currentText){const chars=Array.from(this.currentText);let count=0;this.typing=true;this.text.textContent='';
        this.typeTimer=setInterval(()=>{if(this.paused())return;count+=this.preferences.speed;this.text.textContent=chars.slice(0,count).join('');if(count>=chars.length){this.finishTyping();this.schedule();}},30);}
      this.choices.hidden=this.story.choices.length===0||this.page<this.pages.length-1;this.schedule();}
    finishTyping(){clearInterval(this.typeTimer);this.typing=false;if(this.currentText!=null)this.text.textContent=this.currentText;}
    schedule(){clearTimeout(this.timer);if(!this.story||!this.pages||this.host.hidden||this.paused()||this.busy||this.commandPending||this.waiting)return;
      if(this.story.kind==='opening'&&this.preferences.skipOpening&&!this.resumePaused&&!this.story.choices.length){this.timer=setTimeout(()=>this.command('story_skip'),0);return;}
      if(this.typing)return;
      if(this.story.choices.length&&this.page===this.pages.length-1)return;
      const empty=!this.story.text&&!this.story.choices.length;
      if((this.preferences.auto||empty)&&!this.resumePaused)this.timer=setTimeout(()=>this.advance(),Math.max(empty?50:500,(this.story.waitMs||Math.max(800,800+Array.from(this.currentText||'').length*60))/this.preferences.speed));}
    advance(){if(this.host.classList.contains('story-text-hidden')){this.host.classList.remove('story-text-hidden');this.schedule();return;}
      if(this.paused()||this.busy||this.commandPending)return;if(this.typing){this.finishTyping();this.schedule();return;}
      if(this.waiting){for(const a of this.animations)a.finish();return;}
      if(this.page<this.pages.length-1){this.page++;this.display();return;}if(!this.story.choices.length)this.command('story_next');}
    async command(op,target){if(this.busy||this.commandPending||this.paused()||!this.story)return;this.commandPending=true;this.resumePaused=false;clearTimeout(this.timer);this.interactions();
      for(const b of this.choiceList.children)b.classList.toggle('chosen',op==='story_choice'&&b.dataset.choice===target);
      try{await this.api.act(op,{offer:this.story.id,tag:this.story.node,target});}catch(error){this.api.notice(error.message);}
      finally{this.commandPending=false;for(const b of this.choiceList.children)b.classList.remove('chosen');this.interactions();this.schedule();}}
    sheet(title,history=false){this.finishTyping();this.closeOverlay();this.overlay=document.createElement('div');this.overlay.className='story-overlay';
      const sheet=document.createElement('section');sheet.className='story-sheet'+(history?' story-history':'');const header=document.createElement('header');
      const h=document.createElement('h2');h.textContent=title;h.style.fontSize='22px';header.append(h);this.tool(header,'x','关闭',()=>this.closeOverlay()).className='story-close';
      sheet.append(header);this.overlay.append(sheet);this.host.append(this.overlay);this.schedule();return sheet;}
    closeOverlay(){if(this.overlay)this.overlay.remove();this.overlay=null;this.schedule();}
    settings(){const sheet=this.sheet('剧情设置');
      const label=document.createElement('label');label.textContent='字号';const select=document.createElement('select');select.setAttribute('aria-label','剧情字号');
      for(const [value,text] of [['small','小'],['medium','中'],['large','大']]){const option=document.createElement('option');option.value=value;option.textContent=text;select.append(option);}
      select.value=this.preferences.font;select.onchange=()=>{this.preferences.font=select.value;this.persist();this.paginate(this.story.text);this.page=0;this.display();};label.append(select);sheet.append(label);
      for(const [key,text] of [['typewriter','逐字显示'],['skipOpening','开局跳过剧情']]){const row=document.createElement('label');row.textContent=text;
        const input=document.createElement('input');input.type='checkbox';input.checked=this.preferences[key];input.onchange=()=>{this.preferences[key]=input.checked;this.persist();};row.append(input);sheet.append(row);}}
    history(){const sheet=this.sheet('对话历史',true),list=document.createElement('div');list.className='story-history-list';sheet.append(list);
      for(const entry of this.story.history){const row=document.createElement('div');row.className='story-history-row';const name=document.createElement('div'),text=document.createElement('div');
        name.className='story-history-name';name.textContent=entry.name;text.className='story-history-prose';text.textContent=entry.text;row.append(name,text);
        if(entry.choice){const choice=document.createElement('div');choice.className='story-history-choice';choice.textContent=entry.choice;row.append(choice);}list.append(row);}
      sheet.scrollTop=sheet.scrollHeight;}
    back(){if(this.overlay)this.closeOverlay();else if(this.host.classList.contains('story-text-hidden')){this.host.classList.remove('story-text-hidden');this.schedule();}else this.settings();}
    stop(){clearTimeout(this.timer);this.finishTyping();for(const a of this.animations)a.cancel();this.animations=[];this.waiting=false;}
    reset(){this.version++;this.stop();this.story=null;this.preparing=false;this.closeOverlay();this.key=null;this.media.clear();this.cast.replaceChildren();this.background.removeAttribute('src');}
  }
  root.Ew4StoryUI={Player};
})(globalThis);
