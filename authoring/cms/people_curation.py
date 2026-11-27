"""In-CMS People (npc-###) curation: art-need enrichment, side-by-side compare/merge, and guarded retirement."""

import re

from base_pack import read_section as read_base_section
from library import read_json
from router import ConflictError, NotFoundError

_PAIR_RE = re.compile(r"^(\d+)-(\d+)$")
_MOBILE_RE = re.compile(r"^mobile-(\d+)(?:-(?:male|female))?$")
_FACTION_RE = re.compile(r"^npc-faction-(\d+)$")
_ARCHIVE_RE = re.compile(r"^(?:mobile-|npc-)?(\d+)$")


def _parse_visual_aliases(npc_id: str, aliases: dict[str, str]) -> list[tuple[int, int]]:
    pairs = []
    for alias, target in aliases.items():
        if target != npc_id:
            continue
        arch_str, sep, rec_str = alias.partition("-")
        if sep and arch_str.isdigit() and rec_str.isdigit():
            pairs.append((int(arch_str), int(rec_str)))
    pairs.sort()
    return pairs


def _extract_npc_keys(npc_id: str, raw_aliases: list[str] | tuple[str, ...]) -> tuple[list[tuple[int, int]], list[int], list[int], list[int]]:
    pairs: list[tuple[int, int]] = []
    mobiles: list[int] = []
    archives: list[int] = []
    factions: list[int] = []
    seen_pairs: set[tuple[int, int]] = set()
    seen_mobiles: set[int] = set()
    seen_archives: set[int] = set()
    seen_factions: set[int] = set()

    for alias in (*raw_aliases, npc_id):
        if not isinstance(alias, str):
            continue
        m_pair = _PAIR_RE.match(alias)
        if m_pair:
            pair = (int(m_pair.group(1)), int(m_pair.group(2)))
            if pair not in seen_pairs:
                seen_pairs.add(pair)
                pairs.append(pair)
            continue
        m_fac = _FACTION_RE.match(alias)
        if m_fac:
            fid = int(m_fac.group(1))
            if fid not in seen_factions:
                seen_factions.add(fid)
                factions.append(fid)
            continue
        m_mob = _MOBILE_RE.match(alias)
        if m_mob:
            num = int(m_mob.group(1))
            if num not in seen_mobiles:
                seen_mobiles.add(num)
                mobiles.append(num)
            if num not in seen_archives:
                seen_archives.add(num)
                archives.append(num)
            continue
        m_arch = _ARCHIVE_RE.match(alias)
        if m_arch and alias != npc_id:
            num = int(m_arch.group(1))
            if num not in seen_archives:
                seen_archives.add(num)
                archives.append(num)
    return pairs, mobiles, archives, factions


def _read_base_section_safe(catalog, section_name: str):
    try:
        base_payload = catalog.library.base_payload
        if not base_payload.is_file():
            return None
        return read_base_section(base_payload, section_name, cache=catalog.cache)
    except Exception:
        return None


