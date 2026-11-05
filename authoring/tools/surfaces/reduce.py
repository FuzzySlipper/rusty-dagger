"""Surface reduction (rusty-dagger #10497): a generated surface brought back to Daggerfall's look.

  crunch    Lanczos to twice the classic record's size, its colours matched to the classic in CIE Lab (mean and spread),
            a strong unsharp mask at that size, then every pixel mapped to up to 32 colours of Daggerfall's world-texture
            palette (ART_PAL.COL) chosen for this image, without dithering: crisp up-res pixel art in a PC-era palette.
  winter    a winter record as its refreshed summer with the classic winter's own snow and ice laid over, pixels doubled
            square, wherever the classic winter is brighter in every channel than the classic summer. No generation, so
            winter is the summer surface unchanged under the classic's snow. Only where the classic winter is its summer
            under snow (snowed); some winter records are another picture (a different shelf, a plain wall for a door).
  wraps     whether a classic record repeats across each axis: its opposite edges differ no more than its neighbouring
            pixels do (WRAP times), which says how it was meant to tile.
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageCms, ImageFilter

PALETTE_FILE = Path.home() / "research/daggerfall-files/ART_PAL.COL"
PALETTE = np.frombuffer(PALETTE_FILE.read_bytes()[8:8 + 768], np.uint8).reshape(256, 3).astype(np.float32)
COLOURS = 32
SCALE = 2
WRAP = 1.6
SNOW = 24
SNOWED = 0.6

_srgb, _lab = ImageCms.createProfile("sRGB"), ImageCms.createProfile("LAB")
_to_lab = ImageCms.buildTransformFromOpenProfiles(_srgb, _lab, "RGB", "LAB")
_to_rgb = ImageCms.buildTransformFromOpenProfiles(_lab, _srgb, "LAB", "RGB")


def lab(image):
    """L in 0..255 and a, b signed, which PIL's LAB mode stores as signed bytes."""
    raw = np.asarray(ImageCms.applyTransform(image.convert("RGB"), _to_lab))
    return np.dstack([raw[..., 0].astype(np.float32), raw[..., 1:].view(np.int8).astype(np.float32)])


def unlab(values):
    out = np.empty(values.shape, np.uint8)
    out[..., 0] = np.clip(values[..., 0], 0, 255).astype(np.uint8)
    out[..., 1:] = np.clip(values[..., 1:], -128, 127).astype(np.int8).view(np.uint8)
    return ImageCms.applyTransform(Image.fromarray(out, "LAB"), _to_rgb)


def match(image, classic):
    """The image's colours moved to the classic's: mean and spread per Lab channel."""
    values, target = lab(image), lab(classic)
    mean, spread = values.reshape(-1, 3).mean(0), values.reshape(-1, 3).std(0)
    target_mean, target_spread = target.reshape(-1, 3).mean(0), target.reshape(-1, 3).std(0)
    return unlab((values - mean) / np.maximum(spread, 1e-3) * target_spread + target_mean)


def subset_palette(image, colours=COLOURS):
    """Up to `colours` ART_PAL colours for this image: k-means of its pixels (fixed seed), each centre moved to its
    nearest palette colour, so the surface keeps Daggerfall's own hues and gains no stray ones."""
    pixels = np.asarray(image.convert("RGB"), np.float32).reshape(-1, 3)
    rng = np.random.default_rng(9664)
    sample = pixels[rng.choice(len(pixels), min(len(pixels), 20000), replace=False)]
    colours = min(colours, len(sample))
    centres = sample[rng.choice(len(sample), colours, replace=False)]
    for _ in range(16):
        owner = np.argmin(((sample[:, None] - centres[None]) ** 2).sum(-1), 1)
        centres = np.array([sample[owner == k].mean(0) if (owner == k).any() else centres[k] for k in range(colours)])
    return PALETTE[np.unique(np.argmin(((centres[:, None] - PALETTE[None]) ** 2).sum(-1), 1))]


def crunch(raw, classic):
    """The generated surface at twice the classic's size, in the classic's colours and an ART_PAL subset."""
    source = Image.open(raw).convert("RGB")
    size = (classic.width * SCALE, classic.height * SCALE)
    image = match(source.resize(size, Image.Resampling.LANCZOS), classic).filter(ImageFilter.UnsharpMask(radius=1, percent=140, threshold=1))
    palette = subset_palette(image)
    pixels = np.asarray(image, np.float32)
    nearest = np.argmin(((pixels.reshape(-1, 3)[:, None] - palette[None]) ** 2).sum(-1), 1)
    return Image.fromarray(palette[nearest].reshape(pixels.shape).astype(np.uint8))


