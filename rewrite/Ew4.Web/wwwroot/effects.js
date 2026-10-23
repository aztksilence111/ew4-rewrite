'use strict';
(function(root){
  function track(points,value,key,axis='life',fallback=1){
    if(!points?.length)return fallback;
    let a=points[0],b=a;
    for(const p of points){if(Number(p[axis])<=value)a=p;else{b=p;break;}b=p;}
    const from=Number(a[key]??fallback),to=Number(b[key]??from),span=Number(b[axis])-Number(a[axis]);
    return span>0?from+(to-from)*Math.max(0,Math.min(1,(value-Number(a[axis]))/span)):from;
  }
  // Original 82084: weapon enum, defending category, damage band and sea tile.
  function impact(weapon,category,damage,sea,facing){
    const band=damage>40?3:damage>25?2:damage>10?1:0,level=(band>>1)+1,side=facing<0?' left':' right';
    if(weapon==='gun'||weapon==='mgun')return (category==='fort'?'stonestrike':category==='naval'?'woodstrike':'bodystrike')+level+side;
    if(weapon==='cold')return (category==='fort'||category==='naval'?'coldstonestrike':'coldstrike')+level;
    if(weapon==='guns'&&category!=='fort'&&category!=='naval')return 'bodystrike'+level+side;
    if(weapon==='guns')return 'strike'+(band+1);
    return (sea?'seastrike':'strike')+(band+1);
  }
  function inView(rect,x,y,radius){return !rect||x+radius>=rect.left&&x-radius<=rect.left+rect.width&&y+radius>=rect.top&&y-radius<=rect.top+rect.height;}
  function own(object,key){return object&&Object.prototype.hasOwnProperty.call(object,key)?object[key]:undefined;}
  class ParticleEmitter {
    constructor(definition,event,seed,options={}){
      this.parameters=definition.parameters;this.event=event;this.seed=seed>>>0;this.time=0;this.credit=0;this.particles=[];
      this.until=options.continuous?Infinity:Math.max(0,Math.min(Number(definition.life),options.emissionLife??Infinity));
      this.enabled=true;this.emitted=0;this.peak=0;this.previous={x:0,y:0,time:0};this.position={...this.previous};
      this.maximumLife=Math.max(Number(this.parameters.life?.min||0),Number(this.parameters.life?.max||0),1/60);
    }
    random(){this.seed=(Math.imul(this.seed,1664525)+1013904223)>>>0;return this.seed/4294967296;}
    value(key,fallback=0){const p=this.parameters[key];return p?Number(p.min??fallback)+(Number(p.max??p.min??fallback)-Number(p.min??fallback))*this.random():fallback;}
    move(x,y,time){this.previous=this.position;this.position={x,y,time};}
    stop(time=this.time){this.until=Math.min(this.until,Math.max(0,time));}
    get alive(){return this.time<this.until||this.particles.length>0;}
    spawn(at){
      const p=this.parameters,angle=Number(this.event.rot||0)*Math.PI/180,direction=this.value('angle')*Math.PI/180+angle;
      const life=Math.max(1/60,this.value('life',.5)),speed=this.value('speed'),area=p.settings?.type==='area';
      const dx=area?(this.random()-.5)*Number(p.settings?.width||0):0,dy=area?(this.random()-.5)*Number(p.settings?.height||0):0;
      const span=this.position.time-this.previous.time,f=span>0?Math.max(0,Math.min(1,(at-this.previous.time)/span)):1;
      const x=this.previous.x+(this.position.x-this.previous.x)*f,y=this.previous.y+(this.position.y-this.previous.y)*f;
      this.particles.push({image:p.image,tracks:p.lifetrack,born:at,age:0,life,
        x:x+Number(this.event.x||0)+dx*Math.cos(angle)-dy*Math.sin(angle),y:y+Number(this.event.y||0)+dx*Math.sin(angle)+dy*Math.cos(angle),
        vx:Math.cos(direction)*speed,vy:Math.sin(direction)*speed,gravity:this.value('gravity'),scale:this.value('scale',1),rotation:this.value('rotangle')*Math.PI/180+angle,spin:this.value('rotspeed'),
        color:[this.value('r',255),this.value('g',255),this.value('b',255),this.value('a',255)]});
      this.emitted++;this.peak=Math.max(this.peak,this.particles.length);
    }
    advance(target){
      const step=1/60,end=Math.floor((Math.max(0,target)+1e-9)/step)*step;
      if(end<=this.time)return;
      if(end>=this.until+this.maximumLife){this.particles.length=0;this.time=end;return;}
      if(end-this.time>this.maximumLife+1){this.particles.length=0;this.credit=0;this.time=Math.max(this.time,Math.floor((end-this.maximumLife)/step)*step);}
      // Invisible continuous trails keep their clock, without replaying discarded births.
      if(!this.enabled&&!this.particles.length){this.credit=0;this.time=end;return;}
      while(this.time+step<=end+1e-9){
        for(let i=this.particles.length-1;i>=0;i--){
          const p=this.particles[i],life=p.age/p.life;
          p.x+=p.vx*track(p.tracks,life,'speed')*step;p.y+=p.vy*track(p.tracks,life,'speed')*step;
          p.vy+=p.gravity*track(p.tracks,life,'gravity')*step;p.rotation+=p.spin*track(p.tracks,life,'rotspeed')*step;p.age+=step;
          if(p.age>=p.life){this.particles[i]=this.particles[this.particles.length-1];this.particles.pop();}
        }
        const emissionStep=Math.max(0,Math.min(step,this.until-this.time));
        if(this.enabled&&emissionStep>0){
          this.credit+=Math.max(0,track(this.parameters.timetrack,this.time,'quantity','time',0))*emissionStep;
          const count=Math.floor(this.credit+1e-9);this.credit=Math.max(0,this.credit-count);
          for(let n=0;n<Math.min(count,100-this.particles.length);n++)this.spawn(this.time+emissionStep);
        }
        this.time+=step;
      }
    }
  }
  class UnitVisuals {
    constructor(manifest,data,config){
      if(config&&config.schema!==1)throw new Error('Unsupported unit-visuals schema');
      this.manifest=manifest;this.data=data;this.config=config||{};this.cache=new Map();
    }
    definition(key){return own(this.config.definitions,key)||own(this.data.motions,key);}
    resolve(id,faction){
      const base=this.manifest.profiles[id];if(!base)return base;
      const variant=own(this.config.factions,faction?.id)??faction?.visualVariant??faction?.unitVariant;
      const cacheKey=JSON.stringify([id,variant]);if(this.cache.has(cacheKey))return this.cache.get(cacheKey);
      const slot=base.kind.toLowerCase().replace(/ /g,'_')+'.'+base.grade;
      let key=own(this.config.profiles,id)??own(own(this.config.variants,variant),slot);
      if(key==null&&variant!=null){
        key=[`${base.kind} ${variant} ${base.grade+1}`,this.config.defaults?.[slot],`${base.kind} ${base.grade+1}`,base.kind].find(k=>k&&this.definition(k));
      }
      if(key==null)key=this.data.bindings[id];
      if(!key)return base;
      const source=this.definition(key);if(!source)throw new Error('Unknown unit visual: '+key);
      const motions={};for(const m of source.motions)if(!m.dir||['all','1','right'].includes(m.dir))motions[m.type]=m.name;
      const profile={...base,library:source.res,x:Number(source.x||0),y:Number(source.y||0),scale:Number(source.scale??1),motions,visualMotion:key};
      this.cache.set(cacheKey,profile);return profile;
    }
  }
  class DamageNumbers {
    constructor(font,sheet){this.font=font;this.sheet=sheet;this.instances=[];this.cache=new Map();}
    clear(){this.instances=[];}
    add(amount,critical,x,y,speed=1){
      if(!Number.isFinite(amount)||amount<=0)return;
      const key=amount+':'+!!critical;
      if(!this.cache.has(key)){
        const glyphs=[...('-'+amount)].map(c=>this.font.glyphs[c.codePointAt(0)]);
        let cursor=0,width=0;
        for(const [,,w,,dx,,advance] of glyphs){width=Math.max(width,cursor-dx+w);cursor+=advance;}
        const canvas=document.createElement('canvas');canvas.width=Math.ceil(width);canvas.height=this.font.height;const draw=canvas.getContext('2d');
        cursor=0;
        for(const [x,y,w,h,dx,dy,advance] of glyphs){draw.drawImage(this.sheet,x,y,w,h,cursor-dx,-dy,w,h);cursor+=advance;}
        if(critical){
          const pixels=draw.getImageData(0,0,canvas.width,canvas.height);
          for(let i=0;i<pixels.data.length;i+=4){pixels.data[i+1]*=48/255;pixels.data[i+2]*=48/255;}
          draw.putImageData(pixels,0,0);
        }
        this.cache.set(key,canvas);if(this.cache.size>128)this.cache.delete(this.cache.keys().next().value);
      }
      this.instances.push({amount,critical:!!critical,x,y,speed,started:performance.now(),duration:1600/speed,canvas:this.cache.get(key)});
    }
    frame(draw,time,rect){
      this.instances=this.instances.filter(hit=>{
        const progress=Math.max(0,(time-hit.started)/hit.duration);if(progress>=1)return false;
        const y=hit.y-30-64*progress;if(!inView(rect,hit.x,y,96))return true;
        const scale=24/this.font.height,width=hit.canvas.width*scale,height=hit.canvas.height*scale;
        draw.drawImage(hit.canvas,hit.x-width/2,y,width,height);return true;
      });
    }
  }
  class Effects {
    constructor(api,data,visuals){this.api=api;this.data=data;this.visuals=new UnitVisuals(api.manifest||{profiles:{}},data,visuals);this.instances=[];this.tints=new Map();this.voices=new Set();this.audio=new Map();this.trails=new Map();this.serial=1;}
    profile(id,faction){return this.visuals.resolve(id,faction);}
    motion(profile,action,facing){const source=this.visuals.definition(typeof profile==='string'?this.data.bindings[profile]:profile?.visualMotion),side=facing<0?'left':'right';
      return source?.motions.find(m=>m.type===action&&Number(m.index||0)===0&&(m.dir===side||(!m.dir||m.dir==='all')));}
    firstEvent(name){const events=this.data.timelines[name];return events?.length?Math.min(...events.map(e=>Number(e.at)||0)):null;}
    lastEvent(name){const events=this.data.timelines[name];return events?.length?Math.max(...events.map(e=>Number(e.at)||0)):null;}
    attackPlan(profile,lib,category,facing,speed){
      const firing=this.motion(profile,'attack',facing),timeline=(firing?.effect||'')+(facing<0?' left':' right'),last=this.lastEvent(timeline),segments=[];
      let visualDuration=0,duration=0,attackBasis=0;
      for(const action of ['attack','reload','finish']){
        const motion=this.motion(profile,action,facing),name=motion?.name||profile.motions[action],item=lib.byName.get(name);if(!item)continue;
        const rate=speed*Number(motion?.speed||1),length=item.duration/lib.fps*1000/rate;
        const basis=action==='attack'&&last!=null?Math.min(length,(last+.2+(category==='artillery'?.2:0))*1000/speed):length;
        if(!segments.length)attackBasis=basis;
        segments.push({name,start:visualDuration,end:visualDuration+length,speed:rate,directional:motion?.dir==='left'||motion?.dir==='right'});
        visualDuration+=length;duration+=basis;
      }
      return {segments,timeline,visualDuration,duration,attackBasis,hitAt:last==null?attackBasis:Math.max(attackBasis,(last+.2)*1000/speed),firstShot:(this.firstEvent(timeline)??0)*1000/speed};
    }
    clear(){this.instances=[];this.trails.clear();for(const voice of this.voices){voice.pause();try{voice.currentTime=0;}catch(_){}}this.voices.clear();}
    preloadSounds(files){for(const file of files||[])if(this.data.sounds.includes(file)&&!this.audio.has(file)){const audio=new Audio('/content/sounds/'+file);audio.preload='auto';this.audio.set(file,audio);}}
    sound(file){if(!file||!this.data.sounds.includes(file)||!this.api.volume())return;
      let pool=this.audio.get(file);if(!pool){pool=new Audio('/content/sounds/'+file);pool.preload='auto';this.audio.set(file,pool);}
      const voice=this.voices.has(pool)?pool.cloneNode():pool;voice.volume=this.api.volume()/100;this.voices.add(voice);
      voice.onended=voice.onerror=()=>this.voices.delete(voice);voice.play().catch(()=>this.voices.delete(voice));}
    moveSound(profile,category,sea){this.sound(sea?'sfx_naval.wav':category==='cavalry'?(profile?.unitIndex===9?'sfx_armourmove.wav':'sfx_cavalrymove.wav'):'sfx_leg.wav');}
    timeline(name,x,y,options={}){
      const events=this.data.timelines[name];if(!events)return 0;
      const instance={x,y,events,started:options.started??performance.now(),speed:options.speed||1,scale:options.scale||1,done:new Set(),emitters:[],get particles(){return this.emitters.flatMap(e=>e.particles);},seed:this.serial++};
      this.instances.push(instance);return this.lastEvent(name)*1000/instance.speed;
    }
    effect(name,x,y,options={}){const instance={x,y,events:[{type:'effect',name,at:0}],started:options.started??performance.now(),speed:options.speed||1,scale:options.scale||1,layer:options.layer,continuous:options.continuous,emissionLife:options.emissionLife,done:new Set(),emitters:[],get particles(){return this.emitters.flatMap(e=>e.particles);},seed:this.serial++};this.instances.push(instance);return instance;}
    trail(id,x,y,grade,sea,time,visible=true,speed=1){
      const kind=sea?4:grade===0?1:grade===1?2:3;let trail=this.trails.get(id);
      if(trail&&trail.kind!==kind){this.stopTrail(trail,time);this.trails.delete(id);trail=null;}
      if(!trail){const instance=this.effect('effect_moving'+kind+'.xml',x,y,{continuous:true,scale:.5,layer:'ground',started:time,speed});trail={kind,instance};this.trails.set(id,trail);}
      const instance=trail.instance,elapsed=(time-instance.started)/1000*instance.speed;
      instance.enabled=visible;instance.trailPosition={x:(x-instance.x)/instance.scale,y:(y-instance.y)/instance.scale,time:elapsed};
    }
    stopTrail(trail,time){const i=trail.instance,elapsed=(time-i.started)/1000*i.speed;i.continuous=false;i.emissionLife=elapsed;for(const e of i.emitters)e.stop(elapsed);}
    finishTrails(active,time,discard=false){
      const removed=new Set();
      for(const [id,trail] of this.trails)if(!active.has(id)){this.stopTrail(trail,time);this.trails.delete(id);if(discard)removed.add(trail.instance);}
      if(removed.size)this.instances=this.instances.filter(instance=>!removed.has(instance));
    }
    get particles(){return this.instances.flatMap(i=>i.emitters.flatMap(e=>e.particles));}
    element(name,x,y,time,draw){
      for(const element of this.data.elements?.[name]||[]){const lib=this.api.libraries.get(element.res),item=lib?.byName.get(element.name);if(!item)continue;
        draw.save();draw.translate(x+Number(element.x||0),y+Number(element.y||0));draw.scale(Number(element.hscale||1),Number(element.vscale||1));this.api.animation(lib,item,time/1000*lib.fps,0,draw);draw.restore();}
    }
    emit(instance,event){
      this.sound(event.sound);
      if(event.type==='animation'||event.type==='image')return;
      const definitions=this.data.definitions[event.name];if(!definitions)return;
      for(const definition of definitions)instance.emitters.push(new ParticleEmitter(definition,event,Math.imul(instance.seed,65537)+instance.emitters.length,instance));
    }
    texture(name,color){
      const atlas=this.api.manifest.atlases.eff,sprite=atlas?.[name],image=this.api.image('images/eff.png');
      if(!sprite||!image.complete||!image.naturalWidth)return null;
      const rgb=color.slice(0,3).map(v=>Math.round(v/16)*16),key=name+rgb.join(',');
      if(!this.tints.has(key)){
        if(this.tints.size>256)this.tints.delete(this.tints.keys().next().value);
        const canvas=document.createElement('canvas');canvas.width=sprite.w;canvas.height=sprite.h;const d=canvas.getContext('2d');
        d.drawImage(image,sprite.x,sprite.y,sprite.w,sprite.h,0,0,sprite.w,sprite.h);d.globalCompositeOperation='multiply';d.fillStyle='rgb('+rgb.join(',')+')';d.fillRect(0,0,canvas.width,canvas.height);d.globalCompositeOperation='destination-in';d.drawImage(image,sprite.x,sprite.y,sprite.w,sprite.h,0,0,sprite.w,sprite.h);
        this.tints.set(key,canvas);
      }
      return this.tints.get(key);
    }
    particleAtlas(){
      const image=this.api.image('images/eff.png');if(!image.complete||!image.naturalWidth)return null;
      if(this.gpuAtlas?.source!==image){
        const canvas=document.createElement('canvas');canvas.width=image.naturalWidth;canvas.height=image.naturalHeight;
        const draw=canvas.getContext('2d');draw.fillStyle='#ffffff';draw.fillRect(0,0,canvas.width,canvas.height);draw.drawImage(image,0,0);
        // Preserve the existing multiply -> destination-in edge colors; tinting the
        // original translucent atlas directly would darken its antialiased edges.
        draw.globalCompositeOperation='destination-in';draw.drawImage(image,0,0);
        this.gpuAtlas={source:image,canvas};
      }
      return this.gpuAtlas.canvas;
    }
    frame(draw,time,layer='foreground',visibleRect=null){
      this.instances=this.instances.filter(instance=>{
        if((instance.layer||'foreground')!==layer)return true;
        const elapsed=(time-instance.started)/1000*instance.speed;
        if(elapsed<0)return true;
        instance.events.forEach((event,index)=>{if(elapsed>=Number(event.at||0)&&!instance.done.has(index)){instance.done.add(index);this.emit(instance,event);}});
        for(const emitter of instance.emitters){
          emitter.enabled=instance.enabled!==false;
          if(instance.trailPosition)emitter.move(instance.trailPosition.x,instance.trailPosition.y,instance.trailPosition.time);
          emitter.advance(elapsed-Number(emitter.event.at||0));
        }
        let visualAlive=false;
        for(const event of instance.events){
          const age=elapsed-Number(event.at||0);if(age<0)continue;
          if(event.type==='animation'){
            const lib=this.api.libraries.get(event.res),item=lib?.byName.get(event.name);if(!item||age*lib.fps>=item.duration)continue;
            visualAlive=true;
            const x=instance.x+Number(event.x||0)*instance.scale,y=instance.y+Number(event.y||0)*instance.scale;
            const bounds=Ew4Battlefield.animationBounds(lib,item,age*lib.fps),ratio=lib.pixelRatio||1;
            const radius=bounds?Math.hypot(Math.max(Math.abs(bounds.left),Math.abs(bounds.right))*Number(event.hscale||1),Math.max(Math.abs(bounds.top),Math.abs(bounds.bottom))*Number(event.vscale||1))*instance.scale/ratio:Infinity;
            if(!inView(visibleRect,x,y,radius))continue;
            draw.save();draw.translate(x,y);draw.scale(Number(event.hscale||1)*instance.scale,Number(event.vscale||1)*instance.scale);this.api.animation(lib,item,age*lib.fps,0,draw);draw.restore();
          }else if(event.type==='image'&&age<Number(event.life||0)){
            visualAlive=true;const x=instance.x+Number(event.x||0)*instance.scale,y=instance.y+Number(event.y||0)*instance.scale,sprite=this.api.manifest.atlases.eff?.[event.name];
            const radius=sprite?Math.hypot(sprite.w,sprite.h)*Math.abs(Number(event.scale||1)*instance.scale):Infinity;
            if(!inView(visibleRect,x,y,radius))continue;
            draw.save();draw.translate(x,y);draw.scale(Number(event.scale||1)*instance.scale,Number(event.scale||1)*instance.scale);this.api.sprite('eff',event.name,0,0,1,draw);draw.restore();
          }
        }
        for(const emitter of instance.emitters)for(const p of emitter.particles){const life=p.age/p.life,scale=p.scale*track(p.tracks,life,'scale'),s=instance.scale;
          const alpha=Math.max(0,Math.min(1,p.color[3]*track(p.tracks,life,'a')/255));if(alpha<=1/255)continue;
          const width=Number(p.image.width)*scale*s,height=Number(p.image.height)*scale*s;
          const x=instance.x+p.x*s,y=instance.y+p.y*s,sprite=this.api.manifest.atlases.eff?.[p.image.file];
          const anchorX=sprite?Number(sprite.refx||0)/sprite.w:.5,anchorY=sprite?Number(sprite.refy||0)/sprite.h:.5;
          const radius=Math.hypot(width,height)*Math.max(anchorX,1-anchorX,anchorY,1-anchorY);
          if(!inView(visibleRect,x,y,radius))continue;
          const color=p.color.map((v,i)=>v*track(p.tracks,life,['r','g','b','a'][i])),texture=draw.gpu?this.particleAtlas():this.texture(p.image?.file,color);if(!texture||!sprite)continue;
          draw.save();draw.translate(x,y);draw.rotate(p.rotation);draw.globalAlpha=alpha;draw.globalCompositeOperation=p.image.blend==='add'?'lighter':'source-over';
          if(draw.gpu){const rgb=color.slice(0,3).map(v=>Math.max(0,Math.min(255,Math.round(v/16)*16)));draw.tint=(rgb[0]<<16)|(rgb[1]<<8)|rgb[2];draw.drawImage(texture,sprite.x,sprite.y,sprite.w,sprite.h,-width*anchorX,-height*anchorY,width,height);}
          else draw.drawImage(texture,-width*anchorX,-height*anchorY,width,height);
          draw.restore();
        }
        return visualAlive||instance.done.size<instance.events.length||instance.emitters.some(e=>e.alive);
      });
    }
  }
  root.Ew4Effects={Effects,ParticleEmitter,UnitVisuals,DamageNumbers,impact,track};
})(globalThis);