def _build_extra_usage_maps(catalog, people_pack_index: dict | None) -> dict:
    """Index hostile enemy spawners (enemies.json), wandering street townsfolk (catalogs.json),
    and faction-linked named cast (cast.json / people_pack) so People curation reflects all active game consumers."""
    enemy_by_mobile: dict[int, list[str]] = {}
    enemy_by_tex: dict[int, list[str]] = {}
    enemies_doc = _read_base_section_safe(catalog, "actorSpecsEnemies")
    if isinstance(enemies_doc, dict):
        for spec in enemies_doc.get("specs") or ():
            if not isinstance(spec, dict) or spec.get("family") != "enemy":
                continue
            sid = spec.get("id")
            if not isinstance(sid, str) or not sid:
                continue
            cb = spec.get("classicBinding")
            if isinstance(cb, dict) and isinstance(cb.get("mobileId"), int):
                enemy_by_mobile.setdefault(cb["mobileId"], [])
                if sid not in enemy_by_mobile[cb["mobileId"]]:
                    enemy_by_mobile[cb["mobileId"]].append(sid)
            app = spec.get("appearances")
            if isinstance(app, dict):
                for key in ("maleTexture", "femaleTexture"):
                    tex = app.get(key)
                    if isinstance(tex, int):
                        enemy_by_tex.setdefault(tex, [])
                        if sid not in enemy_by_tex[tex]:
                            enemy_by_tex[tex].append(sid)

    walker_by_archive: dict[int, list[str]] = {}
    catalogs_doc = _read_base_section_safe(catalog, "catalogs")
    if isinstance(catalogs_doc, dict):
        for race in catalogs_doc.get("races") or ():
            if not isinstance(race, dict):
                continue
            race_id = race.get("id")
            townspeople = race.get("townspeople")
            if not isinstance(race_id, str) or not isinstance(townspeople, dict):
                continue
            for gender in ("male", "female"):
                archives = townspeople.get(gender)
                if not isinstance(archives, list):
                    continue
                pool_id = f"street-{race_id}-{gender}"
                for arch in archives:
                    if isinstance(arch, int):
                        walker_by_archive.setdefault(arch, [])
                        if pool_id not in walker_by_archive[arch]:
                            walker_by_archive[arch].append(pool_id)

    cast_by_faction: dict[int, list[dict]] = {}
    cast_seen: dict[int, set[str]] = {}
    if people_pack_index is not None:
        for ind in people_pack_index.get("individuals") or ():
            if not isinstance(ind, dict):
                continue
            fid = ind.get("factionId")
            cid = ind.get("id")
            if isinstance(fid, int) and isinstance(cid, str) and cid:
                if cid not in cast_seen.setdefault(fid, set()):
                    cast_seen[fid].add(cid)
                    cast_by_faction.setdefault(fid, []).append({
                        "id": cid,
                        "name": ind.get("name") or cid,
                        "factionId": fid,
                    })
    cast_doc = _read_base_section_safe(catalog, "actorSpecsCast")
    if isinstance(cast_doc, dict):
        for spec in cast_doc.get("specs") or ():
            if not isinstance(spec, dict):
                continue
            fid = spec.get("factionId")
            cid = spec.get("id")
            if isinstance(fid, int) and isinstance(cid, str) and cid:
                if cid not in cast_seen.setdefault(fid, set()):
                    cast_seen[fid].add(cid)
                    cast_by_faction.setdefault(fid, []).append({
                        "id": cid,
                        "name": spec.get("displayName") or cid,
                        "factionId": fid,
                    })

    return {
        "enemy_by_mobile": enemy_by_mobile,
        "enemy_by_tex": enemy_by_tex,
        "walker_by_archive": walker_by_archive,
        "cast_by_faction": cast_by_faction,
    }


def _resolve_canonical_concept(
    npc_id: str,
    npc_aliases: tuple[str, ...],
    pipeline_ids: list[str] | tuple[str, ...],
    catalog,
    concept_store,
    concept_subjects: set[str],
) -> tuple[bool, str | None]:
    """Return (has_approved_concept, canonical_concept_url) preferring ConceptStore front approval,
    then canonical pipeline front concept."""
    if concept_store is not None and (npc_id in concept_subjects or any(a in concept_subjects for a in npc_aliases)):
        stage = concept_store.stage(npc_id, npc_aliases)
        approvals = stage.get("approvals") or {}
        front = approvals.get("front") or stage.get("approved")
        if isinstance(front, dict) and front.get("url"):
            return True, front["url"]
        if any(approvals.get(v) for v in ("left", "right", "back")):
            fallback_view = next(
                (approvals[v] for v in ("left", "right", "back") if isinstance(approvals.get(v), dict) and approvals[v].get("url")),
                None,
            )
            return True, (fallback_view["url"] if fallback_view else None)

    pipelines = [catalog.pipelines[pid] for pid in pipeline_ids if pid in catalog.pipelines]
    choice = catalog.links.get(npc_id, {})
    conflicts = catalog.conflicts.get(npc_id, [])
    if "concept" not in conflicts:
        concepts = [art for item in pipelines for art in item["concepts"]]
        explicit = choice.get("concept")
        if explicit:
            matched = next(
                (c for c in concepts if c["pipeline"] == explicit["pipeline"] and c["path"] == explicit["path"]),
                None,
            )
            if matched is not None:
                return True, matched.get("url")
        else:
            approved = [art for art in concepts if art["approved"] and art["name"] == "Front concept"]
            if len(approved) == 1:
                return True, approved[0].get("url")
    return False, None


