#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
repo=$(cd "$root/.." && pwd)
engine=${RUSTY_ENGINE_ROOT:-"$repo/../rusty-engine"}
"$root/scripts/build-ui.sh"
dotnet publish "$root/RustyDagger.NativeProduct/RustyDagger.NativeProduct.csproj" -c Release -r linux-x64 -o "$root/native"
exec cargo run --manifest-path "$engine/rust/crates/csharp-product-runtime/Cargo.toml" --locked -- \
  --library "$root/native/RustyDagger.NativeProduct.so" --bundle-dir "$root/browser-bundle" --content-dir "$repo/content" --port "${RUSTY_DAGGER_PORT:-4394}" --mode realtime
