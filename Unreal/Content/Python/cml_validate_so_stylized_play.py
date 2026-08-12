from __future__ import annotations

import json
import os
import traceback

import unreal


MAP = "/Game/Maps/A_10_StarterIsland_AxisPreview"
WIDTH = 1600
HEIGHT = 900
BOOT_SECONDS = 1.5
SETTLE_SECONDS = 6.0
DEADLINE_SECONDS = 45.0

state = {
    "elapsed": 0.0,
    "playing": False,
    "capture_requested": False,
    "handle": None,
    "failure": None,
}


def screenshot_directory():
    return unreal.Paths.convert_relative_path_to_full(
        unreal.Paths.project_saved_dir()) + "Screenshots/WindowsEditor"


def existing_screenshots():
    directory = screenshot_directory()
    return set(os.listdir(directory)) if os.path.isdir(directory) else set()


def game_world():
    return unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_game_world()


def begin_play():
    level = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    method = getattr(level, "editor_play_simulate", None)
    if method is None:
        method = getattr(level, "editor_request_begin_play", None)
    if method is None:
        raise RuntimeError("No editor Play/Simulate API is available")
    method()


def directional_lights(world):
    result = []
    actors = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
    for actor in actors:
        for comp in actor.get_components_by_class(unreal.DirectionalLightComponent):
            result.append({
                "actor": actor.get_actor_label(),
                "class": actor.get_class().get_name(),
                "component": comp.get_name(),
                "intensity": str(comp.get_editor_property("intensity")),
                "forwardPriority": str(comp.get_editor_property("forward_shading_priority")),
            })
    return result


def request_capture(world):
    starts = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.PlayerStart)
    if not starts:
        raise RuntimeError("No PlayerStart exists in the playing world")
    start = starts[0]
    location = start.get_actor_location() + unreal.Vector(0.0, 0.0, 72.0)
    rotation = start.get_actor_rotation()
    unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).set_level_viewport_camera_info(
        location, rotation
    )
    state["before"] = existing_screenshots()
    state["lights"] = directional_lights(world)
    unreal.SystemLibrary.execute_console_command(world, f"HighResShot {WIDTH}x{HEIGHT}")
    state["capture_requested"] = True


def write_report(capture=None):
    report = {
        "map": MAP,
        "directionalLights": state.get("lights", []),
        "directionalLightCount": len(state.get("lights", [])),
        "capture": capture,
        "failure": state.get("failure"),
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedPlayValidation.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)


def finish(capture=None):
    write_report(capture)
    if state["handle"] is not None:
        unreal.unregister_slate_post_tick_callback(state["handle"])
        state["handle"] = None
    if state.get("failure"):
        unreal.log_error("[SoStylized Play Validation] " + state["failure"])
    else:
        unreal.log("[SoStylized Play Validation] Success")
    unreal.SystemLibrary.quit_editor()


def tick(delta):
    try:
        state["elapsed"] += delta
        if state["elapsed"] > DEADLINE_SECONDS:
            state["failure"] = "Timed out before the Play Mode capture was written"
            finish()
            return
        if not state["playing"]:
            if state["elapsed"] < BOOT_SECONDS:
                return
            begin_play()
            state["playing"] = True
            state["play_started"] = state["elapsed"]
            return
        world = game_world()
        if world is None:
            return
        if not state["capture_requested"]:
            if state["elapsed"] - state["play_started"] < SETTLE_SECONDS:
                return
            request_capture(world)
            return
        fresh = existing_screenshots() - state.get("before", set())
        if fresh:
            name = sorted(fresh)[0]
            finish(os.path.join(screenshot_directory(), name))
    except Exception:
        state["failure"] = traceback.format_exc()
        finish()


def main():
    if not unreal.EditorAssetLibrary.does_asset_exist(MAP):
        raise RuntimeError(f"Missing map {MAP}")
    level = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level.load_level(MAP):
        raise RuntimeError(f"Could not load {MAP}")
    state["handle"] = unreal.register_slate_post_tick_callback(tick)


main()
