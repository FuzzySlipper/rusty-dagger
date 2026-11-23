#!/usr/bin/env python3
"""Write a bound splat character as the Engine's skinned splat GLB (Mixamo body bones).

    bound_to_glb.py FINAL_DIR OUT.glb [--mixamo-reference MIXAMO.glb] [--surface SURFACE.npz]

FINAL_DIR is a character's 09-game/bound/final/ (dagger-char bound ID --step choose): character.json names the body
.vsplat and its rig, the scaffold GLB the body was bound to (export_workbench.py). The bound route's .vsplat holds the
scaffold's own 36 joints (Meshy's body bones and the transplanted hands' finger bones) as world bind positions in the
rig's depth-first order, facing +Z with the left on +X, not the 441-joint table vsplat_to_glb.py reads.

This maps those joints onto the 22 Mixamo body bones the Engine already admits for the free bake
(authoring/tools/people/splat/gsplat/vsplat_to_glb.py --rig mixamo, asset-pipeline #10813 / rusty-engine #10805), so Mixamo clips play
on it by name: Spine02, Spine01 and Spine become Spine, Spine1 and Spine2, neck becomes Neck, head_end and headfront go
into Head, and each hand's six finger bones go into its Hand (the fingers no longer pose; Patch 2026-11-19 chose the GLB
the Engine reads over a new format). Joint positions are the scaffold's bind; joint frames are X Bot's, turned onto each
bone as in vsplat_to_glb.mixamo_frames. The GLB layout, encodings and every check (read back from the bytes, posing the
rest skin reproduces the .vsplat, per-joint weight totals, toes forward and left foot left) are vsplat_to_glb.py's.

A static NPC's short chain (static_rig.py: Hips, Spine, Spine1, Spine2, Neck, Head and the hands, 2026-11-22) gets the other 14
joints placed by static_limbs() with no weight, and asset.extras.static; its clips are its own (static_clips.py), since a
Mixamo idle would swing the hands (and a held staff) on arms the body has no Gaussians on.

Skirt bones (skirt_bones.py rig; Patch 2026-11-20: "16 bones in glb") come after the 22 under their own names, SkirtK_U under
mixamorig:Hips and SkirtK_L under it (or SkirtK alone for a one-tier rig), at the scaffold's bind positions with the world
frame. A Mixamo clip has no channel for them, so in the Engine they ride with the hips as the bind pose until something drives
them. --skirt SKIRT.json (skirt_bones.py's) puts what a driver needs in the GLB's extras under "skirt": the cloth sheet's rest
grid and constraints (two tiers) or the column ring (one tier), the leg joints it collides with, in this GLB's metres.
"""
import argparse, json, os, sys
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation as Rot

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent / 'gsplat'))
from retarget_clip import read_glb                                            # noqa: E402
import vsplat_to_glb as V                                                     # noqa: E402

# Mixamo joint <- scaffold bones whose weights it takes; the first gives its position.
MAP = {'Hips': ['Hips'], 'Spine': ['Spine02'], 'Spine1': ['Spine01'], 'Spine2': ['Spine'], 'Neck': ['neck'],
       'Head': ['Head', 'head_end', 'headfront']}
for s in ('Left', 'Right'):
    MAP.update({f'{s}Shoulder': [f'{s}Shoulder'], f'{s}Arm': [f'{s}Arm'], f'{s}ForeArm': [f'{s}ForeArm'],
                f'{s}Hand': [f'{s}Hand'] + [f'{s}Hand{f}{k}' for f in ('Index', 'Thumb') for k in (1, 2, 3)],
                f'{s}UpLeg': [f'{s}UpLeg'], f'{s}Leg': [f'{s}Leg'], f'{s}Foot': [f'{s}Foot'], f'{s}ToeBase': [f'{s}ToeBase']})


def depth_first(doc, joints):
    """Skin joint indices in the order export_workbench.py writes the .vsplat's joints block."""
    at = {node: k for k, node in enumerate(joints)}; out = []

    def visit(n):
        if n in at:
            out.append(at[n])
        for c in doc['nodes'][n].get('children', []):
            visit(c)
    for r in doc['scenes'][doc.get('scene', 0)]['nodes']:
        visit(r)
    return out


