#!/usr/bin/env python3
"""title-scene: the animated title screen's layers, from five pictures, into the art pack.

  town     the night painting of the town through the window (Wayrest from a tower, restyled from an Engine capture)
  clear    the same painting with its clouds removed (an image edit), whose sky is the base the clouds drift over
  window   the window arch and sill, its opening flat pure green
  capture  the Engine frame the town painting was restyled from, at the same framing: the lit windows and street lamps
           in it place the town's lights
  logo     the game's logo, cut out on a transparent ground (an image edit repainting the classic one, never the classic
           one itself)

Writes into content/worldrpg/packs/daggerfall.art-replacements/media/ui/title-scene/:

  town.webp     the town with the clear sky: the title's refreshed replacement image, under every other layer
  clouds.webp   the clouds alone (alpha where the cloudy sky is brighter than the clear one), widened by mirroring for drift
  sea.webp      the bay's water as a vertical strip of frames: a periodic sideways shimmer, larger toward the viewer, its
                moon glints twinkling, so the strip loops exactly
  window.webp   the window with its opening (and every green pocket, such as the lantern's ring) keyed out
  logo.webp     the logo with a soft dark halo, so it reads over the sky; the page draws it in place of the game's name

and states them in payload.json: the `screen.title` image entry points at town.webp, and `titleScene` gives the layers'
sizes and places, the window's opening and flames, and the lights, in town pixels, and the logo's place in window pixels. The page animates them; nothing here
is a video.

    authoring/tools/ui/title-scene.py --town T.png --clear C.png --window W.png --capture CAPTURE.png --logo LOGO.png
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[3]
PACK = ROOT / 'content/worldrpg/packs/daggerfall.art-replacements'
MEDIA = 'media/ui/title-scene'
RIDGE = 300            # town rows above which the painting is sky (the far mountains' crest)
SEA_BAND = (350, 462)  # town rows the bay's water lies within
SEA_FRAMES, SEA_FPS = 48, 12
DRIFT, CYCLE = 240, 60  # cloud drift in town pixels over one cycle's seconds
LOGO_TOP, LOGO_WIDTH = 213, 693  # window pixels: centred under the moon, the arch of its letters framing the moonlit bay
LOGO_HALO = 14  # halo pixels at the logo's own size


def rgb(path, size=None):
    im = Image.open(path).convert('RGB')
    return np.asarray(im.resize(size, Image.LANCZOS) if size else im).astype(np.float32)


def write(array, name, mode, quality=86):
    path = PACK / MEDIA / name
    Image.fromarray(np.clip(array, 0, 255).astype(np.uint8), mode).save(path, quality=quality, method=6)
    data = path.read_bytes()
    return {'path': f'worldrpg/packs/daggerfall.art-replacements/{MEDIA}/{name}', 'byteLength': len(data),
            'sha256': hashlib.sha256(data).hexdigest()}


def town_layers(night, clear):
    H, W = night.shape[:2]
    sky = np.clip((RIDGE - np.arange(H)) / 24, 0, 1)[:, None] * np.ones((1, W))
    town = night * (1 - sky[..., None]) + clear * sky[..., None]
    lift = night.mean(axis=2) - clear.mean(axis=2)
    alpha = ndimage.gaussian_filter(np.clip((lift - 1.5) / 22, 0, 1) ** .8 * sky, 1.2)
    # The moon stays in the town layer: the clouds pass in front of it, never carry it.
    glow = ndimage.gaussian_filter(clear.mean(axis=2) * sky, 6)
    my, mx = np.unravel_index(np.argmax(glow), glow.shape)
    yy, xx = np.ogrid[:H, :W]
    alpha *= np.clip((np.hypot(yy - my, xx - mx) - 40) / 20, 0, 1)
    clouds = np.dstack([night, alpha * 255])[:RIDGE + 20]
    clouds = np.concatenate([clouds, clouds[:, -DRIFT:][:, ::-1]], axis=1)
    return town, clouds


def sea_strip(night):
    H, W = night.shape[:2]
    r, g, b = night[..., 0], night[..., 1], night[..., 2]
    band = np.zeros((H, W), bool)
    band[SEA_BAND[0]:SEA_BAND[1], W * 3 // 10:] = True
    water = ndimage.binary_opening(ndimage.binary_closing(band & (b > r + 18) & (b > g + 4), iterations=2), iterations=1)
    lab, n = ndimage.label(water)
    water = ndimage.binary_fill_holes(lab == max(range(1, n + 1), key=lambda i: (lab == i).sum()))
    ys, xs = np.nonzero(water)
    y0, y1, x0, x1 = ys.min() - 2, ys.max() + 3, xs.min() - 2, min(W, xs.max() + 3)
    crop, alpha = night[y0:y1, x0:x1], ndimage.gaussian_filter(water.astype(np.float32), 1.5)[y0:y1, x0:x1]
    h, w = crop.shape[:2]
    lum = crop.mean(axis=2)
    glint = np.clip((lum - np.percentile(lum[alpha > .5], 80)) / 60, 0, 1)
    phase = np.random.default_rng(3).uniform(0, 2 * math.pi, (h, w))
    gy, gx = np.mgrid[0:h, 0:w].astype(np.float32)
    near = (gy / h) ** 1.5 + .25
    frames = []
    for i in range(SEA_FRAMES):
        t = i / SEA_FRAMES
        dx = (2.2 * np.sin(2 * math.pi * (gy / 7.0 - t)) + 1.3 * np.sin(2 * math.pi * (gx / 53.0 + gy / 11.0 + 2 * t))) * near
        dy = .6 * np.sin(2 * math.pi * (gx / 37.0 - t)) * near
        at = [np.clip(gy + dy, 0, h - 1), np.clip(gx + dx, 0, w - 1)]
        warped = np.stack([ndimage.map_coordinates(crop[..., c], at, order=1, mode='nearest') for c in range(3)], -1)
        twinkle = .75 + .45 * np.sin(2 * math.pi * 2 * t + phase)
        frames.append(np.dstack([warped * (1 + glint[..., None] * (twinkle[..., None] - .75) * 1.2), alpha * 255]))
    return np.concatenate(frames, axis=0), (int(x0), int(y0), int(w), int(h))


def window_layer(window):
    r, g, b = window[..., 0], window[..., 1], window[..., 2]
    spill = g - np.maximum(r, b)
    alpha = 1 - np.clip((spill - 30) / 80, 0, 1)
    lab, n = ndimage.label(alpha < .5)
    opening = lab == max(range(1, n + 1), key=lambda i: (lab == i).sum())
    alpha = np.where(ndimage.binary_dilation((alpha < .5) & (spill > 60), iterations=2), alpha, 1.0)
    out = window.copy()
    out[..., 1] -= np.clip(spill, 0, None) * .95
    ys, xs = np.nonzero(opening)
    box = {'x': int(xs.min()), 'y': int(ys.min()), 'width': int(xs.max() + 1 - xs.min()), 'height': int(ys.max() + 1 - ys.min())}
    # Flames: the white-hot cores below the opening's top third, the larger first.
    hot = (r > 245) & (g > 220) & (b > 150) & ~opening & (np.arange(window.shape[0])[:, None] > window.shape[0] // 3)
    lab, n = ndimage.label(ndimage.binary_dilation(hot, iterations=3))
    found = sorted(((lab == i).sum(), sl) for i, sl in enumerate(ndimage.find_objects(lab), 1))[::-1][:2]
    flames = [{'x': (sl[1].start + sl[1].stop) // 2, 'y': (sl[0].start + sl[0].stop) // 2, 'radius': round(window.shape[1] * .13)}
              for _, sl in found]
    return np.dstack([out, alpha * 255]), box, flames


def logo_layer(logo, window_width):
    """The logo over a soft dark halo of its own shape, and its place in the window, centred."""
    pad = LOGO_HALO * 2
    alpha = np.pad(logo[..., 3] / 255, pad)
    halo = np.clip(ndimage.gaussian_filter(alpha, LOGO_HALO) * 1.6, 0, 1) * .6
    rgb = np.pad(logo[..., :3], ((pad, pad), (pad, pad), (0, 0)))
    out_alpha = alpha + halo * (1 - alpha)
    out = rgb * (alpha / np.maximum(out_alpha, 1e-6))[..., None]
    h, w = out.shape[:2]
    width = round(LOGO_WIDTH * w / logo.shape[1])
    place = {'x': round(window_width / 2 - width / 2), 'y': LOGO_TOP - round(pad * LOGO_WIDTH / logo.shape[1]),
             'width': width, 'height': round(width * h / w)}
    return np.dstack([out, out_alpha * 255]), place


def lights(capture, night):
    """Every lit window and street lamp of the capture, placed on the painting's own light within 7 px."""
    H, W = capture.shape[:2]
    PH, PW = night.shape[:2]
    r, g, b = capture[..., 0], capture[..., 1], capture[..., 2]
    below = np.arange(H)[:, None] > H * .3
    warmth = ndimage.gaussian_filter(np.clip(night[..., 0] - night[..., 2], 0, None) * night.mean(axis=2) / 255, 1.0)
    points = []
    def place(x, y, lamp):
        px, py = x / W * PW, y / H * PH
        x0, y0 = int(max(0, px - 7)), int(max(0, py - 7))
        window = warmth[y0:int(py + 8), x0:int(px + 8)]
        if window.size == 0: return
        iy, ix = np.unravel_index(np.argmax(window), window.shape)
        points.append({'x': x0 + int(ix), 'y': y0 + int(iy), 'lamp': lamp})
    lab, _ = ndimage.label((r > 225) & (g > 195) & (b > 100) & below)
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        ys, xs = np.nonzero(lab[sl] == i)
        place(xs.mean() + sl[1].start, ys.mean() + sl[0].start, True)
    lab, _ = ndimage.label(ndimage.binary_dilation((r > 150) & (r - b > 60) & (g > 90) & ~((g > 195) & (b > 100)), iterations=6))
    for sl in ndimage.find_objects(lab):
        width, y = sl[1].stop - sl[1].start, (sl[0].start + sl[0].stop) / 2
        k = max(1, round(width / 40))  # a long lit facade is several lights
        for j in range(k):
            place(sl[1].start + (j + .5) * width / k, y, False)
    return sorted({(p['x'], p['y']): p for p in points}.values(), key=lambda p: (p['y'], p['x']))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    for name in ('town', 'clear', 'window', 'capture', 'logo'):
        ap.add_argument(f'--{name}', type=Path, required=True)
    a = ap.parse_args()
    (PACK / MEDIA).mkdir(parents=True, exist_ok=True)
    night = rgb(a.town)
    H, W = night.shape[:2]
    town, clouds = town_layers(night, rgb(a.clear, (W, H)))
    strip, (sx, sy, sw, sh) = sea_strip(night)
    window, opening, flames = window_layer(rgb(a.window))
    logo, logo_place = logo_layer(np.asarray(Image.open(a.logo).convert('RGBA')).astype(np.float32), window.shape[1])
    scene = {
        'clouds': {'mediaId': 'refreshed.title-scene.clouds', **write(clouds, 'clouds.webp', 'RGBA'), 'drift': DRIFT, 'cycleSeconds': CYCLE},
        'sea': {'mediaId': 'refreshed.title-scene.sea', **write(strip, 'sea.webp', 'RGBA', 82),
                'x': sx, 'y': sy, 'width': sw, 'height': sh, 'frames': SEA_FRAMES, 'framesPerSecond': SEA_FPS},
        'window': {'mediaId': 'refreshed.title-scene.window', **write(window, 'window.webp', 'RGBA'),
                   'width': window.shape[1], 'height': window.shape[0], 'opening': opening, 'flames': flames},
        'lights': lights(rgb(a.capture), night),
        'logo': {'mediaId': 'refreshed.title-scene.logo', **write(logo, 'logo.webp', 'RGBA', 90), **logo_place},
    }
    title = write(town, 'town.webp', 'RGB')
    payload_path = PACK / 'payload.json'
    payload = json.loads(payload_path.read_text())
    section = payload['artReplacements']
    entry = next(image for image in section['images'] if image['replaces'] == 'screen.title')
    old = PACK / entry['path'].split('daggerfall.art-replacements/', 1)[1]
    entry.update(title, width=W, height=H)
    if old.exists() and old.name != 'town.webp':
        old.unlink()
    section['titleScene'] = scene
    payload_path.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n')
    print(f"town {W}x{H}, clouds, sea {sw}x{sh}x{SEA_FRAMES}, window {window.shape[1]}x{window.shape[0]}, "
          f"{len(scene['lights'])} lights ({sum(p['lamp'] for p in scene['lights'])} lamps), flames {flames}")


if __name__ == '__main__':
    main()
