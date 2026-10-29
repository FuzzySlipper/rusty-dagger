"""Assembles the first-person container's held.json from the authored arms and rig and a capture's outputs.

usage: python3 scripts/fp-arms/assemble.py MODEL.json OUT_DIR BAKE.json [BAKE.json ...]

MODEL.json is fp-capture describe.py's output (the seated hand's hold and wrist in the dagger's frame); each BAKE.json
is one action's fp-capture bake (stance, poses, keys). The first bake's stance seats the dagger at rest. The dagger look
is held as the capture held it: its hold is the capture's, and its grip is where the Engine's wrist placement
(grip - turn * palm) puts the wrist where the capture had it. Writes OUT_DIR/first-person/held.json.
"""
import json
import math
import sys
from pathlib import Path

here = Path(__file__).parent


def turn(d):
    """HeldFrames.Turn as a 3x3 (rows), R = Ry Rx Rz."""
    x, y, z = (math.radians(v) for v in d)
    Rx = [[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]]
    Ry = [[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]]
    Rz = [[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]]

    def mul(a, b):
        return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(mul(Ry, Rx), Rz)


model_path, out, *bakes = sys.argv[1:]
hand = json.load(open(model_path))["hand"]
rig = json.load(open(here / "rig.json"))
arms = json.load(open(here / "arms.json"))
R = turn(hand["hold"])
palm = rig["palm"]
grip = [hand["wrist"][i] + sum(R[i][k] * palm[k] for k in range(3)) for i in range(3)]

motion = {"poses": {"rest": {"position": [0, 0, 0], "rotation": [0, 0, 0]}}, "tracks": {}}
stance = None
for path in bakes:
    bake = json.load(open(path))
    stance = stance or bake["stance"]
    motion["poses"].update(bake["poses"])
    motion["tracks"].update(bake["tracks"])

held = {
    "weapons": ["weapon.dagger.steel"],
    "rig": rig,
    "arms": arms,
    "look": {"model": "first-person/dagger.glb", "stance": stance,
             "grip": [round(v, 5) for v in grip], "hold": hand["hold"]},
    "motion": motion,
}
target = Path(out) / "first-person" / "held.json"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(held, indent=1) + "\n")
print("ASSEMBLED", target, "tracks", list(motion["tracks"]))
