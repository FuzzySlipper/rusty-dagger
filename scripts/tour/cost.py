"""Measure what the world costs to draw and update in a fixed set of heavy scenes.

Each scene of a cost file goes somewhere (a place, then the nearest open ground of a kind), sets the season, hour and
weather, lets the horizon settle and then, for each variant (the whole world first, then with one feature turned off),
reads the renderer's per-pass GPU time (`engine.renderer`, `gpu.passes`, medians of its recent frames), its frame
times, and the updates' own cost from the runtime telemetry (callback and post-callback, sampled for a few seconds).

It talks to any host started with live debug, by its origin: a hosted playtest session's (`--session`), or another
machine's (`--origin http://host:port`, for example den-win11's native build). The view is whatever the host renders
at, so start it at 1920x1080 (a playtest page: `playtest browser SESSION --json '{"op":"resize",...}'`).

    cost.py COST.json (--session ID | --origin URL) [--only NAME ...] [--out DIR]

A cost file is {"name": ..., "variants": {"label": [command, ...], ...}, "reset": [command, ...], "scenes": [scene, ...]};
a scene is {"name", "travel", "scout", "km", "season", "hour", "weather"}. Every variant starts from the reset.
Results go to local/evidence/cost-NAME-TIME/ (or --out) as cost.json and a cost.md table.
"""
import argparse
import json
import statistics
import time
import urllib.error
import urllib.request
from datetime import datetime
from pathlib import Path

REPOSITORY = Path(__file__).resolve().parents[2]
SETTLE_SECONDS = 60
MEASURE_SECONDS = 8


class Host:
    def __init__(self, origin):
        self.origin = origin.rstrip("/")

    def debug(self, command):
        request = urllib.request.Request(f"{self.origin}/__rusty/product/runtime/debug/execute", command.encode(),
                                         {"content-type": "text/plain; charset=utf-8"})
        try:
            with urllib.request.urlopen(request, timeout=600) as response:
                return response.read().decode().strip()
        except urllib.error.HTTPError as refusal:
            if refusal.code != 422:
                raise
            return f"refused {command}: {refusal.read().decode().strip()}"

    def telemetry(self):
        request = urllib.request.Request(f"{self.origin}/__rusty/product/runtime/diagnostics/read", b"{}",
                                         {"Content-Type": "application/json", "Origin": self.origin})
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.load(response)["telemetry"]["updateAttribution"]["latest"]

    def begin(self):
        for _ in range(120):
            try:
                if json.loads(self.debug("world.where"))["playing"]:
                    return
                break
            except (OSError, ValueError):
                time.sleep(5)
        raise SystemExit("No game is running on the host: begin one (the wilderness start) and run again.")

    def settle(self):
        until = time.time() + SETTLE_SECONDS
        while time.time() < until and "settled True" not in self.debug("world.horizon"):
            time.sleep(2)
        time.sleep(4)

    def measure(self):
        """The renderer's medians over its recent frames, and the updates seen over MEASURE_SECONDS."""
        seen, until = {}, time.time() + MEASURE_SECONDS
        while time.time() < until:
            latest = self.telemetry()
            seen[latest["simulationStep"]] = (int(latest["callbackDurationUs"]) / 1000, int(latest["postCallbackDurationUs"]) / 1000)
            time.sleep(0.01)
        renderer = json.loads(self.debug("engine.renderer"))["renderer"]
        passes = {p["pass"]: round(p["medianGpuMs"], 3) for p in renderer["gpu"]["passes"] if p["timedFrames"]}
        frames = renderer.get("stream", {}).get("medianMs") or renderer.get("window", {}).get("medianMs") or {}
        callbacks, posts = [v[0] for v in seen.values()], [v[1] for v in seen.values()]
        return {
            "gpuMs": round(sum(passes.values()), 3), "passes": passes, "frameMs": frames,
            "updates": len(seen),
            "callbackMs": {"p50": round(statistics.median(callbacks), 1), "max": round(max(callbacks), 1)},
            "postMs": {"p50": round(statistics.median(posts), 1), "max": round(max(posts), 1)},
            "adapter": renderer.get("adapter"),
        }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("costs", type=Path)
    where = parser.add_mutually_exclusive_group(required=True)
    where.add_argument("--session")
    where.add_argument("--origin")
    parser.add_argument("--only", nargs="*")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if args.session:
        import subprocess
        status = json.loads(subprocess.run(["playtest", "status", args.session], capture_output=True, text=True, check=True).stdout)
        origin = status["session"]["launch"]["host"]["local_url"]
    else:
        origin = args.origin
    host = Host(origin)
    plan = json.loads(args.costs.read_text())
    out = args.out or REPOSITORY / "local/evidence" / f"cost-{plan['name']}-{datetime.now():%Y%m%d-%H%M}"
    out.mkdir(parents=True, exist_ok=True)
    host.begin()
    results = []
    for scene in plan["scenes"]:
        if args.only and scene["name"] not in args.only:
            continue
        said = []
        if scene.get("travel"):
            said.append(host.debug(f"world.travel {scene['travel']}"))
        if scene.get("season"):
            said.append(host.debug(f"world.season {scene['season']}"))
        if scene.get("scout"):
            said.append(host.debug(f"world.scout {scene['scout']} {scene.get('km', 10)}"))
        said.append(host.debug(f"world.hour {scene.get('hour', 12)}"))
        said.append(host.debug(f"world.weather {scene.get('weather', 'Sunny')}"))
        refused = [line for line in said if line.startswith("refused")]
        place = json.loads(host.debug("world.where"))
        row = {"scene": scene["name"], "place": place.get("place"), "feet": place.get("feet"), "refused": refused, "variants": {}}
        for label, commands in plan["variants"].items():
            for command in plan.get("reset", []) + commands:
                host.debug(command)
            host.settle()
            row["variants"][label] = host.measure()
            print(scene["name"], label, json.dumps(row["variants"][label]), flush=True)
        for command in plan.get("reset", []):
            host.debug(command)
        results.append(row)
        (out / "cost.json").write_text(json.dumps(results, indent=1))
    (out / "cost.md").write_text(table(results))
    print(out / "cost.md")


def table(results):
    lines = []
    for row in results:
        whole = next(iter(row["variants"].values()))
        names = sorted({name for v in row["variants"].values() for name in v["passes"]}, key=lambda n: -whole["passes"].get(n, 0))
        lines += [f"### {row['scene']} ({row['place']}){' NOT REACHED: ' + '; '.join(row['refused']) if row['refused'] else ''}", "",
                  "| variant | GPU ms | " + " | ".join(names) + " | frame ms | update cb p50/max | post p50/max |",
                  "|---" * (len(names) + 5) + "|"]
        for label, v in row["variants"].items():
            # A stream reports its render time, a window its draw time.
            frame = v["frameMs"].get("render", v["frameMs"].get("draw", 0))
            lines.append(f"| {label} | {v['gpuMs']:.2f} | " + " | ".join(f"{v['passes'].get(n, 0):.2f}" for n in names)
                         + f" | {frame:.1f} | {v['callbackMs']['p50']}/{v['callbackMs']['max']} | {v['postMs']['p50']}/{v['postMs']['max']} |")
        lines.append("")
    return "\n".join(lines)


if __name__ == "__main__":
    main()
