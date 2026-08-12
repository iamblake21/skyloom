import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
REPORT_NAME = "AxisPreviewEnvironmentAudit.json"


def object_path(value):
    return value.get_path_name() if value else None


def safe_property(value, name):
    try:
        return value.get_editor_property(name)
    except Exception:
        return None


def main():
    if not unreal.EditorLoadingAndSavingUtils.load_map(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")

    actors = []
    for actor in unreal.EditorLevelLibrary.get_all_level_actors():
        record = {
            "label": actor.get_actor_label(),
            "name": actor.get_name(),
            "class": actor.get_class().get_name(),
            "path": actor.get_path_name(),
            "hidden_in_game": bool(safe_property(actor, "hidden")),
            "components": [],
        }

        landscape_material = safe_property(actor, "landscape_material")
        if landscape_material:
            record["landscape_material"] = object_path(landscape_material)

        for component in actor.get_components_by_class(unreal.ActorComponent):
            component_record = {
                "name": component.get_name(),
                "class": component.get_class().get_name(),
            }

            mesh = safe_property(component, "static_mesh")
            if mesh:
                component_record["static_mesh"] = object_path(mesh)

            materials = []
            try:
                for index in range(component.get_num_materials()):
                    materials.append(object_path(component.get_material(index)))
            except Exception:
                pass
            if materials:
                component_record["materials"] = materials

            record["components"].append(component_record)

        actors.append(record)

    report = {
        "map": MAP_PATH,
        "actor_count": len(actors),
        "actors": actors,
    }
    report_path = os.path.join(unreal.Paths.project_saved_dir(), REPORT_NAME)
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML AxisPreview Audit] Wrote {len(actors)} actors to {report_path}")


if __name__ == "__main__":
    main()
