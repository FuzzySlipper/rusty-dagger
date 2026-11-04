#!/usr/bin/env python3
"""Review sheets for converted surface families (rusty-dagger #10497): for each family, every converted record's classic
beside its refreshed final, both repeated 2x2 at the same size with square pixels, labelled with its ledger key, and its
caption where it has one. Writes authoring/review/surfaces/<family>.png.

    sheet.py exterior-city-a interior-marble-floors      (or no arguments for every converted family)
"""
import json, sys
from pathlib import Path

from PIL import Image, ImageDraw

AUTHORING = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
from batch import CORRECTIONS, OUT, classic, records  # noqa: E402

CELL = 192
REVIEW = AUTHORING / "review/surfaces"


def repeated(image, width):
    height = round(image.height * width / image.width)
    shown = image.convert("RGB").resize((width // 2, height // 2), Image.Resampling.NEAREST)
    out = Image.new("RGB", (width, height))
    for x in (0, 1):
        for y in (0, 1):
            out.paste(shown, (x * (width // 2), y * (height // 2)))
    return out


def main():
    rows = records()
    captions = json.loads((OUT / "captions.json").read_text()) if (OUT / "captions.json").exists() else {}
    families = sys.argv[1:] or sorted({r["set"].removesuffix("-snow") for r in rows})
    REVIEW.mkdir(parents=True, exist_ok=True)
    for family in families:
        done = [r for r in rows if r["set"].removesuffix("-snow") == family
                and (OUT / r["set"] / f"texture-{int(r['archive']):03d}-{int(r['record'])}.png").exists()]
        if not done:
            continue
        cells = []
        for r in done:
            key = f"texture-{int(r['archive']):03d}-{int(r['record'])}"
            pair = [repeated(classic(r), CELL), repeated(Image.open(OUT / r["set"] / f"{key}.png"), CELL)]
            cells.append((key, CORRECTIONS.get(key, {}).get("caption") or captions.get(key, "winter overlay" if r["set"].endswith("-snow") else ""), pair))
        columns = 4
        cell_height = max(p[0].height for _, _, p in cells) + 46
        sheet = Image.new("RGB", (columns * (2 * CELL + 24) + 12, -(-len(cells) // columns) * cell_height + 12), (25, 25, 28))
        draw = ImageDraw.Draw(sheet)
        for i, (key, text, (old, new)) in enumerate(cells):
            row, column = divmod(i, columns)
            x, y = 12 + column * (2 * CELL + 24), 12 + row * cell_height
            draw.text((x, y), key, fill=(235, 235, 235))
            draw.text((x, y + 12), text[:70], fill=(160, 160, 160))
            sheet.paste(old, (x, y + 28))
            sheet.paste(new, (x + CELL + 6, y + 28))
        sheet.save(REVIEW / f"{family}.png")
        print(REVIEW / f"{family}.png", len(cells))


if __name__ == "__main__":
    main()
