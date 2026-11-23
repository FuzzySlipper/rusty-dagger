#!/bin/bash
# guided_clip.sh RUN ID STEP SIZE [NAME]: a character's stick-guided orbit clip (the route on the wiki, rusty-dagger/splat-characters).
# RUN is the run folder; ID is the character (local/art/generated/people/dagger-characters/ID: its scaffold in 02-guide/scaffold/scaffold.glb, the cut
# paintings in 01-concept/wip/cut/{front,back}-cut.png, the clip prompt in 02-guide/wip/orbit-guide/prompt.txt); the clip goes to
# RUN/ID/A/guided/NAME (default stick-SIZE). CHAR=npc-NNN names the character folder when it differs from ID (static-npc: the run is
# NNN, the folder npc-NNN). With no back painting (023) the keyframes
# and REF2VA take the front alone.
#   guide  the stick figure from the scaffold's joints (stick_fit.py; FIT=joints.json gives them instead), its lit-clay (L11 key and fills) and depth orbits at eye level
#          (27 degree lens, 73 frames) rendered with Blender on den-m5's CPU, and the keyframes at SIZE
#   clip   H3 REF2VA on the GPU lab: the lit stick video as <Video 1>, depth control 0.5 from the same camera, the two keyframes as
#          references, turbo 4 steps, nothing pinned. A size above 1088 px can run the 5090 out of memory, which takes ComfyUI down
#          with every job in its queue (2176 px does; 1920 px runs with none to spare): it is only submitted when the queue is empty
#          (waits up to WAIT_EMPTY seconds, default 1800, then exits 7). Lab lock, asset-resources run, the planned restart rule, a
#          VRAM sample every 10 s in RUN/ID/A/guided/vram-NAME.log.
#          HALO=den-m5 or den-nimo runs the clip on that Strix Halo's ComfyUI instead (no lab lock or queue rule; DEADLINE seconds, default 12 h).
#          On a Halo, H3_UNET (model file), LORA (empty = none) and STEPS swap in another H3 fine-tune; times.txt records them.
# Exit code: the first failing step's.
set -u
REPO=$(cd "$(dirname "$0")/../../../../.." && pwd); cd "$REPO"
R=$(realpath --relative-to="$REPO" "$1"); ID=$2; STEP=$3; SZ=$4; N=${5:-stick-$SZ}
CH=local/art/generated/people/dagger-characters/${CHAR:-$ID}; G=$CH/02-guide/wip/orbit-guide; C=$CH/01-concept; T=authoring/tools/people/splat/scaffold; H0=authoring/tools/people/splat/bake
case $STEP in
guide)
  mkdir -p $G/$SZ
  if [ -n "${FIT:-}" ]; then cp "$FIT" $G/fit.json   # FIT=joints.json: stick joints written by hand (a static NPC with no rig: 021)
  else python3 $T/stick_fit.py $CH/02-guide/scaffold/scaffold.glb $G/fit.json > /dev/null || exit 2; fi
  St=$R/halo-guide-$SZ; mkdir -p $St; cp $T/blender_path.py $G/fit.json $St/
  P="--path eyelevel --fov 27 --size $SZ --frames 73"
  asset-resources run --host den-m5 --resource m5-asset-cpu --owner claude --purpose "Blender stick-figure orbit guide for villager $ID at $SZ px" -- \
    authoring/tools/people/splat/hosts/strix-cpu/run.sh den-m5 $St --fresh -- bash -c "blender -b --factory-startup -t 0 --python blender_path.py -- fit.json out/stick-L11 $P --pass shaded --rig keyfill --key 2.1 --fill 0.5 --back 0.45 --dome 0.08 && blender -b --factory-startup -t 0 --python blender_path.py -- fit.json out/stick $P --pass depth" 2>&1 | grep -E "rendered|Error|Traceback" || true
  [ -d $St/out/stick/depth ] || { echo "guided_clip.sh: no guide output" >&2; exit 3; }
  cp -r $St/out/. $G/$SZ/
  python3 - $G/$SZ/stick-L11 <<'PY'
import glob, os, sys, numpy as np
from PIL import Image
d = sys.argv[1]; os.makedirs(f'{d}/shaded-flat', exist_ok=True)
for f in sorted(glob.glob(f'{d}/shaded/*.png')):
    a = np.asarray(Image.open(f).convert('RGBA')).astype(float); al = a[..., 3:] / 255
    Image.fromarray(np.rint(a[..., :3] * al + 128 * (1 - al)).astype(np.uint8)).save(f'{d}/shaded-flat/{os.path.basename(f)}')
PY
  for v in "$G/$SZ/stick/depth $G/$SZ/stick/depth.mp4" "$G/$SZ/stick-L11/shaded-flat $G/$SZ/stick-L11/shaded.mp4"; do set -- $v; ffmpeg -loglevel error -y -framerate 24 -i $1/%03d.png -c:v libx264 -qp 0 -pix_fmt yuv444p $2; done
  Hh=$(python3 -c "print(round(1898/2176*$SZ))"); Fl=$(python3 -c "print(round(2039/2176*$SZ))")
  python3 authoring/tools/people/splat/mesh-splat/flood_cut.py keyframes $C/wip/keys-$SZ front=$C/wip/cut/front-cut.png $([ -f $C/wip/cut/back-cut.png ] && echo back=$C/wip/cut/back-cut.png) --size $SZ --height $Hh --floor $Fl --equal-height > /dev/null || exit 4
  echo "guide $SZ: $(ffprobe -v error -count_frames -select_streams v -show_entries stream=nb_read_frames,width -of csv=p=0 $G/$SZ/stick-L11/shaded.mp4) $(ffprobe -v error -count_frames -select_streams v -show_entries stream=nb_read_frames,width -of csv=p=0 $G/$SZ/stick/depth.mp4)" ;;
