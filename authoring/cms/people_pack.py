"""Inspect, cross-link, validate, and edit the git-tracked daggerfall.people content pack."""

from collections import Counter, defaultdict
import math
from pathlib import Path
import re

from base_pack import read_section as read_base_section, sections_stamp as base_sections_stamp
from cache import FileCache, dir_stamp, file_stamp
from packs import PackStore, ValidationError, ValidationIssue
from router import NotFoundError

GENDERS = ("Male", "Female")
INTERACTIONS = ("none", "ambient-bark", "full-dialogue")
HARMS = ("protected", "law-protected-civilian", "combatant")
VISUAL_MODES = ("pool", "classic-slot")
CLIMATE_NAMES = (
    "Ocean", "Desert", "Desert2", "Mountain", "Rainforest",
    "Swamp", "Subtropical", "MountainWoods", "Woodlands", "HauntedWoodlands",
)
CLIMATE_NAMES_LOWER = frozenset(name.lower() for name in CLIMATE_NAMES)
REQUIRED_ARCHETYPES = ("civilian", "static-npc", "clutter-person", "questor", "guard", "combatant")
MAX_ARCHETYPE_VITAL = 10_000
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


def validate_archetypes_document(
    doc,
    *,
    valid_attributes: set[str] | None = None,
    valid_skills: set[str] | None = None,
    items: dict[str, dict] | None = None,
) -> list[ValidationIssue]:
    issues: list[ValidationIssue] = []
    if not isinstance(doc, dict) or not isinstance(doc.get("archetypes"), list):
        return [ValidationIssue("archetypes", "Archetypes file must contain an 'archetypes' array.")]
    seen: set[str] = set()
    for index, entry in enumerate(doc["archetypes"]):
        path = f"archetypes[{index}]"
        if not isinstance(entry, dict):
            issues.append(ValidationIssue(path, "Archetype must be an object."))
            continue
        aid = entry.get("id")
        if not valid_id(aid):
            issues.append(ValidationIssue(f"{path}.id", "ID must be lowercase kebab/identifier characters."))
        elif aid in seen:
            issues.append(ValidationIssue(f"{path}.id", f"Duplicate archetype ID '{aid}'."))
        else:
            seen.add(aid)

        health = entry.get("health")
        if not isinstance(health, dict):
            issues.append(ValidationIssue(f"{path}.health", "Health must be an object with min and max."))
        else:
            hmin = health.get("min")
            hmax = health.get("max")
            if not _is_int(hmin) or not _is_int(hmax) or hmin < 1 or hmax < hmin or hmax > MAX_ARCHETYPE_VITAL:
                issues.append(ValidationIssue(f"{path}.health", f"Invalid health range [{hmin}, {hmax}]."))

        for vital in ("fatigue", "magicka"):
            val = entry.get(vital)
            if not _is_int(val) or val < 0 or val > MAX_ARCHETYPE_VITAL:
                issues.append(ValidationIssue(f"{path}.{vital}", f"{vital} must be an integer in 0..{MAX_ARCHETYPE_VITAL}."))

        armor = entry.get("armor", 0)
        if not _is_int(armor) or armor < -100 or armor > 100:
            issues.append(ValidationIssue(f"{path}.armor", "armor must be an integer in -100..100."))

        level = entry.get("level", 1)
        if not _is_int(level) or level < 1 or level > 100:
            issues.append(ValidationIssue(f"{path}.level", "level must be an integer in 1..100."))

        base_attr = entry.get("baseAttribute")
        if not _is_int(base_attr) or base_attr < 1 or base_attr > 100:
            issues.append(ValidationIssue(f"{path}.baseAttribute", "baseAttribute must be an integer in 1..100."))

        attrs = entry.get("attributes", {})
        if not isinstance(attrs, dict):
            issues.append(ValidationIssue(f"{path}.attributes", "attributes must be an object."))
        else:
            for k, v in attrs.items():
                if valid_attributes and k not in valid_attributes:
                    issues.append(ValidationIssue(f"{path}.attributes.{k}", f"Unknown attribute '{k}'."))
                if not _is_int(v) or v < 1 or v > 100:
                    issues.append(ValidationIssue(f"{path}.attributes.{k}", "Attribute value must be an integer in 1..100."))

        skills = entry.get("skills", {})
        if not isinstance(skills, dict):
            issues.append(ValidationIssue(f"{path}.skills", "skills must be an object."))
        else:
            for k, v in skills.items():
                if valid_skills and k not in valid_skills:
                    issues.append(ValidationIssue(f"{path}.skills.{k}", f"Unknown skill '{k}'."))
                if not _is_int(v) or v < 0 or v > 100:
                    issues.append(ValidationIssue(f"{path}.skills.{k}", "Skill value must be an integer in 0..100."))

        if entry.get("defaultHarm") not in HARMS:
            issues.append(ValidationIssue(f"{path}.defaultHarm", f"defaultHarm must be one of {', '.join(HARMS)}."))
        if entry.get("defaultInteraction") not in INTERACTIONS:
            issues.append(ValidationIssue(f"{path}.defaultInteraction", f"defaultInteraction must be one of {', '.join(INTERACTIONS)}."))
        if type(entry.get("walks")) is not bool:
            issues.append(ValidationIssue(f"{path}.walks", "walks must be true or false."))

        loadout = entry.get("loadout", [])
        if not isinstance(loadout, list):
            issues.append(ValidationIssue(f"{path}.loadout", "loadout must be an array."))
        else:
            for lidx, litem in enumerate(loadout):
                lpath = f"{path}.loadout[{lidx}]"
                if not isinstance(litem, dict):
                    issues.append(ValidationIssue(lpath, "Loadout entry must be an object."))
                    continue
                item_id = litem.get("itemId")
                qty = litem.get("quantity")
                if not isinstance(item_id, str) or not item_id.strip():
                    issues.append(ValidationIssue(f"{lpath}.itemId", "itemId must be non-empty text."))
                elif items:
                    item_def = items.get(item_id)
                    if item_def is None:
                        issues.append(ValidationIssue(f"{lpath}.itemId", f"References unknown item '{item_id}'."))
                    elif item_def.get("kind") != "fungible":
                        issues.append(ValidationIssue(f"{lpath}.itemId", f"Item '{item_id}' must be fungible."))
                if not _is_int(qty) or qty <= 0:
                    issues.append(ValidationIssue(f"{lpath}.quantity", "quantity must be a positive integer."))

    for req_id in REQUIRED_ARCHETYPES:
        if req_id not in seen:
            issues.append(ValidationIssue("archetypes", f"Missing required archetype '{req_id}'."))
    return issues


