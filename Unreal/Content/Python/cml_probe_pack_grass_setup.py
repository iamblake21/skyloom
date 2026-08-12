import json
import os
import unreal


ROOT = "/Game/_Project/Art/Environment/SoStylized"


def asset_path(obj):
    return obj.get_path_name() if obj else None


def value(obj, name):
    try:
        return str(obj.get_editor_property(name))
    except Exception as exc:
        return "ERROR: " + str(exc)


def asset_record(path):
    obj = unreal.load_asset(path)
    return {
        "path": asset_path(obj),
        "class": obj.get_class().get_name() if obj else None,
        "export": obj.export_text() if obj and hasattr(obj, "export_text") else None,
    }


def main():
    report = {"assets": {}, "demo": {"actors": [], "instances": []}}
    asset_paths = {
        "LG_Grass": f"{ROOT}/Environment/Landscape/LG_Grass",
        "FT_Grass": f"{ROOT}/Environment/FoliageTypes/FT_Grass",
        "FT_Grass_NoRVT": f"{ROOT}/Environment/FoliageTypes/FT_Grass_NoRVT",
        "SM_Grass1": f"{ROOT}/Environment/Foliage/SM_Grass1",
        "SM_Grass2": f"{ROOT}/Environment/Foliage/SM_Grass2",
        "MI_Grass": f"{ROOT}/Environment/Foliage/Materials/MI_Grass",
        "MI_Grass_NoRVT": f"{ROOT}/Environment/Foliage/Materials/MI_Grass_NoRVT",
    }
    for name, path in asset_paths.items():
        report["assets"][name] = asset_record(path)

    grass_type = unreal.load_asset(asset_paths["LG_Grass"])
    report["assets"]["LG_Grass"]["varieties"] = []
    for variety in grass_type.get_editor_property("grass_varieties"):
        rec = {}
        for name in (
            "grass_mesh", "grass_density", "use_grid", "placement_jitter",
            "start_cull_distance", "end_cull_distance", "min_lod", "scaling",
            "scale_x", "scale_y", "scale_z", "random_rotation", "align_to_surface",
        ):
            rec[name] = value(variety, name)
        report["assets"]["LG_Grass"]["varieties"].append(rec)

    for name in ("FT_Grass", "FT_Grass_NoRVT"):
        foliage = unreal.load_asset(asset_paths[name])
        props = {}
        for prop in (
            "mesh", "density", "radius", "scaling", "scale_x", "scale_y", "scale_z",
            "align_to_normal", "random_yaw", "ground_slope_angle", "cull_distance",
        ):
            props[prop] = value(foliage, prop)
        report["assets"][name]["properties"] = props

    for name in ("SM_Grass1", "SM_Grass2"):
        mesh = unreal.load_asset(asset_paths[name])
        report["assets"][name]["materials"] = [
            asset_path(slot.material_interface) for slot in mesh.get_editor_property("static_materials")
        ]
        try:
            report["assets"][name]["bounds"] = str(mesh.get_bounding_box())
        except Exception as exc:
            report["assets"][name]["bounds"] = "ERROR: " + str(exc)

    level = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors_api = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    level.load_level(f"{ROOT}/Maps/CompleteVol1/Demonstration_Vol1")
    for actor in actors_api.get_all_level_actors():
        cls = actor.get_class().get_name()
        if any(token in cls for token in ("Landscape", "Foliage", "RuntimeVirtualTexture")):
            report["demo"]["actors"].append({"label": actor.get_actor_label(), "class": cls})
        for comp in actor.get_components_by_class(unreal.HierarchicalInstancedStaticMeshComponent):
            mesh = comp.get_editor_property("static_mesh")
            if mesh and ("Grass" in mesh.get_name() or "grass" in mesh.get_name()):
                report["demo"]["instances"].append({
                    "actor": actor.get_actor_label(),
                    "component": comp.get_name(),
                    "mesh": asset_path(mesh),
                    "count": comp.get_instance_count(),
                    "materials": [asset_path(comp.get_material(i)) for i in range(comp.get_num_materials())],
                })

    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedPackGrassSetup.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)


if __name__ == "__main__":
    main()
