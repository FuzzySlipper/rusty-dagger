#!/usr/bin/env bash
# Routine product verification. NativeAOT is a separate fidelity target and is
# opt-in through --aot, because the ordinary loop does not need it.
set -euo pipefail

aot=false
for argument in "$@"; do
  case "$argument" in
    --aot) aot=true ;;
    -h|--help)
      echo "usage: scripts/verify.sh [--aot]"
      echo "  no arguments  pair identity, UI dependencies, restore, build, architecture tests, CoreCLR staging"
      echo "  --aot         also run the NativeAOT fidelity publish"
      exit 0
      ;;
    *)
      echo "Unknown argument: $argument (supported: --aot)" >&2
      exit 2
      ;;
  esac
done

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

if [[ ! -x .runtime/verify-pair.sh ]]; then
  echo "Rusty Engine C# pair is not installed. Run ./scripts/install-engine-pair.sh first." >&2
  exit 1
fi

# verify-pair.sh requires a root holding nothing but the manifest's payload, but
# .runtime also holds runtime persistence and any superseded packs. Verify a
# hardlinked view of exactly the payload so live state cannot fail artifact
# accounting.
pair_view=$(mktemp -d "$repo_root/.runtime/.pair-view.XXXXXX")
trap 'rm -rf -- "$pair_view"' EXIT
jq -r '.payload[].path' .runtime/pair-manifest.json | while IFS= read -r payload_path; do
  mkdir -p -- "$pair_view/$(dirname "$payload_path")"
  ln -- "$repo_root/.runtime/$payload_path" "$pair_view/$payload_path"
done
cp -- .runtime/pair-manifest.json "$pair_view/pair-manifest.json"
"$pair_view/verify-pair.sh" --directory "$pair_view"
pair_version=$(sed -n 's|.*<RustyEnginePackageVersion>\([^<]*\)</RustyEnginePackageVersion>.*|\1|p' Directory.Build.props)
pair_source_revision=$(sed -n 's|.*<RustyEnginePairSourceRevision>\([^<]*\)</RustyEnginePairSourceRevision>.*|\1|p' Directory.Build.props)
[[ -n "$pair_version" && -n "$pair_source_revision" ]] || {
  echo "Directory.Build.props must declare the Rusty Engine package version and pair source revision." >&2
  exit 1
}
jq -e --arg package_version "$pair_version" --arg source_revision "$pair_source_revision" \
  '.package.id == "Rusty.Engine" and .package.version == $package_version and .sourceRevision == $source_revision' \
  .runtime/pair-manifest.json >/dev/null || {
  echo "Installed Engine pair does not match Directory.Build.props. Run ./scripts/install-engine-pair.sh." >&2
  exit 1
}
npm ci
node --test tests/WorldRpg.Ui.Tests/*.test.mjs
dotnet restore src/WorldRpg.Host/WorldRpg.Host.csproj
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
dotnet test tests/WorldRpg.Kit.Tests/WorldRpg.Kit.Tests.csproj
dotnet test tests/WorldRpg.Host.Tests/WorldRpg.Host.Tests.csproj

# The workbench's suite belongs here for the same reason: its fake lagged the Engine contract until the
# project no longer compiled, and since nothing ran the project the rot was invisible. It is a product
# in this repository, so its suite is part of the tree that must run.
dotnet test tests/WorldRpg.SpriteWorkbench.Tests/WorldRpg.SpriteWorkbench.Tests.csproj
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:StageRustyEngineCoreClrProduct -p:Configuration=Release

if [[ "$aot" == true ]]; then
  dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:VerifyRustyEngineAot -p:Configuration=Release
  echo "Verified Engine pair ${pair_version} (${pair_source_revision}): CoreCLR and NativeAOT."
else
  echo "Verified Engine pair ${pair_version} (${pair_source_revision}): CoreCLR. Use --aot for the NativeAOT fidelity publish."
fi
