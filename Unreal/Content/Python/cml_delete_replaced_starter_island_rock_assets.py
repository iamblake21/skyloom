import json
import os

import unreal


SOURCE_ROOT = "/Game/Migrated/Project/Art/Environment/StarterIsland/Rocks"
SOURCE_STONE_MESH = (
    SOURCE_ROOT
    + "/Models/ENV_Rock_BoulderSmall_A/ENV_Rock_BoulderSmall_A"
)
TARGET_STONE_MESH = (
    "/Game/_Project/Art/Environment/SoStylized/Environment/Rocks/Classic/SM_RockClassic2"
)
OBSOLETE_MAPS = [
    "/Game/Maps/A_91_StarterIsland_Terrain_Review",
    "/Game/Migration/MapBackups/A_91_StarterIsland_Terrain_Review_BeforeReimport_01",
    "/Game/Migration/MapBackups/A_91_StarterIsland_Terrain_Review_BeforeReimport_02",
]
EXPECTED_GAMEPLAY_REFERENCER = "/Game/Migrated/Project/Resources/Items/BP_PF_Stone"


def main():
    target_mesh = unreal.EditorAssetLibrary.load_asset(TARGET_STONE_MESH)
    source_mesh = unreal.EditorAssetLibrary.load_asset(SOURCE_STONE_MESH)
    if not isinstance(target_mesh, unreal.StaticMesh):
        raise RuntimeError(f"Official replacement mesh is missing: {TARGET_STONE_MESH}")
    if not isinstance(source_mesh, unreal.StaticMesh):
        raise RuntimeError(f"Expected old stone mesh is missing: {SOURCE_STONE_MESH}")

    source_referencers = unreal.EditorAssetLibrary.find_package_referencers_for_asset(
        SOURCE_STONE_MESH,
        load_assets_to_confirm=True,
    )
    if EXPECTED_GAMEPLAY_REFERENCER not in source_referencers:
        raise RuntimeError(
            f"Expected {EXPECTED_GAMEPLAY_REFERENCER} to reference {SOURCE_STONE_MESH}; "
            f"found {source_referencers}"
        )

    # Redirect the functional stone pickup to the official small Classic rock
    # before removing the obsolete environment assets.
    if not unreal.EditorAssetLibrary.consolidate_assets(target_mesh, [source_mesh]):
        raise RuntimeError("Could not consolidate the gameplay stone onto the Classic mesh")

    deleted_maps = []
    for map_path in OBSOLETE_MAPS:
        if unreal.EditorAssetLibrary.does_asset_exist(map_path):
            if not unreal.EditorAssetLibrary.delete_asset(map_path):
                raise RuntimeError(f"Could not delete obsolete map: {map_path}")
            deleted_maps.append(map_path)

    remaining_assets = unreal.EditorAssetLibrary.list_assets(
        SOURCE_ROOT,
        recursive=True,
        include_folder=False,
    )
    external = {}
    for asset in remaining_assets:
        referencers = unreal.EditorAssetLibrary.find_package_referencers_for_asset(
            asset,
            load_assets_to_confirm=True,
        )
        outside = sorted(reference for reference in referencers if not reference.startswith(SOURCE_ROOT))
        if outside:
            external[asset] = outside
    if external:
        raise RuntimeError(f"Old rock assets still have external referencers: {external}")

    if not unreal.EditorAssetLibrary.delete_directory(SOURCE_ROOT):
        raise RuntimeError(f"Could not delete replaced rock directory: {SOURCE_ROOT}")
    if unreal.EditorAssetLibrary.does_directory_exist(SOURCE_ROOT):
        raise RuntimeError(f"Replaced rock directory still exists: {SOURCE_ROOT}")

    target_referencers = unreal.EditorAssetLibrary.find_package_referencers_for_asset(
        TARGET_STONE_MESH,
        load_assets_to_confirm=True,
    )
    if EXPECTED_GAMEPLAY_REFERENCER not in target_referencers:
        raise RuntimeError("Gameplay stone did not retain its reference after consolidation")

    report = {
        "deletedDirectory": SOURCE_ROOT,
        "deletedAssetCount": len(remaining_assets),
        "deletedObsoleteMaps": deleted_maps,
        "gameplayStone": {
            "asset": EXPECTED_GAMEPLAY_REFERENCER,
            "previousMesh": SOURCE_STONE_MESH,
            "currentMesh": TARGET_STONE_MESH,
        },
        "sourceDirectoryStillExists": False,
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "DeletedReplacedRockAssets.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Deleted Replaced Rock Assets] Wrote {output}")


if __name__ == "__main__":
    main()
