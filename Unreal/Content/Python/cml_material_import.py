"""Convert Unity YAML materials into native Unreal material instances.

The converter creates a small, auditable family of Unreal master materials and
maps every Unity material to an instance while retaining Unity GUID/SHA/source
metadata. Custom Unity shaders are deliberately flagged for a later specialised
port; this pass provides a valid PBR fallback rather than pretending that HLSL
from another render pipeline can be copied byte-for-byte.
"""

from __future__ import annotations

import json
import os
import re
import traceback
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

import unreal

from cml_shader_port_library import WHITE, default_texture_path, ensure_default_textures


MASTER_ROOT = "/Game/Migration/Masters"
REPORT_SCHEMA = 1
UNITY_URP_LIT_GUID = "933532a4fcc9baf4fa0491de14d08ed7"
WHITE_TEXTURE = "/Engine/EngineResources/WhiteSquareTexture.WhiteSquareTexture"
NORMAL_TEXTURE = "/Engine/EngineMaterials/DefaultNormal.DefaultNormal"


@dataclass
class UnityTextureValue:
    guid: str = ""
    scale: tuple[float, float] = (1.0, 1.0)
    offset: tuple[float, float] = (0.0, 0.0)


@dataclass
class UnityMaterialSpec:
    name: str
    shader_guid: str
    render_type: str = "Opaque"
    render_queue: int = -1
    textures: dict[str, UnityTextureValue] = field(default_factory=dict)
    floats: dict[str, float] = field(default_factory=dict)
    colors: dict[str, tuple[float, float, float, float]] = field(default_factory=dict)


def _log(message: str) -> None:
    unreal.log(f"[CML Material Migration] {message}")


def _error(message: str) -> None:
    unreal.log_error(f"[CML Material Migration] {message}")


def _sanitize(value: str) -> str:
    value = re.sub(r"[^A-Za-z0-9_]", "_", value.strip())
    value = re.sub(r"_+", "_", value).strip("_") or "Asset"
    if value[0].isdigit():
        value = "A_" + value
    return value


def _destination_for(source: str) -> tuple[str, str]:
    path = PurePosixPath(source)
    parts = list(path.parts)
    if parts and parts[0].lower() == "assets":
        parts = parts[1:]
    if parts and parts[0] == "_Project":
        parts[0] = "Project"
    directory = [_sanitize(part) for part in parts[:-1]]
    destination = "/Game/Migrated"
    if directory:
        destination += "/" + "/".join(directory)
    return destination, _sanitize(Path(parts[-1]).stem)


def _parse_vector(text: str) -> tuple[float, float, float, float]:
    values: dict[str, float] = {}
    for name, value in re.findall(r"([rgba]):\s*([-+0-9.eE]+)", text):
        values[name] = float(value)
    return (
        values.get("r", 0.0),
        values.get("g", 0.0),
        values.get("b", 0.0),
        values.get("a", 1.0),
    )


def _parse_xy(text: str, default: tuple[float, float]) -> tuple[float, float]:
    values = {name: float(value) for name, value in re.findall(r"([xy]):\s*([-+0-9.eE]+)", text)}
    return values.get("x", default[0]), values.get("y", default[1])


