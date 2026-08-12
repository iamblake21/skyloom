import json
import os
import unreal


ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment"


def path(obj):
    return obj.get_path_name() if obj else None


def main():
    report = {"meshes": {}, "grassType": {}, "landscapeMaterial": {}, "rvts": []}

    for mesh_name in ("SM_Grass1", "SM_Grass2"):
        mesh = unreal.load_asset(f"{ROOT}/Foliage/{mesh_name}")
        report["meshes"][mesh_name] = {
            "materials": [path(slot.material_interface) for slot in mesh.get_editor_property("static_materials")]
        }

    grass_type = unreal.load_asset(f"{ROOT}/Landscape/LG_Grass")
    varieties = grass_type.get_editor_property("grass_varieties")
    for index, variety in enumerate(varieties):
        report["grassType"][str(index)] = {
            "mesh": path(variety.get_editor_property("grass_mesh")),
            "density": str(variety.get_editor_property("grass_density")),
            "endCullDistance": str(variety.get_editor_property("end_cull_distance")),
        }

    material = unreal.load_asset(f"{ROOT}/Landscape/Materials/MI_LandscapeVol1")
    library = unreal.MaterialEditingLibrary
    for key, getter_names in {
        "scalars": ("get_scalar_parameter_names", "get_material_instance_scalar_parameter_value"),
        "vectors": ("get_vector_parameter_names", "get_material_instance_vector_parameter_value"),
        "textures": ("get_texture_parameter_names", "get_material_instance_texture_parameter_value"),
    }.items():
        names_getter = getattr(library, getter_names[0], None)
        value_getter = getattr(library, getter_names[1], None)
        values = {}
        if names_getter and value_getter:
            try:
                for name in names_getter(material):
                    try:
                        values[str(name)] = str(value_getter(material, name))
                    except Exception as exc:
                        values[str(name)] = "ERROR: " + str(exc)
            except Exception as exc:
                values["__error__"] = str(exc)
        report["landscapeMaterial"][key] = values

    level = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors_api = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    level.load_level("/Game/Maps/A_10_StarterIsland_AxisPreview")
    for actor in actors_api.get_all_level_actors():
        if "RuntimeVirtualTexture" in actor.get_class().get_name():
            report["rvts"].append({"label": actor.get_actor_label(), "class": actor.get_class().get_name()})

    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedGrassDiagnosis.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)


if __name__ == "__main__":
    main()
