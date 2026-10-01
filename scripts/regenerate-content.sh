#!/usr/bin/env bash
# Regenerates every file that is derived from the operator's Daggerfall inputs: the runtime content
# under content/ and the importer's own records under import-records/. None of it is tracked.
#
# Inputs (all local, none tracked):
#   --arena2 DIR   the Arena2 directory (default $DAGGER_ARENA2, else local/arena2)
#   --donor DIR    the Daggerfall Unity checkout (default $DAGGER_DONOR_ROOT, else /home/research/daggerfall-unity)
#   --sound DIR    the donor song folder (default local/Sound)
#   ffmpeg/ffprobe on PATH, for the cinematics
# Tracked inputs: the source inventory (data/content-source-manifest.csv), the authored UI
# overlay (data/ui-authored-assets.json with data/ui-original/) and the authored base payload
# content/worldrpg/payloads/daggerfall.base.json, which the commands read and never write.
#
# The order below is the importer's dependency order; several commands read what an earlier one
# wrote. scripts/generated-content-paths.txt and docs/coverage/content-scope.md list what this
# produces and what stays authored.
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

usage="usage: scripts/regenerate-content.sh [--arena2 DIR] [--donor DIR] [--sound DIR] [--sprite-authoring DIR]
  --sprite-authoring DIR  an operator sprite-overlay root; a site whose overlay (sprites/SITE.json)
                          exists there is written with it. No overlay is tracked, so the default is none."
arena2=${DAGGER_ARENA2:-local/arena2}
donor=${DAGGER_DONOR_ROOT:-/home/research/daggerfall-unity}
sound=local/Sound
sprite_authoring=
while [[ $# -gt 0 ]]; do
  case "$1" in
    --arena2) arena2=${2:?"$usage"}; shift 2 ;;
    --donor) donor=${2:?"$usage"}; shift 2 ;;
    --sound) sound=${2:?"$usage"}; shift 2 ;;
    --sprite-authoring) sprite_authoring=${2:?"$usage"}; shift 2 ;;
    -h|--help) echo "$usage"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; echo "$usage" >&2; exit 2 ;;
  esac
done

missing() {
  echo "regenerate-content: missing input: $1" >&2
  exit 1
}

# Every input is checked before anything is written, so an absent input stops the run with its
# name instead of leaving a half-regenerated tree.
[[ -d "$arena2" ]] || missing "Arena2 directory '$arena2' (link local/arena2 to the game's ARENA2 folder or pass --arena2)"
for file in MAPS.BSA BLOCKS.BSA ARCH3D.BSA MONSTER.BSA DAGGER.SND TEXT.RSC CLASSES.DAT CLIMATE.PAK POLITIC.PAK WOODS.WLD FACTION.TXT SPELLS.STD MAGIC.DEF ANIM0000.VID AZURA.FLC; do
  [[ -f "$arena2/$file" ]] || missing "Arena2 file '$arena2/$file'"
done
[[ -d "$arena2/books" ]] || missing "Arena2 books directory '$arena2/books'"
[[ -d "$donor" ]] || missing "Daggerfall Unity checkout '$donor' (set DAGGER_DONOR_ROOT or pass --donor)"
donor_files=(
  "Assets/Scripts/Utility/EnemyBasics.cs"
  "Assets/Scripts/Game/Items/ItemEnums.cs"
  "Assets/Scripts/Game/Items/ItemHelper.cs"
  "Assets/Scripts/API/ItemsFile.cs"
  "Assets/Scripts/API/MapsFile.cs"
  "Assets/Scripts/Game/Formulas/FormulaHelper.cs"
  "Assets/Scripts/Game/Entities/RaceTemplate.cs"
  "Assets/Resources/ItemTemplates.txt"
  "Assets/Resources/MagicItemTemplates.txt"
  "Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv"
  "Assets/StreamingAssets/Tables/QuestList-Classic.txt"
)
for file in "${donor_files[@]}"; do
  [[ -f "$donor/$file" ]] || missing "donor file '$donor/$file'"
