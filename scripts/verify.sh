#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

./scripts/audit-engine-boundary.sh
pnpm install --frozen-lockfile
pnpm lab:check
pnpm gameplay:check
cargo fmt --all --check
cargo test --workspace --locked
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo build -p dagger-studio-adapter --bin dagger-studio-adapter --locked
python3 ./scripts/check-adapter.py
cargo run -p dagger-runtime --bin dagger-gameplay-check --locked
cargo run -p dagger-runtime --bin dagger-walkthrough --locked
cargo run -p dagger-runtime --bin dagger-navgrid --locked -- --check
# The Playwright browser gate (check-dagger-lab-browser.sh) is a manual
# opt-in diagnostic and deliberately not part of the automatic gate: the
# automatic suite stays slim, fast, and deterministic.
