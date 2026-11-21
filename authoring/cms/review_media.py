"""Locate native media for stage review on the current character root."""

from pathlib import Path

from library import STAGE_MOUNT, file_link, read_json
from pipelines import STAGES

MEDIA = {
    ".png": "image", ".jpg": "image", ".jpeg": "image", ".webp": "image", ".gif": "image",
    ".mp4": "video", ".webm": "video", ".glb": "model", ".vsplat": "model",
}


def stage_info(library, pipeline, stage):
    if stage not in STAGES or not pipeline or "/" in pipeline:
        return None
    root = (library.characters / pipeline).resolve()
    if not root.is_relative_to(library.characters.resolve()):
        return None
    state = read_json(root / "character.json", {})
    if state.get("id") != pipeline:
        return None
    return state, state.get("stages", {}).get(stage, {})


def file_revision(path):
    try:
        stat = path.stat()
        return f"{stat.st_mtime_ns}:{stat.st_size}" if path.is_file() else "missing"
    except FileNotFoundError:
        return "missing"


def revision(library, category, id):
    if category not in ("pipeline-stages", "pipeline-assets"):
        return None
    pipeline, slash, rest = id.partition("/")
    if not slash:
        return "missing"
    if category == "pipeline-stages":
        info = stage_info(library, pipeline, rest)
        return (info[1].get("updated") or "unrecorded") if info else "missing"
    file = (library.characters / pipeline / rest).resolve()
    return file_revision(file) if file.is_relative_to(library.characters.resolve()) else "missing"


def stage_assets(library, pipeline, stage, state):
    root = library.characters / pipeline
    base = root / stage
    paths = {}

    def add(file, label=None):
        relative = file.relative_to(root).as_posix()
        if file.suffix.lower() in MEDIA and file.is_file():
            paths.setdefault(relative, label or file.stem.replace("-", " ").replace("_", " "))

    # The stage ledger supplies labels and includes outputs outside the conventional folders.
    for item in state.get("items", []):
        if item.get("role") in {"intermediate", "superseded"} or not item.get("path"):
            continue
        file = root / item["path"]
        if file.is_relative_to(root) and file.resolve().is_relative_to(root.resolve()):
            if file.is_dir():
                for child in sorted(file.iterdir()):
                    add(child)
            else:
                add(file, item.get("name"))
    for output in state.get("outputs", {}).values():
        file = root / output.get("path", "")
        if file.is_relative_to(root) and file.resolve().is_relative_to(root.resolve()):
            add(file)
    for folder in ("approved", "candidates", "review", "bound/final", "bound/review"):
        directory = base / folder
        if not directory.is_dir():
            continue
        for child in sorted(directory.iterdir()):
            if child.is_dir():
                for file in sorted(child.iterdir()):
                    add(file)
            else:
                add(child)
    assets = []
    for relative, name in paths.items():
        id = f"{pipeline}/{relative}"
        if url := file_link(library.characters, STAGE_MOUNT, id):
            assets.append({"id": id, "name": name, "url": url, "media": MEDIA[Path(relative).suffix.lower()],
                           "revision": file_revision(root / relative)})
    return assets
