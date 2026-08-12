import json
import os

import unreal


BLUEPRINT_PATH = "/Game/Migrated/Project/Resources/Items/BP_PF_Stone"


def path_of(obj):
    return obj.get_path_name() if obj else None


def component_record(component):
    mesh = component.get_editor_property("static_mesh")
    return {
        "name": component.get_name(),
        "path": component.get_path_name(),
        "mesh": path_of(mesh),
        "materials": [path_of(component.get_material(i)) for i in range(component.get_num_materials())],
    }


def main():
    blueprint = unreal.EditorAssetLibrary.load_asset(BLUEPRINT_PATH)
    generated_class = unreal.EditorAssetLibrary.load_blueprint_class(BLUEPRINT_PATH)
    default_object = unreal.get_default_object(generated_class) if generated_class else None
    report = {
        "blueprint": path_of(blueprint),
        "class": path_of(generated_class),
        "defaultObject": path_of(default_object),
        "defaultComponents": [],
        "constructionScript": None,
        "constructionNodes": [],
        "subobjectComponents": [],
        "errors": [],
    }
    try:
        subsystem = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
        handles = subsystem.k2_gather_subobject_data_for_blueprint(blueprint)
        for handle in handles:
            data = subsystem.k2_find_subobject_data_from_handle(handle)
            obj = unreal.SubobjectDataBlueprintFunctionLibrary.get_object(data)
            if isinstance(obj, unreal.StaticMeshComponent):
                report["subobjectComponents"].append(component_record(obj))
    except Exception as exception:
        report["errors"].append(f"subobject data: {exception}")
    if default_object:
        try:
            report["defaultComponents"] = [
                component_record(component)
                for component in default_object.get_components_by_class(unreal.StaticMeshComponent)
            ]
        except Exception as exception:
            report["errors"].append(f"default components: {exception}")
    try:
        construction_script = blueprint.get_editor_property("simple_construction_script")
        report["constructionScript"] = path_of(construction_script)
        for node in construction_script.get_all_nodes():
            template = node.get_editor_property("component_template")
            record = {"node": node.get_name(), "template": path_of(template)}
            if isinstance(template, unreal.StaticMeshComponent):
                record["component"] = component_record(template)
            report["constructionNodes"].append(record)
    except Exception as exception:
        report["errors"].append(f"construction script: {exception}")

    output = os.path.join(unreal.Paths.project_saved_dir(), "StoneBlueprintProbe.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Stone Blueprint Probe] Wrote {output}")


if __name__ == "__main__":
    main()
