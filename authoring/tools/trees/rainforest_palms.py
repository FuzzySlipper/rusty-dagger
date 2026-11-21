"""Archive 500's three palms. Dimensions are fractions of the painted tree's height.

Each trunk is a single connected tube from the soil into the crown. The green and dry
fronds share one masked atlas, folded down each side of the rib and arched in depth.
"""
import math
from dataclasses import dataclass

import numpy as np
from PIL import Image, ImageDraw


@dataclass(frozen=True)
class Palm:
    # Height, horizontal centre and radius of each trunk ring, starting below the soil.
    stem: tuple
    crown_radius: float
    frond_width: float
    tip_drop: float
    skirt: float
    roots: float
    skirt_rows: tuple  # upper and lower edges of dry fronds, down from the painted top
    bark_rows: tuple  # bare trunk visible in the sprite, measured up from its foot


PALMS = {
    12: Palm(((-.012, 0, .094), (.045, .008, .082), (.13, .012, .058), (.36, -.012, .051),
              (.54, .005, .048), (.70, -.037, .057), (.82, -.072, .047)), .33, .098, .24, 0, .21, (), (.08, .57)),
    14: Palm(((-.01, 0, .094), (.04, -.002, .067), (.20, .002, .049), (.43, -.012, .059),
              (.62, -.028, .071), (.79, -.033, .051)), .46, .114, .23, .38, .13, (.27, .635), (.045, .39)),
    15: Palm(((-.008, 0, .061), (.035, -.002, .044), (.25, .005, .030), (.53, -.002, .038),
              (.68, -.01, .046), (.82, -.014, .034)), .345, .080, .15, .32, .084, (.205, .435), (.04, .55)),
}


def part(role):
    return dict(role=role, positions=[], normals=[], uvs=[], colors=[], indices=[])


def add_grid(out, positions, uvs, *, seam=False, double=False):
    """Join consecutive rows of corners, keeping the two ends of a wrapped UV seam."""
    rows, cols, _ = positions.shape
    normal = np.zeros_like(positions)
    faces = []
    for row in range(rows - 1):
        for col in range(cols - 1):
            a = row * cols + col
            for tri in ((a, a + cols, a + 1), (a + 1, a + cols, a + cols + 1)):
                p, q, r = positions.reshape(-1, 3)[list(tri)]
                normal.reshape(-1, 3)[list(tri)] += np.cross(q - p, r - p)
                faces.extend(tri)
                if double:
                    faces.extend(reversed(tri))
    if seam:  # bark rings duplicate the UV seam; both copies shade alike
        normal[:, 0] += normal[:, -1]
        normal[:, -1] = normal[:, 0]
    normal /= np.linalg.norm(normal, axis=2, keepdims=True)
    start = len(out["positions"]) // 3
    out["indices"].extend(start + i for i in faces)
    out["positions"].extend(positions.round(6).ravel().tolist())
    out["normals"].extend(normal.round(6).ravel().tolist())
    out["uvs"].extend(np.asarray(uvs).round(6).ravel().tolist())
    # Leaf alpha participates in the game's cutout as well as wind; keep painted leaflets solid.
    out["colors"].extend([1, 1, 1, int(out["role"] == "leaves")] * (rows * cols))


def trunk(out, palm):
    around = np.linspace(0, 2 * math.pi, 11)
    rings, uv = [], []
    for y, x, radius in palm.stem:
        rings.append([[x + radius * math.cos(a), y, radius * math.sin(a)] for a in around])
        uv.append([[a / (2 * math.pi), 1 - y / palm.stem[-1][0]] for a in around])
    add_grid(out, np.array(rings), uv, seam=True)
    # Close the hub where the fronds emerge; a gap there shows from above.
    count = len(around)
    tip = len(out["positions"]) // 3
    out["positions"].extend([x, y + .008, 0])
    out["normals"].extend([0, 1, 0])
    out["colors"].extend([1, 1, 1, 0])
    out["uvs"].extend([.5, 0])
    for i in range(tip - count, tip - 1):
        out["indices"].extend([i + 1, i, tip])


def roots(out, palm, rng):
    # Swept root flares intersect the first two stem rings and taper into the soil.
    for i in range(7):
        az = (i + rng.uniform(-.22, .22)) * math.tau / 7
        radial = np.array([math.cos(az), 0, math.sin(az)])
        side = np.array([-math.sin(az), 0, math.cos(az)])
        length = palm.roots * rng.uniform(.74, 1.06)
        rows, uv = [], []
        for t, y, width in ((0, .105, .038), (.53, .032, .026), (1, -.005, .006)):
            center = radial * (.023 + t * length)
            rows.append([center + k * side * width + [0, y - abs(k) * width, 0] for k in (1, 0, -1)])
            uv.append([[(i + u) / 7, .93 + .07 * t] for u in (0, .5, 1)])
        # Open ridges, viewed from above. They meet the stem and the underground base.
        add_grid(out, np.array(rows), uv)


