"""Read-only-style lighting probe at civil midnight, then restore noon."""

from __future__ import annotations

import json
import os
import traceback

import unreal


def _value(obj, name):
    try:
        return str(obj.get_editor_property(name))
    except Exception as error:
        return f"ERROR: {error}"


def _snapshot(sky, hour):
    sky.call_method(
        "Set New Time ClockBased",
        args=(float(hour), 0.0, 0.0, 24.0, 60.0, 60.0),
    )
    components = sky.get_components_by_class(unreal.DirectionalLightComponent)
    if len(components) != 1:
        raise RuntimeError(f"Expected one directional light, found {len(components)}")
    light = components[0]
    return {
        "requested_civil_hour": hour,
        "reported_clock": list(sky.call_method("Get Clock Time")),
        "directional_light": {
            "intensity": _value(light, "intensity"),
            "color": _value(light, "light_color"),
            "rotation": str(light.get_world_rotation()),
            "atmosphere_sun_light": _value(light, "atmosphere_sun_light"),
        },
        "sky_properties": {
            name: _value(sky, name)
            for name in (
                "Current Time of Day",
                "Day Cycle Progress",
                "Day Length",
                "Night Length",
                "Night Brightness",
                "Sun Brightness Multiplier",
                "Moon Brightness Multiplier",
                "Night Skylight Intensity",
                "Sunrise Skylight Intensity",
            )
        },
    }


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    world = unreal.get_editor_subsystem(
        unreal.UnrealEditorSubsystem).get_editor_world()
    skies = [
        actor
        for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
        if "BP_StylizedSky" in actor.get_class().get_name()
    ]
    if len(skies) != 1:
        raise RuntimeError(f"Expected one So Stylized sky, found {len(skies)}")
    sky = skies[0]
    report = {
        "map": world.get_path_name(),
        "midnight": _snapshot(sky, 0),
        "noon": _snapshot(sky, 12),
    }
    level_editor.save_current_level()
    output = os.path.join(
        unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_saved_dir()),
        "MidnightLightingProbe.json",
    )
    with open(output, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, ensure_ascii=False)
    unreal.log(f"[CML Midnight Probe] wrote {output}")
    unreal.log("CML_MIDNIGHT_PROBE_SUCCEEDED")


try:
    main()
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.log_error("CML_MIDNIGHT_PROBE_FAILED")
