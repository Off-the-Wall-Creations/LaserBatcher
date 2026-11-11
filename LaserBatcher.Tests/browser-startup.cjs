// Run against a local published build. No deployment or remote mutations.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const url = process.env.LASERBATCHER_URL || 'http://127.0.0.1:5193/';
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,
    args: ['--disable-features=IsolateSandboxedIframes'] });
  const localOnly = route => new URL(route.request().url()).origin === new URL(url).origin ? route.continue() : route.abort();
  const ready = page => page.locator('.app-header h1').waitFor({ timeout: 60000 });
  async function context(options = {}) {
    const ctx = await browser.newContext(options);
    await ctx.route('**/*', localOnly);
    await ctx.addInitScript(() => { if (window === window.top) navigator.serviceWorker.register = async () => ({}); });
    return ctx;
  }
  try {
    const textOnly = await context({ javaScriptEnabled: false, viewport: { width: 320, height: 568 } });
    const staticPage = await textOnly.newPage();
    const response = await staticPage.goto(url);
    const html = await response.text();
    assert(!html.includes('An unhandled error has occurred'), 'Static readers must not receive an unconditional error message');
    assert((await staticPage.locator('body').innerText()).includes('Arrange SVG copies'));
    assert((await staticPage.locator('noscript').innerText()).includes('Enable it in your browser'));
    assert.equal(await staticPage.locator('#blazor-error-ui').innerText(), '');
    assert(await staticPage.locator('#blazor-error-ui').isHidden());
    await textOnly.close();
    console.log('Static/no-JavaScript view describes the app without reporting a fake failure.');

    for (const failure of ['script', 'manifest', 'wasm-disabled']) {
      const ctx = await context();
      if (failure === 'wasm-disabled') await ctx.addInitScript(() => { if (window === window.top) window.WebAssembly = undefined; });
      const page = await ctx.newPage();
      const errors = []; page.on('pageerror', e => errors.push(e.message));
      if (failure === 'script') await page.route('**/_framework/blazor.webassembly.js', route => route.abort());
      if (failure === 'manifest') await page.route('**/_framework/blazor.boot.json', route => route.fulfill({ status: 404, body: 'missing' }));
      await page.goto(url);
      await page.locator('#blazor-error-ui').waitFor({ state: 'visible' });
      assert((await page.locator('#startup-status').innerText()).includes('could not open'));
      assert(await page.getByRole('button', { name: 'Reload', exact: true }).isVisible());
      assert((await page.locator('#blazor-error-ui').innerText()).includes(failure === 'wasm-disabled' ? 'WebAssembly enabled' : 'Some application files'));
      await page.getByRole('button', { name: 'Dismiss error', exact: true }).click();
      assert(await page.locator('#blazor-error-ui').isHidden());
      assert.deepEqual(errors, [], `${failure} is caught rather than becoming an unhandled rejection`);
      await ctx.close();
    }
    console.log('Blocked startup script, missing boot manifest and disabled WebAssembly produce actionable recovery controls.');

    const healthy = await context(); const page = await healthy.newPage();
    await page.goto(url); await ready(page);
    assert(await page.locator('#blazor-error-ui').isHidden());
    assert.equal(await page.locator('#blazor-error-ui').textContent(), '');
    // Exercise the same signal used by Blazor 7 for a later runtime exception.
    await page.evaluate(() => { document.querySelector('#blazor-error-ui').style.display = 'block'; });
    await page.getByText('LaserBatcher encountered an error.', { exact: true }).waitFor();
    await page.getByRole('button', { name: 'Dismiss error', exact: true }).click();
    assert(await page.locator('#blazor-error-ui').isHidden());
    await page.evaluate(() => { document.querySelector('#blazor-error-ui').style.display = 'block'; });
    await page.getByRole('button', { name: 'Reload', exact: true }).click(); await ready(page);
    assert.equal(await page.locator('#blazor-error-ui').textContent(), '');
    await healthy.close();
    console.log('Healthy startup remains silent; framework runtime error signalling, dismiss and reload still work.');

    for (const waitingInitially of [true, false]) {
      const ctx = await context();
      await ctx.addInitScript(initially => {
        if (window !== window.top) return;
        Object.defineProperty(navigator.serviceWorker, 'controller', { get: () => ({}) });
        const worker = new EventTarget(); worker.state = initially ? 'installed' : 'installing';
        const registration = new EventTarget(); registration.waiting = initially ? worker : null;
        navigator.serviceWorker.register = async () => registration;
        window.finishUpdate = () => {
          registration.installing = worker; registration.dispatchEvent(new Event('updatefound'));
          registration.waiting = worker; worker.state = 'installed'; worker.dispatchEvent(new Event('statechange'));
        };
      }, waitingInitially);
      const page = await ctx.newPage(); let navigations = 0;
      page.on('framenavigated', frame => { if (frame === page.mainFrame()) navigations++; });
      await page.goto(url); await ready(page);
      await page.locator('#svgUpload').setInputFiles({ name: 'Unsaved.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="1in" height="1in"><path id="Score" d="M1 1h10"/></svg>') });
      await page.getByAltText('Uploaded SVG source preview').waitFor();
      if (!waitingInitially) await page.evaluate(() => window.finishUpdate());
      await page.locator('#app-update-notice').waitFor({ state: 'visible' });
      assert((await page.locator('#app-update-notice').innerText()).includes('Download your work'));
      assert.equal(navigations, 1, 'An update must not reload an unsaved job');
      assert.equal(await page.locator('#partName').inputValue(), 'Unsaved');
      await page.getByRole('button', { name: 'Dismiss update notice', exact: true }).click();
      assert(await page.locator('#app-update-notice').isHidden());
      await ctx.close();
    }
    console.log('Already waiting and newly installed PWA updates notify users without reloading their work.');

    const returning = await context();
    await returning.addInitScript(() => {
      if (window !== window.top) return;
      const originalNow = Date.now;
      window.testClockOffset = 0; Date.now = () => originalNow() + window.testClockOffset;
      window.testVisibility = 'visible'; window.updateCalls = 0; window.failUpdate = false;
      Object.defineProperty(document, 'visibilityState', { get: () => window.testVisibility });
      const registration = new EventTarget(); registration.waiting = null;
      registration.update = async () => { window.updateCalls++; if(window.failUpdate) throw new TypeError('offline'); return registration; };
      navigator.serviceWorker.register = async () => registration;
    });
    const returningPage = await returning.newPage(), returningErrors = [];
    returningPage.on('pageerror', error=>returningErrors.push(error.message));
    await returningPage.goto(url); await ready(returningPage);
    await returningPage.locator('#svgUpload').setInputFiles({name:'Kept.svg',mimeType:'image/svg+xml',buffer:Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="1in" height="1in"><path id="Score" d="M1 1h10"/></svg>')});
    await returningPage.getByAltText('Uploaded SVG source preview').waitFor();
    await returningPage.evaluate(()=>{window.testClockOffset=300001;window.testVisibility='hidden';document.dispatchEvent(new Event('visibilitychange'));});
    assert.equal(await returningPage.evaluate(()=>window.updateCalls),0);
    await returningPage.evaluate(()=>{window.testVisibility='visible';document.dispatchEvent(new Event('visibilitychange'));});
    await returningPage.waitForFunction(()=>window.updateCalls===1);
    await returningPage.evaluate(()=>document.dispatchEvent(new Event('visibilitychange')));
    assert.equal(await returningPage.evaluate(()=>window.updateCalls),1,'Returning checks are throttled');
    await returningPage.evaluate(()=>{window.failUpdate=true;window.dispatchEvent(new Event('online'));});
    await returningPage.waitForFunction(()=>window.updateCalls===2);
    assert.equal(await returningPage.locator('#partName').inputValue(),'Kept');
    assert(await returningPage.getByAltText('Uploaded SVG source preview').isVisible());
    assert.deepEqual(returningErrors,[],'A failed update check must not become an unhandled error');
    await returning.close();
    console.log('Returning/reconnecting checks are throttled, catch failures and retain the active job.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
