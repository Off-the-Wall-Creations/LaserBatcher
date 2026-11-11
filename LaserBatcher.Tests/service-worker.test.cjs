const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../LaserBatcher/wwwroot/service-worker.published.js'), 'utf8');
const origin = 'https://batcher.offthewallcreations.xyz';
function worker(network = async () => new Response('network')) {
  const calls = [], documents = new Map([
    ['index.html', '<html>Cached workspace release</html>'],
    ['about/index.html', '<html>Offline overview</html>'],
    ['404.html', '<html>Page not found</html>'],
    ['_framework/app.dll', 'cached assembly']
  ]);
  const context = vm.createContext({
    URL, Request, Response, console,
    self: { location: { origin }, assetsManifest: { version: 'test', assets: [...documents.keys()].map(url=>({ url })) }, importScripts() {}, addEventListener() {} },
    fetch: async (request, options) => { calls.push({ request, options }); return network(request, options); },
    caches: { open: async () => ({ match: async request => {
      const key = typeof request === 'string' ? request : new URL(request.url).pathname.slice(1);
      return documents.has(key) ? new Response(documents.get(key)) : undefined;
    } }) }
  });
  vm.runInContext(source, context);
  return { calls, fetch: (pathname, mode = 'navigate') => context.onFetch({ request: { url: origin + pathname, method: 'GET', mode } }) };
}

for (const route of ['/sitemap.xml', '/robots.txt']) {
  test(`${route} always fetches the network with HTTP caching disabled, including queries and subresource requests`, async () => {
    const w = worker(async () => new Response('fresh crawler file'));
    for (const mode of ['navigate', 'cors']) {
      assert.equal(await (await w.fetch(route + '?probe=1', mode)).text(), 'fresh crawler file');
      assert.equal(w.calls.at(-1).options.cache, 'no-store');
    }
  });
  test(`${route} stays a non-HTML 503 when offline instead of turning into the app or a 404 document`, async () => {
    const w = worker(async () => { throw new TypeError('offline'); });
    const response = await w.fetch(route);
    assert.equal(response.status, 503);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert(response.headers.get('content-type').startsWith('text/plain'));
    assert(!(await response.text()).includes('<html>'));
  });
}

test('public navigation revalidates online and uses its own published document only on network failure', async () => {
  const w = worker(async () => new Response('<html>New overview</html>'));
  assert.equal(await (await w.fetch('/about/?ref=test')).text(), '<html>New overview</html>');
  assert.equal(w.calls[0].options.cache, 'no-cache');
  const offline = worker(async () => { throw new TypeError('offline'); });
  for (const route of ['/about/', '/about/index.html?ref=test'])
    assert.equal(await (await offline.fetch(route)).text(), '<html>Offline overview</html>');
});

test('real public-page HTTP failures are not hidden behind stale successful pages', async () => {
  for (const status of [404, 503]) {
    const w = worker(async () => new Response('server response', { status }));
    const response = await w.fetch('/about/');
    assert.equal(response.status, status); assert.equal(await response.text(), 'server response');
  }
});

test('workspace aliases and framework resources retain one cached release', async () => {
  const w = worker(async () => { throw new Error('Must not mix in a new release'); });
  for (const route of ['/', '/index.html?ref=test'])
    assert.equal(await (await w.fetch(route)).text(), '<html>Cached workspace release</html>');
  assert.equal(await (await w.fetch('/_framework/app.dll', 'cors')).text(), 'cached assembly');
  assert.equal(w.calls.length, 0);
});

test('unknown offline navigation remains HTTP 404, and known directory aliases retain their query', async () => {
  const w = worker(async () => { throw new TypeError('offline'); });
  const response = await w.fetch('/not-a-page');
  assert.equal(response.status, 404); assert((await response.text()).includes('Page not found'));
  const redirect = await w.fetch('/about?ref=test');
  assert.equal(redirect.status, 301); assert.equal(redirect.headers.get('location'), origin+'/about/?ref=test');
});