def colour_ramp(pixels, shares):
    ordered = pixels[np.argsort(pixels @ [.2126, .7152, .0722])]
    return [tuple(map(int, c)) + (255,) for c in ordered[(np.array(shares) * (len(ordered) - 1)).astype(int)]]


def skirt_colours(tile, rgba, rows):
    """Keep the sprite's vertical value pattern: honey-coloured upper blades over old dark tips."""
    mask = rgba[..., 3] > 128
    yy = np.nonzero(mask)[0]
    foot, height = yy.max(), yy.max() - yy.min()
    # Sample wood directly under the crown along the trunk axis. Foliage and sky
    # sometimes overlap it in the painting, so take only its brown and near-black paint.
    centre = np.median(np.where(mask[int(foot - .16 * height)])[0])
    half = height * .115
    strip = rgba[:, max(0, int(centre - half)):int(centre + half), :].astype(float)
    ink = (strip[..., 3] > 128) & (strip[..., 0] >= strip[..., 1]) & (strip[..., 1] >= strip[..., 2])
    art = np.array(tile)
    tone = art[..., 0].copy()
    values = np.unique(tone[art[..., 3] > 0])
    ranks = np.searchsorted(values, tone)
    for y in range(tile.height):
        t = 1 - y / (tile.height - 1)
        source_y = yy.min() + height * (rows[0] + (rows[1] - rows[0]) * t)
        band = slice(int(source_y) - 4, int(source_y) + 5)
        paint = strip[band, :, :3][ink[band]]
        if len(paint):
            ramp = np.array(colour_ramp(paint, np.linspace(.16, .95, len(values))))
            drawn = art[y, :, 3] > 0
            art[y, drawn] = ramp[ranks[y, drawn]]
    return Image.fromarray(art)


def frond_atlas(rgba, palm, rng):
    """Paint broad overlapping leaflets, with open notches toward their tips and a solid central rib."""
    pixels = rgba[rgba[..., 3] > 128, :3].astype(float)
    green = pixels[(pixels[:, 1] > pixels[:, 0] * 1.04) & (pixels[:, 1] > pixels[:, 2] * 1.12)]
    ochre = pixels[(pixels[:, 0] > pixels[:, 1] * 1.2) & (pixels[:, 1] > pixels[:, 2] * 1.22)]
    atlas = Image.new("RGBA", (192, 128))
    for cell in range(3):
        dry = cell == 2 and palm.skirt > 0
        # Shaded lower fronds keep the deep values painted beneath the green crown.
        shares = [.04, .12, .25, .45, .68, .88] if cell == 0 else [.12, .32, .52, .70, .88, .97]
        ramp = colour_ramp(ochre if dry else green, shares)
        tile = Image.new("RGBA", (64, 128))
        draw = ImageDraw.Draw(tile)
        if dry:
            # Several torn old blades on one hanging frond; the dark notches open at the hem.
            for j in range(5):
                x = 8 + 12 * j
                end = int(rng.integers(2, 28))
                tip = x + rng.uniform(-5, 5)
                shade = int(rng.choice((2, 3, 4)))
                draw.polygon([(30, 127), (x - 8, 78), (x - 5, 37), (tip, end),
                              (x + 6, 39), (x + 8, 87), (35, 127)], fill=ramp[shade])
                draw.polygon([(30, 126), (x - 8, 78), (x - 5, 37), (tip, end), (x, 72)], fill=ramp[1])
                draw.line([(x, 82), (tip, end + 8)], fill=ramp[5], width=2)
        else:
            # Broad at the shoulder, narrowing into a short terminal leaflet. The tips
            # overlap their neighbours less than the bases, opening the feathered outline.
            for sign in (-1, 1):
                for i in range(12):
                    t = i / 12
                    y = 114 - t * 102 + rng.uniform(-1.5, 1.5)
                    width = (4 + 27 * math.sin(math.pi * (.10 + .88 * t)) ** .85) * rng.uniform(.84, 1)
                    x = 32 + sign * width
                    tip = (x, y - width * rng.uniform(.56, .86))
                    elbow = (32 + width * sign * .69, y - 1)
                    shade = int(rng.choice((1, 2, 3, 3))) + (sign == 1)
                    draw.polygon([(32, y + 10), elbow, tip, (32, y - 4)], fill=ramp[shade])
                    draw.polygon([(32, y + 10), elbow, tip, (32 + width * sign * .58, y - 2)], fill=ramp[max(0, shade - 2)])
            draw.polygon([(30, 126), (30, 37), (28, 15), (32, 2), (36, 14), (34, 38), (33, 126)], fill=ramp[3])
        if dry:
            tile = skirt_colours(tile, rgba, palm.skirt_rows)
        atlas.paste(tile, (cell * 64, 0))
    return atlas


