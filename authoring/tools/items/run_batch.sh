#!/bin/bash
# Convert one batch of world sprites end to end: Qwen painted reconstruction, Krea retro, back-projection,
# transparent cut-out, compare sheets, then publish the finals to gen-raw/items.
#   run_batch.sh NAME HOST RESOURCE      e.g. run_batch.sh batch-f1 den-m5 m5-comfy
# SOURCE=dir overrides the export root (e.g. gen-raw/source-edits for a patched sprite).
# NAME picks specs/items/items-icons-NAME.json and -NAME-retro.json. Qwen and retro both run on HOST
# (den-m5 / m5-comfy or den-nimo / nimo-comfy). A frames-* batch composites only the flame onto frame 0.
set -e
N=$1; H=$2; RES=$3
AUTH=$(cd "$(dirname "$0")/../.." && pwd)
AP=${ASSET_PIPELINE:-$HOME/dev/asset-pipeline}
SPEC=$AUTH/specs/items; TOOLS=$AUTH/tools/items; RAW=${SOURCE:-$AUTH/df-raw/items}; ST=$AUTH/gen-raw/studies
PAL=${ART_PAL:-$HOME/research/daggerfall-files/ART_PAL.COL}
D=$(date +%Y%m%d)
Q=$ST/items-icons-$N-$D; R=$ST/items-icons-$N-retro-$D; B=$ST/items-icons-$N-bp-$D
cd "$AP"; source legacy/tools/text-to-3d/scripts/lan-env.sh >/dev/null
RUN() { "$HOME/.local/bin/asset-resources" run --host "$H" --resource "$RES" --owner item-art --project rusty-dagger --purpose "$1" -- "${@:2}"; }
python3 $TOOLS/run.py prepare --spec $SPEC/items-icons-$N.json --source $RAW --out $Q
RUN "Icon batch $N Qwen" python3 $TOOLS/run.py generate $Q --host $H
python3 $TOOLS/run.py prepare --spec $SPEC/items-icons-$N-retro.json --source $RAW --out $R --upstream $Q
RUN "Icon batch $N retro" python3 $TOOLS/run.py generate $R --host $H
python3 $TOOLS/postprocess.py run $R --route qwen-retro --palette "$PAL" --out $B --variants original bp-broad-half
python3 $TOOLS/postprocess.py cutout $R $B --out $B/cutout
python3 $TOOLS/postprocess.py compare --study $R --out $ST/items-icons-$N-compare-$D --title "Batch $N: source, retro, BP 0.5" --column "Retro=$B:original" --column "BP 0.5=$B:bp-broad-half"
if [[ $N == frames-* ]]; then python3 $TOOLS/composite.py $SPEC/items-icons-$N-retro.json $R $B/cutout
else python3 $TOOLS/finalize.py $SPEC/items-icons-$N-retro.json $B/cutout; fi
echo BATCH-$N-DONE
