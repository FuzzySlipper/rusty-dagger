"""In-CMS People (npc-###) curation: art-need enrichment, side-by-side compare/merge, and guarded retirement."""

from library import read_json
from router import ConflictError, NotFoundError


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


def enrich_people_rows(rows: list[dict], catalog, concept_store, people_pack) -> list[dict]:
    """Attach lightweight art-need and daggerfall.people usage summaries to each People catalog row."""
    pack_available = people_pack.available()
    index = people_pack._index() if pack_available else None

    # Pre-group (archive, record) pairs by target npc_id once.
    pairs_by_npc: dict[str, list[tuple[int, int]]] = {}
    for alias, target in catalog.aliases.items():
        if target not in catalog.npcs:
            continue
        arch_str, sep, rec_str = alias.partition("-")
        if sep and arch_str.isdigit() and rec_str.isdigit():
            pairs_by_npc.setdefault(target, []).append((int(arch_str), int(rec_str)))

    # Pre-group aliases by canonical npc_id for concept-stage lookups.
    aliases_by_npc: dict[str, list[str]] = {}
    for old, target in catalog.aliases.items():
        if old != target and target in catalog.npcs:
            aliases_by_npc.setdefault(target, []).append(old)

    concept_subjects = set(concept_store.subjects()) if concept_store is not None else set()

    enriched = []
    for row in rows:
        npc_id = row["id"]
        cast_ids: list[str] = []
        spawner_ids: list[str] = []
        slot_count = 0
        if index is not None:
            for pair in pairs_by_npc.get(npc_id, ()):
                slot_count += index["visual_slot_counts"].get(pair, 0)
                for cid in index["visual_cast_ids"].get(pair, ()):
                    if cid not in cast_ids:
                        cast_ids.append(cid)
                for ref in index["visual_spawner_refs"].get(pair, ()):
                    sid = ref["spawnerId"]
                    if sid not in spawner_ids:
                        spawner_ids.append(sid)

        has_approved_concept = False
        npc_aliases = tuple(aliases_by_npc.get(npc_id, ()))
        if concept_store is not None and (npc_id in concept_subjects or any(a in concept_subjects for a in npc_aliases)):
            stage = concept_store.stage(npc_id, npc_aliases)
            has_approved_concept = bool(stage.get("approved"))

        # Check pipeline concept/final status cheaply from catalog pipelines.
        pipelines = [catalog.pipelines[pid] for pid in row.get("pipelineIds", ()) if pid in catalog.pipelines]
        choice = catalog.links.get(npc_id, {})
        conflicts = catalog.conflicts.get(npc_id, [])
        if not has_approved_concept and "concept" not in conflicts:
            concepts = [art for item in pipelines for art in item["concepts"]]
            explicit = choice.get("concept")
            if explicit and any(c["pipeline"] == explicit["pipeline"] and c["path"] == explicit["path"] for c in concepts):
                has_approved_concept = True
            else:
                approved = [art for art in concepts if art["approved"] and art["name"] == "Front concept"]
                has_approved_concept = len(approved) == 1

        finals = [item["final"] for item in pipelines if item["final"]]
        explicit_final = choice.get("final")
        has_final = False
        if explicit_final and any(f["pipeline"] == explicit_final["pipeline"] and f["path"] == explicit_final["path"] for f in finals):
            has_final = True
        elif not explicit_final and "final" not in conflicts and len(finals) == 1:
            has_final = True

        enriched.append(row | {
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

    usage = detail.get("peoplePack") or {"visualKeys": [], "cast": [], "spawners": [], "slotCount": 0}
    pipelines = list(detail.get("pipelineIds", []))
    stage = workspace.concept_stage(canonical) if workspace.concept_store else None
    concept_count = len((stage or {}).get("candidates", []))
    approvals = (stage or {}).get("approvals") or {}
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
