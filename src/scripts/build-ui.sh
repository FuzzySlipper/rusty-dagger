#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
repository_root=$(cd "$root/.." && pwd)
engine_root=${RUSTY_ENGINE_ROOT:-"$repository_root/../rusty-engine"}
out="$root/browser-bundle"
mkdir -p "$out"
tsc_bin=${RUSTY_DAGGER_TSC:-}
if [[ -n "$tsc_bin" ]]; then
  node "$tsc_bin" --target ES2022 --module ES2022 --moduleResolution bundler --strict --noEmitOnError --outDir "$out/ui" "$root/ui/main.ts"
elif [[ -f "$engine_root/render/node_modules/typescript/bin/tsc" ]]; then
  node "$engine_root/render/node_modules/typescript/bin/tsc" --target ES2022 --module ES2022 --moduleResolution bundler --strict --noEmitOnError --outDir "$out/ui" "$root/ui/main.ts"
elif command -v tsc >/dev/null 2>&1; then
  tsc --target ES2022 --module ES2022 --moduleResolution bundler --strict --noEmitOnError --outDir "$out/ui" "$root/ui/main.ts"
else
  echo 'TypeScript compiler not found; set RUSTY_DAGGER_TSC or install tsc' >&2
  exit 1
fi
RUSTY_ENGINE_ROOT="$engine_root" node "$root/scripts/generate-browser-bundle.mjs" "$out"
cp "$root/ui/styles.css" "$out/ui/styles.css"
