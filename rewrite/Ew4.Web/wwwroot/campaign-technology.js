'use strict';
(function(){
  const proto=Ew4Menus.Menus.prototype,openChapter=proto.openChapter;
  proto.openChapter=async function(chapter){
    await openChapter.call(this,chapter);
    if(document.getElementById('campaign-upgrade'))return;
    const header=document.querySelector('#campaign-screen .menu-bar');
    const b=this.api.button('',()=>this.openTechnology().catch(e=>this.api.notice(e.message)),header);
    b.id='campaign-upgrade';b.title='兵种科技';b.setAttribute('aria-label','兵种科技');
    b.append(this.lettering('升级','font_text_3_cn_hd','#ebe6e1'));
  };
  proto.openTechnology=async function(){
    const chapter=this.chapter;if(!chapter)return;
    let data=await this.api.rpc('technology',{group:chapter.id-1});
    if(this.chapter!==chapter||document.getElementById('campaign-screen').hidden)return;
    const layout=new Ew4NativeUI.Layout(this.api,'form_upgrade',()=>removeEventListener('ew4uiscale',fit));layout.body.classList.add('campaign-technology');
    function fit(){
      if(!layout.current())return;
      const style=getComputedStyle(document.documentElement),width=parseFloat(style.getPropertyValue('--screen-safe-width'))||innerWidth,height=parseFloat(style.getPropertyValue('--screen-safe-height'))||innerHeight;
      const scale=Ew4ScreenLayout.fit(layout.width+2,layout.height+2,Math.max(1,parseFloat(style.getPropertyValue('--ui-scale'))||1),width,height,12);
      layout.body.parentElement.style.setProperty('--effective-detail-scale',scale);
    }
    addEventListener('ew4uiscale',fit);
    const tabs=layout.get('group_buttons'),grid=layout.get('grid_units'),upgrade=layout.get('btn_upgrade');
    tabs.replaceChildren();tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','科技分类');
    grid.replaceChildren();grid.setAttribute('role','group');grid.setAttribute('aria-label','科技等级');
    layout.hide('image_selbox');
    layout.get('image_verbar').replaceChildren();
    const art=['france','coalition','holyroman','east','usa','uk'][data.warzone];
    layout.fill('image_title',art?this.api.uiImage('campaign_'+art+'.png',64.5,47.5):this.lettering(data.chapter,'font_text_2_cn_hd','#404040',100,'center'));
    Object.assign(layout.get('image_title').style,{left:'23.25px',width:'64.5px',height:'47.5px'});
    Object.assign(layout.get('image_unit').style,{width:'72px',height:'54px'});
    let group=0,selected=0,pending=false;
    const choose=value=>{group=value;selected=data.entries.find(e=>e.group===value)?.slot??0;render();};
    const groups=[['步兵','infantry'],['骑兵','cavalry'],['炮兵','artillery'],['海军','ship'],['要塞','fortress']];
    for(const [index,[label,icon]] of groups.entries()){
      const b=this.api.button('',()=>choose(index),tabs);b.className='technology-tab';b.title=label;b.setAttribute('aria-label',label);b.setAttribute('role','tab');b.dataset.group=index;
      const name='upgrade_tag_'+icon+'.png',image=Ew4NativeUI.resource(this.manifest,name);
      b.append(image?this.api.uiImage(name,image.w/image.ratio,image.h/image.ratio):this.lettering(label,'font_text_2_cn_hd','#404040',47,'center',{singleLine:true}));
    }
    upgrade.dataset.technologyUpgrade='true';
    upgrade.onclick=async()=>{
      if(pending||upgrade.disabled||!layout.current())return;
      pending=true;render();
      try{data=await this.api.rpc('technology',{op:'upgrade',group:chapter.id-1,slot:selected,revision:data.revision});}
      catch(error){
        if(layout.current())this.api.notice(error.message);
        try{data=await this.api.rpc('technology',{group:chapter.id-1});}catch(refreshError){if(layout.current())this.api.notice(refreshError.message);}
      }finally{pending=false;if(layout.current()){render();upgrade.focus({preventScroll:true});}}
    };
    const render=()=>{
      if(!layout.current())return;
      for(const tab of tabs.children){const index=Number(tab.dataset.group),active=index===group;tab.setAttribute('aria-selected',String(active));tab.disabled=pending;tab.tabIndex=active?0:-1;
        const name='upgrade_tag_'+groups[index][1]+(active?'_s':'')+'.png',image=Ew4NativeUI.resource(this.manifest,name);tab.replaceChildren(this.api.uiImage(name,image.w/image.ratio,image.h/image.ratio));}
      const entries=data.entries.filter(e=>e.group===group),entry=data.entries.find(e=>e.slot===selected);
      grid.replaceChildren();
      for(const level of [3,2,1,0])for(let column=0;column<6;column++){
        const item=entries[column],cell=document.createElement(item?'button':'span');cell.className='technology-cell';grid.append(cell);if(!item)continue;
        cell.type='button';cell.dataset.slot=item.slot;cell.dataset.level=level;
        cell.setAttribute('aria-label',item.name+' LV'+(level+1)+(level<=item.level?' 已解锁':' 未解锁'));cell.setAttribute('aria-pressed',String(item.slot===selected));cell.disabled=pending;
        cell.onclick=()=>{selected=item.slot;render();grid.querySelector('[data-slot="'+selected+'"][data-level="'+level+'"]').focus({preventScroll:true});};
        const iconName=level<=item.level?item.image:'button_upgrade_locked.png',icon=Ew4NativeUI.resource(this.manifest,iconName);
        const badge=this.api.uiImage('button_upgrade_lv'+(level+1)+'.png',40,40.5),symbol=this.api.uiImage(iconName,icon.w/icon.ratio,icon.h/icon.ratio);
        Object.assign(symbol.style,{left:(25-icon.w/icon.ratio/2)+'px',top:(level<=item.level?4:6)+'px',width:icon.w/icon.ratio+'px',height:icon.h/icon.ratio+'px'});cell.append(badge,symbol);
        const text=document.createElement('span');text.className='technology-level';text.append(this.lettering('LV'+(level+1),'font_char_1_hd','#404040',40,'center',{singleLine:true}));cell.append(text);
      }
      layout.get('image_verbar').style.left=(entries.findIndex(item=>item.slot===selected)*50)+'px';
      layout.hide('image_selbox',entry.maximum);
      Object.assign(layout.get('image_selbox').style,{left:(entries.findIndex(item=>item.slot===selected)*50+3)+'px',top:(5+(3-entry.level-1)*50-2)+'px',width:'44px',height:'44.5px'});
      layout.text('text_currstar',String(data.stars),{color:'235,230,225,255'});layout.text('intitle',entry.name,{font:'font_text_2',align:'center'});
      layout.fill('image_unit',this.api.uiImage(entry.previewImage,72,54));
      layout.text('text_from',entry.level<0?'':entry.maximum?'MAX':'LV'+(entry.level+1),{color:'235,230,225,255'});layout.text('text_to',entry.maximum?'':'LV'+(entry.level+2),{color:'235,230,225,255'});
      layout.hide('image_arrow',entry.level<0||entry.maximum);layout.hide('text_from',entry.level<0);
      layout.text('text_needstar',entry.maximum?'MAX':String(entry.cost),{color:entry.canUpgrade?'235,230,225,255':'255,50,0,255'});layout.hide('image_needstar',entry.maximum);
      const price=layout.get('text_needstar');price.style.left=entry.maximum?'0px':'50px';price.style.width=entry.maximum?'83px':'33px';layout.text('text_needstar',entry.maximum?'MAX':String(entry.cost),{align:'center'});
      upgrade.disabled=pending||!entry.canUpgrade;upgrade.title=entry.maximum?'科技已满级':entry.canUpgrade?'升级 '+entry.name:'战役星数不足';
      upgrade.classList.toggle('technology-maximum',entry.maximum);
      upgrade.setAttribute('aria-label',entry.maximum?'科技已满级':'升级 '+entry.name+'，需要 '+entry.cost+' 星');
      layout.body.dataset.warzone=data.warzone;layout.body.dataset.selected=selected;layout.body.dataset.stars=data.stars;
    };
    tabs.onkeydown=e=>{if(pending)return;const next=e.key==='ArrowRight'?(group+1)%5:e.key==='ArrowLeft'?(group+4)%5:null;if(next!==null){e.preventDefault();choose(next);tabs.children[next].focus();}};
    render();fit();tabs.firstElementChild.focus({preventScroll:true});
  };
})();
