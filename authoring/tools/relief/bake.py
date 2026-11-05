#!/usr/bin/env python3
"""Prototype of the relief bake (#10502): WOODS as a drained, eroded landscape.

Runs stream-power landscape evolution over the map, the coast fixed and uplift calibrated back to WOODS's own
heights, so the Iliac Bay keeps its shape and gains valleys and ridges. Climate sets the rain. Every location gets
a calmer pad, and the classic height returns along the coast so ports and shores stay where they are.

    authoring/tools/relief/bake.py STAGING_ROOT ARENA2 OUT_PREVIEW_DIR

Writes worldrpg/terrain/relief.bin below STAGING_ROOT. The product needs a C# port of this before it can ship: a
player's import can't depend on numba. Grid sample (i, j) stands at map pixel (i / FINE, j / FINE) of the WOODS
heightmap, whose pixel (X, Y) is vertex (0, 0) of the surface of map pixel (X + 1, Y).
"""
import heapq, json, os, struct, sys, time
import numpy as np
from numba import njit
from scipy import ndimage as ni

W, H, FINE = 1000, 500, 2
CELL = 819.2 / FINE
OCEAN = 27.2 * 1.5
RAIN = {223: 1.0, 224: .35, 225: .5, 226: .9, 227: 1.7, 228: 1.5, 229: 1.0, 230: 1.2, 231: 1.2, 232: 1.1}
STEPS, ROUNDS, DT = 45, 6, 2e4
ERODIBILITY = 2e-5
SHORE_FROM, SHORE_FULL = 300.0, 1_800.0
PAD_METRES = 900.0


def read_woods(arena2):
    data = open(os.path.join(arena2, 'WOODS.WLD'), 'rb').read()
    offset = struct.unpack_from('<8I', data, 0)[7]
    heightmap = np.frombuffer(data, np.uint8, W * H, offset).reshape(H, W).astype(np.float64)
    table = np.frombuffer(data, '<u4', W * H, 144).astype(np.int64) + 22 + 12
    centre = np.frombuffer(data, np.uint8)[table].reshape(H, W).astype(np.float64)
    return np.clip(np.maximum(heightmap * 8 + centre * 4, 27.2), 0, 1539) * 1.5


def read_payload(staging):
    payload = json.load(open(os.path.join(staging, 'worldrpg/payloads/daggerfall.imported.json')))
    climate = np.zeros((H, W), np.float64)
    for row in payload['climate']['rows']:
        x = 0
        for run in row['runs']:
            value = RAIN.get(run['value'], 1.0)
            climate[row['y'], max(x - 1, 0):x - 1 + run['count']] = value  # the grid's first column is a sentinel
            x += run['count']
    places = [(l['exterior']['mapPixelX'], l['exterior']['mapPixelY'], l['exterior']['width'] * l['exterior']['height'])
              for l in payload['locations']['locations'] if l.get('exterior')]
    return climate, places


def fbm(rng, shape, sigmas):
    total, amplitude = np.zeros(shape), 1.0
    for sigma in sigmas:
        total += amplitude * ni.gaussian_filter(rng.standard_normal(shape), sigma) * sigma
        amplitude *= .6
    return total / total.std()


