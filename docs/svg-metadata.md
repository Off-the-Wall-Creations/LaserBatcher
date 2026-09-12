# Program filenames and semantic SVG exports

LaserBatcher processes the uploaded SVG locally and downloads a separate batch. It does not connect to an Adapter SDK. External adapters should parse the filename below or read the embedded metadata and map those values to their own SDK data items.

## Filenames

Grammar: `[PartName_][Order OrderNumber_][Batch Batch_]xYield[.svg]`.

The underscore before `xYield` is mandatory even when every optional field is absent. Examples:

| Filename | Part | Order | Batch | Yield |
| --- | --- | --- | --- | --- |
| `_x12.svg` | absent | absent | absent | 12 |
| `Widget_x4.svg` | Widget | absent | absent | 4 |
| `Order AB-12_Batch Lot.3_x12.svg` | absent | AB-12 | Lot.3 | 12 |
| `Widget_Order AB-12_Batch Lot.3_x12.svg` | Widget | AB-12 | Lot.3 | 12 |

- Order, Batch, x, and the optional terminal .svg are case-insensitive. Values preserve case.
- Order and Batch begin at the start of the stem or immediately after an underscore. Each marker requires whitespace and a nonempty value. These marker-shaped field starts are reserved.
- Surrounding underscores and whitespace are trimmed from each field. Internal punctuation (including underscores), internal whitespace, and casing are preserved. Hyphens are not trimmed.
- Fields must occur in Part → Order → Batch → Yield order. Repeated, empty, out-of-order, or ambiguous marker fields are rejected; they are not interpreted as part names.
- Yield contains ASCII digits only, in the range 1–2147483647. Leading zeros are accepted on input and removed on output.
- Paths, signed/fractional/zero/overflow yields, extra suffixes, repeated extensions, and unsupported terminal extensions are rejected. A dot inside a field value is allowed.
- `x12.svg`, `Widget_batch_x12.svg`, `Widget_Batch B_Order A_x12.svg`, and `Widget_x1_x2.svg` are invalid.

A source named `Widget.svg` suggests part Widget and source yield 1. Metadata-looking invalid source names show an error requiring correction in the form. Changes to metadata must produce an unambiguous filename before export.

**Total yield = source yield × copy count**, with checked 32-bit overflow. Three copies of `Widget_x4.svg` export as `Widget_x12.svg`. Part, order, and batch remain editable. Empty fields are omitted; no synthetic Batch value is added.

Service interfaces are `ProgramNameParser.Parse`, `Format`, `FromSource`, and `Total`, using `ProgramMetadata(PartName, OrderNumber, Batch, Yield)`. Parsing/formatting failures throw `FormatException`; multiplication overflow throws `OverflowException`.

## Layers and preservation

The exporter uses Engrave → Score → InteriorCut → ExteriorCut. Ordering defaults to **Within each copy**; **Across the sheet** orders the same categories over all copies. Order is SVG document order, not a guarantee of a laser application's scheduling.

Explicit canonical labels are read from Inkscape labels, `data-label`, then IDs. Interior/Exterior Engrave and Score labels map to their operation while original roles remain in source markup. Aliases such as InsideCut and OuterCut are suggestions requiring confirmation. Plain Cut, unlabelled geometry, and conflicting ancestor labels need mapping. Group assignments apply to descendants, with the nearest explicit assignment taking precedence.

Geometry is partitioned without rewriting path data. Exports contain one SVG root; source viewports become ordinary groups with explicit translation and viewBox scaling, including nonzero origins and preserveAspectRatio alignment. Implicit viewport cropping is retained as local clip paths. Original viewport attributes are recorded as `data-laserbatcher-source-*` attributes. Ancestor transforms, units, compound paths, text, embedded raster images, definitions, and unknown metadata remain. Operation wrappers include `data-label`, `data-laserbatcher-operation`, and `data-laserbatcher-copy`; Inkscape sources also receive Inkscape layer labels/group modes.

The operation list shows isolated, fitted thumbnails for elements and groups, preserving source colors and inherited transforms. Child rows are indented beneath their ancestors. Thumbnails load as rows enter the list viewport and do not change export geometry or operation assignments.

When an explicit assignment overrides a canonical source label, the output uses the assigned operation label and retains the original in `data-laserbatcher-original-label`. This avoids conflicting operation labels on re-import while preserving their provenance and the untouched source.

Preview and download serialization use `SvgSerializer.Serialize`: SVG elements always have unprefixed names (`svg`, `g`, `path`, etc.) in the standard SVG default namespace. Existing namespace aliases, namespaced attributes, and foreign metadata remain available. This avoids reliance on importers recognizing tags such as `svg:path`; the SVG namespace itself is never stripped.

