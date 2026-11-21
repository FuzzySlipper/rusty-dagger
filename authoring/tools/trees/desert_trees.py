"""Desert nature paintings in the round. Traces are x, down, depth and radius
in the refreshed image's coordinates, using the same frame as rainforest_trees.Tree.
"""
import math

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt

from rainforest_palms import add_grid, colour_ramp
from rainforest_trees import Tree
from subtropical_trees import palm_atlas, spiky_fan


FORMS = {5: "terraced-acacia", 13: "desert-fan-palm", 23: "desert-thorn"}

STEMS = {
    5: ((.522, 1, 0, .091), (.522, .936, 0, .105), (.526, .864, .014, .062), (.533, .760, .021, .041),
        (.534, .638, .002, .036), (.521, .548, -.023, .032), (.489, .465, -.044, .029),
        (.446, .422, -.038, .024), (.398, .333, -.014, .020), (.385, .269, .013, .017), (.376, .167, .031, .012)),
    13: ((.398, 1, 0, .105), (.398, .962, 0, .094), (.392, .904, .005, .074), (.396, .833, .028, .058),
         (.406, .753, .040, .053), (.433, .670, .025, .056), (.455, .602, -.010, .064),
         (.460, .535, -.029, .086), (.478, .473, -.020, .083), (.489, .438, .012, .060)),
    23: ((.531, 1, 0, .076), (.531, .946, 0, .080), (.519, .864, .010, .061), (.526, .777, .021, .053),
         (.529, .670, .008, .042), (.547, .566, -.016, .031), (.546, .466, -.028, .024),
         (.553, .380, -.022, .018), (.541, .300, -.007, .012), (.519, .230, -.016, .005)),
}

# Record five's main fork carries distinct tiers. The low left arm and the upper
# right arm stay clear of the top, including from the ground beneath the branches.
ACACIA_ARMS = (
    ((.533, .708, .010, .029), (.482, .668, .062, .024), (.415, .646, .118, .019),
     (.373, .603, .129, .016), (.309, .581, .138, .008)),
    ((.522, .618, 0, .036), (.570, .588, .028, .028), (.636, .533, .044, .026),
     (.677, .496, .064, .022), (.676, .430, .088, .018), (.650, .380, .120, .010)),
    ((.671, .465, .074, .020), (.696, .409, .006, .018), (.699, .334, -.054, .015),
     (.700, .252, -.081, .012), (.734, .170, -.111, .007)),
    ((.489, .462, -.044, .025), (.502, .412, -.090, .020), (.531, .368, -.148, .016),
     (.526, .290, -.211, .014), (.555, .214, -.249, .008)),
    ((.406, .350, -.014, .019), (.452, .320, .064, .015), (.467, .253, .106, .015),
     (.445, .181, .135, .012), (.463, .109, .143, .007)),
    ((.388, .284, .011, .019), (.308, .278, .032, .016), (.254, .257, .074, .013),
     (.224, .228, .099, .011), (.151, .211, .110, .006)),
    ((.376, .224, .018, .014), (.337, .188, -.056, .012), (.293, .148, -.096, .008)),
)

