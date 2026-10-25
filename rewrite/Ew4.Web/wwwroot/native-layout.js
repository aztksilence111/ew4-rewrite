'use strict';
(function(root){
  const atlases=['image_ui_hd','image_item_hd','image_recruit_hd','image_recruit_hd2','image_recruit_hd3','image_menu_hd','image_newgame_hd','commanders1_hd','commanders2_hd','princesses_hd','princesses_sketch_hd','image_ui'];
  function resource(manifest,name){
    const atlas=atlases.find(a=>manifest.atlases[a]?.[name]);
    if(!atlas)return null;
    return {file:'/content/ui/'+atlas+'/'+encodeURIComponent(name),...manifest.atlases[atlas][name],ratio:atlas.endsWith('_hd')?2:1};
  }
  class Layout {
    constructor(api,id,onClose){
      this.api=api;this.definition=api.manifest.layouts[id];this.nodes=new Map();
      const a=this.definition.attributes;this.width=Number(a.w)||568;this.height=Number(a.h)||320;
      this.body=api.detail(a.title||'',this.width,this.height,null,onClose);this.body.className='native-layout';
      this.body.dataset.layout=id;
      this.body.parentElement.classList.add('native-dialog');
      this.body.nativeLayout=this;
      for(const node of this.definition.children)this.mount(node,this.body,'',this.width,this.height-27,true);
    }
    mount(node,parent,path,pw,ph,topLevel=false){
      const a=node.attributes,id=path?path+'/'+a.id:a.id,type=a.type;
      let el=type.includes('button')?this.api.button('',()=>{},parent):document.createElement('div');
      if(!el.parentElement)parent.append(el);el.className='native-node native-'+type;el.dataset.nativeId=a.id;el.dataset.tutorialId=a.id;
      this.nodes.set(id,el);el.nativeAttributes=a;
      let art=resource(this.api.manifest,a.name||a.frm1),w=Number(a.w)||art?.w/art?.ratio||pw,h=Number(a.h)||art?.h/art?.ratio||ph;
      if(['extend','hextend'].includes(a.DrawMode)&&!a.w)w=pw;
      if(['extend','vextend'].includes(a.DrawMode)&&!a.h)h=ph;
      if(type==='tmp_commander'){w=Number(a.w)||78;h=Number(a.h)||98;}
      if(type==='tmp_intitle')h=22;
      if(type==='text'){h=Number(a.h)||18;w=Number(a.w)||pw-Number(a.x||0);}
      const x=a.align==='hmiddle'?(pw-w)/2+Number(a.x||0):Number(a.x||0);
      Object.assign(el.style,{left:x+'px',top:(Number(a.y||0)-(topLevel?27:0))+'px',width:w+'px',height:h+'px'});
      if(a.scale){el.style.transform='scale('+a.scale+')';el.style.transformOrigin='top left';}
      if(a.hscale||a.vscale){el.style.transform=`scale(${a.hscale||1},${a.vscale||1})`;el.style.transformOrigin='top left';}
      if(a.frame){const frame=resource(this.api.manifest,a.frame);if(frame){el.classList.add('native-frame');el.style.borderImageSource=`url("${frame.file}")`;}}
      if(type==='image'&&art){const img=this.api.uiImage(a.name,w,h);el.append(img);el.classList.add('native-art');}
      if(type.includes('button')){
        el.style.justifyContent='center';el.style.alignItems='center';
        if(type==='tmp_button'){el.classList.add('native-command');if(/blue/.test(a.frm1))el.classList.add('blue');if(/red/.test(a.frm1))el.classList.add('red');}
        else if(art){el.style.backgroundImage=`url("${art.file}")`;el.style.backgroundSize='100% 100%';}
        if(a.frm2){const pressed=resource(this.api.manifest,a.frm2);if(pressed)el.style.setProperty('--pressed-art',`url("${pressed.file}")`);}
        el.disabled=true;
      }
      if(type==='text'||type==='tmp_intitle'||(type.includes('button')&&(a.text||a.string))){
        const value=a.text||a.title||a.string||'';el.classList.add('native-caption');
        if(a.align==='center'||type==='tmp_intitle')el.style.justifyContent='center';
        if(a.valign==='middle')el.style.alignItems='center';
        this.write(el,this.api.manifest.strings[value]||value,a);
      }
      if(type==='grid'||type==='listbox'){
        el.classList.add('native-list');
        if(type==='grid'){el.style.display='grid';el.style.gridTemplateColumns=`repeat(${a.cols||7},${a.rowh||45}px)`;el.style.gridAutoRows=(a.rowh||45)+'px';el.style.gap=(a.vbland||3)+'px '+(a.hbland||3)+'px';}
        else {el.style.display='flex';el.style.gap=(a.interval||0)+'px';}
      }
      for(const child of node.children)this.mount(child,el,id,w,h);
      return el;
    }
    get(id){const exact=this.nodes.get(id);if(exact)return exact;for(const [key,el] of this.nodes)if(key.endsWith('/'+id))return el;throw Error('Unknown original control '+id);}
    current(){return this.body.isConnected&&this.body.nativeLayout===this&&!document.getElementById('detail-modal').hidden;}
    write(el,value,a=el.nativeAttributes||{}){
      const font=(a.font||'font_text_2').replace(/^(font_(?:text|char)_\d)$/,(_,s)=>s.startsWith('font_text')?s+'_cn_hd':s+'_hd');
      const rgba=(a.color||a.textcolor||(a.type?.includes('button')?'235,230,225,255':'64,64,64,255')).split(',').map(Number);
      const color=`rgba(${rgba[0]},${rgba[1]},${rgba[2]},${rgba[3]/255})`;
      const width=parseFloat(el.style.width),height=parseFloat(el.style.height),singleLine=a.breakwords!=='true';
      const align=['left','center','right'].includes(a.align)?a.align:a.type?.includes('button')||a.type==='tmp_intitle'?'center':'left';
      el.replaceChildren(this.api.text(value,font,color,Number.isFinite(width)?width:undefined,align,{size:a.size?Number(a.size):undefined,singleLine,maxHeight:singleLine?height:undefined}));
    }
    text(id,value,attributes){const el=this.get(id);if(attributes)el.nativeAttributes={...el.nativeAttributes,...attributes};this.write(el,value);return el;}
    fill(id,...nodes){const el=this.get(id);
      if(el.nativeAttributes.type==='tmp_rank'){
        const name=el.nativeAttributes.ranktype==='military'?'borad_rank.png':'borad_standing.png',art=resource(this.api.manifest,name);
        Object.assign(el.style,{width:art.w/art.ratio+'px',height:art.h/art.ratio+'px',display:'flex',alignItems:'center',justifyContent:'center'});
        if(nodes.some(n=>n.classList.contains('rank-gauge')))el.replaceChildren(...nodes);
        else{const board=this.api.uiImage(name,art.w/art.ratio,art.h/art.ratio);board.className='native-rank-board';el.replaceChildren(board,...nodes);}
      }else el.replaceChildren(...nodes);return el;
    }
    rows(ids,gap=5){
      let y=Number(this.get(ids[0]).nativeAttributes.y)-27;
      for(const id of ids){const el=this.get(id);el.style.top=y+'px';y+=parseFloat(el.style.height)+gap;}
      this.height=y+27;this.body.parentElement.style.height=this.height+'px';
    }
    action(id,label,action,enabled=true){const el=this.get(id);el.disabled=!enabled;el.setAttribute('aria-label',label);el.title=label;let pending=false;el.onclick=async()=>{if(pending||el.disabled||!this.current())return;pending=true;el.disabled=true;try{await action();await this.api.tutorialUi(el.dataset.tutorialId);}catch(e){this.api.notice(e.message);}finally{pending=false;if(this.current())el.disabled=!enabled;}};return el;}
    hide(id,value=true){this.get(id).hidden=value;}
  }
  root.Ew4NativeUI={Layout,resource};
})(globalThis);
