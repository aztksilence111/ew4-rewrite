'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync('rewrite/Ew4.Web/wwwroot/effects.js','utf8');
let checks=0;
const check=(value,label)=>{assert.ok(value,label);checks++;};
function fixture(webAudio=true){
  const log={starts:[],fetches:0,decodes:0,contexts:0},clock={now:0},document={hidden:false};let volume=60;
  class Media {
    constructor(){this.currentTime=9;this.paused=true;}
    cloneNode(){return new Media();}
    play(){this.paused=false;log.starts.push(this);return Promise.resolve();}
    pause(){this.paused=true;}
  }
  class Context {
    constructor(options){log.contexts++;this.options=options;this.state='suspended';}
    resume(){this.state='running';return Promise.resolve();}
    suspend(){this.state='suspended';return Promise.resolve();}
    close(){this.state='closed';return Promise.resolve();}
    decodeAudioData(bytes){log.decodes++;return Promise.resolve({bytes,duration:.2});}
    createGain(){return {gain:{value:0},connect(){},disconnect(){}};}
    createBufferSource(){return {connect(){},disconnect(){},start(){log.starts.push(this);},stop(){this.stopped=true;this.onended?.();}};}
  }
  const sandbox={document,performance:{now:()=>clock.now},Audio:Media,AudioContext:webAudio?Context:undefined,
    fetch:async()=>{log.fetches++;return {ok:true,arrayBuffer:async()=>new ArrayBuffer(4)};}};
  vm.createContext(sandbox);vm.runInContext(source,sandbox);
  const effects=new sandbox.Ew4Effects.Effects({volume:()=>volume},{sounds:['click.wav','other.wav'],motions:{}},null);
  return {effects,audio:effects.sounds,log,clock,document,sandbox,volume:value=>volume=value};
}
const flush=async()=>{for(let i=0;i<12;i++)await Promise.resolve();};
(async()=>{
  const f=fixture(),{effects,audio,log}=f;
  check(log.contexts===0,'audio context created lazily');
  await Promise.all([effects.preloadSounds(['click.wav','unknown.wav']),effects.preloadSounds(['click.wav'])]);
  check(log.fetches===1&&log.decodes===1,'concurrent preloads share fetch and decode');
  check(audio.context.options.latencyHint==='interactive','interactive audio latency requested');
  check(audio.context.state==='suspended'&&log.starts.length===0,'preload never starts playback');
  await effects.unlockAudio();effects.sound('click.wav');effects.sound('click.wav');
  check(log.starts.length===2&&log.starts[0]!==log.starts[1],'rapid clicks have independent sources');
  check(log.starts.every(v=>v.buffer===effects.audio.get('click.wav')),'clicks reuse decoded PCM');
  check(log.fetches===1&&log.decodes===1,'warm click does no I/O');
  effects.clear();check(effects.voices.size===0&&log.starts.every(v=>v.stopped),'clear stops every source');
  f.volume(0);effects.sound('click.wav');f.volume(60);effects.sound('unknown.wav');
  check(log.starts.length===2,'muted and unknown sounds ignored');
  f.document.hidden=true;effects.sound('click.wav');f.document.hidden=false;
  check(log.starts.length===2,'hidden document silent');
  effects.setVisible(false);effects.sound('click.wav');
  check(audio.context.state==='suspended'&&log.starts.length===2,'native background suspends playback');
  effects.setVisible(true);await flush();effects.sound('click.wav');
  check(log.starts.length===3,'foreground playback restored');
  const voice=log.starts[2];voice.onended();check(!effects.voices.has(voice),'ended source released');
  effects.dispose();effects.sound('click.wav');
  check(audio.context.state==='closed'&&effects.audio.size===0&&log.starts.length===3,'dispose closes old resource context');

  for(const cancel of ['late','clear','dispose','mute','hidden']){
    const f=fixture();await f.effects.unlockAudio();let finish;
    f.sandbox.fetch=()=>new Promise(resolve=>{finish=()=>resolve({ok:true,arrayBuffer:async()=>new ArrayBuffer(4)});});
    f.effects.sound('other.wav');
    if(cancel==='late')f.clock.now=151;
    if(cancel==='clear')f.effects.clear();
    if(cancel==='dispose')f.effects.dispose();
    if(cancel==='mute')f.volume(0);
    if(cancel==='hidden')f.document.hidden=true;
    finish();await flush();
    check(f.log.starts.length===0,'pending load canceled by '+cancel);
    if(cancel==='dispose')check(f.effects.audio.size===0,'disposed load cannot repopulate cache');
  }
  const cold=fixture();cold.effects.sound('other.wav');await flush();
  check(cold.log.starts.length===1,'quick cold load plays once');
  const retry=fixture();let requests=0;
  retry.sandbox.fetch=async()=>({ok:++requests>1,arrayBuffer:async()=>new ArrayBuffer(4)});
  await retry.effects.preloadSounds(['click.wav']);check(retry.effects.audio.size===0,'failed load not cached');
  await retry.effects.preloadSounds(['click.wav']);check(retry.effects.audio.size===1&&requests===2,'failed fetch retries');
  const decode=fixture();decode.audio.getContext().decodeAudioData=()=>Promise.reject(Error('bad WAV'));
  await decode.effects.preloadSounds(['click.wav']);check(decode.effects.audio.size===0,'bad decode leaves no cached sound');
  const visibility=fixture();await visibility.effects.unlockAudio();let suspended;
  visibility.audio.context.suspend=()=>new Promise(resolve=>{suspended=()=>{visibility.audio.context.state='suspended';resolve();};});
  visibility.effects.setVisible(false);visibility.effects.setVisible(true);suspended();await flush();
  check(visibility.audio.context.state==='running','fast foreground return recovers an in-flight suspend');

  const fallback=fixture(false);await fallback.effects.preloadSounds(['click.wav']);
  fallback.effects.sound('click.wav');fallback.effects.sound('click.wav');
  check(fallback.log.starts.length===2&&fallback.log.starts[0]!==fallback.log.starts[1],'media fallback supports overlapping voices');
  check(fallback.log.starts.every(v=>v.currentTime===0&&v.volume===.6),'fallback restarts from beginning at selected volume');
  fallback.log.starts[0].onended();fallback.effects.sound('click.wav');
  check(fallback.log.starts[2]===fallback.log.starts[0]&&fallback.log.starts[2].currentTime===0,'ended fallback pool restarts');
  fallback.effects.clear();check(fallback.log.starts.every(v=>v.paused),'fallback clear pauses voices');
  console.log(JSON.stringify({checks,errors:0}));
})().catch(error=>{console.error(error.message);process.exitCode=1;});
