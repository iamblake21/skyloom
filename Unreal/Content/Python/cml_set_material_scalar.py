"""Set one material-instance scalar from environment for deterministic QA."""

from __future__ import annotations

import os

import unreal


def main() -> None:
    object_path = os.environ["CML_MATERIAL_OBJECT"]
    parameter = os.environ["CML_MATERIAL_SCALAR"]
    value = float(os.environ["CML_MATERIAL_VALUE"])
    material = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(material, unreal.MaterialInstanceConstant):
        raise RuntimeError(f"Not a MaterialInstanceConstant: {object_path}")
    unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(
        material, parameter, value
    )
    resolved = unreal.MaterialEditingLibrary.get_material_instance_scalar_parameter_value(
        material, parameter
    )
    if abs(float(resolved) - value) > 1.0e-5:
        raise RuntimeError(
            f"Unable to set {parameter} on {object_path}: resolved {resolved}"
        )
    unreal.EditorAssetLibrary.save_loaded_asset(material, only_if_is_dirty=False)
    unreal.log(
        f"CML_SET_MATERIAL_SCALAR_SUCCEEDED object={object_path} "
        f"parameter={parameter} value={value}"
    )


main()
