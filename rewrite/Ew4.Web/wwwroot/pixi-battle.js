'use strict';
(function(root){
  // The existing BILE evaluator submits atlas quads through this small draw API.
  // Pixi owns the WebGL context and batches those quads in their original order.
  class Transform {
    constructor(){this.matrix=new DOMMatrix();this.stack=[];this.globalAlpha=1;this.globalCompositeOperation='source-over';this.tint=0xffffff;}
    save(){this.stack.push([this.matrix,this.globalAlpha,this.globalCompositeOperation,this.tint,this.fillStyle,this.strokeStyle,this.lineWidth]);this.matrix=DOMMatrix.fromMatrix(this.matrix);}
    restore(){[this.matrix,this.globalAlpha,this.globalCompositeOperation,this.tint,this.fillStyle,this.strokeStyle,this.lineWidth]=this.stack.pop();}
    setTransform(a,b,c,d,e,f){this.matrix=new DOMMatrix([a,b,c,d,e,f]);}
    resetTransform(){this.matrix=new DOMMatrix();}
    getTransform(){return this.matrix;}
    transform(a,b,c,d,e,f){this.matrix.multiplySelf(new DOMMatrix([a,b,c,d,e,f]));}
    translate(x,y){this.matrix.translateSelf(x,y);}
    scale(x,y=x){this.matrix.scaleSelf(x,y);}
    rotate(angle){this.matrix.rotateSelf(angle*180/Math.PI);}
  }
  class Pose extends Transform {
    constructor(){super();this.commands=[];}
    drawImage(...args){
      const [source,...rect]=args,width=source.naturalWidth||source.width,height=source.naturalHeight||source.height;
      let sx=0,sy=0,sw=width,sh=height,dx,dy,dw=width,dh=height;
      if(rect.length===8)[sx,sy,sw,sh,dx,dy,dw,dh]=rect;else if(rect.length===4)[dx,dy,dw,dh]=rect;else [dx,dy]=rect;
      if(!(sw>0&&sh>0&&dw&&dh))return;
      const m=this.matrix;
      this.commands.push({args,matrix:DOMMatrix.fromMatrix(m),alpha:this.globalAlpha,source,sx,sy,sw,sh,
        a:m.a*dw/sw,b:m.b*dw/sw,c:m.c*dh/sh,d:m.d*dh/sh,e:m.a*dx+m.c*dy+m.e,f:m.b*dx+m.d*dy+m.f});
    }
    replay(draw){
      if(draw.drawPose){draw.drawPose(this.commands);return;}
      for(const c of this.commands){draw.save();draw.matrix.multiplySelf(c.matrix);draw.globalAlpha*=c.alpha;draw.drawImage(...c.args);draw.restore();}
    }
  }
  class Context extends Transform {
    constructor(canvas,onRestore){
      super();this.canvas=canvas;this.gpu=true;this.stage=new PIXI.Container();this.pool=[];this.sources=new Map();this.sourcePixels=0;this.serial=0;this.quadMatrix=new PIXI.Matrix();
      this.renderer=new PIXI.Renderer({view:canvas,width:1,height:1,resolution:1,backgroundColor:0x688e91,antialias:false,powerPreference:'high-performance'});
      this.stats={frames:0,quads:0,uploads:0,evictions:0,contextLosses:0,contextRestores:0};
      canvas.addEventListener('webglcontextlost',event=>{event.preventDefault();this.lost=true;this.stats.contextLosses++;});
      canvas.addEventListener('webglcontextrestored',()=>{this.lost=false;this.stats.contextRestores++;onRestore?.();});
    }
    begin(width,height){
      if(this.lost||this.renderer.gl.isContextLost())return false;
      if(this.renderer.width!==width||this.renderer.height!==height)this.renderer.resize(width,height);
      this.cursor=0;this.lastObject=null;this.serial++;this.stack.length=0;this.resetTransform();this.globalAlpha=1;this.tint=0xffffff;this.globalCompositeOperation='source-over';
      return true;
    }
    object(kind){
      let slot=this.pool[this.cursor];if(!slot){slot={};this.pool[this.cursor]=slot;}
      let object=slot[kind];
      if(!object){object=kind==='sprite'?new PIXI.Sprite():kind==='pattern'?new PIXI.TilingSprite(PIXI.Texture.EMPTY,1,1):kind==='layer'?new PIXI.Container():new PIXI.Graphics();slot[kind]=object;}
      if(object.parent!==this.stage){
        if(slot.active?.parent===this.stage)this.stage.addChildAt(object,this.stage.getChildIndex(slot.active));else this.stage.addChild(object);
      }
      if(slot.active&&slot.active!==object)slot.active.visible=false;
      slot.active=object;this.lastObject=object;
      object.visible=true;this.cursor++;object.alpha=this.globalAlpha;object.blendMode=this.globalCompositeOperation==='lighter'?PIXI.BLEND_MODES.ADD:PIXI.BLEND_MODES.NORMAL;
      return object;
    }
    texture(source,x,y,w,h){
      this.captureSources?.add(source);
      let entry=this.sources.get(source);
      if(!entry){const base=new PIXI.BaseTexture(source,{scaleMode:PIXI.SCALE_MODES.LINEAR,mipmap:PIXI.MIPMAP_MODES.OFF});entry={base,frames:new Map(),pixels:base.width*base.height};this.sources.set(source,entry);this.sourcePixels+=entry.pixels;this.stats.uploads++;}
      entry.used=this.serial;
      const key=[x,y,w,h].join(',');let texture=entry.frames.get(key);
      if(!texture){texture=new PIXI.Texture(entry.base,new PIXI.Rectangle(x,y,w,h));entry.frames.set(key,texture);}
      return texture;
    }
    drawImage(source,...args){
      const width=source.naturalWidth||source.width,height=source.naturalHeight||source.height;if(!width||!height)return;
      let sx=0,sy=0,sw=width,sh=height,dx,dy,dw=width,dh=height;
      if(args.length===8)[sx,sy,sw,sh,dx,dy,dw,dh]=args;else if(args.length===4)[dx,dy,dw,dh]=args;else [dx,dy]=args;
      if(!(sw>0&&sh>0&&dw&&dh))return;
      const s=this.object('sprite'),m=this.matrix;
      s.texture=this.texture(source,sx,sy,sw,sh);s.tint=this.tint;
      s.transform.setFromMatrix(this.quadMatrix.set(m.a*dw/sw,m.b*dw/sw,m.c*dh/sh,m.d*dh/sh,m.a*dx+m.c*dy+m.e,m.b*dx+m.d*dy+m.f));
      this.stats.quads++;
    }
    drawPattern(source,scale,rect){
      const s=this.object('pattern'),m=this.matrix;
      s.texture=this.texture(source,0,0,source.naturalWidth||source.width,source.naturalHeight||source.height);
      s.width=rect.width;s.height=rect.height;s.tileScale.set(scale);s.tilePosition.set(-rect.left,-rect.top);
      s.transform.setFromMatrix(this.quadMatrix.set(m.a,m.b,m.c,m.d,m.a*rect.left+m.c*rect.top+m.e,m.b*rect.left+m.d*rect.top+m.f));
      this.stats.quads++;
    }
    cachedLayer(signature,rect,paint){
      const layer=this.object('layer'),m=this.matrix;
      layer.transform.setFromMatrix(this.quadMatrix.set(m.a,m.b,m.c,m.d,m.e,m.f));
      if(layer.signature!==signature||layer.rect!==rect){
        const keys=['stage','pool','cursor','lastObject','matrix','stack','globalAlpha','globalCompositeOperation','tint','fillStyle','strokeStyle','lineWidth','captureSources'];
        const saved=keys.map(key=>this[key]);
        this.stage=layer;this.pool=layer.drawPool||(layer.drawPool=[]);this.cursor=0;this.lastObject=null;
        this.matrix=new DOMMatrix();this.stack=[];this.globalAlpha=1;this.globalCompositeOperation='source-over';this.tint=0xffffff;this.captureSources=new Set();
        try{
          paint();const end=this.lastObject?layer.getChildIndex(this.lastObject)+1:0;
          if(layer.children.length>end)layer.removeChildren(end);
          layer.sources=this.captureSources;layer.signature=signature;layer.rect=rect;
        }finally{keys.forEach((key,i)=>{this[key]=saved[i];});}
      }
      for(const source of layer.sources||[]){const entry=this.sources.get(source);if(entry)entry.used=this.serial;}
    }
    drawPose(commands){
      const m=this.matrix,alpha=this.globalAlpha;
      for(const c of commands){
        const s=this.object('sprite');s.alpha=alpha*c.alpha;s.tint=this.tint;
        s.texture=this.texture(c.source,c.sx,c.sy,c.sw,c.sh);
        s.transform.setFromMatrix(this.quadMatrix.set(m.a*c.a+m.c*c.b,m.b*c.a+m.d*c.b,
          m.a*c.c+m.c*c.d,m.b*c.c+m.d*c.d,m.a*c.e+m.c*c.f+m.e,m.b*c.e+m.d*c.f+m.f));
        this.stats.quads++;
      }
    }
    beginPath(){this.path=[];}
    moveTo(x,y){this.path.push(['moveTo',x,y]);}
    lineTo(x,y){this.path.push(['lineTo',x,y]);}
    closePath(){this.path.push(['closePath']);}
    arc(x,y,r,start,end,anticlockwise=false){this.path.push(['arc',x,y,r,start,end,anticlockwise]);}
    paint(stroke){
      const g=this.object('graphics'),m=this.matrix;g.transform.setFromMatrix(this.quadMatrix.set(m.a,m.b,m.c,m.d,m.e,m.f));
      const key=JSON.stringify([stroke,this.strokeStyle,this.fillStyle,this.lineWidth,this.path]);
      if(g.pathKey===key)return;g.pathKey=key;g.clear();
      const color=new PIXI.Color(stroke?this.strokeStyle:this.fillStyle);
      if(stroke)g.lineStyle(this.lineWidth||1,color.toNumber(),color.alpha);else g.beginFill(color.toNumber(),color.alpha);
      for(const [method,...args] of this.path)g[method](...args);
      if(!stroke)g.endFill();
    }
    fill(){this.paint(false);}
    stroke(){this.paint(true);}
    fillRect(x,y,w,h){this.beginPath();this.moveTo(x,y);this.lineTo(x+w,y);this.lineTo(x+w,y+h);this.lineTo(x,y+h);this.closePath();this.fill();}
    end(){
      const end=this.lastObject?this.stage.getChildIndex(this.lastObject)+1:0;
      if(this.stage.children.length>end)this.stage.removeChildren(end);
      if(!this.lost)this.renderer.render(this.stage);
      this.stats.frames++;
      // Evict only sources unused by this frame; atlas and text textures share a budget.
      if(this.sourcePixels>48*1024*1024)for(const [source,entry] of this.sources){if(entry.used===this.serial)continue;this.sources.delete(source);this.sourcePixels-=entry.pixels;for(const t of entry.frames.values())t.destroy();entry.base.destroy();this.stats.evictions++;if(this.sourcePixels<=40*1024*1024)break;}
    }
    release(source){const e=this.sources.get(source);if(!e)return;this.sources.delete(source);this.sourcePixels-=e.pixels;for(const t of e.frames.values())t.destroy();e.base.destroy();}
  }
  function canvasContext(canvas){
    const draw=canvas.getContext('2d');
    if(!draw)throw Error('Canvas 2D unavailable');
    draw.gpu=false;
    draw.begin=(width,height)=>{
      if(canvas.width!==width||canvas.height!==height){canvas.width=width;canvas.height=height;}
      draw.setTransform(1,0,0,1,0,0);draw.globalAlpha=1;draw.globalCompositeOperation='source-over';return true;
    };
    draw.end=()=>{};
    return draw;
  }
  root.Ew4Pixi={Context,Pose,createContext(canvas,onRestore){
    const forceCanvas=new URLSearchParams(location.search).get('renderer')==='canvas';
    if(!forceCanvas&&root.PIXI&&root.DOMMatrix){
      // Probe on a separate surface: a WebGL canvas cannot acquire a 2D context.
      const probe=document.createElement('canvas'),gl=probe.getContext('webgl2')||probe.getContext('webgl');
      if(gl){gl.getExtension('WEBGL_lose_context')?.loseContext();try{return new Context(canvas,onRestore);}catch(error){
        const fresh=canvas.cloneNode(false);canvas.replaceWith(fresh);canvas=fresh;
      }}
    }
    return canvasContext(canvas);
  }};
})(globalThis);
