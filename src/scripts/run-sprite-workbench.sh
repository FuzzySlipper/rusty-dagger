#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
usage="usage: bash src/scripts/run-sprite-workbench.sh [PUBLICATION_ROOT [AUTHORING_ROOT [OVERLAY_PATH [PORT]]]]
Each omitted argument comes from RUSTY_WORKBENCH_PUBLICATION, RUSTY_WORKBENCH_AUTHORING,
RUSTY_WORKBENCH_OVERLAY or RUSTY_WORKBENCH_PORT, and otherwise defaults to Privateer's Hold,
the operator-local authoring/ directory, sprites/privateers-hold.json and port 4175."
if [[ $# -gt 4 ]]; then
  echo "$usage" >&2
  exit 2
fi

default_authoring="$repo_root/authoring"
publication_arg=${1:-${RUSTY_WORKBENCH_PUBLICATION:-$repo_root/content/worldrpg/imports/privateers-hold}}
authoring_arg=${2:-${RUSTY_WORKBENCH_AUTHORING:-$default_authoring}}
overlay_path=${3:-${RUSTY_WORKBENCH_OVERLAY:-sprites/privateers-hold.json}}
port=${4:-${RUSTY_WORKBENCH_PORT:-4175}}
# The default authoring root is operator-local output (ignored by Git); create it on first use.
if [[ "$authoring_arg" == "$default_authoring" ]]; then
  mkdir -p "$default_authoring/sprites"
fi

publication_root=$(cd "$publication_arg" && pwd)
authoring_root=$(cd "$authoring_arg" && pwd)
if [[ ! -f "$publication_root/import-manifest.json" || ! -d "$authoring_root" || ! "$overlay_path" =~ ^sprites/.+\.json$ || ! "$port" =~ ^[0-9]+$ ]]; then
  echo "publication must be a generated import root; authoring must exist; overlay is sprites/*.json; and port is numeric." >&2
  echo "$usage" >&2
  exit 2
fi

stage_dir="$repo_root/src/WorldRpg.SpriteWorkbench/workbench-content"
rm -rf -- "$stage_dir"
mkdir -p "$stage_dir"
trap 'rm -rf -- "$stage_dir"' EXIT INT TERM
cp -a "$publication_root/." "$stage_dir/"
# The workbench reads the importer's neutral inspection document, never the publication's sidecars.
dotnet run --project "$repo_root/src/Daggerfall.Import.Tool/Daggerfall.Import.Tool.csproj" -- \
  sprite-inspection --publication "$publication_root" --output "$stage_dir/sprite-inspection.json"
node -e 'const fs = require("node:fs"); fs.writeFileSync(process.argv[1], JSON.stringify({ publicationSeparationRoot: process.argv[2], authoringRoot: process.argv[3], overlayPath: process.argv[4] }) + "\n");' "$stage_dir/sprite-workbench.json" "$publication_root" "$authoring_root" "$overlay_path"

exec rusty dev \
  --project "$repo_root/src/WorldRpg.SpriteWorkbench/WorldRpg.SpriteWorkbench.csproj" \
  --bind-host "${RUSTY_WORKBENCH_BIND_HOST:-0.0.0.0}" \
  --port "$port"
