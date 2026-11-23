#!/usr/bin/env python3
"""level_splat.py BAKE_DIR ROUND: put a round's splat upright in the review's frame (Y up, front toward +Z, height 1.0, centred), so a tilted
fit frame cannot reach a review sheet (lead, 2026-11-14). Up = the long principal axis of the opaque splats (a standing figure), signed to
agree with the median camera up; front = toward the first training camera (frame 0 = the front painting's view), projected level.
Why not the cameras' up: fuse.py sets the fit frame's up from the MEDIAN camera up, which leans on paths that rise on one side and dip on
the other (about 30 deg on a +70/-60 cycle), and aligning to the designed cameras leaves 15-17 deg on the sweeps, where H3's real camera
differs from the design. Known case: on the flat orbit (j-flat) the figure axis and the design-aligned up agree within 0.8 deg.
Writes BAKE_DIR/ROUND-level/splat.npz and prints the old frame's lean (figure axis against the frame's Y)."""
import json, os, sys, numpy as np
bake, rnd = sys.argv[1:3]
sp = dict(np.load(f'{bake}/{rnd}/splat.npz')); c2w = np.linalg.inv(np.load(f'{bake}/data/cameras.npz')['viewmats'])
op = 1 / (1 + np.exp(-sp['opacities'].reshape(-1))); sel = op > 0.5; m0 = sp['means'][sel]; ctr = np.median(m0, 0)
w, v = np.linalg.eigh(np.cov((m0 - ctr).T)); up = v[:, -1]
cam_up = -np.median(c2w[:, :3, 1], 0); up *= np.sign(up @ cam_up) or 1
lean = np.degrees(np.arccos(np.clip(abs(up[1]), -1, 1)))
f = c2w[0, :3, 3] - ctr; f -= (f @ up) * up; f /= np.linalg.norm(f); r = np.cross(up, f)
Q = np.stack([r, up, f])          # rows: new X, Y, Z in old coordinates -> new = Q @ old
if np.linalg.det(Q) < 0: Q[0] *= -1
m = (sp['means'] - ctr) @ Q.T
def mat2quat(R):
    t = np.trace(R)
    if t > 0:
        S = np.sqrt(t + 1) * 2; return np.array([S / 4, (R[2, 1] - R[1, 2]) / S, (R[0, 2] - R[2, 0]) / S, (R[1, 0] - R[0, 1]) / S])
    i = np.argmax(np.diag(R))
    if i == 0: S = np.sqrt(1 + R[0, 0] - R[1, 1] - R[2, 2]) * 2; return np.array([(R[2, 1] - R[1, 2]) / S, S / 4, (R[0, 1] + R[1, 0]) / S, (R[0, 2] + R[2, 0]) / S])
    if i == 1: S = np.sqrt(1 + R[1, 1] - R[0, 0] - R[2, 2]) * 2; return np.array([(R[0, 2] - R[2, 0]) / S, (R[0, 1] + R[1, 0]) / S, S / 4, (R[1, 2] + R[2, 1]) / S])
    S = np.sqrt(1 + R[2, 2] - R[0, 0] - R[1, 1]) * 2; return np.array([(R[1, 0] - R[0, 1]) / S, (R[0, 2] + R[2, 0]) / S, (R[1, 2] + R[2, 1]) / S, S / 4])
w1, x1, y1, z1 = mat2quat(Q); q = sp['quats'] / np.linalg.norm(sp['quats'], axis=1)[:, None]; w2, x2, y2, z2 = q.T
sp['quats'] = np.stack([w1*w2 - x1*x2 - y1*y2 - z1*z2, w1*x2 + x1*w2 + y1*z2 - z1*y2, w1*y2 - x1*z2 + y1*w2 + z1*x2, w1*z2 + x1*y2 - y1*x2 + z1*w2], 1).astype(np.float32)
lo, hi = np.percentile(m[sel, 1], [0.5, 99.5]); sc = 1.0 / (hi - lo)
m = (m - np.array([0, (lo + hi) / 2, 0])) * sc; sp['means'] = m.astype(np.float32); sp['scales'] = (sp['scales'] + np.log(sc)).astype(np.float32)
os.makedirs(f'{bake}/{rnd}-level', exist_ok=True); np.savez(f'{bake}/{rnd}-level/splat.npz', **sp)
# the transform, so the round's cameras (data/cameras.npz, world -> camera) can be moved into the levelled frame: x_new = sc * (Q (x - ctr) - [0, mid, 0])
json.dump({'Q': Q.tolist(), 'ctr': ctr.tolist(), 'mid': float((lo + hi) / 2), 'sc': float(sc)}, open(f'{bake}/{rnd}-level/transform.json', 'w'))
print(json.dumps({'bake': bake.rstrip('/').split('/')[-1], 'round': rnd, 'old_frame_lean_deg': round(float(lean), 1), 'scale': round(float(sc), 3)}))
