"""Reader for Unity's binary ``SerializedFile`` format.

Unity honours the project's Force Text serialisation setting for almost every
asset, but not for ``TerrainData``: those are written in the binary
``SerializedFile`` container whatever the setting says. Since the terrain is the
one thing the migration cannot read as YAML, this module reads the binary form
directly rather than asking a running Unity editor to export it. That keeps the
whole migration reproducible offline, with no dependency on an editor licence.

Editor-authored assets embed a full type tree, so nothing here needs a hardcoded
layout for any Unity class: the file describes its own structure and this module
walks it.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


# Type and field names shared by every Unity asset are not repeated in each
# file; a node whose string offset has the high bit set indexes this table
# instead. The exact byte offsets are part of the format, so the table is
# reproduced verbatim and self-checked below.
COMMON_STRINGS = (
    "AABB\0AnimationClip\0AnimationCurve\0AnimationState\0Array\0Base\0BitField\0"
    "bitset\0bool\0char\0ColorRGBA\0Component\0data\0deque\0double\0dynamic_array\0"
    "FastPropertyName\0first\0float\0Font\0GameObject\0Generic Mono\0GradientNEW\0"
    "GUID\0GUIStyle\0int\0list\0long long\0map\0Matrix4x4f\0MdFour\0MonoBehaviour\0"
    "MonoScript\0m_ByteSize\0m_Curve\0m_EditorClassIdentifier\0m_EditorHideFlags\0"
    "m_Enabled\0m_ExtensionPtr\0m_GameObject\0m_Index\0m_IsArray\0m_IsStatic\0"
    "m_MetaFlag\0m_Name\0m_ObjectHideFlags\0m_PrefabInternal\0m_PrefabParentObject\0"
    "m_Script\0m_StaticEditorFlags\0m_Type\0m_Version\0Object\0pair\0PPtr<Component>\0"
    "PPtr<GameObject>\0PPtr<Material>\0PPtr<MonoBehaviour>\0PPtr<MonoScript>\0"
    "PPtr<Object>\0PPtr<Prefab>\0PPtr<Sprite>\0PPtr<TextAsset>\0PPtr<Texture>\0"
    "PPtr<Texture2D>\0PPtr<Transform>\0Prefab\0Quaternionf\0Rectf\0RectInt\0"
    "RectOffset\0second\0set\0short\0size\0SInt16\0SInt32\0SInt64\0SInt8\0"
    "staticvector\0string\0TextAsset\0TextMesh\0Texture\0Texture2D\0Transform\0"
    "TypelessData\0UInt16\0UInt32\0UInt64\0UInt8\0unsigned int\0"
    "unsigned long long\0unsigned short\0vector\0Vector2f\0Vector3f\0Vector4f\0"
    "m_ScriptingClassIdentifier\0Gradient\0Type*\0int2_storage\0int3_storage\0"
    "BoundsInt\0m_CorrespondingSourceObject\0m_PrefabInstance\0m_PrefabAsset\0"
    "FileSize\0Hash128\0"
).encode("utf-8")

# If the table above were off by a single byte every name in every file would
# come out wrong, so two known offsets are asserted at import time rather than
# left to produce silently corrupt output.
assert COMMON_STRINGS[55:59] == b"Base", "common string table is misaligned"
assert COMMON_STRINGS[874:883] == b"Texture2D", "common string table is misaligned"

ALIGN_FLAG = 0x4000
COMMON_STRING_FLAG = 0x80000000


class UnityFormatError(Exception):
    """Raised when a file does not match the format this reader understands."""


def _read_null_terminated(buffer: bytes, offset: int) -> str:
    end = buffer.index(b"\0", offset)
    return buffer[offset:end].decode("utf-8", errors="replace")


@dataclass
class TypeTreeNode:
    version: int
    level: int
    type_flags: int
    type_name: str
    name: str
    byte_size: int
    index: int
    meta_flag: int

    @property
    def aligns(self) -> bool:
        return (self.meta_flag & ALIGN_FLAG) != 0

    @property
    def is_array(self) -> bool:
        return self.type_flags == 1


@dataclass
class SerializedType:
    class_id: int
    is_stripped: bool
    script_type_index: int
    nodes: list[TypeTreeNode] = field(default_factory=list)


@dataclass
class ObjectInfo:
    path_id: int
    byte_start: int
    byte_size: int
    type_index: int
    class_id: int


@dataclass
class ExternalReference:
    """One entry of the file's dependency table.

    A ``PPtr`` with ``m_FileID`` zero points inside the same file; anything
    higher is a 1-based index into this table.
    """

    guid: str
    type: int
    path_name: str


def guid_to_text(raw: bytes) -> str:
    """Converts a binary GUID to the hex form Unity writes in ``.meta`` files.

    Unity writes each byte low nibble first, so a straight ``hex()`` would
    produce a string that matches no ``.meta`` file in the project.
    """
    return "".join(f"{byte & 0x0F:x}{byte >> 4:x}" for byte in raw)


class _Cursor:
    """Little-endian reader with Unity's 4-byte alignment rule."""

    def __init__(self, data: bytes, position: int = 0, origin: int = 0) -> None:
        self.data = data
        self.position = position
        self.origin = origin

    def take(self, count: int) -> bytes:
        end = self.position + count
        if end > len(self.data):
            raise UnityFormatError("read past end of file")
        chunk = self.data[self.position:end]
        self.position = end
        return chunk

    def unpack(self, layout: str) -> tuple:
        values = struct.unpack_from(layout, self.data, self.position)
        self.position += struct.calcsize(layout)
        return values

    def one(self, layout: str):
        return self.unpack(layout)[0]

    def align(self, alignment: int = 4) -> None:
        # Alignment is measured from the start of the object's data, which Unity
        # always places on an 8-byte boundary, so this agrees with the absolute
        # stream position the format is defined against.
        offset = self.position - self.origin
        remainder = offset % alignment
        if remainder:
            self.position += alignment - remainder

    def aligned_string(self) -> str:
        length = self.one("<i")
        raw = self.take(length)
        self.align()
        return raw.decode("utf-8", errors="replace")


