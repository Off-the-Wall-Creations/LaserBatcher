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

- Verifies the root base, CNAME, all local HTML resource paths, required framework files, and every generated PWA asset hash.
- Copies the unchanged index to `404.html` for the Pages fallback.
- Adds `.nojekyll` so GitHub Pages serves underscore-prefixed directories such as `_framework`.
- Adds `.gitattributes` with `* -text` to the deployment output so Git preserves the exact hashed bytes.

Any validation failure stops the workflow before the deploy step. The workflow supports both pushes to `main` and manual **Run workflow**. Local edits must reach `main` before its workflow can deploy them; generating local publish artifacts does not change the hosted site.

## Verification

```powershell
node --test LaserBatcher.Tests/pages-deployment.test.cjs
dotnet test LaserBatcher.Tests/LaserBatcher.Tests.csproj
dotnet publish LaserBatcher/LaserBatcher.csproj -c Release -o LaserBatcher.Tests/obj/pages
node tools/prepare-pages.cjs LaserBatcher.Tests/obj/pages/wwwroot
node LaserBatcher.Tests/serve-published.cjs LaserBatcher.Tests/obj/pages/wwwroot 5193
```

With Playwright available, run `node LaserBatcher.Tests/browser-pages.cjs` with `LASERBATCHER_URL=http://127.0.0.1:5193`. It checks root resource URLs, the service-worker scope, offline reload, and graceful registration failure. The broader identity/PWA regression is documented in the [brand guide](brand-guide.md).

After deployment, inspect the network response for `index.html` to confirm its base is `/`. `/manifest.json`, `/_framework/blazor.webassembly.js`, and `/service-worker.js` should return 200 with their actual content, not HTML from the 404 fallback.

If an already installed PWA still shows an older version, close all tabs and installed app windows for this site, then reopen it. If an old service worker still serves stale HTML, use browser DevTools → Application → Service Workers to unregister that site's worker, remove its entries under Cache Storage, and reload while online. This is a one-time recovery for stale cached builds; do not routinely delete unrelated browser storage.

The `runtime.lastError` / “message channel closed” errors in the reported console are consistent with browser-extension messaging. LaserBatcher does not use Chrome extension messaging APIs. If they remain after the resource errors are fixed, compare with a browser profile that has extensions disabled; they do not explain the incorrect asset URLs.

References: [Blazor app base paths](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/app-base-path?view=aspnetcore-7.0), [Blazor PWA content integrity](https://learn.microsoft.com/en-us/aspnet/core/blazor/progressive-web-app?view=aspnetcore-7.0), [GitHub Pages custom domains](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site), [Chrome extension messaging](https://developer.chrome.com/docs/extensions/develop/concepts/messaging).
