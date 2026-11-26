#!/bin/bash
# --no-same-owner on remote extracts: a Secure Cloud RunPod container may not chown (2026-11-24, npc-468)
# bake.sh RUN ID STAGE [HALO]: one stage of a character's splat bake from its orbit clip (the batch route, docs/mesh-pinned-splat.md
# "Batch route: villager batch 1"; townsman 385's route with round 1 at --anchor-weight 4, the reduced repaint - three rings, VACE
# at 1024² - and the batch-1 fixes: --open-holes mattes, repaint colour match, ignore masks).
#
#   RUN    the batch run folder (e.g. live-evidence/villagers-batch1-20261113-r01), relative to the repository or absolute
#   ID     NNN-X: the character's folder is RUN/NNN/X; RUN/NNN/X/bake.env sets CLIP (the clip's attempt folder, relative to RUN) and
#          SIZE (the clip's frame size); RUN/NNN/X/prompts.json gives the VACE prompts (keys head, high, legs, negative,
#          negative-head, negative-legs; copied into the bake folder by the frames stage). The front cut-out for the VACE
#          references is RUN/NNN/X/cut-t6/front-cut.png, else cut/front-cut.png.
#   HALO   den-m5 (default) | den-nimo: where the CPU stages run (authoring/tools/people/splat/hosts/strix-cpu/run.sh), never den-agents
#   STAGE  tools   [lab]   stage the repository's lab tools to /workspace/BATCH/tools (once per batch; ID is ignored)
#          frames  hard-link the clip's frames into the bake folder
#          (fuse: --up figure since 2026-11-14 - the median camera up leaned ~30-37 deg on sweeps; a standing figure's axis is level within 2)
#          (mattes: LAST, default 71, is the last frame used and STEP, default 5, the degrees per frame - a 141-frame orbit is LAST=139 STEP=2.5714)
#          (mattes: BiRefNet masks (birefnet_masks.py, the Halos' ComfyUI) decide the enclosed and ground-coloured parts (orbit_views.py
#           --birefnet); without them --open-holes 500 --hole-large 4000,2.5 and the clip's depth guide (GUIDE_DEPTH for multi-clip bakes);
#           then a matte review on the same Halo (Patch, 2026-11-15; a glance, not a gate): review/mattes/CLIP/ - mattes.mp4 (cut-out on
#           magenta | what was cut), sheet.png (every 6th frame, full size, removed / kept-hole px, EDGE) and numbers.json, per clip;
#           multi-clip bakes set MATTE_CLIPS=name:first-last,... (matte_review.py); a failed review warns and the bake carries on)
#          (mattes: KEY_HOLES=DIST and DESPILL=PX for a chroma-key clip (guided_clip.sh BACKDROP): every enclosed patch near the backdrop
#           colour is opened whatever its size, and the key's spill is taken off the edge; npc-024, 2026-11-23)
#          (mattes: BIREFNET_EDGE=PX also cuts the matte's outline back to the BiRefNet mask widened by PX px at 1088 (orbit_views.py
#           --birefnet-edge): for depth-controlled clips, whose pale glow the colour key keeps as a grey fringe - villager 381's finger flecks)
#          mattes  [Halo]  orbit_views.py (--threshold 8, the clip's direction, --last 71, --open-holes) and orbit_inputs.py
#          cams    [lab]   Solv3R at 504², 32 calls, nine reserved views
#          fuse    [Halo]  fuse.py --cloud-only, loop_data.py at the clip's size (--heldout 9; overlap test on its fixed 768 grid)
#          r0      [lab]   round 0: free fit 15000 steps, cap 400k, no ring, pose lr 2e-5, opacity-reg 0; score_at; cloud report;
#                          loop_review sheets (views, steep, details) -> r0, review-r0
#          rings   [lab]   head-high / high-close / legs-low rings from round 0, at 1024 (VACE control) and the clip's size
#          refs    [local] VACE references cropped from the front cut-out at each ring's framing (make_refs.py)
#          vace    [lab]   Wan 2.2 VACE at 1024², strength 0.8, prompts.json, seeds 610000+ (one ring after another)
#          match   [Halo]  repaint_match.py per ring: VACE frames colour-matched to their round-0 renders, hue-blob ignore masks,
#                          views dropped only over a quarter ignored -> vace-matched/RING (stops, exit 3, if a ring drops over a third)
#          r1data  [Halo]  loop_dataset.py: anchors + repainted rings (vace-matched/, with their ignore masks; MATCH=0: raw vace/),
#                          --exact-every 9 --fill-holes 300 --dilate 6 --background-from-alpha
#          r1      [lab]   round 1: 15000 steps, cap 400k, no ring, pose lr 2e-5, --anchor-weight 4, opacity-reg 0; cloud report; review
#          soles   [Halo]  cap_splat.py --shade-below Y --shade 0.45 --max-scale 0.005 -> final; Y = 0.1% lowest point + 0.022
#          final   [lab]   agreed cloud (splat_to_cloud --min-views 3 --agree 0.004), review-final (round 0 against final), close-ups
# Lab stages: one flock -w LOCK_WAIT (default 7200 s; fails loudly, exit 75) round staging, the run (asset-resources run) and the fetch;
# a VRAM line before each job in the bake folder's vram.log; nothing started inside the lock outlives it. Every stage appends
# "STAGE start/end EPOCH HOST" to times.txt. BAKE=NAME (default bake) puts the run in RUN/NNN/X/NAME (lab folder
# /workspace/BATCH/ID plus NAME's suffix after "bake"), so a variant bake leaves bake/ as it is. OPEN_HOLES (default 500, 0 = off).
# DRY=1 prints the lab script or Halo command a stage would run and runs nothing on the lab or a Halo. CLIP_OVERRIDE=PATH (relative
# to RUN) bakes another clip of the character than bake.env's; use it with its own BAKE folder. SIZE_OVERRIDE replaces bake.env's SIZE;
# EXTRA_REVIEW adds loop_review.py arguments (e.g. more --detail views) to the r0 review. Before each VACE ring, inside the
# lock, the lab's ComfyUI is restarted when its RSS is above RESTART_RSS_GB (default 20) and its queue is empty (lab-comfy-restart.sh).
set -u
REPO=$(cd "$(dirname "$0")/../../../../.." && pwd); cd "$REPO"
W=$(realpath --relative-to="$REPO" "$1"); B=$(basename "$W"); ID=$2; STAGE=$3; HALO=${4:-den-m5}; HERE=authoring/tools/people/splat/bake
BAKE=${BAKE:-bake}; K=${ID%-*}/${ID#*-}; D=$W/$K/$BAKE; LAB=/workspace/$B/$ID${BAKE#bake}; L=~/server-access/gpu-lab/connect
if [ "$STAGE" != tools ]; then
  [ -f $W/$K/bake.env ] || { echo "bake.sh: no $W/$K/bake.env (CLIP=..., SIZE=...)" >&2; exit 2; }
  . $W/$K/bake.env
  [ -n "${CLIP_OVERRIDE:-}" ] && CLIP=$CLIP_OVERRIDE   # another clip of the same character (e.g. a test clip), relative to RUN
  [ -n "${SIZE_OVERRIDE:-}" ] && SIZE=$SIZE_OVERRIDE   # e.g. 1088 for a 1088^2 test clip baked beside the batch's 2176 ones
fi
PY=/workspace/splat-probe/venv/bin/python; SPY=python; SOLV3R=/workspace/solv3r; SOLV3R_W=/cache/huggingface/solv3r-c95998a.safetensors
# GPU=den-m5|den-nimo runs the lab stages on a Strix Halo's GPU instead of the 5090 (Patch 2026-11-18: the lab crashed under the
# bakes; a Halo is ~5x slower but may crash). Env: authoring/tools/people/splat/hosts/strix-gpu/setup.sh HOST (ROCm torch, patched gsplat 1.5.3, Solv3R); stages
# run in ~/.local/state/asset-gpu/work/BATCH/ID under a per-Halo lock and the Halo's ComfyUI resource; VACE uses that Halo's ComfyUI.
GPU=${GPU:-gpu-lab}; GS=/home/agent/.local/state/asset-gpu; GRES=${GPU#den-}-comfy
# GPU=runpod-*: a rented RunPod pod reached by that ssh alias, its environment in /workspace/asset-gpu (hosts/runpod/setup.sh;
# asset-pipeline docs/runpod-bake-pods.md). Only the gsplat stages belong there; VACE stays on the 5090.
case $GPU in runpod*|colab*) GS=/workspace/asset-gpu; GRES=$GPU ;; esac   # colab-*: a Google Colab runtime over `colab ssh --proxy-mode` (hosts/colab/README.md)
GW=$GS/work/$B/$ID${BAKE#bake}; export GRES
if [ "$GPU" != gpu-lab ]; then PY=$GS/venv/bin/python; SPY=$PY; SOLV3R=$GS/solv3r; SOLV3R_W=$GS/hf/solv3r-c95998a.safetensors; fi
# EXTRA_RING=NAME:LOW,HIGH:FOV:TARGET_Y (bake.env): a fourth close-up repaint ring, rendered, referenced, repainted by VACE
# (prompts.json NAME / negative-NAME), matched and put in the round-1 data with the three. npc-023 (2026-11-22): hands hanging
# at the hips fall between high-close (from above, 0.26) and legs-low (-0.31), and baked to a blur: hands:-10,20:9.5:0.0
RINGS="head-high high-close legs-low${EXTRA_RING:+ ${EXTRA_RING%%:*}}"
mkdir -p $D
stamp() { echo "$STAGE $1 $(date +%s) $2" >> $D/times.txt; }
# Staged-tool guard (2026-11-15: a lab copy of the trainer staged before per-view 'weight' existed ignored it for a day, so every
# weight-0 fit was a joint fit). Before a lab or Halo stage the staged copies are hashed against the repository's files; a mismatch is
# re-staged (said in the bake's times.txt and on stderr) and checked again, and a second mismatch refuses the stage (exit 76);
# STRICT_TOOLS=1 refuses at the first mismatch instead.
LAB_TOOLS="authoring/tools/people/splat/gsplat/train_loop_splat.py authoring/tools/people/splat/solv3r/solve.py authoring/tools/people/splat/solv3r/loop_review.py authoring/tools/people/splat/solv3r/render_rings.py authoring/tools/people/splat/solv3r/splat_to_cloud.py authoring/tools/people/splat/mesh-splat/visible_weight.py $HERE/score_at.py $HERE/compare.py $HERE/residual_by_view.py"
lab_tools_ok() {
  local want got; want=$(for f in $LAB_TOOLS; do echo "$(sha256sum < $f | cut -c1-64) $(basename $f)"; done | LC_ALL=C sort -k2)
  if [ "$GPU" != gpu-lab ]; then
    got=$(ssh -o BatchMode=yes $GPU "cd $GS/work/$B/tools 2>/dev/null && for f in $(for f in $LAB_TOOLS; do basename $f; done | tr '\n' ' '); do echo \$(sha256sum < \$f | cut -c1-64) \$f; done" 2>/dev/null | LC_ALL=C sort -k2)
    [ "$want" = "$got" ]; return
  fi
  got=$($L "gpu-lab exec bash -c 'cd /workspace/$B/tools 2>/dev/null && for f in $(for f in $LAB_TOOLS; do basename $f; done | tr '\n' ' '); do echo \$(sha256sum < \$f | cut -c1-64) \$f; done'" 2>/dev/null | grep -v Container | LC_ALL=C sort -k2)
  [ "$want" = "$got" ]
}
halo_tools_ok() {
  local want got
  want=$( (cd authoring/tools/people/splat && find solv3r mesh-splat gsplat -name '*.py' -not -path '*__pycache__*' | xargs sha256sum) | LC_ALL=C sort -k2)
  got=$(ssh -o BatchMode=yes $HALO "cd ~/.local/state/asset-cpu/rusty-dagger 2>/dev/null && find solv3r mesh-splat gsplat -name '*.py' -not -path '*__pycache__*' | xargs sha256sum" 2>/dev/null | LC_ALL=C sort -k2)
  [ "$want" = "$got" ]
}
guard_tools() {   # guard_tools lab|halo
  [ -n "${DRY:-}" ] && return 0
  if [ $1 = lab ]; then
    lab_tools_ok && return 0
    [ -n "${STRICT_TOOLS:-}" ] && { echo "bake.sh: the lab's staged tools differ from the repository - refusing $STAGE (STRICT_TOOLS)" | tee -a $D/times.txt >&2; exit 76; }
    echo "bake.sh: the lab's staged tools differ from the repository - re-staging" | tee -a $D/times.txt >&2; bash $HERE/bake.sh $W $ID tools $HALO > /dev/null
    lab_tools_ok || { echo "bake.sh: the lab's staged tools still differ after re-staging - refusing $STAGE" | tee -a $D/times.txt >&2; exit 76; }
  else
    halo_tools_ok && return 0
    [ -n "${STRICT_TOOLS:-}" ] && { echo "bake.sh: $HALO's tool copy differs from the repository - refusing $STAGE (STRICT_TOOLS)" | tee -a $D/times.txt >&2; exit 76; }
    echo "bake.sh: $HALO's tool copy differs from the repository - re-syncing" | tee -a $D/times.txt >&2; authoring/tools/people/splat/hosts/strix-cpu/setup.sh $HALO > /dev/null 2>&1
    halo_tools_ok || { echo "bake.sh: $HALO's tool copy still differs after re-syncing - refusing $STAGE" | tee -a $D/times.txt >&2; exit 76; }
  fi
}
halo() {   # halo PURPOSE CMD...: run CMD in bake/ on the Halo (synced there and back)
  local p=$1; shift; local res=${HALO#den-}-asset-cpu
  [ -n "${DRY:-}" ] && { echo "DRY halo $HALO in $D: $*"; return 0; }
  guard_tools halo
  asset-resources run --host $HALO --resource $res --owner claude --purpose "Villager $ID bake: $p" -- \
    authoring/tools/people/splat/hosts/strix-cpu/run.sh $HALO $D -- bash -c "$*" 2>&1 | grep -v '^resource-board'
}
lab() {    # lab RESOURCE PURPOSE PUT FETCH CMD: CMD is written to bake/lab-STAGE.sh and run with bash in $LAB on the lab; under one lock:
           # tar PUT (paths under bake/, plus the script) to the lab, run it, fetch FETCH back.
  local res=$1 p=$2 put=$3 fetch=$4 cmd=$5
  printf '#!/bin/bash\n# %s (bake.sh %s %s), run on the GPU lab in %s\nPY=%s\n%s\n' "$p" "$ID" "$STAGE" "$LAB" "$PY" "$cmd" > $D/lab-$STAGE.sh
  put="lab-$STAGE.sh $put"
  [ -n "${DRY:-}" ] && { echo "DRY lab $LAB ($res): put [$put] fetch [$fetch]"; cat $D/lab-$STAGE.sh; return 0; }
  guard_tools lab
  if [ "$GPU" != gpu-lab ]; then
    export D GW GPU p put fetch ID STAGE
    flock -w ${LOCK_WAIT:-7200} /tmp/halo-gpu-$GPU.lock bash -c '
      echo "$(date +%s) before $ID $STAGE on $GPU" >> $D/vram.log
      ssh -o BatchMode=yes $GPU "mkdir -p $GW" && tar czf - -C $D $put | ssh -o BatchMode=yes $GPU "tar xzf - --no-same-owner -C $GW"
      asset-resources run --host $GPU --resource $GRES --owner claude --purpose "Villager $ID bake on the Halo GPU: $p" -- \
        ssh -o BatchMode=yes $GPU "export OMP_NUM_THREADS=8; cd $GW && nice -n 5 bash lab-$STAGE.sh" 2>&1 | grep -v "^resource-board"
      rc=${PIPESTATUS[0]}
      [ -n "$fetch" ] && { ssh -o BatchMode=yes $GPU "cd $GW && tar czf - $fetch 2>/dev/null" | tar xzf - -C $D; }
      exit $rc'
    local rc=$?
    if [ $rc = 1 ] && ! grep -q "before $ID $STAGE on $GPU" $D/vram.log 2>/dev/null; then echo "bake.sh: no $GPU GPU lock after ${LOCK_WAIT:-7200} s" >&2; return 75; fi
    return $rc
  fi
  export D LAB L res p put fetch ID STAGE
  flock -w ${LOCK_WAIT:-7200} /tmp/gpu-lab-claude.lock bash -c '
    echo "$(date +%s) before $ID $STAGE: vram $($L "gpu-lab exec nvidia-smi --query-gpu=memory.used,utilization.gpu --format=csv,noheader" 2>/dev/null | grep -v Container)" >> $D/vram.log
    $L "gpu-lab exec mkdir -p $LAB" 2>&1 | grep -v Container
    tar czf - -C $D $put | $L "gpu-lab exec tar xzf - -C $LAB" 2>&1 | grep -v Container
    asset-resources run --host gpu-lab --resource $res --owner claude --purpose "Villager $ID bake: $p" -- \
      $L "gpu-lab exec bash -c \"echo 1000 > /proc/self/oom_score_adj; export TORCH_EXTENSIONS_DIR=/cache/torch_extensions/py312_cu130 OMP_NUM_THREADS=2; cd $LAB && bash lab-$STAGE.sh\"" 2>&1 | grep -v -e Container -e "^resource-board"
    rc=${PIPESTATUS[0]}
    [ -n "$fetch" ] && { $L "gpu-lab exec tar czf - -C $LAB $fetch" 2>/dev/null | tar xzf - -C $D; }
    exit $rc'
  local rc=$?
  if [ $rc = 1 ] && ! grep -q "before $ID $STAGE" $D/vram.log 2>/dev/null; then echo "bake.sh: no lab lock after ${LOCK_WAIT:-7200} s" >&2; return 75; fi
  return $rc
}
stamp start $([ "$STAGE" = r0 -o "$STAGE" = cams -o "$STAGE" = rings -o "$STAGE" = vace -o "$STAGE" = r1 -o "$STAGE" = final ] && echo $GPU || echo $HALO)
case $STAGE in
tools)     # the lab copies, from the repository, to /workspace/$B/tools, under the lock
  S=$(mktemp -d); mkdir $S/tools
  cp $LAB_TOOLS $S/tools/
  [ -n "${DRY:-}" ] && { ls $S/tools; rm -rf $S; exit 0; }
  if [ "$GPU" != gpu-lab ]; then tar czf - -C $S tools | ssh -o BatchMode=yes $GPU "mkdir -p $GS/work/$B && tar xzf - --no-same-owner -C $GS/work/$B && ls $GS/work/$B/tools | wc -l"; rm -rf $S; exit 0; fi
  export L B S; flock -w ${LOCK_WAIT:-7200} /tmp/gpu-lab-claude.lock bash -c 'tar czf - -C $S tools | $L "gpu-lab exec bash -c \"mkdir -p /workspace/$B && tar xzf - -C /workspace/$B && ls /workspace/$B/tools | wc -l\"" 2>&1 | grep -v Container'
  rm -rf $S ;;
frames)
  # A bake starts here, so nothing from an earlier bake of this run id may survive: the Halo sync (strix-cpu/run.sh) brings the whole
  # staged folder back without deleting, and the GPU host keeps its copy too. npc-468's second run (2026-11-24) trained on the first
  # run's VACE repaints (the vace stage saw finished rings and skipped) and mattes from its BiRefNet masks. Clear both remote
  # copies and every local stage output (prompts.json stays).
  ssh -o BatchMode=yes $HALO "rm -rf ~/.local/state/asset-cpu/work/${D:?}" || echo "bake.sh: could not clear $HALO's staged copy of $D" >&2
  if [ "$GPU" = gpu-lab ]; then $L "gpu-lab exec rm -rf ${LAB:?}" > /dev/null 2>&1 || true; else ssh -o BatchMode=yes $GPU "rm -rf ${GW:?}" || true; fi
  find "${D:?}" -mindepth 1 -maxdepth 1 ! -name prompts.json -exec rm -rf {} +
  rm -rf $D/frames "${D:?}/birefnet"; cp -al $W/$CLIP/frames $D/frames; cp $W/$CLIP/run.json $D/clip-run.json
  echo "$W/$CLIP" > $D/clip.txt; [ -f $D/prompts.json ] || cp $W/$K/prompts.json $D/ 2>/dev/null; ls $D/frames | wc -l ;;
mattes)
  dir=$(python3 -c "import json;d=json.load(open('$D/clip-run.json')).get('orbit_direction',{}).get('direction',-1);print(d if d else -1)")
  # the clip's depth guide as a prior for which enclosed holes are background (2026-11-15: the lit-clay clips' grey has a glow, so the
  # flat test kept the gaps between arm and tunic): clip-run.json's depth control, or GUIDE_DEPTH="VIDEO[:FRAMES] ..." in clip order for
  # multi-clip bakes; guide/NNNNN.png is frame N's guide. No guide -> the flat and large tests only.
  GD=${GUIDE_DEPTH:-$(python3 -c "import json;d=json.load(open('$D/clip-run.json'));c=[x.split(':')[1] for x in d.get('control',[]) if x.startswith('depth:')];print(c[0] if c else '')" 2>/dev/null)}
  rm -rf "${D:?}/guide"; GOPT=""
  if [ -n "$GD" ]; then mkdir -p $D/guide; k=0
    for g in $GD; do v=${g%%:*}; n=${g#*:}; [ "$n" = "$g" ] && n=100000
      [ -f "$v" ] || { echo "guide $v missing: mattes without the guide" >&2; rm -rf "${D:?}/guide"; break; }
      tmp=$(mktemp -d); ffmpeg -loglevel error -i $v $tmp/%05d.png
      for f in $(ls $tmp | head -n $n); do mv $tmp/$f $D/guide/$(printf %05d $k).png; k=$((k+1)); done; rm -rf "${tmp:?}"; done
    [ -d $D/guide ] && GOPT="--guide guide"; fi
  # BiRefNet decides the ground-coloured parts (Patch 2026-11-15; Rusty Dagger's guarded matte): masks from the Halos' ComfyUI, made
  # here (HTTP), read by orbit_views.py --birefnet on the Halo. BIREFNET=0, or masks that do not all come back, -> the tests above decide.
  BOPT=""
  if [ "${BIREFNET:-1}" = 1 ] && [ -z "${DRY:-}" ]; then
    asset-resources run --host den-m5 --resource m5-comfy --owner claude --purpose "Villager $ID bake: BiRefNet masks for the mattes" -- \
      python3 authoring/tools/people/splat/mesh-splat/birefnet_masks.py $D/frames $D/birefnet --comfy ${BIREFNET_COMFY:-http://192.168.1.24:8188,http://192.168.1.23:8188} --last ${LAST:-71} 2>&1 | grep -v '^resource-board' | tail -n 1
    [ "$(ls $D/birefnet 2>/dev/null | wc -l)" -ge "$(( ${LAST:-71} + 1 ))" ] && BOPT="--birefnet birefnet${BIREFNET_EDGE:+ --birefnet-edge $BIREFNET_EDGE}" || echo "BiRefNet masks incomplete: the flat / large / guide tests decide" >&2
  fi
  halo "mattes and Solv3R inputs" "python3 \$ASSET_CPU_REPO/mesh-splat/orbit_views.py frames orbit --direction $dir --last ${LAST:-71} --step ${STEP:-5} --threshold 8 --open-holes ${OPEN_HOLES:-500} --hole-large ${HOLE_LARGE:-4000,2.5} ${KEY_HOLES:+--key-holes $KEY_HOLES} ${KEY_FILL:+--key-fill $KEY_FILL} ${DESPILL:+--despill $DESPILL} $GOPT $BOPT > /dev/null && python3 \$ASSET_CPU_REPO/mesh-splat/orbit_inputs.py orbit/views inputs > /dev/null && ls inputs/images | wc -l"; mrc=$?
  [ $mrc = 0 ] && { halo "matte review (glance)" "python3 \$ASSET_CPU_REPO/mesh-splat/matte_review.py . ${MATTE_CLIPS:+--clips $MATTE_CLIPS}" || echo "matte review failed (warning only)"; }
  (exit $mrc) ;;
cams)
  [ "$GPU" = gpu-lab ] && $L "gpu-lab exec mkdir -p /workspace/$B/tools" > /dev/null 2>&1
  # CAMS_MATTED=1 solves on the cut-outs over flat grey: a clip whose background is a flat texture fixed on screen (396's dark noise,
  # no floor) makes Solv3R keep every camera at one place (2026-11-18); the check after the stages refuses such a solve
  CI=inputs; if [ "${CAMS_MATTED:-0}" = 1 ] && [ -z "${DRY:-}" ]; then CI=inputs-matted; python3 -c "
from PIL import Image; import glob, os, numpy as np
D='$D'; os.makedirs(D+'/inputs-matted/images', exist_ok=True)
for f in sorted(glob.glob(D+'/inputs/images/*.png')):
    a=np.asarray(Image.open(f).convert('RGB')).astype(np.float32); m=Image.open(os.path.join(D,'inputs/masks',os.path.basename(f))).convert('L').resize((a.shape[1],a.shape[0]))
    k=np.asarray(m).astype(np.float32)[...,None]/255; Image.fromarray((a*k+128*(1-k)).astype(np.uint8)).save(D+'/inputs-matted/images/'+os.path.basename(f))"; fi
  lab lab-solv3r "Solv3R cameras" "$CI inputs/split.json" "solves" \
    "cp ../tools/solve.py . 2>/dev/null; PYTHONPATH=$SOLV3R $SPY solve.py $CI/images $SOLV3R_W solves --size 504 --calls 32 --split inputs/split.json 2>&1 | tail -n 3" ;;
fuse)
  halo "fuse and loop data" "OPENBLAS_NUM_THREADS=8 python3 \$ASSET_CPU_REPO/solv3r/fuse.py solves/solve-32.npz inputs cloud --cloud-only --up figure 2>&1 | tail -n 1 && rm -rf data && OPENBLAS_NUM_THREADS=8 python3 \$ASSET_CPU_REPO/solv3r/loop_data.py cloud inputs data --heldout 9 --size $SIZE 2>&1 | tail -n 1 | cut -c1-200"
  python3 -c "
