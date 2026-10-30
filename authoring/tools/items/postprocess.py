#!/usr/bin/env python3
"""Deterministic post-process prototype for completed item-art studies.

Two treatments, alone and stacked, applied to one route of a finished study:

- Back-projection: repeatedly shrink the generated image to the source sprite's
  grid, measure its colour difference from the native sprite (optionally after
  a blur at native scale, so only broad masses are enforced), enlarge that
  difference smoothly and add it back. The result keeps generated detail below
  the source pixel size while its broad colour/value masses return to the
  original. Correction is restricted to the generated object. A partial
  strength blends the converged correction in OKLab (an iteration step below 1
  still converges over the five iterations, so it is not a strength control).
  The correction field is shaped by a guided filter on the generated image's
  lightness, so it stays within the drawing's ink-bounded shapes.
- Palette grading: pull each object pixel part-way toward its nearest colour in
  a palette (OKLab), mostly in hue/chroma, with the per-pixel shift blurred so
  gradients are not posterised. Either the full Daggerfall palette, or only the
  palette entries the source sprite itself uses (its "item ramps").

No inference. `run` reads the study's plan.json, input.json and native PNGs
and writes a new output directory with images, per-item records, metrics and
sheets. `cutout` turns a chosen variant into transparent RGBA icons. `compare` lays out chosen variants from several runs side by side.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
GUIDED_EPS = 2e-3

VARIANTS = {
    "original": "Route output (input)",
    "bp-broad": "Back-projected, broad",
    "bp-broad-half": "Back-projected, broad, half",
    "bp-broad-07": "Back-projected, broad, 0.7",
    "bp-strict": "Back-projected, strict",
    "palette-full": "Full ART_PAL grade",
    "palette-item": "Item-ramp grade",
    "bp-broad-palette": "Broad BP + item ramps",
}


def srgb_to_oklab(rgb):
    c = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)
    lms = c @ np.array([[0.4122214708, 0.2119034982, 0.0883024619],
                        [0.5363325363, 0.6806995451, 0.2817188376],
                        [0.0514459929, 0.1073969566, 0.6299787005]])
    lms = np.cbrt(lms)
    return lms @ np.array([[0.2104542553, 1.9779984951, 0.0259040371],
                           [0.7936177850, -2.4285922050, 0.7827717662],
                           [-0.0040720468, 0.4505937099, -0.8086757660]])


def oklab_to_srgb(lab):
    lms = lab @ np.array([[1.0, 1.0, 1.0],
                          [0.3963377774, -0.1055613458, -0.0894841775],
                          [0.2158037573, -0.0638541728, -1.2914855480]])
    c = (lms ** 3) @ np.array([[4.0767416621, -1.2684380046, -0.0041960863],
                               [-3.3077115913, 2.6097574011, -0.7034186147],
                               [0.2309699292, -0.3413193965, 1.7076147010]])
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def _convolve(a, k, axis):
    r = len(k) // 2
    pad = [(0, 0)] * a.ndim
    pad[axis] = (r, r)
    windows = np.lib.stride_tricks.sliding_window_view(np.pad(a, pad), len(k), axis)
    return windows @ k


def blur(a, sigma):
    """Separable Gaussian over the first two axes; zero padding."""
    if sigma <= 0:
        return a
    r = int(3 * sigma + 0.5)
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    return _convolve(_convolve(a, k / k.sum(), 0), k / k.sum(), 1)


def box(a, r):
    """Mean over a (2r+1)^2 window, edge-clamped, via summed-area table."""
    pad = np.pad(a, [(r + 1, r), (r + 1, r)] + [(0, 0)] * (a.ndim - 2), mode="edge")
    c = pad.cumsum(0).cumsum(1)
    n = 2 * r + 1
    return (c[n:, n:] - c[:-n, n:] - c[n:, :-n] + c[:-n, :-n]) / n ** 2


def guided(guide, p, r, eps):
    """He et al. guided filter: smooths p while following edges of the single-channel guide."""
    g = guide[..., None]
    mg, mp = box(g, r), box(p, r)
    a = (box(g * p, r) - mg * mp) / (box(g * g, r) - mg * mg + eps)
    b = mp - a * mg
    return box(a, r) * g + box(b, r)


def weighted_blur(a, w, sigma):
    num, den = blur(a * w[..., None], sigma), blur(w, sigma)
    return num / np.maximum(den, 1e-6)[..., None], den


def resize(a, size):
    """Bilinear resize of a float HxWxC array to (width, height)."""
    return np.stack([np.asarray(Image.fromarray(a[..., c].astype(np.float32), "F")
                                .resize(size, Image.Resampling.BILINEAR)) for c in range(a.shape[2])], -1)


def object_mask(rgb):
    # Routes do not reproduce the requested ground exactly (direct Krea draws a darker navy),
    # so take the ground from the image border rather than the spec.
    border = np.concatenate([rgb[:16].reshape(-1, 3), rgb[-16:].reshape(-1, 3),
                             rgb[:, :16].reshape(-1, 3), rgb[:, -16:].reshape(-1, 3)])
    bg = np.median(border, 0)
    hard = (np.abs(rgb - bg).max(-1) > 0.06).astype(np.float64)
    return blur(hard, 1.5)


def load_palette(path):
    data = Path(path).read_bytes()
    entries = np.frombuffer(data[len(data) - 768:], np.uint8).reshape(256, 3)[1:]
    entries = np.unique(entries[(entries != [255, 0, 255]).any(1)], axis=0)
    return srgb_to_oklab(entries / 255)


class Item:
    def __init__(self, study, item_id, image):
        meta = json.loads((study / "inputs" / item_id / "input.json").read_text())
        src = np.asarray(Image.open(study / "inputs" / item_id / "source.png").convert("RGBA")) / 255
        self.alpha = (src[..., 3] > 0.5).astype(np.float64)
        # The sprite's own palette entries: the Daggerfall ramps this item actually uses.
        self.ramps = np.unique(srgb_to_oklab(src[..., :3][self.alpha > 0]), axis=0)
        self.src_lab = srgb_to_oklab(src[..., :3])
        self.scale = meta["integer_scale"]
        self.x, self.y = meta["placement"]
        self.h, self.w = self.alpha.shape
        self.gen = np.asarray(Image.open(image).convert("RGB")) / 255
        self.mask = object_mask(self.gen)

    def crop(self, a):
        return a[self.y:self.y + self.h * self.scale, self.x:self.x + self.w * self.scale]

    def shrink(self, a):
        s = self.scale
        return self.crop(a).reshape(self.h, s, self.w, s, -1).mean((1, 3))

    def weights(self, mask):
        # Compare only source-opaque cells the generated object actually covers.
        return self.alpha * (self.shrink(mask[..., None])[..., 0] > 0.6)

    def source_error(self, rgb, sigma):
        w = self.weights(self.mask)
        a, _ = weighted_blur(srgb_to_oklab(self.shrink(rgb)), w, sigma)
        b, _ = weighted_blur(self.src_lab, w, sigma)
        return float((np.linalg.norm(a - b, axis=-1) * w).sum() / max(w.sum(), 1))

    def back_project(self, rgb, sigma, iterations=5, step=1.0):
        out = srgb_to_oklab(rgb)
        w = self.weights(self.mask)
        target, _ = weighted_blur(self.src_lab, w, sigma)
        size = (self.w * self.scale, self.h * self.scale)
        guide = self.crop(srgb_to_oklab(self.gen))[..., 0]
        for _ in range(iterations):
            current, _ = weighted_blur(srgb_to_oklab(self.shrink(oklab_to_srgb(out))), w, sigma)
            err = (target - current) * w[..., None]
            up = resize(err, size)
            up_w = resize(w[..., None], size)[..., 0]
            # Normalise so cells beside transparent/uncovered ones are not damped, then let the
            # generated drawing's own value edges shape the field so it stays inside ink-bounded shapes.
            up = up / np.maximum(up_w, 1e-3)[..., None] * (up_w > 0.05)[..., None]
            up = guided(guide, up, self.scale, GUIDED_EPS)
            self.crop(out)[:] += step * up * self.crop(self.mask)[..., None]
        return oklab_to_srgb(out)

    def palette_grade(self, rgb, palette, chroma=0.6, lightness=0.25, sigma=2.0):
        lab = srgb_to_oklab(rgb)
        flat = lab.reshape(-1, 3)
        nearest = np.empty_like(flat)
        for i in range(0, len(flat), 65536):
            d = ((flat[i:i + 65536, None, :] - palette[None]) ** 2).sum(-1)
            nearest[i:i + 65536] = palette[d.argmin(1)]
        delta = blur((nearest - flat).reshape(lab.shape), sigma)
        delta *= np.array([lightness, chroma, chroma]) * self.mask[..., None]
        return oklab_to_srgb(lab + delta)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save(rgb, path):
    Image.fromarray((np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8)).save(path)


def sheet(study, items, columns, title, path):
    """columns: (label, run directory, variant) triples; the source reference is always first."""
    font, small = ImageFont.truetype(FONT, 15), ImageFont.truetype(FONT, 12)
    metrics = {run: json.loads((run / "metrics.json").read_text()) for _, run, _ in columns}
    cell, cols = 212, [("Source / nearest", None, "source"), *columns]
    canvas = Image.new("RGB", (130 + cell * len(cols), 60 + (cell + 34) * len(items)), "#202329")
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 8), title, fill="white", font=font)
    draw.text((12, 32), "Broad = native-grid blur sigma 1.0; strict = no blur. Numbers: mean OKLab error vs source at "
              "sigma 1.0 (x100, lower = closer to the original's broad masses).", fill="#aebccd", font=small)
    for c, (label, _, _) in enumerate(cols):
        draw.text((130 + c * cell + 6, 48), label, fill="#e4d4ab", font=small)
    for r, item in enumerate(items):
        top = 66 + r * (cell + 34)
        draw.text((10, top + cell // 2 - 8), item["name"].split(" (")[0], fill="white", font=font)
        draw.text((10, top + cell // 2 + 12), item["name"].split(" ")[-1], fill="#aebccd", font=small)
        for c, (_, run, name) in enumerate(cols):
            if name == "source":
                im = Image.open(study / "inputs" / item["id"] / "reference.png").resize((cell - 8,) * 2, Image.Resampling.NEAREST)
            else:
                im = Image.open(run / "images" / item["id"] / f"{name}.png").resize((cell - 8,) * 2, Image.Resampling.LANCZOS)
            canvas.paste(im, (130 + c * cell + 4, top))
            if name != "source":
                draw.text((130 + c * cell + 6, top + cell - 2), f"{metrics[run][item['id']][name]['broad'] * 100:.1f}",
                          fill="#aebccd", font=small)
    canvas.save(path)


def detail_sheet(item_ids, names, columns, path, box=448):
    font = ImageFont.truetype(FONT, 14)
    cols = columns
    canvas = Image.new("RGB", (16 + (box + 12) * len(cols), 36 + (box + 30) * len(item_ids)), "#202329")
    draw = ImageDraw.Draw(canvas)
    for r, item_id in enumerate(item_ids):
        top = 30 + r * (box + 30)
        for c, (label, run, name) in enumerate(cols):
            im = Image.open(run / "images" / item_id / f"{name}.png")
            m = object_mask(np.asarray(im.convert("RGB")) / 255) > 0.5
            ys, xs = np.nonzero(m)
            cx, cy = int(xs.mean()), int(ys.mean())
            x0, y0 = np.clip(cx - box // 2, 0, 1024 - box), np.clip(cy - box // 2, 0, 1024 - box)
            canvas.paste(im.crop((x0, y0, x0 + box, y0 + box)), (8 + c * (box + 12), top + 20))
            draw.text((8 + c * (box + 12), top), f"{names[item_id]} — {label} (native 1:1 crop)",
                      fill="#e4d4ab", font=font)
    canvas.save(path)


def run(args):
    study, out = args.study.resolve(), args.out.resolve()
    if (out / "record.json").exists():
        raise SystemExit(f"{out} already holds a run; use a new directory")
    plan = json.loads((study / "plan.json").read_text())
    spec = plan["spec"]
    palette = load_palette(args.palette)
    params = {"broad_sigma": 1.0, "strict_sigma": 0.0, "iterations": 5, "step": 1.0, "guided_eps": GUIDED_EPS,
              "half_strength": 0.5, "partial_strength": 0.7, "palette_chroma": 0.6, "palette_lightness": 0.25, "palette_blur_sigma": 2.0}
    record = {"study": str(study), "route": args.route, "seed": args.seed, "palette": str(args.palette.resolve()),
              "palette_sha256": sha256(args.palette), "palette_colours": len(palette), "params": params,
              "metric": "mean OKLab distance between the image shrunk to the source grid and the source, "
                        "both blurred at sigma 1.0 (broad) or unblurred (strict), over source-opaque covered cells",
              "variants": {v: VARIANTS[v] for v in args.variants}, "items": {}}
    metrics = {}
    for item in spec["items"]:
        job = next(j for j in plan["jobs"] if j["route"] == args.route and j["item"] == item["id"]
                   and args.seed in (None, j["seed"]))
        it = Item(study, item["id"], job["output"])
        d = out / "images" / item["id"]
        d.mkdir(parents=True, exist_ok=True)
        grade = (params["palette_chroma"], params["palette_lightness"], params["palette_blur_sigma"])
        cache = {}

        def broad():
            if "broad" not in cache:
                cache["broad"] = it.back_project(it.gen, params["broad_sigma"], params["iterations"], params["step"])
            return cache["broad"]

        def partial(strength):
            gen = srgb_to_oklab(it.gen)
            return oklab_to_srgb(gen + strength * (srgb_to_oklab(broad()) - gen))

        makers = {
            "original": lambda: it.gen,
            "bp-broad": broad,
            "bp-broad-half": lambda: partial(params["half_strength"]),
            "bp-broad-07": lambda: partial(params["partial_strength"]),
            "bp-strict": lambda: it.back_project(it.gen, params["strict_sigma"], params["iterations"], params["step"]),
            "palette-full": lambda: it.palette_grade(it.gen, palette, *grade),
            "palette-item": lambda: it.palette_grade(it.gen, it.ramps, *grade),
            "bp-broad-palette": lambda: it.palette_grade(broad(), it.ramps, *grade),
        }
        metrics[item["id"]] = {}
        for name in args.variants:
            rgb = makers[name]()
            save(rgb, d / f"{name}.png")
            metrics[item["id"]][name] = {"broad": it.source_error(rgb, 1.0), "strict": it.source_error(rgb, 0.0)}
        record["items"][item["id"]] = {"job": job["id"], "input": job["output"], "input_sha256": sha256(job["output"]),
                                      "source": str(study / "inputs" / item["id"] / "source.png")}
        print(item["id"], {k: round(v["broad"] * 100, 2) for k, v in metrics[item["id"]].items()})
    (out / "metrics.json").write_text(json.dumps(metrics, indent=2) + "\n")
    items = spec["items"]
    columns = [(VARIANTS[v], out, v) for v in args.variants]
    for page, start in enumerate(range(0, len(items), 5), 1):
        sheet(study, items[start:start + 5], columns,
              f"{spec['title']} — back-projection and Daggerfall palette grading (no inference), page {page}",
              out / f"sheet-{page}.png")
    if args.detail:
        detail = [c for c in columns if c[2] in ("original", "bp-broad", "bp-broad-palette")]
        detail_sheet(args.detail, {i["id"]: i["name"].split(" (")[0] for i in items}, detail, out / "detail-native.png")
    (out / "record.json").write_text(json.dumps(record, indent=2) + "\n")


def screen_sheet(study, items, columns, height, title, path):
    """Each column's object box at the size a 320x200 frame scaled to `height` would show it, 1:1 pixels."""
    font, small = ImageFont.truetype(FONT, 15), ImageFont.truetype(FONT, 12)
    k = height / 200
    cols = [("Source / nearest", None, "source"), *columns]
    rows = []
    for item in items:
        native = json.loads((study / "inputs" / item["id"] / "input.json").read_text())["native_size"]
        size = (round(native[0] * k), round(native[1] * k))
        cells = []
        for _, run, name in cols:
            if name == "source":
                im = Image.open(study / "inputs" / item["id"] / "source.png").convert("RGBA")
                bg = Image.new("RGBA", im.size, "#30343b")
                cells.append(Image.alpha_composite(bg, im).convert("RGB").resize(size, Image.Resampling.NEAREST))
                continue
            meta = json.loads((Path(json.loads((run / "record.json").read_text())["study"]) / "inputs" / item["id"]
                               / "input.json").read_text())
            (x, y), s = meta["placement"], meta["integer_scale"]
            im = Image.open(run / "images" / item["id"] / f"{name}.png").convert("RGB")
            cells.append(im.crop((x, y, x + native[0] * s, y + native[1] * s)).resize(size, Image.Resampling.LANCZOS))
        rows.append((item, size, cells))
    width = max(16 + len(cols) * (size[0] + 16) for _, size, _ in rows)
    canvas = Image.new("RGB", (max(width, 900), 50 + sum(size[1] + 46 for _, size, _ in rows)), "#202329")
    draw = ImageDraw.Draw(canvas)
    draw.text((12, 8), title, fill="white", font=font)
    draw.text((12, 28), f"1:1 pixels at a {height}p screen: each 320x200 source pixel = {k:.1f} screen pixels. "
              "Generated boxes are downsampled (Lanczos) to that size.", fill="#aebccd", font=small)
    top = 50
    for item, size, cells in rows:
        for c, ((label, _, _), cell) in enumerate(zip(cols, cells)):
            x = 16 + c * (size[0] + 16)
            draw.text((x, top + 4), f"{item['name'].split(' (')[0]} — {label}", fill="#e4d4ab", font=small)
            canvas.paste(cell, (x, top + 24))
        top += size[1] + 46
    canvas.save(path)


