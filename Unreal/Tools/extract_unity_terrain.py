"""Extract Unity terrains into data an Unreal Landscape can be built from.

Unity and Unreal disagree about what heightmap sizes are legal. Unity uses
``2^n + 1`` samples; Unreal uses ``components x componentQuads + 1`` where the
component size is always a multiple of ``2^n - 1``. No Unreal size divides
Unity's 1024 quads, so one of the two has to give.

This exporter resamples over the complete normalised terrain extent. Padding a
Unity heightmap to the next legal Unreal resolution creates a flat strip at two
edges; keeping the Unity metres-per-quad scale also makes the Landscape larger
than the scene that surrounds it. The closest legal Landscape resolution is
used instead, and both height and layer weights are sampled over [0, 1]. That
keeps the world footprint and every scene-space landmark aligned.

Heights are re-based, not rescaled: Unreal's raw height 32768 is its zero plane,
so a Unity sample ``r`` is written as ``32768 + r`` and the landscape's Z scale
carries the metric conversion. That is one Unreal unit per Unity unit with no
rounding anywhere.
"""

from __future__ import annotations

import argparse
import array
import hashlib
import json
import math
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Content" / "Python"))

from unity_serialized_file import SerializedFile, UnityFormatError, guid_to_text  # noqa: E402
from cml_unity_yaml import index_by_file_id, load_unity_documents, parse_reference  # noqa: E402


PROJECT_ROOT = Path(__file__).resolve().parents[1]
OUTPUT_ROOT = PROJECT_ROOT / "Migration" / "UnityTerrain"
REPORT_PATH = PROJECT_ROOT / "Migration" / "unity_terrain_extract_report.json"

CLASS_TEXTURE2D = 28
CLASS_TERRAIN_DATA = 156

# Unity stores heights as 15-bit fractions of the terrain's height range. The
# value is not assumed: extract() checks it against the height quadtree's root
# node, which records the terrain's own normalised min and max.
UNITY_MAX_RAW_HEIGHT = 32766

# Unreal's raw height 32768 is the landscape's zero plane and 128 raw units make
# one unscaled world unit.
UNREAL_HEIGHT_ORIGIN = 32768
UNREAL_HEIGHT_UNITS_PER_UNIT = 128

# One subsection of 127 quads per component. Eight components produce 1017
# samples for Unity's 1025-sample terrains: only eight samples are removed by a
# full-extent resample, instead of adding a 47-quad artificial skirt.
SECTION_SIZE_QUADS = 127
SUBSECTIONS_PER_COMPONENT = 1

UNITY_UNITS_TO_UNREAL = 100.0

# The rest of the migration converts what the game ships: everything under
# ``Assets/_Project``. Unity's crash-recovery copies and the demo scenes that
# come with third-party packages are out of scope there, so a terrain is only
# exported if a scene that is itself in scope places it.
IN_SCOPE_SCENE_ROOT = "Assets/_Project/"


class TerrainExtractError(Exception):
    pass


@dataclass
class TerrainPlacement:
    """Where a scene puts a terrain, and which asset it puts there."""

    scene: str
    game_object_name: str
    position: tuple[float, float, float]
    material_guid: str


@dataclass
class _UnityTransform:
    position: tuple[float, float, float] = (0.0, 0.0, 0.0)
    rotation: tuple[float, float, float, float] = (0.0, 0.0, 0.0, 1.0)
    scale: tuple[float, float, float] = (1.0, 1.0, 1.0)

    def compose(self, child: "_UnityTransform") -> "_UnityTransform":
        scaled = tuple(child.position[i] * self.scale[i] for i in range(3))
        rotated = _rotate(self.rotation, scaled)
        return _UnityTransform(
            tuple(self.position[i] + rotated[i] for i in range(3)),
            _multiply(self.rotation, child.rotation),
            tuple(self.scale[i] * child.scale[i] for i in range(3)),
        )


@dataclass
class _PrefabTerrain:
    data_guid: str
    game_object_name: str
    local_transform: _UnityTransform
    material_guid: str
    terrain_file_id: int
    game_object_file_id: int
    root_transform_file_id: int


