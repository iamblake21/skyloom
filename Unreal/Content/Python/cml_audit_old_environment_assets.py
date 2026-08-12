import json
import os
import unreal


CANDIDATES = [
    "/Game/Migration/LandscapeGrass/LGT_CML_StarterIslandGrass",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_CliffPeach_ReferenceMatch_v1",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_DirtPath",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_GrassDeep",
    "/Game/Migration/LandscapeLayers/TL_StarterIsland_GrassSun",
    "/Game/Migration/LandscapeMaterials/MI_TD_StarterIsland",
    "/Game/Migration/LandscapeMaterials/MI_TerrainData_c7381312_f45e_4991_a939_1f289b31c874",
    "/Game/Migration/Masters/M_CML_Env_AtmosphericSky",
    "/Game/Migration/Masters/M_CML_Env_OriginalCliffMass",
    "/Game/Migration/Masters/M_CML_Env_StylizedWater",
    "/Game/Migration/Masters/M_CML_Env_TerrainReferenceMatch",
    "/Game/Migration/Masters/M_CML_Env_TerrainSplat",
    "/Game/Migration/Masters/M_CML_Env_VerticalRockAutoGrass",
    "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit/Materials/M_OriginalCliffMass",
    "/Game/Migrated/Project/Art/Environment/OriginalCliffMassKit/Materials/M_OriginalCliffMass_NoGrass",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/M_StarterIsland_Skybox",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/M_StarterIsland_Terrain",
    "/Game/Migrated/Project/Art/Environment/StarterIsland/Terrain/Materials/M_StarterIsland_TerrainWater",
]


def strings(values):
    return sorted(str(value) for value in values)


def main():
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.search_all_assets(True)
    options = unreal.AssetRegistryDependencyOptions(
        include_soft_package_references=True,
        include_hard_package_references=True,
        include_searchable_names=True,
        include_soft_management_references=True,
        include_hard_management_references=True,
    )
    report = []
    for path in CANDIDATES:
        exists = unreal.EditorAssetLibrary.does_asset_exist(path)
        rec = {"asset": path, "exists": exists}
        if exists:
            asset = unreal.EditorAssetLibrary.load_asset(path)
            rec["class"] = asset.get_class().get_name() if asset else None
            try:
                rec["referencers"] = strings(registry.get_referencers(path, options))
            except Exception as exc:
                rec["referencers_error"] = str(exc)
            try:
                rec["dependencies"] = strings(registry.get_dependencies(path, options))
            except Exception as exc:
                rec["dependencies_error"] = str(exc)
        report.append(rec)
    output = os.path.join(unreal.Paths.project_saved_dir(), "OldEnvironmentAssetAudit.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML Old Environment Audit] Wrote {output}")


if __name__ == "__main__":
    main()
