'use strict';
const assert = require('node:assert/strict');
const cp = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const WebSocket = require('./frontend/node_modules/ws');
const serial = process.argv[2], output = path.resolve(process.argv[3] || 'build/honor-startup-20261028/device');
assert(serial && (serial.startsWith('emulator-') || serial.startsWith('127.0.0.1:')), 'Use an isolated emulator');
const adb = 'C:/Users/WIN11/.codex/tools/android-sdk/platform-tools/adb.exe';
const pkg = 'org.ew4.reconstruction';
const run = (...args) => cp.execFileSync(adb, ['-s', serial, ...args], { encoding: 'utf8', timeout: 30000 });
assert(run('shell', 'dumpsys', 'package', pkg).includes('versionName=0.2.24-startup-test'), 'Only test the startup test app');
fs.mkdirSync(output, { recursive: true });
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const report = { checks: [], errors: [], scope: 'Native x64 Android emulator, diagnostic build; no Honor device execution' };
let ws, sequence = 0;
const requests = new Map();
function check(ok, label) { assert(ok, label); report.checks.push(label); }
function send(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++sequence, timer = setTimeout(() => { requests.delete(id); reject(Error(method + ' timed out')); }, 45000);
    requests.set(id, { resolve, reject, timer }); ws.send(JSON.stringify({ id, method, params }));
  });
}
async function connect() {
  const pid = run('shell', 'pidof', pkg).trim();
  run('forward', 'tcp:9224', 'localabstract:webview_devtools_remote_' + pid);
  let pages;
  for (let i = 0; i < 120; i++) {
    try { pages = await fetch('http://127.0.0.1:9224/json').then(r => r.json()); } catch (error) { }
    if (pages?.some(p => p.type === 'page')) break;
    await pause(500);
  }
  const page = pages?.find(p => p.type === 'page'); assert(page, 'WebView debug page available');
  ws = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { ws.once('open', resolve); ws.once('error', reject); });
  ws.on('message', raw => {
    const event = JSON.parse(raw);
    if (event.id) {
      const pending = requests.get(event.id); if (!pending) return;
      requests.delete(event.id); clearTimeout(pending.timer);
      event.error ? pending.reject(Error(JSON.stringify(event.error))) : pending.resolve(event.result);
    }
  });
}
async function evaluate(expression) {
  const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}
async function ready() {
  for (let i = 0; i < 120; i++) {
    if (await evaluate('typeof startupReady !== "undefined" && startupReady')) return;
    await pause(500);
  }
  throw Error('Menu did not become ready');
}
function nativeUi() {
  run('shell', 'uiautomator', 'dump', '/sdcard/ew4-startup-check.xml');
  return run('shell', 'cat', '/sdcard/ew4-startup-check.xml');
}
function screenshot(name) {
  run('shell', 'screencap', '-p', '/sdcard/ew4-startup-check.png');
  run('pull', '/sdcard/ew4-startup-check.png', path.join(output, name));
}
function tapText(xml, text) {
  const script = 'import sys,json,xml.etree.ElementTree as E; n=next(n for n in E.fromstring(sys.stdin.read()).iter("node") if n.get("text")==sys.argv[1]); print(json.dumps([int(v) for v in n.get("bounds").replace("][",",").strip("[]").split(",")]))';
  const box = JSON.parse(cp.execFileSync('python', ['-X', 'utf8', '-c', script, text], { input: xml, encoding: 'utf8' }));
  run('shell', 'input', 'tap', String((box[0] + box[2]) / 2), String((box[1] + box[3]) / 2));
}
(async () => {
  try {
    const initial = nativeUi();
    if (initial.includes('text="继续启动"')) tapText(initial, '继续启动');
    else if (initial.includes('text="重试"')) tapText(initial, '重试');
    await connect(); await ready();
    check(await evaluate('!document.querySelector("#main-menu").hidden && !document.querySelector(".main-entry").disabled'), 'menu ready after resource checks');
    screenshot('menu.png');
    await send('Page.enable');
    await send('Page.addScriptToEvaluateOnNewDocument', { source: `
      window.ew4StartupFixture = true;
      let startup;
      Object.defineProperty(window, 'Ew4Startup', {configurable:true,
        get(){return startup;}, set(value){startup=value;value.ready=function(){};}});
      document.addEventListener('DOMContentLoaded', function(){
        window.dispatchEvent(new ErrorEvent('error', {message:'Startup fixture failure', filename:'startup-fixture.js', lineno:1}));
      }, {once:true});` });
    await evaluate('location.reload(); true');
    let xml;
    for (let i = 0; i < 12; i++) { xml = nativeUi(); if (xml.includes('游戏页面启动失败')) break; await pause(500); }
    if (!xml.includes('游戏页面启动失败')) report.diagnostic = await evaluate('({fixture:window.ew4StartupFixture,ready:startupReady,bridge:typeof Ew4ModImport.startup,message:document.querySelector("#startup-message").textContent})');
    check(xml.includes('游戏页面启动失败') && xml.includes('startup-fixture.js'), 'script failure displays native error and filename');
    check(xml.includes('WebView：com.android.webview') && xml.includes('重试'), 'native failure includes WebView version and retry');
    screenshot('script-failure.png');
    ws.close();
    run('shell', 'am', 'force-stop', pkg);
    const activity = run('shell', 'cmd', 'package', 'resolve-activity', '--brief', pkg).trim().split(/\r?\n/).at(-1);
    run('shell', 'am', 'start', '-W', '-n', activity);
    xml = nativeUi();
    check(xml.includes('上次启动未完成') && xml.includes('startup-fixture.js') && xml.includes('继续启动'), 'next launch preserves the failure before initializing WebView');
    screenshot('previous-failure.png');
    tapText(xml, '继续启动');
    await connect(); await ready();
    check(await evaluate('startupReady && document.querySelector("#startup-status").hidden'), 'continue recovers menu after failure');
    ws.close(); run('shell', 'am', 'force-stop', pkg); run('shell', 'am', 'start', '-W', '-n', activity);
    await connect(); await ready();
    check(await evaluate('startupReady'), 'successful warm launch skips the previous failure prompt');
    console.log(JSON.stringify({ checks: report.checks.length, errors: report.errors }));
  } catch (error) { report.errors.push(error.message); throw error; }
  finally {
    ws?.close();
    try { run('forward', '--remove', 'tcp:9224'); } catch (error) { }
    fs.writeFileSync(path.join(output, 'verification.json'), JSON.stringify(report, null, 2));
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
