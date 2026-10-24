#!/usr/bin/env bash
# Regenerates the loose tree of files derived from the operator's Daggerfall inputs: the runtime content
# under content/ and the importer's own records under import-records/. None of it is tracked.
#
# The work is the import tool's `import` command, which checks every input, runs every import step in
# dependency order into local/import/staging, and packs local/daggerfall-data.rpak and local/music.rpak.
# This script adds --publish-loose, which copies the staged run into content/ and import-records/, the
# tree the product reads until it opens the containers; it goes away when the product does.
#
# Inputs (all local, none tracked):
#   --arena2 DIR   the Arena2 directory (default $DAGGER_ARENA2, else local/arena2)
#   --donor DIR    the Daggerfall Unity checkout (default $DAGGER_DONOR_ROOT, else /home/research/daggerfall-unity)
#   --sound DIR    the music folder, <track>.<ext> recordings such as the donor song pack (default local/Sound)
#   ffmpeg/ffprobe on PATH, for the cinematics
# Tracked inputs: the source inventory (data/content-source-manifest.csv), the authored UI
# overlay (data/ui-authored-assets.json with data/ui-original/) and the authored base payload
# content/worldrpg/payloads/daggerfall.base.json, which the commands read and never write.
#
# scripts/generated-content-paths.txt and docs/coverage/content-scope.md list what this produces and what
# stays authored.
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

usage="usage: scripts/regenerate-content.sh [--arena2 DIR] [--donor DIR] [--sound DIR] [--sprite-authoring DIR] [--block-variants FILE]
  --sprite-authoring DIR  an operator sprite-overlay root; a site whose overlay (sprites/SITE.json)
                          exists there is written with it. No overlay is tracked, so the default is none.
  --block-variants FILE   authored normalized block/building variant descriptor."
sound=local/Sound
import_args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --arena2|--donor|--sprite-authoring|--block-variants) import_args+=("$1" "${2:?"$usage"}"); shift 2 ;;
    --sound) sound=${2:?"$usage"}; shift 2 ;;
    -h|--help) echo "$usage"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; echo "$usage" >&2; exit 2 ;;
  esac
done

# Files written after this stamp are this run's output; the check at the end looks only at those, so an
# uncommitted authored file does not count as generated.
run_stamp=$(mktemp)
trap 'rm -f "$run_stamp"' EXIT
dotnet build src/Daggerfall.Import.Tool/Daggerfall.Import.Tool.csproj --configuration Release --nologo -v quiet
dotnet src/Daggerfall.Import.Tool/bin/Release/net10.0/Daggerfall.Import.Tool.dll import \
  "${import_args[@]}" --music "$sound" --publish-loose

# Nothing generated may be committed: the Arena2-derived data is not redistributed. In a Git
# checkout, a file this run wrote that Git would pick up (untracked and not ignored) is named and the
# run fails, so a new output path cannot slip into a commit unnoticed.
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  exposed=$(git status --porcelain --untracked-files=all -z -- content import-records \
    | while IFS= read -r -d '' line; do
        [[ "$line" == '?? '* && "${line#?? }" -nt "$run_stamp" ]] && echo "${line#?? }"
      done || true)
  if [[ -n "$exposed" ]]; then
    echo "regenerate-content: these generated paths are not ignored; list them in scripts/generated-content-paths.txt and .gitignore:" >&2
    echo "$exposed" | head -n 20 >&2
    exit 1
  fi
fi

