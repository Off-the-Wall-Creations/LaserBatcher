# LaserBatcher brand guide

LaserBatcher is a warm maker studio: calm work surfaces, legible controls, and amber accents. Always write **LaserBatcher** as one word with this capitalization. Display the credit **by Off the Wall Creations** beside the wordmark; it is attribution, not part of the installed app name.

## Palette and appearance

| Token | Dark | Light |
| --- | --- | --- |
| Canvas | `#1C1917` | `#F7F3EC` |
| Panels | `#292524` | `#FFFCF7` |
| Main text | `#FAF7F2` | `#292524` |
| Secondary text | `#BDB4A8` | `#655E55` |
| Primary action | `#F5A524` | `#9A4C0A` |
| Primary-action text | `#1C1917` | `#FFFFFF` |

Use the `--lb-*` CSS tokens in `wwwroot/css/app.css`. System appearance is the default; Light and Dark follow the user's explicit choice. Apply appearance before the initial paint. Storage is optional. Do not recolor imported artwork or the selected material preview background. Status messages use separate, theme-aware colors and readable text, not color alone.

## Type and spacing

Use the platform system sans-serif stack, a 16px base, 1.5 line height, and restrained 600-weight headings. Quantities and durations use tabular numerals. Base spacing is 4px, commonly 8, 12, 16, 24, and 32px. Panels have 12px corners; controls have 8px corners. Keep a visible 3px keyboard-focus outline. Allow browser zoom and responsive wrapping.

## Icon

The editable master is `wwwroot/brand/laserbatcher.svg`: three offset sheets with a small amber focal point. It represents preparation and batching without tying the app to a machine. Use `laserbatcher-mono.svg` for single-color printing; its `currentColor` strokes work in either ink or reversed white. Keep the geometry and proportions intact. Prefer at least 24px; favicon exports are 32px. Leave clear space around the mark equal to at least the focal point's diameter. Beside a visible wordmark, use an empty image alt to avoid repetition.

PNG exports cover the favicon (32px), Apple touch icon (180px), regular PWA icons (192px/512px), and maskable PWA icons (192px/512px). The maskable version has a solid charcoal full-bleed background and inset artwork inside the central 80% safe circle. Do not use the regular icon as a maskable icon.

Regenerate assets from the SVG with `node tools/export-brand-icons.cjs` using Playwright. An existing installation can be selected with `PLAYWRIGHT_MODULE` and `PLAYWRIGHT_CHROMIUM_EXECUTABLE`; this does not add an application dependency.

## Support area

Place one support area beneath the workspace. Keep the supplied Buy me a coffee yellow, Cookie font, and creator slug `tbm0115` intact, isolated in its sandboxed iframe. The normal support link remains available when the vendor script is blocked or offline. Never place this third-party script in the live Blazor document.

## Checks

Run the existing service suite and `LaserBatcher.Tests/browser-smoke.cjs`, then `LaserBatcher.Tests/browser-identity.cjs` against a running app. The identity checks exercise themes, storage failure, disclosure behavior, support fallback, and asset geometry. See that script's comments for the optional vendor-script and published-PWA checks. No payment submission is part of verification.

```powershell
dotnet test LaserBatcher.Tests/LaserBatcher.Tests.csproj
dotnet build LaserBatcher/LaserBatcher.csproj
dotnet run --no-build --project LaserBatcher --urls http://127.0.0.1:5188
# In a second terminal (with Playwright available):
$env:LASERBATCHER_URL = 'http://127.0.0.1:5188'
node LaserBatcher.Tests/browser-smoke.cjs
node LaserBatcher.Tests/browser-identity.cjs
```

Set `BMC_SCRIPT_FILE` to an unmodified local copy of the supplied vendor script for a repeatable actual-widget test, or set `BMC_LIVE=1` to check the CDN and Cookie font directly. Without either, the test uses a small `document.writeln` fixture. Both modes also verify a blocked-CDN fallback and reject readiness messages from another window. The browser test disables Chromium's separate sandboxed-iframe process to intercept its first network request reliably; the iframe's opaque-origin security restrictions remain enabled and are verified.

For the offline check, build local artifacts with `dotnet publish LaserBatcher/LaserBatcher.csproj -c Release -o LaserBatcher.Tests/obj/pwa`, then serve them with `node LaserBatcher.Tests/serve-published.cjs LaserBatcher.Tests/obj/pwa/wwwroot 5193`. Set `PWA_URL=http://127.0.0.1:5193` before running the identity test. This checks the release service worker, offline reload, cached brand assets, support fallback, and batch rendering. It does not deploy the application. Screenshots go to the ignored `LaserBatcher.Tests/obj/browser-qa` directory.
