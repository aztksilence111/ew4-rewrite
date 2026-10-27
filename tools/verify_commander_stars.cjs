'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');
const { chromium } = createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const [url, fixture, output] = process.argv.slice(2);
let checks = 0;
const errors = [];
function check(pass, label) { assert.ok(pass, label); checks++; }
(async () => {
  const headquarters = JSON.parse(await fs.readFile(fixture, 'utf8'));
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', args: ['--mute-audio'] });
  try {
    const page = await browser.newPage();
    page.on('pageerror', e => errors.push(e.message));
    page.on('response', r => { if (r.status() >= 400) errors.push(r.status() + ' ' + r.url()); });
    await page.goto(url);
    await page.waitForFunction(() => startupReady && catalog && manifest, null, { timeout: 120000 });
    for (const viewport of [{ width: 1280, height: 720 }, { width: 844, height: 390 }, { width: 390, height: 844 }]) {
      await page.setViewportSize(viewport);
      await page.evaluate(data => {
        menus.data = data;
        menus.commanderInfo(data.definitions.find(c => c.legacyId === 1), data.roster[0]);
      }, headquarters);
      for (const [i, ability] of ['infantry', 'cavalry', 'artillery', 'naval', 'fort', 'commerce', 'movement'].entries()) {
        const row = page.locator('.commander-ability[data-ability="' + ability + '"]');
        check(await row.getAttribute('aria-label') === ability + ' ' + (i + 6), 'Exact imported value: ' + ability);
        check(await row.locator('.ability-stars>img').count() === 1, 'Bounded star count: ' + ability);
        check(await row.locator('.original-text').getAttribute('aria-label') === '\u00d7' + (i + 6), 'Exact visible count: ' + ability);
      }
      await page.screenshot({ path: path.join(output, 'commander-stars-' + viewport.width + '.png') });
      for (const value of [0, 1, 5, 6, 12, 99, 1000, 2147483647]) {
        await page.evaluate(value => {
          const definition = menus.data.definitions.find(c => c.legacyId === 1);
          const abilities = Object.fromEntries(Object.keys(definition.abilities).map(key => [key, value]));
          menus.commanderInfo({ ...definition, abilities, training: value }, null);
        }, value);
        const rows = await page.locator('.commander-ability').evaluateAll(elements => elements.map(el => {
          const stars = el.querySelector('.ability-stars'), bounds = el.getBoundingClientRect();
          return { value: Number(stars.dataset.value), images: stars.querySelectorAll('img').length,
            label: stars.querySelector('.original-text')?.getAttribute('aria-label'),
            fits: [...stars.children].every(child => { const r = child.getBoundingClientRect(); return r.left >= bounds.left && r.right <= bounds.right + 1 && r.top >= bounds.top && r.bottom <= bounds.bottom + 1; }) };
        }));
        check(rows.length === 8 && rows.every(r => r.value === value && r.images === (value > 5 ? 1 : value)
          && r.fits && (value <= 5 || r.label === '\u00d7' + value)), 'Full value fits at ' + value + ' / ' + viewport.width);
      }
    }
    check(errors.length === 0, 'No browser or asset errors: ' + errors.join('; '));
    await fs.writeFile(path.join(output, 'checks.json'), JSON.stringify({ checks, errors }, null, 2));
    console.log(JSON.stringify({ checks, errors }));
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
