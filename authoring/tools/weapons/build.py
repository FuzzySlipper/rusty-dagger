"""Blender: builds one weapon's model from its upright icon and its parts in authoring/specs/weapons/weapons.json.

  blender -b --factory-startup --python authoring/tools/weapons/build.py -- NAME ICON.png OUT_DIR

Each part is the icon's pixels of one material class (steel, brass, leather), picked by `pick`: `top` or `bottom`
when the class appears more than once (a guard and a pommel), a piece's index counted from the top (a ring between
them), or `all` its pieces; build.json lists every piece per class to choose from. A `flat` part is two sheets that
meet at its outline, offset by `half` metres toward the middle over `ramp` metres (`profile` `ridge` rises straight,
a blade's diamond section; `round` rises in a quarter circle, a bar). A `lathe` part is a ring every 5 mm as wide as
the icon there, round in section; `smooth` rings median-filter the radii so studs and spikes on the outline do not
swell it. `ball` makes a lathe part a sphere, the largest circle inside its outline, keeping the turned outline
below it (a collar). `spikes` sets cones on its widest ring's sphere: `count`, `length` and `base` in metres,
spread evenly. A part's `light` is passed to paint.py.

Writes OUT_DIR/reference.glb (the icon projected flat onto every part, the colour layout `palette_cells.py`
classifies) and OUT_DIR/model.glb (the same mesh with unwrapped UVs and blank textures for it to paint). Both are
in the held-look frame: origin at the grip's centre, length along -Z with the tip forward, +Y one edge (the
guard's arms), the broad face toward ±X. Each part is its own object; materials are `metal`
(the steel parts, tinted per metal by the game) and `trim` (everything else), one UV layout each. build.json
gives each part's seed colours: its icon pixels' colours at eight steps of brightness, for palette_cells regions.
"""
import colorsys
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
name, icon_path, out = sys.argv[sys.argv.index('--') + 1:][:3]
out = Path(out)
out.mkdir(parents=True, exist_ok=True)
spec = json.loads((ROOT / 'authoring/specs/weapons/weapons.json').read_text())
weapon = next(w for w in spec['weapons'] if w['name'] == name)
icon = bpy.data.images.load(str(Path(icon_path).resolve()))
W, H = icon.size
pixels = np.array(icon.pixels[:]).reshape(H, W, 4)[::-1]   # row 0 at the top, as the PNG
METRES = weapon['length'] / H                                # the icon's height is the weapon's length


def classify():
    """Each opaque pixel's class: 0 none, 1 steel, 2 brass, 3 leather; ink outlines take their nearest class."""
    rgb, alpha = pixels[..., :3], pixels[..., 3]
    hsv = np.array([colorsys.rgb_to_hsv(*p) for p in rgb.reshape(-1, 3)]).reshape(H, W, 3)
    hue, sat, val = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
    cls = np.zeros((H, W), np.int8)
    opaque = alpha > 0.5
    cls[opaque & (sat < 0.22) & (val > 0.18)] = 1
    cls[opaque & (sat >= 0.22) & (hue >= 33) & (hue < 70) & (val > 0.18)] = 2
    cls[opaque & (sat >= 0.22) & ((hue < 33) | (hue >= 330)) & (val > 0.12)] = 3
    for _ in range(12):   # grow the classes into unclassified opaque pixels (ink, dark shadows)
        todo = opaque & (cls == 0)
        if not todo.any():
            break
        grown = cls.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            shifted = np.roll(np.roll(cls, dy, 0), dx, 1)
            take = todo & (grown == 0) & (shifted > 0)
            grown[take] = shifted[take]
        cls = grown
    return cls


def components(mask):
    """Connected components of a mask (4-neighbour), largest first."""
    label = np.zeros(mask.shape, np.int32)
    found = []
    for y, x in zip(*np.nonzero(mask)):
        if label[y, x]:
            continue
        n = len(found) + 1
        stack, cells = [(y, x)], []
        label[y, x] = n
        while stack:
            cy, cx = stack.pop()
            cells.append((cy, cx))
            for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                if 0 <= ny < H and 0 <= nx < W and mask[ny, nx] and not label[ny, nx]:
                    label[ny, nx] = n
                    stack.append((ny, nx))
        found.append(label == n)
    return sorted(found, key=lambda m: -m.sum())


def part_mask(cls, part):
    want = {'steel': 1, 'brass': 2, 'leather': 3}[part['class']]
    pieces = [m for m in components(cls == want) if m.sum() > 100]
    if not pieces:
        raise SystemExit(f'{name}: no {part["class"]} pixels for {part["name"]}')
    pieces.sort(key=lambda m: np.nonzero(m)[0].mean())   # top to bottom
    pick = part.get('pick', 'largest')
    if pick == 'all':
        return np.any(pieces, axis=0)
    if isinstance(pick, int):
        return pieces[pick]
    if pick == 'top':
        return pieces[0]
    if pick == 'bottom':
        return pieces[-1]
    return max(pieces, key=lambda m: m.sum())


