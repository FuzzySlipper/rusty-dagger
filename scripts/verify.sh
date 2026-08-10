#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

./scripts/audit-engine-boundary.sh
pnpm install --frozen-lockfile
pnpm lab:check
cargo fmt --all --check
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo build -p dagger-studio-adapter --bin dagger-studio-adapter --locked
python3 ./scripts/check-adapter.py
./scripts/verify-native-host.sh
./scripts/check-dagger-lab-browser.sh