@njit(cache=True)
def step(h, uplift, k, rain, sea, seed, dt, cell):
    rows, cols = h.shape
    count = rows * cols
    filled = h.copy()
    done = np.zeros(count, np.bool_)
    receiver = np.full(count, -1, np.int64)
    order = np.empty(count, np.int64)
    n = 0
    heap = [(0.0, np.int64(0))]
    heap.pop()
    for y in range(rows):
        for x in range(cols):
            if sea[y, x] or y == 0 or y == rows - 1 or x == 0 or x == cols - 1:
                i = y * cols + x
                done[i] = True
                heapq.heappush(heap, (filled[y, x], np.int64(i)))
    dx = (-1, 0, 1, -1, 1, -1, 0, 1)
    dy = (-1, -1, -1, 0, 0, 1, 1, 1)
    np.random.seed(seed)
    while len(heap) > 0:
        level, i = heapq.heappop(heap)
        order[n] = i
        n += 1
        y = i // cols
        x = i - y * cols
        for q in range(8):
            yy = y + dy[q]
            xx = x + dx[q]
            if yy < 0 or yy >= rows or xx < 0 or xx >= cols:
                continue
            j = yy * cols + xx
            if done[j]:
                continue
            done[j] = True
            receiver[j] = i
            if filled[yy, xx] <= level + 1e-3:
                filled[yy, xx] = level + 1e-3
            heapq.heappush(heap, (filled[yy, xx], j))
    # Flow leaves down a random slope, weighted by its steepness: a fixed steepest neighbour lines valleys up on the grid.
    for c in range(n):
        i = order[c]
        if receiver[i] < 0:
            continue
        y = i // cols
        x = i - y * cols
        total = 0.0
        for q in range(8):
            yy = y + dy[q]
            xx = x + dx[q]
            if yy < 0 or yy >= rows or xx < 0 or xx >= cols:
                continue
            drop = filled[y, x] - filled[yy, xx]
            if drop > 0:
                total += drop / (1.41421356 if (dx[q] != 0 and dy[q] != 0) else 1.0)
        if total <= 0:
            continue
        pick = np.random.random() * total
        for q in range(8):
            yy = y + dy[q]
            xx = x + dx[q]
            if yy < 0 or yy >= rows or xx < 0 or xx >= cols:
                continue
            drop = filled[y, x] - filled[yy, xx]
            if drop > 0:
                pick -= drop / (1.41421356 if (dx[q] != 0 and dy[q] != 0) else 1.0)
                if pick <= 0:
                    receiver[i] = yy * cols + xx
                    break
    # Random receivers can break the flood's order, so sort downstream-first by filled height, which they all descend.
    keys = np.empty(count, np.float64)
    for i in range(count):
        keys[i] = filled[i // cols, i - (i // cols) * cols]
    order = np.argsort(keys, kind='mergesort')
    area = np.empty(count, np.float64)
    for i in range(count):
        area[i] = cell * cell * rain[i // cols, i - (i // cols) * cols]
    for c in range(count - 1, -1, -1):
        i = order[c]
        if receiver[i] >= 0:
            area[receiver[i]] += area[i]
    out = filled.copy()
    for c in range(count):
        i = order[c]
        r = receiver[i]
        y = i // cols
        x = i - y * cols
        if r < 0 or sea[y, x]:
            continue
        ry = r // cols
        rx = r - ry * cols
        d = cell * (1.41421356 if (ry != y and rx != x) else 1.0)
        below = out[ry, rx]
        f = k[y, x] * dt * np.sqrt(area[i]) / d
        lowered = (out[y, x] + uplift[y, x] * dt + f * below) / (1 + f)
        out[y, x] = lowered if lowered > below else below + 1e-3
    return out, area.reshape(rows, cols)


def evolve(h, uplift, k, rain, sea, steps, seed):
    area = None
    for s in range(steps):
        h, area = step(h, uplift, k, rain, sea, seed + s, DT, CELL)
        h[~sea] += .06 * ni.laplace(h)[~sea]
    return h, area


def pads(h, places, sea):
    """Calms the ground under every location: the rule that keeps a town's flattening from standing on a cliff."""
    weight = np.zeros_like(h)
    rows, cols = h.shape
    for x, y, blocks in places:
        # A location stands in the surface of map pixel (X, Y): between heightmap samples (X - 1, Y - 1) and (X, Y).
        i, j = int((x - .5) * FINE), int((y - .5) * FINE)
        if 0 <= i < cols and 0 <= j < rows:
            weight[j, i] = max(weight[j, i], 1 + min(blocks, 16) / 4)
    reach = PAD_METRES / CELL
    weight = np.clip(ni.gaussian_filter(weight, reach) * 2 * np.pi * reach * reach, 0, 1)
    calm = ni.gaussian_filter(h, reach * 1.5)
    return h + (calm - h) * weight


def main():
    staging, arena2, preview = sys.argv[1:4]
    os.makedirs(preview, exist_ok=True)
    started = time.time()
    classic = read_woods(arena2)
    rain_map, places = read_payload(staging)
    sea = np.kron(classic <= OCEAN + .01, np.ones((FINE, FINE), bool))
    target = ni.zoom(ni.gaussian_filter(classic, 1.5), FINE, order=3)
    target[sea] = OCEAN
    target = np.maximum(target, OCEAN)
    rain = np.clip(ni.zoom(ni.gaussian_filter(rain_map, 3), FINE, order=1), .2, 2)
    rng = np.random.default_rng(10502)
    k = ERODIBILITY / np.clip(1 + .35 * fbm(rng, target.shape, [60, 25, 10]), .4, 1.8)
    h = np.where(sea, OCEAN, target * .5 + 5 + 6 * np.abs(fbm(rng, target.shape, [12, 5, 2])))
    uplift = np.where(sea, 0, np.maximum(target, 10) / 1500 * 1e-3)
    for attempt in range(ROUNDS):
        h, area = evolve(h, uplift, k, rain, sea, STEPS, 1000 * attempt)
        smooth, wanted = ni.gaussian_filter(h, 6), ni.gaussian_filter(target, 6)
        uplift = np.where(sea, 0, uplift * np.clip((wanted + 40) / (smooth + 40), .5, 2.5) ** 1.3)
        print(f'round {attempt}: {time.time() - started:.0f} s, peak {h.max():.0f} m, '
              f'rms {np.sqrt(((smooth - wanted)[~sea] ** 2).mean()):.0f} m', flush=True)
    h = pads(h, places, sea)
    distance = ni.distance_transform_edt(~sea) * CELL
    share = np.clip((distance - SHORE_FROM) / (SHORE_FULL - SHORE_FROM), 0, 1)
    share = share * share * (3 - 2 * share)
    out = os.path.join(staging, 'worldrpg/terrain/relief.bin')
    with open(out, 'wb') as f:
        f.write(struct.pack('<4sII', b'RLF1', h.shape[1], h.shape[0]))
        f.write(h.astype('<f4').tobytes())
        f.write(np.round(share * 255).astype(np.uint8).tobytes())
    np.save(os.path.join(preview, 'relief.npy'), h.astype(np.float32))
    np.save(os.path.join(preview, 'area.npy'), area.astype(np.float32))
    print(f'{out}: {os.path.getsize(out) / 1e6:.1f} MB in {time.time() - started:.0f} s')


main()
