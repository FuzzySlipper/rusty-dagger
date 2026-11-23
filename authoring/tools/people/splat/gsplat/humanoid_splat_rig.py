#!/usr/bin/env python3
"""Rig a static humanoid splat on Viggle's canonical 441-joint skeleton and write a VIGGCHAR v1 character.

  python3 humanoid_splat_rig.py SPLAT.npz LANDMARKS.json OUT.vsplat [--pelvis-height 0.964 | --height M]
      [--skeleton extracted/sdk-source/src/loader/skeleton/skeleton-data-441.ts] [--no-repose] [--repose-legs [--level-hips]] [--legs-by-bone]
      [--skirt-floor 0.12] [--skirt-calf 0.3] [--skirt-split 0.3] [--skirt-ramp 0.2] [--shoe-damp 0.8] [--smooth 6]
      [--tunic [--tunic-max 0.8 --tunic-split 0.6 --tunic-ramp 0.05 --tunic-from 0.25 --tunic-sides 0.5]] [--hang X0,X1,Y0,Y1,Z0,Z1 ...]

The result loads with Character.load like a PINOC character, so the free
PINOC library clips (splat-skeleton GLB) play on it, and the crowd page can
instance it. No Viggle character is needed as a donor: the skeleton comes
from the SDK's canonical tables (run `npm run inspect:sdk` once to extract
them) and the weights are computed on the splat itself.

LANDMARKS.json gives joint positions in the splat's own frame (Y up, front
+Z, the subject's left +X): pelvis, neck_01, head, head_top, and per side
upperarm, lowerarm, hand, hand_tip, thigh, calf, foot, ball, toe_tip, plus
`sole` (floor height) and `skirt: {waist, hem}` for a skirt or tunic.

What it does:
- Moves the splat into the SDK's frame (faces +X, left -Z, metres, soles on
  y = 0), scaled so the pelvis sits at --pelvis-height, the pelvis height of
  the PINOC clips; their pelvis translation is absolute, so this keeps the
  feet on the ground.
- Builds a per-character rest: the landmarks for trunk, head and legs, the
  canonical skeleton stretched between them for the spine and neck, and
  canonical offsets for fingers, toes and face. Rotations stay canonical,
  because a VIGGCHAR file can only carry translations.
- Re-poses the arms into the canonical A-pose. Clips rotate bones relative to
  that A-pose, so arms that rest nearer the body would be driven into it.
  Each arm bone keeps its own length and turns onto the canonical direction;
  the Gaussians follow by linear blend skinning with the weights below.
  --no-repose skips this (for checking how much it matters).
  --arm-radius widens the arm capsules for wide sleeves, whose outer
  Gaussians would otherwise count as body and stay behind when the arm moves.
  --arm-no-cloth keeps cloth-coloured Gaussians (blue over red) off the arms below the waist, for a long
  tunic hanging beside the hands. Below the waist only the arm's core (--arm-core of its capsule) counts as
  arm, so a long skirt behind a hand stays put; 1 lets a sleeve that hangs
  beside a short tunic follow the arm whole.
- --repose-legs does the same for the legs, for a figure that stands in a
  stride or with bent knees: thigh and calf keep their measured lengths and
  turn onto the canonical directions, and the foot onto the canonical foot
  direction, so the rest is the canonical straight-legged A-pose and clips
  bend the legs from there. The rest is then lowered or raised so its
  lowest Gaussians stand on y = 0. --level-hips also raises the lower hip
  (with its whole leg) to the higher one's height in the rest, for a figure
  standing with a dropped hip over a relaxed, bent leg: her landmarks then
  give both legs the same bone lengths, and the rest has level hips.
- --legs-by-bone splits what is under the hem between the legs by the
  nearer leg bones instead of by the side of x = 0, for a figure whose
  shoe crosses the middle line.
- --height M scales the figure to M metres tall (sole to crown) instead of
  putting the pelvis at --pelvis-height. The clips' pelvis height is then
  not his, so the page scales the clip's pelvis translation to his legs
  (see clip_fit.py).
- Weights each Gaussian, in the original pose: capsule distances decide arm,
  trunk/head, shoe and skirt regions; trunk and arm weights fall off
  softly with distance to each bone segment; a long skirt is weighted to the
  pelvis near the waist, to the thighs down to the knee and to thighs and
  calves below it, each side leaning to its own leg over a smooth ramp
  across the middle, so it swings with the legs it hides and shears rather
  than tears between them. --skirt-floor keeps that much pelvis weight down
  to the knee, --skirt-calf sets the calves' share at the hem (so a trailing
  shoe stays near the hem), --skirt-split how far each side leans to its
  own leg (lower keeps the skirt closed but lets the shoes stride out), and
  --skirt-ramp the half-width of the left/right blend across the centre.
  --shoe-damp gives each shoe (the Gaussians below the hem) one blend: its
  foot bone, plus that share following both legs as the hem above it does, so under a stiff skirt the shoes travel part of the stride and
  stay near the hem. The default 0.8 keeps townswoman 453's shoes at her hem
  edge; the standing shoe then drifts from its planted spot (about half the
  walk's speed, unseen in place, foot skating with root motion on) and sits
  1-1.5 cm above the floor at its lowest. 0 restores feet that follow the
  clip exactly (thigh, calf, foot and ball weights per Gaussian), which is
  also what bare legs below a short tunic want.
  The defaults (0.3, 0.3, 0.2) keep the skirt intact and stiff: stronger leg
  influence (townswoman 453 round 2 used calf 0.85, split 0.65, ramp 0.07)
  follows the legs closely but pulls neighbouring Gaussians apart where the
  skirt shears between them, which reads as torn, multicoloured cloth. Weights are then averaged over each Gaussian's
  neighbours within its region (--smooth passes), and the four strongest
  are kept.
- --tunic replaces that skirt blend, from the waist to the hem, with
  layer_rig.py's thighs weighting (for a short tunic over visible legs, as
  townsman 385's): pelvis at the waist, the leg share growing to
  --tunic-max at the hem, each side leaning --tunic-split to its own leg
  across a --tunic-ramp ramp about the knee line, the sides at
  --tunic-sides of the front and back's share.
- --hang X0,X1,Y0,Y1,Z0,Z1 puts the trunk Gaussians in that box (splat
  frame) on the pelvis alone, in a region of their own: a pouch or a case
  at the hip hangs from the belt instead of swinging with a thigh or an
  arm (arm Gaussians in the box outside the hand and forearm capsules
  count too).
  --hang-warm keeps only the warm trunk Gaussians in the boxes (red >=
  blue), so leather hangs and the teal cloth behind it keeps the tunic's
  weights.

Writes OUT.vsplat, OUT.json (summary, bone names used) and OUT-rig.npz
(rest means and attributes, global bone indices and weights, rest joint
translations, her-pose means, the frame transform) for pose_splat_preview.py.
"""
import argparse
import json
import os
import re
import sys

