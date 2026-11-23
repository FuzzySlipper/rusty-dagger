#!/bin/bash
# run.sh HOST RUN_DIR [--back PATH]... -- COMMAND: run a CPU step of an asset run on a Strix Halo instead of den-agents.
#
# RUN_DIR (a folder under this repository, e.g. live-evidence/<run>/<sub>) is rsynced to HOST:~/.local/state/asset-cpu/work/<same
# path>, COMMAND runs there under nice -n 10 with the asset-cpu venv first on PATH (python3 = the venv; the repo tools are in
# $ASSET_CPU_REPO), and the whole RUN_DIR is rsynced back (no --delete, so nothing on den-agents is removed). The call waits for
# the command, so wrap it in asset-resources run --host HOST --resource HOST-asset-cpu. Build the env first with setup.sh HOST.
# `blender` on PATH is the portable Blender 5.2.2 setup.sh puts in the state folder.
# The copy there is not cleared between calls (no --delete either way), so files deleted here stay there: a reused scratch folder
# can feed an earlier job's files to the next one (2026-11-14: a review video built from a glob got three clips back to back).
# --fresh (first, before --) deletes the remote copy before syncing; use it for scratch folders, not for a bake folder whose
# earlier stages' outputs live on both sides.
set -euo pipefail
H=$1; D=$2; shift 2; FRESH=; [ "$1" = --fresh ] && { FRESH=1; shift; }; [ "$1" = -- ] && shift
REPO=$(cd "$(dirname "$0")/../../../../../.." && pwd); REL=$(realpath --relative-to="$REPO" "$REPO/$D")
[ -n "$FRESH" ] && ssh -o BatchMode=yes "$H" "rm -rf ~/.local/state/asset-cpu/work/$REL"   # nothing left from an earlier use of RUN_DIR
ssh -o BatchMode=yes "$H" "mkdir -p ~/.local/state/asset-cpu/work/$REL"
rsync -a --exclude __pycache__ "$REPO/$REL/" "$H:.local/state/asset-cpu/work/$REL/"
CMD=$(printf '%q ' "$@")
ssh -o BatchMode=yes "$H" "S=~/.local/state/asset-cpu; export PATH=\$S/venv/bin:\$S/blender-5.2.2:\$PATH ASSET_CPU_REPO=\$S/rusty-dagger OMP_NUM_THREADS=\${OMP_NUM_THREADS:-8}; cd \$S/work/$REL && nice -n 10 bash -c $(printf '%q' "$CMD")"
rsync -a --exclude __pycache__ "$H:.local/state/asset-cpu/work/$REL/" "$REPO/$REL/"
