#!/bin/bash
# pipeline.sh RUN ID HALO [FROM [TO]]: run a character's bake stages in order (frames mattes cams fuse r0 rings refs vace match r1data
# r1 soles final), from FROM to TO (default frames..r0, the first checkpoint). Stops at the first failing stage. BAKE, DRY, LOCK_WAIT,
# OPEN_HOLES, MATCH pass through to bake.sh.
H0=$(cd "$(dirname "$0")" && pwd); RUN=$(realpath "$1"); ID=$2; H=$3; FROM=${4:-frames}; TO=${5:-r0}
S=(frames mattes cams fuse r0 rings refs vace match r1data r1 soles final); on=0
for st in "${S[@]}"; do [ $st = $FROM ] && on=1; [ $on = 1 ] || continue
  echo "== $ID $st $(date +%T)"; $H0/bake.sh "$RUN" $ID $st $H || { echo "!! $ID $st failed rc $?"; exit 1; }
  [ $st = $TO ] && break; done
echo "== $ID done to $TO $(date +%T)"
