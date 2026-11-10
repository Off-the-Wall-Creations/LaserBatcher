// Deliberately small validator for the authored static HTML contract; no runtime dependency.
const site = new URL('https://batcher.offthewallcreations.xyz/');
const routes = ['/', '/about/', '/guides/svg-batching/', '/guides/svg-preparation/', '/guides/metadata-and-runtime/', '/guides/operation-mapping/'];
const fileFor = route => route.slice(1) + 'index.html';
const decode = s => s.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>');
function attributes(tag) {
  return Object.fromEntries([...tag.matchAll(/([\w:-]+)\s*=\s*(["'])([\s\S]*?)\2/g)].map(m => [m[1].toLowerCase(), decode(m[3])]));
}
function verifyPublicPages(readAsset) {
  const requiredAssets = new Set(), titles = new Set(), descriptions = new Set();
  const read = name => { requiredAssets.add(name); return readAsset(name); };
  const check = (ok, message) => { if (!ok) throw new Error(message); };
  const localFile = (url, document) => {
    const target = new URL(url, document);
    if (target.origin !== site.origin) return null;
    const pathname = decodeURIComponent(target.pathname).slice(1);
    return { target, file: pathname.endsWith('/') || !pathname ? pathname + 'index.html' : pathname };
  };
  for (const route of [...routes, '/404.html']) {
    const file = route === '/404.html' ? '404.html' : fileFor(route);
    const html = read(file).toString('utf8'), document = new URL(route, site);
    check((html.match(/<h1\b/gi) || []).length === 1, `${file}: expected one descriptive H1.`);
    check(/<html\b[^>]*lang="en"/i.test(html), `${file}: expected English language.`);
    // Canonical/page links identify documents; styles, scripts and images identify resources.
    for (const match of html.matchAll(/<(script|link|img|a)\b[^>]*>/gi)) {
      const tag = match[1].toLowerCase(), a = attributes(match[0]);
      const href = a.src || a.href;
      if (!href || (tag === 'link' && a.rel === 'canonical')) continue;
      const local = localFile(href, document);
      if (!local) continue;
      const content = read(local.file);
      if (tag === 'a' && local.target.hash) {
        const ids = [...content.toString('utf8').matchAll(/\bid=["']([^"']+)["']/g)].map(m=>m[1]);
        check(ids.includes(decodeURIComponent(local.target.hash.slice(1))), `${file}: missing link fragment ${href}.`);
      }
      if (tag === 'img') check(a.alt !== undefined && Number(a.width) > 0 && Number(a.height) > 0, `${file}: images need alt text and dimensions.`);
    }
    const meta = [...html.matchAll(/<meta\b[^>]*>/gi)].map(m=>attributes(m[0]));
    if (file === '404.html') {
      check(meta.some(m=>m.name==='robots' && /\bnoindex\b/.test(m.content)), '404.html must be noindex.');
      check(!/blazor\.webassembly|startup\.js/.test(html), '404.html must be a dedicated static document.');
      continue;
    }
    check(!meta.some(m=>m.name==='robots' && /noindex/.test(m.content)), `${file}: public documents must be indexable.`);
    const titleMatches = [...html.matchAll(/<title>([^<]+)<\/title>/gi)];
    check(titleMatches.length === 1, `${file}: expected one title.`);
    const title = decode(titleMatches[0][1]);
    check(!titles.has(title), `${file}: duplicate title.`); titles.add(title);
    if (route === '/') check(title === 'LaserBatcher | SVG Batching for Laser Cutting', 'Root title must match the workspace title.');
    const getMeta = key => {
      const items = meta.filter(m=>m.name===key || m.property===key);
      check(items.length===1 && !!items[0].content, `${file}: expected one ${key} meta tag.`);
      return items[0].content;
    };
    const description = getMeta('description');
    check(!descriptions.has(description), `${file}: duplicate description.`); descriptions.add(description);
    const canonicals = [...html.matchAll(/<link\b[^>]*>/gi)].map(m=>attributes(m[0])).filter(a=>a.rel==='canonical');
    check(canonicals.length===1 && canonicals[0].href===document.href, `${file}: expected one absolute canonical ${document.href}.`);
    for (const key of ['og:title','twitter:title']) check(getMeta(key)===title, `${file}: ${key} must match title.`);
    for (const key of ['og:description','twitter:description']) check(getMeta(key)===description, `${file}: ${key} must match description.`);
    for (const key of ['og:url','twitter:url']) check(getMeta(key)===document.href, `${file}: ${key} must match canonical.`);
    for (const key of ['og:image','twitter:image']) check(getMeta(key)===new URL('brand/laserbatcher-sharing.png',site).href, `${file}: sharing image must be absolute.`);
    getMeta('og:image:alt'); getMeta('twitter:image:alt');
    check(getMeta('twitter:card')==='summary_large_image', `${file}: expected summary_large_image.`);
    check(getMeta('og:image:width')==='1200' && getMeta('og:image:height')==='630', `${file}: incorrect sharing dimensions.`);
    const graphs = [...html.matchAll(/<script\b[^>]*type=["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi)].map(m=>JSON.parse(m[1]));
    const nodes = graphs.flatMap(g => g['@graph'] || [g]);
    check(graphs.length > 0 && graphs.every(g=>g['@context']==='https://schema.org'), `${file}: missing JSON-LD context.`);
    check(!/"(?:aggregateRating|review)"\s*:/.test(JSON.stringify(graphs)), `${file}: do not invent reviews.`);
    if (route === '/') {
      check(nodes.some(n=>n['@type']==='WebSite' && n.url===site.href) && nodes.some(n=>n['@type']==='Organization' && n.name==='Off the Wall Creations'), 'Root requires WebSite and creator Organization.');
    } else {
      const breadcrumb = nodes.filter(n=>n['@type']==='BreadcrumbList');
      const expected = route==='/about/' ? [site.href,document.href] : [site.href,new URL('/about/',site).href,document.href];
      check(breadcrumb.length===1 && JSON.stringify(breadcrumb[0].itemListElement.map(n=>n.item))===JSON.stringify(expected), `${file}: breadcrumbs must match the page hierarchy.`);
      check(breadcrumb[0].itemListElement.every((n,i)=>n.position===i+1 && n.name), `${file}: invalid breadcrumb positions or names.`);
      if (route === '/about/') check(nodes.some(n=>n['@type']==='WebApplication' && n.url===site.href), 'Overview requires WebApplication.');
      check(!/blazor\.webassembly|startup\.js|serviceWorker\.register/.test(html), `${file}: public pages must not start Blazor or a worker.`);
    }
    check((html.match(/gtag\(['"]config['"],\s*['"]G-4X5DN7KDYD['"]\)/g)||[]).length===1, `${file}: expected one existing Analytics configuration.`);
    check((html.match(/src="https:\/\/www\.googletagmanager\.com\/gtag\/js\?id=G-4X5DN7KDYD"/g)||[]).length===1, `${file}: expected one Analytics loader.`);
  }
  const png = read('brand/laserbatcher-sharing.png');
  check(png.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10])) && png.readUInt32BE(16)===1200 && png.readUInt32BE(20)===630, 'Sharing image must be a 1200×630 PNG.');
  read('brand/laserbatcher-sharing.svg');
  const sitemap = read('sitemap.xml').toString('utf8');
  const locations = [...sitemap.matchAll(/<loc>([^<]+)<\/loc>/g)].map(m=>m[1]);
  check(JSON.stringify(locations.slice().sort())===JSON.stringify(routes.map(r=>new URL(r,site).href).sort()), 'Sitemap must contain exactly the six canonical URLs.');
  check(!/<(?:lastmod|priority|changefreq)\b/.test(sitemap), 'Sitemap must not add unsupported freshness or priority claims.');
  const robots = read('robots.txt').toString('utf8');
  check(/^User-agent: \*\r?$/m.test(robots) && /^Allow: \/\r?$/m.test(robots) && !/^Disallow:\s*\S/m.test(robots) && robots.includes(`Sitemap: ${site.href}sitemap.xml`), 'robots.txt must allow crawling and identify the sitemap.');
  return { requiredAssets, routes };
}
module.exports = { verifyPublicPages, routes, fileFor };
