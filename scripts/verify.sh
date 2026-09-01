#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

engine_root=$(cd "$repo_root/../rusty-engine" && pwd)
dotnet restore "$engine_root/csharp/Rusty.Engine.BindingGenerator/Rusty.Engine.BindingGenerator.csproj"
dotnet build src/WorldRpg.Kit/WorldRpg.Kit.csproj
dotnet build src/WorldRpg.Rulesets.Daggerfall/WorldRpg.Rulesets.Daggerfall.csproj
dotnet build src/WorldRpg.Host/WorldRpg.Host.csproj
dotnet test tests/WorldRpg.Kit.Tests/WorldRpg.Kit.Tests.csproj
dotnet test tests/WorldRpg.Rulesets.Daggerfall.Tests/WorldRpg.Rulesets.Daggerfall.Tests.csproj
dotnet test tests/WorldRpg.Rulesets.Canary.Tests/WorldRpg.Rulesets.Canary.Tests.csproj
dotnet test tests/WorldRpg.Architecture.Tests/WorldRpg.Architecture.Tests.csproj
dotnet test tests/Daggerfall.Import.Tests/Daggerfall.Import.Tests.csproj
dotnet test tests/WorldRpg.Host.Tests/WorldRpg.Host.Tests.csproj
dotnet test tests/WorldRpg.SpriteWorkbench.Tests/WorldRpg.SpriteWorkbench.Tests.csproj
src/scripts/build-ui.sh
sprite_workbench_bundle=$(mktemp -d "${TMPDIR:-/tmp}/rusty-dagger-sprite-workbench.XXXXXX")
cleanup_sprite_workbench_bundle() {
  rm -rf "$sprite_workbench_bundle"
}
trap cleanup_sprite_workbench_bundle EXIT
node src/scripts/build-sprite-workbench-ui.mjs "$sprite_workbench_bundle"
dotnet publish src/RustyDagger.NativeProduct/RustyDagger.NativeProduct.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -o src/native
