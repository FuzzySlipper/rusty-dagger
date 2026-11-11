#!/usr/bin/env python3
"""Composite paper dolls offline from classic layers and generated finals, to check a set fits before it is promoted.

Each doll is drawn three ways side by side, with every layer placed where the game places it: at its
header offset less the donor's paperDollOrigin (200, 8), in the classic 110x184 doll grid.

  - classic:   the local/art/reference source layers.
  - generated: each final at the doll-space rect its sidecar JSON states, so drift from the source shows as
               offset or overhang.
  - overlay:   the generated doll at half strength over the classic one, to judge registration by eye.

Every final states its rect in a sidecar JSON beside it (`rect`, in doll units). finalize.py writes it from the
generation's input transform (`run.py prepare` records the source's whole-number scale and its placement on the
canvas), so the whole canvas lands in doll space; dress.py, bodies.py and pictures.py write the rect they draw
in. A final without one is refused rather than placed by guesswork. A layer with no final is drawn classic in every column. The report (printed, and written as JSON beside the
PNG) gives each layer's doll-space rect and its overlap with the source silhouette (IoU), which is the
registration number to watch while generating.

After each doll's layer rects it prints the proportion check of its generated body and head (landmarks.py):
each landmark and outline band beyond tolerance against the classic standard.

Layers are drawn in the order given: background colour, body, head, then each --item in turn. The game orders
items by template draw order and cuts the clothed body at the waist under chest or leg items; list items in
draw order and pick the body layer (i0 unclothed, i1 clothed) to match.

    doll_preview.py --race 0 --gender male --face 3 --item texture-251-record-002-frame-000
    doll_preview.py --sheet bodies                      # every race and gender, head 0, no items
    doll_preview.py --sheet faces --race 0 --gender female

Races are the BODY/FACE file numbers: 0 breton, 1 redguard, 2 nord, 3 dark elf, 4 high elf, 5 wood elf,
6 khajiit, 7 argonian. Output goes to local/art/review/paper-doll/ unless --out says otherwise.
"""
import argparse, glob, json
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

AUTHORING = Path(__file__).resolve().parents[2]
ART = AUTHORING.parent / "local" / "art"
DF_DOLL = ART / "reference" / "paper-doll"
DF_ITEMS = ART / "reference" / "items"
# Where finals are looked for, first match first: --paper-doll and --worn put a directory in front.
FINALS = {"paper-doll": [ART / "generated" / "paper-doll"], "worn": [ART / "generated" / "worn"]}
# Where worn sources are read, first match first: a source edit (local/art/generated/source-edits/worn-neutral-mens, with
# the dye placeholder neutralised) in the export's layout goes in front.
WORN_SOURCES = [DF_ITEMS]

ORIGIN = (200, 8)            # the donor's paperDollOrigin
DOLL = (110, 184)            # the donor's doll size
MARGIN = 24                  # doll units around the doll, so overhang stays visible
GROUND = (48, 52, 59, 255)   # the generation background, #30343b
RACES = ["breton", "redguard", "nord", "dark-elf", "high-elf", "wood-elf", "khajiit", "argonian"]
MORPH = {0: "human", 1: "human", 2: "human", 3: "elf", 4: "elf", 5: "elf", 6: "khajiit", 7: "argonian"}
MODES = ("classic", "canvas")


class Layer:
    """One source layer: its native image, where the doll draws it, and the final that replaces it, if any."""

    def __init__(self, label, source, entry, final):
        self.label, self.final = label, final
        self.source = Image.open(source).convert("RGBA")
        self.x, self.y = entry["xOffset"] - ORIGIN[0], entry["yOffset"] - ORIGIN[1]


def final(area, file):
    return next((d / file for d in FINALS[area] if (d / file).exists()), None)


def doll_layer(family, name, record, label):
    directory = DF_DOLL / family / name
    image = f"{name}-record-{record:03d}-frame-000-source.png"
    entry = next(e for e in json.load(open(directory / "manifest.json"))["images"] if e["file"] == image)
    return Layer(label, directory / image, entry, final("paper-doll", f"{family}--{name}--{image.removesuffix('-source.png')}.png"))


