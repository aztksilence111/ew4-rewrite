'use strict';
(function(root){
  // Semantic targets retain the existing elements, accessibility, and event handlers.
  const screens={
    main:['#main-menu',{entries:'nav',campaign:'[data-mode="1"]',conquest:'[data-mode="2"]',tutorial:'[data-mode="4"]',headquarters:'#headquarters',settings:'#main-settings',title:'.game-title'}],
    scenario:['#scenario-menu',{list:'#scenarios',pagination:'#conquest-pagination',load:'#continue',title:'#mode-title'}],
    country:['#countries',{confirm:'#confirm-country'}],campaign:['#campaign-screen',{confirm:'#campaign-confirm',intro:'#campaign-intro'}],
    battle:['#play',{pause:'#menu-button',endTurn:'#end-turn',undo:'#undo-move',nextUnit:'#next-unit',skipAi:'#skip-ai',aiCountry:'#ai-action',resources:'.battle-resources',capacity:'#army-capacity',mapMode:'#map-view-controls'}],
    pause:['#pause .pause-dialog',{save:'#save',settings:'#pause-settings',restart:'#restart',exit:'#return-menu',turn:'#turn'}],
    settings:['#settings .settings-dialog',{}]
  };
  class Skin {
    constructor(manifest){this.manifest=manifest;this.saved=new Map();this.decorations=[];}
    remember(el){if(!this.saved.has(el))this.saved.set(el,{style:el.getAttribute('style'),skin:el.classList.contains('ui-skin-art')});}
    style(el,spec,battle=false){
      if(!el)return;this.remember(el);
      const length=n=>battle?`calc(${n}px * var(--battle-hud-scale))`:n+'px';
      const geometry=['x','y','right','bottom','w','h'].some(k=>k in spec);
      if(geometry){el.style.position='absolute';el.style.margin='0';}
      for(const [key,css,opposite] of [['x','left','right'],['y','top','bottom'],['right','right','left'],['bottom','bottom','top'],['w','width'],['h','height']]){
        if(!(key in spec))continue;
        el.style.setProperty(css,length(spec[key]),'important');if(opposite)el.style.setProperty(opposite,'auto','important');
      }
      if(battle&&geometry){el.style.transform='none';el.style.setProperty('transform','none','important');}
      for(const [key,css] of [['color','color'],['background','background-color']])if(spec[key])el.style.setProperty(css,spec[key],'important');
      if(spec.fontSize)el.style.fontSize=length(spec.fontSize);
      if(spec.image||spec.pressedImage){
        const image=spec.image?Ew4NativeUI.resource(this.manifest,spec.image):{file:null};
        if(image){
          if(el.tagName==='IMG'&&image.file){this.saved.get(el).src??=el.getAttribute('src');el.src=image.file;}
          else{
            const normal=image.file?`url("${image.file}")`:getComputedStyle(el).backgroundImage;
            el.classList.add('ui-skin-art');el.style.setProperty('--skin-image',normal);
            const pressed=Ew4NativeUI.resource(this.manifest,spec.pressedImage);
            el.style.setProperty('--skin-pressed-image',pressed?`url("${pressed.file}")`:normal);
          }
        }
      }
    }
    apply(){
      for(const config of [this.manifest.legacyUiPresentation,this.manifest.uiPresentation]){
      if(!config)continue;
      for(const [name,spec] of Object.entries(config.screens||{})){
        const [selector,targets]=screens[name]||[],panel=selector&&document.querySelector(selector);if(!panel)continue;
        if(spec.panel)this.style(panel,spec.panel);
        for(const [id,style] of Object.entries(spec.controls||{}))if(targets[id])this.style(panel.querySelector(targets[id]),style,name==='battle');
        for(const style of spec.decorations||[]){
          const decoration=document.createElement('div');decoration.className='ui-skin-decoration';decoration.setAttribute('aria-hidden','true');panel.append(decoration);
          this.style(decoration,style,name==='battle');this.decorations.push(decoration);
        }
      }
      }
    }
    dispose(){
      for(const [el,saved] of this.saved){if(saved.style===null)el.removeAttribute('style');else el.setAttribute('style',saved.style);if(!saved.skin)el.classList.remove('ui-skin-art');if(saved.src!==undefined)el.setAttribute('src',saved.src);}
      this.decorations.forEach(el=>el.remove());this.saved.clear();this.decorations=[];
    }
  }
  root.Ew4UiPresentation={Skin,screens};
})(globalThis);
