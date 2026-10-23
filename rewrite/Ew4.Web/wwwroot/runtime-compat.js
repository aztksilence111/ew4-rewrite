'use strict';
(function(root){
  const html=document.documentElement;
  const modernMath=CSS.supports('width','min(1px, 2px)');
  let hasSelectors=false;
  try{hasSelectors=CSS.supports('selector(:has(*))');}catch(_){}
  const probe=document.createElement('div');
  probe.style.cssText='position:absolute;visibility:hidden;display:flex;flex-direction:column;row-gap:1px';
  probe.append(document.createElement('i'),document.createElement('i'));document.body.append(probe);
  const flexGap=probe.scrollHeight===1;probe.remove();
  html.classList.toggle('compat-math',!modernMath);
  html.classList.toggle('compat-selectors',!hasSelectors);
  if(modernMath&&hasSelectors&&flexGap)return;
  const margins=new WeakMap();let scheduled=false;
  function update(){
    scheduled=false;
    const visible=id=>{const node=document.getElementById(id);return node&&!node.hidden;};
    html.classList.toggle('compat-main',visible('main-menu'));
    html.classList.toggle('compat-dialogue',visible('play')&&visible('dialogue'));
    html.classList.toggle('compat-event',visible('play')&&visible('scenario-event'));
    html.classList.toggle('compat-paused',visible('play')&&visible('pause'));
    const scale=Number(getComputedStyle(html).getPropertyValue('--ui-scale'))||1;
    html.style.setProperty('--compat-main-width',Math.max(134,190/scale)+'px');
    html.style.setProperty('--compat-main-height',Math.max(33,48/scale)+'px');
    html.style.setProperty('--compat-main-text-scale',Math.max(1,1/scale));
    if(!flexGap)for(const parent of document.body.querySelectorAll('*')){
      if(!parent.children.length||!parent.getClientRects().length)continue;
      const style=getComputedStyle(parent);
      if(style.display!=='flex'&&style.display!=='inline-flex')continue;
      const row=parseFloat(style.rowGap)||0,column=parseFloat(style.columnGap)||0;
      if(!row&&!column)continue;
      const vertical=style.flexDirection.indexOf('column')===0,reverse=style.flexDirection.endsWith('reverse');
      const children=Array.from(parent.children).filter(child=>getComputedStyle(child).position!=='absolute'&&getComputedStyle(child).position!=='fixed'&&child.getClientRects().length);
      for(let i=0;i<children.length;i++){
        const child=children[i];let base=margins.get(child);
        if(!base){const css=getComputedStyle(child);base={top:parseFloat(css.marginTop)||0,left:parseFloat(css.marginLeft)||0,right:parseFloat(css.marginRight)||0,bottom:parseFloat(css.marginBottom)||0};margins.set(child,base);}
        const side=vertical?(reverse?'bottom':'top'):(reverse?'right':'left');
        for(const key of ['top','left','right','bottom'])child.style.setProperty('--compat-margin-'+key,(base[key]+(i&&key===side?(vertical?row:column):0))+'px');
        child.classList.add('compat-flex-child');
      }
    }
  }
  function schedule(){if(!scheduled){scheduled=true;requestAnimationFrame(update);}}
  new MutationObserver(records=>{
    if(records.some(record=>record.type==='childList'||record.attributeName==='hidden'||record.attributeName==='disabled'||record.attributeName==='class'&&record.target.className.indexOf('compat-flex-child')<0))schedule();
  }).observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['hidden','class','disabled']});
  root.addEventListener('resize',schedule);root.addEventListener('ew4uiscale',schedule);schedule();
})(window);
