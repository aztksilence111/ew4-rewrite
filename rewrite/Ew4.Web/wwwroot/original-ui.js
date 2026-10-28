'use strict';
(function(root){
  const mapFamilies=['EW4 Kai','EW4 Serif','EW4 Han','EW4 Han Plane 2','EW4 Han Plane 3','EW4 Unicode','EW4 Unicode Supplement'];
  const families=['EW4 UI Latin','EW4 UI Kai',...mapFamilies];
  const fontFamily=families.map(name=>'"'+name+'"').join(',');
  const mapFontFamily=mapFamilies.map(name=>'"'+name+'"').join(',');
  const combatFamilies=['EW4 Combat',...families];
  const combatFontFamily=combatFamilies.map(name=>'"'+name+'"').join(',');
  const numberFamilies=families;
  const numberFamily=numberFamilies.map(name=>'"'+name+'"').join(',');
  let fontPreparation,glyphRanges,coverage,revision=0,loadQueue=Promise.resolve();
  const faceStates=new Map(),requestedPoints=new Set(),paintTargets=new Set(),candidateLists=new Map();
  const loadedFaces=[];
  const fontStatus={failures:[]};
  const missingGlyphs=new Set();
  function inRanges(point,ranges){
    let low=0,high=ranges.length-1;
    while(low<=high){const middle=(low+high)>>1,[start,end]=ranges[middle];if(point<start)high=middle-1;else if(point>end)low=middle+1;else return true;}
    return false;
  }
  function hasGlyph(point){
    if(!glyphRanges)throw new Error('Call TextRenderer.prepare before querying bundled glyphs');
    return inRanges(point,glyphRanges);
  }
  function displayText(value){
    return [...String(value).normalize('NFC')].map(char=>{
      const point=char.codePointAt(0);
      if(!glyphRanges||hasGlyph(point)||/[\r\n\t]/.test(char)||/\p{Default_Ignorable_Code_Point}/u.test(char))return char;
      if(!missingGlyphs.has(point)){missingGlyphs.add(point);console.warn('Bundled font has no glyph for U+'+point.toString(16).toUpperCase());}
      return '\ufffd';
    }).join('');
  }
  function publishStatus(){
    fontStatus.failures=[...faceStates].filter(([,state])=>state.error).map(([file,state])=>({file,message:state.error}));
    root.dispatchEvent(new Event('ew4fontstatus'));
  }
  function repaintFonts(){
    revision++;
    for(const reference of paintTargets){const target=reference.deref();if(target)target.ew4Repaint();else paintTargets.delete(reference);}
    if(typeof WeakRef!=='function')for(const target of document.querySelectorAll('.original-text>canvas'))target.ew4Repaint?.();
    root.dispatchEvent(new Event('ew4fontsloaded'));
  }
  function loadFace(face){
    if(faceStates.has(face.file))return faceStates.get(face.file).promise;
    const state={loaded:false,error:null};faceStates.set(face.file,state);
    // Serialize decoding so several large font buffers cannot expand together on Android.
    state.promise=loadQueue.then(async()=>{
      let timer;
      try{
        const unicodeRange=face.ranges.map(([a,b])=>'U+'+a.toString(16)+(a===b?'':'-'+b.toString(16))).join(',');
        const font=new FontFace(face.family,'url("/content/fonts/'+face.file+'")',{weight:'400',unicodeRange});
        await Promise.race([font.load(),new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error('载入超时')),12000);})]);
        document.fonts.add(font);state.loaded=true;loadedFaces.push(face);repaintFonts();
        return true;
      }catch(error){state.error=error.message||String(error);console.warn('Bundled font failed: '+face.file+': '+state.error);return false;}
      finally{clearTimeout(timer);publishStatus();}
    });
    loadQueue=state.promise.then(()=>{});
    return state.promise;
  }
  function availableText(value){
    return [...displayText(value)].map(char=>{
      const point=char.codePointAt(0);
      return /[\r\n\t]/.test(char)||/\p{Default_Ignorable_Code_Point}/u.test(char)||loadedFaces.some(face=>inRanges(point,face.ranges))?char:'\ufffd';
    }).join('');
  }
  async function prepareText(value,preferred=families){
    if(!coverage)return;
    const order=preferred.join('|');
    if(!candidateLists.has(order))candidateLists.set(order,preferred.flatMap(family=>coverage.faces.filter(face=>face.family===family)));
    const candidates=candidateLists.get(order);
    const commonFallback=coverage.faces.some(face=>face.bootstrap&&faceStates.get(face.file)?.error);
    const points=[...new Set([...String(value).normalize('NFC')].map(char=>char.codePointAt(0)))];
    points.forEach(point=>requestedPoints.add(point));
    for(let pass=0;pass<families.length;pass++){
      const needed=new Set();
      for(const point of points){
        if(!hasGlyph(point))continue;
        if(commonFallback&&loadedFaces.some(face=>inRanges(point,face.ranges)))continue;
        const best=candidates.find(face=>inRanges(point,face.ranges)&&!faceStates.get(face.file)?.error);
        if(best&&!faceStates.get(best.file)?.loaded)needed.add(best);
      }
      if(!needed.size)return;
      await Promise.all([...needed].map(loadFace));
    }
  }
  async function prepareFonts(){
    if(!fontPreparation)fontPreparation=(async()=>{
      let response;
      try{response=await fetch('/content/fonts/coverage.json?v=20261021.3');}
      catch(error){throw new Error('字库索引载入失败：coverage.json：'+error.message);}
      if(!response.ok)throw new Error('字库索引载入失败：coverage.json ('+response.status+')');
      const data=await response.json();
      if(data.schema!==1||data.loading!=='subsets-v1'||!Array.isArray(data.faces)||!Array.isArray(data.ranges)||
        !data.faces.some(face=>face.bootstrap)||data.faces.some(face=>!combatFamilies.includes(face.family)||!Array.isArray(face.ranges)||!/^parts\/[A-Za-z0-9-]+\.woff2$/.test(face.file)))throw new Error('字库索引无效：coverage.json');
      coverage=data;glyphRanges=data.ranges;
      for(const face of coverage.faces.filter(face=>face.bootstrap)){
        if(await loadFace(face)){document.documentElement.dataset.fontsReady='true';return;}
      }
      throw new Error('内置常用字库载入失败：'+fontStatus.failures.map(face=>face.file).join('、'));
    })().catch(error=>{fontPreparation=null;throw error;});
    return fontPreparation;
  }
  async function retryFonts(){
    for(const [file,state] of faceStates)if(state.error)faceStates.delete(file);
    publishStatus();
    await prepareFonts();
    await prepareText([...requestedPoints].map(point=>String.fromCodePoint(point)).join(''));
    repaintFonts();
  }
  class TextRenderer {
    constructor(manifest){this.fonts=manifest.fonts;this.sheets=new Map();}
    get fontFamily(){return fontFamily;}
    async prepare(){
      await prepareFonts();
      this.measure=document.createElement('canvas').getContext('2d');
    }
    sheet(name){
      if(!this.sheets.has(name)){const image=new Image();image.src='/content/images/'+encodeURIComponent(name)+'.png';this.sheets.set(name,image);}
      return this.sheets.get(name);
    }
    glyph(char,font){
      return null;
    }
    layout(text,font,width=Infinity,options={}){
      const heights={font_text_1_cn_hd:24,font_text_2_cn_hd:24,font_text_3_cn_hd:26,font_text_4_cn_hd:32,font_char_1_hd:18,font_char_2_hd:24,font_char_3_hd:28,font_char_4_hd:42,font_ascii:16,font_city:13,font_city_hd:26,font_city_labels_hd:26};
      let height=options.size?options.size*2:this.fonts[font]?.height||heights[font]||24;
      const lines=[{glyphs:[],width:0}],limit=Number.isFinite(width)?Math.max(1,width*2-8):Infinity,extended=true;
      let value=displayText(text).replace(/\\n/g,'\n');
      const typeface=options.numeric?numberFamily:fontFamily;
      const setFont=()=>{this.measure.font=height+'px '+typeface;this.measure.fontKerning='none';};
      const advance=char=>{const glyph=this.glyph(char,font);return glyph?glyph.record[6]*height/glyph.height:this.measure.measureText(availableText(char)).width;};
      setFont();
      if(options.singleLine){
        value=value.replace(/[\r\n]+/g,' ');
        const natural=[...value].reduce((sum,char)=>sum+advance(char),0);
        const heightLimit=options.maxHeight?Math.max(1,options.maxHeight*2-10)/height:1;
        height*=Math.min(1,natural?limit/natural:1,heightLimit);setFont();
      }
      for(const char of value){
        if(char==='\r')continue;
        if(char==='\n'){lines.push({glyphs:[],width:0});continue;}
        const step=advance(char);
        let line=lines.at(-1);if(!options.singleLine&&line.width&&line.width+step>limit){line={glyphs:[],width:0};lines.push(line);}
        line.glyphs.push({char,x:line.width,glyph:this.glyph(char,font)});line.width+=step;
      }
      const natural=Math.max(1,...lines.map(l=>l.width));
      return {lines,height,extended,width:Number.isFinite(limit)?options.shrinkWidth?Math.min(limit,natural):limit:natural};
    }
    element(text,font='font_text_2_cn_hd',color='#404040',width,align='left',options={}){
      const span=document.createElement('span');span.className='original-text';span.setAttribute('aria-label',String(text));
      const canvas=document.createElement('canvas');canvas.setAttribute('aria-hidden','true');span.append(canvas);
      const padding=4,outlined=font.startsWith('font_city');
      const typeface=options.numeric?numberFamily:fontFamily;
      span.dataset.font=font;span.dataset.typeface=font;
      span.dataset.color=outlined?'#ffffff':color;
      let paintedKey;
      const redraw=()=>{
        const uiScale=parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--ui-scale'))||1;
        const requested=options.rasterScale||Math.max(1,Math.min(6,Math.ceil(uiScale*(root.devicePixelRatio||1))));
        // A newly loaded subset only invalidates text whose resolved font actually changes.
        const preferred=options.numeric?numberFamilies:families;
        const faces=[...new Set([...displayText(text)].map(char=>{
          const point=char.codePointAt(0);
          for(const family of preferred){const face=loadedFaces.find(face=>face.family===family&&inRanges(point,face.ranges));if(face)return face.file;}
          return '';
        }))];
        const key=JSON.stringify([requested,availableText(text),faces,this.fonts[font]?.height,options]);
        if(key===paintedKey)return;
        const layout=this.layout(text,font,width,options);span.dataset.size=String(layout.height/2);
        const inkCenter=options.singleLine&&options.inkCenter&&options.maxHeight;
        const canvasHeight=inkCenter?options.maxHeight*2:layout.lines.length*(layout.height+2)+padding*2;
        const rasterScale=Math.min(requested,Math.sqrt(4*1024*1024/((layout.width+padding*2)*(layout.lines.length*(layout.height+2)+padding*2))));
        canvas.width=Math.ceil((layout.width+padding*2)*rasterScale);canvas.height=Math.ceil(canvasHeight*rasterScale);
        canvas.style.width=(layout.width+padding*2)/2+'px';canvas.style.height=canvasHeight/2+'px';
        canvas.style.setProperty('--ew4-text-height',canvasHeight/2+'px');
        const draw=canvas.getContext('2d');draw.clearRect(0,0,canvas.width,canvas.height);
        draw.scale(rasterScale,rasterScale);
        draw.font=layout.height+'px '+typeface;draw.fontKerning='none';
        const metrics=inkCenter?layout.lines[0].glyphs.map(({char})=>draw.measureText(availableText(char))):[];
        const ascent=Math.max(0,...metrics.map(m=>m.actualBoundingBoxAscent)),descent=Math.max(0,...metrics.map(m=>m.actualBoundingBoxDescent));
        for(const [row,line] of layout.lines.entries()){
          const offset=align==='center'?(layout.width-line.width)/2:align==='right'?layout.width-line.width:0;
          for(const {char,x,glyph} of line.glyphs){
            const y=row*(layout.height+2)+padding;
            if(glyph){
              const [sx,sy,w,h,dx,dy]=glyph.record,scale=layout.height/glyph.height;
              draw.drawImage(glyph.sheet,sx,sy,w,h,padding+offset+x-dx*scale,y-dy*scale,w*scale,h*scale);continue;
            }
            draw.font=layout.height+'px '+typeface;draw.fontKerning='none';draw.textBaseline=inkCenter?'alphabetic':'top';
            const baseline=inkCenter?(canvasHeight-ascent-descent)/2+ascent:y;
            const fallback=availableText(char);
            if(outlined){draw.strokeStyle='#000000';draw.lineWidth=2;draw.lineJoin='round';draw.strokeText(fallback,padding+offset+x,baseline);}
            draw.fillStyle=outlined?'#ffffff':color||'#404040';draw.fillText(fallback,padding+offset+x,baseline);
          }
        }
        if(!outlined){draw.globalCompositeOperation='source-in';draw.fillStyle=color||'#404040';draw.fillRect(0,0,canvas.width/rasterScale,canvas.height/rasterScale);draw.globalCompositeOperation='source-over';}
        paintedKey=key;
      };
      canvas.ew4Repaint=redraw;if(typeof WeakRef==='function')paintTargets.add(new WeakRef(canvas));
      const fallback=[...String(text)].filter(char=>!this.glyph(char,font)).join('');
      redraw();if(fallback)void prepareText(fallback,options.numeric?numberFamilies:families).then(()=>{if(typeof WeakRef!=='function')redraw();});return span;
    }
    replace(element,text,font,color,width,align){element.replaceChildren(this.element(text,font,color,width,align));}
    prose(text,font,color,width){
      const body=document.createElement('span');body.className='original-prose';
      const lines=this.layout(text,font,width).lines;
      // Keep long event prose in small canvases so Android never allocates a page-sized texture.
      for(let i=0;i<lines.length;i+=6){const chunk=lines.slice(i,i+6).map(line=>line.glyphs.map(g=>g.char).join('')).join('\n');
        const span=this.element(chunk,font,color,width);span.style.display='block';span.style.margin='-2px 0';body.append(span);}
      return body;
    }
    forElement(element,text){
      const style=getComputedStyle(element),number=/^[\d.,%+\-/ ]+$/.test(text);
      const singleLine=!!element.closest('button,h1,h2,.commander-name,.native-caption')||element.hasAttribute('data-label');
      const paragraph=!!element.closest('p,#notice,.play-notice');
      const px=value=>parseFloat(value)||0;
      let width=element.clientWidth||px(style.width);
      width-=px(style.paddingLeft)+px(style.paddingRight);
      const notice=element.id==='notice';
      // A toast's DOM text width can be narrower than the native bitmap glyphs.
      if(notice)width=Math.max(1,document.documentElement.clientWidth*.9-px(style.paddingLeft)-px(style.paddingRight)-px(style.borderLeftWidth)-px(style.borderRightWidth));
      if(singleLine){
        // Reserve icon and sibling space before fitting text into a fixed control.
        const siblings=[...element.children].filter(child=>!child.matches('.original-text')&&getComputedStyle(child).position!=='absolute');
        for(const child of siblings){const css=getComputedStyle(child);width-=child.offsetWidth+px(css.marginLeft)+px(css.marginRight);}
        width-=siblings.length*px(style.columnGap);
      }
      const font=element.closest('[data-original-font]')?.dataset.originalFont||(number?'font_char_2_hd':element.closest('h1,h2')?'font_text_4_cn_hd':'font_text_2_cn_hd');
      const bounded=(singleLine||paragraph)&&width>8?width:undefined;
      if(element.matches('.menu-bar>h1,.menu-bar>h2,.load-icon')){
        const height=element.clientHeight-px(style.paddingTop)-px(style.paddingBottom);
        return this.element(text,font,style.color,bounded,'center',{size:element.matches('.load-icon')?15:16,singleLine:true,shrinkWidth:true,inkCenter:true,maxHeight:height||21});
      }
      if(element.matches('.map-mode-tip'))return this.element(text,font,style.color,bounded,'center',{size:px(style.fontSize)||8,singleLine:true,shrinkWidth:true,inkCenter:true,maxHeight:18});
      return this.element(text,font,style.color,bounded,style.textAlign,{size:px(style.fontSize)||12,singleLine,shrinkWidth:singleLine||notice});
    }
    paintTree(rootElement){
      const walker=document.createTreeWalker(rootElement,NodeFilter.SHOW_TEXT),nodes=[];
      // Event prose is rendered in bounded chunks by refreshHud.
      while(walker.nextNode()){const node=walker.currentNode;if(!node.textContent.trim()||node.parentElement.closest('#startup-status,#scenario-event-text,#pause-objectives,#story-player,.original-text,script,style,canvas,select,option,textarea,output,[hidden]')||!node.parentElement.getClientRects().length)continue;nodes.push(node);}
      for(const node of nodes)node.replaceWith(this.forElement(node.parentElement,node.textContent));
    }
  }
  root.Ew4OriginalUI={TextRenderer,fontFamily,mapFontFamily,combatFontFamily,prepareMapText:(text,land=false)=>prepareText(text,land?['EW4 Serif',...mapFamilies]:mapFamilies),prepareCombatText:text=>prepareText(text,combatFamilies),hasGlyph,displayText,availableText,prepareText,retryFonts,fontStatus,get revision(){return revision;}};
  root.addEventListener?.('ew4uiscale',repaintFonts);
  if(typeof module!=='undefined')module.exports=root.Ew4OriginalUI;
})(globalThis);
