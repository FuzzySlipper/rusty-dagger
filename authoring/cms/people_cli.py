#!/usr/bin/env python3
"""Inspect, validate, and update daggerfall.people cast, spawners, and block slots via the CMS."""

import argparse
import json
import os
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode
from urllib.request import Request, urlopen


def request(origin: str, path: str, *, method: str = "GET", body=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    headers = {"Content-Type": "application/json"} if body is not None else {}
    with urlopen(Request(origin + path, data=data, headers=headers, method=method), timeout=30) as response:
        return json.load(response)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default=os.getenv("DAGGER_CMS_URL", "http://127.0.0.1:8784"))
    commands = parser.add_subparsers(dest="command", required=True)

    commands.add_parser("overview", help="show cast/spawner/slot counts and anomaly totals")
    commands.add_parser("validate", help="validate cast, spawners, and block slots")

    cast_cmd = commands.add_parser("cast", help="list or inspect cast individuals")
    cast_cmd.add_argument("id", nargs="?", help="cast individual ID to inspect")

    edit_cast = commands.add_parser("edit-cast", help="update metadata or visual on a cast individual")
    edit_cast.add_argument("id")
    edit_cast.add_argument("--name")
    edit_cast.add_argument("--race", help="race string or 'none' to clear")
    edit_cast.add_argument("--gender", choices=("Male", "Female"))
    edit_cast.add_argument("--interaction", choices=("none", "ambient-bark", "full-dialogue"))
    edit_cast.add_argument("--harm", choices=("protected", "law-protected-civilian", "combatant"))
    edit_cast.add_argument("--archive", type=int)
    edit_cast.add_argument("--record", type=int)

    spawner_cmd = commands.add_parser("spawners", help="list or inspect typed spawners")
    spawner_cmd.add_argument("id", nargs="?", help="spawner ID to inspect")

    blocks_cmd = commands.add_parser("blocks", help="query block/slot cross-references")
    blocks_cmd.add_argument("--q", help="text search over block, slot, spawner, cast, or faction")
    blocks_cmd.add_argument("--spawner")
    blocks_cmd.add_argument("--cast", dest="cast_id")
    blocks_cmd.add_argument("--kind", choices=("rmb-interior", "rmb-exterior", "rdb-dungeon"))
    blocks_cmd.add_argument("--building-type", type=int)
    blocks_cmd.add_argument("--derive-regional", choices=("true", "false"))
    blocks_cmd.add_argument("--interaction", choices=("none", "ambient-bark", "full-dialogue"))
    blocks_cmd.add_argument("--harm", choices=("protected", "law-protected-civilian", "combatant"))
    blocks_cmd.add_argument("--page", type=int, default=1)
    blocks_cmd.add_argument("--page-size", type=int, default=50)

    args = parser.parse_args(argv)
    origin = args.url.rstrip("/")

    try:
        if args.command == "overview":
            result = request(origin, "/api/people-pack/overview")["counts"]
        elif args.command == "validate":
            result = request(origin, "/api/people-pack/validate")
        elif args.command == "cast":
            path = f"/api/people-pack/cast/{quote(args.id, safe='')}" if args.id else "/api/people-pack/cast"
            result = request(origin, path)
        elif args.command == "edit-cast":
            body = {}
            if args.name is not None:
                body["name"] = args.name
            if args.race is not None:
                body["race"] = None if args.race.lower() == "none" else args.race
            if args.gender is not None:
                body["gender"] = args.gender
            if args.interaction is not None:
                body["interaction"] = args.interaction
            if args.harm is not None:
                body["harm"] = args.harm
            if (args.archive is None) != (args.record is None):
                parser.error("--archive and --record must be supplied together")
            if args.archive is not None:
                body["visual"] = {"archive": args.archive, "record": args.record}
            if not body:
                parser.error("edit-cast requires at least one field flag")
            result = request(origin, f"/api/people-pack/cast/{quote(args.id, safe='')}", method="PUT", body=body)
        elif args.command == "spawners":
            path = f"/api/people-pack/spawners/{quote(args.id, safe='')}" if args.id else "/api/people-pack/spawners"
            result = request(origin, path)
        elif args.command == "blocks":
            params = {
                key: value
                for key, value in (
                    ("q", args.q),
                    ("spawner", args.spawner),
                    ("castId", args.cast_id),
                    ("kind", args.kind),
                    ("buildingType", args.building_type),
                    ("deriveRegional", args.derive_regional),
                    ("interaction", args.interaction),
                    ("harm", args.harm),
                    ("page", args.page),
                    ("pageSize", args.page_size),
                )
                if value is not None
            }
            result = request(origin, "/api/people-pack/blocks?" + urlencode(params))
        else:
            parser.error("Unknown command")
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 0
    except HTTPError as error:
        print(error.read().decode("utf-8", errors="replace"), file=sys.stderr)
        return 1
    except URLError as error:
        print(error.reason, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
