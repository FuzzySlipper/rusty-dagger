#!/usr/bin/env python3
"""Submit concept images and retrieve the approved artwork from the authoring CMS."""

import argparse
import base64
import json
import os
from pathlib import Path
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen


def request(origin, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    with urlopen(Request(origin + path, data=data, headers={"Content-Type": "application/json"}), timeout=60) as response:
        return json.load(response)


def add_batch(origin, path, manifest_path):
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    batch = manifest["batch"]
    candidates = manifest["candidates"]
    if not isinstance(candidates, list) or not candidates:
        raise ValueError("The manifest needs a nonempty candidates list.")
    for candidate in candidates:
        image = manifest_path.parent / candidate["image"]
        details = manifest.get("defaults", {}) | {key: value for key, value in candidate.items() if key not in ("image", "name")}
        row = request(origin, path + "/candidates", {
            "batch": batch, "name": candidate.get("name", image.stem), "filename": image.name,
            "image": base64.b64encode(image.read_bytes()).decode(), "details": details,
        })
        print(f"{row['batch']} · {row['name']} · {row['id']}")
    return {"review": origin + "/#concepts/" + path.rsplit("/", 1)[-1]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default=os.getenv("DAGGER_CMS_URL", "http://127.0.0.1:8784"))
    commands = parser.add_subparsers(dest="command", required=True)
    add = commands.add_parser("add", help="submit a batch using a JSON manifest")
    add.add_argument("subject")
    add.add_argument("manifest", type=Path)
    listing = commands.add_parser("list", help="show candidates and front and back approvals")
    listing.add_argument("subject")
    approved = commands.add_parser("approved", help="retrieve the approved front or back and its generation details")
    approved.add_argument("subject")
    approved.add_argument("--view", choices=("front", "back"), default="front")
    approved.add_argument("--image", type=Path, help="save the approved image to this path")
    args = parser.parse_args()
    origin = args.url.rstrip("/")
    path = "/api/concepts/" + quote(args.subject, safe="")
    try:
        result = add_batch(origin, path, args.manifest) if args.command == "add" else request(origin, path)
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
