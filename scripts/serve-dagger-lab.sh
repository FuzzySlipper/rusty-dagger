#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <bind-host> <port>" >&2
  exit 64
fi

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

pnpm lab:build
exec xvfb-run -a env -u WAYLAND_DISPLAY -u WAYLAND_SOCKET \
  cargo run -p dagger-studio-adapter --bin dagger-native-host -- \
    "--lab-host=$1" "--lab-port=$2"
