#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
out="$root/browser-bundle"
mkdir -p "$out"
tsc_bin=${RUSTY_DAGGER_TSC:-}
if [[ -z "$tsc_bin" ]]; then
  tsc_bin=/home/dev/worktrees/rusty-engine-csharp-runtime/render/node_modules/typescript/bin/tsc
  if [[ ! -f "$tsc_bin" ]]; then
    # Compiler-only fallback; the served browser-host closure remains the
    # candidate artifact copied by generate-browser-bundle.mjs.
    tsc_bin=/home/dev/rusty-engine/render/node_modules/typescript/bin/tsc
  fi
fi
node "$tsc_bin" --target ES2022 --module ES2022 --moduleResolution bundler --strict --noEmitOnError --outDir "$out/ui" "$root/ui/main.ts"
node "$root/scripts/generate-browser-bundle.mjs" "$out"
cp "$root/ui/styles.css" "$out/ui/styles.css"