done
[[ -d "$donor/Assets/StreamingAssets/Quests" ]] || missing "donor quest text '$donor/Assets/StreamingAssets/Quests'"
[[ -d "$sound" ]] || missing "music folder '$sound' (link local/Sound to the donor song folder or pass --sound)"
command -v ffmpeg >/dev/null || missing "ffmpeg on PATH (the cinematics are converted with it)"
command -v ffprobe >/dev/null || missing "ffprobe on PATH (the cinematics are checked with it)"
if [[ -n "$sprite_authoring" && ! -d "$sprite_authoring" ]]; then
  missing "sprite authoring root '$sprite_authoring'"
fi

inventory=data/content-source-manifest.csv
ui_assets=data/ui-authored-assets.json
ui_original=data/ui-original
authored=content/worldrpg/payloads/daggerfall.base.json
# The imported payload holds every base section the commands derive; the daggerfall.imported pack
# carries it beside the authored daggerfall.base pack.
imported=content/worldrpg/payloads/daggerfall.imported.json
# The complete block document is an importer record (geometry reads its use sites); the daggerfall.blocks
# pack carries building fields, map positions, and compact automap footprints the runtime reads.
blocks=import-records/daggerfall.blocks.json
buildings=content/worldrpg/payloads/daggerfall.blocks.json
# Importer records nothing at runtime reads (the mesh inventory and the original quest-source
# selections the corpus payloads are built from); they stay outside the runtime content root.
records=import-records/daggerfall.import-records.json
music_manifest=content/worldrpg/media/music/manifest.json
for file in "$inventory" "$ui_assets" "$authored"; do
  [[ -f "$file" ]] || missing "tracked file '$file'"
done

# The cinematics' bytes depend on the FFmpeg build (each artifact records it), so name it up front.
echo "regenerate-content: $(ffmpeg -version | head -n 1)"
started=$SECONDS
# Files written after this stamp are this run's output; the check at the end looks only at those, so an
# uncommitted authored file does not count as generated.
run_stamp=$(mktemp)
trap 'rm -f "$run_stamp"' EXIT
dotnet build src/Daggerfall.Import.Tool/Daggerfall.Import.Tool.csproj --configuration Release --nologo -v quiet
tool() {
  echo "+ daggerfall-import-tool $*"
  dotnet src/Daggerfall.Import.Tool/bin/Release/net10.0/Daggerfall.Import.Tool.dll "$@"
}