import json,numpy as np; v=json.load(open('$D/data/views.json')); v=v['views'] if isinstance(v,dict) else v
a=np.array([x['cloud_iou'] for x in v]); r=[x['cloud_iou'] for x in v if x.get('role')!='anchor']
print('$ID iou min %.3f p10 %.3f median %.3f reserved mean %.3f'%(a.min(),np.percentile(a,10),np.median(a),np.mean(r)), 'views',len(v),'anchors',sum(x.get('role')=='anchor' for x in v),'worst',sorted([(round(x['cloud_iou'],3),x['name'][8:12]) for x in v])[:5])" | tee -a $D/cloud-iou.txt ;;
r0)
  lab lab-gsplat "round 0 at ${SIZE}px" "data" "r0 review-r0 r0.log" \
    "cp ../tools/*.py . && $PY -u train_loop_splat.py data r0 --steps 15000 --cap 400000 --ring 0 --pose-lr 2e-5 --opacity-reg 0 > r0.log 2>&1; grep -E 'SCORE|rror|Traceback' r0.log; $PY score_at.py r0 data 768 $SIZE; $PY splat_to_cloud.py r0/splat.npz r0/cloud.ply --report-only | cut -c1-300; $PY loop_review.py review-r0 'Round 0=r0' --orbit 72 --orbit-swing 40 --detail Face:0,0.39,0.06:0:5:6 --detail Face-above:0,0.39,0.04:0:40:8 --detail Legs-side:0,-0.32,0:90:5:16 --detail Legs-back:0,-0.32,0:180:5:16 --detail Legs-side-x:0,-0.32,0:-90:5:16 --detail Hem-below:0,-0.2,0:20:-50:18 --detail Shoes-below:0,-0.47,0:0:-40:14 ${EXTRA_REVIEW:-} | grep -c wrote" ;;
