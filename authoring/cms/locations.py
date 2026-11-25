"""Browse staged Daggerfall locations (towns, cities, settlements, and dungeons) for 3D composition."""

from collections import Counter
import json
from pathlib import Path

from cache import FileCache, file_stamp
from people_pack import building_type_name
from router import BadRequestError, NotFoundError

LOCATION_TYPES = {
    0: "City",
    1: "Town / Hamlet",
    2: "Village",
    3: "Farmstead",
    4: "Dungeon Labyrinth",
    5: "Temple",
    6: "Tavern",
    7: "Dungeon Keep",
    8: "Manor / Home",
    9: "Cult / Lodge",
    10: "Ruins",
    11: "Poor Home",
    12: "Graveyard",
    13: "Coven",
    14: "Player Ship",
}

DUNGEON_TYPES = {
    0: "Crypt",
    1: "Orc Stronghold",
    2: "Human Stronghold",
    3: "Prison",
    4: "Desecrated Temple",
    5: "Mine",
    6: "Natural Cave",
    7: "Coven",
    8: "Vampire Haunt",
    9: "Laboratory",
    10: "Harpy Nest",
    11: "Ruined Castle",
    12: "Spider Nest",
    13: "Giant Stronghold",
    14: "Dragons Den",
    15: "Barbarian Stronghold",
    16: "Volcanic Caves",
    17: "Scorpion Nest",
    18: "Cemetery",
}


def location_type_name(value: int | None) -> str:
    if value is None:
        return "Unknown"
    return LOCATION_TYPES.get(value, f"Type {value}")


def dungeon_type_name(value: int | None) -> str | None:
    if value is None or value < 0:
        return None
    return DUNGEON_TYPES.get(value, f"Dungeon {value}")


