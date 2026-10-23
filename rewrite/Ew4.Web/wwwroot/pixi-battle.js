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
    drawImage(...args){this.commands.push({args,matrix:DOMMatrix.fromMatrix(this.matrix),alpha:this.globalAlpha});}
    replay(draw){for(const c of this.commands){draw.save();draw.matrix.multiplySelf(c.matrix);draw.globalAlpha*=c.alpha;draw.drawImage(...c.args);draw.restore();}}
  }
  class Context extends Transform {
    constructor(canvas,onRestore){
      super();this.canvas=canvas;this.gpu=true;this.stage=new PIXI.Container();this.pool=[];this.sources=new Map();this.sourcePixels=0;this.serial=0;
      this.renderer=new PIXI.Renderer({view:canvas,width:1,height:1,resolution:1,backgroundColor:0x688e91,antialias:false,powerPreference:'high-performance'});
      this.stats={frames:0,quads:0,uploads:0,evictions:0,contextLosses:0,contextRestores:0};
      canvas.addEventListener('webglcontextlost',event=>{event.preventDefault();this.lost=true;this.stats.contextLosses++;});
      canvas.addEventListener('webglcontextrestored',()=>{this.lost=false;this.stats.contextRestores++;onRestore?.();});
    }
    begin(width,height){
      if(this.lost||this.renderer.gl.isContextLost())return false;
      if(this.renderer.width!==width||this.renderer.height!==height)this.renderer.resize(width,height);
      this.cursor=0;this.serial++;this.stack.length=0;this.resetTransform();this.globalAlpha=1;this.tint=0xffffff;this.globalCompositeOperation='source-over';
      return true;
    }
    object(kind){
      let slot=this.pool[this.cursor];
      if(!slot){slot={sprite:new PIXI.Sprite(),graphics:new PIXI.Graphics()};this.pool[this.cursor]=slot;this.stage.addChild(slot.sprite,slot.graphics);}
      slot.sprite.visible=kind==='sprite';slot.graphics.visible=kind==='graphics';
      const object=slot[kind];
      object.visible=true;this.cursor++;object.alpha=this.globalAlpha;object.blendMode=this.globalCompositeOperation==='lighter'?PIXI.BLEND_MODES.ADD:PIXI.BLEND_MODES.NORMAL;
      return object;
    }
    texture(source,x,y,w,h){
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
      s.transform.setFromMatrix(new PIXI.Matrix(m.a*dw/sw,m.b*dw/sw,m.c*dh/sh,m.d*dh/sh,m.a*dx+m.c*dy+m.e,m.b*dx+m.d*dy+m.f));
      this.stats.quads++;
    }
    beginPath(){this.path=[];}
    moveTo(x,y){this.path.push(['moveTo',x,y]);}
    lineTo(x,y){this.path.push(['lineTo',x,y]);}
    closePath(){this.path.push(['closePath']);}
    arc(x,y,r,start,end,anticlockwise=false){this.path.push(['arc',x,y,r,start,end,anticlockwise]);}
    paint(stroke){
      const g=this.object('graphics'),m=this.matrix;g.transform.setFromMatrix(new PIXI.Matrix(m.a,m.b,m.c,m.d,m.e,m.f));
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
      for(let i=this.cursor;i<this.pool.length;i++){this.pool[i].sprite.visible=false;this.pool[i].graphics.visible=false;}
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
