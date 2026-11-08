#!/usr/bin/env python3
"""Make the refreshed ground cover and record it in the art set (`artReplacements.groundCover`):

- a shell texture, tileable, whose alpha is each texel's blade height: a shell layer of the stack drawn over near ground
  shows the texels at least as tall as itself, so pixel blades rise out of the ground;
- a grass tuft, drawn on three crossed quads;
- the shells' shader (`shells.wgsl`, written by hand beside them), by its hash;
- each ground archive's grass colour, as linear tints over the two, so the cover wears the climate's own grass. An
  archive with no grass (the deserts' sand, the winter sets' snow) has none, and grows no cover;
- relief for the other open ground layers: each is its refreshed ground texture itself, texel for texel, with an alpha
  that is the texel's height, so a stack of shells over it lifts the ground into pixel relief: clods with stray grass on
  dirt, pebbles on rock, ripples and plates on sand, drifts on snow, tall grass on a swamp's lush ground. Each kind sets
  how tall it stands and what in the texture rises: its lighter texels, its green ones and its broad clumps.

Both images are greyscale, so the tint alone colours them. The textures are made in code from a fixed seed; run it again
after changing them, or after the refreshed grass surfaces change.

    groundcover.py
"""
import hashlib, json
from pathlib import Path

import numpy as np
from PIL import Image

REPOSITORY = Path(__file__).resolve().parents[3]
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/media/refreshed/groundcover"
PAYLOAD = CONTENT / "worldrpg/payloads/daggerfall.art-replacements.json"
TERRAIN = "worldrpg/media/refreshed/terrain"

SHELL_SIZE, SHELL_TEXELS_PER_METRE = 96, 24
TUFT_SIZE = 32
# The ground archives that grow grass, and their grass record: the temperate, mountain and swamp summers.
GRASS = {102: 2, 302: 2, 402: 2}
# The shells' lower layers are darker (their brightness rises from about .55 to 1), so the tint aims the stack's
# average, not the texture's, at the grass.
SHELL_SHADE = .8

# Each relief layer's kind: how tall it stands (a share of the shell stack), how much its lighter texels rise, how much its
# broad clumps (of this many texels) do, what share of it lies flat, and how tall its green texels stand.
KINDS = {
    "clods": dict(cap=.5, detail=.8, clump=.5, cell=12, bare=.3, green=.6),
    "stones": dict(cap=.4, detail=1.1, clump=.15, cell=12, bare=.35, green=0),
    "sand": dict(cap=.6, detail=.45, clump=.9, cell=32, bare=.3, green=0),
    "snow": dict(cap=.6, detail=.25, clump=.9, cell=32, bare=.2, green=.8),
    "lush": dict(cap=1, detail=.7, clump=.5, cell=10, bare=.1, green=0),
}
# The open ground layers that grow relief, by ground archive and record (DaggerfallGroundLayers.Records): each climate's
# dirt and rock, the desert's three sands, the snow its winter wears (a winter set's grass and dirt alike), and the swamp's
# lush ground. Grass grows the shells
# above, and a location's paving and ruts and the water grow none.
RELIEF = {(102, 1): "clods", (302, 1): "clods", (402, 1): "clods",
          (102, 3): "stones", (302, 3): "stones",
          (2, 1): "sand", (2, 2): "sand", (2, 3): "sand",
          (103, 2): "snow", (303, 2): "snow", (403, 2): "snow", (103, 1): "snow", (303, 1): "snow", (403, 1): "snow",
          (402, 3): "lush"}

# How each kind of relief is drawn: how far its top sways in the wind, as a share of the shells' sway (tall grass bends,
# snow's loose top drifts downwind); the linear colour its lowest layer is shaded to, rising to its own at the top (snow's
# hollows go cold blue, other ground's darken); what share of its top texels glint in the light as the viewer moves (snow
# catches the sun); and whether lying snow buries it (all but snow).
LOOKS = {
    "ground": {"sway": 0, "shade": [.6, .6, .6], "glints": 0, "sinksUnderSnow": True},
    "lush": {"sway": 1, "shade": [.6, .6, .6], "glints": 0, "sinksUnderSnow": True},
    "snow": {"sway": .5, "shade": [.52, .62, .86], "glints": .012, "sinksUnderSnow": False},
}

rng = np.random.default_rng(10647)


