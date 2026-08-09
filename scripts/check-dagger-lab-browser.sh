#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

if [[ -z "${DISPLAY:-}" ]]; then
  exec xvfb-run -a "$0" "$@"
fi

host_log=$(mktemp -t dagger-lab-native.XXXXXX.log)
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
cargo build -p dagger-studio-adapter --bin dagger-native-host --locked

env -u WAYLAND_DISPLAY -u WAYLAND_SOCKET \
  GDK_BACKEND=x11 LIBGL_ALWAYS_SOFTWARE=1 WEBKIT_DISABLE_COMPOSITING_MODE=1 \
  ./target/debug/dagger-native-host --lab-port=4274 >"$host_log" 2>&1 &
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
node scripts/check-dagger-lab-browser.mjs

trap - EXIT
cleanup