def compare(args):
    """Side-by-side sheets of variants from several `run` outputs over the same items."""
    study, out = args.study.resolve(), args.out.resolve()
    if (out / "compare.json").exists():
        raise SystemExit(f"{out} already holds a comparison; use a new directory")
    out.mkdir(parents=True)
    columns = []
    for spec in args.column:
        label, rest = spec.split("=", 1)
        run_dir, variant = rest.rsplit(":", 1)
        columns.append((label, Path(run_dir).resolve(), variant))
    items = json.loads((study / "plan.json").read_text())["spec"]["items"]
    for page, start in enumerate(range(0, len(items), 5), 1):
        sheet(study, items[start:start + 5], columns, f"{args.title}, page {page}", out / f"compare-{page}.png")
    if args.detail:
        names = {i["id"]: i["name"].split(" (")[0] for i in items}
        detail_sheet(args.detail, names, [columns[i] for i in args.detail_columns], out / "detail-native.png")
    for height in args.screen_height:
        for item in items:
            screen_sheet(study, [item], columns, height, f"{args.title} — {item['name'].split(' (')[0]}, {height}p screen size",
                         out / f"screen-{height}-{item['id']}.png")
    (out / "compare.json").write_text(json.dumps({"study": str(study), "title": args.title,
                                                  "columns": [[l, str(r), v] for l, r, v in columns],
                                                  "detail": args.detail, "detail_columns": args.detail_columns,
                                                  "screen_height": args.screen_height},
                                                 indent=2) + "\n")


