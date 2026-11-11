#!/usr/bin/env bash
# Moves a checkout's ignored working files from the old layout (loose under local/, and authoring/'s df-raw, gen-raw
# and review) into local/'s lifecycle folders (README, Game data). Run it once in each main checkout that predates the
# layout, only while no session imports, promotes art or runs the game from it or from a worktree that links it.
#
#   scripts/split-local.sh [--dry-run] [--evidence DIR]
#
# Every move is a rename within the checkout's file system. It refuses, before moving anything, while a process has a
# file under those folders open or mapped, or runs the import, an art tool, verify.sh or the product from this checkout
# or one of its worktrees; it never replaces a file, and a second run skips what has moved. Anything it does not
# recognise is listed and left where it is. In a worktree it moves nothing: follow the README's worktree recipe.
#
# --evidence DIR also moves local/evidence's entries into DIR (on den-agents, the NAS's evidence/rusty-dagger), checks
# the copy byte for byte, then leaves local/evidence a link to DIR. An entry DIR already has stops it.
set -euo pipefail
shopt -s extglob

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)
cd "$root"
dry=false
evidence=
while [[ $# -gt 0 ]]; do
  case $1 in
    --dry-run) dry=true ;;
    --evidence) evidence=${2:?--evidence names a directory}; shift ;;
    *) echo "usage: scripts/split-local.sh [--dry-run] [--evidence DIR]" >&2; exit 2 ;;
  esac
  shift
done

if git rev-parse --git-dir >/dev/null 2>&1 && [[ $(git rev-parse --git-dir) != $(git rev-parse --git-common-dir) ]]; then
  echo "This is a worktree: move the main checkout, then give this one the README's worktree links." >&2
  exit 1
fi
if [[ -L local ]]; then
  echo "local is a link to $(readlink local): move that checkout, then give this one the README's worktree links." >&2
  exit 1
fi

# Old path, new path. A directory moving onto one that exists is merged entry by entry.
moves=(
  local/arena2 local/inputs/arena2
  local/Sound local/inputs/Sound
  local/fp-melee local/inputs/fp-melee
  local/daggerfall-data.rpak local/data/daggerfall-data.rpak
  local/music.rpak local/data/music.rpak
  local/first-person.rpak local/data/first-person.rpak
  local/import local/build/import
  local/fp-arms local/build/fp-arms
  local/verify local/build/verify
  authoring/df-raw local/art/reference
  authoring/gen-raw local/art/generated
  authoring/review local/art/review
  local/exports/daggerfall-mobile-idle-unmirrored-r01 local/art/reference/daggerfall-mobile-idle-unmirrored-r01
  local/exports/daggerfall-remaining-directional-unmirrored-r01 local/art/reference/daggerfall-remaining-directional-unmirrored-r01
  local/exports/old local/scratch/exports-old
  local/image-gen-tests local/scratch/image-gen-tests
  local/fp-capture local/scratch/fp-capture
  local/ui-art-kit local/scratch/ui-art-kit
  local/rpak-2026-11-08 local/scratch/rpak-2026-11-08
)
layout=(inputs data build art scratch evidence)

# In use: an open or mapped file, or a working directory, under what moves; or one of the repository's own commands
# running in this checkout or a worktree of it.
checkouts=("$root")
if git rev-parse --git-dir >/dev/null 2>&1; then
  while read -r key path; do [[ $key == worktree && $path != "$root" ]] && checkouts+=("$path"); done < <(git worktree list --porcelain)
