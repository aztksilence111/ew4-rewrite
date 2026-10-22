'use strict';
const fs=require('node:fs'),https=require('node:https'),cp=require('node:child_process'),path=require('node:path'),crypto=require('node:crypto');
const cwd=path.resolve(__dirname,'..'),repo='aztksilence111/ew4-rewrite';
const [action,buildArg,version,date]=process.argv.slice(2);
if(!['inspect','push','publish','audit'].includes(action)||!buildArg||!version||!date)
  throw Error('Usage: node tools/github_release.cjs inspect|push|publish|audit build-directory version YYYYMMDD');
const build=path.resolve(cwd,buildArg),tag='v'+version+'-'+date;
const notes=path.join(cwd,'rewrite','RELEASE_'+version+'_'+date+'.md');
function git(...args){return cp.execFileSync('git',args,{cwd,maxBuffer:64*1024*1024});}
function releaseFiles(manifest){
  const names=[...manifest.files.map(f=>f.name),'release-manifest.json','SHA256SUMS.txt'];
  if(new Set(names).size!==names.length||names.some(n=>path.basename(n)!==n||!n||n.includes('\\')))
    throw Error('Invalid release asset manifest');
  return names;
}
const credential=cp.execFileSync('git',['credential','fill'],{cwd,input:'protocol=https\nhost=github.com\n\n',encoding:'utf8',
  env:{...process.env,GIT_TERMINAL_PROMPT:'0',GCM_INTERACTIVE:'never'}});
