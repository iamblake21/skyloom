"""Adds the empty-GameObject pivots the first scene conversion dropped.

The scene importer only made actors for GameObjects that drew something, so
Unity's empty transforms were lost. Those are not decoration: they are the
pivots a cinematic orbits, the parents whose visibility a shot toggles, and the
markers a script keys off. Without them the intro map had everything to look at
and nothing to move it.

This patches already-converted maps in place rather than re-importing them. A
full re-import means deleting and recreating a level asset, which an unattended
editor does not survive — it crashes in EditorServer rather than reporting a
refusal. Adding what is missing to a live level needs none of that, touches
nothing that already converted correctly, and can be run again safely.
"""

from __future__ import annotations

import json
import re
import traceback
from pathlib import Path

import unreal

from cml_unity_yaml import index_by_file_id, load_unity_documents, parse_reference

MAP_ROOT = "/Game/Maps"


def _log(message: str) -> None:
    unreal.log(f"[CML Pivots] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Pivots] {message}")


def _sanitize(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", str(value).strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Object"
    return f"A_{value}" if value[0].isdigit() else value


def _project_dir() -> Path:
    return Path(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()))


def _patch(source: str, unity_root: Path) -> dict:
    documents = load_unity_documents(unity_root / Path(source))
    by_file_id = index_by_file_id(documents)

    # Same world-transform composition the importer does: a Unity placement is
    # relative to its parent, an Unreal actor is placed in world space.
    transforms = {
        document.file_id: document
        for document in documents if document.type_name == "Transform"
    }

    def world_of(file_id: int, guard: set) -> unreal.Transform:
        document = transforms.get(file_id)
        if document is None or file_id in guard:
            return unreal.Transform()
        guard.add(file_id)
        position = document.get("m_LocalPosition") or {}
        rotation = document.get("m_LocalRotation") or {}
        local = unreal.Transform(
            location=unreal.Vector(
                float(position.get("z", 0.0)) * 100.0,
                float(position.get("x", 0.0)) * 100.0,
                float(position.get("y", 0.0)) * 100.0),
            rotation=unreal.Quat(
                -float(rotation.get("z", 0.0)),
                -float(rotation.get("x", 0.0)),
                -float(rotation.get("y", 0.0)),
                float(rotation.get("w", 1.0))).rotator(),
            scale=unreal.Vector(1.0, 1.0, 1.0))
        parent = parse_reference(document.get("m_Father"))
        guard.discard(file_id)
        if not parent.file_id:
            return local
        # Composed by the engine rather than by hand: doing it manually means
        # rotating the child's offset by the parent's rotation, and getting that
        # subtly wrong puts a pivot near enough to look plausible and far enough
        # to frame the wrong thing.
        return unreal.MathLibrary.compose_transforms(local, world_of(parent.file_id, guard))

    components: dict[int, list] = {}
    for document in documents:
        owner = parse_reference(document.get("m_GameObject"))
        if owner.file_id:
            components.setdefault(owner.file_id, []).append(document)

    map_path = f"{MAP_ROOT}/{_sanitize(Path(source).stem)}"
    if not unreal.EditorAssetLibrary.does_asset_exist(map_path):
        return {"status": "skipped", "scene": source, "reason": "no converted map"}

    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not level_editor.load_level(map_path):
        raise RuntimeError(f"cannot open {map_path}")

    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    existing = {
        actor.get_actor_label()
        for actor in unreal.GameplayStatics.get_all_actors_of_class(world, unreal.Actor)
    }
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)

    added: list[str] = []
    for document in documents:
        if document.type_name != "GameObject":
            continue
        owned = components.get(document.file_id, [])
        transform_document = next(
            (item for item in owned if item.type_name == "Transform"), None)
        if transform_document is None:
            continue
        # Only GameObjects that draw nothing: anything else already converted.
        if any(item.type_name not in ("Transform", "MonoBehaviour") for item in owned):
            continue

        label = _sanitize(str(document.get("m_Name", "Object")))
        if label in existing:
            continue

        placement = world_of(transform_document.file_id, set())
        # A TargetPoint rather than a bare AActor: it has a root component, so
        # it carries a transform that can be read and moved at runtime.
        actor = actor_subsystem.spawn_actor_from_class(
            unreal.TargetPoint, placement.translation, placement.rotation.rotator())
        if actor is None:
            continue
        actor.set_actor_label(label)
        existing.add(label)
        added.append(label)

    if added:
        level_editor.save_current_level()
    return {"status": "patched", "scene": source, "map": map_path, "added": added}


def main() -> int:
    project_dir = _project_dir()
    manifest = json.loads(
        (project_dir / "Migration" / "unity_asset_manifest.json").read_text("utf-8"))
    unity_root = Path(manifest["unityRoot"])

    results: list[dict] = []
    for entry in manifest["entries"]:
        if entry["kind"] != "scene":
            continue
        try:
            outcome = _patch(entry["source"], unity_root)
            results.append(outcome)
            if outcome["status"] == "patched":
                _log(f"{outcome['map']}: added {len(outcome['added'])} pivot(s)")
        except Exception as exception:  # noqa: BLE001 - reported per scene
            _error(f"{entry['source']}: {exception}")
            _error(traceback.format_exc())
            results.append({"status": "failed", "scene": entry["source"]})

    failed = sum(item["status"] == "failed" for item in results)
    report = {
        "schema": 1,
        "scenes": len(results),
        "patched": sum(item["status"] == "patched" for item in results),
        "failed": failed,
        "results": results,
    }
    path = project_dir / "Migration" / "unity_scene_pivot_report.json"
    path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    _log(f"Complete: patched={report['patched']}, failed={failed}")
    return 0 if failed == 0 else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_SCENE_PIVOTS_FAILED code={_exit_code}")
else:
    _log("CML_SCENE_PIVOTS_SUCCEEDED")
