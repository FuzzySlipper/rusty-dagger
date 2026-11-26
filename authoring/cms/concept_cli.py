#!/usr/bin/env python3
"""Submit, approve, and retrieve concept art (front/left/right/back) from the authoring CMS."""

import argparse
import base64
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import struct
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen

VIEWS = ("front", "left", "right", "back")


def request(origin, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    with urlopen(Request(origin + path, data=data, headers={"Content-Type": "application/json"}), timeout=60) as response:
        return json.load(response)


def _extract_comfy_png_defaults(image_bytes: bytes) -> dict:
    """Best-effort ComfyUI tEXt prompt metadata extraction for one-shot CLI uploads."""
    if not image_bytes.startswith(b"\x89PNG\r\n\x1a\n"):
        return {}
    pos = 8
    raw_prompt = None
    while pos + 12 <= len(image_bytes):
        length = struct.unpack(">I", image_bytes[pos:pos + 4])[0]
        ctype = image_bytes[pos + 4:pos + 8]
        cdata = image_bytes[pos + 8:pos + 8 + length]
        if ctype == b"tEXt" and cdata.startswith(b"prompt\x00"):
            raw_prompt = cdata.split(b"\x00", 1)[1].decode("utf-8", errors="replace")
            break
        pos += 12 + length
    if not raw_prompt:
        return {}
    try:
        graph = json.loads(raw_prompt)
    except ValueError:
        return {}
    if not isinstance(graph, dict):
        return {}

    prompt_text = ""
    seed = None
    model = None
    for node in graph.values():
        if not isinstance(node, dict):
            continue
        ct = str(node.get("class_type") or "")
        inp = node.get("inputs") or {}
        if not isinstance(inp, dict):
            continue
        if not model:
            for k in ("unet_name", "ckpt_name", "model_name"):
                if isinstance(inp.get(k), str) and inp[k].strip():
                    model = inp[k].strip()
                    break
        if seed is None:
            for k in ("seed", "noise_seed"):
                v = inp.get(k)
                if isinstance(v, (int, str)) and not isinstance(v, bool):
                    seed = str(v)
                    break
        if not prompt_text and ("TextEncode" in ct or "Conditioning" in ct):
            for k in ("prompt", "text", "t5xxl", "clip_l"):
                v = inp.get(k)
                if isinstance(v, str) and v.strip():
                    prompt_text = v.strip()
                    break
    out = {}
    if prompt_text:
        out["prompt"] = prompt_text
    if seed is not None:
        out["seed"] = seed
    if model:
        out["model"] = model
    return out


def add_batch(origin, path, manifest_path, default_approve=None):
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    batch = manifest["batch"]
    candidates = manifest["candidates"]
    if not isinstance(candidates, list) or not candidates:
        raise ValueError("The manifest needs a nonempty candidates list.")
    if default_approve and len(candidates) > 1:
        raise ValueError("--approve on `add` applies to a 1-candidate manifest; set \"approve\" per candidate in multi-image manifests.")
    for candidate in candidates:
        image = manifest_path.parent / candidate["image"]
        details = manifest.get("defaults", {}) | {
            key: value for key, value in candidate.items() if key not in ("image", "name", "approve")
        }
        payload = {
            "batch": batch,
            "name": candidate.get("name", image.stem),
            "filename": image.name,
            "image": base64.b64encode(image.read_bytes()).decode(),
            "details": details,
        }
        approve = candidate.get("approve") or default_approve
        if approve:
            payload["approve"] = approve
        row = request(origin, path + "/candidates", payload)
        suffix = f" [approved: {', '.join(row['approvedViews'])}]" if row.get("approvedViews") else ""
        print(f"{row['batch']} · {row['name']} · {row['id']}{suffix}")
    return {"review": origin + "/#concepts/" + path.rsplit("/", 1)[-1]}


def upload_single(origin, path, image_path: Path, *, batch: str, name: str | None, prompt: str | None, seed: str | None, model: str | None, approve: list[str] | None):
    raw_bytes = image_path.read_bytes()
    comfy = _extract_comfy_png_defaults(raw_bytes)
    final_prompt = (prompt or "").strip() or comfy.get("prompt") or f"Uploaded via concept_cli ({image_path.name})"
    final_seed = seed if seed is not None else comfy.get("seed")
    if final_seed == "" or final_seed == "null":
        final_seed = None
    details = {"prompt": final_prompt, "seed": final_seed}
    final_model = (model or "").strip() or comfy.get("model")
    if final_model:
        details["model"] = final_model
    payload = {
        "batch": batch,
        "name": (name or "").strip() or image_path.stem,
        "filename": image_path.name,
        "image": base64.b64encode(raw_bytes).decode(),
        "details": details,
    }
    if approve:
        payload["approve"] = approve if len(approve) > 1 else approve[0]
    return request(origin, path + "/candidates", payload)


def approve_candidate(origin, path, candidate_ref: str, view: str):
    stage = request(origin, path)
    candidates = stage.get("candidates") or []
    match = next((c for c in candidates if c["id"] == candidate_ref), None)
    if match is None:
        by_name = [c for c in candidates if c["name"] == candidate_ref or f"{c['batch']}/{c['name']}" == candidate_ref]
        if len(by_name) == 1:
            match = by_name[0]
        elif len(by_name) > 1:
            raise ValueError(f"Multiple candidates named '{candidate_ref}'; pass the candidate ID or 'batch/name'.")
    candidate_id = match["id"] if match else candidate_ref
    return request(origin, path + "/approve", {"candidate": candidate_id, "view": view})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default=os.getenv("DAGGER_CMS_URL", "http://127.0.0.1:8784"))
    commands = parser.add_subparsers(dest="command", required=True)

    add = commands.add_parser("add", help="submit a batch using a JSON manifest (supports per-candidate 'approve')")
    add.add_argument("subject")
    add.add_argument("manifest", type=Path)
    add.add_argument("--approve", choices=VIEWS, default=None, help="approve a single-candidate manifest immediately")

    upload = commands.add_parser("upload", help="submit one concept image directly without writing a manifest")
    upload.add_argument("subject")
    upload.add_argument("image", type=Path)
    upload.add_argument("--batch", default="agent-" + datetime.now(timezone.utc).strftime("%Y-%m-%d"))
    upload.add_argument("--name", default=None)
    upload.add_argument("--prompt", default=None, help="generation prompt (auto-read from ComfyUI PNG if omitted)")
    upload.add_argument("--seed", default=None, help="generation seed or 'null' (auto-read from ComfyUI PNG if omitted)")
    upload.add_argument("--model", default=None, help="model name (auto-read from ComfyUI PNG if omitted)")
    upload.add_argument("--approve", choices=VIEWS, action="append", default=None,
                        help="mark this upload approved for one or more views (repeatable)")

    approve_cmd = commands.add_parser("approve", help="mark an existing candidate approved for front/left/right/back")
    approve_cmd.add_argument("subject")
    approve_cmd.add_argument("candidate", help="candidate ID, unique name, or 'batch/name'")
    approve_cmd.add_argument("--view", choices=VIEWS, default="front")

    listing = commands.add_parser("list", help="show candidates and front/left/right/back approvals")
    listing.add_argument("subject")

    approved = commands.add_parser("approved", help="retrieve the approved front/left/right/back view and its generation details")
    approved.add_argument("subject")
    approved.add_argument("--view", choices=VIEWS, default="front")
    approved.add_argument("--image", type=Path, help="save the approved image to this path")

    args = parser.parse_args()
    origin = args.url.rstrip("/")
    path = "/api/concepts/" + quote(args.subject, safe="")
    try:
        if args.command == "add":
            result = add_batch(origin, path, args.manifest, default_approve=args.approve)
        elif args.command == "upload":
            result = upload_single(
                origin, path, args.image,
                batch=args.batch, name=args.name, prompt=args.prompt,
                seed=args.seed, model=args.model, approve=args.approve,
            )
        elif args.command == "approve":
            result = approve_candidate(origin, path, args.candidate, args.view)
        else:
            result = request(origin, path)

        if args.command == "approved":
            result = result["approvals"][args.view] if "approvals" in result else (
                result["approved"] if args.view == "front" else None
            )
            if result is None:
                raise ValueError(f"{args.subject} has no approved {args.view} concept.")
            if args.image:
                with urlopen(origin + result["url"], timeout=60) as response:
                    args.image.write_bytes(response.read())
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 0
    except HTTPError as error:
        print(error.read().decode(), file=sys.stderr)
    except (OSError, URLError, ValueError, KeyError, TypeError) as error:
        print(error, file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
