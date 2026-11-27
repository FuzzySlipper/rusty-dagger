#!/usr/bin/env python3
"""Render the Bay's arms (#11144) as banners and shields for the art pack.

Reads content/worldrpg/packs/daggerfall.base/world/heraldry.json, the one home of the arms, and the pictorial charges'
masks in authoring/tools/heraldry/charges/ (black on white, generated art). Writes each coat's hanging banner and shield
as 8-bit RGBA PNGs into the art pack's media/heraldry/, and the art pack's `heraldry` section with each file's size and
sha256. Template charges are drawn here; a pictorial charge is its mask tinted in its tincture.

    python3 authoring/tools/heraldry/arms.py            # render and update the art pack
    python3 authoring/tools/heraldry/arms.py --sheet OUT.png   # also a review sheet of every coat
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[3]
HERALDRY = ROOT / "content/worldrpg/packs/daggerfall.base/world/heraldry.json"
CHARGES = Path(__file__).resolve().parent / "charges"
PACK = ROOT / "content/worldrpg/packs/daggerfall.art-replacements"
MEDIA = "media/heraldry"
SS = 4  # supersampling
BANNER = (256, 512)
SHIELD = (256, 288)
TAIL = 72  # the swallowtail's depth on a banner
OUTLINE = (26, 23, 20)


def rgb(hex_colour):
    value = hex_colour.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def field_mask(size, division):
    """The second tincture's share of a divided field, as a mask."""
    w, h = size
    mask = Image.new("L", size, 0)
    d = ImageDraw.Draw(mask)
    if division == "per-pale":
        d.rectangle([w / 2, 0, w, h], fill=255)
    elif division == "per-fess":
        d.rectangle([0, h / 2, w, h], fill=255)
    elif division == "per-bend":
        d.polygon([(w, 0), (w, h), (0, h)], fill=255)
    elif division == "quarterly":
        d.rectangle([w / 2, 0, w, h / 2], fill=255)
        d.rectangle([0, h / 2, w / 2, h], fill=255)
    elif division == "per-saltire":
        d.polygon([(0, 0), (w / 2, h / 2), (0, h)], fill=255)
        d.polygon([(w, 0), (w / 2, h / 2), (w, h)], fill=255)
    return mask


def star(cx, cy, outer, inner, points, turn=-math.pi / 2):
    return [(cx + (outer if i % 2 == 0 else inner) * math.cos(turn + i * math.pi / points),
             cy + (outer if i % 2 == 0 else inner) * math.sin(turn + i * math.pi / points)) for i in range(points * 2)]