@lru_cache
def worn_sources(role):
    """Every player-layer source for one body (e.g. female-human), or for any body with no role, by art key."""
    found = {}
    for path in [p for root in WORN_SOURCES for p in sorted(glob.glob(str(root / "*" / "*" / "manifest.json")))]:
        for e in json.load(open(path))["images"]:
            if e["file"].endswith("-source.png") and (role is None or e["role"].split("/")[1:2] == [role]):
                key = Path(e["file"]).name.removesuffix("-source.png")
                found.setdefault(key, (Path(path).parent / e["file"], e))
    return found


def worn_layer(key, role):
    """A worn layer drawn for one body, or shared by every body (a held weapon, an artifact)."""
    found = worn_sources(role).get(key) or worn_sources(None).get(key)
    if found is None:
        raise SystemExit(f"No {role} player layer named {key} in {[str(r) for r in WORN_SOURCES]}.")
    source, entry = found
    return Layer(key, source, entry, final("worn", f"{key}.png"))


def layers_for(race, gender, face, body_layer, items):
    number = race + (10 if gender == "female" else 0)
    stack = [doll_layer("bodies", f"body{number:02d}{body_layer}", 0, "body"),
             doll_layer("faces", f"face{number:02d}i0", face, f"head {face}")]
    return stack + [worn_layer(key, f"{gender}-{MORPH[race]}") for key in items]


def generation_rect(entry, size, scale, at):
    """The doll-space rect of a generation canvas: run.py prepare scaled the source (its export entry) by a whole
    number `scale` and pasted it at `at` on a size x size canvas, so the canvas reaches that far past the source."""
    x, y = entry["xOffset"] - ORIGIN[0], entry["yOffset"] - ORIGIN[1]
    return [x - at[0] / scale, y - at[1] / scale, size / scale, size / scale]


def placed(layer, mode):
    """The image to draw and its doll-space rect (x, y, width, height)."""
    native = layer.source
    if mode == "classic" or layer.final is None:
        return native, (layer.x, layer.y, native.width, native.height)
    stated = layer.final.with_suffix(".json")
    if not stated.exists():
        raise SystemExit(f"{layer.final} has no sidecar stating its doll-space rect; finalize.py writes it.")
    return Image.open(layer.final).convert("RGBA"), tuple(json.load(open(stated))["rect"])


def draw(image, rect, k, canvas):
    x, y, w, h = rect
    size = (max(1, round(w * k)), max(1, round(h * k)))
    smooth = image.width > w * 1.5
    drawn = image.resize(size, Image.Resampling.LANCZOS if smooth else Image.Resampling.NEAREST)
    layer = Image.new("RGBA", canvas.size)
    layer.paste(drawn, (round((x + MARGIN) * k), round((y + MARGIN) * k)))
    canvas.alpha_composite(layer)
    return layer.getchannel("A")


def render(layers, mode, k, report):
    w, h = (DOLL[0] + 2 * MARGIN) * k, (DOLL[1] + 2 * MARGIN) * k
    canvas = Image.new("RGBA", (w, h), GROUND)
    for layer in layers:
        image, rect = placed(layer, mode)
        alpha = draw(image, rect, k, canvas)
        row = report.setdefault(layer.label, {"final": str(layer.final) if layer.final else None})
        if mode == "canvas" and layer.final is not None:
            row["rect"] = [round(v, 2) for v in rect]
            source = Image.new("RGBA", canvas.size)
            source_alpha = draw(layer.source, (layer.x, layer.y, *layer.source.size), k, source)
            a, b = np.array(alpha) > 16, np.array(source_alpha) > 16
            row["iou"] = round(float((a & b).sum() / max(1, (a | b).sum())), 3)
    frame = ImageDraw.Draw(canvas)
    frame.rectangle((MARGIN * k, MARGIN * k, (MARGIN + DOLL[0]) * k - 1, (MARGIN + DOLL[1]) * k - 1), outline=(200, 180, 120, 255))
    return canvas


