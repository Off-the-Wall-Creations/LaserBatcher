// Reproduce the historical catch-all worker and exercise a real waiting-worker
// upgrade without skipWaiting, clearing user data, or reloading an active job.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { createServer } = require('./serve-published.cjs');
const directory = path.resolve(process.env.LASERBATCHER_PUBLISHED || 'LaserBatcher.Tests/obj/pages/wwwroot');
const legacy = `
self.addEventListener('install', event => event.waitUntil(caches.open('offline-cache-legacy-test').then(cache=>cache.add('index.html'))));
self.addEventListener('fetch', event => {
  if (event.request.method === 'GET' && event.request.mode === 'navigate')
    event.respondWith(caches.open('offline-cache-legacy-test').then(cache=>cache.match('index.html')));
});`;
(async()=>{
  const server=createServer(directory), serve=server.listeners('request')[0]; server.removeAllListeners('request');
  let oldWorker=true, revisedOverview=false;
  const crawlerRequests=[];
  server.on('request',(request,response)=>{
    const route=new URL(request.url,'http://localhost').pathname;
    if(route==='/service-worker.js' && oldWorker) {response.writeHead(200,{'Content-Type':'text/javascript','Cache-Control':'no-store'}).end(legacy);return;}
    if(route==='/sitemap.xml'||route==='/robots.txt') crawlerRequests.push({route,cache:request.headers['cache-control']});
    if(route==='/about/' && revisedOverview) {response.writeHead(200,{'Content-Type':'text/html','Cache-Control':'max-age=600'}).end(fs.readFileSync(path.join(directory,'about/index.html'),'utf8').replace('Prepare repeatable SVG batches for your laser','New guide content from the network'));return;}
    serve(request,response);
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base=`http://127.0.0.1:${server.address().port}/`;
  const browser=await chromium.launch({headless:true,executablePath:process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,args:['--disable-features=IsolateSandboxedIframes']});
  try {
    const ctx=await browser.newContext({viewport:{width:1366,height:768}});
    await ctx.route('**/*',r=>r.request().url().startsWith(base)?r.continue():r.abort());
    let page=await ctx.newPage(); page.setDefaultTimeout(60000);
    await page.goto(base); await page.locator('.app-header').waitFor();
    await page.evaluate(()=>navigator.serviceWorker.ready); await page.reload(); await page.locator('.app-header').waitFor();
    assert(await page.evaluate(()=>!!navigator.serviceWorker.controller));
    const oldSitemap=await page.goto(base+'sitemap.xml');
    assert(oldSitemap.headers()['content-type'].includes('text/html'));
    await page.getByText("Sorry, there's nothing at this address.",{exact:true}).waitFor();
    console.log('Reproduced screenshot: the legacy worker turns sitemap.xml into Blazor Not found.');
    await page.goto(base); await page.locator('.app-header').waitFor();
    await page.locator('#svgUpload').setInputFiles({name:'KeepJob_x1.svg',mimeType:'image/svg+xml',buffer:Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="1in" height="1in"><path id="Score" d="M1 1h20" stroke="black"/></svg>')});
    await page.getByAltText('Uploaded SVG source preview').waitFor();
    await page.getByRole('button',{name:'Render batch',exact:true}).click(); await page.getByAltText('Ordered laser batch preview').waitFor();
    const rendered=await page.getByAltText('Ordered laser batch preview').getAttribute('src');
    oldWorker=false;
    await page.evaluate(async()=>{await (await navigator.serviceWorker.getRegistration()).update();});
    await page.waitForFunction(async()=>!!(await navigator.serviceWorker.getRegistration()).waiting);
    await page.locator('#app-update-notice').waitFor({state:'visible'});
    assert.equal(await page.getByAltText('Ordered laser batch preview').getAttribute('src'),rendered);
    const worker=ctx.serviceWorkers().at(-1);
    // Close the last site tab without deleting registrations or caches.
    await page.close();
    await worker.evaluate(async()=>{
      for(let i=0;i<200;i++) {
        if(!self.registration.waiting && self.registration.active?.state==='activated' && !(await caches.keys()).includes('offline-cache-legacy-test')) return;
        await new Promise(resolve=>setTimeout(resolve,50));
      }
      throw new Error('New worker did not activate after the final site tab closed.');
    });
    page=await ctx.newPage(); page.setDefaultTimeout(60000);
    await page.goto(base); await page.locator('.app-header').waitFor();
    assert(await page.evaluate(()=>!!navigator.serviceWorker.controller));
    assert(!(await page.evaluate(()=>caches.keys())).includes('offline-cache-legacy-test'));
    for(const route of ['sitemap.xml','robots.txt']) {
      const response=await page.goto(base+route);
      assert.equal(response.status(),200); assert(response.fromServiceWorker());
      assert(response.headers()['content-type'].includes(route.endsWith('.xml')?'application/xml':'text/plain'));
      assert(!(await response.text()).includes('<html'));
      const record=crawlerRequests.findLast(r=>r.route==='/'+route);
      assert(record && /no-cache|no-store/.test(record.cache),'Crawler request bypasses HTTP caching');
    }
    await page.goto(base+'about/');
    revisedOverview=true; await page.reload();
    assert.equal(await page.locator('h1').innerText(),'New guide content from the network','Online guide reload is not pinned to the installed version');
    await ctx.setOffline(true); await page.reload();
    assert.equal(await page.locator('h1').innerText(),'Prepare repeatable SVG batches for your laser','Offline guide uses its integrity-checked document');
    for(const route of ['sitemap.xml','robots.txt']) {
      const response=await page.goto(base+route);
      assert.equal(response.status(),503); assert(response.headers()['content-type'].includes('text/plain'));
      assert(!(await response.text()).includes('<html'));
    }
    await page.goto(base); await page.locator('.app-header').waitFor();
    console.log('Actual worker upgrade preserves jobs; crawler files stay raw, guides refresh online and workspace/guides work offline.');
    await ctx.close();
  } finally {await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});
