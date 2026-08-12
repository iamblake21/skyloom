"""Renders one converted map to a PNG so the result can actually be looked at.

Every headless check runs with `-NullRHI`, which draws nothing: it can prove the
code runs and the materials compile, and it cannot prove the world looks right.
A material that falls back to the engine default, a landscape at the wrong
scale, a sky that never binds — all of those pass a headless run and are obvious
in one frame.

Captured through a SceneCapture2D rather than `take_high_res_screenshot`, which
is serviced asynchronously by the renderer: the script ends, the editor exits,
and the request is cancelled before it reaches disk. `capture_scene` and
`export_render_target` both complete before the next line runs, so the file
exists by the time this returns.
"""

from __future__ import annotations

import os
import traceback

import unreal

DEFAULT_MAP = "/Game/Maps/A_91_StarterIsland_Terrain_Review"
WIDTH = 1280
HEIGHT = 720


def frame_the_content(world):
    """Where to put the camera so the level fills the frame.

    Only actors with visible geometry count: lights and markers would drag the
    centre towards wherever they happen to sit, and an empty or brown frame
    reads exactly like a broken material — a diagnosis best not made twice.
    """
    total = unreal.Vector(0.0, 0.0, 0.0)
    lowest = None
    highest = None
    counted = 0

    for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor):
        if actor.get_component_by_class(unreal.MeshComponent) is None:
            continue
        location = actor.get_actor_location()
        total = total + location
        if lowest is None:
            lowest = unreal.Vector(location.x, location.y, location.z)
            highest = unreal.Vector(location.x, location.y, location.z)
        lowest = unreal.Vector(
            min(lowest.x, location.x), min(lowest.y, location.y), min(lowest.z, location.z))
        highest = unreal.Vector(
            max(highest.x, location.x), max(highest.y, location.y), max(highest.z, location.z))
        counted += 1

    if counted == 0:
        return unreal.Vector(0.0, 0.0, 500.0), unreal.Rotator(-20.0, 45.0, 0.0), 0

    centre = unreal.Vector(total.x / counted, total.y / counted, total.z / counted)
    span = max(highest.x - lowest.x, highest.y - lowest.y, 2000.0)
    distance = span * 0.55 + 3000.0
    eye = unreal.Vector(
        centre.x - distance * 0.7, centre.y - distance * 0.7, centre.z + distance * 0.5)

    direction = centre - eye
    look = unreal.MathLibrary.conv_vector_to_rotator(direction)
    return eye, look, counted


def main() -> int:
    map_path = os.environ.get("CML_CAPTURE_MAP", DEFAULT_MAP)
    if not unreal.EditorAssetLibrary.does_asset_exist(map_path):
        unreal.log_error(f"[CML Capture] missing {map_path}")
        return 2

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(map_path):
        unreal.log_error(f"[CML Capture] cannot open {map_path}")
        return 2

    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    eye, look, counted = frame_the_content(world)
    unreal.log(f"[CML Capture] framing {counted} mesh actor(s)")

    # Eight-bit, explicitly. A float render target is exported as OpenEXR
    # whatever the filename says, so the ".png" would be an EXR that no image
    # viewer opens — a file that exists, has plausible size, and is unreadable.
    target = unreal.RenderingLibrary.create_render_target2d(
        world, WIDTH, HEIGHT, unreal.TextureRenderTargetFormat.RTF_RGBA8)
    capture = unreal.EditorLevelLibrary.spawn_actor_from_class(
        unreal.SceneCapture2D, eye, look)
    component = capture.capture_component2d
    component.texture_target = target
    # Final colour, not a G-buffer channel: the point is to see what a player
    # would see, including the sky and the tone mapping.
    component.capture_source = unreal.SceneCaptureSource.SCS_FINAL_COLOR_LDR
    component.capture_scene()

    directory = unreal.Paths.convert_relative_path_to_full(
        unreal.Paths.project_saved_dir()) + "Captures"
    name = map_path.rsplit("/", 1)[-1]
    unreal.RenderingLibrary.export_render_target(world, target, directory, f"{name}.png")
    unreal.EditorLevelLibrary.destroy_actor(capture)

    unreal.log(f"[CML Capture] wrote {directory}/{name}.png")
    return 0


try:
    _exit_code = main()
except Exception:
    unreal.log_error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    unreal.log_error(f"CML_CAPTURE_FAILED code={_exit_code}")
else:
    unreal.log("CML_CAPTURE_SUCCEEDED")
