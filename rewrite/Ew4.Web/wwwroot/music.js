'use strict';
(function(root){
  class Player {
    constructor(configuration,volume){
      this.configuration=configuration;this.volume=volume;this.audio=new Audio();this.audio.preload='none';this.audio.loop=true;
      this.key=null;this.file=null;this.visible=true;this.unlocked=!!root.navigator?.userActivation?.hasBeenActive;this.pending=null;this.failed=false;
      this.generation=0;this.retries=0;this.lastRetry=0;this.retryTimer=null;this.loadTimer=null;this.loading=null;this.controller=null;this.objectUrl=null;this.disposed=false;this.lastError=null;this.resumeRequested=false;
      this.audio.addEventListener('error',()=>{if(this.audio.getAttribute('src')&&this.audio.error)this.fail(this.audio.error);});
      this.audio.addEventListener('playing',()=>{clearTimeout(this.loadTimer);this.loadTimer=null;this.retries=0;this.lastError=null;});
      this.gesture=()=>this.activate();
      // Touch activation is granted at pointerup/click, not pointerdown. Capture also
      // reaches controls that stop propagation; retry after a rejected early play.
      this.gestures=['pointerdown','pointerup','click','keydown'];
      for(const event of this.gestures)document.addEventListener(event,this.gesture,{passive:true,capture:true});
      this.visibility=()=>this.resume(true);document.addEventListener('visibilitychange',this.visibility);
    }
    menu(){this.select('menu',this.configuration.menu);}
    sync(state,restart=false){
      if(state.battleResult?.outcome==='Defeat'){this.select('defeat:'+state.battleResult.id,this.configuration.defeat);return;}
      const key='battle:'+state.scenario;
      if(!restart&&this.key===key)return;
      const tracks=this.configuration.battle;
      this.select(key,tracks.length?tracks[Math.floor(Math.random()*tracks.length)]:null,restart);
    }
    select(key,file,reset=false){
      if(this.key===key&&!reset)return;
      this.key=key;
      if(this.file!==file||reset){
        this.releaseSource();this.file=file;this.failed=false;this.retries=0;this.lastError=null;
        if(file){this.audio.src='/content/music/'+encodeURIComponent(file);this.audio.load();}
        else{this.audio.removeAttribute('src');this.audio.load();}
      }
      this.resume();
    }
    releaseSource(){
      this.generation++;this.audio.pause();this.pending=null;this.loading=null;this.resumeRequested=false;
      clearTimeout(this.retryTimer);clearTimeout(this.loadTimer);this.retryTimer=null;this.loadTimer=null;
      this.controller?.abort();this.controller=null;
      this.audio.removeAttribute('src');
      if(this.objectUrl)URL.revokeObjectURL(this.objectUrl);this.objectUrl=null;
    }
    fail(error){
      if(this.disposed||this.failed||!this.file)return;
      this.failed=true;this.generation++;this.audio.pause();this.pending=null;this.loading=null;
      this.controller?.abort();this.controller=null;
      clearTimeout(this.loadTimer);this.loadTimer=null;
      this.lastError={file:this.file,name:error?.name||'MediaError',code:error?.code||0,message:error?.message||'Audio load failed'};
      console.warn('EW4 music',this.lastError);
      // Fetch through the local resource handler before native media decoding.
      // A complete blob avoids depending on HTTP byte ranges for local assets.
      if(this.retries<2){this.retries++;this.retryTimer=setTimeout(()=>{this.retryTimer=null;this.loadBlob();},this.retries*500);}
    }
    loadBlob(){
      if(this.disposed||!this.file||!this.visible||document.hidden||!Number(this.volume()))return;
      this.releaseSource();this.failed=false;this.lastRetry=Date.now();
      const generation=this.generation,controller=new AbortController();this.controller=controller;
      this.loadTimer=setTimeout(()=>this.fail({name:'TimeoutError',message:'Audio request timed out'}),15000);
      this.loading=(async()=>{
        try{
          const response=await fetch('/content/music/'+encodeURIComponent(this.file),{signal:controller.signal});
          if(!response.ok)throw new Error('Audio HTTP '+response.status);
          const blob=await response.blob();
          if(this.disposed||generation!==this.generation||controller.signal.aborted)return;
          clearTimeout(this.loadTimer);this.loadTimer=null;this.controller=null;this.loading=null;
          this.objectUrl=URL.createObjectURL(blob);this.audio.src=this.objectUrl;this.audio.load();this.resume();
        }catch(error){if(generation===this.generation&&!this.disposed)this.fail(error);}
      })();
    }
    activate(forceRetry=false){this.unlocked=true;if(forceRetry)this.lastRetry=0;this.resume(true);}
    setVisible(visible){this.visible=visible;if(visible)this.lastRetry=0;this.resume(true);}
    resume(retry=false){
      if(this.disposed)return;
      const volume=Number(this.volume());this.audio.volume=Number.isFinite(volume)?Math.max(0,Math.min(1,volume/100)):0;
      if(!this.unlocked||!this.visible||document.hidden||!this.audio.volume||!this.file){
        this.audio.pause();if(!this.loading){clearTimeout(this.loadTimer);this.loadTimer=null;}return;
      }
      if(this.failed){
        if(retry&&!this.retryTimer&&Date.now()-this.lastRetry>=3000){this.retries=0;this.loadBlob();}
        return;
      }
      if(this.loading||!this.audio.paused)return;
      if(this.pending){
        this.resumeRequested=true;
        if(!this.loadTimer)this.loadTimer=setTimeout(()=>this.fail({name:'TimeoutError',message:'Audio resume did not start'}),15000);
        return;
      }
      // Source switches can reject an older play promise; only release its own pending slot.
      const generation=this.generation;
      this.loadTimer=setTimeout(()=>this.fail({name:'TimeoutError',message:'Audio playback did not start'}),15000);
      const pending=this.audio.play();this.pending=pending;
      pending.catch(error=>{
        if(generation!==this.generation||this.disposed)return;
        clearTimeout(this.loadTimer);this.loadTimer=null;
        if(error.name!=='NotAllowedError'&&error.name!=='AbortError')this.fail(error);
        else this.lastError={file:this.file,name:error.name,message:error.message};
      }).finally(()=>{
        if(this.pending!==pending)return;
        this.pending=null;
        if(this.resumeRequested){this.resumeRequested=false;this.resume();}
      });
    }
    dispose(){
      this.disposed=true;this.releaseSource();this.audio.load();
      for(const event of this.gestures)document.removeEventListener(event,this.gesture,true);
      document.removeEventListener('visibilitychange',this.visibility);
    }
  }
  root.Ew4Music={Player};
})(globalThis);
