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
    and name == "rusty-engine"
]
if engine_dependencies != [
    (
        "rusty-engine",
        {
            "path": "../rusty-engine/rust/crates/rusty-engine",
        },
    )
]:
    raise SystemExit(
        "workspace must expose exactly one adjacent rusty-engine facade dependency"
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

active_docs = [root / "README.md", *sorted((root / "docs").rglob("*.md"))]
retired_paths = (
    "scripts/check-engine-freshness.py",
    "scripts/serve-studio.sh",
    "scripts/studio-host.mjs",
    "scripts/check-studio-host.mjs",
    "scripts/check-studio-browser.sh",
)
retired_dependency_claims = (
    "follows the provider's public `main` branch",
    "resolved by `Cargo.lock`",
)
for document in active_docs:
    text = document.read_text()
    for retired in (*retired_paths, *retired_dependency_claims):
        if retired in text:
            raise SystemExit(f"{document}: retired Engine/Studio guidance remains: {retired}")

provenance = (root / "docs/source-provenance.md").read_text()
if "../rusty-engine/rust/crates/rusty-engine" not in provenance:
    raise SystemExit("source provenance must name the adjacent rusty-engine facade")
studio_guidance = (root / "docs/studio-host.md").read_text()
for required in ("Engine-hosted product", ".rusty-studio.json", "dagger-studio-adapter"):
    if required not in studio_guidance:
        raise SystemExit(f"Studio guidance is missing current ownership: {required}")
PY

for retired_path in \
  scripts/check-engine-freshness.py \
  scripts/serve-studio.sh \
  scripts/studio-host.mjs \
  scripts/check-studio-host.mjs \
  scripts/check-studio-browser.sh; do
  if [[ -e "$retired_path" ]]; then
    echo "$retired_path: retired Engine/Studio path exists" >&2
    exit 1
  fi
done

if git grep -n -E \
  '@rusty-engine/(render-contracts|render-projection|renderer-host|renderer-three|studio-[a-z-]+)' \
  -- '*.ts' '*.tsx' '*.js' '*.mjs' '*.html' 'package.json'; then
  echo 'downstream source imports Engine Studio or renderer implementation packages' >&2
  exit 1
fi

grep -F 'use rusty_engine::{' \
  'crates/dagger-studio-adapter/src/bin/dagger-native-host/application.rs' >/dev/null
grep -F 'renderer_webview_host::{' \
  'crates/dagger-studio-adapter/src/bin/dagger-native-host/application.rs' >/dev/null
native_main='crates/dagger-studio-adapter/src/bin/dagger-native-host/main.rs'
if (($(wc -l <"$native_main") > 30)); then
  echo "$native_main: product/diagnostic dispatch grew beyond bounded wiring" >&2
  exit 1
fi
grep -F 'let options = proof::Options::parse()?;' "$native_main" >/dev/null
grep -F 'return connected_application::run(options);' "$native_main" >/dev/null
grep -F 'application::run(options)' "$native_main" >/dev/null
echo 'Engine boundary audit passed'
