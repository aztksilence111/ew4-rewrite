'use strict';
(function(root){
  const modes=[{id:'normal',name:'普通地图'},{id:'camp',name:'阵营地图'},{id:'district',name:'辖区地图'}];
  const palette=['#79aadd60','#d67b7b60','#dcb36a60','#85b38a60','#b08fbd60','#6db6b060','#c9937260','#a7a9ba60'];
  const packed=hex=>{const n=parseInt(hex.slice(1,7),16),alpha=hex.length===9?parseInt(hex.slice(7,9),16):255;return (alpha<<24|(n&255)<<16|(n&0xff00)|(n>>>16))>>>0;};
  class Projection {
    constructor(){this.camps=new Map();this.used=new Set();}
    sync(state,map){
      const reset=this.map!==map||this.scenario!==state.scenario;
      if(reset){this.camps.clear();this.used.clear();this.map=map;this.scenario=state.scenario;}
      const config=state.mapColors||{},signature=JSON.stringify(config),changed=this.colorSignature!==signature;
      this.colorSignature=signature;
      const factionChanged=reset||!this.factions||this.factions.size!==state.factions.length||state.factions.some(f=>{
        const prior=this.factions.get(f.id);
        return !prior||['camp','color','country','flag'].some(key=>prior[key]!==f[key]);
      });
      if(!factionChanged&&!changed)return false;
      const colors=config.campPalette||palette;
      const overrides=new Map(Object.entries(config.camps||{}));
      if(factionChanged)this.factions=new Map(state.factions.map(f=>[f.id,{camp:f.camp,color:f.color,country:f.country,flag:f.flag}]));
      const ordered=[...state.factions].sort((a,b)=>String(a.id).localeCompare(String(b.id),'en',{numeric:true}));
      for(const faction of ordered){
        if(this.camps.has(faction.camp))continue;
        const seed=String(faction.country||faction.flag||faction.id).toUpperCase().replace(/[^A-Z]/g,'')||'C';
        let abbreviation=seed[0],length=1,suffix=0;
        while(this.used.has(abbreviation))abbreviation=length<seed.length?seed.slice(0,++length):seed+this.letters(suffix++);
        this.used.add(abbreviation);
        this.camps.set(faction.camp,{abbreviation,color:0});
      }
      let index=0;
      for(const [id,camp] of this.camps){camp.color=packed(overrides.get(id)??colors[index%colors.length]);index++;}
      return changed||factionChanged;
    }
    letters(index){let value='';do{value=String.fromCharCode(65+index%26)+value;index=Math.floor(index/26)-1;}while(index>=0);return value;}
    color(site,mode){
      const faction=this.factions.get(site.owner),normal=faction?.color||0;
      return mode==='camp'&&faction?this.camps.get(faction.camp)?.color??normal:normal;
    }
    borderOwner(site,mode){return mode==='camp'&&this.factions.has(site.owner)?this.factions.get(site.owner).camp:site.owner;}
    abbreviation(owner){return this.camps.get(this.factions.get(owner)?.camp)?.abbreviation||'';}
  }
  class Controls {
    constructor(host,onChange){
      this.host=host;this.onChange=onChange;this.mode='normal';this.open=false;this.optionButtons=new Map();
      this.main=host.querySelector('#map-mode');this.options=host.querySelector('#map-mode-options');
      this.main.onclick=()=>this.setOpen(!this.open);
      for(const event of ['pointerdown','pointerup','click','wheel'])host.addEventListener(event,e=>e.stopPropagation());
      document.addEventListener('pointerdown',e=>{if(!host.contains(e.target))this.close();},true);
      document.addEventListener('keydown',e=>{
        if(e.key==='Escape'&&this.close()){this.main.focus();e.preventDefault();e.stopImmediatePropagation();}
        else if(e.key==='ArrowDown'&&document.activeElement===this.main){this.setOpen(true);this.options.firstElementChild.focus();e.preventDefault();}
        else if(this.open&&this.options.contains(e.target)&&['ArrowDown','ArrowUp','Home','End'].includes(e.key)){
          const buttons=[...this.options.children],index=buttons.indexOf(e.target);
          buttons[e.key==='Home'?0:e.key==='End'?buttons.length-1:(index+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length].focus();e.preventDefault();
        }
      },true);
      host.addEventListener('focusout',()=>{setTimeout(()=>{if(!host.contains(document.activeElement))this.close();},0);});
      this.renderMain();this.renderOptions();
    }
    renderMain(){
      const mode=modes.find(m=>m.id===this.mode);
      this.main.title=mode.name;this.main.setAttribute('aria-label',mode.name);
      const image=this.main.querySelector('img'),source='map-'+mode.id+'.png';
      if(image.getAttribute('src')!==source)image.src=source;
      this.main.querySelector('.map-mode-tip').textContent=mode.name;
      this.host.dataset.mapMode=mode.id;
    }
    renderOptions(){
      this.optionsStale=false;
      const buttons=modes.filter(m=>m.id!==this.mode).map(mode=>{
        if(this.optionButtons.has(mode.id))return this.optionButtons.get(mode.id);
        const button=document.createElement('button');button.type='button';button.className='map-mode-button';
        button.setAttribute('role','menuitemradio');button.setAttribute('aria-checked','false');button.setAttribute('aria-label',mode.name);button.title=mode.name;button.tabIndex=this.open?0:-1;
        const image=document.createElement('img');image.src='map-'+mode.id+'.png';image.alt='';
        const tip=document.createElement('span');tip.className='map-mode-tip';tip.textContent=mode.name;button.append(image,tip);
        button.onclick=event=>{this.select(mode.id);if(event.detail===0)this.main.focus();};
        this.optionButtons.set(mode.id,button);return button;
      });
      if(buttons.some((button,index)=>this.options.children[index]!==button))this.options.replaceChildren(...buttons);
    }
    select(mode){
      if(!modes.some(m=>m.id===mode))return;
      const changed=this.mode!==mode;this.mode=mode;this.optionsStale=changed||this.optionsStale;this.close();if(changed)this.renderMain();
      if(this.options.hidden)this.renderOptions();
      if(changed)this.onChange(mode);
    }
    setOpen(open){
      if(this.open===open)return;
      clearTimeout(this.timer);this.open=open;
      if(open){if(this.options.hidden||this.optionsStale)this.renderOptions();this.options.hidden=false;getComputedStyle(this.options.firstElementChild).transform;}
      this.host.classList.toggle('open',open);this.main.setAttribute('aria-expanded',String(open));this.options.setAttribute('aria-hidden',String(!open));
      for(const button of this.options.children)button.tabIndex=open?0:-1;
      if(!open)this.timer=setTimeout(()=>{this.options.hidden=true;if(this.optionsStale)this.renderOptions();},280);
    }
    close(){if(!this.open)return false;this.setOpen(false);return true;}
    reset(){this.close();this.select('normal');}
  }
  root.Ew4MapViews=Object.freeze({Projection,Controls,modes});
})(typeof window!=='undefined'?window:globalThis);
