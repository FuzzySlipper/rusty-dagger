"""places: the travel map's small models of places, one Engine static mesh (.rstatmsh) a kind, for travel_map.py (#10950).

Each model is a map piece: low and flat-shaded, in plain colours, in house widths (the map draws a house width at its
`expedition.map.house` tuning), standing on its origin with its ground a little under it, so it sits into a slope. Shapes
are laid out with z up, as a modelling tool would, and written y up for the Engine. Each colour is a material slot, which
the art pack's travelMap entry names in sRGB for the map to bind, so a kind draws as one static mesh every place of it
shares. A
city is a walled ring of houses round a keep and a temple, a town a looser cluster about a church, a village a few
houses by a well; farms, homes, taverns, temples, shrines, dungeons, ruins, graveyards and the player's ship each have
their own shape, so the kind of a place reads from its model. Layouts are seeded per kind, so the same models every run.
"""
import hashlib
import json
import math
import random
import struct

COLOURS = {
    'plaster': '#d8c8a4', 'timber': '#7a5a3a', 'thatch': '#9a7442', 'tile': '#a4492f', 'slate': '#4a5262',
    'stone': '#8e8a82', 'yard': '#7c7870', 'darkstone': '#4c4846', 'gold': '#d8ad45', 'wood': '#6a4a2c',
    'field': '#c8b26a', 'crop': '#7f9a46', 'hedge': '#3f5a2e', 'moss': '#5e6b48', 'bone': '#cfc8b4',
    'purple': '#5a3a64', 'water': '#3d7fa0', 'sail': '#e8e0cc', 'banner': '#2c4fb0', 'window': '#2c3340',
    'shadow': '#141820',
}


