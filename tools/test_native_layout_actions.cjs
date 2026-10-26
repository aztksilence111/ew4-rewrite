'use strict';
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const {test}=require('node:test');

const web=path.resolve(__dirname,'../rewrite/Ew4.Web/wwwroot');
for(const folder of ['', 'compat']){
  const context=vm.createContext({});
  vm.runInContext(fs.readFileSync(path.join(web,folder,'native-layout.js'),'utf8'),context);
  const prototype=context.Ew4NativeUI.Layout.prototype;
  function fixture(tutorialUi){
    const errors=[],events=[];
    const button={dataset:{tutorialId:'btn_done'},setAttribute(){}};
    const layout=Object.create(prototype);
    layout.api={tutorialUi,notice:message=>errors.push(message)};
    layout.get=()=>button;layout.current=()=>true;
    return {layout,button,errors,events};
  }
  test(`${folder||'source'}: optional tutorial callback`,async()=>{
    for(const callback of [undefined,null,false]){
      const f=fixture(callback);
      f.layout.action('test','test',()=>f.events.push('action'));
      await f.button.onclick();
      assert.deepEqual(f.events,['action']);assert.deepEqual(f.errors,[]);
      assert.equal(f.button.disabled,false);
    }
  });
  test(`${folder||'source'}: tutorial runs after action`,async()=>{
    const f=fixture(id=>f.events.push(id));
    f.layout.action('test','test',async()=>{await Promise.resolve();f.events.push('action');});
    await f.button.onclick();
    assert.deepEqual(f.events,['action','btn_done']);assert.deepEqual(f.errors,[]);
  });
  test(`${folder||'source'}: real failures stay visible`,async()=>{
    const f=fixture(()=>f.events.push('tutorial'));
    f.layout.action('test','test',()=>{throw Error('action failed');});
    await f.button.onclick();
    assert.deepEqual(f.errors,['action failed']);assert.deepEqual(f.events,[]);
    assert.equal(f.button.disabled,false);
    f.layout.api.tutorialUi=()=>{throw Error('tutorial failed');};
    f.layout.action('test','test',()=>{});await f.button.onclick();
    assert.deepEqual(f.errors,['action failed','tutorial failed']);
  });
  test(`${folder||'source'}: disabled and pending buttons cannot repeat`,async()=>{
    const f=fixture();let release;
    f.layout.action('test','test',()=>f.events.push('disabled'),false);
    await f.button.onclick();assert.deepEqual(f.events,[]);
    f.layout.action('test','test',()=>{f.events.push('action');return new Promise(resolve=>{release=resolve;});});
    const pending=f.button.onclick();await f.button.onclick();
    assert.deepEqual(f.events,['action']);release();await pending;
    assert.equal(f.button.disabled,false);
  });
}
