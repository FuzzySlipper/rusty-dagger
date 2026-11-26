"""Unified read-only Actors inspector over daggerfall.base/actors/{templates,specs}/.

Provides single-parent inheritance resolution with per-field provenance, direct children,
level/gender/region effective previews, reverse references (spawners, encounter tables,
block slots, offices, board seats), and bidirectional links to npc-### / creature-### art.
"""

from collections import Counter, defaultdict
from pathlib import Path

from base_pack import read_section as read_base_section, sections_stamp as base_sections_stamp
from cache import FileCache, file_stamp
from library import read_json
from router import NotFoundError

TEMPLATE_SECTIONS = (
    ("bases", "actorTemplateBases"),
    ("creatures", "actorTemplateCreatures"),
    ("classes", "actorTemplateClasses"),
    ("roles", "actorTemplateRoles"),
)

SPEC_SECTIONS = (
    ("player", "actorSpecsPlayer"),
    ("creature", "actorSpecsCreatures"),
    ("enemy", "actorSpecsEnemies"),
    ("town", "actorSpecsTownsfolk"),
    ("cast", "actorSpecsCast"),
)

ATTRIBUTE_ORDER = (
    "strength", "intelligence", "willpower", "agility",
    "endurance", "personality", "speed", "luck",
)

LOOKUP_SECTIONS = (
    "vocabulary",
    "actorTemplateBases",
    "actorTemplateCreatures",
    "actorTemplateClasses",
    "actorTemplateRoles",
    "actorSpecsPlayer",
    "actorSpecsCreatures",
    "actorSpecsEnemies",
    "actorSpecsTownsfolk",
    "actorSpecsCast",
    "catalogs",
    "encounters",
    "enemySpells",
    "magic",
    "boardSeats",
    "factions",
    "buildingNames",
)

MAGIC_SCHOOLS = (
    "destruction", "restoration", "illusion", "alteration", "thaumaturgy", "mysticism",
)

FIRST_HUMAN_MOBILE = 128

# Canonical region -> default playable race mapping (matches Daggerfall region demographics).
_REGION_DEFAULT_RACE = {
    0: "redguard", 1: "redguard", 11: "redguard", 20: "redguard", 22: "redguard",
    43: "redguard", 44: "redguard", 45: "redguard", 46: "redguard", 47: "redguard",
    48: "redguard", 49: "redguard", 50: "redguard", 51: "redguard", 53: "redguard",
    54: "redguard", 55: "redguard", 56: "redguard", 61: "redguard",
    9: "high-elf",
    26: "orc",
}

_FIELD_DISPLAY_ORDER = (
    "careerRef",
    "scaling",
    "level",
    "health",
    "hitPointsPerLevel",
    "fatigue",
    "magicka",
    "armor",
    "weight",
    "xpReward",
    "tactic",
    "tendencies",
    "action",
    "attacks",
    "loadout",
    "gearPolicy",
    "fixedEquipmentVariant",
    "bodyWeightMode",
    "minMetalToHit",
    "lootTableKey",
    "castsMagic",
    "spellPolicy",
    "spells",
    "movement",
    "canOpenDoors",
    "seesThroughInvisibility",
    "prefersRanged",
    "hasRangedAttack1",
    "hasRangedAttack2",
    "usesNaturalArmor",
    "takesExtraBluntDamage",
    "isPlayer",
    "isCombatEncounter",
    "cityWatchArrest",
    "defaultHarm",
    "defaultInteraction",
    "walks",
    "allowedGenders",
    "fixedRace",
    "allowedRaces",
    "enemyGroup",
    "languageSkill",
    "traits",
    "attackModifierFlags",
    "resistanceFlags",
    "immunityFlags",
    "lowToleranceFlags",
    "criticalWeaknessFlags",
)


def _is_int(value) -> bool:
    return type(value) is int


def _clone(value):
    if isinstance(value, dict):
        return {k: _clone(v) for k, v in value.items()}
    if isinstance(value, list):
        return [_clone(v) for v in value]
    return value


def _art_summary(catalog, concept_store, subject_id: str | None) -> dict | None:
    if not subject_id or subject_id not in catalog.subjects:
        return None
    row = catalog.subjects[subject_id]
    summary = catalog.summary(subject_id, row)
    concept_url = None
    has_approved_concept = False
    if concept_store is not None:
        aliases = tuple(old for old, target in catalog.aliases.items() if target == subject_id and old != subject_id)
        stage = concept_store.stage(subject_id, aliases)
        if stage.get("approved"):
            concept_url = stage["approved"]["url"]
            has_approved_concept = True
    detail = catalog.get(subject_id)
    if not concept_url and detail and detail.get("concept"):
        concept_url = detail["concept"]["url"]
        has_approved_concept = bool(detail["concept"].get("approved"))
    has_final = bool(detail and detail.get("final"))
    return {
        "id": subject_id,
        "category": summary["category"],
        "name": summary["name"],
        "kind": summary["kind"],
        "classic": summary["classic"],
        "conceptUrl": concept_url,
        "hasApprovedConcept": has_approved_concept,
        "hasFinalAsset": has_final,
    }


