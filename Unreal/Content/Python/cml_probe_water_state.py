import json
import os
import unreal


def vec(value):
    return [value.x, value.y, value.z]


def main():
    level = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors_api = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    level.load_level("/Game/Maps/A_10_StarterIsland_AxisPreview")
    actor = next(a for a in actors_api.get_all_level_actors() if a.get_actor_label() == "ENV_SoStylized_Water_Pond")
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    location = actor.get_actor_location()
    hit = unreal.SystemLibrary.line_trace_single(
        world,
        unreal.Vector(location.x, location.y, 100000.0),
        unreal.Vector(location.x, location.y, -100000.0),
        unreal.TraceTypeQuery.TRACE_TYPE_QUERY1,
        True,
        [actor],
        unreal.DrawDebugTrace.NONE,
        True,
        unreal.LinearColor.RED,
        unreal.LinearColor.GREEN,
        0.0,
    )
    report = {
        "actorLocation": vec(location),
        "verticalTrace": str(hit),
        "hitDir": dir(hit),
        "hitTuple": str(hit.to_tuple()),
        "hitExport": hit.export_text(),
        "traceFields": {},
        "components": [],
    }
    for field in ("blocking_hit", "impact_point", "location", "trace_start", "trace_end", "distance"):
        try:
            value = hit.get_editor_property(field)
            report["traceFields"][field] = vec(value) if isinstance(value, unreal.Vector) else str(value)
        except Exception as exc:
            report["traceFields"][field] = "ERROR: " + str(exc)
    for comp in actor.get_components_by_class(unreal.StaticMeshComponent):
        rec = {
            "name": comp.get_name(),
            "class": comp.get_class().get_name(),
            "visible": comp.is_visible(),
            "hiddenInGame": comp.get_editor_property("hidden_in_game"),
            "mesh": comp.get_editor_property("static_mesh").get_path_name() if comp.get_editor_property("static_mesh") else None,
            "materials": [comp.get_material(i).get_path_name() if comp.get_material(i) else None for i in range(comp.get_num_materials())],
        }
        if isinstance(comp, unreal.InstancedStaticMeshComponent):
            rec["instances"] = comp.get_instance_count()
            transforms = []
            for index in range(comp.get_instance_count()):
                transform = comp.get_instance_transform(index, world_space=False)
                transforms.append({"translation": vec(transform.translation), "scale": vec(transform.scale3d)})
            rec["instanceTransforms"] = transforms
        report["components"].append(rec)
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedWaterState.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)


if __name__ == "__main__":
    main()
