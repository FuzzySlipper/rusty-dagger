#!/bin/bash
# town385_glbs.sh [OUT]: write townsman 385's skinned splat GLBs for the Engine (asset-pipeline #10813, rusty-engine #10805)
# on both rigs, for every tier present, then check them. OUT defaults to /data/assets/splat-characters/town385 (den-agents,
# outside any public repo). OUT/mixamo/town385-<tier>.glb is the default product route: the Mixamo body skeleton
# (mixamorig: names), framed on PINOC's free library "Walking" as mixamo-glb, with its hips fit (k, A) for PINOC's
# mixamo-glb Walking and Standing Idle written to OUT/checks/. OUT/viggle441/town385-<tier>.glb is the 441-rig contract as
# filed, for PINOC's splat-skeleton clips. The clips live in /data/assets/splat-characters/pinoc-clips/{mixamo,splat-skeleton}/
# on this machine only: never commit or package them.
# Each tier's SRC/town385-<tier>-surface.npz (splat_surface_normals.py, asset-pipeline #10820) adds _NORMAL and _ALBEDO
# for the Engine's splat lighting (rusty-engine #10815). With RUN set to the bake's run folder
# (live-evidence/town385-hires-20261112-r01: the 400k final, the fit16/48/96 distils and their rig npz), the sidecars are
# rebuilt first, with their sheets and reports in OUT/checks/surface-<tier>.{png,json}.
# Needs the SDK tables (npm run inspect:sdk in tests/splats) and, for validation, a gltf-validator in the npx cache.
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd); SPLATS=$(dirname "$HERE")
OUT=${1:-/data/assets/splat-characters/town385}
SRC=${SRC:-$SPLATS/public/pinoc/town385}
SKELETON=${SKELETON:-$SPLATS/extracted/sdk-source/src/loader/skeleton/skeleton-data-441.ts}
[ -f "$SKELETON" ] || SKELETON=/home/agent/dev/asset-pipeline/tests/splats/extracted/sdk-source/src/loader/skeleton/skeleton-data-441.ts
CLIPS=${CLIPS:-/data/assets/splat-characters/pinoc-clips}
REF=${REF:-$CLIPS/mixamo/walking.glb}
WORK=$OUT/rest-check   # inspect_vsplat.py's decoded rest.ply per tier, overwritten on each run
mkdir -p "$OUT/viggle441" "$OUT/mixamo"
for tier in 16k 48k 96k; do
  vsplat=$SRC/town385-$tier.vsplat
  [ -f "$vsplat" ] || { echo "skip $tier: no $vsplat"; continue; }
  surface=$SRC/town385-$tier-surface.npz
  if [ -n "${RUN:-}" ]; then
    mkdir -p "$OUT/checks"
    python3 -I "$HERE/splat_surface_normals.py" "$RUN/final/splat.npz" "$RUN/runtime/fit${tier%k}/splat.npz" \
      "$RUN/runtime/rig/town385-$tier-rig.npz" "$surface" --png "$OUT/checks/surface-$tier.png" > "$OUT/checks/surface-$tier.json"
  fi
  SURFACE=(); [ -f "$surface" ] && SURFACE=(--surface "$surface")
  python3 -I "$SPLATS/inspect_vsplat.py" "$vsplat" --export-dir "$WORK/$tier" > /dev/null
  python3 -I "$HERE/vsplat_to_glb.py" "$vsplat" "$OUT/viggle441/town385-$tier.glb" --skeleton "$SKELETON" \
    --check-ply "$WORK/$tier/rest.ply" --expect-joints 26 --rig viggle441 "${SURFACE[@]}" > "$OUT/viggle441/town385-$tier.json"
  python3 -I "$HERE/vsplat_to_glb.py" "$vsplat" "$OUT/mixamo/town385-$tier.glb" --skeleton "$SKELETON" \
    --check-ply "$WORK/$tier/rest.ply" --expect-joints 22 --rig mixamo --mixamo-reference "$REF" "${SURFACE[@]}" > "$OUT/mixamo/town385-$tier.json"
  mkdir -p "$OUT/checks"
  python3 -I "$HERE/splat_glb_clip_fit.py" "$OUT/mixamo/town385-$tier.glb" "$OUT/checks/mixamo-clip-fit-$tier.json" \
    walk="$CLIPS/mixamo/walking.glb" idle="$CLIPS/mixamo/standing-idle.glb:leg" | sed "s/^/  $tier mixamo fit: /"
  echo "town385-$tier: mixamo and viggle441 written and checked${SURFACE:+, with _NORMAL and _ALBEDO}"
done
VALIDATOR=$(ls -d "$HOME"/.npm/_npx/*/node_modules/gltf-validator 2>/dev/null | head -1 || true)
if [ -n "$VALIDATOR" ]; then
  node -e '
    const v = require(process.argv[1]); const fs = require("fs");
    (async () => { let bad = 0; for (const f of process.argv.slice(2)) {
      const r = await v.validateBytes(new Uint8Array(fs.readFileSync(f)));
      const i = r.issues; bad += i.numErrors + i.numWarnings;
      console.log(`${f}: ${i.numErrors} errors, ${i.numWarnings} warnings, ${i.numInfos} infos, ${i.numHints} hints (glTF Validator ${r.validatorVersion})`);
    } process.exit(bad ? 1 : 0); })();' "$VALIDATOR" "$OUT"/viggle441/*.glb "$OUT"/mixamo/*.glb
else
  echo "no gltf-validator in the npx cache: structural self-checks only"
fi
