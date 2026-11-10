// Public HTML, progressive enhancement, active-job links and real PWA navigation.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { routes } = require('../tools/verify-public-pages.cjs');
const url = new URL(process.env.LASERBATCHER_URL || 'http://127.0.0.1:5193/');
const artifacts = path.join(__dirname,'obj/browser-qa');
(async () => {
  const browser = await chromium.launch({headless:true,executablePath:process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,args:['--disable-features=IsolateSandboxedIframes']});
  const localOnly = r=>new URL(r.request().url()).origin===url.origin?r.continue():r.abort();
  const noHorizontalScroll = async page=>assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No horizontal document overflow');
  try {
    fs.mkdirSync(artifacts,{recursive:true});
    const textOnly=await browser.newContext({javaScriptEnabled:false,viewport:{width:390,height:844}});
    await textOnly.route('**/*',localOnly);
    const textPage=await textOnly.newPage();
    for (const route of routes) {
      const response=await textPage.goto(new URL(route,url).href);
      assert.equal(response.status(),200);
      assert.equal(await textPage.locator('h1').count(),1);
      assert.equal(await textPage.locator('link[rel=canonical]').getAttribute('href'),'https://batcher.offthewallcreations.xyz'+route);
      if(route!=='/') {
        assert((await textPage.locator('article').innerText()).length>1500,'Complete guide text without JS');
        assert(await textPage.getByRole('link',{name:'Open LaserBatcher',exact:true}).first().isVisible());
        assert((await textPage.locator('script[type="application/ld+json"]').textContent()).includes('BreadcrumbList'));
        assert(await textPage.locator('.public-theme').isHidden());
      } else assert(await textPage.getByRole('link',{name:'About & guides (new tab)',exact:true}).isVisible());
      await noHorizontalScroll(textPage);
    }
    await textOnly.close();
    console.log('All six pages expose content, navigation and metadata without JavaScript.');

    const fresh=await browser.newContext({viewport:{width:1366,height:768},colorScheme:'light'});
    await fresh.route('**/*',localOnly);
    const page=await fresh.newPage(), requested=[], errors=[];
    page.on('request',r=>requested.push(r.url())); page.on('pageerror',e=>errors.push(e.message));
    for(const route of routes.slice(1)) {
      await page.goto(new URL(route,url).href);
      await page.locator('#public-appearance').waitFor();
      assert.equal(await page.locator('h1').count(),1);
      await page.keyboard.press('Tab'); assert.equal(await page.locator(':focus').innerText(),'Skip to content');
      await page.keyboard.press('Enter'); assert.equal(await page.evaluate(()=>document.activeElement.id),'main');
      // Dimensions reserve image space before lazy decoding and remain accurate after it.
      for(const img of await page.locator('article img').all()) {
        assert.equal(await img.getAttribute('loading'),'lazy');
        const before=await img.boundingBox(); await img.scrollIntoViewIfNeeded(); await img.evaluate(e=>e.decode());
        const after=await img.boundingBox(); assert.equal(before.height,after.height); assert.equal(before.width,after.width);
        assert(await img.evaluate(e=>e.naturalWidth===+e.getAttribute('width')&&e.naturalHeight===+e.getAttribute('height')));
      }
      for (const theme of ['light','dark']) {
        await page.locator('#public-appearance').selectOption(theme);
        assert.equal(await page.locator('html').getAttribute('data-bs-theme'),theme);
        for (const width of [320,390,768,1366]) {await page.setViewportSize({width,height:844});await noHorizontalScroll(page);}
      }
      await page.setViewportSize({width:640,height:720});
      await page.evaluate(()=>document.documentElement.style.zoom='2'); await noHorizontalScroll(page);
      await page.evaluate(()=>document.documentElement.style.zoom='');
    }
    assert(!requested.some(u=>/_framework\/|startup\.js|service-worker/.test(u)),'Fresh guides never fetch Blazor or a worker');
    assert.equal((await page.evaluate(()=>navigator.serviceWorker.getRegistrations())).length,0);
    await page.goto(new URL('/about/?utm_source=test',url).href);
    assert.equal(await page.locator('html').getAttribute('data-bs-theme'),'dark','Preference survives navigation');
    await page.locator('#public-appearance').selectOption('system'); await page.emulateMedia({colorScheme:'light'});
    assert.equal(await page.locator('html').getAttribute('data-bs-theme'),'light');
    await page.emulateMedia({colorScheme:'dark'}); await page.waitForFunction(()=>document.documentElement.dataset.bsTheme==='dark');
    await page.screenshot({path:path.join(artifacts,'public-about-dark-mobile.png'),fullPage:true});
    await page.setViewportSize({width:1366,height:768}); await page.locator('#public-appearance').selectOption('light');
    await page.goto(new URL('/guides/svg-batching/',url).href);
    await page.screenshot({path:path.join(artifacts,'public-batching-light.png'),fullPage:true});
    assert.deepEqual(errors,[]); await fresh.close();
    const denied=await browser.newContext(); await denied.route('**/*',localOnly);
    await denied.addInitScript(()=>{Object.defineProperty(window,'localStorage',{get(){throw new DOMException('Blocked','SecurityError');}});});
    const deniedPage=await denied.newPage(); await deniedPage.goto(new URL('/about/',url).href);
    await deniedPage.locator('#public-appearance').selectOption('dark'); assert.equal(await deniedPage.locator('html').getAttribute('data-bs-theme'),'dark');
    await denied.close();
    console.log('Fresh guides use no Blazor/worker; themes, blocked storage, keyboard, mobile, 200% layout and image dimensions passed.');

    const installed=await browser.newContext({viewport:{width:1366,height:768}}); await installed.route('**/*',localOnly);
    const app=await installed.newPage(); await app.goto(url.href); await app.locator('.app-header').waitFor({timeout:60000});
    assert.equal(await app.title(),'LaserBatcher | SVG Batching for Laser Cutting');
    await app.evaluate(()=>navigator.serviceWorker.ready); await app.reload(); await app.locator('.app-header').waitFor();
    assert(await app.evaluate(()=>!!navigator.serviceWorker.controller));
    await app.locator('#svgUpload').setInputFiles({name:'Example_x1.svg',mimeType:'image/svg+xml',buffer:Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="1in" height="1in"><path id="Score" d="M10 10h50" fill="none" stroke="black"/></svg>')});
    await app.getByAltText('Uploaded SVG source preview').waitFor();
    await app.getByRole('button',{name:'Render batch',exact:true}).click(); await app.getByAltText('Ordered laser batch preview').waitFor();
    const source=await app.getByAltText('Ordered laser batch preview').getAttribute('src');
    const popupPending=app.waitForEvent('popup'); await app.getByRole('link',{name:'About & guides (new tab)',exact:true}).click();
    const guide=await popupPending; await guide.waitForLoadState();
    assert.equal(new URL(guide.url()).pathname,'/about/'); assert.equal(await guide.locator('.app-header').count(),0);
    assert.equal(await app.getByAltText('Ordered laser batch preview').getAttribute('src'),source);
    assert(await app.getByRole('button',{name:'Download SVG',exact:true}).isEnabled());
    assert.equal(await guide.evaluate(()=>window.opener),null,'New tab cannot access job document');
    for(const offline of [false,true]) {
      await installed.setOffline(offline);
      for(const route of routes.slice(1)) {
        const response=await guide.goto(new URL(route+'?campaign=check',url).href);
        assert.equal(response.status(),200); assert(response.fromServiceWorker());
        assert.equal(await guide.locator('.app-header').count(),0);
        assert((await guide.locator('article').innerText()).length>1500);
        assert((await guide.locator('link[rel=canonical]').getAttribute('href')).endsWith(route));
      }
      await guide.goto(new URL('/about?source=check',url).href); assert.equal(new URL(guide.url()).pathname,'/about/');
      assert.equal(new URL(guide.url()).search,'?source=check');
      await guide.goto(new URL('/guides/svg-batching/index.html?source=alias',url).href);
      assert.equal(await guide.locator('link[rel=canonical]').getAttribute('href'),'https://batcher.offthewallcreations.xyz/guides/svg-batching/');
      for(const missing of ['/not-a-real-page','/guides/not-a-real-guide/']) {
        const response=await guide.goto(new URL(missing,url).href); assert.equal(response.status(),404);
        assert.equal(await guide.locator('h1').innerText(),'Page not found');
        assert.equal(await guide.locator('meta[name=robots]').getAttribute('content'),'noindex');
        assert.equal(await guide.locator('script[src*="blazor"]').count(),0);
      }
    }
    await guide.goto(new URL('/index.html?source=alias',url).href); await guide.locator('.app-header').waitFor();
    assert.equal(await guide.title(),'LaserBatcher | SVG Batching for Laser Cutting');
    await installed.close();
    console.log('Active-job new-tab link, cached public documents, aliases, redirects and genuine online/offline 404s passed.');
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