def _parse_unity_material(path: Path) -> UnityMaterialSpec:
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    name = path.stem
    shader_guid = ""
    render_type = "Opaque"
    render_queue = -1
    section = ""
    current_texture = ""
    textures: dict[str, UnityTextureValue] = {}
    floats: dict[str, float] = {}
    colors: dict[str, tuple[float, float, float, float]] = {}

    for line in lines:
        stripped = line.strip()
        if stripped.startswith("m_Name:") and name == path.stem:
            name = stripped.split(":", 1)[1].strip() or path.stem
        elif stripped.startswith("m_Shader:"):
            match = re.search(r"guid:\s*([0-9a-fA-F]+)", stripped)
            shader_guid = match.group(1).lower() if match else ""
        elif stripped.startswith("m_CustomRenderQueue:"):
            try:
                render_queue = int(stripped.split(":", 1)[1].strip())
            except ValueError:
                render_queue = -1
        elif stripped.startswith("RenderType:"):
            render_type = stripped.split(":", 1)[1].strip()
        elif stripped == "m_TexEnvs:":
            section = "textures"
            current_texture = ""
        elif stripped == "m_Floats:":
            section = "floats"
            current_texture = ""
        elif stripped == "m_Colors:":
            section = "colors"
            current_texture = ""
        elif stripped.startswith("m_Ints:") or stripped.startswith("m_BuildTextureStacks:"):
            section = ""
            current_texture = ""
        elif section == "textures":
            property_match = re.match(r"-\s+([^:]+):\s*$", stripped)
            if property_match:
                current_texture = property_match.group(1)
                textures[current_texture] = UnityTextureValue()
                continue
            if current_texture and stripped.startswith("m_Texture:"):
                guid_match = re.search(r"fileID:\s*(-?[0-9]+)(?:,\s*guid:\s*([0-9a-fA-F]+))?", stripped)
                if guid_match and int(guid_match.group(1)) != 0 and guid_match.group(2):
                    textures[current_texture].guid = guid_match.group(2).lower()
            elif current_texture and stripped.startswith("m_Scale:"):
                textures[current_texture].scale = _parse_xy(stripped, (1.0, 1.0))
            elif current_texture and stripped.startswith("m_Offset:"):
                textures[current_texture].offset = _parse_xy(stripped, (0.0, 0.0))
        elif section == "floats":
            match = re.match(r"-\s+([^:]+):\s*([-+0-9.eE]+)\s*$", stripped)
            if match:
                floats[match.group(1)] = float(match.group(2))
        elif section == "colors":
            match = re.match(r"-\s+([^:]+):\s*\{(.*)\}\s*$", stripped)
            if match:
                colors[match.group(1)] = _parse_vector(match.group(2))

    return UnityMaterialSpec(
        name=name,
        shader_guid=shader_guid,
        render_type=render_type,
        render_queue=render_queue,
        textures=textures,
        floats=floats,
        colors=colors,
    )


def _expression(material: unreal.Material, expression_class, x: int, y: int):
    result = unreal.MaterialEditingLibrary.create_material_expression(
        material, expression_class, node_pos_x=x, node_pos_y=y
    )
    if not result:
        raise RuntimeError(f"Unable to create {expression_class} in {material.get_path_name()}")
    return result


def _connect(source, output: str, destination, input_name: str) -> None:
    if not unreal.MaterialEditingLibrary.connect_material_expressions(
        source, output, destination, input_name
    ):
        raise RuntimeError(
            f"Unable to connect {source.get_name()}.{output} -> "
            f"{destination.get_name()}.{input_name}"
        )


def _connect_property(source, output: str, property_name) -> None:
    if not unreal.MaterialEditingLibrary.connect_material_property(source, output, property_name):
        raise RuntimeError(f"Unable to connect material property {property_name}")


def _texture_parameter(material, name: str, texture, sampler_type, x: int, y: int):
    node = _expression(material, unreal.MaterialExpressionTextureSampleParameter2D, x, y)
    node.set_editor_property("parameter_name", name)
    node.set_editor_property("texture", texture)
    node.set_editor_property("sampler_type", sampler_type)
    return node


def _scalar_parameter(material, name: str, value: float, x: int, y: int):
    node = _expression(material, unreal.MaterialExpressionScalarParameter, x, y)
    node.set_editor_property("parameter_name", name)
    node.set_editor_property("default_value", value)
    return node


def _vector_parameter(material, name: str, value, x: int, y: int):
    node = _expression(material, unreal.MaterialExpressionVectorParameter, x, y)
    node.set_editor_property("parameter_name", name)
    node.set_editor_property("default_value", unreal.LinearColor(*value))
    return node


