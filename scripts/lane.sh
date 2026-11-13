#!/usr/bin/env bash
# The build router's lanes for this repository. den-services' build config names each one; `den-tool build run`
# checks the commit out cleanly on a worker and runs it there. Sessions start them with scripts/route.py.
#
#   scripts/lane.sh ruleset        the whole ruleset suite
#   scripts/lane.sh ruleset K/N    part K of the ruleset suite cut by domain folder (PARTS below)
#   scripts/lane.sh import-suite   the whole import suite
#   scripts/lane.sh verify         scripts/verify.sh --import --play
#   scripts/lane.sh data           imports the commit and publishes its data to the store under its importer revision
#
# Each run prepares the checkout by the README's steps (rusty, the pinned pair, npm ci) and links local/inputs to the
# inputs kept beside the router's checkout (BUILD_ROOT/rusty-dagger/kept). The test lanes link local/data and
# local/build/import to kept data, which they pull from the store (scripts/data-sync.sh) at the commit's importer
# revision; when the store has none they stop before testing, since a suite run against other data is a silently
# wrong result. The data lane imports into the checkout itself and publishes; it runs only on PUBLISHER, so that one
# revision has one set of bytes.
set -euo pipefail

usage="usage: scripts/lane.sh ruleset [K/N] | import-suite | verify | data"
lane=${1:-}
part=${2:-}
case $lane in
  ruleset) [[ -z $part || $part =~ ^[1-9][0-9]*/[1-9][0-9]*$ ]] || { echo "$usage" >&2; exit 2; } ;;
  # Transition (#10775): import is import-suite's old name, until the router config drops it.
  import) lane=import-suite; [[ -z $part ]] || { echo "$usage" >&2; exit 2; } ;;
  import-suite|verify|data) [[ -z $part ]] || { echo "$usage" >&2; exit 2; } ;;
  *) echo "$usage" >&2; exit 2 ;;
esac

# Each part takes these folders of tests/WorldRpg.Rulesets.Daggerfall.Tests; the last part takes every test the
# others do not, so a new folder or class is never missed and none runs twice. Balanced by the test time each
# folder took in a whole run (its results file's durations, summed by folder): about a third each.
PARTS=(
  "Quests Exterior Character Scene Dialogue"
  "Magic Content Afflictions Time Developer"
  ""
)

# The one worker that imports for the store. The cinematics' bytes depend on the machine as well as the importer
# revision: libopus encodes the same audio differently on den-agents' Intel and den-k8's AMD with the same ffmpeg
# build, and another ffmpeg version changes it again. The router's os pin sends the data lane here; moving it changes
# only the revisions published after the move.
PUBLISHER=den-k8

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"
# The lane replaces local/ with links to the worker's kept data; in an ordinary checkout that would hide its own.
[[ $(git config --local --get den-build.managed || true) == true ]] ||
  { echo "lane: $root is not a den-build checkout; the lane would replace its local/ with links. Use scripts/route.py." >&2; exit 2; }
export PATH=$HOME/.local/bin:$HOME/.dotnet:$PATH MSBUILDDISABLENODEREUSE=1
commit=$(git rev-parse HEAD)
kept=$(dirname "$root")/kept
inputs_source=${DAGGER_INPUTS_SOURCE:-den-agents:dev/rusty-dagger/local/inputs}
donor_source=${DAGGER_DONOR_SOURCE:-den-agents:/home/research/daggerfall-unity}
# The store, through the NAS mount where the worker has one and over ssh through den-agents where it does not.
store=/mnt/den-nas-backup/data/rusty-dagger/lan
[[ -d $store ]] || store=den-agents:$store
export DAGGER_DATA_STORE=${DAGGER_DATA_STORE:-$store}
out=$DEN_BUILD_OUTPUT_DIR
# What a test lane pulled from the store is kept together, apart from the inputs, so that one place says which
# revision is held. The data lane writes its own, in the checkout, which the router clears before the next run.
imported=$kept/imported
mkdir -p "$kept"/inputs/{arena2,Sound} "$imported"/{data,import} "$out" local/build
ln -sfn "$kept/inputs" local/inputs
if [[ $lane == data ]]; then
  mkdir -p local/data local/build/import
else
  ln -sfn "$imported/data" local/data
  ln -sfn "$imported/import" local/build/import
fi
started=$SECONDS
step() { echo "lane: $* ($((SECONDS - started)) s)"; }
step "run $(basename "$out") of $lane${part:+ part $part} on $(hostname) at $commit"

# The README's setup: rusty from the Engine bootstrap (again when it is older than the pinned pair), the pair, npm ci.
bootstrap() { curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash; }
command -v rusty >/dev/null || bootstrap
rusty install || { bootstrap && rusty install; }
npm ci --no-audit --no-fund
step "pair and UI dependencies ready"

