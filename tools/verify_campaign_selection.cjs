'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');
const { chromium } = createRequire('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/_probe.cjs')('playwright');
const url = process.argv[2];
const output = path.resolve(process.argv[3]);
const report = { checks: 0, errors: [], missing: [], chapters: [], branches: [], views: [] };
function check(value, message) { assert.ok(value, message); report.checks++; }
(async () => {
  await fs.mkdir(output, { recursive: true });
  const browser = await chromium.launch({ headless: true, executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', args: ['--mute-audio'] });
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
    const checkMenuLabels = async () => {
      for (const selector of ['#mode-title', '#continue']) {
        const data = await page.locator(selector).evaluate(el => {
          const canvas = el.querySelector('canvas'), rect = canvas.getBoundingClientRect(), control = el.getBoundingClientRect();
          const pixels = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
          let top = canvas.height, bottom = -1;
          for (let y = 0; y < canvas.height; y++) for (let x = 0; x < canvas.width; x++) if (pixels[(y * canvas.width + x) * 4 + 3] > 16) { top = Math.min(top, y); bottom = Math.max(bottom, y); }
          return { error: Math.abs(rect.top + (top + bottom + 1) / 2 / canvas.height * rect.height - (control.top + control.bottom) / 2), visible: bottom >= top,
            contained: rect.top >= control.top - 1 && rect.bottom <= control.bottom + 1, size: Number(el.querySelector('.original-text').dataset.size) };
        });
        check(data.visible && data.error <= 1.5 && data.contained && data.size <= 16, 'Menu title/load ink is centered and sized: ' + selector + ' ' + JSON.stringify(data));
      }
    };
    page.on('pageerror', e => report.errors.push(e.message));
    page.on('response', r => { if (r.status() >= 400) report.missing.push([r.status(), r.url()]); });
    await page.goto(url);
    await page.waitForFunction(() => startupReady && catalog && manifest, null, { timeout: 120000 });
    await page.locator('.main-entry[data-mode="1"]').click();
    await checkMenuLabels();
    const chapters = await page.evaluate(() => catalog.chapters);
    const presentations = await page.evaluate(() => Object.values(catalog.presentations).filter(p => p.chapter > 0));
    const branches = presentations.filter(p => p.alternate && p.countries.length);
    check(branches.length > 0, 'Catalog exposes alternate country branches');
    for (let i = 0; i < chapters.length; i++) {
      await page.locator('.chapter-entry').nth(i).click();
      await page.locator('.chapter-preview-confirm').click();
      await page.locator('#campaign-screen').waitFor();
      check(await page.locator('#campaign-title').evaluate(el => {
        const title = el.getBoundingClientRect(), canvas = el.querySelector('canvas').getBoundingClientRect();
        return title.top >= -1 && canvas.top >= title.top - 1 && canvas.bottom <= title.bottom + 1 && canvas.left >= title.left - 1 && canvas.right <= title.right + 1;
      }), 'Complete chapter title fits header: ' + chapters[i].id);
      const count = await page.locator('#campaign-battles>[data-campaign]').count();
      const visible = await page.evaluate(chapter => Object.values(catalog.presentations).filter(p => p.chapter === chapter && (!p.hidden || catalog.unlockedCampaigns.includes(p.id))).length, chapters[i].id);
      check(count === visible && count > 0, 'Chapter list matches imported catalog: ' + chapters[i].id);
      report.chapters.push({ chapter: chapters[i], entries: count });
      await page.locator('#campaign-back').click();
    }
    await page.evaluate(() => { window.selectionCalls = []; window.originalSelectionStart = menus.api.start; menus.api.start = async id => window.selectionCalls.push(id); });
    for (const viewport of [{ width: 2376, height: 1080 }, { width: 1280, height: 720 }, { width: 844, height: 390 }, { width: 390, height: 844 }]) {
      await page.setViewportSize(viewport);
      await checkMenuLabels();
      await page.screenshot({ path: path.join(output, 'campaign-menu-' + viewport.width + '.png') });
      for (let index = 0; index < chapters.length; index++) {
        await page.locator('.chapter-entry').nth(index).click();
        await page.locator('.chapter-preview-confirm').click();
        await page.locator('#campaign-screen').waitFor();
        check(await page.locator('#campaign-title').evaluate(el => {
          const title = el.getBoundingClientRect(), text = el.querySelector('canvas').getBoundingClientRect();
          return text.top >= title.top - 1 && text.bottom <= title.bottom + 1 && text.left >= 0 && text.right <= innerWidth;
        }), 'Chapter title fits each viewport: ' + chapters[index].name + ' ' + viewport.width);
        await page.locator('#campaign-battles').evaluate(el => Promise.all(el.getAnimations({ subtree: true }).map(a => a.finished.catch(() => {}))));
        for (const edge of ['first', 'last']) {
          const row = page.locator('#campaign-battles>[data-campaign]')[edge]();
          const beforeSize = await row.evaluate(el => ({ width: el.offsetWidth, height: el.offsetHeight }));
          await row.click();
          await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
          check(await row.evaluate(el => {
            const r = el.getBoundingClientRect(), list = el.parentElement.getBoundingClientRect();
            return el.parentElement.scrollLeft === 0 && r.left >= list.left - 1 && r.top >= list.top - 1 && r.bottom <= list.bottom + 1 && document.getElementById('menu-stage').scrollTop === 0;
          }), 'Chapter first/last selected card retains its left corner: ' + chapters[index].name + ' ' + edge);
          check(await row.evaluate((el, before) => {
            const rect = el.getBoundingClientRect(), list = el.parentElement.getBoundingClientRect();
            const other = el.parentElement.querySelector('[aria-pressed="false"]');
            const image = getComputedStyle(el).backgroundImage;
            const name = Object.keys(manifest.atlases.image_menu_hd).find(n => image.includes('/' + n));
            const art = manifest.atlases.image_menu_hd[name];
            return before.width === el.offsetWidth && before.height === el.offsetHeight && other.offsetWidth === el.offsetWidth && other.offsetHeight === el.offsetHeight
              && Math.abs(parseFloat(getComputedStyle(el).width) - art.w / 2) < .01 && Math.abs(parseFloat(getComputedStyle(el).height) - art.h / 2) < .01
              && Math.abs(rect.right - list.right) <= 1 && Math.abs(list.right - innerWidth) <= 1 && other.getBoundingClientRect().left > rect.left + 5;
          }, beforeSize), 'Selection moves original-size art left while covering screen right edge: ' + chapters[index].name + ' ' + edge);
        }
        await page.locator('#campaign-back').click();
      }
      for (const branch of branches) {
        const chapterIndex = chapters.findIndex(c => c.id === branch.chapter);
        await page.locator('.chapter-entry').nth(chapterIndex).click();
        await page.locator('.chapter-preview-confirm').click();
        await page.locator('#campaign-screen').waitFor();
        await page.locator('#campaign-battles').evaluate(el => Promise.all(el.getAnimations({ subtree: true }).map(a => a.finished.catch(() => {}))));
        const row = page.locator('[data-campaign="' + branch.id + '"]');
        await row.click();
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(resolve)));
        const menuFits = await row.evaluate(el => {
          const row = el.getBoundingClientRect(), list = el.parentElement.getBoundingClientRect();
          const header = document.querySelector('#campaign-screen>.menu-bar').getBoundingClientRect();
          const next = el.parentElement.querySelector('[aria-pressed="false"]').getBoundingClientRect();
          const data = { stage: document.getElementById('menu-stage').scrollTop, scrollLeft: el.parentElement.scrollLeft, row: row.toJSON(), list: list.toJSON(), next: next.toJSON(), header: header.toJSON() };
          const fits = data.stage === 0 && data.scrollLeft === 0 && header.top >= -1 && row.left >= list.left - 1 && next.left > row.left + 5 && row.top >= list.top - 1 && row.bottom <= list.bottom + 1;
          return { fits, data };
        });
        check(menuFits.fits, 'Selected campaign and header are not clipped: ' + JSON.stringify(menuFits.data));
        check(await row.getAttribute('aria-pressed') === 'true', 'Campaign row selected: ' + branch.id);
        await page.locator('#campaign-confirm').click();
        const popup = page.locator('[data-layout="form_selcountry"]');
        await popup.waitFor();
        check(await page.locator('#detail-modal').evaluate(el => getComputedStyle(el).backgroundColor === 'rgba(0, 0, 0, 0)'), 'Original fade=0 leaves campaign visible');
        check(await popup.locator('[data-native-id="group_select"]').evaluate(el => getComputedStyle(el).borderImageSlice === '20'), 'Original selected corner is preserved');
        check(await popup.locator('[data-native-id="btn_left"]').getAttribute('aria-pressed') === 'true', 'Left branch initially selected');
        for (const side of ['btn_right', 'btn_left']) {
          await popup.locator('[data-native-id="' + side + '"]').click();
          check(await popup.locator('[data-native-id="' + side + '"]').getAttribute('aria-pressed') === 'true', 'Country selection toggles');
        }
        const geometry = await popup.evaluate(async el => {
          await Promise.all([...el.querySelectorAll('img')].map(img => img.decode()));
          const dialog = el.closest('.dialog').getBoundingClientRect();
          const controls = [...el.querySelectorAll('[data-native-id="btn_left"],[data-native-id="btn_right"],[data-native-id="group_select"]')];
          return { fits: dialog.left >= -1 && dialog.top >= -1 && dialog.right <= innerWidth + 1 && dialog.bottom <= innerHeight + 1,
            controlsFit: controls.every(b => { const r = b.getBoundingClientRect(); return r.width > 0 && r.height > 0 && r.left >= dialog.left - 1 && r.right <= dialog.right + 1 && r.top >= dialog.top - 1 && r.bottom <= dialog.bottom + 1; }),
            missing: [...el.querySelectorAll('img')].filter(img => !img.naturalWidth).map(img => img.src) };
        });
        check(geometry.fits && geometry.controlsFit && !geometry.missing.length, 'Country dialog and assets fit viewport');
        await page.screenshot({ path: path.join(output, branch.id + '-' + viewport.width + '.png') });
        const before = await page.evaluate(() => selectionCalls.length);
        await page.locator('#close-detail').click();
        check(await page.locator('#detail-modal').isHidden() && await page.evaluate(() => selectionCalls.length) === before, 'Cancel starts no battle');
        for (const index of [0, 1]) {
          await page.locator('#campaign-confirm').click();
          await popup.waitFor();
          await popup.locator('[data-native-id="' + (index ? 'btn_right' : 'btn_left') + '"]').click();
          await page.locator('#detail-confirm').click();
          check(await page.locator('#detail-modal').isHidden(), 'Confirm closes selector');
          check(await page.evaluate(() => selectionCalls.at(-1)) === (index ? branch.alternate : branch.id), 'Confirm routes to correct BTL');
        }
        report.views.push({ viewport, id: branch.id, countries: branch.countries });
        await page.locator('#campaign-back').click();
      }
    }
    await page.locator('.chapter-entry').nth(2).click();
    await page.locator('.chapter-preview-confirm').click();
    await page.locator('#campaign-screen').waitFor();
    await page.evaluate(() => { document.getElementById('menu-stage').style.overflow = 'hidden'; });
    await page.locator('#campaign-battles').evaluate(el => Promise.all(el.getAnimations({ subtree: true }).map(a => a.finished.catch(() => {}))));
    await page.locator('#campaign-battles>[data-campaign]').last().click();
    await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
    check(await page.evaluate(() => document.getElementById('menu-stage').scrollTop === 0 && document.getElementById('menu-stage').scrollLeft === 0), 'Chrome 70 overflow fallback cannot displace menu');
    await page.locator('[data-campaign="' + branches[0].id + '"]').click();
    await page.locator('#campaign-confirm').click();
    await page.locator('[data-layout="form_selcountry"]').waitFor();
    await page.locator('#close-detail').click();
    await page.locator('#campaign-upgrade').click();
    await page.locator('.campaign-technology').waitFor();
    check(await page.locator('#detail-modal').evaluate(el => getComputedStyle(el).backgroundColor !== 'rgba(0, 0, 0, 0)'), 'Country appearance does not leak into other dialogs');
    await page.locator('#close-detail').click();
    // Exercise real game startup after verifying UI routing independently.
    await page.evaluate(() => { menus.api.start = window.originalSelectionStart; });
    await page.setViewportSize({ width: 1280, height: 720 });
    for (const branch of branches) for (const id of [branch.id, branch.alternate]) {
      const view = await page.evaluate(async id => rpc('start', { scenario: id }), id);
      check(view.scenario === id && view.factions.some(f => f.id === view.playerFaction), 'Actual branch backend starts: ' + id);
      report.branches.push({ id, country: view.factions.find(f => f.id === view.playerFaction).country });
    }
    // Verify both branches through the original frontend callback too.
    const screenshotBranch = branches[0];
    for (const side of ['btn_left', 'btn_right']) {
      await page.reload();
      await page.waitForFunction(() => startupReady && catalog, null, { timeout: 120000 });
      await page.locator('.main-entry[data-mode="1"]').click();
      await page.locator('.chapter-entry').nth(chapters.findIndex(c => c.id === screenshotBranch.chapter)).click();
      await page.locator('.chapter-preview-confirm').click();
      await page.locator('[data-campaign="' + screenshotBranch.id + '"]').click();
      await page.locator('#campaign-confirm').click();
      await page.locator('[data-layout="form_selcountry"] [data-native-id="' + side + '"]').click();
      await page.locator('#detail-confirm').click();
      const id = side === 'btn_left' ? screenshotBranch.id : screenshotBranch.alternate;
      await page.waitForFunction(id => state?.scenario === id && !busy, id, { timeout: 60000 });
      check(await page.locator('#play').isVisible(), 'Real frontend opens branch battlefield: ' + id);
    }
    check(!report.errors.length, 'No browser exceptions: ' + report.errors.join(';'));
    check(!report.missing.length, 'No missing resources: ' + JSON.stringify(report.missing));
    console.log(JSON.stringify(report));
  } finally {
    await fs.writeFile(path.join(output, 'verification.json'), JSON.stringify(report, null, 2));
    await browser.close();
  }
})().catch(e => { console.error(e.message); process.exitCode = 1; });
