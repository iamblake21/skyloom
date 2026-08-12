import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
LABEL = "ENV_SoStylized_Water_Pond"


def main():
    level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    actors_api = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    if not level_editor.load_level(MAP_PATH):
        raise RuntimeError(f"Could not load {MAP_PATH}")
    actors = [a for a in actors_api.get_all_level_actors() if a.get_actor_label() == LABEL]
    if len(actors) != 1 or actors[0].get_class().get_name() != "BP_Classic_Water_C":
        raise RuntimeError("The official water actor is missing")
    actor = actors[0]
    # Place the supported official grid on the preserved pond footprint.  The
    # landscape surface at its centre is Z=4299.54, so keep the water subtly
    # above it instead of leaving the blueprint buried at the old actor pivot.
    actor.set_actor_location(
        unreal.Vector(-7332.625732421875, 17754.23974609375, 4300.5), False, False
    )
    actor.set_actor_scale3d(
        unreal.Vector(2728.795166015625 / 400.0, 4102.66650390625 / 400.0, 1.0)
    )
    if not level_editor.save_current_level():
        raise RuntimeError(f"Could not save {MAP_PATH}")
    unreal.log("[CML SoStylized Water] Positioned official Classic water grid on preserved pond bounds")


if __name__ == "__main__":
    main()
