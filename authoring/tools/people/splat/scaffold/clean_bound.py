#!/usr/bin/env python3
"""Clean a bound bake by editing its Gaussians: no new bake.

    clean_bound.py SPLAT.npz BINDING.npz SCAFFOLD.glb OUT_DIR [--no-hands] [--no-face] [--no-crown]

A bound bake knows which bone leads every Gaussian (BINDING.npz, export_bound.py --binding), so faults that sit on a known part
and have the wrong colour for it can be taken out by rule. Three rules, each found on villager 381 (2026-11-16):

  hands   The cut-out kept grey background between the fingers (a clip with a pale glow round the outline; the threshold matte
          takes the glow for figure), and the bake painted it as pale Gaussians among the fingers. On the hand and finger bones,
          Gaussians that are near neutral, mid to light, and far from the hand's own median colour are removed. Dark ones stay (ink
          lines and shadows between fingers), and so do warm strays (pieces of a hem bound to the hand: they want rebinding).
  face    Stray greenish Gaussians on the head (green above both red and blue) are removed.
  crown   No view sees the top of the head well, so it carries pale and coloured streaks. In the top tenth of the head, Gaussians
          far from the hair's median colour, judged by the crown's own spread, take that colour (kept in place, so the hair stays
          whole). Hair with a wide spread of its own (inked blonde) is left as it is.

Writes OUT_DIR/splat.npz and binding.npz (the same Gaussians minus the removed ones, in the same order) and clean.json (counts).
It does not change light baked into the surface, and it cannot add detail a view never saw.
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
    ap.add_argument("splat", type=Path); ap.add_argument("binding", type=Path); ap.add_argument("scaffold", type=Path); ap.add_argument("out", type=Path)
    for k in ("hands", "face", "crown"):
        ap.add_argument(f"--no-{k}", action="store_true")
    a = ap.parse_args()
    s = dict(np.load(a.splat)); b = dict(np.load(a.binding)); n = len(s["means"])
    names = np.array(load_rig(a.scaffold)["names"])
    lead = names[b["bones"][np.arange(n), b["weights"].argmax(1)]]
    rgb = np.clip(s["rgb"].astype(np.float64), 0, 1); op = 1 / (1 + np.exp(-s["opacities"].reshape(-1).astype(np.float64)))
    lum = rgb @ [0.2126, 0.7152, 0.0722]; chroma = rgb.max(1) - rgb.min(1); M = s["means"]
    drop = np.zeros(n, bool); report = {"gaussians": n}
    if not a.no_hands:
        k = 0
        for side in ("Left", "Right"):
            hand = np.char.startswith(lead.astype(str), side + "Hand")
            if hand.sum() < 20:
                continue
            med = np.median(rgb[hand & (op > 0.3)], 0)
            # background grey or its pale glow: near neutral, mid to light, and not the hand's colour. Dark near-neutral Gaussians stay:
            # on light skin they are the ink lines and shadows between the fingers (454: the first rule took a fifth of her hands)
            pale = hand & (np.linalg.norm(rgb - med, axis=1) > 0.22) & (chroma < 0.10) & (lum > 0.27) & (lum < 0.80)
            drop |= pale; k += int(pale.sum())
        report["hands_removed"] = k
    head = lead == "Head"
    if not a.no_face and head.any():
        green = head & ((rgb[:, 1] - np.maximum(rgb[:, 0], rgb[:, 2])) > 0.04)
        drop |= green; report["face_green_removed"] = int(green.sum())
    if not a.no_crown and head.any():
        top = head & (M[:, 1] > np.percentile(M[head, 1], 90)) & ~drop
        hair = np.median(rgb[top], 0); dist = np.linalg.norm(rgb - hair, axis=1)
        # far from the hair's colour by the crown's own spread: plain black hair has a tight spread and its pale streaks stand out;
        # inked blonde hair varies widely and is left alone (454: a fixed threshold recoloured 85% of her crown)
        dm = float(np.median(dist[top])); mad = float(np.median(np.abs(dist[top] - dm)))
        stray = top & (dist > max(0.25, dm + 4 * mad))
        new = s["rgb"].copy(); new[stray] = hair.astype(new.dtype); s["rgb"] = new
        report["crown_recoloured"] = int(stray.sum()); report["crown_gaussians"] = int(top.sum())
    keep = ~drop
    a.out.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(a.out / "splat.npz", **{k: (v[keep] if getattr(v, "shape", ()) and v.shape[0] == n else v) for k, v in s.items()})
    np.savez_compressed(a.out / "binding.npz", **{k: (v[keep] if getattr(v, "shape", ()) and v.shape[0] == n else v) for k, v in b.items()})
    report["kept"] = int(keep.sum())
    (a.out / "clean.json").write_text(json.dumps(report, indent=1) + "\n"); print(json.dumps(report))


if __name__ == "__main__":
    main()