class Model:
    """Triangles and quads with a material each, turned and placed as they are added."""

    def __init__(self):
        self.verts, self.uvs, self.faces, self.materials = [], [], [], []
        self.names = []

    def material(self, name):
        if name not in self.names:
            self.names.append(name)
        return self.names.index(name)

    def face(self, points, name, uvs=None):
        base = len(self.verts)
        self.verts.extend(points)
        self.uvs.extend(uvs if uvs is not None else [(0.0, 0.0)] * len(points))
        self.faces.append(list(range(base, base + len(points))))
        self.materials.append(self.material(name))

    def box(self, centre, size, name, turn=0.0, top=None):
        """A box on the ground at centre (x, y, z of its foot), size (width, depth, height), turned about z; top paints its lid."""
        cx, cy, cz = centre
        w, d, h = size[0] / 2, size[1] / 2, size[2]
        c, s = math.cos(turn), math.sin(turn)

        def at(x, y, z):
            return (cx + x * c - y * s, cy + x * s + y * c, cz + z)

        lo = [at(-w, -d, 0), at(w, -d, 0), at(w, d, 0), at(-w, d, 0)]
        hi = [at(-w, -d, h), at(w, -d, h), at(w, d, h), at(-w, d, h)]
        for i in range(4):
            j = (i + 1) % 4
            self.face([lo[i], lo[j], hi[j], hi[i]], name)
        self.face(hi, top or name)

    def box_windows(self, centre, size, turn=0.0, long_count=2, short_count=1, rows=1, z_base=0.0):
        """Warm-lit window panes slightly proud of a box's walls, reading cleanly from both 3 km and 10 km."""
        cx, cy, cz = centre
        w, d, h = size[0] / 2, size[1] / 2, size[2]
        c, s = math.cos(turn), math.sin(turn)
        eps = 0.018
        win_w = min(0.16, max(0.08, size[0] * 0.27))
        win_d = min(0.15, max(0.08, size[1] * 0.28))
        win_h = min(0.18, max(0.10, h * (0.28 if rows > 1 else 0.44)))

        def at(x, y, z):
            return (cx + x * c - y * s, cy + x * s + y * c, cz + z)

        def offsets(span, count, pane):
            if count <= 0 or span < pane * 1.25:
                return []
            if count == 1:
                return [0.0]
            step = span / (count + 0.45)
            return [(-0.5 * (count - 1) + i) * step for i in range(count)]

        for row in range(rows):
            z0 = z_base + h * (0.24 + row * 0.36 if rows > 1 else 0.34)
            z1 = min(z_base + h * 0.92, z0 + win_h)
            if z1 <= z0 + 0.02:
                continue
            for ox in offsets(size[0], long_count, win_w):
                hw = win_w / 2
                # South wall (y = -d) and north wall (y = +d), matching Model.box outward winding.
                self.face([at(ox - hw, -d - eps, z0), at(ox + hw, -d - eps, z0), at(ox + hw, -d - eps, z1), at(ox - hw, -d - eps, z1)], 'window')
                self.face([at(ox + hw, d + eps, z0), at(ox - hw, d + eps, z0), at(ox - hw, d + eps, z1), at(ox + hw, d + eps, z1)], 'window')
            for oy in offsets(size[1], short_count, win_d):
                hd = win_d / 2
                # East wall (x = +w) and west wall (x = -w), matching Model.box outward winding.
                self.face([at(w + eps, oy - hd, z0), at(w + eps, oy + hd, z0), at(w + eps, oy + hd, z1), at(w + eps, oy - hd, z1)], 'window')
                self.face([at(-w - eps, oy + hd, z0), at(-w - eps, oy - hd, z0), at(-w - eps, oy - hd, z1), at(-w - eps, oy + hd, z1)], 'window')

    def tower_windows(self, centre, radius, z0, z1, count=4):
        """Lantern-lit upper tower/belfry openings evenly spaced around a tower drum."""
        cx, cy, cz = centre
        r = radius + 0.018
        half_arc = min(0.24, math.pi / (count * 2.2))
        for i in range(count):
            a = i * math.tau / count
            a0, a1 = a - half_arc, a + half_arc
            x0, y0 = cx + r * math.cos(a0), cy + r * math.sin(a0)
            x1, y1 = cx + r * math.cos(a1), cy + r * math.sin(a1)
            self.face([(x0, y0, cz + z0), (x1, y1, cz + z0), (x1, y1, cz + z1), (x0, y0, cz + z1)], 'window')

    def gable(self, centre, size, rise, name, turn=0.0, eave=0.08):
        """A pitched roof over a box's top, its ridge along x, its eaves a little past the walls."""
        cx, cy, cz = centre
        w, d = size[0] / 2 + eave, size[1] / 2 + eave
        c, s = math.cos(turn), math.sin(turn)

        def at(x, y, z):
            return (cx + x * c - y * s, cy + x * s + y * c, cz + z)

        a, b, e, f = at(-w, -d, 0), at(w, -d, 0), at(w, d, 0), at(-w, d, 0)
        r0, r1 = at(-w, 0, rise), at(w, 0, rise)
        self.face([a, b, r1, r0], name)
        self.face([e, f, r0, r1], name)
        self.face([b, e, r1], name)
        self.face([f, a, r0], name)

    def spire(self, centre, radius, rise, name, sides=4, turn=0.0):
        cx, cy, cz = centre
        ring = [(cx + radius * math.cos(turn + i * math.tau / sides), cy + radius * math.sin(turn + i * math.tau / sides), cz) for i in range(sides)]
        tip = (cx, cy, cz + rise)
        for i in range(sides):
            self.face([ring[i], ring[(i + 1) % sides], tip], name)

    def tower(self, centre, radius, height, name, sides=8, cap=None, rise=0.0, battlement=None):
        cx, cy, cz = centre
        ring = [(cx + radius * math.cos(i * math.tau / sides), cy + radius * math.sin(i * math.tau / sides)) for i in range(sides)]
        for i in range(sides):
            (x0, y0), (x1, y1) = ring[i], ring[(i + 1) % sides]
            self.face([(x0, y0, cz), (x1, y1, cz), (x1, y1, cz + height), (x0, y0, cz + height)], name)
        self.face([(x, y, cz + height) for x, y in ring], name)
        if cap:
            self.spire((cx, cy, cz + height), radius * 1.15, rise, cap, sides)
        if battlement:
            for i in range(0, sides, 2):
                angle = (i + 0.5) * math.tau / sides
                self.box((cx + radius * 0.85 * math.cos(angle), cy + radius * 0.85 * math.sin(angle), cz + height),
                         (radius * 0.45, radius * 0.3, radius * 0.4), battlement, angle)

    def house(self, x, y, w, d, h, rise, walls, roof, turn, lit=False):
        self.box((x, y, -0.18), (w, d, h + 0.18), walls, turn)
        self.gable((x, y, h), (w, d), rise, roof, turn)
        if lit:
            self.box_windows((x, y, 0.0), (w, d, h), turn, long_count=2 if w >= 0.58 else 1, short_count=1)

    def wall_ring(self, radius, height, thick, sides, name, towers=None, gate=None, lit=False):
        for i in range(sides):
            if gate is not None and i == gate:
                continue
            a0, a1 = i * math.tau / sides, (i + 1) * math.tau / sides
            mid = (a0 + a1) / 2
            length = 2 * radius * math.sin(math.pi / sides) + thick
            self.box((radius * math.cos(mid), radius * math.sin(mid), -0.18), (thick, length, height + 0.18), name, mid)
        if towers:
            for i in range(sides):
                angle = i * math.tau / sides
                tx, ty = radius * math.cos(angle), radius * math.sin(angle)
                self.tower((tx, ty, -0.18), thick * 1.5, height * 1.45 + 0.13, name, 6, towers, height * 0.6)
                if lit and i % 2 == 0:
                    self.tower_windows((tx, ty, 0.0), thick * 1.5, height * 0.72, height * 1.18, count=3)

    def ground(self, radius, name, sides=12, lift=0.085):
        """A low plate of ground under a place (a yard, a field), cleanly clear of the land and its contact shadow."""
        ring = [(radius * math.cos(i * math.tau / sides), radius * math.sin(i * math.tau / sides), lift) for i in range(sides)]
        self.face(ring, name)

    def shadow(self, rx, ry=None, sides=16, lift=0.075, droop=0.055):
        """A soft radial contact-shadow apron under the place, sitting cleanly above sunken flat ground and fading to transparent."""
        ry = rx if ry is None else ry
        centre = (0.0, 0.0, lift)
        inner_r = 0.62
        inner = [(rx * inner_r * math.cos(i * math.tau / sides), ry * inner_r * math.sin(i * math.tau / sides), lift) for i in range(sides)]
        outer = [(rx * math.cos(i * math.tau / sides), ry * math.sin(i * math.tau / sides), droop) for i in range(sides)]
        inner_uv = [(0.5 + 0.5 * inner_r * math.cos(i * math.tau / sides), 0.5 + 0.5 * inner_r * math.sin(i * math.tau / sides)) for i in range(sides)]
        outer_uv = [(0.5 + 0.5 * math.cos(i * math.tau / sides), 0.5 + 0.5 * math.sin(i * math.tau / sides)) for i in range(sides)]
        for i in range(sides):
            j = (i + 1) % sides
            self.face([centre, inner[i], inner[j]], 'shadow', [(0.5, 0.5), inner_uv[i], inner_uv[j]])
            self.face([inner[i], outer[i], outer[j], inner[j]], 'shadow', [inner_uv[i], outer_uv[i], outer_uv[j], inner_uv[j]])

    def mesh(self, name):
        """The Engine's binary static mesh of these faces, and the sRGB colour and role name of each material slot in order.

        Each face is fanned into triangles with its own corners and its own normal, so it shades flat; triangles are
        grouped by colour. Positions turn from z up to y up: (x, y, z) becomes (x, z, -y), a rotation, so windings hold."""
        triangles = {slot: [] for slot in range(len(self.names))}
        for face, slot in zip(self.faces, self.materials):
            corners = [self.verts[index] for index in face]
            uvs = [self.uvs[index] for index in face]
            for i in range(1, len(corners) - 1):
                triangles[slot].append((
                    [(x, z, -y) for x, y, z in (corners[0], corners[i], corners[i + 1])],
                    [uvs[0], uvs[i], uvs[i + 1]],
                ))
        positions, normals, texcoords, groups = [], [], [], []
        for slot, tris in triangles.items():
            groups.append({'materialSlot': slot, 'start': len(positions), 'count': len(tris) * 3})
            for (a, b, c), (ua, ub, uc) in tris:
                u, v = [b[k] - a[k] for k in range(3)], [c[k] - a[k] for k in range(3)]
                n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
                length = math.sqrt(sum(k * k for k in n)) or 1
                positions += [a, b, c]
                normals += [[k / length for k in n]] * 3
                texcoords += [ua, ub, uc]
        count = len(positions)
        resource = bytearray(b'RMSHLE02')
        size = 16 + count * 8 * 4 + count * 4
        resource += struct.pack('<II', size, 1)
        offsets = {'positions': len(resource)}
        for point in positions: resource += struct.pack('<3f', *point)
        offsets['normals'] = len(resource)
        for normal in normals: resource += struct.pack('<3f', *normal)
        offsets['uvs'] = len(resource)
        for uv in texcoords: resource += struct.pack('<2f', *uv)
        offsets['indices'] = len(resource)
        resource += struct.pack(f'<{count}I', *range(count))
        assert len(resource) == size
        digest = hashlib.sha256(resource).hexdigest()
        lows = [min(point[k] for point in positions) for k in range(3)]
        highs = [max(point[k] for point in positions) for k in range(3)]
        descriptor = {
            'asset': f'travel-map-{name}',
            'payload': {
                'layout': {'vertexCount': count, 'indexCount': count, 'indexWidth': 'u32', 'attributes': [
                    {'name': 'position', 'components': 3, 'kind': 'f32'},
                    {'name': 'normal', 'components': 3, 'kind': 'f32'},
                    {'name': 'uv', 'components': 2, 'kind': 'f32'}]},
                'groups': groups,
                'bounds': {'min': lows, 'max': highs},
                'source': {'kind': 'resource', 'resource': f'mesh-resource/{digest}', 'contentHash': f'sha256:{digest}',
                           'byteLength': size, 'encoding': 'packedStreamsLeV2', 'positionsByteOffset': offsets['positions'],
                           'normalsByteOffset': offsets['normals'], 'uvsByteOffset': offsets['uvs'],
                           'indicesByteOffset': offsets['indices']},
                'provenance': 'staticAsset'},
            'materialSlots': [{'slot': slot, 'material': f'travel-map/{colour}'} for slot, colour in enumerate(self.names)],
            'collision': {'kind': 'visualOnly'},
        }
        head = json.dumps(descriptor, separators=(',', ':')).encode()
        return b'RSTATMSH' + struct.pack('<I', len(head)) + head + bytes(resource), [COLOURS[colour] for colour in self.names], list(self.names)