resid)     # per-view residual of round 0 against its frames, by body band and picture side (residual_by_view.py) -> resid-r0.jsonl
  lab lab-gsplat "round 0 per-view residuals" "" "resid-r0.jsonl" \
    "cp ../tools/residual_by_view.py . && $PY residual_by_view.py r0 data 768 > resid-r0.jsonl; wc -l < resid-r0.jsonl" ;;
rings)
  XR=; if [ -n "${EXTRA_RING:-}" ]; then IFS=: read -r xn xe xf xt <<< "$EXTRA_RING"
    XR="\$PY render_rings.py r0/splat.npz \$o --ring $xn:$xe --fov $xf --target 0.0 $xt 0.0 --size \$n --background 128 128 128 > /dev/null;"; fi
  lab lab-gsplat "ring renders from round 0" "" "rings-1024 rings-$SIZE" \
    "for n in 1024 $SIZE; do o=rings-\$n; $PY render_rings.py r0/splat.npz \$o --ring head-high:30,50 --fov 10.5 --target 0.0 0.40 0.0 --size \$n --background 128 128 128 > /dev/null; $PY render_rings.py r0/splat.npz \$o --ring high-close:50,78:1.1 --fov 19 --target 0.0 0.26 0.0 --size \$n --background 128 128 128 > /dev/null; $PY render_rings.py r0/splat.npz \$o --ring legs-low:-20,15:2.2 --fov 12 --target 0.0 -0.31 0.0 --size \$n --background 128 128 128 > /dev/null; $XR done; ls rings-1024" ;;
