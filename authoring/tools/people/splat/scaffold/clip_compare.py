#!/usr/bin/env python3
"""The same Mixamo clips on several rigged splats of one character, side by side.

    clip_compare.py prep OUT_DIR --metres S [--free NAME=FREE.glb ...]
        [--bound NAME=BOUND.npz:BINDING.npz:SCAFFOLD.glb:TRANSFORM.json ...] --clip NAME=CLIP.json[:FRAMES] ... [--views quarter,close]
    clip_compare.py videos OUT_DIR RENDERS_DIR --name SET --grid "NAME=label,NAME=label/NAME=label,..." [--panel 1088]
        [--views ...] [--clips ...] [--loops CLIP=N ...] [--stills CLIP:FRAME:VIEW ...]

prep writes what render_posed.py needs for each figure, in one set of units and with the same
cameras about each figure's own hips:

- a --free figure is a free bake as a skinned splat GLB on the Mixamo body bones (vsplat_to_glb.py
  --rig mixamo of its humanoid_splat_rig.py character)
- a --bound figure is a bound bake with export_bound.py --binding, the rigged scaffold it was bound
  to and score_outline.py's fit of that scaffold to the bake. The bake is put in the scaffold's
  frame at S metres per bake unit, the free rig's scale (humanoid_splat_rig.py reports it)
- each clip goes onto every rig by retarget_clip.py

Files: OUT_DIR/NAME.npz, NAME-binding.npz, poses-NAME-CLIP.npz, prep.json. Views: front, quarter,
side, back-quarter (whole figure), close (upper body, front quarter) hands (hips to knees) and face. After render_posed.py has
drawn RENDERS_DIR/NAME/CLIP/, videos lays the figures out as --grid says (rows split by "/"), one
MP4 per clip and view under OUT_DIR/videos/SET-CLIP-VIEW.mp4, and writes each --stills frame at
full panel size as SET-CLIP-fNNN-VIEW.png.
"""
import argparse
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation as Rot

sys.path.insert(0, str(Path(__file__).parent))
from retarget_clip import load_rig, retarget, REPO   # noqa: E402

VIEWS = {"front": (0, 0.92, 4.1), "quarter": (35, 0.92, 4.1), "side": (90, 0.92, 4.1), "back-quarter": (145, 0.92, 4.1), "close": (30, 1.33, 2.0),
         "hands": (35, 0.78, 2.1), "face": (25, 1.55, 1.0)}
SIZE, FOV = 1088, 30.0


def cameras(centre, ground, views=tuple(VIEWS)):
    vm, Ks = [], []
    f = SIZE / 2 / np.tan(np.radians(FOV / 2))
    for v in views:
        az, h, d = VIEWS[v]
        a = np.radians(az)
        tgt = np.array([centre[0], ground + h, centre[2]])
        c = tgt + d * np.array([np.sin(a), 0.06, np.cos(a)])
        fw = (tgt - c) / np.linalg.norm(tgt - c)
        r = np.cross(fw, [0, 1, 0]); r /= np.linalg.norm(r)
        R = np.stack([r, np.cross(fw, r), fw])
        m = np.eye(4); m[:3, :3] = R; m[:3, 3] = -R @ c
        vm.append(m); Ks.append([[f, 0, SIZE / 2], [0, f, SIZE / 2], [0, 0, 1]])
    return np.array(vm, np.float32), np.array(Ks, np.float32)


def prep(a):
    out = a.out; out.mkdir(parents=True, exist_ok=True)
    xbot = json.loads(a.xbot_bind.read_text())
    views = a.views.split(",")
    rigs, grounds = {}, {}
    for spec in a.free:
        name, glb = spec.split("=", 1)
        rig = load_rig(glb)
        acc, at = rig["acc"], rig["attributes"]
        col = acc(at["_COLOR"]).astype(np.float64)
        al = np.clip(col[:, 3], 1 / 510, 1 - 1 / 510)
        np.savez(out / f"{name}.npz", means=rig["positions"].astype(np.float32), scales=np.log(acc(at["_SCALE"]).astype(np.float32)),
                 quats=acc(at["_ROTATION"]).astype(np.float32)[:, [3, 0, 1, 2]], opacities=np.log(al / (1 - al)).astype(np.float32), rgb=col[:, :3].astype(np.float32))
        w = acc(at["WEIGHTS_0"]).astype(np.float32)
        np.savez(out / f"{name}-binding.npz", bones=acc(at["JOINTS_0"]).astype(np.int16), weights=w / w.sum(1, keepdims=True))
        rig["ground"] = float(np.percentile(rig["positions"][col[:, 3] > 0.3, 1], 0.5))
        rigs[name] = rig
    for spec in a.bound:
        name, rest = spec.split("=", 1)
        splat, binding, scaffold, transform = rest.split(":")
        tr = json.loads(Path(transform).read_text())
        s, R, t = tr["scale"], np.array(tr["rotation"]), np.array(tr["translation"])
        sp = np.load(splat)
        means = a.metres * (sp["means"] - t) @ R                      # R^T (p - t), as rows
        rot = Rot.from_matrix(R.T) * Rot.from_quat(sp["quats"][:, [1, 2, 3, 0]])
        np.savez(out / f"{name}.npz", means=means.astype(np.float32), scales=(sp["scales"] + np.log(a.metres)).astype(np.float32),
                 quats=rot.as_quat()[:, [3, 0, 1, 2]].astype(np.float32), opacities=sp["opacities"], rgb=sp["rgb"])
        bd = np.load(binding)
        np.savez(out / f"{name}-binding.npz", bones=bd["bones"], weights=bd["weights"])
        rig = load_rig(scaffold, a.metres * s)
        if scaffold not in grounds:                                   # one ground per scaffold, from the first figure bound to it
            grounds[scaffold] = float(np.percentile(means[1 / (1 + np.exp(-sp["opacities"])) > 0.3, 1], 0.5))
        rig["ground"] = grounds[scaffold]
        rigs[name] = rig
    report = {"metres_per_bake_unit": a.metres, "views": views, "size": SIZE, "figures": {n: {"gaussians": int(len(np.load(out / f"{n}.npz")["means"])),
              "bones": len(r["names"]), "ground": round(r["ground"], 4)} for n, r in rigs.items()}, "clips": {}}
    for spec in a.clip:
        name, src = spec.split("=", 1)
        src, _, n = src.partition(":")
        clip = json.loads(Path(src).read_text())
        rec = {"source": clip.get("source", src), "fps": clip["fps"]}
        for tag, rig in rigs.items():
            skin, joints, k = retarget(rig, clip, xbot, int(n) if n else None)
            vm, Ks = cameras(rig["bind"][rig["names"].index("Hips")], rig["ground"], views)
            np.savez_compressed(out / f"poses-{tag}-{name}.npz", skin=skin.astype(np.float32), viewmats=vm, Ks=Ks, size=SIZE,
                                names=np.array([f"{name}-f{f:03d}" for f in range(len(skin))]), joints=joints.astype(np.float32))
            rec["frames"] = len(skin)
            rec.setdefault("leg_scale", {})[tag] = round(k, 4)
        report["clips"][name] = rec
        print(name, json.dumps(rec))
    (out / "prep.json").write_text(json.dumps(report, indent=1))


