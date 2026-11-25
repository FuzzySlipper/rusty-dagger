#!/usr/bin/env python3
"""The Gaussians of a bound bake that named bones lead, as a mask for budget_seed.py --region.

    bone_region.py BOUND.npz BINDING.npz SCAFFOLD.glb OUT.npy --bones Head [--front Z]

BINDING.npz is export_bound.py --binding of BOUND.npz on SCAFFOLD.glb. A Gaussian is in the region
when the bone with its largest weight is one of --bones; --front Z keeps only those with z greater
than Z in the bake's own units (the figure faces +z), which makes `--bones Head --front 0` the
face, front hair and beard without the back of the head (`--front auto`: the front half of the region itself, for a figure not
centred in z). Prints the region's size, its share of
the bake's opacity (what a plain budget_seed.py draw would give it), and its centre and extent in
bake units, for aiming close views at it (distill_dataset.py --target ... --arc ...).
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from retarget_clip import load_rig   # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("bound", type=Path); ap.add_argument("binding", type=Path); ap.add_argument("scaffold", type=Path); ap.add_argument("out", type=Path)
    ap.add_argument("--bones", required=True, help="comma-separated joint names (Mixamo names, as retarget_clip.py reads them)")
    ap.add_argument("--front", help="keep only Gaussians with z above this, in bake units; 'auto': above the region's own median z")
    a = ap.parse_args()
    sp, bd = np.load(a.bound), np.load(a.binding)
    names = load_rig(a.scaffold)["names"]
    want = [names.index(b) for b in a.bones.split(",")]
    lead = bd["bones"][np.arange(len(bd["bones"])), bd["weights"].argmax(1)]
    mask = np.isin(lead, want)
    if a.front is not None:   # auto (npc-471, 2026-11-25): a sword held out in front moved the bake's centre forward, and the whole
        z = sp["means"][:, 2]     # head sat behind z 0, so '--front 0' gave an empty face region
        mask &= z > (float(np.median(z[mask])) if a.front == "auto" else float(a.front))
    op = 1 / (1 + np.exp(-sp["opacities"].reshape(-1).astype(np.float64)))
    P = sp["means"][mask]
    np.save(a.out, mask)
    print(json.dumps({"gaussians": int(mask.sum()), "of": int(len(mask)), "opacity_share": round(float(op[mask].sum() / op.sum()), 4),
                      "centre": np.round(np.average(P, axis=0, weights=op[mask]), 4).tolist(),
                      "low": np.round(np.percentile(P, 1, axis=0), 4).tolist(), "high": np.round(np.percentile(P, 99, axis=0), 4).tolist()}))


if __name__ == "__main__":
    main()
