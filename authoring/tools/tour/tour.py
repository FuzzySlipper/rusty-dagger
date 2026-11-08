#!/usr/bin/env python3
"""Tour the world for a look at many environments at once: each scene of a tour file goes somewhere (a place, then the
nearest open ground of a kind), sets the season, hour and weather, looks one way and captures a world frame for each
variant it compares; a contact sheet of every frame, labelled, ends the tour.

It drives a hosted playtest session (crew-services `playtest`, profile rusty-dagger-hosted): the session's own host runs
this checkout, the page presses Begin on the title's wilderness start (choose it there once: the host keeps the
choice), the host's live debug moves the player
(world.travel, world.scout, world.season, world.hour, world.weather) and its frame capture takes the pictures. A host
that restarted after a rebuild is back at the title, so a tour begins the game whenever none is running.

    tour.py TOUR.json [--session ID | --profile PROFILE] [--out DIR] [--only NAME ...] [--keep]

Without --session it starts a session and stops it at the end (--keep leaves it running). Frames go to
local/evidence/tour-NAME-TIME/ unless --out says otherwise, beside a scenes.json of what each one shows.

A tour file is {"name": ..., "scenes": [scene, ...]}; a scene is
    {"name": "desert sand",            what the sheet calls it
     "travel": "0/5",                  a place to land first (name or region/index), optional
     "scout": "sand", "km": 20,        then the nearest open ground mostly of this kind (world.scout), optional
     "season": "Winter",               optional; the hour defaults to 12 and the weather to Sunny
     "pitch": -15, "yaw": 0,           degrees: the view's pitch, and its turn from the scene's arrival
     "compare": {"flat": "none", "cover": "all"},
     "reset": ["world.groundcover all"], "settle": 2}
`compare` names settings to capture one after another, each a developer command or, for short, a world.groundcover
setting, waiting `settle` seconds after each; `reset` runs after them (by default world.groundcover all). Without
`compare` the scene takes one frame as it is.
"""
import argparse, json, subprocess, sys, time, urllib.error, urllib.request
from datetime import datetime
from pathlib import Path

from PIL import Image, ImageDraw

REPOSITORY = Path(__file__).resolve().parents[3]
PROFILE = "rusty-dagger-hosted"
WILDERNESS = "Developer: wilderness start"
WIDTH, HEIGHT = 1920, 1080
SETTLE_SECONDS = 45


def playtest(*args):
    result = subprocess.run(["playtest", *args], capture_output=True, text=True)
    if result.returncode:
        raise SystemExit(f"playtest {' '.join(args[:2])} failed: {result.stderr.strip() or result.stdout.strip()}")
    return json.loads(result.stdout) if result.stdout.strip().startswith("{") else result.stdout


