#!/bin/bash
# lab-comfy-restart.sh LOG [--in-lock] [--allow-own] [--if-rss-gb N]: restart the GPU lab's ComfyUI between our jobs, to clear held
# memory (Patch, 2026-11-14: "You can restart comfy as needed and since it seems to hold onto memory it might not be a bad idea when
# doing long running comfy work to just plan on restarting it every so often to clean out any leaks or holds on memory.").
# Lab ComfyUI only - never a Halo's - and only through the supported action (`gpu-lab comfy restart`, Supervisor; never the
# ComfyUI-Manager reboot, never the container). Rules (lead, same day): only while holding our lab lock (/tmp/gpu-lab-claude.lock;
# without --in-lock this script takes it with flock -w LOCK_WAIT, failing loudly), and only when ComfyUI's queue has no running or
# pending job (--allow-own also accepts jobs submitted by our own clients, whose client_id is one of ours - they are killed).
# Before and after: VRAM, lab RAM and ComfyUI's RSS appended to LOG. --if-rss-gb N restarts only when ComfyUI's RSS is above N GB.
# Exit 0 restarted (or not needed), 5 queue busy with someone else's job, 6 ComfyUI did not come back within 5 minutes, 75 no lock.
set -u
LOG=$1; shift; IN=; OWN=; RSS_MIN=
while [ $# -gt 0 ]; do case $1 in --in-lock) IN=1 ;; --allow-own) OWN=1 ;; --if-rss-gb) RSS_MIN=$2; shift ;; esac; shift; done
L=~/server-access/gpu-lab/connect; COMFY=${COMFY_URL:-http://192.168.1.20:8188}
state() {
  local v r rss
  v=$($L "gpu-lab exec nvidia-smi --query-gpu=memory.used,utilization.gpu --format=csv,noheader" 2>/dev/null | grep -v Container | tr -d '\n')
  r=$($L "gpu-lab exec bash -c 'free -g | awk \"/Mem/{print \\\$3\\\"/\\\"\\\$2\\\" GiB used, \\\"\\\$7\\\" available\\\"}\"'" 2>/dev/null | grep -v Container | tr -d '\n')
  rss=$($L "gpu-lab exec bash -c 'ps -C python -o rss=,args= | grep main.py | awk \"{s+=\\\$1} END {printf \\\"%.1f\\\", s/1048576}\"'" 2>/dev/null | grep -v Container | tr -d '\n')
  echo "vram ${v:-?} | ram ${r:-?} | comfy rss ${rss:-?} GB"
}
act() {
  local q
  q=$(curl -s -m 20 $COMFY/queue) || { echo "$(date +%s) restart: ComfyUI not answering - not restarting" | tee -a "$LOG"; return 6; }
  local verdict
  verdict=$(python3 -c "
import json,sys
q=json.loads(sys.argv[1]); ours=('h3-fl2va-anchor','h3-orbit-dataset','r2v-orbit','qe-','rat-vace','vace')
items=q['queue_running']+q['queue_pending']
own=[i for i in items if any(str((i[3] if len(i)>3 else {}).get('client_id','')).startswith(o) for o in ours)]
print('empty' if not items else ('own' if len(own)==len(items) else 'other'), len(items))" "$q")
  local before; before=$(state)
  case $verdict in
    other*) echo "$(date +%s) restart: queue holds someone else's job ($verdict) - not restarting | $before" | tee -a "$LOG"; return 5 ;;
    own*) [ -n "$OWN" ] || { echo "$(date +%s) restart: queue holds our own job ($verdict), no --allow-own - not restarting | $before" | tee -a "$LOG"; return 5; } ;;
  esac
  if [ -n "$RSS_MIN" ]; then
    local rss; rss=$(echo "$before" | sed -n 's/.*comfy rss \([0-9.]*\) GB.*/\1/p')
    python3 -c "import sys; sys.exit(0 if float('${rss:-0}' or 0) > $RSS_MIN else 1)" || { echo "$(date +%s) restart: not needed (comfy rss ${rss:-?} GB <= $RSS_MIN) | $before" | tee -a "$LOG"; return 0; }
  fi
  echo "$(date +%s) restart: before | $before" | tee -a "$LOG"
  $L "gpu-lab comfy restart" 2>&1 | grep -v Container | tail -n 2
  local t0; t0=$(date +%s)
  until curl -s -m 10 $COMFY/queue > /dev/null && curl -s -m 30 $COMFY/object_info/MiniMaxH3ImageToVideo | grep -q MiniMaxH3ImageToVideo; do
    [ $(( $(date +%s) - t0 )) -gt 300 ] && { echo "$(date +%s) restart: ComfyUI not back after 300 s" | tee -a "$LOG"; return 6; }
    sleep 5
  done
  echo "$(date +%s) restart: after $(( $(date +%s) - t0 )) s | $(state)" | tee -a "$LOG"
}
if [ -n "$IN" ]; then act; exit $?; fi
export -f act state; export LOG OWN RSS_MIN L COMFY
flock -w ${LOCK_WAIT:-7200} /tmp/gpu-lab-claude.lock bash -c act
rc=$?; [ $rc = 1 ] && { echo "lab-comfy-restart.sh: no lab lock after ${LOCK_WAIT:-7200} s" >&2; exit 75; }
exit $rc
