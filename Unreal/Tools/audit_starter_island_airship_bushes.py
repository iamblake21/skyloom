import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
REPORT_PATH = os.path.join(unreal.Paths.project_saved_dir(), "starter_island_airship_bush_audit.json")


def vector_dict(value):
    return {"x": value.x, "y": value.y, "z": value.z}


def rotator_dict(value):
    return {"pitch": value.pitch, "yaw": value.yaw, "roll": value.roll}


if unreal.EditorLevelLibrary.get_editor_world().get_path_name().split(":")[0] != MAP_PATH:
    unreal.EditorLoadingAndSavingUtils.load_map(MAP_PATH)

actors = unreal.EditorLevelLibrary.get_all_level_actors()
bushes = []
airships = []
sky_actors = []
directional_lights = []

for actor in actors:
    label = actor.get_actor_label()
    actor_path = actor.get_path_name()
    class_name = actor.get_class().get_name()
    components = actor.get_components_by_class(unreal.ActorComponent)
    mesh_paths = []
    component_records = []

    for component in components:
        component_class = component.get_class().get_name()
        if isinstance(component, unreal.StaticMeshComponent):
            mesh = component.get_editor_property("static_mesh")
            if mesh:
                mesh_path = mesh.get_path_name()
                mesh_paths.append(mesh_path)
                component_records.append({
                    "name": component.get_name(),
                    "class": component_class,
                    "mesh": mesh_path,
                    "relative_rotation": rotator_dict(component.get_editor_property("relative_rotation")),
                    "relative_location": vector_dict(component.get_editor_property("relative_location")),
                    "relative_scale": vector_dict(component.get_editor_property("relative_scale3d")),
                })
        elif "DirectionalLight" in component_class:
            directional_lights.append({
                "actor": label,
                "actor_class": class_name,
                "component": component.get_name(),
                "component_class": component_class,
                "rotation": rotator_dict(actor.get_actor_rotation()),
            })

    haystack = " ".join([label, actor_path, class_name] + mesh_paths).lower()
    base_record = {
        "label": label,
        "actor_path": actor_path,
        "class": class_name,
        "location": vector_dict(actor.get_actor_location()),
        "rotation": rotator_dict(actor.get_actor_rotation()),
        "scale": vector_dict(actor.get_actor_scale3d()),
        "components": component_records,
    }

    if any(keyword in haystack for keyword in ("bush", "shrub", "cesp", "cloudbush")):
        bushes.append(base_record)
    if "airship" in haystack or "aeronave" in haystack:
        airships.append(base_record)
    if any(keyword in haystack for keyword in ("stylizedsky", "sky", "sole", "sun")):
        sky_actors.append(base_record)

report = {
    "map": MAP_PATH,
    "actor_count": len(actors),
    "bush_count": len(bushes),
    "bushes": bushes,
    "airship_count": len(airships),
    "airships": airships,
    "sky_actor_count": len(sky_actors),
    "sky_actors": sky_actors,
    "directional_light_component_count": len(directional_lights),
    "directional_lights": directional_lights,
}

with open(REPORT_PATH, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)

unreal.log(f"CML audit written to {REPORT_PATH}")
