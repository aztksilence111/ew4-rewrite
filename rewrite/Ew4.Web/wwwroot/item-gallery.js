'use strict';
(function(root){
  const $=id=>document.getElementById('item-gallery'+(id?'-'+id:''));
  class Library{
    constructor(api){
      this.api=api;this.entries=[];this.urls=new Map();this.page=0;
      $('open').onclick=()=>this.open().catch(e=>api.notice(e.message));$('close').onclick=()=>this.close();
      for(const id of ['search','filter'])$(id).addEventListener(id==='search'?'input':'change',()=>{this.page=0;this.render();});
      $('prev').onclick=()=>{this.page--;this.render();};$('next').onclick=()=>{this.page++;this.render();};
      $('upload').onclick=()=>{if(this.working)return;if(window.Ew4ModImport?.chooseItemTexture)window.Ew4ModImport.chooseItemTexture();else $('file').click();};
      $('file').onchange=async()=>{const file=$('file').files[0];if(file)await this.readFile(file);$('file').value='';};
      $('cancel').onclick=()=>this.clearCrop();$('save').onclick=()=>this.save();$('restore').onclick=()=>this.mutate({op:'remove',item:this.selected});
      for(const id of ['zoom','x','y'])$(id).oninput=()=>this.draw();
      window.ew4ItemTextureFileSelected=data=>this.readFile(new File([Uint8Array.from(atob(data.base64),c=>c.charCodeAt(0))],data.name||'item',{type:data.type||'application/octet-stream'}));
      window.ew4ItemTextureFileError=message=>this.message(message||'');
      $('').addEventListener('keydown',e=>{
        if(e.key==='Escape'){e.preventDefault();e.stopPropagation();this.close();}
        if(e.key==='Tab'){const controls=[...$('').querySelectorAll('button,input,select')].filter(n=>!n.disabled&&n.getClientRects().length),first=controls[0],last=controls[controls.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}
      });
    }
    async request(args={}){
      const body=JSON.stringify({...args,revision:this.data?.revision??-1,knownVersion:this.data?.identity});let reply;
      if(window.Ew4ModImport?.itemTextureCommand)reply=JSON.parse(window.Ew4ModImport.itemTextureCommand(body));
      else{const response=await fetch('/api/item-textures',{method:'POST',headers:{'Content-Type':'application/json'},body,cache:'no-store'});if(!response.ok)throw Error('物品图库连接失败 ('+response.status+')');reply=await response.json();}
      if(!reply.ok)throw Error(reply.message||reply.error||'物品贴图操作失败');return reply.data;
    }
    url(name){return this.urls.get(name);}
    install(data){
      this.data=data;this.entries=data.entries;this.urls.clear();
      for(const row of this.entries)if(row.texture)this.urls.set(row.id,'/content/'+row.texture.split('/').map(encodeURIComponent).join('/'));
      for(const image of document.querySelectorAll('img[data-item-texture]'))image.src=this.url(image.dataset.itemTexture)||image.dataset.originalItemTexture;
    }
    async refresh(){const data=await this.request({op:'list'});this.install(data);return data;}
    async open(){if(this.working)return;this.returnFocus=document.activeElement;await this.refresh();this.selected=this.entries.some(e=>e.id===this.selected)?this.selected:this.entries[0]?.id;this.clearCrop();this.message('');$('').hidden=false;this.render();$('search').focus();}
    close(){if(this.working)return;this.clearCrop();$('').hidden=true;this.returnFocus?.focus({preventScroll:true});}
    message(text){$('message').textContent=text;}
    render(){
      const query=$('search').value.trim().toLocaleLowerCase(),custom=$('filter').value==='custom';
      const rows=this.entries.filter(e=>(!custom||e.texture)&&(!query||[e.name,e.id,...e.itemIds].join(' ').toLocaleLowerCase().includes(query)));
      const pages=Math.max(1,Math.ceil(rows.length/36));this.page=Math.max(0,Math.min(this.page,pages-1));const grid=$('grid');grid.replaceChildren();
      for(const row of rows.slice(this.page*36,(this.page+1)*36)){
        const tile=document.createElement('button');tile.type='button';tile.className='item-gallery-tile';tile.dataset.item=row.id;tile.setAttribute('aria-pressed',String(row.id===this.selected));
        const image=document.createElement('img');image.alt=row.name;image.loading='lazy';image.src=this.url(row.id)||this.api.original(row.id);
        const name=document.createElement('strong');name.textContent=row.name;const status=document.createElement('small');status.textContent=row.texture?'已上传':'默认贴图';tile.append(image,name,status);
        tile.onclick=()=>{if(this.working)return;this.selected=row.id;this.clearCrop();this.message('');this.render();};grid.append(tile);
      }
      if(!rows.length){const empty=document.createElement('p');empty.textContent='没有符合条件的物品。';grid.append(empty);}
      $('count').textContent=rows.length+' 种 · '+(this.page+1)+' / '+pages;$('prev').disabled=this.page===0;$('next').disabled=this.page>=pages-1;
      const row=this.entries.find(e=>e.id===this.selected);$('editor').hidden=!row;if(!row)return;
      $('name').textContent=row.name;$('preview').src=this.url(row.id)||this.api.original(row.id);$('status').textContent=row.texture?'正在使用上传贴图':'当前游戏或 Mod 的默认贴图';$('restore').disabled=this.working||!row.texture;
    }
    clearCrop(){this.readVersion=(this.readVersion||0)+1;if(this.cropUrl)URL.revokeObjectURL(this.cropUrl);this.cropUrl=null;this.cropImage=null;$('crop').hidden=true;}
    async readFile(file){
      if(this.working||$('').hidden)return;this.clearCrop();const version=this.readVersion,item=this.selected;let url;
      try{
        if(!item)throw Error('请先选择物品。');if(file.size>10*1024*1024)throw Error('图片超过 10 MiB，请压缩后重试。');
        const header=new Uint8Array(await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=()=>reject(Error('无法读取图片'));reader.readAsArrayBuffer(file.slice(0,12));}));
        const png=header[0]===137&&header[1]===80&&header[2]===78&&header[3]===71,jpeg=header[0]===255&&header[1]===216&&header[2]===255,webp=String.fromCharCode(...header.slice(0,4))==='RIFF'&&String.fromCharCode(...header.slice(8,12))==='WEBP';
        if(!png&&!jpeg&&!webp)throw Error('仅支持有效的 PNG、JPG、WebP 图片。');
        url=URL.createObjectURL(file);const image=new Image();image.src=url;await image.decode();
        if(version!==this.readVersion||item!==this.selected||$('').hidden){URL.revokeObjectURL(url);return;}
        if(image.naturalWidth>4096||image.naturalHeight>4096)throw Error('图片最长边不能超过 4096 像素。');
        this.cropUrl=url;this.cropImage=image;this.cropItem=item;for(const [id,value] of [['zoom','1'],['x','0'],['y','0']])$(id).value=value;
        $('crop').hidden=false;this.draw();this.message('调整后点击“保存贴图”应用。');$('crop').scrollIntoView({block:'nearest'});
      }catch(e){if(url)URL.revokeObjectURL(url);if(version===this.readVersion)this.message(e.message);}
    }
    draw(){if(!this.cropImage)return;const canvas=$('canvas'),draw=canvas.getContext('2d'),image=this.cropImage,scale=Math.min(128/image.naturalWidth,128/image.naturalHeight)*Number($('zoom').value),w=image.naturalWidth*scale,h=image.naturalHeight*scale;draw.clearRect(0,0,128,128);draw.drawImage(image,(128-w)/2+Number($('x').value)*1.28,(128-h)/2+Number($('y').value)*1.28,w,h);}
    async save(){if(!this.cropImage||this.cropItem!==this.selected)return;if(await this.mutate({op:'upload',item:this.selected,texture:$('canvas').toDataURL('image/png').split(',')[1]}))this.clearCrop();}
    async mutate(args){
      if(this.working)return false;this.working=true;$('').setAttribute('aria-busy','true');
      const controls=[...$('').querySelectorAll('button,input,select')],disabled=controls.map(n=>n.disabled);controls.forEach(n=>n.disabled=true);
      try{this.install(await this.request(args));this.message(args.op==='remove'?'已恢复当前游戏或 Mod 的默认物品贴图。':'物品贴图已保存。');return true;}
      catch(e){this.message(e.message);await this.refresh().catch(()=>{});return false;}
      finally{this.working=false;$('').setAttribute('aria-busy','false');controls.forEach((n,i)=>n.disabled=disabled[i]);this.render();}
    }
  }
  root.Ew4ItemGallery={Library};
})(globalThis);