def _create_master(asset_name: str, blend_mode, unlit: bool) -> unreal.Material:
    object_path = f"{MASTER_ROOT}/{asset_name}.{asset_name}"
    existing = unreal.EditorAssetLibrary.load_asset(object_path)
    if isinstance(existing, unreal.Material):
        # Rebuild rather than reuse: keeping a previously generated master meant
        # a fix to this function could never reach an already-migrated project,
        # and the graph would silently stay on the old, broken wiring.
        unreal.EditorAssetLibrary.delete_asset(f"{MASTER_ROOT}/{asset_name}")

    material = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
        asset_name,
        MASTER_ROOT,
        unreal.Material,
        unreal.MaterialFactoryNew(),
    )
    if not isinstance(material, unreal.Material):
        raise RuntimeError(f"Unable to create master material {object_path}")
    material.set_editor_property("blend_mode", blend_mode)
    material.set_editor_property(
        "shading_model",
        unreal.MaterialShadingModel.MSM_UNLIT
        if unlit
        else unreal.MaterialShadingModel.MSM_DEFAULT_LIT,
    )

    # Unreal rejects an sRGB texture bound to a linear sampler, so the colour
    # and the linear slots need different default assets. Engine's white square
    # is sRGB only, which is why the linear twin is imported by the port library.
    white = unreal.EditorAssetLibrary.load_asset(
        default_texture_path(WHITE, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR)
    )
    white_linear = unreal.EditorAssetLibrary.load_asset(
        default_texture_path(WHITE, unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR)
    )
    normal = unreal.EditorAssetLibrary.load_asset(NORMAL_TEXTURE)
    if not all(isinstance(item, unreal.Texture) for item in (white, white_linear, normal)):
        raise RuntimeError("Unable to load Unreal default material textures")

    base_texture = _texture_parameter(
        material, "BaseTexture", white, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, -900, -250
    )
    base_color = _vector_parameter(material, "BaseColor", (1.0, 1.0, 1.0, 1.0), -900, -100)
    base_multiply = _expression(material, unreal.MaterialExpressionMultiply, -620, -180)
    _connect(base_texture, "RGB", base_multiply, "A")
    _connect(base_color, "RGB", base_multiply, "B")

    emission_texture = _texture_parameter(
        material, "EmissionTexture", white, unreal.MaterialSamplerType.SAMPLERTYPE_COLOR, -900, 520
    )
    emission_color = _vector_parameter(material, "EmissionColor", (0.0, 0.0, 0.0, 1.0), -900, 650)
    emission_multiply = _expression(material, unreal.MaterialExpressionMultiply, -620, 580)
    _connect(emission_texture, "RGB", emission_multiply, "A")
    _connect(emission_color, "RGB", emission_multiply, "B")

    if unlit:
        emissive_add = _expression(material, unreal.MaterialExpressionAdd, -350, 50)
        _connect(base_multiply, "", emissive_add, "A")
        _connect(emission_multiply, "", emissive_add, "B")
        _connect_property(emissive_add, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    else:
        _connect_property(base_multiply, "", unreal.MaterialProperty.MP_BASE_COLOR)
        _connect_property(emission_multiply, "", unreal.MaterialProperty.MP_EMISSIVE_COLOR)
        metallic = _scalar_parameter(material, "Metallic", 0.0, -500, 130)
        roughness = _scalar_parameter(material, "Roughness", 0.7, -500, 220)
        normal_texture = _texture_parameter(
            material, "NormalTexture", normal, unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL, -700, 320
        )
        ao_texture = _texture_parameter(
            material,
            "OcclusionTexture",
            white_linear,
            unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR,
            -700,
            430,
        )
        _connect_property(metallic, "", unreal.MaterialProperty.MP_METALLIC)
        _connect_property(roughness, "", unreal.MaterialProperty.MP_ROUGHNESS)
        _connect_property(normal_texture, "RGB", unreal.MaterialProperty.MP_NORMAL)
        _connect_property(ao_texture, "R", unreal.MaterialProperty.MP_AMBIENT_OCCLUSION)

    opacity_multiply = _expression(material, unreal.MaterialExpressionMultiply, -350, -320)
    _connect(base_texture, "A", opacity_multiply, "A")
    _connect(base_color, "A", opacity_multiply, "B")
    if blend_mode == unreal.BlendMode.BLEND_MASKED:
        material.set_editor_property("opacity_mask_clip_value", 0.3333)
        _connect_property(opacity_multiply, "", unreal.MaterialProperty.MP_OPACITY_MASK)
    elif blend_mode == unreal.BlendMode.BLEND_TRANSLUCENT:
        _connect_property(opacity_multiply, "", unreal.MaterialProperty.MP_OPACITY)

    unreal.MaterialEditingLibrary.layout_material_expressions(material)
    unreal.MaterialEditingLibrary.recompile_material(material)
    unreal.EditorAssetLibrary.set_metadata_tag(material, "CML.Schema", "UnityFallbackPBR.v1")
    unreal.EditorAssetLibrary.save_loaded_asset(material, only_if_is_dirty=False)
    return material


def _create_masters() -> dict[str, unreal.Material]:
    return {
        "lit-opaque": _create_master("M_CML_UnityLit_Opaque", unreal.BlendMode.BLEND_OPAQUE, False),
        "lit-masked": _create_master("M_CML_UnityLit_Masked", unreal.BlendMode.BLEND_MASKED, False),
        "lit-translucent": _create_master(
            "M_CML_UnityLit_Translucent", unreal.BlendMode.BLEND_TRANSLUCENT, False
        ),
        "unlit-opaque": _create_master(
            "M_CML_UnityUnlit_Opaque", unreal.BlendMode.BLEND_OPAQUE, True
        ),
        "unlit-masked": _create_master(
            "M_CML_UnityUnlit_Masked", unreal.BlendMode.BLEND_MASKED, True
        ),
        "unlit-translucent": _create_master(
            "M_CML_UnityUnlit_Translucent", unreal.BlendMode.BLEND_TRANSLUCENT, True
        ),
    }


def _first_color(spec: UnityMaterialSpec, names: tuple[str, ...], default):
    for name in names:
        if name in spec.colors:
            return spec.colors[name]
    return default


def _first_float(spec: UnityMaterialSpec, names: tuple[str, ...], default: float) -> float:
    for name in names:
        if name in spec.floats:
            return spec.floats[name]
    return default


def _first_texture(spec: UnityMaterialSpec, names: tuple[str, ...]) -> UnityTextureValue | None:
    for name in names:
        value = spec.textures.get(name)
        if value and value.guid:
            return value
    return None


def _shader_name(shader_guid: str, shader_sources: dict[str, Path]) -> str:
    source = shader_sources.get(shader_guid)
    if source and source.is_file():
        match = re.search(r'\bShader\s+"([^"]+)"', source.read_text(encoding="utf-8", errors="replace"))
        if match:
            return match.group(1)
        return source.stem
    if shader_guid == UNITY_URP_LIT_GUID:
        return "Universal Render Pipeline/Lit"
    return f"Unknown/{shader_guid or 'missing'}"


def _classification(spec: UnityMaterialSpec, shader_name: str) -> str:
    lower_shader = shader_name.lower()
    unlit = any(token in lower_shader for token in ("unlit", "sky", "deep space", "star streak", "warp"))
    alpha_clip = _first_float(spec, ("_AlphaClip", "_AlphaTest", "_UseAlphaClipping"), 0.0) > 0.5
    transparent = (
        spec.render_type.lower() in {"transparent", "transparentcutout"}
        or spec.render_queue >= 3000
        or _first_float(spec, ("_Surface",), 0.0) > 0.5
    )
    if alpha_clip or spec.render_type.lower() == "transparentcutout":
        return "lit-masked" if not unlit else "unlit-masked"
    if transparent:
        return "unlit-translucent" if unlit else "lit-translucent"
    return "unlit-opaque" if unlit else "lit-opaque"


def _resolve_texture(value: UnityTextureValue | None, texture_objects: dict[str, str]):
    if not value or not value.guid:
        return None
    object_path = texture_objects.get(value.guid)
    if not object_path:
        return None
    asset = unreal.EditorAssetLibrary.load_asset(object_path)
    return asset if isinstance(asset, unreal.Texture) else None


def _set_instance_parameters(instance, spec, texture_objects) -> dict:
    base_color = _first_color(spec, ("_BaseColor", "_Color", "_Tint", "_BaseTint"), (1, 1, 1, 1))
    emission_color = _first_color(spec, ("_EmissionColor", "_EmissiveColor"), (0, 0, 0, 1))
    smoothness = max(0.0, min(1.0, _first_float(spec, ("_Smoothness", "_Glossiness"), 0.3)))
    metallic = max(0.0, min(1.0, _first_float(spec, ("_Metallic",), 0.0)))
    unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
        instance, "BaseColor", unreal.LinearColor(*base_color)
    )
    unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
        instance, "EmissionColor", unreal.LinearColor(*emission_color)
    )
    unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(instance, "Roughness", 1.0 - smoothness)
    unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(instance, "Metallic", metallic)

    mappings = {
        "BaseTexture": _first_texture(spec, ("_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo")),
        "NormalTexture": _first_texture(spec, ("_BumpMap", "_NormalMap", "_NormalTex")),
        "OcclusionTexture": _first_texture(spec, ("_OcclusionMap", "_AOMap")),
        "EmissionTexture": _first_texture(spec, ("_EmissionMap", "_EmissiveMap")),
    }
    resolved: dict[str, str] = {}
    unresolved: dict[str, str] = {}
    for parameter, value in mappings.items():
        texture = _resolve_texture(value, texture_objects)
        if texture:
            unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
                instance, parameter, texture
            )
            resolved[parameter] = texture.get_path_name()
        elif value and value.guid:
            unresolved[parameter] = value.guid
    return {
        "baseColor": base_color,
        "emissionColor": emission_color,
        "metallic": metallic,
        "roughness": 1.0 - smoothness,
        "resolvedTextures": resolved,
        "unresolvedTextures": unresolved,
    }


