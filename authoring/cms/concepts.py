"""Immutable concept submissions and the one approved image for a character."""

import base64
from contextlib import closing, contextmanager
from datetime import datetime, timezone
import json
from pathlib import Path
import sqlite3
from uuid import uuid4

from library import asset_url

MEDIA = "/media/concepts/"
IMAGE_FORMATS = {
    ".png": lambda image: image.startswith(b"\x89PNG\r\n\x1a\n"),
    ".jpg": lambda image: image.startswith(b"\xff\xd8\xff"),
    ".jpeg": lambda image: image.startswith(b"\xff\xd8\xff"),
    ".webp": lambda image: image.startswith(b"RIFF") and image[8:12] == b"WEBP",
}


def now():
    return datetime.now(timezone.utc).isoformat()


class ConceptStore:
    def __init__(self, root):
        self.images = root / "images"
        self.images.mkdir(parents=True, exist_ok=True)
        self.database = root / "concepts.sqlite3"
        with self.connection() as db:
            db.executescript("""
                CREATE TABLE IF NOT EXISTS candidates (
                    id TEXT PRIMARY KEY, subject TEXT NOT NULL, batch TEXT NOT NULL, name TEXT NOT NULL,
                    file TEXT NOT NULL, details TEXT NOT NULL, created TEXT NOT NULL,
                    UNIQUE (subject, batch, name)
                );
                CREATE TABLE IF NOT EXISTS approvals (
                    subject TEXT PRIMARY KEY, candidate TEXT NOT NULL REFERENCES candidates(id),
                    approved_at TEXT NOT NULL
                );
                """)

    @contextmanager
    def connection(self):
        with closing(sqlite3.connect(self.database, timeout=10)) as db, db:
            db.row_factory = sqlite3.Row
            yield db

    def submit(self, subject, body):
        if not isinstance(body, dict):
            raise ValueError("Send a concept candidate as JSON.")
        batch, name, filename, details, encoded = (body.get(key) for key in ("batch", "name", "filename", "details", "image"))
        if not all(isinstance(value, str) and value.strip() for value in (batch, name, filename, encoded)):
            raise ValueError("A batch name, candidate name, filename and image are required.")
        if not isinstance(details, dict) or not isinstance(details.get("prompt"), str) or not details["prompt"].strip():
            raise ValueError("Supply the exact generation prompt.")
        if "seed" not in details:
            raise ValueError("Supply the generation seed, or null when the generator does not report one.")
        seed = details["seed"]
        if seed is not None and not (type(seed) is int or isinstance(seed, str) and seed.strip()):
            raise ValueError("Seed must be an integer, a seed string, or null.")
        details = details | {"seed": str(seed) if seed is not None else None}
        provenance = json.dumps(details, sort_keys=True, ensure_ascii=False, allow_nan=False)
        suffix = Path(filename).suffix.lower()
        image = base64.b64decode(encoded, validate=True)
        if suffix not in IMAGE_FORMATS or not IMAGE_FORMATS[suffix](image):
            raise ValueError("Supply a PNG, JPEG or WebP image with its matching file extension.")
        batch, name = batch.strip(), name.strip()
        created = None
        try:
            with self.connection() as db:
                db.execute("BEGIN IMMEDIATE")
                existing = db.execute("SELECT * FROM candidates WHERE subject = ? AND batch = ? AND name = ?",
                                      (subject, batch, name)).fetchone()
                if existing:
                    # A retry can reuse a submission, but replacing its image would also replace what was approved.
                    if existing["details"] != provenance or (self.images / existing["file"]).read_bytes() != image:
                        raise ValueError("That candidate already exists with different art or generation details. Use a new candidate name.")
                    return self.candidate(existing)
                identifier = uuid4().hex
                created = self.images / (identifier + suffix)
                created.write_bytes(image)
                db.execute("INSERT INTO candidates VALUES (?, ?, ?, ?, ?, ?, ?)",
                           (identifier, subject, batch, name, created.name, provenance, now()))
                return self.candidate(db.execute("SELECT * FROM candidates WHERE id = ?", (identifier,)).fetchone())
        except (OSError, sqlite3.Error):
            if created:
                created.unlink(missing_ok=True)
            raise

    def subjects(self):
        with self.connection() as db:
            return [row["subject"] for row in db.execute("SELECT DISTINCT subject FROM candidates ORDER BY subject")]

    def stage(self, subject, aliases=()):
        subjects = (subject, *aliases)
        placeholders = ", ".join("?" for _ in subjects)
        with self.connection() as db:
            rows = db.execute(f"SELECT * FROM candidates WHERE subject IN ({placeholders}) ORDER BY created, id", subjects)
            candidates = [self.candidate(row) for row in rows]
            approvals = db.execute(f"SELECT * FROM approvals WHERE subject IN ({placeholders})", subjects).fetchall()
        choices = {row["candidate"] for row in approvals}
        selected = next((row for row in approvals if row["subject"] == subject), None)
        if selected is None and len(choices) == 1:
            selected = approvals[0]
        approved = next((candidate for candidate in candidates if selected and candidate["id"] == selected["candidate"]), None)
        if approved:
            approved = approved | {"approvedAt": selected["approved_at"]}
        return {"id": subject, "candidates": candidates, "approved": approved, "conflict": selected is None and len(choices) > 1}

    def approve(self, subject, aliases, candidate):
        if not isinstance(candidate, str):
            raise ValueError("Choose one candidate to approve.")
        with self.connection() as db:
            db.execute("BEGIN IMMEDIATE")
            row = db.execute("SELECT subject, file FROM candidates WHERE id = ?", (candidate,)).fetchone()
            if not row or row["subject"] not in (subject, *aliases) or not (self.images / row["file"]).is_file():
                raise ValueError("Choose a candidate image from this character.")
            db.executemany("DELETE FROM approvals WHERE subject = ?", ((alias,) for alias in aliases))
            db.execute("""
                INSERT INTO approvals(subject, candidate, approved_at) VALUES (?, ?, ?)
                ON CONFLICT(subject) DO UPDATE SET candidate = excluded.candidate, approved_at = excluded.approved_at
                """, (subject, candidate, now()))
        return self.stage(subject, aliases)

    @staticmethod
    def candidate(row):
        return {"id": row["id"], "subject": row["subject"], "batch": row["batch"], "name": row["name"],
                "url": asset_url(MEDIA, row["file"]), "details": json.loads(row["details"]), "createdAt": row["created"]}
