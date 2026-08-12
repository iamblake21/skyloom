import json
import os
import unreal


SOURCE_ROOT = "/Game/SoStylized"
DESTINATION_ROOT = "/Game/_Project/Art/Environment/SoStylized"
BATCH_SIZE = 50


def log(message):
    unreal.log(f"[CML SoStylized Relocate] {message}")


def main():
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.scan_paths_synchronous([SOURCE_ROOT], force_rescan=True)

    asset_paths = unreal.EditorAssetLibrary.list_assets(
        SOURCE_ROOT,
        recursive=True,
        include_folder=False,
    )
    asset_paths = sorted(path for path in asset_paths if not path.endswith("/"))
    log(f"Discovered {len(asset_paths)} assets")

    asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
    moved = []
    failed = []

    for start in range(0, len(asset_paths), BATCH_SIZE):
        batch_paths = asset_paths[start : start + BATCH_SIZE]
        rename_data = []
        rename_records = []

        for object_path in batch_paths:
            asset = unreal.EditorAssetLibrary.load_asset(object_path)
            if asset is None:
                failed.append({"asset": object_path, "reason": "load_failed"})
                continue

            # A previous interrupted pass can leave redirectors at the source
            # and already-moved assets at the destination. Both are safe to skip.
            if asset.get_class().get_name() == "ObjectRedirector":
                continue

            package_path, _ = object_path.rsplit("/", 1)
            asset_name = asset.get_name()
            relative_package = package_path[len(SOURCE_ROOT) :].lstrip("/")
            destination_package = DESTINATION_ROOT
            if relative_package:
                destination_package += "/" + relative_package

            destination_asset = destination_package + "/" + asset_name
            if unreal.EditorAssetLibrary.does_asset_exist(destination_asset):
                continue

            rename_data.append(
                unreal.AssetRenameData(asset, destination_package, asset_name)
            )
            rename_records.append(
                {"name": asset_name, "destination": destination_package}
            )

        if not rename_data:
            continue

        succeeded = asset_tools.rename_assets(rename_data)
        if succeeded:
            moved.extend(rename_records)
        else:
            for record in rename_records:
                record["reason"] = "rename_failed"
                failed.append(record)

        log(
            f"Processed {min(start + BATCH_SIZE, len(asset_paths))}/"
            f"{len(asset_paths)}; moved={len(moved)} failed={len(failed)}"
        )

    unreal.EditorAssetLibrary.save_directory(
        DESTINATION_ROOT,
        only_if_is_dirty=False,
        recursive=True,
    )

    report = {
        "source_root": SOURCE_ROOT,
        "destination_root": DESTINATION_ROOT,
        "discovered": len(asset_paths),
        "moved": len(moved),
        "failed": failed,
    }
    report_path = os.path.join(
        unreal.Paths.project_saved_dir(),
        "SoStylizedRelocationReport.json",
    )
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)

    log(f"Completed: {json.dumps(report)}")


if __name__ == "__main__":
    main()
