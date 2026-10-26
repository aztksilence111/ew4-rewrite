'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const {spawn} = require('node:child_process');
const {createRequire} = require('node:module');
const {chromium} = createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const root = path.resolve(__dirname, '..');
const output = path.join(root, 'build/startup-assets-20261026/integration');
const runtime = path.join(root, 'build/startup-assets-20261026/web-artifacts/bin/Ew4.Web/debug/Ew4.Web.dll');
const base = 'http://127.0.0.1:5575';
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
let server, logs = '', checks = 0;
const check = (value, label) => { assert.ok(value, label); checks++; };
function start() {
  logs = '';
  server = spawn('C:/Program Files/dotnet/dotnet.exe', [runtime, '--urls', base,
    '--data-root', path.join(output, 'data'), '--save-root', path.join(output, 'profile'),
    '--contentRoot', path.join(root, 'rewrite/Ew4.Web'), '--Logging:LogLevel:Default', 'Warning'], {windowsHide:true});
  server.stdout.on('data', bytes => { logs += bytes.toString(); });
  server.stderr.on('data', bytes => { logs += bytes.toString(); });
}
async function stop() {
  if (!server || server.exitCode !== null) return;
  const stopped = new Promise(resolve => server.once('exit', resolve));
  server.kill(); await stopped;
}
async function listening() {
  for (let attempt = 0; attempt < 120; attempt++) {
    if (server.exitCode !== null) throw Error('Web startup exited: ' + logs.slice(-1000));
    try { return await fetch(base + '/api/mods'); } catch { await pause(250); }
  }
  throw Error('Web startup timed out');
}
(async () => {
  await fs.mkdir(output, {recursive:true});
  await fs.cp(path.join(root, 'rewrite/data'), path.join(output, 'data'), {recursive:true});
  await fs.copyFile('C:/Users/WIN11/Downloads/def_army(3).xml', path.join(output, 'data/original/def_army.xml'));
  await fs.copyFile('C:/Users/WIN11/Downloads/def_terraintype(3).xml', path.join(output, 'data/original/def_terraintype.xml'));
  start();
  const browser = await chromium.launch({headless:true, executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', args:['--mute-audio']});
  try {
    const failed = await listening();
    check(failed.status === 503, 'invalid terrain returns a recoverable HTTP error');
    const failure = await failed.json();
    check(failure.code === 'startup.assets' && failure.message.includes('def_terraintype.xml'), 'startup API names the invalid original asset');
    const page = await browser.newPage({viewport:{width:1920,height:873}});
    const errors = [], missing = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('response', response => { if (response.status() >= 400 && response.status() !== 503) missing.push(response.url()); });
    const response = await page.goto(base);
    check(response.status() === 503, 'invalid input has a visible error page');
    check(await page.getByRole('button', {name:'重试'}).isVisible(), 'error page allows retry');
    await page.screenshot({path:path.join(output, 'failure-desktop.png')});
    await page.setViewportSize({width:390,height:844});
    check(await page.getByRole('button', {name:'重试'}).isVisible(), 'retry fits a mobile viewport');
    await page.screenshot({path:path.join(output, 'failure-mobile.png')});
    await fs.copyFile(path.join(root, 'rewrite/data/original/def_terraintype.xml'), path.join(output, 'data/original/def_terraintype.xml'));
    await page.getByRole('button', {name:'重试'}).click();
    await page.waitForFunction(() => typeof startupReady !== 'undefined' && startupReady, null, {timeout:180000});
    check(await page.evaluate(() => Number(manifest.profiles['ew4.unit.fra.grenadier.1'].stats.strength) === 145), 'startup rebuilt display data from the user army XML');
    await page.screenshot({path:path.join(output, 'ready-mobile.png')});
    await page.setViewportSize({width:1920,height:873});
    await page.screenshot({path:path.join(output, 'ready-desktop.png')});
    const started = await page.evaluate(async () => (await fetch('/api/start', {method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({scenario:'conquest1',country:'fra'})})).json());
    check(started.ok === true, 'rebuilt assets start a real conquest');
    check(errors.length === 0 && missing.length === 0, 'startup has no browser errors or missing resources');
    await stop();
    start();
    const reopened = await listening();
    check(reopened.status === 200, 'next process reopens the validated asset set');
    check(!logs.includes('验证地图') && !logs.includes('重建'), 'unchanged Web process skips regeneration and deep validation');
    console.log(JSON.stringify({startupWebChecks:checks, errors:0}));
  } finally { await browser.close(); await stop(); }
})().catch(async error => { await stop(); console.error(error.message); process.exitCode = 1; });
