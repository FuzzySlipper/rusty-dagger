#!/usr/bin/env python3
"""Publish cut-out finals: copy `<id>.png` from a cutout directory to gen-raw/<area> under the image's art key.

    finalize.py SPEC.json CUTOUT_DIR [--area items] [--finals DIR]
"""
import argparse, json, shutil
from pathlib import Path

from art_key import art_key

AUTHORING = Path(__file__).resolve().parents[2]
ap = argparse.ArgumentParser()
ap.add_argument("spec"); ap.add_argument("cutout")
ap.add_argument("--area", default="items")
ap.add_argument("--finals")
a = ap.parse_args()
finals = Path(a.finals or AUTHORING / "gen-raw" / a.area); finals.mkdir(parents=True, exist_ok=True)
items = json.load(open(a.spec))["items"]
for i in items:
    shutil.copyfile(Path(a.cutout) / (i["id"] + ".png"), finals / (art_key(a.area, i["directory"], i["image"]) + ".png"))
print("published", len(items))
