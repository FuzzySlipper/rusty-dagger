#!/usr/bin/env python3
"""Fit generated framed pictures to their classic pictures: whole opaque images with a frame of their own, such as
the beast-form dolls (paintings drawn in place of the doll's background) and the spellmaker's targeting and
element tiles (pictures in a bevelled gold frame).

A cut-out finds no edge to keep there (a grey stone or gold frame against the grey generation background), so the
picture is taken from the batch's back-projected image instead: the rows and columns about as full as the
fullest are the picture, which is cropped, stretched to the classic picture's aspect and written opaque. A
paper-doll picture also gets a sidecar stating the classic rect, so doll_preview.py and promote.py draw it
exactly where the classic picture was.

    pictures.py BP_DIR SPEC.json [--area paper-doll]   # e.g. gen-raw/studies/paper-doll-c1-beasts-bp-DATE specs/paper-doll/paper-doll-c1-beasts.json
    pictures.py BP_DIR SPEC.json --area ui --source gen-raw/source-edits/ui-screens --image original --only title death

Finals go to gen-raw/AREA under their art keys.
"""
import argparse, json
from pathlib import Path

import numpy as np
from PIL import Image

from art_key import art_key
from doll_preview import AUTHORING, doll_layer

EDGE = 8                   # border pixels whose median is the background the model actually painted
UNLIKE = 24                # summed channel difference over which a pixel is not background
COVER = 0.5                # share of the fullest row's or column's unlike pixels a row or column needs to be in the picture


def picture(image):
    """The image cropped to the picture: the span of rows and columns about as unlike the background as the fullest."""
    rgb = np.asarray(image.convert("RGB"), np.int32)
    # The spec asks for #30343b around the picture, but the model may paint it a shade off; a few units over the
    # threshold would make every row and column part of the picture.
    border = np.concatenate([rgb[:EDGE].reshape(-1, 3), rgb[-EDGE:].reshape(-1, 3), rgb[:, :EDGE].reshape(-1, 3), rgb[:, -EDGE:].reshape(-1, 3)])
    unlike = np.abs(rgb - np.median(border, 0)).sum(-1) > UNLIKE
    rows, columns = (np.flatnonzero(share > COVER * share.max()) for share in (unlike.mean(1), unlike.mean(0)))
    return image.crop((columns[0], rows[0], columns[-1] + 1, rows[-1] + 1))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("bp", type=Path)
    ap.add_argument("spec", type=Path)
    ap.add_argument("--area", default="paper-doll")
    ap.add_argument("--source", type=Path, help="the source root the batch ran from (its SOURCE), when not df-raw/AREA")
    ap.add_argument("--image", choices=("bp-broad-half", "original"), default="bp-broad-half",
                    help="original takes the picture before back-projection, which would bring back lettering the batch painted out")
    ap.add_argument("--only", nargs="+", help="item ids to fit; the rest are left as they are")
    a = ap.parse_args()
    finals = AUTHORING / "gen-raw" / a.area
    sources = a.source or AUTHORING / "df-raw" / a.area
    for item in json.load(open(a.spec))["items"]:
        if a.only and item["id"] not in a.only:
            continue
        source = Image.open(sources / item["directory"] / item["image"])
        found = picture(Image.open(a.bp / "images" / item["id"] / f"{a.image}.png"))
        size = (round(found.height * source.width / source.height), found.height)
        key = art_key(a.area, item["directory"], item["image"])
        found.convert("RGB").resize(size, Image.Resampling.LANCZOS).convert("RGBA").save(finals / f"{key}.png")
        placed = ""
        if a.area == "paper-doll":
            family, name = item["directory"].split("/")
            classic = doll_layer(family, name, 0, name)
            rect = [classic.x, classic.y, classic.source.width, classic.source.height]
            json.dump({"rect": rect}, open(finals / f"{key}.json", "w"))
            placed = f" at {rect}"
        print(f"{item['id']}: picture {found.size} -> {size}{placed}", flush=True)


if __name__ == "__main__":
    main()
