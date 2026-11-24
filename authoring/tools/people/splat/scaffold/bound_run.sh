#!/bin/bash
# bound_run.sh RUN ID STEP [ARGS]: the bound route's steps after a character's bake has reached round 1 (bake.sh ... r1), as run for
# villagers 381 and 454 (docs/splat-scaffold.md). RUN is the run folder, ID is NNN-X (the bake is RUN/NNN/X/bake); results go to
# RUN/NNN/X/bound. BAKE=bake-m3 takes the bake from RUN/NNN/X/bake-m3 and the lab folder /workspace/RUN/NNN-X-m3, as bake.sh names them.
# Without a free round-1 fit (bake run to r1data only) score and the bound review use round 0 instead.
# BOUND_DATA=data bakes from round 0's data (no repaint rings) and BOUND_NAME=bound-r0 writes RUN/NNN/X/bound-r0 (lab: bound-r0,
# bound-r0-data) beside the approved bake: the bound step only (distil, clean and clips still read RUN/NNN/X/bound/bound/).
# The scaffold is the character folder's 02-guide/scaffold/scaffold.glb (CHAR=id overrides the folder name NNN).
#
#   score              fit the scaffold to the bake and score its outline against the cut-outs (score_outline.py) -> bound/score
#   bound              the full-size bound bake: 200,000 start points on the scaffold coloured from the pictures, the distance grid
#                      (2.5 cm reach, 2.4 times that round the hands), round 1's pictures, cameras and trainer settings on the GPU lab,
#                      review sheets (round 1 above, bound below), binding to the rig -> bound/bound, bound/binding.npz
#   distil [N] [SHARE] a runtime body of N Gaussians (default 16000) with SHARE of the seed on the face (default 0.25): 252 renders of
#                      the full bake at 2176 px, seed with a face share, 15000 steps held to the same shell, binding
#                      -> bound/sizes/<N/1000>k-face-share
#   clean [NAME]       clean_bound.py on the full bake (-> bound/bound-cleaned) and on size NAME (default 16k-face-share -> NAME-cleaned)
#   skirt [NAME] [W]   for a character in a long skirt: skirt_weights.py --width W (default 1.6) on the cleaned full bake
#                      (-> bound/bound-cleaned-skirt) and on size NAME (default 16k-face-share-cleaned -> NAME-skirt), so the skirt
#                      stretches between the legs instead of tearing where they part; SKIRT_BONES=8 hangs it on its own ring of bones instead
#   hands [NAME]       unmix_hands.py on the cleaned full bake and size NAME (default 16k-face-share-cleaned -> NAME-hands): a
#                      Gaussian weighted to both a hand and the body follows the larger side (hands near a hem streaked in a walk)
#   tidy [NAME]        after clips NAME: drop_loose.py drops Gaussians that come loose from the body in the clips (flecks beside a
#                      hem) from size NAME and its full bake -> NAME-tidy; clips NAME-tidy renders them again
#   idle [NAME]        a static NPC (static_rig.py): static_idle.py's procedural idle and look clips in place of clips, six views
#                      -> bound/idle/renders
#   clips [NAME]       the full bake and size NAME (default 16k-face-share-cleaned) on Mixamo Walking and Around, rendered as Gaussians
#                      on the lab, four views -> bound/clips/renders (clip_compare.py videos builds the videos from them)
#
# Lab steps run under the shared lab lock with asset-resources run and oom_score_adj 1000. Run long steps under batch/watchdog.sh.
# Exit code: the first failing step's.
set -u
REPO=$(cd "$(dirname "$0")/../../../../.." && pwd); cd "$REPO"
R=$(realpath --relative-to="$REPO" "$1"); ID=$2; STEP=$3; shift 3
K=${ID%-*}/${ID#*-}; B=$(basename "$R"); BK=${BAKE:-bake}; D=$R/$K/$BK; BN=${BOUND_NAME:-bound}; BD=$R/$K/$BN; DATA=${BOUND_DATA:-r1-data}; T=authoring/tools/people/splat/scaffold; C=authoring/tools/people/splat/gsplat
CH=local/art/generated/people/dagger-characters/${CHAR:-$([ -d local/art/generated/people/dagger-characters/$ID ] && echo $ID || echo ${ID%-*})}
LOOSE=${LOOSE:-LeftHand,RightHand}; LOOSE_FACTOR=${LOOSE_FACTOR:-2.4}   # LOOSE=Head,LeftHand,RightHand LOOSE_FACTOR=2.8: more room inside a deep hood or a mane of hair, where the mesher's head is too thick (021's see-through face, 2026-11-22)
SC=${SCAFFOLD:-$CH/02-guide/scaffold/scaffold.glb}; TR=$BD/score/transform.json   # SCAFFOLD=: another rig for the same body (cape_chain.py rig adds a cape chain)
LAB=/workspace/$B/$ID${BK#bake}; L=~/server-access/gpu-lab/connect; GPY=/workspace/splat-probe/venv/bin/python
# GPU=den-m5|den-nimo: the lab steps on a Strix Halo's GPU (authoring/tools/people/splat/hosts/strix-gpu/setup.sh; bake.sh GPU=... stages the tools there)
GPU=${GPU:-gpu-lab}; GS=/home/agent/.local/state/asset-gpu; LOCKF=/tmp/gpu-lab-claude.lock; GRES=${GPU#den-}-comfy
case $GPU in runpod*) GS=/workspace/asset-gpu; GRES=$GPU ;; esac   # a rented RunPod pod (ssh alias runpod-*; bake.sh says more)
export GRES
if [ "$GPU" != gpu-lab ]; then LAB=$GS/work/$B/$ID${BK#bake}; GPY=$GS/venv/bin/python; LOCKF=/tmp/halo-gpu-$GPU.lock; fi
rx() { if [ "$GPU" = gpu-lab ]; then $L "gpu-lab exec $*"; else ssh -o BatchMode=yes $GPU "$*"; fi; }   # one simple command on the GPU host
export -f rx; export GPU L
CLIPS=${CLIPS:-local/art/generated/people/dagger-characters/385/09-game/bound/clips}
[ -f $SC ] || { echo "bound_run.sh: no scaffold at $SC" >&2; exit 2; }
mkdir -p $BD
reach() { python3 -c "import json;print(round(${SHELL_M:-0.025}*json.load(open('$TR'))['scale'],5))"; }   # 2.5 cm in the bake's units (the scaffold is in metres); SHELL_M=0.06: a static NPC whose mesher missed the robe's real shape by 6 cm (017, 2026-11-22)
lab() {   # lab PURPOSE PUT_DIR PUT_FILES SCRIPT LOG FETCH: tar PUT_FILES (under PUT_DIR) to the GPU host's folder, run SCRIPT there, fetch FETCH into bound/
  local purpose=$1 pd=$2 put=$3 script=$4 log=$5 fetch=$6
  export BD LAB L pd put script log fetch purpose GPU
  flock -w ${LOCK_WAIT:-7200} $LOCKF bash -c '
    rx "mkdir -p $LAB" 2>&1 | grep -v Container
    tar czf - -C $pd $put | rx "tar xzf - -C $LAB" 2>&1 | grep -v Container
    if [ "$GPU" = gpu-lab ]; then
      asset-resources run --host gpu-lab --resource lab-gsplat --owner claude --purpose "$purpose" -- \
        $L "gpu-lab exec bash -c \"echo 1000 > /proc/self/oom_score_adj; export TORCH_EXTENSIONS_DIR=/cache/torch_extensions/py312_cu130 OMP_NUM_THREADS=2; cd $LAB && bash $script\"" 2>&1 | grep --line-buffered -v -e Container -e "^resource-board" >> $log
    else
      asset-resources run --host $GPU --resource $GRES --owner claude --purpose "$purpose (Halo GPU)" -- \
        ssh -o BatchMode=yes $GPU "export OMP_NUM_THREADS=8; cd $LAB && nice -n 5 bash $script" 2>&1 | grep --line-buffered -v -e "^resource-board" >> $log
    fi
    rc=${PIPESTATUS[0]}
    [ -n "$fetch" ] && { rx "tar czf - -C $LAB $fetch" 2>/dev/null | tar xzf - -C $BD; }
    exit $rc'
}
src_of() {   # SRC: the full bake that goes with size NAME (cleaned, skirt or hands variant, then -tidy / -sleeves after those steps)
  case $1 in *-tidy) src_of "${1%-tidy}"; SRC=$SRC-tidy; return ;; *-sleeves) src_of "${1%-sleeves}"; SRC=$SRC-sleeves; return ;; *-cape) src_of "${1%-cape}"; SRC=$SRC-cape; return ;; esac
  local n=$1; SRC=$BD/bound-cleaned; [ -f $SRC/splat.npz ] || SRC=$BD/bound
  [ "${n%-skirt}" != "$n" ] && [ -f $BD/bound-cleaned-skirt/splat.npz ] && SRC=$BD/bound-cleaned-skirt
  [ "${n%-hands}" != "$n" ] && [ -f $BD/bound-cleaned-hands/splat.npz ] && SRC=$BD/bound-cleaned-hands
  true
}
case $STEP in
score)
  FIT=r1; [ -f $D/r1/splat.npz ] || FIT=r0   # NO_R1 bakes (pipeline.sh ... r1data) have no free round-1 fit: round 0 lines the scaffold up
  python3 $T/score_outline.py $SC $D/r1-data $D/$FIT/splat.npz $BD/score --views 0:72:6 ${YAW:+--yaw $YAW} | tail -n 1 ;;   # YAW=: paintings drawn at an angle (static_rig.py --view-yaw)
