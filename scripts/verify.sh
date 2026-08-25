#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

rusty_cli="${RUSTY_CLI:-rusty}"
if ! command -v "$rusty_cli" >/dev/null 2>&1 \
  && [[ -x "$repo_root/../rusty-engine/target/debug/rusty" ]]; then
  rusty_cli="$repo_root/../rusty-engine/target/debug/rusty"
fi
if ! command -v "$rusty_cli" >/dev/null 2>&1 && [[ ! -x "$rusty_cli" ]]; then
  echo "Rusty Product CLI is unavailable; build/install the adjacent public rusty CLI first" >&2
  exit 1
fi

# Offline format/import and Studio-adapter mechanisms remain ordinary Dagger
# tooling. Product admission, generated assembly, browser proof, and package
# closure are owned by the public Rusty CLI below.
cargo fmt --all --check
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo build --locked --bin dagger-studio-adapter
python3 ./scripts/check-adapter.py

# The Product Model owns rules/content closure, nested Product Kernel probing,
# generated build output, actual Chromium host evidence, and wrapper package
# policy. No Dagger HTTP server, polling browser script, or alternate canvas
# participates in this path.
"$rusty_cli" check --path "$repo_root"
"$rusty_cli" build --path "$repo_root"
"$rusty_cli" test --path "$repo_root"
"$rusty_cli" package --path "$repo_root" --wrapper desktop
