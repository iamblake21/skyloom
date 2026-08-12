import bpy
import sys
from mathutils import Vector


source = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)
needles = (
    "ACCESSDOOR", "PILOTCONTROL", "PILOTCONSOLE",
    "NACELLE_PORT_NOZZLE", "NACELLE_STARBOARD_NOZZLE",
)
for obj in bpy.context.scene.objects:
    upper = obj.name.upper()
    if any(needle in upper for needle in needles):
        position = obj.matrix_world.translation
        if obj.type == "MESH":
            corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
            centre = sum(corners, Vector()) / len(corners)
        else:
            centre = position
        print(f"CML_PIVOT {obj.name} type={obj.type} world=({position.x:.4f},{position.y:.4f},{position.z:.4f}) centre=({centre.x:.4f},{centre.y:.4f},{centre.z:.4f}) parent={obj.parent.name if obj.parent else '-'}")
