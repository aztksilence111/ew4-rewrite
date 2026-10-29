'use strict';
(()=>{
  const button=document.getElementById('save-export'),row=document.getElementById('save-export-row');
  if(!button||!row)return;
  row.hidden=false;
  const reset=()=>{button.disabled=false;button.textContent='导出存档';};
  window.ew4SaveExportFinished=result=>{reset();if(result.message&&typeof notice==='function')notice(result.message);};
  button.onclick=async()=>{
    if(button.disabled)return;
    button.disabled=true;button.textContent='正在导出';
    try{
      if(typeof window.Ew4ModImport?.exportSaves==='function'){window.Ew4ModImport.exportSaves();return;}
      const response=await fetch('/api/save-export',{method:'POST',cache:'no-store'});
      if(!response.ok||!response.headers.get('Content-Type')?.includes('application/zip')){
        const result=await response.json().catch(()=>({}));throw Error(result.error||result.message||'存档导出失败 ('+response.status+')');
      }
      const blob=await response.blob(),url=URL.createObjectURL(blob),link=document.createElement('a');
      const filename=response.headers.get('Content-Disposition')?.match(/filename="?([a-zA-Z0-9._-]+\.zip)/)?.[1];
      link.href=url;link.download=filename||'ew4-saves.zip';document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),60000);
      const count=Number.parseInt(response.headers.get('X-EW4-Save-Files'),10);
      window.ew4SaveExportFinished({ok:true,message:Number.isFinite(count)?'已导出 '+count+' 个存档及备份文件':'已导出存档'});
    }catch(error){window.ew4SaveExportFinished({ok:false,message:error.message});}
  };
})();