refs)
  ext=$(python3 -c "
import numpy as np; s=np.load('$D/r0/splat.npz'); y=s['means'][:,1]; print(round(float(np.percentile(y,99.9)-np.percentile(y,0.1)),4))")
  front=$W/$K/cut-t6/front-cut.png; [ -f $front ] || front=$W/$K/cut/front-cut.png
  XF=; [ -n "${EXTRA_RING:-}" ] && { IFS=: read -r xn xe xf xt <<< "$EXTRA_RING"; XF="$xn:$xf:$xt"; }
  mkdir -p $D/refs; python3 $HERE/make_refs.py $front $D/refs $ext head-high:10.5:0.40 high-close:19:0.26 legs-low:12:-0.31 $XF
  echo "extent $ext from $front" ;;
vace)
  J=$D/prompts.json; SEED=610000; OUT=$D/vace; mkdir -p $OUT; G0=$GPU
  # VACE runs on the 5090's ComfyUI unless VACE_GPU names a Halo or ~/.local/state/gpu-lab-off exists (Patch needs the 5090: touch it; rm to allow again)
  [ -n "${DRY:-}" ] && { echo "DRY vace: rings from $D/rings-1024, refs $D/refs, prompts $J"; exit 0; }
  for ring in $RINGS; do
    # a ring made before the current refs belongs to an earlier design (395's capelet reused the long cape's VACE, 2026-11-19): redo it
    [ -d $OUT/$ring ] && [ -n "$(ls $OUT/$ring 2>/dev/null)" ] && [ ! $D/refs -nt $OUT/$ring ] && { SEED=$((SEED+1)); continue; }
    P=$(python3 -c "import json;d=json.load(open('$J'));print(d['${ring%%-*}'])"); N=$(python3 -c "import json;d=json.load(open('$J'));print(d.get('negative-${ring%%-*}', d['negative']))")
    t0=$(date +%s)
    VLOCK=/tmp/gpu-lab-claude.lock; VCOMFY=http://192.168.1.20:8188; VRES=lab-wan-vace
    # VACE is ComfyUI, not gsplat: on a Halo bake it still runs on the 5090 (12 min there against r0's 7x Halo slow-down; Patch
    # 2026-11-18: moving some stages back is fine), unless VACE_GPU names the Halo or the lab's ComfyUI doesn't answer.
    VG=${VACE_GPU:-gpu-lab}; [ -e ~/.local/state/gpu-lab-off ] && VG=$G0; [ "$VG" = gpu-lab ] && ! curl -sf -m 10 $VCOMFY/queue > /dev/null && VG=$G0
    [ "$G0" != gpu-lab ] && [ "$VG" = gpu-lab ] && echo "vace $ring on the 5090's ComfyUI"
    GPU=$VG
    [ "$GPU" != gpu-lab ] && { VLOCK=/tmp/halo-gpu-$GPU.lock; VRES=${GPU#den-}-comfy; VCOMFY=http://$(case $GPU in den-m5) echo 192.168.1.24;; den-nimo) echo 192.168.1.23;; esac):8188; }
    export ring P N SEED OUT D ID GPU VCOMFY VRES
    vrun() {
      flock -w ${LOCK_WAIT:-7200} $VLOCK bash -c 'set -o pipefail
        echo "$(date +%s) before $ID vace $ring: vram $(~/server-access/gpu-lab/connect "gpu-lab exec nvidia-smi --query-gpu=memory.used,utilization.gpu --format=csv,noheader" 2>/dev/null | grep -v Container)" >> $D/vram.log
        [ "$GPU" = gpu-lab ] && authoring/tools/people/splat/bake/lab-comfy-restart.sh $D/vram.log --in-lock --if-rss-gb ${RESTART_RSS_GB:-20}   # clear held memory (Patch, 2026-11-14)
        asset-resources run --host $GPU --resource $VRES --owner claude --purpose "Villager $ID bake: VACE ring $ring" -- \
          python3 -I authoring/tools/people/splat/gsplat/vace_ring.py $D/rings-1024/$ring $D/refs/$ring.png $OUT/$ring --comfy $VCOMFY \
          --strength 0.8 --prompt "$P" --negative "$N" --seed $SEED 2>&1 | grep -E "done|rror|status"'
    }
    # a Halo's ComfyUI can crash mid-job (den-m5, 2026-11-24: SEGV/ABRT three times on model loads after gsplat work): retry the
    # ring once on the 5090 when it answers
    vrun || { [ "$GPU" != gpu-lab ] && curl -sf -m 10 http://192.168.1.20:8188/queue > /dev/null && { echo "vace $ring failed on $GPU; again on the 5090"
      GPU=gpu-lab VLOCK=/tmp/gpu-lab-claude.lock VCOMFY=http://192.168.1.20:8188 VRES=lab-wan-vace; export GPU VCOMFY VRES; vrun; }; } || { echo "FAILED $ring"; exit 1; }
    echo "$ring $(( $(date +%s) - t0 )) s" | tee -a $OUT/times.txt; SEED=$((SEED+1))
  done ;;
match)
  # MATCH_OPTS (bake.env) passes repaint_match.py options: 453's legs-low dropped its back third because round 0 shades the skirt's
  # back nearly black and VACE paints it lit, a lighting difference, not new content (2026-11-19: --max-ignore 0.5)
  halo "VACE colour match and ignore masks" "rm -rf vace-matched; for ring in $RINGS; do OPENBLAS_NUM_THREADS=8 python3 \$ASSET_CPU_REPO/gsplat/repaint_match.py rings-1024/\$ring vace/\$ring vace-matched/\$ring ${MATCH_OPTS:-} || exit 3; done" ;;
r1data)
  V=vace-matched; [ "${MATCH:-1}" = 0 ] && V=vace
  halo "round-1 training set" "rm -rf r1-data; OPENBLAS_NUM_THREADS=8 python3 \$ASSET_CPU_REPO/gsplat/loop_dataset.py r1-data --anchors data --poses r0/poses.npz --init r0/splat.npz --ring rings-$SIZE/head-high:$V/head-high --ring rings-$SIZE/high-close:$V/high-close --ring rings-$SIZE/legs-low:$V/legs-low ${EXTRA_RING:+--ring rings-$SIZE/${EXTRA_RING%%:*}:$V/${EXTRA_RING%%:*}} --exact-every 9 --fill-holes 300 --dilate 6 --background-from-alpha 2>&1 | tail -n 2 | cut -c1-300" ;;
r1)
  lab lab-gsplat "round 1 at ${SIZE}px, anchor weight 4" "r1-data" "r1 review-r1 r1.log" \
    "cp ../tools/*.py . 2>/dev/null; $PY -u train_loop_splat.py r1-data r1 --steps 15000 --cap 400000 --ring 0 --pose-lr 2e-5 --anchor-weight 4 --opacity-reg 0 > r1.log 2>&1; grep -E 'SCORE|rror|Traceback' r1.log; $PY splat_to_cloud.py r1/splat.npz r1/cloud.ply --report-only | cut -c1-300; $PY loop_review.py review-r1 'Round 0=r0' 'Round 1=r1' --detail Face:0,0.39,0.06:0:5:6 --detail Face-above:0,0.39,0.04:0:40:8 --detail Crown:0,0.42,0:150:55:9 --detail Legs-side:0,-0.32,0:90:5:16 --detail Legs-back:0,-0.32,0:180:5:16 --detail Shoes-below:0,-0.47,0:0:-40:14 --detail Hem-below:0,-0.2,0:20:-50:18 --detail Shoulders-above:0,0.25,0:30:65:16 | grep -c wrote" ;;
