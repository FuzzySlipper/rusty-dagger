#!/usr/bin/env python3
"""Publish cut-out finals: copy `<id>.png` from a cutout directory to local/art/generated/<area> under the image's art key.

A paper-doll or worn final also gets a sidecar JSON stating its doll-space rect, from the generation's own input
transform: the source's whole-number scale and its placement on the canvas, as `run.py prepare` recorded them in
the study's `inputs/<id>/input.json`, and the spec's canvas size.

    finalize.py SPEC.json CUTOUT_DIR [--area items] [--finals DIR] [--study STUDY_DIR]
"""
import argparse, json, shutil, subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "paper-doll"))
from art_key import art_key
from doll_preview import generation_rect

AUTHORING = Path(__file__).resolve().parents[2]
ART = AUTHORING.parent / "local" / "art"
DOLL_AREAS = ("paper-doll", "worn")
ap = argparse.ArgumentParser()
ap.add_argument("spec"); ap.add_argument("cutout")
ap.add_argument("--area", default="items")
ap.add_argument("--finals")
ap.add_argument("--study", type=Path, help="the study the cut-outs came from; paper-doll and worn finals need it")
a = ap.parse_args()
if a.area in DOLL_AREAS and a.study is None:
    ap.error(f"--study is required for {a.area} finals, which state their doll-space rect")
finals = Path(a.finals or ART / "generated" / a.area); finals.mkdir(parents=True, exist_ok=True)
spec = json.load(open(a.spec))
for i in spec["items"]:
    target = finals / (art_key(a.area, i["directory"], i["image"]) + ".png")
    shutil.copyfile(Path(a.cutout) / (i["id"] + ".png"), target)
    for kept in (target.parent / "pre-matte" / target.name, target.parent / "pre-matte" / f"{target.stem}-mask.png"):
        kept.unlink(missing_ok=True)   # matte.py starts from what it kept, which this final replaces
    if a.area in ("flats", "people"):
        subprocess.run([sys.executable, Path(__file__).with_name("despeckle.py"), target], check=True)
    if a.area in DOLL_AREAS:
        given = json.load(open(a.study / "inputs" / i["id"] / "input.json"))
        rect = generation_rect(given["export_entry"], spec["size"], given["integer_scale"], given["placement"])
        target.with_suffix(".json").write_text(json.dumps({"rect": rect}))
print("published", len(spec["items"]))
