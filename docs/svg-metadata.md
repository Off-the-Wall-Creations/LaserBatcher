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

A source named `Widget.svg` without supported embedded metadata suggests part Widget and source yield 1. Metadata-looking invalid source names show an error requiring correction in the form. Supported embedded metadata restores program fields, including the effective total yield, after a file is renamed. If a valid structured filename disagrees, choose the embedded values, choose the filename values, or correct the fields before downloading. Changes to metadata must produce an unambiguous filename before export.

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

Conservative export checks block unresolved stylesheets, external references, active/animated/conditional content, cyclic or mixed-operation clones, duplicate/broken IDs, and unsafe container splitting. Group compositing, bounds-dependent transforms, object-bounding-box clipping, or references to split containers require source correction. Mapping alone cannot resolve these structural errors. Inline styles and local URL references are supported. Simple `.class` rules containing only `stroke` and `fill` with `none`, three-digit hex, or six-digit hex values are resolved in document order; inline styles retain priority over class rules, and class rules override presentation attributes. The original stylesheet XML is archived as escaped text in `lb:sourceStylesheet` metadata. Other selectors, declarations, priority rules, or stylesheet attributes remain unsupported rather than being partially applied.

Source width and height must use explicit positive `in`, `mm`, `cm`, `pt`, or `pc` dimensions. Conversion uses 25.4 mm/in, 2.54 cm/in, 72 pt/in, and 6 pc/in. Missing, unitless, pixel-only, percentage, and nonfinite root dimensions are rejected. Output sheet dimensions use inches and a viewBox at 96 SVG units/inch, independent of screen pixel density. Source viewBox transforms are represented by group matrices. Nested viewport dimensions support SVG numeric lengths, px/in/mm/cm/pt/pc and percentages; simple percentage geometry lengths are resolved to the original viewport. CSS viewport positioning or unresolved percentage styles require source correction. Margins/spacing must be nonnegative and every copy must fit. The source canvas is retained without cropping, including the extended canvas in Glowforge exports. Preview changes invalidate the existing batch; batch downloads rebuild and validate current inputs.

## Embedded metadata v2 and v1 migration

Locate the direct child `svg:metadata/lb:batch`, where `svg` is `http://www.w3.org/2000/svg` and `lb` is `urn:laserbatcher:metadata:2`. New exports and metadata updates have `schemaVersion="2"` in both XML and JSON; the element's XML-decoded text is JSON. Existing metadata is retained separately. JSON property names use camelCase.

Imports also accept `urn:laserbatcher:metadata:1` with matching version 1. V1 requires the original calculated yield; v2 adds explicit overrides while retaining original counts as provenance. Metadata-only updates migrate the authoritative record to v2 while preserving its unknown JSON properties and XML attributes. Readers must recognize the new namespace and override rule; this is deliberately versioned rather than changing the meaning of a v1 record. Nested historical records never initialize the form. Multiple authoritative records, mismatched versions, duplicate JSON properties, inconsistent counts, or invalid runtimes produce warnings and disable metadata-only editing. Batch creation can still use filename/manual values and preserve that unsupported source metadata.

| JSON property | Meaning |
| --- | --- |
| `schemaVersion` | Integer 2 (legacy imports accept 1) |
| `program` | Normalized partName, orderNumber, batch (nullable strings), yield (positive integer); matches the exported filename |
| `sourceFilename`, `sourceYield`, `copyCount`, `totalYield` | Provenance and finished-part count; sourceYield × copyCount is checked for overflow |
| `yieldOverride` | Optional positive integer equal to program.yield and totalYield; absent means totalYield = sourceYield × copyCount |
| `estimatedRuntimeSeconds` | Optional positive integer: user-entered seconds for running the entire SVG once; absent means unknown |
| `orderingMode` | WithinEachCopy or AcrossSheet |
| `sheet` | width, height, horizontalMargin, verticalMargin, spacing, units (`in`), svgUnitsPerInch (96) |
| `operationMappings` | Source structural key, resolved operation, and whether directly assigned |
| `hiddenLayers` | One record per hidden/semantic element per copy |
| `referenceMappings` | copy (one-based), sourceId, outputId; split containers can have multiple output IDs |
| `vendorProvenance.glowforge` | Original definition-container XML, readable report lines, and source-to-output step mappings |

Each hidden record contains `copy`, `sourceKey`, `sourceId`, `outputIds`, `label`, `parent` (source key), `element`, `hidden`, `visibility`, `semanticCategory`, `declaredDisplay`, and `declaredVisibility`. Structural keys are slash-separated zero-based element-sibling indices, beginning with the source root (`0`). They identify elements without source IDs.

Visibility distinguishes visible content, hiding by self/ancestor, semantic-container exclusion, and unresolved stylesheets in analysis. Unresolved stylesheets block export rather than publishing a falsely computed visibility result. Material thickness, kerf, constraints, licensing, and custom semantic content are retained from the source; absent manufacturing facts are not invented.

