#!/usr/bin/env bash
# Routine product verification. NativeAOT is a separate fidelity target and is
# opt-in through --aot, because the ordinary loop does not need it.
set -euo pipefail

aot=false
play=false
record=false
import=false
for argument in "$@"; do
  case "$argument" in
    --aot) aot=true ;;
    --play) play=true ;;
    --record) record=true ;;
    --import) import=true ;;
    -h|--help)
      echo "usage: scripts/verify.sh [--aot] [--play] [--record] [--import]"
      echo "  no arguments  pinned pair install, restore, build, UI tests, every test project, CoreCLR staging"
      echo "  --aot         also run the NativeAOT fidelity publish"
      echo "  --play        also start the product on its runtime and press Begin until it reaches ordinary play"
      echo "  --record      on success, attach the run's summary to HEAD as a git note (refs/notes/verify) and push it"
      echo "  --import      run the import suite even when nothing it runs changed since its last green run here"
      exit 0
      ;;
    *)
      echo "Unknown argument: $argument (supported: --aot, --play, --record, --import)" >&2
      exit 2
      ;;
  esac
done

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

# The corpus and donor suites skip, with a stated reason, when their data is absent, so a green run
# means different things with and without them. Say up front which ones this run has.
arena2_root=${DAGGER_ARENA2:-local/arena2}
donor_root=${DAGGER_DONOR_ROOT:-/home/research/daggerfall-unity}
corpora="Arena2 corpus $arena2_root: $([[ -d "$arena2_root" ]] && echo present || echo absent); donor checkout $donor_root: $([[ -d "$donor_root" ]] && echo present || echo absent)"
echo "corpora: $corpora"

# The Arena2-derived content is not tracked: the import command packs it into local/daggerfall-data.rpak,
# with its import records in local/import/records (and the score in local/music.rpak). Without it the product
# still stages and states why it cannot be played, the suites that read it skip with the reason printed, and
# --play, which plays it, is refused.
import_command="dotnet run --project src/Daggerfall.Import.Tool -- import --music local/Sound"
generated_missing=()
for path in local/daggerfall-data.rpak local/import/records; do
  [[ -e "$path" ]] || generated_missing+=("$path")
