# Canvas workspace

LaserBatcher is a batching and inspection tool. Render batch creates an SVG preview; it never starts a laser.

## Layout and controls

The workspace fills the viewport (`100dvh`). The application document does not scroll. The mapping list, review dialogs and compact toolbar scroll internally as needed. A single mounted Buy me a coffee component remains in the approximately 80px bottom support/status strip (with an extra status row on small screens).

Desktop toolbars expose file/workflow, sheet dimensions and margins in inches, spacing, copies/Fill, program fields, operation ordering and Render/Clear/Download. Download stays disabled until the current batch has a valid render. Clear preview returns to Source and keeps settings. Metadata-only mode hides batching controls and downloads an independently updated SVG without requiring a preview, operation mapping or sheet fit.

Below 1200px wide or 640px high, Sheet and Program settings move into draft dialogs and Mapping opens a modal drawer. Apply commits valid draft values; Cancel and Escape leave the current job unchanged. Review dialogs use native browser focus containment and return focus when closed. Issues includes filename-conflict choices and complete import/export warnings. Blocking messages and runtime-cleared notices remain in the bottom status area.

The desktop mapping panel defaults to 360px; drag its divider or focus the divider and use Left/Right (16px), Home (280px) or End (480px). Close hides the panel; Mapping restores it. Group disclosure is independent of selection and assignment. Collapsed child choices survive parent collapse and workflow switches; a new upload expands all groups.

## Canvas and inspection

Source appears after upload; Render switches to Batch. Source/Batch buttons change views without changing the job. Fit runs after upload/render. Drag or focus the canvas and use arrows to pan; Shift increases the keyboard distance. Home fits, + / − zoom, and Alt + wheel zooms. Normal wheel scrolling pans (Shift pans horizontally). Ctrl/Command browser zoom shortcuts remain available. Zoom spans 1%–1600%; 100% is 96 CSS pixels per inch, not a calibrated physical size. Material background, zoom and pan are view state.

Click a mapping thumbnail to select the enlarged preview inside that panel. The main canvas does not change. Enhanced inspection uses contrasting colors and non-scaling strokes. Original shows production styling. Enhanced styles are isolated from source, main-canvas SVGs, metadata and downloads. Raster image attributes/pixels remain unchanged. Preview fitting includes transformed stroke padding and handles horizontal/vertical paths. Clipping and inherited transforms are retained.

Use consistent nonzero strokes, suitable contrast, intentional fills and opacity, and explicit operation labels when preparing SVGs. Enhanced inspection avoids changing production styles solely to make small thumbnails readable. Text rendering still depends on fonts available to the consuming laser software.

## Ignore and runtime

The closest explicit assignment applies. A group Ignore may be overridden by a child's executable operation. “From source / parent” removes an override. Ignore omits production occurrences, while licensing, hidden content and semantic metadata remain preserved. An included clone keeps its required original target in non-rendering definitions; assignments do not propagate through clone references. Unsupported dependency transformations require cleanup in the source and produce an export error.

Included, ignored and unresolved counts cover all production elements, including collapsed descendants. Ignore never counts as unresolved. At least one production element must remain included.

Runtime is an optional whole-job estimate entered by the user. It is independent of laser type and is not multiplied by copies or yield. Sheet/copy/mapping/order changes clear it with a notice; program-field edits and view controls retain it. Re-upload a generated SVG to edit metadata independently. See [the v2 metadata contract](svg-metadata.md).

## Verification

Run the service suite and build, then the browser scripts with an installed Playwright/Chromium (paths can be selected through `PLAYWRIGHT_MODULE` and `PLAYWRIGHT_CHROMIUM_EXECUTABLE`). Set `LASERBATCHER_URL` to a local development or prepared published build:

```powershell
dotnet test LaserBatcher.Tests/LaserBatcher.Tests.csproj
dotnet build LaserBatcher/LaserBatcher.csproj
node LaserBatcher.Tests/browser-smoke.cjs
node LaserBatcher.Tests/browser-identity.cjs
node LaserBatcher.Tests/browser-workspace.cjs
node --test LaserBatcher.Tests/pages-deployment.test.cjs
# Against prepared, locally served Release output:
node LaserBatcher.Tests/browser-pages.cjs
```

The workspace regression compares clone pixels with an independently prepared expected SVG, checks that view controls leave exports unchanged, exercises Ignore and metadata updates, and checks 1366×768, 1280×720, compact windows and mobile sizes. The identity regression also covers theme contrast, storage failure, coffee-widget readiness/origin isolation/fallback and icon safe areas. The Pages regression checks root asset URLs, worker scope, offline modules and offline upload/rendering. None of these checks deploys the app or verifies actual Glowforge application scheduling.