def validate_cast_document(
    doc,
    factions: dict[int, dict] | None = None,
    archetypes: dict[str, dict] | None = None,
) -> list[ValidationIssue]:
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

        # A placed individual names a homeBlock and exactly one of homeSlotId and homePosition; homeSite pins the home to
        # one location and is required with a position. An unplaced individual names none of them.
        home_block, home_slot, home_position, home_site = (entry.get(key) for key in ("homeBlock", "homeSlotId", "homePosition", "homeSite"))
        if home_block is None:
            if home_slot is not None or home_position is not None or home_site is not None:
                issues.append(ValidationIssue(f"{path}.homeBlock", "A home slot, position or site needs a homeBlock."))
        else:
            if not isinstance(home_block, str) or not home_block.strip():
                issues.append(ValidationIssue(f"{path}.homeBlock", "homeBlock must be non-empty text."))
            if (home_slot is None) == (home_position is None):
                issues.append(ValidationIssue(f"{path}.homeSlotId", "A home names exactly one of homeSlotId and homePosition."))
            if home_slot is not None and (not isinstance(home_slot, str) or not home_slot.strip()):
                issues.append(ValidationIssue(f"{path}.homeSlotId", "homeSlotId must be non-empty text."))
            if home_position is not None and (not isinstance(home_position, list) or len(home_position) != 3
                                              or not all(isinstance(v, (int, float)) for v in home_position)):
                issues.append(ValidationIssue(f"{path}.homePosition", "homePosition must be three numbers."))
            if home_position is not None and home_site is None:
                issues.append(ValidationIssue(f"{path}.homeSite", "A homePosition needs a homeSite to set it in."))
        if home_site is not None and not (isinstance(home_site, dict) and _is_int(home_site.get("region")) and _is_int(home_site.get("index"))):
            issues.append(ValidationIssue(f"{path}.homeSite", "homeSite must be an object with integer region and index."))

        if entry.get("interaction") not in INTERACTIONS:
            issues.append(ValidationIssue(f"{path}.interaction", f"Interaction must be one of {', '.join(INTERACTIONS)}."))
        if entry.get("harm") not in HARMS:
            issues.append(ValidationIssue(f"{path}.harm", f"Harm must be one of {', '.join(HARMS)}."))
        arch = entry.get("archetype")
        if arch is not None:
            if not valid_id(arch):
                issues.append(ValidationIssue(f"{path}.archetype", "Archetype ID must be lowercase kebab/identifier characters."))
            elif archetypes is not None and arch not in archetypes:
                issues.append(ValidationIssue(f"{path}.archetype", f"References unknown archetype '{arch}'."))
    return issues


