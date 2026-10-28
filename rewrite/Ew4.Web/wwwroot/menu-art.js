'use strict';
(function(root){
  class MenuArt {
    constructor(api){this.api=api;this.textures=new Map();}
    async texture(file){
      if(this.textures.has(file))return this.textures.get(file);
      const stem=file.replace(/\.(?:pkm|png|pvr)$/i,''),mapped=this.api.manifest.menuTextures?.[file];
      const candidates=mapped?[mapped]:[{file:'images/'+stem+(stem.endsWith('@2x')?'':'@2x')+'.png',ratio:2},{file:'images/'+stem+'.png',ratio:stem.endsWith('@2x')?2:1}];
      for(const candidate of candidates){
        const image=this.api.image(candidate.file);
        try{await image.decode();const texture={...candidate,image};this.textures.set(file,texture);return texture;}catch(error){if(candidate===candidates.at(-1))throw error;}
      }
    }
    async prepare(){
      const layouts=this.api.manifest.layouts;
      const main=document.createElement('canvas');main.width=1136;main.height=640;
      const draw=main.getContext('2d');draw.scale(2,2);
      for(const node of layouts.form_mainmenu.children){
        const a=node.attributes;if(a.type!=='image'||!a.texture||a.id==='image_title')continue;
        const {image,ratio}=await this.texture(a.texture);
        const sx=Number(a.tx||0),sy=Number(a.ty||0),sw=Number(a.tw||image.naturalWidth/ratio),sh=Number(a.th||image.naturalHeight/ratio);
        draw.save();draw.translate(Number(a.x||0),Number(a.y||0));draw.rotate(Number(a.rot||0)*Math.PI/180);
        draw.scale(Number(a.hscale||a.scale||1),Number(a.vscale||a.scale||1));
        draw.drawImage(image,sx*ratio,sy*ratio,sw*ratio,sh*ratio,0,0,sw,sh);draw.restore();
      }
      if(this.api.manifest.uiOverrides?.main){const image=this.api.image(this.api.manifest.uiOverrides.main);await image.decode();draw.clearRect(0,0,568,320);draw.drawImage(image,0,0,568,320);}
      this.main=main.toDataURL();
      document.getElementById('main-menu').style.background='url("'+this.main+'") center/cover no-repeat';
      const title=layouts.form_mainmenu.children.find(n=>n.attributes.id==='image_title')?.attributes;
      if(title)Object.assign(document.querySelector('#main-menu .game-title').style,{left:Number(title.x||0)+'px',top:Number(title.y||0)+'px'});
      this.backgrounds={};
      for(const id of ['form_selcampaign','form_selconquest']){
        const a=layouts[id].attributes,{image,ratio}=await this.texture(a.back);
        const canvas=document.createElement('canvas');canvas.width=1136;canvas.height=640;
        canvas.getContext('2d').drawImage(image,Number(a.tx||0)*ratio,Number(a.ty||0)*ratio,Number(a.tw||image.naturalWidth/ratio)*ratio,Number(a.th||image.naturalHeight/ratio)*ratio,0,0,1136,640);
        if(this.api.manifest.uiOverrides?.[id]){const override=this.api.image(this.api.manifest.uiOverrides[id]);await override.decode();canvas.getContext('2d').drawImage(override,0,0,1136,640);}
        this.backgrounds[id]=canvas.toDataURL();
      }
      this.strategic=await this.texture('tex_campaign_back.pkm');
      // The legacy power-of-two texture has white padding below its 1024 x 720 map.
      this.strategic.height=Math.min(720,this.strategic.image.naturalHeight/this.strategic.ratio);
      if(this.api.manifest.uiOverrides?.strategic){const image=this.api.image(this.api.manifest.uiOverrides.strategic);await image.decode();this.strategic={image,ratio:2};}
      this.apply(2);
    }
    apply(mode){
      const target=document.getElementById('scenario-menu');
      target.style.setProperty('background-image','url("'+this.backgrounds[mode===1?'form_selcampaign':'form_selconquest']+'")','important');
      target.style.backgroundSize=mode===1?'100% 100%':'cover';
    }
    drawStrategic(draw,definition,scale,centerX=205,centerY=128,textureScale=1){
      const {image,ratio}=this.strategic,cx=Number(definition.centerx??410),cy=Number(definition.centery??288);
      const sourceHeight=this.strategic.height||image.naturalHeight/ratio;
      const width=image.naturalWidth/ratio*textureScale,height=sourceHeight*textureScale;
      draw.drawImage(image,0,0,image.naturalWidth,sourceHeight*ratio,centerX-cx*scale,centerY-cy*scale,width*scale,height*scale);
    }
    strategicCamera(definition,scale,textureScale,viewportWidth=438,viewportHeight=320){
      const {image,ratio}=this.strategic,cx=Number(definition.centerx||410),cy=Number(definition.centery||288);
      const width=image.naturalWidth/ratio*textureScale*scale,height=(this.strategic.height||image.naturalHeight/ratio)*textureScale*scale;
      // Native 8e4c0: reserve the 130px sidebar, center on the full-height map, clamp its edges.
      const clamp=(offset,total,viewport)=>total<=viewport?(total-viewport)/2:Math.max(0,Math.min(total-viewport,offset));
      return {x:cx*scale-clamp(cx*scale-viewportWidth/2,width,viewportWidth),y:cy*scale-clamp(cy*scale-viewportHeight/2,height,viewportHeight)};
    }
  }
  function cityName(name,language,names){
    if(language==='zh')return names[name]||name;
    return Object.keys(names).find(key=>names[key]===name)||name;
  }
  root.Ew4MenuArt={MenuArt,cityName};
})(globalThis);
