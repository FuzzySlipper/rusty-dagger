#!/usr/bin/env python3
"""Apply downloaded NPC merge candidates and regenerate the local reference chart."""

import argparse
import fcntl
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / "src/Daggerfall.Source.Tool/ReferenceExports"
CHART = ROOT / "local/art/reference/npc"
NPC_ID = re.compile(r"npc-[0-9]{3}")


def registry_aliases():
    aliases = {}
    for line in (REFERENCE / "NpcReferenceIds.txt").read_text().splitlines():
        fields = line.split("|", 1)[0].split()
        if not fields or fields[0].startswith("#"):
            continue
        keeper = f"npc-{fields[0]}"
        aliases[keeper] = keeper
        for key in fields[2:]:
            if NPC_ID.fullmatch(key):
                aliases[key] = keeper
    return aliases


def apply_candidate(path, aliases, redirects):
    candidate = json.loads(path.read_text())
    if not isinstance(candidate, dict):
        raise ValueError("expected a JSON object with keep and merge fields")
    keeper, members = candidate.get("keep"), candidate.get("merge")
    if not isinstance(members, list) or not members:
        raise ValueError("select at least two NPCs")
    requested = [keeper, *members]
    if not all(isinstance(npc, str) and npc in aliases for npc in requested):
        raise ValueError("candidate names an unknown NPC ID")
    if len(set(requested)) != len(requested):
        raise ValueError("each NPC ID must appear once")

    current = [redirects.get(aliases[npc], aliases[npc]) for npc in requested]
    if keeper != current[0] or any(npc != resolved and resolved != keeper for npc, resolved in zip(members, current[1:])):
        raise ValueError("an NPC is already part of a different merge; review the current chart")

    sources = set(current[1:]) - {keeper}
    for source, target in list(redirects.items()):
        if target in sources:
            redirects[source] = keeper
    redirects.update((source, keeper) for source in sources)


def publish_chart(output, ledger, original, redirects):
    output.parent.mkdir(parents=True, exist_ok=True)
    with (
        TemporaryDirectory(prefix=".npc-merge-", dir=ledger.parent) as ledger_temp,
        TemporaryDirectory(prefix=".npc-chart-", dir=output.parent) as chart_temp,
        TemporaryDirectory(prefix=".npc-previous-", dir=output.parent) as backup_temp,
    ):
        proposed = Path(ledger_temp) / "merges.json"
        proposed.write_text(json.dumps(redirects, indent=2, sort_keys=True) + "\n")
        rebuilt = Path(chart_temp)
        subprocess.run(
            ["dotnet", "run", "--project", str(ROOT / "src/Daggerfall.Source.Tool"),
             "--", "export-npc", "--output", str(rebuilt), "--merges", str(proposed)],
            cwd=ROOT,
            check=True,
        )
        index = json.loads((rebuilt / "manifest.json").read_text())
        if any(index["aliases"].get(source) != target for source, target in redirects.items()):
            raise ValueError("the rebuilt chart is missing a merge; the existing chart and candidates were preserved")
        if ledger.read_bytes() != original:
            raise ValueError("the merge list changed during export; rerun to apply against the current list")

        previous = Path(backup_temp) / "chart"
        if output.exists():
            output.rename(previous)
        installed = False
        try:
            rebuilt.rename(output)
            installed = True
            proposed.replace(ledger)
        except OSError:
            if installed:
                output.rename(rebuilt)
            if previous.exists():
                previous.rename(output)
            raise


def apply_batch(paths, output, ledger):
    if output.exists() and any(output.iterdir()):
        manifest = output / "manifest.json"
        existing = json.loads(manifest.read_text()) if manifest.is_file() else None
        if not isinstance(existing, dict) or existing.get("briefs") != "briefs.json":
            raise ValueError(f"{output} does not contain an NPC chart; its files were preserved")

    original = ledger.read_bytes()
    redirects = json.loads(original)
    aliases = registry_aliases()
    active = {npc for npc, target in aliases.items() if npc == target}
    if not isinstance(redirects, dict) or any(
        source not in active or not isinstance(target, str) or target not in active or target in redirects
        for source, target in redirects.items()
    ):
        raise ValueError("the merge file must map retired NPC IDs directly to active keepers")

    accepted = []
    for path in paths:
        try:
            apply_candidate(path, aliases, redirects)
        except (OSError, ValueError) as error:
            print(f"{path.name}: {error}. Left in place.", file=sys.stderr)
            continue
        accepted.append(path)

    if not accepted:
        return 1
    publish_chart(output, ledger, original, redirects)
    for path in accepted:
        path.unlink(missing_ok=True)
    print(f"Applied {len(accepted)} candidate file(s). Open {output / 'index.html'}.")
    return int(len(accepted) != len(paths))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidates", nargs="+", type=Path, help="downloaded candidate files or directories containing them")
    parser.add_argument("--output", type=Path, default=CHART, help="generated NPC chart to replace")
    parser.add_argument("--merges", type=Path, default=REFERENCE / "NpcReferenceMerges.json", help="file holding confirmed merges")
    args = parser.parse_args()
    paths = sorted({file.resolve() for path in args.candidates
                    for file in (path.glob("npc-merge-*.json") if path.is_dir() else [path])}, key=lambda path: (path.name, path))
    if not paths:
        print("No NPC merge candidates found.")
        return 0

    output = args.output.resolve()
    ledger = args.merges.resolve()
    if any(path.is_relative_to(output) for path in [ledger, *paths]):
        parser.error("Keep candidates and the merge file outside the generated chart, which is replaced on rebuild.")

    try:
        # Lock the containing directory so replacing the ledger file cannot release the lock.
        lock = os.open(ledger.parent, os.O_RDONLY)
        try:
            fcntl.flock(lock, fcntl.LOCK_EX)
            return apply_batch(paths, output, ledger)
        finally:
            os.close(lock)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"NPC merge: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
