import hashlib
import json
import os
from pathlib import Path

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
LANDSCAPE_LABEL = "TerrainTop"
PACK_ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment/Landscape"
LAYER_PATHS = {
    "Grass": PACK_ROOT + "/LL_Grass",
    "Dirt": PACK_ROOT + "/LL_Dirt",
    "Rock": PACK_ROOT + "/LL_Rock",
    "Sand": PACK_ROOT + "/LL_Sand",
}


def load_layer(path):
    layer = unreal.EditorAssetLibrary.load_asset(path)
    if not isinstance(layer, unreal.LandscapeLayerInfoObject):
        raise RuntimeError(f"Official So Stylized Landscape layer is missing: {path}")
    return layer


def file_record(path):
    data = path.read_bytes()
    return {
        "file": str(path),
        "bytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "nonZeroSamples": sum(value != 0 for value in data),
        "fullWeightSamples": sum(value == 255 for value in data),
        "min": min(data),
        "max": max(data),
    }


def main():
    project_dir = Path(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))
    source_dir = project_dir / "Migration" / "UnityTerrain" / "TD_StarterIsland"
    source_files = {
        "GrassSun": source_dir / "weight_0_TL_StarterIsland_GrassSun.r8",
        "GrassDeep": source_dir / "weight_1_TL_StarterIsland_GrassDeep.r8",
        "DirtPath": source_dir / "weight_2_TL_StarterIsland_DirtPath.r8",
        "Rock": source_dir / "weight_3_TL_StarterIsland_CliffPeach_ReferenceMatch_v1.r8",
    }
    for path in source_files.values():
        if not path.is_file():
            raise RuntimeError(f"Original TerrainData weightmap is missing: {path}")

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")
    landscapes = [
        actor for actor in actor_subsystem.get_all_level_actors()
        if isinstance(actor, unreal.Landscape) and actor.get_actor_label() == LANDSCAPE_LABEL
    ]
    if len(landscapes) != 1:
        raise RuntimeError(f"Expected exactly one {LANDSCAPE_LABEL} Landscape, found {len(landscapes)}")
    landscape = landscapes[0]
    material = landscape.get_editor_property("landscape_material")
    if not material or "/SoStylized/Environment/Landscape/" not in material.get_path_name():
        raise RuntimeError(f"Landscape does not use the official So Stylized material: {material}")

    layers = {name: load_layer(path) for name, path in LAYER_PATHS.items()}
    mappings = {
        "Grass": [source_files["GrassSun"], source_files["GrassDeep"]],
        "Dirt": [source_files["DirtPath"]],
        "Rock": [source_files["Rock"]],
    }
    for target_name, files in mappings.items():
        if not unreal.CMLLandscapeImportLibrary.import_landscape_layer_from_raw_files(
            landscape,
            layers[target_name],
            [str(path) for path in files],
        ):
            raise RuntimeError(f"Could not import and verify official layer {target_name}")

    # Sand is intentionally empty on the authored island, but registering the
    # official LayerInfo makes it immediately available in Landscape Paint.
    if not unreal.CMLLandscapeImportLibrary.fill_landscape_layer(landscape, layers["Sand"], 0):
        raise RuntimeError("Could not register the official Sand paint layer")

    if not unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(landscape):
        raise RuntimeError("Could not refresh Landscape material instances")

    camera_locations = []
    for actor in actor_subsystem.get_all_level_actors():
        if isinstance(actor, (unreal.PlayerStart, unreal.CameraActor)):
            camera_locations.append(actor.get_actor_location())
    if not camera_locations:
        camera_locations = [landscape.get_actor_location()]
    if not unreal.CMLLandscapeImportLibrary.build_landscape_grass(landscape, camera_locations):
        raise RuntimeError("Could not rebuild Landscape grass after restoring paint weights")

    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save {MAP_PATH}")

    report = {
        "map": MAP_PATH,
        "landscape": LANDSCAPE_LABEL,
        "material": material.get_path_name(),
        "resolution": 1017,
        "paintableLayers": {name: layer.get_path_name() for name, layer in layers.items()},
        "restoredMappings": {
            name: [str(path) for path in files] for name, files in mappings.items()
        },
        "sourceWeightmaps": {name: file_record(path) for name, path in source_files.items()},
        "sandInitialWeight": 0,
        "grassRebuiltAtLocations": [
            {"x": float(location.x), "y": float(location.y), "z": float(location.z)}
            for location in camera_locations
        ],
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedLandscapeLayers.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Landscape Layers] Wrote {output}")


if __name__ == "__main__":
    main()
