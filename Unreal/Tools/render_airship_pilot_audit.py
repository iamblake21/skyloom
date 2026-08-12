import bpy
import math
import os
import sys
from mathutils import Vector


def look_at(camera, target):
    direction = (target - camera.location).normalized()
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    source = os.path.abspath(args[0])
    destination = os.path.abspath(args[1])

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=source)

    for obj in bpy.context.scene.objects:
        upper = obj.name.upper()
        if upper.startswith(("COLMESH_", "SYS_")):
            obj.hide_render = True

    eye = bpy.data.objects.get("REF_PilotCamera")
    controls = bpy.data.objects.get("REF_PilotControls")
    if eye is None or controls is None:
        raise RuntimeError("AIR_Airship is missing its pilot reference nodes")

    camera_data = bpy.data.cameras.new("PilotAuditCamera")
    camera_data.lens = 24.0
    camera_data.sensor_width = 36.0
    camera_data.clip_start = 0.01
    camera = bpy.data.objects.new("PilotAuditCamera", camera_data)
    bpy.context.scene.collection.objects.link(camera)
    camera.matrix_world.translation = eye.matrix_world.translation

    # Follow the authored forward direction toward the controls, but retain the
    # eye's height so this audits the cockpit framing rather than looking down.
    target = controls.matrix_world.translation.copy()
    target.z = camera.location.z
    look_at(camera, target)

    sun_data = bpy.data.lights.new("AuditSun", type="SUN")
    sun_data.energy = 4.0
    sun = bpy.data.objects.new("AuditSun", sun_data)
    sun.rotation_euler = (math.radians(35.0), 0.0, math.radians(-35.0))
    bpy.context.scene.collection.objects.link(sun)

    world = bpy.data.worlds.new("AuditWorld")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (
        0.08, 0.14, 0.22, 1.0)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    bpy.context.scene.world = world

    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1920
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = destination
    scene.render.film_transparent = False
    bpy.ops.render.render(write_still=True)
    print(
        "CML_PILOT_AUDIT=" + destination
        + ";eye=" + str(tuple(round(v, 4) for v in camera.location))
        + ";controls=" + str(tuple(round(v, 4) for v in controls.matrix_world.translation)))


if __name__ == "__main__":
    main()