def _resolve_templates(raw_templates: dict[str, dict], branches: dict[str, str], careers: dict[str, dict]):
    """Resolve single-parent template inheritance with per-field and per-stat provenance.

    Mirrors C# DaggerfallActorTemplates.ResolveMerged:
    - Applies careerRef defaults at the exact declaring inheritance layer before that layer's own overrides.
    - Maps (`attributes`, `skills`) merge by key; null clears an entry or map.
    - Infers `spellPolicy: "fixed"` per layer when a layer authors non-empty `spells` without `spellPolicy`,
      while preserving explicit `spellPolicy: "none"` (which clears effective spells).
    """
    resolved_cache: dict[str, dict] = {}
    children_map: dict[str, list[str]] = defaultdict(list)
    for tid, raw in raw_templates.items():
        parent = raw.get("extends")
        if isinstance(parent, str) and parent in raw_templates:
            children_map[parent].append(tid)
    for parent in children_map:
        children_map[parent].sort()

    def resolve_one(tid: str, visiting: set[str]) -> dict:
        if tid in resolved_cache:
            return resolved_cache[tid]
        raw = raw_templates[tid]
        visiting = visiting | {tid}
        parent_id = raw.get("extends")
        if isinstance(parent_id, str) and parent_id in raw_templates and parent_id not in visiting:
            parent = resolve_one(parent_id, visiting)
            chain = [*parent["chain"], tid]
            merged = _clone(parent["merged"])
            field_origin = {k: dict(v) for k, v in parent["field_origin"].items()}
            attr_origin = {k: dict(v) for k, v in parent["attr_origin"].items()}
            skill_origin = {k: dict(v) for k, v in parent["skill_origin"].items()}
        else:
            chain = [tid]
            merged = {}
            field_origin = {}
            attr_origin = {}
            skill_origin = {}

        # Declaring-layer careerRef defaults (applied BEFORE this layer's own properties).
        declared_career_ref = raw.get("careerRef")
        if isinstance(declared_career_ref, str) and declared_career_ref in careers:
            career = careers[declared_career_ref]
            attr_vals = career.get("attributeValues") or []
            attrs_target = merged.get("attributes") if isinstance(merged.get("attributes"), dict) else {}
            for idx, attr_name in enumerate(ATTRIBUTE_ORDER):
                if idx < len(attr_vals) and _is_int(attr_vals[idx]):
                    prev = attr_origin.get(attr_name)
                    attrs_target[attr_name] = attr_vals[idx]
                    attr_origin[attr_name] = {
                        "value": attr_vals[idx],
                        "definedBy": tid,
                        "source": "career-default",
                        "careerRef": declared_career_ref,
                        "previousValue": prev["value"] if prev else None,
                        "previousDefinedBy": prev["definedBy"] if prev else None,
                    }
            merged["attributes"] = attrs_target
            hp_per_lvl = career.get("hitPointsPerLevel")
            if _is_int(hp_per_lvl) and hp_per_lvl > 0:
                health_default = {"minimum": 10, "maximum": 10 + hp_per_lvl}
                prev_h = field_origin.get("health")
                merged["health"] = health_default
                field_origin["health"] = {
                    "definedBy": tid,
                    "source": "career-default",
                    "careerRef": declared_career_ref,
                    "previousValue": prev_h["value"] if prev_h else None,
                    "previousDefinedBy": prev_h["definedBy"] if prev_h else None,
                    "value": health_default,
                }
                prev_hp = field_origin.get("hitPointsPerLevel")
                merged["hitPointsPerLevel"] = hp_per_lvl
                field_origin["hitPointsPerLevel"] = {
                    "definedBy": tid,
                    "source": "career-default",
                    "careerRef": declared_career_ref,
                    "previousValue": prev_hp["value"] if prev_hp else None,
                    "previousDefinedBy": prev_hp["definedBy"] if prev_hp else None,
                    "value": hp_per_lvl,
                }

        # Apply this layer's own authored properties.
        for key, value in raw.items():
            if key in ("id", "extends"):
                continue
            if key in ("attributes", "skills"):
                origin_map = attr_origin if key == "attributes" else skill_origin
                if value is None:
                    merged[key] = {}
                    for existing_key in list(origin_map.keys()):
                        prev = origin_map[existing_key]
                        origin_map[existing_key] = {
                            "value": None,
                            "definedBy": tid,
                            "source": "cleared",
                            "previousValue": prev["value"],
                            "previousDefinedBy": prev["definedBy"],
                        }
                elif isinstance(value, dict):
                    target = merged.get(key) if isinstance(merged.get(key), dict) else {}
                    for stat_key, stat_val in value.items():
                        prev = origin_map.get(stat_key)
                        if stat_val is None:
                            target.pop(stat_key, None)
                            origin_map[stat_key] = {
                                "value": None,
                                "definedBy": tid,
                                "source": "cleared",
                                "previousValue": prev["value"] if prev else None,
                                "previousDefinedBy": prev["definedBy"] if prev else None,
                            }
                        else:
                            target[stat_key] = stat_val
                            is_override = prev is not None and prev.get("definedBy") != tid and prev.get("value") is not None
                            origin_map[stat_key] = {
                                "value": stat_val,
                                "definedBy": tid,
                                "source": "overridden" if is_override else "local",
                                "previousValue": prev["value"] if is_override else None,
                                "previousDefinedBy": prev["definedBy"] if is_override else None,
                            }
                    merged[key] = target
                continue

            prev = field_origin.get(key)
            cloned = _clone(value)
            merged[key] = cloned
            if cloned is None and prev is not None and prev.get("value") is not None and prev.get("definedBy") != tid:
                source = "cleared"
            elif prev is not None and prev.get("definedBy") != tid:
                source = "overridden"
            else:
                source = "local"
            field_origin[key] = {
                "value": cloned,
                "definedBy": tid,
                "source": source,
                "previousValue": prev["value"] if (prev and prev.get("definedBy") != tid) else None,
                "previousDefinedBy": prev["definedBy"] if (prev and prev.get("definedBy") != tid) else None,
            }

        # Per-layer spellPolicy inference (matches C# DaggerfallActorTemplates.ResolveMerged).
        if "spellPolicy" not in raw and "spells" in raw:
            raw_spells = raw.get("spells")
            if isinstance(raw_spells, list) and len(raw_spells) > 0:
                prev = field_origin.get("spellPolicy")
                merged["spellPolicy"] = "fixed"
                field_origin["spellPolicy"] = {
                    "value": "fixed",
                    "definedBy": tid,
                    "source": "overridden" if (prev and prev.get("definedBy") != tid) else "local",
                    "previousValue": prev["value"] if (prev and prev.get("definedBy") != tid) else None,
                    "previousDefinedBy": prev["definedBy"] if (prev and prev.get("definedBy") != tid) else None,
                }
            elif raw_spells is None and merged.get("spellPolicy") == "fixed":
                prev = field_origin.get("spellPolicy")
                merged["spellPolicy"] = "none"
                field_origin["spellPolicy"] = {
                    "value": "none",
                    "definedBy": tid,
                    "source": "cleared",
                    "previousValue": prev["value"] if prev else None,
                    "previousDefinedBy": prev["definedBy"] if prev else None,
                }

        # Effective spells: explicit spellPolicy == "none" clears effective spells.
        effective_merged = _clone(merged)
        if effective_merged.get("spellPolicy") == "none":
            effective_merged["spells"] = []
        effective_merged["id"] = tid

        # Classify field provenance relative to `tid` (inherited vs local/overridden/cleared/career-default).
        fields_list = []
        ordered_keys = [k for k in _FIELD_DISPLAY_ORDER if k in field_origin] + sorted(
            k for k in field_origin if k not in _FIELD_DISPLAY_ORDER
        )
        for key in ordered_keys:
            meta = field_origin[key]
            defined_by = meta["definedBy"]
            if defined_by == tid:
                status = meta["source"]
            else:
                status = "inherited"
            fields_list.append({
                "field": key,
                "value": effective_merged.get(key) if key == "spells" else meta["value"],
                "definedBy": defined_by,
                "status": status,
                "careerRef": meta.get("careerRef"),
                "previousValue": meta.get("previousValue") if defined_by == tid else None,
                "previousDefinedBy": meta.get("previousDefinedBy") if defined_by == tid else None,
            })

        def _stat_rows(origin_map: dict[str, dict], order: tuple[str, ...] | None = None) -> list[dict]:
            keys = ([k for k in order if k in origin_map] + sorted(k for k in origin_map if k not in (order or ()))) if order else sorted(origin_map)
            rows = []
            for sk in keys:
                meta = origin_map[sk]
                if meta["value"] is None and meta["definedBy"] != tid:
                    continue
                defined_by = meta["definedBy"]
                status = meta["source"] if defined_by == tid else "inherited"
                rows.append({
                    "stat": sk,
                    "value": meta["value"],
                    "definedBy": defined_by,
                    "status": status,
                    "careerRef": meta.get("careerRef"),
                    "previousValue": meta.get("previousValue") if defined_by == tid else None,
                    "previousDefinedBy": meta.get("previousDefinedBy") if defined_by == tid else None,
                })
            return rows

        result = {
            "id": tid,
            "branch": branches.get(tid, "unknown"),
            "extends": parent_id if isinstance(parent_id, str) else None,
            "chain": chain,
            "depth": len(chain) - 1,
            "directChildren": children_map.get(tid, []),
            "raw": raw,
            "merged": effective_merged,
            "field_origin": field_origin,
            "attr_origin": attr_origin,
            "skill_origin": skill_origin,
            "fields": fields_list,
            "attributeRows": _stat_rows(attr_origin, ATTRIBUTE_ORDER),
            "skillRows": _stat_rows(skill_origin),
        }
        resolved_cache[tid] = result
        return result

    for tid in sorted(raw_templates):
        resolve_one(tid, set())
    return resolved_cache


