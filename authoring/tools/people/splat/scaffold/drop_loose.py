#!/usr/bin/env python3
"""Drop Gaussians that come loose from the body in motion.

    drop_loose.py SPLAT.npz BINDING.npz PREP_DIR TAG OUT_DIR [--link M] [--clips walking,around] [--every 2] [--rig GLB --keep Skirt,Head]

A bound body can carry a few Gaussians that sit on the body at rest but end up cut off from it when a clip
poses the rig: hem and pouch pieces weighted to a different bone from the cloth round them. They read as
floating flecks beside the hem (villager 383-A, Patch 2026-11-17). PREP_DIR is clip_compare.py prep's
folder: TAG.npz, TAG-binding.npz and poses-TAG-CLIP.npz (metres; the same Gaussians in the same order as
SPLAT, which bound_run.sh clips made it from). A Gaussian is loose when it is in the largest group linked by
steps under --link metres at rest and, at some sampled frame of a clip, in a group smaller than --part of the body
(382's 16k right hand, 76 Gaussians, parts from the wrist in the walk and was dropped whole before --part, 2026-11-18). OUT_DIR gets splat.npz and
binding.npz without them, and loose.json with the counts per clip.

--keep (with --rig, the rig the clips posed) never drops a Gaussian whose leading bone's name starts with one of these. Skirt bones stretch the cloth by design and long
hair lifts off the back when the head turns: of the 636 Gaussians 456's 16k lost, 592 were skirt cloth (the hem shards at full
stride) and 43 hair, which left a hole of skin in the back of her hair (Patch 2026-11-20). '' keeps nothing.
"""
import argparse, json
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree
from scipy.sparse.csgraph import connected_components
from scipy.sparse import coo_matrix


def body(y, link, part=0):
    """The largest group linked by steps under link, plus every group of at least part Gaussians (a hand that parts from the arm)."""
    n = len(y); pairs = cKDTree(y).query_pairs(link, output_type='ndarray')
    _, lab = connected_components(coo_matrix((np.ones(len(pairs)), (pairs[:, 0], pairs[:, 1])), shape=(n, n)), directed=False)
    cnt = np.bincount(lab); big = cnt == cnt.max()
    if part: big |= cnt >= part
    return big[lab]


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('splat', type=Path); p.add_argument('binding', type=Path); p.add_argument('prep', type=Path); p.add_argument('tag')
    p.add_argument('out', type=Path); p.add_argument('--link', type=float, help='metres (default 0.03 up to 50,000 Gaussians, else 0.015)')
    p.add_argument('--part', type=float, default=0.002, help='a group with at least this share of the Gaussians stays (default 0.002: a hand that parts from its wrist in a pose)')
    p.add_argument('--clips', default='walking,around'); p.add_argument('--every', type=int, default=2)
    p.add_argument('--keep', default='Skirt,Head', help="bone-name prefixes whose Gaussians are never dropped ('' for none)")
    p.add_argument('--rig', type=Path, help='the rig the prep posed (its skin joints name the bones); --keep needs it')
    a = p.parse_args()
    s = np.load(a.prep / f'{a.tag}.npz'); b = np.load(a.prep / f'{a.tag}-binding.npz')
    x = s['means'].astype(np.float64); n = len(x); X = np.c_[x, np.ones(n)]; bo = b['bones'].astype(np.int64); w = b['weights']
    sp = dict(np.load(a.splat)); bd = dict(np.load(a.binding))
    if len(sp['means']) != n:
        raise SystemExit(f'{a.splat} has {len(sp["means"])} Gaussians, the prep {a.tag} {n}: not the same body')
    link = a.link or (0.03 if n <= 50000 else 0.015)
    part = max(10, int(a.part * n)); rest = body(x, link); loose = np.zeros(n, bool); per = {}
    pre = tuple(k for k in a.keep.split(',') if k) if a.rig else (); kept_by_bone = np.zeros(n, bool)
    if pre:
        import sys; sys.path.insert(0, str(Path(__file__).parent)); from retarget_clip import read_glb
        doc, _ = read_glb(a.rig); names = [doc['nodes'][j]['name'] for j in doc['skins'][0]['joints']]
        kept_by_bone = np.array([m.startswith(pre) for m in names])[bo[np.arange(n), w.argmax(1)]]
    for c in a.clips.split(','):
        sk = np.load(a.prep / f'poses-{a.tag}-{c}.npz')['skin']; got = np.zeros(n, bool)
        for f in range(0, len(sk), a.every):
            got |= rest & ~body(np.einsum('nk,nkij,nj->ni', w, sk[f][bo], X)[:, :3], link, part)
        per[c] = int((got & ~kept_by_bone).sum()); loose |= got
    spared = int((loose & kept_by_bone).sum()); loose &= ~kept_by_bone
    keep = ~loose; a.out.mkdir(parents=True, exist_ok=True)
    for f in ('splat.npz', 'binding.npz'):   # new files: the old ones may be hard-linked into a character folder
        (a.out / f).unlink(missing_ok=True)
    np.savez(a.out / 'splat.npz', **{k: v[keep] if len(v) == n else v for k, v in sp.items()})
    np.savez(a.out / 'binding.npz', **{k: v[keep] if len(v) == n else v for k, v in bd.items()})
    rep = {'gaussians': n, 'link_m': link, 'part': part, 'loose_per_clip': per, 'dropped': int(loose.sum()), 'kept': int(keep.sum()),
           'spared_by_bone': spared, 'keep_bones': a.keep}
    (a.out / 'loose.json').write_text(json.dumps(rep) + '\n'); print(json.dumps(rep))


if __name__ == '__main__':
    main()
