#!/bin/bash
# watchdog.sh --name NAME --budget SECONDS [--stall SECONDS] --watch PATH [--watch PATH ...] -- CHAIN COMMAND...
# Runs a chain detached (setsid: its own process group) and watches it. It exits:
#   with the chain's own exit code when the chain ends (0 = done; anything else = the chain failed),
#   with 90 when none of the watched paths (logs, stage files, frame folders: newest mtime of anything under them) has changed for
#      STALL seconds (default 1800) while the chain is still running,
#   with 91 when the chain is still running after BUDGET seconds.
# On 90/91 it STOPS the chain's whole process group (TERM, then KILL after 10 s), so a stalled chain cannot carry on behind a gone
# watchdog (2026-11-15: a stale chain sat in a wait loop for an hour after its watchdog gave up). Run the watchdog itself in the
# background of the agent session, so its exit wakes the agent. Each run has its own files, /tmp/claude-watchdog/NAME-START-PID.
# {pid,rc,out,status} (START = epoch seconds, PID = the watchdog's), so two chains of one name never read each other's exit code;
# NAME.{pid,rc,out,status} are symlinks to the newest run's. Nothing parses log text - completion is the chain process ending, and
# success is its exit code (lead's rule, 2026-11-14). Replace chain scripts by writing a new file and mv, never in place, while a
# chain may still have the old one open (bash reads scripts as it runs).
set -u
NAME=; BUDGET=; STALL=1800; WATCH=()
while [ $# -gt 0 ]; do case $1 in
  --name) NAME=$2; shift ;; --budget) BUDGET=$2; shift ;; --stall) STALL=$2; shift ;; --watch) WATCH+=("$2"); shift ;;
  --) shift; break ;; *) echo "watchdog.sh: unknown $1" >&2; exit 2 ;; esac; shift; done
[ -n "$NAME" ] && [ -n "$BUDGET" ] && [ ${#WATCH[@]} -gt 0 ] && [ $# -gt 0 ] || { echo "usage: see header" >&2; exit 2; }
S=/tmp/claude-watchdog; mkdir -p $S; T0=$(date +%s); RUN=$NAME-$T0-$$
setsid bash -c '"$@"; echo $? > '"$S/$RUN.rc" _ "$@" < /dev/null > $S/$RUN.out 2>&1 &
PID=$!; echo $PID > $S/$RUN.pid; echo "running since $T0 (chain pid $PID, process group $PID)" > $S/$RUN.status
for x in pid rc out status; do ln -sfn $RUN.$x $S/$NAME.$x; done
newest() { local n=0 t; for w in "${WATCH[@]}"; do [ -e "$w" ] || continue
  t=$(find "$w" -printf '%T@\n' 2>/dev/null | sort -n | tail -1 | cut -d. -f1); [ -n "$t" ] && [ "$t" -gt "$n" ] && n=$t; done; echo $n; }
stop() { kill -TERM -- -$PID 2>/dev/null; for i in 1 2 3 4 5 6 7 8 9 10; do kill -0 -- -$PID 2>/dev/null || return; sleep 1; done; kill -KILL -- -$PID 2>/dev/null; }
last=$(newest); [ "$last" -lt "$T0" ] && last=$T0
while true; do
  sleep ${WD_POLL:-30}
  if [ -f $S/$RUN.rc ]; then rc=$(cat $S/$RUN.rc); echo "ended rc $rc after $(( $(date +%s) - T0 )) s" > $S/$RUN.status; exit $rc; fi
  if ! kill -0 $PID 2>/dev/null; then echo "chain process gone without an exit code" > $S/$RUN.status; exit 92; fi
  n=$(newest); [ "$n" -gt "$last" ] && last=$n
  now=$(date +%s)
  if [ $(( now - last )) -ge $STALL ]; then stop; echo "STALL: nothing under the watched paths changed for $(( now - last )) s; chain process group $PID stopped" > $S/$RUN.status; exit 90; fi
  if [ $(( now - T0 )) -ge $BUDGET ]; then stop; echo "BUDGET: chain still running after $(( now - T0 )) s (budget $BUDGET); chain process group $PID stopped" > $S/$RUN.status; exit 91; fi
done