soles)
  y=$(python3 -c "
import numpy as np; s=np.load('$D/r1/splat.npz'); print(round(float(np.percentile(s['means'][:,1],0.1))+0.022,3))")
  halo "sole pass below $y" "mkdir -p final && python3 \$ASSET_CPU_REPO/mesh-splat/cap_splat.py r1/splat.npz final/splat.npz --shade-below $y --shade 0.45 --max-scale 0.005 --report final/sole.json > /dev/null && cat final/sole.json | tr -d '\n ' | cut -c1-300; echo" ;;
final)
  lab lab-gsplat "final cloud and review sheets" "final" "final review-final" \
    "$PY splat_to_cloud.py final/splat.npz final/cloud.ply --min-views 3 --agree 0.004 | cut -c1-400; rm -rf review-final; $PY loop_review.py review-final 'Round 0=r0' 'Final=final' --detail Face:0,0.39,0.06:0:5:6 --detail Face-above:0,0.39,0.04:0:40:8 --detail Crown:0,0.42,0:150:55:9 --detail Legs-side:0,-0.32,0:90:5:16 --detail Legs-back:0,-0.32,0:180:5:16 --detail Shoes-below:0,-0.47,0:0:-40:14 --detail Hem-below:0,-0.2,0:20:-50:18 --detail Shoulders-above:0,0.25,0:30:65:16 | grep -c wrote; $PY compare.py review-final/closeups.png 'Round 0=r0/splat.npz' 'Final=final/splat.npz'" ;;
