"""Captures the Unreal intro at the same four moments Unity's preview captures.

Unity has `IntroCinematicPreviewCapture`, which writes reference frames to
`outputs/IntroCinematic`. Those frames are the only real specification of what
the opening is supposed to look like: the shot list and the timings were ported
faithfully and still say nothing about whether the result *reads* the same.

Shot boundaries come from `FCMLIntroSequenceSettings`, so the sample times below
sit inside the intended shot rather than on a boundary where a frame could
belong to either side:

    Hyperspace 4.5 | Cockpit 2.6 | Flight ~25 | Alarm 4.5 | RiftOpen 5.5 | ...

The director stages the whole opening at runtime, so nothing here exists until
the world is playing — the same reason an editor-world capture of any CML map
comes back unlit.
"""

from __future__ import annotations

import os
import traceback

import unreal

MAP = "/Game/Maps/A_01_IntroCinematic"
WIDTH = 1920
HEIGHT = 1080

# (shot to wait for, camera actor to borrow, name for the comparison)
#
# Keyed on the director's own shot, never on a stopwatch. The opening advances
# on game time and the editor simulates slower than the clock, so sampling at
# fixed wall-clock seconds photographed whichever shot happened to be running —
# and then reported the rift missing from a frame taken before it opens.
SCHEDULE = [
    (0, "CIN_RuntimeChaseCamera", "01_hyperspace_chase"),   # Hyperspace
    (2, "CIN_RuntimeCockpitCamera", "02_cockpit"),          # Flight
    (3, "CIN_RuntimeCockpitCamera", "03_alarm"),            # Alarm
    (4, "CIN_RuntimeCockpitCamera", "04_cockpit_rift"),     # RiftOpen
]

BOOT = 1.5
# The last sample sits at t=39s of simulated time and the simulation does not
# run at wall-clock speed, so the ceiling has to clear it with room to spare.
DEADLINE = 300.0

state = {
    "elapsed": 0.0,
    "playing": False,
    "started_at": 0.0,
    "index": 0,
    "pending": None,
    "before": set(),
    "written": [],
    "failure": None,
    "handle": None,
}


def log(message: str) -> None:
    unreal.log(f"[CML Intro Capture] {message}")


def shot_directory() -> str:
    return unreal.Paths.convert_relative_path_to_full(
        unreal.Paths.project_saved_dir()) + "Screenshots/WindowsEditor"


def existing_shots() -> set:
    directory = shot_directory()
    if not os.path.isdir(directory):
        return set()
    return set(os.listdir(directory))


def find_actor(world, token: str):
    """Locate a staged actor by name.

    The director spawns these, so they carry object names rather than editor
    labels; PIE duplication also suffixes names. Matching on a substring of
    either is what survives both.
    """
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor):
        if token in actor.get_name():
            return actor
        try:
            if token in actor.get_actor_label():
                return actor
        except Exception:
            pass
    return None


def playing_world():
    return unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_game_world()


def current_shot(world):
    """The shot the director says it is in, or None before it exists."""
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor):
        if "IntroDirector" not in actor.get_class().get_name():
            continue
        try:
            return int(actor.get_editor_property("State").shot)
        except Exception:
            return None
    return None


def take_shot(world, camera_token: str, label: str) -> None:
    camera = find_actor(world, camera_token)
    if camera is None:
        log(f"WARNING {label}: no '{camera_token}' staged; skipping")
        state["pending"] = None
        return

    editor = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
    editor.set_level_viewport_camera_info(
        camera.get_actor_location(), camera.get_actor_rotation())

    state["before"] = existing_shots()
    unreal.SystemLibrary.execute_console_command(world, f"HighResShot {WIDTH}x{HEIGHT}")
    state["pending"] = label
    log(f"requested {label} from {camera_token}")


def collect(label: str) -> bool:
    fresh = existing_shots() - state["before"]
    if not fresh:
        return False
    produced = sorted(fresh)[0]
    state["written"].append((label, produced))
    log(f"{label} -> {produced}")
    return True


def finish() -> None:
    if state["handle"] is not None:
        unreal.unregister_slate_post_tick_callback(state["handle"])
        state["handle"] = None
    for label, produced in state["written"]:
        log(f"FRAME {label}={produced}")
    if state["failure"]:
        unreal.log_error(f"CML_INTRO_CAPTURE_FAILED {state['failure']}")
    elif not state["written"]:
        unreal.log_error("CML_INTRO_CAPTURE_FAILED no frames were written")
    else:
        log(f"CML_INTRO_CAPTURE_SUCCEEDED frames={len(state['written'])}")
    unreal.SystemLibrary.quit_editor()


def on_tick(delta: float) -> None:
    try:
        state["elapsed"] += delta

        if state["elapsed"] > DEADLINE:
            state["failure"] = f"deadline at {DEADLINE:.0f}s"
            finish()
            return

        if not state["playing"]:
            if state["elapsed"] < BOOT:
                return
            unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).editor_play_simulate()
            state["playing"] = True
            state["started_at"] = state["elapsed"]
            log("simulation started")
            return

        world = playing_world()
        if world is None:
            return

        if state["pending"] is not None:
            if collect(state["pending"]):
                state["pending"] = None
                state["index"] += 1
                if state["index"] >= len(SCHEDULE):
                    finish()
            return

        if state["index"] >= len(SCHEDULE):
            finish()
            return

        wanted, camera_token, label = SCHEDULE[state["index"]]
        shot = current_shot(world)
        if shot is None:
            return
        if shot < wanted:
            return
        if shot > wanted:
            log(f"WARNING {label}: shot {wanted} already passed (now {shot}); skipping")
            state["index"] += 1
            if state["index"] >= len(SCHEDULE):
                finish()
            return

        take_shot(world, camera_token, label)
        if state["pending"] is None:
            # The camera was missing; do not stall the whole schedule on it.
            state["index"] += 1
            if state["index"] >= len(SCHEDULE):
                finish()
    except Exception:
        state["failure"] = "exception in tick"
        unreal.log_error(traceback.format_exc())
        finish()


try:
    if not unreal.EditorAssetLibrary.does_asset_exist(MAP):
        unreal.log_error(f"CML_INTRO_CAPTURE_FAILED missing {MAP}")
        unreal.SystemLibrary.quit_editor()
    elif not unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level(MAP):
        unreal.log_error(f"CML_INTRO_CAPTURE_FAILED cannot open {MAP}")
        unreal.SystemLibrary.quit_editor()
    else:
        log(f"opened {MAP}")
        state["handle"] = unreal.register_slate_post_tick_callback(on_tick)
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.log_error("CML_INTRO_CAPTURE_FAILED during setup")
    unreal.SystemLibrary.quit_editor()