def background_components(ground, source_open):
    """Ground-coloured pixels that are background: components touching the canvas border,
    and enclosed regions where the source sprite itself is transparent (a chain's loop). A large
    enclosed ground-coloured region over opaque source cells is the object's interior (a glass jar)."""
    h, w = ground.shape
    g, seen = ground.tolist(), [[False] * w for _ in range(h)]
    out = np.zeros(ground.shape, bool)
    for y0 in range(h):
        for x0 in range(w):
            if not g[y0][x0] or seen[y0][x0]:
                continue
            comp, i, border = [(y0, x0)], 0, False
            seen[y0][x0] = True
            while i < len(comp):
                y, x = comp[i]
                i += 1
                border |= y in (0, h - 1) or x in (0, w - 1)
                for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                    if 0 <= yy < h and 0 <= xx < w and g[yy][xx] and not seen[yy][xx]:
                        seen[yy][xx] = True
                        comp.append((yy, xx))
            ys, xs = np.array(comp).T
            if border or source_open[ys, xs].mean() > 0.5:
                out[ys, xs] = True
    return out


def cutout(item, rgb, lo=0.035, hi=0.11, reach=4):
    """RGBA icon: the flat generation ground (taken from the border) becomes transparent, edge pixels get
    a soft alpha beside the ground with the ground colour removed from their RGB."""
    border = np.concatenate([rgb[:16].reshape(-1, 3), rgb[-16:].reshape(-1, 3),
                             rgb[:, :16].reshape(-1, 3), rgb[:, -16:].reshape(-1, 3)])
    bg = np.median(border, 0)
    d = np.abs(rgb - bg).max(-1)
    # Where the source sprite is opaque, grown by one source pixel for the drawing's overhang.
    opaque = np.zeros(d.shape)
    s = item.scale
    opaque[item.y:item.y + item.h * s, item.x:item.x + item.w * s] = np.kron(item.alpha, np.ones((s, s)))
    open_ = np.asarray(Image.fromarray((opaque * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(2 * s + 1))) < 128
    ground = background_components(d < lo, open_)
    near = np.asarray(Image.fromarray((ground * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(2 * reach + 1))) > 0
    a = np.where(ground, 0.0, np.clip((d - lo) / (hi - lo), 0, 1))
    a = np.where(near, a, 1.0)
    a = np.clip(blur(a, 0.6), 0, 1)
    a = np.where(ground & (a < 0.02), 0, a)[..., None]
    colour = np.where(a > 0.02, (rgb - (1 - a) * bg) / np.maximum(a, 0.02), rgb)
    return np.concatenate([np.clip(colour, 0, 1), a], -1)


def cutouts(args):
    study, run_dir, out = args.study.resolve(), args.run.resolve(), args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    for d in sorted((run_dir / "images").iterdir()):
        image = d / f"{args.variant}.png"
        it = Item(study, d.name, image)
        rgba = cutout(it, it.gen)
        Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), "RGBA").save(out / f"{d.name}.png")
        print(d.name, f"transparent {1 - rgba[..., 3].mean():.0%}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("run", help="post-process one route of a completed study")
    p.add_argument("study", type=Path, help="completed study directory (plan.json, inputs/, jobs/)")
    p.add_argument("--route", required=True)
    p.add_argument("--palette", type=Path, required=True, help="Daggerfall .COL/.PAL file")
    p.add_argument("--out", type=Path, required=True, help="new output directory")
    p.add_argument("--seed", type=int, help="job seed, for studies with several seeds per route and item")
    p.add_argument("--variants", nargs="+", choices=list(VARIANTS), default=list(VARIANTS))
    p.add_argument("--detail", nargs="*", default=[], help="item ids for the native-resolution crop sheet")
    p.set_defaults(func=run)
    t = sub.add_parser("cutout", help="write transparent RGBA icons from one variant of a run")
    t.add_argument("study", type=Path, help="study whose inputs/ give each source sprite and placement")
    t.add_argument("run", type=Path, help="post-process run directory holding images/<id>/<variant>.png")
    t.add_argument("--variant", default="bp-broad-half", choices=list(VARIANTS))
    t.add_argument("--out", type=Path, required=True, help="directory for <id>.png (created if absent)")
    t.set_defaults(func=cutouts)
    c = sub.add_parser("compare", help="side-by-side sheets across run outputs")
    c.add_argument("--study", type=Path, required=True, help="study whose inputs/ supply the source column")
    c.add_argument("--out", type=Path, required=True, help="new output directory")
    c.add_argument("--column", action="append", required=True, metavar="LABEL=RUN_DIR:VARIANT")
    c.add_argument("--title", required=True)
    c.add_argument("--detail", nargs="*", default=[], help="item ids for the native-resolution crop sheet")
    c.add_argument("--detail-columns", nargs="+", type=int, default=[0, 1], help="column indices for the crops")
    c.add_argument("--screen-height", nargs="*", type=int, default=[],
                   help="also lay out each item at the size a 320x200 frame scaled to this height shows it")
    c.set_defaults(func=compare)
    args = parser.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
