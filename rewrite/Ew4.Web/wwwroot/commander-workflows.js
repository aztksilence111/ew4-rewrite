'use strict';
(function(){
  const proto=Ew4Menus.Menus.prototype;
  proto.native=function(id,onClose){return new Ew4NativeUI.Layout(this.api,id,onClose);};
  proto.held=function(id){return this.data.roster.find(c=>c.id===id);};
  proto.definition=function(held){return this.data.definitions.find(d=>d.id===held.definitionId);};
  proto.progressionBadge=function(track,state){
    const threshold=this.data.progression?.[track]?.[state.level],capped=threshold===null,known=Number.isFinite(threshold)&&threshold>0;
    const ratio=capped?1:known?Math.max(0,Math.min(1,state.experience/threshold)):0;
    const badge=document.createElement('span');badge.className='rank-gauge';badge.dataset.track=track;badge.dataset.level=state.level;badge.dataset.experience=state.experience;
    badge.setAttribute('role','progressbar');badge.setAttribute('aria-label',track==='rank'?'军衔经验':'爵位经验');
    badge.setAttribute('aria-valuemin','0');if(capped||known){badge.setAttribute('aria-valuemax',capped?1:threshold);badge.setAttribute('aria-valuenow',capped?1:Math.min(threshold,state.experience));}
    badge.setAttribute('aria-valuetext',capped?'MAX':known?state.experience+' / '+threshold:String(state.experience));badge.title=badge.getAttribute('aria-label')+' '+badge.getAttribute('aria-valuetext');
    badge.dataset.sweep=ratio*180;
    const canvas=document.createElement('canvas');canvas.width=canvas.height=57;canvas.setAttribute('aria-hidden','true');badge.append(canvas);
    const board=this.api.uiImage(track==='rank'?'borad_rank.png':'borad_standing.png',57),bar=this.api.uiImage(track==='rank'?'bar_rank.png':'bar_standing.png',70,40);
    const icon=state.level>0?this.api.uiImage((track==='rank'?'rank_':'class_')+state.level+'.png',42,48):null;
    Promise.all([board,bar,icon].filter(Boolean).map(image=>image.decode())).then(()=>{
      const draw=canvas.getContext('2d');draw.drawImage(board,0,0,57,57);
      if(ratio>0){
        const overlay=document.createElement('canvas');overlay.width=overlay.height=57;const ink=overlay.getContext('2d');
        ink.beginPath();ink.moveTo(28,28);ink.arc(28,28,60,Math.PI,Math.PI+ratio*Math.PI);ink.closePath();ink.clip();ink.drawImage(bar,-7,-2,70,40);
        // Clip the paint to the original gray track before scaling the whole badge.
        const base=draw.getImageData(0,0,57,57).data,paint=ink.getImageData(0,0,57,57);
        for(let i=0;i<base.length;i+=4)paint.data[i+3]*=base[i]===base[i+1]&&base[i]===base[i+2]?Math.min(1,base[i]/64)*base[i+3]/255:0;
        ink.putImageData(paint,0,0);draw.drawImage(overlay,0,0);
      }
      if(icon){const scale=Math.min(42/icon.naturalWidth,48/icon.naturalHeight),w=icon.naturalWidth*scale,h=icon.naturalHeight*scale;draw.drawImage(icon,28-w/2,28-h/2,w,h);}
      badge.dataset.ready='true';
    }).catch(error=>this.api.notice(error.message));
    return badge;
  };
  proto.reopenCommander=function(id){const held=this.held(id);if(held)this.commanderInfo(this.definition(held),held,this.commanderReturn);};
  proto.updateAccount=async function(args){this.data=await this.api.rpc('commander',{revision:this.data.revision,...args});this.drawRoster();};
  proto.editCommanderTags=async function(){
    if(this.tagSaving)return;
    if(!this.tagDraft){this.tagDraft=new Set(this.data.roster.filter(c=>c.tag).map(c=>c.id));this.drawRoster();return;}
    this.tagSaving=true;this.drawRoster();
    try{
      this.data=await this.api.rpc('commander',{op:'tags',revision:this.data.revision,taggedCommanders:[...this.tagDraft]});
      this.tagDraft=null;
    }catch(error){this.api.notice(error.message);}
    finally{this.tagSaving=false;this.drawRoster();}
  };
  proto.headquartersControls=function(){
    for(const id of ['hq-princess','hq-shop','hq-tags'])document.getElementById(id)?.remove();
    if(this.recruiting||this.deployment)return;
    const layout=this.manifest.layouts.form_deploygeneral;
    for(const [native,id,title,action] of [['btn_princess','hq-princess',this.text('btn_princess'),()=>this.princesses()],['btn_shop','hq-shop',this.text('title_shop'),()=>this.shop()]]){
      const a=layout.children.find(e=>e.attributes.id===native).attributes,b=this.api.button('',action,document.getElementById('hq-screen'));
      b.id=id;b.className='native-command'+(native==='btn_shop'?' blue':'');b.setAttribute('aria-label',title);
      b.disabled=!!this.tagDraft;
      Object.assign(b.style,{position:'absolute',left:a.x+'px',top:a.y+'px',width:a.w+'px',height:a.h+'px'});
      b.append(native==='btn_shop'?this.api.uiImage('image_shop.png',20,16):this.lettering(title,'font_text_3_cn_hd','#ebe6e1'));
    }
    const title=this.tagDraft?'确认':'标签',tags=this.api.button('',()=>this.editCommanderTags(),document.getElementById('hq-screen'));
    tags.id='hq-tags';tags.className='native-command'+(this.tagDraft?' orange':' blue');tags.setAttribute('aria-label',this.tagDraft?'确认标签':'标签');tags.title=this.tagDraft?'确认标签':'赋予标签';tags.setAttribute('aria-pressed',String(!!this.tagDraft));tags.disabled=this.tagSaving;
    tags.append(this.lettering(title,'font_text_3_cn_hd','#ebe6e1'));
  };
  proto.dismissSkillTip=function(){this.skillPopup?.remove();this.skillEvents?.();this.skillEvents=null;this.skillPopup=null;this.skillAnchor=null;this.skillPinned=false;};
  proto.skillTip=function(anchor,skill,pin=false){
    if(this.skillAnchor===anchor&&this.skillPopup){if(pin){if(this.skillPinned)this.dismissSkillTip();else this.skillPinned=true;}return;}
    this.dismissSkillTip();this.skillAnchor=anchor;this.skillPinned=pin;
    const tip=document.createElement('div');tip.id='commander-skill-tip';tip.className='skill-tooltip';tip.setAttribute('role','tooltip');tip.append(this.lettering(skill.description,'font_text_2_cn_hd','#ebe6e1',145));this.skillPopup=tip;
    const dialog=document.getElementById('detail-modal').firstElementChild;dialog.append(tip);
    const a=anchor.getBoundingClientRect(),d=dialog.getBoundingClientRect(),scale=d.width/dialog.offsetWidth;
    const left=a.left-d.left>170*scale;
    tip.classList.toggle('tip-right',!left);tip.style.left=((left?a.left-d.left-165*scale:a.right-d.left+8*scale)/scale)+'px';
    tip.style.top=Math.max(27,Math.min(dialog.offsetHeight-tip.offsetHeight-4,(a.top-d.top)/scale+(anchor.offsetHeight-tip.offsetHeight)/2))+'px';
    tip.style.setProperty('--tip-arrow-top',Math.max(0,Math.min(tip.offsetHeight-19,(a.top+a.bottom-2*d.top)/2/scale-parseFloat(tip.style.top)-9.5))+'px');
    tip.onclick=()=>this.dismissSkillTip();
    const outside=e=>{if(!anchor.contains(e.target)&&!tip.contains(e.target))this.dismissSkillTip();};
    const escape=e=>{if(e.key==='Escape'){e.stopPropagation();this.dismissSkillTip();}};
    document.addEventListener('pointerdown',outside);
    document.addEventListener('keydown',escape,true);
    this.skillEvents=()=>{document.removeEventListener('pointerdown',outside);document.removeEventListener('keydown',escape,true);};
  };
  proto.promotionShortcut=function(card,held,onClose){
    if(!held||held.locked||held.learning.rank.level>=Math.max(...Object.keys(this.data.rankHealth).map(Number))&&held.learning.title.level>=Math.max(...Object.keys(this.data.titleHealing).map(Number)))return;
    const b=document.createElement('button');b.className='promote-commander';b.setAttribute('aria-label','晋升');b.title='晋升';b.append(this.api.uiImage('button_lvlup.png',22,25));
    b.onclick=e=>{e.stopPropagation();this.promotion(held.id,onClose);};card.append(b);
  };
  proto.princesses=function(){
    const l=this.native('form_princess',()=>this.drawRoster());
    for(const [index,p] of this.data.princesses.entries()){
      const d=this.data.definitions.find(d=>d.id===p.definitionId),card=this.card({...d,image:p.unlocked?p.image:p.image.replace('.png','_sketch.png')},null,()=>this.commanderInfo(d,this.data.roster.find(c=>c.definitionId===d.id),()=>this.princesses()));
      card.querySelector('.commander-info').onclick=e=>{e.stopPropagation();this.commanderInfo(d,this.data.roster.find(c=>c.definitionId===d.id),()=>this.princesses());};
      card.classList.toggle('reward-claimed',p.claimed);l.get(index<4?'lbox_princess_up':'lbox_princess_dn').append(card);
      l.action('btn_go_'+(index+1),p.claimed?'已领取':'领取 '+d.name,async()=>{await this.updateAccount({op:'claim',offer:p.id});this.princesses();},p.unlocked&&!p.claimed);
      if(p.claimed)l.text('btn_go_'+(index+1),'已领取');
    }
  };
  proto.itemButton=function(item,quantity,select){
    const b=this.api.button('',select,document.createElement('div'));b.className='native-item';b.setAttribute('aria-label',item?.name||'空物品位');
    b.append(this.api.uiImage(item?.image||'item_blank.png',45));
    if(item&&Number.isFinite(quantity)&&(item.consumable||quantity>1)){const n=document.createElement('span');n.className='item-quantity';n.append(this.lettering(quantity,'font_char_2_hd','#ebe6e1'));b.append(n);}
    return b;
  };
  proto.equipment=function(id){
    const held=this.held(id),definition=this.definition(held),l=this.native('form_deployitem',()=>this.reopenCommander(id));
    const description=l.get('group_desc'),equip=l.get('btn_equip');
    description.append(equip);
    Object.assign(equip.style,{left:'50%',transform:'translateX(-50%)',top:'auto',bottom:'4px',width:'83px',height:'30px'});
    Object.assign(l.get('text_desc').style,{height:(parseFloat(description.style.height)-60)+'px',overflowY:'auto'});
    l.text('btn_equip',this.text('btn_equip'),{size:15});
    l.fill('tcmder',this.card(definition,held,()=>{}));
    const list=this.data.roster,index=list.indexOf(held);
    for(const [name,offset] of [['btn_prev',-1],['btn_next',1]])l.action(name,offset<0?'上一位将领':'下一位将领',()=>this.equipment(list[index+offset].id),index+offset>=0&&index+offset<list.length);
    for(const [key,track] of [['rank_military','rank'],['rank_nobility','title']])l.fill(key,this.progressionBadge(track,held.learning[track]));
    l.text('text_life',held.healthBonus);l.text('text_apply',held.healing);
    let chosen=null,removing=false;
    const choose=(item,remove,b)=>{chosen=item;removing=remove;for(const n of l.body.querySelectorAll('.native-item'))n.setAttribute('aria-pressed',String(n===b));l.text('intitle_desc',item.name);l.text('text_desc',item.description);l.text('btn_equip',this.text(remove?'text_unequip':'btn_equip').replace('text_unequip','卸下'));l.action('btn_equip',remove?'卸下 '+item.name:'装备 '+item.name,async()=>{await this.updateAccount({op:remove?'unequip':'equip',commander:id,item:item.id});this.equipment(id);},!held.locked&&(remove||!item.consumable&&held.items.length<(this.data.equipmentCapacity??Infinity)&&!held.items.includes(item.id)));};
    for(let i=0;i<(this.data.equipmentCapacity??2);i++){
      const item=this.data.items.find(d=>d.id===held.items[i]);let b=this.itemButton(item,0,()=>item&&choose(item,true,b));b.disabled=!item;l.get('lbox_equipitem').append(b);
    }
    for(const item of this.data.items.filter(i=>i.available>0)){
      let b=this.itemButton(item,item.available,()=>choose(item,false,b));l.get('grid_items').append(b);
    }
    l.action('btn_equip',this.text('btn_equip'),()=>{},false);l.text('intitle_desc','');
  };
  proto.battleEquipment=function(held,onClose){
    const l=this.native('form_deployitem',onClose),definition=this.definition(held);
    const portrait=this.card(definition,held,()=>{});delete portrait.dataset.locked;portrait.querySelector('.commander-info').remove();l.fill('tcmder',portrait);
    for(const [key,track] of [['rank_military','rank'],['rank_nobility','title']])l.fill(key,this.progressionBadge(track,held.learning[track]));
    l.text('text_life',held.healthBonus);l.text('text_apply',held.healing);
    for(const key of ['btn_prev','btn_next','btn_equip'])l.hide(key);
    l.hide('grid_items');l.text('intitle_desc','');
    for(let i=0;i<Math.max(this.data.equipmentCapacity??2,held.items.length);i++){
      const item=this.data.items.find(d=>d.id===held.items[i]);let b=this.itemButton(item,0,()=>{l.text('intitle_desc',item.name);l.text('text_desc',item.description);});b.disabled=!item;l.get('lbox_equipitem').append(b);
    }
  };
  proto.promotion=function(id,onClose){
    const held=this.held(id),definition=this.definition(held),l=this.native('form_generalupgrade',onClose||(()=>this.reopenCommander(id)));
    l.fill('tcmder',this.card(definition,held,()=>{}));
    for(const [path,el] of l.nodes){
      if(!/text_life(?:from|to)$|text_medal$/.test(path))continue;
      const cost=path.endsWith('text_medal');
      el.style.height='20px';el.style.top=cost?'5px':'22px';
      el.nativeAttributes={...el.nativeAttributes,size:15,align:'center'};
    }
    for(const [kind,track,price,effects] of [['military','rank',held.rankPrice,this.data.rankHealth],['nobility','title',held.titlePrice,this.data.titleHealing]]){
      const level=held.learning[track].level,next=price===null?level:level+1,path='group_'+kind+'/';
      l.fill(path+'rank_from',this.progressionBadge(track,held.learning[track]));l.fill(path+'rank_to',this.progressionBadge(track,{level:next,experience:price===null?held.learning[track].experience:0}));
      l.text(path+'text_lifefrom',effects[level]);l.text(path+'text_lifeto',effects[next]);l.text(path+'btn_'+kind+'full/text_medal',price??'MAX');
      l.action(path+'btn_'+kind+'full',track==='rank'?'晋升军衔':'晋升爵位',async()=>{await this.updateAccount({op:track,commander:id});this.promotion(id,onClose);},!held.locked&&price!==null&&price<=this.data.medals);
    }
    l.text('group_fullrank/btn_allfull/text_medal',held.fullPrice||'MAX');
    l.action('btn_allfull','军衔爵位升至满级',async()=>{await this.updateAccount({op:'full',commander:id});this.promotion(id,onClose);},!held.locked&&held.fullPrice>0&&held.fullPrice<=this.data.medals);
  };
  proto.regroup=async function(id,donorId){
    const receiver=this.held(id),definition=this.definition(receiver),l=this.native('form_regroup',()=>this.reopenCommander(id));
    l.fill('tcmder_source',this.card(definition,receiver,()=>{}));
    l.text('text_notice',this.text('text_regroupnotice'));
    const donors=this.data.roster.filter(c=>c.id!==id&&!c.locked);donorId=donors.some(c=>c.id===donorId)?donorId:donors[0]?.id;
    for(const held of donors){const b=this.card(this.definition(held),held,()=>this.regroup(id,held.id));b.setAttribute('aria-pressed',String(held.id===donorId));l.get('lbox_general').append(b);}
    if(!donorId){l.action('btn_regroup',this.text('btn_regroup'),()=>{},false);return;}
    const donor=this.held(donorId);l.fill('tcmder_target',this.card(this.definition(donor),donor,()=>{}));
    for(let i=0;i<(this.data.equipmentCapacity??2);i++)l.get('lbox_equipitem').append(this.api.uiImage(this.data.items.find(d=>d.id===donor.items[i])?.image||'item_blank.png',45));
    const preview=await this.api.rpc('commander',{op:'preview_inherit',revision:this.data.revision,commander:id,target:donorId});
    if(!l.current())return;
    for(const [key,track] of [['rank_military','rank'],['rank_nobility','title']])l.fill(key,this.progressionBadge(track,preview.learning[track]));
    l.text('text_life',this.data.rankHealth[preview.learning.rank.level]);
    l.text('text_apply',this.data.titleHealing[preview.learning.title.level]);
    const grid=l.get('grid_preview');for(const [key,value] of Object.entries(preview.learning.abilities).filter(([key,value])=>value>receiver.learning.abilities[key])){const row=document.createElement('div');row.append(this.api.uiImage((key==='naval'?'upgrade_tag_ship':key==='fort'?'upgrade_tag_fortress':'upgrade_tag_'+key)+'.png',20),this.lettering(value,'font_char_2_hd'));grid.append(row);}
    l.action('btn_regroup',this.text('btn_regroup'),()=>{
      const confirm=this.api.detail('title_notice',320,170,async()=>{await this.updateAccount({op:'inherit',commander:id,target:donorId});this.reopenCommander(id);},()=>this.regroup(id,donorId));confirm.className='regroup-confirm';
      confirm.append(this.lettering(this.text('text_regroup'),'font_text_2_cn_hd','#404040'),this.card(this.definition(donor),donor,()=>{}),this.lettering(this.text('text_regroupnotice'),'font_text_1_cn_hd','#a02320',270));
    },!receiver.locked&&preview.price<=this.data.medals);
  };
  proto.useItems=async function(unit){
    const data=await this.api.rpc('items',{unit}),l=this.native('form_useitem',()=>this.api.refresh());
    const confirm=document.getElementById('detail-confirm');
    let chosen=null,pending=false;
    confirm.hidden=false;confirm.disabled=true;confirm.setAttribute('aria-label','使用物品');confirm.title='使用物品';
    l.text('group_desc/intitle','');l.text('text_desc','');
    const choose=(item,b)=>{
      if(pending)return;chosen=item;
      for(const node of l.get('lbox_item').children)node.setAttribute('aria-pressed',String(node===b));
      l.text('group_desc/intitle',item.name);
      l.text('text_desc',item.description+(item.reason?'\n'+item.reason:''),{breakwords:'true'});
      confirm.disabled=!item.available;confirm.setAttribute('aria-label','使用 '+item.name);confirm.title='使用 '+item.name;
    };
    for(const item of data.items){
      const b=this.itemButton(item,item.quantity,()=>choose(item,b));
      b.dataset.tutorialId='lbox_item';b.dataset.tutorialRow=data.items.indexOf(item);
      b.setAttribute('aria-label',item.name+' × '+item.quantity);b.title=item.name+' × '+item.quantity;
      b.classList.toggle('unavailable-item',!item.available);
      if(item.quantity<=1){const n=document.createElement('span');n.className='item-quantity';n.append(this.lettering(item.quantity,'font_char_2_hd','#ebe6e1'));b.append(n);}
      l.get('lbox_item').append(b);
    }
    confirm.onclick=async()=>{
      if(pending||!l.current()||!chosen?.available)return;
      pending=true;confirm.disabled=true;
      try{
        await this.api.act('use_item',{unit,item:chosen.id,revision:data.revision});
        if(l.current())this.api.closeDetail();
      }catch(e){this.api.notice(e.message);if(l.current()){await this.api.refresh();if(l.current())await this.useItems(unit);}}
      finally{pending=false;}
    };
    const first=data.items.find(i=>i.available)||data.items[0];
    if(first)choose(first,l.get('lbox_item').children[data.items.indexOf(first)]);
  };
  proto.commerceStars=function(layout,level){
    const list=layout.get('lbox_stars');list.replaceChildren();list.setAttribute('aria-label','经商 '+level+(level<=5?' / 5':''));
    const count=level>5?1:5;
    for(let i=0;i<count;i++){const star=this.api.uiImage('star_middle.png',13.5,12.5);star.classList.toggle('commerce-star-empty',i>=level);list.append(star);}
    if(level>5)list.append(this.lettering('x'+level,'font_char_1_hd','#ebe6e1'));
  };
  proto.serviceCommander=function(layout,slot,data,onReturn){
    const held=data.commanderView||(data.commander?this.held(data.commander):null);
    if(!held)return;
    const definition=this.definition(held),open=()=>this.commanderInfo(definition,held,onReturn),card=this.card(definition,held,open);
    delete card.dataset.locked;card.title='';
    card.querySelector('.commander-info').onclick=e=>{e.stopPropagation();open();};
    layout.fill(slot,card);
  };
  proto.exchangeQuote=function(layout,key,resource,left,right){
    const quote=document.createElement('span');quote.className='exchange-quote';
    const icons=key.startsWith('buy')?['money',resource]:[resource,'money'];
    quote.append(this.api.uiImage('marker_'+icons[0]+'.png',12,16),this.lettering(left,'font_char_2_hd','#ebe6e1'),this.lettering('=','font_char_2_hd','#ebe6e1'),this.api.uiImage('marker_'+icons[1]+'.png',12,16),this.lettering(right,'font_char_2_hd','#ebe6e1'));
    layout.get('btn_'+key).replaceChildren(quote);
  };
  proto.shop=async function(cell=-1,snapshot){
    if(!this.data)this.data=await this.api.rpc('headquarters');
    let data=snapshot||await this.api.rpc('commerce',{cell}),pending=false,refresh;
    const l=this.native('form_shop',async()=>{this.data=await this.api.rpc('headquarters');this.drawRoster();await this.api.refresh();});
    const refreshLabel=()=>{
      if(!refresh)return;
      refresh.replaceChildren(this.api.uiImage('button_refresh.png',20,22));
      const count=data.refresh?.remaining;
      if(count!=null)refresh.append(this.lettering(count,'font_char_2_hd','#ebe6e1'));
      refresh.title='刷新商品'+(count==null?'':'（剩余 '+count+' 次）');refresh.setAttribute('aria-label',refresh.title);
    };
    const transact=async op=>{
      if(pending||!l.current())return;
      pending=true;if(refresh)refresh.disabled=true;
      const command=l.get('btn_buy'),wasDisabled=command.disabled;command.disabled=true;
      try{
        const next=await this.api.rpc('commerce',{cell,revision:data.revision,...op});
        if(!l.current())return;
        data=next;
        if(op.op==='refresh'){
          renderSeller();if(buying)clearSelection();
        }else{renderSeller();renderBuyer();clearSelection();}
        refreshLabel();
      }finally{pending=false;if(refresh)refresh.disabled=false;command.disabled=wasDisabled;}
    };
    if(cell<0){
      refresh=this.api.button('',()=>transact({op:'refresh'}),l.body);
      refresh.className='shop-refresh native-command blue';refreshLabel();
    }
    let chosen,buying;
    const choose=(item,buy,b,offer)=>{
      if(pending)return;
      chosen={item,offer};buying=buy;for(const n of l.body.querySelectorAll('.native-item'))n.setAttribute('aria-pressed',String(n===b));
      l.hide('btn_buy',false);
      l.get('btn_buy').style.width=b.offsetWidth+'px';
      l.text('group_info/text_name',item.name);l.text('text_desc',item.description);l.text('text_price',buy?offer.price:data.sales.find(s=>s.id===item.id)?.price||0);l.text('btn_buy',this.text(buy?'text_buy':'text_sell'));
      const price=buy?offer.price:0;
      const command=l.action('btn_buy',this.text(buy?'text_buy':'text_sell')+' '+item.name,async()=>{
        await transact({op:buying?'buy':'sell',offer:chosen.offer?.id,item:chosen.item.id});
      },buy?offer.available&&data.medals>=price:item.available>0);
      const itemRect=b.getBoundingClientRect(),body=l.body.getBoundingClientRect(),scale=body.width/l.width;
      const itemTop=(itemRect.top-body.top)/scale,itemLeft=(itemRect.left-body.left)/scale;
      const above=b.offsetTop>=command.offsetHeight;
      Object.assign(command.style,{left:itemLeft+'px',top:(above?itemTop-command.offsetHeight:itemTop+b.offsetHeight)+'px'});
      this.api.popup(command);
    };
    const renderSeller=()=>{
      const grid=l.get('grid_seller');grid.replaceChildren();
      for(let slot=0;slot<14;slot++){const offer=data.offers.find(o=>o.slot===slot),item=offer&&data.items.find(i=>i.id===offer.itemId);let b=this.itemButton(item,offer?.remaining,()=>offer&&choose(item,true,b,offer));b.disabled=!offer;if(offer)b.classList.toggle('unavailable-item',!offer.available);grid.append(b);}
    };
    const renderBuyer=()=>{
      const grid=l.get('grid_buyer');grid.replaceChildren();
      for(const item of data.items.filter(i=>i.quantity>0)){let b=this.itemButton(item,item.quantity,()=>choose(item,false,b));grid.append(b);}
      while(grid.children.length<14){const b=this.itemButton(null,0,()=>{});b.disabled=true;grid.append(b);}
    };
    const clearSelection=()=>{chosen=null;buying=false;l.hide('btn_buy');l.text('group_info/text_name','');l.text('text_desc',this.text('text_sellersays'),{color:'235,230,225,255'});l.text('text_price','');};
    renderSeller();renderBuyer();
    l.text('group_seller/text_name',this.text('name_Seller'),{color:'235,230,225,255'});
    clearSelection();l.text('text_discount',data.commerce?'-'+data.commerce*4+'%':'');
    const merchant=l.get('group_seller');merchant.setAttribute('aria-label',this.text('name_Seller'));
    this.serviceCommander(l,'tcmder_buyer',data,()=>this.shop(cell));
    this.commerceStars(l,data.commerce);
  };
  proto.exchange=async function(cell,snapshot){
    if(!this.data)this.data=await this.api.rpc('headquarters');
    const data=snapshot||await this.api.rpc('exchange',{cell}),l=this.native('form_exchange',()=>this.api.refresh());
    l.text('group_seller/text_name',this.text('name_Seller'),{color:'235,230,225,255'});
    l.text('text_rate','x'+data.rate.toFixed(1),{color:'235,230,225,255'});
    for(const [index,offer] of data.offers.entries())for(const [op,allowed] of [['buy',offer.canBuy],['sell',offer.canSell]]){
      const key=op+'_'+(index+1);l.text('text_'+key+'a',op==='buy'?offer.buyMoney:offer.sellAmount);l.text('text_'+key+'b',op==='buy'?offer.buyAmount:offer.sellMoney);
      this.exchangeQuote(l,key,offer.resource,op==='buy'?offer.buyMoney:offer.sellAmount,op==='buy'?offer.buyAmount:offer.sellMoney);
      l.action('btn_'+key,(op==='buy'?'购买':'出售')+(offer.resource==='industry'?'工业':'粮食')+' '+(op==='buy'?offer.buyAmount:offer.sellAmount),async()=>{const next=await this.api.rpc('exchange',{cell,revision:data.revision,op,offer:offer.id});await this.api.refresh();await this.exchange(cell,next);},allowed);
    }
    this.serviceCommander(l,'tcmder_player',data,()=>this.exchange(cell));
    this.commerceStars(l,data.commerce);
  };
  proto.tavern=async function(cell){
    this.data=await this.api.rpc('headquarters');const data=await this.api.rpc('commerce',{op:'tavern',cell}),l=this.native('form_recruitgeneral',()=>this.api.refresh());
    const groups=[...l.nodes].filter(([key])=>/^group_gen_\d+$/.test(key));
    l.rows(groups.slice(0,Math.max(1,data.candidates.length)).map(([key])=>key));
    groups.forEach(([key,el],index)=>{
      const candidate=data.candidates[index];if(!candidate){el.hidden=true;return;}
      const d=this.data.definitions.find(d=>d.id===candidate.definitionId),card=this.card(d,null,()=>this.commanderInfo(d,null,()=>this.tavern(cell)));card.querySelector('.commander-info').onclick=e=>{e.stopPropagation();this.commanderInfo(d,null,()=>this.tavern(cell));};l.fill(key+'/tcmder',card);
      card.classList.add('portrait-only');card.classList.toggle('unavailable-candidate',!candidate.available);
      l.text(key+'/text_name',d.name);l.text(key+'/group/text_medal',candidate.medals);l.text(key+'/group/text_money',candidate.cost.money);l.text(key+'/group/text_indus',candidate.cost.industry);
      for(const [name,kind,level] of [['rank_military','rank',d.rank],['rank_nobility','class',d.title]])l.fill(key+'/'+name,...(level?[this.api.uiImage(kind+'_'+level+'.png',21,24)]:[]));
      l.action(key+'/btn_rec_'+(index+1),this.text('btn_recruit')+' '+d.name,async()=>{await this.api.act('hire',{unit:data.unit,offer:candidate.id});await this.tavern(cell);},candidate.available);
      if(data.round<candidate.availableRound)l.text(key+'/btn_rec_'+(index+1),this.text('btn_round_word')+' '+data.round+'/'+candidate.availableRound);
    });
  };
})();
