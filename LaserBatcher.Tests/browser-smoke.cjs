// Optional browser regression: start the app on localhost:5188, then run with Node and Playwright.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

(async () => {
  const artifacts = path.join(__dirname, 'obj', 'browser-qa');
  fs.mkdirSync(artifacts, { recursive: true });
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE });
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 2, serviceWorkers: 'block' });
  await page.route('**/*', route => ['localhost', '127.0.0.1'].includes(new URL(route.request().url()).hostname) ? route.continue() : route.abort());
  page.setDefaultTimeout(15000);
  const errors = [];
  page.on('requestfailed', request => {
    if (['localhost', '127.0.0.1'].includes(new URL(request.url()).hostname)) errors.push('Local resource failed: ' + request.url());
  });
  page.on('pageerror', e => { errors.push(e.message); console.error(e.message); });
  try {
    await page.goto(process.env.LASERBATCHER_URL || 'http://localhost:5188', { waitUntil: 'commit' });
    await page.getByRole('heading', { name: 'Laser Batcher', exact: true }).waitFor({ timeout: 60000 });
    const source = fs.readFileSync(path.join(__dirname, 'Fixtures', 'semantic.svg'));
    await page.locator('#svgUpload').setInputFiles({ name: 'Widget_x4.svg', mimeType: 'image/svg+xml', buffer: source });
    await page.waitForFunction(() => document.querySelector('#filenamePreview')?.textContent === 'Widget_x4.svg');
    await page.locator('img[data-source-key]').first().scrollIntoViewIfNeeded();
    await page.waitForFunction(() => document.querySelector('img[data-source-key]')?.dataset.previewReady === 'true');
    assert(await page.locator('img[data-source-key]').count() > 1);
    const indentation = await page.locator('tr[data-depth]').evaluateAll(rows => rows.map(row => ({ depth: Number(row.dataset.depth), padding: parseFloat(getComputedStyle(row.querySelector('.mapping-element')).paddingLeft) })));
    assert(indentation.some(row => row.depth > 1));
    assert(indentation.every(row => row.padding === row.depth * 14));
    for (const [id, value] of [['boxWidth', '4'], ['boxHeight', '3'], ['horizontalMargin', '0.25'], ['verticalMargin', '0.25'], ['spaceBetween', '0.1']]) {
      await page.locator('#' + id).fill(value);
      await page.locator('#' + id).blur();
    }
    await page.getByRole('button', { name: 'Render batch' }).click();
    await page.getByAltText('Ordered laser batch preview').waitFor();
    const oneCopy = await page.getByAltText('Ordered laser batch preview').getAttribute('src');
    const visual = await page.evaluate(async ({ source, batch }) => {
      async function draw(src, width, height) {
        const image = new Image();
        image.src = src;
        await image.decode();
        const c = document.createElement('canvas');
        c.width = width; c.height = height;
        c.getContext('2d').drawImage(image, 0, 0);
        return c;
      }
      const a = await draw(source, 96, 96);
      const b = await draw(batch, 384, 288);
      const before = a.getContext('2d').getImageData(0, 0, 96, 96).data;
      const after = b.getContext('2d').getImageData(24, 24, 96, 96).data;
      let changed = 0;
      for (let i = 0; i < before.length; i++) if (Math.abs(before[i] - after[i]) > 2) changed++;
      return { changedChannels: changed, source: a.toDataURL(), batch: b.toDataURL() };
    }, { source: 'data:image/svg+xml;base64,' + source.toString('base64'), batch: oneCopy });
    fs.writeFileSync(path.join(artifacts, 'source.png'), Buffer.from(visual.source.split(',')[1], 'base64'));
    fs.writeFileSync(path.join(artifacts, 'batch.png'), Buffer.from(visual.batch.split(',')[1], 'base64'));
    assert.equal(visual.changedChannels, 0, 'Batch must render the same source geometry at the expected physical placement');
    console.log('Visual equivalence passed. Testing metadata changes and stale previews.');

    await page.locator('#copyQuantity').fill('3');
    await page.locator('#copyQuantity').blur();
    await page.waitForFunction(() => !document.querySelector('img[alt="Ordered laser batch preview"]'));
    assert.equal(await page.locator('#filenamePreview').innerText(), 'Widget_x12.svg');
    await page.locator('#orderNumber').fill('AB-12'); await page.locator('#orderNumber').blur();
    await page.locator('#batch').fill('Lot.3'); await page.locator('#batch').blur();
    await page.locator('#ordering').selectOption('AcrossSheet');
    await page.getByRole('button', { name: 'Render batch' }).click();
    const downloadPromise = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Download SVG' }).click();
    const download = await downloadPromise;
    assert.equal(download.suggestedFilename(), 'Widget_Order AB-12_Batch Lot.3_x12.svg');
    await download.saveAs(path.join(artifacts, download.suggestedFilename()));
    const exported = fs.readFileSync(path.join(artifacts, download.suggestedFilename()), 'utf8');
    assert.equal((exported.match(/<svg(?:\s|>)/g) || []).length, 1, 'Export must have only one SVG root');
    const prefixedSvgElements = await page.evaluate(xml => [...new DOMParser().parseFromString(xml, 'image/svg+xml').querySelectorAll('*')]
      .filter(element => element.namespaceURI === 'http://www.w3.org/2000/svg' && element.prefix).map(element => element.tagName), exported);
    assert.deepEqual(prefixedSvgElements, [], 'Downloaded SVG elements must use unprefixed names even when the source declares SVG aliases');
    assert(exported.includes('"totalYield":12'));
    assert(exported.includes('urn:laserbatcher:metadata:1'));
    assert(exported.includes('hidden-in-cut'));
    await page.screenshot({ path: path.join(artifacts, 'desktop.png'), fullPage: true });

    const unmapped = Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="1in" height="1in"><g id="InsideCut"><path id="outline" d="M1 1h10v10h-10z"/></g></svg>');
    await page.locator('#svgUpload').setInputFiles({ name: 'Plain.svg', mimeType: 'image/svg+xml', buffer: unmapped });
    await page.waitForFunction(() => document.querySelector('#partName')?.value === 'Plain');
    assert(await page.getByRole('button', { name: 'Render batch' }).isDisabled());
    await page.getByRole('combobox', { name: 'Operation for InsideCut (0/0)', exact: true }).selectOption('InteriorCut');
    await page.getByRole('combobox', { name: 'Operation for outline (0/0/0)', exact: true }).selectOption('ExteriorCut');
    await page.getByRole('button', { name: 'Render batch' }).click();
    await page.getByAltText('Ordered laser batch preview').waitFor();

    await page.locator('#svgUpload').setInputFiles({ name: 'Broken_batch_x0.svg', mimeType: 'image/svg+xml', buffer: source });
    await page.getByRole('alert').filter({ hasText: 'Source filename:' }).waitFor();
    assert(await page.getByRole('button', { name: 'Render batch' }).isDisabled());
    await page.locator('#partName').fill('Fixed'); await page.locator('#partName').blur();
    await page.getByRole('button', { name: 'Render batch' }).click();
    await page.getByAltText('Ordered laser batch preview').waitFor();

    await page.locator('#svgUpload').setInputFiles({ name: 'malformed.svg', mimeType: 'image/svg+xml', buffer: Buffer.from('<svg>') });
    await page.getByRole('alert').filter({ hasText: 'Could not load SVG' }).waitFor();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: path.join(artifacts, 'mobile.png'), fullPage: true });
    assert.deepEqual(errors, []);
    console.log('Browser smoke passed: upload, mapping, metadata correction, yield, both order modes, stale preview, download, visual equivalence at DPR 2, XML errors.');
    console.log('Artifacts: ' + artifacts);
  } catch (error) { await page.screenshot({ path: path.join(artifacts, 'failure.png'), fullPage: true }); console.error(await page.locator('body').innerText()); throw error; }
  finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
