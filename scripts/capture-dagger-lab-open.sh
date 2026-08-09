#!/usr/bin/env bash
set -euo pipefail

: "${DAGGER_LAB_OPEN_CAPTURE:?DAGGER_LAB_OPEN_CAPTURE is required}"
url=${1:?Dagger Lab URL is required}
printf '%s\n' "$url" >"$DAGGER_LAB_OPEN_CAPTURE"
