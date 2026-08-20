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

retired_product_dir='crates/dagger-studio-adapter/src/bin/dagger-native-host'
if [[ -e "$retired_product_dir" ]]; then
  echo "$retired_product_dir: retired fixed application still exists" >&2
  exit 1
fi

if git grep -n -E \
  'dagger-native-host|verify-native-host|RendererWebviewAdapter|winit::|gtk::' \
  -- ':!Cargo.lock' ':!scripts/audit-engine-boundary.sh'; then
  echo 'retired fixed application reference remains' >&2
  exit 1
fi

product_main='crates/dagger-studio-adapter/src/bin/dagger-product-server/main.rs'
if (($(wc -l <"$product_main") > 120)); then
  echo "$product_main: product-server wiring grew beyond its bounded role" >&2
  exit 1
fi
grep -F 'mod connected_application;' "$product_main" >/dev/null
grep -F 'connected_application::run(Options::parse()?)' "$product_main" >/dev/null
grep -F 'name = "dagger-product-server"' crates/dagger-studio-adapter/Cargo.toml >/dev/null
echo 'Engine boundary audit passed'
