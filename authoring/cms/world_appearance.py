"""Climate/season swaps, dungeon texture tables, and art-replacement overlays for the CMS 3D viewer."""

from collections import defaultdict
import json
from urllib.parse import quote

from cache import FileCache, file_stamp
from locations import DUNGEON_TYPES

ART_PACK_PREFIX = "worldrpg/packs/daggerfall.art-replacements/"
ART_PACK_MOUNT = "/media/art-pack/"

DEFAULT_CLIMATE_VALUES = (
    {"value": 231, "name": "Woodlands", "climateBase": "temperate"},
    {"value": 232, "name": "HauntedWoodlands", "climateBase": "temperate"},
    {"value": 230, "name": "MountainWoods", "climateBase": "temperate"},
    {"value": 226, "name": "Mountain", "climateBase": "mountain"},
    {"value": 224, "name": "Desert", "climateBase": "desert"},
    {"value": 225, "name": "Desert2", "climateBase": "desert"},
    {"value": 229, "name": "Subtropical", "climateBase": "desert"},
    {"value": 228, "name": "Swamp", "climateBase": "swamp"},
    {"value": 227, "name": "Rainforest", "climateBase": "swamp"},
    {"value": 223, "name": "Ocean", "climateBase": "swamp"},
)


def is_desert_climate(climate_value: int) -> bool:
    return climate_value in (224, 225, 229)


def ground_texture_archive(climate_value: int, season: str) -> int:
    """Exact match for DaggerfallClimateArchives.GroundTextureArchive."""
    if climate_value in (223, 227, 228):
        base = 402
    elif climate_value in (224, 225, 229):
        base = 2
    elif climate_value in (226, 230):
        base = 102
    else:
        base = 302
    if season == "rain":
        return base + 2
    if season == "winter" and not is_desert_climate(climate_value):
        return base + 1
    return base


def nature_texture_archive(climate_value: int, season: str) -> int:
    """Exact match for DaggerfallClimateArchives.NatureTextureArchive."""
    mapping = {
        223: 504,
        231: 504,
        224: 503,
        225: 503,
        226: 510,
        227: 500,
        228: 502,
        229: 501,
        230: 506,
        232: 508,
    }
    base = mapping.get(climate_value, 504)
    supports_winter = climate_value in (223, 226, 230, 231, 232)
    return base + 1 if season == "winter" and supports_winter else base


def _art_pack_url(raw_path: str | None) -> str | None:
    if not raw_path or not isinstance(raw_path, str):
        return None
    rel = raw_path
    if rel.startswith(ART_PACK_PREFIX):
        rel = rel[len(ART_PACK_PREFIX):]
    return ART_PACK_MOUNT + quote(rel.lstrip("/"), safe="/")


