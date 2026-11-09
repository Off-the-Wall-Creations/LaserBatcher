// Run with Node and Playwright. Optional PLAYWRIGHT_MODULE / PLAYWRIGHT_CHROMIUM_EXECUTABLE.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');

(async () => {
  const root = path.resolve(__dirname, '../LaserBatcher/wwwroot');
  const source = fs.readFileSync(path.join(root, 'brand/laserbatcher.svg'), 'utf8');
  // Keep the complete mark within the maskable icon's central 80% safe circle.
  const mark = source.replace(/<svg[^>]*>|<\/svg>/g, '').replace(/<rect width="64" height="64" rx="14" fill="#1C1917"\s*\/>/, '');
  const maskable = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" fill="#1C1917"/><g transform="translate(8 8) scale(.75)">${mark}</g></svg>`;
  const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE, headless: true });
  try {
    const page = await browser.newPage();
    for (const [name, size, svg] of [
      ['favicon.png', 32, source], ['apple-touch-icon.png', 180, source],
      ['icon-192.png', 192, source], ['icon-512.png', 512, source],
      ['icon-maskable-192.png', 192, maskable], ['icon-maskable-512.png', 512, maskable]
    ]) {
      const png = await page.evaluate(async ({ svg, size }) => {
        const image = new Image(); image.src = 'data:image/svg+xml;base64,' + btoa(svg); await image.decode();
        const canvas = document.createElement('canvas'); canvas.width = canvas.height = size;
        canvas.getContext('2d').drawImage(image, 0, 0, size, size);
        return canvas.toDataURL('image/png').split(',')[1];
      }, { svg, size });
      const target = path.join(root, name);
      const bytes = Buffer.from(png, 'base64');
      if (!fs.existsSync(target) || !fs.readFileSync(target).equals(bytes)) fs.writeFileSync(target, bytes);
      console.log(`Exported ${name} (${size} × ${size})`);
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
