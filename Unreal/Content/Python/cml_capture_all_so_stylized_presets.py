"""Capture every So Stylized sky preset at four civil times.

The active editor viewport is used so Landscape Grass streaming and materials
match what the player sees. The map is modified only in editor memory and is
reloaded from disk when the run finishes or fails.
"""

from __future__ import annotations

import json
import os
import time
import traceback

import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
PRESET_ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment/Sky/PRESETS"
WIDTH = 1920
HEIGHT = 1080
PRESET_SETTLE_SECONDS = 5.0
TIME_SETTLE_SECONDS = 3.0
CAPTURE_DELAY_SECONDS = 1.0
CAPTURE_TIMEOUT_SECONDS = 300.0

PRESETS = (
    ("01_Classic", "BP_StylizedSky_Classic"),
    ("02_Classic_MoonPhases", "BP_StylizedSky_Classic_MoonPhases"),
    ("03_Cinematic", "BP_StylizedSky_Cinematic"),
    ("04_Dreamy", "BP_StylizedSky_Dreamy"),
    ("05_Desert", "BP_StylizedSky_Desert"),
    ("06_Alien", "BP_StylizedSky_Alien"),
    ("07_Apocalypse", "BP_StylizedSky_Apocalypse"),
    ("08_Tatooine", "BP_StylizedSky_Tatooine"),
    ("09_Toxic", "BP_StylizedSky_Toxic"),
)

TIMES = (
    ("00_00_Midnight", 0),
    ("06_00_Dawn", 6),
    ("12_00_Noon", 12),
    ("18_00_Sunset", 18),
)


def _unique_output_directory():
    desktop = os.path.join(os.path.expanduser("~"), "Desktop")
    base = os.path.join(desktop, "SoStylized_Preset_Comparison_2026-08-12")
    if not os.path.exists(base):
        return base
    suffix = 2
    while os.path.exists(f"{base}_{suffix:02d}"):
        suffix += 1
    return f"{base}_{suffix:02d}"


def _write_json(path, payload):
    with open(path, "w", encoding="utf-8") as stream:
        json.dump(payload, stream, indent=2, ensure_ascii=False, default=str)


def _write_progress(status, message=""):
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    payload = {
        "status": status,
        "message": message,
        "output_directory": state["output_directory"],
        "completed": len(state["captures"]),
        "total": len(PRESETS) * len(TIMES),
        "current_preset": state.get("current_preset"),
        "current_time": state.get("current_time"),
        "captures": state["captures"],
        "failure": state.get("failure"),
    }
    _write_json(os.path.join(state["output_directory"], "progress.json"), payload)


def _find_original_sky(world):
    skies = [
        actor
        for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
        if "BP_StylizedSky" in actor.get_class().get_name()
    ]
    if len(skies) != 1:
        raise RuntimeError(f"Expected one original So Stylized sky, found {len(skies)}")
    return skies[0]


def _set_optional_property(actor, names, value):
    for name in names:
        try:
            actor.set_editor_property(name, value)
            return name
        except Exception:
            pass
    return None


def _spawn_preset():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    if state.get("actor"):
        unreal.EditorLevelLibrary.destroy_actor(state["actor"])
        state["actor"] = None

    folder_name, asset_name = PRESETS[state["preset_index"]]
    asset_path = f"{PRESET_ROOT}/{asset_name}"
    actor_class = unreal.EditorAssetLibrary.load_blueprint_class(asset_path)
    if actor_class is None:
        raise RuntimeError(f"Could not load preset class {asset_path}")

    actor = unreal.EditorLevelLibrary.spawn_actor_from_class(
        actor_class,
        state["sky_location"],
        state["sky_rotation"],
    )
    if actor is None:
        raise RuntimeError(f"Could not spawn {asset_name}")
    actor.set_actor_scale3d(state["sky_scale"])
    actor.set_actor_label(f"TEMP_CAPTURE_{asset_name}")
    _set_optional_property(actor, ("Day Length", "day_length"), 600.0)
    _set_optional_property(actor, ("Night Length", "night_length"), 600.0)
    actor.call_method(
        "Set New Time ClockBased",
        args=(12.0, 0.0, 0.0, 24.0, 60.0, 60.0),
    )

    output_folder = os.path.join(state["output_directory"], folder_name)
    os.makedirs(output_folder, exist_ok=True)
    state["actor"] = actor
    state["current_preset"] = folder_name
    state["time_index"] = 0
    state["phase"] = "preset_settle"
    state["phase_started"] = state["elapsed"]
    _write_progress("running", f"Preparing {folder_name}")
    unreal.log(f"[CML Preset Capture] Preparing {folder_name}")


