#!/bin/bash
# colab-lane.sh SESSION ALIAS:ID... : bake characters on a Colab runtime (Patch 2026-11-25). Builds the env once (setup.sh on the first alias),
# then r1, bound, graft, walk for each ALIAS:ID - in turn when SESSION is "bake" (the L4), side by side otherwise (one big card,
# one alias per character). Times per step in SESSION.tsv; stops the runtime when every character is done.
SES=$1; shift
cd "$(dirname "$0")/../../../../../.."   # the rusty-dagger root
S=authoring/tools/people/splat/static-npc/static-npc
D=${LANE_LOGS:-$HOME/.local/state/colab-lanes}; mkdir -p $D   # SESSION.log, SESSION.tsv (seconds per step), ID-colab.log
# STEPS (default "r1 bound graft walk") picks the steps: STEPS=r1 for a creature. A failed setup stops the lane (no tools on the runtime).
# STEPS="bake bound graft walk" (or bake:STAGE to resume) runs the whole bake here too, Solv3R and round 0 included (Patch 2026-11-28:
# splat training next to the lab's ComfyUI crashed the lab twice; the lab is for ComfyUI only for now, so the lab does the clip alone).
C=local/art/generated/people/dagger-characters
T=$D/$SES.tsv
first=${1%%:*}
t0=$(date +%s); echo "== setup $first $(date +%T)" >> $D/$SES.log
# The first connection creates the runtime and now and then fails with nothing printed (creature-007, 2026-11-27): close the
# ssh master and try again, three tries in all.
for try in 1 2 3; do
  authoring/tools/people/splat/hosts/runpod/setup.sh $first >> $D/$SES.log 2>&1 && break
  echo "!! setup try $try failed (rc $?) $(date +%T)" >> $D/$SES.log; ssh -O exit $first >> $D/$SES.log 2>&1
  [ $try = 3 ] && { echo "!! setup failed" >> $D/$SES.log; for x in "$@"; do echo "!! ${x#*:} failed at setup" >> $D/${x#*:}-colab.log; done; exit 1; }
  timeout 60 tail -f /dev/null
done
printf 'setup\t-\t%s\t%s\n' $t0 $(date +%s) >> $T
one() {
  local host=${1%%:*} id=${1#*:}; local L=$D/$id-colab.log
  local ch=npc-$id rid=$id; case $id in creature-*) ch=$id; rid=c${id#creature-};; esac   # creature-NNN runs as cNNN
  python3 - "$C/$ch/00-brief/npc.json" "$host" <<'PY'
import json, sys; p, h = sys.argv[1:]; c = json.load(open(p)); c['gpu_host'] = h; open(p, 'w').write(json.dumps(c, indent=1) + '\n')
PY
  ( set -e
    GPU=$host CHAR=$ch authoring/tools/people/splat/bake/bake.sh $C/$ch/work $rid-A tools >> $L 2>&1 || exit 1   # a fresh runtime has no tools until r1 stages them
    for st in ${STEPS:-r1 bound graft walk}; do
      a=$(date +%s); echo "== $id $st on $host $(date +%T)" >> $L
      case $st in   # bake[:STAGE]: the whole bake to r1 here, from STAGE (default frames); only VACE goes to the lab's ComfyUI
        bake*) r=${st#bake}; r=${r#:}; $S bake $ch --to r1 ${r:+--resume $r} >> $L 2>&1 || exit 1 ;;
        *) $S $st $ch >> $L 2>&1 || exit 1 ;;
      esac
      printf '%s\t%s\t%s\t%s\n' $id $st $a $(date +%s) >> $T
    done
    echo "== $id done $(date +%T)" >> $L ) || echo "!! $id failed at $(grep '^== ' $L | tail -1)" >> $L
}
if [ "${SES#bake}" != "$SES" ]; then for x in "$@"; do one $x; done
else for x in "$@"; do one $x & sleep 60; done; wait; fi
/home/agent/.local/bin/colab stop -s $SES >> $D/$SES.log 2>&1
ssh -O exit $first >> $D/$SES.log 2>&1   # the multiplexed master outlives the runtime (ControlPersist): the next lane would ride it onto a terminated VM
printf 'stopped\t-\t%s\t%s\n' $t0 $(date +%s) >> $T
for x in "$@"; do id=${x#*:}; ch=npc-$id; case $id in creature-*) ch=$id;; esac; python3 - "$C/$ch/00-brief/npc.json" <<'PY'
import json, sys; p = sys.argv[1]; c = json.load(open(p)); c['gpu_host'] = 'gpu-lab'; open(p, 'w').write(json.dumps(c, indent=1) + '\n')
PY
done
/home/agent/.local/bin/colab usage >> $D/$SES.log 2>&1
echo "== lane $SES done $(date +%T)" >> $D/$SES.log