fi
busy=()
for proc in /proc/[0-9]*; do
  pid=${proc#/proc/}
  [[ $pid == "$$" || $pid == "$BASHPID" || $pid == "$PPID" ]] && continue
  held=$( { find "$proc/fd" -maxdepth 1 -lname "$root/local/*" 2>/dev/null
           find "$proc/fd" -maxdepth 1 -regextype egrep -lname "$root/authoring/(df-raw|gen-raw|review)/.*" 2>/dev/null
           grep -lE " $root/(local|authoring/(df-raw|gen-raw|review))/" "$proc/maps" 2>/dev/null
         } | head -n 1) || true
  cwd=$(readlink "$proc/cwd" 2>/dev/null) || continue
  cmd=$(tr '\0' ' ' < "$proc/cmdline" 2>/dev/null) || continue
  [[ $cwd == "$root/local" || $cwd == "$root/local/"* || $cwd == "$root/authoring/"@(df-raw|gen-raw|review)?(/*) ]] && held=$cwd
  if [[ -z $held ]] && grep -qE 'Daggerfall\.Import\.Tool|daggerfall-import-tool|WorldRpg\.Host|authoring/tools/|tools/[a-z]+/[a-z_]+\.(py|sh)|scripts/fp-arms/|scripts/verify\.sh' <<<"$cmd"; then
    for checkout in "${checkouts[@]}"; do [[ $cwd == "$checkout" || $cwd == "$checkout/"* || $cmd == *"$checkout/"* ]] && held="runs in $checkout"; done
  fi
  [[ -n $held ]] && busy+=("$pid ${cmd:0:120} ($held)")
done
if [[ ${#busy[@]} -gt 0 ]]; then
  echo "Something is using these files; nothing was moved:" >&2
  printf '  %s\n' "${busy[@]}" >&2
  exit 1
fi

# Moves one path; merges a directory into an existing one, refusing to replace anything.
move() {
  local from=$1 to=$2
  [[ -e $from || -L $from ]] || return 0
  if [[ ! -e $to && ! -L $to ]]; then
    if $dry; then echo "would move $from -> $to"; return 0; fi
    mkdir -p "$(dirname "$to")"
    mv -n "$from" "$to"
    echo "moved $from -> $to"
  elif [[ -d $from && ! -L $from && -d $to ]]; then
    local entry
    for entry in "$from"/* "$from"/.[!.]*; do
      [[ -e $entry || -L $entry ]] || continue
      move "$entry" "$to/${entry##*/}"
    done
    $dry || rmdir "$from"
  else
    echo "both $from and $to exist; move or remove one by hand and run again" >&2
    exit 1
  fi
}

# Check every move first, so a conflict stops the run before anything has moved.
for ((i = 0; i < ${#moves[@]}; i += 2)); do
  from=${moves[i]} to=${moves[i + 1]}
  if [[ (-e $from || -L $from) && (-e $to || -L $to) ]] && ! [[ -d $from && ! -L $from && -d $to ]]; then
    echo "both $from and $to exist; move or remove one by hand and run again" >&2
    exit 1
  fi
done
for ((i = 0; i < ${#moves[@]}; i += 2)); do move "${moves[i]}" "${moves[i + 1]}"; done
if ! $dry && [[ -d local/exports && -z $(ls -A local/exports) ]]; then rmdir local/exports; fi

if [[ -n $evidence ]]; then
  if [[ -L local/evidence ]]; then
    echo "local/evidence already links to $(readlink local/evidence)"
  elif [[ -d local/evidence ]]; then
    [[ -d $evidence ]] || { echo "$evidence is not a directory" >&2; exit 1; }
    for entry in local/evidence/* local/evidence/.[!.]*; do
      [[ -e $entry ]] || continue
      [[ -e $evidence/${entry##*/} ]] && { echo "$evidence already has ${entry##*/}; nothing of local/evidence was moved" >&2; exit 1; }
    done
    if $dry; then
      echo "would: copy local/evidence ($(du -sh local/evidence | cut -f1)) into $evidence, check it, and link local/evidence to it"
    else
      rsync -a local/evidence/ "$evidence/"
      differ=$(rsync -a --checksum --dry-run --itemize-changes local/evidence/ "$evidence/")
      [[ -z $differ ]] || { echo "the copy in $evidence differs; local/evidence is left as it is:" >&2; echo "$differ" | head -n 20 >&2; exit 1; }
      rm -rf local/evidence
      ln -s "$evidence" local/evidence
      echo "moved local/evidence -> $evidence (local/evidence links to it)"
    fi
  fi
fi

unknown=()
for entry in local/* local/.[!.]*; do
  [[ -e $entry || -L $entry ]] || continue
  [[ " ${layout[*]} " == *" ${entry#local/} "* || " ${moves[*]} local/exports " == *" $entry "* ]] || unknown+=("$entry")
done
[[ ${#unknown[@]} -eq 0 ]] || { echo "left in place, not part of the layout (move into local/scratch/ or ask):"; printf '  %s\n' "${unknown[@]}"; }
$dry && echo "dry run: nothing was moved"
echo "done"