def _read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def _multiply(a: tuple[float, ...], b: tuple[float, ...]) -> tuple[float, ...]:
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def _rotate(q: tuple[float, ...], v: tuple[float, ...]) -> tuple[float, ...]:
    x, y, z, w = q
    vx, vy, vz = v
    dot_uv = x * vx + y * vy + z * vz
    dot_uu = x * x + y * y + z * z
    cross = (y * vz - z * vy, z * vx - x * vz, x * vy - y * vx)
    return (
        2.0 * dot_uv * x + (w * w - dot_uu) * vx + 2.0 * w * cross[0],
        2.0 * dot_uv * y + (w * w - dot_uu) * vy + 2.0 * w * cross[1],
        2.0 * dot_uv * z + (w * w - dot_uu) * vz + 2.0 * w * cross[2],
    )


def _transform(values: dict) -> _UnityTransform:
    position = values.get("m_LocalPosition") or {}
    rotation = values.get("m_LocalRotation") or {}
    scale = values.get("m_LocalScale") or {}
    return _UnityTransform(
        tuple(float(position.get(axis, 0.0)) for axis in "xyz"),
        (
            float(rotation.get("x", 0.0)),
            float(rotation.get("y", 0.0)),
            float(rotation.get("z", 0.0)),
            float(rotation.get("w", 1.0)),
        ),
        tuple(float(scale.get(axis, 1.0)) for axis in "xyz"),
    )


def _override_transform(modifications: list, root_transform_file_id: int) -> _UnityTransform:
    values: dict[str, float] = {}
    for modification in modifications:
        if not isinstance(modification, dict):
            continue
        target = parse_reference(modification.get("target"))
        if target.file_id != root_transform_file_id:
            continue
        path = str(modification.get("propertyPath", ""))
        value = modification.get("value")
        if isinstance(value, (int, float)):
            values[path] = float(value)
    return _UnityTransform(
        tuple(values.get(f"m_LocalPosition.{axis}", 0.0) for axis in "xyz"),
        (
            values.get("m_LocalRotation.x", 0.0),
            values.get("m_LocalRotation.y", 0.0),
            values.get("m_LocalRotation.z", 0.0),
            values.get("m_LocalRotation.w", 1.0),
        ),
        tuple(values.get(f"m_LocalScale.{axis}", 1.0) for axis in "xyz"),
    )


def _world_transforms(documents) -> dict[int, _UnityTransform]:
    transforms = {item.file_id: item for item in documents if item.type_name == "Transform"}
    resolved: dict[int, _UnityTransform] = {}

    def resolve(file_id: int, guard: set[int]) -> _UnityTransform:
        if file_id in resolved:
            return resolved[file_id]
        document = transforms.get(file_id)
        if document is None or file_id in guard:
            return _UnityTransform()
        guard.add(file_id)
        local = _transform(document.values)
        parent = parse_reference(document.get("m_Father"))
        world = resolve(parent.file_id, guard).compose(local) if parent.file_id else local
        guard.discard(file_id)
        resolved[file_id] = world
        return world

    for file_id in transforms:
        resolve(file_id, set())
    return resolved


def _prefab_terrains(unity_root: Path) -> dict[str, list[_PrefabTerrain]]:
    catalogue: dict[str, list[_PrefabTerrain]] = {}
    for prefab in sorted((unity_root / "Assets").rglob("*.prefab")):
        meta = prefab.with_suffix(prefab.suffix + ".meta")
        match = re.search(r"^guid:\s*([0-9a-f]{32})", _read_text(meta), re.M)
        if not match:
            continue
        documents = load_unity_documents(prefab)
        by_file_id = index_by_file_id(documents)
        transforms = _world_transforms(documents)
        transform_by_owner = {
            parse_reference(item.get("m_GameObject")).file_id: item
            for item in documents
            if item.type_name == "Transform"
        }
        root_transforms = [
            item.file_id
            for item in documents
            if item.type_name == "Transform" and not parse_reference(item.get("m_Father")).file_id
        ]
        if not root_transforms:
            continue
        root_id = root_transforms[0]
        for terrain in (item for item in documents if item.type_name == "Terrain"):
            owner = parse_reference(terrain.get("m_GameObject"))
            data = parse_reference(terrain.get("m_TerrainData"))
            transform_document = transform_by_owner.get(owner.file_id)
            game_object = by_file_id.get(owner.file_id)
            if not data.guid or transform_document is None or game_object is None:
                continue
            material = parse_reference(terrain.get("m_MaterialTemplate"))
            catalogue.setdefault(match.group(1), []).append(
                _PrefabTerrain(
                    data.guid,
                    str(game_object.get("m_Name", "Terrain")),
                    transforms[transform_document.file_id],
                    material.guid,
                    terrain.file_id,
                    owner.file_id,
                    root_id,
                )
            )
    return catalogue


