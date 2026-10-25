'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs/promises');
const path=require('node:path');
const crypto=require('node:crypto');
const {execFileSync,spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..'),rewrite=path.join(root,'rewrite');
const bundle=path.resolve(process.argv[2]||path.join(rewrite,'local/content-bundle-runtime'));
const url='http://127.0.0.1:'+Number(process.argv[3]||5185);
const folder=path.join(rewrite,'reports/content_bundle_20261019');
const dotnet='C:/Users/WIN11/.codex/tools/dotnet-android/dotnet.exe';
const sha=b=>crypto.createHash('sha256').update(b).digest('hex');
(async()=>{
  await fs.mkdir(folder,{recursive:true});
  const report={checks:0,errors:[],bundle,scope:'portable Web publish with no repository root argument; Android evaluated asset closure only, no APK build'};
  const check=(v,label)=>{assert.ok(v,label);report.checks++;};
  const manifest=JSON.parse(await fs.readFile(path.join(rewrite,'data/runtime-content.json'),'utf8'));
  for(const item of manifest.files){
    const source=await fs.readFile(path.join(rewrite,'data',item.path)),packed=await fs.readFile(path.join(bundle,'data',item.path));
    check(source.length===item.bytes&&sha(source)===item.sha256,'source integrity '+item.path);
    check(packed.equals(source),'published byte identity '+item.path);
  }
  report.runtimeFiles=manifest.files.length;report.runtimeBytes=manifest.files.reduce((n,f)=>n+f.bytes,0);
  const evaluated=JSON.parse(execFileSync(dotnet,['msbuild',path.join(rewrite,'Ew4.Android/Ew4.Android.csproj'),'-getItem:AndroidAsset','-p:Configuration=Release','-verbosity:quiet'],{encoding:'utf8',maxBuffer:64*1024*1024}));
  const assets=evaluated.Items.AndroidAsset,links=new Set();
  for(const asset of assets){
    const relative=path.relative(rewrite,asset.FullPath);
    check(!relative.startsWith('..')&&!path.isAbsolute(relative),'Android source stays inside rewrite '+asset.Identity);
    const link=asset.Link.replace(/\\/g,'/');
    check(!links.has(link),'unique Android package path '+link);links.add(link);
  }
  for(const item of manifest.files){
    const name=/^(original|rewrite)\//.test(item.path)?item.path:item.path.startsWith('client/')?'www/content/'+item.path.slice(7):'data/'+path.basename(item.path);
    check(links.has(name),'Android packages runtime input '+name);
  }
  report.androidAssets=assets.length;report.originalAssets=assets.filter(a=>a.Link.startsWith('original/')).length;
  const profile=await fs.mkdtemp(path.join(folder,'release-profile-'));
  const child=spawn(dotnet,[path.join(bundle,'Ew4.Web.dll'),'--urls',url,'--save-root',profile,'--Logging:LogLevel:Default','Warning'],{cwd:bundle,windowsHide:true,stdio:['ignore','pipe','pipe']});
  let log='';child.stdout.on('data',d=>{log+=d;});child.stderr.on('data',d=>{log+=d;});
  const rpc=async(route,args={})=>{
    const response=await fetch(url+'/api/'+route,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(args)});
    const result=await response.json();assert.ok(result.ok,result.message);return result.data;
  };
  try{
    let catalog;
    for(let i=0;i<80;i++){
      if(child.exitCode!==null)throw Error('standalone server exited: '+log);
      try{catalog=await rpc('catalog');break;}catch(e){if(i===79)throw e;await new Promise(r=>setTimeout(r,100));}
    }
    check(catalog.scenarios.length===95,'portable catalog contains all 95 source scenarios');
    report.starts=[];
    for(const [scenario,country,mode] of [['conquest1','fra',2],['campaign1_01',null,1],['campaign1_02b',null,1],['campaign5_01',null,1],['tutorials1',null,4],['tutorials2',null,4]]){
      const state=await rpc('start',{scenario,country});
      check(state.scenario===scenario&&state.units.length>0,'portable start '+scenario);
      await rpc('save',{slot:4,mode});const loaded=await rpc('load',{slot:4,mode});
      check(loaded.scenario===state.scenario&&loaded.revision===state.revision&&JSON.stringify(loaded.units)===JSON.stringify(state.units),'portable save/load '+scenario);
      report.starts.push({scenario,units:state.units.length,map:state.map});
    }
    for(const file of ['/','/app.js?v=20261019.11','/content/manifest.json','/content/maps/europe/0_0.png','/content/maps/america/0_0.png']){
      const response=await fetch(url+file);check(response.ok,'portable static resource '+file);await response.arrayBuffer();
    }
    report.serverLog=log;
  }finally{
    const exited=new Promise(resolve=>child.once('exit',resolve));child.kill();if(child.exitCode===null)await exited;
    await fs.writeFile(folder+'/standalone-server.log',log);
    await fs.writeFile(folder+'/verification.json',JSON.stringify(report,null,2));
  }
  console.log(JSON.stringify(report));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