def ordinary_mask(size, charge, figure_box):
    """A template charge's mask: an ordinary across the whole surface, or a figure in its box."""
    w, h = size
    mask = Image.new("L", size, 0)
    d = ImageDraw.Draw(mask)
    band = min(w, h) * 0.28
    if charge == "pale":
        d.rectangle([w / 2 - band / 2, 0, w / 2 + band / 2, h], fill=255)
    elif charge == "fess":
        cy = figure_box[1] + (figure_box[3] - figure_box[1]) / 2
        d.rectangle([0, cy - band / 2, w, cy + band / 2], fill=255)
    elif charge in ("bend", "saltire"):
        d.line([(0, 0), (w, h)], fill=255, width=int(band))
        if charge == "saltire":
            d.line([(w, 0), (0, h)], fill=255, width=int(band))
    elif charge == "cross":
        cy = figure_box[1] + (figure_box[3] - figure_box[1]) / 2
        d.rectangle([w / 2 - band / 2, 0, w / 2 + band / 2, h], fill=255)
        d.rectangle([0, cy - band / 2, w, cy + band / 2], fill=255)
    elif charge == "chevron":
        x0, y0, x1, y1 = figure_box
        apex = (w / 2, y0 + (y1 - y0) * 0.2)
        d.line([(0, y1), apex, (w, y1)], fill=255, width=int(band), joint="curve")
    elif charge == "chief":
        d.rectangle([0, 0, w, h * 0.3], fill=255)
    elif charge == "bordure":
        pass  # drawn from the outline of the shape it borders, in paint()
    elif charge == "pile":
        d.polygon([(w * 0.18, 0), (w * 0.82, 0), (w / 2, figure_box[3])], fill=255)
    else:
        x0, y0, x1, y1 = figure_box
        cx, cy, r = (x0 + x1) / 2, (y0 + y1) / 2, min(x1 - x0, y1 - y0) / 2
        if charge == "roundel":
            d.ellipse([cx - r * .8, cy - r * .8, cx + r * .8, cy + r * .8], fill=255)
        elif charge == "annulet":
            d.ellipse([cx - r * .8, cy - r * .8, cx + r * .8, cy + r * .8], fill=255)
            d.ellipse([cx - r * .5, cy - r * .5, cx + r * .5, cy + r * .5], fill=0)
        elif charge == "lozenge":
            d.polygon([(cx, cy - r), (cx + r * .62, cy), (cx, cy + r), (cx - r * .62, cy)], fill=255)
        elif charge == "mullet":
            d.polygon(star(cx, cy, r * .95, r * .55, 8), fill=255)
        elif charge == "crescent":
            d.ellipse([cx - r * .85, cy - r * .75, cx + r * .85, cy + r * .95], fill=255)
            d.ellipse([cx - r * .7, cy - r * 1.0, cx + r * .7, cy + r * .5], fill=0)
        elif charge == "tower":
            tw, th = r * 1.1, r * 1.6
            left, top = cx - tw / 2, cy - th / 2 + r * .1
            d.rectangle([left, top, left + tw, top + th], fill=255)
            merlon = tw / 5
            for i in (0, 2, 4):
                d.rectangle([left + i * merlon, top - merlon, left + (i + 1) * merlon, top], fill=255)
            d.rectangle([cx - tw * .16, top + th * .62, cx + tw * .16, top + th], fill=0)
            d.ellipse([cx - tw * .16, top + th * .62 - tw * .16, cx + tw * .16, top + th * .62 + tw * .16], fill=0)
        else:
            raise SystemExit(f"Unknown template charge '{charge}'.")
    return mask