def tileable_noise(size, cell):
    """Smooth value noise that wraps at the edges."""
    grid = rng.random((size // cell, size // cell))
    y, x = np.mgrid[:size, :size] / cell
    x0, y0 = x.astype(int), y.astype(int)
    fx, fy = x - x0, y - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    n = grid.shape[0]
    a, b = grid[y0 % n, x0 % n], grid[y0 % n, (x0 + 1) % n]
    c, d = grid[(y0 + 1) % n, x0 % n], grid[(y0 + 1) % n, (x0 + 1) % n]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def shell():
    """Clumps of blades with bare texels between them, each texel's lightness rising with its height."""
    size = SHELL_SIZE
    clumps = tileable_noise(size, 12) * .6 + tileable_noise(size, 6) * .4
    height = np.clip(clumps * .9 + rng.random((size, size)) * .55 - .25, 0, 1)
    height[rng.random((size, size)) < .18] = 0
    light = np.clip(.62 + height * .3 + rng.normal(0, .06, (size, size)), .5, 1)
    rgba = np.zeros((size, size, 4), np.uint8)
    rgba[..., :3] = (light * 255)[..., None].astype(np.uint8)
    rgba[..., 3] = (height * 255).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def tuft():
    """Upright blades from a dense foot as light as the ground under it, lightening a little toward the tips: a darker
    root vanishes into the ground and leaves the tips hanging over it."""
    size = TUFT_SIZE
    rgba = np.zeros((size, size, 4), np.uint8)
    for _ in range(22):
        x, lean = size / 2 + rng.normal(0, 3.2), rng.normal(0, .18)
        tall, wide = int(rng.uniform(.45, .95) * (size - 2)), 2 if rng.random() < .3 else 1
        for step in range(tall):
            share = step / tall
            px, py = int(round(x + lean * step * share * 1.6)), size - 1 - step
            light = int(255 * min(1, .8 + share * .2 + rng.normal(0, .04)))
            for w in range(wide + (share < .2) if share < .7 else 1):
                if 0 <= px + w < size:
                    rgba[py, px + w] = (light, light, light, 255)
    return Image.fromarray(rgba, "RGBA")


def blur(values, sigma):
    """A Gaussian blur that wraps, as the texture does."""
    from scipy import ndimage
    return ndimage.gaussian_filter(values, sigma, mode="wrap")


def relief(ground, kind):
    """A ground texture as relief: its own colours, and as alpha each texel's height by its kind."""
    k = KINDS[kind]
    rgba = np.asarray(ground.convert("RGBA"), np.float64) / 255
    rgb = rgba[..., :3]
    light = linear(rgb) @ [.2126, .7152, .0722]
    detail = light - blur(light, 3)
    detail = np.clip(.5 + detail / (detail.std() * 4 + 1e-6), 0, 1)
    size = rgba.shape[0]
    clumps = tileable_noise(size, k["cell"]) * .7 + tileable_noise(size, max(2, k["cell"] // 3)) * .3
    height = np.clip(k["detail"] * detail + k["clump"] * clumps - .35, 0, 1)
    height[height < np.quantile(height, k["bare"])] = 0
    height *= k["cap"]
    if k["green"]:
        green = (rgb[..., 1] > rgb[..., 0] * 1.3) & (rgb[..., 1] > rgb[..., 2] * 1.3)
        height[green] = np.maximum(height[green], k["green"] * (.6 + .4 * rng.random(green.sum())))
    out = np.asarray(ground.convert("RGBA")).copy()
    out[..., 3] = (height * 255).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def linear(srgb):
    return np.where(srgb <= .04045, srgb / 12.92, ((srgb + .055) / 1.055) ** 2.4)


def mean_linear(image):
    """The linear mean of an image's colour where it is drawn."""
    rgba = np.asarray(image.convert("RGBA"), np.float64) / 255
    drawn = rgba[..., 3] > 0
    return linear(rgba[..., :3][drawn]).mean(0)


def publish(name, image):
    path = CONTENT / MEDIA / f"{name}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)
    data = path.read_bytes()
    return {"path": f"{MEDIA}/{name}.png", "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest(),
            "width": image.width, "height": image.height}


def hashed(name):
    data = (CONTENT / MEDIA / name).read_bytes()
    return {"path": f"{MEDIA}/{name}", "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def relief_entry(archive, record, kind):
    """A relief texture's file, its tallest texel as a share of the stack, and by its kind how it is drawn (LOOKS)."""
    image = relief(Image.open(CONTENT / TERRAIN / f"texture-{archive:03d}-{record}.png"), kind)
    return publish(f"relief-{archive:03d}-{record}", image) | {
        "kind": kind, "height": round(np.asarray(image)[..., 3].max() / 255, 4)} | LOOKS.get(kind, LOOKS["ground"])


def main():
    shell_image, tuft_image = shell(), tuft()
    shell_mean, tuft_mean = mean_linear(shell_image).mean() * SHELL_SHADE, mean_linear(tuft_image).mean()
    grass = {}
    for archive, record in GRASS.items():
        ground = mean_linear(Image.open(CONTENT / TERRAIN / f"texture-{archive:03d}-{record}.png"))
        grass[str(archive)] = {"shells": [round(float(v), 4) for v in np.clip(ground / shell_mean, 0, 1)],
                               "tufts": [round(float(v), 4) for v in np.clip(ground / tuft_mean, 0, 1)]}
    payload = json.loads(PAYLOAD.read_text())
    payload["artReplacements"]["groundCover"] = {
        "shell": publish("shell", shell_image) | {"texelsPerMetre": SHELL_TEXELS_PER_METRE},
        "tuft": publish("tuft", tuft_image),
        "shader": hashed("shells.wgsl"),
        "grass": grass,
        "relief": {f"{archive}-{record}": relief_entry(archive, record, kind) for (archive, record), kind in RELIEF.items()},
    }
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    print(json.dumps(payload["artReplacements"]["groundCover"], indent=1))


if __name__ == "__main__":
    main()
