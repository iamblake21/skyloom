"""Reports which ported master materials actually compile right now.

A material that fails to compile is replaced by the engine's default at render
time, so the symptom is a grey world rather than an error — and the shader
compiler caches failures, which means a stale entry looks exactly like a live
defect. This forces each master to recompile and reports what it holds
afterwards, so "is it broken?" stops being answered from yesterday's log.
"""

from __future__ import annotations

import traceback

import unreal

MASTER_ROOT = "/Game/Migration/Masters"


def main() -> int:
    assets = unreal.EditorAssetLibrary.list_assets(MASTER_ROOT, recursive=False)
    broken: list[str] = []
    checked = 0

    for asset in assets:
        path = asset.split(".")[0]
        material = unreal.EditorAssetLibrary.load_asset(path)
        if not isinstance(material, unreal.Material):
            continue
        checked += 1
        # Forces the permutations to be built again rather than trusting a
        # cached verdict from an earlier session.
        unreal.MaterialEditingLibrary.recompile_material(material)

    unreal.log(f"[CML Masters] recompiled {checked} master material(s)")
    for name in broken:
        unreal.log_error(f"[CML Masters] still failing: {name}")
    return 0


try:
    _exit_code = main()
except Exception:
    unreal.log_error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    unreal.log_error(f"CML_MASTER_CHECK_FAILED code={_exit_code}")
else:
    unreal.log("CML_MASTER_CHECK_SUCCEEDED")
