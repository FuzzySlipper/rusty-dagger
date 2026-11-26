"""Serve the 2D World & Political Atlas over staged relief tiles, POLITIC/CLIMATE masks, factions, and locations."""

from __future__ import annotations

from collections import Counter, defaultdict
import json
from pathlib import Path

from base_pack import read_section as read_base_section, sections_stamp as base_sections_stamp
from cache import FileCache, file_stamp
from router import NotFoundError

MAP_WIDTH = 1000
MAP_HEIGHT = 500

RULER_TITLES = (
    None,
    "King",
    "Queen",
    "Duke",
    "Duchess",
    "Marquis",
    "Marquise",
    "Count",
    "Countess",
    "Baron",
    "Baroness",
    "Lord",
    "Lady",
)

CLIMATE_COLORS = {
    "Ocean": "#2a5674",
    "Woodlands": "#5c8c4a",
    "HauntedWoodlands": "#486852",
    "Mountain": "#8f8578",
    "MountainWoods": "#4c785c",
    "Rainforest": "#358046",
    "Subtropical": "#9e9854",
    "Swamp": "#5e7556",
    "Desert": "#d4ad6a",
    "Desert2": "#c4985a",
}

# Exact classic travel map layout matching WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallTravelMap
CLASSIC_WORLD_MAP = {
    "url": "/media/maps/map-trav0i00.png",
    "width": 320,
    "height": 200,
    "left": 0.0,
    "top": -12.0 / (160.0 / 500.0),
    "right": 320.0 / (160.0 / 500.0),
    "bottom": (200.0 - 12.0) / (160.0 / 500.0),
}


def _sheet(region: int, media_file: str, origin_x: float, origin_y: float, scale: int = 1) -> dict:
    return {
        "region": region,
        "url": f"/media/maps/{media_file}",
        "width": 320,
        "height": 160,
        "left": origin_x,
        "top": origin_y,
        "right": origin_x + 320.0 / scale,
        "bottom": origin_y + 160.0 / scale,
    }


CLASSIC_SHEETS = (
    _sheet(0, "map-fmapai00.png", 212, 340),
    _sheet(0, "map-fmapbi00.png", 322, 340),
    _sheet(1, "map-fmapai01.png", 583, 279),
    _sheet(1, "map-fmapbi01.png", 680, 279),
    _sheet(1, "map-fmapci01.png", 583, 340),
    _sheet(1, "map-fmapdi01.png", 680, 340),
    _sheet(5, "map-fmap0i05.png", 381, 4),
    _sheet(9, "map-fmap0i09.png", 525, 114),
    _sheet(11, "map-fmap0i11.png", 437, 340),
    _sheet(16, "map-fmapai16.png", 578, 0),
    _sheet(16, "map-fmapbi16.png", 680, 0),
    _sheet(16, "map-fmapci16.png", 578, 52),
    _sheet(16, "map-fmapdi16.png", 680, 52),
    _sheet(17, "map-fmap0i17.png", 39, 106),
    _sheet(18, "map-fmap0i18.png", 20, 29),
    _sheet(19, "map-fmap0i19.png", 80, 123.0 / 4.0 + 212.0, scale=4),
    _sheet(20, "map-fmap0i20.png", 217, 293),
    _sheet(21, "map-fmap0i21.png", 263, 79),
    _sheet(22, "map-fmap0i22.png", 548, 219),
    _sheet(23, "map-fmap0i23.png", 680, 146),
    _sheet(26, "map-fmap0i26.png", 680, 80),
    _sheet(32, "map-fmap0i32.png", 41, 0),
    _sheet(33, "map-fmap0i33.png", 660, 101),
    _sheet(34, "map-fmap0i34.png", 578, 40),
    _sheet(35, "map-fmap0i35.png", 525, 3),
    _sheet(36, "map-fmap0i36.png", 440, 40),
    _sheet(37, "map-fmap0i37.png", 448, 0),
    _sheet(38, "map-fmap0i38.png", 366, 0),
    _sheet(39, "map-fmap0i39.png", 300, 8),
    _sheet(40, "map-fmap0i40.png", 202, 0),
    _sheet(41, "map-fmap0i41.png", 223, 6),
    _sheet(42, "map-fmap0i42.png", 148, 76),
    _sheet(43, "map-fmap0i43.png", 15, 340),
    _sheet(44, "map-fmap0i44.png", 61, 340),
    _sheet(45, "map-fmap0i45.png", 86, 338),
    _sheet(46, "map-fmap0i46.png", 132, 340),
    _sheet(47, "map-fmap0i47.png", 344, 309),
    _sheet(48, "map-fmap0i48.png", 381, 251),
    _sheet(49, "map-fmap0i49.png", 553, 255),
    _sheet(50, "map-fmap0i50.png", 661, 217),
    _sheet(51, "map-fmap0i51.png", 672, 275),
    _sheet(52, "map-fmap0i52.png", 680, 256),
    _sheet(53, "map-fmap0i53.png", 680, 340),
    _sheet(54, "map-fmap0i54.png", 491, 340),
    _sheet(55, "map-fmap0i55.png", 293, 340),
    _sheet(56, "map-fmap0i56.png", 263, 340),
    _sheet(57, "map-fmap0i57.png", 680, 157),
    _sheet(58, "map-fmap0i58.png", 17, 53),
    _sheet(59, "map-fmap0i59.png", 0, 0),
    _sheet(60, "map-fmap0i60.png", 107, 11),
    _sheet(61, "map-fmap0i61.png", 440.0 + (255.0 - 440.0) / 4.0, 340.0 + (275.0 - 340.0) / 4.0, scale=4),
)

