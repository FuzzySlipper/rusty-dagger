#!/usr/bin/env python3
"""The joints of a rigged scaffold as blender_path.py's stick figure: a capsule mannequin in the character's stance.

    stick_fit.py SCAFFOLD.glb OUT.json

The orbit guide must not look like the character (a guide that does is blended into it: villager 381, 2026-11-16), so the guide is
a stick figure with the scaffold's joints, scaled to the 1.72 m the guide renderer's fixed depth range expects, standing on y = 0
at the origin (x = the character's left, y up, z = the front).
"""
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from retarget_clip import load_rig   # noqa: E402

MAP = {'neck': 'Neck', 'rsho': 'RightArm', 'relb': 'RightForeArm', 'rwri': 'RightHand', 'lsho': 'LeftArm', 'lelb': 'LeftForeArm', 'lwri': 'LeftHand',
       'rhip': 'RightUpLeg', 'rkne': 'RightLeg', 'rank': 'RightFoot', 'lhip': 'LeftUpLeg', 'lkne': 'LeftLeg', 'lank': 'LeftFoot'}
rig = load_rig(sys.argv[1]); names = list(rig['names']); B = np.asarray(rig['bind'], float); P = np.asarray(rig['positions'], float)
lo, hi = P.min(0), P.max(0); k = 1.72 / (hi[1] - lo[1]); c = np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2])
fit = {'joints': {a: [round(float(x), 4) for x in (B[names.index(b)] - c) * k] for a, b in MAP.items()},
       'source': f'{sys.argv[1]}: rig joints scaled to a 1.72 m figure (x left, y up, z front)'}
Path(sys.argv[2]).write_text(json.dumps(fit, indent=1) + '\n'); print(json.dumps({'joints': len(fit['joints']), 'left_shoulder_x': fit['joints']['lsho'][0]}))
