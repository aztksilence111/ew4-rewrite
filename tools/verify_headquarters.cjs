'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');
const { chromium } = createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const base = process.argv[2] || 'http://127.0.0.1:5262';
const folder = path.resolve(process.argv[3] || path.join(__dirname, '../rewrite/reports/headquarters-mode-20261021'));
(async () => {
  await fs.mkdir(folder, { recursive: true });
  const report = { checks: 0, errors: [], missing: [], screenshots: [] };
  const check = (value, label) => { assert.ok(value, label); report.checks++; };
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe' });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 }, hasTouch: true });
  const selectFilter = async label => {
    await page.locator('#hq-filter-main').click();
    await page.getByRole('menuitemradio', { name: label, exact: true }).click();
    await page.waitForFunction(() => document.getElementById('hq-filter-shield').hidden);
  };
  page.on('pageerror', e => report.errors.push(e.message));
  page.on('response', r => { if (r.status() >= 400) report.missing.push(r.url()); });
  const shot = async name => {
    await page.evaluate(async () => { await Promise.all([...document.querySelectorAll('.popup-opening')].flatMap(el => el.getAnimations()).map(a => a.finished.catch(() => {}))); });
    await page.waitForFunction(() => [...document.querySelectorAll('#hq-roster img,#hq-filters img,#detail-modal:not([hidden]) img')].every(i => i.complete && i.naturalWidth));
    await page.screenshot({ path: path.join(folder, name + '.png') }); report.screenshots.push(name);
  };
  try {
    await page.goto(base); await page.waitForFunction(() => startupReady);
    await page.locator('#headquarters').click();
    await page.waitForFunction(() => menus.data?.roster);
    await page.evaluate(async () => {
      const section = d => Math.max(0, Math.min(2, Math.floor((d.rating - 1) / 3)));
      for (let group = 0; group < 3; group++) {
        while (menus.data.roster.filter(c => section(menus.data.definitions.find(d => d.id === c.definitionId)) === group).length < 2) {
          if (!menus.data.offers[group].length) await menus.updateAccount({ op: 'refresh', group });
          const id = menus.data.offers[group][0], d = menus.data.definitions.find(d => d.id === id);
          await menus.updateAccount({ op: d.price > 0 ? 'recruit' : 'recruit_badge', commander: id, group });
        }
      }
    });
    check(await page.locator('#hq-filters [role=menuitemradio]').count() === 4, 'four filters');
    check(await page.locator('#hq-roster .commander-card').count() === 60, 'all filter contains 60 slots');
    check(await page.locator('#hq-scrollbar').count() === 0, 'no visible scrollbar control');
    await page.evaluate(async () => { await menus.updateAccount({ op: 'tags', taggedCommanders: [] }); });
    const rosterOrder = () => page.locator('#hq-roster [data-commander]').evaluateAll(cards => cards.map(c => c.dataset.commander));
    const descendingSections = () => page.evaluate(() => {
      const sections = [...document.querySelectorAll('#hq-roster [data-commander]')].map(card => {
        const held = menus.data.roster.find(c => c.id === card.dataset.commander);
        const definition = menus.data.definitions.find(d => d.id === held.definitionId);
        return { section: Math.max(0, Math.min(2, Math.floor((definition.rating - 1) / 3))), tagged: !!held.tag };
      });
      return sections.every((c, i) => !i || (sections[i - 1].tagged && !c.tagged)
        || (sections[i - 1].tagged === c.tagged && sections[i - 1].section >= c.section));
    });
    const initialOrder = await rosterOrder();
    check(await descendingSections(), 'unmarked roster sorts three, two, then one star');
    const tagged = await page.evaluate(() => menus.data.roster[1]);
    const taggedCard = () => page.locator('#hq-roster [data-commander="' + tagged.id + '"]');
    const baseline = await page.evaluate(() => menus.data.revision);
    await page.locator('#hq-tags').click();
    check(await page.locator('#hq-tags').getAttribute('aria-label') === '确认标签', 'same button becomes explicit confirmation');
    check(await page.locator('#hq-tags .original-text').getAttribute('aria-label') === '确认', 'confirmation has visible text');
    await taggedCard().locator('.commander-open').click();
    check(await taggedCard().locator('.commander-tag').count() === 1, 'click adds triangle');
    check(await page.locator('#detail-modal').isHidden(), 'tag click does not open detail');
    await taggedCard().locator('.commander-open').click();
    check(await taggedCard().locator('.commander-tag').count() === 0, 'second click removes triangle');
    await taggedCard().locator('.commander-open').click();
    check(await page.evaluate(() => menus.data.revision) === baseline, 'clicks do not write before confirmation');
    check(await taggedCard().evaluate(el => {
      const marker = el.querySelector('.commander-tag').getBoundingClientRect(), r = el.getBoundingClientRect();
      return Math.abs(marker.left - r.left) < 1 && Math.abs(marker.bottom - r.bottom) < 1;
    }), 'triangle is at slot bottom left');
    check(JSON.stringify(await rosterOrder()) === JSON.stringify(initialOrder), 'editing does not reorder cards');
    await selectFilter('二星区');
    await selectFilter('全选');
    check(await taggedCard().locator('.commander-tag').count() === 1, 'draft survives filter changes');
    await shot('tag-mode-desktop');
    await page.route('**/api/commander?*', route => route.fulfill({ json: { ok: false, code: 'test.failure', message: '保存失败测试' } }));
    await page.locator('#hq-tags').click();
    await page.waitForFunction(() => !menus.tagSaving);
    check(await page.evaluate(() => !!menus.tagDraft) && await taggedCard().locator('.commander-tag').count() === 1, 'failed save retains selection and mode');
    await page.unroute('**/api/commander?*');
    await page.locator('#hq-tags').click();
    await page.waitForFunction(() => !menus.tagDraft && !menus.tagSaving);
    check(await page.evaluate(() => menus.data.revision) === baseline + 1, 'confirmation saves once');
    check(await page.locator('#hq-roster .commander-card').first().getAttribute('data-commander') === tagged.id, 'tagged commander sorts first');
    check(await descendingSections(), 'saved roster keeps tag priority then descending stars');
    await page.reload(); await page.waitForFunction(() => startupReady);
    await page.locator('#headquarters').click();
    await page.waitForFunction(() => menus.data?.roster);
    check(await page.locator('#hq-roster .commander-card').first().getAttribute('data-commander') === tagged.id, 'pin remains first after reload');
    for (const [name, width, height] of [['desktop', 1280, 720], ['landscape', 844, 390], ['portrait', 390, 844]]) {
      await page.setViewportSize({ width, height });
      await selectFilter('全选');
      await shot(name + '-all');
      check(await page.locator('#hq-tags').evaluate(el => {
        const r = el.getBoundingClientRect(), shop = document.getElementById('hq-shop').getBoundingClientRect(), count = document.getElementById('hq-count').getBoundingClientRect();
        return r.left >= shop.right && r.right < count.left;
      }), name + ' tag button sits right of shop without overlap');
      await page.locator('#hq-tags').click();
      const geometry = await page.locator('#hq-roster').evaluate(el => {
        const r = el.getBoundingClientRect(), s = r.width / el.offsetWidth, cards = [...el.children].slice(0, 12).map(c => c.getBoundingClientRect());
        return { rows: new Set(cards.map(c => Math.round(c.top))).size, contained: cards.every(c => c.top >= r.top - 1 && c.bottom <= r.bottom + 1),
          alternating: cards.every((c, i) => Math.abs(c.left - cards[i - i % 2].left) < 1
            && (i % 2 ? c.top > cards[i - 1].top : !i || c.left > cards[i - 2].left)),
          left: r.left, top: r.top, width: r.width, height: r.height, scale: s };
      });
      check(geometry.rows === 2 && geometry.contained, name + ' exactly two fully visible rows');
      check(geometry.alternating, name + ' columns fill top then bottom before moving right');
      check(await page.locator('#hq-filters').evaluate(el => {
        const children = [...el.querySelectorAll(':scope>button')].map(e => e.getBoundingClientRect()), recruit = document.getElementById('hq-recruit').getBoundingClientRect();
        return children.every((r, i) => r.right <= recruit.left && (!i || r.left >= children[i - 1].right));
      }), name + ' filters do not overlap');
      // Start on a portrait to verify drag suppresses the ordinary detail click.
      const startX = geometry.left + 250 * geometry.scale, y = geometry.top + 45 * geometry.scale;
      await page.mouse.move(startX, y); await page.mouse.down();
      await page.mouse.move(startX - 180 * geometry.scale, y, { steps: 12 }); await page.mouse.up();
      check(await page.locator('#hq-roster').evaluate(el => el.scrollLeft > 150), name + ' drag scrolls across card area');
      check(await page.locator('#detail-modal').isHidden(), name + ' dragging does not open detail');
      check(await page.evaluate(() => menus.tagDraft.size) === 1, name + ' dragging does not toggle tags');
      await shot(name + '-dragged');
      const cdp = await page.context().newCDPSession(page);
      const beforeTouch = await page.locator('#hq-roster').evaluate(el => el.scrollLeft);
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x: startX, y }] });
      for (let step = 1; step <= 10; step++) await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x: startX - step * 12 * geometry.scale, y }] });
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
      check(await page.locator('#hq-roster').evaluate((el, before) => el.scrollLeft > before + 60, beforeTouch), name + ' touch swipe scrolls');
      check(await page.locator('#detail-modal').isHidden(), name + ' touch swipe does not open detail');
      check(await page.evaluate(() => menus.tagDraft.size) === 1, name + ' touch swipe does not toggle tags');
      await cdp.detach();
      for (const label of ['一星区', '二星区', '三星区']) {
        await selectFilter(label);
        check(await page.locator('#hq-roster .commander-card').count() === 20, name + ' ' + label + ' 20 slots');
        check(await page.locator('#hq-roster .commander-card:not(.empty-commander)').count() === 2, name + ' ' + label + ' correct members');
        check(await page.locator('#hq-roster').evaluate(el => el.scrollLeft === 0), name + ' filter resets scroll');
      }
      await shot(name + '-three-star');
      const favorite = page.locator('#hq-roster .commander-open').first();
      await favorite.click();
      check(await page.evaluate(() => menus.tagDraft.size) === 2, name + ' mode accepts more than one favorite');
      await favorite.click();
      check(await page.evaluate(() => menus.tagDraft.size) === 1, name + ' mode toggles favorite off');
      await page.locator('#hq-tags').click();
      await page.waitForFunction(() => !menus.tagDraft && !menus.tagSaving);
      await page.locator('#hq-roster .commander-open').first().click();
      check(await page.locator('#detail-modal').isVisible(), name + ' click still opens detail');
      check(await page.locator('.commander-tag-editor,#commander-tag').count() === 0, name + ' detail contains no tag editor');
      await shot(name + '-detail'); await page.locator('#close-detail').click();
    }
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.evaluate(() => {
      menus.rosterRating = null;
      const original = menus.data.roster.slice();
      menus.data.roster = Array.from({ length: 60 }, (_, i) => ({ ...original[i % original.length], id: 'fixture.' + i }));
      menus.drawRoster();
      const roster = document.getElementById('hq-roster'); roster.scrollLeft = roster.scrollWidth;
    });
    check(await page.locator('#hq-roster .commander-card:not(.empty-commander)').count() === 60, '60 owned commanders render without truncation');
    check(await page.locator('#hq-roster').evaluate(el => {
      const last = el.lastElementChild.getBoundingClientRect(), r = el.getBoundingClientRect();
      return last.right <= r.right + 1 && last.left >= r.left && last.bottom <= r.bottom + 1;
    }), 'last slot is reachable');
    await shot('full-roster-last-page');
    await page.evaluate(async () => { menus.data = await rpc('headquarters'); menus.drawRoster(); });
    await selectFilter('全选');
    await page.locator('#hq-tags').click();
    await taggedCard().locator('.commander-open').click();
    await page.locator('#hq-back').click();
    check(await page.locator('#hq-screen').isVisible() && await taggedCard().locator('.commander-tag').count() === 1, 'back discards draft and keeps saved tag');
    await page.locator('#hq-tags').click();
    await taggedCard().locator('.commander-open').click();
    await page.locator('#hq-tags').click();
    await page.waitForFunction(() => !menus.tagDraft && !menus.tagSaving);
    check(await page.evaluate(() => menus.data.roster.every(c => !c.tag)), 'confirmation persists removing last tag');
    await page.locator('#hq-recruit').click();
    check(await page.locator('#hq-filters [role=tab]').count() === 3, 'academy retains three original filters');
    await shot('academy');
    check(report.errors.length === 0, 'no browser errors');
    check(report.missing.length === 0, 'no missing images');
  } catch (error) { report.failure = error.message; process.exitCode = 1; }
  finally { await fs.writeFile(path.join(folder, 'browser.json'), JSON.stringify(report, null, 2)); await browser.close(); }
  console.log(JSON.stringify(report));
})().catch(e => { console.error(e.message); process.exitCode = 1; });