class LocationsStore:
    """Cached, read-only index of staged locations and their RMB/RDB block grids."""

    def __init__(self, library, cache: FileCache, world_blocks, people_pack):
        self.library = library
        self.cache = cache
        self.world_blocks = world_blocks
        self.people_pack = people_pack

    @property
    def payload_path(self) -> Path:
        return self.library.imported_payload

    def available(self) -> bool:
        return self.payload_path.is_file()

    def _region_names(self) -> dict[int, str]:
        base_path = self.library.base_payload
        if not base_path.is_file():
            return {}
        stamp = file_stamp(base_path)

        def load():
            data = json.loads(base_path.read_text(encoding="utf-8"))
            names = {}
            building_regions = (data.get("buildingNames") or {}).get("regionNames") or []
            for idx, name in enumerate(building_regions):
                if isinstance(name, str) and name:
                    names[idx] = name
            for entry in data.get("regions") or []:
                idx = entry.get("index")
                name = entry.get("name")
                if isinstance(idx, int) and isinstance(name, str):
                    names[idx] = name
            return names

        return self.cache.get(("locations-region-names", base_path), stamp, load)

    def _index(self) -> dict | None:
        if not self.payload_path.is_file():
            return None
        base_path = self.library.base_payload
        stamp = (file_stamp(self.payload_path), file_stamp(base_path))

        def build():
            raw = json.loads(self.payload_path.read_text(encoding="utf-8"))
            section = raw.get("locations") or {}
            raw_locs = section.get("locations") or []
            raw_dungeons = section.get("dungeons") or []
            region_names = self._region_names()

            dungeons_by_key: dict[tuple[int, int], dict] = {}
            for d in raw_dungeons:
                key = (int(d["region"]), int(d["index"]))
                dungeons_by_key[key] = d

            items: list[dict] = []
            by_key: dict[tuple[int, int], dict] = {}
            region_counts: Counter = Counter()
            type_counts: Counter = Counter()
            cities = 0
            towns_and_villages = 0
            multi_block = 0
            with_dungeons = 0

            for loc in raw_locs:
                reg = int(loc["region"])
                idx = int(loc["index"])
                ltype = loc.get("locationType")
                ext = loc.get("exterior") or {}
                ext_blocks = ext.get("blocks") or []
                width = int(ext.get("width") or 0)
                height = int(ext.get("height") or 0)
                block_count = len(ext_blocks)
                unique_blocks = len({b.get("sourceName") for b in ext_blocks if b.get("sourceName")})
                dung = dungeons_by_key.get((reg, idx))
                dung_placements = (dung.get("blockPlacements") or []) if dung else []

                if ltype == 0:
                    cities += 1
                elif ltype in (1, 2):
                    towns_and_villages += 1
                if block_count > 1:
                    multi_block += 1
                if dung is not None and dung_placements:
                    with_dungeons += 1

                region_counts[reg] += 1
                if isinstance(ltype, int):
                    type_counts[ltype] += 1

                reg_name = region_names.get(reg, f"Region {reg}")
                row = {
                    "region": reg,
                    "regionName": reg_name,
                    "index": idx,
                    "name": loc.get("name") or f"Location {reg}:{idx}",
                    "locationType": ltype,
                    "locationTypeName": location_type_name(ltype),
                    "climate": loc.get("climate"),
                    "mapPixelX": ext.get("mapPixelX"),
                    "mapPixelY": ext.get("mapPixelY"),
                    "width": width,
                    "height": height,
                    "exteriorBlockCount": block_count,
                    "uniqueExteriorBlocks": unique_blocks,
                    "hasDungeon": bool(dung is not None and dung_placements),
                    "dungeonBlockCount": len(dung_placements),
                    "dungeonType": loc.get("dungeonType"),
                    "dungeonTypeName": dungeon_type_name(loc.get("dungeonType")),
                    "buildingTypes": ext.get("buildingTypes") or [],
                    "discovered": bool(loc.get("discovered", False)),
                }
                items.append(row)
                by_key[(reg, idx)] = {
                    "summary": row,
                    "raw": loc,
                    "dungeon": dung,
                }

            regions = [
                {"index": r, "name": region_names.get(r, f"Region {r}"), "count": count}
                for r, count in sorted(region_counts.items(), key=lambda pair: region_names.get(pair[0], f"Region {pair[0]}"))
            ]
            location_types = [
                {"id": lt, "name": location_type_name(lt), "count": count}
                for lt, count in sorted(type_counts.items())
            ]

            return {
                "counts": {
                    "total": len(items),
                    "cities": cities,
                    "townsAndVillages": towns_and_villages,
                    "multiBlock": multi_block,
                    "withDungeons": with_dungeons,
                },
                "regions": regions,
                "locationTypes": location_types,
                "items": items,
                "by_key": by_key,
            }

        return self.cache.get(("locations-index", self.payload_path, base_path), stamp, build)

    def summary_counts(self) -> dict:
        index = self._index()
        if index is None:
            return {
                "available": False,
                "total": 0,
                "cities": 0,
                "townsAndVillages": 0,
                "multiBlock": 0,
                "withDungeons": 0,
            }
        return {"available": True, **index["counts"]}

    def query_locations(
        self,
        *,
        q: str = "",
        region: int | None = None,
        location_type: int | None = None,
        has_dungeon: bool = False,
        multi_block: bool = False,
        sort: str = "size",
        offset: int = 0,
        limit: int = 50,
    ) -> dict:
        index = self._index()
        if index is None:
            return {
                "available": False,
                "reason": (
                    f"Staged imported payload not found at '{self.payload_path}'. "
                    "Sync or import local game data (scripts/data-sync.sh) to enable the Towns & Locations 3D viewer."
                ),
                "counts": {"total": 0, "cities": 0, "townsAndVillages": 0, "multiBlock": 0, "withDungeons": 0},
                "regions": [],
                "locationTypes": [],
                "total": 0,
                "offset": 0,
                "limit": limit,
                "items": [],
            }

        terms = [t for t in q.strip().lower().split() if t]
        matched: list[dict] = []
        for row in index["items"]:
            if region is not None and row["region"] != region:
                continue
            if location_type is not None and row["locationType"] != location_type:
                continue
            if has_dungeon and not row["hasDungeon"]:
                continue
            if multi_block and row["exteriorBlockCount"] <= 1:
                continue
            if terms:
                hay = f"{row['name']} {row['regionName']} {row['locationTypeName']}".lower()
                if not all(t in hay for t in terms):
                    continue
            matched.append(row)

        if sort == "name":
            matched.sort(key=lambda r: (r["name"].lower(), r["regionName"].lower()))
        elif sort == "region":
            matched.sort(key=lambda r: (r["regionName"].lower(), -r["exteriorBlockCount"], r["name"].lower()))
        elif sort == "dungeon":
            matched.sort(key=lambda r: (-r["dungeonBlockCount"], -r["exteriorBlockCount"], r["name"].lower()))
        else:
            # Default: largest settlements first so major capitals & towns surface immediately
            matched.sort(key=lambda r: (-r["exteriorBlockCount"], -r["dungeonBlockCount"], r["name"].lower()))

        total = len(matched)
        safe_limit = max(1, min(int(limit), 200))
        safe_offset = max(0, min(int(offset), total))
        return {
            "available": True,
            "counts": index["counts"],
            "regions": index["regions"],
            "locationTypes": index["locationTypes"],
            "total": total,
            "offset": safe_offset,
            "limit": safe_limit,
            "items": matched[safe_offset:safe_offset + safe_limit],
        }

    def location_detail(self, region: int, index_num: int) -> dict | None:
        index = self._index()
        if index is None:
            return None
        record = index["by_key"].get((region, index_num))
        if record is None:
            return None

        summary = record["summary"]
        raw = record["raw"]
        dung = record["dungeon"]

        wb_index = self.world_blocks._index()
        ext_map = wb_index["exterior_by_source"] if wb_index else {}
        rdb_map = wb_index["rdb_by_source"] if wb_index else {}
        interiors_by_source = wb_index["interiors_by_source"] if wb_index else {}
        _, rmb_total_slots = self.world_blocks._people_slot_counts()

        raw_ext = raw.get("exterior") or {}
        exterior_blocks = []
        for b in raw_ext.get("blocks") or []:
            src = b["sourceName"]
            exterior_blocks.append({
                "x": int(b["x"]),
                "y": int(b["y"]),
                "sourceName": src,
                "blockKey": ext_map.get(src),
                "interiorCount": len(interiors_by_source.get(src, {})),
                "peopleSlotCount": rmb_total_slots.get(src, 0),
            })

        dungeon_detail = None
        if dung is not None and (dung.get("blockPlacements") or []):
            placements = []
            for p in dung.get("blockPlacements") or []:
                src = p["sourceKey"]
                placements.append({
                    "x": int(p["x"]),
                    "z": int(p["z"]),
                    "sourceKey": src,
                    "blockKey": rdb_map.get(src),
                    "start": bool(p.get("start", False)),
                })
            dungeon_detail = {
                "dungeonLocationId": dung.get("dungeonLocationId"),
                "dungeonType": summary["dungeonType"],
                "dungeonTypeName": summary["dungeonTypeName"],
                "blockCount": len(placements),
                "uniqueBlocks": len({p["sourceKey"] for p in placements}),
                "placements": placements,
                "textureTable": dung.get("textureTable"),
            }

        building_type_labels = [
            {"id": bt, "name": building_type_name(bt)}
            for bt in (raw_ext.get("buildingTypes") or [])
            if isinstance(bt, int)
        ]

        return {
            "location": {
                **summary,
                "mapId": raw.get("mapId"),
                "longitude": raw.get("longitude"),
                "latitude": raw.get("latitude"),
                "buildingTypeLabels": building_type_labels,
            },
            "exterior": {
                "locationId": raw_ext.get("locationId"),
                "width": int(raw_ext.get("width") or 0),
                "height": int(raw_ext.get("height") or 0),
                "blocks": exterior_blocks,
                "uniqueBlockKeys": sorted({b["blockKey"] for b in exterior_blocks if b["blockKey"]}),
            },
            "dungeon": dungeon_detail,
            "worldBlocksAvailable": self.world_blocks.available(),
            "terrainArchive": self.world_blocks._terrain_archive(),
            "mediaUrls": {
                "geometry": "/media/world-geometry/",
                "materials": "/media/world-materials/",
                "billboards": "/media/world-billboards/",
                "terrain": "/media/world-terrain/",
            },
        }


