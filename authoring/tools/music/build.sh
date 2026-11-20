#!/usr/bin/env bash
# Builds local/data/music.rpak: Patch's arrangement of the score, one TRACK.ogg per line of sources.tsv at the
# container's root, packed from the store's assets/music/ (encode.sh writes them). The playlists in
# src/WorldRpg.Rulesets.Daggerfall/Scene/DaggerfallMusicDirector.cs name the tracks; the game warns of any they name
# that the container lacks. The container ships with the game, so after a rebuild `scripts/data-sync.sh ship music.rpak` puts it
# in the store's ship/ root for every checkout. Needs the rusty CLI.
#
#   authoring/tools/music/build.sh
#   MUSIC_ASSETS=DIR authoring/tools/music/build.sh
set -euo pipefail
root=$(cd "$(dirname "$0")/../../.." && pwd)
cd "$root"
assets=${MUSIC_ASSETS:-/mnt/den-nas-backup/data/rusty-dagger/assets/music}
stage=local/build/music
[[ -L local/data || -L local ]] &&
  { echo "local/data is a link (scripts/data-sync.sh link): build music.rpak in a checkout with a local/data of its own" >&2; exit 1; }
missing=()
while IFS=$'\t' read -r track _; do
  [[ -z $track || $track == \#* || -f $assets/$track.ogg ]] || missing+=("$track")
done <authoring/tools/music/sources.tsv
((${#missing[@]} == 0)) || { echo "$assets has no recording of [${missing[*]}]: run authoring/tools/music/encode.sh" >&2; exit 1; }
rm -rf "$stage"
mkdir -p "$stage" local/data
cut -f1 authoring/tools/music/sources.tsv | grep -v '^#' | while read -r track; do cp "$assets/$track.ogg" "$stage/"; done
rusty pack-content "$stage" --output local/data/music.rpak
echo "BUILT local/data/music.rpak ($(ls "$stage" | wc -l) tracks)"
