#!/usr/bin/env bash
# Routine product verification. NativeAOT is a separate fidelity target and is
# opt-in through --aot, because the ordinary loop does not need it.
set -euo pipefail

aot=false
play=false
record=false
for argument in "$@"; do
  case "$argument" in
    --aot) aot=true ;;
    --play) play=true ;;
    --record) record=true ;;
    -h|--help)
      echo "usage: scripts/verify.sh [--aot] [--play] [--record]"
      echo "  no arguments  pinned pair install, UI tests, restore, build, every test project, CoreCLR staging"
      echo "  --aot         also run the NativeAOT fidelity publish"
      echo "  --play        also start the product on its runtime and press Begin until it reaches ordinary play"
      echo "  --record      on success, attach the run's summary to HEAD as a git note (refs/notes/verify) and push it"
      exit 0
      ;;
    *)
      echo "Unknown argument: $argument (supported: --aot, --play, --record)" >&2
      exit 2
      ;;
  esac
done

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

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
    echo "corpus: local/arena2 $( [[ -d local/arena2 ]] && echo present || echo absent )"
    grep -E '^# (pass|fail|skipped) |^(Passed|Failed)!|^play-smoke .* passed:|^Verified Engine pair' "$run_log" \
      | sed -E 's/, Duration: [^-]*- / /'
  )
  git notes --ref=verify add -f -m "$summary" HEAD
  git push -q origin refs/notes/verify
  echo "Recorded the run on $(git rev-parse --short HEAD) in refs/notes/verify."
  exit 0
fi

npm ci
node --test tests/WorldRpg.Ui.Tests/*.test.mjs

# Installs the pinned pair when it is missing (a no-op offline once installed),
# then restores and stages the host through rusty, which supplies the pair's
# SDK feed; the later dotnet commands find the restored package.
rusty install
rusty build --project src/WorldRpg.Host/WorldRpg.Host.csproj
pair_version=$(sed -n 's|.*<RustyEnginePackageVersion>\([^<]*\)</RustyEnginePackageVersion>.*|\1|p' Directory.Build.props)
dotnet restore tests/WorldRpg.Architecture.Tests/WorldRpg.Architecture.Tests.csproj
dotnet build src/WorldRpg.Host/WorldRpg.Host.csproj --configuration Release --no-restore
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
dotnet test tests/Daggerfall.Import.Tests/Daggerfall.Import.Tests.csproj
dotnet test tests/WorldRpg.Rulesets.Daggerfall.Tests/WorldRpg.Rulesets.Daggerfall.Tests.csproj
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
else
  echo "Verified Engine pair ${pair_version}: CoreCLR. Use --aot for the NativeAOT fidelity publish."
fi

