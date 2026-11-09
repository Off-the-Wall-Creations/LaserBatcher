// Run against locally served, prepared Release output. No production mutation.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const url = new URL(process.env.LASERBATCHER_URL || 'http://localhost:5193/');

(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,
    args: ['--disable-features=IsolateSandboxedIframes'] });
  try {
    const context = await browser.newContext();
    await context.route('**/*', route => new URL(route.request().url()).origin === url.origin ? route.continue() : route.abort());
    const page = await context.newPage();
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const failedAssets = []; page.on('response', response => {
      if (new URL(response.url()).origin === url.origin && response.status() >= 400) failedAssets.push(response.url());
    });
    await page.goto(url.href);
    await page.locator('.app-header h1').waitFor({ timeout: 60000 });
    assert.equal(await page.evaluate(() => document.baseURI), url.origin + '/');
    assert.deepEqual(failedAssets, [], 'Page resources load from the correct base path');
    for (const [asset, type] of [['manifest.json', 'application/json'], ['_framework/blazor.webassembly.js', 'javascript'],
      ['service-worker.js', 'javascript'], ['css/app.css', 'text/css']]) {
      const response = await context.request.get(new URL(asset, url.origin).href);
      assert.equal(response.status(), 200, asset);
      assert(response.headers()['content-type'].includes(type), `${asset} must not be an HTML fallback`);
    }
    assert.equal((await context.request.get(new URL('/LaserBatcher/manifest.json', url.origin).href)).status(), 404);
    assert.equal((await (await context.request.get(new URL('/CNAME', url.origin).href)).text()).trim(), 'batcher.offthewallcreations.xyz');
    assert.equal((await context.request.get(new URL('/.nojekyll', url.origin).href)).status(), 200);
    const registration = await page.evaluate(async () => {
      const registration = await navigator.serviceWorker.ready;
      return { scope: registration.scope, updateViaCache: registration.updateViaCache };
    });
    assert.equal(registration.scope, url.origin + '/');
    assert.equal(registration.updateViaCache, 'none');
    await page.reload(); await page.locator('.app-header h1').waitFor();
    assert(await page.evaluate(() => !!navigator.serviceWorker.controller));
    await context.setOffline(true); await page.reload(); await page.locator('.app-header h1').waitFor();
    assert.equal(await page.locator('.app-header h1').innerText(), 'LaserBatcher');
    assert.deepEqual(errors, []);
    await context.close();
    console.log('Root resource URLs, content types, CNAME, worker scope, integrity installation and offline reload passed.');

    const failedWorker = await browser.newContext();
    await failedWorker.route('**/*', route => new URL(route.request().url()).origin === url.origin ? route.continue() : route.abort());
    await failedWorker.addInitScript(() => {
      if (window === window.top) navigator.serviceWorker.register = () => Promise.reject(new TypeError('Simulated registration failure'));
    });
    const fallback = await failedWorker.newPage();
    const fallbackErrors = [], warnings = [];
    fallback.on('pageerror', error => fallbackErrors.push(error.message));
    fallback.on('console', message => { if (message.type() === 'warning') warnings.push(message.text()); });
    await fallback.goto(url.href); await fallback.locator('.app-header h1').waitFor({ timeout: 60000 });
    await fallback.locator('#svgUpload').setInputFiles({ name: 'OfflineSupportFailed.svg', mimeType: 'image/svg+xml',
      buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="1in" height="1in"><path id="Score" d="M1 1h10" stroke="black"/></svg>') });
    await fallback.getByRole('button', { name: 'Render batch', exact: true }).click();
    await fallback.getByAltText('Ordered laser batch preview').waitFor();
    assert(warnings.some(message => message.includes('LaserBatcher offline support is unavailable.')));
    assert.deepEqual(fallbackErrors, []);
    await failedWorker.close();
    console.log('Service-worker registration failure is caught; upload and rendering remain usable.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
