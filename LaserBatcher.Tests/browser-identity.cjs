// Start the development app, then run with Node and Playwright.
// LASERBATCHER_URL defaults to http://localhost:5188.
// BMC_SCRIPT_FILE optionally points to a locally fetched, unmodified vendor script
// to exercise the real document.writeln widget without network-dependent tests.
// BMC_LIVE=1 instead loads the live widget and Cookie font (no payment actions).
// PWA_URL optionally points to a locally served Release publish/wwwroot directory.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const url = process.env.LASERBATCHER_URL || 'http://localhost:5188';
const artifacts = path.join(__dirname, 'obj/browser-qa');
fs.mkdirSync(artifacts, { recursive: true });
const vendorUrl = 'https://cdnjs.buymeacoffee.com/1.0.0/button.prod.min.js';
const fixture = `<svg xmlns="http://www.w3.org/2000/svg" width="1in" height="1in" viewBox="0 0 96 96" id="Document"><defs/>
<g id="Parent"><g id="Nested"><path id="Child" d="M2 2h10" stroke="black"/></g><circle id="Direct" cx="20" cy="20" r="5"/></g>
${Array.from({ length: 8 }, (_, i) => `<rect id="Filler${i}" x="${i * 5}" y="30" width="3" height="3"/>`).join('')}
<g id="Sibling"><path id="SiblingChild" d="M2 60h10" stroke="red"/></g></svg>`;

async function appReady(page) { await page.locator('.app-header h1').waitFor({ timeout: 60000 }); }
async function theme(page, value, expected = value) {
  await page.locator('#appearance').selectOption(value);
  await page.waitForFunction(expected => document.documentElement.dataset.bsTheme === expected, expected);
}
async function localOnly(route) {
  const host = new URL(route.request().url()).hostname;
  return ['localhost', '127.0.0.1'].includes(host) ? route.continue() : route.abort();
}
async function withoutDevWorker(context) {
  // Avoid Playwright's injected worker blocker in opaque iframes; prevent the
  // development worker from bypassing network routes after reload instead.
  await context.addInitScript(() => { if (window === window.top) navigator.serviceWorker.register = async () => ({}); });
}
async function download(page) {
  const pending = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download SVG', exact: true }).click();
  return fs.readFileSync(await (await pending).path(), 'utf8');
}

