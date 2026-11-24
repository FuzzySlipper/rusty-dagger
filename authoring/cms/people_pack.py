"""Inspect, cross-link, validate, and edit the git-tracked daggerfall.people content pack."""

from collections import Counter, defaultdict
from pathlib import Path
import re

from cache import FileCache, dir_stamp, file_stamp
from packs import PackStore, ValidationError, ValidationIssue
from router import NotFoundError

GENDERS = ("Male", "Female")
INTERACTIONS = ("none", "ambient-bark", "full-dialogue")
HARMS = ("protected", "law-protected-civilian", "combatant")
VISUAL_MODES = ("pool", "classic-slot")
KNOWN_RACES = (
    "breton", "redguard", "nord", "dark-elf", "high-elf", "wood-elf", "khajiit", "argonian", "orc",
)
STATIC_PEOPLE_ARCHIVES = frozenset({
    175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 186, 197, 334, 346, 357,
})
INDIVIDUAL_FACTION_TYPE = 4
VALID_ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]*$")

BUILDING_TYPES = {
    0: "Alchemist",
    1: "House for Sale",
    2: "Armorer",
    3: "Bank",
    4: "Town 4",
    5: "Bookseller",
    6: "Clothing Store",
    7: "Furniture Store",
    8: "Gem Store",
    9: "General Store",
    10: "Library",
    11: "Guild Hall",
    12: "Pawn Shop",
    13: "Weapon Smith",
    14: "Temple",
    15: "Tavern",
    16: "Palace",
    17: "House 1",
    18: "House 2",
    19: "House 3",
    20: "House 4",
    21: "House 5",
    22: "House 6",
    23: "Town 23",
}


def valid_id(value) -> bool:
    return isinstance(value, str) and bool(VALID_ID_RE.match(value))


def building_type_name(building_type: int | None) -> str | None:
    if building_type is None:
        return None
    return BUILDING_TYPES.get(building_type, f"Type {building_type}")


def _is_int(value) -> bool:
    return type(value) is int


def _validate_pool(entries, field_prefix: str, issues: list[ValidationIssue], *, require_non_empty: bool = False):
    if not isinstance(entries, list):
        issues.append(ValidationIssue(field_prefix, "Must be a list of visual pool entries."))
        return
    if require_non_empty and not entries:
        issues.append(ValidationIssue(field_prefix, "Pool must contain at least one entry."))
        return
    for index, entry in enumerate(entries):
        path = f"{field_prefix}[{index}]"
        if not isinstance(entry, dict):
            issues.append(ValidationIssue(path, "Pool entry must be an object."))
            continue
        archive = entry.get("archive")
        record = entry.get("record")
        gender = entry.get("gender")
        weight = entry.get("weight")
        if not _is_int(archive) or archive < 0:
            issues.append(ValidationIssue(f"{path}.archive", "Archive must be a non-negative integer."))
        if not _is_int(record) or record < 0:
            issues.append(ValidationIssue(f"{path}.record", "Record must be a non-negative integer."))
        if gender not in GENDERS:
            issues.append(ValidationIssue(f"{path}.gender", f"Gender must be one of {', '.join(GENDERS)}."))
        if not _is_int(weight) or weight <= 0:
            issues.append(ValidationIssue(f"{path}.weight", "Weight must be a positive integer."))


def validate_cast_document(doc, factions: dict[int, dict] | None = None) -> list[ValidationIssue]:
    issues: list[ValidationIssue] = []
    if not isinstance(doc, dict) or not isinstance(doc.get("individuals"), list):
        return [ValidationIssue("individuals", "Cast file must contain an 'individuals' array.")]
    seen: set[str] = set()
    for index, entry in enumerate(doc["individuals"]):
        path = f"individuals[{index}]"
        if not isinstance(entry, dict):
            issues.append(ValidationIssue(path, "Cast individual must be an object."))
            continue
        cid = entry.get("id")
        if not valid_id(cid):
            issues.append(ValidationIssue(f"{path}.id", "ID must be lowercase kebab/identifier characters."))
        elif cid in seen:
            issues.append(ValidationIssue(f"{path}.id", f"Duplicate cast individual ID '{cid}'."))
        else:
            seen.add(cid)

        faction_id = entry.get("factionId")
        if not _is_int(faction_id) or faction_id <= 0:
            issues.append(ValidationIssue(f"{path}.factionId", "factionId must be a positive integer."))
        elif factions:
            faction = factions.get(faction_id)
            if faction is None:
                issues.append(ValidationIssue(f"{path}.factionId", f"References missing faction {faction_id}."))
            elif faction.get("type") != INDIVIDUAL_FACTION_TYPE:
                issues.append(ValidationIssue(
                    f"{path}.factionId",
                    f"Faction {faction_id} ('{faction.get('name', '?')}') has type {faction.get('type')}, expected Individual (4).",
                ))

        name = entry.get("name")
        if not isinstance(name, str) or not name.strip():
            issues.append(ValidationIssue(f"{path}.name", "Name must be non-empty text."))

        race = entry.get("race")
        if race is not None and (not isinstance(race, str) or not race.strip()):
            issues.append(ValidationIssue(f"{path}.race", "Race must be null or non-empty text."))

        if entry.get("gender") not in GENDERS:
            issues.append(ValidationIssue(f"{path}.gender", f"Gender must be one of {', '.join(GENDERS)}."))

        visual = entry.get("visual")
        if not isinstance(visual, dict):
            issues.append(ValidationIssue(f"{path}.visual", "Visual must be an object with archive and record."))
        else:
            if not _is_int(visual.get("archive")) or visual["archive"] < 0:
                issues.append(ValidationIssue(f"{path}.visual.archive", "Archive must be a non-negative integer."))
            if not _is_int(visual.get("record")) or visual["record"] < 0:
                issues.append(ValidationIssue(f"{path}.visual.record", "Record must be a non-negative integer."))

        for key in ("homeBlock", "homeSlotId"):
            val = entry.get(key)
            if not isinstance(val, str) or not val.strip():
                issues.append(ValidationIssue(f"{path}.{key}", f"{key} must be non-empty text."))

        if entry.get("interaction") not in INTERACTIONS:
            issues.append(ValidationIssue(f"{path}.interaction", f"Interaction must be one of {', '.join(INTERACTIONS)}."))
        if entry.get("harm") not in HARMS:
            issues.append(ValidationIssue(f"{path}.harm", f"Harm must be one of {', '.join(HARMS)}."))
    return issues


