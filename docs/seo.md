# Public content and SEO

The workspace remains at `https://batcher.offthewallcreations.xyz/`. Five static HTML documents under `LaserBatcher/wwwroot` provide complete content without JavaScript or Blazor:

| Canonical path | Content |
| --- | --- |
| `/about/` | Capabilities, local processing, boundaries, creator and guide directory |
| `/guides/svg-batching/` | Sheet setup, repeated copies, rendering and download |
| `/guides/svg-preparation/` | Physical sizing, styling, labels, embedded resources and format limits |
| `/guides/metadata-and-runtime/` | Program fields, yield, manual whole-job runtime and metadata-only updates |
| `/guides/operation-mapping/` | Inheritance, Ignore, child overrides, clone dependencies and ordering |

Edit these HTML documents directly. Shared `css/public.css` supplies the scrolling layout; `css/app.css` provides the existing identity tokens. `theme.js` reads the same appearance preference before paint, and `public-site.js` progressively enables the selector. With JavaScript unavailable, navigation and content remain available and the inactive selector stays hidden. Public pages load neither `startup.js` nor the framework, register no worker, and use a plain support link. Workspace links to guides explicitly open new tabs to preserve the current job.

The existing Google Analytics configuration (`G-4X5DN7KDYD`) appears once in each canonical document. No additional tracker, custom event or SVG-content collection was added. The dedicated noindex 404 is not an Analytics landing page. Do not describe local SVG processing as a promise that the whole site makes no network requests.

## Metadata and content maintenance

Keep page titles and descriptions unique, canonical links absolute, and public canonical paths slash-terminated. The root title is `LaserBatcher | SVG Batching for Laser Cutting` in both `index.html` and `Pages/Index.razor`. Queries and explicit index aliases use the same canonical as their clean URL. There is one H1 per public document.

Open Graph and Twitter/X large-summary metadata use the branded 1200×630 PNG. Its editable source is `wwwroot/brand/laserbatcher-sharing.svg`. The two guide screenshots use only `LaserBatcher.Tests/Fixtures/guide-demo.svg`, a synthetic geometric tag authored for documentation. They are JPEGs with declared 1366×768 dimensions, descriptive alt text and lazy loading below the introductory content. No customer artwork is included.

To refresh these assets against a local Release build, set `PLAYWRIGHT_MODULE`, `PLAYWRIGHT_CHROMIUM_EXECUTABLE`, and `LASERBATCHER_URL`, then run:

```powershell
node tools/export-seo-assets.cjs
```

That command writes source assets in `wwwroot`; publish again afterward. Never change generated output bytes or integrity hashes. The public pages, 404 and required assets are included in the PWA manifest during `dotnet publish`; preparation only validates them and writes the un-hashed Pages support files.