# scripts/data-sync.sh status: this commit's importer revision, the container's, "partial" when the container departs
# from one whole import, and "held" when the store keeps this revision.
status() {
  local report
  report=$(scripts/data-sync.sh status)
  printf '%s %s %s %s\n' "$(sed -n 's/^importer revision of this checkout: //p' <<<"$report")" \
    "$(sed -n 's/^importer revision of the container: \([0-9a-f]*\).*/\1/p' <<<"$report")" \
    "$(grep -q 'not one whole import\|records no departures' <<<"$report" && echo partial || echo whole)" \
    "$(grep -q '^The store holds importer revision' <<<"$report" && echo held || echo missing)"
}
read -r wanted have whole stored < <(status)
[[ $wanted =~ ^[0-9a-f]{64}$ ]] || { echo "lane: cannot compute this commit's importer revision" >&2; exit 1; }
# The inputs: the corpus the import and its suite read, and the score; changed files only.
rsync -rtL --delete "$inputs_source/arena2/" "$kept/inputs/arena2/"
rsync -rtL --delete "$inputs_source/Sound/" "$kept/inputs/Sound/"
if [[ $lane == data ]]; then
  if [[ $stored == held ]]; then
    step "the store already holds importer revision $wanted; nothing to import"
    exit 0
  fi
  [[ $(hostname) == "$PUBLISHER" ]] ||
    { echo "lane: refusing to import on $(hostname): only $PUBLISHER publishes, since another machine encodes the cinematics' audio differently" >&2; exit 2; }
elif [[ $have != "$wanted" || $whole != whole ]]; then
  if [[ $stored != held ]]; then
    echo "lane: refusing to test $commit: the store has no data for its importer revision $wanted, and a suite against" >&2
    echo "lane: other data is a silently wrong result. Import it first: scripts/route.py data $commit" >&2
    exit 3
  fi
  scripts/data-sync.sh pull --import-tree --mirror
  read -r _ have whole _ < <(status)
  [[ $have == "$wanted" && $whole == whole ]] ||
    { echo "lane: the data pulled from $DAGGER_DATA_STORE is at ${have:-no revision} ($whole), not $wanted" >&2; exit 3; }
fi
rsync -rtl --delete "$donor_source/" "$kept/daggerfall-unity/"
export DAGGER_DONOR_ROOT=$kept/daggerfall-unity
step "inputs and data ready for importer revision $wanted"

# A test host may hold at most this much, resident plus swapped: a runaway would otherwise take the worker down
# (one reached 31 GiB resident and 79 GiB of swap on den-agents). The whole ruleset suite peaks near 13 GiB.
total_gib=$(($(awk '/^MemTotal/ { print $2 }' /proc/meminfo) / 1024 / 1024))
ceiling_gib=${DAGGER_TEST_CEILING_GIB:-$((total_gib * 3 / 4 < 32 ? total_gib * 3 / 4 : 32))}
# dotnet-stack, into the kept directory, names the tests a stopped host was running.
[[ -x $kept/tools/dotnet-stack ]] || dotnet tool install --tool-path "$kept/tools" dotnet-stack --version 10.0.745401
step "test hosts are stopped past $ceiling_gib GiB"

