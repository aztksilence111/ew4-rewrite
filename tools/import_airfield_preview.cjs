'use strict';
const fs=require('node:fs/promises'),path=require('node:path');
(async()=>{
  const base=process.argv[2]||'http://127.0.0.1:57946',home=path.resolve('颓垣饿鸠/art/airfield-v1');
  const fixture=JSON.parse(await fs.readFile(path.join(home,'package.json'),'utf8'));
  const form=new FormData();form.append('file',new Blob([await fs.readFile(fixture.package)]),'airfield.ew4mod');
  let response=await fetch(base+'/api/mod-import',{method:'POST',body:form});
  if(!response.ok)throw Error(await response.text());
  for(let i=0;i<240;i++){
    const status=await (await fetch(base+'/api/mods')).json();
    if(status.import?.status==='failed')throw Error(JSON.stringify(status.import));
    const mod=status.import?.status==='complete'&&status.mods?.find(m=>m.key===status.import.key&&m.version==='0.1.16'&&m.id==='tuiyuan-ejiu-portrait-opening');
    if(mod){
      response=await fetch(base+'/api/mods',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({op:'activate',key:mod.key})});
      const activated=await response.json();if(activated.active!==mod.key)throw Error(JSON.stringify(activated));
      console.log(JSON.stringify({key:mod.key,active:activated.active,base}));return;
    }
    await new Promise(r=>setTimeout(r,1000));
  }
  throw Error('Import timed out');
})().catch(e=>{console.error(e.message);process.exitCode=1;});