bound)
  [ -f $TR ] || { echo "bound_run.sh: run the score step first" >&2; exit 2; }
  REACH=$(reach); echo "reach $REACH bake units" > $BD/bound.log; FIT=r1; [ -f $D/r1/splat.npz ] || FIT=r0; FITL=$( [ $FIT = r1 ] && echo 'Round 1' || echo 'Round 0' )
  python3 $T/bind_colours.py $SC $TR x $BD/init.bin --count 200000 --frames $D/$DATA --views 0:72:3 >> $BD/bound.log 2>&1 || exit 2
  mkdir -p $BD/$BN-data; python3 $T/bound_data.py $BD/init.bin $SC $TR $BD/$BN-data --loose-bones $LOOSE --loose-factor $LOOSE_FACTOR >> $BD/bound.log 2>&1 || exit 3
  cat > $BD/lab-bound.sh <<EOF
#!/bin/bash
# lab side of $ID's bound bake, run in $LAB
PY=$GPY
cp ../tools/*.py . ; ln -sfn ../$DATA/images $BN-data/images; [ -d $DATA/ignore ] && ln -sfn ../$DATA/ignore $BN-data/ignore; cp $DATA/cameras.npz $DATA/views.json $BN-data/ || exit 2
\$PY -u ${VRAM_PEAK:+vram_peak.py} train_loop_splat.py $BN-data $BN --steps 15000 --cap 400000 --ring 0 --pose-lr 2e-5 --anchor-weight 4 --opacity-reg 0 --shell $BN-data/shell.npz --shell-reach $REACH > $BN.log 2>&1
rc=\$?; grep -E 'SCORE|rror|Traceback|shell |VRAM_PEAK' $BN.log; [ \$rc = 0 ] || exit \$rc
\$PY splat_to_cloud.py $BN/splat.npz $BN/cloud.ply --report-only | cut -c1-300
\$PY loop_review.py review-$BN '$FITL=$FIT' 'Bound=$BN' --detail Face:0,0.39,0.06:0:5:6 --detail Face-above:0,0.39,0.04:0:40:8 --detail Crown:0,0.42,0:150:55:9 --detail Legs-side:0,-0.32,0:90:5:16 --detail Legs-back:0,-0.32,0:180:5:16 --detail Shoes-below:0,-0.47,0:0:-40:14 --detail Hem-below:0,-0.2,0:20:-50:18 --detail Shoulders-above:0,0.25,0:30:65:16 | grep -c wrote
EOF
  # without a free round-1 fit nothing has put the training data on the lab yet: send it (about 600 MB) under the lab lock. A stamp of
  # the local files (names, sizes, times) must match the staged copy's: 395's capelet rebake was once bound to the long-cape bake's
  # views left on den-m5, because only the presence of cameras.npz was checked (2026-11-19)
  STAMP=$(cd $D && find $DATA -type f ! -name .stamp -printf '%P %s %T@\n' | sort | md5sum | cut -c1-12); echo $STAMP > $D/$DATA/.stamp
  flock -w ${LOCK_WAIT:-7200} $LOCKF bash -c "[ \"\$(rx 'cat $LAB/$DATA/.stamp' 2>/dev/null | grep -v Container)\" = $STAMP ] || { rx 'mkdir -p $LAB' 2>&1 | grep -v Container; rx 'rm -rf $LAB/$DATA' 2>&1 | grep -v Container; tar czf - -C $D $DATA | rx 'tar xzf - -C $LAB' 2>&1 | grep -v Container; echo 'staged $DATA ($STAMP) to $GPU'; }" >> $BD/bound.log
  [ -n "${VRAM_PEAK:-}" ] && cp $T/vram_peak.py $BD/   # VRAM_PEAK=1: the trainer's peak GPU memory in the log (vram_peak.py)
  # the review compares with the free fit ($FIT), which a host that never ran it (a fresh RunPod pod, npc-023 2026-11-22) lacks
  [ "$GPU" != gpu-lab ] && { rx "test -f $LAB/$FIT/splat.npz" || tar czf - -C $D $FIT/splat.npz | rx "mkdir -p $LAB && tar xzf - -C $LAB"; }
  lab "Villager $ID bound bake on the scaffold" $BD "lab-bound.sh $BN-data/shell.npz $BN-data/init_points.npz${VRAM_PEAK:+ vram_peak.py}" lab-bound.sh $BD/bound.log "$BN/splat.npz $BN/score.json $BN/poses.npz $BN.log review-$BN" || exit 4
  [ -f $BD/$BN/splat.npz ] || exit 5
  python3 $T/export_bound.py $BD/$BN/splat.npz $SC $TR $BD/discs.bin --binding $BD/binding.npz --loose-bones $LOOSE --loose-factor $LOOSE_FACTOR > $BD/binding.json 2>> $BD/bound.log || exit 6
  grep -E "SCORE" $BD/bound.log | tail -n 1 | cut -c1-400; cat $BD/binding.json ;;
distil)
  N=${1:-16000}; SH=${2:-0.25}; NAME=$((N / 1000))k-face-share; REACH=$(reach); O=$BD/sizes/$NAME; mkdir -p $O
  python3 $T/bone_region.py $BD/$BN/splat.npz $BD/binding.npz $SC $BD/face.npy --bones Head --front 0 > $BD/face.json || exit 2
  DD=dist-data-$(md5sum < $BD/$BN/splat.npz | cut -c1-8)   # views of this bake only (391-A's re-bound body was once trained on the old bake's views, 2026-11-18)
  cat > $BD/lab-distil.sh <<EOF
#!/bin/bash
# lab side of $ID's distil to $NAME, run in $LAB
PY=$GPY
[ -f $DD/views.json ] || \$PY distill_dataset.py $BN/splat.npz $DD --size 2176 --distance 2.25 --elevations=-55,-35,-15,0,15,35,60 > dist.log 2>&1 || { tail -n 5 dist.log; exit 2; }
echo "dataset \$(ls dist-data/images | wc -l) views"
\$PY budget_seed.py $DD $BN/splat.npz $NAME-data --count $N --link --region face.npy --share $SH > $NAME.seed.json || { cat $NAME.seed.json; exit 3; }
\$PY -u ${VRAM_PEAK:+vram_peak.py} train_loop_splat.py $NAME-data $NAME --cap $N --steps 15000 --ring 0 --freeze-poses --anchor-weight 1 --opacity-reg 0 --shell $BN-data/shell.npz --shell-reach $REACH > $NAME.train.log 2>&1 || { tail -n 5 $NAME.train.log; exit 4; }
grep -E 'SCORE|shell |VRAM_PEAK' $NAME.train.log
EOF
  mkdir -p $BD/stage-distil; cp $C/budget_seed.py $C/distill_dataset.py $C/train_loop_splat.py $T/vram_peak.py $BD/lab-distil.sh $BD/face.npy $BD/stage-distil/
  lab "Villager $ID runtime body: $NAME from the bound bake" $BD/stage-distil "budget_seed.py distill_dataset.py train_loop_splat.py vram_peak.py lab-distil.sh face.npy" lab-distil.sh $BD/distil-$NAME.log "" || exit 4
  rx "tar czf - -C $LAB $NAME/splat.npz $NAME/score.json $NAME.seed.json $NAME.train.log" 2>/dev/null | tar xzf - -C $BD/sizes --transform "s,^$NAME\\.,$NAME/,"
  [ -f $O/splat.npz ] || exit 5
  python3 $T/export_bound.py $O/splat.npz $SC $TR $O/discs.bin --binding $O/binding.npz --loose-bones $LOOSE --loose-factor $LOOSE_FACTOR > $O/binding.json || exit 6
  tail -n 2 $BD/distil-$NAME.log | cut -c1-400; cat $O/binding.json ;;
clean)
  NAME=${1:-16k-face-share}
  # CLEAN_OPTS="--no-face --no-crown": a character whose hair is greenish or who wears a crown (npc-024, 2026-11-23: the face rule
  # took his grey-green hair, the crown rule recoloured his gold crown to hair); static-npc passes npc.json "clean_opts"
  python3 $T/clean_bound.py $BD/bound/splat.npz $BD/binding.npz $SC $BD/bound-cleaned ${CLEAN_OPTS:-} || exit 2
  python3 $T/clean_bound.py $BD/sizes/$NAME/splat.npz $BD/sizes/$NAME/binding.npz $SC $BD/sizes/$NAME-cleaned ${CLEAN_OPTS:-} || exit 3
  cp $BD/sizes/$NAME/score.json $BD/sizes/$NAME-cleaned/score.json ;;   # the uncleaned body's score: the cleaned one is not scored again
skirt)
  NAME=${1:-16k-face-share-cleaned}; WD=${2:-1.6}; SKR=$BD/skirt-rig; SSC=$SC; BONES=
  # SKIRT_BONES=8: the skirt on 8 columns of skirt bones (skirt_bones.py; 395 and 397, Patch 2026-11-19), since 2026-11-20 two tiers each
  # (SkirtK_U, SkirtK_L) driven by an XPBD cloth sheet per clip (Patch: "make the xpbd skirt standard"); SKIRT_RIG_OPTS passes rig options
  # (--slit for a skirt that parts at the front, --tiers 1 for the rigid ring). clips and bound.py playable then use the rig in
  # bound/skirt-rig and drive those bones per clip. Without it the rig is removed.
  rm -rf "${SKR:?}"
  if [ -n "${SKIRT_BONES:-}" ]; then
    python3 $T/skirt_bones.py rig $SC $TR $BD/bound-cleaned/splat.npz $BD/bound-cleaned/binding.npz $SKR --columns $SKIRT_BONES ${SKIRT_RIG_OPTS:-} || exit 2
    SSC=$SKR/scaffold.glb; BONES="--bones $SKR/skirt.json"
  fi
  for pair in "$BD/bound-cleaned $BD/bound-cleaned-skirt $BD/bound/score.json" "$BD/sizes/$NAME $BD/sizes/$NAME-skirt $BD/sizes/$NAME/score.json"; do set -- $pair
    python3 $T/skirt_weights.py $1/splat.npz $1/binding.npz $SSC $TR $2 --width $WD ${SKIRT_OPTS:-} $BONES || exit 2   # SKIRT_OPTS="--hips 0.5": 397-B
    [ -L $2/splat.npz ] && { rm $2/splat.npz; cp $1/splat.npz $2/splat.npz; }; cp $3 $2/score.json   # real files: they are hard-linked into the character folder
  done ;;
hands)
  NAME=${1:-16k-face-share-cleaned}
  python3 $T/unmix_hands.py $BD/bound-cleaned/splat.npz $BD/bound-cleaned/binding.npz $SC $BD/bound-cleaned-hands || exit 2
  python3 $T/unmix_hands.py $BD/sizes/$NAME/splat.npz $BD/sizes/$NAME/binding.npz $SC $BD/sizes/$NAME-hands || exit 3
  cp $BD/bound/score.json $BD/bound-cleaned-hands/; cp $BD/sizes/$NAME/score.json $BD/sizes/$NAME-hands/ ;;
sleeves)
  # long sleeves that hang beside the thighs (392's bell sleeves): sleeve_flood.py binds the sleeve wholly to its forearm and hand,
  # on size NAME and its full bake -> NAME-sleeves and <full>-sleeves; then clips NAME-sleeves (CLIPS_TAG=-sleeves) to look again
  NAME=${1:?size name}; src_of "$NAME"
  python3 $T/sleeve_flood.py $SRC/splat.npz $SRC/binding.npz $SC $TR $SRC-sleeves || exit 3
  python3 $T/sleeve_flood.py $BD/sizes/$NAME/splat.npz $BD/sizes/$NAME/binding.npz $SC $TR $BD/sizes/$NAME-sleeves || exit 4
  cp $SRC/score.json $SRC-sleeves/ 2>/dev/null; cp $BD/sizes/$NAME/score.json $BD/sizes/$NAME-sleeves/ 2>/dev/null; true ;;
tidy)
  # after clips: drop_loose.py on size NAME (default 16k-face-share-cleaned-hands) and the full bake the clips showed beside it, with
  # the clips' own poses (bound_run.sh clips NAME first) -> NAME-tidy and <full>-tidy; then clips NAME-tidy to look again
  NAME=${1:-16k-face-share-cleaned-hands}; P=$BD/clips${CLIPS_TAG:-}/data; src_of "$NAME"
  [ -f $P/body.npz ] || { echo "bound_run.sh: run clips $NAME first" >&2; exit 2; }
  # the rig the clips posed names the bones: skirt-bone cloth and head hair are never dropped (456, 2026-11-20: tidy cut the
  # stretched hem into shards and left a hole of skin in her back hair)
  RIG=$SC; case $NAME in *-skirt*) [ -f $BD/skirt-rig/skirt.json ] && RIG=$BD/skirt-rig/scaffold.glb ;; esac
  python3 $T/drop_loose.py $SRC/splat.npz $SRC/binding.npz $P bound400k $SRC-tidy --rig $RIG || exit 3
  python3 $T/drop_loose.py $BD/sizes/$NAME/splat.npz $BD/sizes/$NAME/binding.npz $P body $BD/sizes/$NAME-tidy --rig $RIG || exit 4
  cp $SRC/score.json $SRC-tidy/ 2>/dev/null || cp $BD/bound/score.json $SRC-tidy/; cp $BD/sizes/$NAME/score.json $BD/sizes/$NAME-tidy/ ;;
clips)
  NAME=${1:-16k-face-share-cleaned}; O=$BD/clips${CLIPS_TAG:-}; mkdir -p $O; src_of "$NAME"
  BIND=$SRC/binding.npz; [ -f $BIND ] || BIND=$BD/binding.npz
  M=$(python3 -c "import json;print(1/json.load(open('$TR'))['scale'])"); CSC=$SC; SKJ=
  case $SRC in *-skirt*) [ -f $BD/skirt-rig/skirt.json ] && { CSC=$BD/skirt-rig/scaffold.glb; SKJ=$BD/skirt-rig/skirt.json; } ;; esac   # skirt bones (SKIRT_BONES)
  python3 $T/clip_compare.py prep $O/data --metres $M --views quarter,close,face,back-quarter \
    --bound bound400k=$SRC/splat.npz:$BIND:$CSC:$TR --bound body=$BD/sizes/$NAME/splat.npz:$BD/sizes/$NAME/binding.npz:$CSC:$TR \
    --clip walking=$CLIPS/walking.json --clip around=$CLIPS/around.json > $O/prep.log 2>&1 || { tail -n 5 $O/prep.log; exit 2; }
  [ -n "$SKJ" ] && { python3 $T/skirt_bones.py drive $O/data $SKJ ${SKIRT_DRIVE_OPTS:-} >> $O/prep.log 2>&1 || { tail -n 5 $O/prep.log; exit 2; }; }
  [ -n "${CAPE:-}" ] && { python3 $T/cape_chain.py sim $O/data $CAPE >> $O/prep.log 2>&1 || { tail -n 5 $O/prep.log; exit 2; }; }   # CAPE=chain.json: swing the cape chain (with SCAFFOLD= its rig)
  TAG=${CLIPS_TAG:-}; cat > $O/render.sh <<EOF
#!/bin/bash
py=$GPY; cd clips-bound$TAG || exit 1
for c in walking around; do for t in bound400k body; do \$py ../render_posed.py data/\$t.npz data/\$t-binding.npz data/poses-\$t-\$c.npz renders/\$t/\$c --jpg || exit 1; done; done
ls renders/*/* | wc -l
EOF
  rm -rf "${O:?}/clips-bound$TAG"; mkdir -p $O/clips-bound$TAG; cp -al $O/data $O/clips-bound$TAG/data 2>/dev/null || cp -r $O/data $O/clips-bound$TAG/data; cp $O/render.sh $O/clips-bound$TAG/; cp $T/render_posed.py $O/render_posed.py   # the staging copy is rebuilt: its hard links would keep an earlier run's files
  lab "Villager $ID bound bake and $NAME on the walking and turning clips" $O "clips-bound$TAG render_posed.py" clips-bound$TAG/render.sh $O/render.log "" || { tail -n 5 $O/render.log; exit 4; }
  rx "tar cf - -C $LAB/clips-bound$TAG renders" 2>/dev/null | tar xf - -C $O
  ls $O/renders/body/walking | wc -l ;;
