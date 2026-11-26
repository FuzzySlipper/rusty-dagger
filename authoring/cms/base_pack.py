"""Mtime-cached section reader for daggerfall.base (manifest-split or inline fallback)."""

from pathlib import Path

from cache import FileCache, file_stamp
from library import read_json


def _read_manifest(base_payload: Path, cache: FileCache | None = None) -> dict:
    data = cache.json(base_payload, {}) if cache is not None else read_json(base_payload, {})
    return data if isinstance(data, dict) else {}


def _sections_map(manifest: dict) -> dict[str, str] | None:
    sections = manifest.get("sections")
    return sections if isinstance(sections, dict) else None


def section_path(base_payload: Path, section: str, cache: FileCache | None = None) -> tuple[Path | None, bool]:
    """Return (path, is_split) for one base-pack section.

    When payload.json declares a 'sections' map, resolves the section's sibling JSON file
    without touching any other section (such as the 3 MB text table). When payload.json
    is a legacy/test inline document, returns (base_payload, False).
    """
    manifest = _read_manifest(base_payload, cache)
    sections = _sections_map(manifest)
    if sections is not None:
        rel = sections.get(section)
        if isinstance(rel, str) and rel:
            return base_payload.parent / rel, True
        return None, True
    return base_payload, False


def sections_stamp(base_payload: Path, sections: tuple[str, ...], cache: FileCache | None = None):
    """Build a cache stamp covering payload.json plus only the referenced split section files."""
    manifest = _read_manifest(base_payload, cache)
    sec_map = _sections_map(manifest)
    if sec_map is None:
        return file_stamp(base_payload)
    stamps = [file_stamp(base_payload)]
    for name in sections:
        rel = sec_map.get(name)
        if isinstance(rel, str) and rel:
            stamps.append(file_stamp(base_payload.parent / rel))
        else:
            stamps.append(None)
    return tuple(stamps)


def read_section(base_payload: Path, section: str, default=None, cache: FileCache | None = None):
    """Read one section from daggerfall.base, parsing only that section's file when split."""
    manifest = _read_manifest(base_payload, cache)
    sec_map = _sections_map(manifest)
    if sec_map is not None:
        rel = sec_map.get(section)
        if not isinstance(rel, str) or not rel:
            return default
        path = base_payload.parent / rel
        return cache.json(path, default) if cache is not None else read_json(path, default)
    value = manifest.get(section)
    return default if value is None else value
