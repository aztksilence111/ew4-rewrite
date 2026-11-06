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
  const importButton=document.getElementById('save-import'),fileInput=document.getElementById('save-import-file');
  const make=(tag,cls,text)=>{const el=document.createElement(tag);el.className=cls||'';if(text!==undefined)el.textContent=text;return el;};
  const modal=make('div','modal');modal.id='save-manager-modal';modal.hidden=true;
  const dialog=make('section','dialog save-manager-dialog');dialog.setAttribute('role','dialog');dialog.setAttribute('aria-modal','true');dialog.setAttribute('aria-label','存档管理');
  modal.append(dialog);document.body.append(modal);
  let working=false,confirming=false,pendingFile=null,returnFocus=null,confirmCancel=null;
  const command=(label,action,parent)=>{const el=make('button','native-command blue',label);el.type='button';el.onclick=()=>Promise.resolve(action()).catch(e=>notice(e.message));parent.append(el);return el;};
  function close(){if(working)return;if(confirming){confirmCancel?.();return;}modal.hidden=true;returnFocus?.focus();}
  function header(title){dialog.replaceChildren();const bar=make('header','menu-bar'),x=make('button','close-icon');x.type='button';x.title=x.ariaLabel='关闭';x.onclick=close;bar.append(x,make('h2','',title));dialog.append(bar);}
  async function confirmAction(text,label='确认'){
    const wasHidden=modal.hidden,old=[...dialog.childNodes];modal.hidden=false;header('存档操作');confirming=true;
    dialog.append(make('p','save-manager-message',text));const actions=make('div','save-manager-footer');dialog.append(actions);
    return new Promise(resolve=>{
      const done=value=>{confirming=false;confirmCancel=null;dialog.replaceChildren(...old);modal.hidden=wasHidden;resolve(value);};confirmCancel=()=>done(false);
      command('取消',()=>done(false),actions).focus();command(label,()=>done(true),actions);
    });
  }
  function importReset(){working=false;importButton.disabled=false;importButton.textContent='导入';}
  async function upload(overwrite=false){
    working=true;importButton.disabled=true;importButton.textContent='导入中';
    try{
      const form=new FormData();form.append('file',pendingFile);form.append('overwrite',String(overwrite));
      const response=await fetch('/api/save-import',{method:'POST',body:form,cache:'no-store'}),result=await response.json();
      if(!response.ok)throw Error(result.error||'存档导入失败');await window.ew4SaveImportFinished(result);
    }catch(e){importReset();notice(e.message);pendingFile=null;fileInput.value='';}
  }
  window.ew4SaveImportFinished=async result=>{
    importReset();
    if(result.error){notice('存档导入失败：'+result.error);return;}
    if(result.cancelled)return;
    if(result.conflicts&&!result.imported){
      const accepted=await confirmAction('存档包包含 '+result.files+' 个文件，其中 '+result.conflicts+' 个将覆盖现有文件。覆盖前会保留恢复副本。导入后返回主菜单。','覆盖并导入');
      if(typeof window.Ew4ModImport?.confirmSaveImport==='function'){
        if(accepted){working=true;importButton.disabled=true;}window.Ew4ModImport.confirmSaveImport(accepted);
      }else if(accepted){await upload(true);return;}
      else {pendingFile=null;fileInput.value='';}
    }else if(result.imported){location.reload();}
  };
  importButton.onclick=()=>{
    if(working)return;
    if(typeof window.Ew4ModImport?.importSaves==='function'){
      working=true;importButton.disabled=true;try{window.Ew4ModImport.importSaves();}catch(e){importReset();notice(e.message);}
    }else fileInput.click();
  };
  fileInput.onchange=async()=>{pendingFile=fileInput.files[0];if(!pendingFile)return;if(pendingFile.size>256*1024*1024){notice('存档包超过256MiB');fileInput.value='';return;}await upload();};
  async function manager(){
    if(working)return;
    returnFocus=document.activeElement;const data=await rpc('save-manager');modal.hidden=false;header('存档保留');
    dialog.append(make('p','save-manager-identity',data.modName+' · '+data.identity));
    const grid=make('div','save-retentions');dialog.append(grid);
    for(const slot of data.slots){
      const panel=make('section','save-retention');panel.dataset.slot=slot.slot;panel.append(make('h3','','槽位 '+slot.slot),make('p','',slot.modName));
      panel.append(make('p','',slot.corrupt?'备份损坏':slot.exists?'勋章 '+slot.medals+'\n已结算战局 '+slot.battles+' · 将领 '+slot.commanders:'空槽位'));
      if(slot.savedAt)panel.append(make('p','',new Date(slot.savedAt).toLocaleString()));
      const actions=make('div','save-retention-actions');panel.append(actions);
      const operate=async op=>{
        if(working)return;
        const copy=op==='restore'?'加载槽位 '+slot.slot+'，替换当前Mod的账户及全部战局存档，并返回主菜单。':op==='delete'?'删除槽位 '+slot.slot+' 的备份。':'将当前Mod的账户及全部战局存档保存到槽位 '+slot.slot+(slot.exists?'，覆盖原备份。':'。');
        if(!await confirmAction(copy))return;
        working=true;modal.setAttribute('aria-busy','true');
        try{await rpc('save-manager',{op,slot:slot.slot});if(op==='restore'){location.reload();return;}working=false;await manager();}
        finally{working=false;modal.removeAttribute('aria-busy');}
      };
      command('保存',()=>operate('save'),actions);command('加载',()=>operate('restore'),actions).disabled=!slot.exists||slot.corrupt;command('删除',()=>operate('delete'),actions).disabled=!slot.exists;grid.append(panel);
    }
    const footer=make('div','save-manager-footer');dialog.append(footer);
    command('清理当前Mod存档',async()=>{
      if(working||!await confirmAction('清理当前Mod的账户和全部战局存档，重新开始游戏。8个备份槽保留，清理前另存恢复副本。','清理并重启'))return;
      working=true;try{await rpc('save-manager',{op:'clear'});location.reload();}catch(e){working=false;throw e;}
    },footer);
    dialog.querySelector('button').focus();
  }
  document.getElementById('save-manager').onclick=()=>manager().catch(e=>notice(e.message));
  const mainEntry=command('存档管理',()=>{if(startupReady)return manager();},document.getElementById('main-menu'));mainEntry.id='main-save-manager';
  window.ew4SaveManagerBack=()=>{if(modal.hidden)return false;close();return true;};
  modal.addEventListener('keydown',e=>{
    if(e.key==='Escape'){e.preventDefault();e.stopPropagation();close();}
    if(e.key==='Tab'){
      const buttons=[...dialog.querySelectorAll('button:not(:disabled)')],first=buttons[0],last=buttons[buttons.length-1];
      if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}
    }
  });
})();