## Estimated Runtime and metadata-only updates

Recognized exports open in **Edit metadata** mode. Part name, order, batch, total yield, and runtime are editable without rebuilding, repartitioning, flattening, fitting, or changing machine settings. The filename follows the edited program fields. The browser downloads a separate file; uploaded originals are never overwritten. XML formatting may change, but all non-target XML and unknown JSON fields retain their meaning. This mode does not require production mapping or a compatible stylesheet. XML parsing still prohibits DTDs and external entity resolution; unrendered source content is not executed.

Runtime is entered as optional hours, minutes, and seconds. Hours can exceed 23; minutes and seconds must be 0–59. Values must be whole nonnegative components producing a positive duration no larger than 922337203685 seconds (the whole-second `TimeSpan` range). All blank means unknown, and **Clear estimate** removes the property. Runtime is never placed in the filename, multiplied by yield/copies, or inferred from geometry, laser type, or vendor settings.

For example, 25 hours, 1 minute, 1 second is stored as `"estimatedRuntimeSeconds":90061`. Editing an original source yield of 4 × 3 copies from 12 to 11 sets `program.yield`, `totalYield`, and `yieldOverride` to 11 while preserving sourceYield 4 and copyCount 3. Restoring total yield 12 removes the override.

Selecting **Create batch** uses the effective imported total as the new source yield. A yield of 8 copied three times produces 24. The old override and runtime remain only in historical source metadata, never as the current output estimate. New batch estimates begin blank. Changing copies, dimensions, margins, spacing, mappings, or ordering clears an entered estimate with a notice; editing program fields or the preview background retains it.

The service boundary is `SvgMetadataService.Parse`, `Read`, `SelectProgram`, and `Update`. `Update` operates on the untouched `SvgAnalysis.SourceDocument`, patches the existing JSON, and returns a `BatchResult`. `SvgAnalysis.Document` is the separate prepared batching copy. `SvgBatchService.Build` accepts an optional whole-job runtime; `ProgramMetadata` and its filename grammar remain unchanged.

## Glowforge compatibility subset

The observed namespace is `http://glowforge.com/namespaces/gf`. `gf:laserdefs/gf:laserdef` records bind to artwork through class tokens. Repeated identical tokens are harmless. Raw attributes, including unknown fields, remain separate from human-readable report text. No conversion is assumed between raw speed/focus/power values and the application's displayed values.

In the supplied examples, Score can be encoded with `type="cut"` and a `score::...` setting ID. Consistent evidence can suggest Score or Engrave; ordinary Cut still needs an InteriorCut/ExteriorCut assignment. Conflicting operation evidence requires review. Recognized settings reports use the Glowforge header, design/timestamp/version lines, and step records together with native definitions; they are retained hidden during batching and shown in the read-only panel. Ordinary text stays production artwork.

For batch exports, native steps follow the emitted copy/operation partitions, then first artwork occurrence within each partition. Definitions are split into unique class bindings as needed, and orders are regenerated from zero. All other settings remain unchanged. A warning reports changed native ordering, and the original definitions and each sourceClass/sourceOrder → outputClass/outputOrder/copy/operation/sourceKeys mapping are stored as provenance. Duplicate definitions/orders, conflicting bindings, ID-bearing definitions, bindings on containers/clones, or visible artwork without a binding block native ordered export instead of guessing.

This is support for the demonstrated export format, not a claim of a published vendor schema or universal Glowforge compatibility. Actual application re-import must be checked separately before relying on regenerated native ordering. Settings are displayed and preserved; no machine-setting editor or automatic runtime estimator is provided.

## Verification

Run `dotnet test LaserBatcher.Tests/LaserBatcher.Tests.csproj` and `dotnet build LaserBatcher/LaserBatcher.csproj`. CI runs the service regression suite before publication. Tests compile the same browser-independent service files as the app.

Optional browser regression requires Node, Playwright, and Chromium. Start the app with `dotnet run --project LaserBatcher --urls http://localhost:5188`, then run `node LaserBatcher.Tests/browser-smoke.cjs`. Set `NODE_PATH` if Playwright is installed outside the repository, or `PLAYWRIGHT_MODULE` to an installed Playwright/Playwright Core package path. `PLAYWRIGHT_CHROMIUM_EXECUTABLE` can select an installed Chromium binary and `LASERBATCHER_URL` can select the local app URL.

The browser check exercises upload, mapping, both batching orders, metadata-only updates, runtime persistence/validation/invalidation, yield overrides, filename conflicts, Glowforge settings/order warnings, XML errors, and downloads. It compares source/batch pixels at a device scale factor of 2 and checks that metadata-only edits preserve non-target XML. Unrelated remote requests and service workers are disabled for a deterministic application test; offline caching is not tested. Screenshots and sample exports are written under the ignored `LaserBatcher.Tests/obj/browser-qa` directory. The small SVG fixtures reproduce the six supplied file formats without including the original design geometry.