def validate_spawners_document(doc) -> list[ValidationIssue]:
    issues: list[ValidationIssue] = []
    if not isinstance(doc, dict) or not isinstance(doc.get("spawners"), list):
        return [ValidationIssue("spawners", "Spawners file must contain a 'spawners' array.")]
    seen: set[str] = set()
    for index, entry in enumerate(doc["spawners"]):
        path = f"spawners[{index}]"
        if not isinstance(entry, dict):
            issues.append(ValidationIssue(path, "Spawner must be an object."))
            continue
        sid = entry.get("id")
        if not valid_id(sid):
            issues.append(ValidationIssue(f"{path}.id", "ID must be lowercase kebab/identifier characters."))
        elif sid in seen:
            issues.append(ValidationIssue(f"{path}.id", f"Duplicate spawner ID '{sid}'."))
        else:
            seen.add(sid)

        role = entry.get("role")
        if not isinstance(role, str) or not role.strip():
            issues.append(ValidationIssue(f"{path}.role", "Role must be non-empty text."))
        if entry.get("interaction") not in INTERACTIONS:
            issues.append(ValidationIssue(f"{path}.interaction", f"Interaction must be one of {', '.join(INTERACTIONS)}."))
        if entry.get("harm") not in HARMS:
            issues.append(ValidationIssue(f"{path}.harm", f"Harm must be one of {', '.join(HARMS)}."))
        if type(entry.get("deriveRegionalFaction")) is not bool:
            issues.append(ValidationIssue(f"{path}.deriveRegionalFaction", "deriveRegionalFaction must be true or false."))

        visual_mode = entry.get("visualMode")
        if visual_mode not in VISUAL_MODES:
            issues.append(ValidationIssue(f"{path}.visualMode", f"visualMode must be one of {', '.join(VISUAL_MODES)}."))

        _validate_pool(entry.get("defaultPool"), f"{path}.defaultPool", issues, require_non_empty=(visual_mode == "pool"))

        regional = entry.get("regionalPools")
        if not isinstance(regional, dict):
            issues.append(ValidationIssue(f"{path}.regionalPools", "regionalPools must be an object."))
        else:
            for region_key, pool in regional.items():
                if not isinstance(region_key, str) or not region_key.strip():
                    issues.append(ValidationIssue(f"{path}.regionalPools", "Region key must be non-empty text."))
                    continue
                _validate_pool(pool, f"{path}.regionalPools.{region_key}", issues, require_non_empty=True)
    return issues


def _raise_if_issues(summary: str, issues: list[ValidationIssue]):
    if issues:
        raise ValidationError(summary, issues)


