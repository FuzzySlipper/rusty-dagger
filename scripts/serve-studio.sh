#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

cargo build -q -p dagger-studio-adapter
STATIC_ROOT="${RUSTY_ENGINE_STUDIO_STATIC_ROOT:-/home/dev/rusty-engine/studio/dist/apps/studio-app/browser}"
if [[ ! -f "$STATIC_ROOT/index.html" ]]; then
    echo "Studio static app not found at $STATIC_ROOT" >&2
    echo "Set RUSTY_ENGINE_STUDIO_STATIC_ROOT to the exact Rusty Engine Studio build." >&2
    exit 1
fi
COMMIT="$(git rev-parse HEAD)"
export RUSTY_DAGGER_COMMIT="$COMMIT"
exec node scripts/studio-host.mjs \
    --adapter-binary "$PWD/target/debug/dagger-studio-adapter" \
    --static-root "$STATIC_ROOT" \
    --host "${RUSTY_STUDIO_HOST:-127.0.0.1}" \
    --port "${RUSTY_STUDIO_PORT:-4173}"
