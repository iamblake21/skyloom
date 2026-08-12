import json
import os
import unreal


MAPS = [
    "/Game/_Project/Art/Environment/SoStylized/Maps/CompleteVol1/Demonstration_Vol1",
    "/Game/_Project/Art/Environment/SoStylized/Maps/Sky/Overview_Sky",
    "/Game/_Project/Art/Environment/SoStylized/Maps/Water/Demonstration_Water_Classic",
]

MATERIALS = [
    "/Game/_Project/Art/Environment/SoStylized/Environment/Landscape/Materials/MI_LandscapeVol1",
    "/Game/_Project/Art/Environment/SoStylized/Environment/Landscape/Materials/MI_Landscape",
    "/Game/_Project/Art/Environment/SoStylized/Environment/Water/Materials/Presets/Classic/MI_Water_Classic",
]


def safe_prop(obj, name):
    try:
        return obj.get_editor_property(name)
    except Exception:
        return None


def path_of(obj):
    return obj.get_path_name() if obj else None


def vector_record(value):
    return [value.x, value.y, value.z]


def rotator_record(value):
    return [value.roll, value.pitch, value.yaw]


def actor_record(actor):
    rec = {
        "label": actor.get_actor_label(),
        "name": actor.get_name(),
        "class": actor.get_class().get_name(),
        "path": actor.get_path_name(),
        "location": vector_record(actor.get_actor_location()),
        "rotation": rotator_record(actor.get_actor_rotation()),
        "scale": vector_record(actor.get_actor_scale3d()),
        "components": [],
    }
    for prop in ("landscape_material", "landscape_hole_material"):
        value = safe_prop(actor, prop)
        if value:
            rec[prop] = path_of(value)
    for comp in actor.get_components_by_class(unreal.ActorComponent):
        crec = {"name": comp.get_name(), "class": comp.get_class().get_name()}
        mesh = safe_prop(comp, "static_mesh")
        if mesh:
            crec["static_mesh"] = path_of(mesh)
        mats = []
        try:
            mats = [path_of(comp.get_material(i)) for i in range(comp.get_num_materials())]
        except Exception:
            pass
        if mats:
            crec["materials"] = mats
        rec["components"].append(crec)
    return rec


def relevant(actor):
    text = f"{actor.get_actor_label()} {actor.get_name()} {actor.get_class().get_name()}".lower()
    terms = ("sky", "sun", "light", "fog", "cloud", "water", "landscape", "grass")
    if any(term in text for term in terms):
        return True
    for comp in actor.get_components_by_class(unreal.ActorComponent):
        mesh = safe_prop(comp, "static_mesh")
        if mesh and any(term in path_of(mesh).lower() for term in terms):
            return True
        try:
            for i in range(comp.get_num_materials()):
                mat = path_of(comp.get_material(i)) or ""
                if any(term in mat.lower() for term in terms):
                    return True
        except Exception:
            pass
    return False


def material_record(asset_path):
    mat = unreal.EditorAssetLibrary.load_asset(asset_path)
    rec = {"asset": asset_path, "loaded": bool(mat)}
    if not mat:
        return rec
    lib = unreal.MaterialEditingLibrary
    methods = {
        "scalar": "get_scalar_parameter_names",
        "vector": "get_vector_parameter_names",
        "texture": "get_texture_parameter_names",
        "static_switch": "get_static_switch_parameter_names",
    }
    for key, method_name in methods.items():
        method = getattr(lib, method_name, None)
        if method:
            try:
                rec[key] = [str(x) for x in method(mat)]
            except Exception as exc:
                rec[key + "_error"] = str(exc)
    return rec


def main():
    report = {"maps": {}, "materials": [material_record(path) for path in MATERIALS]}
    for map_path in MAPS:
        if not unreal.EditorLoadingAndSavingUtils.load_map(map_path):
            report["maps"][map_path] = {"error": "load failed"}
            continue
        actors = [actor_record(a) for a in unreal.EditorLevelLibrary.get_all_level_actors() if relevant(a)]
        report["maps"][map_path] = {"actor_count": len(actors), "actors": actors}
    out = os.path.join(unreal.Paths.project_saved_dir(), "SoStylizedProbe.json")
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    unreal.log(f"[CML SoStylized Probe] Wrote {out}")


if __name__ == "__main__":
    main()
