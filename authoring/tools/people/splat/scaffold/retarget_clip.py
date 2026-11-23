#!/usr/bin/env python3
"""A Mixamo clip on a rig with Mixamo-like bone names, as skinning matrices for render_posed.py.

    retarget_clip.py RIG.glb CLIP.json OUT.npz [--scale F] [--frames N] [--xbot-bind JSON]

RIG.glb is any skinned GLB whose joints carry Mixamo's body names, with or without the
`mixamorig:` prefix, or Meshy's (Spine02, Spine01, Spine, neck). CLIP.json is the dump
`authoring/tools/people/splat/character/fbx_clip_dump.mjs CLIP.fbx CLIP.json 30` writes from an unedited Mixamo
FBX. The same rule serves rigs whose rest poses differ (a recovered rig re-posed to a canonical
rest, a mesher's rig in the pose as drawn), so two figures get the same motion:

- every bone turns by X Bot's change from its own bind (world space), as in mocap.py's Retarget;
- upper arms, forearms, thighs and shins are first swung so the bone points the way X Bot's does
  in its bind, which takes out a different arm angle or a flexed knee in the rest pose;
- a foot is only turned about the vertical to X Bot's heading (it keeps the sole flat as modelled);
- hands, toes and bones X Bot does not have follow their parent's rule, and finger bones ride the
  hand as one piece (a baked hand has its own finger pose);
- the hips move in place: height scaled by the leg length against X Bot's, a travelling clip's
  drift taken off, then one height offset for the whole clip that puts the lowest sole point of
  any frame on the rig's ground.

OUT.npz: `skin` [F, B, 4, 4] (row-major, rest = identity, B in the skin's joint order, in the
GLB's frame times --scale), `joints` [F, B, 3], `names`, `fps`, `k`, `ground`.
"""
import argparse
import json
import struct
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as Rot

REPO = Path(__file__).resolve().parents[5]
CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
MESHY = {"Spine02": "Spine", "Spine01": "Spine1", "Spine": "Spine2", "neck": "Neck"}
CHILD = {"Arm": "ForeArm", "ForeArm": "Hand", "UpLeg": "Leg", "Leg": "Foot"}
FOLLOW = {"Hand": "ForeArm", "ToeBase": "Foot"}


def read_glb(path):
    raw = Path(path).read_bytes()
    n = struct.unpack("<I", raw[12:16])[0]
    doc = json.loads(raw[20:20 + n])
    blob = raw[20 + n + 8:]

    def acc(i):
        ac = doc["accessors"][i]
        bv = doc["bufferViews"][ac["bufferView"]]
        k, dt = NC[ac["type"]], np.dtype(CT[ac["componentType"]])
        off = bv.get("byteOffset", 0) + ac.get("byteOffset", 0)
        stride = bv.get("byteStride") or k * dt.itemsize
        if stride == k * dt.itemsize:
            arr = np.frombuffer(blob, dt, ac["count"] * k, off)
        else:
            arr = np.concatenate([np.frombuffer(blob, dt, k, off + j * stride) for j in range(ac["count"])])
        arr = arr.reshape(ac["count"], k)
        if ac.get("normalized"):
            arr = arr.astype(np.float32) / np.iinfo(dt).max
        return arr
    return doc, acc


def load_rig(path, scale=1.0):
    doc, acc = read_glb(path)
    nodes, skin = doc["nodes"], doc["skins"][0]
    jn = skin["joints"]
    parent = {c: i for i, nd in enumerate(nodes) for c in nd.get("children", [])}
    raw = [nodes[j]["name"].split(":")[-1] for j in jn]
    names = [MESHY.get(n, n) for n in raw] if "Spine02" in raw else raw
    ibm = acc(skin["inverseBindMatrices"]).astype(np.float64).reshape(-1, 4, 4).transpose(0, 2, 1)
    body = next(nd for nd in nodes if nd.get("skin") is not None)
    at = doc["meshes"][body["mesh"]]["primitives"][0]["attributes"]
    pos = acc(at["POSITION"]).astype(np.float64) * scale
    return {"doc": doc, "acc": acc, "attributes": at, "names": names, "parents": [jn.index(parent[j]) if parent.get(j) in jn else -1 for j in jn],
            "bind": np.linalg.inv(ibm)[:, :3, 3] * scale, "ground": float(np.percentile(pos[:, 1], 0.5)), "positions": pos}


def swing(u, v):
    u, v = u / np.linalg.norm(u), v / np.linalg.norm(v)
    ax = np.cross(u, v)
    s = np.linalg.norm(ax)
    return np.eye(3) if s < 1e-9 else Rot.from_rotvec(ax / s * np.arctan2(s, float(u @ v))).as_matrix()


def yaw_to(u, v):
    return Rot.from_euler("y", np.arctan2(v[0], v[2]) - np.arctan2(u[0], u[2])).as_matrix()


def finger(n):
    return n.startswith(("LeftHand", "RightHand")) and not n.endswith("Hand")


