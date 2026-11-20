#!/usr/bin/env bash
# Encodes Patch's arrangement of the score into the store's music assets: for each line of sources.tsv (the track
# name the playlists use, a tab, the approved render's path in his music-dev repository) the render is read from
# that repository's HEAD and written as TRACK.ogg (Ogg Vorbis, quality 6, at the render's own rate) into the store's
# assets/music/, the raw assets build.sh packs into music.rpak. Only the listed renders are read, so the large
# repository is never cloned. Needs ffmpeg with libvorbis.
#
#   authoring/tools/music/encode.sh                 # every track
#   authoring/tools/music/encode.sh dungeon-15 ...  # only these tracks, after Patch re-renders them
#   MUSIC_DEV=HOST:/path/to/music-dev.git MUSIC_ASSETS=DIR authoring/tools/music/encode.sh
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repository=${MUSIC_DEV:-den-nas:/srv/git/music-dev.git}
assets=${MUSIC_ASSETS:-/mnt/den-nas-backup/data/rusty-dagger/assets/music}
host=${repository%%:*} path=${repository#*:}
[[ $repository == *:* ]] || host=''
git_show() {
  local command="git -c safe.directory='$path' --git-dir='$path' cat-file blob 'HEAD:$1'"
  if [[ -n $host ]]; then ssh -o BatchMode=yes "$host" "$command"; else bash -c "$command"; fi
}
mkdir -p "$assets"
encoded=0
while IFS=$'\t' read -r track render <&3; do
  [[ -z $track || $track == \#* ]] && continue
  if (($# > 0)) && [[ ! " $* " == *" $track "* ]]; then continue; fi
  partial=$assets/.$track.ogg.partial
  git_show "$render" | ffmpeg -nostdin -hide_banner -loglevel error -y -i pipe:0 -map_metadata -1 -c:a libvorbis -q:a 6 \
    -f ogg "$partial" || { rm -f "$partial"; echo "encode: '$render' for $track could not be read or encoded" >&2; exit 1; }
  mv "$partial" "$assets/$track.ogg"
  encoded=$((encoded + 1))
  echo "encoded $track.ogg ($(du -h "$assets/$track.ogg" | cut -f1)) from $render"
done 3<"$here/sources.tsv"
echo "ENCODED $encoded tracks into $assets"
