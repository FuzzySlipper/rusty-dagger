"""forests: lightweight low-poly miniature tree-cluster models (.rstatmsh) for the 3D travel map (#11113).

Each model is a compact tabletop grove of 4–5 trees in house widths (matching places.py), standing on its origin with
deepened roots and a 6-triangle UV-mapped radial undergrowth shadow apron. Four climate silhouettes (< 25 KiB total):

  temperate  mixed broadleaf oak/beech crowns and conifers (High Rock woodlands & rainforests)
  pine       tiered alpine spruce/pine grove whose upper tiers frost bright white in winter (mountains & cold woods)
  palm       gently leaning oasis palms with radiating fronds and arid scrub bushes (Alik'r Desert & Sentinel)
  deadwood   gnarled dark twisted deadwood trunks and mossy swamp pines (swamps & haunted woodlands)
"""
import math
import random

from places import Model as PlaceModel

FOREST_COLOURS = {
    'trunk': '#5c4028',
    'bark': '#3c342e',
    'canopy': '#396228',
    'canopy-alt': '#4c7830',
    'pine': '#27482c',
    'pine-snow': '#335a36',
    'frond': '#567c32',
    'scrub': '#7a7e44',
    'moss-canopy': '#3a4a30',
    'shadow': '#141820',
}


class ForestModel(PlaceModel):
    """Extends places.Model with ultra-compact low-poly tree silhouettes and forest-palette slot export."""

    def undergrowth_shadow(self, radius, sides=5, lift=0.060, droop=0.045):
        """A 5-triangle radial shadow fan whose soft circular falloff comes from shadow.png."""
        centre = (0.0, 0.0, lift)
        outer = [(radius * math.cos(i * math.tau / sides), radius * math.sin(i * math.tau / sides), droop) for i in range(sides)]
        outer_uv = [(0.5 + 0.5 * math.cos(i * math.tau / sides), 0.5 + 0.5 * math.sin(i * math.tau / sides)) for i in range(sides)]
        for i in range(sides):
            j = (i + 1) % sides
            self.face([centre, outer[i], outer[j]], 'shadow', [(0.5, 0.5), outer_uv[i], outer_uv[j]])

    def trunk(self, x, y, radius, height, name='trunk', lean_x=0.0, lean_y=0.0, z0=-0.16):
        """A compact 3-triangle tapered trunk spire sunk into slopes at z0 and leaning to (x + lean_x, y + lean_y, height)."""
        sides = 3
        ring = [(x + radius * math.cos(i * math.tau / sides), y + radius * math.sin(i * math.tau / sides), z0) for i in range(sides)]
        tip = (x + lean_x, y + lean_y, height)
        for i in range(sides):
            self.face([ring[i], ring[(i + 1) % sides], tip], name)

    def pine_tree(self, x, y, scale, turn, bottom_name='pine', top_name='pine-snow'):
        """A crisp 2-tier low-poly spruce: lower boughs stay dark evergreen in winter while upper crown takes snow."""
        h = 0.94 * scale
        self.trunk(x, y, 0.068 * scale, h * 0.44, 'trunk')
        self.spire((x, y, h * 0.22), 0.34 * scale, h * 0.54, bottom_name, sides=4, turn=turn)
        self.spire((x, y, h * 0.48), 0.25 * scale, h * 0.54, top_name, sides=4, turn=turn + 0.40)

    def broadleaf_tree(self, x, y, scale, turn, crown_name='canopy'):
        """A sturdy oak/beech with a rounded flat-topped faceted canopy (clearly distinct from pointy conifers)."""
        h = 0.80 * scale
        self.trunk(x, y, 0.078 * scale, h * 0.54, 'trunk')
        # Wide lower skirt + beveled sides + flat top cap = rounded leafy oak crown in 10 triangles.
        z0 = h * 0.42
        z1 = h * 0.84
        r0 = 0.42 * scale
        r1 = 0.28 * scale
        sides = 4
        lo = [(x + r0 * math.cos(turn + i * math.tau / sides), y + r0 * math.sin(turn + i * math.tau / sides), z0) for i in range(sides)]
        hi = [(x + r1 * math.cos(turn + i * math.tau / sides), y + r1 * math.sin(turn + i * math.tau / sides), z1) for i in range(sides)]
        for i in range(sides):
            j = (i + 1) % sides
            self.face([lo[i], lo[j], hi[j], hi[i]], crown_name)
        self.face(hi, crown_name)

    def palm_tree(self, x, y, scale, turn):
        """A leaning oasis palm with 4 arched radiating flat-shaded fronds."""
        h = 0.96 * scale
        lean = 0.18 * scale
        lx, ly = lean * math.cos(turn), lean * math.sin(turn)
        self.trunk(x, y, 0.068 * scale, h + 0.04 * scale, 'trunk', lean_x=lx, lean_y=ly)
        cx, cy, cz = x + lx, y + ly, h
        fronds = 4
        for i in range(fronds):
            a = turn + i * math.tau / fronds
            ca, sa = math.cos(a), math.sin(a)
            px, py = -sa, ca
            mid_r, tip_r = 0.24 * scale, 0.46 * scale
            half_w = 0.11 * scale
            p0 = (cx, cy, cz + 0.02 * scale)
            p_left = (cx + mid_r * ca + half_w * px, cy + mid_r * sa + half_w * py, cz + 0.10 * scale)
            p_right = (cx + mid_r * ca - half_w * px, cy + mid_r * sa - half_w * py, cz + 0.10 * scale)
            p_tip = (cx + tip_r * ca, cy + tip_r * sa, cz - 0.08 * scale)
            self.face([p0, p_right, p_left], 'frond')
            self.face([p_left, p_right, p_tip], 'frond')

    def scrub_bush(self, x, y, scale, turn, name='scrub'):
        """A low mounded desert/heath bush (3-sided pyramid)."""
        self.spire((x, y, -0.05), 0.30 * scale, 0.25 * scale, name, sides=3, turn=turn)

    def dead_tree(self, x, y, scale, turn):
        """A crooked dark deadwood trunk with two gnarled bare limbs."""
        h = 0.84 * scale
        lx, ly = 0.15 * scale * math.cos(turn), 0.15 * scale * math.sin(turn)
        self.trunk(x, y, 0.072 * scale, h, 'bark', lean_x=lx, lean_y=ly)
        for az in (turn + 1.15, turn - 1.35):
            bx, by, bz = x + lx * 0.56, y + ly * 0.56, h * 0.50
            ex, ey, ez = bx + 0.30 * scale * math.cos(az), by + 0.30 * scale * math.sin(az), bz + 0.26 * scale
            w = 0.052 * scale
            px, py = -math.sin(az) * w, math.cos(az) * w
            # Upward-angled crooked branch wedge (visible from overhead map camera angles).
            self.face([(bx - px, by - py, bz), (bx + px, by + py, bz), (ex, ey, ez)], 'bark')

    def forest_mesh(self, name):
        """Exports RMSHLE02 using FOREST_COLOURS instead of places.COLOURS."""
        import places as _places
        saved = _places.COLOURS
        try:
            _places.COLOURS = FOREST_COLOURS
            return self.mesh(f'forest-{name}')
        finally:
            _places.COLOURS = saved