def _opt_int(req, name: str) -> int | None:
    raw = (req.arg(name, "") or "").strip()
    if not raw:
        return None
    try:
        return int(raw)
    except ValueError:
        return None


def register_routes(router, workspace):
    """Register Locations / Towns & Dungeons inspection endpoints on the CMS router."""

    def get_locations(req):
        q = req.arg("q", "") or ""
        region = _opt_int(req, "region")
        location_type = _opt_int(req, "locationType")
        has_dungeon = (req.arg("hasDungeon", "") or "").lower() in ("1", "true", "yes")
        multi_block = (req.arg("multiBlock", "") or "").lower() in ("1", "true", "yes")
        sort = req.arg("sort", "size") or "size"
        offset = _opt_int(req, "offset") or 0
        limit = _opt_int(req, "limit") or 50
        return workspace.locations.query_locations(
            q=q,
            region=region,
            location_type=location_type,
            has_dungeon=has_dungeon,
            multi_block=multi_block,
            sort=sort,
            offset=offset,
            limit=limit,
        )

    def get_location_detail(req):
        region = _opt_int(req, "region")
        index_num = _opt_int(req, "index")
        if region is None or index_num is None:
            raise BadRequestError("Required integer query parameters: region and index.")
        detail = workspace.locations.location_detail(region, index_num)
        if detail is None:
            raise NotFoundError(f"Unknown location region={region} index={index_num}.")
        return detail

    router.get("/api/locations", get_locations)
    router.get("/api/locations/detail", get_location_detail)
