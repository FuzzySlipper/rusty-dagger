#!/usr/bin/env bash
# Rebuild the whole content chain for Privateer's Hold:
#   1. extract dungeon -> GLB + engine mesh.json (dagger-import)
#   2. import mesh.json through the engine's asset pipeline (rusty-asset-import)
#   3. publish imported artifacts into the studio-openable project doc
set -euo pipefail
cd "$(dirname "$0")/.."

RUSTY_ASSET_IMPORT="${RUSTY_ASSET_IMPORT:-/home/dev/rusty-engine/target/debug/rusty-asset-import}"
# Offline import/build artifacts never belong in the checked product source.
export CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-/tmp/rusty-dagger-product-model-7266-import-target}"

cargo run -q -p dagger-import --bin dagger-import -- \
    --out authoring-content/privateers-hold.glb
# Hand-edited sprite manifest fields (pivots, sizes, fps, playback sequences)
# survive regeneration via each entry's "edited" marker;
# DAGGER_CLOBBER_SPRITES=1 rewrites everything from classic defaults.
CLOBBER_ARGS=()
if [[ "${DAGGER_CLOBBER_SPRITES:-0}" == "1" ]]; then
  CLOBBER_ARGS+=(--clobber-sprites)
fi
cargo run -q -p dagger-import --bin dagger-import -- \
    --format mesh-json --texture-dir authoring-content/textures --out authoring-content/privateers-hold.mesh.json \
    "${CLOBBER_ARGS[@]}"
"$RUSTY_ASSET_IMPORT" write authoring-content/privateers-hold.mesh.json authoring-content/imported
python3 scripts/generate-project.py --write
# Sprite art validation: deterministic quality checks + visual dump for flagged cases
# Classic per-orientation variance (Rat 65x67 vs 70x27 etc) is warn-level (classic authored);
# extraction mismatches (manifest worldSize vs DFU, PNG dims) are error-level and fail closed.
cargo run -q -p dagger-import --bin dagger-validate-sprites -- \
    --out authoring-content/validation/sprites.json --html authoring-content/validation/sprites
cargo build -q -p dagger-studio-adapter
python3 scripts/check-adapter.py
