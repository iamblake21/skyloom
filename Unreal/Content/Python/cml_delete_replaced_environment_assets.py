import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
SO_ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment"
CLIFF_MATERIAL = SO_ROOT + "/Rocks/Materials/Classic/MI_RockClassic_Cliff"
SHELF_MATERIAL = SO_ROOT + "/Rocks/Materials/Classic/MI_RockClassic_Shelves"

# Visual assets superseded on AxisPreview. Geometry, trees, minor rocks, the
# portal and the terrain underbody are deliberately not part of this list.
DELETE_ASSETS = [
    "/Game/Migration/LandscapeGrass/LGT_CML_StarterIslandGrass",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_CliffPeach_ReferenceMatch_v1",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_DirtPath",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_GrassDeep",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_GrassSun",
    "/Game/Migration/LandscapeMaterials/MI_TD_StarterIsland",
    "/Game/Migration/LandscapeMaterials/MI_TerrainData_c7381312_f45e_4991_a939_1f289b31c874",
    "/Game/Migration/LandscapeTextures/T_TD_StarterIsland_Control",
    "/Game/Migration/LandscapeTextures/T_TerrainData_c7381312_f45e_4991_a939_1f289b31c874_Control",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/ReferenceMatch/M_StarterIsland_Terrain_ReferenceMatch_v1",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/ReferenceMatch/M_StarterIsland_Terrain_ReferenceMatch_v2_OriginalShader",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/ReferenceMatch/M_StarterIsland_Terrain_ReferenceMatch_v3_MainIsland",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/ReferenceMatch/Preview_StarterIsland_CliffPeach_ReferenceMatch_v1",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/ReferenceMatch/T_StarterIsland_CliffPeach_ReferenceMatch_v1",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Textures/T_StarterIsland_DirtPath",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Textures/T_StarterIsland_DirtPath_Normal",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Textures/T_StarterIsland_GrassDeep",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Textures/T_StarterIsland_GrassDeep_Normal",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Textures/T_StarterIsland_GrassDetailColor",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Textures/T_StarterIsland_GrassSun",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Textures/T_StarterIsland_GrassSun_Normal",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/M_StarterIsland_Skybox",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/M_StarterIsland_Terrain",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/M_StarterIsland_TerrainWater",
    "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit/Materials/M_OriginalCliffMass",
    "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit/Materials/M_OriginalCliffMass_NoGrass",
    "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit/Textures/T_OriginalCliff_Albedo_Procedural_v2",
    "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit/Textures/T_OriginalCliff_Albedo_v1",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/CliffRockKit/Materials/M_StarterIsland_CliffRockKit",
    "/Game/Migration/Masters/M_CML_Env_AtmosphericSky",
    "/Game/Migration/Masters/M_CML_Env_OriginalCliffMass",
    "/Game/Migration/Masters/M_CML_Env_StylizedWater",
    "/Game/Migration/Masters/M_CML_Env_TerrainReferenceMatch",
    "/Game/Migration/Masters/M_CML_Env_TerrainSplat",
]


def update_cliff_mesh_defaults(cliff_material, shelf_material):
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    assets = registry.get_assets_by_path(
        "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit", recursive=True
    )
    changed = []
    for data in assets:
        asset = unreal.EditorAssetLibrary.load_asset(str(data.package_name))
        if not isinstance(asset, unreal.StaticMesh):
            continue
        path = asset.get_path_name()
        selected = shelf_material if "Shelf" in path else cliff_material
        slot_count = len(asset.get_editor_property("static_materials") or [])
        local_changed = 0
        for slot in range(slot_count):
            current = asset.get_material(slot)
            if current and "M_OriginalCliffMass" in current.get_path_name():
                asset.set_material(slot, selected)
                local_changed += 1
        if local_changed:
            if not unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=False):
                raise RuntimeError(f"Could not save corrected mesh defaults: {path}")
            changed.append({"asset": path, "slots": local_changed, "material": selected.get_path_name()})
    return changed


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load target map: {MAP_PATH}")
    cliff_material = unreal.EditorAssetLibrary.load_asset(CLIFF_MATERIAL)
    shelf_material = unreal.EditorAssetLibrary.load_asset(SHELF_MATERIAL)
    if not isinstance(cliff_material, unreal.MaterialInterface) or not isinstance(shelf_material, unreal.MaterialInterface):
        raise RuntimeError("Official SoStylized Cliff/Shelf materials are unavailable")

    report = {
        "map": MAP_PATH,
        "meshDefaultsUpdated": update_cliff_mesh_defaults(cliff_material, shelf_material),
        "deleted": [],
        "missingBeforeDeletion": [],
        "failed": [],
    }

    # Delete leaf instances/textures first, then their old masters.
    for path in DELETE_ASSETS:
        if not unreal.EditorAssetLibrary.does_asset_exist(path):
            report["missingBeforeDeletion"].append(path)
            continue
        if unreal.EditorAssetLibrary.delete_asset(path):
            report["deleted"].append(path)
        else:
            report["failed"].append(path)

    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True)
    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save cleaned target map: {MAP_PATH}")

    report["stillExists"] = [
        path for path in DELETE_ASSETS if unreal.EditorAssetLibrary.does_asset_exist(path)
    ]
    output = os.path.join(unreal.Paths.project_saved_dir(), "ReplacedEnvironmentDeletionReport.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Replaced Environment Delete] Wrote {output}")
    if report["failed"] or report["stillExists"]:
        raise RuntimeError(
            f"Old environment deletion incomplete: failed={len(report['failed'])} stillExists={len(report['stillExists'])}"
        )


if __name__ == "__main__":
    main()
