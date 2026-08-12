"""Record the live PIE clock reported by the So Stylized sky."""

import json
import os
import traceback

import unreal


def main():
    editor = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
    world = editor.get_game_world()
    if world is None:
        raise RuntimeError("No PIE game world is active")

    skies = []
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor):
        if "BP_StylizedSky" in actor.get_class().get_name():
            skies.append(actor)
    if len(skies) != 1:
        raise RuntimeError(f"Expected one PIE sky, found {len(skies)}")

    sky = skies[0]
    subsystem_report = {}
    try:
        subsystem = unreal.SubsystemBlueprintLibrary.get_world_subsystem(
            world, unreal.CMLDayNightSubsystem)
        subsystem_report = {
            "available": subsystem is not None,
            "class": subsystem.get_class().get_name() if subsystem else None,
            "time": subsystem.get_time_of_day_hours() if subsystem else None,
            "clock_running": subsystem.is_clock_running() if subsystem else None,
        }
    except Exception as exc:
        subsystem_report = {"available": False, "error": str(exc)}

    report = {
        "world": world.get_path_name(),
        "sky": sky.get_path_name(),
        "so_stylized_clock": list(sky.call_method("Get Clock Time")),
        "cml_subsystem": subsystem_report,
        "subsystem_helpers": sorted(
            name for name in dir(unreal)
            if "subsystem" in name.lower() and "world" in name.lower()
        ),
    }
    output = os.path.join(
        unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_saved_dir()),
        "pie_daynight_probe.json",
    )
    with open(output, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
    unreal.log(f"[CML PIE DayNight Probe] wrote {output}")


try:
    main()
except Exception:
    unreal.log_error(traceback.format_exc())