PIN_CATEGORIES = (
    {"id": "city", "label": "Cities", "types": [0], "color": "#ffd166"},
    {"id": "town", "label": "Towns & Villages", "types": [1, 2], "color": "#4de096"},
    {"id": "temple", "label": "Temples & Guilds", "types": [5, 6, 9], "color": "#72c4ff"},
    {"id": "dungeon", "label": "Dungeons", "types": [4, 7, 10, 12], "color": "#ff6b6b"},
    {"id": "home", "label": "Homes & Farms", "types": [3, 8, 11], "color": "#c5b4e3"},
    {"id": "other", "label": "Covens & Other", "types": [13, 14], "color": "#f4978e"},
)


def _ruler_title(code: int | None) -> str | None:
    if not isinstance(code, int) or code <= 0:
        return None
    if code < len(RULER_TITLES):
        return RULER_TITLES[code]
    return "Lord"


def _decode_rle_grid(rows: list[dict], value_to_code: dict[int, int], default_code: int) -> list[int]:
    """Expand a 1001x500 PAK RLE structure into a flat 1000x500 row-major grid (stripping column 1000 sentinel)."""
    grid = [default_code] * (MAP_WIDTH * MAP_HEIGHT)
    for row_obj in rows:
        y = int(row_obj.get("y", -1))
        if y < 0 or y >= MAP_HEIGHT:
            continue
        x = 0
        row_base = y * MAP_WIDTH
        for run in row_obj.get("runs") or []:
            count = int(run.get("count") or 0)
            if count <= 0:
                continue
            raw_val = int(run.get("value", 0))
            code = value_to_code.get(raw_val, default_code)
            end = min(x + count, MAP_WIDTH)
            if x < MAP_WIDTH and end > x:
                grid[row_base + x:row_base + end] = [code] * (end - x)
            x += count
            if x >= MAP_WIDTH:
                break
    return grid


def _encode_flat_rle(grid: list[int]) -> list[int]:
    """Encode a flat integer grid as alternating [value, count, value, count, ...] for compact JSON transport."""
    if not grid:
        return []
    out: list[int] = []
    cur = grid[0]
    run = 1
    for val in grid[1:]:
        if val == cur:
            run += 1
        else:
            out.append(cur)
            out.append(run)
            cur = val
            run = 1
    out.append(cur)
    out.append(run)
    return out


