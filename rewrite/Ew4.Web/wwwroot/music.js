'use strict';
(function(root){
  class Player {
    constructor(configuration,volume){
      this.configuration=configuration;this.volume=volume;this.audio=new Audio();this.audio.preload='none';this.audio.loop=true;
      this.key=null;this.file=null;this.visible=true;this.unlocked=false;this.pending=null;this.failed=false;
      this.audio.addEventListener('error',()=>{this.failed=true;});
      this.gesture=()=>{this.unlocked=true;this.resume();};
      // Touch activation is granted at pointerup/click, not pointerdown. Capture also
      // reaches controls that stop propagation; retry after a rejected early play.
      this.gestures=['pointerdown','pointerup','click','keydown'];
      for(const event of this.gestures)document.addEventListener(event,this.gesture,{passive:true,capture:true});
      this.visibility=()=>this.resume();document.addEventListener('visibilitychange',this.visibility);
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
        this.audio.pause();this.pending=null;this.file=file;this.failed=false;
        if(file){this.audio.src='/content/music/'+encodeURIComponent(file);this.audio.load();}
        else{this.audio.removeAttribute('src');this.audio.load();}
      }
      this.resume();
    }
    setVisible(visible){this.visible=visible;this.resume();}
    resume(){
      const volume=Number(this.volume());this.audio.volume=Number.isFinite(volume)?Math.max(0,Math.min(1,volume/100)):0;
      if(!this.unlocked||!this.visible||document.hidden||!this.audio.volume||!this.file||this.failed){this.audio.pause();return;}
      if(this.pending||!this.audio.paused)return;
      // Source switches can reject an older play promise; only release its own pending slot.
      const pending=this.audio.play();this.pending=pending;
      pending.catch(()=>{}).finally(()=>{if(this.pending===pending)this.pending=null;});
    }
    dispose(){
      this.audio.pause();this.audio.removeAttribute('src');this.audio.load();
      for(const event of this.gestures)document.removeEventListener(event,this.gesture,true);
      document.removeEventListener('visibilitychange',this.visibility);
    }
  }
  root.Ew4Music={Player};
})(globalThis);