# Runs a step and says how long it took and the most memory the lane's processes held together (the router starts
# the lane as its own process group), sampled every two seconds. A test host past the ceiling has its managed stacks
# written beside the run's outputs and is stopped, and the step fails naming the tests on those stacks.
measured() {
  local name=$1 began=$SECONDS peak=$out/$1.peak breach=$out/$1.ceiling sampler status=0
  shift
  (
    group=$(ps -o pgid= -p $BASHPID | tr -d ' ') most=0
    while sleep 2; do
      now=$(ps -eo pgid=,rss= | awk -v group="$group" '$1 == group { sum += $2 } END { print sum + 0 }')
      ((now > most)) && most=$now && echo "$most" >"$peak"
      for host in $(ps -eo pid=,pgid=,args= | awk -v group="$group" '$2 == group && /exec --runtimeconfig .*Tests\.runtimeconfig\.json/ { print $1 }'); do
        held=$(awk '/^(VmRSS|VmSwap):/ { sum += $2 } END { print int(sum / 1024 / 1024) }' "/proc/$host/status" 2>/dev/null || echo 0)
        ((held > ceiling_gib)) || continue
        timeout 120 "$kept/tools/dotnet-stack" report -p "$host" >"$breach.stacks" 2>&1 || true
        kill -KILL "$host" 2>/dev/null || true
        # Each thread's outermost frame in a test assembly is the test method it runs.
        running=$(awk '/^ *Thread / { if (test) print test; test = "" } /Tests![^ ]/ { sub(/.*Tests!/, ""); sub(/\(.*/, ""); test = $0 } END { if (test) print test }' \
          "$breach.stacks" | sort -u | paste -sd ' ')
        echo "test host $host held $held GiB, past the $ceiling_gib GiB ceiling; running: ${running:-(no test frame on its stacks)}" >"$breach"
      done
    done
  ) &
  sampler=$!
  "$@" || status=$?
  kill "$sampler"
  wait "$sampler" 2>/dev/null || true
  echo "lane: $name took $((SECONDS - began)) s, peak memory $(($(cat "$peak" 2>/dev/null || echo 0) / 1024)) MiB"
  if [[ -f $breach ]]; then
    echo "lane: $name stopped: $(cat "$breach"); stacks $(hostname):$breach.stacks" >&2
    return 4
  fi
  return "$status"
}
case $lane in
  ruleset)
    project=tests/WorldRpg.Rulesets.Daggerfall.Tests/WorldRpg.Rulesets.Daggerfall.Tests.csproj
    # The staged product manifest is what the suite's [StagedProductFact]s read; only staging the Host writes it.
    measured stage rusty build --project src/WorldRpg.Host/WorldRpg.Host.csproj
    measured build dotnet build "$project"
    filter=()
    if [[ -n $part ]]; then
      [[ ${part#*/} == "${#PARTS[@]}" ]] ||
        { echo "lane: part $part names ${part#*/} parts, scripts/lane.sh has ${#PARTS[@]}; a part would be missed or doubled" >&2; exit 2; }
      filter=(--filter "$(python3 - "${part%/*}" "${PARTS[@]}" <<'EOF'
import pathlib, re, sys, hashlib
# A top-level type of the suite's file-scoped namespace, declared at the start of a line.
declaration = re.compile(r'^(?:[a-z]+\s+)*(?:class|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)\b(?!\s*<)', re.M)
tests = pathlib.Path('tests/WorldRpg.Rulesets.Daggerfall.Tests')
number, parts = int(sys.argv[1]), [p.split() for p in sys.argv[2:]]
owner = {}
for folder in sorted(p.name for p in tests.iterdir() if p.is_dir()):
    for source in sorted((tests / folder).rglob('*.cs')):
        for name in declaration.findall(source.read_text(encoding='utf-8-sig')):
            owner.setdefault(name, folder)
taken = [sorted(name for name, folder in owner.items() if folder in part) for part in parts[:-1]]
prefix = 'FullyQualifiedName{}WorldRpg.Rulesets.Daggerfall.Tests.{}{}'
digest = hashlib.sha256(repr(taken).encode()).hexdigest()[:12]
if number < len(parts):
    names = taken[number - 1]
    print(f'lane: part {number}/{len(parts)} runs {len(names)} classes of {", ".join(parts[number - 1])} (partition {digest})', file=sys.stderr)
    print('|'.join(prefix.format('~', name, end) for name in names for end in '.+'))
else:
    others = sum(len(names) for names in taken)
    print(f'lane: part {number}/{len(parts)} runs every test outside the {others} classes of the other parts (partition {digest})', file=sys.stderr)
    print('&'.join(prefix.format('!~', name, end) for names in taken for name in names for end in '.+'))
EOF
)")
    fi
    measured test dotnet test "$project" --no-build "${filter[@]}" --logger "trx;LogFileName=$out/ruleset.trx"
    echo "lane: results $(hostname):$out/ruleset.trx"
    ;;
  import-suite)
    measured test dotnet test tests/Daggerfall.Import.Tests/Daggerfall.Import.Tests.csproj --logger "trx;LogFileName=$out/import.trx"
    echo "lane: results $(hostname):$out/import.trx"
    ;;
  verify)
    # The play smoke's headless Chromium, from Playwright into the agent's home (the README's step).
    npx -y playwright@1.62.1 install chromium
    RUSTY_CHROMIUM_PATH=$(ls -d "$HOME"/.cache/ms-playwright/chromium-*/chrome-linux64/chrome | sort -V | tail -n 1)
    export RUSTY_CHROMIUM_PATH
    measured verify scripts/verify.sh --import --play
    ;;
  data)
    measured import dotnet run --project src/Daggerfall.Import.Tool -- import --music local/inputs/Sound
    # A stale terrain bake is rebaked and rewritten in the checkout; the change that made it stale commits it.
    relief=src/Daggerfall.Import.Tool/inputs/relief
    if [[ -n $(git status --porcelain -- "$relief") ]]; then
      mkdir -p "$out/relief" && cp -a "$relief/." "$out/relief/"
      echo "lane: the import rebaked the terrain; commit $(hostname):$out/relief/ as $relief/ with this change"
    fi
    measured publish scripts/data-sync.sh publish
    ;;
esac
step "$lane${part:+ part $part} passed at $commit"