import numpy as np
from scipy.spatial import cKDTree
from scipy.spatial.transform import Rotation as Rot

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_skinned_vsplat import write_vsplat  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_SKELETON = os.path.join(HERE, '..', 'extracted', 'sdk-source', 'src', 'loader', 'skeleton', 'skeleton-data-441.ts')
# The splat's frame (front +Z, left +X) to the SDK's (front +X, left -Z): a quarter turn about Y.
TO_SDK = np.array([[0.0, 0.0, 1.0], [0.0, 1.0, 0.0], [-1.0, 0.0, 0.0]])
TRUNK = ['spine_01', 'spine_02', 'spine_03', 'spine_04', 'spine_05']


def load_skeleton(path):
    text = open(path).read()

    def array(name):
        return json.loads(re.search(r'export const ' + name + r'[^=]*=\s*(\[.*?\]);', text, re.S).group(1))
    names = array('BONE_NAMES_441')
    parents = array('PARENTS_441')
    trans = np.array(array('REST_TRANSLATIONS_441'), np.float64).reshape(-1, 3)
    rots = np.array(array('REST_ROTATIONS_441'), np.float64).reshape(-1, 4)  # x, y, z, w
    return names, parents, trans, rots


def world_rest(parents, trans, rots):
    n = len(parents)
    R = np.zeros((n, 3, 3)); P = np.zeros((n, 3))
    for i in range(n):
        r = Rot.from_quat(rots[i]).as_matrix()
        if parents[i] < 0:
            R[i], P[i] = r, trans[i]
        else:
            R[i] = R[parents[i]] @ r; P[i] = P[parents[i]] + R[parents[i]] @ trans[i]
    return R, P


