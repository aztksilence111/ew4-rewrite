'use strict';
(function(root){
  class Feedback {
    constructor(play){
      this.play=play;this.presses=new Map();
      this.down=e=>{this.presses.clear();this.presses.set(e.pointerId,{x:e.clientX,y:e.clientY,moved:false});};
      this.move=e=>{const p=this.presses.get(e.pointerId);if(p&&Math.hypot(e.clientX-p.x,e.clientY-p.y)>6)p.moved=true;};
      this.cancel=e=>this.presses.delete(e.pointerId);
      this.click=e=>{
        const button=e.target.closest?.('button,input[type=checkbox],[role=button]');
        const press=this.presses.get(e.pointerId);this.presses.delete(e.pointerId);
        if(!e.isTrusted||!button||button.matches(':disabled')||button.closest('[hidden],[inert]')||press?.moved)return;
        const file=this.sound(button);if(!file)return;
        // Play during user activation, before async handlers disable/remove the button.
        this.play(file);
      };
      document.addEventListener('pointerdown',this.down,true);
      document.addEventListener('pointermove',this.move,true);
      document.addEventListener('pointercancel',this.cancel,true);
      document.addEventListener('click',this.click,true);
    }
    sound(button){
      const owner=button.closest('[data-ui-sound]');
      if(owner)return owner.dataset.uiSound;
      for(let el=button;el;el=el.parentElement){
        if(el.nativeAttributes&&Object.prototype.hasOwnProperty.call(el.nativeAttributes,'sound'))return el.nativeAttributes.sound;
      }
      if(button.matches('.back-icon,.close-icon,[id^=cancel-],[id^=close-],#hq-back,#resume,#return-menu')||/btn_(cancel|close|back)$/.test(button.dataset.nativeId||''))return 'sfx_cancel.wav';
      if(button.matches('.country-entry,.chapter-entry,.commander-open,[role=tab],[role=menuitemradio],input[type=checkbox]'))return 'sfx_select.wav';
      return 'sfx_click.wav';
    }
    dispose(){
      document.removeEventListener('pointerdown',this.down,true);document.removeEventListener('pointermove',this.move,true);
      document.removeEventListener('pointercancel',this.cancel,true);document.removeEventListener('click',this.click,true);
      this.presses.clear();
    }
  }
  root.Ew4UiAudio={Feedback};
})(globalThis);
