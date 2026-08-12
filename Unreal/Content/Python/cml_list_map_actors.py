"""Lists what a converted map actually contains.

Written to answer one question the logs could not: the intro director looks its
actors up by label, and finding none of them means either the labels differ or
the actors were never created. Guessing between those two is what this avoids.
"""

from __future__ import annotations

import unreal

MAP = "/Game/Maps/A_01_IntroCinematic"


def main() -> int:
    editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not editor.load_level(MAP):
        unreal.log_error(f"[CML Map] cannot open {MAP}")
        return 2

    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    actors = unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
    unreal.log(f"[CML Map] {MAP}: {len(actors)} actors")
    for actor in sorted(actors, key=lambda a: a.get_actor_label()):
        unreal.log(f"[CML Map]   {actor.get_actor_label()}  <{actor.get_class().get_name()}>")
    return 0


try:
    _exit_code = main()
except Exception:
    import traceback
    unreal.log_error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    unreal.log_error(f"CML_MAP_LIST_FAILED code={_exit_code}")
else:
    unreal.log("CML_MAP_LIST_SUCCEEDED")
