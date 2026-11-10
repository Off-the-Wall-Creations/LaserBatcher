// Prepare a Blazor publish directory for this repository's custom-domain site.
// Never rewrite index.html after publish: its bytes are in the PWA manifest.
const fs = require('node:fs');
const path = require('node:path');
const { createHash } = require('node:crypto');
const { verifyPublicPages } = require('./verify-public-pages.cjs');

const site = new URL('https://batcher.offthewallcreations.xyz/');

function verifyPages(directory) {
  const root = path.resolve(directory);
  function readAsset(relative) {
    if (typeof relative !== 'string' || !relative || relative.startsWith('/') || /[\\?#:]/.test(relative))
      throw new Error(`Invalid published asset path: ${relative}`);
    const target = path.resolve(root, relative);
    if (!target.startsWith(root + path.sep)) throw new Error(`Asset is outside the publish directory: ${relative}`);
    if (!fs.existsSync(target) || !fs.statSync(target).isFile()) throw new Error(`Missing published asset: ${relative}`);
    return fs.readFileSync(target);
  }

  const html = readAsset('index.html').toString('utf8');
  const bases = [...html.matchAll(/<base\b[^>]*\bhref\s*=\s*["']([^"']*)["'][^>]*>/gi)];
  if (bases.length !== 1 || bases[0][1] !== site.pathname)
    throw new Error(`The custom domain ${site.hostname} requires exactly one <base href="/" />. Set the base before dotnet publish, never afterward.`);
  if (readAsset('CNAME').toString('utf8').trim() !== site.hostname)
    throw new Error(`CNAME must contain ${site.hostname}.`);

  const publicPages = verifyPublicPages(readAsset);
  for (const required of ['manifest.json', 'service-worker.js', '_framework/blazor.webassembly.js']) readAsset(required);

  const manifestScript = readAsset('service-worker-assets.js').toString('utf8');
  const match = manifestScript.match(/^\s*self\.assetsManifest\s*=\s*(\{[\s\S]*\})\s*;?\s*$/);
  if (!match) throw new Error('Cannot read the generated service-worker-assets.js manifest.');
  const manifest = JSON.parse(match[1]);
  if (!Array.isArray(manifest.assets) || !manifest.version) throw new Error('Incomplete PWA asset manifest.');
  const urls = new Set();
  for (const asset of manifest.assets) {
    if (urls.has(asset.url)) throw new Error(`Duplicate PWA asset: ${asset.url}`);
    urls.add(asset.url);
    const actual = 'sha256-' + createHash('sha256').update(readAsset(asset.url)).digest('base64');
    if (asset.hash !== actual)
      throw new Error(`PWA integrity mismatch: ${asset.url}. A published file was changed after hashing; update source files before dotnet publish.`);
  }
  if (!urls.has('index.html')) throw new Error('The PWA manifest must include index.html.');
  for (const file of publicPages.requiredAssets) {
    if (!urls.has(file)) throw new Error(`The PWA manifest must include ${file}.`);
  }
  return { root, assetCount: urls.size };
}

function preparePages(directory) {
  const result = verifyPages(directory);
  // 404.html and every public page were authored before publish and hashing.
  fs.writeFileSync(path.join(result.root, '.nojekyll'), '');
  // A branch-based deploy must preserve the exact bytes used by Blazor's hashes.
  fs.writeFileSync(path.join(result.root, '.gitattributes'), '* -text\n');
  return result;
}

module.exports = { preparePages, verifyPages };
if (require.main === module) {
  try {
    if (process.argv.length !== 3) throw new Error('Usage: node tools/prepare-pages.cjs <published-wwwroot>');
    const result = preparePages(process.argv[2]);
    console.log(`Pages output verified: ${site.href}, ${result.assetCount} asset hashes, CNAME, 404.html and .nojekyll.`);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
