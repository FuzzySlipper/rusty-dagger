#!/usr/bin/env bash
# Copies the containers the game opens (local/data: daggerfall-data.rpak, music.rpak, first-person.rpak) between this
# checkout and the den NAS store's lan/ root, or another Linux checkout over ssh. den-agents is the one machine that
# imports; every other machine pulls. The containers are derived from arena2 and never leave our network.
#
#   scripts/data-sync.sh pull                       # the store into this checkout (a new checkout's first copy)
#   scripts/data-sync.sh push                       # this checkout into the store (den-agents, after an import)
#   scripts/data-sync.sh push den-k8:build/workspaces/rusty-dagger   # into that checkout over ssh
#   scripts/data-sync.sh pull den-agents:dev/rusty-dagger            # from that checkout over ssh
#   scripts/data-sync.sh status                     # only compare the container's importer revision with this tree's
#   ... --mirror                                    # also delete what the destination has and the source lacks
#   DAGGER_DATA_STORE=/path scripts/data-sync.sh pull
#
# Adds and updates by default and deletes nothing. Over ssh only the changed 8 KiB blocks travel (an importer change
# sends tens of megabytes, not 1.8 GB); onto or off the NFS store each changed file is copied whole. The receiver
# writes a temporary file and renames it, so a game that has a container open keeps reading the old one.
# Windows checkouts pull with scripts/data-sync.ps1.
set -euo pipefail
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
data=$root/local/data
store=${DAGGER_DATA_STORE:-/mnt/den-nas-backup/data/rusty-dagger/lan}
usage="usage: scripts/data-sync.sh push|pull [HOST:CHECKOUT] [--mirror] | status"

direction=${1:-}
shift || true
remote=
options=(-rt --info=stats1)
for argument in "$@"; do
  case $argument in
    --mirror) options+=(--delete) ;;
    *:*) remote=${argument%/}/local/data ;;
    *) echo "$usage" >&2; exit 2 ;;
  esac
done
there=$store
if [[ -n $remote ]]; then
  there=$remote
  options+=(--no-whole-file --block-size=8192)
fi

# The import records its revision in the container's first entry, worldrpg/data-container.json.
container_revision() {
  head -c 8192 "$data/daggerfall-data.rpak" 2>/dev/null | grep -ao '"importerRevision": *"[0-9a-f]*"' | grep -o '[0-9a-f]\{64\}' ||
    echo "none (no local/data/daggerfall-data.rpak)"
}
# The same target the build stamps the revision with; no restore or compile, about a second.
checkout_revision() {
  command -v dotnet >/dev/null || { echo "unknown (dotnet is not on PATH)"; return; }
  local revision
  revision=$(dotnet msbuild "$root/src/Daggerfall.Import/Daggerfall.Import.csproj" -nologo -t:StampImporterRevision \
    -getProperty:_ImporterRevision 2>&1) || true
  [[ $revision =~ ^[0-9a-f]{64}$ ]] && echo "$revision" || echo "unknown (msbuild could not compute it)"
}
report() {
  local container checkout
  container=$(container_revision)
  checkout=$(checkout_revision)
  echo "importer revision of the container: $container"
  echo "importer revision of this checkout: $checkout"
  if [[ ! $container =~ ^[0-9a-f]{64}$ || ! $checkout =~ ^[0-9a-f]{64}$ ]]; then
    echo "Cannot compare them."
  elif [[ $container != "$checkout" ]]; then
    echo "They differ: the game will call this data stale. Pull from den-agents once it has imported this revision; never import here."
  fi
}

case $direction in
  push) from=$data; to=$there ;;
  pull) from=$there; to=$data ;;
  status) report; exit 0 ;;
  *) echo "$usage" >&2; exit 2 ;;
esac
if [[ $direction == push ]]; then
  report
  [[ -d $data ]] || { echo "data-sync: $data does not exist" >&2; exit 1; }
  if [[ -n $remote ]]; then ssh "${remote%%:*}" mkdir -p "${remote#*:}"; else mkdir -p "$store"; fi
  rsync "${options[@]}" "$data/" "$to/"
else
  [[ -n $remote || -d $store ]] || { echo "data-sync: $store is not reachable" >&2; exit 1; }
  mkdir -p "$data"
  rsync "${options[@]}" "$from/" "$data/"
  report
fi
