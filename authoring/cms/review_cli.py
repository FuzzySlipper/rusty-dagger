#!/usr/bin/env python3
"""Read or update Rusty Dagger CMS reviews by category and stable ID."""

import argparse
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode
from urllib.request import Request, urlopen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default=os.getenv("DAGGER_CMS_URL", "http://127.0.0.1:8784"))
    commands = parser.add_subparsers(dest="command", required=True)
    marked = commands.add_parser("marked", help="list records in the temporary selection")
    marked.add_argument("category")
    marked.add_argument("--ids", action="store_true", help="print one ID per line")
    listing = commands.add_parser("list", help="list saved notes, marks and ratings")
    listing.add_argument("category")
    listing.add_argument("--rating", choices=("good", "maybe", "bad"))
    listing.add_argument("--prefix", help="match an ID prefix, such as 452/01-concept/ for stage assets")
    for command in ("get", "set"):
        item = commands.add_parser(command)
        item.add_argument("category")
        item.add_argument("id")
        if command == "set":
            marking = item.add_mutually_exclusive_group()
            marking.add_argument("--mark", dest="marked", action="store_true", default=None)
            marking.add_argument("--unmark", dest="marked", action="store_false")
            item.add_argument("--rating", choices=("good", "maybe", "bad", "clear"))
            notes = item.add_mutually_exclusive_group()
            notes.add_argument("--note")
            notes.add_argument("--note-file", type=argparse.FileType("r", encoding="utf-8"))
    clear = commands.add_parser("clear-marks")
    clear.add_argument("category")
    args = parser.parse_args()
    body = None
    if args.command in ("marked", "list"):
        filters = {"category": args.category}
        if args.command == "marked":
            filters["marked"] = "1"
        else:
            filters.update({key: value for key, value in (("rating", args.rating), ("prefix", args.prefix)) if value})
        path = "/api/reviews?" + urlencode(filters)
    elif args.command == "clear-marks":
        path = f"/api/marks/{quote(args.category, safe='')}/clear"
        body = {}
    else:
        path = f"/api/reviews/{quote(args.category, safe='')}/{quote(args.id, safe='')}"
        if args.command == "set":
            note = args.note_file.read() if args.note_file else args.note
            body = {key: value for key, value in (("marked", args.marked), ("note", note)) if value is not None}
            if args.rating:
                body["rating"] = None if args.rating == "clear" else args.rating
            if not body:
                parser.error("set requires --mark, --unmark, --rating, or a note")
    data = None if body is None else json.dumps(body).encode()
    try:
        with urlopen(Request(args.url.rstrip("/") + path, data=data,
                             headers={"Content-Type": "application/json"}), timeout=15) as response:
            result = json.load(response)
    except HTTPError as error:
        print(error.read().decode(), file=sys.stderr)
        return 1
    except URLError as error:
        print(error.reason, file=sys.stderr)
        return 1
    if args.command == "marked" and args.ids:
        for row in result:
            print(row["id"])
    else:
        print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