Hidden artwork is excluded from production mapping. CustomizationZones, MaterialSpecs, and DesignConstraints remain non-production and are forced hidden if originally exposed. Existing hidden markup stays in its ancestral context in the first operation partition for each copy. The source document itself is never changed.

Original IDs are retained on the first occurrence in the first copy. Additional copies and split ancestor containers receive deterministic, collision-free IDs. Local href/xlink:href, url(#id), and ARIA ID references are rewritten. Hidden elements lacking IDs receive new IDs for inventory lookup. Unknown application-specific reference encodings are preserved verbatim; consumers can use the source/output mapping to resolve them.

Conservative export checks block unresolved stylesheets, external references, active/animated/conditional content, cyclic or mixed-operation clones, duplicate/broken IDs, and unsafe container splitting. Group compositing, bounds-dependent transforms, object-bounding-box clipping, or references to split containers require source correction. Mapping alone cannot resolve these structural errors. Inline styles and local URL references are supported. CSS that cannot be resolved is reported explicitly.

Source width and height must use explicit positive inches. Output sheet dimensions use inches and a viewBox at 96 SVG units/inch, independent of screen pixel density. Source viewBox transforms are represented by group matrices. Nested viewport dimensions support SVG numeric lengths, px/in/mm/cm/pt/pc and percentages; simple percentage geometry lengths are resolved to the original viewport. CSS viewport positioning or unresolved percentage styles require source correction. Margins/spacing must be nonnegative and every copy must fit. Preview changes invalidate the existing batch; download rebuilds and validates current inputs.

## Embedded metadata v1

Locate the direct child `svg:metadata/lb:batch`, where `svg` is `http://www.w3.org/2000/svg` and `lb` is `urn:laserbatcher:metadata:1`. The batch element has `schemaVersion="1"`; its XML-decoded text is JSON. Existing metadata is retained separately. JSON property names use camelCase.

| JSON property | Meaning |
| --- | --- |
| `schemaVersion` | Integer 1 |
| `program` | Normalized partName, orderNumber, batch (nullable strings), yield (positive integer); matches the exported filename |
| `sourceFilename`, `sourceYield`, `copyCount`, `totalYield` | Provenance and checked finished-part count |
| `orderingMode` | WithinEachCopy or AcrossSheet |
| `sheet` | width, height, horizontalMargin, verticalMargin, spacing, units (`in`), svgUnitsPerInch (96) |
| `operationMappings` | Source structural key, resolved operation, and whether directly assigned |
| `hiddenLayers` | One record per hidden/semantic element per copy |
| `referenceMappings` | copy (one-based), sourceId, outputId; split containers can have multiple output IDs |

Each hidden record contains `copy`, `sourceKey`, `sourceId`, `outputIds`, `label`, `parent` (source key), `element`, `hidden`, `visibility`, `semanticCategory`, `declaredDisplay`, and `declaredVisibility`. Structural keys are slash-separated zero-based element-sibling indices, beginning with the source root (`0`). They identify elements without source IDs.

Visibility distinguishes visible content, hiding by self/ancestor, semantic-container exclusion, and unresolved stylesheets in analysis. Unresolved stylesheets block export rather than publishing a falsely computed visibility result. Material thickness, kerf, constraints, licensing, and custom semantic content are retained from the source; absent manufacturing facts are not invented.

## Verification

Run `dotnet test LaserBatcher.Tests/LaserBatcher.Tests.csproj` and `dotnet build LaserBatcher/LaserBatcher.csproj`. CI runs the service regression suite before publication. Tests compile the same browser-independent service files as the app.

Optional browser regression requires Node, Playwright, and Chromium. Start the app with `dotnet run --project LaserBatcher --urls http://localhost:5188`, then run `node LaserBatcher.Tests/browser-smoke.cjs`. Set `NODE_PATH` if Playwright is installed outside the repository; `PLAYWRIGHT_CHROMIUM_EXECUTABLE` can select an installed Chromium binary and `LASERBATCHER_URL` can select the local app URL.

The browser check exercises upload, mapping, metadata correction, yield multiplication, both modes, preview invalidation, XML error display, and downloading. It also compares source and batch pixels at a device scale factor of 2. Unrelated remote requests and service workers are disabled for a deterministic application test; offline caching is not tested. Screenshots and a sample export are written under the ignored `LaserBatcher.Tests/obj/browser-qa` directory.
