#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="${RUSTY_STUDIO_BROWSER_OUT:-/tmp/rusty-dagger-studio-check-$$}"
RUSTY_STUDIO_BROWSER_OUT="$OUT" node scripts/check-studio-browser.mjs

python3 - "$OUT" <<'PY'
from pathlib import Path
import sys
from PIL import Image, ImageChops

output = Path(sys.argv[1])
for viewport in ('desktop', 'narrow'):
    before = Image.open(output / f'{viewport}-before-canvas.png').convert('RGB')
    after = Image.open(output / f'{viewport}-canvas.png').convert('RGB')
    if before.size != after.size or before.width == 0 or before.height == 0:
        raise SystemExit(f'{viewport}: invalid focused canvas captures')
    difference = ImageChops.difference(before, after)
    changed = sum(1 for pixel in difference.getdata() if max(pixel) > 12)
    foreground = sum(
        1 for pixel in after.getdata()
        if max(pixel) > 70 and max(pixel) - min(pixel) > 18
    )
    changed_ratio = changed / (before.width * before.height)
    foreground_ratio = foreground / (after.width * after.height)
    if changed_ratio < 0.10:
        raise SystemExit(f'{viewport}: focusing the dungeon did not change enough of the renderer frame ({changed_ratio:.3f})')
    if foreground_ratio < 0.005:
        raise SystemExit(f'{viewport}: focused renderer frame has no meaningful project pixels ({foreground_ratio:.3f})')
    print(f'{viewport}: changed={changed_ratio:.3f} foreground={foreground_ratio:.3f}')
PY

echo "STUDIO BROWSER CHECK PASSED; focused screenshots and DOM captures are in $OUT"
