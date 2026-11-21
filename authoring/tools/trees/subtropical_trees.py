"""The distinct spiky palms and moss-covered forked snag of archive 501.

Stem and limb coordinates follow the painted image, x across, y down. Depth and
radius are fractions of its width, in the same frame as the rainforest recipes.
"""
import math

import numpy as np
from PIL import Image, ImageDraw

from rainforest_palms import add_grid, colour_ramp
from rainforest_trees import Tree, blade_atlas, filled, rosette


FORMS = {11: "leaning-palm", 16: "spike-palm", 19: "mossy-snag"}
STEMS = {
    11: ((.286, 1, 0, .055), (.315, .89, .01, .044), (.360, .78, .022, .036),
         (.403, .66, .035, .038), (.432, .56, .033, .045), (.453, .47, .017, .054),
         (.455, .415, 0, .051)),
    16: ((.555, 1, 0, .11), (.563, .964, .008, .074), (.579, .87, .022, .042),
         (.581, .75, .035, .040), (.57, .63, .031, .040), (.555, .54, .019, .044),
         (.550, .465, 0, .062), (.53, .403, -.01, .041)),
    19: ((.375, 1, 0, .153), (.36, .91, .015, .180), (.36, .80, .043, .155),
         (.389, .67, .053, .145), (.445, .53, .021, .169), (.548, .416, -.022, .154),
         (.640, .312, -.056, .118), (.732, .234, -.075, .076),
         (.834, .165, -.065, .048), (.878, .095, -.06, .023), (.938, .051, -.036, .007)),
}