class WorldAppearanceStore:
    """Cached appearance tables for the CMS 3D block & town viewer."""

    def __init__(self, library, cache: FileCache):
        self.library = library
        self.cache = cache

    def appearance(self) -> dict:
        imp_path = self.library.imported_payload
        art_path = self.library.art_payload
        stamp = (file_stamp(imp_path), file_stamp(art_path))

        def build():
            climates, swaps, dungeon_presets = self._read_imported(imp_path)
            art = self._read_art_replacements(art_path)
            return {
                "climates": climates,
                "seasons": ["summer", "winter", "rain"],
                "swaps": swaps,
                "dungeonPresets": dungeon_presets,
                "artReplacements": art,
            }

        return self.cache.get(("world-appearance", imp_path, art_path), stamp, build)

    def _read_imported(self, imp_path):
        if not imp_path.is_file():
            climates = [self._climate_row(v) for v in DEFAULT_CLIMATE_VALUES]
            return climates, {}, []

        raw = json.loads(imp_path.read_text(encoding="utf-8"))
        clim_sec = raw.get("climate") or {}
        raw_values = clim_sec.get("values") or list(DEFAULT_CLIMATE_VALUES)
        climates = sorted(
            (self._climate_row(v) for v in raw_values if isinstance(v, dict) and "value" in v),
            key=lambda r: (r["climateBase"], r["name"]),
        )

        # Group swaps: swaps[climateBase][season][str(sourceArchive)] = [[firstRecord, lastRecord, targetArchive], ...]
        swaps: dict[str, dict[str, dict[str, list[list[int]]]]] = {}
        for row in clim_sec.get("swaps") or []:
            c_base = row.get("climate")
            season = row.get("season")
            archive = row.get("archive")
            target = row.get("targetArchive")
            if not c_base or not season or archive is None or target is None:
                continue
            first_rec = int(row.get("firstRecord") or 0)
            last_rec = int(row["lastRecord"]) if row.get("lastRecord") is not None else first_rec + 255
            swaps.setdefault(c_base, {}).setdefault(season, {}).setdefault(str(int(archive)), []).append(
                [first_rec, last_rec, int(target)]
            )

        # Extract one representative dungeon texture table preset per dungeonType
        locs = (raw.get("locations") or {}).get("locations") or []
        loc_dtype: dict[tuple[int, int], int] = {}
        for loc in locs:
            dt = loc.get("dungeonType")
            if isinstance(dt, int) and dt >= 0:
                loc_dtype[(int(loc["region"]), int(loc["index"]))] = dt

        seen_types: dict[int, dict] = {}
        for dung in (raw.get("locations") or {}).get("dungeons") or []:
            key = (int(dung["region"]), int(dung["index"]))
            dt = loc_dtype.get(key)
            if dt is None or dt in seen_types:
                continue
            table = dung.get("textureTable") or {}
            remaps = {
                str(int(r["archive"])): int(r["targetArchive"])
                for r in (table.get("remaps") or [])
                if r.get("archive") is not None and r.get("targetArchive") is not None
            }
            if remaps:
                seen_types[dt] = {
                    "id": f"dungeon-{dt}",
                    "dungeonType": dt,
                    "name": DUNGEON_TYPES.get(dt, f"Dungeon {dt}"),
                    "remaps": remaps,
                }
        dungeon_presets = [seen_types[k] for k in sorted(seen_types)]
        return climates, swaps, dungeon_presets

    def _climate_row(self, raw: dict) -> dict:
        val = int(raw["value"])
        return {
            "value": val,
            "name": raw.get("name") or f"Climate {val}",
            "climateBase": raw.get("climateBase") or "temperate",
            "ground": {
                "summer": ground_texture_archive(val, "summer"),
                "winter": ground_texture_archive(val, "winter"),
                "rain": ground_texture_archive(val, "rain"),
            },
            "nature": {
                "summer": nature_texture_archive(val, "summer"),
                "winter": nature_texture_archive(val, "winter"),
                "rain": nature_texture_archive(val, "rain"),
            },
        }

    def _read_art_replacements(self, art_path) -> dict:
        if not art_path.is_file():
            return {"available": False, "surfaces": {}, "scenery": {}, "models": {}, "counts": {"surfaces": 0, "scenery": 0, "models": 0}}
        data = json.loads(art_path.read_text(encoding="utf-8"))
        ar = data.get("artReplacements") or {}

        surfaces: dict[str, str] = {}
        for entry in ar.get("surfaces") or []:
            rep = entry.get("replaces") or {}
            arch = rep.get("archive")
            rec = rep.get("record")
            url = _art_pack_url(entry.get("path"))
            if isinstance(arch, int) and isinstance(rec, int) and url:
                surfaces[f"{arch}-{rec}"] = url

        scenery: dict[str, str] = {}
        for entry in ar.get("scenery") or []:
            rep = entry.get("replaces") or {}
            arch = rep.get("archive")
            rec = rep.get("record")
            url = _art_pack_url(entry.get("path"))
            if isinstance(arch, int) and isinstance(rec, int) and url:
                scenery[f"{arch}-{rec}"] = url

        models: dict[str, dict] = {}
        for mid, entry in (ar.get("models") or {}).items():
            mesh_url = _art_pack_url(entry.get("path"))
            if not mesh_url:
                continue
            slot_overrides: dict[str, dict] = {}
            for s in entry.get("slots") or []:
                slot_idx = s.get("slot")
                if slot_idx is None:
                    continue
                tex = s.get("texture") or {}
                tex_url = _art_pack_url(tex.get("path"))
                mat = s.get("material")
                if tex_url:
                    slot_overrides[str(slot_idx)] = {"textureUrl": tex_url}
                elif mat:
                    slot_overrides[str(slot_idx)] = {"material": mat}
            models[str(mid)] = {"meshUrl": mesh_url, "slots": slot_overrides}

        return {
            "available": True,
            "surfaces": surfaces,
            "scenery": scenery,
            "models": models,
            "counts": {
                "surfaces": len(surfaces),
                "scenery": len(scenery),
                "models": len(models),
            },
        }


def register_routes(router, workspace):
    """Register world appearance (climate swaps, dungeon presets, art replacements) endpoint."""
    router.get("/api/world-blocks/appearance", lambda _req: workspace.world_appearance.appearance())
