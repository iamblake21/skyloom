import json
import os
import unreal


TARGET_MAP = "/Game/Maps/A_10_StarterIsland_AxisPreview"
INTRO_MAP = "/Game/Maps/A_01_IntroCinematic"
SO_ROOT = "/Game/_Project/Art/Environment/SoStylized/Environment"
EXPECTED = {
    "landscape": SO_ROOT + "/Landscape/Materials/MI_LandscapeVol1.MI_LandscapeVol1",
    "cliff": SO_ROOT + "/Rocks/Materials/Classic/MI_RockClassic_Cliff.MI_RockClassic_Cliff",
    "shelf": SO_ROOT + "/Rocks/Materials/Classic/MI_RockClassic_Shelves.MI_RockClassic_Shelves",
    "water": SO_ROOT + "/Water/Materials/Presets/Classic/MI_Water_Classic.MI_Water_Classic",
}


def main():
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.search_all_assets(True)
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors_api = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(TARGET_MAP):
        raise RuntimeError("AxisPreview no longer loads")
    actors = list(actors_api.get_all_level_actors())
    issues = []

    landscape = [a for a in actors if isinstance(a, unreal.Landscape) and a.get_actor_label() == "TerrainTop"]
    if len(landscape) != 1:
        issues.append(f"TerrainTop count is {len(landscape)}")
    else:
        path = landscape[0].get_editor_property("landscape_material").get_path_name()
        if path != EXPECTED["landscape"]:
            issues.append(f"Landscape material is {path}")

    sky = [a for a in actors if a.get_actor_label() == "ENV_SoStylized_Sky_Classic"]
    if len(sky) != 1 or sky[0].get_class().get_name() != "BP_StylizedSky_Classic_C":
        issues.append("Official Classic sky actor is missing or duplicated")
    old_labels = {"ENV_Sun", "CML_HeightFog", "CML_SkyAtmosphere", "CML_SkyLight"}
    remaining_old = sorted(a.get_actor_label() for a in actors if a.get_actor_label() in old_labels)
    if remaining_old:
        issues.append("Old environment actors remain: " + ", ".join(remaining_old))

    rock_counts = {"cliff": 0, "shelf": 0}
    wrong_rocks = []
    for actor in actors:
        for comp in actor.get_components_by_class(unreal.StaticMeshComponent):
            mesh = comp.get_editor_property("static_mesh")
            mesh_path = mesh.get_path_name() if mesh else ""
            kind = "cliff" if "REF_SM_CliffClassic" in mesh_path else "shelf" if "REF_SM_ShelfClassic" in mesh_path else None
            if not kind:
                continue
            rock_counts[kind] += 1
            for slot in range(comp.get_num_materials()):
                mat = comp.get_material(slot)
                path = mat.get_path_name() if mat else ""
                if path != EXPECTED[kind]:
                    wrong_rocks.append({"actor": actor.get_actor_label(), "mesh": mesh_path, "material": path})
    if rock_counts != {"cliff": 17, "shelf": 7}:
        issues.append(f"Unexpected Cliff/Shelf counts: {rock_counts}")
    if wrong_rocks:
        issues.append(f"{len(wrong_rocks)} Cliff/Shelf components have wrong materials")

    water = [a for a in actors if a.get_actor_label() == "ENV_SoStylized_Water_Pond"]
    if len(water) != 1:
        issues.append(f"SoStylized water actor count is {len(water)}")
    else:
        if water[0].get_class().get_name() != "BP_Classic_Water_C":
            issues.append(f"Water class is {water[0].get_class().get_name()}")
        comps = water[0].get_components_by_class(unreal.InstancedStaticMeshComponent)
        if len(comps) != 1:
            issues.append(f"Official water surface component count is {len(comps)}")
        else:
            mesh = comps[0].get_editor_property("static_mesh")
            mesh_path = mesh.get_path_name() if mesh else ""
            if "SM_WaterPlane_16" not in mesh_path:
                issues.append(f"Official water surface mesh is {mesh_path}")
            material = comps[0].get_material(0)
            path = material.get_path_name() if material else ""
            if "MI_Water_Classic" not in path:
                issues.append(f"Water material is {path}")

    grass_state = {"mesh": None, "materials": []}
    grass_type = unreal.load_asset(SO_ROOT + "/Landscape/LG_Grass")
    if not isinstance(grass_type, unreal.LandscapeGrassType):
        issues.append("Official LG_Grass is missing")
    else:
        varieties = grass_type.get_editor_property("grass_varieties")
        if len(varieties) != 1:
            issues.append(f"LG_Grass variety count is {len(varieties)}")
        else:
            mesh = varieties[0].get_editor_property("grass_mesh")
            grass_state["mesh"] = mesh.get_path_name() if mesh else None
            grass_state["materials"] = [
                material.get_path_name() if material else None
                for material in varieties[0].get_editor_property("override_materials")
            ]
            if not grass_state["mesh"] or "SM_Grass2" not in grass_state["mesh"]:
                issues.append(f"Landscape grass mesh is {grass_state['mesh']}")
            if len(grass_state["materials"]) != 3 or any(
                not path or "MI_Grass_NoRVT" not in path for path in grass_state["materials"]
            ):
                issues.append(f"Landscape grass materials are {grass_state['materials']}")

    old_name_fragments = (
        "/LandscapeGrass/LGT_CML_StarterIslandGrass",
        "/LandscapeMaterials/MI_TD_StarterIsland",
        "/Masters/M_CML_Env_AtmosphericSky",
        "/Masters/M_CML_Env_OriginalCliffMass",
        "/Masters/M_CML_Env_StylizedWater",
        "/Masters/M_CML_Env_TerrainSplat",
        "/Materials/M_OriginalCliffMass",
        "/Materials/M_StarterIsland_TerrainWater",
    )
    surviving_old_assets = []
    for data in registry.get_all_assets():
        package = str(data.package_name)
        if any(fragment in package for fragment in old_name_fragments):
            surviving_old_assets.append(package)
    if surviving_old_assets:
        issues.append("Superseded assets remain: " + ", ".join(sorted(surviving_old_assets)))

    intro_loaded = bool(level_editor.load_level(INTRO_MAP))
    if not intro_loaded:
        issues.append("IntroCinematic no longer loads")

    report = {
        "targetMap": TARGET_MAP,
        "introMap": INTRO_MAP,
        "introLoads": intro_loaded,
        "rockCounts": rock_counts,
        "wrongRocks": wrong_rocks,
        "grass": grass_state,
        "survivingOldAssets": surviving_old_assets,
        "issues": issues,
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "AxisPreviewSoStylizedValidation.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Validation] Wrote {output}")
    if issues:
        raise RuntimeError("; ".join(issues))


if __name__ == "__main__":
    main()
