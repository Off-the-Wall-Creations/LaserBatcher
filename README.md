# LaserBatcher
An open-source, online tool for batching multiple parts for laser operations. All files are processed locally, so no need to worry about your designs being stored up in the :cloud: someplace :smiley:.

Batch exports preserve hidden layers and semantic metadata, support per-copy or sheet-wide operation ordering, and use strict program filenames for external metadata extraction. See [SVG metadata and filename conventions](docs/svg-metadata.md) for the grammar, layer mapping, compatibility limits, and tests.

This program supports Generic SVG and Inkscape-labelled designs with the following considerations:

 - The document dimensions **must** be in **inches**
 
![image](https://github.com/Off-the-Wall-Creations/LaserBatcher/assets/11471252/260ec1d4-dd0e-4cfb-a9a3-388687552519)

 - The document **should** be resized to fit the bounds of the SVG
 
![image](https://github.com/Off-the-Wall-Creations/LaserBatcher/assets/11471252/10361e8c-436f-47ed-80e8-d0bf515029a1)

 - Text and embedded raster images are preserved. Conversion to paths is optional and depends on the consuming laser software. External references and unresolved stylesheets must be resolved before ordered export.
 
![image](https://github.com/Off-the-Wall-Creations/LaserBatcher/assets/11471252/721b6483-ecba-4a55-b3c9-b7fc15edbd64)

# Sponsorship
Please consider sponsoring this project if you found have begun incorporating it into your process. We would love to receive feedback on which parts are useful so we can make sure we keep or improve them. However, bugs and new ideas are even better so we can approach a wider audience.

Also let us know if you have specific needs for you business, we may be able to help with a custom project.
