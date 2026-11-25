"""Read staged world-blocks (RMB exteriors, RMB interiors, RDB dungeons) for the CMS 3D viewer."""

from collections import Counter, defaultdict
import json
from pathlib import Path
import re

from cache import FileCache, file_stamp
from people_pack import building_type_name
from router import BadRequestError, NotFoundError

BLOCK_KINDS = ("rmbExterior", "rmbInterior", "rdb")
TERRAIN_TEXTURE_RE = re.compile(r"^texture-(\d+)-\d+\.png$")


def _people_slot_key(kind: str, source_key: str, building_index: int | None) -> tuple[str, str, int | None]:
    """Map a world-blocks entry to the (slot_kind, block_source, building_index) tuple used by daggerfall.people."""
    if kind == "rmbInterior":
        return ("rmb-interior", source_key, building_index)
    if kind == "rmbExterior":
        return ("rmb-exterior", source_key, None)
    return ("rdb-dungeon", source_key, None)


class WorldBlocksStore:
    """Cached, read-only view of staged world-blocks joined with daggerfall.people and CMS catalog links."""

    def __init__(self, library, cache: FileCache, people_pack):
        self.library = library
        self.cache = cache
        self.people_pack = people_pack

    @property
    def root(self) -> Path:
        return self.library.world_blocks

    @property
    def index_path(self) -> Path:
        return self.root / "blocks.json"

    def available(self) -> bool:
        return self.index_path.is_file()

    def _terrain_archive(self) -> int:
        terrain_dir = self.library.world_shared / "media/dungeon/terrain"
        if not terrain_dir.is_dir():
            return 3
        stamp = file_stamp(terrain_dir)

        def detect():
            archives: set[int] = set()
            for entry in terrain_dir.iterdir():
                match = TERRAIN_TEXTURE_RE.match(entry.name)
                if match:
                    archives.add(int(match.group(1)))
            if 3 in archives:
                return 3
            return min(archives) if archives else 3

        return self.cache.get(("world-blocks-terrain-archive", terrain_dir), stamp, detect)

    def _index(self) -> dict | None:
        if not self.index_path.is_file():
            return None
        stamp = file_stamp(self.index_path)

        def build():
            raw = json.loads(self.index_path.read_text(encoding="utf-8"))
            blocks = raw.get("blocks") or []
            by_key: dict[str, dict] = {}
            exterior_by_source: dict[str, str] = {}
            interiors_by_source: dict[str, dict[int, str]] = defaultdict(dict)
            rdb_by_source: dict[str, str] = {}

            for entry in blocks:
                key = entry["key"]
                kind = entry["kind"]
                source_key = entry["sourceKey"]
                bidx = entry.get("buildingIndex")
                by_key[key] = entry
                if kind == "rmbExterior":
                    exterior_by_source[source_key] = key
                elif kind == "rmbInterior" and bidx is not None:
                    interiors_by_source[source_key][int(bidx)] = key
                elif kind == "rdb":
                    rdb_by_source[source_key] = key

            counts = raw.get("counts") or {}
            return {
                "counts": {
                    "rmbExterior": int(counts.get("rmbExterior", 0)),
                    "rmbInterior": int(counts.get("rmbInterior", 0)),
                    "rdb": int(counts.get("rdb", 0)),
                    "total": len(blocks),
                    "unplacedRmb": len(raw.get("unplacedRmbBlocks") or []),
                },
                "blocks": blocks,
                "by_key": by_key,
                "exterior_by_source": exterior_by_source,
                "interiors_by_source": {k: dict(sorted(v.items())) for k, v in interiors_by_source.items()},
                "rdb_by_source": rdb_by_source,
            }

        return self.cache.get(("world-blocks-index", self.index_path), stamp, build)

    def _people_slot_counts(self) -> tuple[Counter, Counter]:
        """Count daggerfall.people slots per exact block and per RMB source (including its interiors)."""
        exact: Counter = Counter()
        by_rmb_source: Counter = Counter()
        if not self.people_pack.available():
            return exact, by_rmb_source
        pack_idx = self.people_pack._index()
        for slot in pack_idx["slots"]:
            skind = slot["kind"]
            src = slot["block"]
            bidx = slot["buildingIndex"] if skind == "rmb-interior" else None
            exact[(skind, src, bidx)] += 1
            if skind in ("rmb-exterior", "rmb-interior"):
                by_rmb_source[src] += 1
        return exact, by_rmb_source

    def summary_counts(self) -> dict:
        index = self._index()
        if index is None:
            return {
                "available": False,
                "rmbExterior": 0,
                "rmbInterior": 0,
                "rdb": 0,
                "total": 0,
                "unplacedRmb": 0,
            }
        return {"available": True, **index["counts"]}

    def query_blocks(
        self,
        *,
        kind: str = "",
        q: str = "",
        has_people: bool = False,
        offset: int = 0,
        limit: int = 100,
    ) -> dict:
        index = self._index()
        if index is None:
            return {
                "available": False,
                "reason": (
                    f"Staged world-blocks index not found at '{self.index_path}'. "
                    "Sync or import local game data (scripts/data-sync.sh) to enable the 3D block viewer."
                ),
                "counts": {"rmbExterior": 0, "rmbInterior": 0, "rdb": 0, "total": 0, "unplacedRmb": 0},
                "total": 0,
                "offset": 0,
                "limit": limit,
                "items": [],
            }

        exact_slots, rmb_total_slots = self._people_slot_counts()
        interiors_by_source = index["interiors_by_source"]
        terms = [t for t in q.strip().lower().split() if t]

        matched: list[dict] = []
        for entry in index["blocks"]:
            ekind = entry["kind"]
            if kind and ekind != kind:
                continue
            source_key = entry["sourceKey"]
            key = entry["key"]
            bidx = entry.get("buildingIndex")

            direct_slots = exact_slots.get(_people_slot_key(ekind, source_key, bidx), 0)
            total_slots = rmb_total_slots.get(source_key, 0) if ekind == "rmbExterior" else direct_slots
            if has_people and total_slots == 0:
                continue

            if terms:
                hay = f"{key} {source_key} {ekind}".lower()
                if bidx is not None:
                    hay += f" building {bidx} interior-{bidx}"
                if not all(t in hay for t in terms):
                    continue

            interior_count = len(interiors_by_source.get(source_key, {})) if ekind == "rmbExterior" else 0
            matched.append({
                "key": key,
                "kind": ekind,
                "sourceKey": source_key,
                "buildingIndex": bidx,
                "interiorCount": interior_count,
                "peopleSlotCount": direct_slots,
                "totalPeopleSlotCount": total_slots,
                "hasSpatial": bool(entry.get("spatial")),
            })

        total = len(matched)
        safe_limit = max(1, min(int(limit), 500))
        safe_offset = max(0, min(int(offset), total))
        page = matched[safe_offset:safe_offset + safe_limit]

        return {
            "available": True,
            "counts": index["counts"],
            "total": total,
            "offset": safe_offset,
            "limit": safe_limit,
            "items": page,
        }

    def resolve_source(self, source_key: str, building_index: int | None = None, kind: str = "") -> dict | None:
        index = self._index()
        if index is None:
            return None
        source = source_key.strip()
        if not source:
            return None

        # Match case-insensitively on sourceKey if needed
        target_source = source
        if (
            target_source not in index["exterior_by_source"]
            and target_source not in index["rdb_by_source"]
            and target_source not in index["interiors_by_source"]
        ):
            upper = target_source.upper()
            if upper in index["exterior_by_source"] or upper in index["rdb_by_source"] or upper in index["interiors_by_source"]:
                target_source = upper

        if kind == "interior" or building_index is not None:
            interiors = index["interiors_by_source"].get(target_source, {})
            if building_index is not None and building_index in interiors:
                return {"key": interiors[building_index], "kind": "rmbInterior", "sourceKey": target_source, "buildingIndex": building_index}
            if building_index is None and interiors:
                first_idx = min(interiors)
                return {"key": interiors[first_idx], "kind": "rmbInterior", "sourceKey": target_source, "buildingIndex": first_idx}

        if target_source in index["exterior_by_source"]:
            return {"key": index["exterior_by_source"][target_source], "kind": "rmbExterior", "sourceKey": target_source, "buildingIndex": None}
        if target_source in index["rdb_by_source"]:
            return {"key": index["rdb_by_source"][target_source], "kind": "rdb", "sourceKey": target_source, "buildingIndex": None}
        return None

    def _model_asset_map(self, catalog_overview: dict) -> dict[str, dict]:
        """Map ARCH3D model number -> CMS asset summary {id, name, category} for remodelled/noted models."""
        found: dict[str, dict] = {}
        for category in ("nature", "props", "architecture"):
            for row in catalog_overview.get(category) or []:
                facts = row.get("facts") or {}
                arch_id = facts.get("ARCH3D model")
                if arch_id is not None:
                    found[str(arch_id)] = {
                        "id": row["id"],
                        "name": row["name"],
                        "category": category,
                    }
        return found

    def block_detail(self, key: str, catalog, concept_store, catalog_overview: dict) -> dict | None:
        index = self._index()
        if index is None:
            return None
        entry = index["by_key"].get(key)
        if entry is None:
            return None

        doc_path = (self.root / entry["document"]).resolve()
        if not doc_path.is_relative_to(self.root.resolve()) or not doc_path.is_file():
            return None

        doc = json.loads(doc_path.read_text(encoding="utf-8"))
        kind = doc.get("kind") or entry["kind"]
        source_key = doc.get("sourceKey") or entry["sourceKey"]
        bidx = doc.get("buildingIndex")

        interiors_map = index["interiors_by_source"].get(source_key, {})
        parent_exterior_key = index["exterior_by_source"].get(source_key) if kind == "rmbInterior" else None

        buildings = []
        for b in doc.get("buildings") or []:
            idx = b.get("index")
            btype = b.get("buildingType")
            buildings.append({
                **b,
                "buildingTypeName": building_type_name(btype),
                "interiorKey": interiors_map.get(idx) if idx is not None else None,
            })

        interior_building = doc.get("interiorBuilding")
        if isinstance(interior_building, dict):
            interior_building = {
                **interior_building,
                "buildingTypeName": building_type_name(interior_building.get("buildingType")),
            }

        # Join matching daggerfall.people block slots for this exact block
        people_slots = []
        art_memo: dict = {}
        if self.people_pack.available():
            pack_idx = self.people_pack._index()
            target_tuple = _people_slot_key(kind, source_key, bidx)
            individuals_by_id = pack_idx["individuals_by_id"]
            for slot in pack_idx["slots"]:
                slot_tuple = (
                    slot["kind"],
                    slot["block"],
                    slot["buildingIndex"] if slot["kind"] == "rmb-interior" else None,
                )
                if slot_tuple != target_tuple:
                    continue
                vis = slot["visual"]
                archive = vis.get("archive")
                record = vis.get("record")
                art = (
                    self.people_pack.enrich_visual(catalog, concept_store, archive, record, art_memo)
                    if isinstance(archive, int) and isinstance(record, int)
                    else self.people_pack._npc_art_info(catalog, concept_store, None)
                )
                cast_id = slot.get("castId")
                cast_name = individuals_by_id[cast_id]["name"] if cast_id and cast_id in individuals_by_id else None
                people_slots.append({
                    **slot,
                    "castName": cast_name,
                    "art": art,
                })

        # Enrich staticNpcs with resolved CMS People record (npc-###) when matched by (archive, record)
        static_npcs = []
        for npc in doc.get("staticNpcs") or []:
            arch = npc.get("billboardArchive")
            rec = npc.get("billboardRecord")
            art = (
                self.people_pack.enrich_visual(catalog, concept_store, arch, rec, art_memo)
                if isinstance(arch, int) and isinstance(rec, int)
                else None
            )
            static_npcs.append({
                **npc,
                "npcId": art["npcId"] if art else None,
                "npcName": art["npcName"] if art else None,
                "classic": art["classic"] if art else None,
            })

        # Map placed modelIds to CMS model assets where catalogued
        model_assets = self._model_asset_map(catalog_overview)
        model_links = {}
        for m in doc.get("models") or []:
            mid = str(m.get("modelId", ""))
            if mid and mid in model_assets and mid not in model_links:
                model_links[mid] = model_assets[mid]

        return {
            "block": {
                **doc,
                "buildings": buildings,
                "interiorBuilding": interior_building,
                "staticNpcs": static_npcs,
            },
            "parentExteriorKey": parent_exterior_key,
            "interiorKeys": {str(k): v for k, v in interiors_map.items()},
            "peopleSlots": people_slots,
            "modelLinks": model_links,
            "terrainArchive": self._terrain_archive(),
            "mediaUrls": {
                "geometry": "/media/world-geometry/",
                "materials": "/media/world-materials/",
                "billboards": "/media/world-billboards/",
                "terrain": "/media/world-terrain/",
            },
        }


    def block_batch(self, keys: list[str], catalog, concept_store, catalog_overview: dict) -> dict:
        """Load multiple block details in one call for multi-block town and dungeon composition."""
        if not self.available():
            return {
                "available": False,
                "blocks": {},
                "missing": list(keys),
                "terrainArchive": 3,
                "mediaUrls": {
                    "geometry": "/media/world-geometry/",
                    "materials": "/media/world-materials/",
                    "billboards": "/media/world-billboards/",
                    "terrain": "/media/world-terrain/",
                },
            }
        unique_keys = list(dict.fromkeys(k.strip() for k in keys if isinstance(k, str) and k.strip()))
        if len(unique_keys) > 128:
            raise BadRequestError("Batch block request exceeds maximum of 128 unique keys.")
        loaded = {}
        missing = []
        for key in unique_keys:
            detail = self.block_detail(key, catalog, concept_store, catalog_overview)
            if detail is None:
                missing.append(key)
            else:
                loaded[key] = detail
        return {
            "available": True,
            "blocks": loaded,
            "missing": missing,
            "terrainArchive": self._terrain_archive(),
            "mediaUrls": {
                "geometry": "/media/world-geometry/",
                "materials": "/media/world-materials/",
                "billboards": "/media/world-billboards/",
                "terrain": "/media/world-terrain/",
            },
        }