Root JSON-LD describes the WebSite and creator Organization. The overview describes the WebApplication, and each public page has BreadcrumbList data matching its visible hierarchy. There are no invented ratings or reviews. This factual markup is not a claim of software-app rich-result eligibility: [Google's software-app guidance](https://developers.google.com/search/docs/appearance/structured-data/software-app) requires additional review information. Static documents follow [Google's JavaScript SEO guidance](https://developers.google.com/search/docs/crawling-indexing/javascript/javascript-seo-basics) by exposing content and links in the initial response.

`robots.txt` allows crawling, including CSS, JS and framework resources. `sitemap.xml` contains exactly the root and five public canonicals, with no index aliases, query URLs, 404, fabricated lastmod, priority or changefreq.

## Local verification

```powershell
node --test LaserBatcher.Tests/pages-deployment.test.cjs LaserBatcher.Tests/service-worker.test.cjs
dotnet test LaserBatcher.Tests/LaserBatcher.Tests.csproj
dotnet build LaserBatcher/LaserBatcher.csproj
dotnet publish LaserBatcher/LaserBatcher.csproj -c Release -o LaserBatcher.Tests/obj/pages
node tools/prepare-pages.cjs LaserBatcher.Tests/obj/pages/wwwroot
node LaserBatcher.Tests/serve-published.cjs LaserBatcher.Tests/obj/pages/wwwroot 5193
# With Playwright/Chromium paths configured, in another terminal:
node LaserBatcher.Tests/browser-seo.cjs
node LaserBatcher.Tests/browser-startup.cjs
node LaserBatcher.Tests/browser-workspace.cjs
node LaserBatcher.Tests/browser-smoke.cjs
node LaserBatcher.Tests/browser-identity.cjs
node LaserBatcher.Tests/browser-pages.cjs
$env:LASERBATCHER_PUBLISHED = 'LaserBatcher.Tests/obj/pages/wwwroot'
node LaserBatcher.Tests/browser-cache-upgrade.cjs
```

The preparation validator checks canonicals separately from resource links, titles/descriptions, JSON-LD, local links/fragments, sharing-image dimensions, Analytics configuration, sitemap coverage and all generated asset hashes. Browser checks cover complete no-JS content, fresh-guide request isolation, preferences/storage failure, keyboard navigation, mobile/200% layout, image dimensions without layout shift, active-job new-tab links, and real installed-worker online/offline documents and 404 status.

Public HTML now revalidates online and falls back to its published document offline; the workspace retains a consistent cached release. Crawler files use network-only requests, with a plain-text HTTP 503 while offline. An old catch-all worker must finish its normal close-all-tabs upgrade before those rules take effect. See [sitemap/cache recovery](github-pages.md#sitemap-showing-the-app-or-search-console-reporting-couldnt-fetch) for the distinction between browser caching and Search Console fetch status, and the real legacy-worker upgrade test.

For Lighthouse, install the audit tool in the ignored test output directory, not as an application dependency. This pass uses Lighthouse 13.6.0 with Node 24.22.2 and the local Playwright Chromium. Configure `LIGHTHOUSE_CLI` to its `lighthouse/cli/index.js`, `CHROME_PATH` to Chromium, and `LASERBATCHER_URL` to the local published server, then run `node tools/audit-seo.cjs`. `--baseline` audits only the unchanged workspace; `SEO_AUDIT_OUTPUT` selects the report directory. Full runs audit all six pages in desktop and simulated mobile configurations. Reports default to `LaserBatcher.Tests/obj/seo-audits`.

Audits block Analytics, the coffee vendor and external fonts to keep third-party traffic out of local comparisons. Scores are lab observations on a local, uncompressed server, not production field metrics. Workspace performance includes the existing Blazor startup cost. No framework/runtime upgrade or machine behavior change belongs in this pass.

Local results recorded November 10, 2026:

| Document / device | Performance | Accessibility | Best practices | SEO |
| --- | --- | --- | --- | --- |
| Workspace baseline, desktop / mobile | 51 / 40 | 100 / 100 | 100 / 100 | 100 / 100 |
| Workspace after changes, desktop / mobile | 51 / 41 | 100 / 100 | 100 / 100 | 100 / 100 |
| Each of the five public pages, desktop and mobile | 100 | 100 | 100 | 100 |

All ten public-page audits reported zero cumulative layout shift. Reserving space for the progressively enabled Appearance selector removed a small header shift found in the first pass. The workspace's existing startup performance remains a separate concern; the one-point mobile difference is normal lab variation, not an optimization claim. Full JSON reports are in the ignored `obj/seo-baseline` and `obj/seo-audits` folders.

The 139 service tests, 12 deployment/HTTP tests, application build, Release publish and 1,600 generated asset-hash checks passed. Browser runs passed for static SEO content, startup failure/update notices, workspace controls, SVG/metadata downloads, identity/support isolation and published-worker offline behavior. Build output retains the pre-existing .NET 7 support warning. Search Console and production measurements remain pending deployment.

## Release and Search Console handoff

Implementation prepares repository changes only. Deployment and account actions are separate; no sitemap has been submitted and no indexing request has been made by this change.

After the normal GitHub deployment:

1. Confirm the workflow's prepare/verify step passed and a fresh browser receives the new root HTML, five static guides, real 404s, robots and sitemap. Check the six canonical URLs with JavaScript disabled. Close all site tabs/installed windows before testing an existing PWA's update, then check offline guides. Do not force-reload an active job; download it first.
2. In the existing verified Search Console property for **batcher.offthewallcreations.xyz**, submit `https://batcher.offthewallcreations.xyz/sitemap.xml` through Sitemaps. Do not create a second property or replace the Analytics configuration.
3. Inspect each of the six canonical URLs listed above (including `/`). Use the live test to check fetch/indexing eligibility and rendered content. Record the user-declared canonical and, when available in indexed data, Google-selected canonical. Live tests alone do not establish which canonical Google has indexed. Request indexing through the account UI as appropriate after successful tests.
4. Record initial sitemap status, discovered/indexed page coverage and exclusions. Export available Performance → Search results metrics: queries and pages, clicks, impressions, CTR and average position. Record the exact date range, search type, filters and deployment date. Newly added pages may have no data; mark it as unavailable rather than zero performance.
5. At two and four weeks after deployment, repeat the same property/filter/date-window method and compare equivalent windows. Check indexing and canonical decisions first, then query/page impressions, clicks, CTR and position. Note incomplete reporting windows and any other site changes.

Suggested rollout record (fill in from the account; no initial figures are assumed):

| Checkpoint | Date / range / filters | Sitemap and coverage | Canonical issues | Clicks / impressions / CTR / position |
| --- | --- | --- | --- | --- |
| Deployment baseline | Pending deployment | Not recorded | Not inspected | Not recorded |
| Two weeks | Pending | Pending | Pending | Pending |
| Four weeks | Pending | Pending | Pending | Pending |

Indexing and ranking are measured outcomes, not guaranteed acceptance criteria. There is no automatic reminder, deployment or account mutation in this implementation.
