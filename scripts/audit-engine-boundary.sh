#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

python3 - <<'PY'
from pathlib import Path
import tomllib

root = Path.cwd()
with (root / "Cargo.toml").open("rb") as source:
    workspace = tomllib.load(source)["workspace"]
engine_dependencies = [
    (name, spec)
    for name, spec in workspace["dependencies"].items()
    if isinstance(spec, dict)
    and spec.get("git") == "https://github.com/FuzzySlipper/rusty-engine.git"
]
if engine_dependencies != [
    (
        "rusty-engine",
        {
            "git": "https://github.com/FuzzySlipper/rusty-engine.git",
            "branch": "main",
        },
    )
]:
    raise SystemExit(
        "workspace must expose exactly one rolling rusty-engine facade dependency"
    )

for manifest in root.glob("crates/*/Cargo.toml"):
    text = manifest.read_text()
    for forbidden in (
        "core-ids.workspace",
        "core-math.workspace",
        "core-space.workspace",
        "engine-spatial.workspace",
        "entity-state.workspace",
        "svc-collision.workspace",
    ):
        if forbidden in text:
            raise SystemExit(f"{manifest}: selective Engine dependency remains: {forbidden}")

for source in root.glob("crates/**/*.rs"):
    text = source.read_text()
    for namespace in (
        "core_ids",
        "core_math",
        "core_space",
        "engine_spatial",
        "entity_state",
        "svc_collision",
    ):
        without_facade = text.replace(f"rusty_engine::{namespace}::", "")
        if f"{namespace}::" in without_facade:
            raise SystemExit(f"{source}: bypasses rusty_engine::{namespace}")
PY

if git grep -n -E \
  '@rusty-engine/(render-contracts|render-projection|renderer-host|renderer-three)' \
  -- '*.ts' '*.tsx' '*.js' '*.mjs' '*.html' 'package.json'; then
  echo 'downstream source imports Engine renderer implementation packages' >&2
  exit 1
fi

grep -F 'use rusty_engine::{' \
  'crates/dagger-studio-adapter/src/bin/dagger-native-host/application.rs' >/dev/null
grep -F 'renderer_webview_host::{' \
  'crates/dagger-studio-adapter/src/bin/dagger-native-host/application.rs' >/dev/null
native_main='crates/dagger-studio-adapter/src/bin/dagger-native-host/main.rs'
if (($(wc -l <"$native_main") > 30)); then
  echo "$native_main: native composition root grew beyond bounded wiring" >&2
  exit 1
fi
grep -F 'application::run(proof::Options::parse()?)' "$native_main" >/dev/null
echo 'Engine boundary audit passed'