def _set_current_time():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    time_name, hour = TIMES[state["time_index"]]
    state["actor"].call_method(
        "Set New Time ClockBased",
        args=(float(hour), 0.0, 0.0, 24.0, 60.0, 60.0),
    )
    state["current_time"] = time_name
    state["phase"] = "time_settle"
    state["phase_started"] = state["elapsed"]
    _write_progress("running", f"Settling {state['current_preset']} at {time_name}")


def _request_capture():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    folder_name, _ = PRESETS[state["preset_index"]]
    time_name, hour = TIMES[state["time_index"]]
    filename = os.path.join(
        state["output_directory"],
        folder_name,
        f"{time_name}.png",
    )
    globals()["CML_PRESET_SCREENSHOT_TASK"] = (
        unreal.AutomationLibrary.take_high_res_screenshot(
            WIDTH,
            HEIGHT,
            filename,
            delay=CAPTURE_DELAY_SECONDS,
            force_game_view=True,
        )
    )
    state["capture_path"] = filename
    state["capture_requested_at"] = state["elapsed"]
    state["phase"] = "capture_wait"
    _write_progress("running", f"Capturing {folder_name} at {time_name}")
    unreal.log(
        f"[CML Preset Capture] {folder_name} {hour:02d}:00 -> {filename}")


def _record_capture():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    folder_name, asset_name = PRESETS[state["preset_index"]]
    time_name, hour = TIMES[state["time_index"]]
    path = state["capture_path"]
    state["captures"].append({
        "preset": folder_name,
        "asset": asset_name,
        "civil_time": f"{hour:02d}:00",
        "file": path,
        "bytes": os.path.getsize(path),
    })
    _write_progress("running", f"Captured {folder_name} at {time_name}")
    state["time_index"] += 1
    if state["time_index"] < len(TIMES):
        _set_current_time()
        return

    state["preset_index"] += 1
    if state["preset_index"] < len(PRESETS):
        _spawn_preset()
        return
    _finish_success()


def _write_readme():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    lines = [
        "SO STYLIZED - CONFRONTO PRESET",
        "================================",
        "",
        "Mappa: A_10_StarterIsland_AxisPreview",
        "Risoluzione: 1920x1080",
        "Orari: 00:00, 06:00, 12:00, 18:00",
        "Durata normalizzata: 600 s giorno + 600 s notte",
        "",
        "Ogni sottocartella contiene lo stesso punto di vista e gli stessi orari.",
        "La mappa originale non e stata modificata: e stata ricaricata dal disco",
        "dopo le catture.",
        "",
        "Preset:",
    ]
    lines.extend(f"- {folder}" for folder, _ in PRESETS)
    with open(
        os.path.join(state["output_directory"], "LEGGIMI.txt"),
        "w",
        encoding="utf-8",
    ) as stream:
        stream.write("\n".join(lines) + "\n")


def _restore_map():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    if state.get("actor"):
        unreal.EditorLevelLibrary.destroy_actor(state["actor"])
        state["actor"] = None
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    level_editor.load_level(MAP_PATH)
    unreal.get_editor_subsystem(
        unreal.UnrealEditorSubsystem).set_level_viewport_camera_info(
            state["camera_location"], state["camera_rotation"])


