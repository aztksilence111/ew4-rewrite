const { chromium } = require('C:/Users/WIN11/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs = require('node:fs');
const path = require('node:path');
const home = path.resolve(__dirname, '..');
const base = process.argv[2] || 'http://127.0.0.1:5206';
(async () => {
  const browser = await chromium.launch({ headless: true });
  const results = [];
  try {
    for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
      const page = await browser.newPage({ viewport });
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(base);
      await page.waitForFunction(() => document.querySelector('[data-mode="2"]').getAttribute('aria-label'));
      await page.locator('[data-mode="2"]').click();
      await page.locator('[data-scenario="conquest7"]').waitFor();
      await page.screenshot({ path: path.join(home, `reports/conquests-${viewport.width}.png`) });
      await page.locator('#conquest-next').click();
      await page.locator('[data-scenario="conquest13"]').waitFor();
      await page.screenshot({ path: path.join(home, `reports/conquests-page2-${viewport.width}.png`) });
      await page.locator('[data-scenario="conquest13"]').click();
      await page.locator('#country-list button').first().waitFor();
      await page.locator('#country-list button').first().click();
      await page.locator('#confirm-country').click();
      await page.locator('#play').waitFor({ state: 'visible' });
      await page.waitForFunction(() => {
        const c = document.querySelector('#map');
        if (!c || c.width < 100) return false;
        const ctx = c.getContext('2d');
        const data = ctx.getImageData(0, 0, c.width, c.height).data;
        const colors = new Set();
        for (let i = 0; i < data.length; i += 400) if (data[i + 3]) colors.add(`${data[i]},${data[i + 1]},${data[i + 2]}`);
        return colors.size > 80;
      });
      await page.screenshot({ path: path.join(home, `reports/battle-${viewport.width}.png`) });
      const pixel = await page.locator('#map').evaluate(c => ({ width: c.width, height: c.height }));
      const brokenImages = await page.locator('img:visible').evaluateAll(images => images.filter(i => !i.complete || i.naturalWidth === 0).map(i => i.src));
      if (errors.length || brokenImages.length) throw new Error(JSON.stringify({ errors, brokenImages }));
      results.push({ viewport, canvas: pixel, errors, brokenImages });
      await page.close();
    }
    fs.writeFileSync(path.join(home, 'reports/browser-validation.json'), JSON.stringify({ status: 'passed', results }, null, 2));
    console.log(JSON.stringify(results));
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
