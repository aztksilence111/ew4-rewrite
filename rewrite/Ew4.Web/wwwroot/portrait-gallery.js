'use strict';
(function(root){
  const $=id=>document.getElementById(id);
  const requirements='支持 PNG、JPG、WebP；单张不超过 10 MiB，最长边不超过 4096 像素。建议至少 512×512，可在下方裁剪。请使用有权使用的合法图片。';
  class Library{
    constructor(api){
      this.api=api;this.entries=[];this.byImage=new Map();this.bitmaps=new Map();this.page=0;
      $('portrait-gallery-open').onclick=()=>this.open().catch(e=>api.notice(e.message));
      $('portrait-gallery-close').onclick=()=>this.close();
      for(const id of ['portrait-search','portrait-country','portrait-rating','portrait-filter','portrait-sort'])$(id).addEventListener(id==='portrait-search'?'input':'change',()=>{this.page=0;this.render();});
      $('portrait-prev').onclick=()=>{this.page--;this.render();};$('portrait-next').onclick=()=>{this.page++;this.render();};
      $('portrait-upload').onclick=()=>{if(window.Ew4ModImport?.choosePortrait)window.Ew4ModImport.choosePortrait();else $('portrait-file').click();};
      $('portrait-file').onchange=async()=>{const file=$('portrait-file').files[0];if(file)await this.readFile(file);$('portrait-file').value='';};
      $('portrait-crop-cancel').onclick=()=>this.clearCrop();
      $('portrait-crop-save').onclick=()=>this.saveCrop();
      for(const id of ['portrait-zoom','portrait-x','portrait-y'])$(id).oninput=()=>this.drawCrop();
      window.ew4PortraitFileSelected=data=>this.readFile(this.dataFile(data));
      window.ew4PortraitFileError=message=>this.message(message||'');
      $('portrait-requirements').textContent=requirements;
      $('portrait-upload').title=requirements;
      $('portrait-gallery').addEventListener('keydown',e=>{
        if(e.key==='Escape'){e.preventDefault();e.stopPropagation();this.close();}
        if(e.key==='Tab'){const controls=[...$('portrait-gallery').querySelectorAll('button,input,select')].filter(n=>!n.disabled&&n.getClientRects().length);const first=controls[0],last=controls[controls.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}
      });
    }
    async request(args={}){
      const body=JSON.stringify({...args,revision:this.data?.revision??-1,knownVersion:this.data?.identity});
      let reply;
      if(window.Ew4ModImport?.portraitCommand)reply=JSON.parse(window.Ew4ModImport.portraitCommand(body));
      else{const response=await fetch('/api/portraits',{method:'POST',headers:{'Content-Type':'application/json'},body,cache:'no-store'});if(!response.ok)throw Error('头像图库连接失败 ('+response.status+')');reply=await response.json();}
      if(!reply.ok)throw Error(reply.message||reply.error||'头像操作失败');
      return reply.data;
    }
    source(path){return path?.startsWith('rewrite/')?'/content/extensions/'+path.slice(8).split('/').map(encodeURIComponent).join('/') : path?'/content/'+path:null;}
    url(name){return this.byImage.get(name)?.portraitUrl;}
    marker(name){
      const row=this.byImage.get(name);if(!row?.markerUrl)return null;
      if(!this.bitmaps.has(row.markerUrl)){
        const img=new Image();img.onload=()=>this.api.changed();img.onerror=()=>{img.failed=true;this.api.changed();};img.src=row.markerUrl;this.bitmaps.set(row.markerUrl,img);
      }
      const image=this.bitmaps.get(row.markerUrl);return image.complete&&image.naturalWidth?image:null;
    }
    async refresh(){
      const version=(this.refreshVersion||0)+1;this.refreshVersion=version;
      const data=await this.request({op:'list'});const signature=JSON.stringify(data.entries.map(e=>[e.id,e.portrait,e.marker]));
      if(this.refreshVersion!==version)return data;
      this.data=data;this.entries=data.entries;
      if(this.signature!==signature){
        this.signature=signature;this.byImage.clear();this.bitmaps.clear();
        for(const e of this.entries){e.portraitUrl=this.source(e.portrait);e.markerUrl=this.source(e.marker);if(e.portraitUrl)this.byImage.set(e.image,e);}
        // Replace existing DOM portraits too, including a detail window behind this panel.
        for(const image of document.querySelectorAll('img[data-commander-portrait]')){
          const name=image.dataset.commanderPortrait;image.src=this.url(name)||image.dataset.originalPortrait;
        }
        this.api.changed();
      }else for(const e of this.entries){e.portraitUrl=this.source(e.portrait);e.markerUrl=this.source(e.marker);}
      return data;
    }
    schedule(){
      clearTimeout(this.timer);this.timer=setTimeout(()=>this.refresh().catch(e=>this.api.notice('头像更新失败：'+e.message)),150);
    }
    async open(commander=null){
      this.returnFocus=document.activeElement;this.onlyCommander=commander;await this.refresh();
      this.selected=commander||this.selected||this.entries[0]?.id;
      const country=$('portrait-country'),previous=country.value;country.replaceChildren(new Option('全部阵营',''));
      const countries=[...new Set(this.entries.map(e=>e.country))].sort();
      for(const id of countries)country.add(new Option(this.api.countryName(id),id));country.value=previous;
      $('portrait-gallery-title').textContent=commander?'将领皮肤':'将领头像图库';
      $('portrait-gallery').hidden=false;$('portrait-gallery').classList.toggle('single-commander',!!commander);
      this.clearCrop();this.message('');this.render();$(commander?'portrait-gallery-close':'portrait-search').focus();
    }
    close(){if(this.working)return;this.clearCrop();$('portrait-gallery').hidden=true;this.returnFocus?.focus({preventScroll:true});}
    message(text){$('portrait-message').textContent=text;}
    filtered(){
      const query=$('portrait-search').value.trim().toLocaleLowerCase(),country=$('portrait-country').value,rating=$('portrait-rating').value,filter=$('portrait-filter').value;
      const rows=this.entries.filter(e=>(!this.onlyCommander||e.id===this.onlyCommander)&&(!query||[e.name,e.id,e.legacyId,this.api.countryName(e.country)].join(' ').toLocaleLowerCase().includes(query))
        &&(!country||e.country===country)&&(!rating||String(Math.max(1,Math.min(3,Math.floor((e.rating-1)/3)+1)))===rating)&&(!filter||filter==='owned'&&e.owned||filter==='custom'&&e.custom||filter==='skins'&&e.skins.length));
      const sort=$('portrait-sort').value,compare=(a,b)=>a.name.localeCompare(b.name,'zh-CN',{numeric:true})||a.legacyId-b.legacyId;
      return rows.sort((a,b)=>sort==='rating'?b.rating-a.rating||compare(a,b):sort==='owned'?Number(b.owned)-Number(a.owned)||compare(a,b):sort==='country'?a.country.localeCompare(b.country)||compare(a,b):compare(a,b));
    }
    image(row,original=false){
      const image=document.createElement('img');image.alt=row.name;image.loading='lazy';image.src=!original&&row.portraitUrl||this.api.original(row.image);return image;
    }
    render(){
      const rows=this.filtered(),perPage=36,total=Math.max(1,Math.ceil(rows.length/perPage));this.page=Math.max(0,Math.min(this.page,total-1));
      const list=$('portrait-grid');list.replaceChildren();
      for(const row of rows.slice(this.page*perPage,(this.page+1)*perPage)){
        const button=document.createElement('button');button.type='button';button.className='portrait-tile';button.dataset.commander=row.id;button.setAttribute('aria-pressed',String(row.id===this.selected));
        const name=document.createElement('strong');name.textContent=row.name;const info=document.createElement('span');info.textContent=this.api.countryName(row.country)+' · '+Math.max(1,Math.min(3,Math.floor((row.rating-1)/3)+1))+'星区';
        const status=document.createElement('small');status.textContent=[row.owned?'已拥有':'未拥有',row.custom?'已上传':row.portrait?'Mod皮肤':'原始头像'].join(' · ');
        button.append(this.image(row),name,info,status);button.onclick=()=>{this.selected=row.id;this.clearCrop();this.message('');this.render();};list.append(button);
      }
      if(!rows.length){const empty=document.createElement('p');empty.className='portrait-empty';empty.textContent='没有符合条件的将领，请调整搜索或筛选。';list.append(empty);}
      $('portrait-count').textContent=rows.length+' 位将领 · '+(this.page+1)+' / '+total;$('portrait-prev').disabled=this.page===0;$('portrait-next').disabled=this.page>=total-1;
      this.renderSelected();
    }
    renderSelected(){
      const row=this.entries.find(e=>e.id===this.selected);$('portrait-editor').hidden=!row;if(!row)return;
      $('portrait-selected-name').textContent=row.name;$('portrait-selected-image').replaceChildren(this.image(row));
      const choices=$('portrait-choices');choices.replaceChildren();
      const choose=(label,value,active,disabled=false,description='',imagePath=null)=>{
        const button=document.createElement('button');button.type='button';button.className='portrait-choice';button.setAttribute('aria-pressed',String(active));button.disabled=disabled;
        if(imagePath){const image=document.createElement('img');image.src=this.source(imagePath);image.alt='';button.append(image);}
        const text=document.createElement('span');text.textContent=label;button.append(text);if(description){const small=document.createElement('small');small.textContent=description;button.append(small);}
        button.onclick=()=>this.mutate({op:'select',commander:row.id,offer:value});choices.append(button);
      };
      choose('原始头像','original',row.selected==='');
      if(row.skins.length)choose('跟随剧情自动切换','automatic',row.selected==null,false,'按已解锁皮肤的优先级切换');
      if(row.custom)choose('我的上传','custom',row.selected==='custom');
      for(const skin of row.skins)choose(skin.name,skin.id,row.selected===skin.id,!skin.unlocked,skin.unlocked?'已解锁':skin.unlockDescription,skin.image);
      $('portrait-remove').hidden=!row.custom;$('portrait-remove').onclick=()=>this.mutate({op:'remove',commander:row.id});
    }
    async mutate(args){
      if(this.working)return;this.working=true;this.setBusy(true);
      try{await this.request(args);await this.refresh();this.render();this.api.updated?.();this.message('头像已保存，局内小头像已同步。');return true;}
      catch(e){this.message(e.message);await this.refresh().catch(()=>{});return false;}
      finally{this.working=false;this.setBusy(false);}
    }
    setBusy(busy){$('portrait-gallery').classList.toggle('portrait-working',busy);$('portrait-gallery').setAttribute('aria-busy',String(busy));}
    dataFile(data){const bytes=Uint8Array.from(atob(data.base64),c=>c.charCodeAt(0));return new File([bytes],data.name||'portrait',{type:data.type||'application/octet-stream'});}
    async readFile(file){
      if(this.working)return;
      this.clearCrop();const version=this.readVersion,commander=this.selected;
      try{
        if(!this.entries.some(e=>e.id===this.selected))throw Error('请先选择将领。');
        if(file.size>10*1024*1024)throw Error('图片超过 10 MiB，请压缩后重试。');
        const header=new Uint8Array(await new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(reader.result);reader.onerror=()=>reject(Error('无法读取图片'));reader.readAsArrayBuffer(file.slice(0,12));}));
        const png=header[0]===137&&header[1]===80&&header[2]===78&&header[3]===71,jpeg=header[0]===255&&header[1]===216&&header[2]===255,
          webp=String.fromCharCode(...header.slice(0,4))==='RIFF'&&String.fromCharCode(...header.slice(8,12))==='WEBP';
        if(!png&&!jpeg&&!webp)throw Error('仅支持有效的 PNG、JPG、WebP 图片。');
        const url=URL.createObjectURL(file),image=new Image();
        try{image.src=url;await image.decode();}catch(e){URL.revokeObjectURL(url);throw Error('图片无法读取，请换一张有效图片。');}
        if(version!==this.readVersion||commander!==this.selected||$('portrait-gallery').hidden){URL.revokeObjectURL(url);return;}
        if(image.naturalWidth>4096||image.naturalHeight>4096){URL.revokeObjectURL(url);throw Error('图片最长边不能超过 4096 像素。');}
        this.cropImage=image;this.cropUrl=url;this.cropCommander=this.selected;
        $('portrait-zoom').value='1';$('portrait-x').value='0';$('portrait-y').value='0';$('portrait-crop').hidden=false;
        this.message(image.naturalWidth<512||image.naturalHeight<512?'图片小于建议尺寸，放大后可能模糊；仍可保存。':'拖动滑块调整取景，两个预览会同步更新。');this.drawCrop();
        $('portrait-crop').scrollIntoView({block:'nearest'});
      }catch(e){this.message(e.message);}
    }
    crop(canvas,size){
      canvas.width=canvas.height=size;const draw=canvas.getContext('2d'),image=this.cropImage;
      const edge=Math.min(image.naturalWidth,image.naturalHeight)/Number($('portrait-zoom').value),
        x=(image.naturalWidth-edge)*(Number($('portrait-x').value)+100)/200,y=(image.naturalHeight-edge)*(Number($('portrait-y').value)+100)/200;
      draw.clearRect(0,0,size,size);draw.drawImage(image,x,y,edge,edge,0,0,size,size);
    }
    drawCrop(){if(!this.cropImage)return;this.crop($('portrait-crop-preview'),384);this.crop($('portrait-marker-preview'),128);}
    clearCrop(){this.readVersion=(this.readVersion||0)+1;if(this.cropUrl)URL.revokeObjectURL(this.cropUrl);this.cropUrl=null;this.cropImage=null;$('portrait-crop').hidden=true;}
    async saveCrop(){
      if(!this.cropImage||this.cropCommander!==this.selected)return;
      if(await this.mutate({op:'upload',commander:this.selected,portrait:$('portrait-crop-preview').toDataURL('image/png').split(',')[1],marker:$('portrait-marker-preview').toDataURL('image/png').split(',')[1]}))this.clearCrop();
    }
  }
  root.Ew4PortraitGallery={Library};
})(globalThis);
