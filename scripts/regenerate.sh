#!/usr/bin/env bash
# Rebuild the whole content chain for Privateer's Hold:
#   1. extract dungeon -> GLB + engine mesh.json (dagger-import)
#   2. import mesh.json through the engine's asset pipeline (rusty-asset-import)
#   3. publish imported artifacts into the studio-openable project doc
set -euo pipefail
cd "$(dirname "$0")/.."

RUSTY_ASSET_IMPORT="${RUSTY_ASSET_IMPORT:-/home/dev/rusty-engine/target/debug/rusty-asset-import}"

cargo run -q -p dagger-import -- \
    --out content/privateers-hold.glb
cargo run -q -p dagger-import -- \
    --format mesh-json --out content/privateers-hold.mesh.json
"$RUSTY_ASSET_IMPORT" write content/privateers-hold.mesh.json content/imported
python3 scripts/generate-project.py --write
python3 scripts/check-adapter.py