def min_rotation(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    v = np.cross(a, b); c = float(a @ b)
    if np.linalg.norm(v) < 1e-9:
        return np.eye(3)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + k + k @ k * (1 / (1 + c))


def descendants(parents, root):
    out, frontier = [], [root]
    while frontier:
        nxt = [i for i, p in enumerate(parents) if p in frontier]
        out += nxt; frontier = nxt
    return out


def segment_distance(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / max(ab @ ab, 1e-12), 0, 1)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('splat'); ap.add_argument('landmarks'); ap.add_argument('out')
    ap.add_argument('--pelvis-height', type=float, default=0.964)
    ap.add_argument('--skeleton', default=DEFAULT_SKELETON)
    ap.add_argument('--no-repose', action='store_true')
    ap.add_argument('--repose-legs', action='store_true', help='straighten the legs onto the canonical rest as the arms are')
    ap.add_argument('--legs-by-bone', action='store_true', help='split the Gaussians under the hem between the legs by the nearer leg bones, not by the side of x = 0')
    ap.add_argument('--level-hips', action='store_true', help='with --repose-legs: raise the lower hip to the higher one in the rest')
    ap.add_argument('--height', type=float, help='scale to this many metres tall instead of --pelvis-height')
    ap.add_argument('--skirt-floor', type=float, default=0.12, help='pelvis weight kept down to the knee')
    ap.add_argument('--skirt-calf', type=float, default=0.3, help='calf share of the leg weight at the hem')
    ap.add_argument('--skirt-split', type=float, default=0.3, help='how far each side leans to its own leg (1 = fully)')
    ap.add_argument('--skirt-ramp', type=float, default=0.2, help='half-width of the left/right ramp across the centre, splat units')
    ap.add_argument('--shoe-damp', type=float, default=0.8, help="share of each shoe's weight that follows the hem above it")
    ap.add_argument('--smooth', type=int, default=6)
    ap.add_argument('--arm-radius', type=float, default=1.0, help='multiply the arm capsule radii (wide sleeves)')
    ap.add_argument('--arm-reach', type=float, default=1.0, help='a Gaussian nearer an arm than the trunk is arm out to this many capsule radii')
    ap.add_argument('--arm-core', type=float, default=0.7, help='below the waist, only this much of the arm capsule counts as arm (1 = all of it)')
    ap.add_argument('--tunic', action='store_true', help="weight what hangs from the waist to the hem as layer_rig.py's thighs mode (a short tunic)")
    ap.add_argument('--tunic-max', type=float, default=0.8, help='--tunic: leg share at the hem')
    ap.add_argument('--tunic-split', type=float, default=0.6, help='--tunic: how far each side leans to its own leg (1 = fully)')
    ap.add_argument('--tunic-ramp', type=float, default=0.05, help='--tunic: half-width of the left/right ramp about the knee line, splat units')
    ap.add_argument('--tunic-from', type=float, default=0.25, help='--tunic: no leg share above this fraction of waist to hem')
    ap.add_argument('--tunic-sides', type=float, default=0.5, help="--tunic: leg share at the tunic's sides against front and back")
    ap.add_argument('--hang', action='append', default=[], metavar='X0,X1,Y0,Y1,Z0,Z1',
                    help='trunk Gaussians inside this box (splat frame) go on the pelvis alone: a pouch or case hanging from the belt (repeatable)')
    ap.add_argument('--arm-no-cloth', action='store_true', help='below the waist, cloth-coloured (blue over red) or near-black Gaussians outside --arm-no-cloth-core never go with an arm')
    ap.add_argument('--under-hem-clean', action='store_true', help="inside the tunic above the hem: interior Gaussians take the hose's colour, or are dropped if far from it. Tried once on 386-C (2026-11-15): no visible change in the walk (the bright junk there sits below the hem, in the boot cuff); off by default")
    ap.add_argument('--under-hem-height', type=float, default=0.15, help='--under-hem-clean: how far above the hem (splat units)')
    ap.add_argument('--under-hem-far', type=float, default=0.25, help='--under-hem-clean: max channel difference from the hose colour above which a Gaussian is dropped')
    ap.add_argument('--drop-stranded', action='store_true', help='below the waist, drop (opacity ~0) Gaussians whose static neighbours mostly belong to another body part')
    ap.add_argument('--arm-to-tip', action='store_true', help='nothing lower than the hand tip landmark (less 1 cm) goes with the arm')
    ap.add_argument('--arm-no-cloth-yellow', action='store_true', help='--arm-no-cloth: saturated yellow (a yellow trim) is cloth too, and not skin')
    ap.add_argument('--hand-clusters', type=float, default=0.0, help="metres: hand-weighted Gaussians outside the hand's largest single-link cluster at this gap take their nearest body Gaussian's weights (0 = off)")
    ap.add_argument('--arm-no-cloth-core', type=float, default=0.6, help='--arm-no-cloth: within this many capsule radii a blue-tinted or dark Gaussian is still the hand')
    ap.add_argument('--hang-warm', action='store_true', help='--hang: only warm Gaussians (red >= blue: leather, not teal or blue cloth)')
    a = ap.parse_args()

    names, parents, can_t, can_r = load_skeleton(a.skeleton)
    idx = {n: i for i, n in enumerate(names)}
    Rw, Pc = world_rest(parents, can_t, can_r)
    lm = json.load(open(a.landmarks)); J = lm['joints']
    sole = lm['sole']
    s_in = np.load(a.splat)
    scale = a.pelvis_height / (J['pelvis'][1] - sole)
    if a.height:
        scale = a.height / (float(s_in['means'][:, 1].max()) - sole)
    offset = -scale * (TO_SDK @ np.array([0.0, sole, 0.0]))

    def to_sdk(p):
        return scale * (np.asarray(p, np.float64) @ TO_SDK.T) + offset
    L = {k: to_sdk(v) for k, v in J.items()}

    # Her joint positions (original pose), canonical skeleton stretched between landmarks.
    n = len(names)
    her = np.zeros((n, 3))
    k_body = (L['head'][1] - L['pelvis'][1]) / (Pc[idx['head']][1] - Pc[idx['pelvis']][1])
    pel_c, neck_c, head_c = Pc[idx['pelvis']], Pc[idx['neck_01']], Pc[idx['head']]
    for i in range(n):
        her[i] = L['pelvis'] + k_body * (Pc[i] - pel_c)          # fallback: uniform about the pelvis
    for b in TRUNK + ['neck_01', 'neck_02', 'head']:
        i = idx[b]
        lo, hi, lo_c, hi_c = (L['pelvis'], L['neck_01'], pel_c, neck_c) if b in TRUNK or b == 'neck_01' else (L['neck_01'], L['head'], neck_c, head_c)
        f = (Pc[i][1] - lo_c[1]) / (hi_c[1] - lo_c[1])
        her[i] = lo + f * (hi - lo) + k_body * ((Pc[i] - (lo_c + f * (hi_c - lo_c))) * [1, 0, 1])
    for b in ('pelvis', 'neck_01', 'head'):
        her[idx[b]] = L[b]
    for b in [x for x in names if x.startswith('FACIAL')]:
        her[idx[b]] = her[idx['head']] + k_body * (Pc[idx[b]] - head_c)
    for s in ('l', 'r'):
        cl = idx['clavicle_' + s]
        her[cl] = her[idx['spine_05']] + k_body * (Pc[cl] - Pc[idx['spine_05']])
        for b in ('upperarm', 'lowerarm', 'hand', 'thigh', 'calf', 'foot', 'ball'):
            her[idx[f'{b}_{s}']] = L[f'{b}_{s}']
        hand = idx['hand_' + s]
        k_hand = np.linalg.norm(L['hand_tip_' + s] - L['hand_' + s]) / np.linalg.norm(Pc[idx['middle_03_' + s]] - Pc[hand])
        for d in descendants(parents, hand):
            her[d] = her[hand] + k_hand * (Pc[d] - Pc[hand])      # rest-oriented here; re-aimed below
        ball = idx['ball_' + s]
        for d in descendants(parents, ball):
            her[d] = her[ball] + k_body * (Pc[d] - Pc[ball])
        tw = idx['calf_twist_01_' + s]
        her[tw] = her[idx['calf_' + s]] + 0.5 * (her[idx['foot_' + s]] - her[idx['calf_' + s]])

    # Rest positions: the same, except the arm chain turned onto the canonical directions.
    rest = her.copy(); A = {}
    for s in ('l', 'r'):
        ua, la, hd = idx['upperarm_' + s], idx['lowerarm_' + s], idx['hand_' + s]
        tip_c = Pc[idx['middle_03_' + s]]
        if not a.no_repose:
            rest[la] = rest[ua] + np.linalg.norm(her[la] - her[ua]) * (Pc[la] - Pc[ua]) / np.linalg.norm(Pc[la] - Pc[ua])
            rest[hd] = rest[la] + np.linalg.norm(her[hd] - her[la]) * (Pc[hd] - Pc[la]) / np.linalg.norm(Pc[hd] - Pc[la])
        k_hand = np.linalg.norm(L['hand_tip_' + s] - L['hand_' + s]) / np.linalg.norm(tip_c - Pc[hd])
        for d in descendants(parents, hd):
            rest[d] = rest[hd] + k_hand * (Pc[d] - Pc[hd])
        rest_tip = rest[hd] + k_hand * (tip_c - Pc[hd])
        # A rotates a bone's rest direction onto her direction (identity with --no-repose).
        A[ua] = min_rotation(rest[la] - rest[ua], her[la] - her[ua])
        A[la] = min_rotation(rest[hd] - rest[la], her[hd] - her[la])
        A[hd] = min_rotation(rest_tip - rest[hd], L['hand_tip_' + s] - her[hd])
        if a.no_repose:
            A[ua] = A[la] = A[hd] = np.eye(3)
        for d in descendants(parents, hd):
            A[d] = A[hd]; her[d] = her[hd] + A[hd] @ (rest[d] - rest[hd])
    hips_raised = {}
    if a.repose_legs:
        if a.level_hips:
            top = max(rest[idx['thigh_l']][1], rest[idx['thigh_r']][1])
            for s in ('l', 'r'):
                hips_raised['thigh_' + s] = float(top - rest[idx['thigh_' + s]][1])
                rest[idx['thigh_' + s]][1] = top
        for s in ('l', 'r'):
            th, cf, ft, bl = (idx[f'{b}_{s}'] for b in ('thigh', 'calf', 'foot', 'ball'))
            rest[cf] = rest[th] + np.linalg.norm(her[cf] - her[th]) * (Pc[cf] - Pc[th]) / np.linalg.norm(Pc[cf] - Pc[th])
            rest[ft] = rest[cf] + np.linalg.norm(her[ft] - her[cf]) * (Pc[ft] - Pc[cf]) / np.linalg.norm(Pc[ft] - Pc[cf])
            rest[bl] = rest[ft] + np.linalg.norm(her[bl] - her[ft]) * (Pc[bl] - Pc[ft]) / np.linalg.norm(Pc[bl] - Pc[ft])
            for d in descendants(parents, bl):
                rest[d] = rest[bl] + k_body * (Pc[d] - Pc[bl])
            tw = idx['calf_twist_01_' + s]
            rest[tw] = rest[cf] + 0.5 * (rest[ft] - rest[cf])
            A[th] = min_rotation(rest[cf] - rest[th], her[cf] - her[th])
            A[cf] = min_rotation(rest[ft] - rest[cf], her[ft] - her[cf])
            A[tw] = A[cf]
            A[ft] = min_rotation(rest[bl] - rest[ft], her[bl] - her[ft])
            for d in [bl] + descendants(parents, bl):
                A[d] = A[ft]; her[d] = her[bl] + A[ft] @ (rest[d] - rest[bl]) if d != bl else her[bl]
    rest[idx['root']] = 0.0

    # Per-bone deformation from her pose to the rest: G = B H^-1 with B = [Rw | rest], H = [A Rw | her].
    G = np.tile(np.eye(4), (n, 1, 1))
    for i in range(n):
        Rh = A.get(i, np.eye(3)) @ Rw[i]
        B = np.eye(4); B[:3, :3] = Rw[i]; B[:3, 3] = rest[i]
        H = np.eye(4); H[:3, :3] = Rh; H[:3, 3] = her[i]
        G[i] = B @ np.linalg.inv(H)

    # Splat in the SDK frame, her pose.
    means = to_sdk(s_in['means'])
    count = len(means)
    y_her = s_in['means'][:, 1]; x_her = s_in['means'][:, 0]

    def seg(b, end):
        return segment_distance(means, her[idx[b]], end)
    tip = {s: L['hand_tip_' + s] for s in 'lr'}
    toe = {s: L['toe_tip_' + s] for s in 'lr'}
    segs = {  # bone: (distance, capsule radius in her units)
        'pelvis': (seg('pelvis', her[idx['spine_02']]), 0.11),
        'spine_01': (seg('spine_01', her[idx['spine_02']]), 0.10), 'spine_02': (seg('spine_02', her[idx['spine_03']]), 0.10),
        'spine_03': (seg('spine_03', her[idx['spine_04']]), 0.10), 'spine_04': (seg('spine_04', her[idx['spine_05']]), 0.10),
        'spine_05': (seg('spine_05', her[idx['neck_01']]), 0.10),
        'neck_01': (seg('neck_01', her[idx['neck_02']]), 0.05), 'neck_02': (seg('neck_02', her[idx['head']]), 0.05),
        'head': (seg('head', L['head_top']), 0.09),
    }
    for s in 'lr':
        segs['clavicle_' + s] = (seg('clavicle_' + s, her[idx['upperarm_' + s]]), 0.06)
        segs['upperarm_' + s] = (seg('upperarm_' + s, her[idx['lowerarm_' + s]]), 0.045 * a.arm_radius)
        segs['lowerarm_' + s] = (seg('lowerarm_' + s, her[idx['hand_' + s]]), 0.035 * a.arm_radius)
        segs['hand_' + s] = (seg('hand_' + s, tip[s]), 0.04 * a.arm_radius)
        segs['thigh_' + s] = (seg('thigh_' + s, her[idx['calf_' + s]]), 0.07)
        segs['calf_' + s] = (seg('calf_' + s, her[idx['foot_' + s]]), 0.05)
        segs['foot_' + s] = (seg('foot_' + s, her[idx['ball_' + s]]), 0.04)
        segs['ball_' + s] = (seg('ball_' + s, toe[s]), 0.04)
    bones = list(segs)
    D = np.stack([segs[b][0] for b in bones], 1)
    Rad = np.array([segs[b][1] for b in bones]) * scale
    norm = D / Rad
    arm_bones = {s: [bones.index(f'{b}_{s}') for b in ('upperarm', 'lowerarm', 'hand')] for s in 'lr'}
    body_bones = [bones.index(b) for b in ['pelvis'] + TRUNK + ['neck_01', 'neck_02', 'head', 'clavicle_l', 'clavicle_r']]
    waist, hem = lm['skirt']['waist'], lm['skirt']['hem']

    region = np.zeros(count, np.int64)     # 0 body, 1 left arm, 2 right arm, 3 left shoe, 4 right shoe
    body_n = norm[:, body_bones].min(1)
    rgb = np.clip(s_in['rgb'], 0, 1)
    pale = (rgb.mean(1) > 0.55) & (rgb[:, 0] >= rgb[:, 1]) & (rgb[:, 1] >= rgb[:, 2])    # skin, not the red/teal skirt
    # --arm-no-cloth-yellow: a saturated yellow trim (green near red, little blue) is cloth too, and never 'pale' skin (villager 383-A's
    # yellow zigzag hem beside dark hands went with them, 2026-11-15). Only for a character whose skin is not yellow-lit.
    yellow = (rgb[:, 1] > 0.7 * rgb[:, 0]) & (rgb[:, 0] > 0.35) & (rgb[:, 2] < 0.6 * rgb[:, 1]) if a.arm_no_cloth_yellow else np.zeros(count, bool)
    pale &= ~yellow
    for r, s in ((1, 'l'), (2, 'r')):
        arm_n = norm[:, arm_bones[s]].min(1)
        is_arm = (arm_n < body_n) & (arm_n < a.arm_reach) & (y_her < J['upperarm_' + s][1] - 0.02)
        # Beside the skirt, only the arm's core or skin-coloured Gaussians: the skirt behind a hand stays put.
        is_arm &= (y_her >= waist) | (arm_n < a.arm_core) | pale
        # --arm-no-cloth: below the waist, cloth-coloured Gaussians (blue over red: a teal or blue tunic beside the hand; or near-black, the
        # cloth's shadow) never go with
        # the arm, however near the capsule (villager 386-C's knee-length tunic: shards flew with the hands, 2026-11-15).
        # Inside the capsule's own radius (--arm-no-cloth-core) a blue-tinted Gaussian is the hand's (rim light on the fingers) and stays.
        if a.arm_no_cloth:
            is_arm &= (y_her >= waist) | ~((rgb[:, 2] > rgb[:, 0] + 0.03) | (rgb.max(1) < 0.08) | yellow) | (arm_n < a.arm_no_cloth_core)
        # --arm-to-tip: nothing lower than the hand tip landmark (less 1 cm) goes with the arm: below a hand that hangs beside a long
        # tunic the widened capsule otherwise takes the tunic's side edge, which then swings out past the fingertips (386-C, 2026-11-15).
        # Skin a little outside the hand capsule (the palm's front) still belongs to the hand.
        is_arm |= pale & (norm[:, bones.index('hand_' + s)] < 1.6) & (y_her < waist + 0.1)
        if a.arm_to_tip:   # after the palm's skin above: nothing below the fingertips, pale or not
            is_arm &= y_her >= J['hand_tip_' + s][1] - 0.01
        region[is_arm] = r
    if a.legs_by_bone:
        # Each Gaussian under the hem goes to the leg whose bones are nearer, not to its side of x = 0:
        # a figure standing off-centre or with crossing feet has one shoe over the middle line.
        leg_n = {s: norm[:, [bones.index(f'{b}_{s}') for b in ('thigh', 'calf', 'foot', 'ball')]].min(1) for s in 'lr'}
        left_side = leg_n['l'] < leg_n['r']
    else:
        left_side = x_her > 0
    for r, s in ((3, 'l'), (4, 'r')):
        side = left_side if s == 'l' else ~left_side
        region[(y_her < hem - 0.01) & side & (region == 0)] = r

    W = np.zeros((count, n))
    tau = 0.02 * scale

    def soft(rows, cols):
        d = D[np.ix_(rows, cols)]
        w = np.exp(-(d - d.min(1, keepdims=True)) / tau)
        w[d - d.min(1, keepdims=True) > 4 * tau] = 0
        return w / w.sum(1, keepdims=True)

    # Trunk, head, shoulders: soft over trunk bones, plus the upper arms above the armpits only.
    rows = np.where(region == 0)[0]
    cols = body_bones + [bones.index('upperarm_l'), bones.index('upperarm_r')]
    w_up = soft(rows, cols)
    for j, s in ((-2, 'l'), (-1, 'r')):
        w_up[:, j] *= smoothstep(J['upperarm_' + s][1] - 0.07, J['upperarm_' + s][1] - 0.03, y_her[rows])
    w_up /= np.maximum(w_up.sum(1, keepdims=True), 1e-9)
    # Skirt: pelvis near the waist, thighs to the knee, thighs and calves below it, each side
    # leaning to its own leg (--skirt-split) across a smooth ramp over the middle.
    u = np.clip((waist - y_her[rows]) / (waist - hem), 0, 1)
    knee_u = (waist - (J['calf_l'][1] + J['calf_r'][1]) / 2) / (waist - hem)
    wp = (1 - u) ** 2 * (1 - smoothstep(knee_u - 0.15, knee_u, u)) + a.skirt_floor * (1 - smoothstep(knee_u - 0.15, knee_u, u))
    wp = np.clip(wp, 0, 1)
    calf = a.skirt_calf * smoothstep(knee_u - 0.1, 1.0, u) if knee_u < 1 else np.zeros_like(u)   # a tunic ends above the knee
    left = 0.5 + a.skirt_split * (smoothstep(-a.skirt_ramp, a.skirt_ramp, x_her[rows]) - 0.5)
    blend = smoothstep(waist - 0.04, waist + 0.04, y_her[rows])[:, None]   # 1 above the waist band
    for j, b in enumerate(cols):
        W[rows, idx[bones[b]]] += blend[:, 0] * w_up[:, j]
    below = 1 - blend[:, 0]
    W[rows, idx['pelvis']] += below * wp
    for s, share in (('l', left), ('r', 1 - left)):
        W[rows, idx['thigh_' + s]] += below * (1 - wp) * share * (1 - calf)
        W[rows, idx['calf_' + s]] += below * (1 - wp) * share * calf
    if a.tunic:
        # A short tunic: pelvis at the waist, blending toward the thighs down to the hem (none above --tunic-from of the
        # way), each side leaning to its own leg across the line between the knees, the sides (90 deg from the front)
        # taking --tunic-sides of the front and back's leg share: the knee in front pushes the cloth forward and the hem
        # swings with the stride, as layer_rig.py's thighs mode does for a separate skirt layer.
        rows = np.where(region == 0)[0]
        yr, xr, zr = y_her[rows], x_her[rows], s_in['means'][rows, 2]
        cx = (J['calf_l'][0] + J['calf_r'][0]) / 2; cz = (J['calf_l'][2] + J['calf_r'][2]) / 2
        u = np.clip((waist - yr) / (waist - hem), 0, 1)
        theta = np.arctan2(xr - cx, zr - cz)
        sector = a.tunic_sides + (1 - a.tunic_sides) * np.abs(np.cos(theta))
        leg = a.tunic_max * smoothstep(a.tunic_from, 1.0, u) * sector
        left = 0.5 + a.tunic_split * (smoothstep(-a.tunic_ramp, a.tunic_ramp, xr - cx) - 0.5)
        below = 1 - smoothstep(waist - 0.04, waist + 0.04, yr)          # 1 under the waist band
        for b in ('pelvis', 'thigh_l', 'thigh_r', 'calf_l', 'calf_r'):
            W[rows, idx[b]] *= 1 - below
        W[rows, idx['pelvis']] += below * (1 - leg)
        W[rows, idx['thigh_l']] += below * leg * left
        W[rows, idx['thigh_r']] += below * leg * (1 - left)
    hung = np.zeros(count, bool)
    for box in a.hang:
        x0, x1, y0, y1, z0, z1 = (float(v) for v in box.split(','))
        p = s_in['means']
        inside = (p[:, 0] >= x0) & (p[:, 0] <= x1) & (p[:, 1] >= y0) & (p[:, 1] <= y1) & (p[:, 2] >= z0) & (p[:, 2] <= z1)
        # Trunk Gaussians, and arm ones outside the hand and forearm capsules' cores (0.7): wide arm capsules reach a case
        # hanging beside the hand (its brass is pale, so colour cannot tell it from skin).
        limb = np.min(norm[:, [bones.index(f'{b}_{s}') for b in ('hand', 'lowerarm') for s in 'lr']], 1) < 0.7
        # --hang-warm: trunk Gaussians only if warm (leather, not the teal cloth behind it).
        trunk = (region == 0) & ((rgb[:, 0] >= rgb[:, 2]) if a.hang_warm else True)
        hung |= inside & (trunk | (np.isin(region, [1, 2]) & ~limb))
    if hung.any():
        W[hung] = 0; W[hung, idx['pelvis']] = 1
        region[hung] = 5                     # its own region: neighbour averaging does not mix it with the tunic
    for r, s in ((1, 'l'), (2, 'r')):
        rows = np.where(region == r)[0]
        cols = arm_bones[s]
        if len(rows):
            w = soft(rows, cols)
            for j, b in enumerate(cols):
                W[rows, idx[bones[b]]] += w[:, j]
    for r, s in ((3, 'l'), (4, 'r')):
        rows = np.where(region == r)[0]
        cols = [bones.index(f'{b}_{s}') for b in ('thigh', 'calf', 'foot', 'ball')]
        if len(rows) and a.shoe_damp <= 0:
            w = soft(rows, cols)
            for j, b in enumerate(cols):
                W[rows, idx[bones[b]]] += w[:, j]
        elif len(rows):
            # --shoe-damp: one blend for the whole shoe, so it moves as a piece: its foot bone, and
            # the rest following what the hem above it follows (both legs' thighs and calves, in the
            # skirt's left/right and calf shares at the shoe's mean position), so the shoe travels
            # part of the stride and stays near a stiff skirt's hem. Per-Gaussian blends split the
            # shoe apart at toe-off, and following its own leg alone does not shorten its travel.
            other = 'r' if s == 'l' else 'l'
            own = 0.5 + a.skirt_split * (smoothstep(-a.skirt_ramp, a.skirt_ramp, np.array([x_her[rows].mean()]))[0] - 0.5)
            own = own if s == 'l' else 1 - own
            hem_calf = a.skirt_calf
            W[rows, idx['foot_' + s]] += 1 - a.shoe_damp
            for side, share in ((s, own), (other, 1 - own)):
                W[rows, idx['thigh_' + side]] += a.shoe_damp * share * (1 - hem_calf)
                W[rows, idx['calf_' + side]] += a.shoe_damp * share * hem_calf

    # --drop-stranded: below the waist, a Gaussian whose static neighbours mostly belong to another body part (an arm against the
    # tunic, or the tunic against an arm) ends up alone in the air once the arm moves: a bake floater that only looked attached
    # because the hand hung over it (386-C's specks beside the hips, 2026-11-15). Their opacity goes to ~0 (the row stays).
    dropped = np.zeros(count, bool)
    if a.drop_stranded:
        _, nb8 = cKDTree(s_in['means']).query(s_in['means'], k=9); nb8 = nb8[:, 1:]
        armish = np.isin(region, [1, 2]); arm_share = armish[nb8].mean(1)
        low = y_her < waist
        dropped = low & ((armish & (arm_share < 0.25)) | ((region == 0) & (arm_share > 0.5)))
        s_in = dict(s_in); op = s_in['opacities'].copy().reshape(-1); op[dropped] = -12.0
        s_in['opacities'] = op.reshape(np.asarray(s_in['opacities']).shape)
        print(f'drop-stranded: {int(dropped.sum())} Gaussians', file=sys.stderr)
    # --under-hem-clean: inside the tunic above the hem, Gaussians well inside the cloth's outer surface (never seen from outside:
    # bake noise) are carried into view under the hem when a thigh swings (386-C's pink-yellow blob, 2026-11-15). Those near the
    # visible hose's colour take its median colour; those far from it (max channel difference > --under-hem-far) are dropped.
    under_hem = {}
    if a.under_hem_clean:
        rgb_in = np.clip(np.asarray(s_in['rgb'], float), 0, 1); p_in = np.asarray(s_in['means'], float)
        band = (region == 0) & (y_her > hem) & (y_her < hem + a.under_hem_height)
        cx_, cz_ = np.median(p_in[band][:, 0]), np.median(p_in[band][:, 2])
        rad = np.hypot(p_in[:, 0] - cx_, p_in[:, 2] - cz_); ang = (np.degrees(np.arctan2(p_in[:, 0] - cx_, p_in[:, 2] - cz_)) % 360 // 10).astype(int)
        hb = np.floor((y_her - hem) / 0.02).astype(int); key = ang * 1000 + hb; inner = np.zeros(count, bool)
        for k_ in np.unique(key[band]):
            sel = band & (key == k_)
            if sel.sum() >= 10:
                inner |= sel & (rad < np.percentile(rad[sel], 90) - 0.02)
        hose_sel = np.isin(region, [3, 4]) & (y_her < hem - 0.01) & (y_her > hem - 0.08)
        hose = np.median(rgb_in[hose_sel], 0) if hose_sel.any() else rgb_in[band].mean(0)
        far = inner & (np.abs(rgb_in - hose).max(1) > a.under_hem_far)
        s_in = dict(s_in); rgb_new = np.asarray(s_in['rgb'], float).copy(); rgb_new[inner & ~far] = hose; s_in['rgb'] = rgb_new.astype(np.asarray(s_in['rgb']).dtype)
        op = np.asarray(s_in['opacities'], float).copy().reshape(-1); op[far] = -12.0; s_in['opacities'] = op.reshape(np.asarray(s_in['opacities']).shape)
        under_hem = {'interior': int(inner.sum()), 'recoloured': int((inner & ~far).sum()), 'dropped': int(far.sum()), 'hose_rgb': [round(float(v), 3) for v in hose],
                     'where': p_in[inner].round(4).tolist()}
        print(f"under-hem-clean: {under_hem['interior']} interior Gaussians, {under_hem['recoloured']} recoloured to the hose, {under_hem['dropped']} dropped", file=sys.stderr)
    # Neighbour averaging within each region.
    used = np.where(W.sum(0) > 0)[0]
    Wu = W[:, used]
    tree = cKDTree(means)
    _, nb = tree.query(means, k=12)
    same = region[nb] == region[:, None]
    for _ in range(a.smooth):
        avg = (Wu[nb] * same[..., None]).sum(1) / same.sum(1, keepdims=True)
        Wu = 0.5 * Wu + 0.5 * avg
    top = np.argsort(-Wu, 1)[:, :4]
    tw = np.take_along_axis(Wu, top, 1); tw /= np.maximum(tw.sum(1, keepdims=True), 1e-9)
    gbones = used[top]

    # --hand-clusters GAP: Gaussians weighted mainly to a hand (hand and finger bones) that are not in the hand's largest single-link
    # cluster at GAP metres are not hand: they take the bones and weights of their nearest body Gaussian (not dropped). Villager 383-A
    # (2026-11-15): hem trim beside his fingers rode the hand bones and hung as a clump apart from each hand, 1.5-2 cm clear of it.
    hand_clusters = {}
    if a.hand_clusters > 0:
        from scipy.sparse import coo_matrix
        from scipy.sparse.csgraph import connected_components
        op_now = np.asarray(s_in['opacities'], float).reshape(-1)
        hw = {}
        for s in 'lr':
            hb = [i for i, b in enumerate(names) if b.endswith('_' + s) and (b.startswith('hand') or b.split('_')[0] in ('thumb', 'index', 'middle', 'ring', 'pinky'))]
            hw[s] = (np.isin(gbones, hb) * tw).sum(1)
        donors = np.where((region == 0) & (hw['l'] < 0.1) & (hw['r'] < 0.1) & (op_now > -10))[0]
        dtree = cKDTree(means[donors])
        for s in 'lr':
            H = np.where((hw[s] > 0.5) & (op_now > -10))[0]
            pr = cKDTree(means[H]).query_pairs(a.hand_clusters, output_type='ndarray')   # her pose, metres
            _, lab = connected_components(coo_matrix((np.ones(len(pr)), (pr[:, 0], pr[:, 1])), shape=(len(H), len(H))), directed=False)
            sizes = np.bincount(lab); main = np.argmax(sizes); out = H[lab != main]
            if len(out):
                _, j = dtree.query(means[out]); src = donors[j]
                tw[out] = tw[src]; gbones[out] = gbones[src]; region[out] = 0
            gap = float(cKDTree(means[H[lab == main]]).query(means[out])[0].min()) if len(out) else None
            hand_clusters[s] = {'hand_weighted': int(len(H)), 'main': int(sizes[main]), 'moved': out.tolist(),
                                'other_clusters': sorted(sizes[np.arange(len(sizes)) != main].tolist(), reverse=True)[:8], 'nearest_gap_m': gap}
            print(f"hand-clusters {s}: {len(H)} hand-weighted, main cluster {sizes[main]}, {len(out)} in {len(sizes) - 1} other clusters "
                  f"(largest {hand_clusters[s]['other_clusters'][:3]}) go to their nearest body Gaussian's weights", file=sys.stderr)

    # Re-pose into the rest by linear blend skinning.
    M = np.einsum('nk,nkij->nij', tw, G[gbones])
    rest_means = np.einsum('nij,nj->ni', M[:, :3, :3], means) + M[:, :3, 3]
    ground = 0.0
    if a.repose_legs:
        # Stand the straightened rest on y = 0 (its lowest Gaussians), joints and frame with it.
        ground = float(np.percentile(rest_means[:, 1], 0.1))
        rest_means[:, 1] -= ground; rest[1:, 1] -= ground; offset = offset - [0.0, ground, 0.0]
    U, _, Vt = np.linalg.svd(M[:, :3, :3])
    Rb = U @ Vt
    q_in = s_in['quats'][:, [1, 2, 3, 0]]                              # wxyz -> xyzw
    q = (Rot.from_matrix(Rb) * Rot.from_matrix(np.broadcast_to(TO_SDK, (count, 3, 3))) * Rot.from_quat(q_in)).as_quat()
    quats = q[:, [3, 0, 1, 2]]
    scales = s_in['scales'] + np.log(scale)

    # Local rest translations for the joints block.
    T = np.zeros((n, 3))
    for i in range(n):
        T[i] = rest[i] if parents[i] < 0 else Rw[parents[i]].T @ (rest[i] - rest[parents[i]])

    palette = sorted(set(gbones[tw > 0].tolist()))
    lookup = {b: k for k, b in enumerate(palette)}
    stored = np.vectorize(lambda b: lookup.get(b, 0))(gbones)
    infl = np.where(np.isin(gbones, palette), tw, 0)
    total = write_vsplat(a.out, rest_means, scales, quats, s_in['opacities'], s_in['rgb'], stored, infl, palette, T)
    base = a.out.rsplit('.', 1)[0]
    dominant = gbones[:, 0]
    summary = {
        'splat': a.splat, 'landmarks': a.landmarks, 'splats': count, 'bytes': total, 'scale': scale,
        'height_m': float(rest_means[:, 1].max() - rest_means[:, 1].min()), 'repose': not a.no_repose, 'repose_legs': a.repose_legs,
        'rest_lowered_m': ground, 'hips_raised_m': hips_raised, 'pelvis_rest_m': float(rest[idx['pelvis']][1]),
        'skirt_floor': a.skirt_floor, 'skirt_calf': a.skirt_calf, 'skirt_split': a.skirt_split, 'skirt_ramp': a.skirt_ramp, 'shoe_damp': a.shoe_damp, 'smooth': a.smooth,
        'regions': {k: int((region == r).sum()) for r, k in enumerate(['body', 'arm_l', 'arm_r', 'shoe_l', 'shoe_r', 'hung'])},
        'tunic': {'max': a.tunic_max, 'split': a.tunic_split, 'ramp': a.tunic_ramp, 'from': a.tunic_from, 'sides': a.tunic_sides} if a.tunic else None,
        'hang': a.hang,
        'bones_used': [names[b] for b in palette],
        'dominant': {names[b]: int((dominant == b).sum()) for b in np.unique(dominant)},
        'arm_repose_degrees': {names[i]: float(np.degrees(np.arccos(np.clip((np.trace(A[i]) - 1) / 2, -1, 1)))) for i in A if names[i].split('_')[0] in ('upperarm', 'lowerarm', 'hand')},
        'leg_repose_degrees': {names[i]: float(np.degrees(np.arccos(np.clip((np.trace(A[i]) - 1) / 2, -1, 1)))) for i in A if names[i].split('_')[0] in ('thigh', 'calf', 'foot')},
    }
    json.dump(summary, open(base + '.json', 'w'), indent=1)
    if under_hem:
        json.dump(under_hem, open(base + '-under-hem.json', 'w'))
    if hand_clusters:
        json.dump(hand_clusters, open(base + '-hand-clusters.json', 'w'))
    np.savez(base + '-rig.npz', rest_means=rest_means, scales=scales, quats=quats, opacities=s_in['opacities'], rgb=s_in['rgb'],
             bones=gbones, weights=tw, joints=T, her_means=means, region=region, scale=scale, offset=offset, to_sdk=TO_SDK)
    print(json.dumps({k: summary[k] for k in ('splats', 'bytes', 'scale', 'height_m', 'pelvis_rest_m', 'regions', 'arm_repose_degrees', 'leg_repose_degrees')}))


if __name__ == '__main__':
    main()
