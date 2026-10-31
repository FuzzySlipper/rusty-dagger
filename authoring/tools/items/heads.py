#!/usr/bin/env python3
"""Trim generated heads to their classic silhouettes, so a head carries no more neck or hair than Daggerfall's.

A head is drawn over the body's neck. A generated head that runs lower than its classic one paints its own
neck over the body's, in the wrong shape and often the wrong skin; one that runs wider or higher breaks the
proportions landmarks.py checks. Each final's alpha is multiplied by the classic head's silhouette, mapped
through the generation input transform, grown by GROW doll units and feathered over FEATHER, on the final's
own canvas, so the trimmed final keeps its registration and needs no sidecar.

    heads.py OUT [--face 00 --face 10]     # every generated head of those FACE files (default all)

Trimmed finals go to OUT/finals with the paper-doll naming, for doll_preview.py --paper-doll OUT/finals and
landmarks.py check --paper-doll OUT/finals before they replace gen-raw/paper-doll.
"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

from doll_preview import doll_layer, placed
from landmarks import BODIES, HEADS

GROW = 1.0     # doll units the head may reach past its classic silhouette
FEATHER = 1.0  # doll units over which the trim fades


def trim(layer):
    final, (x, y, w, _) = placed(layer, "canvas", 1024, 0.74)
    k = final.width / w  # canvas pixels per doll unit
    source = layer.source.getchannel("A").point(lambda a: 255 if a > 16 else 0)
    classic = Image.new("L", final.size)
    classic.paste(source.resize((round(source.width * k), round(source.height * k)), Image.Resampling.NEAREST),
                  (round((layer.x - x) * k), round((layer.y - y) * k)))
    grown = classic.filter(ImageFilter.MaxFilter(2 * round(GROW * k) + 1)).filter(ImageFilter.GaussianBlur(FEATHER * k / 2))
    alpha = np.array(final.getchannel("A"), np.float32) * np.array(grown, np.float32) / 255
    final.putalpha(Image.fromarray((alpha + .5).astype(np.uint8)))
    return final


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path)
    ap.add_argument("--face", action="append", type=int, choices=BODIES, help="FACE file number, e.g. 10")
    a = ap.parse_args()
    (a.out / "finals").mkdir(parents=True, exist_ok=True)
    for number in a.face or BODIES:
        for record in HEADS:
            layer = doll_layer("faces", f"face{number:02d}i0", record, "head")
            if layer.final is None:
                continue
            trim(layer).save(a.out / "finals" / layer.final.name)
        print(f"face{number:02d}: trimmed", flush=True)


if __name__ == "__main__":
    main()