clip)
  [ -f $G/prompt.txt ] || { echo "guided_clip.sh: no $G/prompt.txt" >&2; exit 2; }
  if [ -n "${HALO:-}" ]; then   # HALO=den-m5|den-nimo: the same job on a Strix Halo's ComfyUI (slower; int8 text encoder, as NVFP4 needs Blackwell; no lab lock)
    ip=$(case $HALO in den-m5) echo 192.168.1.24 ;; den-nimo) echo 192.168.1.23 ;; *) exit 2 ;; esac); res=$( [ $HALO = den-m5 ] && echo m5-comfy || echo nimo-comfy )
    K=$C/wip/keys-$SZ; SEED=${SEED:-$((610000000 + 10#$(echo ${ID%%-*} | tr -dc 0-9) * 1000))}; mkdir -p $R/$ID/A/guided
    LORA=${LORA-minimax_h3_ref2v_turbo_4step_v0.1_comfyui_bf16.safetensors}; LORA_ARG=(); [ -n "$LORA" ] && LORA_ARG=(--lora "$LORA")   # LORA= (empty) for a fine-tune with no LoRA
    echo "guided $N start $(date +%s) size $SZ seed $SEED host $HALO unet ${H3_UNET:-default} lora ${LORA:-none} steps ${STEPS:-4}" >> $R/times.txt; rc=0
    COMFY_URL=http://$ip:8188 H3_CLIP=${H3_CLIP:-qwen3vl_32b_minimax_h3_int8_convrot.safetensors} \
    asset-resources run --host $HALO --resource $res --owner claude --purpose "Villager $ID stick-guided orbit $N ($SZ px, REF2VA) on $HALO" -- \
      python3 $T/ref2va_orbit.py $R/$ID/A/guided/$N --image $K/front.png $([ -f $K/back.png ] && echo --image $K/back.png) --prompt-file $G/prompt.txt --video $G/$SZ/stick-L11/shaded.mp4 \
        --size $SZ --frames 73 --seed $SEED --steps ${STEPS:-4} ${LORA_ARG[@]+"${LORA_ARG[@]}"} --sampler res_multistep --scheduler simple \
        --control depth:$G/$SZ/stick/depth.mp4:0.5 --free-after-encode --deadline ${DEADLINE:-43200} || rc=$?
    echo "guided $N end $(date +%s) rc $rc host $HALO" >> $R/times.txt; exit $rc
  fi
  export R ID N SZ G H0 T K=$C/wip/keys-$SZ SEED=${SEED:-$((610000000 + 10#$(echo ${ID%%-*} | tr -dc 0-9) * 1000))}
  mkdir -p $R/$ID/A/guided; echo "guided $N waiting-for-lock $(date +%s)" >> $R/times.txt
  flock -w 7200 /tmp/gpu-lab-claude.lock bash -c '
    if [ $SZ -gt 1088 ]; then t0=$(date +%s); while [ "$(python3 $H0/queue.py)" != "running 0 pending 0" ]; do
      [ $(( $(date +%s) - t0 )) -gt ${WAIT_EMPTY:-1800} ] && { echo "guided $N not submitted: queue $(python3 $H0/queue.py) $(date +%s)" >> $R/times.txt; exit 7; }; sleep 60; done; fi
    $H0/lab-comfy-restart.sh ${ASSET_PIPELINE:-$HOME/dev/asset-pipeline}/live-evidence/villagers-batch1-20261113-r01/stab-vram.log --in-lock --if-rss-gb 20 >> $R/lab-restarts.log 2>&1
    $H0/vram.sh $R/$ID/A/guided/vram-$N.log & mon=$!; trap "pkill -P $mon 2>/dev/null; kill $mon 2>/dev/null" EXIT
    echo "guided $N start $(date +%s) size $SZ seed $SEED" >> $R/times.txt; rc=0
    asset-resources run --host gpu-lab --resource lab-comfy --owner claude --purpose "Villager $ID stick-guided orbit $N ($SZ px, REF2VA)" -- \
      python3 $T/ref2va_orbit.py $R/$ID/A/guided/$N --image $K/front.png $([ -f $K/back.png ] && echo --image $K/back.png) --prompt-file $G/prompt.txt --video $G/$SZ/stick-L11/shaded.mp4 \
        --size $SZ --frames 73 --seed $SEED --steps 4 --lora minimax_h3_ref2v_turbo_4step_v0.1_comfyui_bf16.safetensors --sampler res_multistep --scheduler simple \
        --control depth:$G/$SZ/stick/depth.mp4:0.5 --free-after-encode || rc=$?
    echo "guided $N end $(date +%s) rc $rc queue $(python3 $H0/queue.py) peak-vram-MiB $(sort -k2 -n $R/$ID/A/guided/vram-$N.log | tail -1 | cut -d" " -f2)" >> $R/times.txt; exit $rc' ;;
*) echo "guided_clip.sh: unknown step $STEP (guide, clip)" >&2; exit 2 ;;
esac