def cluster(model, rng, count, spread, walls, roofs, keep_out=0.0, size=1.0, lit=False):
    """Houses scattered in a ring between keep_out and spread, turned to face its middle, none on another."""
    placed = []
    tries = 0
    while len(placed) < count and tries < count * 60:
        tries += 1
        angle, away = rng.uniform(0, math.tau), math.sqrt(rng.uniform((keep_out / spread) ** 2, 1)) * spread
        x, y = away * math.cos(angle), away * math.sin(angle)
        w, d = rng.uniform(0.55, 0.85) * size, rng.uniform(0.42, 0.6) * size
        if any(math.hypot(x - px, y - py) < (w + pw) * 0.62 for px, py, pw in placed):
            continue
        placed.append((x, y, w))
        model.house(x, y, w, d, rng.uniform(0.32, 0.5) * size, rng.uniform(0.28, 0.4) * size, rng.choice(walls), rng.choice(roofs),
                    angle + math.pi / 2 + rng.uniform(-0.25, 0.25), lit=lit and rng.random() < 0.88)


def temple(model, x, y, scale=1.0, roof='gold', lit=False):
    model.box((x, y, -0.18), (1.1 * scale, 0.6 * scale, 0.6 * scale + 0.13), 'plaster')
    model.gable((x, y, 0.55 * scale), (1.1 * scale, 0.6 * scale), 0.3 * scale, 'slate')
    model.tower((x + 0.62 * scale, y, -0.18), 0.2 * scale, 1.05 * scale + 0.13, 'plaster', 8, roof, 0.55 * scale)
    if lit:
        model.box_windows((x, y, 0.0), (1.1 * scale, 0.6 * scale, 0.55 * scale), long_count=3, short_count=1)
        model.tower_windows((x + 0.62 * scale, y, 0.0), 0.2 * scale, 0.68 * scale, 0.94 * scale, count=4)


