#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

dotnet build src/Dagger.Game/Dagger.Game.csproj
dotnet test tests/Dagger.Game.Tests/Dagger.Game.Tests.csproj
src/scripts/build-ui.sh
dotnet publish src/Dagger.NativeProduct/Dagger.NativeProduct.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -o src/native
