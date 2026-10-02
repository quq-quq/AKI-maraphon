# Tropical fish export

15 separate FBX files and ready-to-use prefabs: `TropicalFish01..15`.

Only BaseMap textures are imported, physically resized to 512x512. Textures are sRGB, compressed DXT1 on the current desktop target, with mipmaps, bilinear filtering and CPU Read/Write disabled. Each fish has one URP Simple Lit material and no extra maps. The supplied current geometry was preserved: 9–16 triangles per fish, 191 triangles in all. No additional decimation was applied. Camera, lights, unused material slots and scene placement offsets were excluded. Prefab shadows and light/reflection probes are off.

The source project contained static fish without armatures; these are static mesh prefabs. No new fish animation or scene population was requested or added. Desktop source was not overwritten; the open project was backed up before export.

Use the prefabs under `Prefabs`, not raw FBX files, to keep the material/performance setup.