def keep(model, x, y, scale, stone, roof, lit=False):
    model.box((x, y, -0.18), (0.8 * scale, 0.8 * scale, 1.0 * scale + 0.13), stone)
    for cx, cy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        model.tower((x + cx * 0.4 * scale, y + cy * 0.4 * scale, -0.18), 0.16 * scale, 1.35 * scale + 0.13, stone, 8, roof, 0.42 * scale)
        if lit:
            model.tower_windows((x + cx * 0.4 * scale, y + cy * 0.4 * scale, 0.0), 0.16 * scale, 0.92 * scale, 1.20 * scale, count=3)
    if lit:
        model.box_windows((x, y, 0.0), (0.8 * scale, 0.8 * scale, 0.95 * scale), long_count=2, short_count=2, rows=2)


def stones(model, rng, radius, count, name, height=0.5):
    for i in range(count):
        angle = i * math.tau / count
        model.box((radius * math.cos(angle), radius * math.sin(angle), -0.18), (0.12, 0.08, height * rng.uniform(0.7, 1.1) + 0.13), name, angle)


def city(rng):
    model = Model()
    model.shadow(3.65)
    model.ground(2.55, 'yard', 16)
    model.wall_ring(2.4, 0.5, 0.14, 12, 'stone', towers='slate', gate=3, lit=True)
    keep(model, 0.15, 0.1, 1.0, 'stone', 'slate', lit=True)
    temple(model, -1.05, -0.75, 0.85, lit=True)
    cluster(model, rng, 20, 2.15, ['plaster', 'plaster', 'stone'], ['tile', 'tile', 'slate', 'thatch'], keep_out=0.8, size=0.85, lit=True)
    return model


