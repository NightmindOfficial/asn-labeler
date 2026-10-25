// Run with Node and Playwright installed; no live service or credentials needed.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../../reservation-service/web');
const version = fs.readFileSync(path.join(root, '../VERSION'), 'utf8').trim();

(async () => {
  fs.mkdirSync(path.resolve('artifacts'), {recursive:true});
  const browser = await chromium.launch({ headless: true, channel: process.env.BROWSER_CHANNEL || undefined });
  try {
    for (const size of [{width:1280,height:800},{width:390,height:844}]) {
      const page = await browser.newPage({ viewport: size });
      const errors = []; page.on('pageerror', e => errors.push(e.message));
      let offline = false, healthCalls = 0, serverVersion = version, documentFailure = false, logCalls = 0;
      await page.addInitScript(() => Object.defineProperty(navigator, 'clipboard', {value:{writeText:async text => {window.copiedEntry = text;}}}));
      await page.route('http://localhost/**', async route => {
        const url = new URL(route.request().url());
        if (url.pathname === '/health') {
          healthCalls++;
          if (offline) return route.abort();
          return route.fulfill({json:{ok:true},headers:{'X-ASN-Version':serverVersion}});
        }
        if (url.pathname.startsWith('/api/')) {
          if (offline) return route.abort();
          if (url.pathname === '/api/admin/logs') logCalls++;
          const items = url.pathname === '/api/documents/status'
            ? [0,1,2,231389271893].map((copies,i)=>({barcodeText:'ASN0000'+(4-i),status:'imported',documentTitle:'Beispieldokument '+i,reservationId:'example'+i,secondaryLabelCount:copies,updatedAt:1792800000}))
            : url.pathname === '/api/admin/logs' ? [{time:1792800000,event:'service_started',version}] : [];
          return route.fulfill({json:{version,paperlessHighWater:1,paperlessMax:1,total:items.length,items},headers:{'X-ASN-Version':serverVersion}});
        }
        if (['/admin/license','/admin/notices','/admin/changelog'].includes(url.pathname)) {
          if (documentFailure) return route.fulfill({status:502,contentType:'text/html',body:'<html>Gateway error</html>'});
          return route.fulfill({contentType:'text/plain; charset=utf-8',body:('Example <script>alert(1)</script>\nLong document line for readable scrolling.\n').repeat(100)});
        }
        const file = path.join(root,url.pathname === '/admin/' ? 'index.html' : path.basename(url.pathname));
        const type = {'.js':'text/javascript','.css':'text/css','.html':'text/html','.ico':'image/x-icon','.ttf':'font/ttf'}[path.extname(file)];
        let body = fs.readFileSync(file);
        if (file.endsWith('.html')) body = Buffer.from(body.toString().replaceAll('__ASN_VERSION__',version));
        await route.fulfill({body,contentType:type});
      });
      await page.clock.install();
      await page.goto('http://localhost/admin/');
      // Public information stays available before authentication, without navigation.
      for (const document of ['license','notices','changelog']) {
        await page.locator('footer a[href="/admin/'+document+'"]').click();
        await page.waitForFunction(() => document.getElementById('documentContent').textContent.startsWith('Example'));
        assert.equal(await page.locator('#documentContent script').count(),0,'Document content is rendered as text');
        assert.equal(await page.locator('#documentSource').isVisible(),document==='license');
        assert(await page.locator('#documentContent').evaluate(el=>el.scrollHeight>el.clientHeight),'Long document scrolls inside dialog');
        const box = await page.locator('#documentDialog').boundingBox();
        assert(box.y>=0 && box.y+box.height<=size.height && box.x>=0 && box.x+box.width<=size.width);
        if (document==='license') await page.screenshot({path:path.resolve('artifacts/admin-license-'+size.width+'.png')});
        await page.keyboard.press('Escape');
        await page.locator('#documentDialog').waitFor({state:'hidden'});
      }
      documentFailure = true;
      await page.locator('footer a[href="/admin/changelog"]').click();
      await page.waitForFunction(() => document.getElementById('documentContent').textContent.startsWith('Inhalt konnte'));
      await page.locator('#closeDocument').click(); documentFailure = false;
      await page.locator('#key').fill('test-only');
      await page.locator('#loginForm button').click();
      await page.locator('#workspace').waitFor({state:'visible'});
      await page.waitForFunction(() => document.getElementById('refresh').disabled === false);
      assert.equal(await page.locator('footer a[href="/admin/source.zip"]').count(),0);
      assert.equal(await page.locator('[data-tab="logs"]').isVisible(),false);
      assert.equal(logCalls,0,'Developer logs are not fetched by default');
      assert.equal(await page.locator('#rows tr').nth(0).locator('.copy-marker').count(),0);
      assert.equal(await page.locator('#rows tr').nth(1).locator('td').nth(1).locator('.copy-marker').textContent(),'');
      assert.equal(await page.locator('#rows tr').nth(2).locator('td').nth(1).locator('.copy-marker').textContent(),'');
      assert.match(await page.locator('.copy-marker').last().getAttribute('title'),/231389271893 Sekundärlabels/);
      const geometry = await page.locator('#rows tr').evaluateAll(rows => rows.map(row => {
        const marker = row.querySelector('.copy-marker'), status = row.querySelector('.status');
        const rect = marker?.getBoundingClientRect(), text = status.getBoundingClientRect();
        return {height:row.getBoundingClientRect().height, marker:rect && {width:rect.width,height:rect.height,center:rect.y+rect.height/2},
          center:text.y+text.height/2,fontSize:parseFloat(getComputedStyle(status).fontSize),
          multiple:marker?.classList.contains('multiple'),content:marker?.textContent,
          background:marker && getComputedStyle(marker).backgroundColor,shadow:marker && getComputedStyle(marker).boxShadow};
      }));
      geometry.forEach(item => assert.equal(item.height,geometry[0].height,'Markers never increase row height'));
      geometry.slice(1).forEach((item,index)=>{
        assert.equal(item.marker.width,item.fontSize); assert.equal(item.marker.height,item.fontSize);
        assert.equal(item.marker.center,item.center,'Marker is vertically centered on status');
        assert.equal(item.content,''); assert.equal(item.multiple,index>0);
        if(index>0) { assert.equal(item.background,'rgba(0, 0, 0, 0)'); assert.match(item.shadow,/inset/); }
      });
      assert.equal(await page.locator('#rows td:nth-child(3) .copy-marker').count(),0);
      await page.locator('#settings').click(); await page.locator('#developerMode').check();
      await page.screenshot({path:path.resolve('artifacts/admin-settings-'+size.width+'.png')});
      await page.locator('#closeSettings').click();
      await page.locator('[data-tab="logs"]').click();
      await page.locator('.copy-log').click();
      assert.equal((await page.evaluate(()=>JSON.parse(window.copiedEntry))).event,'service_started');
      await page.reload();
      assert.equal(await page.evaluate(()=>localStorage.getItem('asn-admin.developerMode')),'true');
      await page.locator('#key').fill('test-only'); await page.locator('#loginForm button').click();
      await page.locator('[data-tab="logs"]').click();
      await page.waitForFunction(() => !document.getElementById('settings').disabled);
      await page.locator('#settings').click(); await page.locator('#developerMode').uncheck();
      await page.waitForFunction(() => document.querySelector('[data-tab="documents"]').getAttribute('aria-current')==='page' && !document.getElementById('closeSettings').disabled);
      await page.locator('#closeSettings').click();
      assert.equal(await page.locator('[data-tab="logs"]').isVisible(),false);
      await page.locator('#language').selectOption('en');
      await page.waitForFunction(() => !document.getElementById('settings').disabled);
      await page.locator('#settings').click();
      assert.equal(await page.locator('.developer-setting').innerText(),'Developer mode');
      await page.locator('#closeSettings').click(); await page.locator('#language').selectOption('de');
      await page.waitForFunction(() => !document.getElementById('refresh').disabled);
      assert.equal(await page.locator('footer a').first().getAttribute('href'),'https://nightmind.dev');
      const footer = await page.locator('footer').boundingBox();
      assert(Math.abs(footer.y + footer.height - size.height) <= 1);
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
      await page.screenshot({path:path.resolve('artifacts/admin-ready-'+size.width+'.png')});
      offline = true;
      await page.locator('#refresh').click();
      await page.locator('#connectionDialog').waitFor({state:'visible'});
      const scrollBefore = await page.evaluate(()=>scrollY);
      await page.mouse.move(10,size.height-20); await page.mouse.wheel(0,800);
      await page.keyboard.press('PageDown'); await page.clock.runFor(500);
      assert.equal(await page.evaluate(()=>scrollY),scrollBefore,'Offline modal locks background scrolling');
      assert.equal(await page.evaluate(()=>getComputedStyle(document.documentElement).overflowY),'hidden');
      offline = false;
      await page.clock.runFor(10500);
      await page.locator('#connectionDialog').waitFor({state:'hidden'});
      assert.equal(await page.locator('#updateDialog').isVisible(),false,'Same-version recovery does not require reload');
      assert.notEqual(await page.evaluate(()=>getComputedStyle(document.documentElement).overflowY),'hidden');
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
      offline = false; serverVersion = '99.0.0';
      await page.clock.runFor(10500);
      await page.locator('#updateDialog').waitFor({state:'visible'});
      assert.equal(await page.locator('#connectionDialog').isVisible(),false);
      await page.close();
    }
    console.log('Admin UI: desktop/mobile dialogs, developer settings, log copy, markers, scroll lock, offline timeout and reload prompt passed.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
