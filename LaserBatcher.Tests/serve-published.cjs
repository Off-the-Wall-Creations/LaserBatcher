// Local-only static host for a Release PWA check; no deployment or dependencies.
// node LaserBatcher.Tests/serve-published.cjs LaserBatcher.Tests/obj/pwa/wwwroot [5193]
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg', '.woff': 'font/woff', '.xml': 'application/xml', '.txt': 'text/plain' };
function createServer(directory) {
  const root = fs.realpathSync(path.resolve(directory));
  return http.createServer((request, response) => {
    let url, relative;
    try { url = new URL(request.url, 'http://localhost'); relative = decodeURIComponent(url.pathname).replace(/^\/+/, ''); }
    catch { response.writeHead(400).end(); return; }
    let file = path.resolve(root, relative);
    const contained = target => target === root || target.startsWith(root + path.sep);
    if (!contained(file) || relative.includes('\\') || relative.includes('\0')) { response.writeHead(403).end(); return; }
    // Real-path checks also prevent a symlink escaping this local-only host.
    if (fs.existsSync(file) && !contained(fs.realpathSync(file))) { response.writeHead(403).end(); return; }
    if (fs.existsSync(file) && fs.statSync(file).isDirectory()) {
      if (!url.pathname.endsWith('/')) { response.writeHead(301, { Location: url.pathname + '/' + url.search }).end(); return; }
      file = path.join(file, 'index.html');
    }
    if (fs.existsSync(file) && !contained(fs.realpathSync(file))) { response.writeHead(403).end(); return; }
    fs.readFile(file, (error, data) => {
      if (error) {
        const missing = path.join(root, '404.html');
        response.writeHead(404, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-cache' });
        response.end(request.method === 'HEAD' ? undefined : fs.existsSync(missing) ? fs.readFileSync(missing) : 'Page not found');
        return;
      }
      response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
      response.end(request.method === 'HEAD' ? undefined : data);
    });
  });
}
module.exports = { createServer };
if (require.main === module) createServer(process.argv[2]).listen(Number(process.argv[3] || 5193), '127.0.0.1', () => console.log(`Serving ${path.resolve(process.argv[2])} locally`));