(async () => {
  // Chromium otherwise starts srcdoc's first request before Playwright attaches
  // to its isolated process. Keep sandbox origin restrictions while allowing
  // deterministic CDN routing; the parent-document access assertion still runs.
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE, args: ['--disable-features=IsolateSandboxedIframes'] });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, colorScheme: 'dark' });
  await withoutDevWorker(context);
  await context.route('**/*', localOnly);
  const page = await context.newPage();
  page.setDefaultTimeout(15000);
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  try {
    await page.goto(url); await appReady(page);
    assert.equal(await page.locator('#appearance').inputValue(), 'system');
    assert.equal(await page.locator('html').getAttribute('data-bs-theme'), 'dark');
    await page.emulateMedia({ colorScheme: 'light' });
    await page.waitForFunction(() => document.documentElement.dataset.bsTheme === 'light');
    await theme(page, 'dark');
    await page.reload(); await appReady(page);
    await page.waitForFunction(() => document.querySelector('#appearance').value === 'dark');
    assert.equal(await page.locator('html').getAttribute('data-bs-theme'), 'dark');
    // Theme is applied while Blazor is still blocked, before the loading screen paints.
    const early = await context.newPage();
    await early.route('**/_framework/blazor.webassembly.js', route => route.abort());
    await early.goto(url);
    assert.equal(await early.locator('html').getAttribute('data-bs-theme'), 'dark');
    assert.equal(await early.locator('.loading-brand h1').innerText(), 'LaserBatcher');
    await early.close();
    await theme(page, 'system', 'light');
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.waitForFunction(() => document.documentElement.dataset.bsTheme === 'dark');

    for (const value of ['light', 'dark']) {
      await theme(page, value);
      const contrast = await page.evaluate(() => {
        const style = getComputedStyle(document.documentElement);
        function luminance(token) {
          const hex = style.getPropertyValue(token).trim().slice(1);
          return [0, 2, 4].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
            .map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4)
            .reduce((sum, x, i) => sum + x * [.2126, .7152, .0722][i], 0);
        }
        return [['--lb-text', '--lb-panel'], ['--lb-muted', '--lb-panel'], ['--lb-on-primary', '--lb-primary'],
          ['--lb-warning-text', '--lb-warning-bg'], ['--lb-info-text', '--lb-info-bg'], ['--lb-error-text', '--lb-error-bg']]
          .map(([a, b]) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05))
          .concat((Math.max(luminance('--lb-border'), luminance('--lb-panel')) + .05) / (Math.min(luminance('--lb-border'), luminance('--lb-panel')) + .05));
      });
      assert(contrast.slice(0, -1).every(ratio => ratio >= 4.5), `${value} text contrast: ${contrast}`);
      assert(contrast.at(-1) >= 3, `${value} control boundary contrast: ${contrast.at(-1)}`);
      assert.equal(await page.locator('meta[name="theme-color"]').getAttribute('content'), value === 'dark' ? '#1C1917' : '#F7F3EC');
    }
    await page.locator('#appearance').focus(); await page.keyboard.press('Tab'); await page.keyboard.press('Shift+Tab');
    assert.equal(await page.locator('#appearance').evaluate(el => getComputedStyle(el).outlineStyle), 'solid');
    assert(!/user-scalable\s*=\s*no|maximum-scale/.test(await page.locator('meta[name="viewport"]').getAttribute('content')));

    // Blocked vendor, forged readiness, and repeat component rendering.
    await page.locator('.support-fallback').waitFor({ state: 'visible' });
    await page.evaluate(() => window.postMessage({ type: 'laserbatcher:support-ready' }, '*'));
    await theme(page, 'light');
    assert(await page.locator('.support-fallback').isVisible());
    assert.equal(await page.locator('.support-frame').count(), 1);
    assert(!(await page.locator('.support-frame').getAttribute('sandbox')).includes('allow-same-origin'));

    await page.locator('#svgUpload').setInputFiles({ name: 'Groups.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(fixture) });
    const row = key => page.locator(`tr[data-key="${key}"]`);
    await row('0/10/0').waitFor({ state: 'attached' });
    assert.equal(await page.locator('tr[data-key][hidden]').count(), 0);
    const totalRows = await page.locator('tr[data-key]').count();
    assert.equal(await row('0/10/0').locator('img').getAttribute('data-preview-ready'), null, 'Offscreen thumbnail starts lazy');
    await row('0/1/0/0').evaluate(el => window.retainedMappingRow = el);
    assert((await row('0').innerText()).includes('11 production · 11 unresolved'));
    await row('0/1/0').getByRole('button').click();
    await row('0/1').getByRole('button').click();
    assert(await row('0/1/0').isHidden());
    assert(await row('0/10').isVisible(), '0/1 must not hide sibling 0/10');
    assert.equal(await page.locator('tr[data-key]').count(), totalRows, 'Rows remain mounted');
    assert(await row('0/1/0/0').evaluate(el => el === window.retainedMappingRow), 'Collapse preserves DOM identity');
    assert((await row('0/1').innerText()).includes('2 production · 2 unresolved'));
    assert(await page.getByRole('button', { name: 'Render batch', exact: true }).isDisabled());
    await row('0/1').getByRole('button').click();
    assert(await row('0/1/0').isVisible());
    assert(await row('0/1/0/0').isHidden(), 'Nested collapse survives parent reopening');
    await row('0/1/0').getByRole('button').focus(); await page.keyboard.press('Enter');
    assert.equal(await row('0/1/0').getByRole('button').getAttribute('aria-expanded'), 'true');
    await row('0/1/0/0').getByRole('combobox').selectOption('Score');
    await row('0/1').getByRole('button').click();
    await row('0/1').getByRole('combobox').selectOption('Engrave');
    assert((await row('0/1').innerText()).includes('2 production · 0 unresolved'));
    await page.getByRole('button', { name: 'Collapse all', exact: true }).click();
    assert.equal(await page.locator('tr[data-key]:visible').count(), 1);
    await row('0').getByRole('combobox').selectOption('ExteriorCut');
    assert((await row('0').innerText()).includes('11 production · 0 unresolved'));
    assert(!(await page.getByRole('button', { name: 'Render batch', exact: true }).isDisabled()));
    await row('0').getByRole('button').click();
    assert.equal(await row('0/1').getByRole('button').getAttribute('aria-expanded'), 'false');
    // Tab cannot enter controls on hidden descendants.
    await row('0/1').getByRole('combobox').focus(); await page.keyboard.press('Tab');
    assert(await page.evaluate(() => !document.activeElement.closest('tr[hidden]')));
    await page.getByRole('button', { name: 'Expand all', exact: true }).click();
    assert.equal(await page.locator('tr[data-key][hidden]').count(), 0);
    assert((await row('0/1/0/0').innerText()).includes('Score'), 'Individual overrides remain authoritative');
    await row('0/10/0').scrollIntoViewIfNeeded();
    await page.waitForFunction(() => document.querySelector('img[data-source-key="0/10/0"]')?.dataset.previewReady === 'true');
    const cachedThumbnail = await row('0/10/0').locator('img').getAttribute('src');
    await page.locator('#runtimeHours').fill('25'); await page.locator('#runtimeHours').blur();
    await page.getByRole('button', { name: 'Render batch', exact: true }).click();
    const image = page.getByAltText('Ordered laser batch preview'); await image.waitFor();
    const before = await image.getAttribute('src');
    const exported = await download(page);
    await page.getByRole('button', { name: 'Collapse all', exact: true }).click();
    assert.equal(await page.locator('#runtimeHours').inputValue(), '25');
    assert.equal(await image.getAttribute('src'), before);
    assert.equal(await download(page), exported, 'Collapse must not change exported content');
    await theme(page, 'dark');
    assert.equal(await image.getAttribute('src'), before, 'Theme must not recolor source artwork');
    assert.equal(await page.locator('.svg-preview').evaluate(el => getComputedStyle(el).backgroundColor), 'rgb(139, 69, 19)');
    await page.getByRole('button', { name: 'Expand all', exact: true }).click();
    assert.equal(await row('0/10/0').locator('img').getAttribute('src'), cachedThumbnail);
    assert.equal(await page.locator('#runtimeHours').inputValue(), '25');
    assert.equal(await image.getAttribute('src'), before);
    await row('0/10/0').scrollIntoViewIfNeeded();
    assert.equal(await row('0/10/0').locator('img').getAttribute('data-preview-ready'), 'true');
    await page.screenshot({ path: path.join(artifacts, 'identity-dark.png'), fullPage: true, animations: 'disabled' });
    await theme(page, 'light');
    await page.screenshot({ path: path.join(artifacts, 'identity-light.png'), fullPage: true, animations: 'disabled' });
    assert.equal(await page.getByRole('button', { name: 'Render batch', exact: true }).evaluate(el => getComputedStyle(el).backgroundColor), 'rgb(154, 76, 10)');
    assert.equal(await page.getByRole('button', { name: 'Clear estimate', exact: true }).evaluate(el => getComputedStyle(el).color), 'rgb(41, 37, 36)');
    for (const width of [320, 390, 768]) {
      await page.setViewportSize({ width, height: 900 });
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `No page overflow at ${width}px`);
      if (width === 390) await page.screenshot({ path: path.join(artifacts, 'identity-mobile.png'), fullPage: true, animations: 'disabled' });
    }
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.evaluate(() => document.body.style.zoom = '2');
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), '200% layout zoom');
    await page.screenshot({ path: path.join(artifacts, 'identity-zoom.png'), fullPage: true });
    await page.evaluate(() => document.body.style.zoom = '');
    await page.locator('#svgUpload').setInputFiles({ name: 'Groups_x1.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(exported) });
    await page.waitForFunction(() => document.querySelector('#workMode').value === 'EditMetadata');
    await page.locator('#workMode').selectOption('CreateBatch');
    assert.equal(await page.locator('tr[data-key][hidden]').count(), 0, 'New upload resets collapse state');
    await row('0').getByRole('button').click();
    await page.locator('#workMode').selectOption('EditMetadata');
    await page.locator('#workMode').selectOption('CreateBatch');
    assert.equal(await row('0').getByRole('button').getAttribute('aria-expanded'), 'false', 'Workflow switches retain collapse state');
    assert.equal(await page.locator('tr[data-key]:visible').count(), 1);
    assert.deepEqual(errors, []);
    console.log('Identity, nested mapping state, keyboard access, unchanged runtime/exports, mobile and zoom passed.');

    const blocked = await browser.newContext({ colorScheme: 'dark' });
    await withoutDevWorker(blocked);
    await blocked.route('**/*', localOnly);
    await blocked.addInitScript(() => {
      Storage.prototype.getItem = () => { throw new DOMException('Denied', 'SecurityError'); };
      Storage.prototype.setItem = () => { throw new DOMException('Denied', 'SecurityError'); };
    });
    const blockedPage = await blocked.newPage(); await blockedPage.goto(url); await appReady(blockedPage);
    await theme(blockedPage, 'light'); assert.equal(await blockedPage.locator('#appearance').inputValue(), 'light');
    await theme(blockedPage, 'system', 'dark'); await blocked.close();
    console.log('Blocked storage leaves System / Light / Dark usable.');

    const widget = await browser.newContext(); let vendorRequests = 0;
    await withoutDevWorker(widget);
    const vendorScript = process.env.BMC_SCRIPT_FILE ? fs.readFileSync(process.env.BMC_SCRIPT_FILE, 'utf8')
      : `document.writeln('<a class="bmc-btn" target="_blank" href="https://buymeacoffee.com/tbm0115">Buy me a coffee</a>');`;
    await widget.route('**/*', route => {
      if (route.request().url() === vendorUrl) { vendorRequests++; return process.env.BMC_LIVE ? route.continue() : route.fulfill({ contentType: 'text/javascript', body: vendorScript }); }
      if (process.env.BMC_LIVE && ['fonts.googleapis.com', 'fonts.gstatic.com'].includes(new URL(route.request().url()).hostname)) return route.continue();
      return localOnly(route);
    });
    const widgetPage = await widget.newPage(); await widgetPage.goto(url); await appReady(widgetPage);
    await widgetPage.locator('[data-support-ready="true"]').waitFor();
    assert(await widgetPage.locator('.support-fallback').isHidden());
    const frame = await widgetPage.locator('.support-frame').elementHandle().then(handle => handle.contentFrame());
    const isolation = await frame.evaluate(() => {
      let isolated = false; try { void parent.document.body; } catch { isolated = true; }
      const script = document.querySelector('script[src]');
      const button = document.querySelector('.bmc-btn');
      const rect = button.getBoundingClientRect();
      return { isolated, config: Object.fromEntries([...script.attributes].map(a => [a.name, a.value])),
        href: button.href, target: button.target, fits: rect.left >= 0 && rect.right <= innerWidth && rect.top >= 0 && rect.bottom <= innerHeight };
    });
    assert(isolation.isolated); assert(isolation.fits, 'Widget fits without clipping');
    for (const [key, value] of Object.entries({ 'data-name': 'bmc-button', 'data-slug': 'tbm0115', 'data-color': '#FFDD00', 'data-emoji': '', 'data-font': 'Cookie', 'data-text': 'Buy me a coffee', 'data-outline-color': '#000000', 'data-font-color': '#000000', 'data-coffee-color': '#ffffff' })) assert.equal(isolation.config[key], value);
    assert(isolation.href.includes('buymeacoffee.com/tbm0115')); assert.equal(isolation.target, '_blank');
    if (process.env.BMC_LIVE) {
      await frame.evaluate(() => document.fonts.ready);
      assert(await frame.evaluate(() => document.fonts.check('28px Cookie')), 'Live Cookie font loads');
    }
    await theme(widgetPage, 'dark'); assert.equal(vendorRequests, 1);
    await widgetPage.locator('.app-footer').screenshot({ path: path.join(artifacts, 'coffee-rendered.png') });
    await widgetPage.setViewportSize({ width: 320, height: 900 });
    await widgetPage.locator('.app-footer').screenshot({ path: path.join(artifacts, 'coffee-mobile.png'), animations: 'disabled' });
    assert(await frame.evaluate(() => { const r = document.querySelector('.bmc-btn').getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth; }), 'Widget fits a 320px screen');
    // Dispose removes the listener; a late message from the old iframe is ignored.
    await widgetPage.evaluate(async () => { const container = document.querySelector('.support-widget'); await (await import('./supportButton.js')).dispose(container); delete container.dataset.supportReady; });
    await frame.evaluate(() => parent.postMessage({ type: 'laserbatcher:support-ready' }, '*')).catch(() => {});
    assert(await widgetPage.locator('.support-fallback').isVisible());
    await widget.close();
    console.log(`Support isolation, sender validation, disposal, fallback and ${process.env.BMC_LIVE ? 'live CDN / Cookie font' : process.env.BMC_SCRIPT_FILE ? 'actual vendor' : 'document.writeln test'} rendering passed.`);

    const assetChecks = await page.evaluate(async () => {
      const manifest = await (await fetch('manifest.json')).json();
      const results = [];
      for (const icon of manifest.icons) {
        const image = new Image(); image.src = icon.src; await image.decode();
        const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
        const ctx = canvas.getContext('2d'); ctx.drawImage(image, 0, 0);
        let outsideSafe = 0;
        if (icon.purpose === 'maskable') {
          const pixels = ctx.getImageData(0, 0, image.width, image.height).data;
          for (let y = 0; y < image.height; y++) for (let x = 0; x < image.width; x++) {
            const i = (y * image.width + x) * 4;
            if (pixels[i + 3] !== 255 || Math.hypot(x + .5 - image.width / 2, y + .5 - image.height / 2) > image.width * .4 &&
              (pixels[i] !== 28 || pixels[i + 1] !== 25 || pixels[i + 2] !== 23)) outsideSafe++;
          }
        }
        results.push({ declared: icon.sizes, actual: `${image.width}x${image.height}`, outsideSafe });
      }
      return { name: manifest.name, results };
    });
    assert.equal(assetChecks.name, 'LaserBatcher');
    for (const check of assetChecks.results) { assert.equal(check.declared, check.actual); assert.equal(check.outsideSafe, 0); }
    console.log('PWA asset sizes and maskable safe areas passed.');

    if (process.env.PWA_URL) {
      const offline = await browser.newContext();
      const pwa = await offline.newPage(); await pwa.goto(process.env.PWA_URL); await appReady(pwa);
      await pwa.evaluate(() => navigator.serviceWorker.ready);
      await pwa.reload(); await appReady(pwa);
      assert(await pwa.evaluate(() => !!navigator.serviceWorker.controller));
      await offline.setOffline(true); await pwa.reload(); await appReady(pwa);
      const cached = await pwa.evaluate(async () => Promise.all(['brand/laserbatcher.svg', 'brand/laserbatcher-mono.svg', 'icon-maskable-512.png', 'theme.js', 'supportButton.js'].map(async path => (await fetch(path)).ok)));
      assert(cached.every(Boolean));
      await pwa.locator('.support-fallback').waitFor({ state: 'visible' });
      await pwa.locator('#svgUpload').setInputFiles({ name: 'Offline.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(fixture) });
      await pwa.locator('tr[data-key="0"]').getByRole('combobox').selectOption('Score');
      await pwa.getByRole('button', { name: 'Render batch', exact: true }).click();
      await pwa.getByAltText('Ordered laser batch preview').waitFor();
      await pwa.screenshot({ path: path.join(artifacts, 'pwa-offline.png'), fullPage: true });
      await offline.close(); console.log('Published PWA reload, SVG/icon caching, support fallback and batch rendering passed offline.');
    }
  } catch (error) { await page.screenshot({ path: path.join(artifacts, 'identity-failure.png'), fullPage: true }); throw error; }
  finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
