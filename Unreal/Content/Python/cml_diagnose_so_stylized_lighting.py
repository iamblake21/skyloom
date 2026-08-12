import json
import os
import unreal


MAP = "/Game/Maps/A_10_StarterIsland_AxisPreview"


def prop(obj, name):
    try:
        value = obj.get_editor_property(name)
        return value.get_path_name() if hasattr(value, "get_path_name") and value else str(value)
    except Exception as exc:
        return "ERROR: " + str(exc)


def main():
    level = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors_api = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    level.load_level(MAP)
    report = {"directionalLights": [], "postProcesses": [], "fog": [], "skyLights": []}
    for actor in actors_api.get_all_level_actors():
        base = {"label": actor.get_actor_label(), "class": actor.get_class().get_name(), "hidden": actor.is_hidden_ed()}
        for comp in actor.get_components_by_class(unreal.DirectionalLightComponent):
            rec = dict(base)
            rec.update({
                "component": comp.get_name(),
                "visible": comp.is_visible(),
                "hiddenInGame": prop(comp, "hidden_in_game"),
                "intensity": prop(comp, "intensity"),
                "color": prop(comp, "light_color"),
                "forwardPriority": prop(comp, "forward_shading_priority"),
                "atmosphereSun": prop(comp, "atmosphere_sun_light"),
                "atmosphereIndex": prop(comp, "atmosphere_sun_light_index"),
            })
            report["directionalLights"].append(rec)
        if "PostProcess" in actor.get_class().get_name():
            report["postProcesses"].append(base)
        if actor.get_components_by_class(unreal.ExponentialHeightFogComponent):
            report["fog"].append(base)
        if actor.get_components_by_class(unreal.SkyLightComponent):
            report["skyLights"].append(base)
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedLightingDiagnosis.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)


if __name__ == "__main__":
    main()
