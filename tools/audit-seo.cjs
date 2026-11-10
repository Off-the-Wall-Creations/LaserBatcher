// Optional local audit tool; Lighthouse is not an application dependency.
// LIGHTHOUSE_CLI points to lighthouse/cli/index.js; CHROME_PATH selects Chromium.
const { spawnSync } = require('node:child_process');
const fs = require('node:fs'), path = require('node:path');
const base = process.env.LASERBATCHER_URL || 'http://127.0.0.1:5193/';
const output = path.resolve(process.env.SEO_AUDIT_OUTPUT || 'LaserBatcher.Tests/obj/seo-audits');
const routes = process.argv.includes('--baseline') ? ['/'] : ['/', '/about/', '/guides/svg-batching/', '/guides/svg-preparation/', '/guides/metadata-and-runtime/', '/guides/operation-mapping/'];
fs.mkdirSync(output, { recursive: true });
for (const route of routes) for (const device of ['desktop', 'mobile']) {
  const file = path.join(output, (route === '/' ? 'workspace' : route.split('/').filter(Boolean).at(-1)) + '-' + device + '.json');
  const args = [process.env.LIGHTHOUSE_CLI, new URL(route, base).href,
    '--chrome-flags=--headless --disable-features=IsolateSandboxedIframes',
    '--only-categories=performance,accessibility,best-practices,seo',
    ...['*googletagmanager.com/*','*google-analytics.com/*','*buymeacoffee.com/*','*fonts.googleapis.com/*','*fonts.gstatic.com/*'].map(pattern=>'--blocked-url-patterns='+pattern),
    '--output=json', '--output-path=' + file, '--quiet'];
  if (!args[0]) throw new Error('Set LIGHTHOUSE_CLI to the installed Lighthouse CLI.');
  if (device === 'desktop') args.push('--preset=desktop');
  const run = spawnSync(process.execPath, args, { stdio: 'inherit', env: process.env });
  if (run.status !== 0) process.exit(run.status || 1);
  const report = JSON.parse(fs.readFileSync(file, 'utf8'));
  console.log(route, device, JSON.stringify(Object.fromEntries(Object.entries(report.categories).map(([k,v])=>[k,Math.round(v.score*100)]))));
  if (report.runtimeError) throw new Error(report.runtimeError.message);
}
