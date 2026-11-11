#!/usr/bin/env bash
# Builds local/data/first-person.rpak: the 3D first-person arms and their clip packs (scripts/fp-arms/README.md), baked by
# build.py from the old game's art in local/inputs/fp-melee (copy-sources.sh) for every clip the held definition
# (content/worldrpg/first-person/daggerfall.held.json) names, and the committed weapon models beside it. Prints what
# the definition is fitted with (local/build/fp-arms/measure.json). Needs Blender 5 and the rusty CLI. The container holds
# Asset Store art: never commit it.
#
#   scripts/fp-arms/build.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
sources=local/inputs/fp-melee
definition=content/worldrpg/first-person/daggerfall.held.json
stage=local/build/fp-arms/staging
[[ -d $sources/MedievalFPArms ]] || { echo "$sources is missing: run scripts/fp-arms/copy-sources.sh first" >&2; exit 1; }
rm -rf "$stage"
mkdir -p "$stage"
blender -b --factory-startup --python scripts/fp-arms/build.py -- "$sources" "$definition" "$stage" | grep '^BUILT ' >/dev/null \
  || { echo "build.py failed: rerun it by hand to see Blender's output" >&2; exit 1; }
mv "$stage/measure.json" local/build/fp-arms/measure.json
python3 - "$stage/first-person" <<'PY'
import hashlib, json, pathlib, sys
out = pathlib.Path(sys.argv[1])
json.dump({
    "asset": "first-person arms (the MedievalFPArms sets, medium colour, and the claw arms build.py grows on their skeleton) and the FPP melee clips the held definition plays",
    "source": "the old Rift Riders game's Assets/MoreArt/FpMelee: Ironbelly Studios' AAA First Person Arm Pack and the FPP melee clips, Unity Asset Store purchases that ship inside the game and are never redistributed",
    "command": "scripts/fp-arms/build.sh (scripts/fp-arms/build.py under Blender 5)",
    "sha256": {str(p.relative_to(out.parent)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(out.rglob("*.glb"))},
}, open(out / "provenance.json", "w"), indent=1)
PY
# The weapon models are the game's own art (authoring/tools/weapons), committed beside the definition; the
# provenance above covers only the arms.
cp -r content/worldrpg/first-person/weapons "$stage/first-person/weapons"
cp -r content/worldrpg/first-person/effects "$stage/first-person/effects"
cp -r content/worldrpg/first-person/mounts "$stage/first-person/mounts"
for glb in $(find "$stage" -name '*.glb'); do rusty asset check "$glb" >/dev/null || { echo "rusty refuses $glb" >&2; exit 1; }; done
rusty pack-content "$stage" --output local/data/first-person.rpak
echo "BUILT local/data/first-person.rpak (fitting measured in local/build/fp-arms/measure.json)"