def distance(mask):
    """Pixels to the outline (a city-block erosion count), for every pixel inside the mask."""
    d = np.zeros(mask.shape, np.float32)
    inside = mask.copy()
    k = 0
    while inside.any():
        k += 1
        d[inside] = k
        inside = (inside & np.roll(inside, 1, 0) & np.roll(inside, -1, 0)
                  & np.roll(inside, 1, 1) & np.roll(inside, -1, 1))
    return d


def place(x, y, depth):
    """Pixel column x and row y (row 0 at the top) and depth in metres to Blender coordinates; the export turns
    Blender +Y into glTF -Z (the length, tip forward) and Blender +Z into glTF +Y (the up edge)."""
    return Vector((depth, (ORIGIN[1] - y) * METRES, (x - ORIGIN[0]) * METRES))


def uv(x, y):
    return (x / W, 1 - y / H)


def flat(bm, mask, part):
    step = 2
    m = mask[::step, ::step]
    d = distance(m) * step * METRES
    ramp, half = part['ramp'], part['half']
    rise = np.clip(d / ramp, 0, 1)
    rise = np.sqrt(1 - (1 - rise) ** 2) if part.get('profile') == 'round' else rise
    t = half * rise
    h, w = m.shape
    front, back = {}, {}

    def corner(cy, cx):
        cells = [(cy - a, cx - b) for a in (0, 1) for b in (0, 1)]
        values = [t[a, b] if 0 <= a < h and 0 <= b < w and m[a, b] else None for a, b in cells]
        depth = 0.0 if None in values else float(np.mean(values))
        key = (cy, cx)
        if key not in front:
            px, py = cx * step, cy * step
            front[key] = bm.verts.new(place(px, py, depth))
            back[key] = front[key] if depth == 0 else bm.verts.new(place(px, py, -depth))
        return front[key], back[key]

    for cy, cx in zip(*np.nonzero(m)):
        quad = [corner(cy, cx), corner(cy, cx + 1), corner(cy + 1, cx + 1), corner(cy + 1, cx)]
        for side, order in ((0, quad), (1, quad[::-1])):
            verts = [c[side] for c in order]
            # A cell with every corner on the outline is one sheet: its back face would repeat its front.
            if len(set(verts)) < 3 or (side and all(c[0] is c[1] for c in quad)):
                continue
            bm.faces.new(list(dict.fromkeys(verts)))