def find_scene_placements(unity_root: Path) -> dict[str, list[TerrainPlacement]]:
    """Finds every ``Terrain`` component in the project's scenes.

    Unity keeps the heights in the TerrainData asset but the placement in the
    scene, so neither file alone says where the ground is.
    """
    placements: dict[str, list[TerrainPlacement]] = {}
    prefab_terrains = _prefab_terrains(unity_root)
    for scene in sorted((unity_root / "Assets").rglob("*.unity")):
        if not scene.relative_to(unity_root).as_posix().startswith(IN_SCOPE_SCENE_ROOT):
            continue
        documents = load_unity_documents(scene)
        by_file_id = index_by_file_id(documents)
        world_transforms = _world_transforms(documents)
        transform_by_owner = {
            parse_reference(item.get("m_GameObject")).file_id: item
            for item in documents
            if item.type_name == "Transform"
        }

        for terrain in (item for item in documents if item.type_name == "Terrain"):
            owner = parse_reference(terrain.get("m_GameObject"))
            data = parse_reference(terrain.get("m_TerrainData"))
            transform_document = transform_by_owner.get(owner.file_id)
            game_object = by_file_id.get(owner.file_id)
            if not data.guid or transform_document is None:
                continue
            material = parse_reference(terrain.get("m_MaterialTemplate"))
            placements.setdefault(data.guid, []).append(
                TerrainPlacement(
                    scene.relative_to(unity_root).as_posix(),
                    str(game_object.get("m_Name", "Terrain")) if game_object else "Terrain",
                    world_transforms[transform_document.file_id].position,
                    material.guid,
                )
            )

        for instance in (item for item in documents if item.type_name == "PrefabInstance"):
            source = parse_reference(instance.get("m_SourcePrefab"))
            entries = prefab_terrains.get(source.guid) or []
            if not entries:
                continue
            modifications = instance.get("m_Modification.m_Modifications") or []
            parent = parse_reference(instance.get("m_Modification.m_TransformParent"))
            parent_world = world_transforms.get(parent.file_id, _UnityTransform())
            removed = {
                parse_reference(item).file_id
                for item in (instance.get("m_Modification.m_RemovedGameObjects") or [])
            }
            for entry in entries:
                if entry.game_object_file_id in removed:
                    continue
                instance_world = parent_world.compose(
                    _override_transform(modifications, entry.root_transform_file_id)
                )
                terrain_world = instance_world.compose(entry.local_transform)
                if any(abs(value - 1.0) > 1.0e-5 for value in terrain_world.scale):
                    raise TerrainExtractError(
                        f"{scene}: scaled Terrain prefab instances are not supported"
                    )
                rotation_vector = _rotate(terrain_world.rotation, (0.0, 0.0, 1.0))
                if abs(rotation_vector[0]) > 1.0e-5 or abs(rotation_vector[1]) > 1.0e-5:
                    raise TerrainExtractError(
                        f"{scene}: rotated Terrain prefab instances are not supported"
                    )
                material_guid = entry.material_guid
                for modification in modifications:
                    target = parse_reference(modification.get("target"))
                    if target.file_id != entry.terrain_file_id:
                        continue
                    if str(modification.get("propertyPath", "")) == "m_MaterialTemplate":
                        reference = parse_reference(modification.get("objectReference"))
                        material_guid = reference.guid or material_guid
                placements.setdefault(entry.data_guid, []).append(
                    TerrainPlacement(
                        scene.relative_to(unity_root).as_posix(),
                        entry.game_object_name,
                        terrain_world.position,
                        material_guid,
                    )
                )
    return placements


def read_asset_guid(asset: Path) -> str:
    meta = asset.with_suffix(asset.suffix + ".meta")
    match = re.search(r"^guid:\s*([0-9a-f]{32})", _read_text(meta), re.M)
    if not match:
        raise TerrainExtractError(f"{meta} has no guid")
    return match.group(1)