class PeoplePackStore:
    """Mtime-cached reader, cross-linker, validator, and atomic editor for daggerfall.people."""

    def __init__(self, library, cache: FileCache, packs: PackStore):
        self.library = library
        self.cache = cache
        self.packs = packs

    @property
    def root(self) -> Path:
        return self.library.people_pack

    def _paths(self):
        payload = self.packs.read_json(self.root / "payload.json", {}) or {}
        cast_rel = payload.get("castFile", "cast/individuals.json")
        spawners_rel = payload.get("spawnersFile", "spawners.json")
        blocks_rel = payload.get("blocksFolder", "blocks")
        return self.root / cast_rel, self.root / spawners_rel, self.root / blocks_rel

    def available(self) -> bool:
        cast_path, spawners_path, _ = self._paths()
        return cast_path.is_file() and spawners_path.is_file()

    def _base_lookups(self):
        base_path = self.library.base_payload
        stamp = file_stamp(base_path)

        def build():
            base = self.packs.read_json(base_path, {}) or {}
            factions_raw = (base.get("factions") or {}).get("factions") or []
            factions = {}
            for row in factions_raw:
                if isinstance(row, dict) and _is_int(row.get("id")):
                    factions[row["id"]] = {
                        "id": row["id"],
                        "name": row.get("name") or f"Faction {row['id']}",
                        "type": row.get("type"),
                        "typeName": row.get("typeName") or "",
                        "region": row.get("region"),
                        "parent": row.get("parent"),
                    }
            region_names = (base.get("buildingNames") or {}).get("regionNames") or []
            regions = [{"index": i, "key": str(i), "name": name} for i, name in enumerate(region_names)]
            region_by_key = {}
            for entry in regions:
                region_by_key[entry["key"]] = entry
                region_by_key[entry["name"].lower()] = entry
            return {"factions": factions, "regions": regions, "region_by_key": region_by_key}

        return self.cache.get(("people-base-lookups", base_path), stamp, build)

    def resolve_region(self, key: str) -> dict:
        lookups = self._base_lookups()
        found = lookups["region_by_key"].get(key) or lookups["region_by_key"].get(key.lower())
        if found:
            return {"key": key, "canonicalKey": found["key"], "index": found["index"], "name": found["name"]}
        return {"key": key, "canonicalKey": key, "index": None, "name": key}

    def _index(self):
        cast_path, spawners_path, blocks_dir = self._paths()
        base_path = self.library.base_payload
        stamp = (
            file_stamp(self.root / "payload.json"),
            file_stamp(cast_path),
            file_stamp(spawners_path),
            dir_stamp(blocks_dir, "*.json"),
            file_stamp(base_path),
        )

        def build():
            lookups = self._base_lookups()
            factions = lookups["factions"]
            cast_doc = self.packs.read_json(cast_path, {"individuals": []}) or {"individuals": []}
            spawners_doc = self.packs.read_json(spawners_path, {"spawners": []}) or {"spawners": []}

            individuals = list(cast_doc.get("individuals") or [])
            individuals_by_id = {row["id"]: row for row in individuals if isinstance(row, dict) and "id" in row}
            spawners = list(spawners_doc.get("spawners") or [])
            spawners_by_id = {row["id"]: row for row in spawners if isinstance(row, dict) and "id" in row}

            slots = []
            spawner_slot_counts = Counter()
            spawner_blocks: dict[str, set[str]] = defaultdict(set)
            spawner_by_kind: dict[str, Counter] = defaultdict(Counter)
            spawner_by_building: dict[str, Counter] = defaultdict(Counter)

            cast_slot_counts = Counter()
            cast_blocks: dict[str, set[str]] = defaultdict(set)

            visual_slot_counts = Counter()
            visual_cast_ids: dict[tuple[int, int], list[str]] = defaultdict(list)
            visual_spawner_refs: dict[tuple[int, int], list[dict]] = defaultdict(list)

            for ind in individuals:
                vis = ind.get("visual") or {}
                if _is_int(vis.get("archive")) and _is_int(vis.get("record")):
                    visual_cast_ids[(vis["archive"], vis["record"])].append(ind["id"])

            for sp in spawners:
                sid = sp["id"]
                for entry in sp.get("defaultPool") or []:
                    if _is_int(entry.get("archive")) and _is_int(entry.get("record")):
                        visual_spawner_refs[(entry["archive"], entry["record"])].append({
                            "spawnerId": sid, "role": sp.get("role", ""), "pool": "default",
                            "regionName": None, "weight": entry.get("weight", 0), "gender": entry.get("gender", ""),
                        })
                for rkey, entries in (sp.get("regionalPools") or {}).items():
                    rinfo = self.resolve_region(rkey)
                    for entry in entries or []:
                        if _is_int(entry.get("archive")) and _is_int(entry.get("record")):
                            visual_spawner_refs[(entry["archive"], entry["record"])].append({
                                "spawnerId": sid, "role": sp.get("role", ""), "pool": rkey,
                                "regionName": rinfo["name"], "weight": entry.get("weight", 0),
                                "gender": entry.get("gender", ""),
                            })

            if blocks_dir.is_dir():
                for block_file in sorted(blocks_dir.glob("*.json")):
                    doc = self.packs.read_json(block_file, {}) or {}
                    kind = doc.get("kind") or block_file.stem
                    blocks = doc.get("blocks") or {}
                    for block_key, block_val in blocks.items():
                        if kind == "rmb-interior" and isinstance(block_val, dict):
                            for bidx_str, building in block_val.items():
                                bidx = int(bidx_str) if bidx_str.isdigit() else None
                                btype = building.get("buildingType")
                                bfaction = building.get("buildingFactionId")
                                for slot in building.get("slots") or []:
                                    self._append_slot(
                                        slots, kind, block_key, bidx, btype, bfaction, slot,
                                        individuals_by_id, spawners_by_id, factions,
                                        spawner_slot_counts, spawner_blocks, spawner_by_kind, spawner_by_building,
                                        cast_slot_counts, cast_blocks, visual_slot_counts,
                                    )
                        elif isinstance(block_val, list):
                            for slot in block_val:
                                self._append_slot(
                                    slots, kind, block_key, None, slot.get("buildingType"), slot.get("buildingFactionId"),
                                    slot, individuals_by_id, spawners_by_id, factions,
                                    spawner_slot_counts, spawner_blocks, spawner_by_kind, spawner_by_building,
                                    cast_slot_counts, cast_blocks, visual_slot_counts,
                                )

            spawner_usage = {}
            for sp in spawners:
                sid = sp["id"]
                spawner_usage[sid] = {
                    "slotCount": spawner_slot_counts[sid],
                    "blockCount": len(spawner_blocks[sid]),
                    "byKind": dict(sorted(spawner_by_kind[sid].items())),
                    "byBuildingType": [
                        {"buildingType": bt, "name": building_type_name(bt), "count": count}
                        for bt, count in spawner_by_building[sid].most_common()
                    ],
                }

            cast_usage = {}
            for ind in individuals:
                cid = ind["id"]
                cast_usage[cid] = {
                    "slotCount": cast_slot_counts[cid],
                    "blocks": sorted(cast_blocks[cid]),
                }

            return {
                "individuals": individuals,
                "individuals_by_id": individuals_by_id,
                "spawners": spawners,
                "spawners_by_id": spawners_by_id,
                "slots": slots,
                "spawner_usage": spawner_usage,
                "cast_usage": cast_usage,
                "visual_slot_counts": visual_slot_counts,
                "visual_cast_ids": visual_cast_ids,
                "visual_spawner_refs": visual_spawner_refs,
            }

        return self.cache.get(("people-pack-index", self.root, base_path), stamp, build)

    def _append_slot(
        self, slots, kind, block_key, bidx, btype, bfaction, slot,
        individuals_by_id, spawners_by_id, factions,
        spawner_slot_counts, spawner_blocks, spawner_by_kind, spawner_by_building,
        cast_slot_counts, cast_blocks, visual_slot_counts,
    ):
        cast_id = slot.get("castId")
        spawner_id = slot.get("spawner")
        faction_id = slot.get("factionId")
        faction = factions.get(faction_id) if _is_int(faction_id) else None
        vis = slot.get("visual") or {}
        archive = vis.get("archive")
        record = vis.get("record")

        if cast_id and cast_id in individuals_by_id:
            effective_harm = individuals_by_id[cast_id].get("harm")
        elif spawner_id and spawner_id in spawners_by_id:
            effective_harm = spawners_by_id[spawner_id].get("harm")
        else:
            effective_harm = None

        if spawner_id:
            spawner_slot_counts[spawner_id] += 1
            spawner_blocks[spawner_id].add(block_key)
            spawner_by_kind[spawner_id][kind] += 1
            if btype is not None:
                spawner_by_building[spawner_id][btype] += 1
        if cast_id:
            cast_slot_counts[cast_id] += 1
            cast_blocks[cast_id].add(block_key)
        if _is_int(archive) and _is_int(record):
            visual_slot_counts[(archive, record)] += 1

        slots.append({
            "kind": kind,
            "block": block_key,
            "buildingIndex": bidx,
            "buildingType": btype,
            "buildingTypeName": building_type_name(btype),
            "buildingFactionId": bfaction,
            "slotId": slot.get("slotId", ""),
            "castId": cast_id,
            "spawner": spawner_id,
            "position": slot.get("position") or [0, 0, 0],
            "interaction": slot.get("interaction", ""),
            "effectiveHarm": effective_harm,
            "factionId": faction_id,
            "factionName": faction["name"] if faction else None,
            "deriveRegionalFaction": bool(slot.get("deriveRegionalFaction", False)),
            "visual": {
                "archive": archive,
                "record": record,
                "gender": vis.get("gender", ""),
            },
        })

    def _npc_art_info(self, catalog, concept_store, npc_id: str | None) -> dict:
        if not npc_id or npc_id not in catalog.subjects:
            return {
                "npcId": None, "npcName": None, "npcKind": None, "classic": None,
                "conceptUrl": None, "hasApprovedConcept": False, "hasFinalAsset": False,
                "pipelineCount": 0,
            }
        row = catalog.subjects[npc_id]
        summary = catalog.summary(npc_id, row)
        concept_url = None
        has_approved_concept = False
        if concept_store is not None:
            aliases = tuple(old for old, target in catalog.aliases.items() if target == npc_id and old != npc_id)
            stage = concept_store.stage(npc_id, aliases)
            if stage.get("approved"):
                concept_url = stage["approved"]["url"]
                has_approved_concept = True
        detail = catalog.get(npc_id)
        if not concept_url and detail and detail.get("concept"):
            concept_url = detail["concept"]["url"]
            has_approved_concept = bool(detail["concept"].get("approved"))
        has_final = bool(detail and detail.get("final"))
        return {
            "npcId": npc_id,
            "npcName": summary["name"],
            "npcKind": summary["kind"],
            "classic": summary["classic"],
            "conceptUrl": concept_url,
            "hasApprovedConcept": has_approved_concept,
            "hasFinalAsset": has_final,
            "pipelineCount": len(summary["pipelineIds"]),
        }

    def enrich_visual(self, catalog, concept_store, archive: int, record: int, memo: dict | None = None) -> dict:
        pair_key = f"{archive}-{record}"
        npc_id = catalog.aliases.get(pair_key)
        if memo is not None and npc_id in memo:
            art = memo[npc_id]
        else:
            art = self._npc_art_info(catalog, concept_store, npc_id)
            if memo is not None and npc_id:
                memo[npc_id] = art
        return {"archive": archive, "record": record, "key": pair_key} | art

    def _enrich_cast(self, ind: dict, index: dict, lookups: dict, catalog, concept_store, memo: dict) -> dict:
        vis = ind.get("visual") or {}
        archive, record = vis.get("archive", -1), vis.get("record", -1)
        art = self.enrich_visual(catalog, concept_store, archive, record, memo)
        faction = lookups["factions"].get(ind.get("factionId"))
        shared_with = [cid for cid in index["visual_cast_ids"].get((archive, record), []) if cid != ind["id"]]
        usage = index["cast_usage"].get(ind["id"], {"slotCount": 0, "blocks": []})
        return {
            **ind,
            "factionName": faction["name"] if faction else None,
            "art": art,
            "usage": usage,
            "sharedVisualWith": shared_with,
            "needsConceptArt": not art["hasApprovedConcept"],
            "needsFinalArt": not art["hasFinalAsset"],
        }

    def _enrich_pool(self, entries: list[dict], catalog, concept_store, memo: dict) -> list[dict]:
        total_weight = sum(entry.get("weight", 0) for entry in entries) or 1
        out = []
        for entry in entries:
            art = self.enrich_visual(catalog, concept_store, entry["archive"], entry["record"], memo)
            weight = entry.get("weight", 0)
            out.append({
                "archive": entry["archive"],
                "record": entry["record"],
                "gender": entry["gender"],
                "weight": weight,
                "share": round(weight / total_weight, 4),
                "art": art,
            })
        return out

    def _enrich_spawner(self, sp: dict, index: dict, catalog, concept_store, memo: dict) -> dict:
        default_pool = self._enrich_pool(sp.get("defaultPool") or [], catalog, concept_store, memo)
        regional_pools = []
        for rkey, entries in (sp.get("regionalPools") or {}).items():
            rinfo = self.resolve_region(rkey)
            regional_pools.append({
                "regionKey": rkey,
                "canonicalKey": rinfo["canonicalKey"],
                "regionIndex": rinfo["index"],
                "regionName": rinfo["name"],
                "entries": self._enrich_pool(entries or [], catalog, concept_store, memo),
            })
        regional_pools.sort(key=lambda item: (item["regionIndex"] is None, item["regionIndex"] or 0, item["regionName"]))
        usage = index["spawner_usage"].get(sp["id"], {"slotCount": 0, "blockCount": 0, "byKind": {}, "byBuildingType": []})
        return {
            "id": sp["id"],
            "role": sp["role"],
            "interaction": sp["interaction"],
            "harm": sp["harm"],
            "deriveRegionalFaction": bool(sp.get("deriveRegionalFaction", False)),
            "visualMode": sp["visualMode"],
            "defaultPool": default_pool,
            "regionalPools": regional_pools,
            "usage": usage,
        }

    def summary_counts(self, catalog, concept_store) -> dict:
        if not self.available():
            return {"available": False, "castCount": 0, "spawnerCount": 0, "slotCount": 0, "castNeedingArt": 0}
        index = self._index()
        memo: dict = {}
        needing = 0
        for ind in index["individuals"]:
            vis = ind.get("visual") or {}
            art = self.enrich_visual(catalog, concept_store, vis.get("archive", -1), vis.get("record", -1), memo)
            if not art["hasApprovedConcept"]:
                needing += 1
        return {
            "available": True,
            "castCount": len(index["individuals"]),
            "spawnerCount": len(index["spawners"]),
            "slotCount": len(index["slots"]),
            "castNeedingArt": needing,
        }

    def overview(self, catalog, concept_store) -> dict:
        index = self._index()
        lookups = self._base_lookups()
        memo: dict = {}
        cast = [self._enrich_cast(ind, index, lookups, catalog, concept_store, memo) for ind in index["individuals"]]
        spawners = [self._enrich_spawner(sp, index, catalog, concept_store, memo) for sp in index["spawners"]]
        return {
            "available": self.available(),
            "counts": {
                "cast": len(cast),
                "spawners": len(spawners),
                "slots": len(index["slots"]),
                "castNeedingConceptArt": sum(1 for c in cast if c["needsConceptArt"]),
                "castNeedingFinalArt": sum(1 for c in cast if c["needsFinalArt"]),
                "castSharingVisual": sum(1 for c in cast if c["sharedVisualWith"]),
                "spawnersSingleVisual": sum(1 for s in spawners if len(s["defaultPool"]) <= 1),
                "spawnersWithRegionalPools": sum(1 for s in spawners if len(s["regionalPools"]) > 0),
            },
            "enums": {
                "genders": list(GENDERS),
                "interactions": list(INTERACTIONS),
                "harms": list(HARMS),
                "visualModes": list(VISUAL_MODES),
                "races": list(KNOWN_RACES),
                "buildingTypes": [{"id": k, "name": v} for k, v in sorted(BUILDING_TYPES.items())],
                "regions": lookups["regions"],
            },
            "cast": cast,
            "spawners": spawners,
        }

    def list_cast(self, catalog, concept_store) -> list[dict]:
        index = self._index()
        lookups = self._base_lookups()
        memo: dict = {}
        return [self._enrich_cast(ind, index, lookups, catalog, concept_store, memo) for ind in index["individuals"]]

    def get_cast(self, cast_id: str, catalog, concept_store) -> dict | None:
        index = self._index()
        ind = index["individuals_by_id"].get(cast_id)
        if not ind:
            return None
        lookups = self._base_lookups()
        memo: dict = {}
        enriched = self._enrich_cast(ind, index, lookups, catalog, concept_store, memo)
        slots = [slot for slot in index["slots"] if slot["castId"] == cast_id]
        return enriched | {"slots": slots}

    def update_cast(self, cast_id: str, body, catalog, concept_store) -> dict:
        if not isinstance(body, dict):
            raise ValidationError("Cast update must be a JSON object.")
        cast_path, _, _ = self._paths()
        lookups = self._base_lookups()
        allowed = {"name", "race", "gender", "interaction", "harm", "visual"}
        unknown = sorted(set(body) - allowed)
        if unknown:
            raise ValidationError(
                f"Unsupported cast fields: {', '.join(unknown)}.",
                [ValidationIssue(field, "This field cannot be edited here.") for field in unknown],
            )

        def mutate(doc):
            if not isinstance(doc, dict) or not isinstance(doc.get("individuals"), list):
                raise ValidationError("Cast file is malformed.")
            found = False
            updated_list = []
            for entry in doc["individuals"]:
                if isinstance(entry, dict) and entry.get("id") == cast_id:
                    found = True
                    next_entry = dict(entry)
                    if "name" in body:
                        next_entry["name"] = body["name"].strip() if isinstance(body["name"], str) else body["name"]
                    if "race" in body:
                        race = body["race"]
                        next_entry["race"] = None if race is None or (isinstance(race, str) and not race.strip()) else (
                            race.strip() if isinstance(race, str) else race
                        )
                    if "gender" in body:
                        next_entry["gender"] = body["gender"]
                    if "interaction" in body:
                        next_entry["interaction"] = body["interaction"]
                    if "harm" in body:
                        next_entry["harm"] = body["harm"]
                    if "visual" in body:
                        vis = body["visual"]
                        if isinstance(vis, dict):
                            next_entry["visual"] = {"archive": vis.get("archive"), "record": vis.get("record")}
                        else:
                            next_entry["visual"] = vis
                    updated_list.append(next_entry)
                else:
                    updated_list.append(entry)
            if not found:
                raise NotFoundError(f"Cast individual '{cast_id}' not found.")
            return {"individuals": updated_list}, cast_id

        def validator(doc):
            issues = validate_cast_document(doc, lookups["factions"] or None)
            _raise_if_issues("Cast individual validation failed.", issues)

        self.packs.update_json(cast_path, mutate, validator=validator, sort_keys=False)
        self.cache.invalidate(("people-pack-index", self.root, self.library.base_payload))
        return self.get_cast(cast_id, catalog, concept_store)

    def list_spawners(self, catalog, concept_store) -> list[dict]:
        index = self._index()
        memo: dict = {}
        return [self._enrich_spawner(sp, index, catalog, concept_store, memo) for sp in index["spawners"]]

    def get_spawner(self, spawner_id: str, catalog, concept_store) -> dict | None:
        index = self._index()
        sp = index["spawners_by_id"].get(spawner_id)
        if not sp:
            return None
        memo: dict = {}
        return self._enrich_spawner(sp, index, catalog, concept_store, memo)

    def update_spawner(self, spawner_id: str, body, catalog, concept_store) -> dict:
        if not isinstance(body, dict):
            raise ValidationError("Spawner update must be a JSON object.")
        _, spawners_path, _ = self._paths()
        allowed = {"role", "interaction", "harm", "deriveRegionalFaction", "visualMode", "defaultPool", "regionalPools"}
        unknown = sorted(set(body) - allowed)
        if unknown:
            raise ValidationError(
                f"Unsupported spawner fields: {', '.join(unknown)}.",
                [ValidationIssue(field, "This field cannot be edited here.") for field in unknown],
            )

        def normalize_pool(pool):
            if not isinstance(pool, list):
                return pool
            return [
                {
                    "archive": item.get("archive"),
                    "record": item.get("record"),
                    "gender": item.get("gender"),
                    "weight": item.get("weight"),
                } if isinstance(item, dict) else item
                for item in pool
            ]

        def mutate(doc):
            if not isinstance(doc, dict) or not isinstance(doc.get("spawners"), list):
                raise ValidationError("Spawners file is malformed.")
            found = False
            updated_list = []
            for entry in doc["spawners"]:
                if isinstance(entry, dict) and entry.get("id") == spawner_id:
                    found = True
                    next_entry = dict(entry)
                    if "role" in body:
                        next_entry["role"] = body["role"].strip() if isinstance(body["role"], str) else body["role"]
                    if "interaction" in body:
                        next_entry["interaction"] = body["interaction"]
                    if "harm" in body:
                        next_entry["harm"] = body["harm"]
                    if "deriveRegionalFaction" in body:
                        next_entry["deriveRegionalFaction"] = body["deriveRegionalFaction"]
                    if "visualMode" in body:
                        next_entry["visualMode"] = body["visualMode"]
                    if "defaultPool" in body:
                        next_entry["defaultPool"] = normalize_pool(body["defaultPool"])
                    if "regionalPools" in body:
                        rp = body["regionalPools"]
                        if isinstance(rp, dict):
                            next_entry["regionalPools"] = {
                                str(k): normalize_pool(v) for k, v in sorted(rp.items(), key=lambda kv: (not str(kv[0]).isdigit(), int(kv[0]) if str(kv[0]).isdigit() else 0, str(kv[0])))
                            }
                        else:
                            next_entry["regionalPools"] = rp
                    updated_list.append(next_entry)
                else:
                    updated_list.append(entry)
            if not found:
                raise NotFoundError(f"Spawner '{spawner_id}' not found.")
            return {"spawners": updated_list}, spawner_id

        def validator(doc):
            issues = validate_spawners_document(doc)
            _raise_if_issues("Spawner validation failed.", issues)

        self.packs.update_json(spawners_path, mutate, validator=validator, sort_keys=False)
        self.cache.invalidate(("people-pack-index", self.root, self.library.base_payload))
        return self.get_spawner(spawner_id, catalog, concept_store)

    def query_blocks(
        self,
        catalog,
        concept_store,
        *,
        q: str | None = None,
        spawner: str | None = None,
        cast_id: str | None = None,
        kind: str | None = None,
        building_type: int | None = None,
        derive_regional: bool | None = None,
        interaction: str | None = None,
        harm: str | None = None,
        page: int = 1,
        page_size: int = 50,
    ) -> dict:
        index = self._index()
        terms = q.lower().split() if q and q.strip() else []
        matched = []
        blocks_seen = set()
        by_kind = Counter()
        by_building = Counter()

        for slot in index["slots"]:
            if spawner and slot["spawner"] != spawner:
                continue
            if cast_id and slot["castId"] != cast_id:
                continue
            if kind and slot["kind"] != kind:
                continue
            if building_type is not None and slot["buildingType"] != building_type:
                continue
            if derive_regional is not None and slot["deriveRegionalFaction"] != derive_regional:
                continue
            if interaction and slot["interaction"] != interaction:
                continue
            if harm and slot["effectiveHarm"] != harm:
                continue
            if terms:
                haystack = " ".join(str(v) for v in (
                    slot["block"], slot["slotId"], slot["spawner"] or "", slot["castId"] or "",
                    slot["buildingTypeName"] or "", slot["factionName"] or "",
                    f"{slot['visual']['archive']}-{slot['visual']['record']}",
                )).lower()
                if not all(term in haystack for term in terms):
                    continue
            matched.append(slot)
            blocks_seen.add(slot["block"])
            by_kind[slot["kind"]] += 1
            if slot["buildingType"] is not None:
                by_building[slot["buildingType"]] += 1

        page_size = max(1, min(200, page_size))
        total = len(matched)
        total_pages = max(1, (total + page_size - 1) // page_size)
        page = max(1, min(page, total_pages))
        start = (page - 1) * page_size
        page_slice = matched[start:start + page_size]

        memo: dict = {}
        items = []
        for slot in page_slice:
            vis = slot["visual"]
            art = self.enrich_visual(catalog, concept_store, vis["archive"], vis["record"], memo)
            items.append(slot | {"art": art})

        return {
            "total": total,
            "page": page,
            "pageSize": page_size,
            "items": items,
            "summary": {
                "blockCount": len(blocks_seen),
                "byKind": dict(sorted(by_kind.items())),
                "byBuildingType": [
                    {"buildingType": bt, "name": building_type_name(bt), "count": count}
                    for bt, count in by_building.most_common()
                ],
            },
        }

    def visual_options(self, catalog, concept_store, q: str | None = None) -> list[dict]:
        index = self._index()
        pairs: set[tuple[int, int]] = set()
        for alias in catalog.aliases:
            arch_str, sep, rec_str = alias.partition("-")
            if sep and arch_str.isdigit() and rec_str.isdigit():
                archive, record = int(arch_str), int(rec_str)
                if archive in STATIC_PEOPLE_ARCHIVES:
                    pairs.add((archive, record))
        for ind in index["individuals"]:
            vis = ind.get("visual") or {}
            if _is_int(vis.get("archive")) and _is_int(vis.get("record")):
                pairs.add((vis["archive"], vis["record"]))
        for sp in index["spawners"]:
            for entry in sp.get("defaultPool") or []:
                pairs.add((entry["archive"], entry["record"]))
            for entries in (sp.get("regionalPools") or {}).values():
                for entry in entries or []:
                    pairs.add((entry["archive"], entry["record"]))

        terms = q.lower().split() if q and q.strip() else []
        memo: dict = {}
        results = []
        for archive, record in sorted(pairs):
            art = self.enrich_visual(catalog, concept_store, archive, record, memo)
            if terms:
                haystack = f"{archive}-{record} {archive}.{record} {art['npcId'] or ''} {art['npcName'] or ''} {art['npcKind'] or ''}".lower()
                if not all(term in haystack for term in terms):
                    continue
            results.append(art | {
                "castCount": len(index["visual_cast_ids"].get((archive, record), ())),
                "spawnerCount": len({ref["spawnerId"] for ref in index["visual_spawner_refs"].get((archive, record), ())}),
                "slotCount": index["visual_slot_counts"].get((archive, record), 0),
            })
        return results

    def usage_for_npc(self, npc_id: str, catalog, concept_store) -> dict:
        """Reverse Art -> Data links for a People (npc-###) character detail page."""
        if not self.available():
            return {"visualKeys": [], "cast": [], "spawners": [], "slotCount": 0}
        index = self._index()
        lookups = self._base_lookups()
        pairs = []
        for alias, target in catalog.aliases.items():
            if target != npc_id:
                continue
            arch_str, sep, rec_str = alias.partition("-")
            if sep and arch_str.isdigit() and rec_str.isdigit():
                pairs.append((int(arch_str), int(rec_str)))
        pairs.sort()

        cast_ids = []
        spawner_refs = []
        slot_count = 0
        for pair in pairs:
            slot_count += index["visual_slot_counts"].get(pair, 0)
            for cid in index["visual_cast_ids"].get(pair, ()):
                if cid not in cast_ids:
                    cast_ids.append(cid)
            for ref in index["visual_spawner_refs"].get(pair, ()):
                spawner_refs.append(ref | {"archive": pair[0], "record": pair[1]})

        cast_rows = []
        for cid in cast_ids:
            ind = index["individuals_by_id"].get(cid)
            if ind:
                faction = lookups["factions"].get(ind.get("factionId"))
                cast_rows.append({
                    "id": ind["id"],
                    "name": ind["name"],
                    "race": ind.get("race"),
                    "gender": ind["gender"],
                    "factionId": ind["factionId"],
                    "factionName": faction["name"] if faction else None,
                    "homeBlock": ind["homeBlock"],
                    "homeSlotId": ind["homeSlotId"],
                    "interaction": ind["interaction"],
                    "harm": ind["harm"],
                    "visual": ind["visual"],
                })

        return {
            "visualKeys": [{"archive": a, "record": r, "key": f"{a}-{r}"} for a, r in pairs],
            "cast": cast_rows,
            "spawners": spawner_refs,
            "slotCount": slot_count,
        }

    def validate_all(self) -> dict:
        cast_path, spawners_path, blocks_dir = self._paths()
        lookups = self._base_lookups()
        factions = lookups["factions"] or None

        cast_doc = self.packs.read_json(cast_path, None)
        spawners_doc = self.packs.read_json(spawners_path, None)
        issues = validate_cast_document(cast_doc, factions) + validate_spawners_document(spawners_doc)

        individuals_by_id = {
            row["id"]: row for row in (cast_doc or {}).get("individuals", [])
            if isinstance(row, dict) and isinstance(row.get("id"), str)
        }
        spawners_by_id = {
            row["id"]: row for row in (spawners_doc or {}).get("spawners", [])
            if isinstance(row, dict) and isinstance(row.get("id"), str)
        }

        slot_count = 0
        if blocks_dir.is_dir():
            for block_file in sorted(blocks_dir.glob("*.json")):
                doc = self.packs.read_json(block_file, {}) or {}
                kind = doc.get("kind")
                blocks = doc.get("blocks") or {}
                if kind not in ("rmb-interior", "rmb-exterior", "rdb-dungeon"):
                    issues.append(ValidationIssue(block_file.name, f"Unsupported block kind '{kind}'."))
                    continue
                for block_key, block_val in blocks.items():
                    groups = []
                    if kind == "rmb-interior" and isinstance(block_val, dict):
                        for bidx, bobj in block_val.items():
                            groups.append((f"{block_key}[{bidx}]", (bobj or {}).get("slots") or []))
                    elif isinstance(block_val, list):
                        groups.append((block_key, block_val))
                    for scope, slot_list in groups:
                        seen_slots = set()
                        for slot in slot_list:
                            slot_count += 1
                            sid = slot.get("slotId")
                            if not sid or sid in seen_slots:
                                issues.append(ValidationIssue(f"{scope}/{sid}", "Missing or duplicate slotId."))
                            else:
                                seen_slots.add(sid)
                            cid = slot.get("castId")
                            spid = slot.get("spawner")
                            if (cid is None) == (spid is None):
                                issues.append(ValidationIssue(f"{scope}/{sid}", "Must specify exactly one of castId or spawner."))
                            elif cid is not None and cid not in individuals_by_id:
                                issues.append(ValidationIssue(f"{scope}/{sid}", f"References unknown castId '{cid}'."))
                            elif spid is not None and spid not in spawners_by_id:
                                issues.append(ValidationIssue(f"{scope}/{sid}", f"References unknown spawner '{spid}'."))
                            fid = slot.get("factionId")
                            if _is_int(fid) and factions:
                                faction = factions.get(fid)
                                if faction is None:
                                    issues.append(ValidationIssue(f"{scope}/{sid}", f"References missing faction {fid}."))
                                elif cid is None and faction.get("type") == INDIVIDUAL_FACTION_TYPE:
                                    issues.append(ValidationIssue(
                                        f"{scope}/{sid}",
                                        f"Non-cast spawner slot illegally carries Type-4 Individual faction {fid}.",
                                    ))

        return {
            "valid": len(issues) == 0,
            "issues": [issue.to_dict() for issue in issues],
            "counts": {
                "cast": len(individuals_by_id),
                "spawners": len(spawners_by_id),
                "slots": slot_count,
            },
        }


def _parse_optional_int(raw: str | None, name: str) -> int | None:
    if raw is None or raw == "":
        return None
    try:
        return int(raw)
    except ValueError as error:
        raise ValueError(f"Query parameter '{name}' must be an integer.") from error


def _parse_optional_bool(raw: str | None, name: str) -> bool | None:
    if raw is None or raw == "":
        return None
    lowered = raw.lower()
    if lowered in ("1", "true", "yes"):
        return True
    if lowered in ("0", "false", "no"):
        return False
    raise ValueError(f"Query parameter '{name}' must be true or false.")


def register_routes(router, workspace):
    """Register daggerfall.people inspection, cross-reference, validation, and edit routes."""

    def get_overview(_req):
        return workspace.people_pack.overview(workspace.catalog(), workspace.concept_store)

    def get_cast_list(_req):
        return workspace.people_pack.list_cast(workspace.catalog(), workspace.concept_store)

    def get_cast_item(req):
        item = workspace.people_pack.get_cast(req.params["id"], workspace.catalog(), workspace.concept_store)
        if item is None:
            raise NotFoundError(f"Cast individual '{req.params['id']}' not found.")
        return item

    def put_cast_item(req):
        return workspace.people_pack.update_cast(req.params["id"], req.json(), workspace.catalog(), workspace.concept_store)

    def get_spawner_list(_req):
        return workspace.people_pack.list_spawners(workspace.catalog(), workspace.concept_store)

    def get_spawner_item(req):
        item = workspace.people_pack.get_spawner(req.params["id"], workspace.catalog(), workspace.concept_store)
        if item is None:
            raise NotFoundError(f"Spawner '{req.params['id']}' not found.")
        return item

    def put_spawner_item(req):
        return workspace.people_pack.update_spawner(req.params["id"], req.json(), workspace.catalog(), workspace.concept_store)

    def get_blocks(req):
        return workspace.people_pack.query_blocks(
            workspace.catalog(),
            workspace.concept_store,
            q=req.arg("q"),
            spawner=req.arg("spawner"),
            cast_id=req.arg("castId"),
            kind=req.arg("kind"),
            building_type=_parse_optional_int(req.arg("buildingType"), "buildingType"),
            derive_regional=_parse_optional_bool(req.arg("deriveRegional"), "deriveRegional"),
            interaction=req.arg("interaction"),
            harm=req.arg("harm"),
            page=_parse_optional_int(req.arg("page"), "page") or 1,
            page_size=_parse_optional_int(req.arg("pageSize"), "pageSize") or 50,
        )

    def get_visuals(req):
        return workspace.people_pack.visual_options(workspace.catalog(), workspace.concept_store, q=req.arg("q"))

    def get_validate(_req):
        return workspace.people_pack.validate_all()

    router.get("/api/people-pack/overview", get_overview)
    router.get("/api/people-pack/cast", get_cast_list)
    router.get("/api/people-pack/cast/{id}", get_cast_item)
    router.put("/api/people-pack/cast/{id}", put_cast_item)
    router.get("/api/people-pack/spawners", get_spawner_list)
    router.get("/api/people-pack/spawners/{id}", get_spawner_item)
    router.put("/api/people-pack/spawners/{id}", put_spawner_item)
    router.get("/api/people-pack/blocks", get_blocks)
    router.get("/api/people-pack/visuals", get_visuals)
    router.get("/api/people-pack/validate", get_validate)
