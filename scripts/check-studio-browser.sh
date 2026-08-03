#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="${RUSTY_STUDIO_BROWSER_OUT:-/tmp/rusty-dagger-studio-check-$$}"
RUSTY_STUDIO_BROWSER_OUT="$OUT" node scripts/check-studio-browser.mjs

echo "Focused screenshots, DOM captures, and metric reports are in $OUT"
