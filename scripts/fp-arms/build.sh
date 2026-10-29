#!/usr/bin/env bash
# Builds local/first-person.rpak: the 3D first-person arms, the dagger and their held motion (scripts/fp-arms/README.md).
# Needs Blender 5, the rusty CLI, rusty-riders' painted arms (RIDERS_ARMS, default ../rusty-riders/content/art/fp-arms/
# painted.glb) and a capture's outputs in CAPTURE (default local/fp-capture/r01: model.json and bake-*.json).
#
#   scripts/fp-arms/build.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
riders=${RIDERS_ARMS:-../rusty-riders/content/art/fp-arms/painted.glb}
capture=${CAPTURE:-local/fp-capture/r01}
stage=local/fp-arms/staging
[[ -f $riders ]] || { echo "rusty-riders' painted arms are missing at $riders (set RIDERS_ARMS)" >&2; exit 1; }
[[ -f $capture/model.json ]] || { echo "no capture at $capture (set CAPTURE)" >&2; exit 1; }
rm -rf "$stage"
mkdir -p "$stage/first-person"
blender -b --factory-startup --python scripts/fp-arms/build-arms.py -- "$riders" local/fp-arms/arms.glb | grep '^BUILT'
blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- local/fp-arms/dagger.glb | grep '^BUILT'
cp local/fp-arms/arms.glb local/fp-arms/dagger.glb "$stage/first-person/"
python3 scripts/fp-arms/assemble.py "$capture/model.json" "$stage" "$capture"/bake-*.json
for glb in "$stage"/first-person/*.glb; do rusty asset check "$glb" >/dev/null || { echo "rusty refuses $glb" >&2; exit 1; }; done
rusty pack-content "$stage" --output local/first-person.rpak
echo "BUILT local/first-person.rpak"