def _unregister():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    handle = state.get("handle")
    if handle is not None:
        unreal.unregister_slate_post_tick_callback(handle)
        state["handle"] = None


def _finish_success():
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    _unregister()
    _restore_map()
    _write_readme()
    manifest = {
        "status": "complete",
        "map": MAP_PATH,
        "resolution": [WIDTH, HEIGHT],
        "captures": state["captures"],
    }
    _write_json(os.path.join(state["output_directory"], "manifest.json"), manifest)
    _write_progress("complete", "All presets captured; original map restored")
    unreal.log(
        f"CML_ALL_PRESET_CAPTURES_SUCCEEDED {state['output_directory']}")


def _finish_failure(error_text):
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    state["failure"] = error_text
    _unregister()
    try:
        _restore_map()
    except Exception:
        state["failure"] += "\nRESTORE FAILURE:\n" + traceback.format_exc()
    _write_progress("failed", "Capture run failed; see failure field")
    unreal.log_error("CML_ALL_PRESET_CAPTURES_FAILED\n" + state["failure"])


def _tick(delta_seconds):
    state = globals()["CML_PRESET_CAPTURE_STATE"]
    try:
        state["elapsed"] += max(0.0, float(delta_seconds))
        phase = state["phase"]
        if phase == "preset_settle":
            if state["elapsed"] - state["phase_started"] >= PRESET_SETTLE_SECONDS:
                _set_current_time()
        elif phase == "time_settle":
            if state["elapsed"] - state["phase_started"] >= TIME_SETTLE_SECONDS:
                _request_capture()
        elif phase == "capture_wait":
            path = state["capture_path"]
            if os.path.isfile(path) and os.path.getsize(path) > 0:
                _record_capture()
            elif (
                state["elapsed"] - state["capture_requested_at"]
                > CAPTURE_TIMEOUT_SECONDS
            ):
                raise RuntimeError(f"Timed out waiting for screenshot {path}")
    except Exception:
        _finish_failure(traceback.format_exc())


def main():
    prior = globals().get("CML_PRESET_CAPTURE_STATE")
    if prior and prior.get("handle") is not None:
        unreal.unregister_slate_post_tick_callback(prior["handle"])

    output_directory = _unique_output_directory()
    os.makedirs(output_directory, exist_ok=False)
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")
    editor = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
    camera_location, camera_rotation = editor.get_level_viewport_camera_info()
    world = editor.get_editor_world()
    original_sky = _find_original_sky(world)
    state = {
        "output_directory": output_directory,
        "camera_location": camera_location,
        "camera_rotation": camera_rotation,
        "sky_location": original_sky.get_actor_location(),
        "sky_rotation": original_sky.get_actor_rotation(),
        "sky_scale": original_sky.get_actor_scale3d(),
        "actor": None,
        "preset_index": 0,
        "time_index": 0,
        "phase": "starting",
        "phase_started": 0.0,
        "elapsed": 0.0,
        "capture_path": None,
        "capture_requested_at": 0.0,
        "captures": [],
        "current_preset": None,
        "current_time": None,
        "failure": None,
        "handle": None,
    }
    globals()["CML_PRESET_CAPTURE_STATE"] = state
    unreal.EditorLevelLibrary.destroy_actor(original_sky)
    _spawn_preset()
    state["handle"] = unreal.register_slate_post_tick_callback(_tick)
    _write_progress("running", "Capture run started")
    unreal.log(f"[CML Preset Capture] Output: {output_directory}")


try:
    main()
except Exception:
    error = traceback.format_exc()
    if globals().get("CML_PRESET_CAPTURE_STATE"):
        _finish_failure(error)
    else:
        unreal.log_error("CML_ALL_PRESET_CAPTURES_FAILED\n" + error)
