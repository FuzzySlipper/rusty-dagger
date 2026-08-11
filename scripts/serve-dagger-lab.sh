#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <bind-host> <port>" >&2
  exit 64
fi

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

NG_CLI_ANALYTICS=false pnpm lab:build
gallery_args=()
if [[ "${DAGGER_ENCOUNTER_GALLERY:-0}" == "1" ]]; then
  gallery_args+=(--encounter-gallery)
fi
exec cargo run -p dagger-studio-adapter --bin dagger-native-host -- \
  --browser-product "${gallery_args[@]}" "--lab-host=$1" "--lab-port=$2"
