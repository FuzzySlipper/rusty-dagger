#!/usr/bin/env python3
"""Render the nature records into local/art/review/flora using the pinned Rusty renderer.

    python3 authoring/tools/trees/review.py                 # all nature records
    python3 authoring/tools/trees/review.py 506-11 507-11   # refresh just these records
"""
import argparse
import hashlib
import io
import json
import math
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import urllib.request

from PIL import Image

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
CONTENT = ROOT / "content"
PACK = CONTENT / "worldrpg/packs/daggerfall.art-replacements"
TUNING = CONTENT / "worldrpg/tuning/daggerfall.defaults/payload.json"
OUTPUT = ROOT / "local/art/review/flora"
PROJECT = HERE / "preview/FloraPreview.csproj"
# The nature archives' order, as in the flats reference export.
CLIMATES = ("Rainforest", "Subtropical", "Swamp", "Desert", "Temperate woodland", "Temperate woodland · snow",
            "Woodland hills", "Woodland hills · snow", "Haunted woodlands", "Haunted woodlands · snow",
            "Mountains", "Mountains · snow")


def framing(directory, flat, tree):
    """Fit all views to one scale, including the painted frame's foot and the actual mesh vertices."""
    size = flat["placement"]["size"]
    foot = flat["placement"]["foot"]
    low = -size["height"] * foot["y"]
    high = low + size["height"]
    radius = size["width"] * max(foot["x"], 1 - foot["x"])
    triangles = 0
    if tree:
        data = json.loads((directory / tree["mesh"]["path"]).read_bytes())
        scale = tree["placement"]["height"] / tree["mesh"]["height"]
        for part in data["parts"]:
            xyz = iter(part["positions"])
            triangles += len(part["indices"]) // 3
            for x, y, z in zip(xyz, xyz, xyz):
                world_y = y * scale + tree["placement"]["foot"]
                low = min(low, world_y)
                high = max(high, world_y)
                radius = max(radius, math.hypot(x, z) * scale)
    return 1.10 * max(high - low, 2 * radius * 9 / 16), (high + low) / 2, triangles


