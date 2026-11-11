#!/usr/bin/env bash
# In-game contact sheet of a family's attacks, drawn by the game's own held viewmodel through the developer workbench
# (authoring/tools/fp-arms/README.md): for each attack, the view at rest and held every STEP seconds of the attack as the rules
# time it, each tile labelled with its moment, phase and clip time, the hit's tile outlined. The host must run with
# --live-debug, a 3D viewmodel style and local/data/first-person.rpak; the sheet holds world time while it
# draws, then gives it back and leaves the viewer off.
#
#   authoring/tools/fp-arms/sheet.sh ORIGIN LOOK OUT_DIR [STEP=0.05] [ATTACK...]
#   authoring/tools/fp-arms/sheet.sh http://127.0.0.1:39937 weapon.dagger.steel local/evidence/sheets
set -euo pipefail
origin=$1; look=$2; out=$3; step=${4:-0.05}; shift $(( $# < 4 ? $# : 4 ))
root=$(cd "$(dirname "$0")/../../.." && pwd)
pin=$(sed -n 's:.*<RustyEnginePackageVersion>\(.*\)</RustyEnginePackageVersion>.*:\1:p' "$root/Directory.Build.props")
debug=${RUSTY_ENGINE_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/rusty-engine}/pairs/$pin/runtime-pack/bin/rusty-live-debug
run() { "$debug" --origin "$origin" --command "$*"; }
capture() {
  curl -s -H "Origin: $origin" "$origin/__rusty/product/runtime/frames/capture?format=png&width=960&height=540" |
    python3 -c "import sys; b = sys.stdin.buffer.read(); open(sys.argv[1], 'wb').write(b[b.find(b'\x89PNG'):])" "$1"
}
strikes=("$@")
(( ${#strikes[@]} )) || strikes=(fastA fastB fastC fastD powerIn powerOut kick cast)
mkdir -p "$out"
run engine.time.mode manual >/dev/null
for strike in "${strikes[@]}"; do
  dir=$out/$strike; mkdir -p "$dir"; : > "$dir/labels.txt"
  shown=$(run held.motion "$look" "$strike") || { echo "$shown" >&2; exit 1; }
  run engine.time.advance 400 >/dev/null; capture "$dir/rest.png"; echo "rest" >> "$dir/labels.txt"
  hit=$(sed -n 's/.*the hit is at \([0-9.]*\) s.*/\1/p' <<<"$shown" | head -1)
  duration=$(sed -n 's/.*recovery \([0-9.]*\) s.*/\1/p' <<<"$shown" | head -1)
  total=$(python3 -c "import re,sys; s=sys.argv[1]; print(sum(float(x) for x in re.findall(r'(?:windup|commit|recovery) ([0-9.]+) s', s)))" "$shown")
  first=1
  for t in $(python3 -c "import sys; d,s=float(sys.argv[1]),float(sys.argv[2]); n=int(d/s+1e-6); print(' '.join(f'{i*s:.3f}' for i in range(n+1)))" "$total" "$step"); do
    line=$(run held.motion.at "$t")
    # The first moment fades in over the idle loop; later ones only move forward through the clip.
    run engine.time.advance $([[ $first == 1 ]] && echo 300 || echo 17) >/dev/null; first=0
    capture "$dir/$t.png"; echo "$line" >> "$dir/labels.txt"
  done
  echo "$hit" > "$dir/hit.txt"
done
run held.motion.off >/dev/null
run engine.time.mode realtime >/dev/null
python3 - "$out" "$look" "${strikes[@]}" <<'PY'
import glob, os, re, sys
from PIL import Image, ImageDraw
out, look, strikes = sys.argv[1], sys.argv[2], sys.argv[3:]
for strike in strikes:
    d = f"{out}/{strike}"
    hit = float(open(f"{d}/hit.txt").read())
    frames = ["rest"] + sorted((p[:-4] for p in os.listdir(d) if re.match(r"^[0-9.]+\.png$", p)), key=float)
    labels = [l.strip() for l in open(f"{d}/labels.txt")]
    cols = 4; rows = -(-len(frames) // cols); w, h = 480, 270
    sheet = Image.new("RGB", (w * cols, h * rows + 24), (20, 20, 20))
    ImageDraw.Draw(sheet).text((8, 6), f"{look} {strike} (hit at {hit:.3f} s)", fill="white")
    for i, (name, label) in enumerate(zip(frames, labels)):
        tile = Image.open(f"{d}/{name}.png").resize((w, h))
        draw = ImageDraw.Draw(tile)
        text = "rest" if name == "rest" else re.sub(r"^\S+ at ", "", label).split("; the hit")[0]
        draw.rectangle((0, 0, w, 18), fill=(0, 0, 0))
        draw.text((6, 3), text, fill="yellow")
        if name != "rest" and abs(float(name) - hit) < 1e-3:
            draw.rectangle((0, 0, w - 1, h - 1), outline=(255, 60, 60), width=4)
        sheet.paste(tile, ((i % cols) * w, (i // cols) * h + 24))
    sheet.save(f"{out}/{strike}.png")
    print(f"{out}/{strike}.png")
PY