done
if [[ ${#generated_missing[@]} -eq 0 ]]; then
  generated="present"
else
  generated="absent (${generated_missing[*]}); run \`$import_command\` with local/arena2 supplied"
fi
echo "generated content: $generated"
skip() { echo "skipped $1: the data container is absent; run \`$import_command\`"; }

# A green run is only worth keeping if it names what it ran on. The record is a git note on the
# exact commit, so it cannot drift from the tree it describes; a run over uncommitted changes would
# attach a result to code that is not in that commit, so recording refuses a dirty tree.
if [[ "$record" == true ]]; then
  if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    echo "--record needs a clean tree: the note would describe changes that are not in HEAD." >&2
    exit 2
  fi
  run_log=$(mktemp)
  trap 'rm -f "$run_log"' EXIT
  gate_arguments=()
  for argument in "$@"; do [[ "$argument" == --record ]] || gate_arguments+=("$argument"); done
  set +e
  "$0" "${gate_arguments[@]}" 2>&1 | tee "$run_log"
  status=${PIPESTATUS[0]}
  set -e
  [[ "$status" -eq 0 ]] || exit "$status"
  summary=$(
    echo "scripts/verify.sh ${gate_arguments[*]}"
    echo "corpora: $corpora"
    echo "generated content: $generated"
    grep -E '^# (pass|fail|skipped) |^(Passed|Failed)!|^play-smoke .* passed:|^Verified Engine pair|^skipped ' "$run_log" \
      | sed -E 's/, Duration: [^-]*- / /'
  )
  git notes --ref=verify add -f -m "$summary" HEAD
  git push -q origin refs/notes/verify
  echo "Recorded the run on $(git rev-parse --short HEAD) in refs/notes/verify."
  exit 0
fi

if [[ ${#generated_missing[@]} -ne 0 ]] && [[ "$play" == true ]]; then
  echo "--play plays the product, which needs the data container: run \`$import_command\` first." >&2
  exit 2
fi

npm ci

# Installs the pinned pair when it is missing (a no-op offline once installed),
# then restores and stages the host through rusty, which supplies the pair's
# SDK feed; the later dotnet commands find the restored package.
rusty install
rusty build --project src/WorldRpg.Host/WorldRpg.Host.csproj
pair_version=$(sed -n 's|.*<RustyEnginePackageVersion>\([^<]*\)</RustyEnginePackageVersion>.*|\1|p' Directory.Build.props)
dotnet restore tests/WorldRpg.Architecture.Tests/WorldRpg.Architecture.Tests.csproj
dotnet build src/WorldRpg.Host/WorldRpg.Host.csproj --configuration Release --no-restore
# The UI tests type-check the DOM against the Engine's UI types, which the Host build copies into
# src/ui/engine-types; on a fresh checkout they exist only from here on.
node --test tests/WorldRpg.Ui.Tests/*.test.mjs
dotnet build src/WorldRpg.SpriteWorkbench/WorldRpg.SpriteWorkbench.csproj --configuration Release

# The importer's tool is a product of its own and nothing else in this script compiles it, so a
# change to the import API could leave it broken while every test and build here stayed green. That
# happened: a new field in an import record was passed by the tests and missed by the tool, and the
# error sat unnoticed until someone ran the tool by hand.
dotnet build src/Daggerfall.Import.Tool/Daggerfall.Import.Tool.csproj --configuration Release
dotnet test tests/WorldRpg.Architecture.Tests/WorldRpg.Architecture.Tests.csproj --no-restore

# The behavior suites, because compiling projects is not the same as exercising them: a pin move that
# changed an engine interface broke the ruleset suite's content fake while every build here stayed
# green, and a field added to an import record broke the tool the same way. A verification script that
# only compiles reports on a tree that no longer runs.
#
# The import suite tests the importer against the corpus, so its result changes only when something it runs
# changes: the importer revision's inputs, the suite's own sources and the Engine pin, the corpus, the donor
# checkout, or the imported data it reads. A green run leaves a stamp of those on this machine, and a later run
# that finds the same stamp skips its 15 to 20 minutes; --import runs it anyway. The ruleset suite always runs,
# and its contract tests read what the importer wrote.
import_stamp=local/import/records/import-suite.stamp
import_inputs() {
  find src/Daggerfall.Import src/Daggerfall.Import.Tool src/WorldRpg.SpriteAuthoring data content/worldrpg/media/ui/authored \
      tests/Daggerfall.Import.Tests tests/Shared tests/SharedEngine \( -name bin -o -name obj \) -prune -o -type f -print \
    | LC_ALL=C sort | xargs sha256sum
  sha256sum src/ImporterRevision.targets tests/Directory.Build.props Directory.Build.props
  # The corpus and the data container by name, size and modification time; the donor by its commit.
  for path in "$arena2_root"/* local/daggerfall-data.rpak; do [[ -e "$path" ]] && stat -c '%n %s %Y' "$path"; done
  git -C "$donor_root" rev-parse HEAD 2>/dev/null || true
}
import_key=$(import_inputs | sha256sum | cut -d' ' -f1)
if [[ "$import" == false && -f "$import_stamp" && "$(cat "$import_stamp")" == "$import_key" ]]; then
  dotnet build tests/Daggerfall.Import.Tests/Daggerfall.Import.Tests.csproj
  echo "skipped suite tests/Daggerfall.Import.Tests: nothing it runs changed since its last green run on this machine (stamp ${import_key:0:12}); --import runs it"
else
  rm -f "$import_stamp"
  dotnet test tests/Daggerfall.Import.Tests/Daggerfall.Import.Tests.csproj
  if [[ -d local/import/records ]]; then echo "$import_key" > "$import_stamp"; fi
fi
if [[ ${#generated_missing[@]} -eq 0 ]]; then
  dotnet test tests/WorldRpg.Rulesets.Daggerfall.Tests/WorldRpg.Rulesets.Daggerfall.Tests.csproj
else
  skip "suite tests/WorldRpg.Rulesets.Daggerfall.Tests (its facts read the data container's site closures and payloads)"
fi
dotnet test tests/WorldRpg.Rulesets.Canary.Tests/WorldRpg.Rulesets.Canary.Tests.csproj
dotnet test tests/WorldRpg.Kit.Tests/WorldRpg.Kit.Tests.csproj
dotnet test tests/WorldRpg.Host.Tests/WorldRpg.Host.Tests.csproj

# The workbench's suite belongs here for the same reason: its fake lagged the Engine contract until the
# project no longer compiled, and since nothing ran the project the rot was invisible. It is a product
# in this repository, so its suite is part of the tree that must run.
dotnet test tests/WorldRpg.SpriteWorkbench.Tests/WorldRpg.SpriteWorkbench.Tests.csproj
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:StageRustyEngineCoreClrProduct -p:Configuration=Release

# Every suite above proves its Engine-facing paths against fakes that do not enforce the Engine's
# ownership rules; a render resource released under a live owner passed them all while the real
# runtime refused it. The play stage runs the product itself. It takes about four minutes, most of
# them the opening cinematics, so it is opt-in.
if [[ "$play" == true ]]; then
  node scripts/play-smoke.mjs
fi

if [[ "$aot" == true ]]; then
  dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:VerifyRustyEngineAot -p:Configuration=Release
  echo "Verified Engine pair ${pair_version}: CoreCLR and NativeAOT."
elif [[ ${#generated_missing[@]} -eq 0 ]]; then
  echo "Verified Engine pair ${pair_version}: CoreCLR. Use --aot for the NativeAOT fidelity publish."
else
  echo "Verified Engine pair ${pair_version}: builds, staging and content-free suites only; the data container is absent."
fi