def town(rng):
    model = Model()
    model.shadow(2.55)
    temple(model, 0, 0, 0.8, 'slate', lit=True)
    cluster(model, rng, 10, 1.55, ['plaster', 'plaster', 'timber'], ['thatch', 'tile', 'slate'], keep_out=0.7, size=0.9, lit=True)
    return model


def village(rng):
    model = Model()
    model.shadow(1.90)
    model.tower((0, 0, -0.18), 0.12, 0.29, 'stone', 8)
    cluster(model, rng, 5, 1.05, ['plaster', 'timber'], ['thatch', 'thatch', 'tile'], keep_out=0.4, size=0.85, lit=True)
    return model


def farm(rng):
    model = Model()
    model.shadow(1.90)
    for i, (x, y, colour) in enumerate(((0.75, 0.45, 'field'), (0.75, -0.45, 'crop'), (-0.2, -0.85, 'field'))):
        model.box((x, y, -0.18), (0.8, 0.8, 0.265), colour, 0.1 * i)
    model.house(-0.25, 0.25, 0.7, 0.5, 0.36, 0.32, 'plaster', 'thatch', 0.2, lit=True)
    model.house(0.1, -0.2, 0.6, 0.4, 0.42, 0.3, 'timber', 'thatch', 1.7, lit=True)
    return model


def wealthy_home(rng):
    model = Model()
    model.shadow(1.65)
    model.wall_ring(0.95, 0.12, 0.08, 10, 'hedge')
    model.house(0, 0.05, 1.0, 0.5, 0.55, 0.3, 'plaster', 'slate', 0, lit=True)
    model.house(-0.3, -0.3, 0.45, 0.4, 0.55, 0.3, 'plaster', 'slate', math.pi / 2, lit=True)
    model.tower((0.45, -0.25, -0.18), 0.14, 0.98, 'plaster', 8, 'slate', 0.35)
    return model


def poor_home(rng):
    model = Model()
    model.shadow(1.15)
    model.house(0, 0, 0.55, 0.42, 0.28, 0.32, 'timber', 'thatch', 0.3, lit=True)
    model.box((0.45, -0.25, -0.18), (0.25, 0.25, 0.33), 'wood', 0.3)
    return model


def tavern(rng):
    model = Model()
    model.shadow(1.50)
    model.house(0, 0, 0.9, 0.55, 0.62, 0.34, 'plaster', 'tile', 0, lit=True)
    model.house(0.15, -0.6, 0.6, 0.35, 0.32, 0.22, 'timber', 'thatch', 0, lit=True)
    model.box((-0.6, 0.35, -0.18), (0.05, 0.05, 0.73), 'wood')
    model.box((-0.6, 0.42, 0.42), (0.04, 0.2, 0.14), 'banner')
    return model


def temple_place(rng):
    model = Model()
    model.shadow(1.80)
    model.ground(1.0, 'yard', 12)
    model.box((0, 0, -0.18), (1.3, 0.75, 0.88), 'plaster')
    model.gable((0, 0, 0.7), (1.3, 0.75), 0.35, 'slate')
    model.box_windows((0, 0, 0.0), (1.3, 0.75, 0.7), long_count=3, short_count=1)
    model.tower((0.75, 0, -0.18), 0.24, 1.43, 'plaster', 8, 'gold', 0.6)
    model.tower_windows((0.75, 0, 0.0), 0.24, 0.85, 1.18, count=4)
    model.tower((-0.55, 0, 0.7), 0.26, 0.15, 'gold', 10, 'gold', 0.28)
    return model


def cult(rng):
    model = Model()
    model.shadow(1.30)
    stones(model, rng, 0.7, 9, 'darkstone')
    model.tower((0, 0, -0.18), 0.16, 1.03, 'darkstone', 4, 'darkstone', 0.35)
    return model


