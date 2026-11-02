#!/usr/bin/env python3
"""Trim generated heads and held items to their classic silhouettes, so a head carries no more neck or hair than
Daggerfall's and a held weapon leaves the gap where the body's hand closes round its grip.

A head is drawn over the body's neck. A generated head that runs lower than its classic one paints its own
neck over the body's, in the wrong shape and often the wrong skin; one that runs wider or higher breaks the
proportions landmarks.py checks. A held weapon or artifact is drawn over the body's hand, which shows through a
gap the classic layer leaves at the grip; a generated one draws its grip whole, over the fist. Each final's alpha is multiplied by the classic head's silhouette, mapped
into the final's doll-space rect, grown by GROW doll units and feathered over FEATHER, on the final's own
canvas, so the trimmed final keeps its registration and its sidecar is copied beside it.

    heads.py OUT [--face 00 --face 10]     # every generated head of those FACE files (default all)
    heads.py OUT --worn texture-234-record-013-frame-000 ...   # those held items instead
    heads.py OUT --creature vamp00i0 --creature wolf00i0        # every record of those creature files instead

Trimmed heads go to OUT/finals with the paper-doll naming, for doll_preview.py --paper-doll OUT/finals and
landmarks.py check --paper-doll OUT/finals before they replace gen-raw/paper-doll (a creature's too: a vampire
head, or a beast doll, whose classic picture is a solid frame, losing only stray specks); trimmed held items go to
OUT/worn, for doll_preview.py --worn OUT/worn before they replace gen-raw/worn.
"""
import argparse, shutil
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

from doll_preview import DF_DOLL, doll_layer, placed, worn_layer
from landmarks import BODIES, HEADS

GROW = 1.0     # doll units the head may reach past its classic silhouette
FEATHER = 1.0  # doll units over which the trim fades


def trim(layer):
    final, (x, y, w, _) = placed(layer, "canvas")
    k = final.width / w  # canvas pixels per doll unit
    source = layer.source.getchannel("A").point(lambda a: 255 if a > 16 else 0)
    classic = Image.new("L", final.size)
    classic.paste(source.resize((round(source.width * k), round(source.height * k)), Image.Resampling.NEAREST),
                  (round((layer.x - x) * k), round((layer.y - y) * k)))
    grown = classic.filter(ImageFilter.MaxFilter(2 * round(GROW * k) + 1)).filter(ImageFilter.GaussianBlur(FEATHER * k / 2))
    alpha = np.array(final.getchannel("A"), np.float32) * np.array(grown, np.float32) / 255
    final.putalpha(Image.fromarray((alpha + .5).astype(np.uint8)))
    return final


def keep(layer, path):
    """Writes the trimmed final, with its sidecar, at path."""
    trim(layer).save(path)
    shutil.copyfile(layer.final.with_suffix(".json"), path.with_suffix(".json"))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path)
    ap.add_argument("--face", action="append", type=int, choices=BODIES, help="FACE file number, e.g. 10")
    ap.add_argument("--worn", action="append", help="a held item's art key, e.g. texture-234-record-013-frame-000")
    ap.add_argument("--creature", action="append", help="a creature file, e.g. vamp00i0")
    a = ap.parse_args()
    if a.worn:
        (a.out / "worn").mkdir(parents=True, exist_ok=True)
        for key in a.worn:
            keep(worn_layer(key, None), a.out / "worn" / f"{key}.png")
        print(f"trimmed {len(a.worn)} held items", flush=True)
        return
    (a.out / "finals").mkdir(parents=True, exist_ok=True)
    if a.creature:
        for name in a.creature:
            for record in range(len(list((DF_DOLL / "creatures" / name).glob("*-source.png")))):
                layer = doll_layer("creatures", name, record, name)
                if layer.final is not None:
                    keep(layer, a.out / "finals" / layer.final.name)
            print(f"{name}: trimmed", flush=True)
        return
    for number in a.face or BODIES:
        for record in HEADS:
            layer = doll_layer("faces", f"face{number:02d}i0", record, "head")
            if layer.final is None:
                continue
            keep(layer, a.out / "finals" / layer.final.name)
        print(f"face{number:02d}: trimmed", flush=True)


if __name__ == "__main__":
    main()
