#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 || $# -gt 5 ]]; then
  echo "usage: bash src/scripts/run-sprite-workbench.sh PUBLICATION_ROOT AUTHORING_ROOT OVERLAY_PATH PORT [--exercise]" >&2
  exit 2
fi

publication_root=$(cd "$1" && pwd)
authoring_root=$(cd "$2" && pwd)
overlay_path=$3
port=$4
exercise=${5:-}
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
engine_root=$(cd "$repo_root/../rusty-engine" && pwd)
if [[ ! -f "$publication_root/import-manifest.json" || ! -d "$authoring_root" || ! "$overlay_path" =~ ^sprites/.+\.json$ || ! "$port" =~ ^[0-9]+$ || ( -n "$exercise" && "$exercise" != "--exercise" ) ]]; then
  echo "publication must be a generated import root; authoring must exist; overlay is sprites/*.json; port is numeric." >&2
  exit 2
fi

stage_dir=$(mktemp -d /tmp/rusty-dagger-sprite-workbench-content.XXXXXX)
bundle_dir=$(mktemp -d /tmp/rusty-dagger-sprite-workbench-bundle.XXXXXX)
cleanup() { rm -rf "$stage_dir" "$bundle_dir"; }
trap cleanup EXIT INT TERM
cp -a "$publication_root/." "$stage_dir/"
node -e 'const fs = require("node:fs"); fs.writeFileSync(process.argv[1], JSON.stringify({ publicationSeparationRoot: process.argv[2], authoringRoot: process.argv[3], overlayPath: process.argv[4] }) + "\n");' "$stage_dir/sprite-workbench.json" "$publication_root" "$authoring_root" "$overlay_path"

dotnet build "$repo_root/src/WorldRpg.SpriteWorkbench/WorldRpg.SpriteWorkbench.csproj"
node "$repo_root/src/scripts/build-sprite-workbench-ui.mjs" "$bundle_dir"
cargo run --manifest-path "$engine_root/Cargo.toml" -p csharp-product-runtime --bin csharp-product-runtime -- \
  --loader coreclr \
  --library "$repo_root/src/WorldRpg.SpriteWorkbench/bin/Debug/net10.0/WorldRpg.SpriteWorkbench.dll" \
  --runtimeconfig "$repo_root/src/WorldRpg.SpriteWorkbench/bin/Debug/net10.0/WorldRpg.SpriteWorkbench.runtimeconfig.json" \
  --bundle-dir "$bundle_dir" \
  --content-dir "$stage_dir" \
  --mode realtime \
  --port "$port" \
  --direct-intent sprite-workbench=payload:worldrpg.sprite-workbench.intent.v1 \
  ${exercise:+--exercise}