def crunch_sprites(frames, size):
    """Transparent art crunched for a world billboard: each RGBA frame at `size`, a strong unsharp mask, hard edges, and one
    ART_PAL subset across the frames, so an animation doesn't flicker between palettes."""
    colour, solid = [], []
    for frame in frames:
        channels = np.asarray(frame.convert("RGBA"), np.float32) / 255
        channels[..., :3] *= channels[..., 3:]
        small = np.dstack([np.asarray(Image.fromarray(channels[..., i]).resize(size, Image.Resampling.LANCZOS)) for i in range(4)])
        rgb = np.clip(small[..., :3] / np.maximum(small[..., 3:], 0.02), 0, 1)
        colour.append(np.asarray(Image.fromarray((rgb * 255).astype(np.uint8)).filter(ImageFilter.UnsharpMask(radius=1, percent=140, threshold=1)), np.float32))
        solid.append(small[..., 3] > 0.5)
    palette = subset_palette(Image.fromarray(np.concatenate([c[s] for c, s in zip(colour, solid)])[None].astype(np.uint8)))
    out = []
    for pixels, mask in zip(colour, solid):
        image = np.zeros((*mask.shape, 4), np.uint8)
        image[mask, :3] = palette[np.argmin(((pixels[mask][:, None] - palette[None]) ** 2).sum(-1), 1)]
        image[mask, 3] = 255
        out.append(Image.fromarray(image, "RGBA"))
    return out


def winter(summer_refreshed, summer_classic, winter_classic):
    """The refreshed summer with the classic winter's snow and ice laid over it, pixels doubled square."""
    s = np.asarray(summer_classic.convert("RGB"), np.int16)
    w = np.asarray(winter_classic.convert("RGB"), np.int16)
    snow = (w.min(-1) - s.min(-1)) > SNOW
    size = summer_refreshed.size
    overlay = np.asarray(winter_classic.convert("RGB").resize(size, Image.Resampling.NEAREST))
    mask = np.asarray(Image.fromarray(snow.astype(np.uint8) * 255).resize(size, Image.Resampling.NEAREST)) > 0
    out = np.asarray(summer_refreshed.convert("RGB"), np.uint8).copy()
    out[mask] = overlay[mask]
    return Image.fromarray(out)


def snowed(summer_classic, winter_classic):
    """Whether a classic winter is its summer under snow: the same size, and at least SNOWED of the pixels it does not
    snow over within SNOW of the summer's (snow variants measure 0.6 and up, redrawn records 0.58 and below)."""
    if summer_classic.size != winter_classic.size:
        return False
    s = np.asarray(summer_classic.convert("RGB"), np.int16)
    w = np.asarray(winter_classic.convert("RGB"), np.int16)
    bare = (w.min(-1) - s.min(-1)) <= SNOW
    return not bare.any() or (np.abs(w - s).max(-1)[bare] <= SNOW).mean() >= SNOWED


def wraps(classic):
    """(x, y): whether the classic repeats across each axis."""
    a = np.asarray(classic.convert("L"), np.float32)
    inner_x = np.mean(np.abs(np.diff(a, axis=1))) + 1
    inner_y = np.mean(np.abs(np.diff(a, axis=0))) + 1
    return (np.mean(np.abs(a[:, 0] - a[:, -1])) <= WRAP * inner_x, np.mean(np.abs(a[0] - a[-1])) <= WRAP * inner_y)


FRAME = 2.0


def unframe(classic):
    """The classic with a bevel frame removed: where an axis's outer two lines on each side differ from the next line in
    by more than FRAME times the neighbouring-pixel difference, and one side is lighter than the middle and the other
    darker (a bevel lit from one side), those four lines are interpolated across the wrap. A joint the same on both sides
    (dark or light grout) is the edge of the record's own slab and stays."""
    a = np.asarray(classic.convert("RGB"), np.float32)
    for axis in (0, 1):
        b = np.moveaxis(a, axis, 0)
        n = b.shape[0]
        if n < 8:
            continue
        inner = np.mean(np.abs(np.diff(b[2:n - 2], axis=0))) + 1
        edge = max(np.mean(np.abs(b[0] - b[2])), np.mean(np.abs(b[1] - b[2])), np.mean(np.abs(b[n - 1] - b[n - 3])), np.mean(np.abs(b[n - 2] - b[n - 3])))
        middle = b[2:n - 2].mean()
        bevel = (b[:2].mean() - middle) * (b[n - 2:].mean() - middle) < 0
        if edge > FRAME * inner and bevel:
            lo, hi = b[2].copy(), b[n - 3].copy()
            for k, i in enumerate((n - 2, n - 1, 0, 1), 1):
                b[i] = hi + (lo - hi) * k / 5
        a = np.moveaxis(b, 0, axis)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8))
