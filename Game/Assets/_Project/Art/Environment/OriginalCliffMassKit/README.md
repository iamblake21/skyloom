# Original Cliff Mass Kit

Eight original, watertight modular cliff masses generated from coarse measured
shape and surface statistics. No extracted source mesh or texture is included.

## Geometry split

- LOD0: 1,090 vertices / 2,176 triangles. Macro silhouette, crown bevel,
  undercut, vertical flutes, long crease channels, crease shoulders and recessed
  planes are present in the mesh.
- LOD1: 354 vertices / 704 triangles.
- LOD2: 130 vertices / 256 triangles and used for the prefab collider.
- Fine rock grain remains procedural in the material, matching the same division
  observed in the studied references: their low-triangle meshes cannot contain
  the fine normal-map striation geometrically.

## Assembly

- Use `ENV_CliffMass_A` and `B` for near-vertical retaining walls.
- Sink the narrow bases of `ENV_CliffMass_C` and `D` into terrain or another mass
  to create high plateaus, overhangs and floating-island silhouettes.
- Use the four `ENV_CliffShelf_*` pieces for terraces, feet and overlap seams.
- Keep non-uniform scale roughly within 0.75-1.30. Rotate repeated pieces and
  overlap them; do not place every pivot directly on the terrain surface.
- Prefabs include three LODs, a simplified collider and six snap anchors.

The two preview scenes demonstrate a catalog and a multi-level assembly.

## Shared landmass material

`M_OriginalCliffMass` and the Starter Island Terrain now use the same
world-aligned landmass surface. This is deliberate: a shelf placed against a
Terrain slope keeps the same rock scale, erosion direction, normal response and
grass language instead of looking like a separate prop.

- Rock albedo and normal are full three-axis world projections at a 30 m world
  scale, so rotated or non-uniformly scaled modules do not expose UV seams.
- The grass cap is derived from the real world-space surface normal. Its source
  values are slope offset `-0.6` and hardness `10`; a broad 64 m variation mask
  only nudges the edge and palette. It never cuts the cap with fine noise.
- A narrow soil fringe softens the transition between the rock face and grass.
  This replaces the previous perfectly sharp green lid.
- `M_OriginalCliffMass_NoGrass` is the production variant for buried pieces,
  undersides and assemblies where a second grass lip would be visible.
- On the Terrain, automatic cliff coverage may consume only the two painted
  grass layers. Painted dirt/path weights remain untouched.

The texture set is an original, deterministic clean-room recreation. Regenerate
it with `Game/Tools/generate_landmass_textures.py`; no extracted game texture is
shipped in this kit.

The shared implementation lives in
`StarterIsland/Shaders/Includes/StarterIslandLandmassSurface.hlsl`. Keep material
values synchronized between the Terrain and cliff materials when art-directing
the world scale, slope edge or seasonal grass palette.
