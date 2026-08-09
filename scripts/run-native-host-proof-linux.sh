#!/usr/bin/env bash
set -euo pipefail

proof_output=${1:?proof output path is required}
repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

export GDK_BACKEND=x11
export LIBGL_ALWAYS_SOFTWARE=1
export WEBKIT_DISABLE_COMPOSITING_MODE=1
unset WAYLAND_DISPLAY WAYLAND_SOCKET

./target/debug/dagger-native-host --proof >"$proof_output" 2>&1 &
application_pid=$!

cleanup() {
  if kill -0 "$application_pid" 2>/dev/null; then
    kill "$application_pid" 2>/dev/null || true
    wait "$application_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT

for _ in $(seq 1 2100); do
  if grep -Fq 'DAGGER_NATIVE_INPUT_ARMED' "$proof_output"; then
    break
  fi
  if ! kill -0 "$application_pid" 2>/dev/null; then
    wait "$application_pid"
  fi
  sleep 0.05
done
grep -Fq 'DAGGER_NATIVE_INPUT_ARMED' "$proof_output"
python3 ./scripts/x11-send-dagger-proof-input.py
wait "$application_pid"
trap - EXIT
