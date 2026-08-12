import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
GRASS_LAYER = "/Game/_Project/Art/Environment/SoStylized/Environment/Landscape/LL_Grass"


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")
    landscapes = [
        actor for actor in actor_subsystem.get_all_level_actors()
        if isinstance(actor, unreal.Landscape) and actor.get_actor_label() == "TerrainTop"
    ]
    if len(landscapes) != 1:
        raise RuntimeError(f"Expected one TerrainTop, found {len(landscapes)}")
    layer = unreal.EditorAssetLibrary.load_asset(GRASS_LAYER)
    if not isinstance(layer, unreal.LandscapeLayerInfoObject):
        raise RuntimeError(f"Official SoStylized Grass layer is missing: {GRASS_LAYER}")
    landscape = landscapes[0]
    if not unreal.CMLLandscapeImportLibrary.fill_landscape_layer(landscape, layer, 255):
        raise RuntimeError("Could not fill the official SoStylized Grass layer")
    if not unreal.CMLLandscapeImportLibrary.refresh_landscape_materials(landscape):
        raise RuntimeError("Could not refresh the initialized Landscape")
    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save {MAP_PATH}")
    report = {
        "map": MAP_PATH,
        "landscape": landscape.get_actor_label(),
        "layer": layer.get_path_name(),
        "weight": 255,
    }
    output = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedLandscapeInitialization.json")
    with open(output, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Landscape] Wrote {output}")


if __name__ == "__main__":
    main()
