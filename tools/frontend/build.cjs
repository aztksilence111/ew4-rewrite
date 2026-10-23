'use strict';
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const esbuild=require('esbuild'),postcss=require('postcss'),acorn=require('acorn');
const root=path.resolve(__dirname,'../..'),web=path.join(root,'rewrite/Ew4.Web/wwwroot');
const output=path.join(web,'compat');
const hash=data=>crypto.createHash('sha256').update(data).digest('hex');
fs.mkdirSync(output,{recursive:true});
const manifest={target:'chrome70',files:{}};
function write(name,source,result){
  const destination=path.join(output,name);fs.mkdirSync(path.dirname(destination),{recursive:true});
  if(!fs.existsSync(destination)||fs.readFileSync(destination,'utf8')!==result)fs.writeFileSync(destination,result);
  manifest.files[name]={sourceSha256:hash(source),sha256:hash(result)};
}
for(const name of fs.readdirSync(web).filter(n=>n.endsWith('.js')).concat(
  fs.readdirSync(path.join(web,'vendor')).filter(n=>n.endsWith('.js')).map(n=>'vendor/'+n))){
  const source=fs.readFileSync(path.join(web,name),'utf8');
  const result=(name==='startup.js'?'window.Ew4FrontendTarget=70;\n':'')+
    esbuild.transformSync(source,{target:['chrome70','es2018'],format:undefined,charset:'utf8',legalComments:'inline'}).code;
  // ES2018 is the syntax floor; APIs are provided by startup.js, independently.
  acorn.parse(result,{ecmaVersion:2018});write(name,source,result);
}
for(const name of fs.readdirSync(web).filter(n=>n.endsWith('.css'))){
  const source=fs.readFileSync(path.join(web,name),'utf8'),sheet=postcss.parse(source);
  sheet.walkDecls('inset',decl=>{
    const values=postcss.list.space(decl.value),[top,right=top,bottom=top,left=right]=values;
    for(const [prop,value] of Object.entries({top,right,bottom,left}))decl.cloneBefore({prop,value});
    decl.remove();
  });
  sheet.walkDecls(decl=>{
    // CSS is served from compat/, while images remain at the frontend root.
    decl.value=decl.value.replace(/url\(\s*(['"]?)(?!\/|data:|https?:|#)([^)'"\s]+)\1\s*\)/g,'url($1../$2$1)');
  });
  write(name,source,esbuild.transformSync(sheet.toString(),{loader:'css',target:'chrome70',charset:'utf8'}).code);
}
const listing=JSON.stringify(manifest,null,2)+'\n';
if(!fs.existsSync(path.join(output,'manifest.json'))||fs.readFileSync(path.join(output,'manifest.json'),'utf8')!==listing)
  fs.writeFileSync(path.join(output,'manifest.json'),listing);
console.log(JSON.stringify({target:manifest.target,files:Object.keys(manifest.files).length,errors:0}));
