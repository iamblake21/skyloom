import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
REPORT_PATH = os.path.join(unreal.Paths.project_saved_dir(), "time_of_day_runtime_audit.json")
KEYWORDS = ("time", "hour", "day", "night", "sun", "clock")

level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
if unreal.EditorLevelLibrary.get_editor_world().get_path_name().split(":")[0] != MAP_PATH:
    level_editor.load_level(MAP_PATH)

candidate_actors = []
sky_report = None
for actor in actor_subsystem.get_all_level_actors():
    label = actor.get_actor_label()
    class_name = actor.get_class().get_name()
    haystack = f"{label} {class_name}".lower()
    if any(keyword in haystack for keyword in KEYWORDS):
        candidate_actors.append({"label": label, "class": class_name})

    if class_name == "BP_StylizedSky_Classic_C":
        reflected_names = sorted({
            name for name in dir(actor)
            if any(keyword in name.lower() for keyword in KEYWORDS)
        })
        components = []
        for component in actor.get_components_by_class(unreal.ActorComponent):
            component_class = component.get_class().get_name()
            if "DirectionalLight" in component_class:
                mobility = None
                try:
                    mobility = str(component.get_editor_property("mobility"))
                except Exception:
                    pass
                components.append({
                    "name": component.get_name(),
                    "class": component_class,
                    "mobility": mobility,
                    "world_rotation": [
                        component.get_world_rotation().pitch,
                        component.get_world_rotation().yaw,
                        component.get_world_rotation().roll,
                    ],
                })
        sky_report = {
            "label": label,
            "class": class_name,
            "tick_enabled": actor.is_actor_tick_enabled(),
            "tick_interval": actor.get_actor_tick_interval(),
            "reflected_time_names": reflected_names,
            "directional_lights": components,
        }

report = {
    "map": MAP_PATH,
    "candidate_time_actors": candidate_actors,
    "sky": sky_report,
}
with open(REPORT_PATH, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
unreal.log(f"CML time-of-day audit written to {REPORT_PATH}")