def bark_wrap(rgba, palm):
    """Wrap the painted stem grain around the tube, using the visible wood between root and collar."""
    rows = np.where((rgba[..., 3] > 128).any(1))[0]
    top, foot = rows[0], rows[-1]
    height = foot - top
    result = np.empty((128, 96, 4), np.uint8)
    # A cylindrical wrap through both faces of the painted strip has a continuous seam.
    across = .5 + .43 * np.sin(np.arange(96) * math.tau / 96)
    for v in range(128):
        y = (1 - v / 127) * palm.stem[-1][0]
        lo, hi = palm.bark_rows
        # Reflect at either end of the visible grain to paint the wood under roots and fronds.
        t = 1 - abs(((y - lo) / (hi - lo)) % 2 - 1)
        row = rgba[round(foot - height * (lo + t * (hi - lo)))]
        occupied = np.flatnonzero(row[:, 3] > 128)
        result[v] = row[occupied[np.rint(across * (len(occupied) - 1)).astype(int)]]
    result[..., 3] = 255
    return Image.fromarray(result)


def frond(out, hub, azimuth, reach, lift, drop, width, twist, cell, hanging=False):
    radial = np.array([math.cos(azimuth), 0, math.sin(azimuth)])
    side = np.array([-radial[2], 0, radial[0]])
    rows, uv = [], []
    steps = (0, .35, .7, 1) if hanging else (0, .24, .48, .73, 1)
    for t in steps:
        arch = 4 * t * (1 - t)
        distance = (.7 * t + .65 * arch) if hanging else t
        centre = hub + radial * reach * distance + side * twist * t * t + [0, lift * arch - drop * t * t, 0]
        # A keel down the rib and pitched wings give edge-on leaves depth.
        span = width * (.26 + .74 * math.sin(math.pi * t) ** .65)
        wings = (1, -1) if hanging else (1, 0, -1)
        rows.append([centre + k * side * span + [0, -abs(k) * span * .45, 0] for k in wings])
        uv.append([[(cell * 64 + 32 - k * 31.5) / 192, (127 - 126 * t) / 128] for k in wings])
    add_grid(out, np.array(rows), uv, double=True)


def build(record, rgba):
    palm = PALMS[record]
    rng = np.random.default_rng(record)
    bark, leaves = part("bark"), part("leaves")
    trunk(bark, palm)
    roots(bark, palm, rng)
    y, x, _ = palm.stem[-1]
    hub = np.array([x, y, 0])
    # Stagger two radial rows. One rises through the centre, the longer outside row hangs down.
    for row, count, reach, lift, drop in ((0, 8, 1, .11, palm.tip_drop), (1, 6, .84, .17, -.016)):
        for i in range(count):
            angle = (i + .46 * row + rng.uniform(-.22, .22)) * math.tau / count
            # 14 has one high, sweeping frond above a lopsided, low outer crown.
            arch = lift * (1.5 if record == 14 and row == 1 and i == 0 else 1)
            frond(leaves, hub, angle, palm.crown_radius * reach * rng.uniform(.86, 1.06), arch * rng.uniform(.58, 1.3),
                  drop * rng.uniform(.75, 1.18), palm.frond_width * rng.uniform(.82, 1.15), rng.uniform(-.085, .085), row)
    if record == 12:
        # Short upright growth fills the middle of this compact, green-headed palm.
        for i in range(4):
            frond(leaves, hub, .6 + i * math.tau / 4, palm.crown_radius * .54,
                  .16, .005, palm.frond_width * .80, rng.uniform(-.04, .04), 1)
    if palm.skirt:
        # Dead fronds form a dense hanging collar. Each starts at the same living crown.
        for i in range(14):
            # Two staggered lengths break up the hem and expose the shadow between blades.
            short = i % 2
            angle = (i + rng.uniform(-.18, .18)) * math.tau / 14
            frond(leaves, hub, angle, palm.frond_width * (1.30 if short else 1.02) * rng.uniform(.90, 1.20), .012,
                  palm.skirt * (.73 if short else 1.02) * rng.uniform(.89, 1.08),
                  palm.frond_width * .72, rng.uniform(-.03, .03), 2, hanging=True)
    top = max(leaves["positions"][1::3])
    return {"height": top, "parts": [bark, leaves]}, bark_wrap(rgba, palm), frond_atlas(rgba, palm, rng)
