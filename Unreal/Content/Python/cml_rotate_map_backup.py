"""Moves stale map backups aside so a re-import can run.

The scene importer refuses to overwrite an existing backup, which is the right
default: a second run would otherwise destroy the copy the first one made. This
renames them rather than deleting, so nothing kept is lost — a run that has
already happened leaves one backup per scene, and every one of them would block
the next run.

It also steps off whatever level is currently open. A level that is loaded
cannot be renamed, and the importer's first act is to rename its target out of
the way — so re-importing the map the editor happens to have open fails on a
package-in-use error that reads like a permissions problem.
"""

from __future__ import annotations

import unreal

BACKUP_ROOT = "/Game/Migration/MapBackups"
SCRATCH_LEVEL = "/Game/Migration/Scratch/EmptyForReimport"


def step_off_every_level() -> None:
    """Opens a scratch level so no migrated map is the active one."""
    editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if unreal.EditorAssetLibrary.does_asset_exist(SCRATCH_LEVEL):
        editor.load_level(SCRATCH_LEVEL)
    else:
        editor.new_level(SCRATCH_LEVEL)
    unreal.log(f"[CML Backup] stepped off onto {SCRATCH_LEVEL}")


def main() -> int:
    step_off_every_level()

    if not unreal.EditorAssetLibrary.does_directory_exist(BACKUP_ROOT):
        unreal.log("[CML Backup] no backups to rotate")
        return 0

    assets = unreal.EditorAssetLibrary.list_assets(BACKUP_ROOT, recursive=False)
    rotated = 0
    for asset in assets:
        path = asset.split(".")[0]
        name = path.rsplit("/", 1)[-1]
        # Already-rotated copies end in _NN and must not be rotated again, or
        # each run would shuffle the whole history one place along.
        if len(name) > 3 and name[-3] == "_" and name[-2:].isdigit():
            continue

        index = 1
        while unreal.EditorAssetLibrary.does_asset_exist(f"{path}_{index:02d}"):
            index += 1
        destination = f"{path}_{index:02d}"
        if not unreal.EditorAssetLibrary.rename_asset(path, destination):
            unreal.log_error(f"[CML Backup] cannot move {path} to {destination}")
            return 2
        unreal.log(f"[CML Backup] kept {name} as {destination.rsplit('/', 1)[-1]}")
        rotated += 1

    unreal.log(f"[CML Backup] rotated {rotated} backup(s)")
    return 0


try:
    _exit_code = main()
except Exception:
    import traceback
    unreal.log_error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    unreal.log_error(f"CML_BACKUP_ROTATE_FAILED code={_exit_code}")
else:
    unreal.log("CML_BACKUP_ROTATE_SUCCEEDED")