def bound_skin(v, rig, reference):
    doc, acc = read_glb(rig); sk = doc['skins'][0]; joints = sk['joints']
    bind = np.linalg.inv(acc(sk['inverseBindMatrices']).reshape(-1, 4, 4).transpose(0, 2, 1))
    order = depth_first(doc, joints)
    names = [doc['nodes'][joints[k]]['name'] for k in order]                  # .vsplat bone b is names[b]
    if len(names) != len(v['joints']):
        raise SystemExit(f"{rig}: {len(names)} joints, the .vsplat's joints block {len(v['joints'])}")
    err = float(np.abs(bind[order, :3, 3] - v['joints']).max())
    if err > 1e-4:
        raise SystemExit(f'{rig}: bind positions differ from the .vsplat joints block by {err:.2e} m: not the rig it was written with')
    P = dict(zip(names, bind[order, :3, 3]))
    if 'Spine02' not in P and 'Spine2' in P:   # a rig with Mixamo's own spine names (395's capelet scaffold) maps onto itself
        for m, b in (('Spine', 'Spine'), ('Spine1', 'Spine1'), ('Spine2', 'Spine2'), ('Neck', 'Neck' if 'Neck' in P else 'neck')):
            MAP[m] = [b]
        MAP['Head'] = ['Head'] + [b for b in names if b in ('head_end', 'headfront', 'HeadTop_End')]
    static = 'LeftHand' in P and 'LeftArm' not in P and 'LeftUpLeg' not in P
    if static:   # static_rig.py's short chain (a static NPC): the arms and legs placed, with no weight
        P.update(static_limbs(P))
    missing = [b for bs in MAP.values() for b in bs[:1] if b not in P]
    if missing:
        raise SystemExit(f'{rig}: no {", ".join(missing)}')
    pos = {m: P[bs[0]] for m, bs in MAP.items()}
    mix, index, translation, rotation, world, hips_ref = V.mixamo_frames(pos, reference)
    by_bone = {names.index(b): index[m] for m, bs in MAP.items() for b in bs if b in names}
    skirt = [b for b in names if b.startswith('Skirt')]   # skirt bones: their own joints after the 22, parented as in the rig
    if skirt:
        world = [np.asarray(w, float) for w in world]; translation = list(translation); rotation = list(rotation)
        par_of = {doc['nodes'][c]['name']: doc['nodes'][p]['name'] for p in joints for c in doc['nodes'][p].get('children', [])}
        mix = list(mix); parents_extra = {}
        for b in skirt:
            pn = par_of.get(b, 'Hips'); pi = index['Hips'] if pn == 'Hips' else index[pn]
            w = np.eye(4); w[:3, 3] = P[b]; local = np.linalg.inv(world[pi]) @ w
            index[b] = len(mix); mix.append(b); parents_extra[b] = pi; world.append(w)
            translation.append(local[:3, 3]); rotation.append(Rot.from_matrix(local[:3, :3]).as_quat())
            by_bone[names.index(b)] = index[b]
    unmapped = set(np.unique(v['bones'][v['weights'] > 0]).tolist()) - set(by_bone)
    if unmapped:
        raise SystemExit('weighted scaffold bones with no Mixamo joint: ' + ', '.join(names[b] for b in sorted(unmapped)))
    return {
        'order': mix, 'index': index, 'names': [V.PREFIX + n for n in mix[:len(V.MIXAMO)]] + skirt,
        'parents': [index[p] if p else -1 for _, p, *_ in V.MIXAMO] + [parents_extra[b] for b in skirt] if skirt else
                   [index[p] if p else -1 for _, p, *_ in V.MIXAMO],
        'translation': translation, 'rotation': rotation, 'world': np.array(world),
        'character_rotation': np.array([0.0, 0.0, 0.0, 1.0]), 'bind_turn': Rot.identity(),
        'source_forward': [0.0, 0.0, 1.0], 'source_left': [1.0, 0.0, 0.0],         # the scaffold already faces +Z
        'feet': tuple(V.PREFIX + n for n in ('LeftFoot', 'RightFoot', 'LeftToeBase', 'RightToeBase')), 'rig': 'mixamo',
        'bone_to_joint': by_bone, 'hips_height_ratio': float(pos['Hips'][1] / hips_ref),
        'scaffold_joints': names, 'skirt_joints': skirt, 'static': static,
    }


