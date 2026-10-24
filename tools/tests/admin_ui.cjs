// Run with Node and Playwright installed; no live service or credentials needed.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../../reservation-service/web');

(async () => {
  fs.mkdirSync(path.resolve('artifacts'), {recursive:true});
  const browser = await chromium.launch({ headless: true, channel: process.env.BROWSER_CHANNEL || undefined });
  try {
    for (const size of [{width:1280,height:800},{width:390,height:844}]) {
      const page = await browser.newPage({ viewport: size });
      const errors = []; page.on('pageerror', e => errors.push(e.message));
      let offline = false, healthCalls = 0, serverVersion = '2.1.0';
      await page.route('http://localhost/**', async route => {
        const url = new URL(route.request().url());
        if (url.pathname === '/health') {
          healthCalls++;
          if (offline) return route.abort();
          return route.fulfill({json:{ok:true},headers:{'X-ASN-Version':serverVersion}});
        }
        if (url.pathname.startsWith('/api/')) {
          if (offline) return route.abort();
          return route.fulfill({json:{version:'2.1.0',paperlessHighWater:1,paperlessMax:1,total:0,items:[]},headers:{'X-ASN-Version':serverVersion}});
        }
        const file = path.join(root,url.pathname === '/admin/' ? 'index.html' : path.basename(url.pathname));
        const type = {'.js':'text/javascript','.css':'text/css','.html':'text/html','.ico':'image/x-icon','.ttf':'font/ttf'}[path.extname(file)];
        let body = fs.readFileSync(file);
        if (file.endsWith('.html')) body = Buffer.from(body.toString().replaceAll('__ASN_VERSION__','2.1.0'));
        await route.fulfill({body,contentType:type});
      });
      await page.clock.install();
      await page.goto('http://localhost/admin/');
      await page.locator('#key').fill('test-only');
      await page.locator('#loginForm button').click();
      await page.locator('#workspace').waitFor({state:'visible'});
      await page.waitForFunction(() => document.getElementById('refresh').disabled === false);
      assert.equal(await page.locator('footer a').first().getAttribute('href'),'https://nightmind.dev');
      const footer = await page.locator('footer').boundingBox();
      assert(Math.abs(footer.y + footer.height - size.height) <= 1);
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
      offline = true;
      await page.locator('#refresh').click();
      await page.locator('#connectionDialog').waitFor({state:'visible'});
      offline = false;
      await page.clock.runFor(10500);
      await page.locator('#connectionDialog').waitFor({state:'hidden'});
      assert.equal(await page.locator('#updateDialog').isVisible(),false,'Same-version recovery does not require reload');
      await page.waitForFunction(() => document.getElementById('refresh').disabled === false);
      offline = true;
      await page.locator('#refresh').click();
      await page.locator('#connectionDialog').waitFor({state:'visible'});
      await page.clock.runFor(121000);
      await page.locator('#reloadConnection').waitFor({state:'visible'});
      const stoppedAt = healthCalls;
      offline = false;
      await page.clock.runFor(40000);
      assert.equal(healthCalls,stoppedAt,'No retry after two-minute deadline');
      assert(await page.locator('#connectionDialog').isVisible());
      assert.equal(await page.locator('#connectionSpinner').isVisible(),false);
      await page.screenshot({path:path.resolve('artifacts/admin-offline-'+size.width+'.png')});
      assert.deepEqual(errors,[]);
      await page.reload();
      await page.locator('#key').fill('test-only');
      await page.locator('#loginForm button').click();
      await page.locator('#workspace').waitFor({state:'visible'});
      await page.waitForFunction(() => document.getElementById('refresh').disabled === false);
      offline = true;
      await page.locator('#refresh').click();
      await page.locator('#connectionDialog').waitFor({state:'visible'});
      offline = false; serverVersion = '2.2.0';
      await page.clock.runFor(10500);
      await page.locator('#updateDialog').waitFor({state:'visible'});
      assert.equal(await page.locator('#connectionDialog').isVisible(),false);
      await page.close();
    }
    console.log('Admin UI: desktop/mobile footer, offline timeout and reload prompt passed.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
