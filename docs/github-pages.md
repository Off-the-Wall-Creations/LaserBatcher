# GitHub Pages hosting

LaserBatcher is served at **https://batcher.offthewallcreations.xyz/**. This is a custom domain configured for this repository, so the application base is `/`:

```html
<base href="/" />
```

`/LaserBatcher/` would be appropriate for a project site served at `https://off-the-wall-creations.github.io/LaserBatcher/`. It is incorrect for this repository's custom-domain root. With that incorrect base, the browser requests `/LaserBatcher/css/app.css`, `/LaserBatcher/_framework/blazor.webassembly.js`, and `/LaserBatcher/service-worker.js`, although the files are actually at `/css/app.css`, `/_framework/blazor.webassembly.js`, and `/service-worker.js`.

## Build and deploy

The workflow in `.github/workflows/main.yml` publishes `release/wwwroot` to the existing `gh-pages` branch. In repository Settings → Pages, keep the branch publishing source set to **gh-pages / (root)** and the custom domain set to **batcher.offthewallcreations.xyz**. The source file `LaserBatcher/wwwroot/CNAME` ensures each published artifact retains that domain.

Do not rewrite `release/wwwroot/index.html` after `dotnet publish`. Blazor's generated `service-worker-assets.js` contains its SHA-256 hash. A subsequent base-tag replacement changes the bytes without updating the hash and prevents the offline cache from installing. The deployed failure had both an incorrect base and a mismatched index hash.

If the hosting path changes in the future, update the **source** base tag and the expected domain/base in `tools/prepare-pages.cjs`, then publish afresh. Do not repair generated hashes manually.

After publishing, `node tools/prepare-pages.cjs release/wwwroot`:

- Verifies the root base, CNAME, all six canonical documents, metadata, internal links, public assets, required framework files, and every generated PWA asset hash.
- Verifies the dedicated static, noindex `404.html` that was included before publication. Unknown URLs retain HTTP 404; they do not receive a copy of the workspace.
- Adds `.nojekyll` so GitHub Pages serves underscore-prefixed directories such as `_framework`.
- Adds `.gitattributes` with `* -text` to the deployment output so Git preserves the exact hashed bytes.

Any validation failure stops the workflow before the deploy step. The workflow supports both pushes to `main` and manual **Run workflow**. Local edits must reach `main` before its workflow can deploy them; generating local publish artifacts does not change the hosted site.

## Verification

```powershell
node --test LaserBatcher.Tests/pages-deployment.test.cjs LaserBatcher.Tests/service-worker.test.cjs
dotnet test LaserBatcher.Tests/LaserBatcher.Tests.csproj
dotnet publish LaserBatcher/LaserBatcher.csproj -c Release -o LaserBatcher.Tests/obj/pages
node tools/prepare-pages.cjs LaserBatcher.Tests/obj/pages/wwwroot
node LaserBatcher.Tests/serve-published.cjs LaserBatcher.Tests/obj/pages/wwwroot 5193
```

With Playwright available, run `node LaserBatcher.Tests/browser-pages.cjs` with `LASERBATCHER_URL=http://127.0.0.1:5193`. It checks root resource URLs, the service-worker scope, offline reload, and graceful registration failure. The broader identity/PWA regression is documented in the [brand guide](brand-guide.md).

The static public documents are `/about/`, `/guides/svg-batching/`, `/guides/svg-preparation/`, `/guides/metadata-and-runtime/`, and `/guides/operation-mapping/`. The local host reproduces directory indexes, trailing-slash redirects and the custom 404 response. `browser-seo.cjs` checks raw/no-JavaScript content, themes, keyboard and mobile layouts, new-tab workspace links, fresh guide visits without Blazor or a worker, and installed-worker online/offline navigation. See [public content and SEO](seo.md) for content maintenance, audits and the separate Search Console rollout.

The published worker uses real HTML entries from its generated asset manifest. Root `/` and its explicit `/index.html` Blazor route load the workspace from the same integrity-checked release as the framework assets. Public paths and their index aliases request current HTML with HTTP revalidation online and fall back to their own cached document on network failure. Real HTTP errors remain visible instead of being replaced with an older successful page. Unknown navigations use the server's response online and the cached noindex 404 document with HTTP 404 offline. Guide pages never register a worker themselves, but a worker installed through the workspace caches their documents and required assets. The usual close-all-tabs update lifecycle still applies to previously installed versions; this pass does not force a reload of an active job.

`/sitemap.xml` and `/robots.txt`, including query variants and non-navigation requests, bypass Cache Storage and browser HTTP caching with a network-only request. They are never substituted with app HTML or an offline 404 document. Offline requests return a plain-text, non-cacheable HTTP 503 rather than pretend that a crawler file was read successfully. These browser-side rules do not change GitHub Pages' CDN headers or Google's own fetches.

Run `node LaserBatcher.Tests/browser-startup.cjs` against the same local publish to check non-JavaScript readers, a blocked startup script, a missing boot manifest, disabled WebAssembly, runtime error controls, and waiting-update notifications. The script simulates update lifecycle events without reloading an unsaved job. `browser-pages.cjs` separately verifies the real published worker and offline startup.

## Loading HTML versus a failed application