def retarget(rig, clip, xbot, frames=None):
    names, parents, pb = rig["names"], rig["parents"], rig["bind"]
    nb = len(names)
    X = {k.split(":")[-1]: np.array(v, float).reshape(4, 4).T for k, v in xbot.items()}
    unit = 0.01 if X["Hips"][1, 3] > 10 else 1.0
    xr = {k: m[:3, :3] / np.linalg.norm(m[:3, :3], axis=0) for k, m in X.items()}
    xp = {k: m[:3, 3] * unit for k, m in X.items()}
    idx = {n: i for i, n in enumerate(names)}
    # the X Bot bone each joint takes its motion from, and its own turn before that
    source, S = [None] * nb, [np.eye(3)] * nb
    for j, n in enumerate(names):
        a = j
        while a >= 0 and (names[a] not in X or finger(names[a])):
            a = parents[a]
        source[j] = names[a] if a >= 0 else "Hips"
    order = sorted(range(nb), key=lambda j: sum(1 for _ in iter_up(parents, j)))
    for j in order:
        n = names[j]
        side = "Left" if n.startswith("Left") else "Right" if n.startswith("Right") else ""
        part = n[len(side):]
        if side and part in CHILD and side + CHILD[part] in idx:
            c = side + CHILD[part]
            S[j] = swing(pb[idx[c]] - pb[j], xp[c] - xp[n])
        elif side and part == "Foot" and side + "ToeBase" in idx:
            c = side + "ToeBase"
            S[j] = yaw_to(pb[idx[c]] - pb[j], xp[c] - xp[n])
        elif side and part in FOLLOW and side + FOLLOW[part] in idx:
            S[j] = S[idx[side + FOLLOW[part]]]
        elif (n not in X or finger(n)) and parents[j] >= 0:
            S[j] = S[parents[j]]
    leg = lambda p, s: np.linalg.norm(p(s + "Leg") - p(s + "UpLeg")) + np.linalg.norm(p(s + "Foot") - p(s + "Leg"))
    k = float(np.mean([leg(lambda n: pb[idx[n]], s) for s in ("Left", "Right")]) / np.mean([leg(lambda n: xp[n], s) for s in ("Left", "Right")]))

    cn = [b["name"].split(":")[-1] for b in clip["bones"]]
    samples = clip["samples"][:frames] if frames else clip["samples"]
    nf = len(samples)
    hips = np.array([s[cn.index("Hips")]["world_p"] for s in samples], float)
    hips *= 0.01 if hips[0, 1] > 10 else 1.0
    t = np.arange(nf)
    for a in (0, 2):
        travel = hips[-1, a] - hips[0, a]
        hips[:, a] -= hips[0, a] + (travel * t / max(nf - 1, 1) if abs(travel) > 0.3 else 0)
    G = np.zeros((nf, nb, 3, 3))
    P = np.zeros((nf, nb, 3))
    for f, s in enumerate(samples):
        for j in order:
            G[f, j] = Rot.from_quat(s[cn.index(source[j])]["world_q"]).as_matrix() @ xr[source[j]].T @ S[j]
            p = parents[j]
            P[f, j] = pb[j] + [k * hips[f, 0], k * hips[f, 1] - pb[j, 1], k * hips[f, 2]] if p < 0 else P[f, p] + G[f, p] @ (pb[j] - pb[p])
    # the lowest sole point of the clip goes on the rig's ground
    ground, low = rig["ground"], []
    for s in ("Left", "Right"):
        fo, to = idx[s + "Foot"], idx[s + "ToeBase"]
        h = pb[to] - pb[fo]
        h = np.array([h[0], 0, h[2]]) / np.hypot(h[0], h[2])
        heel = np.array([pb[fo, 0], ground, pb[fo, 2]]) - 0.06 * h
        ball = np.array([pb[to, 0], ground, pb[to, 2]])
        low += [(P[:, fo] + np.einsum("fij,j->fi", G[:, fo], q - pb[fo]))[:, 1] for q in (heel, ball)]
        low.append((P[:, to] + np.einsum("fij,j->fi", G[:, to], ball + 0.08 * h - pb[to]))[:, 1])
    P[:, :, 1] += ground - float(np.min(low))
    skin = np.tile(np.eye(4), (nf, nb, 1, 1))
    skin[:, :, :3, :3] = G
    skin[:, :, :3, 3] = P - np.einsum("fbij,bj->fbi", G, pb)
    return skin, P, k


def iter_up(parents, j):
    while parents[j] >= 0:
        j = parents[j]
        yield j


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("rig", type=Path)
    ap.add_argument("clip", type=Path)
    ap.add_argument("out", type=Path)
    ap.add_argument("--scale", type=float, default=1.0, help="multiply the rig's positions (to share another figure's units)")
    ap.add_argument("--frames", type=int, help="keep the first N frames")
    ap.add_argument("--xbot-bind", type=Path, default=REPO / "local/art/generated/people/dagger-characters/batches/xbot-bind.json",
                    help="fbx_bind_dump.mjs of X Bot.fbx")
    a = ap.parse_args()
    rig, clip = load_rig(a.rig, a.scale), json.loads(a.clip.read_text())
    skin, joints, k = retarget(rig, clip, json.loads(a.xbot_bind.read_text()), a.frames)
    np.savez_compressed(a.out, skin=skin.astype(np.float32), joints=joints.astype(np.float32), names=np.array(rig["names"]),
                        fps=clip["fps"], k=k, ground=rig["ground"])
    print(a.out.name, "frames", len(skin), "bones", len(rig["names"]), "leg scale", round(k, 4), "ground", round(rig["ground"], 4),
          "hips height", np.round(joints[:, rig["names"].index("Hips"), 1].min(), 3), "to", np.round(joints[:, rig["names"].index("Hips"), 1].max(), 3))


if __name__ == "__main__":
    main()
