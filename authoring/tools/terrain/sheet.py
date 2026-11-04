#!/usr/bin/env python3
"""Review sheets for the ground (rusty-dagger #10498), in authoring/review/terrain/. For each set, CLIMATE-SEASON.png:
every record's classic above its refreshed final at the same size. And a set's materials.png row: each base repeated
2x2, so seams show. Writes one sheet per built set.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from ground import AUTHORING, OUT, RECORDS, SETS, classic, folders  # noqa: E402

REVIEW = AUTHORING / "review/terrain"
CELL = 128
COLUMNS = 14


def main():
    REVIEW.mkdir(parents=True, exist_ok=True)
    for archive in SETS:
        folder = OUT / folders()[archive]
        rows = -(-RECORDS // COLUMNS)
        bases = sorted((folder / "bases").glob("*.png"))
        height = rows * (2 * CELL + 22) + 2 * CELL + 30
        sheet = Image.new("RGB", (COLUMNS * (CELL + 4) + 8, height), (25, 25, 28))
        draw = ImageDraw.Draw(sheet)
        for record in range(RECORDS):
            row, column = divmod(record, COLUMNS)
            x, y = 6 + column * (CELL + 4), 4 + row * (2 * CELL + 22)
            draw.text((x, y), f"{archive:03d}-{record}", fill=(225, 225, 225))
            sheet.paste(classic(archive, record).resize((CELL, CELL), Image.Resampling.NEAREST), (x, y + 14))
            sheet.paste(Image.open(folder / f"texture-{archive:03d}-{record}.png").convert("RGB"), (x, y + 16 + CELL))
        y = 4 + rows * (2 * CELL + 22)
        for k, path in enumerate(bases):
            base = Image.open(path).convert("RGB")
            x = 6 + k * (2 * CELL + 8)
            draw.text((x, y), path.stem, fill=(225, 225, 225))
            for dx in (0, CELL):
                for dy in (0, CELL):
                    sheet.paste(base, (x + dx, y + 14 + dy))
        sheet.save(REVIEW / f"{folder.name}.png")
        print(REVIEW / f"{folder.name}.png", flush=True)


if __name__ == "__main__":
    main()