def _collect_extra_usage(
    npc_id: str,
    raw_aliases: list[str] | tuple[str, ...],
    extra: dict,
) -> tuple[list[dict], list[str]]:
    pairs, mobiles, archives, factions = _extract_npc_keys(npc_id, raw_aliases)
    cast_items: list[dict] = []
    seen_cast: set[str] = set()
    for fid in factions:
        for item in extra["cast_by_faction"].get(fid, ()):
            if item["id"] not in seen_cast:
                seen_cast.add(item["id"])
                cast_items.append(item)

    spawner_ids: list[str] = []
    seen_spawners: set[str] = set()
    for mid in mobiles:
        for sid in extra["enemy_by_mobile"].get(mid, ()):
            if sid not in seen_spawners:
                seen_spawners.add(sid)
                spawner_ids.append(sid)
    for arch in archives:
        for sid in extra["enemy_by_tex"].get(arch, ()):
            if sid not in seen_spawners:
                seen_spawners.add(sid)
                spawner_ids.append(sid)
        for sid in extra["walker_by_archive"].get(arch, ()):
            if sid not in seen_spawners:
                seen_spawners.add(sid)
                spawner_ids.append(sid)
    for pair in pairs:
        for sid in extra["enemy_by_tex"].get(pair[0], ()):
            if sid not in seen_spawners:
                seen_spawners.add(sid)
                spawner_ids.append(sid)
    return cast_items, spawner_ids


def enrich_people_rows(rows: list[dict], catalog, concept_store, people_pack) -> list[dict]:
    """Attach lightweight art-need, canonical concept preview, and active game-usage summaries to each People row."""
    pack_available = people_pack.available()
    index = people_pack._index() if pack_available else None
    extra = _build_extra_usage_maps(catalog, index)

    # Pre-group aliases by canonical npc_id once.
    aliases_by_npc: dict[str, list[str]] = {}
    for old, target in catalog.aliases.items():
        if old != target and target in catalog.npcs:
            aliases_by_npc.setdefault(target, []).append(old)

    concept_subjects = set(concept_store.subjects()) if concept_store is not None else set()

    enriched = []
    for row in rows:
        npc_id = row["id"]
        combined_aliases = list(dict.fromkeys([*(row.get("aliases") or ()), *aliases_by_npc.get(npc_id, ())]))
        pairs, _, _, _ = _extract_npc_keys(npc_id, combined_aliases)
        extra_cast, extra_spawners = _collect_extra_usage(npc_id, combined_aliases, extra)

        cast_ids: list[str] = []
        spawner_ids: list[str] = []
        slot_count = 0
        if index is not None:
            for pair in pairs:
                slot_count += index["visual_slot_counts"].get(pair, 0)
                for cid in index["visual_cast_ids"].get(pair, ()):
                    if cid not in cast_ids:
                        cast_ids.append(cid)
                for ref in index["visual_spawner_refs"].get(pair, ()):
                    sid = ref["spawnerId"]
                    if sid not in spawner_ids:
                        spawner_ids.append(sid)

        for item in extra_cast:
            if item["id"] not in cast_ids:
                cast_ids.append(item["id"])
        for sid in extra_spawners:
            if sid not in spawner_ids:
                spawner_ids.append(sid)

        npc_aliases = tuple(aliases_by_npc.get(npc_id, ()))
        has_approved_concept, concept_preview = _resolve_canonical_concept(
            npc_id, npc_aliases, row.get("pipelineIds", ()), catalog, concept_store, concept_subjects,
        )

        pipelines = [catalog.pipelines[pid] for pid in row.get("pipelineIds", ()) if pid in catalog.pipelines]
        choice = catalog.links.get(npc_id, {})
        conflicts = catalog.conflicts.get(npc_id, [])
        finals = [item["final"] for item in pipelines if item["final"]]
        explicit_final = choice.get("final")
        has_final = False
        if explicit_final and any(f["pipeline"] == explicit_final["pipeline"] and f["path"] == explicit_final["path"] for f in finals):
            has_final = True
        elif not explicit_final and "final" not in conflicts and len(finals) == 1:
            has_final = True

        enriched.append(row | {
            "conceptPreview": concept_preview,
            "curation": {
                "castCount": len(cast_ids),
                "castIds": cast_ids,
                "spawnerCount": len(spawner_ids),
                "spawnerIds": spawner_ids,
                "slotCount": slot_count,
                "hasApprovedConcept": has_approved_concept,
                "hasFinalAsset": has_final,
            },
        })
    return enriched


