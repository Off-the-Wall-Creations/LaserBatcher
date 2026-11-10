// Reproducible public screenshots from synthetic artwork; run before final publish.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../LaserBatcher/wwwroot');
const url = process.env.LASERBATCHER_URL || 'http://127.0.0.1:5193/';
(async () => {
  const browser = await chromium.launch({ headless:true, executablePath:process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE, args:['--disable-features=IsolateSandboxedIframes'] });
  try {
    const context = await browser.newContext({ viewport:{ width:1366, height:768 }, colorScheme:'light', deviceScaleFactor:1 });
    await context.route('**/*', r => new URL(r.request().url()).origin === new URL(url).origin ? r.continue() : r.abort());
    await context.addInitScript(() => { if (window===window.top) { navigator.serviceWorker.register=async()=>({}); localStorage.setItem('laserbatcher.theme','light'); } });
    const page = await context.newPage();
    const svg = fs.readFileSync(path.join(root,'brand/laserbatcher-sharing.svg'),'utf8');
    const png = await page.evaluate(async svg => {
      const img = new Image(); img.src='data:image/svg+xml;charset=utf-8,'+encodeURIComponent(svg); await img.decode();
      const canvas=document.createElement('canvas'); canvas.width=1200; canvas.height=630;
      canvas.getContext('2d').drawImage(img,0,0); return canvas.toDataURL('image/png').split(',')[1];
    },svg);
    fs.writeFileSync(path.join(root,'brand/laserbatcher-sharing.png'),Buffer.from(png,'base64'));
    await page.goto(url); await page.locator('.app-header').waitFor({timeout:60000});
    await page.locator('#svgUpload').setInputFiles({ name:'GeometricTag_x1.svg', mimeType:'image/svg+xml', buffer:fs.readFileSync(path.resolve(__dirname,'../LaserBatcher.Tests/Fixtures/guide-demo.svg')) });
    await page.getByAltText('Uploaded SVG source preview').waitFor();
    await page.locator('#woodColor').selectOption('#FFFFFF');
    await page.getByRole('combobox',{name:/^Operation for Construction /}).selectOption('Ignore');
    for (const [id,value] of [['boxWidth','8'],['boxHeight','5'],['horizontalMargin','0.25'],['verticalMargin','0.25'],['spaceBetween','0.125'],['copyQuantity','6']]) { await page.locator('#'+id).fill(value); await page.locator('#'+id).press('Tab'); }
    await page.getByRole('button',{name:'Render batch',exact:true}).click();
    await page.getByAltText('Ordered laser batch preview').waitFor();
    await page.locator('.mapping-thumbnail[data-preview-ready="true"]').first().waitFor();
    // Wait for canvas fit and image decode, not arbitrary rendering delays.
    await page.getByAltText('Ordered laser batch preview').evaluate(img=>img.decode());
    await page.getByRole('button',{name:'Fit',exact:true}).click();
    await page.locator('h1').click();
    fs.mkdirSync(path.join(root,'images'),{recursive:true});
    await page.screenshot({path:path.join(root,'images/batch-workspace.jpg'),type:'jpeg',quality:85});
    await page.getByRole('button',{name:'Source',exact:true}).click();
    await page.getByRole('button',{name:/^Inspect ExteriorCut /}).click();
    await page.locator('.inspector-image:not([hidden])').waitFor();
    await page.locator('.inspector-image').evaluate(img=>img.decode());
    await page.getByRole('combobox',{name:/^Operation for Construction /}).scrollIntoViewIfNeeded();
    await page.locator('h1').click();
    await page.screenshot({path:path.join(root,'images/mapping-inspection.jpg'),type:'jpeg',quality:85});
    console.log('Exported 1200×630 sharing PNG and two 1366×768 synthetic screenshots.');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
