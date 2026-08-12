"""Set one civil time and schedule a real editor-viewport screenshot.

Caller provides CML_CAPTURE_HOUR and CML_CAPTURE_FILE in the Python console.
The active viewport is used so Landscape Grass streaming matches what the
player and editor actually see; SceneCapture2D does not drive grass streaming.
"""

from __future__ import annotations

import unreal


hour = float(globals()["CML_CAPTURE_HOUR"])
filename = str(globals()["CML_CAPTURE_FILE"])
world = unreal.get_editor_subsystem(
    unreal.UnrealEditorSubsystem).get_editor_world()
skies = [
    actor
    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
    if "BP_StylizedSky" in actor.get_class().get_name()
]
if len(skies) != 1:
    raise RuntimeError(f"Expected one So Stylized sky, found {len(skies)}")

skies[0].call_method(
    "Set New Time ClockBased",
    args=(hour, 0.0, 0.0, 24.0, 60.0, 60.0),
)
globals()["CML_SCREENSHOT_TASK"] = unreal.AutomationLibrary.take_high_res_screenshot(
    1920,
    1080,
    filename,
    delay=2.0,
    force_game_view=True,
)
unreal.log(f"[CML DayNight Viewport] scheduled {hour:02.0f}:00 -> {filename}")