def static_limbs(P):
    """Places for the Mixamo arm and leg joints a static NPC's short chain lacks (Hips..Head and the hands; static_rig.py), in the
    scaffold's metres with the floor at 0: shoulders between Spine2 and Neck, each elbow below the middle of its arm, the legs
    straight down from the hips, toes forward. Nothing is weighted to them; they are there because the Engine admits the 22
    joints, and so that static_clips.py can turn each arm to keep its hand where the clip puts it."""
    s2, nk, hp = (np.asarray(P[n], float) for n in ('Spine2', 'Neck', 'Hips'))
    sho_y = (s2[1] + nk[1]) / 2; out = {}
    for s, sx in (('Left', 1), ('Right', -1)):
        h = np.asarray(P[f'{s}Hand'], float); arm = np.array([s2[0] + 0.17 * sx, sho_y - 0.02, s2[2]])
        out[f'{s}Shoulder'] = np.array([s2[0] + 0.05 * sx, sho_y, s2[2]]); out[f'{s}Arm'] = arm
        out[f'{s}ForeArm'] = (arm + h) / 2 + np.array([0.05 * sx, -0.08, -0.03])
        out[f'{s}UpLeg'] = np.array([hp[0] + 0.09 * sx, hp[1] - 0.05, hp[2]]); out[f'{s}Leg'] = np.array([hp[0] + 0.09 * sx, hp[1] * 0.5, hp[2]])
        out[f'{s}Foot'] = np.array([hp[0] + 0.09 * sx, 0.08, hp[2]]); out[f'{s}ToeBase'] = np.array([hp[0] + 0.09 * sx, 0.02, hp[2] + 0.12])
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('final', type=Path); ap.add_argument('out')
    ap.add_argument('--mixamo-reference', default=V.MIXAMO_REFERENCE)
    ap.add_argument('--surface', help="splat_surface_normals.py's npz for this .vsplat: adds _NORMAL and _ALBEDO")
    ap.add_argument('--skirt', type=Path, help="skirt_bones.py rig's skirt.json: the skirt driver's data in the GLB extras")
    a = ap.parse_args()
    man = json.loads((a.final / 'character.json').read_text())
    vs = a.final / man['body']; rig = a.final / man['rig']
    v = V.read_vsplat(str(vs))
    if a.surface:
        V.read_surface(a.surface, v)
    skin = bound_skin(v, rig, a.mixamo_reference)
    attrs = V.splat_attributes(v, skin)
    extras = {'source': vs.name, 'rig': 'mixamo', 'from_rig': f'bound scaffold ({len(skin["scaffold_joints"])} joints, fingers merged into the hands'
              + (f', {len(skin["skirt_joints"])} skirt bones after the 22)' if skin['skirt_joints'] else ')'),
              'splats': int(v['n']), 'spec': 'asset-pipeline #10813 / rusty-engine #10805'}
    if skin['static']:
        extras['from_rig'] = (f'static NPC short chain ({len(skin["scaffold_joints"])} joints: Hips..Head and the hands); the arm and leg joints '
                              'are placed but carry no weight. Play its own clips (ID-idle.glb, ID-look.glb, static_clips.py), not the Mixamo walks and idles')
        extras['static'] = True
    if skin['skirt_joints'] and a.skirt:
        C = json.loads(a.skirt.read_text())
        keep = ('tiers', 'mode', 'joints', 'nu', 'nv', 'bone_cols', 'grid0', 'theta', 'sim_theta', 'centre', 'top', 'hem', 'mid', 'ys', 'rad', 'pivots')
        extras['skirt'] = {k: C[k] for k in keep if k in C}
        extras['skirt'].update({'legs': {sd: [V.PREFIX + sd + n for n in ('UpLeg', 'Leg', 'Foot')] for sd in ('Left', 'Right')},
                                'hips': V.PREFIX + 'Hips', 'units': 'metres, the scaffold frame = this GLB bind space',
                                'driver': 'authoring/tools/people/splat/scaffold/skirt_bones.py drive (drive_cloth for two tiers): capsules 0.082/0.064 m + 0.018 gap, 8 substeps, homing 18/s, damping 0.92'})
    data = V.build_glb(skin, attrs, extras)
    report = V.check(data, v, skin, 22 + len(skin['skirt_joints']))
    tmp = a.out + '.part'
    with open(tmp, 'wb') as f:
        f.write(data)
    os.replace(tmp, a.out)
    report.update({'out': a.out, 'bytes': len(data), 'rig': 'mixamo', 'joint_names': skin['names'], 'skirt_joints': len(skin['skirt_joints']), 'static': skin['static'],
                   'hips_height_over_reference': skin['hips_height_ratio']})
    print(json.dumps(report, indent=1))


if __name__ == '__main__':
    main()
