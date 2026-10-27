'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const net = require('node:net');
const {spawn} = require('node:child_process');

async function main() {
  const root = path.resolve(__dirname, '..');
  const runtime = path.resolve(process.argv[2]);
  const profile = path.join(root, 'build/startup-performance-20261027/web-profile', require('node:crypto').randomUUID());
  const listener = net.createServer();
  await new Promise(resolve => listener.listen(0, '127.0.0.1', resolve));
  const port = listener.address().port;
  await new Promise(resolve => listener.close(resolve));
  let checks = 0;
  for (const warm of [false, true]) {
    let logs = '', windowsLogs = '';
    const decoder = new TextDecoder('gbk');
    const server = spawn('dotnet', [runtime, '--urls', `http://127.0.0.1:${port}`, '--root', root,
      '--save-root', profile, '--contentRoot', path.join(root, 'rewrite/Ew4.Web'), '--Logging:LogLevel:Default', 'Warning'], {windowsHide:true});
    server.stdout.on('data', bytes => { logs += bytes; windowsLogs += decoder.decode(bytes, {stream:true}); });
    server.stderr.on('data', bytes => { logs += bytes; });
    try {
      let response;
      for (let attempt = 0; attempt < 120; attempt++) {
        assert.equal(server.exitCode, null, logs.slice(-1000));
        try { response = await fetch(`http://127.0.0.1:${port}/api/mods`); break; }
        catch { await new Promise(resolve => setTimeout(resolve, 250)); }
      }
      assert.ok(response, 'startup timeout');
      assert.equal(response.status, 200); checks++;
      const mods = await response.json();
      assert.equal(mods.active, null); checks++;
      const catalog = await fetch(`http://127.0.0.1:${port}/api/catalog`, {method:'POST', headers:{'Content-Type':'application/json'}, body:'{}'});
      assert.equal(catalog.status, 200); checks++;
      const content = await catalog.json();
      assert.equal(content.ok, true, JSON.stringify(content)); checks++;
      const output = logs + windowsLogs;
      assert.equal(output.includes('验证地图、剧本和规则'), !warm, `warm=${warm}: ${output}`); checks++;
      assert.ok(output.includes('资源检查完成'), output); checks++;
    } finally {
      if (server.exitCode === null) {
        const stopped = new Promise(resolve => server.once('exit', resolve));
        server.kill(); await stopped;
      }
    }
  }
  console.log(JSON.stringify({startupCacheWebChecks:checks, errors:0}));
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
