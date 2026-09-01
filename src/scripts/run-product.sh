#!/usr/bin/env bash
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
repo=$(cd "$root/.." && pwd)
engine=${RUSTY_ENGINE_ROOT:-"$repo/../rusty-engine"}
"$root/scripts/build-ui.sh"
dotnet publish "$root/RustyDagger.NativeProduct/RustyDagger.NativeProduct.csproj" -c Release -r linux-x64 -o "$root/native"
exec cargo run --manifest-path "$engine/rust/crates/csharp-product-runtime/Cargo.toml" --bin csharp-product-runtime --locked -- \
  --library "$root/native/RustyDagger.NativeProduct.so" --bundle-dir "$root/browser-bundle" --content-dir "$repo/content" --port "${RUSTY_DAGGER_PORT:-4394}" --mode realtime \
  --direct-intent move.forward=digital --direct-intent move.left=digital --direct-intent move.backward=digital --direct-intent move.right=digital --direct-intent attack=digital \
  --physical-mapping move.forward=move.forward:key:key-w:held \
  --physical-mapping move.left=move.left:key:key-a:held \
  --physical-mapping move.backward=move.backward:key:key-s:held \
  --physical-mapping move.right=move.right:key:key-d:held
