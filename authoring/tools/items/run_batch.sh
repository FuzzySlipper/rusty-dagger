#!/bin/bash
# Convert one batch of source sprites end to end: Qwen painted reconstruction, Krea retro, back-projection,
# transparent cut-out, compare sheets, then publish the finals to gen-raw/<area>.
#   [AREA=items] run_batch.sh NAME HOST RESOURCE      e.g. AREA=spells run_batch.sh pilot-icons den-m5 m5-comfy
# AREA (items, worn, paper-doll, spells, effects) picks the source root df-raw/AREA (worn reads df-raw/items), the spec directory specs/AREA and the finals gen-raw/AREA
# (default items). NAME picks specs/AREA/PREFIX-NAME.json and PREFIX-NAME-retro.json, PREFIX being
# items-icons for items and the area name otherwise. Qwen and retro both run on HOST
# (den-m5 / m5-comfy or den-nimo / nimo-comfy). SOURCE=dir overrides the source root.
# A frames-* batch composites only the flame onto frame 0 (items only).
set -e
N=$1; H=$2; RES=$3
AREA=${AREA:-items}
if [[ $AREA == items ]]; then PFX=items-icons; else PFX=$AREA; fi
AUTH=$(cd "$(dirname "$0")/../.." && pwd)
AP=${ASSET_PIPELINE:-$HOME/dev/asset-pipeline}
SPEC=$AUTH/specs/$AREA; TOOLS=$AUTH/tools/items; if [[ $AREA == worn ]]; then SRCDIR=items; else SRCDIR=$AREA; fi
RAW=${SOURCE:-$AUTH/df-raw/$SRCDIR}; ST=$AUTH/gen-raw/studies
PAL=${ART_PAL:-$HOME/research/daggerfall-files/ART_PAL.COL}
D=$(date +%Y%m%d)
Q=$ST/$PFX-$N-$D; R=$ST/$PFX-$N-retro-$D; B=$ST/$PFX-$N-bp-$D
cd "$AP"; source legacy/tools/text-to-3d/scripts/lan-env.sh >/dev/null
RUN() { "$HOME/.local/bin/asset-resources" run --host "$H" --resource "$RES" --owner item-art --project rusty-dagger --purpose "$1" -- "${@:2}"; }
python3 $TOOLS/run.py prepare --spec $SPEC/$PFX-$N.json --source $RAW --out $Q --host $H
RUN "$AREA batch $N Qwen" python3 $TOOLS/run.py generate $Q --host $H
python3 $TOOLS/run.py prepare --spec $SPEC/$PFX-$N-retro.json --source $RAW --out $R --upstream $Q --host $H
RUN "$AREA batch $N retro" python3 $TOOLS/run.py generate $R --host $H
python3 $TOOLS/postprocess.py run $R --route qwen-retro --palette "$PAL" --out $B --variants original bp-broad-half
python3 $TOOLS/postprocess.py cutout $R $B --out $B/cutout
python3 $TOOLS/postprocess.py compare --study $R --out $ST/$PFX-$N-compare-$D --title "$AREA batch $N: source, retro, BP 0.5" --column "Retro=$B:original" --column "BP 0.5=$B:bp-broad-half"
if [[ $N == frames-* ]]; then python3 $TOOLS/composite.py $SPEC/$PFX-$N-retro.json $R $B/cutout
else python3 $TOOLS/finalize.py $SPEC/$PFX-$N-retro.json $B/cutout --area $AREA; fi
echo BATCH-$N-DONE
