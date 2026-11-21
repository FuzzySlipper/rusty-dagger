"""Read character stage records and the confirmed runtime exports they refer to."""

from library import STAGE_MOUNT, asset_url, file_link, read_json

STAGES = {
    "00-brief": "Brief", "01-concept": "Concept", "02-guide": "Scaffold", "03-clip": "Animation",
    "04-cutouts": "Cutouts", "05-cameras": "Cameras", "06-first-fit": "First fit", "07-refine": "Refinement",
    "08-final": "Final bake", "09-game": "Game representation",
}
REVIEW_STATES = {"needs-Patch", "needs-check"}


def character_index(library):
    ready = {entry["id"]: entry for entry in read_json(library.characters / "ready.json", {}).get("ready", [])}
    records = {}
    for state_path in sorted(library.characters.glob("*/character.json")):
        pipeline_id = state_path.parent.name
        state = read_json(state_path, {})
        if state.get("id") != pipeline_id:
            continue
        nodes = state.get("stages", {})
        stages = [stage_row(key, name, nodes.get(key, {})) for key, name in STAGES.items()]
        concept = concept_outputs(library, pipeline_id, nodes.get("01-concept", {}))
        final = final_output(library, pipeline_id, nodes.get("09-game", {}), ready.get(pipeline_id))
        records[pipeline_id] = {
            "id": pipeline_id,
            "name": state.get("name") or pipeline_id,
            "recipe": state.get("recipe"),
            "stages": stages,
            "concepts": concept,
            "final": final,
            "stateUrl": asset_url(STAGE_MOUNT, f"{pipeline_id}/character.json"),
            "timelineUrl": file_link(library.characters, STAGE_MOUNT, f"{pipeline_id}/index.html"),
        }
    return records


def stage_row(key, name, stage):
    batch = stage.get("review", {}).get("batch") or {}
    return {
        "id": key, "name": name, "state": stage.get("state", "empty"),
        "note": stage.get("why") or stage.get("note") or "",
        "reviewUrl": batch.get("url"),
        "selection": [item.get("name", "") if isinstance(item, dict) else item for item in stage.get("selected", [])],
    }


def concept_outputs(library, pipeline_id, stage):
    result = []
    for direction in ("front", "back"):
        recorded = (stage.get("outputs", {}).get(direction) or {}).get("path")
        path = recorded or f"01-concept/approved/{direction}.png"
        url = file_link(library.characters, STAGE_MOUNT, f"{pipeline_id}/{path}")
        if url:
            result.append({"path": path, "url": url, "name": f"{direction.title()} concept", "pipeline": pipeline_id,
                           "approved": bool(recorded and stage.get("state") == "approved"),
                           "recorded": bool(recorded)})
    return result


def final_output(library, pipeline_id, game, ready):
    if game.get("state") != "approved" or not ready:
        return None
    path = ready.get("engine_glb")
    url = file_link(library.characters, STAGE_MOUNT, path)
    playable = file_link(library.characters, STAGE_MOUNT, ready.get("playable"))
    if not url and not playable:
        return None
    return {"pipeline": pipeline_id, "name": ready.get("runtime_body") or "Approved game representation",
            "path": path if url else ready["playable"], "url": url or playable, "playable": playable,
            "note": ready.get("patch_said")}
