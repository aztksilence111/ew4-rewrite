'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');
const { chromium } = createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const folder = path.resolve(__dirname, '../rewrite/reports/single-actions-20261021');
const runtime = process.argv[3] ? path.resolve(process.argv[3]) : path.resolve(__dirname, '../build/single-actions-20261021/profile');
const base = process.argv[2] || 'http://127.0.0.1:5222';
(async () => {
  await fs.mkdir(runtime, { recursive: true });
  const fixture = JSON.parse(await fs.readFile(path.join(folder, 'backend.json'), 'utf8'));
  const report = { checks: 0, errors: [], missing: [], screenshots: [] };
  const check = (pass, label) => { assert.ok(pass, label); report.checks++; };
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe' });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  page.on('pageerror', e => report.errors.push(e.message));
  page.on('response', r => { if (r.status() >= 400) report.missing.push(r.url()); });
  const shot = async name => { await page.screenshot({ path: path.join(folder, name + '.png') }); report.screenshots.push(name + '.png'); };
  const seed = async name => {
    await fs.copyFile(path.join(folder, name + '-slot.json'), path.join(runtime, 'slot-4.json'));
    await page.evaluate(async cell => {
      await installState(await rpc('load', { slot: 4 }), true);
      await selectCell(cell, true);view.x = point(cell).x;view.y = point(cell).y;clampView();requestRender();
    }, fixture.target.cell);
  };
  const open = async op => {
    await page.locator('#actions [data-action="' + op + '"]').click();
    await page.locator('#single-action').waitFor();
    check(await page.locator('#detail-modal').isHidden(), op + ' opens no central modal');
    check(await page.locator('#single-action .unit-card').count() === 0, op + ' has no selection card');
    return page.evaluate(() => {
      const p = pendingAction, r = $('single-action-cost').getBoundingClientRect(), b = $('single-action-confirm').getBoundingClientRect();
      return { cost: p.quote.cost, affordable: p.quote.affordable, refund: p.refund, revision: state.revision,
        funds: state.factions.find(f => f.id === state.playerFaction).resources,
        rows: [...$('single-action-cost').children].map(row => ({ key: row.dataset.resource, value: Number(row.dataset.value), label: row.querySelector('.original-text').getAttribute('aria-label'),
          color: row.querySelector('.original-text').dataset.color, image: row.querySelector('img').getAttribute('src') })),
        fits: [r, b].every(v => v.width > 0 && v.height > 0 && v.x >= 0 && v.y >= 0 && v.right <= innerWidth + 1 && v.bottom <= innerHeight + 1) };
    });
  };
  try {
    await page.goto(base);await page.waitForFunction(() => startupReady);
    for (const op of ['train', 'upgrade', 'transport', 'disband']) {
      await seed(op === 'disband' ? 'disband' : 'rich');
      const quote = await open(op);
      check(quote.affordable && quote.fits, op + ' usable quote fits desktop');
      check(quote.rows.every(row => row.value === quote.cost[row.key]), op + ' every displayed amount equals quote');
      check(quote.rows.every(row => row.image.includes('marker_' + row.key + '.png')), op + ' matching resource icons');
      if (op === 'train') check(quote.rows.map(r => r.key).join() === 'money,food' && quote.cost.food > 0, 'training shows actual food, not industry');
      if (op === 'disband') check(quote.refund && quote.rows.every(row => row.label.startsWith('+')), 'disband displays positive refund');
      await shot(op + '-desktop');
      for (const size of [{ width: 844, height: 390 }, { width: 390, height: 844 }]) {
        await page.setViewportSize(size);
        await page.waitForFunction(() => {
          positionSingleAction();
          const p = $('single-action-cost').getBoundingClientRect(), b = $('single-action-confirm').getBoundingClientRect();
          return [p, b].every(r => r.width > 0 && r.height > 0 && r.x >= 0 && r.y >= 0 && r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1);
        }).catch(async error => { throw new Error(op + ' layout ' + JSON.stringify(await page.evaluate(() => ({
          pending: !!pendingAction, panel: $('single-action-cost').getBoundingClientRect().toJSON(),
          button: $('single-action-confirm').getBoundingClientRect().toJSON(), width: innerWidth, height: innerHeight
        }))) + ': ' + error.message); });
        check(true, op + ' fits ' + size.width + 'x' + size.height);
        await shot(op + '-' + size.width);
      }
      await page.setViewportSize({ width: 1280, height: 720 });
      await page.evaluate(() => { $('single-action-confirm').click();$('single-action-confirm').click(); });
      await page.waitForFunction(revision => !busy && state.revision > revision, quote.revision);
      const result = await page.evaluate(id => ({ revision: state.revision, funds: state.factions.find(f => f.id === state.playerFaction).resources,
        exists: state.units.some(u => u.id === id), hidden: $('single-action').hidden, modal: $('detail-modal').hidden }), fixture.target.id);
      check(result.revision === quote.revision + 1 && result.hidden && result.modal, op + ' confirms exactly once without modal');
      const sign = op === 'disband' ? -1 : 1;
      check(['money', 'industry', 'food'].every(key => quote.funds[key] - result.funds[key] === sign * quote.cost[key]), op + ' exact cost/refund applied');
      if (op === 'disband') check(!result.exists, 'disband removes selected unit');
    }
    for (const size of [{ width: 1280, height: 720 }, { width: 844, height: 390 }]) {
      await page.setViewportSize(size);
      await seed('allied-transport');
      const before = await page.evaluate(() => ({ owner: state.cells[selection.cell].owner,
        movement: selected.remainingMovement, allyFunds: state.factions.find(f => f.id === state.cells[selection.cell].owner).resources }));
      const quote = await open('transport');
      check(quote.affordable && quote.fits, 'allied transport quote fits ' + size.width);
      await page.locator('#single-action-confirm').click();
      await page.waitForFunction(revision => !busy && state.revision > revision, quote.revision);
      const after = await page.evaluate(() => ({ owner: state.cells[selection.cell].owner, movement: selected.remainingMovement,
        funds: state.factions.find(f => f.id === state.playerFaction).resources,
        allyFunds: state.factions.find(f => f.id === state.cells[selection.cell].owner).resources,
        available: selection.transport }));
      check(after.owner === before.owner && JSON.stringify(after.allyFunds) === JSON.stringify(before.allyFunds), 'allied transport preserves territory and ally funds');
      check(after.movement === before.movement && !after.available, 'allied transport preserves movement and prevents repeat purchase');
      check(['money', 'industry', 'food'].every(key => quote.funds[key] - after.funds[key] === quote.cost[key]), 'allied transport spends the displayed quote');
    }
    await page.setViewportSize({ width: 1280, height: 720 });
    await seed('poor');
    for (const op of ['train', 'upgrade', 'transport']) {
      const quote = await open(op);
      check(!quote.affordable && await page.locator('#single-action-confirm').isDisabled(), op + ' insufficient resources disable confirmation');
      check(quote.rows.some(row => row.value > 0 && row.color === '#ff3200'), op + ' missing resources shown red');
      await page.evaluate(() => $('single-action-confirm').click());
      check(await page.evaluate(revision => state.revision === revision, quote.revision), op + ' disabled confirmation cannot mutate state');
      await shot(op + '-poor');
    }
    await page.evaluate(() => window.ew4Back());
    check(await page.locator('#single-action').isHidden() && await page.locator('#pause').isHidden(), 'back cancels quote before opening pause');
    await seed('unavailable');
    check(await page.locator('#actions [data-action="train"],#actions [data-action="upgrade"],#actions [data-action="transport"]').count() === 0, 'ineligible actions have no entry');
    await seed('capped');
    check(await page.locator('#actions [data-action="train"]').count() === 0, 'maximum training level has no free upgrade entry');
    await seed('empty');
    const emptyQuote = await open('upgrade');
    check(await page.evaluate(() => !selected), 'empty building upgrade requires no unit');
    await page.locator('#single-action-confirm').click();
    await page.waitForFunction(revision => !busy && state.revision > revision, emptyQuote.revision);
    check(await page.locator('#single-action').isHidden(), 'empty building upgrades through direct confirmation');
    await seed('discount');
    const discounted = await open('upgrade');
    check(discounted.cost.money < fixture.quotes.upgrade.money, 'commander discount appears on quote');
    await page.locator('#actions [data-action="transport"]').click();
    check(await page.locator('#single-action').getAttribute('data-action') === 'transport', 'switching actions replaces old quote');
    check(await page.evaluate(cost => pendingAction.quote.cost.money < cost, fixture.quotes.transport.money), 'transport discount is displayed');
    await page.mouse.click(20, 150);
    check(await page.locator('#single-action').isHidden(), 'map click cancels pending confirmation');
    await seed('rich');await open('upgrade');
    await page.evaluate(() => menus.saveMenu(false, 2));
    check(await page.locator('#single-action').isHidden() && await page.locator('#detail-modal').isVisible(), 'opening another modal clears quote');
    await page.locator('#close-detail').click();
    const seedFort = async name => {
      await fs.copyFile(path.join(folder, name + '-slot.json'), path.join(runtime, 'slot-4.json'));
      return page.evaluate(async () => {
        await installState(await rpc('load', { slot: 4 }), true);
        const unit = state.units.find(u => u.id === 'warning.fort');
        if (!unit) throw new Error('No owned fort in ' + state.scenario);
        await selectCell(unit.cell, true);view.x = point(unit.cell).x;view.y = point(unit.cell).y;clampView();requestRender();
        return { id: unit.id, cell: unit.cell, revision: state.revision,
          funds: state.factions.find(f => f.id === state.playerFaction).resources };
      });
    };
    const fortBefore = await seedFort('fort-used');
    await page.locator('#actions [data-action="disband"]').click();
    await page.locator('#single-action-confirm').click();
    check(await page.locator('#detail-modal').isVisible(), 'used fort opens warning before disband');
    check(await page.locator('#detail-content .original-text').getAttribute('aria-label') === '解散后本回合内不能再建造要塞。', 'warning names remaining turn restriction');
    for (const size of [{width:1280,height:720},{width:844,height:390},{width:390,height:844}]) {
      await page.setViewportSize(size);
      await page.waitForTimeout(250);
      check(await page.evaluate(() => {
        const text = $('detail-content').firstElementChild.getBoundingClientRect();
        const confirm = $('detail-confirm').getBoundingClientRect();
        return [text,confirm].every(r => r.width > 0 && r.height > 0 && r.x >= 0 && r.y >= 0 && r.right <= innerWidth && r.bottom <= innerHeight)
          && text.bottom <= confirm.top;
      }), 'fort warning and confirm fit without overlap ' + size.width);
      await shot('fort-warning-' + size.width);
    }
    await page.setViewportSize({width:1280,height:720});
    await page.locator('#close-detail').click();
    check(await page.evaluate(({revision,id,funds}) => state.revision === revision && state.units.some(u => u.id === id)
      && JSON.stringify(state.factions.find(f => f.id === state.playerFaction).resources) === JSON.stringify(funds), fortBefore), 'canceling fort warning preserves unit, resources and state');
    const fortAgain = await seedFort('fort-used');
    await page.locator('#actions [data-action="disband"]').click();
    const fortRefund = await page.evaluate(() => pendingAction.quote.cost);
    await page.locator('#single-action-confirm').click();
    await page.locator('#detail-confirm').click();
    await page.waitForFunction(revision => !busy && state.revision > revision, fortAgain.revision);
    const afterFortDisband = await page.evaluate(id => ({
      revision: state.revision, exists: state.units.some(u => u.id === id), hidden: $('detail-modal').hidden,
      funds: state.factions.find(f => f.id === state.playerFaction).resources
    }), fortAgain.id);
    check(afterFortDisband.revision === fortAgain.revision + 1 && !afterFortDisband.exists && afterFortDisband.hidden,
      'confirming fort warning disbands exactly once');
    check(['money','industry','food'].every(k => afterFortDisband.funds[k] - fortAgain.funds[k] === fortRefund[k]), 'confirmed fort returns exact quoted resources');
    const unusedFortBefore = await seedFort('fort-unused');
    await page.locator('#actions [data-action="disband"]').click();
    const unusedFortQuote = await page.evaluate(() => pendingAction.quote);
    check(!unusedFortQuote.warning && await page.locator('#detail-modal').isHidden(), 'unused fort disband has no warning');
    await page.locator('#single-action-confirm').click();
    await page.waitForFunction(revision => !busy && state.revision > revision, unusedFortBefore.revision);
    check(await page.evaluate(id => $('detail-modal').hidden && !state.units.some(u => u.id === id), unusedFortBefore.id), 'unused fort confirms directly without warning');
    await seed('ordinary-fort-used');
    await page.locator('#actions [data-action="disband"]').click();
    const ordinaryPending = await page.evaluate(() => ({quote:pendingAction.quote,revision:pendingAction.revision}));
    check(!ordinaryPending.quote.warning && await page.locator('#detail-modal').isHidden(), 'ordinary unit has no fort warning');
    await page.locator('#single-action-confirm').click();
    await page.waitForFunction(revision => !busy && state.revision > revision, ordinaryPending.revision);
    check(await page.evaluate(id => $('detail-modal').hidden && !state.units.some(u => u.id === id), fixture.target.id), 'ordinary unit confirms directly despite used fort allowance');
    check(report.errors.length === 0 && report.missing.length === 0, 'no script or resource errors');
    console.log(JSON.stringify({ checks: report.checks, errors: report.errors.length, missing: report.missing.length, screenshots: report.screenshots.length }));
  } finally {
    await fs.writeFile(path.join(folder, 'browser.json'), JSON.stringify(report, null, 2));
    await browser.close();
  }
})().catch(error => { console.error(error.message);process.exitCode = 1; });
