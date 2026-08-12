"""Capture the Starter Island from one camera at four civil times."""

from __future__ import annotations

import json
import os
import traceback

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
WIDTH = 1920
HEIGHT = 1080
CAPTURE_TIMES = (
    ("00_00_midnight", 0, 0, 0),
    ("06_00_dawn", 6, 0, 0),
    ("12_00_noon", 12, 0, 0),
    ("18_00_sunset", 18, 0, 0),
)


def _find_one(world, actor_class, predicate, description):
    matches = [
        actor
        for actor in unreal.GameplayStatics.get_all_actors_of_class(world, actor_class)
        if predicate(actor)
    ]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one {description}, found {len(matches)}")
    return matches[0]


def _clock_args(civil_hour, civil_minute, civil_second):
    # ClockBased accepts conventional civil time plus the clock dimensions.
    return civil_hour, civil_minute, civil_second, 24, 60, 60


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")

    world = unreal.get_editor_subsystem(
        unreal.UnrealEditorSubsystem).get_editor_world()
    sky = _find_one(
        world,
        unreal.Actor,
        lambda actor: "BP_StylizedSky" in actor.get_class().get_name(),
        "So Stylized sky",
    )
    player_start = _find_one(
        world,
        unreal.PlayerStart,
        lambda actor: True,
        "PlayerStart",
    )

    camera_location = player_start.get_actor_location() + unreal.Vector(0.0, 0.0, 74.0)
    camera_rotation = player_start.get_actor_rotation()
    capture_actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
        unreal.SceneCapture2D,
        camera_location,
        camera_rotation,
    )
    capture_actor.set_actor_label("TEMP_CML_DayNightCapture")
    component = capture_actor.capture_component2d
    component.capture_every_frame = False
    component.capture_on_movement = False
    component.always_persist_rendering_state = True
    component.capture_source = unreal.SceneCaptureSource.SCS_FINAL_COLOR_LDR
    component.fov_angle = 90.0

    render_target = unreal.RenderingLibrary.create_render_target2d(
        world,
        WIDTH,
        HEIGHT,
        unreal.TextureRenderTargetFormat.RTF_RGBA8,
    )
    component.texture_target = render_target

    output_directory = os.path.join(
        unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_saved_dir()),
        "DayNightCaptures",
    )
    os.makedirs(output_directory, exist_ok=True)
    report = {
        "map": MAP_PATH,
        "camera_location": str(camera_location),
        "camera_rotation": str(camera_rotation),
        "captures": [],
    }

    for name, civil_hour, civil_minute, civil_second in CAPTURE_TIMES:
        clock_args = _clock_args(civil_hour, civil_minute, civil_second)
        sky.call_method("Set New Time ClockBased", args=clock_args)
        component.capture_scene()
        unreal.RenderingLibrary.export_render_target(
            world,
            render_target,
            output_directory,
            f"CML_{name}.png",
        )
        reported_clock = list(sky.call_method("Get Clock Time"))
        output_file = os.path.join(output_directory, f"CML_{name}.png")
        report["captures"].append({
            "civil_time": f"{civil_hour:02d}:{civil_minute:02d}:{civil_second:02d}",
            "so_stylized_input": list(clock_args),
            "so_stylized_reported_clock": reported_clock,
            "file": output_file,
        })
        unreal.log(
            f"[CML DayNight Capture] {civil_hour:02d}:{civil_minute:02d} -> {output_file}")

    # Leave the editor in the migrated Unity baseline: civil noon.
    sky.call_method("Set New Time ClockBased", args=_clock_args(12, 0, 0))
    unreal.EditorLevelLibrary.destroy_actor(capture_actor)

    report_path = os.path.join(output_directory, "capture_report.json")
    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, ensure_ascii=False)
    unreal.log(f"[CML DayNight Capture] wrote {report_path}")
    unreal.log("CML_DAYNIGHT_CAPTURE_SUCCEEDED")


try:
    main()
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.log_error("CML_DAYNIGHT_CAPTURE_FAILED")
