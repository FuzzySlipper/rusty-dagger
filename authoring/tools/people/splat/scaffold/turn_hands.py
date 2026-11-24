#!/usr/bin/env python3
"""Re-rest a bound body's hands: turn each hand about its wrist so it lies along the forearm with the thumb forward.

    turn_hands.py SIZE_DIR SCAFFOLD.glb TRANSFORM.json SCAFFOLD_REPORT.json OUT_DIR [--straighten 1] [--turn 1]

SIZE_DIR holds a bound body (splat.npz in bake units, binding.npz on SCAFFOLD's bones), as bound_run.sh writes them.
SCAFFOLD_REPORT.json is transplant_hands.py's report (02-guide/scaffold/scaffold.json): per side the donor hand's axis (wrist to
fingertips) and its thumb direction, in the scaffold's frame.

Why (npc-468, Patch 2026-11-23: "his hands seem to have a goofy orientation that might impact his final animation"): a painting
in an open-handed A-pose gives a rest hand with the palm turned forward, the thumb out and up and the hand bent down from the
forearm. Mixamo clips only rotate the bones from the rest, so the hands kept that gesture through every walk and would fight a
grip on a staff. Here every Gaussian a hand or finger bone leads is rotated about the wrist joint: first the hand axis onto the
forearm (--straighten, the share of that rotation), then about the forearm until the thumb points forward (--turn, the share).
Positions and orientations both turn; scales and colours stay. OUT_DIR gets splat.npz, binding.npz (unchanged) and turn.json.
The scaffold mesh is not changed (the Engine GLB's hand normals still come from it).
"""
import argparse, json, shutil, sys
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as Rot

sys.path.insert(0, str(Path(__file__).parent))
from retarget_clip import load_rig   # noqa: E402


def between(u, v):
    u = u / np.linalg.norm(u); v = v / np.linalg.norm(v); ax = np.cross(u, v); s = np.linalg.norm(ax)
    return np.eye(3) if s < 1e-9 else Rot.from_rotvec(ax / s * np.arctan2(s, float(u @ v))).as_matrix()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('size', type=Path); ap.add_argument('scaffold'); ap.add_argument('transform'); ap.add_argument('report'); ap.add_argument('out', type=Path)
    ap.add_argument('--straighten', type=float, default=1.0); ap.add_argument('--turn', type=float, default=1.0)
    a = ap.parse_args()
    s = dict(np.load(a.size / 'splat.npz')); b = dict(np.load(a.size / 'binding.npz'))
    rig = load_rig(a.scaffold); names = list(rig['names']); J = {n: np.asarray(rig['bind'][i], float) for i, n in enumerate(names)}
    T = json.load(open(a.transform)); sc, R0, t0 = float(T['scale']), np.asarray(T['rotation'], float), np.asarray(T['translation'], float)
    rep = json.load(open(a.report)); M = s['means'].astype(np.float64); n = len(M)
    lead = np.array(names)[b['bones'][np.arange(n), b['weights'].argmax(1)]].astype(str)
    out = {}
    for side in ('Left', 'Right'):
        if side not in rep:
            continue
        sel = np.char.startswith(lead, side + 'Hand')
        f = J[side + 'Hand'] - J[side + 'ForeArm']; f /= np.linalg.norm(f)
        axis = np.asarray(rep[side]['axis'], float); thumb = np.asarray(rep[side]['thumb'], float)
        R1 = between(axis, f)
        R1 = Rot.from_rotvec(Rot.from_matrix(R1).as_rotvec() * a.straighten).as_matrix()
        t1 = R1 @ thumb; fwd = np.array([0, 0, 1.0])
        p = lambda v: (v - (v @ f) * f) / max(np.linalg.norm(v - (v @ f) * f), 1e-9)
        ang = float(np.arctan2(np.cross(p(t1), p(fwd)) @ f, p(t1) @ p(fwd)))
        R2 = Rot.from_rotvec(f * ang * a.turn).as_matrix(); R = R2 @ R1
        Rb = R0 @ R @ R0.T; w = sc * R0 @ J[side + 'Hand'] + t0            # in the bake frame: bake = sc * R0 v + t0
        M[sel] = w + (M[sel] - w) @ Rb.T
        q = s['quats'][sel].astype(np.float64)                             # wxyz
        qr = (Rot.from_matrix(Rb) * Rot.from_quat(q[:, [1, 2, 3, 0]])).as_quat()
        s['quats'][sel] = qr[:, [3, 0, 1, 2]].astype(s['quats'].dtype)
        out[side] = {'gaussians': int(sel.sum()), 'straighten_deg': round(float(np.degrees(np.linalg.norm(Rot.from_matrix(R1).as_rotvec()))), 1),
                     'turn_deg': round(float(np.degrees(ang * a.turn)), 1)}
    s['means'] = M.astype(s['means'].dtype)
    a.out.mkdir(parents=True, exist_ok=True)
    for f_ in ('splat.npz', 'binding.npz'):
        (a.out / f_).unlink(missing_ok=True)   # new files: the old ones may be hard-linked into a character folder
    np.savez_compressed(a.out / 'splat.npz', **s); np.savez_compressed(a.out / 'binding.npz', **b)
    for extra in ('score.json', 'graft.json', 'clean.json'):
        if (a.size / extra).exists() and a.size != a.out:
            shutil.copy(a.size / extra, a.out / extra)
    (a.out / 'turn.json').write_text(json.dumps(out, indent=1) + '\n'); print(json.dumps(out))


if __name__ == '__main__':
    main()
