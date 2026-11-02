#!/usr/bin/env python3
"""Fit a generated framed picture to its classic picture's rect: the beast-form dolls, which are whole paintings.

A beast form draws one opaque framed painting in place of the doll's background. A cut-out finds no edge to
keep there (the stone frame is as grey as the generation background), so the picture is taken from the
batch's back-projected image instead: the rows and columns about as full as the fullest are the picture,
which is cropped, stretched to the classic picture's aspect and written opaque, with a sidecar stating the
classic rect, so doll_preview.py and promote.py draw it exactly where the classic picture was.

    pictures.py BP_DIR SPEC.json        # e.g. gen-raw/studies/paper-doll-c1-beasts-bp-DATE specs/paper-doll/paper-doll-c1-beasts.json

Finals go to gen-raw/paper-doll under their art keys.
"""
import argparse, json
from pathlib import Path

import numpy as np
from PIL import Image

from art_key import art_key
from doll_preview import AUTHORING, doll_layer

BACKGROUND = (48, 52, 59)  # the generation background, #30343b
UNLIKE = 24                # summed channel difference over which a pixel is not background
COVER = 0.5                # share of the fullest row's or column's unlike pixels a row or column needs to be in the picture


def picture(image):
    """The image cropped to the picture: the span of rows and columns about as unlike the background as the fullest."""
    rgb = np.asarray(image.convert("RGB"), np.int32)
    unlike = np.abs(rgb - BACKGROUND).sum(-1) > UNLIKE
    rows, columns = (np.flatnonzero(share > COVER * share.max()) for share in (unlike.mean(1), unlike.mean(0)))
    return image.crop((columns[0], rows[0], columns[-1] + 1, rows[-1] + 1))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("bp", type=Path)
    ap.add_argument("spec", type=Path)
    a = ap.parse_args()
    finals = AUTHORING / "gen-raw" / "paper-doll"
    for item in json.load(open(a.spec))["items"]:
        family, name = item["directory"].split("/")
        classic = doll_layer(family, name, 0, name)
        found = picture(Image.open(a.bp / "images" / item["id"] / "bp-broad-half.png"))
        size = (round(found.height * classic.source.width / classic.source.height), found.height)
        key = art_key("paper-doll", item["directory"], item["image"])
        found.convert("RGB").resize(size, Image.Resampling.LANCZOS).convert("RGBA").save(finals / f"{key}.png")
        rect = [classic.x, classic.y, classic.source.width, classic.source.height]
        json.dump({"rect": rect}, open(finals / f"{key}.json", "w"))
        print(f"{item['id']}: picture {found.size} -> {size} at {rect}", flush=True)


if __name__ == "__main__":
    main()
