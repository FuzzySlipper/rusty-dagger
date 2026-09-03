#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

if [[ ! -x .runtime/verify-pair.sh ]]; then
  echo "Rusty Engine C# pair is not installed. Run ./scripts/install-engine-pair.sh first." >&2
  exit 1
fi

./.runtime/verify-pair.sh --directory .runtime
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
dotnet restore src/WorldRpg.Host/WorldRpg.Host.csproj
dotnet restore tests/WorldRpg.Architecture.Tests/WorldRpg.Architecture.Tests.csproj
dotnet build src/WorldRpg.Host/WorldRpg.Host.csproj --configuration Release --no-restore
dotnet build src/WorldRpg.SpriteWorkbench/WorldRpg.SpriteWorkbench.csproj --configuration Release
dotnet test tests/WorldRpg.Architecture.Tests/WorldRpg.Architecture.Tests.csproj --no-restore
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:StageRustyEngineCoreClrProduct -p:Configuration=Release
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:VerifyRustyEngineAot -p:Configuration=Release
