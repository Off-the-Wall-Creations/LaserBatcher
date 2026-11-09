# LaserBatcher
An open-source, online tool for batching multiple parts for laser operations. All files are processed locally, so no need to worry about your designs being stored up in the :cloud: someplace :smiley:.

Batch exports preserve hidden layers and semantic metadata, support per-copy or sheet-wide operation ordering, and use strict program filenames for external metadata extraction. Upload an existing LaserBatcher export to edit program fields and an optional, laser-independent **Estimated Runtime** without rebuilding its layout. Glowforge imports show saved settings and align native step order with the batch while preserving machine-setting values. See [SVG metadata and filename conventions](docs/svg-metadata.md) for the v2 contract, legacy imports, compatibility limits, and tests.

The viewport-filling workspace has compact job toolbars, a resizable mapping panel and a pan/zoom canvas. Upload shows the source; **▶ Render** generates the batch preview. **Ignore** omits selected production geometry, with explicit child overrides and preserved clone dependencies. Runtime, hidden content, Glowforge settings and issues open in dialogs. See [workspace controls](docs/workspace.md).

Choose System, Light, or Dark appearance in the header. Operation Mapping groups can be collapsed individually or together; their counts and assignments still include hidden descendants. These view controls leave the runtime estimate and exported artwork unchanged. See the [brand guide](docs/brand-guide.md) for the palette, editable icon assets, creator credit, and browser checks.

The [GitHub Pages hosting guide](docs/github-pages.md) explains the custom-domain root path, deployment checks, PWA integrity hashes, and recovery from stale cached builds.

This program supports Generic SVG and Inkscape-labelled designs with the following considerations:

 - Document dimensions must explicitly use **in, mm, cm, pt, or pc**. Sheet controls remain in inches; unitless and pixel-only root dimensions are rejected.
 
![image](https://github.com/Off-the-Wall-Creations/LaserBatcher/assets/11471252/260ec1d4-dd0e-4cfb-a9a3-388687552519)

 - The document **should** be resized to fit the bounds of the SVG
 
![image](https://github.com/Off-the-Wall-Creations/LaserBatcher/assets/11471252/10361e8c-436f-47ed-80e8-d0bf515029a1)

 - Text and embedded raster images are preserved. Conversion to paths is optional and depends on the consuming laser software. External references and unresolved stylesheets must be resolved before ordered export.
 
![image](https://github.com/Off-the-Wall-Creations/LaserBatcher/assets/11471252/721b6483-ecba-4a55-b3c9-b7fc15edbd64)

# Sponsorship
Please consider sponsoring this project if you found have begun incorporating it into your process. We would love to receive feedback on which parts are useful so we can make sure we keep or improve them. However, bugs and new ideas are even better so we can approach a wider audience.

Also let us know if you have specific needs for you business, we may be able to help with a custom project.