idle)
  # a static NPC (static_rig.py's short rig, no legs to retarget onto): static_idle.py's procedural idle and look clips on the
  # full bake and size NAME (default 16k-face-share-cleaned), rendered as clips does -> bound/idle/renders (IDLE_ARGS: --turn ...)
  NAME=${1:-16k-face-share-cleaned}; O=$BD/idle; mkdir -p $O; src_of "$NAME"
  BIND=$SRC/binding.npz; [ -f $BIND ] || BIND=$BD/binding.npz
  M=$(python3 -c "import json;print(1/json.load(open('$TR'))['scale'])")
  python3 $T/clip_compare.py prep $O/data --metres $M --views front,quarter,side,back-quarter,close,face \
    --bound bound400k=$SRC/splat.npz:$BIND:$SC:$TR --bound body=$BD/sizes/$NAME/splat.npz:$BD/sizes/$NAME/binding.npz:$SC:$TR > $O/prep.log 2>&1 \
    && python3 $T/static_idle.py $O/data $SC $TR ${IDLE_ARGS:-} >> $O/prep.log 2>&1 || { tail -n 5 $O/prep.log; exit 2; }
  cat > $O/render.sh <<EOF
#!/bin/bash
py=$GPY; cd idle-bound || exit 1
for c in idle look; do for t in bound400k body; do \$py ../render_posed.py data/\$t.npz data/\$t-binding.npz data/poses-\$t-\$c.npz renders/\$t/\$c --jpg || exit 1; done; done
ls renders/*/* | wc -l
EOF
  rm -rf "${O:?}/idle-bound"; mkdir -p $O/idle-bound; cp -r $O/data $O/idle-bound/data; cp $O/render.sh $O/idle-bound/; cp $T/render_posed.py $O/render_posed.py
  lab "Static NPC $ID bound bake and $NAME on procedural idle clips" $O "idle-bound render_posed.py" idle-bound/render.sh $O/render.log "" || { tail -n 5 $O/render.log; exit 4; }
  rx "tar cf - -C $LAB/idle-bound renders" 2>/dev/null | tar xf - -C $O
  ls $O/renders/body/idle | wc -l ;;
*) echo "bound_run.sh: unknown step $STEP (score, bound, distil, clean, skirt, hands, sleeves, clips, idle, tidy)" >&2; exit 2 ;;
esac