def compare_records(body, workspace) -> dict:
    if not isinstance(body, dict) or not isinstance(body.get("ids"), list):
        raise ValueError("Provide an 'ids' array of at least two People records to compare.")
    raw_ids = [item for item in body["ids"] if isinstance(item, str) and item.strip()]
    catalog = workspace.catalog()
    resolved_ids = []
    for raw in raw_ids:
        canonical = catalog.resolve(raw)
        if canonical not in catalog.npcs:
            raise NotFoundError(f"People record '{raw}' not found.")
        if canonical not in resolved_ids:
            resolved_ids.append(canonical)
    if len(resolved_ids) < 2:
        raise ValueError("Select at least two distinct People records to compare or merge.")

    reviews = workspace.reviews()
    items = []
    approved_fronts = []
    approved_backs = []
    explicit_concepts = set()
    explicit_finals = set()

    for npc_id in resolved_ids:
        detail = workspace.character(npc_id)
        review = reviews.item("people", npc_id)
        stage = workspace.concept_stage(npc_id) if workspace.concept_store else None
        front = (stage or {}).get("approvals", {}).get("front") or (stage or {}).get("approved")
        back = (stage or {}).get("approvals", {}).get("back")
        if front:
            approved_fronts.append(npc_id)
        if back:
            approved_backs.append(npc_id)
        if detail.get("explicitConcept"):
            ec = detail["explicitConcept"]
            explicit_concepts.add((ec["pipeline"], ec["path"]))
        if detail.get("explicitFinal"):
            ef = detail["explicitFinal"]
            explicit_finals.add((ef["pipeline"], ef["path"]))
        items.append({
            "id": detail["id"],
            "name": detail["name"],
            "kind": detail["kind"],
            "classic": detail["classic"],
            "classicUrl": detail["classicUrl"],
            "brief": detail.get("brief"),
            "aliases": detail.get("aliases", []),
            "pipelineIds": detail.get("pipelineIds", []),
            "concept": detail.get("concept"),
            "final": detail.get("final"),
            "conceptStage": {
                "count": len((stage or {}).get("candidates", [])),
                "front": front,
                "back": back,
            },
            "review": review,
            "placementCount": detail.get("game", {}).get("placementCount", 0),
            "peoplePack": detail.get("peoplePack", {"visualKeys": [], "cast": [], "spawners": [], "slotCount": 0}),
        })

    warnings = []
    if len(approved_fronts) > 1:
        warnings.append(
            f"Multiple records have approved front concepts ({', '.join(approved_fronts)}); choose one on the keeper after merging."
        )
    if len(approved_backs) > 1:
        warnings.append(
            f"Multiple records have approved back concepts ({', '.join(approved_backs)}); choose one on the keeper after merging."
        )
    if len(explicit_concepts) > 1:
        warnings.append("Selected records have different explicit pipeline concept picks; resolve on the keeper after merging.")
    if len(explicit_finals) > 1:
        warnings.append("Selected records have different explicit final asset picks; resolve on the keeper after merging.")

    return {"items": items, "warnings": warnings}


