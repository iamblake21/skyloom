"""Minimal reader for Unity's serialised YAML (prefabs, scenes, assets).

Unity does not emit general YAML: every document is introduced by
`--- !u!<classId> &<fileId>`, inline maps are always single-line `{a: 1, b: 2}`,
and indentation is two spaces per level. A dependency-free reader that matches
exactly that shape is far more predictable inside UnrealEditor's embedded Python
than pulling in a full YAML parser, and it keeps the file IDs - which are the
only stable identity Unity objects have - intact.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path


DOCUMENT_HEADER = re.compile(r"^--- !u!(?P<class_id>\d+) &(?P<file_id>-?\d+)")
INLINE_MAP = re.compile(r"^\{(?P<body>.*)\}$")
FILE_REFERENCE = re.compile(
    r"fileID:\s*(?P<file_id>-?\d+)(?:,\s*guid:\s*(?P<guid>[0-9a-fA-F]+))?(?:,\s*type:\s*(?P<type>\d+))?"
)


@dataclass(frozen=True)
class UnityReference:
    """A Unity object pointer: local when `guid` is empty, external otherwise."""

    file_id: int
    guid: str = ""

    @property
    def is_null(self) -> bool:
        return self.file_id == 0 and not self.guid

    @property
    def is_external(self) -> bool:
        return bool(self.guid)


@dataclass
class UnityObject:
    file_id: int
    class_id: int
    type_name: str
    values: dict = field(default_factory=dict)

    def get(self, path: str, default=None):
        """Read a nested value with a dotted path."""
        current = self.values
        for part in path.split("."):
            if not isinstance(current, dict) or part not in current:
                return default
            current = current[part]
        return current


def parse_reference(value) -> UnityReference:
    if isinstance(value, dict):
        return UnityReference(int(value.get("fileID", 0) or 0), str(value.get("guid", "") or "").lower())
    if isinstance(value, str):
        match = FILE_REFERENCE.search(value)
        if match:
            return UnityReference(int(match.group("file_id")), (match.group("guid") or "").lower())
    return UnityReference(0, "")


def _parse_scalar(text: str):
    text = text.strip()
    if not text:
        return ""
    inline = INLINE_MAP.match(text)
    if inline:
        body = inline.group("body").strip()
        if not body:
            return {}
        result: dict = {}
        # Inline maps never nest in Unity's output, so splitting on commas is safe.
        for item in body.split(","):
            if ":" not in item:
                continue
            key, _, raw = item.partition(":")
            key = key.strip()
            # Unity's builtin-resource GUID contains a long `e000...`
            # sequence.  Treating arbitrary scalars as floats turns that GUID
            # into numeric zero, so builtin Plane/Cube/Quad references become
            # indistinguishable from null references.
            result[key] = raw.strip() if key == "guid" else _parse_scalar(raw)
        return result
    if text.startswith(("'", '"')) and text.endswith(("'", '"')) and len(text) > 1:
        return text[1:-1]
    try:
        return int(text)
    except ValueError:
        pass
    try:
        return float(text)
    except ValueError:
        pass
    return text


def _parse_block(lines: list[tuple[int, str]], index: int, indent: int):
    """Parse one indentation level, returning (value, next_index)."""
    if index >= len(lines):
        return {}, index

    if lines[index][1].startswith("- "):
        items = []
        while index < len(lines) and lines[index][0] == indent and lines[index][1].startswith("- "):
            content = lines[index][1][2:]
            if ":" in content and not content.strip().startswith("{"):
                # A list entry that opens a mapping on the same line.
                key, _, raw = content.partition(":")
                entry: dict = {}
                if raw.strip():
                    entry[key.strip()] = _parse_scalar(raw)
                    index += 1
                else:
                    index += 1
                    nested, index = _parse_block(lines, index, indent + 2)
                    entry[key.strip()] = nested
                while index < len(lines) and lines[index][0] == indent + 2:
                    sub_key, _, sub_raw = lines[index][1].partition(":")
                    if sub_raw.strip():
                        entry[sub_key.strip()] = _parse_scalar(sub_raw)
                        index += 1
                    else:
                        index += 1
                        nested, index = _parse_block(lines, index, indent + 4)
                        entry[sub_key.strip()] = nested
                items.append(entry)
            else:
                items.append(_parse_scalar(content))
                index += 1
        return items, index

    mapping: dict = {}
    while index < len(lines) and lines[index][0] == indent:
        line = lines[index][1]
        if line.startswith("- "):
            break
        key, _, raw = line.partition(":")
        key = key.strip()
        if raw.strip():
            mapping[key] = _parse_scalar(raw)
            index += 1
            continue
        index += 1
        if index < len(lines) and lines[index][0] > indent:
            nested, index = _parse_block(lines, index, lines[index][0])
            mapping[key] = nested
        elif index < len(lines) and lines[index][1].startswith("- ") and lines[index][0] == indent:
            nested, index = _parse_block(lines, index, indent)
            mapping[key] = nested
        else:
            mapping[key] = {}
    return mapping, index


def load_unity_documents(path: Path) -> list[UnityObject]:
    """Read every `--- !u!` document in a Unity YAML file, in file order."""
    text = path.read_text(encoding="utf-8", errors="replace")
    documents: list[UnityObject] = []
    current_header: re.Match | None = None
    buffer: list[str] = []

    def flush() -> None:
        if current_header is None:
            return
        lines: list[tuple[int, str]] = []
        for raw in buffer:
            if not raw.strip() or raw.lstrip().startswith("#"):
                continue
            lines.append((len(raw) - len(raw.lstrip(" ")), raw.strip()))
        if not lines:
            return
        # The first line is the type name (`Transform:`), the body follows.
        type_name = lines[0][1].rstrip(":")
        values, _ = _parse_block(lines, 1, lines[1][0]) if len(lines) > 1 else ({}, 1)
        documents.append(
            UnityObject(
                file_id=int(current_header.group("file_id")),
                class_id=int(current_header.group("class_id")),
                type_name=type_name,
                values=values if isinstance(values, dict) else {},
            )
        )

    for raw_line in text.splitlines():
        header = DOCUMENT_HEADER.match(raw_line)
        if header:
            flush()
            current_header = header
            buffer = []
            continue
        if current_header is not None:
            buffer.append(raw_line)
    flush()
    return documents


def index_by_file_id(documents: list[UnityObject]) -> dict[int, UnityObject]:
    return {document.file_id: document for document in documents}
