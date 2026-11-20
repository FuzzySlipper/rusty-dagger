#!/usr/bin/env python3
"""travel_map: the 3D travel map's art (#10950), and the art pack's `travelMap` section that names it.

Writes into content/worldrpg/packs/daggerfall.art-replacements/media/travel-map/:

  rain.png          a falling drop's streak, white, its alpha the streak, in four frames side by side from fully seen to
                    faintest: a drop fades in and out through them.
  snow.png          a snowflake, a soft white disc, in four frames as the rain's.
  detail.png        a tiling mottle of the land seen from a few kilometres up (broad swells, patches of scrub and copse,
                    a faint fine texture), red, green and blue alike, read as data: it multiplies the climate colours the
                    land is painted in, 1 at its lightest, so it only darkens and never washes the colours out. Its mips
                    average to a flat tone, so it holds from the nearest zoom out without the grain of the walked
                    ground's textures.
  detail-normal.png the same land's small relief (hillocks and gullies) as a tangent-space normal map, x east, y north.
  places/KIND.rstatmsh  a small model for each kind of place (places.py), an Engine static mesh in house widths, standing
                    on its origin, a material slot to each of its colours.

and states artReplacements.travelMap in payload.json, each file with its length and digest and each model's slot colours
in sRGB. Deterministic: the same art every run.

    authoring/tools/travel-map/travel_map.py
"""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

import places as place_models

ROOT = Path(__file__).resolve().parents[3]
PACK = ROOT / 'content/worldrpg/packs/daggerfall.art-replacements'
MEDIA = PACK / 'media/travel-map'
DETAIL = 1024
# The place kinds places.py models, by the names the travelMap section gives them.
KINDS = ['city', 'town', 'village', 'farm', 'wealthy-home', 'poor-home', 'tavern', 'temple', 'cult', 'coven',
         'labyrinth', 'keep', 'ruin', 'graveyard', 'ship']


def band(rng, size, least, most, slope):
    """A periodic field over integer wave numbers between least and most, its amplitude falling as k^slope, scaled to unit spread."""
    k = np.fft.fftfreq(size, 1 / size)
    kx, ky = np.meshgrid(k, k)
    length = np.hypot(kx, ky)
    shape = np.where((length >= least) & (length <= most), np.maximum(length, 1) ** slope, 0)
    value = np.real(np.fft.ifft2(shape * np.exp(2j * np.pi * rng.random((size, size)))))
    return value / value.std()


def smooth(value, low, high):
    t = np.clip((value - low) / (high - low), 0, 1)
    return t * t * (3 - 2 * t)


def detail(rng):
    """The mottle and its relief: broad swells, woods in soft-edged patches with their canopy's texture, open ground between."""
    swell = band(rng, DETAIL, 1, 6, -1.6)
    patches = band(rng, DETAIL, 3, 18, -1.3)
    edges = band(rng, DETAIL, 18, 60, -1.0)
    canopy = band(rng, DETAIL, 60, 160, -0.6)
    meadow = band(rng, DETAIL, 20, 70, -0.8)
    # Woods: about a fifth of the land in patches a tenth of the tile across, their edges ragged, darker and lumpy.
    woods = smooth(patches + 0.35 * edges, 0.55, 1.0)
    shade = 1 - (0.06 * (1 - swell / 3)) - (0.025 * (1 + meadow / 3)) - (woods * (0.13 + 0.035 * canopy))
    shade = np.clip(shade / shade.max(), 0, 1)
    height = (2.5 * swell) + (0.8 * woods) + (0.12 * woods * canopy) + (0.1 * meadow)
    level = np.round(shade * 255).astype(np.uint8)
    mottle = np.dstack([level, level, level, np.full_like(level, 255)])
    # The relief is broad, so half the texels carry it: slopes in its texels, wrapped, as x (east) and y (north, up the image).
    height = height[::2, ::2]
    east = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) / 2
    north = (np.roll(height, 1, 0) - np.roll(height, -1, 0)) / 2
    normal = np.dstack([-east, -north, np.full_like(height, 0.25)])
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    packed = np.round((normal * 0.5 + 0.5) * 255).astype(np.uint8)
    return mottle, np.dstack([packed, np.full(height.shape, 255, np.uint8)])


# How much of a drop each fade frame shows, from fully seen to faintest.
FADES = [1.0, 0.62, 0.34, 0.14]


def frames(alpha):
    """A drop's frames side by side, each its alpha scaled by a fade."""
    white = np.full(alpha.shape, 255, np.uint8)
    return np.hstack([np.dstack([white, white, white, np.round(alpha * fade).astype(np.uint8)]) for fade in FADES])


def rain():
    """A streak down the middle of a 64-texel square: soft across, fading in from its top."""
    size = 64
    y, x = np.mgrid[0:size, 0:size].astype(float) + 0.5
    across = np.exp(-(((x - size / 2) / 1.1) ** 2))
    along = np.clip((y - 8) / 40, 0, 1) * np.clip((58 - y) / 4, 0, 1)
    return frames(across * along * 255)


def snow():
    """A soft round flake in a 32-texel square."""
    size = 32
    y, x = np.mgrid[0:size, 0:size].astype(float) + 0.5
    distance = np.hypot(x - size / 2, y - size / 2) / (size / 2)
    return frames(np.clip(1 - distance, 0, 1) ** 1.4 * 255)


def entry(path):
    data = path.read_bytes()
    return {'path': path.relative_to(ROOT / 'content').as_posix(), 'byteLength': len(data), 'sha256': hashlib.sha256(data).hexdigest()}


def main():
    rng = np.random.default_rng(10950)
    MEDIA.mkdir(parents=True, exist_ok=True)
    mottle, normal = detail(rng)
    Image.fromarray(mottle, 'RGBA').save(MEDIA / 'detail.png', optimize=True)
    Image.fromarray(normal, 'RGBA').save(MEDIA / 'detail-normal.png', optimize=True)
    Image.fromarray(rain(), 'RGBA').save(MEDIA / 'rain.png', optimize=True)
    Image.fromarray(snow(), 'RGBA').save(MEDIA / 'snow.png', optimize=True)
    places = MEDIA / 'places'
    for old in places.glob('*.glb'):
        old.unlink()
    slots = place_models.write(places)
    payload_path = PACK / 'payload.json'
    payload = json.loads(payload_path.read_text())
    payload['artReplacements']['travelMap'] = {
        'rain': entry(MEDIA / 'rain.png'), 'snow': entry(MEDIA / 'snow.png'),
        'detail': entry(MEDIA / 'detail.png'), 'detailNormal': entry(MEDIA / 'detail-normal.png'),
        'places': {kind: {**entry(places / f'{kind}.rstatmsh'), 'slots': slots[kind]} for kind in KINDS}}
    payload_path.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n')
    print('wrote the travel map art and stated artReplacements.travelMap')


if __name__ == '__main__':
    main()