def temperate(rng):
    """Mixed High Rock woodland clump: 3 oak/beech broadleaves + 1 conifer over soft undergrowth shadow."""
    model = ForestModel()
    model.undergrowth_shadow(1.42)
    spots = [
        (0.0, 0.06, 1.16, 'broad', 'canopy'),
        (-0.52, 0.32, 0.98, 'broad', 'canopy-alt'),
        (0.54, 0.20, 1.04, 'pine', 'pine'),
        (-0.18, -0.48, 0.94, 'broad', 'canopy'),
    ]
    for x, y, sc, kind, col in spots:
        turn = rng.uniform(0, math.tau)
        if kind == 'pine':
            model.pine_tree(x, y, sc, turn)
        else:
            model.broadleaf_tree(x, y, sc, turn, col)
    return model


def pine(rng):
    """Alpine & cold-woods conifer clump: 4 tiered spruces whose upper tiers take crisp winter snow."""
    model = ForestModel()
    model.undergrowth_shadow(1.40)
    spots = [
        (0.02, 0.04, 1.22),
        (-0.50, 0.34, 1.00),
        (0.54, 0.24, 1.06),
        (-0.16, -0.50, 0.92),
    ]
    for x, y, sc in spots:
        model.pine_tree(x, y, sc, rng.uniform(0, math.tau))
    return model


def palm(rng):
    """Alik'r Desert / Sentinel oasis palm & arid scrub grove."""
    model = ForestModel()
    model.undergrowth_shadow(1.36)
    for x, y, sc in ((0.0, 0.06, 1.16), (-0.48, 0.24, 0.94), (0.48, -0.22, 1.00)):
        model.palm_tree(x, y, sc, rng.uniform(0, math.tau))
    for x, y, sc in ((0.36, 0.40, 1.02), (-0.42, -0.36, 0.92)):
        model.scrub_bush(x, y, sc, rng.uniform(0, math.tau))
    return model


def deadwood(rng):
    """Swamp & haunted-woodland gnarled deadwood and dark mossy pines."""
    model = ForestModel()
    model.undergrowth_shadow(1.38)
    for x, y, sc in ((0.0, 0.05, 1.16), (-0.50, 0.26, 0.94), (0.46, -0.32, 0.98)):
        model.dead_tree(x, y, sc, rng.uniform(0, math.tau))
    for x, y, sc in ((0.42, 0.34, 0.96), (-0.46, -0.34, 0.88)):
        model.pine_tree(x, y, sc, rng.uniform(0, math.tau), bottom_name='moss-canopy', top_name='moss-canopy')
    return model


BUILDS = {'temperate': temperate, 'pine': pine, 'palm': palm, 'deadwood': deadwood}


def write(out):
    """Writes each forest clump static mesh into out, and answers each kind's slot colours and role names."""
    out.mkdir(parents=True, exist_ok=True)
    slots, slot_names = {}, {}
    total_bytes = 0
    for kind, build in BUILDS.items():
        data, slots[kind], slot_names[kind] = build(random.Random(f'forest-{kind}')).forest_mesh(kind)
        (out / f'{kind}.rstatmsh').write_bytes(data)
        total_bytes += len(data)
    return slots, slot_names, total_bytes