def landscape_resolution(unity_resolution: int) -> tuple[int, int]:
    """Legal Unreal size nearest to the Unity heightmap resolution."""
    component_quads = SECTION_SIZE_QUADS * SUBSECTIONS_PER_COMPONENT
    quads = unity_resolution - 1
    components = max(1, int(round(quads / component_quads)))
    return components, components * component_quads + 1


def _verify_height_scale(heightmap: dict[str, Any], heights: array.array) -> None:
    """Checks the raw-height scale against the terrain's own quadtree root.

    ``m_MinMaxPatchHeights`` stores normalised min/max pairs for a quadtree of
    height patches, coarsest last. The final pair therefore describes the whole
    terrain, which gives an independent reading of the scale rather than a
    constant taken on trust.
    """
    patch_heights = heightmap.get("m_MinMaxPatchHeights") or []
    if len(patch_heights) < 2:
        return
    normalised_low, normalised_high = patch_heights[-2], patch_heights[-1]
    for raw, normalised in ((min(heights), normalised_low), (max(heights), normalised_high)):
        if normalised <= 0.0:
            continue
        implied = raw / normalised
        if abs(implied - UNITY_MAX_RAW_HEIGHT) > 1.0:
            raise TerrainExtractError(
                f"height scale looks wrong: raw {raw} over normalised {normalised} "
                f"implies {implied:.1f}, expected {UNITY_MAX_RAW_HEIGHT}"
            )


def build_heightmap(
    heights: array.array, unity_resolution: int, target_resolution: int
) -> bytes:
    """Transposes and bilinearly resamples Unity's heightmap over its full extent.

    Unity indexes ``[z][x]`` with x along its own X axis; Unreal's landscape
    runs its first axis along world X, which is Unity's Z. The two are the same
    grid read in the other order, so the transpose here is the whole of the
    coordinate change for the heightfield.
    """
    output = array.array("H", [0]) * (target_resolution * target_resolution)
    last = unity_resolution - 1
    target_last = target_resolution - 1
    # Unreal stores a Landscape sample at [UnrealY][UnrealX].  With the
    # project-wide coordinate conversion Unreal.X=Unity.Z and
    # Unreal.Y=Unity.X, the output row therefore samples Unity X and the
    # output column samples Unity Z.  The previous implementation sampled row
    # as Z and column as X, despite the docstring saying it transposed the
    # grid.  That rotated the heightfield relative to every migrated actor.
    for row in range(target_resolution):
        unity_x = row * last / target_last
        low_x = int(math.floor(unity_x))
        high_x = min(low_x + 1, last)
        weight_x = unity_x - low_x
        base = row * target_resolution
        for column in range(target_resolution):
            unity_z = column * last / target_last
            low_z = int(math.floor(unity_z))
            high_z = min(low_z + 1, last)
            weight_z = unity_z - low_z
            low_row = low_z * unity_resolution
            high_row = high_z * unity_resolution
            low_value = (
                heights[low_row + low_x] * (1.0 - weight_x)
                + heights[low_row + high_x] * weight_x
            )
            high_value = (
                heights[high_row + low_x] * (1.0 - weight_x)
                + heights[high_row + high_x] * weight_x
            )
            raw_height = int(round(low_value * (1.0 - weight_z) + high_value * weight_z))
            output[base + column] = UNREAL_HEIGHT_ORIGIN + raw_height
    if sys.byteorder != "little":
        output.byteswap()
    return output.tobytes()