# Tiny tufts on a broad, stunted crown. The amber twig ends are as visible
# against the sky as the dark limbs, so they are modeled all the way to their tips.
THORN_ARMS = (
    ((.523, .827, .014, .052), (.450, .716, .036, .044), (.405, .621, .066, .032),
     (.363, .547, .074, .023), (.294, .485, .083, .020), (.253, .410, .103, .012),
     (.224, .333, .122, .007), (.201, .295, .119, .003)),
    ((.409, .632, .061, .032), (.372, .558, .022, .027), (.369, .480, -.009, .022),
     (.384, .392, -.014, .017), (.399, .296, -.041, .012), (.414, .210, -.045, .012), (.432, .117, -.035, .005)),
    ((.375, .503, 0, .020), (.347, .450, -.061, .014), (.342, .378, -.103, .010), (.317, .302, -.119, .005)),
    ((.437, .712, .038, .034), (.374, .651, .101, .024), (.299, .628, .135, .019),
     (.233, .566, .146, .015), (.185, .554, .162, .014), (.153, .488, .165, .012),
     (.137, .389, .158, .008), (.153, .316, .150, .003)),
    ((.206, .559, .154, .014), (.175, .508, .220, .010), (.127, .456, .252, .008),
     (.081, .445, .276, .006), (.045, .390, .29, .004), (.017, .326, .287, .002)),
    ((.351, .649, .108, .021), (.271, .680, .106, .014), (.208, .674, .085, .010),
     (.170, .646, .073, .007), (.143, .593, .061, .003)),
    ((.527, .784, .019, .049), (.591, .688, -.007, .039), (.626, .603, -.024, .031),
     (.677, .530, -.053, .024), (.712, .433, -.073, .019), (.773, .391, -.084, .012),
     (.801, .303, -.075, .007), (.807, .270, -.066, .003)),
    ((.680, .517, -.057, .024), (.722, .509, .027, .019), (.779, .532, .068, .016),
     (.855, .533, .081, .015), (.893, .497, .096, .011), (.918, .417, .108, .008),
     (.942, .360, .114, .006), (.950, .290, .118, .003)),
    ((.604, .674, -.013, .026), (.663, .636, -.095, .019), (.719, .658, -.147, .013),
     (.788, .648, -.165, .010), (.822, .601, -.178, .005), (.845, .578, -.185, .002)),
    ((.547, .546, -.017, .027), (.569, .472, -.081, .019), (.581, .389, -.145, .016),
     (.629, .329, -.198, .013), (.651, .278, -.205, .011), (.654, .184, -.193, .006)),
    ((.524, .750, .016, .036), (.485, .661, -.090, .024), (.474, .578, -.180, .016),
     (.441, .517, -.243, .013), (.411, .430, -.281, .009), (.420, .357, -.326, .004)),
    ((.544, .630, 0, .035), (.543, .551, .094, .025), (.576, .491, .169, .020),
     (.584, .420, .215, .015), (.623, .378, .246, .010), (.631, .315, .278, .004)),
)

# x, down, depth, half-width, half-height. Each cluster is textured from its
# own position in the painting, repeated around a fixed three-dimensional hub.
ACACIA_PADS = (
    (.305, .570, .134, .187, .054), (.415, .553, .085, .123, .060),
    (.633, .315, .107, .199, .065), (.767, .397, .087, .155, .055),
    (.778, .166, -.102, .210, .071), (.564, .234, -.243, .132, .061),
    (.290, .130, -.092, .179, .074), (.436, .090, .105, .163, .078),
    (.157, .211, .111, .155, .049), (.366, .190, .016, .148, .058),
)
THORN_PADS = (
    (.428, .109, -.035, .082, .110), (.648, .172, -.190, .061, .105),
    (.943, .283, .117, .052, .077), (.794, .288, -.071, .056, .082),
    (.154, .322, .153, .060, .076), (.312, .302, -.118, .075, .064),
)


def wood_colours(tree):
    rgb = tree.pixels[..., :3].astype(float)
    warm = (tree.pixels[..., 3] > 128) & (rgb[..., 0] > rgb[..., 1]*1.035) & (rgb[..., 1] >= rgb[..., 2]*.97)
    ink = (tree.pixels[..., 3] > 128) & (rgb.max(axis=2) < 52)
    down = np.arange(len(rgb))[:, None] / len(rgb)
    return rgb[(warm | ink) & (down > .59)], rgb[warm & (down < .58)]


def trunk_paint(tree, size, band):
    """Wrap the highlights and ink on the exposed shaft into a seamless chart."""
    h, w = tree.pixels.shape[:2]
    path = tree.stem[::-1]
    down = np.linspace(*band, size[1])
    axis = np.interp(down, path[:, 1], path[:, 0])
    radius = np.interp(down, path[:, 1], path[:, 3])
    across = np.sin(np.arange(size[0])*math.tau/size[0]+.4)
    x = np.rint((axis[:, None]+.83*radius[:, None]*across)*(w-1)).astype(int)
    y = np.rint(down*(h-1)).astype(int)
    return tree.pixels[y[:, None], x].astype(float)