def _int_param(req, name: str, default: int) -> int:
    raw = req.arg(name, "") or ""
    if not raw:
        return default
    try:
        return int(raw)
    except ValueError:
        return default


def register_routes(router, workspace):
    """Register World Blocks inspection endpoints on the CMS router."""

    def get_world_blocks(req):
        kind = req.arg("kind", "") or ""
        if kind and kind not in BLOCK_KINDS:
            kind = ""
        q = req.arg("q", "") or ""
        has_people = (req.arg("hasPeople", "") or "").lower() in ("1", "true", "yes")
        offset = _int_param(req, "offset", 0)
        limit = _int_param(req, "limit", 100)
        return workspace.world_blocks.query_blocks(
            kind=kind, q=q, has_people=has_people, offset=offset, limit=limit,
        )

    def get_world_block_detail(req):
        key = (req.arg("key", "") or "").strip()
        if not key:
            raise NotFoundError("Missing block key.")
        catalog = workspace.catalog()
        overview = workspace.catalog_store.overview()
        detail = workspace.world_blocks.block_detail(key, catalog, workspace.concept_store, overview)
        if detail is None:
            raise NotFoundError(f"Unknown world block '{key}'.")
        return detail

    def resolve_world_block(req):
        source = (req.arg("source", "") or "").strip()
        kind = (req.arg("kind", "") or "").strip()
        raw_bidx = (req.arg("buildingIndex", "") or "").strip()
        bidx = int(raw_bidx) if raw_bidx.isdigit() else None
        resolved = workspace.world_blocks.resolve_source(source, building_index=bidx, kind=kind)
        if resolved is None:
            raise NotFoundError(f"No staged world block found for '{source}'.")
        return resolved

    def post_world_blocks_batch(req):
        body = req.json()
        if not isinstance(body, dict) or not isinstance(body.get("keys"), list):
            raise BadRequestError("Expected JSON body {\"keys\": [...]}.")
        catalog = workspace.catalog()
        overview = workspace.catalog_store.overview()
        return workspace.world_blocks.block_batch(body["keys"], catalog, workspace.concept_store, overview)

    router.get("/api/world-blocks", get_world_blocks)
    router.get("/api/world-blocks/detail", get_world_block_detail)
    router.get("/api/world-blocks/resolve", resolve_world_block)
    router.post("/api/world-blocks/batch", post_world_blocks_batch)
