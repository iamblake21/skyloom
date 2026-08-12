"""Builder used to port Unity URP shaders into native Unreal master materials.

Design note
-----------
The Unity shaders are real HLSL, not shader-graph node soup: they contain value
noise, triplanar projection, wind deformation and hand-written lighting terms.
Rebuilding that out of Unreal material nodes would be a re-interpretation, not a
port, so the ported bodies live in `Shaders/*.ush` and are called from material
`Custom` expressions. Texture sampling stays in real sampler nodes so every
Unity texture property remains an overridable Unreal texture parameter.

Unreal parameter names are the Unity property names verbatim (`_BaseColor`,
`_LandmassWorldSize`, ...). That keeps material-instance population a mechanical
copy of the Unity `.mat` values with no translation table to drift out of date.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import unreal


MASTER_ROOT = "/Game/Migration/Masters"
DEFAULT_TEXTURE_ROOT = "/Game/Migration/DefaultTextures"
COMMON_INCLUDE = "/CML/CMLMaterialCommon.ush"

# Unity's built-in `"white"`, `"gray"`, `"black"` and `"bump"` property defaults
# have no exact Engine-content equivalent, so they are imported once from
# Migration/DefaultTextures and referenced by every ported master. A wrong
# fallback would silently change any material that leaves a map unassigned.
#
# Unreal validates that a sampler's declared type matches the bound texture's
# colour space, so each default exists twice: sRGB for colour samplers and
# linear for mask/linear-colour samplers.
FLAT_NORMAL_TEXTURE = "/Engine/EngineMaterials/DefaultNormal.DefaultNormal"

WHITE = "white"
GREY = "grey"
BLACK = "black"
CLEAR = "clear"
FLAT_NORMAL = "normal"

_DEFAULT_ASSET_NAMES = {
    WHITE: "T_CML_UnityWhite",
    GREY: "T_CML_UnityGrey",
    BLACK: "T_CML_UnityBlack",
    CLEAR: "T_CML_UnityClear",
}
_LINEAR_SAMPLERS = {
    unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR,
    unreal.MaterialSamplerType.SAMPLERTYPE_MASKS,
    unreal.MaterialSamplerType.SAMPLERTYPE_GRAYSCALE,
    unreal.MaterialSamplerType.SAMPLERTYPE_ALPHA,
}


def default_texture_path(token: str, sampler) -> str:
    """Resolve a Unity property default to the asset matching `sampler`."""
    if token == FLAT_NORMAL or sampler == unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL:
        return FLAT_NORMAL_TEXTURE
    name = _DEFAULT_ASSET_NAMES.get(token)
    if name is None:
        raise RuntimeError(f"Unknown Unity texture default {token!r}")
    if sampler in _LINEAR_SAMPLERS:
        name += "_Linear"
    return f"{DEFAULT_TEXTURE_ROOT}/{name}.{name}"

SAMPLER_COLOR = unreal.MaterialSamplerType.SAMPLERTYPE_COLOR
SAMPLER_LINEAR = unreal.MaterialSamplerType.SAMPLERTYPE_LINEAR_COLOR
SAMPLER_NORMAL = unreal.MaterialSamplerType.SAMPLERTYPE_NORMAL
SAMPLER_MASKS = unreal.MaterialSamplerType.SAMPLERTYPE_MASKS

FLOAT1 = unreal.CustomMaterialOutputType.CMOT_FLOAT1
FLOAT2 = unreal.CustomMaterialOutputType.CMOT_FLOAT2
FLOAT3 = unreal.CustomMaterialOutputType.CMOT_FLOAT3
FLOAT4 = unreal.CustomMaterialOutputType.CMOT_FLOAT4


def enum_value(enum_type, *candidate_names: str):
    """Resolve an enum member by any of its possible Python spellings.

    Unreal derives Python enum names from the C++ identifiers, so a value such
    as `BLEND_AlphaComposite` becomes `BLEND_ALPHA_COMPOSITE`. Guessing wrong
    fails deep inside a port with an opaque AttributeError, so the candidates
    are tried explicitly and the available members are reported on failure.
    """
    for name in candidate_names:
        value = getattr(enum_type, name, None)
        if value is not None:
            return value
    available = sorted(name for name in dir(enum_type) if name.isupper())
    raise RuntimeError(
        f"None of {candidate_names} exist on {enum_type.__name__}; available: {available}"
    )


BLEND_ALPHA_COMPOSITE = enum_value(
    unreal.BlendMode, "BLEND_ALPHA_COMPOSITE", "BLEND_ALPHACOMPOSITE", "BLEND_ALPHA_COMPOSITE_PREMULTIPLIED_ALPHA"
)


def load_texture(object_path: str) -> unreal.Texture:
    asset = unreal.EditorAssetLibrary.load_asset(object_path)
    if not isinstance(asset, unreal.Texture):
        raise RuntimeError(f"Not a texture: {object_path}")
    return asset


def ensure_default_textures() -> list[str]:
    """Import the Unity property-default textures once; idempotent.

    Each source PNG produces an sRGB asset and a `_Linear` twin so that every
    sampler type in the ported masters can be given a default whose colour
    space Unreal accepts.
    """
    import pathlib

    source_root = pathlib.Path(unreal.Paths.project_dir()) / "Migration" / "DefaultTextures"
    imported: list[str] = []
    for png in sorted(source_root.glob("*.png")):
        for suffix, srgb in (("", True), ("_Linear", False)):
            name = f"{png.stem}{suffix}"
            package = f"{DEFAULT_TEXTURE_ROOT}/{name}"
            if unreal.EditorAssetLibrary.does_asset_exist(package):
                continue
            task = unreal.AssetImportTask()
            task.set_editor_property("filename", str(png))
            task.set_editor_property("destination_path", DEFAULT_TEXTURE_ROOT)
            task.set_editor_property("destination_name", name)
            task.set_editor_property("automated", True)
            task.set_editor_property("replace_existing", True)
            task.set_editor_property("save", True)
            unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
            texture = unreal.EditorAssetLibrary.load_asset(f"{package}.{name}")
            if not isinstance(texture, unreal.Texture):
                raise RuntimeError(f"Unable to import default texture {png} as {name}")
            texture.set_editor_property("srgb", srgb)
            # Point sampling on a 4x4 constant keeps these exact under mip bias.
            texture.set_editor_property("filter", unreal.TextureFilter.TF_NEAREST)
            texture.set_editor_property("lod_group", unreal.TextureGroup.TEXTUREGROUP_UI)
            unreal.EditorAssetLibrary.save_loaded_asset(texture, only_if_is_dirty=False)
            imported.append(f"{package}.{name}")
    return imported


@dataclass
class ParameterSpec:
    """One Unity material property exposed as an Unreal parameter."""

    name: str
    kind: str  # "scalar" | "vector" | "texture"
    default: object
    sampler: object = None
    group: str = "Unity"


@dataclass
class PortResult:
    unity_shader: str
    master_object: str
    parameters: list[str] = field(default_factory=list)


class MasterMaterialBuilder:
    """Creates one Unreal master material for one ported Unity shader."""

    def __init__(
        self,
        asset_name: str,
        *,
        unity_shader: str,
        blend_mode=unreal.BlendMode.BLEND_OPAQUE,
        shading_model=unreal.MaterialShadingModel.MSM_DEFAULT_LIT,
        two_sided: bool = False,
        opacity_mask_clip_value: float = 0.3333,
        include_files: tuple[str, ...] = (),
    ) -> None:
        self.asset_name = asset_name
        self.unity_shader = unity_shader
        self.includes = (COMMON_INCLUDE,) + tuple(include_files)
        self.parameter_names: list[str] = []
        self._cursor_y = -1200

        object_path = f"{MASTER_ROOT}/{asset_name}.{asset_name}"
        existing = unreal.EditorAssetLibrary.load_asset(object_path)
        if isinstance(existing, unreal.Material):
            # Never delete a master that already has migrated material
            # instances as referencers.  Deleting/recreating the package leaves
            # every loaded instance pointing at an orphaned UObject until the
            # entire material import is rerun; in practice that made the map
            # render with grey/default materials after every shader iteration.
            # Rebuild only the expression graph so the package identity and all
            # references remain stable.
            unreal.MaterialEditingLibrary.delete_all_material_expressions(existing)
            material = existing
        else:
            material = unreal.AssetToolsHelpers.get_asset_tools().create_asset(
                asset_name, MASTER_ROOT, unreal.Material, unreal.MaterialFactoryNew()
            )
        if not isinstance(material, unreal.Material):
            raise RuntimeError(f"Unable to create master material {object_path}")

        material.set_editor_property("blend_mode", blend_mode)
        material.set_editor_property("shading_model", shading_model)
        material.set_editor_property("two_sided", two_sided)
        material.set_editor_property("opacity_mask_clip_value", opacity_mask_clip_value)
        self.material = material

    # ---------------------------------------------------------------- layout

    def _next_position(self, step: int = 140) -> tuple[int, int]:
        y = self._cursor_y
        self._cursor_y += step
        return -1500, y

    def _expression(self, expression_class, x: int | None = None, y: int | None = None):
        if x is None or y is None:
            x, y = self._next_position()
        node = unreal.MaterialEditingLibrary.create_material_expression(
            self.material, expression_class, node_pos_x=x, node_pos_y=y
        )
        if not node:
            raise RuntimeError(f"Unable to create {expression_class} in {self.asset_name}")
        return node

    # ------------------------------------------------------------ parameters

    def scalar(self, name: str, default: float, group: str = "Unity"):
        node = self._expression(unreal.MaterialExpressionScalarParameter)
        node.set_editor_property("parameter_name", name)
        node.set_editor_property("default_value", float(default))
        node.set_editor_property("group", group)
        self.parameter_names.append(name)
        return node

    def vector(self, name: str, default, group: str = "Unity"):
        node = self._expression(unreal.MaterialExpressionVectorParameter)
        node.set_editor_property("parameter_name", name)
        node.set_editor_property("default_value", unreal.LinearColor(*default))
        node.set_editor_property("group", group)
        self.parameter_names.append(name)
        return node

    def vector4(self, name: str, default, group: str = "Unity"):
        """A vector parameter delivered to Custom nodes as a genuine float4.

        A VectorParameter's default output is masked to RGB, so a Custom node
        input fed from it is typed float3 and any `.a` / `.zw` swizzle fails to
        compile. Unity properties that carry data in W (`_BaseMap_ST`, alpha in
        `_BaseColor`) must therefore be re-appended into a four-component value.
        """
        parameter = self.vector(name, default, group)
        appended = self._expression(unreal.MaterialExpressionAppendVector)
        # UE 5.8 may report a successful editor connection for the unnamed
        # VectorParameter output but later compile the AppendVector with an
        # empty A input.  Bind the explicit RGB pin so the float4 survives
        # material translation deterministically.
        self.connect(parameter, "RGB", appended, "A")
        self.connect(parameter, "A", appended, "B")
        return appended

    def texture(
        self,
        name: str,
        default_texture: str = WHITE,
        sampler=SAMPLER_COLOR,
        uv=None,
        group: str = "Unity",
        register: bool = True,
    ):
        """A texture parameter sampler.

        Triplanar ports sample one Unity texture at several projections. Unreal
        resolves same-named texture parameters to one value, so each projection
        gets its own sampler node sharing the parameter name; only the first is
        registered as a parameter to keep the report free of duplicates.
        """
        node = self._expression(unreal.MaterialExpressionTextureSampleParameter2D)
        node.set_editor_property("parameter_name", name)
        node.set_editor_property("texture", load_texture(default_texture_path(default_texture, sampler)))
        node.set_editor_property("sampler_type", sampler)
        node.set_editor_property("group", group)
        if uv is not None:
            self.connect(uv, "", node, "UVs")
        if register and name not in self.parameter_names:
            self.parameter_names.append(name)
        return node

    # ----------------------------------------------------------------- nodes

    def constant(self, value: float):
        node = self._expression(unreal.MaterialExpressionConstant)
        node.set_editor_property("r", float(value))
        return node

    def constant3(self, value):
        node = self._expression(unreal.MaterialExpressionConstant3Vector)
        node.set_editor_property("constant", unreal.LinearColor(value[0], value[1], value[2], 1.0))
        return node

    def world_position(self):
        return self._expression(unreal.MaterialExpressionWorldPosition)

    def vertex_normal(self):
        return self._expression(unreal.MaterialExpressionVertexNormalWS)

    def pixel_normal(self):
        return self._expression(unreal.MaterialExpressionPixelNormalWS)

    def vertex_color(self):
        return self._expression(unreal.MaterialExpressionVertexColor)

    def vertex_color4(self):
        """Vertex colour delivered to Custom nodes as a genuine float4.

        The unnamed output looks four-channel in the editor but a Custom node
        input fed from it is typed `float3`, so every ported shader that takes a
        `float4 VertexColor` failed to compile with "no matching function" — an
        error that names the function and says nothing about which argument is
        wrong. The alpha is re-appended explicitly, exactly as `vector4` has to
        do for VectorParameter.

        Unity's foliage and surface shaders carry real data in vertex alpha
        (wind weight, wetness, blend masks), so dropping to RGB would not merely
        fail to compile — it would silently lose a channel if it did.
        """
        colour = self.vertex_color()
        appended = self._expression(unreal.MaterialExpressionAppendVector)
        # Unlike VectorParameter, this expression has no output named "RGB":
        # its unnamed default output is the colour, and connecting *that* is
        # what yields the three components to append the alpha onto.
        self.connect(colour, "", appended, "A")
        self.connect(colour, "A", appended, "B")
        return appended

    def object_position(self):
        return self._expression(unreal.MaterialExpressionObjectPositionWS)

    def object_local_position(self):
        # Unreal's local position is in centimetres in the mesh's own space,
        # which is exactly Unity's positionOS once rescaled.
        return self._expression(unreal.MaterialExpressionObjectLocalBounds)

    def local_position(self):
        return self._expression(unreal.MaterialExpressionLocalPosition)

    def time(self, period: float = 0.0):
        node = self._expression(unreal.MaterialExpressionTime)
        if period > 0.0:
            node.set_editor_property("override_period", True)
            node.set_editor_property("period", period)
        return node

    def camera_vector(self):
        return self._expression(unreal.MaterialExpressionCameraVectorWS)

    def texcoord(self, index: int = 0):
        node = self._expression(unreal.MaterialExpressionTextureCoordinate)
        node.set_editor_property("coordinate_index", index)
        return node

    def custom(
        self,
        description: str,
        code: str,
        output_type=FLOAT3,
        inputs: dict | None = None,
        extra_includes: tuple[str, ...] = (),
    ):
        node = self._expression(unreal.MaterialExpressionCustom, x=-700, y=self._cursor_y)
        self._cursor_y += 200
        node.set_editor_property("description", description)
        node.set_editor_property("output_type", output_type)
        node.set_editor_property("code", code)
        node.set_editor_property("include_file_paths", list(self.includes + tuple(extra_includes)))

        inputs = inputs or {}
        custom_inputs = []
        for input_name in inputs:
            custom_input = unreal.CustomInput()
            custom_input.set_editor_property("input_name", input_name)
            custom_inputs.append(custom_input)
        node.set_editor_property("inputs", custom_inputs)

        for input_name, source in inputs.items():
            expression, output = source if isinstance(source, tuple) else (source, "")
            self.connect(expression, output, node, input_name)
        return node

    # ------------------------------------------------------------ connection

    def connect(self, source, output: str, destination, input_name: str) -> None:
        if not unreal.MaterialEditingLibrary.connect_material_expressions(
            source, output, destination, input_name
        ):
            raise RuntimeError(
                f"{self.asset_name}: cannot connect "
                f"{source.get_name()}.{output or 'out'} -> {destination.get_name()}.{input_name}"
            )

    def output(self, source, output: str, material_property) -> None:
        if not unreal.MaterialEditingLibrary.connect_material_property(
            source, output, material_property
        ):
            raise RuntimeError(
                f"{self.asset_name}: cannot connect {source.get_name()} to {material_property}"
            )

    # -------------------------------------------------------------- finalise

    def finalize(self) -> PortResult:
        unreal.MaterialEditingLibrary.layout_material_expressions(self.material)
        unreal.MaterialEditingLibrary.recompile_material(self.material)
        unreal.EditorAssetLibrary.set_metadata_tag(self.material, "CML.UnityShader", self.unity_shader)
        unreal.EditorAssetLibrary.set_metadata_tag(self.material, "CML.Schema", "UnityShaderPort.v1")
        unreal.EditorAssetLibrary.save_loaded_asset(self.material, only_if_is_dirty=False)
        return PortResult(
            unity_shader=self.unity_shader,
            master_object=self.material.get_path_name(),
            parameters=list(self.parameter_names),
        )


def unity_world_position(builder: MasterMaterialBuilder):
    """Absolute world position expressed in Unity's Y-up metre space."""
    return builder.custom(
        "UnityWorldPosition",
        "return CMLToUnityPosition(WorldPosition);",
        FLOAT3,
        {"WorldPosition": builder.world_position()},
    )


def unity_vertex_normal(builder: MasterMaterialBuilder):
    return builder.custom(
        "UnityVertexNormal",
        "return CMLToUnityDirection(normalize(Normal));",
        FLOAT3,
        {"Normal": builder.vertex_normal()},
    )