def coven(rng):
    model = Model()
    model.shadow(1.35)
    stones(model, rng, 0.75, 7, 'purple', 0.4)
    model.house(0, 0, 0.5, 0.42, 0.3, 0.42, 'darkstone', 'moss', 0.5)
    model.tower((0.25, 0.25, 0.2), 0.05, 0.45, 'darkstone', 6)
    return model


def labyrinth(rng):
    model = Model()
    model.shadow(2.55)
    model.ground(1.7, 'darkstone', 4, 0.085)
    for i in range(4):
        angle = i * math.tau / 4 + math.pi / 4
        model.box((1.15 * math.cos(angle), 1.15 * math.sin(angle), -0.18), (0.16, 1.7, 0.78), 'darkstone', angle)
        corner = angle + math.pi / 4
        model.tower((1.2 * math.cos(corner), 1.2 * math.sin(corner), -0.18), 0.22, 1.13, 'darkstone', 8, battlement='darkstone')
    keep(model, 0, 0, 1.35, 'darkstone', 'slate')
    return model


def keep_place(rng):
    model = Model()
    model.shadow(1.45)
    model.wall_ring(0.75, 0.32, 0.1, 8, 'stone')
    model.tower((0, 0, -0.18), 0.36, 1.43, 'stone', 8, battlement='stone')
    model.tower((0, 0, 1.25), 0.1, 0.35, 'wood')
    model.box((0, 0.08, 1.45), (0.03, 0.2, 0.12), 'banner')
    return model


def ruin(rng):
    model = Model()
    model.shadow(1.45)
    for i in range(7):
        angle = i * math.tau / 7 + rng.uniform(-0.2, 0.2)
        model.box((0.7 * math.cos(angle), 0.7 * math.sin(angle), -0.18), (0.14, rng.uniform(0.3, 0.6), rng.uniform(0.12, 0.6) + 0.13), 'stone', angle)
    model.tower((0.1, -0.1, -0.18), 0.28, 0.88, 'stone', 7)
    model.box((0.1, -0.1, 0.7), (0.3, 0.2, 0.08), 'moss', 0.4)
    for i in range(4):
        model.box((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), -0.18), (0.18, 0.12, 0.23), 'moss', rng.uniform(0, math.tau))
    return model


def graveyard(rng):
    model = Model()
    model.shadow(1.60)
    model.wall_ring(0.95, 0.1, 0.05, 12, 'wood')
    for row in range(-2, 3):
        for column in range(-2, 2):
            model.box((column * 0.3 + 0.15, row * 0.3, -0.18), (0.12, 0.05, rng.uniform(0.14, 0.22) + 0.13), 'bone')
    model.box((-0.7, 0, -0.18), (0.35, 0.3, 0.45), 'stone')
    model.gable((-0.7, 0, 0.3), (0.35, 0.3), 0.14, 'slate')
    return model


def ship(rng):
    model = Model()
    model.shadow(1.35, 0.80)
    hull = [(-0.75, 0), (-0.45, -0.25), (0.45, -0.25), (0.8, 0), (0.45, 0.25), (-0.45, 0.25)]
    for i in range(len(hull)):
        (x0, y0), (x1, y1) = hull[i], hull[(i + 1) % len(hull)]
        model.face([(x0 * 0.8, y0 * 0.6, -0.10), (x1 * 0.8, y1 * 0.6, -0.10), (x1, y1, 0.25), (x0, y0, 0.25)], 'wood')
    model.face([(x, y, 0.25) for x, y in hull], 'timber')
    model.box((0, 0, 0.25), (0.05, 0.05, 1.0), 'wood')
    model.box((0.02, 0, 0.45), (0.02, 0.62, 0.48), 'sail')
    return model


BUILDS = {'city': city, 'town': town, 'village': village, 'farm': farm, 'wealthy-home': wealthy_home, 'poor-home': poor_home,
          'tavern': tavern, 'temple': temple_place, 'cult': cult, 'coven': coven, 'labyrinth': labyrinth, 'keep': keep_place,
          'ruin': ruin, 'graveyard': graveyard, 'ship': ship}


def write(out):
    """Writes each kind's static mesh into out, and answers each kind's slot colours and role names."""
    out.mkdir(parents=True, exist_ok=True)
    slots, slot_names = {}, {}
    for kind, build in BUILDS.items():
        data, slots[kind], slot_names[kind] = build(random.Random(kind)).mesh(kind)
        (out / f'{kind}.rstatmsh').write_bytes(data)
    return slots, slot_names
