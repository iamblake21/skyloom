"""Runs the gameplay map briefly and reports what the simulation says.

The HUD draws nothing at all — no hotbar, no panels — unless
`GetPlayerInventoryPresentation` returns a snapshot with a full set of slots.
Three places in the bootstrap can refuse before that is ever true, and each
logs its own reason:

    Bootstrap catalog refused (failure N, id X).
    The bootstrap catalog has no player container.
    The authoritative player inventory could not be projected for the HUD.

None of them can be told apart from a blank screen, and no gameplay session
had been logged, so this exists to make the world say which one it is. Logs
only: no screenshots, no viewport work, a few seconds of simulation.
"""

from __future__ import annotations

import traceback

import unreal

MAP = "/Game/Maps/A_10_StarterIsland_AxisPreview"
BOOT = 1.5
SETTLE = 8.0
DEADLINE = 120.0

state = {"elapsed": 0.0, "playing": False, "started_at": 0.0, "handle": None}


def log(message: str) -> None:
    unreal.log(f"[CML Probe] {message}")


def finish(reason: str) -> None:
    if state["handle"] is not None:
        unreal.unregister_slate_post_tick_callback(state["handle"])
        state["handle"] = None
    log(f"CML_PROBE_DONE {reason}")
    unreal.SystemLibrary.quit_editor()


def on_tick(delta: float) -> None:
    try:
        state["elapsed"] += delta
        if state["elapsed"] > DEADLINE:
            finish("deadline")
            return

        if not state["playing"]:
            if state["elapsed"] < BOOT:
                return
            unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).editor_play_simulate()
            state["playing"] = True
            state["started_at"] = state["elapsed"]
            log("simulation started")
            return

        if state["elapsed"] - state["started_at"] < SETTLE:
            return

        world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_game_world()
        log(f"world={'present' if world is not None else 'missing'}")
        finish("settled")
    except Exception:
        unreal.log_error(traceback.format_exc())
        finish("exception")


try:
    if not unreal.get_editor_subsystem(unreal.LevelEditorSubsystem).load_level(MAP):
        unreal.log_error(f"CML_PROBE_DONE cannot open {MAP}")
        unreal.SystemLibrary.quit_editor()
    else:
        log(f"opened {MAP}")
        state["handle"] = unreal.register_slate_post_tick_callback(on_tick)
except Exception:
    unreal.log_error(traceback.format_exc())
    unreal.SystemLibrary.quit_editor()
