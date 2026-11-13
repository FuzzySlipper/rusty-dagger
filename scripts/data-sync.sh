#!/usr/bin/env bash
# Copies the data the game and the tests read between this checkout and the den NAS store, which keeps one set per
# importer revision: revisions/REVISION/ holds daggerfall-data.rpak, music.rpak, import.tar.zst (the import tree,
# local/build/import: 82,000 files, which the NAS writes at about 20 a second when loose), SHA256SUMS (every file,
# the tree's by its path inside it) and published (when, from which commit and worker, with which ffmpeg). first-person.rpak is not an import output and
# is kept once, at the store's root. The import lane (scripts/route.py data) is the one publisher; every checkout pulls
# the revision its commit needs. The data is derived from arena2 and never leaves our network.
#
#   scripts/data-sync.sh pull                  # this checkout's revision into local/data, with first-person.rpak
#   scripts/data-sync.sh pull --import-tree    # also local/build/import, for a box that runs the tests
#   scripts/data-sync.sh link --import-tree    # instead of copying: link local/data and local/build/import to this
#                                              # machine's one read-only copy of the revision (den-agents' checkouts)
#   scripts/data-sync.sh status                # the container's revision beside this tree's, and whether the store has it
#   scripts/data-sync.sh store                 # the revisions the store holds, with their size and publish time
#   scripts/data-sync.sh publish               # the import lane only: this checkout's whole import into the store
#   ... --mirror                               # also delete from local/data what the store lacks (the import tree
#                                              # is always made to match the revision)
#   DAGGER_DATA_STORE=HOST:/path scripts/data-sync.sh pull   # a store over ssh (changed 8 KiB blocks only)
#
# publish refuses a container that departs from one whole import (an import --only or pack repack), and refuses a
# revision the store already holds unless every file is identical, naming those that differ: one revision has one set
# of bytes. It writes revisions/.incoming-* and renames it into place, so a half-written revision is never read, and
# links a container unchanged since the newest revision instead of storing it again. pull refuses to write through a
# link to another checkout's data (a worktree's, per the README), since it would replace what that checkout opens.
# Nothing here deletes from the store:
# old revisions are pruned by hand, with Patch's say (README, Game data).
# The receiver writes a temporary file and renames it, so a game that has a container open keeps reading the old one.
# Windows checkouts pull with scripts/data-sync.ps1.
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
data=$root/local/data
store=${DAGGER_DATA_STORE:-/mnt/den-nas-backup/data/rusty-dagger/lan}
store=${store%/}
usage="usage: scripts/data-sync.sh pull|link [--import-tree] [--mirror] | status | store | publish"

direction=${1:-}
shift || true
import_tree=''
options=(-rt --info=stats1)
for argument in "$@"; do
  case $argument in
    --mirror) options+=(--delete) ;;
    --import-tree) import_tree=1 ;;
    *) echo "$usage" >&2; exit 2 ;;
  esac
done
# A store over ssh: commands run there, and only changed blocks travel.
store_host='' store_path=$store
if [[ $store == *:* ]]; then
  store_host=${store%%:*} store_path=${store#*:}
  options+=(--no-whole-file --block-size=8192)
fi
at_store() { if [[ -n $store_host ]]; then ssh -o BatchMode=yes "$store_host" "$*"; else bash -c "$*"; fi; }

# What the container says of itself (worldrpg/data-container.json), read through its index, so an entry stored
# compressed (import --compress) reads the same: the revision on the first line, then "whole", "departs" or
# "unrecorded" (a description from before departures were recorded), then one departure per line.
describe_container() {
  python3 - "$data/daggerfall-data.rpak" <<'EOF'
import json, os, struct, subprocess, sys
if not os.path.isfile(sys.argv[1]):
    sys.exit(print('none (no local/data/daggerfall-data.rpak)'))
with open(sys.argv[1], 'rb') as container:
    offset, length = struct.unpack('<QQ', container.read(24)[8:24])
    container.seek(offset)
    entry = next((e for e in json.loads(container.read(length))['entries'] if e['path'] == 'worldrpg/data-container.json'), None)
    if entry is None:
        sys.exit(print('none (the container carries no description)'))
    container.seek(entry['offset'])
    data = container.read(entry.get('zstdLength', entry['byteLength']))
if 'zstdLength' in entry:
    data = subprocess.run(['zstd', '-dcq'], input=data, capture_output=True, check=True).stdout
description = json.loads(data)
departures = description.get('departures')
print(description.get('importerRevision') or 'none (the description names no revision)')
print('unrecorded' if departures is None else 'departs' if departures else 'whole')
for departure in departures or []:
    print(departure)
EOF
}
# The same target the build stamps the revision with; no restore or compile, about a second.
checkout_revision() {
  command -v dotnet >/dev/null || { echo "unknown (dotnet is not on PATH)"; return; }
  local revision
  revision=$(dotnet msbuild "$root/src/Daggerfall.Import/Daggerfall.Import.csproj" -nologo -t:StampImporterRevision \
    -getProperty:_ImporterRevision 2>&1) || true
  [[ $revision =~ ^[0-9a-f]{64}$ ]] && echo "$revision" || echo "unknown (msbuild could not compute it)"
}
revision=$(checkout_revision)
held() { [[ $revision =~ ^[0-9a-f]{64}$ ]] && at_store "test -f '$store_path/revisions/$revision/SHA256SUMS'" 2>/dev/null; }
missing() {
  echo "The store ($store) has no data for importer revision $revision. Import it with" \
    "scripts/route.py data $(git -C "$root" rev-parse HEAD) on our network, or locally with the README's import command."
}
report() {
  local container
  description=$(describe_container)
  container=$(sed -n 1p <<<"$description")
  echo "importer revision of the container: $container"
  echo "importer revision of this checkout: $revision"
  if [[ ! $container =~ ^[0-9a-f]{64}$ || ! $revision =~ ^[0-9a-f]{64}$ ]]; then
    echo "Cannot compare them."
  elif [[ $container != "$revision" ]]; then
    if held; then echo "They differ: the game will call this data stale. scripts/data-sync.sh pull brings this revision."
    else echo "They differ: the game will call this data stale. $(missing)"; fi
  fi
  case $(sed -n 2p <<<"$description") in
    departs) echo "The container is not one whole import, so the game calls it stale:"; sed -n '3,$s/^/  /p' <<<"$description" ;;
    unrecorded) echo "The container records no departures (an import from before they were recorded), so this build calls it malformed." ;;
  esac
}

