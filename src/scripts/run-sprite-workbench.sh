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
# A site closure carries only what is its own and references every sprite atlas from the product-wide
# world media and the classic media group, so it is inspected with both and the atlases it names are
# staged beside it from whichever publication carries them. The world media publication itself is read alone.
world_media="$repo_root/content/worldrpg/imports/shared"
classic_group="$repo_root/content/worldrpg"
product_args=()
if [[ "$publication_root" != "$world_media" ]]; then
  product_args=(--shared "$world_media" --classic-group "$classic_group")
fi
# The workbench reads the importer's neutral inspection document, never the publication's sidecars.
dotnet run --project "$repo_root/src/Daggerfall.Import.Tool/Daggerfall.Import.Tool.csproj" -- \
  sprite-inspection --publication "$publication_root" "${product_args[@]}" --output "$stage_dir/sprite-inspection.json"
node -e 'const fs = require("node:fs"); const path = require("node:path");
const [stage, ...roots] = process.argv.slice(1);
for (const entry of JSON.parse(fs.readFileSync(path.join(stage, "sprite-inspection.json"))).catalog.entries) {
  const target = path.join(stage, entry.closure.relativePath);
  if (fs.existsSync(target)) continue;
  const source = roots.map(root => path.join(root, entry.closure.relativePath)).find(candidate => fs.existsSync(candidate));
  if (!source) throw new Error(`no publication carries sprite atlas ${entry.closure.relativePath}`);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.copyFileSync(source, target);
}' "$stage_dir" "$world_media" "$classic_group"
node -e 'const fs = require("node:fs"); fs.writeFileSync(process.argv[1], JSON.stringify({ publicationSeparationRoot: process.argv[2], authoringRoot: process.argv[3], overlayPath: process.argv[4] }) + "\n");' "$stage_dir/sprite-workbench.json" "$publication_root" "$authoring_root" "$overlay_path"

exec rusty dev \
  --project "$repo_root/src/WorldRpg.SpriteWorkbench/WorldRpg.SpriteWorkbench.csproj" \
  --bind-host "${RUSTY_WORKBENCH_BIND_HOST:-0.0.0.0}" \
  --port "$port"
