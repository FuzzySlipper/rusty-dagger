#!/usr/bin/env python3
"""Make the refreshed ground cover and record it in the art set (`artReplacements.groundCover`):

- a shell texture, tileable, whose alpha is each texel's blade height: a shell layer of the stack drawn over near ground
  shows the texels at least as tall as itself, so pixel blades rise out of the ground;
- a grass tuft, drawn on three crossed quads;
- the shells' shader (`shells.wgsl`, written by hand beside them), by its hash;
- each ground archive's grass colour, as linear tints over the two, so the cover wears the climate's own grass. An
  archive with no grass (the deserts' sand, the winter sets' snow) has none, and grows no cover.

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
    """Upright blades from a narrow foot, darker at the root."""
    size = TUFT_SIZE
    rgba = np.zeros((size, size, 4), np.uint8)
    for _ in range(15):
        x, lean = size / 2 + rng.normal(0, 3.2), rng.normal(0, .18)
        tall, wide = int(rng.uniform(.45, .95) * (size - 2)), 2 if rng.random() < .3 else 1
        for step in range(tall):
            share = step / tall
            px, py = int(round(x + lean * step * share * 1.6)), size - 1 - step
            light = int(255 * min(1, .55 + share * .45 + rng.normal(0, .04)))
            for w in range(wide if share < .7 else 1):
                if 0 <= px + w < size:
                    rgba[py, px + w] = (light, light, light, 255)
    return Image.fromarray(rgba, "RGBA")


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


def main():
    shell_image, tuft_image = shell(), tuft()
    shell_mean, tuft_mean = mean_linear(shell_image).mean() * SHELL_SHADE, mean_linear(tuft_image).mean()
    grass = {}
    for archive, record in GRASS.items():
        ground = mean_linear(Image.open(CONTENT / TERRAIN / f"texture-{archive:03d}-{record}.png"))
        # The tufts stand a little proud of the ground, so they take its colour slightly lighter.
        grass[str(archive)] = {"shells": [round(float(v), 4) for v in np.clip(ground / shell_mean, 0, 1)],
                               "tufts": [round(float(v), 4) for v in np.clip(ground * 1.15 / tuft_mean, 0, 1)]}
    payload = json.loads(PAYLOAD.read_text())
    payload["artReplacements"]["groundCover"] = {
        "shell": publish("shell", shell_image) | {"texelsPerMetre": SHELL_TEXELS_PER_METRE},
        "tuft": publish("tuft", tuft_image),
        "shader": hashed("shells.wgsl"),
        "grass": grass,
    }
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    print(json.dumps(payload["artReplacements"]["groundCover"], indent=1))


if __name__ == "__main__":
    main()