def validate_spawners_document(doc, archetypes: dict[str, dict] | None = None) -> list[ValidationIssue]:
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
        arch = entry.get("archetype")
        if arch is not None:
            if not valid_id(arch):
                issues.append(ValidationIssue(f"{path}.archetype", "Archetype ID must be lowercase kebab/identifier characters."))
            elif archetypes is not None and arch not in archetypes:
                issues.append(ValidationIssue(f"{path}.archetype", f"References unknown archetype '{arch}'."))
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

        climate_pools = entry.get("climatePools")
        if climate_pools is not None:
            if not isinstance(climate_pools, dict):
                issues.append(ValidationIssue(f"{path}.climatePools", "climatePools must be an object."))
            else:
                for climate_key, pool in climate_pools.items():
                    if not isinstance(climate_key, str) or climate_key.lower() not in CLIMATE_NAMES_LOWER:
                        issues.append(ValidationIssue(f"{path}.climatePools.{climate_key}", f"Unknown climate '{climate_key}'."))
                        continue
                    _validate_pool(pool, f"{path}.climatePools.{climate_key}", issues, require_non_empty=True)
    return issues


def _validate_text_list(
    entries,
    field_prefix: str,
    issues: list[ValidationIssue],
    *,
    require_non_empty: bool = True,
    required_tokens: tuple[str, ...] = (),
):
    if not isinstance(entries, list):
        issues.append(ValidationIssue(field_prefix, "Must be a list of non-empty strings."))
        return
    if require_non_empty and not entries:
        issues.append(ValidationIssue(field_prefix, "Must contain at least one line."))
        return
    for index, line in enumerate(entries):
        path = f"{field_prefix}[{index}]"
        if not isinstance(line, str) or not line.strip():
            issues.append(ValidationIssue(path, "Must be non-empty text."))
            continue
        for token in required_tokens:
            if token not in line:
                issues.append(ValidationIssue(path, f"Must contain placeholder '{token}'."))


def _validate_text_map(
    mapping,
    field_prefix: str,
    issues: list[ValidationIssue],
    *,
    valid_keys: set[str] | frozenset[str] | None = None,
    case_insensitive_keys: bool = False,
    required_tokens: tuple[str, ...] = (),
):
    if mapping is None:
        return
    if not isinstance(mapping, dict):
        issues.append(ValidationIssue(field_prefix, "Must be an object mapping keys to string lists."))
        return
    for key, lines in mapping.items():
        if not isinstance(key, str) or not key.strip():
            issues.append(ValidationIssue(field_prefix, "Map key must be non-empty text."))
            continue
        if valid_keys is not None:
            check_key = key.lower() if case_insensitive_keys else key
            if check_key not in valid_keys:
                issues.append(ValidationIssue(f"{field_prefix}.{key}", f"References unknown key '{key}'."))
        _validate_text_list(lines, f"{field_prefix}.{key}", issues, require_non_empty=True, required_tokens=required_tokens)


