'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');
const { chromium } = createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const folder = path.resolve(process.argv[3] || path.join(__dirname, '../build/installation-fix-20261023'));
const profile = path.resolve(process.argv[4] || path.join(folder, 'profile'));
const base = process.argv[2] || 'http://127.0.0.1:5491';
(async () => {
  const report = { checks: 0, errors: [], missing: [], warnings: [], screenshots: [] };
  const check = (pass, label) => { assert.ok(pass, label); report.checks++; };
  const fixture = JSON.parse(await fs.readFile(path.join(folder, 'backend.json'), 'utf8'));
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe' });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  page.on('pageerror', e => report.errors.push(e.message));
  page.on('response', r => { if (r.status() >= 400) report.missing.push(r.url()); });
  page.on('console', m => { if (m.type() === 'warning' && /Missing original/.test(m.text())) report.warnings.push(m.text()); });
  const seed = async name => {
    await fs.copyFile(path.join(folder, name + '-slot.json'), path.join(profile, 'slot-4.json'));
    await page.evaluate(async cell => {
      closeDetail();await installState(await rpc('load', { slot: 4 }), true);
      await selectCell(cell, true);const p = point(cell);view.x=p.x;view.y=p.y;clampView();requestRender();
    }, fixture.cell);
  };
  const open = async () => {
    await page.getByRole('button', { name: '防御', exact: true }).click();
    await page.locator('.defense-cards').waitFor();
    await page.waitForFunction(() => [...document.querySelectorAll('.defense-cards button>img')].every(i => i.complete && i.naturalWidth > 0));
  };
  try {
    await page.goto(base);
    await page.waitForFunction(() => typeof manifest !== 'undefined' && manifest && typeof catalog !== 'undefined' && catalog);
    for (const mode of ['rich', 'discount']) {
      for (const [index, name] of ['战壕', '栅栏', '掩体'].entries()) {
        await seed(mode);await open();
        check(await page.locator('.defense-cards button').count() === 3, mode + ' has all three cards');
        check(await page.locator('.defense-cards button[aria-pressed=true]').count() === 1, 'initial selection is visible');
        await page.getByRole('button', { name, exact: true }).click();
        const before = await page.evaluate(index => ({ offer:selection.installationOffers[index], revision:state.revision,
          funds:state.factions.find(f=>f.id===state.playerFaction).resources,
          values:[...document.querySelectorAll('.defense-cards button[aria-pressed=true] [data-resource]')].map(e=>({key:e.dataset.resource,value:Number(e.querySelector('.original-text')?.getAttribute('aria-label')??e.textContent)})),
          title:document.querySelector('#detail-content h3').textContent,
          desc:document.querySelector('#detail-content .unit-description p').textContent }), index);
        check(before.title === name && before.desc.length > 10, name + ' description');
        check(before.values.every(v => v.value === before.offer.quote.cost[v.key]), name + ' actual quote displayed: ' + JSON.stringify(before));
        check(!await page.locator('#detail-confirm').isDisabled(), 'affordable confirmation enabled');
        await page.locator('#detail-confirm').click();
        await page.waitForFunction(revision => !busy && state.revision === revision+1, before.revision);
        const after = await page.evaluate(cell => ({ kind:state.cells[cell].installationKind,
          funds:state.factions.find(f=>f.id===state.playerFaction).resources, movement:selected?.remainingMovement,
          acted:state.units.find(u=>u.cell===cell).acted }), fixture.cell);
        check(after.kind === before.offer.kind, name + ' built on real tile');
        check(['money','industry','food'].every(k => before.funds[k]-after.funds[k] === before.offer.quote.cost[k]), name + ' exact displayed spending');
        check(await page.locator('#detail-modal').isHidden(), 'successful confirmation closes panel');
        await page.evaluate(async cell=>{await selectCell(cell,true);},fixture.cell);
        check(await page.getByRole('button',{name:'防御',exact:true}).count()===0, 'built tile cannot build again');
      }
    }
    await seed('poor');await open();
    for (const name of ['战壕','栅栏','掩体']) {
      await page.getByRole('button',{name,exact:true}).click();
      check(await page.locator('#detail-confirm').isDisabled(), name+' insufficient funds disables confirm');
      check(await page.evaluate(()=>[...document.querySelectorAll('.defense-cards button[aria-pressed=true] [data-resource]')].every(e=>getComputedStyle(e).color==='rgb(255, 50, 0)')), 'missing resource red');
    }
    const revision=await page.evaluate(()=>state.revision);
    await page.evaluate(()=>$('detail-confirm').click());
    check(await page.evaluate(r=>state.revision===r,revision),'disabled confirmation does not spend');
    await seed('rich');await open();
    for (const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]) {
      await page.setViewportSize(size);await page.waitForTimeout(250);
      const layout=await page.evaluate(()=>{
        const rect=e=>{const r=e.getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,bottom:r.bottom,width:r.width,height:r.height};};
        return {dialog:rect($('detail-modal').firstElementChild),cards:[...document.querySelectorAll('.defense-cards button')].map(rect),desc:rect(document.querySelector('#detail-content .unit-description')),confirm:rect($('detail-confirm')),w:innerWidth,h:innerHeight};
      });
      check(layout.dialog.x>=-1&&layout.dialog.y>=-1&&layout.dialog.right<=layout.w+1&&layout.dialog.bottom<=layout.h+1,'defense dialog fits viewport');
      check(layout.cards.every(c=>c.bottom<=layout.desc.y&&c.x>=layout.dialog.x&&c.right<=layout.dialog.right),'cards and description do not overlap');
      const image=path.join(folder,`defense-${size.width}x${size.height}.png`);await page.screenshot({path:image});report.screenshots.push(image);
    }
    for(const mode of ['building','installed','foreign','cavalry']){
      await seed(mode);check(await page.getByRole('button',{name:'防御',exact:true}).count()===0,mode+' hides defense action');
    }
    check(!report.errors.length&&!report.missing.length&&!report.warnings.length,'no script errors or missing images');
    console.log(JSON.stringify(report));
  } finally {
    await fs.writeFile(path.join(folder,'browser.json'),JSON.stringify(report,null,2));await browser.close();
  }
})().catch(e=>{console.error(e.message);process.exitCode=1;});
