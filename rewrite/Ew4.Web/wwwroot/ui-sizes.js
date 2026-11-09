'use strict';
(function(root){
  const definitions=[
    ['menu','菜单与选国界面',60,140],['dialogs','信息与操作弹窗',60,180],
    ['buttons','战场角落按钮',60,150],['actions','战场操作栏',60,150],
    ['info','资源与部队信息',60,150],['dialogue','对白与教程',60,180]
  ];
  function normalize(values){
    return Object.fromEntries(definitions.map(([key,,min,max])=>[key,
      Number.isFinite(values?.[key])?Math.round(Math.max(min,Math.min(max,values[key]))):100]));
  }
  function mount({settings,apply,persist}){
    if(root.Ew4ModImport)return;
    settings.uiSizes=normalize(settings.uiSizes);
    const content=document.getElementById('settings-content'),entry=document.createElement('div');
    entry.id='ui-sizes-row';entry.className='setting-row';
    const title=document.createElement('span');title.textContent='界面尺寸';
    const open=document.createElement('button');open.id='ui-sizes-open';open.type='button';open.className='native-command blue';open.textContent='调整';
    entry.append(title,open);content.insertBefore(entry,content.querySelector('.settings-audio'));
    for(const selector of ['.setting-row.screen','.setting-row.event-size'])content.querySelector(selector).hidden=true;
    const panel=document.createElement('dialog');panel.id='ui-sizes-panel';panel.setAttribute('aria-labelledby','ui-sizes-title');
    const heading=document.createElement('h2');heading.id='ui-sizes-title';heading.textContent='界面尺寸';
    const description=document.createElement('p');description.textContent='拖动后即时预览，保存后在此浏览器生效。界面超出可用空间时会自动缩小。';
    const rows=document.createElement('div');rows.className='ui-sizes-controls';
    const controls=[];
    for(const [key,label,min,max] of [['screen','整体尺寸',80,100],...definitions,['eventSize','事件弹窗',60,180]]){
      const row=document.createElement('label'),name=document.createElement('span'),output=document.createElement('output'),input=document.createElement('input');
      row.className='ui-size-control';name.textContent=label;input.type='range';input.min=min;input.max=max;input.step=1;input.id='ui-size-'+key;
      input.setAttribute('aria-label',label);output.htmlFor=input.id;
      input.oninput=()=>{if(key==='screen'||key==='eventSize')settings[key]=Number(input.value);else settings.uiSizes[key]=Number(input.value);apply();sync();};
      row.append(name,output,input);rows.append(row);controls.push({key,input,output});
    }
    const footer=document.createElement('div');footer.className='ui-sizes-footer';
    function button(id,text,handler){const el=document.createElement('button');el.id=id;el.type='button';el.textContent=text;el.onclick=handler;footer.append(el);return el;}
    let previous=null,accepted=false;
    function snapshot(){return {screen:settings.screen,eventSize:settings.eventSize,uiSizes:{...settings.uiSizes}};}
    function sync(){for(const {key,input,output} of controls){const value=key==='screen'||key==='eventSize'?settings[key]:settings.uiSizes[key];input.value=value;output.textContent=value+'%';}}
    button('ui-sizes-reset','恢复默认',()=>{settings.uiSizes=normalize();settings.screen=100;settings.eventSize=100;apply();sync();});
    button('ui-sizes-cancel','取消',()=>panel.close());
    button('ui-sizes-save','保存',()=>{if(persist()===false){description.textContent='浏览器未允许保存设定。当前预览仍有效，可取消或允许存储后重试。';return;}accepted=true;panel.close();});
    panel.append(heading,description,rows,footer);document.body.append(panel);
    open.onclick=()=>{previous=snapshot();accepted=false;sync();panel.showModal();controls[0].input.focus();};
    panel.addEventListener('close',()=>{if(!accepted&&previous){Object.assign(settings,previous);apply();}previous=null;open.focus();});
  }
  root.Ew4UiSizes={normalize,mount};
})(globalThis);