class ActorsCatalogStore:
    """Mtime-cached unified Actors inspector over daggerfall.base/actors/{templates,specs}/."""

    def __init__(self, library, cache: FileCache, packs, people_pack):
        self.library = library
        self.cache = cache
        self.packs = packs
        self.people_pack = people_pack

    @property
    def canon_board_path(self) -> Path:
        return self.library.repo / "src/WorldRpg.Politics/canon/iliac-bay.board.json"

    def available(self) -> bool:
        base_path = self.library.base_payload
        if not base_path.is_file():
            return False
        doc = read_base_section(base_path, "actorTemplateBases", None, self.cache)
        return isinstance(doc, dict) and isinstance(doc.get("templates"), list)

    def _index(self) -> dict:
        base_path = self.library.base_payload
        board_path = self.canon_board_path
        people_stamp = self.people_pack.cache_stamp() if self.people_pack.available() else None
        stamp = (
            base_sections_stamp(base_path, LOOKUP_SECTIONS, self.cache),
            file_stamp(board_path),
            people_stamp,
        )

        def build():
            raw_templates: dict[str, dict] = {}
            template_branches: dict[str, str] = {}
            for branch, section in TEMPLATE_SECTIONS:
                doc = read_base_section(base_path, section, {}, self.cache) or {}
                for entry in doc.get("templates") or []:
                    if isinstance(entry, dict) and isinstance(entry.get("id"), str):
                        raw_templates[entry["id"]] = entry
                        template_branches[entry["id"]] = branch

            catalogs_doc = read_base_section(base_path, "catalogs", {}, self.cache) or {}
            careers = {
                row["id"]: row
                for row in (catalogs_doc.get("careers") or [])
                if isinstance(row, dict) and isinstance(row.get("id"), str)
            }
            races = [
                row["id"]
                for row in (catalogs_doc.get("races") or [])
                if isinstance(row, dict) and isinstance(row.get("id"), str)
            ]

            templates = _resolve_templates(raw_templates, template_branches, careers)

            # Read all spawner specs across families.
            specs: dict[str, dict] = {}
            spec_order: list[str] = []
            spawners_by_template: dict[str, list[str]] = defaultdict(list)
            specs_by_mobile: dict[int, list[str]] = defaultdict(list)

            for default_family, section in SPEC_SECTIONS:
                doc = read_base_section(base_path, section, {}, self.cache) or {}
                for entry in doc.get("specs") or []:
                    if not isinstance(entry, dict) or not isinstance(entry.get("id"), str):
                        continue
                    sid = entry["id"]
                    family = entry.get("family") or default_family
                    spec = dict(entry)
                    spec["family"] = family
                    specs[sid] = spec
                    spec_order.append(sid)
                    tid = spec.get("template")
                    if isinstance(tid, str):
                        spawners_by_template[tid].append(sid)
                    cb = spec.get("classicBinding")
                    if isinstance(cb, dict) and _is_int(cb.get("mobileId")):
                        specs_by_mobile[cb["mobileId"]].append(sid)

            for tid in spawners_by_template:
                spawners_by_template[tid].sort()

            # Encounter table reverse references.
            encounters_doc = read_base_section(base_path, "encounters", {}, self.cache) or {}
            encounter_tables_by_spec: dict[str, list[dict]] = defaultdict(list)
            for table_idx, table in enumerate(encounters_doc.get("tables") or []):
                if not isinstance(table, list):
                    continue
                counts = Counter(entry for entry in table if isinstance(entry, str))
                total = len(table) or 1
                for sid, slot_count in counts.items():
                    encounter_tables_by_spec[sid].append({
                        "tableIndex": table_idx,
                        "slots": slot_count,
                        "totalSlots": len(table),
                        "share": round(slot_count / total, 4),
                    })

            # Factions, board seats, and canonical political board reverse references.
            factions_doc = read_base_section(base_path, "factions", {}, self.cache) or {}
            factions = {
                row["id"]: row
                for row in (factions_doc.get("factions") or [])
                if isinstance(row, dict) and _is_int(row.get("id"))
            }
            board_seats_doc = read_base_section(base_path, "boardSeats", {}, self.cache) or {}
            board_seats = list(board_seats_doc.get("seats") or [])
            canon_doc = read_json(board_path, {}) if board_path.is_file() else {}
            canon_seats_by_id = {
                row["id"]: row
                for row in (canon_doc.get("seats") or [])
                if isinstance(row, dict) and isinstance(row.get("id"), str)
            }

            board_seats_by_spec: dict[str, list[dict]] = defaultdict(list)
            offices_by_spec: dict[str, list[dict]] = defaultdict(list)
            for seat in board_seats:
                if not isinstance(seat, dict) or not isinstance(seat.get("id"), str):
                    continue
                seat_id = seat["id"]
                office_fid = seat.get("officeFactionId")
                office_faction = factions.get(office_fid) if _is_int(office_fid) else None
                canon = canon_seats_by_id.get(seat_id, {})
                seat_info = {
                    "seatId": seat_id,
                    "seatName": canon.get("name") or (office_faction.get("name") if office_faction else seat_id),
                    "kind": canon.get("kind"),
                    "title": seat.get("title"),
                    "officeFactionId": office_fid,
                    "officeFactionName": office_faction.get("name") if office_faction else None,
                    "home": canon.get("home"),
                    "canonicalHolder": canon.get("holder"),
                }
                matched_specs: set[str] = set()
                holder = canon.get("holder")
                if isinstance(holder, str) and holder in specs:
                    matched_specs.add(holder)
                # Also reverse-match via faction parent hierarchy (e.g., officeFaction.parent == cast.factionId or cast.faction.parent == officeFactionId).
                for sid, sp in specs.items():
                    if sp.get("family") != "cast":
                        continue
                    fid = sp.get("factionId")
                    if not _is_int(fid):
                        continue
                    f_row = factions.get(fid)
                    if office_faction and office_faction.get("parent") == fid:
                        matched_specs.add(sid)
                    elif f_row and f_row.get("parent") == office_fid:
                        matched_specs.add(sid)
                for sid in sorted(matched_specs):
                    board_seats_by_spec[sid].append(seat_info)

            # Offices for any cast individual whose faction has a parent office/faction.
            for sid, sp in specs.items():
                if sp.get("family") != "cast":
                    continue
                fid = sp.get("factionId")
                f_row = factions.get(fid) if _is_int(fid) else None
                if f_row:
                    parent_fid = f_row.get("parent")
                    parent_f = factions.get(parent_fid) if _is_int(parent_fid) and parent_fid > 0 else None
                    offices_by_spec[sid].append({
                        "factionId": fid,
                        "factionName": f_row.get("name"),
                        "region": f_row.get("region"),
                        "parentFactionId": parent_fid if parent_f else None,
                        "parentFactionName": parent_f.get("name") if parent_f else None,
                    })

            # Spells lookup (key -> name, identity -> key/name) + enemy class tiers.
            magic_doc = read_base_section(base_path, "magic", {}, self.cache) or {}
            spells_by_key: dict[str, dict] = {}
            spells_by_identity: dict[int, dict] = {}
            for row in magic_doc.get("spells") or []:
                if isinstance(row, dict) and isinstance(row.get("key"), str):
                    info = {"key": row["key"], "identity": row.get("identity"), "name": row.get("name") or row["key"]}
                    spells_by_key[row["key"]] = info
                    if _is_int(row.get("identity")):
                        spells_by_identity[row["identity"]] = info

            enemy_spells_doc = read_base_section(base_path, "enemySpells", {}, self.cache) or {}
            class_tiers = list(enemy_spells_doc.get("classTiers") or [])

            vocab_doc = read_base_section(base_path, "vocabulary", {}, self.cache) or {}
            vocab_skills = [s for s in (vocab_doc.get("skills") or []) if isinstance(s, str)]

            region_names = (read_base_section(base_path, "buildingNames", {}, self.cache) or {}).get("regionNames") or []
            regions = [{"index": i, "name": name} for i, name in enumerate(region_names)]

            # Block slot usage from people_pack (when available).
            people_index = self.people_pack._index() if self.people_pack.available() else None

            return {
                "templates": templates,
                "specs": specs,
                "spec_order": spec_order,
                "spawners_by_template": spawners_by_template,
                "specs_by_mobile": specs_by_mobile,
                "encounter_tables_by_spec": encounter_tables_by_spec,
                "board_seats_by_spec": board_seats_by_spec,
                "offices_by_spec": offices_by_spec,
                "factions": factions,
                "careers": careers,
                "races": races,
                "vocab_skills": vocab_skills,
                "spells_by_key": spells_by_key,
                "spells_by_identity": spells_by_identity,
                "class_tiers": class_tiers,
                "regions": regions,
                "people_index": people_index,
            }

        return self.cache.get(("actors-catalog-index", base_path, board_path), stamp, build)

    def _art_links_for_spec(self, spec: dict, catalog, concept_store, memo: dict) -> list[dict]:
        """Find linked npc-### / creature-### art records for a spawner spec without renaming either side."""
        subject_ids: list[str] = []
        seen: set[str] = set()

        def add_subject(subject_id: str | None, role: str, extra: dict | None = None):
            if not subject_id or subject_id not in catalog.subjects:
                return
            key = f"{subject_id}:{role}"
            if key in seen:
                return
            seen.add(key)
            if subject_id not in memo:
                memo[subject_id] = _art_summary(catalog, concept_store, subject_id)
            art = memo[subject_id]
            if art:
                entry = dict(art) | {"role": role}
                if extra:
                    entry.update(extra)
                subject_ids.append(entry)

        cb = spec.get("classicBinding")
        if isinstance(cb, dict) and _is_int(cb.get("mobileId")):
            mid = cb["mobileId"]
            if mid < FIRST_HUMAN_MOBILE:
                add_subject(f"creature-{mid:03d}", "creature-mobile", {"mobileId": mid})
            else:
                add_subject(catalog.aliases.get(f"mobile-{mid}"), "enemy-male", {"mobileId": mid, "gender": "male"})
                add_subject(catalog.aliases.get(f"mobile-{mid}-female"), "enemy-female", {"mobileId": mid, "gender": "female"})

        vis = spec.get("visual")
        if isinstance(vis, dict) and _is_int(vis.get("archive")) and _is_int(vis.get("record")):
            pair = f"{vis['archive']}-{vis['record']}"
            add_subject(catalog.aliases.get(pair), "cast-visual", {"visualKey": pair})

        for entry in spec.get("defaultPool") or []:
            if isinstance(entry, dict) and _is_int(entry.get("archive")) and _is_int(entry.get("record")):
                pair = f"{entry['archive']}-{entry['record']}"
                add_subject(catalog.aliases.get(pair), "pool-default", {"visualKey": pair, "gender": entry.get("gender")})
        for rkey, entries in (spec.get("regionalPools") or {}).items():
            for entry in entries or []:
                if isinstance(entry, dict) and _is_int(entry.get("archive")) and _is_int(entry.get("record")):
                    pair = f"{entry['archive']}-{entry['record']}"
                    add_subject(catalog.aliases.get(pair), f"pool-region-{rkey}", {"visualKey": pair, "gender": entry.get("gender")})
        for ckey, entries in (spec.get("climatePools") or {}).items():
            for entry in entries or []:
                if isinstance(entry, dict) and _is_int(entry.get("archive")) and _is_int(entry.get("record")):
                    pair = f"{entry['archive']}-{entry['record']}"
                    add_subject(catalog.aliases.get(pair), f"pool-climate-{ckey}", {"visualKey": pair, "gender": entry.get("gender")})

        return subject_ids

    def _block_usage_for_spec(self, spec: dict, people_index: dict | None) -> dict:
        if not people_index:
            return {"slotCount": 0, "blockCount": 0, "blocks": [], "byKind": {}, "byBuildingType": []}
        sid = spec["id"]
        if spec.get("family") == "cast":
            usage = people_index["cast_usage"].get(sid, {"slotCount": 0, "blocks": []})
            return {
                "slotCount": usage["slotCount"],
                "blockCount": len(usage["blocks"]),
                "blocks": usage["blocks"][:20],
                "byKind": {},
                "byBuildingType": [],
            }
        usage = people_index["spawner_usage"].get(sid, {"slotCount": 0, "blockCount": 0, "byKind": {}, "byBuildingType": []})
        return {
            "slotCount": usage["slotCount"],
            "blockCount": usage["blockCount"],
            "blocks": [],
            "byKind": usage.get("byKind") or {},
            "byBuildingType": usage.get("byBuildingType") or [],
        }

    def _descendant_templates(self, tid: str, templates: dict[str, dict]) -> list[str]:
        out: list[str] = []
        stack = list(templates[tid]["directChildren"])
        while stack:
            cur = stack.pop(0)
            if cur not in out:
                out.append(cur)
                stack.extend(templates[cur]["directChildren"])
        return out

    def overview(self, catalog, concept_store) -> dict:
        if not self.available():
            return {"available": False, "counts": {}, "templates": [], "specs": [], "enums": {}}
        index = self._index()
        templates = index["templates"]
        specs = index["specs"]
        people_index = index["people_index"]
        memo: dict = {}

        template_rows = []
        for tid in sorted(templates):
            t = templates[tid]
            merged = t["merged"]
            direct_spawners = index["spawners_by_template"].get(tid, [])
            descendants = self._descendant_templates(tid, templates)
            desc_spawner_count = sum(len(index["spawners_by_template"].get(d, [])) for d in descendants)
            template_rows.append({
                "id": tid,
                "branch": t["branch"],
                "extends": t["extends"],
                "chain": t["chain"],
                "depth": t["depth"],
                "directChildrenCount": len(t["directChildren"]),
                "directSpawnerCount": len(direct_spawners),
                "descendantSpawnerCount": desc_spawner_count,
                "scaling": merged.get("scaling"),
                "careerRef": merged.get("careerRef"),
                "tactic": merged.get("tactic"),
                "spellPolicy": merged.get("spellPolicy"),
                "level": merged.get("level"),
                "health": merged.get("health"),
                "castsMagic": bool(merged.get("castsMagic")),
            })

        spec_rows = []
        family_counts = Counter()
        for sid in index["spec_order"]:
            sp = specs[sid]
            fam = sp["family"]
            family_counts[fam] += 1
            art_links = self._art_links_for_spec(sp, catalog, concept_store, memo)
            primary_art = art_links[0] if art_links else None
            block_usage = self._block_usage_for_spec(sp, people_index)
            enc_tables = index["encounter_tables_by_spec"].get(sid, [])
            seats = index["board_seats_by_spec"].get(sid, [])
            spec_rows.append({
                "id": sid,
                "family": fam,
                "template": sp.get("template"),
                "displayName": sp.get("displayName") or sid,
                "role": sp.get("role"),
                "published": bool(sp.get("published", True)),
                "harm": sp.get("harm"),
                "interaction": sp.get("interaction"),
                "race": sp.get("race"),
                "team": sp.get("team"),
                "factionId": sp.get("factionId"),
                "familyId": sp.get("familyId"),
                "socialGrouping": sp.get("socialGrouping"),
                "mobileId": (sp.get("classicBinding") or {}).get("mobileId"),
                "primaryArt": primary_art,
                "artCount": len({a["id"] for a in art_links}),
                "encounterTableCount": len(enc_tables),
                "blockSlotCount": block_usage["slotCount"],
                "boardSeatCount": len(seats),
                "editorHref": f"#cast/{sid}" if fam == "cast" else (f"#spawners/{sid}" if fam == "town" else None),
            })

        return {
            "available": True,
            "counts": {
                "templates": len(template_rows),
                "specs": len(spec_rows),
                "byFamily": dict(sorted(family_counts.items())),
                "byBranch": dict(sorted(Counter(t["branch"] for t in template_rows).items())),
            },
            "enums": {
                "branches": [b for b, _ in TEMPLATE_SECTIONS],
                "families": ["creature", "enemy", "town", "cast", "player"],
                "genders": ["male", "female", "neutral"],
                "regions": index["regions"],
                "races": index["races"],
            },
            "templates": template_rows,
            "specs": spec_rows,
        }

    def get_template(self, template_id: str, catalog, concept_store) -> dict | None:
        if not self.available():
            return None
        index = self._index()
        t = index["templates"].get(template_id)
        if not t:
            return None
        memo: dict = {}
        people_index = index["people_index"]
        direct_spawner_ids = index["spawners_by_template"].get(template_id, [])
        spawner_refs = []
        encounter_tables: dict[int, int] = defaultdict(int)
        total_block_slots = 0
        art_by_id: dict[str, dict] = {}
        board_seats: list[dict] = []
        seen_seats: set[str] = set()

        for sid in direct_spawner_ids:
            sp = index["specs"][sid]
            b_usage = self._block_usage_for_spec(sp, people_index)
            total_block_slots += b_usage["slotCount"]
            encs = index["encounter_tables_by_spec"].get(sid, [])
            for e in encs:
                encounter_tables[e["tableIndex"]] += e["slots"]
            for seat in index["board_seats_by_spec"].get(sid, []):
                if seat["seatId"] not in seen_seats:
                    seen_seats.add(seat["seatId"])
                    board_seats.append(seat | {"spawnerId": sid})
            arts = self._art_links_for_spec(sp, catalog, concept_store, memo)
            for a in arts:
                art_by_id.setdefault(a["id"], a)
            spawner_refs.append({
                "id": sid,
                "family": sp["family"],
                "displayName": sp.get("displayName") or sid,
                "published": bool(sp.get("published", True)),
                "blockSlotCount": b_usage["slotCount"],
                "encounterTableCount": len(encs),
                "primaryArt": arts[0] if arts else None,
            })

        return {
            "id": t["id"],
            "branch": t["branch"],
            "extends": t["extends"],
            "chain": [
                {"id": cid, "branch": index["templates"][cid]["branch"]}
                for cid in t["chain"]
                if cid in index["templates"]
            ],
            "depth": t["depth"],
            "directChildren": [
                {
                    "id": cid,
                    "branch": index["templates"][cid]["branch"],
                    "directSpawnerCount": len(index["spawners_by_template"].get(cid, [])),
                }
                for cid in t["directChildren"]
                if cid in index["templates"]
            ],
            "fields": t["fields"],
            "attributeRows": t["attributeRows"],
            "skillRows": t["skillRows"],
            "resolved": t["merged"],
            "reverseRefs": {
                "spawners": spawner_refs,
                "encounterTables": [
                    {"tableIndex": tidx, "slots": sc}
                    for tidx, sc in sorted(encounter_tables.items())
                ],
                "blockSlotCount": total_block_slots,
                "boardSeats": board_seats,
            },
            "artLinks": list(art_by_id.values()),
        }

    def get_spec(self, spec_id: str, catalog, concept_store) -> dict | None:
        if not self.available():
            return None
        index = self._index()
        sp = index["specs"].get(spec_id)
        if not sp:
            return None
        tid = sp.get("template")
        tpl = index["templates"].get(tid) if isinstance(tid, str) else None
        memo: dict = {}
        art_links = self._art_links_for_spec(sp, catalog, concept_store, memo)
        block_usage = self._block_usage_for_spec(sp, index["people_index"])
        enc_tables = index["encounter_tables_by_spec"].get(spec_id, [])
        board_seats = index["board_seats_by_spec"].get(spec_id, [])
        offices = index["offices_by_spec"].get(spec_id, [])
        fam = sp["family"]
        family_id = sp.get("familyId")
        family_members = []
        if isinstance(family_id, str) and family_id:
            for other_id in index["spec_order"]:
                if other_id == spec_id:
                    continue
                other = index["specs"][other_id]
                if other.get("familyId") == family_id:
                    other_arts = self._art_links_for_spec(other, catalog, concept_store, memo)
                    family_members.append({
                        "id": other_id,
                        "family": other["family"],
                        "displayName": other.get("displayName") or other_id,
                        "primaryArt": other_arts[0] if other_arts else None,
                    })

        return {
            "id": spec_id,
            "family": fam,
            "displayName": sp.get("displayName") or spec_id,
            "role": sp.get("role"),
            "template": tid,
            "templateChain": [
                {"id": cid, "branch": index["templates"][cid]["branch"]}
                for cid in (tpl["chain"] if tpl else [])
                if cid in index["templates"]
            ],
            "published": bool(sp.get("published", True)),
            "genders": sp.get("genders") or [],
            "race": sp.get("race"),
            "naming": sp.get("naming"),
            "harm": sp.get("harm"),
            "interaction": sp.get("interaction"),
            "team": sp.get("team"),
            "factionId": sp.get("factionId"),
            "factionName": (index["factions"].get(sp.get("factionId")) or {}).get("name") if _is_int(sp.get("factionId")) else None,
            "home": sp.get("home"),
            "familyId": family_id,
            "socialGrouping": sp.get("socialGrouping"),
            "familyMembers": family_members,
            "visual": sp.get("visual"),
            "visualMode": sp.get("visualMode"),
            "appearances": sp.get("appearances"),
            "sounds": sp.get("sounds"),
            "classicBinding": sp.get("classicBinding"),
            "deriveRegionalFaction": sp.get("deriveRegionalFaction"),
            "defaultPoolCount": len(sp.get("defaultPool") or []),
            "regionalPoolCount": len(sp.get("regionalPools") or {}),
            "climatePoolCount": len(sp.get("climatePools") or {}),
            "editorHref": f"#cast/{spec_id}" if fam == "cast" else (f"#spawners/{spec_id}" if fam == "town" else None),
            "templateFields": tpl["fields"] if tpl else [],
            "attributeRows": tpl["attributeRows"] if tpl else [],
            "skillRows": tpl["skillRows"] if tpl else [],
            "reverseRefs": {
                "encounterTables": enc_tables,
                "blockSlots": block_usage,
                "offices": offices,
                "boardSeats": board_seats,
            },
            "artLinks": art_links,
        }

    def preview(
        self,
        kind: str,
        actor_id: str,
        catalog,
        concept_store,
        *,
        level: int = 1,
        gender: str | None = None,
        region: int | None = None,
    ) -> dict:
        if not self.available():
            raise NotFoundError("Actor catalog is not available.")
        index = self._index()
        level = max(1, min(30, level))

        if kind == "spec":
            spec = index["specs"].get(actor_id)
            if not spec:
                raise NotFoundError(f"Actor spawner spec '{actor_id}' not found.")
            tid = spec.get("template")
            tpl = index["templates"].get(tid) if isinstance(tid, str) else None
        elif kind == "template":
            spec = None
            tpl = index["templates"].get(actor_id)
            if not tpl:
                raise NotFoundError(f"Actor template '{actor_id}' not found.")
        else:
            raise ValueError("kind must be 'spec' or 'template'.")

        merged = tpl["merged"] if tpl else {}
        notes: list[str] = []

        # 1. Resolve gender honoring template.allowedGenders and spec.genders.
        allowed_genders = list(merged.get("allowedGenders") or ["male", "female"])
        spec_genders = [
            g["gender"] for g in (spec.get("genders") or [])
            if isinstance(g, dict) and isinstance(g.get("gender"), str) and (g.get("weight") or 0) > 0
        ] if spec else []
        effective_allowed = [g for g in spec_genders if g in allowed_genders] if spec_genders else allowed_genders
        if not effective_allowed:
            effective_allowed = allowed_genders or ["male"]

        req_gender = gender.lower() if isinstance(gender, str) and gender.strip() else None
        if req_gender and req_gender in effective_allowed:
            resolved_gender = req_gender
        else:
            resolved_gender = effective_allowed[0]
            if req_gender:
                notes.append(f"Requested gender '{req_gender}' is disallowed here; snapped to '{resolved_gender}'.")

        # 2. Resolve race honoring template.fixedRace, template.allowedRaces, spec.race, region default.
        fixed_race = merged.get("fixedRace")
        allowed_races = merged.get("allowedRaces")
        spec_race = spec.get("race") if spec else None
        region_default_race = _REGION_DEFAULT_RACE.get(region, "breton") if region is not None else "breton"
        if isinstance(fixed_race, str) and fixed_race:
            resolved_race = fixed_race
            race_source = "template.fixedRace"
        elif isinstance(spec_race, str) and spec_race:
            resolved_race = spec_race
            race_source = "spec.race"
        elif isinstance(allowed_races, list) and allowed_races:
            resolved_race = region_default_race if region_default_race in allowed_races else allowed_races[0]
            race_source = "template.allowedRaces"
        else:
            resolved_race = region_default_race
            race_source = f"region:{region}" if region is not None else "default"

        # 3. Resolve level, health, attributes, skills, spells, and magicka matching C# runtime
        #    (DaggerfallEncounterActors.AtLevel, DaggerfallEnemySpells.For / MagickaMaximum,
        #     and DaggerfallMechanicsState.CreateStats).
        scaling = merged.get("scaling") or "fixed"
        is_town_or_cast = spec is not None and spec.get("family") in ("town", "cast")
        base_level = merged.get("level") if _is_int(merged.get("level")) else 1
        scales_with_level = (not is_town_or_cast) and scaling in ("career-leveled", "level-scaled-skills")
        effective_level = level if scales_with_level else (base_level or level)
        if not scales_with_level and level != base_level:
            notes.append(f"Uses fixed authored level {base_level}; level selector does not rescale stats.")

        health = dict(merged.get("health") or {"minimum": 10, "maximum": 20})
        hp_per_lvl = merged.get("hitPointsPerLevel")
        if (not is_town_or_cast) and scaling == "career-leveled" and _is_int(hp_per_lvl) and hp_per_lvl > 0:
            health = {
                "minimum": 10 + effective_level,
                "maximum": 10 + effective_level * hp_per_lvl,
            }

        # 4. Resolve spells (fixed vs class-tiers vs none) using runtime tier boundary `level // 3`.
        spell_policy = merged.get("spellPolicy") or "none"
        resolved_spells: list[dict] = []
        if spell_policy == "fixed":
            for skey in merged.get("spells") or []:
                info = index["spells_by_key"].get(skey, {"key": skey, "name": skey, "identity": None})
                resolved_spells.append(info)
        elif spell_policy == "class-tiers" and index["class_tiers"]:
            tier_idx = min(effective_level // 3, len(index["class_tiers"]) - 1)
            for spell_ident in index["class_tiers"][tier_idx]:
                info = index["spells_by_identity"].get(
                    spell_ident,
                    {"key": f"spell.{spell_ident:03d}", "name": f"Spell #{spell_ident}", "identity": spell_ident},
                )
                resolved_spells.append(info | {"tier": tier_idx + 1})

        # 5. Resolve skills and magicka matching DaggerfallEncounterActors.AtLevel + DaggerfallMechanicsState.CreateStats.
        base_attrs = dict(merged.get("attributes") or {})
        base_skills = dict(merged.get("skills") or {})
        effective_skills = dict(base_skills)
        if scales_with_level:
            scaled_skill = min(100, effective_level * 5 + 30)
            skill_keys = list(index["vocab_skills"]) if index["vocab_skills"] else list(base_skills.keys())
            for sk in skill_keys:
                effective_skills[sk] = scaled_skill
            for sk in base_skills:
                effective_skills[sk] = scaled_skill

        is_runtime_caster = (not is_town_or_cast) and (
            spell_policy == "class-tiers" or (spell_policy == "fixed" and len(resolved_spells) > 0)
        )
        if is_runtime_caster:
            for school in MAGIC_SCHOOLS:
                effective_skills[school] = 80
            effective_magicka = 10 * effective_level + 100
        else:
            effective_magicka = merged.get("magicka")

        # 5. Resolve visual preview for (gender, region).
        memo: dict = {}
        visual_preview: dict = {"mode": "none", "entries": []}
        if spec:
            fam = spec["family"]
            if fam == "town":
                reg_pools = spec.get("regionalPools") or {}
                rkey = str(region) if region is not None else None
                if rkey and rkey in reg_pools:
                    raw_pool = reg_pools[rkey]
                    pool_source = f"regionalPools[{rkey}]"
                else:
                    raw_pool = spec.get("defaultPool") or []
                    pool_source = "defaultPool"
                legacy_gender = "Male" if resolved_gender == "male" else ("Female" if resolved_gender == "female" else "")
                gender_filtered = [e for e in raw_pool if not legacy_gender or e.get("gender") == legacy_gender]
                active_pool = gender_filtered or raw_pool
                total_w = sum(e.get("weight", 0) for e in active_pool) or 1
                entries = []
                for e in active_pool:
                    art = self.people_pack.enrich_visual(catalog, concept_store, e["archive"], e["record"], memo)
                    entries.append({
                        "archive": e["archive"],
                        "record": e["record"],
                        "gender": e.get("gender"),
                        "weight": e.get("weight", 0),
                        "share": round((e.get("weight", 0)) / total_w, 4),
                        "art": art,
                    })
                visual_preview = {"mode": "pool", "source": pool_source, "entries": entries}
            elif fam == "cast":
                vis = spec.get("visual") or {}
                if _is_int(vis.get("archive")) and _is_int(vis.get("record")):
                    art = self.people_pack.enrich_visual(catalog, concept_store, vis["archive"], vis["record"], memo)
                    visual_preview = {"mode": "pinned", "source": "cast.visual", "entries": [{"archive": vis["archive"], "record": vis["record"], "gender": resolved_gender, "weight": 1, "share": 1.0, "art": art}]}
            else:
                apps = spec.get("appearances") or {}
                tex = apps.get("femaleTexture") if resolved_gender == "female" else apps.get("maleTexture")
                arts = self._art_links_for_spec(spec, catalog, concept_store, memo)
                picked_art = next((a for a in arts if a.get("gender") == resolved_gender), arts[0] if arts else None)
                visual_preview = {
                    "mode": "mobile-texture",
                    "source": "appearances.femaleTexture" if resolved_gender == "female" else "appearances.maleTexture",
                    "textureArchive": tex,
                    "corpse": apps.get("corpse"),
                    "art": picked_art,
                    "entries": [],
                }

        region_name = None
        if region is not None and 0 <= region < len(index["regions"]):
            region_name = index["regions"][region]["name"]

        return {
            "kind": kind,
            "id": actor_id,
            "templateId": tpl["id"] if tpl else None,
            "inputs": {
                "level": level,
                "gender": resolved_gender,
                "allowedGenders": effective_allowed,
                "region": region,
                "regionName": region_name,
            },
            "effective": {
                "level": effective_level,
                "scaling": scaling,
                "race": resolved_race,
                "raceSource": race_source,
                "health": health,
                "hitPointsPerLevel": hp_per_lvl,
                "fatigue": merged.get("fatigue"),
                "magicka": effective_magicka,
                "armor": merged.get("armor", 0),
                "attributes": base_attrs,
                "skills": effective_skills,
                "attacks": merged.get("attacks") or [],
                "loadout": merged.get("loadout") or [],
                "tactic": merged.get("tactic"),
                "tendencies": merged.get("tendencies"),
                "spellPolicy": spell_policy,
                "spells": resolved_spells,
                "harm": (spec.get("harm") if spec else None) or merged.get("defaultHarm"),
                "interaction": (spec.get("interaction") if spec else None) or merged.get("defaultInteraction"),
                "team": spec.get("team") if spec else None,
                "factionId": spec.get("factionId") if spec else None,
                "factionName": (index["factions"].get(spec.get("factionId")) or {}).get("name") if (spec and _is_int(spec.get("factionId"))) else None,
                "home": spec.get("home") if spec else None,
                "visual": visual_preview,
            },
            "notes": notes,
        }

    def usage_for_art(self, subject_id: str, catalog) -> dict:
        """Reverse Art -> Actors links for both People (npc-###) and Creatures (creature-###) pages."""
        if not self.available():
            return {"spawners": [], "templates": []}
        index = self._index()
        specs = index["specs"]

        matched_spec_ids: list[str] = []
        seen_specs: set[str] = set()

        def add_spec(sid: str):
            if sid in specs and sid not in seen_specs:
                seen_specs.add(sid)
                matched_spec_ids.append(sid)

        # 1. Creature mobile match (creature-NNN -> mobileId NNN).
        if subject_id.startswith("creature-"):
            suffix = subject_id.removeprefix("creature-")
            if suffix.isdigit():
                for sid in index["specs_by_mobile"].get(int(suffix), ()):
                    add_spec(sid)

        # 2. Human enemy mobile alias match (mobile-NNN or mobile-NNN-female -> npc-###) + static (archive-record) match.
        visual_pairs: set[tuple[int, int]] = set()
        for alias, target in catalog.aliases.items():
            if target != subject_id:
                continue
            if alias.startswith("mobile-"):
                rest = alias.removeprefix("mobile-").removesuffix("-female")
                if rest.isdigit():
                    for sid in index["specs_by_mobile"].get(int(rest), ()):
                        add_spec(sid)
            else:
                arch_s, sep, rec_s = alias.partition("-")
                if sep and arch_s.isdigit() and rec_s.isdigit():
                    visual_pairs.add((int(arch_s), int(rec_s)))

        if visual_pairs:
            for sid in index["spec_order"]:
                sp = specs[sid]
                vis = sp.get("visual")
                if isinstance(vis, dict) and (vis.get("archive"), vis.get("record")) in visual_pairs:
                    add_spec(sid)
                    continue
                found = False
                for entry in sp.get("defaultPool") or []:
                    if isinstance(entry, dict) and (entry.get("archive"), entry.get("record")) in visual_pairs:
                        found = True
                        break
                if not found:
                    for pool in (sp.get("regionalPools") or {}).values():
                        if any(isinstance(e, dict) and (e.get("archive"), e.get("record")) in visual_pairs for e in (pool or [])):
                            found = True
                            break
                if not found:
                    for pool in (sp.get("climatePools") or {}).values():
                        if any(isinstance(e, dict) and (e.get("archive"), e.get("record")) in visual_pairs for e in (pool or [])):
                            found = True
                            break
                if found:
                    add_spec(sid)

        spawner_rows = []
        template_ids: list[str] = []
        seen_templates: set[str] = set()
        for sid in matched_spec_ids:
            sp = specs[sid]
            tid = sp.get("template")
            spawner_rows.append({
                "id": sid,
                "family": sp["family"],
                "displayName": sp.get("displayName") or sid,
                "template": tid,
                "published": bool(sp.get("published", True)),
            })
            if isinstance(tid, str) and tid in index["templates"] and tid not in seen_templates:
                seen_templates.add(tid)
                template_ids.append(tid)

        template_rows = [
            {
                "id": tid,
                "branch": index["templates"][tid]["branch"],
                "extends": index["templates"][tid]["extends"],
            }
            for tid in template_ids
        ]
        return {"spawners": spawner_rows, "templates": template_rows}


def _parse_optional_int(raw: str | None, name: str) -> int | None:
    if raw is None or raw == "":
        return None
    try:
        return int(raw)
    except ValueError as err:
        raise ValueError(f"Query parameter '{name}' must be an integer.") from err


def register_routes(router, workspace):
    """Register unified Actors read-only inspector & preview routes."""

    def get_overview(_req):
        return workspace.actors_catalog.overview(workspace.catalog(), workspace.concept_store)

    def get_template(req):
        item = workspace.actors_catalog.get_template(req.params["id"], workspace.catalog(), workspace.concept_store)
        if item is None:
            raise NotFoundError(f"Actor template '{req.params['id']}' not found.")
        return item

    def get_spec(req):
        item = workspace.actors_catalog.get_spec(req.params["id"], workspace.catalog(), workspace.concept_store)
        if item is None:
            raise NotFoundError(f"Actor spawner spec '{req.params['id']}' not found.")
        return item

    def get_preview(req):
        kind = req.arg("kind") or "spec"
        actor_id = req.arg("id")
        if not actor_id:
            raise ValueError("Query parameter 'id' is required.")
        level = _parse_optional_int(req.arg("level"), "level") or 1
        gender = req.arg("gender")
        region = _parse_optional_int(req.arg("region"), "region")
        return workspace.actors_catalog.preview(
            kind, actor_id, workspace.catalog(), workspace.concept_store,
            level=level, gender=gender, region=region,
        )

    router.get("/api/actors/overview", get_overview)
    router.get("/api/actors/templates/{id}", get_template)
    router.get("/api/actors/specs/{id}", get_spec)
    router.get("/api/actors/preview", get_preview)