def references_for(npc_id: str, workspace) -> dict:
    catalog = workspace.catalog()
    canonical = catalog.resolve(npc_id)
    if canonical not in catalog.npcs:
        raise NotFoundError(f"People record '{npc_id}' not found.")

    detail = workspace.character(canonical)
    ledger = read_json(workspace.merges, {})
    merged_members = sorted(src for src, dst in ledger.items() if dst == canonical) if isinstance(ledger, dict) else []

    raw_usage = detail.get("peoplePack") or {"visualKeys": [], "cast": [], "spawners": [], "slotCount": 0}
    usage = {
        "visualKeys": list(raw_usage.get("visualKeys") or ()),
        "cast": list(raw_usage.get("cast") or ()),
        "spawners": list(raw_usage.get("spawners") or ()),
        "slotCount": raw_usage.get("slotCount", 0),
    }
    pack_index = workspace.people_pack._index() if workspace.people_pack.available() else None
    extra = _build_extra_usage_maps(catalog, pack_index)
    all_aliases = list(dict.fromkeys([
        *(detail.get("aliases") or ()),
        *(old for old, target in catalog.aliases.items() if target == canonical and old != canonical),
    ]))
    extra_cast, extra_spawners = _collect_extra_usage(canonical, all_aliases, extra)
    existing_cast_ids = {c["id"] for c in usage["cast"] if isinstance(c, dict) and "id" in c}
    for item in extra_cast:
        if item["id"] not in existing_cast_ids:
            existing_cast_ids.add(item["id"])
            usage["cast"].append(item)
    existing_spawner_ids = {s["spawnerId"] for s in usage["spawners"] if isinstance(s, dict) and "spawnerId" in s}
    for sid in extra_spawners:
        if sid not in existing_spawner_ids:
            existing_spawner_ids.add(sid)
            usage["spawners"].append({"spawnerId": sid, "scope": "actor-spec", "weight": 1})

    pipelines = list(detail.get("pipelineIds", []))
    stage = workspace.concept_stage(canonical) if workspace.concept_store else None
    concept_count = len((stage or {}).get("candidates", []))
    approvals = (stage or {}).get("approvals") or {}
    front_approval = approvals.get("front") or (stage or {}).get("approved")
    concept_preview = (
        front_approval.get("url")
        if isinstance(front_approval, dict) and front_approval.get("url")
        else (detail["concept"].get("url") if isinstance(detail.get("concept"), dict) else None)
    )
    has_approved_concept = bool(
        any(approvals.get(v) for v in ("front", "left", "right", "back"))
        or (stage or {}).get("approved")
        or (detail.get("concept") and detail["concept"].get("approved"))
    )
    has_final = bool(detail.get("final"))
    review = workspace.reviews().item("people", canonical)

    blockers = []
    if merged_members:
        blockers.append(
            f"{canonical} is the merge keeper for {', '.join(merged_members)}. It cannot be retired while other NPC IDs redirect to it."
        )

    warnings = []
    if usage["cast"]:
        names = ", ".join(f"{c['name']} ({c['id']})" for c in usage["cast"])
        warnings.append(f"Referenced by {len(usage['cast'])} named cast individual(s): {names}.")
    if usage["spawners"]:
        spawner_names = sorted({s["spawnerId"] for s in usage["spawners"]})
        warnings.append(f"Used in {len(spawner_names)} typed spawner pool(s): {', '.join(spawner_names)}.")
    if usage["slotCount"] > 0:
        warnings.append(f"Sprite alias appears in {usage['slotCount']} block placement slot(s) in daggerfall.people.")
    if pipelines:
        warnings.append(f"Linked to {len(pipelines)} character pipeline(s): {', '.join(pipelines)}.")
    if has_approved_concept:
        warnings.append(f"Has approved concept art ({concept_count} submitted candidate(s)).")
    elif concept_count > 0:
        warnings.append(f"Has {concept_count} submitted concept art candidate(s).")
    if has_final:
        warnings.append("Has a game-ready final asset.")

    return {
        "id": canonical,
        "name": detail["name"],
        "kind": detail["kind"],
        "classic": detail["classic"],
        "conceptPreview": concept_preview,
        "canRetire": len(blockers) == 0,
        "requiresConfirmation": len(warnings) > 0,
        "blockers": blockers,
        "warnings": warnings,
        "mergedMembers": merged_members,
        "peoplePack": usage,
        "pipelines": pipelines,
        "conceptCount": concept_count,
        "hasApprovedConcept": has_approved_concept,
        "hasFinalAsset": has_final,
        "review": review,
    }


def merge_records(body, workspace) -> dict:
    if not isinstance(body, dict):
        raise ValueError("Merge payload must be a JSON object with 'keep' and 'merge'.")
    keep = body.get("keep")
    members = body.get("merge")
    if not isinstance(keep, str) or not isinstance(members, list) or not members:
        raise ValueError("Provide a 'keep' NPC ID and a non-empty 'merge' list.")
    catalog = workspace.catalog()
    if keep not in catalog.npcs:
        raise NotFoundError(f"Keeper '{keep}' is not an active People record.")
    for member in members:
        if not isinstance(member, str) or member not in catalog.npcs:
            raise NotFoundError(f"Member '{member}' is not an active People record.")
    if keep in members or len(set(members)) != len(members):
        raise ValueError("Each NPC ID must appear once and 'keep' cannot appear in 'merge'.")

    workspace.run_npc_ledger_action({"keep": keep, "merge": members})
    return {"message": f"Merged {', '.join(members)} into {keep}.", "keep": keep, "merged": members}


