'use strict';
(()=>{
  const button=document.getElementById('save-export'),row=document.getElementById('save-export-row');
  if(!button||!row||typeof window.Ew4ModImport?.exportSaves!=='function')return;
  row.hidden=false;
  const reset=()=>{button.disabled=false;button.textContent='导出存档';};
  window.ew4SaveExportFinished=result=>{reset();if(result.message&&typeof notice==='function')notice(result.message);};
  button.onclick=()=>{
    if(button.disabled)return;
    button.disabled=true;button.textContent='正在导出';
    try{window.Ew4ModImport.exportSaves();}catch(error){reset();if(typeof notice==='function')notice(error.message);}
  };
})();
