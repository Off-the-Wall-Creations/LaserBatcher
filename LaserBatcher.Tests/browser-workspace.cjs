// Published-build workspace regression. Uses local fixtures; never controls a laser.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const url = process.env.LASERBATCHER_URL || 'http://127.0.0.1:5193/';
const artifacts = path.join(__dirname, 'obj/browser-qa');
(async () => {
  const browser = await chromium.launch({ headless: true, executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE, args: ['--disable-features=IsolateSandboxedIframes'] });
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  await context.addInitScript(() => { if (window === window.top) navigator.serviceWorker.register = async () => ({}); });
  await context.route('**/*', r => new URL(r.request().url()).origin === new URL(url).origin ? r.continue() : r.abort());
  const page = await context.newPage(); page.setDefaultTimeout(15000);
  const errors = []; page.on('pageerror', e => errors.push(e.message)); page.on('console', m => { if(m.type() === 'error' && !m.text().includes('net::ERR_FAILED')) console.error(m.text()); });
  const button = name => page.getByRole('button', { name, exact: true });
  const row = name => page.locator('tr[data-key]').filter({ has: page.getByRole('combobox', { name: new RegExp('^Operation for ' + name + ' \\(') }) });
  const assignment = name => row(name).getByRole('combobox');
  const runtime = async () => { await page.getByRole('button', { name: /^Runtime/ }).click(); await page.getByRole('dialog', { name: 'Estimated Runtime', exact: true }).waitFor(); };
  const closeModal = async name => { await button(name).click(); await page.locator('dialog:modal').waitFor({ state: 'hidden' }); };
  const upload = async (name, content) => {
    await page.locator('#svgUpload').setInputFiles({ name, mimeType: 'image/svg+xml', buffer: Buffer.from(content) });
    await page.getByAltText('Uploaded SVG source preview').waitFor();
  };
  const download = async (name = 'Download SVG') => {
    const pending = page.waitForEvent('download'); await button(name).click(); const result = await pending;
    return fs.readFileSync(await result.path(), 'utf8');
  };
  const noScroll = async label => assert(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight && document.documentElement.scrollWidth <= innerWidth), label);
  const metadata = async xml => page.evaluate(xml => {
    const d = new DOMParser().parseFromString(xml, 'image/svg+xml');
    return JSON.parse([...d.documentElement.children].find(e => e.localName === 'metadata').children[0].textContent);
  }, xml);
  try {
    await page.goto(url); await page.locator('.app-header').waitFor({ timeout: 60000 });
    assert(await button('Download SVG').isDisabled());
    const fixture = fs.readFileSync(path.join(__dirname, 'Fixtures/inspection.svg'), 'utf8');
    await upload('Inspect_x2.svg', fixture);
    await page.waitForFunction(() => document.querySelector('.mapping-thumbnail')?.dataset.previewReady === 'true');
    const sourceUrl = await page.getByAltText('Uploaded SVG source preview').getAttribute('src');
    assert.equal(await page.locator('#previewMode').inputValue(), 'enhanced');
    await assignment('Document').selectOption('Score');
    await runtime(); await page.locator('#runtimeHours').fill('25'); await page.locator('#runtimeSeconds').fill('1'); await closeModal('Apply');
    await button('Render batch').click(); await page.getByAltText('Ordered laser batch preview').waitFor();
    const initialBatch = await page.getByAltText('Ordered laser batch preview').getAttribute('src');
    const initialExport = await download();
    assert.equal((await metadata(initialExport)).estimatedRuntimeSeconds, 90001);

    // Selection, display mode, collapse, zoom, material and resizing are view state.
    await row('thin').getByRole('button', { name: /^Inspect / }).click();
    await page.locator('.inspector-image:not([hidden])').waitFor();
    const enhanced = await page.locator('.inspector-image').getAttribute('src');
    assert(decodeURIComponent(enhanced).includes('non-scaling-stroke'));
    assert(decodeURIComponent(enhanced).includes('stroke-width: 2px'));
    await page.locator('#previewMode').selectOption('original');
    await page.waitForFunction(previous => document.querySelector('.inspector-image').src !== previous, enhanced);
    const original = await page.locator('.inspector-image').getAttribute('src');
    assert(!decodeURIComponent(original).includes('non-scaling-stroke'));
    assert(decodeURIComponent(original).includes('stroke-width="0.01"'));
    const previews = {};
    for (const mode of ['enhanced', 'original']) {
      await page.locator('#previewMode').selectOption(mode);
      for (const name of ['thin', 'vertical', 'thick', 'compound', 'dashed', 'Clone', 'Text', 'Raster']) {
        await row(name).getByRole('button', { name: /^Inspect / }).click();
        const selected = await row(name).getAttribute('data-key');
        // Inspect is asynchronous; wait until its data URL contains the selected ID.
        await page.waitForFunction(name => decodeURIComponent(document.querySelector('.inspector-image').src).includes(`id="${name}"`), name);
        const src = await page.locator('.inspector-image').getAttribute('src'); previews[mode + '/' + name] = src;
        assert(src && selected);
        const box = await page.evaluate(src => new DOMParser().parseFromString(decodeURIComponent(src.split(',')[1]), 'image/svg+xml').documentElement.getAttribute('viewBox').split(' ').map(Number), src);
        assert(box[2] > 0 && box[3] > 0, name + ' has nonzero padded bounds');
      }
    }
    const raster = src => page.evaluate(src => {
      const d = new DOMParser().parseFromString(decodeURIComponent(src.split(',')[1]), 'image/svg+xml');
      return [...d.querySelector('image').attributes].map(a => [a.name, a.value]);
    }, src);
    assert.deepEqual(await raster(previews['enhanced/Raster']), await raster(previews['original/Raster']));
    await page.locator('#previewMode').selectOption('enhanced');
    await row('thick').getByRole('button', { name: /^Inspect / }).click();
    await button('Collapse all').click(); await button('Expand all').click();
    const splitter = page.getByRole('separator', { name: 'Resize operation mapping' });
    await splitter.focus(); await page.keyboard.press('End'); assert.equal(await splitter.getAttribute('aria-valuenow'), '480');
    await page.keyboard.press('Home'); assert.equal(await splitter.getAttribute('aria-valuenow'), '280');
    await page.keyboard.press('ArrowRight'); assert.equal(await splitter.getAttribute('aria-valuenow'), '296');
    const splitBounds = await splitter.boundingBox();
    await page.mouse.move(splitBounds.x + 4, splitBounds.y + 30); await page.mouse.down(); await page.mouse.move(splitBounds.x + 68, splitBounds.y + 30); await page.mouse.up();
    assert.equal(await splitter.getAttribute('aria-valuenow'), '360');
    await button('100%').click(); await page.waitForFunction(() => document.querySelector('.canvas-viewport').dataset.zoom === '1');
    const canvas = page.getByRole('region', { name: 'SVG canvas', exact: true });
    await canvas.focus(); const beforePan = await page.locator('.canvas-paper').getAttribute('style');
    await page.keyboard.press('ArrowRight'); assert.notEqual(await page.locator('.canvas-paper').getAttribute('style'), beforePan);
    await canvas.focus(); for(let i=0;i<25;i++) await page.keyboard.press('+'); await page.waitForFunction(() => document.querySelector('.canvas-viewport').dataset.zoom === '16'); for(let i=0;i<40;i++) await page.keyboard.press('-'); await page.waitForFunction(() => document.querySelector('.canvas-viewport').dataset.zoom === '0.01'); await button('Fit').click();
    await page.locator('#woodColor').selectOption('#FFFFFF');
    assert.equal(await page.getByAltText('Ordered laser batch preview').getAttribute('src'), initialBatch);
    assert.equal(await download(), initialExport, 'View controls never change export or runtime');
    await button('Source').click(); await page.getByAltText('Uploaded SVG source preview').waitFor();
    assert.equal(await page.getByAltText('Uploaded SVG source preview').getAttribute('src'), sourceUrl);
    await button('Batch').click(); await button('Clear preview').click(); await page.getByAltText('Uploaded SVG source preview').waitFor();
    assert(await button('Download SVG').isDisabled());

    // Ignore omitted occurrences while preserving the original clone target.
    await assignment('Original').selectOption('Ignore');
    await assignment('compound').selectOption('Engrave');
    await page.locator('#workspace-status').filter({ hasText: 'Estimated runtime was cleared' }).waitFor();
    await runtime(); assert.equal(await page.locator('#runtimeHours').inputValue(), ''); await closeModal('Cancel');
    assert((await row('thick').innerText()).includes('Ignored'));
    assert((await page.locator('.mapping-totals').innerText()).includes('1 ignored'));
    await page.locator('#boxWidth').fill('2'); await page.locator('#boxWidth').blur();
    await page.locator('#boxHeight').fill('1'); await page.locator('#boxHeight').blur();
    await button('Render batch').click(); await page.getByAltText('Ordered laser batch preview').waitFor();
    const omitted = await download(); const omissionMetadata = await metadata(omitted);
    assert.equal(omissionMetadata.ignoredElements.length, 1);
    assert.equal(omissionMetadata.ignoredElements[0].sourceId, 'thick');
    assert.equal(omissionMetadata.totalYield, 2); assert.equal(omissionMetadata.copyCount, 1);
    assert.equal(omissionMetadata.estimatedRuntimeSeconds, undefined);
    assert(!omitted.includes('non-scaling-stroke'), 'Inspection styling cannot enter output');
    assert(omitted.includes('Keep license') && omitted.includes('Keep hidden'));
    // Pixel equivalence against a manually prepared expected SVG: retain the clone
    // target in defs, omit only the original thick path, leave all other styling.
    let expected = fixture.replace('<g id="Original" transform="translate(10,20)">', '<defs><g id="target" transform="translate(10,20)"><path d="M5 10h35" fill="none" stroke="red" stroke-width="12" stroke-linecap="round"/><path d="M60 0h20v20h-20z M65 5v10h10v-10z" fill="orange" fill-rule="evenodd" stroke="none"/></g></defs><g id="Original" transform="translate(10,20)">').replace('<path id="thick" d="M5 10h35" fill="none" stroke="red" stroke-width="12" stroke-linecap="round"/>','').replace('href="#Original"','href="#target"');
    expected = await page.evaluate(xml => { const d=new DOMParser().parseFromString(xml,'image/svg+xml'); d.documentElement.insertBefore(d.getElementById('Original'),d.getElementById('Parent')); return new XMLSerializer().serializeToString(d); }, expected);
    fs.writeFileSync(path.join(artifacts,'ignore-expected.svg'),expected); fs.writeFileSync(path.join(artifacts,'ignore-actual.svg'),omitted);
    const changed = await page.evaluate(async ({ expected, actual }) => {
      async function pixels(xml) { const img = new Image(); img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml); await img.decode(); const c = document.createElement('canvas'); c.width = 192; c.height = 96; const ctx = c.getContext('2d'); ctx.drawImage(img,0,0); return ctx.getImageData(0,0,192,96).data; }
      const a = await pixels(expected), b = await pixels(actual); let changed = 0;
      for (let i = 0; i < a.length; i++) if (Math.abs(a[i] - b[i]) > 2) changed++;
      console.log('pixel differences',changed); return changed;
    }, { expected, actual: omitted });
    assert.equal(changed, 0, 'Included clone preserves the complete original subtree and physical appearance');
    await assignment('Document').selectOption('Ignore'); await assignment('compound').selectOption('');
    await page.locator('#workspace-status').filter({ hasText: 'Include at least one production element.' }).waitFor();
    assert(await button('Render batch').isDisabled()); assert(await button('Download SVG').isDisabled());
    await assignment('thin').selectOption('Score'); await button('Render batch').click(); await page.getByAltText('Ordered laser batch preview').waitFor();
    assert.equal((await metadata(await download())).operationMappings.length, 1);

    // Native modal focus, cancel drafts, compact drawer, no document scrolling.
    await button('Help').click(); const help = page.getByRole('dialog', { name: 'Workspace help' }); await help.waitFor();
    await page.keyboard.press('Tab'); assert(await page.evaluate(() => !!document.activeElement.closest('dialog:modal')));
    await page.keyboard.press('Escape'); await help.waitFor({ state: 'hidden' }); assert(await button('Help').evaluate(el => el === document.activeElement));
    for (const [width,height] of [[1366,768],[1280,720],[1199,720],[1024,600],[390,844],[320,568],[720,500]]) {
      await page.setViewportSize({ width,height }); await noScroll(`${width} × ${height}`);
      assert(await page.locator('.support-fallback').isVisible());
      if (width < 1200 || height < 640) {
        await button('Sheet').click(); await page.getByRole('dialog', { name: 'Sheet settings' }).waitFor();
        const originalWidth = await page.locator('#draft-boxWidth').inputValue();
        await page.locator('#draft-boxWidth').fill('123'); await closeModal('Cancel');
        await button('Sheet').click(); assert.equal(await page.locator('#draft-boxWidth').inputValue(), originalWidth); await closeModal('Cancel');
        await button('Program').click(); await page.getByRole('dialog', { name: 'Program metadata', exact: true }).waitFor();
        await page.locator('#draft-orderNumber').fill('Compact'); await closeModal('Apply');
        await button('Mapping').click(); await page.locator('.mapping-panel:modal').waitFor();
        await row('thin').getByRole('combobox').focus(); await page.keyboard.press('Escape'); await page.locator('.mapping-panel:modal').waitFor({ state: 'hidden' }); assert(await button('Mapping').evaluate(el => el === document.activeElement));
      }
      await page.screenshot({ path: path.join(artifacts, `workspace-${width}x${height}.png`) });
    }
    await page.setViewportSize({ width:1366,height:768 });
    await upload('Renamed.svg', omitted); await page.waitForFunction(() => document.querySelector('#workMode').value === 'EditMetadata');
    assert(await page.locator('.mapping-panel').isHidden()); assert.equal(await button('Render batch').count(),0);
    const edited = await download('Download updated SVG'); assert.deepEqual((await metadata(edited)).ignoredElements, omissionMetadata.ignoredElements);
    // Unsupported passive preview content must not block safe metadata edits.
    await page.locator('#svgUpload').setInputFiles({name:'NoPreview.svg',mimeType:'image/svg+xml',buffer:Buffer.from(omitted.replace('</svg>', '<foreignObject width="10" height="10"><div xmlns="http://www.w3.org/1999/xhtml">Unpreviewable</div></foreignObject></svg>'))}); await page.locator('.canvas-empty').waitFor();
    await page.waitForFunction(() => document.querySelector('#workMode').value === 'EditMetadata');
    assert(await button('Download updated SVG').isEnabled());
    assert((await download('Download updated SVG')).includes('foreignObject'));
    await page.locator('#svgUpload').setInputFiles({name:'InvalidPreview.svg',mimeType:'image/svg+xml',buffer:Buffer.from(fixture.replace('id="Document"','id="Document" onclick="alert(1)"'))});
    await page.waitForFunction(() => document.querySelector('#workMode').value === 'CreateBatch');
    assert(await row('thin').getByRole('button', {name:/^Inspect /}).isDisabled());
    assert.equal(await page.locator('.inspector-image').getAttribute('src'),null,'Invalid new uploads cannot show a stale selected preview');
    assert.deepEqual(errors, []);
    console.log('Workspace passed: Ignore/clone pixels, provenance, inspection isolation, draft dialogs, resizer, canvas state, compact drawers, downloads and viewport bounds.');
  } catch (e) { await page.screenshot({path:path.join(artifacts,'workspace-failure.png')}); console.error(await page.locator('body').innerText()); throw e; }
  finally { await browser.close(); }
})().catch(e => {console.error(e);process.exitCode=1;});