def pictorial_mask(size, charge, figure_box):
    source = Image.open(CHARGES / f"{charge}.png").convert("L")
    ink = ImageChops.invert(source).point(lambda v: 255 if v > 110 else 0)
    ink = ink.crop(ink.getbbox())
    x0, y0, x1, y1 = (int(v) for v in figure_box)
    scale = min((x1 - x0) / ink.width, (y1 - y0) / ink.height)
    fitted = ink.resize((max(1, int(ink.width * scale)), max(1, int(ink.height * scale))), Image.LANCZOS)
    mask = Image.new("L", size, 0)
    mask.paste(fitted, (x0 + (x1 - x0 - fitted.width) // 2, y0 + (y1 - y0 - fitted.height) // 2))
    return mask


def bordure(shape):
    """A band along the edge of <shape>: the shape less itself shrunk about its middle."""
    w, h = shape.size
    inner = shape.resize((int(w * .8), int(h * .84)), Image.LANCZOS)
    band = shape.copy()
    band.paste(0, (int(w * .1), int(h * .07)), inner)
    return band


def paint(size, arms, tinctures, figure_box, shape):
    field = Image.new("RGBA", size, rgb(tinctures[arms["field"]]) + (255,))
    if "division" in arms:
        field.paste(Image.new("RGBA", size, rgb(tinctures[arms["second"]]) + (255,)), (0, 0), field_mask(size, arms["division"]))
    charge = arms["charge"]
    mask = (pictorial_mask(size, charge, figure_box) if "pictorial" in arms
            else bordure(shape) if charge == "bordure" else ordinary_mask(size, charge, figure_box))
    # A dark outline keeps a charge readable on a field of its own kind of tincture, as the tapestries draw them.
    outline = mask.filter(ImageFilter.MaxFilter(SS * 3 + 1))
    field.paste(Image.new("RGBA", size, OUTLINE + (255,)), (0, 0), outline.point(lambda v: v * 200 // 255))
    field.paste(Image.new("RGBA", size, rgb(tinctures[arms["chargeTincture"]]) + (255,)), (0, 0), mask)
    return field


def banner(arms, tinctures):
    w, h = BANNER[0] * SS, BANNER[1] * SS
    shape = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(shape)
    tail = TAIL * SS
    d.polygon([(0, 0), (w, 0), (w, h), (w / 2, h - tail), (0, h)], fill=255)
    cloth = paint((w, h), arms, tinctures, (w * .1, h * .16, w * .9, h * .62), shape)
    hem = Image.new("L", (w, h), 0)
    ImageDraw.Draw(hem).polygon([(0, 0), (w, 0), (w, h), (w / 2, h - tail), (0, h)], outline=255, width=SS * 6)
    cloth.paste(Image.new("RGBA", (w, h), (196, 160, 72, 255)), (0, 0), hem)
    cloth.putalpha(shape)
    return cloth.resize(BANNER, Image.LANCZOS)


def shield(arms, tinctures):
    w, h = SHIELD[0] * SS, SHIELD[1] * SS
    shape = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(shape)
    points = [(0, 0), (w, 0), (w, h * .45)]
    points += [(w / 2 + (w / 2) * math.cos(t), h * .45 + (h * .55) * math.sin(t)) for t in [i * math.pi / 2 / 24 for i in range(1, 24)]]
    points += [(w / 2, h)]
    points += [(w / 2 - (w / 2) * math.cos(t), h * .45 + (h * .55) * math.sin(t)) for t in [i * math.pi / 2 / 24 for i in range(23, 0, -1)]]
    points += [(0, h * .45)]
    d.polygon(points, fill=255)
    face = paint((w, h), arms, tinctures, (w * .14, h * .12, w * .86, h * .72), shape)
    rim = Image.new("L", (w, h), 0)
    ImageDraw.Draw(rim).polygon(points, outline=255, width=SS * 5)
    face.paste(Image.new("RGBA", (w, h), OUTLINE + (255,)), (0, 0), rim)
    face.putalpha(shape)
    return face.resize(SHIELD, Image.LANCZOS)


def entry(path):
    data = path.read_bytes()
    return {"path": str(path.relative_to(PACK.parent.parent.parent)).replace("\\", "/"), "byteLength": len(data),
            "sha256": hashlib.sha256(data).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--sheet", type=Path, help="also write a review sheet of every coat here")
    args = parser.parse_args()
    heraldry = json.loads(HERALDRY.read_text())
    tinctures = heraldry["tinctures"]
    out = PACK / MEDIA
    out.mkdir(parents=True, exist_ok=True)
    section, images = {}, []
    for kind in ("seats", "regions"):
        for name, arms in heraldry[kind].items():
            key = f"{kind[:-1]}:{name}"
            files = {}
            for form, draw in (("banner", banner), ("shield", shield)):
                image = draw(arms, tinctures)
                path = out / f"{kind[:-1]}-{name}-{form}.png"
                image.save(path, optimize=True)
                files[form] = entry(path)
                if form == "shield":
                    images.append((key, image))
            section[key] = files
    payload_path = PACK / "payload.json"
    payload = json.loads(payload_path.read_text())
    payload["artReplacements"]["heraldry"] = section
    payload_path.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + "\n")
    print(f"Rendered {len(section)} coats into {out}.")
    if args.sheet:
        cols = 8
        rows = math.ceil(len(images) / cols)
        sheet = Image.new("RGBA", (cols * 140, rows * 170), (58, 52, 46, 255))
        d = ImageDraw.Draw(sheet)
        for index, (key, image) in enumerate(images):
            x, y = (index % cols) * 140, (index // cols) * 170
            sheet.alpha_composite(image.resize((112, 126), Image.LANCZOS), (x + 14, y + 8))
            d.text((x + 6, y + 140), key.split(":", 1)[1][:20], fill=(235, 225, 200, 255))
        sheet.save(args.sheet)
        banners = Image.new("RGBA", (6 * 140, 280), (58, 52, 46, 255))
        for index, key in enumerate(["seat:daggerfall", "seat:wayrest", "seat:sentinel", "region:isle-of-balfiera", "region:dakfron", "region:koegria"]):
            kind, name = key.split(":", 1)
            image = Image.open(out / f"{kind}-{name}-banner.png")
            banners.alpha_composite(image.resize((128, 256), Image.LANCZOS), (index * 140 + 6, 12))
        banners.save(args.sheet.with_name(args.sheet.stem + "-banners.png"))


if __name__ == "__main__":
    main()