def retire_record(npc_id: str, body, workspace) -> dict:
    refs = references_for(npc_id, workspace)
    if not refs["canRetire"]:
        raise ConflictError(refs["blockers"][0], details={"references": refs})
    force = isinstance(body, dict) and body.get("force") is True
    if refs["requiresConfirmation"] and not force:
        raise ConflictError(
            "This record has active references. Review them and pass force=true to confirm retirement.",
            details={"references": refs},
        )
    workspace.run_npc_ledger_action({"retire": [refs["id"]]})
    return {"message": f"Retired {refs['id']}.", "retired": [refs["id"]]}


def references_batch(body, workspace) -> dict:
    if not isinstance(body, dict) or not isinstance(body.get("ids"), list) or not body["ids"]:
        raise ValueError("Provide a non-empty 'ids' array of People records.")
    seen = []
    for raw in body["ids"]:
        if isinstance(raw, str) and raw.strip() and raw not in seen:
            seen.append(raw)
    if not seen:
        raise ValueError("Provide a non-empty 'ids' array of People records.")
    items = [references_for(npc_id, workspace) for npc_id in seen]
    # Deduplicate after canonical resolution.
    unique = []
    unique_ids = set()
    for item in items:
        if item["id"] not in unique_ids:
            unique_ids.add(item["id"])
            unique.append(item)
    return {
        "items": unique,
        "canRetireAll": all(item["canRetire"] for item in unique),
        "requiresConfirmation": any(item["requiresConfirmation"] for item in unique),
    }


def retire_batch(body, workspace) -> dict:
    report = references_batch(body, workspace)
    items = report["items"]
    blocked = [item for item in items if not item["canRetire"]]
    if blocked:
        names = ", ".join(item["id"] for item in blocked)
        raise ConflictError(
            f"Cannot retire merge keeper record(s): {names}. Remove them from the selection first.",
            details={"items": items},
        )
    force = isinstance(body, dict) and body.get("force") is True
    guarded = [item for item in items if item["requiresConfirmation"]]
    if guarded and not force:
        names = ", ".join(item["id"] for item in guarded)
        raise ConflictError(
            f"Active references found on {names}. Review them and pass force=true to confirm batch retirement.",
            details={"items": items},
        )
    ids = [item["id"] for item in items]
    workspace.run_npc_ledger_action({"retire": ids})
    return {"message": f"Retired {len(ids)} record(s): {', '.join(ids)}.", "retired": ids}


def retired_records(workspace) -> dict:
    ledger = read_json(workspace.merges, {})
    retired = sorted(src for src, dst in ledger.items() if dst is None) if isinstance(ledger, dict) else []
    return {"retired": retired}


def restore_record(npc_id: str, workspace) -> dict:
    ledger = read_json(workspace.merges, {})
    if not isinstance(ledger, dict) or npc_id not in ledger or ledger[npc_id] is not None:
        raise NotFoundError(f"'{npc_id}' is not currently retired.")
    workspace.run_npc_ledger_action({"restore": [npc_id]})
    return {"message": f"Restored {npc_id}.", "restored": [npc_id]}


def register_routes(router, workspace):
    """Register in-CMS People comparison, merge, retirement, and restore routes."""
    router.post("/api/people/compare", lambda req: compare_records(req.json(), workspace))
    router.post("/api/people/merge", lambda req: merge_records(req.json(), workspace))
    router.post("/api/people/references-batch", lambda req: references_batch(req.json(), workspace))
    router.post("/api/people/retire-batch", lambda req: retire_batch(req.json(), workspace))
    router.get("/api/people/retired", lambda _req: retired_records(workspace))
    router.get("/api/people/{id}/references", lambda req: references_for(req.params["id"], workspace))
    router.post("/api/people/{id}/retire", lambda req: retire_record(req.params["id"], req.json() if req.raw_body else {}, workspace))
    router.post("/api/people/{id}/restore", lambda req: restore_record(req.params["id"], workspace))
