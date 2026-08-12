import json
import os

import unreal


SOURCE_ROOT = "/Game/Migrated/Project/Art/Environment/StarterIsland/Rocks"


def main():
    assets = unreal.EditorAssetLibrary.list_assets(SOURCE_ROOT, recursive=True, include_folder=False)
    records = []
    external_referencers = set()
    for asset in assets:
        referencers = unreal.EditorAssetLibrary.find_package_referencers_for_asset(
            asset,
            load_assets_to_confirm=True,
        )
        external = sorted(
            reference for reference in referencers
            if not reference.startswith(SOURCE_ROOT)
        )
        external_referencers.update(external)
        records.append({
            "asset": asset,
            "referencers": sorted(referencers),
            "externalReferencers": external,
        })

    report = {
        "sourceRoot": SOURCE_ROOT,
        "assetCount": len(assets),
        "externalReferencers": sorted(external_referencers),
        "safeToDelete": not external_referencers,
        "assets": records,
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "ReplacedRockAssetsAudit.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Replaced Rock Asset Audit] Wrote {output}")


if __name__ == "__main__":
    main()
