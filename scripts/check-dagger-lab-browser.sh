#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

host_log=$(mktemp -t dagger-product-host.XXXXXX.log)
cleanup() {
  status=$?
  for pid in "${host_pid:-}"; do
    if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
  if ((status != 0)); then
    tail -n 120 "$host_log" >&2 || true
  fi
  rm -f "$host_log"
  trap - EXIT
  exit "$status"
}
trap cleanup EXIT

pnpm lab:build

# pnpm does not content-track file: dependencies, so a vendored
# @rusty-engine/application-host can silently go stale when the adjacent
# Engine checkout moves (this once shipped upside-down in-game sprites while
# every check passed). Compare against the source artifact before testing.
vendored_host=$(echo node_modules/.pnpm/@rusty-engine+application-host*/node_modules/@rusty-engine/application-host/index.js)
upstream_host="$repo_root/../rusty-engine/render/artifacts/application-host/index.js"
if [[ -f "$upstream_host" ]]; then
  if ! cmp -s "$vendored_host" "$upstream_host"; then
    echo "vendored @rusty-engine/application-host differs from $upstream_host" >&2
    echo "pnpm file: deps do not track content; run: rm -rf node_modules && pnpm install" >&2
    exit 1
  fi
fi
cargo build -p dagger-studio-adapter --bin dagger-native-host --locked

./target/debug/dagger-native-host --browser-product --lab-port=4274 >"$host_log" 2>&1 &
host_pid=$!
for _ in $(seq 1 600); do
  if curl --silent --fail http://127.0.0.1:4274/api/dagger-lab >/dev/null \
    && curl --silent --fail http://127.0.0.1:4274/ >/dev/null; then
    break
  fi
  if ! kill -0 "$host_pid" 2>/dev/null; then
    exit 1
  fi
  sleep 0.1
done
curl --silent --fail http://127.0.0.1:4274/api/dagger-lab >/dev/null
curl --silent --fail http://127.0.0.1:4274/ >/dev/null
grep -F 'DAGGER_PRODUCT_READY product=privateers-hold api=http://127.0.0.1:4274/api/dagger-product/bootstrap' "$host_log"
DAGGER_PRODUCT_HOST_LOG="$host_log" node scripts/check-dagger-lab-browser.mjs

trap - EXIT
cleanup
