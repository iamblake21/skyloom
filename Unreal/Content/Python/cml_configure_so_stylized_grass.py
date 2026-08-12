import json
import os
import unreal


MAP = "/Game/Maps/A_10_StarterIsland_AxisPreview"
ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment"
GRASS_TYPE = f"{ROOT}/Landscape/LG_Grass"
GRASS_MESH = f"{ROOT}/Foliage/SM_Grass2"
GRASS_MATERIALS = [
    f"{ROOT}/Foliage/Materials/MI_Grass_NoRVT",
    f"{ROOT}/Foliage/Materials/LODs/MI_Grass_NoRVT_LOD1",
    f"{ROOT}/Foliage/Materials/LODs/MI_Grass_NoRVT_LOD2",
]


def per_platform_float(value):
    result = unreal.PerPlatformFloat()
    result.set_editor_property("default", float(value))
    return result


def per_platform_int(value):
    result = unreal.PerPlatformInt()
    result.set_editor_property("default", int(value))
    return result


def interval(minimum, maximum):
    result = unreal.FloatInterval()
    result.set_editor_property("min", float(minimum))
    result.set_editor_property("max", float(maximum))
    return result


def main():
    grass_type = unreal.load_asset(GRASS_TYPE)
    mesh = unreal.load_asset(GRASS_MESH)
    materials = [unreal.load_asset(path) for path in GRASS_MATERIALS]
    if not isinstance(grass_type, unreal.LandscapeGrassType):
        raise RuntimeError(f"Missing official LandscapeGrassType: {GRASS_TYPE}")
    if not isinstance(mesh, unreal.StaticMesh):
        raise RuntimeError(f"Missing official volumetric grass mesh: {GRASS_MESH}")
    if not all(isinstance(material, unreal.MaterialInterface) for material in materials):
        raise RuntimeError("One or more official NoRVT grass materials are missing")

    variety = unreal.GrassVariety()
    variety.set_editor_property("grass_mesh", mesh)
    variety.set_editor_property("override_materials", materials)

    # Density and placement come from the official LG_Grass asset.  SM_Grass2,
    # uniform scale and cull distances come from the official FT_Grass_NoRVT
    # preset used by the Complete Vol.1 demonstration map.
    variety.set_editor_property("grass_density", per_platform_float(175.0))
    variety.set_editor_property("use_grid", True)
    variety.set_editor_property("placement_jitter", 1.0)
    variety.set_editor_property("start_cull_distance", per_platform_int(6000))
    variety.set_editor_property("end_cull_distance", per_platform_int(9000))
    variety.set_editor_property("scaling", unreal.GrassScaling.UNIFORM)
    variety.set_editor_property("scale_x", interval(0.9, 1.1))
    variety.set_editor_property("scale_y", interval(1.0, 1.0))
    variety.set_editor_property("scale_z", interval(1.0, 1.0))
    variety.set_editor_property("random_rotation", True)
    variety.set_editor_property("align_to_surface", True)

    grass_type.set_editor_property("grass_varieties", [variety])
    grass_type.set_editor_property("enable_density_scaling", True)
    unreal.EditorAssetLibrary.set_metadata_tag(
        grass_type,
        "CML.SoStylizedConfiguration",
        "SM_Grass2 + MI_Grass_NoRVT; values sourced from LG_Grass and FT_Grass_NoRVT",
    )
    if not unreal.EditorAssetLibrary.save_loaded_asset(grass_type, only_if_is_dirty=False):
        raise RuntimeError(f"Could not save {GRASS_TYPE}")

    level = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors_api = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level.load_level(MAP):
        raise RuntimeError(f"Could not load {MAP}")
    landscapes = [
        actor for actor in actors_api.get_all_level_actors()
        if isinstance(actor, unreal.Landscape) and actor.get_actor_label() == "TerrainTop"
    ]
    if len(landscapes) != 1:
        raise RuntimeError(f"Expected one TerrainTop landscape, found {len(landscapes)}")
    landscape = landscapes[0]
    unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(landscape)
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    unreal.SystemLibrary.execute_console_command(world, "grass.FlushCache")
    starts = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.PlayerStart)
    camera_locations = [landscape.get_actor_location()]
    if starts:
        camera_locations.append(starts[0].get_actor_location())
    if not unreal.CMLLandscapeImportLibrary.build_landscape_grass(landscape, camera_locations):
        raise RuntimeError("Could not synchronously rebuild Landscape Grass")
    if not level.save_current_level():
        raise RuntimeError(f"Could not save {MAP}")

    report = {
        "map": MAP,
        "layerDrivenBy": "LL_Grass through MI_LandscapeVol1 LandscapeGrassOutput",
        "grassType": GRASS_TYPE,
        "mesh": mesh.get_path_name(),
        "materials": [material.get_path_name() for material in materials],
        "density": 175.0,
        "uniformScale": [0.9, 1.1],
        "cullDistance": [6000, 9000],
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedGrassConfiguration.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)


if __name__ == "__main__":
    main()