class SerializedFile:
    """A parsed Unity binary asset file."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.data = path.read_bytes()
        self.types: list[SerializedType] = []
        self.objects: list[ObjectInfo] = []
        self.externals: list[ExternalReference] = []
        self._parse()

    # -- header ---------------------------------------------------------

    def _parse(self) -> None:
        data = self.data
        _, _, version, _ = struct.unpack_from(">IIII", data, 0)
        if version != 22:
            raise UnityFormatError(
                f"{self.path.name}: SerializedFile version {version} is not supported"
            )
        self.version = version
        self.big_endian = data[16] != 0
        if self.big_endian:
            raise UnityFormatError(f"{self.path.name}: big-endian files are not supported")

        (self.metadata_size,) = struct.unpack_from(">I", data, 20)
        self.file_size, self.data_offset, _ = struct.unpack_from(">qqq", data, 24)
        if self.file_size != len(data):
            raise UnityFormatError(
                f"{self.path.name}: header declares {self.file_size} bytes, file has {len(data)}"
            )

        cursor = _Cursor(data, 48)
        self.unity_version = _read_null_terminated(data, cursor.position)
        cursor.position += len(self.unity_version.encode("utf-8")) + 1
        self.target_platform = cursor.one("<i")
        self.has_type_tree = cursor.one("<B") != 0
        if not self.has_type_tree:
            raise UnityFormatError(
                f"{self.path.name}: no type tree, so the layout cannot be recovered"
            )

        for _ in range(cursor.one("<i")):
            self.types.append(self._read_type(cursor))
        self._read_objects(cursor)
        self._read_script_types(cursor)
        self._read_externals(cursor)

    def _read_type(self, cursor: _Cursor) -> SerializedType:
        class_id = cursor.one("<i")
        is_stripped = cursor.one("<B") != 0
        script_type_index = cursor.one("<h")
        if class_id == 114:  # MonoBehaviour carries an extra script hash.
            cursor.take(16)
        cursor.take(16)  # old type hash

        node_count = cursor.one("<i")
        string_buffer_size = cursor.one("<i")
        raw_nodes = [cursor.unpack("<HBBIIiiiQ") for _ in range(node_count)]
        string_buffer = cursor.take(string_buffer_size)

        def resolve(offset: int) -> str:
            if offset & COMMON_STRING_FLAG:
                return _read_null_terminated(COMMON_STRINGS, offset & 0x7FFFFFFF)
            return _read_null_terminated(string_buffer, offset)

        nodes = [
            TypeTreeNode(
                version=node[0],
                level=node[1],
                type_flags=node[2],
                type_name=resolve(node[3]),
                name=resolve(node[4]),
                byte_size=node[5],
                index=node[6],
                meta_flag=node[7],
            )
            for node in raw_nodes
        ]

        cursor.unpack(f"<{cursor.one('<i')}i")  # type dependencies, unused here
        return SerializedType(class_id, is_stripped, script_type_index, nodes)

    def _read_objects(self, cursor: _Cursor) -> None:
        for _ in range(cursor.one("<i")):
            cursor.align()
            path_id = cursor.one("<q")
            byte_start = cursor.one("<q") + self.data_offset
            byte_size = cursor.one("<I")
            type_index = cursor.one("<i")
            self.objects.append(
                ObjectInfo(
                    path_id=path_id,
                    byte_start=byte_start,
                    byte_size=byte_size,
                    type_index=type_index,
                    class_id=self.types[type_index].class_id,
                )
            )

    def _read_script_types(self, cursor: _Cursor) -> None:
        for _ in range(cursor.one("<i")):
            cursor.one("<i")  # local serialized file index
            cursor.align()
            cursor.one("<q")  # local identifier in file

    def _read_externals(self, cursor: _Cursor) -> None:
        for _ in range(cursor.one("<i")):
            start = cursor.position
            cursor.position = self.data.index(b"\0", start) + 1  # always empty
            guid = guid_to_text(cursor.take(16))
            reference_type = cursor.one("<i")
            start = cursor.position
            path_name = _read_null_terminated(self.data, start)
            cursor.position = start + len(path_name.encode("utf-8")) + 1
            self.externals.append(ExternalReference(guid, reference_type, path_name))

    def resolve(self, pointer: dict[str, Any]) -> tuple[str, int]:
        """Turns a ``PPtr`` into ``(guid, pathID)``; the guid is empty if local."""
        file_id = pointer["m_FileID"]
        if file_id == 0:
            return "", pointer["m_PathID"]
        return self.externals[file_id - 1].guid, pointer["m_PathID"]

    # -- object contents ------------------------------------------------

    def read_object(self, info: ObjectInfo) -> dict[str, Any]:
        if info.byte_start % 4:
            raise UnityFormatError("object data is not 4-byte aligned")
        cursor = _Cursor(
            self.data[: info.byte_start + info.byte_size], info.byte_start, info.byte_start
        )
        nodes = self.types[info.type_index].nodes
        value = _read_value(nodes, 0, cursor)[0]
        if not isinstance(value, dict):
            raise UnityFormatError("root object is not a structure")
        return value

    def objects_of_class(self, class_id: int) -> list[ObjectInfo]:
        return [info for info in self.objects if info.class_id == class_id]


def _subtree(nodes: list[TypeTreeNode], index: int) -> list[TypeTreeNode]:
    """The node at ``index`` plus every node nested beneath it."""
    level = nodes[index].level
    end = index + 1
    while end < len(nodes) and nodes[end].level > level:
        end += 1
    return nodes[index:end]


_SCALARS: dict[str, str] = {
    "SInt8": "<b",
    "UInt8": "<B",
    "char": "<B",
    "short": "<h",
    "SInt16": "<h",
    "ushort": "<H",
    "UInt16": "<H",
    "unsigned short": "<H",
    "int": "<i",
    "SInt32": "<i",
    "uint": "<I",
    "UInt32": "<I",
    "unsigned int": "<I",
    "Type*": "<I",
    "long long": "<q",
    "SInt64": "<q",
    "unsigned long long": "<Q",
    "UInt64": "<Q",
    "FileSize": "<Q",
    "float": "<f",
    "double": "<d",
}


def _read_value(nodes: list[TypeTreeNode], index: int, cursor: _Cursor) -> tuple[Any, int]:
    """Reads one value. Returns it together with the number of nodes consumed."""
    node = nodes[index]
    aligns = node.aligns
    consumed = 1

    layout = _SCALARS.get(node.type_name)
    if layout is not None:
        value = cursor.one(layout)
    elif node.type_name == "bool":
        value = cursor.one("<B") != 0
    elif node.type_name == "string":
        value = cursor.aligned_string()
        consumed = len(_subtree(nodes, index))
    elif node.type_name == "TypelessData":
        # A raw byte blob: a length followed by the bytes, with the two child
        # nodes describing a layout that is not actually written out.
        value = cursor.take(cursor.one("<i"))
        consumed = 3
    else:
        subtree = _subtree(nodes, index)
        consumed = len(subtree)
        if len(subtree) > 1 and subtree[1].is_array:
            if subtree[1].aligns:
                aligns = True
            value = _read_array(subtree, cursor)
        else:
            value = {}
            child = 1
            while child < len(subtree):
                if subtree[child].level != node.level + 1:
                    child += 1
                    continue
                value[subtree[child].name], step = _read_value(subtree, child, cursor)
                child += step

    if aligns:
        cursor.align()
    return value, consumed


def _read_array(subtree: list[TypeTreeNode], cursor: _Cursor) -> list[Any]:
    """Reads a ``vector``: the Array node holds a size then the elements."""
    count = cursor.one("<i")
    element = subtree[3]

    # Fixed-width scalars are the overwhelming majority of array payloads
    # (heightmaps run to millions of entries), so they are unpacked in one go
    # rather than one struct call per element.
    layout = _SCALARS.get(element.type_name)
    if layout is not None and not element.aligns:
        values = list(cursor.unpack(f"<{count}{layout[1]}"))
        return values
    if element.type_name == "bool" and not element.aligns:
        return [byte != 0 for byte in cursor.take(count)]

    items = []
    for _ in range(count):
        value, _step = _read_value(subtree, 3, cursor)
        items.append(value)
    return items
