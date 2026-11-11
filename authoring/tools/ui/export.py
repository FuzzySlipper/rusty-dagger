#!/usr/bin/env python3
"""export-ui: the classic 2D screen art the game still draws, as conversion sources.

Reads the import's published classic images (local/import/staging/worldrpg/media, so run the import first) and
writes them in the df-raw layout every area uses: one directory per subject, `<file>-record-RRR-frame-000-source.png`
at native resolution, with index.csv, manifest.json and a README.

  ui/screens/<name>/            the full screens: title, character creation (CHGN00I0, PICK02I0), prison, death
  ui/faction-faces/tfac00i0/    the escort portraits of individual NPCs (TFAC00I0.RCI), record N = faction face N
  ui/child-faces/kids00i0/      the child escort portraits (KIDS00I0.CIF), record N = child face N
  paper-doll/backgrounds/scbgNNi0/  the race backgrounds the paper doll stands on (SCBG00I0-SCBG08I0)

The race backgrounds go with the paper-doll area, whose doll draws them; the rest is the `ui` area.

    authoring/tools/ui/export.py [--staging DIR] [--output authoring/df-raw]
"""
import argparse
import csv
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SCREENS = {"screen.title": "title", "screen.character-generation": "character-generation",
           "screen.pick.02": "pick-02", "screen.prison": "prison", "screen.death": "death"}


def records(inventory, pattern):
    """(mediaId, path, record) for every artifact whose media id matches, record from the id's last number."""
    for artifact in inventory["artifacts"]:
        media = artifact.get("mediaId") or ""
        if m := re.fullmatch(pattern, media):
            yield media, artifact["path"], int(m[1])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--staging", type=Path, default=ROOT / "local/import/staging")
    ap.add_argument("--output", type=Path, default=ROOT / "authoring/df-raw")
    a = ap.parse_args()
    media = a.staging / "worldrpg/media"
    classic = json.loads((media / "classic-media-inventory.json").read_text())
    character = json.loads((media / "character/character-media-inventory.json").read_text())
    rows = {"ui": [], "paper-doll": []}

    # The doll draws every race's background in one frame; its manifest states it as the classic header offset,
    # as the paper-doll export does, so doll_preview.py and promote.py place a background where the doll does.
    imported = (a.staging / "worldrpg/payloads/daggerfall.imported.json").read_text()
    frame = {key: int(re.search(rf'"{key}":(-?\d+)', imported)[1]) for key in ("backgroundX", "backgroundY")}
    origin = (200, 8)  # the donor's paperDollOrigin

    def copy(area, directory, stem, record, media_id, path, **extra):
        target = a.output / area / directory / f"{stem}-record-{record:03d}-frame-000-source.png"
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(a.staging / path, target)
        rows[area].append({"mediaId": media_id, "directory": directory, "image": target.name, "source": path})
        # Each subject directory lists its images, as the importer's exports do, for run.py to read.
        manifest = target.parent / "manifest.json"
        listed = json.loads(manifest.read_text()) if manifest.exists() else {"images": []}
        listed |= {"kind": "ui-image", "directory": directory, "name": stem}
        listed["images"] = [entry for entry in listed["images"] if entry["file"] != target.name] + [
            {"file": target.name, "mediaId": media_id, "record": record, "frame": 0, "source": path,
             "role": "ui", "colors": ["source"], **extra}]
        manifest.write_text(json.dumps(listed, indent=1) + "\n")

    for artifact in classic["artifacts"]:
        if (name := SCREENS.get(artifact.get("mediaId"))) is not None:
            copy("ui", f"screens/{name}", name, 0, artifact["mediaId"], artifact["path"])
    for media_id, path, record in records(character, r"character\.faction-face\.(\d+)"):
        copy("ui", "faction-faces/tfac00i0", "tfac00i0", record, media_id, path)
    for media_id, path, record in records(character, r"character\.child-face\.(\d+)"):
        copy("ui", "child-faces/kids00i0", "kids00i0", record, media_id, path)
    for media_id, path, _ in records(character, r"character\.scbg\.scbg(\d\d)i0\.0"):
        name = media_id.split(".")[2]
        copy("paper-doll", f"backgrounds/{name}", name, 0, media_id, path, role="paper-doll", family="backgrounds",
             xOffset=frame["backgroundX"] + origin[0], yOffset=frame["backgroundY"] + origin[1])

    for area, entries in rows.items():
        root = a.output / area
        if area == "ui":
            (root / "README.txt").write_text(
                "Classic 2D screens and escort portraits the game still draws, from the import's published images.\n"
                "Reproduce: authoring/tools/ui/export.py (after the import). See authoring/README.md.\n")
        with open(root / ("index.csv" if area == "ui" else "backgrounds.csv"), "w", newline="") as f:
            writer = csv.DictWriter(f, ["mediaId", "directory", "image", "source"])
            writer.writeheader()
            writer.writerows(entries)
        if area == "ui":
            (root / "manifest.json").write_text(json.dumps({"purpose": "Conversion sources for the ui area",
                                                            "sources": entries}, indent=1) + "\n")
        print(f"{area}: {len(entries)} images")


if __name__ == "__main__":
    main()