def videos(a):
    from PIL import Image, ImageDraw, ImageFont
    rep = json.loads((a.out / "prep.json").read_text())
    loops = dict(x.split("=") for x in a.loops)
    P = a.panel
    font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", max(18, P // 34))
    big = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 32)
    rows = [[c.split("=", 1) for c in r.split(",")] for r in a.grid.split("/")]
    cols = max(len(r) for r in rows)
    vid = a.out / "videos"; vid.mkdir(exist_ok=True)
    views = a.views.split(",") if a.views else rep["views"]
    clips = a.clips.split(",") if a.clips else list(rep["clips"])

    def sheet(clip, f, view, panel, fnt):
        v = rep["views"].index(view)
        im = Image.new("RGB", (cols * panel + (cols - 1) * 8, len(rows) * panel + (len(rows) - 1) * 8), (20, 20, 20))
        for r, row in enumerate(rows):
            for c, (tag, label) in enumerate(row):
                t = Image.open(a.renders / tag / clip / f"{clip}-f{f:03d}-v{v}.jpg")
                im.paste(t if panel == SIZE else t.resize((panel, panel), Image.LANCZOS), (c * (panel + 8), r * (panel + 8)))
                ImageDraw.Draw(im).text((c * (panel + 8) + panel // 45, r * (panel + 8) + panel // 55), label, fill=(255, 255, 255), font=fnt, stroke_width=3, stroke_fill=(0, 0, 0))
        return im
    for clip in clips:
        rec = rep["clips"][clip]
        nf = rec["frames"]
        loop = nf - 1 if nf <= 40 else nf      # a short cycle's last frame repeats its first
        for view in views:
            d = a.out / "pairs" / f"{a.name}-{clip}-{view}"; d.mkdir(parents=True, exist_ok=True)
            for f in range(loop):
                im = sheet(clip, f, view, P, font)
                im = im.crop((0, 0, im.width - im.width % 2, im.height - im.height % 2))
                im.save(d / f"{f:04d}.jpg", quality=93)
            o = vid / f"{a.name}-{clip}-{view}.mp4"
            r = subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-stream_loop", str(int(loops.get(clip, 1)) - 1), "-framerate", str(rec["fps"]),
                                "-i", str(d / "%04d.jpg"), "-c:v", "libx264", "-crf", "16", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(o)],
                               capture_output=True, text=True)
            if r.returncode:
                raise SystemExit(r.stderr[-800:])
        print(a.name, clip, "frames", loop, "views", views)
    for st in a.stills:
        clip, f, view = st.split(":")
        sheet(clip, int(f), view, SIZE, big).save(vid / f"{a.name}-{clip}-f{int(f):03d}-{view}.png", optimize=True)


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("prep")
    p.add_argument("out", type=Path)
    p.add_argument("--metres", type=float, required=True)
    p.add_argument("--free", action="append", default=[]); p.add_argument("--bound", action="append", default=[]); p.add_argument("--clip", action="append", default=[])   # none: figures only (static_idle.py adds procedural clips)
    p.add_argument("--views", default=",".join(VIEWS))
    p.add_argument("--xbot-bind", type=Path, default=REPO / "local/art/generated/people/dagger-characters/batches/xbot-bind.json")
    v = sub.add_parser("videos")
    v.add_argument("out", type=Path); v.add_argument("renders", type=Path)
    v.add_argument("--name", required=True); v.add_argument("--grid", required=True); v.add_argument("--panel", type=int, default=SIZE)
    v.add_argument("--views"); v.add_argument("--clips"); v.add_argument("--loops", nargs="*", default=[]); v.add_argument("--stills", nargs="*", default=[])
    a = ap.parse_args()
    prep(a) if a.cmd == "prep" else videos(a)


if __name__ == "__main__":
    main()