def scale_bark(tree, lean):
    """Wrap the bark scales sampled along the leaning stem. Outside the exposed band,
    reflect the grain under the crown and into the foot."""
    pixels = tree.pixels
    h, w = pixels.shape[:2]
    lo, hi = (.665, .865) if lean else (.595, .845)
    result = np.empty((128, 96, 4), dtype=np.uint8)
    across = .50 + .34 * np.sin(np.arange(96) * math.tau / 96)
    for v in range(128):
        down = tree.stem[-1, 1] + v/127 * (1 - tree.stem[-1, 1])
        down = lo + (hi-lo) * (1 - abs((down-lo)/(hi-lo) % 2 - 1))
        row = pixels[round(down*(h-1))]
        centre = tree.uv(tree.stem_at(down))[0] * (w-1)
        # A low hanging blade can share a row with the trunk. Take the continuous
        # span nearest its axis and wrap the interior, where the scale marks are painted.
        occupied = np.flatnonzero(row[:, 3] > 128)
        runs = np.split(occupied, np.flatnonzero(np.diff(occupied) > 1) + 1)
        span = min(runs, key=lambda run: min(abs(run - centre)))
        samples = row[span[np.rint(across * (len(span)-1)).astype(int)]].copy()
        dark = samples[..., :3].max(axis=1) < 55
        # Lift painted outline ink using grain from the opposite side. On a lit
        # cylinder a broad strip of ink otherwise reads as a completely black face.
        opposite = np.roll(samples[:, :3], len(samples)//2, axis=0)
        samples[dark, :3] = np.rint(.56*samples[dark, :3] + .44*opposite[dark])
        result[v] = samples
    return Image.fromarray(result)


# Traces from the crown to the tip of two unobscured blades in each painting.
# The last value is the width sampled on either side of the rib, in image widths.
FRONDS = {
    11: (((.53,.455,.027), (.63,.47,.049), (.69,.534,.066), (.732,.627,.084),
          (.756,.712,.076), (.755,.797,.033), (.738,.87,.004)),
         ((.45,.438,.032), (.396,.331,.068), (.337,.24,.056), (.291,.16,.050),
          (.23,.101,.032), (.155,.069,.015), (.10,.053,.004))),
    16: (((.53,.436,.027), (.64,.430,.049), (.77,.451,.060), (.86,.493,.047),
          (.919,.553,.043), (.947,.642,.031), (.941,.726,.005)),
         ((.53,.43,.030), (.63,.338,.052), (.73,.261,.055), (.815,.237,.055),
          (.90,.245,.038), (.951,.284,.019), (.985,.331,.004))),
}


def palm_atlas(tree, record):
    """Straighten painted fronds into atlas cells. The folded 3D blades put the arch back.
    Keep the deep shadows, torn outer leaflets and open spaces visible in the painting."""
    h, w = tree.pixels.shape[:2]
    atlas = blade_atlas(tree, feathered=False)
    for cell, trace in enumerate(FRONDS[record]):
        knots = np.array(trace)
        spine = knots[:, :2] * [w-1, h-1]
        length = np.r_[0, np.cumsum(np.linalg.norm(np.diff(spine, axis=0), axis=1))]
        along = np.linspace(length[-1], 0, 96)
        middle = np.column_stack([np.interp(along, length, axis) for axis in spine.T])
        tangent = np.gradient(middle, axis=0)
        side = tangent[:, [1, 0]] * [-1, 1]
        side /= np.linalg.norm(side, axis=1, keepdims=True)
        radius = np.interp(along, length, knots[:, 2]) * w
        xy = middle[:, None, :] + (side*radius[:, None])[:, None, :] * np.linspace(-1, 1, 64)[None, :, None]
        x, y = np.moveaxis(np.rint(xy).astype(int), -1, 0)
        strip = tree.pixels[y.clip(0, h-1), x.clip(0, w-1)]
        rgb = strip[..., :3].astype(float)
        painted = (strip[..., 3] > 128) & (rgb[..., 1] >= rgb[..., 0]*.91)
        colour = np.asarray(filled(strip, painted))[..., :3].astype(float)
        # Other blades cross the trace near the hub. Keep a continuous feather
        # through these overlaps and use the torn painted edge farther out.
        tile = np.array(atlas.crop((cell*64, 0, (cell+1)*64, 96)))
        u = np.linspace(-1, 1, 64)
        core = np.abs(u)[None, :] < .62*np.sin(np.linspace(0, math.pi, 96))[:, None] + .14
        tile[..., 3] *= painted | core
        tile[..., :3] = np.rint(.58*colour + .42*tile[..., :3]).astype(np.uint8)
        atlas.paste(Image.fromarray(tile), (cell*64, 0))
    return atlas


def shoots(tree, record, texture):
    # The leaning palm has a mound of orange flowers at its foot. The upright
    # palm has short, dry shoots. Both use the actual base paint on radial sprays.
    h = tree.pixels.shape[0]
    base = tree.pixels[round(h*.86):]
    colours = base[base[..., 3] > 128, :3].astype(float)
    warm = colours[(colours[:, 0] > colours[:, 1]*1.16) & (colours[:, 1] > colours[:, 2]*1.23)]
    greens = colours[(colours[:, 1] >= colours[:, 0]) & (colours[:, 1] > colours[:, 2])]
    tile = Image.new("RGBA", (64, 96))
    draw = ImageDraw.Draw(tile)
    grass = colour_ramp(greens if record == 11 else warm, [.18, .39, .62, .85])
    for i in range(18):
        x = tree.rng.uniform(7, 57)
        tip = np.clip(x + tree.rng.uniform(-21, 21), 1, 62)
        y = tree.rng.uniform(14, 68)
        draw.polygon([(x-3, 94), ((x+tip)*.5-2, y+9), (tip, y), ((x+tip)*.5+2, y+16), (x+2, 93)],
                     fill=grass[i % len(grass)])
    if record == 11:
        petals = colour_ramp(warm, [.47, .79, .96])
        for i in range(12):
            x, y = tree.rng.uniform(8, 57), tree.rng.uniform(12, 62)
            draw.line([(32, 91), (x, y)], fill=grass[1], width=2)
            draw.ellipse((x-4, y-2, x+4, y+3), fill=petals[i % 2])
            draw.ellipse((x-2, y-5, x+2, y+3), fill=petals[1])
            draw.point((x, y), fill=petals[2])
    texture.paste(tile, (128, 0))
    extent = (.18 if record == 11 else .22) * tree.aspect
    height = .135 if record == 11 else .039
    base = tree.stem_at(.985)
    for i in range(6):
        angle = math.tau * (i + tree.rng.uniform(-.18, .18)) / 6
        ray = np.array([math.cos(angle), 0, math.sin(angle)])
        side = np.cross(ray, [0, 1, 0])
        rows, uv = [], []
        reach_scale = tree.rng.uniform(.82, 1.11)
        rise_scale = tree.rng.uniform(.64, 1.12)
        for t, reach, rise in ((0, .13, .06), (.46, .59, .59), (1, 1, .78)):
            centre = base + extent * reach * reach_scale * ray + [0, height * rise * rise_scale, 0]
            rows.append([centre + k * extent * .61 * side - k*k*extent*.14*ray
                         - [0, k*k*height*.26, 0] for k in (1, 0, -1)])
            uv.append([[(160 - k*31)/192, .99 - .98*t] for k in (1, 0, -1)])
        add_grid(tree.leaves, np.array(rows), uv, double=True)


def spiky_palm(tree, record):
    lean = record == 11
    top = 1 - tree.stem[-1, 1]
    tree.root_flare(.15 if lean else .22, 5, shoulder=.87, wrap_height=top)
    hub = tree.stem_at(tree.stem[-1, 1])
    # Outer blades droop well below the growing point. A separate fan of
    # upright spears cuts the tall, open profile above it.
    rosette(tree, hub, .55, .095, .38 if lean else .34, .081 if lean else .055, 10)
    rosette(tree, hub, .46, .19, .07, .060 if lean else .057, 8)
    rosette(tree, hub, .38, .15, -.45, .047 if lean else .032, 7)
    # Old leaf bases remain attached under the green crown. On the leaning palm
    # a narrow old frond hangs well below the others.
    for i in range(6):
        a = math.tau*i/6
        ray = np.array([math.cos(a), 0, math.sin(a)]) * tree.aspect
        drop = .20 if lean and i == 1 else tree.rng.uniform(.055, .105)
        centres = [hub, hub + ray*.075 - [0, .035, 0], hub + ray*.085 - [0, drop, 0]]
        tree.tube(centres, [.028, .035, .007], 5, cap=True, wrap_height=top)
    texture = palm_atlas(tree, record)
    shoots(tree, record, texture)
    return texture


def snag(tree):
    # The three split limbs overlap the main bole all the way through the fork.
    limbs = (
        ((.435, .525, .014, .14), (.34, .45, .07, .092), (.226, .379, .092, .069),
         (.148, .300, .082, .050), (.077, .232, .073, .028), (.051, .165, .065, .004)),
        ((.50, .43, -.012, .128), (.423, .369, -.11, .085), (.351, .276, -.165, .069),
         (.309, .165, -.18, .052), (.261, .096, -.16, .030), (.216, .037, -.14, .009)),
        ((.523, .449, .051, .125), (.562, .32, .142, .103), (.543, .218, .172, .080),
         (.572, .122, .142, .064), (.56, .04, .119, .032)),
        ((.633, .316, -.03, .080), (.686, .19, -.011, .047), (.676, .106, .012, .025), (.71, .045, .016, .008)),
        ((.345, .785, .057, .075), (.220, .746, .142, .041), (.141, .678, .172, .021), (.099, .61, .18, .004)),
        ((.59, .399, -.04, .052), (.705, .373, .031, .03), (.792, .326, .046, .009)),
    )
    for limb in limbs:
        tree.tube([tree.point(x, y, z) for x, y, z, _ in limb], [r for _, _, _, r in limb], 6, cap=True)
    splinters = (
        ((.32, .216, -.17, .036), (.34, .125, -.145, .023), (.375, .053, -.12, .008)),
        ((.139, .296, .080, .030), (.128, .222, .104, .020), (.169, .163, .121, .005)),
        ((.797, .187, -.071, .031), (.835, .123, -.102, .022), (.789, .077, -.13, .008)),
        ((.856, .130, -.063, .026), (.915, .116, -.022, .019), (.98, .015, .019, .004)),
        ((.57, .115, .145, .034), (.617, .110, .19, .023), (.64, .045, .212, .009)),
    )
    for limb in splinters:
        tree.tube([tree.point(x, y, z) for x, y, z, _ in limb], [r for _, _, _, r in limb], 4, cap=True)
    # Raised strands give the moss and bark furrows a shifting silhouette around the trunk.
    for i in range(4):
        centres, radii = [], []
        for t in np.linspace(0, 1, 10):
            down = .97 - .74*t
            az = i*math.pi/2 + t*3.9
            radius = np.interp(down, tree.stem[::-1, 1], tree.stem[::-1, 3]) * tree.aspect
            centres.append(tree.stem_at(down) + np.array([math.cos(az), 0, math.sin(az)]) * radius * .88)
            radii.append(.038 - .025*t)
        tree.tube(centres, radii, 5, cap=True)
    tree.root_flare(.37, 7, shoulder=.76)


def build(record, rgba):
    tree = Tree(record, rgba, STEMS[record])
    wrapped = None if record == 19 else 1 - tree.stem[-1, 1]
    tree.tube([tree.point(x, y, z) for x, y, z, _ in tree.stem], tree.stem[:, 3], 10, cap=True, wrap_height=wrapped)
    bark = filled(tree.pixels, tree.pixels[..., 3] > 128) if record == 19 else scale_bark(tree, record == 11)
    images = {"bark": bark}
    parts = [tree.bark]
    if record == 19:
        snag(tree)
    else:
        parts.append(tree.leaves)
        images["leaves"] = spiky_palm(tree, record)
    height = max(max(part["positions"][1::3]) for part in parts)
    return {"height": height, "parts": parts}, images