def validate_barks_document(doc, archetypes: dict[str, dict] | None = None) -> list[ValidationIssue]:
    issues: list[ValidationIssue] = []
    if not isinstance(doc, dict):
        return [ValidationIssue("barks", "Barks file must be a JSON object.")]
    _validate_text_list(doc.get("defaultBarks"), "defaultBarks", issues, require_non_empty=True)
    _validate_text_map(doc.get("byRole"), "byRole", issues)
    _validate_text_map(
        doc.get("byArchetype"),
        "byArchetype",
        issues,
        valid_keys=set(archetypes.keys()) if archetypes is not None else None,
    )
    _validate_text_map(doc.get("byRegion"), "byRegion", issues)
    _validate_text_map(
        doc.get("byClimate"),
        "byClimate",
        issues,
        valid_keys=CLIMATE_NAMES_LOWER,
        case_insensitive_keys=True,
    )
    referrals = doc.get("referrals")
    if not isinstance(referrals, dict):
        issues.append(ValidationIssue("referrals", "referrals must be an object."))
    else:
        issue_tokens = ("{target}", "{contact}", "{speaker}")
        resp_tokens = ("{speaker}", "{tag}")
        _validate_text_list(referrals.get("defaultIssueTemplates"), "referrals.defaultIssueTemplates", issues, require_non_empty=True, required_tokens=issue_tokens)
        _validate_text_map(referrals.get("issueTemplatesByRegion"), "referrals.issueTemplatesByRegion", issues, required_tokens=issue_tokens)
        _validate_text_map(
            referrals.get("issueTemplatesByClimate"),
            "referrals.issueTemplatesByClimate",
            issues,
            valid_keys=CLIMATE_NAMES_LOWER,
            case_insensitive_keys=True,
            required_tokens=issue_tokens,
        )
        _validate_text_list(referrals.get("defaultResponseTemplates"), "referrals.defaultResponseTemplates", issues, require_non_empty=True, required_tokens=resp_tokens)
        _validate_text_map(referrals.get("responseTemplatesByRole"), "referrals.responseTemplatesByRole", issues, required_tokens=resp_tokens)
        _validate_text_list(referrals.get("contactNames"), "referrals.contactNames", issues, require_non_empty=True)
        _validate_text_map(referrals.get("contactNamesByRegion"), "referrals.contactNamesByRegion", issues)
        _validate_text_map(
            referrals.get("contactNamesByClimate"),
            "referrals.contactNamesByClimate",
            issues,
            valid_keys=CLIMATE_NAMES_LOWER,
            case_insensitive_keys=True,
        )
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

    def _archetypes_path(self) -> Path | None:
        payload = self.packs.read_json(self.root / "payload.json", {}) or {}
        arch_rel = payload.get("archetypesFile")
        if arch_rel:
            return self.root / arch_rel
        default_path = self.root / "archetypes.json"
        return default_path if default_path.is_file() else None

    def _barks_path(self) -> Path | None:
        payload = self.packs.read_json(self.root / "payload.json", {}) or {}
        barks_rel = payload.get("barksFile")
        if barks_rel:
            return self.root / barks_rel
        default_path = self.root / "barks.json"
        return default_path if default_path.is_file() else None

    def available(self) -> bool:
        cast_path, spawners_path, _ = self._paths()
        return cast_path.is_file() and spawners_path.is_file()

    _BASE_LOOKUP_SECTIONS = ("factions", "buildingNames", "vocabulary", "items")

    def _base_lookups(self):
        base_path = self.library.base_payload
        stamp = base_sections_stamp(base_path, self._BASE_LOOKUP_SECTIONS, self.cache)

        def build():
            factions_raw = (read_base_section(base_path, "factions", {}, self.cache) or {}).get("factions") or []
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
            region_names = (read_base_section(base_path, "buildingNames", {}, self.cache) or {}).get("regionNames") or []
            regions = [{"index": i, "key": str(i), "name": name} for i, name in enumerate(region_names)]
            region_by_key = {}
            for entry in regions:
                region_by_key[entry["key"]] = entry
                region_by_key[entry["name"].lower()] = entry
            vocab = read_base_section(base_path, "vocabulary", {}, self.cache) or {}
            valid_attributes = set(vocab.get("attributes") or [])
            valid_skills = set(vocab.get("skills") or [])
            items = {
                row["id"]: row
                for row in (read_base_section(base_path, "items", [], self.cache) or [])
                if isinstance(row, dict) and isinstance(row.get("id"), str)
            }
            return {
                "factions": factions,
                "regions": regions,
                "region_by_key": region_by_key,
                "attributes": valid_attributes,
                "skills": valid_skills,
                "items": items,
            }

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
            base_sections_stamp(base_path, self._BASE_LOOKUP_SECTIONS, self.cache),
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
                for ckey, entries in (sp.get("climatePools") or {}).items():
                    for entry in entries or []:
                        if _is_int(entry.get("archive")) and _is_int(entry.get("record")):
                            visual_spawner_refs[(entry["archive"], entry["record"])].append({
                                "spawnerId": sid, "role": sp.get("role", ""), "pool": f"climate:{ckey}",
                                "regionName": ckey, "weight": entry.get("weight", 0),
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
        climate_pools = [
            {
                "climate": ckey,
                "entries": self._enrich_pool(entries or [], catalog, concept_store, memo),
            }
            for ckey, entries in sorted((sp.get("climatePools") or {}).items())
        ]
        usage = index["spawner_usage"].get(sp["id"], {"slotCount": 0, "blockCount": 0, "byKind": {}, "byBuildingType": []})
        return {
            "id": sp["id"],
            "role": sp["role"],
            "archetype": sp.get("archetype"),
            "interaction": sp["interaction"],
            "harm": sp["harm"],
            "deriveRegionalFaction": bool(sp.get("deriveRegionalFaction", False)),
            "visualMode": sp["visualMode"],
            "defaultPool": default_pool,
            "regionalPools": regional_pools,
            "climatePools": climate_pools,
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
                "spawnersWithClimatePools": sum(1 for s in spawners if len(s["climatePools"]) > 0),
            },
            "enums": {
                "genders": list(GENDERS),
                "interactions": list(INTERACTIONS),
                "harms": list(HARMS),
                "visualModes": list(VISUAL_MODES),
                "races": list(KNOWN_RACES),
                "climates": list(CLIMATE_NAMES),
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
        allowed = {"role", "archetype", "interaction", "harm", "deriveRegionalFaction", "visualMode", "defaultPool", "regionalPools", "climatePools"}
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
                    if "archetype" in body:
                        arch = body["archetype"]
                        if arch is None or (isinstance(arch, str) and not arch.strip()):
                            next_entry.pop("archetype", None)
                        else:
                            next_entry["archetype"] = arch.strip() if isinstance(arch, str) else arch
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
                    if "climatePools" in body:
                        cp = body["climatePools"]
                        if isinstance(cp, dict):
                            next_entry["climatePools"] = {
                                str(k): normalize_pool(v) for k, v in sorted(cp.items())
                            }
                        else:
                            next_entry["climatePools"] = cp
                    updated_list.append(next_entry)
                else:
                    updated_list.append(entry)
            if not found:
                raise NotFoundError(f"Spawner '{spawner_id}' not found.")
            return {"spawners": updated_list}, spawner_id

        def validator(doc):
            arch_path = self._archetypes_path()
            archetypes_by_id = None
            if arch_path is not None and arch_path.is_file():
                arch_doc = self.packs.read_json(arch_path, {}) or {}
                archetypes_by_id = {
                    row["id"]: row for row in arch_doc.get("archetypes", [])
                    if isinstance(row, dict) and isinstance(row.get("id"), str)
                }
            issues = validate_spawners_document(doc, archetypes_by_id)
            _raise_if_issues("Spawner validation failed.", issues)

        self.packs.update_json(spawners_path, mutate, validator=validator, sort_keys=False)
        self.cache.invalidate(("people-pack-index", self.root, self.library.base_payload))
        return self.get_spawner(spawner_id, catalog, concept_store)

    def list_archetypes(self) -> list[dict]:
        arch_path = self._archetypes_path()
        if arch_path is None or not arch_path.is_file():
            return []
        doc = self.packs.read_json(arch_path, {"archetypes": []}) or {"archetypes": []}
        return list(doc.get("archetypes") or [])

    def get_archetype(self, archetype_id: str) -> dict | None:
        for entry in self.list_archetypes():
            if isinstance(entry, dict) and entry.get("id") == archetype_id:
                return entry
        return None

    def update_archetype(self, archetype_id: str, body) -> dict:
        if not isinstance(body, dict):
            raise ValidationError("Archetype update must be a JSON object.")
        arch_path = self._archetypes_path()
        if arch_path is None or not arch_path.is_file():
            raise NotFoundError("People pack has no archetypes.json.")
        lookups = self._base_lookups()
        allowed = {
            "health", "fatigue", "magicka", "armor", "level",
            "baseAttribute", "attributes", "skills",
            "defaultHarm", "defaultInteraction", "walks", "loadout",
        }
        unknown = sorted(set(body) - allowed)
        if unknown:
            raise ValidationError(
                f"Unsupported archetype fields: {', '.join(unknown)}.",
                [ValidationIssue(field, "This field cannot be edited here.") for field in unknown],
            )

        def mutate(doc):
            if not isinstance(doc, dict) or not isinstance(doc.get("archetypes"), list):
                raise ValidationError("Archetypes file is malformed.")
            found = False
            updated_list = []
            for entry in doc["archetypes"]:
                if isinstance(entry, dict) and entry.get("id") == archetype_id:
                    found = True
                    next_entry = dict(entry)
                    for key in allowed:
                        if key in body:
                            next_entry[key] = body[key]
                    updated_list.append(next_entry)
                else:
                    updated_list.append(entry)
            if not found:
                raise NotFoundError(f"Archetype '{archetype_id}' not found.")
            return {"archetypes": updated_list}, archetype_id

        def validator(doc):
            issues = validate_archetypes_document(
                doc,
                valid_attributes=lookups["attributes"] or None,
                valid_skills=lookups["skills"] or None,
                items=lookups["items"] or None,
            )
            _raise_if_issues("Archetype validation failed.", issues)

        self.packs.update_json(arch_path, mutate, validator=validator, sort_keys=False)
        self.cache.invalidate(("people-pack-index", self.root, self.library.base_payload))
        return self.get_archetype(archetype_id)

    def get_barks(self) -> dict | None:
        barks_path = self._barks_path()
        if barks_path is None or not barks_path.is_file():
            return None
        return self.packs.read_json(barks_path, None)

    def update_barks(self, body) -> dict:
        if not isinstance(body, dict):
            raise ValidationError("Barks update must be a JSON object.")
        barks_path = self._barks_path()
        if barks_path is None or not barks_path.is_file():
            raise NotFoundError("People pack has no barks.json.")
        allowed = {"defaultBarks", "byRole", "byArchetype", "byRegion", "byClimate", "referrals"}
        unknown = sorted(set(body) - allowed)
        if unknown:
            raise ValidationError(
                f"Unsupported barks fields: {', '.join(unknown)}.",
                [ValidationIssue(field, "This field cannot be edited here.") for field in unknown],
            )

        def mutate(doc):
            if not isinstance(doc, dict):
                raise ValidationError("Barks file is malformed.")
            next_doc = dict(doc)
            for key in allowed:
                if key in body:
                    next_doc[key] = body[key]
            return next_doc, "barks"

        def validator(doc):
            arch_path = self._archetypes_path()
            archetypes_by_id = None
            if arch_path is not None and arch_path.is_file():
                arch_doc = self.packs.read_json(arch_path, {}) or {}
                archetypes_by_id = {
                    row["id"]: row for row in arch_doc.get("archetypes", [])
                    if isinstance(row, dict) and isinstance(row.get("id"), str)
                }
            issues = validate_barks_document(doc, archetypes_by_id)
            _raise_if_issues("Barks validation failed.", issues)

        self.packs.update_json(barks_path, mutate, validator=validator, sort_keys=False)
        return self.get_barks()

    def update_block_slot(self, body: dict, catalog, concept_store) -> dict:
        """Update position and/or binding fields of a single slot in blocks/{rmb-interiors,rmb-exteriors,rdb-dungeons}.json."""
        if not isinstance(body, dict):
            raise ValidationError("Expected a JSON object for block slot update.")

        kind = (body.get("kind") or "").strip()
        file_by_kind = {
            "rmb-interior": "rmb-interiors.json",
            "rmb-exterior": "rmb-exteriors.json",
            "rdb-dungeon": "rdb-dungeons.json",
        }
        if kind not in file_by_kind:
            raise ValidationError(
                f"Unsupported block slot kind '{kind}'.",
                [ValidationIssue("kind", "Kind must be rmb-interior, rmb-exterior, or rdb-dungeon.")],
            )
        block_key = (body.get("block") or "").strip()
        slot_id = (body.get("slotId") or "").strip()
        if not block_key or not slot_id:
            raise ValidationError(
                "Both 'block' and 'slotId' are required.",
                [ValidationIssue("slotId", "Block and slotId are required.")],
            )
        building_index = body.get("buildingIndex")
        if kind == "rmb-interior":
            if not _is_int(building_index) or building_index < 0:
                raise ValidationError(
                    "buildingIndex must be a non-negative integer for rmb-interior slots.",
                    [ValidationIssue("buildingIndex", "buildingIndex is required for rmb-interior.")],
                )

        cast_path, spawners_path, blocks_dir = self._paths()
        target_path = blocks_dir / file_by_kind[kind]
        if not target_path.is_file():
            raise NotFoundError(f"Block slots file '{file_by_kind[kind]}' not found.")

        index = self._index()
        individuals_by_id = index["individuals_by_id"]
        spawners_by_id = index["spawners_by_id"]
        factions = self._base_lookups()["factions"]

        def mutate(doc: dict):
            blocks = doc.get("blocks") if isinstance(doc, dict) else None
            if not isinstance(blocks, dict) or block_key not in blocks:
                raise NotFoundError(f"Block '{block_key}' not found in {file_by_kind[kind]}.")

            if kind == "rmb-interior":
                buildings = blocks[block_key]
                bkey = str(building_index)
                if not isinstance(buildings, dict) or bkey not in buildings:
                    raise NotFoundError(f"Building #{building_index} not found in '{block_key}'.")
                slot_list = buildings[bkey].get("slots")
            else:
                slot_list = blocks[block_key]

            if not isinstance(slot_list, list):
                raise NotFoundError(f"Slot list missing for '{block_key}'.")

            slot = next((item for item in slot_list if isinstance(item, dict) and item.get("slotId") == slot_id), None)
            if slot is None:
                raise NotFoundError(f"Slot '{slot_id}' not found in '{block_key}'.")

            issues: list[ValidationIssue] = []

            if "position" in body:
                pos = body["position"]
                if (
                    not isinstance(pos, list)
                    or len(pos) != 3
                    or not all(isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) for v in pos)
                ):
                    issues.append(ValidationIssue("position", "Position must be an array of 3 finite numbers [x, y, z]."))
                else:
                    slot["position"] = [round(float(v), 3) for v in pos]

            if "spawner" in body or "castId" in body:
                next_spawner = (body.get("spawner") or "").strip() if isinstance(body.get("spawner"), str) else None
                next_cast = (body.get("castId") or "").strip() if isinstance(body.get("castId"), str) else None
                if next_spawner == "":
                    next_spawner = None
                if next_cast == "":
                    next_cast = None
                if (next_spawner is None) == (next_cast is None):
                    issues.append(ValidationIssue("spawner", "Slot must specify exactly one of spawner or castId."))
                elif next_spawner is not None:
                    if next_spawner not in spawners_by_id:
                        issues.append(ValidationIssue("spawner", f"Unknown spawner '{next_spawner}'."))
                    else:
                        slot["spawner"] = next_spawner
                        slot.pop("castId", None)
                elif next_cast is not None:
                    if next_cast not in individuals_by_id:
                        issues.append(ValidationIssue("castId", f"Unknown castId '{next_cast}'."))
                    else:
                        slot["castId"] = next_cast
                        slot.pop("spawner", None)

            if "interaction" in body:
                interaction = body["interaction"]
                if interaction not in INTERACTIONS:
                    issues.append(ValidationIssue("interaction", f"Interaction must be one of {', '.join(INTERACTIONS)}."))
                else:
                    slot["interaction"] = interaction

            if "deriveRegionalFaction" in body:
                derive = body["deriveRegionalFaction"]
                if not isinstance(derive, bool):
                    issues.append(ValidationIssue("deriveRegionalFaction", "deriveRegionalFaction must be a boolean."))
                else:
                    slot["deriveRegionalFaction"] = derive

            if "factionId" in body:
                fid = body["factionId"]
                if fid is None or fid == "":
                    slot.pop("factionId", None)
                elif not _is_int(fid):
                    issues.append(ValidationIssue("factionId", "factionId must be an integer or null."))
                elif factions and fid not in factions:
                    issues.append(ValidationIssue("factionId", f"Unknown faction ID {fid}."))
                else:
                    slot["factionId"] = fid

            # Verify slot-level faction invariant (spawner slots may not carry Type-4 Individual factions)
            effective_fid = slot.get("factionId")
            if _is_int(effective_fid) and factions and slot.get("castId") is None:
                faction = factions.get(effective_fid)
                if faction and faction.get("type") == INDIVIDUAL_FACTION_TYPE:
                    issues.append(
                        ValidationIssue(
                            "factionId",
                            f"Non-cast spawner slot cannot carry Type-4 Individual faction {effective_fid}.",
                        ),
                    )

            if issues:
                raise ValidationError(issues[0].message, issues)

            return doc, slot_id

        self.packs.update_json(target_path, mutate, sort_keys=False)

        # Return the updated enriched slot from the refreshed index
        refreshed = self._index()
        target_bidx = building_index if kind == "rmb-interior" else None
        for slot in refreshed["slots"]:
            if (
                slot["kind"] == kind
                and slot["block"] == block_key
                and slot["buildingIndex"] == target_bidx
                and slot["slotId"] == slot_id
            ):
                vis = slot["visual"]
                archive = vis.get("archive")
                record = vis.get("record")
                art = (
                    self.enrich_visual(catalog, concept_store, archive, record)
                    if isinstance(archive, int) and isinstance(record, int)
                    else self._npc_art_info(catalog, concept_store, None)
                )
                cast_id = slot.get("castId")
                cast_name = (
                    refreshed["individuals_by_id"][cast_id]["name"]
                    if cast_id and cast_id in refreshed["individuals_by_id"]
                    else None
                )
                return {**slot, "castName": cast_name, "art": art}

        raise NotFoundError(f"Updated slot '{slot_id}' could not be reloaded.")

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
            for entries in (sp.get("climatePools") or {}).values():
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
        arch_path = self._archetypes_path()
        archetypes_doc = self.packs.read_json(arch_path, None) if arch_path is not None else None
        issues: list[ValidationIssue] = []
        archetypes_by_id: dict[str, dict] | None = None
        if arch_path is not None:
            issues += validate_archetypes_document(
                archetypes_doc,
                valid_attributes=lookups["attributes"] or None,
                valid_skills=lookups["skills"] or None,
                items=lookups["items"] or None,
            )
            archetypes_by_id = {
                row["id"]: row for row in (archetypes_doc or {}).get("archetypes", [])
                if isinstance(row, dict) and isinstance(row.get("id"), str)
            }
        issues += validate_cast_document(cast_doc, factions, archetypes_by_id)
        issues += validate_spawners_document(spawners_doc, archetypes_by_id)
        barks_path = self._barks_path()
        barks_doc = self.packs.read_json(barks_path, None) if barks_path is not None else None
        if barks_path is not None:
            issues += validate_barks_document(barks_doc, archetypes_by_id)

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
                # site-exterior keys one location's grid cell (region/index/x,y), whose spawner slots it places there alone.
                if kind not in ("rmb-interior", "rmb-exterior", "rdb-dungeon", "site-exterior"):
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
                "archetypes": len(archetypes_by_id or {}),
                "barks": len((barks_doc or {}).get("defaultBarks") or []),
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

    def get_archetypes_list(_req):
        return workspace.people_pack.list_archetypes()

    def get_archetype_item(req):
        item = workspace.people_pack.get_archetype(req.params["id"])
        if item is None:
            raise NotFoundError(f"Archetype '{req.params['id']}' not found.")
        return item

    def put_archetype_item(req):
        return workspace.people_pack.update_archetype(req.params["id"], req.json())

    def get_barks(_req):
        doc = workspace.people_pack.get_barks()
        if doc is None:
            raise NotFoundError("People pack has no barks.json.")
        return doc

    def put_barks(req):
        return workspace.people_pack.update_barks(req.json())

    def get_validate(_req):
        return workspace.people_pack.validate_all()

    def put_block_slot(req):
        return workspace.people_pack.update_block_slot(req.json(), workspace.catalog(), workspace.concept_store)

    router.get("/api/people-pack/overview", get_overview)
    router.get("/api/people-pack/cast", get_cast_list)
    router.get("/api/people-pack/cast/{id}", get_cast_item)
    router.put("/api/people-pack/cast/{id}", put_cast_item)
    router.get("/api/people-pack/spawners", get_spawner_list)
    router.get("/api/people-pack/spawners/{id}", get_spawner_item)
    router.put("/api/people-pack/spawners/{id}", put_spawner_item)
    router.get("/api/people-pack/archetypes", get_archetypes_list)
    router.get("/api/people-pack/archetypes/{id}", get_archetype_item)
    router.put("/api/people-pack/archetypes/{id}", put_archetype_item)
    router.get("/api/people-pack/barks", get_barks)
    router.put("/api/people-pack/barks", put_barks)
    router.get("/api/people-pack/blocks", get_blocks)
    router.put("/api/people-pack/blocks/slot", put_block_slot)
    router.get("/api/people-pack/visuals", get_visuals)
    router.get("/api/people-pack/validate", get_validate)