def triptych(layers, k, title):
    report = {}
    classic, canvas = (render(layers, mode, k, report) for mode in MODES)
    panels = [classic, canvas, Image.blend(classic, canvas, 0.5)]
    sheet = Image.new("RGBA", (sum(p.width for p in panels) + 8 * (len(panels) - 1), panels[0].height + 18), (20, 20, 22, 255))
    ImageDraw.Draw(sheet).text((4, 3), title, fill=(240, 240, 240, 255))
    for i, (panel, label) in enumerate(zip(panels, ("classic", "generated", "overlay"))):
        sheet.paste(panel, (i * (panel.width + 8), 18))
        ImageDraw.Draw(sheet).text((i * (panel.width + 8) + 4, 22), label, fill=(230, 230, 230, 255))
    return sheet, report


def grid(cells, columns):
    w, h = cells[0].width, cells[0].height
    rows = (len(cells) + columns - 1) // columns
    sheet = Image.new("RGBA", (columns * (w + 12), rows * (h + 12)), (12, 12, 14, 255))
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % columns) * (w + 12), (i // columns) * (h + 12)))
    return sheet


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--race", type=int, default=0, choices=range(8))
    ap.add_argument("--gender", default="male", choices=("male", "female"))
    ap.add_argument("--face", type=int, default=0, help="head record, 0-9")
    ap.add_argument("--body-layer", default="i1", choices=("i0", "i1"), help="i0 unclothed, i1 clothed")
    ap.add_argument("--item", action="append", default=[], help="worn art key, e.g. texture-237-record-000-frame-000; repeat, in draw order")
    ap.add_argument("--sheet", choices=("bodies", "faces"), help="every race and gender, or every head of one body")
    ap.add_argument("--scale", type=int, default=4, help="output pixels per doll unit")
    ap.add_argument("--worn", type=Path, action="append", default=[], help="worn finals directory to look in first; repeat, first wins")
    ap.add_argument("--paper-doll", type=Path, action="append", default=[],
                    help="body or head finals directory to look in first; repeat, first wins")
    ap.add_argument("--out", type=Path)
    a = ap.parse_args()
    FINALS["worn"][:0] = a.worn
    FINALS["paper-doll"][:0] = a.paper_doll

    out_dir = ART / "review" / "paper-doll"
    if a.sheet == "bodies":
        dolls = [(race, gender, a.face) for gender in ("male", "female") for race in range(8)]
    elif a.sheet == "faces":
        dolls = [(a.race, a.gender, face) for face in range(10)]
    else:
        dolls = [(a.race, a.gender, a.face)]
    cells, reports = [], {}
    for race, gender, face in dolls:
        title = f"{RACES[race]} {gender}, head {face}" + (f", {len(a.item)} items" if a.item else "")
        cell, report = triptych(layers_for(race, gender, face, a.body_layer, a.item), a.scale, title)
        cells.append(cell)
        reports[title] = report
    sheet = cells[0] if len(cells) == 1 else grid(cells, 2)
    name = a.sheet or f"doll-{RACES[a.race]}-{a.gender}-head{a.face}" + ("-" + "-".join(k.split("-record-")[0].removeprefix("texture-") + "r" + k.split("-record-")[1][:3] for k in a.item) if a.item else "")
    out = a.out or out_dir / f"{name}.png"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.convert("RGB").save(out)
    out.with_suffix(".json").write_text(json.dumps(reports, indent=1))
    # The proportion check (landmarks.py) of each doll's generated body and head, beside its registration.
    from landmarks import STANDARD, body_report, head_report
    spec = json.load(open(STANDARD))
    for (title, report), (race, gender, face) in zip(reports.items(), dolls):
        print(title)
        for label, row in report.items():
            print(f"  {label:40s} " + ("classic only" if row["final"] is None else f"IoU {row['iou']}  rect {row['rect']}"))
        number = race + (10 if gender == "female" else 0)
        for line in (body_report(spec, number, a.body_layer)[0], head_report(spec, number, face)[0]):
            print("  landmarks " + line.replace("\n", "\n  "))
    print("wrote", out)


if __name__ == "__main__":
    # Run as the imported module, so landmarks.py (which imports it) sees the finals directories given here.
    import doll_preview
    doll_preview.main()