def _load_shader_ports(project_dir: Path) -> dict[str, dict]:
    """Unity shader name -> the natively ported Unreal master, if one exists."""
    port_report = project_dir / "Migration" / "unity_shader_port_report.json"
    if not port_report.is_file():
        return {}
    report = json.loads(port_report.read_text(encoding="utf-8"))
    return {
        item["unityShader"]: {"master": item["master"], "parameters": set(item["parameters"])}
        for item in report["results"]
        if item.get("status") == "ported"
    }


def _apply_ported_parameters(
    instance: unreal.MaterialInstanceConstant,
    spec: UnityMaterialSpec,
    texture_objects: dict[str, str],
    allowed: set[str],
) -> dict:
    """Copy the Unity `.mat` values onto a natively ported master.

    The ported masters declare their parameters under the Unity property names,
    so this is a direct transfer with no translation table. Anything the Unity
    material declares but the port does not expose is reported rather than
    silently discarded.
    """
    applied: dict[str, object] = {}
    skipped: list[str] = []
    unresolved: dict[str, str] = {}

    for name, value in spec.floats.items():
        if name in allowed:
            unreal.MaterialEditingLibrary.set_material_instance_scalar_parameter_value(
                instance, name, float(value)
            )
            applied[name] = value
        else:
            skipped.append(name)

    for name, value in spec.colors.items():
        if name in allowed:
            unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
                instance, name, unreal.LinearColor(*value)
            )
            applied[name] = list(value)
        else:
            skipped.append(name)

    for name, value in spec.textures.items():
        scale_offset_name = f"{name}_ST"
        if scale_offset_name in allowed:
            unreal.MaterialEditingLibrary.set_material_instance_vector_parameter_value(
                instance,
                scale_offset_name,
                unreal.LinearColor(value.scale[0], value.scale[1], value.offset[0], value.offset[1]),
            )
            applied[scale_offset_name] = [*value.scale, *value.offset]
        if not value.guid:
            continue
        if name not in allowed:
            skipped.append(name)
            continue
        texture = _resolve_texture(value, texture_objects)
        if texture is None:
            unresolved[name] = value.guid
            continue
        unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(
            instance, name, texture
        )
        applied[name] = texture.get_path_name()

    # Unity clips per material via _Cutoff; Unreal's clip threshold is a base
    # property, so it has to be overridden on the instance to stay per-material.
    cutoff = spec.floats.get("_Cutoff")
    if cutoff is not None:
        overrides = instance.get_editor_property("base_property_overrides")
        overrides.set_editor_property("override_opacity_mask_clip_value", True)
        overrides.set_editor_property("opacity_mask_clip_value", float(cutoff))
        instance.set_editor_property("base_property_overrides", overrides)
        applied["OpacityMaskClipValue"] = float(cutoff)

    return {
        "applied": applied,
        "unmappedUnityProperties": sorted(set(skipped)),
        "unresolvedTextures": unresolved,
    }


