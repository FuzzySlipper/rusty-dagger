#!/usr/bin/env python3
"""Write a batch's two specs (Qwen reconstruction and Krea retro) from an area template and a list of items.

    make_spec.py AREA NAME ITEMS.json --template KIND [--title TEXT]

ITEMS.json is a list of {id, directory, image, content, uncertainty, invention, name?}. `directory` and
`image` address a source image the way the importer manifest does, relative to df-raw/AREA. Every image
is checked against its manifest; `name` defaults to the id plus the native size. The templates
(specs/AREA/templates/KIND.json and KIND-retro.json, or specs/TEMPLATE-AREA/templates with --template-area; the fx template is shared by effects and spells) carry the style, collection rules and routes.
Writes specs/AREA/PREFIX-NAME.json and PREFIX-NAME-retro.json, PREFIX being items-icons for items
and the area name otherwise (the names run_batch.sh and review_ledger.py look for).
"""
import argparse, hashlib, json, sys
from pathlib import Path

from art_key import source_dir

AUTHORING = Path(__file__).resolve().parents[2]
ap = argparse.ArgumentParser()
ap.add_argument("area"); ap.add_argument("name"); ap.add_argument("items")
ap.add_argument("--template", required=True)
ap.add_argument("--template-area", help="area whose templates directory holds the template (default: AREA)")
ap.add_argument("--title")
a = ap.parse_args()

spec_dir = AUTHORING / "specs" / a.area
prefix = "items-icons" if a.area == "items" else a.area
items = json.load(open(a.items))
seen = {}
for it in items:
    manifest = json.load(open(AUTHORING / "df-raw" / source_dir(a.area) / it["directory"] / "manifest.json"))
    entry = next((e for e in manifest["images"] if e["file"] == it["image"]), None)
    if entry is None:
        sys.exit(f"{it['id']}: {it['image']} is not in {it['directory']}/manifest.json")
    digest = hashlib.md5((AUTHORING / "df-raw" / source_dir(a.area) / it["directory"] / it["image"]).read_bytes()).hexdigest()
    if digest in seen:
        print(f"warning: {it['id']} has the same pixels as {seen[digest]}")
    seen[digest] = it["id"]
    it.setdefault("name", f"{it['id']} ({entry['width']}x{entry['height']})")
for suffix in ("", "-retro"):
    spec = json.load(open(AUTHORING / "specs" / (a.template_area or a.area) / "templates" / f"{a.template}{suffix}.json"))
    spec["items"] = items
    spec["title"] = (a.title or a.name) + (" / Qwen then retro" if suffix else "")
    out = spec_dir / f"{prefix}-{a.name}{suffix}.json"
    json.dump(spec, open(out, "w"), indent=2)
    print("wrote", out.relative_to(AUTHORING))
