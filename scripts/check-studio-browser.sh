#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

CHROMIUM="${RUSTY_STUDIO_CHROMIUM:-chromium}"
HOST="${RUSTY_STUDIO_URL:-http://127.0.0.1:4173}"
OUT="${RUSTY_STUDIO_BROWSER_OUT:-/tmp/rusty-dagger-studio-check-$$}"
mkdir -p "$OUT"
ROOT="$(pwd)"
ROOT_QUERY="$(python3 -c 'import sys,urllib.parse; print(urllib.parse.quote(sys.argv[1], safe=""))' "$ROOT")"
PROJECT_QUERY="content%2Fprojects%2Fprivateers-hold.project.json"
URL="${HOST%/}/?root=${ROOT_QUERY}&project=${PROJECT_QUERY}"

for viewport in desktop narrow; do
    if [[ "$viewport" == desktop ]]; then
        size=1440,900
    else
        size=390,844
    fi
    "$CHROMIUM" --headless --no-sandbox --use-gl=swiftshader \
        --enable-unsafe-swiftshader --run-all-compositor-stages-before-draw \
        --virtual-time-budget=8000 --window-size="$size" \
        --screenshot="$OUT/$viewport.png" --dump-dom "$URL" \
        >"$OUT/$viewport.html" 2>"$OUT/$viewport.log"
    python3 - "$OUT/$viewport.html" <<'PY'
import re
import sys

html = open(sys.argv[1], encoding='utf8').read()
for pattern in (
    r'data-renderer-status="ready"',
    r'data-project-assets="[1-9][0-9]*"',
    r'data-retained-ops="[1-9][0-9]*"',
    r"Privateer's Hold",
):
    if re.search(pattern, html) is None:
        raise SystemExit(f'browser proof missing {pattern}: {sys.argv[1]}')
PY
done

echo "STUDIO BROWSER CHECK PASSED; screenshots and DOM captures are in $OUT"
