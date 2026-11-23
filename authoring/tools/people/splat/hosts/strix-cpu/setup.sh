#!/bin/bash
# setup.sh HOST...: build (or refresh) the asset CPU environment on a Strix Halo (den-nimo, den-m5) from den-agents.
#
# The Halos are disposable and rebuilt from ~/server-access/strix-halo/; nothing installed by hand survives a reinstall, so this
# script IS the environment: rerun it after a reinstall and the venv is back. It touches nothing of ComfyUI (no service, no
# install, no model), so it is safe while a ComfyUI job runs; it builds a uv venv in the host's rebuildable state folder
#   ~/.local/state/asset-cpu/venv      numpy / scipy / pillow / imageio-ffmpeg, the versions den-agents runs (python 3.14)
#   ~/.local/state/asset-cpu/work/     per-run working copies (see run.sh); results are synced back to den-agents
#   ~/.local/state/asset-cpu/blender-5.2.2/   the portable Blender den-agents runs (/opt/blender-5.2.2-linux-x64, the same build, so guide
#                                       renders match den-agents'); run.sh puts it on PATH as `blender` (2026-11-14: lengthy CPU work, here
#                                       Cycles guide passes, goes to a Halo rather than den-agents - Patch's preference)
# and copies this repository's CPU tools (authoring/tools/people/splat/solv3r, authoring/tools/people/splat/mesh-splat, authoring/tools/people/splat/gsplat, the run scripts given to run.sh travel with
# the run folder) into ~/.local/state/asset-cpu/rusty-dagger. Rerunning is cheap: uv reuses its cache.
set -euo pipefail
REPO=$(cd "$(dirname "$0")/../../../../../.." && pwd)
for H in "$@"; do
  echo "== $H"
  ssh -o BatchMode=yes "$H" 'set -e; S=~/.local/state/asset-cpu; mkdir -p $S/work $S/repo
    command -v uv >/dev/null || { echo "uv missing: host-user.sh should provide it"; exit 1; }
    [ -x $S/venv/bin/python ] || uv venv -q --python 3.14 $S/venv
    VIRTUAL_ENV=$S/venv uv pip install -q numpy==2.3.5 scipy==1.16.3 pillow==12.1.1
    $S/venv/bin/python -c "import numpy, scipy, PIL; print(\"venv ok\", numpy.__version__, scipy.__version__, PIL.__version__)"'
  rsync -a --delete --exclude __pycache__ "$REPO/authoring/tools/people/splat/solv3r" "$REPO/authoring/tools/people/splat/mesh-splat" "$REPO/authoring/tools/people/splat/gsplat" "$H:.local/state/asset-cpu/rusty-dagger/"
  echo "repo tools synced to $H:~/.local/state/asset-cpu/rusty-dagger"
  BL=/opt/blender-5.2.2-linux-x64
  if [ -x $BL/blender ]; then
    rsync -a --delete $BL/ "$H:.local/state/asset-cpu/blender-5.2.2/"
    ssh -o BatchMode=yes "$H" '~/.local/state/asset-cpu/blender-5.2.2/blender --version | head -1'
  else echo "no $BL on this machine - Blender not synced to $H"; fi
done