# sha256sum lines for this checkout's import output: data/NAME for a container, import/PATH for the tree's files.
sums() {
  (cd "$data" && sha256sum daggerfall-data.rpak music.rpak | sed 's#  #  data/#')
  (cd "$root/local/build/import" && find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum | sed 's#  \./#  import/#')
}

case $direction in
  pull)
    at_store "test -d '$store_path'" || { echo "data-sync: $store is not reachable" >&2; exit 1; }
    if [[ $(git -C "$root" config --local --get den-build.managed || true) != true ]]; then
      for folder in local local/data local/build local/build/import; do
        [[ -L $root/$folder ]] || continue
        echo "data-sync: $folder is a link to $(readlink "$root/$folder"); a pull here would replace what every checkout" \
          "sharing it opens. Link this checkout to its revision instead (scripts/data-sync.sh link), or give it its own" \
          "copy (rm $folder) and pull." >&2
        exit 1
      done
    fi
    held || { missing >&2; exit 1; }
    from=$store/revisions/$revision
    mkdir -p "$data"
    rsync "${options[@]}" "$from/daggerfall-data.rpak" "$from/music.rpak" "$data/"
    if at_store "test -f '$store_path/first-person.rpak'"; then rsync "${options[@]}" "$store/first-person.rpak" "$data/"; fi
    if [[ -n $import_tree ]]; then
      # The archive stays beside the tree, so a second pull of the same revision copies nothing.
      archive=$root/local/build/import.tar.zst unpacked=$root/local/build/import.unpacked
      rm -rf "$unpacked" && mkdir -p "$unpacked" "$root/local/build/import"
      rsync "${options[@]}" "$from/import.tar.zst" "$archive"
      zstd -dcq "$archive" | tar -xf - -C "$unpacked"
      # The tree is one revision's whole output: a file the revision lacks is stale, so it goes.
      rsync -rt --delete "$unpacked/" "$root/local/build/import/"
      rm -rf "$unpacked"
    fi
    report
    ;;
  link)
    # One copy of each revision on this machine's own disk, read-only, which every checkout and worktree here links
    # to: nothing copies the data per checkout, and no checkout can write what another reads.
    cache=${DAGGER_DATA_CACHE:-$HOME/.cache/rusty-dagger/data}
    entry=$cache/$revision
    [[ ! -L $root/local ]] || { echo "data-sync: local is a link to $(readlink "$root/local"); give this checkout its own local/ first" >&2; exit 1; }
    if [[ ! -f $entry/SHA256SUMS ]]; then
      at_store "test -d '$store_path'" || { echo "data-sync: $store is not reachable" >&2; exit 1; }
      held || { missing >&2; exit 1; }
      from=$store/revisions/$revision incoming=$cache/.incoming-$revision-$$
      mkdir -p "$incoming/data" "$incoming/import"
      trap 'chmod -R u+w "$incoming" 2>/dev/null; rm -rf "$incoming"' EXIT
      rsync "${options[@]}" "$from/daggerfall-data.rpak" "$from/music.rpak" "$incoming/data/"
      rsync "${options[@]}" "$from/import.tar.zst" "$from/SHA256SUMS" "$incoming/"
      zstd -dcq "$incoming/import.tar.zst" | tar -xf - -C "$incoming/import"
      rm "$incoming/import.tar.zst"
      (cd "$incoming" && sha256sum --check --quiet SHA256SUMS) ||
        { echo "data-sync: the copy of importer revision $revision does not match its sums; nothing was linked" >&2; exit 1; }
      ln -s ../../first-person.rpak "$incoming/data/first-person.rpak"
      chmod -R a-w "$incoming/data" "$incoming/import"
      # When another link got there first, its copy is the same revision, checked the same way.
      mv -T "$incoming" "$entry" 2>/dev/null || true
    fi
    # first-person.rpak is not an import output: one copy here serves every revision, refreshed from the store's root.
    if at_store "test -f '$store_path/first-person.rpak'" 2>/dev/null; then rsync "${options[@]}" "$store/first-person.rpak" "$cache/"; fi
    folders=(local/data)
    [[ -n $import_tree ]] && folders+=(local/build/import)
    for folder in "${folders[@]}"; do
      path=$root/$folder
      if [[ -L $path ]]; then rm "$path"
      elif [[ -d $path ]]; then
        rmdir "$path" 2>/dev/null || { echo "data-sync: $folder holds data of its own, perhaps the only copy of an import; move it aside, then link" >&2; exit 1; }
      fi
      mkdir -p "$(dirname "$path")"
      ln -s "$entry/${folder##*/}" "$path"
    done
    report
    ;;
  status)
    report
    if held; then echo "The store holds importer revision $revision."
    elif at_store "test -d '$store_path'" 2>/dev/null; then missing; fi
    ;;
  store)
    at_store "cd '$store_path/revisions' && for r in */; do r=\${r%/}; printf '%s  %s  %s\n' \"\$r\" \"\$(du -sh --apparent-size \$r | cut -f1)\" \"\$(sed -n 's/^published: //p' \$r/published)\"; done"
    ;;
  publish)
    [[ $(git -C "$root" config --local --get den-build.managed || true) == true ]] ||
      { echo "data-sync: only the import lane publishes (scripts/route.py data); this is not a den-build checkout" >&2; exit 2; }
    report
    [[ $(sed -n 1p <<<"$description") == "$revision" && $(sed -n 2p <<<"$description") == whole ]] ||
      { echo "data-sync: refusing to publish: the container is not one whole import at importer revision $revision" >&2; exit 1; }
    # Cinematics (import --cinematics) encode differently on another machine, so they stay in the importing checkout.
    if [[ -n $(find "$root/local/build/import/staging/worldrpg/media/cinematics" -type f -print -quit 2>/dev/null) ]]; then
      echo "data-sync: refusing to publish: this import converted the cinematics, whose bytes differ by machine; import without --cinematics" >&2
      exit 1
    fi
    mine=$(mktemp) && chmod 644 "$mine"
    archive=$(mktemp -p "$root/local/build" publish-XXXXXX.tar.zst)
    trap 'rm -f "$mine" "$archive"' EXIT
    sums >"$mine"
    target=$store_path/revisions/$revision
    # The store's set for this revision may only ever be these bytes.
    compare() {
      local differ
      differ=$(diff <(at_store "cat '$target/SHA256SUMS'") "$mine" | sed -n 's/^[<>] [0-9a-f]*  //p' | sort -u) || true
      if [[ -z $differ ]]; then echo "data-sync: the store already holds importer revision $revision, byte for byte the same"; return 0; fi
      echo "data-sync: refusing to publish: the store's importer revision $revision differs in:" >&2
      sed 's/^/  /' <<<"$differ" | head -n 40 >&2
      return 1
    }
    if held; then compare; exit; fi
    incoming=$store_path/revisions/.incoming-$revision-$(hostname)-$$
    newest=$(at_store "ls -t '$store_path'/revisions/*/published 2>/dev/null | head -n 1 | xargs -r dirname")
    at_store "mkdir -p '$incoming'"
    to=${store_host:+$store_host:}$incoming
    tar -C "$root/local/build/import" --sort=name -cf - . | zstd -q -T0 -f -o "$archive"
    # An unchanged container becomes a link to the newest revision's (compared by content: every import stamps new times).
    link=()
    [[ -n $newest ]] && link=(--checksum "--link-dest=$newest")
    rsync "${options[@]}" "${link[@]}" "$data/daggerfall-data.rpak" "$data/music.rpak" "$to/"
    rsync "${options[@]}" "$archive" "$to/import.tar.zst"
    rsync -t "$mine" "$to/SHA256SUMS"
    printf 'published: %s\ncommit: %s\nworker: %s\nencoder: %s\n' "$(date -Is)" "$(git -C "$root" rev-parse HEAD)" \
      "$(hostname)" "$(ffmpeg -version 2>/dev/null | head -n 1)" | at_store "cat > '$incoming/published'"
    # A rename into place is all or nothing; when another run got there first, its set must be these bytes.
    if at_store "mv -T '$incoming' '$target'" 2>/dev/null; then
      echo "data-sync: published importer revision $revision to $store/revisions/$revision"
    else
      at_store "rm -rf '$incoming'"
      compare
    fi
    ;;
  *) echo "$usage" >&2; exit 2 ;;
esac
