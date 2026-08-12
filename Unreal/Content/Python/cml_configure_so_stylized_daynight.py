"""Persist the migrated CML day/night contract on the Starter Island sky."""

from __future__ import annotations

import json
import os
import traceback

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"


def _find_sky(world):
    matches = [
        actor
        for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
        if "BP_StylizedSky" in actor.get_class().get_name()
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one So Stylized sky, found {len(matches)}")
    return matches[0]


def _set_first_property(actor, names, value):
    errors = []
    for name in names:
        try:
            before = actor.get_editor_property(name)
            actor.set_editor_property(name, value)
            after = actor.get_editor_property(name)
            return {"property": name, "before": before, "after": after}
        except Exception as error:
            errors.append(f"{name}: {error}")
    raise RuntimeError("; ".join(errors))


def _set_optional_property(actor, names, value):
    try:
        return _set_first_property(actor, names, value)
    except Exception as error:
        # These inherited Blueprint booleans are runtime-reflectable in C++
        # but not exposed through Unreal's Python editor-property bridge.
        return {"available_in_python": False, "runtime_value": value, "error": str(error)}


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")

    world = unreal.get_editor_subsystem(
        unreal.UnrealEditorSubsystem).get_editor_world()
    sky = _find_sky(world)
    changes = {
        "day_cycle_enabled": _set_optional_property(
            sky,
            ("Day Cycle Enabled?", "Day Cycle Enabled", "day_cycle_enabled"),
            True,
        ),
        "freeze_all_time": _set_optional_property(
            sky,
            ("Freeze All Time?", "Freeze All Time", "freeze_all_time"),
            False,
        ),
        "day_length_seconds": _set_first_property(
            sky,
            ("Day Length", "day_length"),
            600.0,
        ),
        "night_length_seconds": _set_first_property(
            sky,
            ("Night Length", "night_length"),
            600.0,
        ),
    }

    # The official ClockBased API expects normal civil time. It performs the
    # pack's internal midnight/sunrise conversion itself.
    sky.call_method(
        "Set New Time ClockBased",
        args=(12.0, 0.0, 0.0, 24.0, 60.0, 60.0),
    )
    level_editor.save_current_level()

    report = {
        "map": MAP_PATH,
        "sky": sky.get_path_name(),
        "changes": changes,
        "clock_after_configuration": list(sky.call_method("Get Clock Time")),
        "full_day_seconds": 1200.0,
    }
    report_path = os.path.join(
        unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_saved_dir()),
        "SoStylizedDayNightConfiguration.json",
    )
    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, ensure_ascii=False, default=str)
    unreal.log(f"[CML DayNight] wrote {report_path}")
    unreal.log("CML_DAYNIGHT_CONFIGURATION_SUCCEEDED")


try:
    main()
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.log_error("CML_DAYNIGHT_CONFIGURATION_FAILED")
