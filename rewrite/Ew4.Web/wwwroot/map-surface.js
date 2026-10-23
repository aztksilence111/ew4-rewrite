'use strict';
(function(root) {
  const corners=[[32,-18],[32,18],[0,36],[-32,18],[-32,-18],[0,-36]];
  const rgba=value=>[value&255,(value>>>8)&255,(value>>>16)&255,(value>>>24)&255];
  function coastTriangles(neighbors) {
    const result=[];
    for(let d=0;d<6;d++){
      const current=neighbors[d];if(!current)continue;
      let left=0,right=0;
      if(current.active&&!current.sea){if(!current.color)continue;left=right=current.color;}
      else {
        const before=neighbors[(d+5)%6],after=neighbors[(d+1)%6];
        if(before?.active&&!before.sea)left=before.color||0;
        if(after?.active&&!after.sea)right=after.color||0;
        if(!left&&!right)continue;
      }
      result.push([[0,0,0,.5,.5],[...corners[d],left,.5,.5],[...corners[(d+1)%6],right,.5,.5]]);
    }
    return result;
  }
  function borderMask(cell,neighbors) {
    return neighbors.reduce((mask,n,d)=>n?.active&&!n.sea&&n.owner!==cell.owner?mask|(1<<d):mask,0);
  }
  const edge=(a,b,x,y)=>(b[0]-a[0])*(y-a[1])-(b[1]-a[1])*(x-a[0]);
  const topLeft=(a,b)=>b[1]<a[1]||(b[1]===a[1]&&b[0]>a[0]);
  function sample(texture,u,v,out=[0,0,0,0]) {
    out.fill(0);
    const x=u*texture.width-.5,y=v*texture.height-.5,ix=Math.floor(x),iy=Math.floor(y),fx=x-ix,fy=y-iy;
    for(let dy=0;dy<2;dy++)for(let dx=0;dx<2;dx++){
      const px=((ix+dx)%texture.width+texture.width)%texture.width,py=((iy+dy)%texture.height+texture.height)%texture.height;
      const index=(py*texture.width+px)*4,weight=(dx?fx:1-fx)*(dy?fy:1-fy);
      for(let c=0;c<4;c++)out[c]+=texture.data[index+c]*weight;
    }
    return out;
  }
  // Rasterize immutable cell templates once. Frames only composite cached bitmaps.
  function rasterize(triangles,texture=null,ratio=2) {
    const width=64*ratio,height=72*ratio,data=new Uint8ClampedArray(width*height*4);
    for(const triangle of triangles){
      let [a,b,c]=triangle.map(v=>[...v]);if(edge(a,b,c[0],c[1])<0)[b,c]=[c,b];
      const area=edge(a,b,c[0],c[1]);if(area<1e-8)continue;
      const colors=[rgba(a[2]),rgba(b[2]),rgba(c[2])];
      const tl0=topLeft(b,c),tl1=topLeft(c,a),tl2=topLeft(a,b),color=[0,0,0,0],texel=[0,0,0,0];
      const x0=Math.max(0,Math.floor((Math.min(a[0],b[0],c[0])+32)*ratio)),x1=Math.min(width,Math.ceil((Math.max(a[0],b[0],c[0])+32)*ratio));
      const y0=Math.max(0,Math.floor((Math.min(a[1],b[1],c[1])+36)*ratio)),y1=Math.min(height,Math.ceil((Math.max(a[1],b[1],c[1])+36)*ratio));
      for(let y=y0;y<y1;y++)for(let x=x0;x<x1;x++){
        const px=(x+.5)/ratio-32,py=(y+.5)/ratio-36,e0=edge(b,c,px,py),e1=edge(c,a,px,py),e2=edge(a,b,px,py);
        if(e0< -1e-7||e1< -1e-7||e2< -1e-7||Math.abs(e0)<1e-7&&!tl0||Math.abs(e1)<1e-7&&!tl1||Math.abs(e2)<1e-7&&!tl2)continue;
        const w0=e0/area,w1=e1/area,w2=e2/area;
        for(let channel=0;channel<4;channel++)color[channel]=colors[0][channel]*w0+colors[1][channel]*w1+colors[2][channel]*w2;
        if(texture){sample(texture,a[3]*w0+b[3]*w1+c[3]*w2,a[4]*w0+b[4]*w1+c[4]*w2,texel);
          for(let channel=0;channel<4;channel++)color[channel]*=texel[channel]/255;
        }
        const index=(y*width+x)*4,alpha=color[3]/255,oldAlpha=data[index+3]/255,outAlpha=alpha+oldAlpha*(1-alpha);
        if(!outAlpha)continue;
        for(let channel=0;channel<3;channel++)data[index+channel]=(color[channel]*alpha+data[index+channel]*oldAlpha*(1-alpha))/outAlpha;
        data[index+3]=outAlpha*255;
      }
    }
    return {width,height,data};
  }
  function bitmap(pixels){const canvas=document.createElement('canvas');canvas.width=pixels.width;canvas.height=pixels.height;
    canvas.getContext('2d').putImageData(new ImageData(pixels.data,pixels.width,pixels.height),0,0);return canvas;}
  class Surface {
    constructor(){this.coasts=new Map();this.borders=new Map();}
    coast(neighbors){const key=neighbors.map(n=>n?`${+n.active},${+n.sea},${n.color||0}`:'-').join(';');
      if(!this.coasts.has(key)){if(this.coasts.size>=512)this.coasts.delete(this.coasts.keys().next().value);
        this.coasts.set(key,bitmap(rasterize(coastTriangles(neighbors))));}
      return this.coasts.get(key);
    }
    border(mask,rules,image){
      if(!image.complete||!image.naturalWidth)return null;
      if(!this.borders.has(mask)){
        if(!this.texture){const canvas=document.createElement('canvas');canvas.width=rules.borderStorage.hdWidth;canvas.height=rules.borderStorage.hdHeight;
          const ctx=canvas.getContext('2d');ctx.drawImage(image,0,0);this.texture=ctx.getImageData(0,0,canvas.width,canvas.height);}
        const triangles=rules.borders[mask].flatMap(q=>[[q[0],q[1],q[2]],[q[0],q[2],q[3]]]);
        this.borders.set(mask,bitmap(rasterize(triangles,this.texture)));
      }
      return this.borders.get(mask);
    }
  }
  root.Ew4MapSurface=Object.freeze({rgba,coastTriangles,borderMask,rasterize,Surface});
})(typeof window!=='undefined'?window:globalThis);