def _resample_axis(target_resolution: int, source_resolution: int):
    """Precomputes the bilinear taps mapping landscape vertices to alphamap texels.

    Unity samples its alphamap with a clamped bilinear fetch over the terrain's
    normalised extent, so a vertex at fraction ``v`` lands on texel
    ``v * resolution - 0.5``. Target vertices span the full terrain extent.
    """
    taps = []
    for index in range(target_resolution):
        fraction = index / (target_resolution - 1)
        position = fraction * source_resolution - 0.5
        low = int(position // 1)
        weight = position - low
        high = min(max(low + 1, 0), source_resolution - 1)
        low = min(max(low, 0), source_resolution - 1)
        taps.append((low, high, weight))
    return taps


def build_weightmaps(
    alpha_textures: list[tuple[int, int, bytes]],
    layer_count: int,
    unity_resolution: int,
    target_resolution: int,
) -> list[bytes]:
    """Samples Unity's splat textures onto the landscape's vertices.

    Unreal stores one weight per layer per vertex and expects them to sum to
    255, so the samples are renormalised after interpolation instead of being
    trusted to still add up.
    """
    resolutions = {texture[0] for texture in alpha_textures}
    if len(resolutions) != 1:
        raise TerrainExtractError("splat textures disagree about resolution")
    source_resolution = resolutions.pop()

    taps = _resample_axis(target_resolution, source_resolution)
    layers = [array.array("B", bytes(target_resolution * target_resolution))
              for _ in range(layer_count)]

    # Weight payloads use Unity's [Z][X] layout, while Unreal Landscape layer
    # data uses [Y][X] == [UnityX][UnityZ].  Keep the same explicit transpose
    # as the heightmap so painted layers remain attached to the geometry.
    for row in range(target_resolution):
        low_x, high_x, x_weight = taps[row]
        base = row * target_resolution
        for column in range(target_resolution):
            low_z, high_z, z_weight = taps[column]
            corners = (
                ((low_z * source_resolution + low_x) * 4,
                 (1.0 - z_weight) * (1.0 - x_weight)),
                ((low_z * source_resolution + high_x) * 4,
                 (1.0 - z_weight) * x_weight),
                ((high_z * source_resolution + low_x) * 4,
                 z_weight * (1.0 - x_weight)),
                ((high_z * source_resolution + high_x) * 4,
                 z_weight * x_weight),
            )
            samples = []
            for layer in range(layer_count):
                _, _, pixels = alpha_textures[layer // 4]
                channel = layer % 4
                samples.append(
                    sum(pixels[offset + channel] * weight for offset, weight in corners)
                )
            total = sum(samples)
            if total <= 0.0:
                layers[0][base + column] = 255
                continue
            written = 0
            for layer in range(layer_count - 1):
                value = int(round(samples[layer] * 255.0 / total))
                value = min(value, 255 - written)
                layers[layer][base + column] = value
                written += value
            layers[layer_count - 1][base + column] = 255 - written

    return [layer.tobytes() for layer in layers]


def build_control_tga(weightmaps: list[bytes], resolution: int) -> bytes:
    """Pack the first four Unity layers into one global, non-lossy RGBA map.

    The Unreal Landscape still receives the individual weights for editor paint
    semantics.  The material, however, must see the exact normalised Unity
    control vector at a *global* terrain UV; relying on Landscape component UVs
    repeats the alphamap once per component and creates the grid/edge artifacts
    that prompted this migration repair.

    TGA is used deliberately: it is uncompressed, trivial to validate and is
    imported by Unreal without colour-space loss.  Its byte order is BGRA.
    """
    pixel_count = resolution * resolution
    channels = [payload for payload in weightmaps[:4]]
    while len(channels) < 4:
        channels.append(bytes(pixel_count))
    if any(len(channel) != pixel_count for channel in channels):
        raise TerrainExtractError("control-map layer size does not match its resolution")

    header = struct.pack(
        "<BBBHHBHHHHBB",
        0, 0, 2,          # no id, no colour map, uncompressed true-colour
        0, 0, 0,          # colour-map fields
        0, 0,             # origin
        resolution, resolution,
        32, 0x28,         # BGRA8, top-left origin, eight alpha bits
    )
    pixels = bytearray(pixel_count * 4)
    red, green, blue, alpha = channels
    # The layer payloads above are arranged for Landscape import as
    # [UnityX][UnityZ].  A regular texture sampled with (UnityX, UnityZ) needs
    # columns=X and rows=Z, so transpose once more while packing the global
    # control texture.  This deliberately keeps the material texture in its
    # natural Unity UV orientation while the Landscape vertex data stays in
    # Unreal's axis orientation.
    for image_row in range(resolution):
        for image_column in range(resolution):
            landscape_index = image_column * resolution + image_row
            pixel_index = image_row * resolution + image_column
            offset = pixel_index * 4
            pixels[offset + 0] = blue[landscape_index]
            pixels[offset + 1] = green[landscape_index]
            pixels[offset + 2] = red[landscape_index]
            pixels[offset + 3] = alpha[landscape_index]
    return header + bytes(pixels)


def build_visibilitymap(
    holes: bytes | bytearray | array.array,
    source_resolution: int,
    target_resolution: int,
) -> bytes:
    """Converts Unity Paint Holes into Unreal's reserved visibility weights.

    Unity stores one byte per terrain quad (255 means surface, 0 means hole),
    whereas Unreal samples a visibility weight at Landscape vertices (255 means
    hole).  Nearest sampling preserves the authored binary boundary; interpolating
    would create partially cut cells and a bright/transparent fringe.
    """
    if isinstance(holes, array.array):
        source = holes.tobytes()
    else:
        source = bytes(holes)
    expected = source_resolution * source_resolution
    if len(source) != expected:
        raise TerrainExtractError(
            f"holes map holds {len(source)} bytes, expected {expected}"
        )

    output = bytearray(target_resolution * target_resolution)
    target_last = target_resolution - 1
    # Same [UnityX][UnityZ] -> [UnrealY][UnrealX] transpose as height and
    # layer data.  Visibility must move with the quads; leaving it untransposed
    # was the source of the giant triangular/diagonal holes in the migrated
    # map.
    for row in range(target_resolution):
        source_x = min(int(row * source_resolution / target_last), source_resolution - 1)
        target_base = row * target_resolution
        for column in range(target_resolution):
            source_z = min(
                int(column * source_resolution / target_last), source_resolution - 1
            )
            # Unreal visibility is the inverse of Unity's holes texture.
            output[target_base + column] = (
                255 if source[source_z * source_resolution + source_x] < 128 else 0
            )
    return bytes(output)


def read_alpha_textures(
    serialized: SerializedFile, pointers: list[dict[str, Any]]
) -> list[tuple[int, int, bytes]]:
    by_path_id = {info.path_id: info for info in serialized.objects_of_class(CLASS_TEXTURE2D)}
    textures = []
    for pointer in pointers:
        guid, path_id = serialized.resolve(pointer)
        if guid:
            raise TerrainExtractError("splat texture lives outside the terrain asset")
        texture = serialized.read_object(by_path_id[path_id])
        if texture["m_StreamData"]["path"]:
            raise TerrainExtractError(
                f"{texture['m_Name']}: pixels are in a streaming file, not the asset"
            )
        if texture["m_TextureFormat"] != 4:
            raise TerrainExtractError(
                f"{texture['m_Name']}: format {texture['m_TextureFormat']} is not RGBA32"
            )
        width, height = texture["m_Width"], texture["m_Height"]
        if width != height:
            raise TerrainExtractError(f"{texture['m_Name']}: splat texture is not square")
        pixels = texture["image data"][: width * height * 4]
        textures.append((width, height, pixels))
    return textures


def extract(asset: Path, unity_root: Path, placements: list[TerrainPlacement]) -> dict[str, Any]:
    serialized = SerializedFile(asset)
    infos = serialized.objects_of_class(CLASS_TERRAIN_DATA)
    if len(infos) != 1:
        raise TerrainExtractError(f"expected one TerrainData, found {len(infos)}")
    terrain = serialized.read_object(infos[0])

    name = terrain["m_Name"]
    heightmap = terrain["m_Heightmap"]
    heights = heightmap["m_Heights"]
    if not isinstance(heights, array.array):
        heights = array.array("h", heights)
    unity_resolution = heightmap["m_Resolution"]
    if len(heights) != unity_resolution * unity_resolution:
        raise TerrainExtractError("heightmap size does not match its resolution")
    _verify_height_scale(heightmap, heights)

    scale = heightmap["m_Scale"]
    quads = unity_resolution - 1
    size = (scale["x"] * quads, scale["y"], scale["z"] * quads)

    splat = terrain["m_SplatDatabase"]
    layer_guids = [serialized.resolve(pointer)[0] for pointer in splat["m_TerrainLayers"]]
    alpha_textures = read_alpha_textures(serialized, splat["m_AlphaTextures"])

    components, target_resolution = landscape_resolution(unity_resolution)
    destination = OUTPUT_ROOT / name
    destination.mkdir(parents=True, exist_ok=True)

    files: dict[str, str] = {}

    heightmap_bytes = build_heightmap(heights, unity_resolution, target_resolution)
    (destination / "heightmap.r16").write_bytes(heightmap_bytes)
    files["heightmap.r16"] = hashlib.sha256(heightmap_bytes).hexdigest()

    holes = heightmap.get("m_Holes") or b""
    holes_resolution = unity_resolution - 1
    visibility_file = ""
    unity_hole_count = 0
    unreal_hidden_count = 0
    if holes:
        holes_bytes = holes.tobytes() if isinstance(holes, array.array) else bytes(holes)
        unity_hole_count = sum(value < 128 for value in holes_bytes)
        if unity_hole_count:
            visibility_bytes = build_visibilitymap(
                holes_bytes, holes_resolution, target_resolution
            )
            visibility_file = "visibility.r8"
            (destination / visibility_file).write_bytes(visibility_bytes)
            files[visibility_file] = hashlib.sha256(visibility_bytes).hexdigest()
            unreal_hidden_count = sum(value >= 128 for value in visibility_bytes)

    layer_entries = []
    if layer_guids:
        weights = build_weightmaps(
            alpha_textures, len(layer_guids), unity_resolution, target_resolution
        )
        for index, (guid, payload) in enumerate(zip(layer_guids, weights)):
            layer_name = _layer_name(unity_root, guid, index)
            file_name = f"weight_{index}_{layer_name}.r8"
            (destination / file_name).write_bytes(payload)
            files[file_name] = hashlib.sha256(payload).hexdigest()
            layer_entries.append(
                {
                    "unityIndex": index,
                    "name": layer_name,
                    "guid": guid,
                    "weightFile": file_name,
                }
            )

        control_bytes = build_control_tga(weights, target_resolution)
        (destination / "control.tga").write_bytes(control_bytes)
        files["control.tga"] = hashlib.sha256(control_bytes).hexdigest()

    # Unity's X axis (width) becomes Unreal's Y, and its Z (length) becomes
    # Unreal's X. The target Landscape may have a different vertex count, so
    # derive centimetres per target quad from the original physical footprint.
    target_quads = target_resolution - 1
    draw_scale = {
        "x": size[2] * UNITY_UNITS_TO_UNREAL / target_quads,
        "y": size[0] * UNITY_UNITS_TO_UNREAL / target_quads,
        "z": UNREAL_HEIGHT_UNITS_PER_UNIT * size[1] * UNITY_UNITS_TO_UNREAL
        / UNITY_MAX_RAW_HEIGHT,
    }

    return {
        "name": name,
        "guid": read_asset_guid(asset),
        "source": asset.relative_to(unity_root).as_posix(),
        "unity": {
            "heightmapResolution": unity_resolution,
            "alphamapResolution": splat["m_AlphamapResolution"],
            "size": {"x": size[0], "y": size[1], "z": size[2]},
            "rawHeightRange": [min(heights), max(heights)],
            "maxRawHeight": UNITY_MAX_RAW_HEIGHT,
            "holesResolution": holes_resolution if holes else 0,
            "holeCellCount": unity_hole_count,
        },
        "landscape": {
            "sectionSizeQuads": SECTION_SIZE_QUADS,
            "subsectionsPerComponent": SUBSECTIONS_PER_COMPONENT,
            "componentCount": components,
            "resolution": target_resolution,
            "sourceQuads": unity_resolution - 1,
            "targetQuads": target_quads,
            "resampledSampleDelta": target_resolution - unity_resolution,
            "heightmapFile": "heightmap.r16",
            "controlFile": "control.tga" if layer_guids else "",
            "visibilityFile": visibility_file,
            "visibilityHiddenVertexCount": unreal_hidden_count,
            "drawScale": draw_scale,
            "layers": layer_entries,
        },
        "placements": [
            {
                "scene": placement.scene,
                "actorName": placement.game_object_name,
                "unityPosition": {
                    "x": placement.position[0],
                    "y": placement.position[1],
                    "z": placement.position[2],
                },
                # Unreal.X = Unity.z, Unreal.Y = Unity.x, Unreal.Z = Unity.y.
                "unrealLocation": {
                    "x": placement.position[2] * UNITY_UNITS_TO_UNREAL,
                    "y": placement.position[0] * UNITY_UNITS_TO_UNREAL,
                    "z": placement.position[1] * UNITY_UNITS_TO_UNREAL,
                },
                "materialGuid": placement.material_guid,
            }
            for placement in placements
        ],
        "detailPrototypes": [
            {
                "prefabGuid": serialized.resolve(prototype["prototype"])[0],
                "minWidth": prototype["minWidth"],
                "maxWidth": prototype["maxWidth"],
                "minHeight": prototype["minHeight"],
                "maxHeight": prototype["maxHeight"],
                "noiseSeed": prototype["noiseSeed"],
                "noiseSpread": prototype["noiseSpread"],
                "density": prototype["density"],
                "alignToGround": prototype["alignToGround"],
                "positionJitter": prototype["positionJitter"],
                "targetCoverage": prototype["targetCoverage"],
                "useInstancing": bool(prototype["useInstancing"]),
            }
            for prototype in terrain["m_DetailDatabase"]["m_DetailPrototypes"]
        ],
        "detailPatchCount": len(terrain["m_DetailDatabase"]["m_Patches"]),
        "detailResolutionPerPatch": terrain["m_DetailDatabase"]["m_PatchSamples"],
        "detailPatchesPerSide": terrain["m_DetailDatabase"]["m_PatchCount"],
        "treeInstanceCount": len(terrain["m_DetailDatabase"]["m_TreeInstances"]),
        "files": files,
    }


_LAYER_NAMES: dict[Path, dict[str, str]] = {}


def _layer_name(unity_root: Path, guid: str, index: int) -> str:
    catalogue = _LAYER_NAMES.get(unity_root)
    if catalogue is None:
        catalogue = {}
        for meta in (unity_root / "Assets").rglob("*.terrainlayer.meta"):
            match = re.search(r"^guid:\s*([0-9a-f]{32})", _read_text(meta), re.M)
            if match:
                catalogue[match.group(1)] = meta.name[: -len(".terrainlayer.meta")]
        _LAYER_NAMES[unity_root] = catalogue
    return catalogue.get(guid, f"Layer{index}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--unity-root",
        type=Path,
        default=PROJECT_ROOT.parent / "Game",
        help="the Unity project directory",
    )
    arguments = parser.parse_args()
    unity_root = arguments.unity_root.resolve()

    placements = find_scene_placements(unity_root)
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)

    results: list[dict[str, Any]] = []
    skipped: list[dict[str, str]] = []
    for asset in sorted((unity_root / "Assets").rglob("*.asset")):
        if asset.read_bytes()[:5] == b"%YAML":
            continue
        try:
            serialized = SerializedFile(asset)
        except UnityFormatError:
            continue
        if not serialized.objects_of_class(CLASS_TERRAIN_DATA):
            continue
        guid = read_asset_guid(asset)
        if guid not in placements:
            # Unity leaves a TerrainData behind whenever a terrain is rebuilt.
            # Exporting orphans would put ground in the level that no scene ever
            # showed, so they are reported instead.
            skipped.append(
                {"source": asset.relative_to(unity_root).as_posix(), "reason": "no scene uses it"}
            )
            continue
        try:
            results.append(extract(asset, unity_root, placements[guid]))
        except (TerrainExtractError, UnityFormatError) as error:
            skipped.append(
                {"source": asset.relative_to(unity_root).as_posix(), "reason": str(error)}
            )

    report = {
        "schema": 1,
        "unityRoot": unity_root.as_posix(),
        "extracted": len(results),
        "skipped": len(skipped),
        "terrains": results,
        "notExtracted": skipped,
    }
    REPORT_PATH.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for terrain in results:
        landscape = terrain["landscape"]
        print(
            f"{terrain['name']}: {terrain['unity']['heightmapResolution']}^2 -> "
            f"{landscape['resolution']}^2 "
            f"({landscape['componentCount']}x{landscape['componentCount']} components, "
            f"{landscape['resampledSampleDelta']:+d} samples, full-extent resample), "
            f"{len(landscape['layers'])} layers, "
            f"{len(terrain['placements'])} placement(s)"
        )
    print(f"Unity terrains: extracted={len(results)} skipped={len(skipped)} report={REPORT_PATH}")
    return 0 if results else 2


if __name__ == "__main__":
    raise SystemExit(main())
