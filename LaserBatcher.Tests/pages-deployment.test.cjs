const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { preparePages, verifyPages } = require('../tools/prepare-pages.cjs');
const { routes, fileFor } = require('../tools/verify-public-pages.cjs');
const { createServer } = require('./serve-published.cjs');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'laserbatcher-pages-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(root)), path.resolve(os.tmpdir()));
    assert(path.basename(root).startsWith('laserbatcher-pages-'));
    fs.rmSync(root, { recursive: true, force: true });
  });
  const files = {
    'CNAME': 'batcher.offthewallcreations.xyz\n',
    'manifest.json': '{"name":"LaserBatcher","start_url":"./"}',
    'css/app.css': 'body { color: black; }\n',
    'service-worker.js': 'self.addEventListener("fetch", () => {});\n',
    '_framework/blazor.webassembly.js': '// Blazor placeholder\n'
  };
  // Use the actual authored documents. Compiled/runtime resources may be placeholders;
  // public files and hashes must satisfy the same contract as a published build.
  const source = path.resolve(__dirname, '../LaserBatcher/wwwroot');
  const add = name => { if (!(name in files)) files[name] = fs.existsSync(path.join(source,name)) ? fs.readFileSync(path.join(source,name)) : '// compiled placeholder\n'; };
  for (const name of [...routes.map(fileFor), '404.html','robots.txt','sitemap.xml','brand/laserbatcher-sharing.png','brand/laserbatcher-sharing.svg']) add(name);
  for (const name of [...routes.map(fileFor),'404.html']) {
    files[name] = files[name].toString();
    for (const m of files[name].matchAll(/<(?:script|link|img)\b[^>]*?\b(?:src|href)=["']([^"']+)["'][^>]*>/gi)) {
      if (m[0].includes('rel="canonical"')) continue;
      const resource = new URL(m[1], 'https://batcher.offthewallcreations.xyz/'+name);
      if (resource.origin === 'https://batcher.offthewallcreations.xyz') add(resource.pathname.slice(1));
    }
  }
  const write = (name, content) => { const file = path.join(root, name); fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, content); };
  for (const [name, content] of Object.entries(files)) write(name, content);
  const assets = Object.entries(files).map(([url, content]) => ({ url, hash: 'sha256-' + createHash('sha256').update(content).digest('base64') }));
  const writeManifest = () => write('service-worker-assets.js', 'self.assetsManifest = ' + JSON.stringify({ assets, version: 'test' }) + ';');
  writeManifest();
  return { root, files, write, assets, writeManifest };
}

test('root-domain preparation preserves hashed bytes and writes Pages support files', t => {
  const f = fixture(t);
  assert.equal(preparePages(f.root).assetCount, Object.keys(f.files).length);
  for (const [name, content] of Object.entries(f.files)) assert(fs.readFileSync(path.join(f.root, name)).equals(Buffer.from(content)), name);
  assert.equal(fs.readFileSync(path.join(f.root, '404.html'), 'utf8'), f.files['404.html']);
  assert(fs.existsSync(path.join(f.root, '.nojekyll')));
  assert.equal(fs.readFileSync(path.join(f.root, '.gitattributes'), 'utf8'), '* -text\n');
  assert.doesNotThrow(() => preparePages(f.root), 'Preparation is repeatable');
});

test('rejects the deployed /LaserBatcher/ base-path regression', t => {
  const f = fixture(t); f.write('index.html', f.files['index.html'].replace('href="/"', 'href="/LaserBatcher/"'));
  assert.throws(() => preparePages(f.root), /requires exactly one <base href="\/"/);
  assert(!fs.existsSync(path.join(f.root, '.nojekyll')), 'Invalid output is rejected before preparation');
});

test('rejects hardcoded repository paths even with the correct base', t => {
  const f = fixture(t); f.write('index.html', f.files['index.html'].replace('href="css/app.css"', 'href="/LaserBatcher/css/app.css"'));
  assert.throws(() => verifyPages(f.root), /Missing published asset: LaserBatcher\/css\/app.css/);
});

test('rejects any post-publish index change that invalidates its hash', t => {
  const f = fixture(t); f.write('index.html', f.files['index.html'] + '\n');
  assert.throws(() => verifyPages(f.root), /PWA integrity mismatch: index.html/);
});

test('checks other asset hashes, including line ending changes', t => {
  const f = fixture(t); f.write('css/app.css', f.files['css/app.css'].replace(/\n/g, '\r\n'));
  assert.throws(() => verifyPages(f.root), /PWA integrity mismatch: css\/app.css/);
});

