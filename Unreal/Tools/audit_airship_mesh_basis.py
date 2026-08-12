import json
import os
import traceback
import unreal


ASSETS = (
    "/Game/_Project/Art/Vehicles/Airship/SM_Airship_Visual.SM_Airship_Visual",
    "/Game/_Project/Art/Vehicles/Airship/SM_Airship_RuntimeVisual.SM_Airship_RuntimeVisual",
    "/Game/_Project/Art/Vehicles/Airship/SM_Airship_RuntimeDoor.SM_Airship_RuntimeDoor",
)
REPORT_PATH = os.path.join(
    unreal.Paths.project_saved_dir(), "airship_mesh_basis_report.json")


def vector(value):
    return [value.x, value.y, value.z]


def main():
    report = {"success": False, "assets": []}
    try:
        for path in ASSETS:
            mesh = unreal.EditorAssetLibrary.load_asset(path)
            if not isinstance(mesh, unreal.StaticMesh):
                raise RuntimeError("Missing StaticMesh " + path)
            bounds = mesh.get_bounds()
            materials = []
            for index, slot in enumerate(
                    list(mesh.get_editor_property("static_materials") or [])):
                material = slot.get_editor_property("material_interface")
                materials.append({
                    "index": index,
                    "slot": str(slot.get_editor_property("material_slot_name")),
                    "material": material.get_path_name() if material else None,
                })
            report["assets"].append({
                "asset": path,
                "origin": vector(bounds.origin),
                "extent": vector(bounds.box_extent),
                "minimum": vector(bounds.origin - bounds.box_extent),
                "maximum": vector(bounds.origin + bounds.box_extent),
                "materials": materials,
            })
        report["success"] = True
    except Exception as exception:
        report["error"] = str(exception)
        report["traceback"] = traceback.format_exc()
        unreal.log_error(report["traceback"])
    finally:
        with open(REPORT_PATH, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)
        unreal.log("CML airship mesh basis report: " + REPORT_PATH)


main()
