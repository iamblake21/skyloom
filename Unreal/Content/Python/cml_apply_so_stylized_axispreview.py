import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment"
LANDSCAPE_MATERIAL = ROOT + "/Landscape/Materials/MI_LandscapeVol1"
CLIFF_MATERIAL = ROOT + "/Rocks/Materials/Classic/MI_RockClassic_Cliff"
SHELF_MATERIAL = ROOT + "/Rocks/Materials/Classic/MI_RockClassic_Shelves"
WATER_MATERIAL = ROOT + "/Water/Materials/Presets/Classic/MI_Water_Classic"
SKY_BLUEPRINT = ROOT + "/Sky/PRESETS/BP_StylizedSky_Classic"

OLD_ENV_LABELS = {
    "ENV_Sun",
    "CML_HeightFog",
    "CML_SkyAtmosphere",
    "CML_SkyLight",
}


def load_typed(path, expected):
    asset = unreal.EditorAssetLibrary.load_asset(path)
    if not isinstance(asset, expected):
        raise RuntimeError(f"Required asset is missing or has the wrong type: {path}")
    return asset


def component_mesh_path(component):
    try:
        mesh = component.get_editor_property("static_mesh")
    except Exception:
        return ""
    return mesh.get_path_name() if mesh else ""


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load target map: {MAP_PATH}")

    landscape_material = load_typed(LANDSCAPE_MATERIAL, unreal.MaterialInterface)
    cliff_material = load_typed(CLIFF_MATERIAL, unreal.MaterialInterface)
    shelf_material = load_typed(SHELF_MATERIAL, unreal.MaterialInterface)
    water_material = load_typed(WATER_MATERIAL, unreal.MaterialInterface)
    sky_class = unreal.EditorAssetLibrary.load_blueprint_class(SKY_BLUEPRINT)
    if not sky_class:
        raise RuntimeError(f"Could not load sky Blueprint class: {SKY_BLUEPRINT}")

    actors = list(actor_subsystem.get_all_level_actors())
    report = {
        "map": MAP_PATH,
        "landscape": {},
        "rocks": {"components": 0, "cliffs": 0, "shelves": 0},
        "water": {},
        "sky": {"removed": []},
    }

    landscapes = [a for a in actors if isinstance(a, unreal.Landscape) and a.get_actor_label() == "TerrainTop"]
    if len(landscapes) != 1:
        raise RuntimeError(f"Expected exactly one TerrainTop Landscape, found {len(landscapes)}")
    landscape = landscapes[0]
    previous_landscape_material = landscape.get_editor_property("landscape_material")
    landscape.set_editor_property("landscape_material", landscape_material)
    if not unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(landscape):
        raise RuntimeError("Could not rebuild Landscape component material instances")
    report["landscape"] = {
        "actor": landscape.get_actor_label(),
        "previous": previous_landscape_material.get_path_name() if previous_landscape_material else None,
        "current": landscape_material.get_path_name(),
    }

    for actor in actors:
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            mesh_path = component_mesh_path(component)
            mesh_name = mesh_path.rsplit("/", 1)[-1]
            selected = None
            kind = None
            if "REF_SM_CliffClassic" in mesh_name:
                selected = cliff_material
                kind = "cliffs"
            elif "REF_SM_ShelfClassic" in mesh_name:
                selected = shelf_material
                kind = "shelves"
            if selected:
                slot_count = max(1, component.get_num_materials())
                for slot in range(slot_count):
                    component.set_material(slot, selected)
                component.modify()
                report["rocks"]["components"] += 1
                report["rocks"][kind] += 1

    if report["rocks"]["components"] != 24:
        raise RuntimeError(f"Expected 24 Cliff/Shelf components, changed {report['rocks']['components']}")

    water_actors = [a for a in actors if a.get_actor_label() == "ENV_Water_Pond_Main"]
    if len(water_actors) != 1:
        raise RuntimeError(f"Expected one ENV_Water_Pond_Main actor, found {len(water_actors)}")
    water_actor = water_actors[0]
    water_components = water_actor.get_components_by_class(unreal.StaticMeshComponent)
    if len(water_components) != 1:
        raise RuntimeError(f"Expected one water mesh component, found {len(water_components)}")
    water_component = water_components[0]
    previous_water = water_component.get_material(0)
    for slot in range(max(1, water_component.get_num_materials())):
        water_component.set_material(slot, water_material)
    water_component.modify()
    water_actor.set_actor_label("ENV_SoStylized_Water_Pond")
    report["water"] = {
        "actor": water_actor.get_actor_label(),
        "mesh": component_mesh_path(water_component),
        "previous": previous_water.get_path_name() if previous_water else None,
        "current": water_material.get_path_name(),
    }

    for actor in list(actor_subsystem.get_all_level_actors()):
        if actor.get_actor_label() in OLD_ENV_LABELS:
            report["sky"]["removed"].append({"label": actor.get_actor_label(), "class": actor.get_class().get_name()})
            if not actor_subsystem.destroy_actor(actor):
                raise RuntimeError(f"Could not remove old environment actor: {actor.get_actor_label()}")

    if len(report["sky"]["removed"]) != 4:
        raise RuntimeError(f"Expected four old environment actors, removed {len(report['sky']['removed'])}")

    for actor in actor_subsystem.get_all_level_actors():
        if actor.get_actor_label() == "ENV_SoStylized_Sky_Classic":
            actor_subsystem.destroy_actor(actor)
    sky_actor = actor_subsystem.spawn_actor_from_class(sky_class, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, 0))
    if not sky_actor:
        raise RuntimeError("Could not spawn BP_StylizedSky_Classic")
    sky_actor.set_actor_label("ENV_SoStylized_Sky_Classic")
    try:
        sky_actor.set_folder_path("Environment/SoStylized")
        water_actor.set_folder_path("Environment/SoStylized")
    except Exception:
        pass
    report["sky"]["spawned"] = {
        "label": sky_actor.get_actor_label(),
        "class": sky_actor.get_class().get_name(),
        "asset": SKY_BLUEPRINT,
    }

    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save target map: {MAP_PATH}")

    output = os.path.join(unreal.Paths.project_saved_dir(), "AxisPreviewSoStylizedApplyReport.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Apply] Wrote {output}")


if __name__ == "__main__":
    main()
