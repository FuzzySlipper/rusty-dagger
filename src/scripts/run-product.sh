#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
repo=$(cd "$root/.." && pwd)
engine=/home/dev/rusty-engine
"$root/scripts/build-ui.sh"
dotnet publish "$root/Dagger.Product/Dagger.Product.csproj" -c Release -r linux-x64 -o "$root/native"
exec cargo run --manifest-path "$engine/rust/crates/csharp-product-runtime/Cargo.toml" --locked -- \
  --library "$root/native/Dagger.Product.so" --bundle-dir "$root/browser-bundle" --content-dir "$repo/content" --port "${RUSTY_DAGGER_PORT:-4394}"
