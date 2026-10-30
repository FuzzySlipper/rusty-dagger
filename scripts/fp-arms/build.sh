#!/usr/bin/env bash
# Builds local/first-person.rpak: the 3D first-person arms, the dagger and their held motion (scripts/fp-arms/README.md).
# Needs Blender 5, the rusty CLI, an asset-pipeline checkout (ASSET_PIPELINE, default ../asset-pipeline: its fps-arms
# generator with the Daggerfall look, and fp-capture) and a capture's strike bakes in CAPTURE (default
# local/fp-capture/r01: bake-*.json).
#
#   scripts/fp-arms/build.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
pipeline=${ASSET_PIPELINE:-../asset-pipeline}
capture=${CAPTURE:-local/fp-capture/r01}
look=tools/fps-arms/sets/dagger.json
stage=local/fp-arms/staging
[[ -f $pipeline/$look ]] || { echo "asset-pipeline's Daggerfall arms are missing at $pipeline/$look (set ASSET_PIPELINE)" >&2; exit 1; }
compgen -G "$capture/bake-*.json" >/dev/null || { echo "no strike bakes in $capture (set CAPTURE)" >&2; exit 1; }
rm -rf "$stage"
mkdir -p "$stage/first-person"
blender -b --factory-startup --python "$pipeline/tools/fps-arms/generate.py" -- --params "$pipeline/$look" --out local/fp-arms/arms.glb | grep '^FPS_ARMS' | cut -c1-120
blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- local/fp-arms/dagger.glb | grep '^BUILT'
# How the seated hand holds the dagger, measured on these arms (capture.json names them).
blender -b --factory-startup --python "$pipeline/tools/fp-capture/describe.py" -- scripts/fp-arms/capture.json local/fp-arms/model.json | grep '^WROTE'
cp local/fp-arms/arms.glb local/fp-arms/dagger.glb "$stage/first-person/"
python3 - "$pipeline" "$look" "$stage/first-person/arms-provenance.json" <<'EOF'
import hashlib, json, subprocess, sys
pipeline, look, out = sys.argv[1:]
git = lambda *a: subprocess.run(["git", "-C", pipeline, *a], capture_output=True, text=True, check=True).stdout.strip()
json.dump({
    "asset": "first-person arms, Daggerfall look (grey sleeve, brown leather glove)",
    "source": f"asset-pipeline {look} over sets/base.json; hand from MPFB (CC0) via tools/fps-arms/hands/mpfb-default.json",
    "commit": git("rev-parse", "HEAD"),
    "uncommitted": git("status", "--porcelain", "--", "tools/fps-arms", "tools/fp-capture").splitlines(),
    "command": f"blender -b --factory-startup --python <asset-pipeline>/tools/fps-arms/generate.py -- --params <asset-pipeline>/{look} --out first-person/arms.glb",
    "sha256": hashlib.sha256(open("local/fp-arms/arms.glb", "rb").read()).hexdigest(),
}, open(out, "w"), indent=1)
EOF
python3 scripts/fp-arms/assemble.py local/fp-arms/model.json "$stage" "$capture"/bake-*.json
for glb in "$stage"/first-person/*.glb; do rusty asset check "$glb" >/dev/null || { echo "rusty refuses $glb" >&2; exit 1; }; done
rusty pack-content "$stage" --output local/first-person.rpak
echo "BUILT local/first-person.rpak"