*) echo "unknown stage $STAGE" >&2; exit 2 ;;
esac
rc=${PIPESTATUS[0]:-$?}
stamp end $([ "$STAGE" = r0 -o "$STAGE" = cams -o "$STAGE" = rings -o "$STAGE" = vace -o "$STAGE" = r1 -o "$STAGE" = final ] && echo $GPU || echo $HALO)
# A stage that ran but left nothing behind fails here (2026-11-18: right after the GPU lab rebooted, 395's and 396's rings, refs,
# vace, match and r1data each "ended" in seconds with empty folders and the chain went on). Exit 6 names what is missing.
need() { for p in "$@"; do [ -e "$D/$p" ] && [ -n "$(ls -A "$D/$p" 2>/dev/null)" ] || { echo "bake.sh: $STAGE left no $p" >&2; echo "$STAGE missing $p $(date +%s)" >> $D/times.txt; exit 6; }; done; }
if [ -z "${DRY:-}" ]; then
  case $STAGE in
    cams) need solves/solve-32.npz
      python3 -c "import numpy as np,sys; t=np.load('$D/solves/solve-32.npz')['c2w'][:,:3,3]; r=np.linalg.norm(t-t.mean(0),axis=1).mean(); print('camera orbit radius %.3f'%r); sys.exit(int(r<0.2))" \
        || { echo "bake.sh: Solv3R kept the cameras in one place (flat background fixed on screen?) - try CAMS_MATTED=1" >&2; echo "cams degenerate $(date +%s)" >> $D/times.txt; exit 6; } ;; fuse) need data/views.json ;; r0) need r0/splat.npz ;;
    rings) need rings-1024/head-high rings-1024/high-close rings-1024/legs-low ;;
    refs) need refs/head-high.png refs/high-close.png refs/legs-low.png ;;
    vace) need vace/head-high vace/high-close vace/legs-low ;;
    match) need vace-matched/head-high vace-matched/high-close vace-matched/legs-low ;;
    r1data) need r1-data/images r1-data/views.json ;; r1) need r1/splat.npz ;;
  esac
fi
exit 0   # as before: the stage commands' own codes are not used (several end on a grep), only the checks above
