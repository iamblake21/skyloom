# Reference Rock Kit

This kit recreates the separate faceted rock assets visible in the supplied
reference sheet. It is authored as angular, disconnected sculpted chunks: no
sphere cluster, voxel remesh, smooth tube, or inherited project rock geometry
is used.

## Runtime contract

- Unity units are metres; every origin is at ground level.
- Each FBX contains `LOD0`, `LOD1`, and `LOD2` meshes.
- Each prefab owns a three-stage `LODGroup` at 0.55 / 0.22 / 0.01.
- `LOD2` is reused by a static, non-convex `MeshCollider`.
- All renderers share `M_ReferenceFacetedRock` and support GPU instancing.
- The mesh colour layer stores stable per-facet tonal variation.
- Hidden surfaces are inferred in the same faceted language because the source
  reference exposes only one three-quarter view.

## Rebuild

Run `Tools/rebuild-stylized-rock-world-kit.ps1`, then use
`CML/Art/Rebuild Reference Rock Kit` if the Unity editor did not rebuild
automatically. The source-of-truth Blender file and JSON mesh audit live under
`SourceArt/Environment/StylizedRockWorldKit`.

The catalog scene is
`Preview/SCN_ReferenceRockKit_Catalog.unity`; it is deliberately isolated from
the game scenes.