def main() -> int:
    project_dir = Path(unreal.Paths.project_dir())
    manifest_path = project_dir / "Migration" / "unity_asset_manifest.json"
    asset_report_path = project_dir / "Migration" / "unity_asset_import_report.json"
    report_path = project_dir / "Migration" / "unity_material_import_report.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    asset_report = json.loads(asset_report_path.read_text(encoding="utf-8"))
    unity_root = Path(manifest["unityRoot"])
    limit = int(os.environ.get("CML_MATERIAL_LIMIT", "0") or 0)

    entries_by_source = {entry["source"]: entry for entry in manifest["entries"]}
    texture_objects: dict[str, str] = {}
    for result in asset_report["results"]:
        if result["status"] != "imported" or not result["objects"]:
            continue
        # Textures extracted out of Unity `.asset` YAML are registered in the
        # asset report under their own kind: the manifest still classifies them
        # as `unity-asset`, so trust the import result, not the manifest entry.
        if result.get("kind") == "embedded-texture":
            texture_objects[result["guid"]] = result["objects"][0]
            continue
        entry = entries_by_source.get(result["source"])
        if entry and entry["kind"] == "texture":
            texture_objects[entry["guid"]] = result["objects"][0]
    shader_sources = {
        entry["guid"]: unity_root / Path(entry["source"])
        for entry in manifest["entries"]
        if entry["kind"] == "unity-shader"
    }
    material_entries = [entry for entry in manifest["entries"] if entry["kind"] == "unity-material"]
    if limit > 0:
        material_entries = material_entries[:limit]

    ensure_default_textures()
    masters = _create_masters()
    shader_ports = _load_shader_ports(project_dir)
    asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
    results: list[dict] = []
    _log(
        f"Converting {len(material_entries)} Unity materials "
        f"({len(shader_ports)} custom shaders have a native port)"
    )
    for index, entry in enumerate(material_entries, 1):
        source_path = unity_root / Path(entry["source"])
        try:
            spec = _parse_unity_material(source_path)
            shader_name = _shader_name(spec.shader_guid, shader_sources)
            classification = _classification(spec, shader_name)
            destination_path, destination_name = _destination_for(entry["source"])
            object_path = f"{destination_path}/{destination_name}.{destination_name}"
            instance = unreal.EditorAssetLibrary.load_asset(object_path)
            if not isinstance(instance, unreal.MaterialInstanceConstant):
                instance = asset_tools.create_asset(
                    destination_name,
                    destination_path,
                    unreal.MaterialInstanceConstant,
                    unreal.MaterialInstanceConstantFactoryNew(),
                )
            if not isinstance(instance, unreal.MaterialInstanceConstant):
                raise RuntimeError(f"Unable to create {object_path}")

            port = shader_ports.get(shader_name)
            if port is None:
                unreal.MaterialEditingLibrary.set_material_instance_parent(
                    instance, masters[classification]
                )
                parameters = _set_instance_parameters(instance, spec, texture_objects)
                parent_object = masters[classification].get_path_name()
                port_kind = "fallback-pbr"
            else:
                master = unreal.EditorAssetLibrary.load_asset(port["master"])
                if not isinstance(master, unreal.Material):
                    raise RuntimeError(f"Ported master missing: {port['master']}")
                unreal.MaterialEditingLibrary.set_material_instance_parent(instance, master)
                parameters = _apply_ported_parameters(
                    instance, spec, texture_objects, port["parameters"]
                )
                parent_object = port["master"]
                port_kind = "native-port"

            unreal.EditorAssetLibrary.set_metadata_tag(instance, "CML.UnityGuid", entry["guid"])
            unreal.EditorAssetLibrary.set_metadata_tag(instance, "CML.UnitySha256", entry["sha256"])
            unreal.EditorAssetLibrary.set_metadata_tag(instance, "CML.UnitySource", entry["source"])
            unreal.EditorAssetLibrary.set_metadata_tag(instance, "CML.UnityShader", shader_name)
            unreal.EditorAssetLibrary.save_loaded_asset(instance, only_if_is_dirty=False)
            custom_shader = spec.shader_guid != UNITY_URP_LIT_GUID
            results.append(
                {
                    "source": entry["source"],
                    "guid": entry["guid"],
                    "sha256": entry["sha256"],
                    "status": "converted",
                    "object": instance.get_path_name(),
                    "shaderGuid": spec.shader_guid,
                    "shaderName": shader_name,
                    "classification": classification,
                    "parent": parent_object,
                    "portKind": port_kind,
                    "requiresSpecializedShaderPort": custom_shader and port_kind != "native-port",
                    "parameters": parameters,
                }
            )
        except Exception as exception:
            _error(f"{entry['source']}: {exception}")
            results.append(
                {
                    "source": entry["source"],
                    "guid": entry["guid"],
                    "sha256": entry["sha256"],
                    "status": "failed",
                    "error": str(exception),
                }
            )
        if index % 16 == 0:
            unreal.SystemLibrary.collect_garbage()

    report = {
        "schema": REPORT_SCHEMA,
        "requested": len(material_entries),
        "converted": sum(item["status"] == "converted" for item in results),
        "failed": sum(item["status"] != "converted" for item in results),
        "nativePorts": sum(item.get("portKind") == "native-port" for item in results),
        "specializedPortsRequired": sum(
            item.get("requiresSpecializedShaderPort", False) for item in results
        ),
        "masters": {name: material.get_path_name() for name, material in masters.items()},
        "portedMasters": {name: port["master"] for name, port in sorted(shader_ports.items())},
        "results": results,
    }
    temporary_path = report_path.with_suffix(".json.tmp")
    temporary_path.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temporary_path.replace(report_path)
    # Material migration must not save an unrelated dirty editor level.
    unreal.EditorLoadingAndSavingUtils.save_dirty_packages(False, True)
    _log(
        f"Complete: converted={report['converted']}, failed={report['failed']}, "
        f"specialized={report['specializedPortsRequired']}"
    )
    return 0 if report["failed"] == 0 else 2


try:
    _exit_code = main()
except Exception:
    _error(traceback.format_exc())
    _exit_code = 1

if _exit_code:
    _error(f"CML_MATERIAL_IMPORT_FAILED code={_exit_code}")
else:
    _log("CML_MATERIAL_IMPORT_SUCCEEDED")