class Tour:
    def __init__(self, session):
        self.session = session
        self.origin = playtest("status", session)["session"]["launch"]["host"]["local_url"].rstrip("/")

    def debug(self, command):
        request = urllib.request.Request(f"{self.origin}/__rusty/product/runtime/debug/execute", command.encode(),
                                         {"content-type": "text/plain; charset=utf-8"})
        try:
            with urllib.request.urlopen(request, timeout=300) as response:
                return response.read().decode().strip()
        except urllib.error.HTTPError as refusal:
            # A command the game refuses answers 422 with its reason, which the tour reports and goes on from.
            if refusal.code != 422:
                raise
            return f"refused {command}: {refusal.read().decode().strip()}"

    def playing(self):
        return json.loads(self.debug("world.where"))["playing"]

    def page(self, op):
        return playtest("browser", self.session, "--json", json.dumps(op))

    def begin(self):
        """Begins the wilderness start when no game runs, waiting out a host that is still building or restarting."""
        for _ in range(120):
            try:
                if self.playing():
                    return
                break
            except (OSError, ValueError):
                time.sleep(5)
        self.page({"op": "click", "selector": ".dagger-entry-begin"})
        for _ in range(60):
            time.sleep(2)
            if self.playing():
                # The page's loading dialog closes a moment after the game answers.
                time.sleep(8)
                return
        raise SystemExit(f"No game began: choose '{WILDERNESS}' on the title once (the host remembers it) and run again.")

    def settle(self):
        """Waits until the horizon has every tile it asked for, as far as SETTLE_SECONDS allow."""
        until = time.time() + SETTLE_SECONDS
        while time.time() < until and "settled True" not in self.debug("world.horizon"):
            time.sleep(2)
        time.sleep(3)

    def look(self, yaw, pitch):
        here = json.loads(self.debug("world.where"))
        playtest("assist", self.session, "--json", json.dumps(
            {"op": "look", "yaw": yaw, "pitch": pitch - here["pitchDegrees"]}))

    def capture(self, path):
        with urllib.request.urlopen(f"{self.origin}/__rusty/product/runtime/frames/capture?format=png&width={WIDTH}&height={HEIGHT}",
                                    timeout=120) as response:
            data = response.read()
        path.write_bytes(data[data.index(b"\x89PNG"):])

    def scene(self, scene, out, index):
        self.begin()
        said = []
        for key, command in (("travel", "world.travel"), ("season", "world.season")):
            if scene.get(key):
                said.append(self.debug(f"{command} {scene[key]}"))
        said.append(self.debug(f"world.hour {scene.get('hour', 12)}"))
        said.append(self.debug(f"world.weather {scene.get('weather', 'Sunny')}"))
        refused = None
        if scene.get("scout"):
            said.append(self.debug(f"world.scout {scene['scout']} {scene.get('km', 10)}"))
            # A scene that could not reach its ground still captures where it is, labelled as such.
            refused = said[-1] if said[-1].startswith("refused") else None
        self.settle()
        self.look(scene.get("yaw", 0), scene.get("pitch", -15))
        frames = []
        for label, setting in (scene.get("compare") or {"": None}).items():
            if setting:
                # A setting is a developer command, or a world.groundcover setting for short.
                said.append(self.debug(setting if setting.startswith("world.") else f"world.groundcover {setting}"))
                time.sleep(scene.get("settle", 2))
            path = out / f"{index:02d}-{slug(scene['name'])}{'-' + slug(label) if label else ''}.png"
            self.capture(path)
            frames.append({"label": label, "path": path.name})
        for command in scene.get("reset", ["world.groundcover all"] if scene.get("compare") else []):
            self.debug(command)
        where = json.loads(self.debug("world.where"))
        print(f"{scene['name']}: {where.get('place')} {' | '.join(s[:90] for s in said)}", flush=True)
        name = f"{scene['name']} (NOT REACHED: {refused[:80]})" if refused else scene["name"]
        return {"name": name, "frames": frames, "said": said, "where": where}


def slug(text):
    return "".join(c if c.isalnum() else "-" for c in text.lower()).strip("-")


def sheet(results, out):
    """Every frame at half size, a scene to a row, each labelled."""
    columns = max(len(result["frames"]) for result in results)
    w, h, band = WIDTH // 2, HEIGHT // 2, 28
    image = Image.new("RGB", (w * columns, (h + band) * len(results)), "black")
    draw = ImageDraw.Draw(image)
    for row, result in enumerate(results):
        for column, frame in enumerate(result["frames"]):
            x, y = column * w, row * (h + band)
            image.paste(Image.open(out / frame["path"]).convert("RGB").resize((w, h)), (x, y + band))
            draw.text((x + 8, y + 8), f"{result['name']}{' - ' + frame['label'] if frame['label'] else ''}", fill="white")
    image.save(out / "sheet.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("tour", type=Path)
    parser.add_argument("--session", help="a running hosted playtest session to use")
    parser.add_argument("--out", type=Path)
    parser.add_argument("--only", nargs="*", help="run only the scenes with these names")
    parser.add_argument("--keep", action="store_true", help="leave a session this tour started running")
    parser.add_argument("--profile", default=PROFILE, help="the hosted playtest profile to start, when no --session is given")
    args = parser.parse_args()
    tour = json.loads(args.tour.read_text())
    scenes = [scene for scene in tour["scenes"] if not args.only or scene["name"] in args.only]
    out = args.out or REPOSITORY / "local/evidence" / f"tour-{tour['name']}-{datetime.now():%Y%m%d-%H%M}"
    out.mkdir(parents=True, exist_ok=True)
    session = args.session or playtest("start", args.profile, *(["--keep"] if args.keep else []))["id"]
    try:
        driver = Tour(session)
        results = [driver.scene(scene, out, index) for index, scene in enumerate(scenes)]
    finally:
        if not args.session and not args.keep:
            playtest("stop", session)
    (out / "scenes.json").write_text(json.dumps(results, indent=1) + "\n")
    sheet(results, out)
    print(out / "sheet.png")


if __name__ == "__main__":
    sys.exit(main())
