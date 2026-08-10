#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

if [[ -z "${DISPLAY:-}" ]]; then
  exec xvfb-run -a "$0" "$@"
fi

host_log=$(mktemp -t dagger-lab-native.XXXXXX.log)
open_capture=$(mktemp -t dagger-lab-open.XXXXXX.txt)
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
  rm -f "$host_log" "$open_capture"
  trap - EXIT
  exit "$status"
}
trap cleanup EXIT

pnpm lab:build
cargo build -p dagger-studio-adapter --bin dagger-native-host --locked

env -u WAYLAND_DISPLAY -u WAYLAND_SOCKET \
  GDK_BACKEND=x11 LIBGL_ALWAYS_SOFTWARE=1 WEBKIT_DISABLE_COMPOSITING_MODE=1 \
  DAGGER_LAB_OPEN_COMMAND="$repo_root/scripts/capture-dagger-lab-open.sh" \
  DAGGER_LAB_OPEN_CAPTURE="$open_capture" \
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
for attempt in $(seq 1 3); do
  python3 scripts/x11-send-dagger-move.py l
  for _ in $(seq 1 100); do
    if [[ -s "$open_capture" ]]; then
      break 2
    fi
    if ! kill -0 "$host_pid" 2>/dev/null; then
      exit 1
    fi
    sleep 0.05
  done
  echo "native Lab action not observed after physical attempt $attempt; retrying" >&2
done
if [[ "$(cat "$open_capture")" != "http://127.0.0.1:4274/" ]]; then
  echo 'native Lab action did not launch the connected session URL' >&2
  exit 1
fi
grep -F 'DAGGER_LAB_OPENED url=http://127.0.0.1:4274/ launcher=system' "$host_log"
node scripts/check-dagger-lab-browser.mjs

trap - EXIT
cleanup
