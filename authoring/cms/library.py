"""Local sources and browser URLs for the authoring catalog."""

import json
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import quote

CMS = Path(__file__).resolve().parent
REPO = CMS.parents[1]
BASE_PAYLOAD = REPO / "content/worldrpg/packs/daggerfall.base/payload.json"
MODEL_CATEGORIES = CMS / "model-categories.json"
DEFAULT_CONCEPTS = REPO / "local/authoring/concepts"
DEFAULT_REVIEWS = REPO / "local/authoring/reviews.sqlite3"
DEFAULT_NPCS = REPO / "local/art/reference/npc"
DEFAULT_MERGES = REPO / "src/Daggerfall.Source.Tool/ReferenceExports/NpcReferenceMerges.json"
DEFAULT_CHARACTERS = REPO / "local/art/generated/people/dagger-characters"
STAGE_MOUNT = "/generated/people/dagger-characters/"


def read_json(path, default):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError:
        return default


@dataclass(frozen=True)
class Library:
    npcs: Path
    pipeline: Path
    character_root: Path | None = None
    repo: Path = REPO
    people_pack_root: Path | None = None
    world_blocks_root: Path | None = None
    world_shared_root: Path | None = None
    world_classic_shared_root: Path | None = None
    imported_payload_path: Path | None = None

    @property
    def base_payload(self):
        return self.repo / "content/worldrpg/packs/daggerfall.base/payload.json"

    @property
    def imported_payload(self):
        return self.imported_payload_path or (self.repo / "local/build/import/staging/worldrpg/payloads/daggerfall.imported.json")

    @property
    def people_pack(self):
        return self.people_pack_root or (self.repo / "content/worldrpg/packs/daggerfall.people")

    @property
    def world_blocks(self):
        return self.world_blocks_root or (self.repo / "local/build/import/staging/worldrpg/imports/world-blocks")

    @property
    def world_shared(self):
        return self.world_shared_root or (self.repo / "local/build/import/staging/worldrpg/imports/shared")

    @property
    def world_classic_shared(self):
        return self.world_classic_shared_root or (self.repo / "local/build/import/classic-art/worldrpg/imports/shared")

    @property
    def mobiles(self):
        return self.repo / "local/art/reference/daggerfall-mobile-idle-unmirrored-r01"

    @property
    def flora(self):
        return self.repo / "local/art/review/flora"

    @property
    def scenery(self):
        return self.repo / "local/art/reference/scenery"

    @property
    def surfaces(self):
        return self.repo / "local/art/reference/surfaces"

    @property
    def art_payload(self):
        return self.repo / "content/worldrpg/packs/daggerfall.art-replacements/payload.json"

    @property
    def model_notes(self):
        return self.repo / "authoring/ledger/models-notes.json"

    @property
    def music_sources(self):
        return self.repo / "authoring/tools/music/sources.tsv"

    @property
    def classic_media(self):
        return self.repo / "local/build/import/staging/worldrpg/imports/shared/media/classic/manifest.json"

    @property
    def npc_chart_script(self):
        return self.repo / "src/Daggerfall.Source.Tool/ReferenceExports/NpcReferenceChart.js"

    @property
    def merge_script(self):
        return self.repo / "scripts/apply-npc-merges.py"

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
            "/media/scenery/": self.scenery,
            "/media/surfaces/": self.surfaces,
            STAGE_MOUNT: self.characters,
            "/asset-pipeline/outputs/dagger-characters/": self.characters,
            "/media/turnarounds/": self.turnarounds,
            "/media/world-geometry/": (self.world_shared / "geometry", self.world_classic_shared / "geometry"),
            "/media/world-materials/": (
                self.world_shared / "media/dungeon/materials",
                self.world_classic_shared / "media/dungeon/materials",
            ),
            "/media/world-billboards/": (
                self.world_shared / "media/dungeon/billboards",
                self.world_classic_shared / "media/dungeon/billboards",
            ),
            "/media/world-terrain/": (
                self.world_shared / "media/dungeon/terrain",
                self.world_classic_shared / "media/dungeon/terrain",
            ),
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