# Every generated file is removed first, so a file no command writes any more disappears rather than
# surviving from an earlier run, and the imported payload is rebuilt from nothing.
while IFS= read -r entry; do
  [[ -z "$entry" || "$entry" == \#* ]] && continue
  # shellcheck disable=SC2086 # the entry is a glob
  rm -rf -- ${entry#/}
done < scripts/generated-content-paths.txt

# 1. Product-wide media. The site closures name the music cues this publishes.
tool music-media --out content --sound "$sound" --require-all --update
tool classic-media --arena2 "$arena2" --out content --group worldrpg \
  --ui-authored-assets "$ui_assets" --ui-original "$ui_original" --update

# 2. The imported payload's sections, in dependency order, with the block document and the import
#    records. Commands that join authored sections (vocabulary, actors, items) read the authored payload.
tool catalogs --arena2 "$arena2" --inventory "$inventory" --authored "$authored" --pack "$imported" \
  --donor-races "$donor/Assets/Scripts/Game/Entities/RaceTemplate.cs" --update
tool item-template-ledger --donor "$donor/Assets/Scripts/Game/Items" --inventory "$inventory" --authored "$authored" --pack "$imported" --update
tool character-presentation --arena2 "$arena2" --inventory "$inventory" --pack "$imported" --out content --group worldrpg --update
tool locations --arena2 "$arena2" --pack "$imported" --update
tool magic-catalog --arena2 "$arena2" --pack "$imported" \
  --donor-formulas "$donor/Assets/Scripts/Game/Formulas/FormulaHelper.cs" --update
tool mobile-catalog --donor "$donor/Assets/Scripts/Utility/EnemyBasics.cs" --authored "$authored" --pack "$imported" --archive "$arena2/MONSTER.BSA" --update
tool text --arena2 "$arena2" --pack "$imported" --inventory "$inventory" --language en --update
tool internal-strings \
  --source "$donor/Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv" \
  --label "daggerfall-unity/Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv" \
  --pack "$imported" --language en --update
tool blocks --arena2 "$arena2" --document "$blocks" --buildings "$buildings" --inventory "$inventory" --update
tool geometry --arena2 "$arena2" --blocks "$blocks" --records "$records" --inventory "$inventory" --update
tool climate --arena2 "$arena2" --pack "$imported" --inventory "$inventory" --update
tool factions --arena2 "$arena2" --pack "$imported" --inventory "$inventory" --update
tool terrain --arena2 "$arena2" --pack "$imported" --inventory "$inventory" --update
tool items --arena2 "$arena2" --pack "$imported" --inventory "$inventory" \
  --item-templates "$donor/Assets/Resources/ItemTemplates.txt" \
  --magic-templates "$donor/Assets/Resources/MagicItemTemplates.txt" --update
tool quests --arena2 "$arena2" --quest-text "$donor/Assets/StreamingAssets/Quests" \
  --tables "$donor/Assets/StreamingAssets/Tables" --pack "$imported" --records "$records" --inventory "$inventory" --update
tool videos --arena2 "$arena2" --pack "$imported" --inventory "$inventory" --update
tool cinematic-media --arena2 "$arena2" --pack "$imported" --out content --kind vid --update
tool cinematic-media --arena2 "$arena2" --pack "$imported" --out content --kind flc --update
tool building-name-inputs --maps-file "$donor/Assets/Scripts/API/MapsFile.cs" \
  --label daggerfall-unity/Assets/Scripts/API/MapsFile.cs --pack "$imported" --update

# 3. Quest corpus payloads, read from the quest sections and the original-source selections above.
tool fighters-quest-corpus --pack "$imported" --records "$records" --out content/worldrpg/payloads/daggerfall.quests.fighters.json
tool classic-quest-corpora --pack "$imported" --records "$records" --out content/worldrpg/payloads

# 4. Site closures. Each names the published music cues and publishes media for every actor the imported
#    mobile catalog lets the runtime spawn; its source manifest (the corpus scanned against the inventory)
#    goes to the import records.
site_overlay() {
  local overlay="sprites/$1.json"
  if [[ -n "$sprite_authoring" && -f "$sprite_authoring/$overlay" ]]; then
    printf '%s\n' --sprite-authoring "$sprite_authoring" --sprite-overlay "$overlay"
  fi
}
site_common=(--arena2 "$arena2" --ui-authored-assets "$ui_assets" --ui-original "$ui_original" --inventory "$inventory"
  --pack "$imported" --music-manifest "$music_manifest")
site_records=import-records/sites
mapfile -t overlay_args < <(site_overlay privateers-hold)
tool write "${site_common[@]}" --out content/worldrpg/imports/privateers-hold \
  --source-manifest "$site_records/privateers-hold.sources.json" --region 17 --location "Privateer's Hold" --texture-table classic "${overlay_args[@]}"
mapfile -t overlay_args < <(site_overlay castle-necromoghan)
tool write "${site_common[@]}" --out content/worldrpg/imports/castle-necromoghan \
  --source-manifest "$site_records/castle-necromoghan.sources.json" --region 17 --location "Castle Necromoghan" --texture-table default "${overlay_args[@]}"
tool rmb-spatial "${site_common[@]}" --out content/worldrpg/imports/charing/exterior \
  --source-manifest "$site_records/charing-exterior.sources.json" --region 17 --location Charing --profile exterior
tool rmb-spatial "${site_common[@]}" --out content/worldrpg/imports/charing/interior-1-1-0 \
  --source-manifest "$site_records/charing-interior-1-1-0.sources.json" --region 17 --location Charing --profile interior --block-x 1 --block-y 1 --building 0

# 5. Reconcile all current producer citations and raw-record ledgers. This is import coverage,
#    not runtime parity certification; unknown identities and dangling required references fail.
tool source-coverage --arena2 "$arena2" --inventory "$inventory" --repository "$PWD" \
  --output import-records/source-coverage.json

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

echo "regenerate-content: finished in $((SECONDS - started))s"
