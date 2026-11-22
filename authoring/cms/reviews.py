"""Shared authoring notes, temporary marks and review decisions."""

from collections import defaultdict
from contextlib import closing, contextmanager
from datetime import datetime, timezone
import sqlite3

RATINGS = {"good", "maybe", "bad"}


def blank(category, id):
    return {"category": category, "id": id, "marked": False, "rating": None,
            "note": "", "revision": None, "updated": None}


class ReviewStore:
    def __init__(self, path):
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self.connection() as db:
            db.execute("""
                CREATE TABLE IF NOT EXISTS reviews (
                    category TEXT NOT NULL, id TEXT NOT NULL, marked INTEGER NOT NULL DEFAULT 0,
                    rating TEXT, note TEXT NOT NULL DEFAULT '', revision TEXT, updated TEXT,
                    PRIMARY KEY (category, id)
                )
                """)

    @contextmanager
    def connection(self):
        with closing(sqlite3.connect(self.path, timeout=10)) as db, db:
            db.row_factory = sqlite3.Row
            yield db

    def rows(self, category=None):
        with self.connection() as db:
            if category is None:
                rows = db.execute("SELECT * FROM reviews ORDER BY category, id")
            else:
                rows = db.execute("SELECT * FROM reviews WHERE category = ? ORDER BY id", (category,))
            return [dict(row) | {"marked": bool(row["marked"])} for row in rows]

    def patch(self, category, id, change, revision=None, aliases=()):
        if not isinstance(change, dict) or not change or set(change) - {"marked", "note", "rating"}:
            raise ValueError("Send marked, note, or rating to update a review.")
        if "marked" in change and type(change["marked"]) is not bool:
            raise ValueError("Marked must be true or false.")
        if "note" in change and not isinstance(change["note"], str):
            raise ValueError("A note must be text.")
        rating = change.get("rating")
        if rating is not None and (not isinstance(rating, str) or rating not in RATINGS):
            raise ValueError("Choose Good, Maybe, or Bad, or clear the rating.")
        changes = dict(change)
        if "rating" in changes:
            changes["revision"] = revision if rating else None
        changes["updated"] = datetime.now(timezone.utc).isoformat()
        with self.connection() as db:
            db.execute("INSERT OR IGNORE INTO reviews(category, id) VALUES (?, ?)", (category, id))
            fields = ", ".join(f"{field} = ?" for field in changes)
            db.execute(f"UPDATE reviews SET {fields} WHERE category = ? AND id = ?",
                       (*changes.values(), category, id))
            if "marked" in change:
                db.executemany("UPDATE reviews SET marked = 0 WHERE category = ? AND id = ?",
                               ((category, alias) for alias in aliases))

    def clear_marks(self, category):
        with self.connection() as db:
            change = db.execute("UPDATE reviews SET marked = 0 WHERE category = ? AND marked = 1", (category,))
            return change.rowcount


# The categories whose records can be found by an earlier id: people and creatures that were merged or renumbered.
CHARACTERS = ("people", "creatures")


class Reviews:
    def __init__(self, store, aliases, revision):
        self.store = store
        self.aliases = aliases
        self.revision = revision

    def canonical(self, category, id):
        return self.aliases.get(id, id) if category in CHARACTERS else id

    def item(self, category, id):
        id = self.canonical(category, id)
        return next((row for row in self.list(category) if row["id"] == id), blank(category, id))

    def list(self, category=None):
        grouped = defaultdict(list)
        for row in self.store.rows(category):
            grouped[row["category"], self.canonical(row["category"], row["id"])].append(row)
        return [self.project(category, id, rows) for (category, id), rows in grouped.items()]

    def project(self, category, id, rows):
        row = next((row for row in rows if row["id"] == id), blank(category, id)).copy()
        row["marked"] = any(item["marked"] for item in rows)
        inherited = [item for item in rows if item["id"] != id and (item["note"] or item["rating"])]
        if inherited:
            row["related"] = inherited
        if row["rating"] and row["revision"] != self.revision(category, id):
            row["previousRating"] = row["rating"]
            row["rating"] = None
        return row

    def patch(self, category, id, change):
        id = self.canonical(category, id)
        aliases = [old for old, target in self.aliases.items() if target == id and old != id] if category in CHARACTERS else []
        version = self.revision(category, id)
        if isinstance(change, dict) and "expectedRevision" in change:
            change = dict(change)
            expected = change.pop("expectedRevision")
            # A preview can stay open while a producer replaces its file.
            if change.get("rating") and expected != version:
                raise ValueError("The review target changed. Reload the page before rating it.")
        self.store.patch(category, id, change, version, aliases)
        return self.item(category, id)
