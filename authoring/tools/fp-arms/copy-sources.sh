#!/usr/bin/env bash
# Copies the old Rift Riders game's first-person melee art into Dagger's ignored local/inputs/fp-melee/ once
# (authoring/tools/fp-arms/README.md): Ironbelly's MedievalFPArms meshes, textures and unarmed clips, and the FPP_* melee clip
# folders. They are Unity Asset Store purchases: never commit them or anything converted from them.
#
#   authoring/tools/fp-arms/copy-sources.sh ../rusty-riders/old-game/Assets/MoreArt/FpMelee
set -euo pipefail
root=$(cd "$(dirname "$0")/../../.." && pwd)
from=${1:?usage: copy-sources.sh FPMELEE_DIR}
out=$root/local/inputs/fp-melee
[[ -d $from/MedievalFPArms/Meshes && -f $from/FPP_Dag_Idle.fbx ]] || { echo "$from is not the old game's FpMelee folder" >&2; exit 1; }
mkdir -p "$out"
rsync -a --exclude '*.meta' --exclude 'Thumbs.db' \
  --include 'MedievalFPArms/' --include 'MedievalFPArms/Meshes/***' --include 'MedievalFPArms/Textures/***' \
  --include 'MedievalFPArms/Animations/***' --include 'MedievalFPArms/IronbellyFPArms_ReadMe.txt' \
  --include 'FPP_*' --include 'FPP_*/***' --exclude '*' \
  "$from/" "$out/"
echo "COPIED $(du -sh "$out" | cut -f1) into local/inputs/fp-melee"
