#!/usr/bin/env python3
"""Move the CMS's review data from the old categories to the asset categories (rusty-dagger #10981), once.

`npcs` reviews become `people`, `flora` reviews become `nature` with the `nature-` prefix, and a classic mobile's
(`mobiles`, `mobile-N`) becomes its creature (`creature-NNN`) or, for a human class, the people record of the sprite set it
draws. Concept art and character links filed under a mobile id move the same way. Character stage reviews keep their keys.

Run it on the CMS host after `export-npc` has written the chart with the enemy class records, before restarting the
CMS. It refuses to write anything while a key has no new home, and checks that every row, note, rating and mark is still
there afterwards. Where two old records now share one (two classes drawing one sprite set), the later keeps its old id,
which the CMS reads as an earlier id of that record, so nothing merges away.
"""

import argparse
import json
import sqlite3
from contextlib import closing
from pathlib import Path

from catalog import Catalog, category_of
from library import CMS, DEFAULT_CHARACTERS, DEFAULT_CONCEPTS, DEFAULT_NPCS, DEFAULT_REVIEWS, REPO, Library, read_json

KEPT = ("pipelines", "pipeline-stages", "pipeline-assets", "people", "creatures", "nature", "props", "architecture",
        "textures", "animations", "audio")


def character(catalog, id):
    """The new id of a character filed under `id`, or None when no record takes it."""
    resolved = catalog.resolve(id)
    return resolved if resolved in catalog.subjects else None


def review_key(catalog, category, id):
    """The (category, id) a review moves to, or None when it has no new home."""
    if category in KEPT:
        return category, id
    if category == "npcs":
        return "people", id
    if category == "flora":
        return "nature", f"nature-{id}"
    if category == "mobiles" and (new := character(catalog, id)):
        return category_of(new), new
    return None


def subject_key(catalog, id):
    """The id concept art and links filed under a character's id move to, or None when no record takes it."""
    return id if id in catalog.subjects else character(catalog, id) if id.startswith("mobile-") else id


def plan(pairs, target, kept=lambda old, new: old):
    """Each old key's new key, or its `kept` key where another already takes the new one (a merge would lose a row)."""
    taken, moves, missing = set(), {}, []
    for old in sorted(pairs, key=lambda key: target(key) != key):
        new = target(old)
        if new is None:
            missing.append(old)
            continue
        moves[old] = kept(old, new) if new in taken else new
        taken.add(moves[old])
    return moves, missing


def migrate_reviews(catalog, path):
    with closing(sqlite3.connect(path)) as db, db:
        rows = db.execute("SELECT category, id, marked, rating, note, revision, updated FROM reviews").fetchall()
        moves, missing = plan([(row[0], row[1]) for row in rows], lambda key: review_key(catalog, *key),
                              lambda old, new: (new[0], old[1]))
        if missing:
            raise SystemExit(f"{path}: no new home for {', '.join(f'{c}/{i}' for c, i in missing)}; nothing was changed.")
        db.execute("CREATE TEMP TABLE moved AS SELECT * FROM reviews")
        db.execute("DELETE FROM reviews")
        db.executemany("INSERT INTO reviews(category, id, marked, rating, note, revision, updated) SELECT ?, ?, marked, rating, note, "
                       "revision, updated FROM moved WHERE category = ? AND id = ?",
                       [(*moves[(c, i)], c, i) for c, i, *_ in rows])
        after = db.execute("SELECT marked, rating, note, revision, updated FROM reviews").fetchall()
        if sorted(after, key=repr) != sorted([tuple(row[2:]) for row in rows], key=repr):
            raise SystemExit(f"{path}: the moved reviews do not match the old ones; nothing was changed.")
    return {f"{c}/{i}": "/".join(new) for (c, i), new in moves.items() if new != (c, i)}


def migrate_concepts(catalog, path):
    if not path.is_file():
        return {}
    with closing(sqlite3.connect(path)) as db, db:
        subjects = {row[0] for row in db.execute("SELECT subject FROM candidates UNION SELECT subject FROM approvals")}
        moves, missing = plan(subjects, lambda id: subject_key(catalog, id))
        if missing:
            raise SystemExit(f"{path}: no record for {', '.join(missing)}; nothing was changed.")
        counts = [db.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0] for table in ("candidates", "approvals")]
        for old, new in moves.items():
            if new != old:
                db.execute("UPDATE candidates SET subject = ? WHERE subject = ?", (new, old))
                db.execute("UPDATE approvals SET subject = ? WHERE subject = ?", (new, old))
        if [db.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0] for table in ("candidates", "approvals")] != counts:
            raise SystemExit(f"{path}: the concept rows changed in number; nothing was changed.")
    return {old: new for old, new in moves.items() if new != old}


def migrate_links(catalog, path):
    links = read_json(path, {})
    moves, missing = plan(links, lambda id: subject_key(catalog, id))
    if missing:
        raise SystemExit(f"{path}: no record for {', '.join(missing)}; nothing was changed.")
    changed = {old: new for old, new in moves.items() if new != old}
    if changed:
        path.write_text(json.dumps({moves[old]: link for old, link in links.items()}, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return changed


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--npc-root", type=Path, default=DEFAULT_NPCS)
    parser.add_argument("--asset-pipeline", type=Path, default=REPO.parent / "asset-pipeline")
    parser.add_argument("--links", type=Path, default=CMS / "links.json")
    parser.add_argument("--reviews", type=Path, default=DEFAULT_REVIEWS)
    parser.add_argument("--concept-root", type=Path, default=DEFAULT_CONCEPTS)
    args = parser.parse_args(argv)
    library = Library(args.npc_root.resolve(), args.asset_pipeline.resolve(), DEFAULT_CHARACTERS if DEFAULT_CHARACTERS.is_dir() else None)
    catalog = Catalog(library, {})
    if "mobile-128" not in catalog.aliases:
        raise SystemExit(f"{args.npc_root}: the NPC chart has no enemy class records; run export-npc first.")
    report = {"reviews": migrate_reviews(catalog, args.reviews), "concepts": migrate_concepts(catalog, args.concept_root / "concepts.sqlite3"),
              "links": migrate_links(catalog, args.links)}
    print(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