def bark_tile(tree, record):
    """Carry the painted palette around the limb in narrow, tapering furrows.
    V runs from young twig to heavy wood, so the thorn's pale tips grow out
    of its dark forks without a chart change at the join.
    """
    base, tips = wood_colours(tree)
    ranks = [.04, .13, .25, .40, .59, .76, .91, .985]
    bark = np.array(colour_ramp(base, ranks), dtype=float)
    fresh = np.array(colour_ramp(tips, [.14, .28, .41, .53, .66, .79, .91, .98]), dtype=float)
    y, x = np.indices((128, 96))
    u, v = x/96, y/127
    phase = math.tau*u
    flow = phase+.13*np.sin(2*phase+v*8)+.054*np.cos(3*phase-v*19)
    tone = (.49+.19*np.sin(5*flow)+.14*np.sin(11*flow+.9)+.10*np.cos(18*flow-v*3)
            +.064*np.cos(7*phase+v*25)+tree.rng.uniform(-.045, .045, v.shape))
    crevice = np.cos(9*flow+v*4) > .80+.15*np.sin(phase*3-v*17)
    levels = np.clip(np.rint(7*(tone-.28*crevice)).astype(int), 0, 7)
    young = np.clip((.56-v)/.48, 0, 1)[..., None] * (1 if record == 23 else .62)
    if record == 23:
        young_levels = np.minimum(7, levels+2)
    else:
        young_levels = levels
    paint = trunk_paint(tree, (96, 128), (.52, .975) if record == 5 else (.67, .963))
    # Keep the original bark strokes under the grooves. Thin tips pick up the
    # ochre of the twigs and thick fork junctions retain the dark painted ink.
    share = (.38 if record == 5 else .48)*(paint[..., 3:4]>128)
    bole = bark[levels]*(1-share)+paint*share
    return Image.fromarray(np.rint(bole*(1-young)+fresh[young_levels]*young).astype(np.uint8))


def wood_limb(tree, centres, radii, sides, *, root=False):
    start = len(tree.bark["uvs"])
    tree.tube(centres, radii, sides, cap=True, wrap_height=1)
    # Match the wood's age at each section. Phase the ridges around the join.
    radii = np.array(radii)
    v = np.interp(radii, [.002, .009, .022, .045, .080, .110], [.035, .18, .40, .66, .86, .99])
    if root:
        v = np.linspace(.97, .67, len(centres))
    az = centres[0][0]*1.6 + centres[0][2]*.8
    u = np.linspace(az, az+1, sides+1)
    uv = np.stack(np.broadcast_arrays(u[None, :], v[:, None]), axis=-1)
    tree.bark["uvs"][start:start+uv.size] = uv.ravel().tolist()
    tree.bark["uvs"][start+uv.size:] = [.5, v[-1]] * (sides+1)


def limb(tree, trace, sides):
    wood_limb(tree, [tree.point(x, y, z) for x, y, z, _ in trace], [r for _, _, _, r in trace], sides)