test('requires the custom-domain file', t => {
  const f = fixture(t); fs.unlinkSync(path.join(f.root, 'CNAME'));
  assert.throws(() => verifyPages(f.root), /Missing published asset: CNAME/);
  f.write('CNAME', 'wrong.example.com');
  assert.throws(() => verifyPages(f.root), /CNAME must contain/);
});

test('requires framework assets and the cached index', t => {
  const f = fixture(t); fs.unlinkSync(path.join(f.root, '_framework/blazor.webassembly.js'));
  assert.throws(() => verifyPages(f.root), /Missing published asset: _framework\/blazor.webassembly.js/);
  f.write('_framework/blazor.webassembly.js', f.files['_framework/blazor.webassembly.js']);
  f.assets.splice(f.assets.findIndex(asset => asset.url === 'index.html'), 1); f.writeManifest();
  assert.throws(() => verifyPages(f.root), /must include index.html/);
});

test('rejects duplicate or out-of-directory manifest paths', t => {
  const f = fixture(t); f.assets.push(f.assets[0]); f.writeManifest();
  assert.throws(() => verifyPages(f.root), /Duplicate PWA asset/);
  f.assets.at(-1).url = '../outside.js'; f.writeManifest();
  assert.throws(() => verifyPages(f.root), /outside the publish directory/);
});

test('checks canonical documents separately from assets and rejects aliases or duplicate titles', t => {
  const f = fixture(t);
  f.write('about/index.html', f.files['about/index.html'].replace('rel="canonical" href="https://batcher.offthewallcreations.xyz/about/"', 'rel="canonical" href="https://batcher.offthewallcreations.xyz/about/index.html?campaign=test"'));
  assert.throws(()=>verifyPages(f.root), /expected one absolute canonical/);
  f.write('about/index.html', f.files['about/index.html'].replace(/<title>[^<]+/, '<title>LaserBatcher | SVG Batching for Laser Cutting'));
  assert.throws(()=>verifyPages(f.root), /duplicate title/);
});

test('validates JSON-LD, internal links, 404, screenshot and sharing assets', t => {
  const f = fixture(t);
  for (const [file, from, to, expected] of [
    ['about/index.html', '"@context":', 'invalid:', /JSON|Unexpected|property/],
    ['about/index.html', 'href="/guides/svg-batching/"', 'href="/missing/"', /Missing published asset/],
    ['404.html', 'content="noindex"', 'content="index"', /must be noindex/]
  ]) {
    f.write(file, f.files[file].replace(from,to)); assert.throws(()=>verifyPages(f.root),expected); f.write(file,f.files[file]);
  }
  fs.unlinkSync(path.join(f.root,'images/batch-workspace.jpg'));
  assert.throws(()=>verifyPages(f.root),/Missing published asset: images\/batch-workspace.jpg/);
  f.write('images/batch-workspace.jpg',f.files['images/batch-workspace.jpg']);
  const png=Buffer.from(f.files['brand/laserbatcher-sharing.png']); png.writeUInt32BE(1000,16); f.write('brand/laserbatcher-sharing.png',png);
  assert.throws(()=>verifyPages(f.root),/1200×630 PNG/);
});

test('requires all six canonical URLs in sitemap and public documents in the PWA manifest', t => {
  const f=fixture(t);
  f.write('sitemap.xml',f.files['sitemap.xml'].toString().replace('<urlset ', '<url><loc>https://example.com/</loc></url><urlset '));
  assert.throws(()=>verifyPages(f.root),/exactly the six/);
  f.write('sitemap.xml',f.files['sitemap.xml']);
  f.assets.splice(f.assets.findIndex(a=>a.url==='about/index.html'),1); f.writeManifest();
  assert.throws(()=>verifyPages(f.root),/must include about\/index.html/);
});

test('local host reproduces directory indexes, slash redirects, query strings and custom 404 status', async t => {
  const f=fixture(t), server=createServer(f.root);
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  t.after(()=>new Promise(resolve=>server.close(resolve)));
  const base=`http://127.0.0.1:${server.address().port}`;
  const redirect=await fetch(base+'/about?source=test',{redirect:'manual'});
  assert.equal(redirect.status,301); assert.equal(redirect.headers.get('location'),'/about/?source=test');
  assert.equal(await (await fetch(base+'/about/')).text(),f.files['about/index.html']);
  assert.equal(await (await fetch(base+'/about/index.html')).text(),f.files['about/index.html']);
  const missing=await fetch(base+'/not-a-page'); assert.equal(missing.status,404);
  assert.equal(await missing.text(),f.files['404.html']);
  assert.equal((await fetch(base+'/%2e%2e%5coutside')).status,403);
});
