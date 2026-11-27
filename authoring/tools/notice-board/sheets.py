#!/usr/bin/env python3
"""Put the notice board's art into the art pack (#11138).

The board's three sheets of parchment (a wanted poster, a decree and a plain notice) are generated art, reviewed in
local/art/generated/notice-board/<rev>/{bounty,decree,notice}.png. This writes each as an 8-bit RGBA PNG into the
art pack's media/notice-board/ and the pack's `noticeBoard` section: the sheets, and the refreshed plank surfaces
(by the classic records they refresh) its frame, backboard and roof are made of.

    python3 authoring/tools/notice-board/sheets.py --rev r01
"""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
GENERATED = Path.home() / "dev/rusty-dagger/local/art/generated/notice-board"
PACK = ROOT / "content/worldrpg/packs/daggerfall.art-replacements"
MEDIA = "media/notice-board"
SIZE = (240, 320)
# The refreshed planks of TEXTURE.067: knotted dark oak for the posts and rails, warm boards for the backboard, and
# weathered grey boards for the canopy.
WOOD = {"frame": [67, 3], "board": [67, 4], "roof": [67, 6]}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--rev", required=True, help="the reviewed generation folder under local/art/generated/notice-board")
    args = parser.parse_args()
    source = GENERATED / args.rev
    out = PACK / MEDIA
    out.mkdir(parents=True, exist_ok=True)
    payload_path = PACK / "payload.json"
    payload = json.loads(payload_path.read_text())
    surfaces = {(entry["replaces"]["archive"], entry["replaces"]["record"]) for entry in payload["artReplacements"]["surfaces"]}
    for part, (archive, record) in WOOD.items():
        if (archive, record) not in surfaces:
            raise SystemExit(f"The notice board's {part} names TEXTURE.{archive:03d} record {record}, which the art pack does not refresh.")
    sheets = {}
    for kind in ("bounty", "decree", "notice"):
        path = out / f"sheet-{kind}.png"
        Image.open(source / f"{kind}.png").convert("RGBA").resize(SIZE, Image.LANCZOS).save(path, optimize=True)
        data = path.read_bytes()
        sheets[kind] = {"path": f"worldrpg/packs/daggerfall.art-replacements/{MEDIA}/{path.name}", "byteLength": len(data),
                        "sha256": hashlib.sha256(data).hexdigest()}
    payload["artReplacements"]["noticeBoard"] = {"wood": WOOD, "sheets": sheets}
    payload_path.write_text(json.dumps(payload, indent=1) + "\n")
    print(f"Wrote the notice board's sheets from {source} into {out}.")


if __name__ == "__main__":
    main()
