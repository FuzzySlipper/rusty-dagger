#!/usr/bin/env python3
"""Promote ledger-accepted finals into the tracked refreshed-art content pack.

Each accepted final in gen-raw/AREA stands in for one classic media id, which the game draws in its place
when the refreshed art set is chosen (classic stays selectable). For each final this:

  - finds the classic media id it replaces, from its art key: a worn layer `texture-A-record-R-frame-000` is
    the paper doll's `paper-doll.A.R`, a body `bodies--bodyGNi{0,1}--...` is
    `character.body-{unclothed,clothed}.{gender}.0N.0`, and a head `faces--faceGNi0--...-record-RRR-...` is
    `character.head.{gender}.0N.R`;
  - for a paper-doll layer, takes its doll-space rect as doll_preview.py draws it (a sidecar's rect, else the
    generation input transform inverted), crops transparent margins and moves the rect to match;
  - writes it as WebP (lossy colour at QUALITY, lossless alpha) to content/worldrpg/media/refreshed/AREA/;
  - records it in the pack payload's `artReplacements` section with its size, bytes and hash.

Only finals whose ledger/AREA-notes.json status is `accepted` are promoted. Rerunning an area replaces that
area's entries and files and keeps every other area's.

    promote.py --area paper-doll --area worn
"""
import argparse, hashlib, io, json, re
from pathlib import Path

from PIL import Image

from doll_preview import AUTHORING, doll_layer, placed

REPOSITORY = AUTHORING.parent
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/media/refreshed"
PAYLOAD = CONTENT / "worldrpg/payloads/daggerfall.art-replacements.json"
QUALITY = 90
AREAS = ("paper-doll", "worn")

WORN = re.compile(r"texture-(\d+)-record-(\d+)-frame-000")
BODY = re.compile(r"bodies--body(\d)(\d)(i[01])--.*")
FACE = re.compile(r"faces--face(\d)(\d)i0--.*-record-(\d+)-frame-000")
GENDER = {"0": "male", "1": "female"}


def replaces(key):
    """The classic media id a final's art key stands in for."""
    if m := WORN.fullmatch(key):
        return f"paper-doll.{int(m[1])}.{int(m[2])}"
    if m := BODY.fullmatch(key):
        return f"character.body-{'unclothed' if m[3] == 'i0' else 'clothed'}.{GENDER[m[1]]}.0{m[2]}.0"
    if m := FACE.fullmatch(key):
        return f"character.head.{GENDER[m[1]]}.0{m[2]}.{int(m[3])}"
    raise SystemExit(f"No classic media id for art key {key}.")


def rect_of(area, key, path):
    """The final's doll-space rect, as doll_preview.py places it."""
    if area == "worn":
        return tuple(json.load(open(path.with_suffix(".json")))["rect"])
    family, name, rest = key.split("--")
    record = int(re.search(r"record-(\d+)", rest)[1])
    return placed(doll_layer(family, name, record, key), "canvas", 1024, 0.74)[1]


def promote(area, key, path):
    image = Image.open(path).convert("RGBA")
    x, y, w, h = rect_of(area, key, path)
    k = image.width / w
    box = image.getchannel("A").getbbox()
    if box is None:
        raise SystemExit(f"{path} is fully transparent.")
    image = image.crop(box)
    buffer = io.BytesIO()
    image.save(buffer, "WEBP", quality=QUALITY, method=6)
    data = buffer.getvalue()
    target = f"{MEDIA}/{area}/{key}.webp"
    (CONTENT / target).write_bytes(data)
    classic = replaces(key)
    return {
        "replaces": classic, "mediaId": f"refreshed.{classic}", "path": target,
        "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest(),
        "width": image.width, "height": image.height,
        "doll": {"x": round(x + box[0] / k, 4), "y": round(y + box[1] / k, 4),
                 "width": round(image.width / k, 4), "height": round(image.height / k, 4)},
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--area", action="append", choices=AREAS, required=True)
    a = ap.parse_args()
    payload = json.loads(PAYLOAD.read_text()) if PAYLOAD.exists() else {"artReplacements": {"images": []}}
    images = payload["artReplacements"]["images"]
    for area in a.area:
        notes = json.load(open(AUTHORING / "ledger" / f"{area}-notes.json"))
        accepted = sorted(key for key, note in notes.items() if note.get("status") == "accepted")
        directory = CONTENT / MEDIA / area
        directory.mkdir(parents=True, exist_ok=True)
        for old in directory.glob("*.webp"):
            old.unlink()
        images[:] = [entry for entry in images if not entry["path"].startswith(f"{MEDIA}/{area}/")]
        for key in accepted:
            path = AUTHORING / "gen-raw" / area / f"{key}.png"
            if not path.exists():
                raise SystemExit(f"{key} is accepted in ledger/{area}-notes.json but {path} does not exist.")
            images.append(promote(area, key, path))
        size = sum(entry["byteLength"] for entry in images if entry["path"].startswith(f"{MEDIA}/{area}/"))
        print(f"{area}: {len(accepted)} finals, {size / 1e6:.1f} MB", flush=True)
    images.sort(key=lambda entry: entry["replaces"])
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")


if __name__ == "__main__":
    main()