def lathe(bm, mask, part):
    rows = [y for y in range(H) if mask[y].any()]
    segments = part.get('segments', 12)
    step = max(1, min(round(0.005 / METRES), len(rows) // 6))   # a ring every 5 mm, at least six to a part
    stations = []
    for y in rows[::step] + rows[-1:]:
        xs = np.nonzero(mask[y])[0]
        stations.append((y, (xs.min() + xs.max() + 1) / 2, (xs.max() - xs.min() + 1) / 2))
    smooth = part.get('smooth', 1)
    radii = [np.median([s[2] for s in stations[max(0, i - smooth // 2):i + smooth // 2 + 1]]) for i in range(len(stations))]
    if part.get('ball'):   # a true sphere, the largest that fits the outline (spikes do not widen it), the turned outline kept below it (a collar)
        d = distance(mask)
        cy, cx = np.unravel_index(int(np.argmax(d)), d.shape)
        r = float(d.max())
        below = [(s, q) for s, q in zip(stations, radii) if s[0] > cy + r]
        ball = [((cy + r * math.cos(t), cx, 0), max(r * math.sin(t), r * 0.05)) for t in np.linspace(math.pi, 0, segments + 1)]
        stations, radii = [s for s, _ in ball] + [s for s, _ in below], [q for _, q in ball] + [q for _, q in below]
    rings = []
    for (y, cx, _), r in zip(stations, radii):
        ring = []
        for k in range(segments):
            a = 2 * math.pi * k / segments
            ring.append(bm.verts.new(place(cx + r * math.cos(a), y, r * math.sin(a) * METRES)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(segments):
            bm.faces.new([a[k], a[(k + 1) % segments], b[(k + 1) % segments], b[k]])
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    if 'spikes' in part:
        i = int(np.argmax(radii))
        y, cx, _ = stations[i]
        centre, r = place(cx, y, 0), radii[i] * METRES
        spikes = part['spikes']
        n = spikes['count']
        for k in range(n):   # a Fibonacci sphere spreads the spikes evenly
            z = 1 - 2 * (k + 0.5) / n
            a = k * math.pi * (3 - math.sqrt(5))
            direction = Vector((math.sqrt(1 - z * z) * math.cos(a), z, math.sqrt(1 - z * z) * math.sin(a)))
            if direction.y < -0.55:   # none where the haft enters the ball
                continue
            cone = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=spikes['base'], radius2=0,
                                         depth=spikes['length'] + r * 0.3)
            verts = cone['verts']
            rotation = Vector((0, 0, 1)).rotation_difference(direction)
            bmesh.ops.rotate(bm, verts=verts, cent=Vector(), matrix=rotation.to_matrix())
            bmesh.ops.translate(bm, verts=verts, vec=centre + direction * (r + spikes['length'] / 2 - r * 0.15))


def material(label, image=None):
    m = bpy.data.materials.new(label)
    m.use_nodes = True
    bsdf = m.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Metallic'].default_value = 0
    bsdf.inputs['Roughness'].default_value = 0.85
    if image:
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = image
        tex.interpolation = 'Closest'
        m.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    return m


cls = classify()
masks = {p['name']: part_mask(cls, p) for p in weapon['parts']}
grip = weapon['grip']   # the part the hand closes on, and the share of it (from its bottom) the hand covers
gy, gx = np.nonzero(masks[grip['part']])
top, bottom = gy.min(), gy.max() + 1
lo, hi = grip.get('span', [0, 1])
ORIGIN = ((gx.min() + gx.max() + 1) / 2, bottom - (lo + hi) / 2 * (bottom - top))

bpy.ops.wm.read_factory_settings(use_empty=True)
icon = bpy.data.images.load(str(Path(icon_path).resolve()))
objects = []
for part in weapon['parts']:
    bm = bmesh.new()
    (flat if part['shape'] == 'flat' else lathe)(bm, masks[part['name']], part)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    layer = bm.loops.layers.uv.new('UVMap')
    for face in bm.faces:
        for loop in face.loops:   # project the icon flat: front and back both show it, as on the paper doll
            p = loop.vert.co
            loop[layer].uv = uv(p.z / METRES + ORIGIN[0], ORIGIN[1] - p.y / METRES)
    data = bpy.data.meshes.new(part['name'])
    bm.to_mesh(data)
    bm.free()
    obj = bpy.data.objects.new(part['name'], data)
    bpy.context.scene.collection.objects.link(obj)
    if part['shape'] == 'flat':
        mod = obj.modifiers.new('decimate', 'DECIMATE')
        mod.ratio = min(1.0, part.get('faces', 600) / max(1, len(data.polygons)))
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    obj['slot'] = 'metal' if part['class'] == 'steel' else 'trim'
    objects.append(obj)

projected = material('icon', icon)
for obj in objects:
    obj.data.materials.append(projected)
    for poly in obj.data.polygons:
        poly.use_smooth = False
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=str(out / 'reference.glb'), export_format='GLB', use_selection=True)

# The painted model: the same part objects, each slot's parts sharing one material and one unwrapped UV layout
# (so palette_cells can name regions by part), with a blank texture per slot for it to fill.
for slot in ('metal', 'trim'):
    parts = [o for o in objects if o['slot'] == slot]
    if not parts:
        continue
    shared = material(slot, bpy.data.images.new(slot, 64, 64))
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:
        o.data.materials.clear()
        o.data.materials.append(shared)
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=str(out / 'model.glb'), export_format='GLB', use_selection=True)
def seeds(mask):
    rgb = pixels[..., :3][mask & (pixels[..., 3] > 0.5)]
    rgb = rgb[rgb.max(1) > 0.1]   # not the ink outline
    order = np.argsort(rgb @ np.array([0.299, 0.587, 0.114]))
    return [[round(float(c) * 255) for c in np.median(rgb[order][chunk], 0)] for chunk in np.array_split(np.arange(len(rgb)), 8)]


def pieces(want):
    found = sorted((m for m in components(cls == want) if m.sum() > 100), key=lambda m: np.nonzero(m)[0].mean())
    return [[int(np.nonzero(m)[0].min()), int(np.nonzero(m)[0].max()), int(m.sum())] for m in found]


report = {'weapon': name, 'metresPerPixel': METRES, 'origin': ORIGIN,
          'pieces': {c: pieces(i) for c, i in (('steel', 1), ('brass', 2), ('leather', 3))},   # rows and pixels, top first
          'parts': {p['name']: {'slot': 'metal' if p['class'] == 'steel' else 'trim', 'seeds': seeds(masks[p['name']]),
                                **({'light': p['light']} if 'light' in p else {})} for p in weapon['parts']},
          'triangles': {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects}}
(out / 'build.json').write_text(json.dumps(report, indent=1) + '\n')
print('BUILT', json.dumps(report))
