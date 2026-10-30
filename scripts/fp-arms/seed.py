"""Seeds the authored held definition from a capture: the hand's hold on the dagger and the captured strikes.

usage: python3 scripts/fp-arms/seed.py MODEL.json BAKE.json [BAKE.json ...]

MODEL.json is fp-capture describe.py's output (the seated hand's hold and wrist in the dagger's frame, measured on the
arms capture.json names); each BAKE.json is one strike's fp-capture bake (stance, poses, keys), its action the classic
strike id. Rewrites content/worldrpg/first-person/daggerfall.held.json in place: the look's stance (the first bake's),
its hold (the capture's) and its grip (where the Engine's wrist placement, grip - turn * palm with the definition's
rig palm, puts the wrist where the capture had it), and each bake's track with its poses, replacing what the
workbench tuned there. Every other value, and every strike no bake names, is kept.
"""
import json
import math
import re
import sys
from pathlib import Path

definition = Path(__file__).resolve().parents[2] / "content/worldrpg/first-person/daggerfall.held.json"


def turn(d):
    """HeldFrames.Turn as a 3x3 (rows), R = Ry Rx Rz."""
    x, y, z = (math.radians(v) for v in d)
    Rx = [[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]]
    Ry = [[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]]
    Rz = [[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]]

    def mul(a, b):
        return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(mul(Ry, Rx), Rz)


model_path, *bakes = sys.argv[1:]
if not bakes:
    sys.exit(__doc__)
held = json.loads(definition.read_text())
hand = json.load(open(model_path))["hand"]
R = turn(hand["hold"])
palm = held["rig"]["palm"]
look = held["look"]
look["hold"] = hand["hold"]
look["grip"] = [round(hand["wrist"][i] + sum(R[i][k] * palm[k] for k in range(3)), 5) for i in range(3)]
motion = held["motion"]
for path in bakes:
    bake = json.load(open(path))
    if path == bakes[0]:
        look["stance"] = bake["stance"]
    for action in bake["tracks"]:
        # A re-seeded strike drops its old poses, so poses the new keys no longer name do not linger.
        for key in motion["tracks"].get(action, []):
            motion["poses"].pop(key["pose"], None)
    motion["poses"].update(bake["poses"])
    motion["tracks"].update(bake["tracks"])
text = json.dumps(held, indent=2)
# Arrays of plain values on one line, as the definition is written.
text = re.sub(r"\[\s+([^\[\]{}]*?)\s+\]", lambda m: "[" + ", ".join(v.strip() for v in m.group(1).split(",")) + "]", text)
definition.write_text(text + "\n")
print("SEEDED", definition, "tracks", list(motion["tracks"]))
