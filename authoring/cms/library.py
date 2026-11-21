"""Local sources and browser URLs for the authoring catalog."""

import json
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import quote

CMS = Path(__file__).resolve().parent
REPO = CMS.parents[1]
BASE_PAYLOAD = REPO / "content/worldrpg/packs/daggerfall.base/payload.json"
DEFAULT_CONCEPTS = REPO / "local/authoring/concepts"
DEFAULT_REVIEWS = REPO / "local/authoring/reviews.sqlite3"
DEFAULT_NPCS = REPO / "local/art/reference/npc"
DEFAULT_MERGES = REPO / "src/Daggerfall.Source.Tool/ReferenceExports/NpcReferenceMerges.json"
DEFAULT_CHARACTERS = REPO / "local/art/generated/people/dagger-characters"
STAGE_MOUNT = "/generated/people/dagger-characters/"


def read_json(path, default):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return default


@dataclass(frozen=True)
class Library:
    npcs: Path
    pipeline: Path
    character_root: Path | None = None

    @property
    def mobiles(self):
        return REPO / "local/art/reference/daggerfall-mobile-idle-unmirrored-r01"

    @property
    def flora(self):
        return REPO / "local/art/review/flora"

    @property
    def characters(self):
        return self.character_root or self.pipeline / "outputs/dagger-characters"

    @property
    def turnarounds(self):
        return self.pipeline / "references/dagger-character"

    @property
    def mounts(self):
        return {
            "/media/npc/": self.npcs,
            "/media/classic/": self.mobiles,
            "/media/flora/": self.flora,
            STAGE_MOUNT: self.characters,
            "/asset-pipeline/outputs/dagger-characters/": self.characters,
            "/media/turnarounds/": self.turnarounds,
        }


def asset_url(mount, relative):
    return mount + quote(str(relative), safe="/")


def file_link(root, mount, relative):
    if not relative:
        return None
    file = (root / relative).resolve()
    if file.is_relative_to(root.resolve()) and file.is_file():
        return asset_url(mount, relative)
    return None
