#!/usr/bin/env python3
"""Replace a rigged scaffold's own hands with rigged hands from a working model.

    transplant_hands.py SCAFFOLD.glb HANDS.glb OUT.glb [--wrist L,R] [--thumb front|in] [--report OUT.json]

SCAFFOLD.glb is a mesher's body with an API rig (Meshy: 24 bones, no fingers, hands as modelled).
HANDS.glb carries `Exposed LeftHand` / `Exposed RightHand` meshes on Mixamo hand bones, as the WAM
r05 full-skin GLB does (T-pose, palms down, index and thumb chains). For each side:

- the scaffold's own hand is measured from the vertices its Hand bone leads: the long axis from
  the wrist joint to the fingertips, where they end, and the flat of the palm;
- the wrist is where the arm is thinnest past the cuff (a mesher's rig puts its wrist joint up
  inside a long sleeve): the slice of smallest median radius in the first two fifths of the way
  to the fingertips, or --wrist L,R metres past the rig's joint;
- the scaffold's triangles past that wrist are removed, and what is left behind it (the cuff)
  is the forearm's alone;
- the donor hand is scaled so its fingertips end where the scaffold's did, turned onto that axis
  with its palm in that plane and its thumb to the front (or inward), and set with its wrist
  joint there; the stub it carries behind the wrist tucks into the cuff and is the forearm's;
- the scaffold's Hand joint moves to that wrist, and the donor's finger bones are added under it.

OUT.glb is one skinned mesh in the scaffold's frame with plain joints (no rotations), which is
all bound_data.py, export_bound.py and retarget_clip.py read.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from retarget_clip import read_glb   # noqa: E402


def skinned(doc, acc, node):
    prims = doc["meshes"][node["mesh"]]["primitives"]
    V, N, F, J, W, base = [], [], [], [], [], 0
    for pr in prims:
        at = pr["attributes"]
        p = acc(at["POSITION"]).astype(np.float64)
        j = np.concatenate([acc(at[k]).astype(int) for k in sorted(at) if k.startswith("JOINTS_")], 1)
        w = np.concatenate([acc(at[k]).astype(np.float64) for k in sorted(at) if k.startswith("WEIGHTS_")], 1)
        V.append(p); N.append(acc(at["NORMAL"]).astype(np.float64)); J.append(j); W.append(w)
        F.append(acc(pr["indices"]).astype(np.int64).reshape(-1, 3) + base); base += len(p)
    width = max(j.shape[1] for j in J)
    pad = lambda a: np.pad(a, ((0, 0), (0, width - a.shape[1])))
    return np.concatenate(V), np.concatenate(N), np.concatenate(F), np.concatenate([pad(j) for j in J]), np.concatenate([pad(w) for w in W])


def rig(doc, acc):
    nodes, skin = doc["nodes"], doc["skins"][0]
    jn = skin["joints"]
    parent = {c: i for i, nd in enumerate(nodes) for c in nd.get("children", [])}
    ibm = acc(skin["inverseBindMatrices"]).astype(np.float64).reshape(-1, 4, 4).transpose(0, 2, 1)
    return [nodes[j]["name"] for j in jn], [jn.index(parent[j]) if parent.get(j) in jn else -1 for j in jn], np.linalg.inv(ibm)[:, :3, 3]


def dense(J, W, n):
    D = np.zeros((len(J), n))
    for k in range(J.shape[1]):
        np.add.at(D, (np.arange(len(J)), J[:, k]), W[:, k])
    return D / D.sum(1, keepdims=True)


def led_by(path, bones):
    """Vertices, triangles and which triangles the named bones and their descendants lead (over half the weight at every corner).
    A name ending in '=' is that bone alone, without its descendants (npc-458, 2026-11-25: 'Hips=' for a kilt panel below the belt;
    plain 'Hips' is the whole body)."""
    doc, acc = read_glb(path)
    names, parents, _ = rig(doc, acc)
    V, _, F, J, W = skinned(doc, acc, next(nd for nd in doc["nodes"] if nd.get("skin") is not None))
    fam = set(names.index(b) for b in bones if not b.endswith('='))
    for j in range(len(names)):                      # parents come before children in every rig read here
        if parents[j] in fam:
            fam.add(j)
    fam |= set(names.index(b[:-1]) for b in bones if b.endswith('='))
    led = dense(J, W, len(names))[:, sorted(fam)].sum(1) > 0.5
    return V, F, led[F].all(1)


def axes(P, origin):
    """Long axis from the origin (the wrist) to where the farthest tenth of the points lie, the flat normal, and the reach."""
    d = np.linalg.norm(P - origin, axis=1)
    a = P[d >= np.percentile(d, 90)].mean(0) - origin
    a /= np.linalg.norm(a)
    _, _, vt = np.linalg.svd(P - P.mean(0), full_matrices=False)
    return a, vt[2], float(np.percentile((P - origin) @ a, 99.5))


def frame(a, n, t_hint):
    """Columns: long axis, thumb direction (in the palm, on t_hint's side), palm normal."""
    n = n - a * (n @ a); n /= np.linalg.norm(n)
    t = np.cross(n, a)
    t *= np.sign(t @ t_hint) or 1.0
    return np.stack([a, t, np.cross(a, t)], 1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scaffold", type=Path); ap.add_argument("hands", type=Path); ap.add_argument("out", type=Path)
    ap.add_argument("--wrist", help="L,R: metres past the rig's wrist joint where the hand leaves the cuff (default: the thinnest slice)")
    ap.add_argument("--thumb", choices=("front", "in"), default="front", help="which way the thumbs point in the scaffold's pose")
    ap.add_argument("--report", type=Path)
    ap.add_argument("--keep-hands", action="store_true", help="keep the scaffold's own hands (no donor): only rewrite the rig with plain joints "
                    "(npc-468's pose-locked painting, 2026-11-24: the mesher's relaxed hands match the painting; a flat donor hand would not)")
    a = ap.parse_args()
    sd, sacc = read_glb(a.scaffold)
    names, parents, pb = rig(sd, sacc)
    V, N, F, J, W = skinned(sd, sacc, next(nd for nd in sd["nodes"] if nd.get("skin") is not None))
    D = dense(J, W, len(names))
    hd, hacc = read_glb(a.hands)
    hn, _, hp = rig(hd, hacc)
    hn = [n.split(":")[-1] for n in hn]
    parts, report = [(V, N, F, D)], {"scaffold": str(a.scaffold), "hands": str(a.hands), "thumb": a.thumb}
    keep_face = np.ones(len(F), bool)
    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        if a.keep_hands:
            report[side] = {"kept": True, "own_hand_vertices": int((D[:, names.index(side + "Hand")] > 0.5).sum())}
            continue
        hi, fi = names.index(side + "Hand"), names.index(side + "ForeArm")
        joint = pb[hi].copy()
        own = np.flatnonzero(D[:, hi] > 0.5)
        ax, nrm, reach = axes(V[own], joint)
        arm = D[:, hi] + D[:, fi] > 0.5
        u = (V - joint) @ ax
        rad = np.linalg.norm((V - joint) - np.outer(u, ax), axis=1)
        if a.wrist:
            past = float(a.wrist.split(",")[side == "Right"])
        else:                                                    # thinnest centimetre of arm on the way to the palm
            best = (9.0, 0.0)
            for u0 in np.arange(0.0, 0.4 * reach, 0.005):
                m = arm & (u >= u0) & (u < u0 + 0.01) & (rad < 0.12)
                if m.sum() >= 8:
                    best = min(best, (float(np.median(rad[m])), float(u0 + 0.005)))
            past = best[1]
        w = joint + past * ax
        reach -= past
        hint = np.array([0, 0, 1.0]) if a.thumb == "front" else np.array([-sx, 0, 0])
        Fs = frame(ax, nrm, hint)
        # the donor hand in its own frame: wrist joint, long axis, thumb side
        node = next(nd for nd in hd["nodes"] if nd.get("name") == f"Exposed {side}Hand")
        HV, HN, HF, HJ, HW = skinned(hd, hacc, node)
        hw = hp[hn.index(side + "Hand")]
        hax, hnrm, hreach = axes(HV[(HV - hw) @ [sx, 0, 0] > 0], hw)
        Fh = frame(hax, hnrm, hp[hn.index(side + "HandThumb2")] - hw)
        if np.linalg.det(Fh) * np.linalg.det(Fs) < 0:        # keep it a rotation: flip the palm normal, not the hand
            Fs[:, 2] *= -1
        R = Fs @ Fh.T
        s = reach / hreach
        place = lambda p: w + s * (p - hw) @ R.T
        # cut the scaffold's own hand at the wrist; the cuff behind it is the forearm's
        near = arm & (rad < 0.16)
        past_v = near & (u > past)
        keep_face &= ~past_v[F].all(1)
        cuff = near & (u <= past)
        D[cuff, fi] += D[cuff, hi]; D[cuff, hi] = 0
        pb[hi] = w
        # donor weights onto the scaffold's bones, finger bones appended
        HD = dense(HJ, HW, len(hn))
        new = [n for n in hn if n.startswith(side + "Hand") and n != side + "Hand" and HD[:, hn.index(n)].max() > 0]
        first = len(names)
        for n in new:
            p = hn.index(n)
            par = hn[[i for i, m in enumerate(hn) if m == n[:-1] + str(int(n[-1]) - 1)][0]] if n[-1] != "1" else side + "Hand"
            names.append(n); parents.append(names.index(par)); pb = np.vstack([pb, place(hp[p])])
        ND = np.zeros((len(HV), len(names)))
        for i, n in enumerate(hn):
            col = HD[:, i]
            if not col.any():
                continue
            ND[:, names.index(n) if n in names[first:] else fi if n == side + "ForeArm" else hi] += col
        stub = (HV - hw) @ hax < 0                             # behind the wrist: inside the cuff, the forearm's
        ND[stub] = 0; ND[stub, fi] = 1
        parts.append((place(HV), HN @ R.T, HF, ND))
        report[side] = {"scale": round(float(s), 4), "scaffold_reach_m": round(reach, 4), "donor_reach_m": round(hreach, 4), "axis": np.round(ax, 3).tolist(),
                        "palm_normal": np.round(Fs[:, 2], 3).tolist(), "thumb": np.round(Fs[:, 1], 3).tolist(), "wrist_past_joint_m": round(past, 4), "scaffold_hand_vertices_cut": int(past_v.sum()),
                        "donor_vertices": len(HV), "new_joints": new}
    V, N, F0, D = parts[0]
    F0 = F0[keep_face]
    used = np.zeros(len(V), bool); used[F0] = True
    remap = np.cumsum(used) - 1
    nb = len(names)
    Vs, Ns, Fs_, Ds, base = [V[used]], [N[used]], [remap[F0]], [np.pad(D[used], ((0, 0), (0, nb - D.shape[1])))], int(used.sum())
    for v, n, f, d in parts[1:]:
        Vs.append(v); Ns.append(n); Fs_.append(f + base); Ds.append(np.pad(d, ((0, 0), (0, nb - d.shape[1])))); base += len(v)
    V, N, F, D = np.concatenate(Vs), np.concatenate(Ns), np.concatenate(Fs_), np.concatenate(Ds)
    N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-12
    top = np.argsort(-D, 1)[:, :4]
    Wt = np.take_along_axis(D, top, 1); Wt /= Wt.sum(1, keepdims=True)
    write_glb(a.out, V, N, F, top, Wt, names, parents, pb)
    report.update(vertices=len(V), triangles=len(F), joints=nb)
    (a.report or a.out.with_suffix(".json")).write_text(json.dumps(report, indent=1))
    print(json.dumps(report))


def write_glb(path, V, N, F, J, W, names, parents, pb):
    ibm = np.tile(np.eye(4, dtype=np.float32), (len(names), 1, 1)); ibm[:, :3, 3] = -pb
    chunks = [(V.astype(np.float32), 5126, "VEC3", 34962), (N.astype(np.float32), 5126, "VEC3", 34962), (J.astype(np.uint8), 5121, "VEC4", 34962),
              (W.astype(np.float32), 5126, "VEC4", 34962), (F.astype(np.uint32).ravel(), 5125, "SCALAR", 34963),
              (ibm.transpose(0, 2, 1).reshape(-1, 16), 5126, "MAT4", None)]
    blob, views, accs = bytearray(), [], []
    for arr, ct, typ, target in chunks:
        b = np.ascontiguousarray(arr).tobytes()
        v = {"buffer": 0, "byteOffset": len(blob), "byteLength": len(b)}
        if target:
            v["target"] = target
        views.append(v); blob += b + b"\0" * (-len(b) % 4)
        ac = {"bufferView": len(views) - 1, "componentType": ct, "count": len(arr) if typ != "SCALAR" else arr.size, "type": typ}
        if typ == "VEC3" and len(accs) == 0:
            ac["min"], ac["max"] = arr.min(0).tolist(), arr.max(0).tolist()
        accs.append(ac)
    nodes = []
    for i, n in enumerate(names):
        nd = {"name": n, "translation": (pb[i] - (pb[parents[i]] if parents[i] >= 0 else 0)).tolist()}
        ch = [j for j, p in enumerate(parents) if p == i]
        if ch:
            nd["children"] = ch
        nodes.append(nd)
    nodes.append({"name": "scaffold", "mesh": 0, "skin": 0})
    doc = {"asset": {"version": "2.0", "generator": "transplant_hands.py"}, "scene": 0,
           "scenes": [{"nodes": [i for i, p in enumerate(parents) if p < 0] + [len(names)]}], "nodes": nodes,
           "meshes": [{"name": "scaffold", "primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "JOINTS_0": 2, "WEIGHTS_0": 3}, "indices": 4}]}],
           "skins": [{"joints": list(range(len(names))), "inverseBindMatrices": 5, "skeleton": parents.index(-1)}],
           "buffers": [{"byteLength": len(blob)}], "bufferViews": views, "accessors": accs}
    js = json.dumps(doc).encode(); js += b" " * (-len(js) % 4)
    Path(path).write_bytes(struct.pack("<III", 0x46546C67, 2, 28 + len(js) + len(blob)) + struct.pack("<II", len(js), 0x4E4F534A) + js
                           + struct.pack("<II", len(blob), 0x004E4942) + bytes(blob))


if __name__ == "__main__":
    main()