def acacia_pad(tree, patch, centre, width, height):
    """Bow each spray over its supporting twig. Both windings carry leaf cutout."""
    index = len(tree.patches)
    tree.patches.append(patch)
    offset = tree.rng.uniform(0, math.tau)
    for i in range(5):
        az = offset + i*math.tau/5
        ray = np.array([math.cos(az), 0, math.sin(az)])
        side = np.cross(ray, [0, 1, 0])
        rows, uv = [], []
        for t, reach, lift, span in ((0, -.41, .75, .63), (.46, .50, .45, .87), (1, 1, -.82, .71)):
            rows.append([centre + ray*width*(reach-.25*k*k) + side*width*k*span
                         + [0, height*(lift-.23*k*k), 0] for k in (1, 0, -1)])
            uv.append([[(index%8 + u)/8, index//8 + .01+.98*t] for u in (.008, .5, .992)])
        add_grid(tree.leaves, np.array(rows), uv, double=True)


def upright_tuft(tree, patch, centre, width, height):
    # A tuft is a few upright leaves on a fork. Let the painted speckled tips
    # stick up into the sky and tuck the darker base back into the supporting twig.
    index = len(tree.patches)
    tree.patches.append(patch)
    for az in tree.rng.uniform(0, .5) + np.arange(3)*math.tau/3:
        ray = np.array([math.cos(az), 0, math.sin(az)])
        side = np.cross(ray, [0, 1, 0])
        rows, uv = [], []
        for t, reach, y, span in ((0, .51, .99, .67), (.53, .42, .05, .88), (1, -.03, -.71, .49)):
            rows.append([centre+ray*width*(reach-.14*k*k)+side*k*width*span+
                         [0, height*(y-.19*k*k), 0] for k in (1, 0, -1)])
            uv.append([[(index%8+u)/8, index//8+.01+.98*t] for u in (.008, .5, .992)])
        add_grid(tree.leaves, np.array(rows), uv, double=True)


def clusters(tree, record, foliage):
    pads = ACACIA_PADS if record == 5 else THORN_PADS
    for x, down, z, width, height in pads:
        w, h = foliage.size
        box = (round((x-width)*w), round((down-height)*h), round((x+width)*w), round((down+height)*h))
        patch = np.array(foliage.crop(box).resize((64, 56), Image.Resampling.NEAREST))
        py, px = np.indices(patch.shape[:2])
        px, py = (px-31.5)/32, (py-27.5)/28
        angle = np.arctan2(py, px)
        edge = .90+.067*np.sin(7*angle+x*20)+.055*np.cos(11*angle+down*10)
        patch[..., 3] *= px*px+py*py < edge**2
        centre = tree.point(x, down, z)
        if record == 23:
            upright_tuft(tree, Image.fromarray(patch), centre, width*tree.aspect, height)
        else:
            acacia_pad(tree, Image.fromarray(patch), centre, width*tree.aspect, height*1.25)
    if record == 5:
        # Beside the roots the original has low scrub with a dark centre and
        # upright shoots. Use the actual tuft outline right down to the soil.
        w, h = tree.pixels.shape[1], tree.pixels.shape[0]
        tuft = Image.fromarray(tree.pixels).crop((round(.310*w), round(.789*h), round(.489*w), round(.930*h)))
        tuft = tuft.resize((64, 56), Image.Resampling.NEAREST)
        for x, down, z, width, height in ((.390, .905, .040, .092, .069), (.432, .910, -.028, .061, .045),
                                     (.631, .898, -.018, .040, .031)):
            upright_tuft(tree, tuft, tree.point(x, down, z), width*tree.aspect, height)
    return tree.cluster_atlas()


def branch_tree(tree, record):
    limb(tree, tree.stem, 9)
    arms = ACACIA_ARMS if record == 5 else THORN_ARMS
    for arm in arms:
        limb(tree, arm, 6 if record == 5 else 5)
    # Short, asymmetric forks carry individual lobes and leave gaps in the crown.
    for i, arm in enumerate(arms):
        for j in (-3, -2):
            x, y, z, radius = arm[j]
            bearing = i*2.40+j*.76
            spread = .095 if record == 5 else .078
            ray = np.array([math.cos(bearing), 0, math.sin(bearing)])*spread*tree.aspect
            start = tree.point(x, y, z)
            elbow = start + ray*.55 + [0, .014, 0]
            wood_limb(tree, [start, elbow, start + ray + [0, .063 if record == 5 else .09, 0]],
                      [radius*.68, radius*.45, .0025], 4)

    if record == 23:
        # Pale, leafless terminal shoots between the darker forked stems.
        for i in (1, 3, 6, 8, 9, 10):
            x, y, z, _ = arms[i][-3]
            tip = tree.point(x, y, z)
            sign = 1 if i%2 else -1
            wood_limb(tree, [tip, tip+[sign*.030, .040, .006], tip+[sign*.026, .105, .012],
                            tip+[sign*.051, .141, .030]], [.007, .005, .003, .0014], 4)
    if record == 5:
        # The supporting twig meets the painted pad in depth as well as in silhouette.
        points = np.array([tree.point(x, y, z) for trace in (tree.stem, *arms) for x, y, z, _ in trace])
        for x, y, z, _, _ in ACACIA_PADS:
            node = tree.point(x, y, z)
            origin = points[np.argmin(np.linalg.norm(points-node, axis=1))]
            wood_limb(tree, [origin, (node+origin)*.5-[0, .010, 0], node], [.012, .009, .004], 4)
    for i in range(7):
        az = i*math.tau/7 + .2
        ray = np.array([math.cos(az), 0, .9*math.sin(az)])*tree.aspect
        length = tree.rng.uniform(.22, .34) if record == 5 else tree.rng.uniform(.13, .21)
        wood_limb(tree, [tree.stem_at(.84), tree.stem_at(.922)+ray*length*.36,
                   tree.stem_at(.975)+ray*length*.74, tree.stem_at(1)+ray*length],
                  [.040, .040, .019, .003], 5, root=True)
    pixels = tree.pixels
    rgb = pixels[..., :3].astype(float)
    solid = pixels[..., 3] > 128
    foliage = pixels.copy()
    if record == 5:
        green = solid & (rgb[..., 1] > rgb[..., 0]*1.01) & (rgb[..., 1] > rgb[..., 2]*1.09)
        foliage[..., 3] = 255*(solid & (distance_transform_edt(~green)<1.8))
    return {"bark": bark_tile(tree, record), "leaves": clusters(tree, record, Image.fromarray(foliage))}


PALM_FRONDS = (
    ((.482, .461, .020), (.395, .473, .048), (.305, .506, .057), (.235, .559, .042),
     (.162, .622, .032), (.105, .674, .012), (.066, .691, .004)),
    ((.490, .443, .023), (.421, .349, .049), (.369, .257, .051), (.298, .175, .044),
     (.241, .132, .025), (.205, .113, .008)),
)


def palm_bark(tree):
    """Dry overlapping leaf scars in the painting's charcoal and amber.
    Stagger their tips around the trunk; the oldest scars broaden near its foot.
    """
    base, _ = wood_colours(tree)
    ramp = colour_ramp(base, [.06, .14, .24, .36, .51, .67, .82, .94])
    h, w = 128, 96
    y, x = np.indices((h, w))
    fibre = .5+.82*np.sin(math.tau*x/w*9+.6*np.sin(y/19))+.38*np.cos(math.tau*x/w*21-y/17)
    levels = np.clip(np.rint(1.5+fibre+tree.rng.uniform(-.52, .52, x.shape)), 0, 4).astype(int)
    paint = trunk_paint(tree, (w, h), (.665, .964))
    under = np.array(ramp)[levels]
    share = .42*(paint[..., 3:4]>128)
    tile = Image.fromarray(np.rint(under*(1-share)+paint*share).astype(np.uint8))
    draw = ImageDraw.Draw(tile)
    for row in range(13):
        for col in range(5):
            u = round((col+.48*(row%2)+tree.rng.uniform(-.16, .16))*w/5)
            v = round(row*10+tree.rng.uniform(-2, 2))
            span = int(tree.rng.integers(5, 10))
            tone = int(tree.rng.integers(3, 7))
            for cx in (u-w, u, u+w):
                draw.polygon([(cx-span, v), (cx-2, v+3), (cx+span, v-2), (cx+3, v+6), (cx-2, v+7)],
                             fill=ramp[1])
                draw.line([(cx-span+1, v), (cx-2, v+3), (cx+span, v-2)], fill=ramp[tone], width=2)
                draw.line([(cx-2, v+3), (cx+2, v+2)], fill=ramp[min(7, tone+1)], width=1)
    return tile


def desert_palm(tree):
    hub = tree.stem_at(tree.stem[-1, 1])
    top = hub[1]
    tree.tube([tree.point(x, y, z) for x, y, z, _ in tree.stem], tree.stem[:, 3], 10,
              cap=True, wrap_height=top)
    tree.root_flare(.108, 5, shoulder=.92, wrap_height=top)
    # Dark old leaf bases collar the bud; one dry frond hangs in a narrow,
    # ragged strip well below the fresh blades in the painting.
    for i in range(7):
        az = .43+i*math.tau/7
        ray = np.array([math.cos(az), 0, math.sin(az)])*tree.aspect
        tree.tube([hub, hub+ray*.065-[0, .076, 0], hub+ray*.066-[0, .139, 0]],
                  [.039, .041, .013], 5, cap=True, wrap_height=top)
    dry = [tree.point(x, y, z) for x, y, z in ((.48, .49, .02), (.505, .565, .065), (.540, .657, .088),
                                            (.549, .731, .101), (.566, .819, .108))]
    tree.tube(dry, [.020, .018, .012, .010, .004], 5, cap=True, wrap_height=top)
    for i in (1, 2, 3):
        tree.tube([dry[i], (dry[i]+dry[i+1])*.5+[.019, 0, -.004], dry[i+1]+[.015, -.030, .007]],
                  [.014, .011, .003], 4, wrap_height=top)
    # Round spent leaf bases overlap around the neck. Wrap the dark brown
    # patch below the painted bud onto their shaded undersides.
    for i in range(6):
        az = i*math.tau/6
        radial = np.array([math.cos(az), 0, math.sin(az)])
        centre = hub + radial*.065*tree.aspect - [0, .090+.012*(i%2), 0]
        a = np.linspace(0, math.tau, 7)
        rows, uv = [], []
        for y, r in ((-.055, .012), (-.031, .035), (.008, .048), (.046, .034), (.054, .013)):
            rows.append(centre+np.stack([r*np.cos(a), np.full_like(a, y), r*np.sin(a)], axis=1))
            uv.append(np.stack([.08+.84*a/math.tau, np.full_like(a, .12-y)], axis=1))
        add_grid(tree.bark, np.array(rows), uv, seam=True)
    spiky_fan(tree, hub, 11, ((.19, .22), (.50, .14), (.49, -.29)), .095, 0)
    spiky_fan(tree, hub, 7, ((.17, .28), (.36, .35), (.47, .105)), .081, 1)
    spiky_fan(tree, hub, 4, ((.018, .22), (.104, .40), (.244, .445)), .041, 1)
    return {"bark": palm_bark(tree), "leaves": palm_atlas(tree, PALM_FRONDS)}


def build(record, rgba):
    tree = Tree(record, rgba, STEMS[record])
    images = desert_palm(tree) if record == 13 else branch_tree(tree, record)
    parts = [tree.bark, tree.leaves]
    return {"height": max(max(p["positions"][1::3]) for p in parts), "parts": parts}, images
