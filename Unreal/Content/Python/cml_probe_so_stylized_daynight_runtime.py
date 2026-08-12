"""Inspect the live So Stylized sky contract used by the Starter Island.

The Marketplace Blueprint is intentionally treated as an external visual
adapter.  This probe records the names that Unreal actually exposes at runtime
before the project-owned day/night clock binds to them.
"""

from __future__ import annotations

import json
import os
import traceback

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
OUTPUT_NAME = "so_stylized_daynight_runtime_probe.json"


PROPERTY_CANDIDATES = (
    "day_cycle_enabled",
    "day_cycle_progress",
    "current_hour",
    "current_time_of_day",
    "current_time_from_midnight",
    "daily_hours",
    "daily_hours_non_military",
    "hour_length",
    "night_length",
    "hourly_minutes",
    "minutely_seconds",
    "military_clock",
    "DayCycleEnabled",
    "DayCycleProgress",
    "CurrentHour",
    "CurrentTimeOfDay",
    "CurrentTimeFromMidnight",
    "DailyHours",
    "DailyHoursNonMilitary",
    "HourLength",
    "NightLength",
    "HourlyMinutes",
    "MinutelySeconds",
    "MilitaryClock",
    "Day Cycle Enabled",
    "Day Cycle Progress",
    "Current Hour",
    "Current Time of Day",
    "Current Time From Midnight",
    "Daily Hours",
    "Daily Hours Non Military",
    "Hour Length",
    "Night Length",
    "Hourly Minutes",
    "Minutely Seconds",
    "Military Clock",
)

FUNCTION_CANDIDATES = (
    "get_clock_time",
    "clock_time_to_sky_time",
    "set_new_time_clock_based",
    "set_new_time_smooth_clock_based",
)


def _json_value(value):
    if value is None or isinstance(value, (bool, int, float, str)):
        return value
    if isinstance(value, (tuple, list)):
        return [_json_value(item) for item in value]
    return str(value)


def _is_so_stylized_sky(actor):
    label = actor.get_actor_label().lower()
    class_name = actor.get_class().get_name().lower()
    return "stylizedsky" in class_name or "sostylized_sky" in label


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    current_world = unreal.get_editor_subsystem(
        unreal.UnrealEditorSubsystem).get_editor_world()
    if current_world is None or current_world.get_path_name().split(":", 1)[0] != MAP_PATH:
        if not level_editor.load_level(MAP_PATH):
            raise RuntimeError(f"Could not load {MAP_PATH}")
        current_world = unreal.get_editor_subsystem(
            unreal.UnrealEditorSubsystem).get_editor_world()

    skies = [
        actor
        for actor in unreal.GameplayStatics.get_all_actors_of_class(
            current_world, unreal.Actor)
        if _is_so_stylized_sky(actor)
    ]
    if len(skies) != 1:
        raise RuntimeError(f"Expected one So Stylized sky, found {len(skies)}")

    sky = skies[0]
    report = {
        "map": MAP_PATH,
        "actor_label": sky.get_actor_label(),
        "actor_path": sky.get_path_name(),
        "class": sky.get_class().get_name(),
        "properties": {},
        "functions": {},
        "call_method_results": {},
        "python_names_containing_time_clock_cycle": sorted(
            name for name in dir(sky)
            if any(token in name.lower() for token in ("time", "clock", "cycle", "hour", "night"))
        ),
        "python_names_containing_call_function_property": sorted(
            name for name in dir(sky)
            if any(token in name.lower() for token in ("call", "function", "property"))
        ),
    }

    for name in PROPERTY_CANDIDATES:
        try:
            report["properties"][name] = {
                "available": True,
                "value": _json_value(sky.get_editor_property(name)),
            }
        except Exception as exc:
            report["properties"][name] = {
                "available": False,
                "error": str(exc),
            }

    for name in FUNCTION_CANDIDATES:
        try:
            function = getattr(sky, name)
            report["functions"][name] = {
                "available": callable(function),
                "repr": repr(function),
            }
        except Exception as exc:
            report["functions"][name] = {
                "available": False,
                "error": str(exc),
            }

    for name, arguments in (
        ("Get Clock Time", ()),
        ("GetClockTime", ()),
        ("Set New Time ClockBased", (0, 0, 0, 24, 60, 60)),
        ("SetNewTimeClockBased", (0, 0, 0, 24, 60, 60)),
    ):
        try:
            result = sky.call_method(name, args=arguments)
            report["call_method_results"][name] = {
                "succeeded": True,
                "result": _json_value(result),
            }
        except Exception as exc:
            report["call_method_results"][name] = {
                "succeeded": False,
                "error": str(exc),
            }

    report["clock_after_set"] = _json_value(
        sky.call_method("Get Clock Time"))

    output_path = os.path.join(
        unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_saved_dir()),
        OUTPUT_NAME,
    )
    with open(output_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, ensure_ascii=False)

    unreal.log(f"[CML DayNight Probe] wrote {output_path}")
    unreal.log("CML_DAYNIGHT_PROBE_SUCCEEDED")


try:
    main()
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.log_error("CML_DAYNIGHT_PROBE_FAILED")
