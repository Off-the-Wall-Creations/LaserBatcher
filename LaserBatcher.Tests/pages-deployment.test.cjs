const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { preparePages, verifyPages } = require('../tools/prepare-pages.cjs');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'laserbatcher-pages-'));
  t.after(() => {
    assert.equal(path.dirname(path.resolve(root)), path.resolve(os.tmpdir()));
    assert(path.basename(root).startsWith('laserbatcher-pages-'));
    fs.rmSync(root, { recursive: true, force: true });
  });
  const files = {
    'index.html': '<html><head><base href="/" /><link href="manifest.json" rel="manifest" /><link href="css/app.css" rel="stylesheet" /></head><body><script src="_framework/blazor.webassembly.js"></script><script src="https://example.com/external.js"></script></body></html>',
    'CNAME': 'batcher.offthewallcreations.xyz\n',
    'manifest.json': '{"name":"LaserBatcher","start_url":"./"}',
    'css/app.css': 'body { color: black; }\n',
    'service-worker.js': 'self.addEventListener("fetch", () => {});\n',
    '_framework/blazor.webassembly.js': '// Blazor placeholder\n'
  };
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
  for (const [name, content] of Object.entries(f.files)) assert.equal(fs.readFileSync(path.join(f.root, name), 'utf8'), content);
  assert.equal(fs.readFileSync(path.join(f.root, '404.html'), 'utf8'), f.files['index.html']);
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
