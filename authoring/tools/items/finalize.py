#!/usr/bin/env python3
"""Publish cut-out finals: copy `<id>.png` from a cutout directory to gen-raw/items as texture-A-record-R-frame-F.png.

    finalize.py SPEC.json CUTOUT_DIR [--finals DIR]
"""
import argparse, json, re, shutil
from pathlib import Path

AUTHORING = Path(__file__).resolve().parents[2]
ap = argparse.ArgumentParser()
ap.add_argument("spec"); ap.add_argument("cutout")
ap.add_argument("--finals", default=str(AUTHORING / "gen-raw/items"))
a = ap.parse_args()
finals = Path(a.finals); finals.mkdir(parents=True, exist_ok=True)
items = json.load(open(a.spec))["items"]
for i in items:
    name = "texture-%s-record-%s-frame-%s.png" % re.search(r"texture-(\d+)-record-(\d+)-frame-(\d+)", i["image"]).groups()
    shutil.copyfile(Path(a.cutout) / (i["id"] + ".png"), finals / name)
print("published", len(items))
