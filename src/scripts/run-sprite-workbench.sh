#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "usage: bash src/scripts/run-sprite-workbench.sh PUBLICATION_ROOT AUTHORING_ROOT OVERLAY_PATH PORT" >&2
  exit 2
fi

publication_root=$(cd "$1" && pwd)
authoring_root=$(cd "$2" && pwd)
overlay_path=$3
port=$4
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
runtime_pack="$repo_root/.runtime/runtime-pack"
if [[ ! -f "$publication_root/import-manifest.json" || ! -d "$authoring_root" || ! "$overlay_path" =~ ^sprites/.+\.json$ || ! "$port" =~ ^[0-9]+$ || ! -x "$runtime_pack/bin/rusty" ]]; then
  echo "publication must be a generated import root; authoring must exist; overlay is sprites/*.json; port is numeric; and the verified Engine runtime pack must be installed." >&2
  exit 2
fi

stage_dir="$repo_root/src/WorldRpg.SpriteWorkbench/workbench-content"
rm -rf -- "$stage_dir"
mkdir -p "$stage_dir"
trap 'rm -rf -- "$stage_dir"' EXIT INT TERM
cp -a "$publication_root/." "$stage_dir/"
node -e 'const fs = require("node:fs"); fs.writeFileSync(process.argv[1], JSON.stringify({ publicationSeparationRoot: process.argv[2], authoringRoot: process.argv[3], overlayPath: process.argv[4] }) + "\n");' "$stage_dir/sprite-workbench.json" "$publication_root" "$authoring_root" "$overlay_path"

exec "$runtime_pack/bin/rusty" dev \
  --project "$repo_root/src/WorldRpg.SpriteWorkbench/WorldRpg.SpriteWorkbench.csproj" \
  --runtime "$runtime_pack" \
  --port "$port"
