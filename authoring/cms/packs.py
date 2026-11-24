"""Atomic deterministic JSON writes, per-file locking, and structured validation for content packs."""

import copy
from dataclasses import dataclass
import json
import os
from pathlib import Path
from tempfile import NamedTemporaryFile
from threading import Lock

from cache import FileCache


@dataclass(frozen=True)
class ValidationIssue:
    field: str
    message: str

    def to_dict(self):
        return {"field": self.field, "message": self.message}


class ValidationError(ValueError):
    """Structured pack/payload validation failure that renders cleanly to HTTP 400."""

    def __init__(self, message, issues=()):
        super().__init__(message)
        self.issues = [
            issue.to_dict() if isinstance(issue, ValidationIssue) else dict(issue)
            for issue in issues
        ]


def format_json(data, *, indent=2, sort_keys=False):
    """Canonical deterministic UTF-8 JSON formatting for surgical git diffs."""
    return json.dumps(data, indent=indent, sort_keys=sort_keys, ensure_ascii=False, allow_nan=False) + "\n"


def atomic_write_text(path: Path, content: str):
    """Write UTF-8 text atomically via a sibling temp file and os.replace."""
    target = Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    temp_path = None
    try:
        with NamedTemporaryFile(mode="w", encoding="utf-8", dir=target.parent, delete=False) as handle:
            temp_path = Path(handle.name)
            handle.write(content)
            handle.flush()
            os.fsync(handle.fileno())
        temp_path.replace(target)
        temp_path = None
    finally:
        if temp_path is not None:
            temp_path.unlink(missing_ok=True)


def atomic_write_json(path: Path, data, *, indent=2, sort_keys=False):
    """Write canonical deterministic JSON atomically."""
    atomic_write_text(path, format_json(data, indent=indent, sort_keys=sort_keys))


class PackStore:
    """Coordinate cached reads and locked, validated atomic writes for JSON files and packs."""

    def __init__(self, cache: FileCache | None = None):
        self.cache = cache if cache is not None else FileCache()
        self._locks_guard = Lock()
        self._file_locks = {}

    def lock_for(self, path: Path) -> Lock:
        resolved = Path(path).resolve()
        with self._locks_guard:
            lock = self._file_locks.get(resolved)
            if lock is None:
                lock = Lock()
                self._file_locks[resolved] = lock
            return lock

    def read_json(self, path: Path, default=None):
        """Read a cached JSON document (treat returned data as read-only)."""
        return self.cache.json(path, default)

    def read_json_copy(self, path: Path, default=None):
        """Read a fresh deep copy suitable for mutation."""
        return copy.deepcopy(self.read_json(path, default))

    def write_json(self, path: Path, data, *, validator=None, indent=2, sort_keys=False):
        """Validate and atomically write a JSON document under its per-file lock."""
        target = Path(path)
        with self.lock_for(target):
            if validator is not None:
                validator(data)
            atomic_write_json(target, data, indent=indent, sort_keys=sort_keys)
            self.cache.invalidate_path(target)
        return data

    def update_json(self, path: Path, mutator, *, default=None, validator=None, indent=2, sort_keys=False):
        """Atomically read-modify-validate-write a JSON document under its per-file lock.

        `mutator(current)` returns either `updated_data` or `(updated_data, result)`.
        When a single value is returned, `updated_data` is returned to the caller.
        """
        target = Path(path)
        with self.lock_for(target):
            if target.is_file():
                current = json.loads(target.read_text(encoding="utf-8"))
            else:
                current = copy.deepcopy(default() if callable(default) else default)
            outcome = mutator(current)
            if isinstance(outcome, tuple) and len(outcome) == 2:
                updated, result = outcome
            else:
                updated, result = outcome, outcome
            if validator is not None:
                validator(updated)
            atomic_write_json(target, updated, indent=indent, sort_keys=sort_keys)
            self.cache.invalidate_path(target)
            return result
