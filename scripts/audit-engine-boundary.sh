#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$repo_root"

python3 - <<'PY'
from pathlib import Path
import re
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

def claims_engine_dependency_carrier(sentence: str) -> bool:
    normalized = sentence.lower().replace("`", "")
    owns_dependency = (
        re.search(r"\b(?:rusty[ -]engine|engine)\b", normalized) is not None
        and re.search(r"\b(?:facade|dependenc\w*|source|provider)\b", normalized) is not None
    )
    carrier = re.search(
        r"\bmain\s+branch\b|cargo(?:\.|\s*)lock(?:file)?|"
        r"\b(?:pin\w*|revisions?|commits?|sha|freshness|pull\w*|update\w*)\b",
        normalized,
    )
    authority_relation = re.search(
        r"\b(?:track\w*|follow\w*|resolv\w*|pin\w*|lock\w*|suppl\w*|"
        r"provid\w*|depend\w*|manag\w*|consum\w*|use[sd]?|using|through|"
        r"comes? from|source of truth|carrier)\b",
        normalized,
    )
    if not owns_dependency or carrier is None or authority_relation is None:
        return False
    historical = re.search(
        r"\b(?:historical|formerly|previously|retired|removed|no longer)\b",
        normalized,
    )
    explicit_non_authority = re.search(
        r"\b(?:must not|never|does not|do not)\s+(?:\w+\s+){0,3}"
        r"(?:fetch|pull|reset|pin|manage|track|follow|resolve|update)|"
        r"\breview evidence\b|\bnot (?:a |the )?source[- ]dependency protocol\b",
        normalized,
    )
    return historical is None and explicit_non_authority is None


def dependency_clauses(sentence: str) -> list[str]:
    return [
        clause.strip()
        for clause in re.split(
            r"\s*(?:;|,\s*(?:but|however|yet|and)|"
            r"\b(?:but|however|yet|although|though|whereas|while)\b)\s*",
            sentence,
            flags=re.IGNORECASE,
        )
        if clause.strip()
    ]


def sentence_claims_engine_dependency_carrier(sentence: str) -> bool:
    normalized = sentence.lower().replace("`", "")
    carries_engine_context = (
        re.search(r"\b(?:rusty[ -]engine|engine)\b", normalized) is not None
        and re.search(r"\b(?:facade|dependenc\w*|source|provider)\b", normalized) is not None
    )
    for clause in dependency_clauses(sentence):
        candidate = clause
        if carries_engine_context:
            candidate = f"Rusty Engine facade dependency {candidate}"
        if claims_engine_dependency_carrier(candidate):
            return True
    return False


rejected_dependency_claims = (
    "The public Rusty Engine facade tracks the provider main branch through the Cargo lockfile.",
    "Through the Cargo lockfile, the provider main branch supplies the Rusty Engine facade dependency.",
    "Historical notes describe an older setup, but the Rusty Engine facade tracks the provider main branch through Cargo.lock.",
    "Exact Rusty Engine commits are review evidence, but the facade dependency follows the provider main branch through Cargo.lock.",
    "The Rusty Engine facade dependency follows provider main through Cargo.lock, although the older setup is historical.",
)
for claim in rejected_dependency_claims:
    if not sentence_claims_engine_dependency_carrier(claim):
        raise SystemExit(f"Engine dependency-carrier regression was not rejected: {claim}")

allowed_dependency_guidance = (
    "Rusty Engine dependency tooling must not pull, pin, or update the sibling checkout.",
    "Historical: the Rusty Engine facade previously followed a provider revision.",
    "Exact Rusty Engine facade commits are review evidence, not a source-dependency protocol.",
)
for guidance in allowed_dependency_guidance:
    if sentence_claims_engine_dependency_carrier(guidance):
        raise SystemExit(f"valid Engine dependency guidance was rejected: {guidance}")

for document in active_docs:
    text = document.read_text()
    for retired in retired_paths:
        if retired in text:
            raise SystemExit(f"{document}: retired Engine/Studio guidance remains: {retired}")
    paragraphs = re.split(r"\n\s*\n", text)
    for paragraph in paragraphs:
        joined = " ".join(line.strip() for line in paragraph.splitlines())
        for sentence in re.split(r"(?<=[.!?])\s+", joined):
            if sentence_claims_engine_dependency_carrier(sentence):
                raise SystemExit(
                    f"{document}: active Engine dependency carrier claim remains: {sentence}"
                )

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