const token=credential.split(/\r?\n/).find(s=>s.startsWith('password='))?.slice(9);
if(!token)throw Error('GitHub credential unavailable');
async function request(route,method='GET',body,upload){
  const url=new URL(route.startsWith('https:')?route:'https://api.github.com'+route);
  if(!['api.github.com','uploads.github.com'].includes(url.hostname))throw Error('Unexpected release host');
  const data=body===undefined?null:Buffer.from(JSON.stringify(body));
  return await new Promise((resolve,reject)=>{
    const req=https.request(url,{method,lookup:(host,opts,cb)=>{
      const address=host==='uploads.github.com'?'20.205.243.161':'20.205.243.168';
      cb(null,opts.all?[{address,family:4}]:address,4);
    },headers:{Authorization:'Bearer '+token,Accept:'application/vnd.github+json','User-Agent':'ew4-release','X-GitHub-Api-Version':'2022-11-28',
      ...(upload?{'Content-Type':'application/octet-stream','Content-Length':fs.statSync(upload).size}:data?{'Content-Type':'application/json','Content-Length':data.length}:{})}},res=>{
      const chunks=[];res.on('data',c=>chunks.push(c));res.on('end',()=>{
        const text=Buffer.concat(chunks).toString();let result;try{result=JSON.parse(text);}catch{result=text;}
        if(res.statusCode<200||res.statusCode>=300)reject(Error(method+' '+url.pathname+' HTTP '+res.statusCode+' '+JSON.stringify(result).slice(0,300)));
        else resolve(result);
      });
    });
    req.setTimeout(60000,()=>req.destroy(Error('GitHub request timeout')));req.on('error',reject);
    if(upload)fs.createReadStream(upload).pipe(req);else req.end(data);
  });
}
async function push(){
  const head=git('rev-parse','HEAD').toString().trim(),parent=git('rev-parse','HEAD^').toString().trim();
  const remote=await request('/repos/'+repo+'/git/ref/heads/main');
  if(remote.object.sha===head){console.log('Source already synchronized '+head);return;}
  if(remote.object.sha!==parent)throw Error('Remote main changed; refusing to overwrite');
  const known=new Set(git('ls-tree','-r',parent).toString().trim().split('\n').map(s=>s.split(/\s+/)[2]));
  const diff=git('diff-tree','--no-commit-id','--name-status','-r','--no-renames',head).toString().trim().split('\n');
  const tree=[],blobs=new Map();
  for(const line of diff){
    const [status,file]=line.split('\t');
    if(status==='D'){tree.push({path:file,mode:'100644',type:'blob',sha:null});continue;}
    const [mode,type,sha]=git('ls-tree',head,'--',file).toString().trim().split(/\s+/);
    if(type!=='blob')throw Error('Unsupported tree object '+file);
    if(!known.has(sha))blobs.set(sha,file);
    tree.push({path:file,mode,type,sha});
  }
  const pending=[...blobs],workers=Array.from({length:4},async()=>{
    while(pending.length){
      const [sha,file]=pending.shift(),bytes=git('cat-file','blob',sha);
      const created=await request('/repos/'+repo+'/git/blobs','POST',{content:bytes.toString('base64'),encoding:'base64'});
      if(created.sha!==sha)throw Error('Blob identity mismatch '+file);
    }
  });
  await Promise.all(workers);
  const baseTree=git('rev-parse',parent+'^{tree}').toString().trim();
  const createdTree=await request('/repos/'+repo+'/git/trees','POST',{base_tree:baseTree,tree});
  if(createdTree.sha!==git('rev-parse','HEAD^{tree}').toString().trim())throw Error('Source tree mismatch');
  const commit=git('cat-file','commit',head).toString(),split=commit.indexOf('\n\n');
  function person(kind){
    const match=commit.slice(0,split).split('\n').find(s=>s.startsWith(kind+' ')).match(/^\w+ (.*) <(.*)> (\d+) ([+-]\d+)$/);
    if(match[4]!=='+0000')throw Error('Use UTC commit identity for exact REST publication');
    return {name:match[1],email:match[2],date:new Date(Number(match[3])*1000).toISOString()};
  }
  const created=await request('/repos/'+repo+'/git/commits','POST',{message:commit.slice(split+2),tree:createdTree.sha,parents:[parent],author:person('author'),committer:person('committer')});
  if(created.sha!==head)throw Error('Commit identity mismatch');
  await request('/repos/'+repo+'/git/refs/heads/main','PATCH',{sha:head,force:false});
  git('update-ref','refs/remotes/origin/main',head);
  console.log('Source synchronized '+head+'; changed paths '+tree.length);
}
async function publish(){
  const head=git('rev-parse','HEAD').toString().trim();
  const remote=await request('/repos/'+repo+'/git/ref/heads/main');
  if(remote.object.sha!==head)throw Error('Source must be synchronized first');
  const manifest=JSON.parse(fs.readFileSync(path.join(build,'assets/release-manifest.json')));
  if(manifest.source!==head)throw Error('Package source mismatch');
  const list=await request('/repos/'+repo+'/releases?per_page=100');
  let release=list.find(r=>r.tag_name===tag);
  if(!release)release=await request('/repos/'+repo+'/releases','POST',{tag_name:tag,target_commitish:head,
    name:version+' - '+(process.env.EW4_RELEASE_TITLE||'Runtime and gameplay updates'),body:fs.readFileSync(notes,'utf8'),draft:true,prerelease:false});
  const files=releaseFiles(manifest);
  const progress={source:head,tag,releaseId:release.id,draft:release.draft,verified:[],pending:files};
  const saveProgress=()=>fs.writeFileSync(path.join(build,'release-upload-progress.json'),JSON.stringify({...progress,updatedAt:new Date().toISOString()},null,2));
  saveProgress();
  for(const name of files){
    const file=path.join(build,'assets',name),sha=crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
    const old=release.assets.find(a=>a.name===name);
    if(old){
      if(old.digest!=='sha256:'+sha||old.size!==fs.statSync(file).size)throw Error('Existing release asset differs: '+name);
      progress.verified.push(name);progress.pending=progress.pending.filter(x=>x!==name);saveProgress();continue;
    }
    console.log('Uploading '+name+' '+fs.statSync(file).size+' bytes to '+(release.draft?'draft':'existing release'));
    const asset=await request(release.upload_url.split('{')[0]+'?name='+encodeURIComponent(name),'POST',undefined,file);
    if(asset.size!==fs.statSync(file).size||asset.digest!=='sha256:'+sha)throw Error('Uploaded asset mismatch '+name);
    progress.verified.push(name);progress.pending=progress.pending.filter(x=>x!==name);saveProgress();
    console.log('Verified upload '+name+' '+asset.size);
  }
  const staged=await request('/repos/'+repo+'/releases/'+release.id);
  if(staged.assets.length!==files.length||staged.assets.some(a=>!files.includes(a.name)))throw Error('Unexpected staged release assets');
  release=await request('/repos/'+repo+'/releases/'+release.id,'PATCH',{draft:false,make_latest:'true',body:fs.readFileSync(notes,'utf8')});
  progress.draft=false;saveProgress();
  fs.writeFileSync(path.join(build,'release-remote.json'),JSON.stringify({url:release.html_url,tag:release.tag_name,source:head,
    assets:release.assets.map(a=>({name:a.name,size:a.size,digest:a.digest,url:a.browser_download_url}))},null,2));
  console.log(JSON.stringify({url:release.html_url,assets:release.assets.length,source:head}));
}
async function audit(){
  const head=git('rev-parse','HEAD').toString().trim();
  const manifest=JSON.parse(fs.readFileSync(path.join(build,'assets/release-manifest.json'))),source=manifest.source;
  git('merge-base','--is-ancestor',source,head);
  const remote=await request('/repos/'+repo+'/git/ref/heads/main');
  const tagged=await request('/repos/'+repo+'/git/ref/tags/'+tag);
  if(remote.object.sha!==head||tagged.object.sha!==source)throw Error('Remote source or tag mismatch');
  const release=await request('/repos/'+repo+'/releases/tags/'+tag);
  if(release.draft)throw Error('Release not published');
  const files=releaseFiles(manifest);
  if(release.assets.length!==files.length)throw Error('Unexpected release assets');
  for(const asset of release.assets){
    if(!files.includes(asset.name))throw Error('Unexpected release asset '+asset.name);
    const file=path.join(build,'assets',asset.name),sha=crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
    if(asset.digest!=='sha256:'+sha||asset.size!==fs.statSync(file).size)throw Error('Remote checksum mismatch '+asset.name);
  }
  const report={source,repositoryHead:head,tag,url:release.html_url,assets:release.assets.map(a=>({name:a.name,bytes:a.size,digest:a.digest})),errors:0};
  fs.writeFileSync(path.join(build,'remote-audit.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
}
(async()=>{
  if(action==='inspect'){
    const r=await request('/repos/'+repo),remote=await request('/repos/'+repo+'/git/ref/heads/main');
    const releases=await request('/repos/'+repo+'/releases?per_page=10');
    console.log(JSON.stringify({repo:r.full_name,branch:r.default_branch,source:remote.object.sha,
      releases:releases.map(x=>({tag:x.tag_name,draft:x.draft,assets:x.assets.length}))}));
  }
  else if(action==='push')await push();else if(action==='publish')await publish();else await audit();
})().catch(e=>{console.error(e.message);process.exitCode=1;});