def classic_reference(directory, flat):
    # Patch chose the original depictions as mesh references.
    # Stage the exported original privately, at the flat's painted size and foot.
    a, r = flat["replaces"]["archive"], flat["replaces"]["record"]
    if (a, r) not in ((500, 4), (500, 31), (502, 4), (502, 11), (503, 1)):
        return False
    name = f"texture-{a}-record-{r:03}-frame-000-source.png"
    source = next((ROOT / "local/art/reference/scenery").glob(f"*/texture-{a}/sprite/{name}"), None)
    if source is None:
        print(f"{a}-{r}: {name} missing from export-scenery; showing the refreshed sprite")
        return False
    path = directory / flat["path"]
    with Image.open(path) as refreshed, Image.open(source) as classic:
        original = classic.convert("RGBA")
        original = original.crop(original.getbbox())
        left, top, right, bottom = refreshed.convert("RGBA").getbbox()
        scale = min((right-left)/original.width, (bottom-top)/original.height)
        original = original.resize((round(original.width*scale), round(original.height*scale)), Image.Resampling.NEAREST)
        image = Image.new("RGBA", refreshed.size)
        image.paste(original, ((left+right-original.width)//2,bottom-original.height))
    image.save(path)
    data = path.read_bytes()
    flat.update(byteLength=len(data), sha256=hashlib.sha256(data).hexdigest())
    return True


def stage(directory, flats, trees, snow, candidates):
    """Stage only the inputs the requested records draw, at their content-relative paths."""
    files = {flat["path"] for flat in flats}
    for tree in trees.values():
        files.update(tree[role]["path"] for role in ("mesh", "leaves", "bark", "stone") if role in tree)
    files.add(snow["path"])
    for path in files:
        dest = directory / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(CONTENT / path, dest)
    originals = {f"{f['replaces']['archive']}-{f['replaces']['record']}" for f in flats if classic_reference(directory, f)}
    if candidates:
        # Engine content references select bytes by path and digest. Describe the trial bytes in
        # this private stage so a trial never resolves the shipped texture or a previous mesh.
        tree = next(iter(trees.values()))
        for role, source in candidates.items():
            relative = f"candidate/{role}{source.suffix}"
            dest = directory / relative
            dest.parent.mkdir(exist_ok=True)
            data = source.read_bytes()
            dest.write_bytes(data)
            tree[role] = dict(path=relative, byteLength=len(data), sha256=hashlib.sha256(data).hexdigest())
            if role == "mesh":
                tree[role]["height"] = json.loads(data)["height"]
            if role in ("stone", "leaves") and "snow" in tree:
                with Image.open(dest) as texture:
                    tree["snow"]["texels"] = texture.width
    payload = {"images": [], "scenery": flats, "trees": trees, "treeSnow": snow}
    (directory / "flora.json").write_text(json.dumps({"artReplacements": payload}))
    (directory / "flats-tuning.json").write_text(json.dumps(json.loads(TUNING.read_bytes())["flats"]))
    Image.new("RGBA", (8, 4), (133, 141, 148, 255)).save(directory / "background.png")
    return originals


def debug(origin, command):
    req = urllib.request.Request(origin + "/__rusty/product/runtime/debug/execute", command.encode(), {"Content-Type": "text/plain; charset=utf-8"})
    try:
        with urllib.request.urlopen(req, timeout=120) as response:
            return response.read().decode().strip()
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"{command}: {error.read().decode().strip()}") from error


def draw(origin, key, height, centre):
    result = debug(origin, f"flora.draw {key.replace('-', ' ')} {height:.8f} {centre:.8f}")
    if result != key:
        raise ValueError(f"Expected {key} before capture, got {result}")
    until = time.monotonic() + 30
    while (state := debug(origin, "flora.ready")) != "ready":
        if state != "preparing" or time.monotonic() >= until:
            raise RuntimeError(f"Cannot capture {key} with its materials: {state}")
        time.sleep(.05)
    url = origin + "/__rusty/product/runtime/frames/capture?format=png&width=1920&height=1080"
    with urllib.request.urlopen(url, timeout=120) as response:
        frame = response.read()
    return Image.open(io.BytesIO(frame[frame.index(b"\x89PNG"):])).convert("RGB")


def store_frame(frame, key, has_mesh, output):
    # Keep the renderer's original pixels for inspecting leaf edges and winding.
    tiles = ["sprite", "front", "side", "rear"] if has_mesh else ["sprite"]
    for i, name in enumerate(tiles):
        x, y = (i % 2) * 960, (i // 2) * 540
        tile = frame.crop((x, y, x + 960, y + 540))
        tile.save(output / f"{key}-{name}.webp", lossless=True, method=1)


def publish_page(output, records, trial=False):
    # Mesh forms are assigned by the tree recipes. The sprite-only records have climate
    # archives but no shared subject tags, so the review groups those by the painted subject.
    groups = json.loads((HERE / "sprite-categories.json").read_bytes())
    sprites = {f"{archive}-{number}": category for archive, categories in groups.items()
               for category, numbers in categories.items() for number in numbers}
    for key, row in records.items():
        form = row["form"]
        if form == "rock":
            row["category"] = "Rocks"
        elif form.endswith("-cactus"):
            row["category"] = "Cacti"
        elif form.endswith("-bush"):
            row["category"] = "Bushes"
        elif form.startswith("deadwood-") or form in ("logs", "gnarled", "dead"):
            row["category"] = "Deadwood"
        else:
            row["category"] = "Trees" if form else sprites.get(key, "Unclassified")

    page = (HERE / "review.html").read_text().replace("/*RECORDS*/[]", json.dumps(list(records.values())))
    if trial:
        page = page.replace("Daggerfall art review</span>", "Daggerfall art review · Candidate</span>")
    (output / "index.html").write_text(page)
    (output / "catalogue.json").write_text(json.dumps(records, indent=1))
    print(output / "index.html")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("keys", nargs="*", metavar="ARCHIVE-RECORD")
    roles = ("mesh", "bark", "leaves", "stone")
    for role in roles:
        parser.add_argument(f"--{role}", type=Path, help=f"try a local {role} file on a single mesh record")
    args = parser.parse_args()
    candidates = {role: getattr(args, role) for role in roles if getattr(args, role)}
    art = json.loads((PACK / "payload.json").read_bytes())["artReplacements"]
    flats = {}
    for flat in art["scenery"]:
        archive, record = flat["replaces"]["archive"], flat["replaces"]["record"]
        if 500 <= archive <= 511:
            flats[f"{archive}-{record}"] = flat
    unknown = set(args.keys) - flats.keys()
    if unknown:
        parser.error(f"No nature flat for {', '.join(sorted(unknown))}")
    keys = list(dict.fromkeys(args.keys or flats))
    if candidates and (len(keys) != 1 or keys[0] not in art["trees"]):
        parser.error("Choose one mesh record to give the candidate its sprite, placement and materials.")
    output = OUTPUT / "trials" / keys[0] if candidates else OUTPUT
    output.mkdir(parents=True, exist_ok=True)
    catalogue = output / "catalogue.json"
    records = {}
    with tempfile.TemporaryDirectory(prefix="flora-preview-") as tmp, tempfile.TemporaryDirectory(dir=output) as rendered:
        directory = Path(tmp)
        trees = {k: dict(art["trees"][k]) for k in keys if k in art["trees"]}
        originals = stage(directory, [flats[key] for key in keys], trees, art["treeSnow"], candidates)
        start = subprocess.run(["rusty", "dev", "start", "--project", str(PROJECT), "--live-debug"],
                               cwd=ROOT, env={**os.environ, "FloraPreviewContent": tmp}, stdout=subprocess.PIPE, text=True, check=True)
        host = json.loads(start.stdout)
        try:
            for index, key in enumerate(keys, 1):
                flat = flats[key]
                tree = trees.get(key)
                height, centre, triangles = framing(directory, flat, tree)
                frame = draw(host["url"].rstrip("/"), key, height, centre)
                store_frame(frame, key, bool(tree), Path(rendered))
                form = Path(art["trees"][key]["mesh"]["path"]).name.split(".")[0] if tree else ""
                form = re.split(r"-p[0-9]+(?:-|$)", form, maxsplit=1)[0]
                if re.fullmatch(r"rock-[0-9a-f]+", form):
                    form = "rock"
                records[key] = dict(key=key, climate=CLIMATES[flat["replaces"]["archive"] - 500], form=form,
                                    height=tree["placement"]["height"] if tree else flat["placement"]["size"]["height"], triangles=triangles)
                if key in originals:
                    records[key]["classicReference"] = True
                if index == 1 or index % 25 == 0:
                    print(f"{index}/{len(keys)}  {key}", flush=True)
        finally:
            subprocess.run(["rusty", "dev", "stop", str(host["port"])], cwd=ROOT, stdout=subprocess.DEVNULL, check=True)
        images = output / "images"
        images.mkdir(exist_ok=True)
        for image in Path(rendered).iterdir():
            image.replace(images / image.name)
    if catalogue.exists():
        records = json.loads(catalogue.read_bytes()) | records
    records = {key: records[key] for key in flats if key in records}
    publish_page(output, records, trial=bool(candidates))


if __name__ == "__main__":
    main()