The initial HTTP response is a static shell; Blazor loads and renders the editor in the browser. A text-only scraper or AI review tool that does not run JavaScript/WebAssembly cannot inspect the interactive application. Earlier HTML included the literal text “An unhandled error has occurred” in a CSS-hidden element. Extracting that text together with “Preparing your workspace” did **not** establish that the browser had encountered an error or received an old build.

The static shell now describes the application and includes a JavaScript-disabled explanation. `startup.js` explicitly starts Blazor, catches startup rejection, and adds error text and recovery controls only after an actual failure. It also handles Blazor's runtime-error display signal. A normal startup leaves the error container empty and hidden. An automated interactive review still needs a browser capable of running JavaScript and WebAssembly, and must wait for the app header/canvas before inspecting the editor.

To diagnose a reported failure, compare a fresh browser profile with the affected profile. Check the actual visibility of `#blazor-error-ui`, the console, and network responses for the framework, boot manifest and CSS. Do not diagnose a routing failure from static HTML text alone. GitHub Pages' HTTP cache and an installed PWA's Cache Storage are different caches; a fresh profile has no previously installed application worker.

When a newer PWA worker is waiting, the app now displays a notice to download work, close **all** LaserBatcher tabs and installed windows, then reopen. It does not force activation, reload active jobs, or delete browser storage. This notice becomes available after a browser first receives the updated startup code; a browser still running an older cached shell may need the one-time recovery below.

Long-running workspace windows also check for updates on return to the foreground (at most once every five minutes) and after reconnecting. Failures are caught, and updates still wait for all controlled windows to close. The public guides do not register or update the application worker.

After deployment, inspect the network response for `index.html` to confirm its base is `/`. `/manifest.json`, `/_framework/blazor.webassembly.js`, and `/service-worker.js` should return 200 with their actual content, not HTML from the 404 fallback.

If an already installed PWA still shows an older version, close all tabs and installed app windows for this site, then reopen it. If an old service worker still serves stale HTML, use browser DevTools → Application → Service Workers to unregister that site's worker, remove its entries under Cache Storage, and reload while online. This is a one-time recovery for stale cached builds; do not routinely delete unrelated browser storage.

## Sitemap showing the app, or Search Console reporting Couldn't fetch

The browser screenshot showing the LaserBatcher header and “Sorry, there's nothing at this address” at `/sitemap.xml` is the historical catch-all worker behavior: it returns cached `index.html`, then Blazor cannot route the XML path. A hard reload can bypass that worker. New server code cannot replace the old worker while controlled tabs keep it waiting. Download current work, close **every** site tab (including guides or sitemap views) and installed app window, then reopen. Use the site-specific recovery above if needed.

This is distinct from Google's sitemap fetch. Read-only checks on November 10, 2026 found HTTP 200 `application/xml` with the six canonical URLs, HTTP 200 `robots.txt` allowing crawling, and the newer routing worker at the origin. A Googlebot User-Agent probe also returned XML; that probe is not proof of a request from Google's infrastructure. The user separately confirmed Search Console's live test reported **Crawl allowed: Yes** and **Page fetch: Successful**.

GitHub Pages' observed response header was `Cache-Control: max-age=600`. Browser HTTP caching, GitHub's CDN and service-worker Cache Storage are separate layers. HTML meta tags cannot override the worker or the hosting response headers. Do not change sitemap URLs or add cache-busting parameters to the submitted canonical to work around a local browser worker.

For **Couldn't fetch / Sitemap could not be read**, copy the exact submitted `https://batcher.offthewallcreations.xyz/sitemap.xml` into Search Console's URL Inspection → Test live URL → Page availability. If Crawl allowed is Yes and Page fetch is Successful, submit that same sitemap URL again and check the Sitemaps report's subsequent fetch result. A successful live test establishes current fetch access, not indexing or a completed sitemap-processing run. If it fails, use the exact fetch error to investigate hosting/DNS/robots access. See [Google's sitemap fetch troubleshooting](https://support.google.com/webmasters/answer/7451001?hl=en).

`browser-cache-upgrade.cjs` reproduces the old catch-all behavior, installs the actual published replacement as a waiting worker, checks that a rendered job is not reloaded, closes the last tab, and verifies activation without clearing the profile. It then checks XML/text requests, fresh online guides, offline guide/workspace loading and non-HTML offline crawler errors:

```powershell
$env:LASERBATCHER_PUBLISHED = 'LaserBatcher.Tests/obj/pages/wwwroot'
node LaserBatcher.Tests/browser-cache-upgrade.cjs
```

The eight cache-routing tests also run in CI with the twelve deployment/HTTP checks. `browser-startup.cjs` covers throttled return/reconnect checks and failed-update handling. No automated action submits a sitemap, changes DNS, purges the live CDN or forces an update over active work.

The `runtime.lastError` / “message channel closed” errors in the reported console are consistent with browser-extension messaging. LaserBatcher does not use Chrome extension messaging APIs. If they remain after the resource errors are fixed, compare with a browser profile that has extensions disabled; they do not explain the incorrect asset URLs.

References: [Blazor app base paths](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/app-base-path?view=aspnetcore-7.0), [Blazor PWA content integrity](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app?view=aspnetcore-7.0), [GitHub Pages custom domains](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site), [Chrome extension messaging](https://developer.chrome.com/docs/extensions/develop/concepts/messaging).
