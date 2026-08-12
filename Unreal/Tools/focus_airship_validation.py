import json
import os
import unreal


MAP_PATH = "/Game/Maps/A_10_StarterIsland_AxisPreview"
REPORT_PATH = os.path.join(unreal.Paths.project_saved_dir(), "airship_validation_report.json")

level_editor = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
if unreal.EditorLevelLibrary.get_editor_world().get_path_name().split(":")[0] != MAP_PATH:
    level_editor.load_level(MAP_PATH)

matches = [actor for actor in actor_subsystem.get_all_level_actors() if actor.get_actor_label() == "PF_Airship"]
if len(matches) != 1 or not isinstance(matches[0], unreal.StaticMeshActor):
    raise RuntimeError(f"Expected one visual PF_Airship actor, got {matches}")

airship = matches[0]
# Blender's +Y is the Unity model's +Z (the ship's forward axis). Rotate it to
# Unreal +X, matching the Unity-to-Unreal coordinate conversion used elsewhere.
airship.set_actor_rotation(unreal.Rotator(pitch=0.0, yaw=-90.0, roll=0.0), False)
airship.modify()
actor_subsystem.set_selected_level_actors([airship])

component = airship.get_editor_property("static_mesh_component")
mesh = component.get_editor_property("static_mesh")
materials = [
    component.get_material(index).get_path_name() if component.get_material(index) else None
    for index in range(component.get_num_materials())
]

camera_location = unreal.Vector(-20500.0, -29800.0, 3450.0)
camera_rotation = unreal.Rotator(pitch=-21.0, yaw=49.0, roll=0.0)
unreal.EditorLevelLibrary.set_level_viewport_camera_info(camera_location, camera_rotation)

if not level_editor.save_current_level():
    raise RuntimeError("Could not save the final airship orientation")

report = {
    "map": MAP_PATH,
    "actor": airship.get_path_name(),
    "mesh": mesh.get_path_name() if mesh else None,
    "location": [airship.get_actor_location().x, airship.get_actor_location().y, airship.get_actor_location().z],
    "rotation": [airship.get_actor_rotation().pitch, airship.get_actor_rotation().yaw, airship.get_actor_rotation().roll],
    "scale": [airship.get_actor_scale3d().x, airship.get_actor_scale3d().y, airship.get_actor_scale3d().z],
    "materials": materials,
    "collision": str(component.get_collision_enabled()),
}
with open(REPORT_PATH, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
unreal.log(f"CML airship validation written to {REPORT_PATH}")
