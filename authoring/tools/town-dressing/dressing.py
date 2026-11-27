#!/usr/bin/env python3
"""Put the towns' street dressing into the art pack (#11169).

The dressing is generated art, one flat per piece, each drawn on flat magenta and reviewed in
local/art/generated/town-dressing/<rev>/raw-<id>.png. This keys out the magenta (and its spill on the edges), trims
each piece to what it shows, scales it to PER_METRE pixels a metre at the height PIECES gives it, and writes it as an
8-bit RGBA PNG into the art pack's media/town-dressing/, with the pack's `townDressing` section: each piece's kind (the
condition that puts it out), the kinds of spot it stands on in order of preference, and its size and foot.

    python3 authoring/tools/town-dressing/dressing.py --rev r01
    python3 authoring/tools/town-dressing/dressing.py --rev r01 --sheet OUT.png   # also a review sheet
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
GENERATED = Path.home() / "dev/rusty-dagger/local/art/generated/town-dressing"
PACK = ROOT / "content/worldrpg/packs/daggerfall.art-replacements"
MEDIA = "media/town-dressing"
PER_METRE = 96
# id: (kind, spots in order of preference, height in metres)
PIECES = {
    "festival-maypole": ("festival", ["square"], 4.6),
    "festival-garland-arch": ("festival", ["square", "seat", "temple"], 3.4),
    "festival-stall": ("festival", ["square", "shop"], 2.6),
    "prosperity-market-stall": ("prosperity", ["shop", "square"], 2.6),
    "prosperity-goods": ("prosperity", ["shop"], 1.3),
    "prosperity-handcart": ("prosperity", ["square", "shop"], 1.7),
    "unrest-barricade": ("unrest", ["seat", "square"], 1.8),
    "unrest-crates": ("unrest", ["seat", "square", "shop"], 1.6),
    "unrest-torn-notices": ("unrest", ["square", "shop"], 2.2),
    "witch-hunt-pyre": ("witchHunt", ["temple", "square"], 2.8),
    "witch-hunt-stocks": ("witchHunt", ["square", "seat"], 1.5),
    "witch-hunt-warning-post": ("witchHunt", ["temple", "square", "shop"], 2.2),
}
KEY = np.array([255.0, 0.0, 255.0])


def cut(image):
    """The piece with the magenta keyed out: opaque where far from it, fading over the anti-aliased edge, despilled."""
    rgb = np.asarray(image.convert("RGB"), dtype=np.float64)
    distance = np.linalg.norm(rgb - KEY, axis=2)
    alpha = np.clip((distance - 90.0) / 110.0, 0.0, 1.0)
    # Spill: magenta lifts red and blue together above green; pull both back toward green where it shows.
    spill = np.clip(np.minimum(rgb[..., 0], rgb[..., 2]) - rgb[..., 1], 0.0, None) * (1.0 - alpha * 0.6)
    rgb[..., 0] -= spill
    rgb[..., 2] -= spill
    out = np.dstack([np.clip(rgb, 0, 255), alpha * 255.0]).round().astype(np.uint8)
    out[alpha < 0.04] = 0
    return Image.fromarray(out, "RGBA")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--rev", required=True, help="the reviewed generation folder under local/art/generated/town-dressing")
    parser.add_argument("--sheet", type=Path, help="also write a review sheet of every piece on grey")
    args = parser.parse_args()
    source = GENERATED / args.rev
    out = PACK / MEDIA
    out.mkdir(parents=True, exist_ok=True)
    payload_path = PACK / "payload.json"
    payload = json.loads(payload_path.read_text())
    pieces, cuts = [], []
    for piece, (kind, spots, metres) in PIECES.items():
        flat = cut(Image.open(source / f"raw-{piece}.png"))
        flat = flat.crop(flat.getchannel("A").getbbox())
        height = round(metres * PER_METRE)
        width = max(1, round(flat.width * height / flat.height))
        flat = flat.resize((width, height), Image.LANCZOS)
        path = out / f"{piece}.png"
        flat.save(path, optimize=True)
        data = path.read_bytes()
        pieces.append({"id": piece, "kind": kind, "spots": spots,
                       "path": f"worldrpg/packs/daggerfall.art-replacements/{MEDIA}/{path.name}", "byteLength": len(data),
                       "sha256": hashlib.sha256(data).hexdigest(), "width": width, "height": height,
                       "placement": {"size": {"width": round(width / PER_METRE, 4), "height": metres}, "foot": {"x": 0.5, "y": 0.0}}})
        cuts.append(flat)
    payload["artReplacements"]["townDressing"] = {"pieces": pieces}
    payload_path.write_text(json.dumps(payload, indent=1) + "\n")
    print(f"Wrote {len(pieces)} pieces of town dressing from {source} into {out}.")
    if args.sheet:
        gap = 24
        sheet = Image.new("RGBA", (sum(c.width for c in cuts) + gap * (len(cuts) + 1), max(c.height for c in cuts) + 2 * gap), (96, 104, 96, 255))
        x = gap
        for c in cuts:
            sheet.alpha_composite(c, (x, sheet.height - gap - c.height))
            x += c.width + gap
        sheet.save(args.sheet)


if __name__ == "__main__":
    main()