class WorldAtlasStore:
    """Cached 2D World & Political Atlas joining baked relief tiles, POLITIC/CLIMATE masks, factions, and locations."""

    def __init__(self, library, cache: FileCache, locations, people_pack):
        self.library = library
        self.cache = cache
        self.locations = locations
        self.people_pack = people_pack

    @property
    def atlas_manifest_path(self) -> Path:
        return self.library.atlas_root / "atlas.json"

    def available(self) -> bool:
        return self.library.imported_payload.is_file() or self.atlas_manifest_path.is_file()

    def _atlas_manifest(self) -> dict:
        if not self.atlas_manifest_path.is_file():
            return {
                "available": False,
                "tileBaseUrl": "/media/atlas/",
                "tileSize": 256,
                "worldWidth": MAP_WIDTH,
                "worldHeight": MAP_HEIGHT,
                "levels": [],
            }
        raw = json.loads(self.atlas_manifest_path.read_text(encoding="utf-8"))
        return {
            "available": True,
            "tileBaseUrl": "/media/atlas/",
            "tileSize": int(raw.get("tileSize") or 256),
            "worldWidth": int(raw.get("worldWidth") or MAP_WIDTH),
            "worldHeight": int(raw.get("worldHeight") or MAP_HEIGHT),
            "levels": raw.get("levels") or [],
        }

    def _classic_maps(self) -> dict:
        world_file = self.library.maps_root / "map-trav0i00.png"
        if not world_file.is_file():
            return {"available": False, "world": CLASSIC_WORLD_MAP, "sheets": []}
        present_sheets = [
            sheet for sheet in CLASSIC_SHEETS
            if (self.library.maps_root / sheet["url"].removeprefix("/media/maps/")).is_file()
        ]
        return {
            "available": True,
            "world": CLASSIC_WORLD_MAP,
            "sheets": present_sheets,
        }

    _BASE_SECTIONS = ("buildingNames", "regions", "factions", "worldNames")

    def _build_bundle(self) -> dict:
        imported_path = self.library.imported_payload
        base_path = self.library.base_payload
        atlas_path = self.atlas_manifest_path
        people_available = self.people_pack.available()
        people_stamp = (
            file_stamp(self.library.people_pack / "spawners.json"),
            file_stamp(self.library.people_pack / "cast/individuals.json"),
        ) if people_available else None

        stamp = (
            file_stamp(imported_path),
            base_sections_stamp(base_path, self._BASE_SECTIONS, self.cache),
            file_stamp(atlas_path),
            file_stamp(self.library.maps_root / "map-trav0i00.png"),
            people_stamp,
        )

        def build():
            atlas = self._atlas_manifest()
            classic_maps = self._classic_maps()
            if not imported_path.is_file():
                return {
                    "overview": {
                        "available": False,
                        "reason": (
                            f"Imported world payload not found at '{imported_path}'. "
                            "Sync or import local game data to enable the 2D World & Political Atlas."
                        ),
                        "atlas": atlas,
                        "classicMaps": classic_maps,
                        "worldNames": [],
                        "climates": [],
                        "pinCategories": list(PIN_CATEGORIES),
                        "regions": [],
                        "counts": {"regions": 0, "inhabitedRegions": 0, "locations": 0, "diplomaticLinks": 0},
                    },
                    "masks": {
                        "width": MAP_WIDTH,
                        "height": MAP_HEIGHT,
                        "politicRuns": [],
                        "climateRuns": [],
                        "climates": [],
                    },
                    "pins": {
                        "categories": list(PIN_CATEGORIES),
                        "total": 0,
                        "pins": [],
                    },
                }

            imp = json.loads(imported_path.read_text(encoding="utf-8"))
            base = {
                name: read_base_section(base_path, name, None, self.cache)
                for name in self._BASE_SECTIONS
            } if base_path.is_file() else {}

            # 1. Decode 1000x500 politic & climate masks
            politic_section = imp.get("politic") or {}
            politic_val_to_region = {
                int(entry["value"]): int(entry.get("region", -1))
                for entry in politic_section.get("values") or []
                if isinstance(entry, dict) and "value" in entry
            }
            politic_grid = _decode_rle_grid(politic_section.get("rows") or [], politic_val_to_region, -1)

            climate_section = imp.get("climate") or {}
            climates_list: list[dict] = []
            climate_val_to_idx: dict[int, int] = {}
            for idx, entry in enumerate(climate_section.get("values") or []):
                if not isinstance(entry, dict) or "value" not in entry:
                    continue
                cval = int(entry["value"])
                cname = str(entry.get("name") or f"Climate {cval}")
                climate_val_to_idx[cval] = len(climates_list)
                climates_list.append({
                    "index": len(climates_list),
                    "value": cval,
                    "name": cname,
                    "climateBase": entry.get("climateBase") or "temperate",
                    "people": entry.get("people") or "",
                    "color": CLIMATE_COLORS.get(cname, "#6f8a6e"),
                })
            climate_grid = _decode_rle_grid(climate_section.get("rows") or [], climate_val_to_idx, 0)

            # 2. Compute per-region pixel stats, bounds, visual centroids, and climate mix
            region_pixels: Counter = Counter()
            region_sum_x: defaultdict[int, int] = defaultdict(int)
            region_sum_y: defaultdict[int, int] = defaultdict(int)
            region_bounds: dict[int, list[int]] = {}
            region_climates: defaultdict[int, Counter] = defaultdict(Counter)

            for y in range(MAP_HEIGHT):
                row_off = y * MAP_WIDTH
                for x in range(MAP_WIDTH):
                    reg = politic_grid[row_off + x]
                    if reg < 0:
                        continue
                    region_pixels[reg] += 1
                    region_sum_x[reg] += x
                    region_sum_y[reg] += y
                    b = region_bounds.get(reg)
                    if b is None:
                        region_bounds[reg] = [x, y, x, y]
                    else:
                        if x < b[0]:
                            b[0] = x
                        if y < b[1]:
                            b[1] = y
                        if x > b[2]:
                            b[2] = x
                        if y > b[3]:
                            b[3] = y
                    region_climates[reg][climate_grid[row_off + x]] += 1

            # 3. Region names from daggerfall.base
            region_names: dict[int, str] = {}
            for idx, name in enumerate((base.get("buildingNames") or {}).get("regionNames") or []):
                if isinstance(name, str) and name:
                    region_names[idx] = name
            for entry in base.get("regions") or []:
                idx = entry.get("index")
                name = entry.get("name")
                if isinstance(idx, int) and isinstance(name, str):
                    region_names[idx] = name

            # 4. Join LocationsStore for per-region settlement counts & compact pins list
            loc_index = self.locations._index()
            loc_items = loc_index["items"] if loc_index else []
            region_loc_counts: defaultdict[int, Counter] = defaultdict(Counter)
            region_loc_sum_x: defaultdict[int, float] = defaultdict(float)
            region_loc_sum_y: defaultdict[int, float] = defaultdict(float)
            region_loc_xy_n: Counter = Counter()
            pins: list[list] = []

            for item in loc_items:
                reg = int(item["region"])
                ltype = item.get("locationType")
                has_dungeon = bool(item.get("hasDungeon"))
                counts = region_loc_counts[reg]
                counts["total"] += 1
                if ltype == 0:
                    counts["cities"] += 1
                elif ltype in (1, 2):
                    counts["townsAndVillages"] += 1
                elif ltype in (5, 6, 9):
                    counts["temples"] += 1
                elif ltype in (4, 7, 10, 12):
                    counts["dungeons"] += 1
                else:
                    counts["other"] += 1
                if has_dungeon:
                    counts["withDungeons"] += 1

                px = item.get("mapPixelX")
                py = item.get("mapPixelY")
                if isinstance(px, (int, float)) and isinstance(py, (int, float)):
                    region_loc_sum_x[reg] += float(px)
                    region_loc_sum_y[reg] += float(py)
                    region_loc_xy_n[reg] += 1
                    pins.append([
                        reg,
                        int(item["index"]),
                        int(px),
                        int(py),
                        int(ltype) if isinstance(ltype, int) else -1,
                        1 if has_dungeon else 0,
                        item["name"],
                        item.get("climate"),
                    ])

            # 5. Join daggerfall.people regional spawner pools & cast individuals by factionId
            cast_by_faction: dict[int, list[dict]] = defaultdict(list)
            spawners_by_region: dict[int, list[str]] = defaultdict(list)
            if people_available:
                pack_idx = self.people_pack._index()
                for ind in pack_idx["individuals"]:
                    fid = ind.get("factionId")
                    if isinstance(fid, int):
                        cast_by_faction[fid].append({"id": ind["id"], "name": ind["name"]})
                for sp in pack_idx["spawners"]:
                    for rkey in (sp.get("regionalPools") or {}).keys():
                        if str(rkey).isdigit():
                            spawners_by_region[int(rkey)].append(sp["id"])

            # 6. Join daggerfall.base factions & build regional hierarchy + diplomatic graph
            raw_factions = (base.get("factions") or {}).get("factions") or []
            factions_by_id: dict[int, dict] = {
                int(f["id"]): f for f in raw_factions if isinstance(f, dict) and isinstance(f.get("id"), int)
            }

            # Resolve effective region for each faction (direct region >= 0, else walk parent chain)
            def effective_region(fid: int) -> int:
                seen: set[int] = set()
                cur = fid
                while cur in factions_by_id and cur not in seen:
                    seen.add(cur)
                    f = factions_by_id[cur]
                    reg = f.get("region")
                    if isinstance(reg, int) and reg >= 0:
                        return reg
                    par = f.get("parent")
                    if not isinstance(par, int) or par <= 0:
                        break
                    cur = par
                return -1

            faction_region: dict[int, int] = {fid: effective_region(fid) for fid in factions_by_id}
            factions_by_region: defaultdict[int, list[dict]] = defaultdict(list)
            for fid, f in factions_by_id.items():
                reg = faction_region[fid]
                if reg >= 0:
                    factions_by_region[reg].append(f)

            # Build symmetric ally/enemy links per region from all factions belonging to that region
            region_allies: defaultdict[int, list[dict]] = defaultdict(list)
            region_enemies: defaultdict[int, list[dict]] = defaultdict(list)
            seen_ally_edges: defaultdict[int, set[tuple[int, int]]] = defaultdict(set)
            seen_enemy_edges: defaultdict[int, set[tuple[int, int]]] = defaultdict(set)

            def add_edge(bucket, seen_bucket, reg: int, src: dict, dst: dict):
                key = (src["id"], dst["id"])
                if key in seen_bucket[reg]:
                    return
                seen_bucket[reg].add(key)
                dst_reg = faction_region.get(dst["id"], -1)
                bucket[reg].append({
                    "sourceFactionId": src["id"],
                    "sourceFactionName": src["name"],
                    "targetFactionId": dst["id"],
                    "targetFactionName": dst["name"],
                    "targetRegion": dst_reg if dst_reg >= 0 else None,
                    "targetRegionName": region_names.get(dst_reg) if dst_reg >= 0 else None,
                })

            for src in raw_factions:
                if not isinstance(src, dict) or not isinstance(src.get("id"), int):
                    continue
                src_reg = faction_region.get(src["id"], -1)
                for aid in src.get("allies") or []:
                    dst = factions_by_id.get(aid)
                    if not dst:
                        continue
                    dst_reg = faction_region.get(dst["id"], -1)
                    if src_reg >= 0:
                        add_edge(region_allies, seen_ally_edges, src_reg, src, dst)
                    if dst_reg >= 0 and dst_reg != src_reg:
                        add_edge(region_allies, seen_ally_edges, dst_reg, dst, src)
                for eid in src.get("enemies") or []:
                    dst = factions_by_id.get(eid)
                    if not dst:
                        continue
                    dst_reg = faction_region.get(dst["id"], -1)
                    if src_reg >= 0:
                        add_edge(region_enemies, seen_enemy_edges, src_reg, src, dst)
                    if dst_reg >= 0 and dst_reg != src_reg:
                        add_edge(region_enemies, seen_enemy_edges, dst_reg, dst, src)

            all_region_ids = sorted(
                set(region_pixels.keys())
                | set(region_loc_counts.keys())
                | {f["region"] for f in raw_factions if isinstance(f.get("region"), int) and 0 <= f["region"] < 62}
            )

            regions_out: list[dict] = []
            diplomatic_link_pairs: set[tuple[int, int, str]] = set()
            inhabited_count = 0

            for reg in all_region_ids:
                pixels = region_pixels.get(reg, 0)
                b = region_bounds.get(reg)
                if pixels > 0:
                    cx = round(region_sum_x[reg] / pixels, 1)
                    cy = round(region_sum_y[reg] / pixels, 1)
                    bounds = {"minX": b[0], "minY": b[1], "maxX": b[2], "maxY": b[3]}
                elif region_loc_xy_n[reg] > 0:
                    cx = round(region_loc_sum_x[reg] / region_loc_xy_n[reg], 1)
                    cy = round(region_loc_sum_y[reg] / region_loc_xy_n[reg], 1)
                    bounds = None
                else:
                    cx, cy, bounds = 500.0, 250.0, None

                reg_factions = sorted(
                    factions_by_region.get(reg, []),
                    key=lambda f: (0 if f.get("type") == 7 else 1 if f.get("type") in (14, 15) else 2, -int(f.get("power") or 0), f.get("name") or ""),
                )
                province_raw = next((f for f in reg_factions if f.get("type") == 7), None)
                court_raw = next((f for f in reg_factions if f.get("type") == 14), None)
                people_raw = next((f for f in reg_factions if f.get("type") == 15), None)

                province_info = None
                if province_raw:
                    regent_name = None
                    for child_id in province_raw.get("children") or []:
                        child = factions_by_id.get(child_id)
                        if child and child.get("type") == 4:
                            regent_name = child.get("name")
                            break
                    province_info = {
                        "id": province_raw["id"],
                        "name": province_raw["name"],
                        "power": int(province_raw.get("power") or 0),
                        "rulerCode": int(province_raw.get("ruler") or 0),
                        "rulerTitle": _ruler_title(province_raw.get("ruler")),
                        "regentName": regent_name,
                    }

                faction_rows = []
                cast_members = []
                seen_cast: set[str] = set()
                for f in reg_factions:
                    linked_cast = cast_by_faction.get(f["id"], [])
                    for cm in linked_cast:
                        if cm["id"] not in seen_cast:
                            seen_cast.add(cm["id"])
                            cast_members.append(cm)
                    faction_rows.append({
                        "id": f["id"],
                        "name": f["name"],
                        "type": f.get("type"),
                        "typeName": f.get("typeName") or "",
                        "power": int(f.get("power") or 0),
                        "parent": f.get("parent") or 0,
                        "castId": linked_cast[0]["id"] if linked_cast else None,
                    })

                clim_mix = []
                for cidx, count in region_climates.get(reg, Counter()).most_common():
                    cinfo = climates_list[cidx] if 0 <= cidx < len(climates_list) else None
                    if not cinfo:
                        continue
                    clim_mix.append({
                        "index": cidx,
                        "value": cinfo["value"],
                        "name": cinfo["name"],
                        "color": cinfo["color"],
                        "pixels": count,
                        "share": round(count / pixels, 4) if pixels > 0 else 0.0,
                    })

                allies = region_allies.get(reg, [])
                enemies = region_enemies.get(reg, [])
                ally_regions = sorted({e["targetRegion"] for e in allies if e["targetRegion"] is not None and e["targetRegion"] != reg})
                enemy_regions = sorted({e["targetRegion"] for e in enemies if e["targetRegion"] is not None and e["targetRegion"] != reg})
                for ar in ally_regions:
                    diplomatic_link_pairs.add((min(reg, ar), max(reg, ar), "ally"))
                for er in enemy_regions:
                    diplomatic_link_pairs.add((min(reg, er), max(reg, er), "enemy"))

                lcounts = region_loc_counts.get(reg, Counter())
                if lcounts["total"] > 0:
                    inhabited_count += 1

                reg_name = region_names.get(reg) or (province_raw["name"] if province_raw else f"Region {reg}")
                regions_out.append({
                    "id": reg,
                    "name": reg_name,
                    "hasTerritory": pixels > 0,
                    "pixelCount": pixels,
                    "bounds": bounds,
                    "centroid": {"x": cx, "y": cy},
                    "province": province_info,
                    "court": {"id": court_raw["id"], "name": court_raw["name"], "power": int(court_raw.get("power") or 0)} if court_raw else None,
                    "people": {"id": people_raw["id"], "name": people_raw["name"], "power": int(people_raw.get("power") or 0)} if people_raw else None,
                    "factions": faction_rows,
                    "diplomacy": {
                        "allies": allies,
                        "enemies": enemies,
                        "allyRegions": ally_regions,
                        "enemyRegions": enemy_regions,
                    },
                    "climates": clim_mix,
                    "dominantClimate": clim_mix[0]["name"] if clim_mix else None,
                    "locations": {
                        "total": int(lcounts["total"]),
                        "cities": int(lcounts["cities"]),
                        "townsAndVillages": int(lcounts["townsAndVillages"]),
                        "temples": int(lcounts["temples"]),
                        "dungeons": int(lcounts["dungeons"]),
                        "withDungeons": int(lcounts["withDungeons"]),
                        "other": int(lcounts["other"]),
                    },
                    "peoplePack": {
                        "cast": cast_members,
                        "regionalSpawners": sorted(spawners_by_region.get(reg, [])),
                    },
                })

            world_names = (base.get("worldNames") or {}).get("names") or []

            return {
                "overview": {
                    "available": True,
                    "atlas": atlas,
                    "classicMaps": classic_maps,
                    "worldNames": world_names,
                    "climates": climates_list,
                    "pinCategories": list(PIN_CATEGORIES),
                    "regions": regions_out,
                    "counts": {
                        "regions": len(regions_out),
                        "inhabitedRegions": inhabited_count,
                        "locations": len(pins),
                        "diplomaticLinks": len(diplomatic_link_pairs),
                    },
                },
                "masks": {
                    "width": MAP_WIDTH,
                    "height": MAP_HEIGHT,
                    "politicRuns": _encode_flat_rle(politic_grid),
                    "climateRuns": _encode_flat_rle(climate_grid),
                    "climates": climates_list,
                },
                "pins": {
                    "categories": list(PIN_CATEGORIES),
                    "total": len(pins),
                    "pins": pins,
                },
            }

        return self.cache.get(("world-atlas-bundle", imported_path, base_path, atlas_path), stamp, build)

    def overview(self) -> dict:
        return self._build_bundle()["overview"]

    def masks(self) -> dict:
        return self._build_bundle()["masks"]

    def pins(self, region: int | None = None) -> dict:
        data = self._build_bundle()["pins"]
        if region is None:
            return data
        filtered = [p for p in data["pins"] if p[0] == region]
        return {
            "categories": data["categories"],
            "total": len(filtered),
            "pins": filtered,
        }


def register_routes(router, workspace):
    """Register 2D World & Political Atlas endpoints on the CMS router."""

    def get_overview(_req):
        return workspace.world_atlas.overview()

    def get_masks(_req):
        return workspace.world_atlas.masks()

    def get_pins(req):
        raw_region = (req.arg("region", "") or "").strip()
        region = int(raw_region) if raw_region.isdigit() else None
        return workspace.world_atlas.pins(region=region)

    router.get("/api/world-atlas/overview", get_overview)
    router.get("/api/world-atlas/masks", get_masks)
    router.get("/api/world-atlas/pins", get_pins)
