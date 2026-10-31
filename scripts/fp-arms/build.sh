#!/usr/bin/env bash
# Builds local/first-person.rpak: the models of the 3D first-person arms and dagger (scripts/fp-arms/README.md). The
# held definition that draws them is authored content (content/worldrpg/first-person/daggerfall.held.json), not built.
# Needs Blender 5, the rusty CLI and an asset-pipeline checkout (ASSET_PIPELINE, default ../asset-pipeline: its
# fps-arms adopt tool with the PSX arms pack and the Daggerfall look).
#
#   scripts/fp-arms/build.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
pipeline=${ASSET_PIPELINE:-../asset-pipeline}
look=tools/fps-arms/sets/psx-dagger.json
stage=local/fp-arms/staging
[[ -f $pipeline/$look ]] || { echo "asset-pipeline's Daggerfall arms are missing at $pipeline/$look (set ASSET_PIPELINE)" >&2; exit 1; }
rm -rf "$stage"
mkdir -p "$stage/first-person"
blender -b --factory-startup --python "$pipeline/tools/fps-arms/adopt.py" -- --params "$pipeline/$look" --out local/fp-arms/arms.glb | grep '^ADOPT ' | cut -c1-120
blender -b --factory-startup --python scripts/fp-arms/build-dagger.py -- local/fp-arms/dagger.glb | grep '^BUILT'
cp local/fp-arms/arms.glb local/fp-arms/dagger.glb "$stage/first-person/"
python3 - "$pipeline" "$look" "$stage/first-person/arms-provenance.json" <<'PY'
import hashlib, json, subprocess, sys
pipeline, look, out = sys.argv[1:]
git = lambda *a: subprocess.run(["git", "-C", pipeline, *a], capture_output=True, text=True, check=True).stdout.strip()
json.dump({
    "asset": "first-person arms, Daggerfall look (hand-painted brown leather glove and grey sleeve)",
    "source": f"asset-pipeline {look}: Drillimpact's PSX First Person Arms 1.1.0 (CC0, tools/fps-arms/packs/psx-drillimpact) with twist joints, the glove edge cut into the mesh and a hand-painted texture",
    "commit": git("rev-parse", "HEAD"),
    "uncommitted": git("status", "--porcelain", "--", "tools/fps-arms", "tools/fp-capture").splitlines(),
    "command": f"blender -b --factory-startup --python <asset-pipeline>/tools/fps-arms/adopt.py -- --params <asset-pipeline>/{look} --out first-person/arms.glb",
    "sha256": hashlib.sha256(open("local/fp-arms/arms.glb", "rb").read()).hexdigest(),
}, open(out, "w"), indent=1)
PY
for glb in "$stage"/first-person/*.glb; do rusty asset check "$glb" >/dev/null || { echo "rusty refuses $glb" >&2; exit 1; }; done
rusty pack-content "$stage" --output local/first-person.rpak
echo "BUILT local/first-person.rpak"
