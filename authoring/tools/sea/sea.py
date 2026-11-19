#!/usr/bin/env python3
"""sea: the sea's ripple and foam maps, and the art pack's `sea` section that names them with the sea's shader.

Writes into content/worldrpg/packs/daggerfall.art-replacements/media/sea/:

  ripples.png  a tiling map of small wind ripples, crests across +x (the shader turns +x downwind). xy hold each texel's
               tilt (its slope east and south) as 0.5 + tilt / 2, and alpha the tilt's squared length. A mip's alpha less
               its xy's squared length is then the tilt the ripples it averages away still have, which widens the moons'
               glints as the ripples shrink below a pixel.
  foam.png     a tiling mottle (red, green and blue alike), evenly spread from 0 to 1, so a threshold of t foams 1 - t
               of the water.

and states artReplacements.sea in payload.json: the shader (sea.wgsl, written by hand) and the two maps, each with its
length and digest. Run it after editing sea.wgsl too. Deterministic: the same maps every run.

    authoring/tools/sea/sea.py
"""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
PACK = ROOT / 'content/worldrpg/packs/daggerfall.art-replacements'
MEDIA = PACK / 'media/sea'
SIZE = 256
# The ripples' root mean square tilt in the map: the shader's ripple tilt scales it.
RIPPLE_RMS = 0.35
# Ripple wave numbers, in repeats of the map: the longest a third of it, the shortest four texels.
RIPPLE_LEAST, RIPPLE_MOST = 3, SIZE // 4
# How closely the ripples run along the wind: their amplitude by cos(angle off it) to this power.
RIPPLE_SPREAD = 2
FOAM_LEAST, FOAM_MOST = 2, 48


def spectrum(rng, least, most, amplitude):
    """A periodic field from random phases over integer wave numbers between least and most, by amplitude(k, angle)."""
    k = np.fft.fftfreq(SIZE, 1 / SIZE)
    kx, ky = np.meshgrid(k, k)
    length = np.hypot(kx, ky)
    angle = np.arctan2(ky, kx)
    shape = np.where((length >= least) & (length <= most), amplitude(np.maximum(length, 1), angle), 0)
    phases = np.exp(2j * np.pi * rng.random((SIZE, SIZE)))
    return shape * phases, kx, ky


def ripples(rng):
    # Capillary ripples' heights fall as k^-2; their slopes as k^-1. Both directions along the wind's line ripple.
    field, kx, ky = spectrum(rng, RIPPLE_LEAST, RIPPLE_MOST, lambda k, a: k ** -2.0 * np.abs(np.cos(a)) ** RIPPLE_SPREAD)
    tilt = np.stack([np.real(np.fft.ifft2(2j * np.pi * kx * field)), np.real(np.fft.ifft2(2j * np.pi * ky * field))], -1)
    tilt *= RIPPLE_RMS / np.sqrt(np.mean(np.sum(tilt ** 2, -1)))
    tilt = np.clip(tilt, -1, 1)
    xy = np.round((0.5 + tilt / 2) * 255)
    a = np.round(np.minimum(np.sum(tilt ** 2, -1), 1) * 255)
    return np.dstack([xy, np.full((SIZE, SIZE), 128), a]).astype(np.uint8)


def foam(rng):
    field, _, _ = spectrum(rng, FOAM_LEAST, FOAM_MOST, lambda k, a: k ** -2.4)
    value = np.real(np.fft.ifft2(field))
    # Evenly spread: each texel's rank among them all.
    even = np.empty(value.size)
    even[np.argsort(value, axis=None)] = np.linspace(0, 1, value.size)
    level = np.round(even.reshape(value.shape) * 255).astype(np.uint8)
    return np.dstack([level, level, level, np.full_like(level, 255)])


def entry(path):
    data = path.read_bytes()
    return {'path': path.relative_to(ROOT / 'content').as_posix(), 'byteLength': len(data), 'sha256': hashlib.sha256(data).hexdigest()}


def main():
    rng = np.random.default_rng(10910)
    MEDIA.mkdir(parents=True, exist_ok=True)
    Image.fromarray(ripples(rng), 'RGBA').save(MEDIA / 'ripples.png', optimize=True)
    Image.fromarray(foam(rng), 'RGBA').save(MEDIA / 'foam.png', optimize=True)
    payload_path = PACK / 'payload.json'
    payload = json.loads(payload_path.read_text())
    payload['artReplacements']['sea'] = {
        'shader': entry(MEDIA / 'sea.wgsl'), 'ripples': entry(MEDIA / 'ripples.png'), 'foam': entry(MEDIA / 'foam.png')}
    payload_path.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n')
    print('wrote ripples.png and foam.png, and stated artReplacements.sea')


if __name__ == '__main__':
    main()
