"""Summer woodland trees. Traces follow the refreshed sprite bounds, x across and y down.
Depth and radii are in image widths. The same joined branchwork is seen from every bearing.
"""
import math

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

from desert_trees import acacia_pad
from rainforest_palms import add_grid, part
from rainforest_trees import Tree, filled
from subtropical_trees import STEMS as SUBTROPICAL_STEMS, rock_grove


FORMS = {10: "woodland-rock-grove", 11: "stepped-broadleaf", 12: "twisted-fruiting-tree",
         13: "open-tiered-evergreen", 14: "silver-barked-tree", 15: "spreading-copse-tree",
         16: "leaning-green-broadleaf", 17: "amber-forked-tree", 18: "blue-green-broadleaf",
         25: "drooping-spruce"}

STEMS = {
    10: SUBTROPICAL_STEMS[20],
    11: ((.565, 1, 0, .086), (.565, .949, 0, .063), (.563, .835, .016, .047), (.570, .754, .026, .036),
         (.616, .676, .012, .031), (.635, .619, 0, .032), (.604, .544, -.023, .028), (.544, .455, -.025, .025),
         (.507, .379, -.007, .023), (.487, .288, .018, .019), (.482, .198, .023, .017), (.425, .106, .006, .012)),
    12: ((.374, 1, 0, .096), (.378, .941, .012, .108), (.354, .864, .038, .108), (.376, .791, .051, .096),
         (.436, .721, .022, .089), (.449, .667, -.019, .075), (.419, .601, -.049, .064), (.411, .521, -.045, .043),
         (.387, .388, -.005, .026), (.371, .265, .025, .012)),
    13: ((.499, 1, 0, .053), (.499, .954, 0, .040), (.496, .775, .014, .030), (.504, .625, .016, .024),
         (.495, .477, .004, .018), (.502, .303, -.012, .013), (.494, .152, -.007, .009), (.488, .038, 0, .005)),
    14: ((.545, 1, 0, .077), (.541, .935, .008, .048), (.538, .797, .012, .035), (.540, .697, -.008, .034),
         (.512, .644, -.031, .027), (.498, .551, -.044, .023), (.469, .431, -.041, .016), (.464, .268, -.016, .010)),
    15: ((.537, 1, 0, .090), (.537, .937, 0, .061), (.542, .806, .018, .045), (.539, .687, .019, .035),
         (.515, .559, .002, .030), (.462, .493, -.020, .024), (.431, .402, -.036, .020), (.418, .315, -.019, .015),
         (.388, .250, .015, .015), (.379, .167, .032, .009)),
    16: ((.582, 1, 0, .060), (.576, .947, .007, .043), (.554, .854, .019, .033), (.514, .769, .030, .031),
         (.457, .704, .024, .029), (.414, .639, .002, .027), (.386, .563, -.024, .025), (.383, .462, -.043, .017),
         (.347, .308, -.064, .009)),
    17: ((.444, 1, 0, .079), (.452, .927, .009, .057), (.472, .854, .017, .046), (.511, .767, .028, .035),
         (.535, .680, .017, .031), (.543, .592, -.020, .022), (.513, .469, -.049, .015), (.489, .312, -.070, .009)),
    18: ((.508, 1, 0, .072), (.507, .955, .004, .049), (.499, .867, .016, .042), (.485, .776, .008, .038),
         (.475, .689, -.019, .031), (.500, .563, -.048, .024), (.482, .401, -.041, .018), (.477, .257, -.018, .011)),
    25: ((.490, 1, 0, .049), (.492, .955, 0, .038), (.490, .819, .006, .029), (.497, .607, .006, .022),
         (.492, .407, 0, .015), (.501, .226, -.007, .010), (.500, .093, -.006, .006), (.494, .014, 0, .0025)),
}

# Where the trunk is exposed enough to sample grain around a whole wooden limb.
BARK_BANDS = {11:(.83, .968), 12:(.701, .945), 13:(.809, .956), 14:(.690, .959),
              15:(.74, .946), 16:(.72, .947), 17:(.752, .955), 18:(.857, .967), 25:(.84, .969)}
ROOT_SPREAD = {11:.165, 12:.253, 13:.111, 14:.20, 15:.188, 16:.137, 17:.171, 18:.137, 25:.139}

# A fork begins at the given height on the main stem. Sideways bends and
# alternating depth separate the pads in profile as well as in the sprite view.
ARMS = {
    11: ((.863, ((.607, .828, .064, .021), (.684, .803, .092, .009))),
         (.738, ((.508, .713, .061, .024), (.468, .667, .108, .017), (.425, .620, .112, .009))),
         (.673, ((.706, .599, .035, .026), (.791, .554, .045, .022), (.809, .486, .081, .017), (.797, .436, .084, .009))),
         (.435, ((.513, .400, .085, .017), (.492, .341, .157, .013), (.485, .292, .175, .006))),
         (.370, ((.415, .351, -.065, .018), (.375, .302, -.102, .015), (.347, .238, -.127, .009))),
         (.227, ((.528, .183, .063, .012), (.590, .145, .082, .010), (.629, .082, .054, .007)))),
    14: ((.733, ((.602, .668, .061, .027), (.637, .586, .116, .021), (.651, .463, .119, .013))),
         (.636, ((.432, .580, .072, .026), (.409, .485, .124, .018), (.358, .399, .118, .011))),
         (.531, ((.542, .438, -.095, .020), (.583, .343, -.143, .018), (.631, .232, -.142, .011))),
         (.431, ((.378, .356, -.082, .019), (.329, .281, -.089, .016), (.251, .191, -.108, .008))),
         (.340, ((.458, .225, .057, .014), (.371, .145, .111, .012), (.339, .074, .105, .008)))),
    15: ((.735, ((.457, .685, .056, .027), (.386, .663, .095, .020), (.329, .591, .102, .009))),
         (.646, ((.631, .585, -.024, .028), (.684, .527, -.043, .021), (.692, .443, -.091, .016), (.661, .369, -.111, .010))),
         (.590, ((.628, .574, .087, .019), (.707, .495, .152, .013), (.793, .433, .162, .008))),
         (.395, ((.347, .345, .075, .020), (.325, .290, .092, .016), (.332, .228, .095, .009))),
         (.329, ((.346, .293, -.075, .017), (.259, .283, -.094, .014), (.192, .223, -.107, .008))),
         (.246, ((.395, .164, -.064, .012), (.442, .096, -.070, .010), (.455, .058, -.089, .006)))),
    12: ((.697, ((.344, .634, .082, .055), (.292, .565, .104, .036), (.262, .463, .105, .016))),
         (.642, ((.503, .580, -.039, .044), (.579, .529, -.091, .029), (.633, .425, -.095, .015))),
         (.624, ((.432, .546, .099, .036), (.490, .438, .151, .024), (.471, .335, .192, .013)))),
    16: ((.651, ((.332, .579, .061, .028), (.281, .481, .097, .022), (.237, .389, .111, .012))),
         (.586, ((.438, .524, -.069, .030), (.511, .446, -.116, .020), (.580, .349, -.115, .013)))),
    17: ((.801, ((.455, .717, .037, .037), (.422, .634, .071, .027), (.381, .533, .093, .018), (.337, .389, .094, .009))),
         (.715, ((.563, .633, -.058, .024), (.625, .549, -.109, .023), (.683, .456, -.113, .012)))),
    18: ((.837, ((.396, .776, .071, .033), (.319, .706, .093, .026), (.271, .589, .115, .014))),
         (.809, ((.577, .736, -.065, .033), (.652, .658, -.123, .025), (.719, .524, -.140, .014)))),
}

# Centre, depth, half-width and half-height of each separately visible leaf pad.
PADS = {
    11: ((.708, .802, .077, .187, .050), (.414, .608, .115, .166, .050), (.797, .429, .073, .172, .042),
         (.892, .330, -.016, .117, .035), (.537, .326, .158, .113, .037), (.300, .255, -.120, .235, .049),
         (.240, .200, -.088, .225, .047), (.433, .077, .036, .259, .075), (.598, .071, -.004, .182, .056)),
    14: ((.563, .477, .139, .286, .098), (.757, .415, -.011, .199, .081), (.360, .324, .129, .269, .075),
         (.207, .249, -.108, .195, .094), (.492, .235, -.143, .200, .083), (.702, .229, -.144, .234, .112),
         (.600, .143, -.127, .176, .073), (.373, .070, .107, .203, .069)),
    15: ((.268, .622, .104, .217, .064), (.686, .449, -.093, .177, .070), (.810, .386, .163, .159, .071),
         (.718, .299, -.109, .235, .062), (.433, .219, .089, .178, .073), (.224, .218, -.106, .204, .066),
         (.160, .165, -.055, .175, .061), (.229, .075, .027, .213, .063), (.443, .066, -.088, .188, .069),
         (.780, .142, -.154, .202, .075)),
}


def bark_texture(tree, band, projected):
    # The main stem and its raised ridges follow the grain in the painting.
    # Narrow branches have a second, wrapping chart so a sideways fork does
    # not pick up an unrelated stroke or the silhouette's transparent margin.
    h, w = tree.pixels.shape[:2]
    yy, xx = np.indices((h, w))
    shaft = tree.stem[::-1]
    axis = np.interp(yy/(h-1), shaft[:, 1], shaft[:, 0])
    radius = np.interp(yy/(h-1), shaft[:, 1], shaft[:, 3])
    rgb = tree.pixels[..., :3].astype(float)
    stem = (abs(xx/(w-1)-axis)<radius*1.30) & (yy/h>=band[0]-.10)
    wood = stem & (tree.pixels[..., 3]>128) & ((rgb[..., 1]<=rgb[..., 0]*1.04) | (yy/h>.96))
    original = filled(tree.pixels, wood)
    lo, hi = band
    v = np.arange(160)/159
    y = hi-np.abs((v-lo)%(2*(hi-lo))-(hi-lo))
    x = np.interp(y, shaft[:, 1], shaft[:, 0])
    radius = np.interp(y, shaft[:, 1], shaft[:, 3])
    a = np.arange(96)*math.tau/96
    flow = a+.13*np.sin(2*a+v[:, None]*9)+.055*np.cos(5*a-v[:, None]*18)
    col = np.rint((x[:, None]+.84*radius[:, None]*np.sin(flow))*(w-1)).astype(int)
    sample = np.asarray(original)[np.rint(y*(h-1)).astype(int)[:, None], col]
    colours = rgb[wood]
    order = np.argsort(colours@[.21, .72, .07])
    rank = np.clip(.46+.23*np.cos(9*flow)+.16*np.sin(5*flow)+.08*np.cos(15*flow-v[:, None]*4), 0, 1)
    wrapped = sample.copy()
    wrapped[..., :3] = np.rint(.49*sample[..., :3]+.51*colours[order[(rank*(len(order)-1)).astype(int)]])
    atlas = Image.new("RGBA", (96+w, max(160, h)))
    atlas.paste(Image.fromarray(wrapped))
    atlas.paste(original, (96, 0))
    uv = tree.bark["uvs"]
    uv[:projected:2] = [(96+u*w)/atlas.width for u in uv[:projected:2]]
    uv[1:projected:2] = [v*h/atlas.height for v in uv[1:projected:2]]
    uv[projected::2] = [u*96/atlas.width for u in uv[projected::2]]
    uv[projected+1::2] = [v*160/atlas.height for v in uv[projected+1::2]]
    return atlas


def limb(tree, points, radii, sides=6):
    tree.tube(points, radii, sides, cap=True, wrap_height=1)


def branchwork(tree, record, *, arms=None):
    stem = [tree.point(x, y, z) for x, y, z, r in tree.stem]
    tree.tube(stem, tree.stem[:, 3], 9, cap=True)
    if record == 12:
        # The painted trunk turns back on itself. Raised helical ridges follow
        # that bend and disappear into the branch fork and the individual toes.
        for i in range(5):
            points, radii = [], []
            for t in np.linspace(0, 1, 12):
                y = .971-.395*t
                az = i*math.tau/5+t*3.6+.16*math.sin(t*math.tau)
                r = np.interp(y, tree.stem[::-1, 1], tree.stem[::-1, 3])
                points.append(tree.stem_at(y)+.92*r*tree.aspect*np.array([math.cos(az), 0, math.sin(az)]))
                radii.append(.006+.016*math.sin(math.pi*t)**.7)
            tree.tube(points, radii, 5, cap=True)
    projected = len(tree.bark["uvs"])
    anchors = list(stem)
    for attach, trace in (ARMS.get(record, ()) if arms is None else arms):
        points = [tree.stem_at(attach)] + [tree.point(x, y, z) for x, y, z, r in trace]
        limb(tree, points, [trace[0][3]*1.17]+[r for x, y, z, r in trace])
        anchors.extend(points[1:])
    spread = ROOT_SPREAD[record]
    # Low sweeping roots disappear individually into the ground. Thin stems have
    # a broad toe on their leaning side, as in the painting.
    count = 6 if record == 12 else 5
    for i in range(count):
        angle = i*math.tau/count + tree.rng.uniform(-.23, .23)
        ray = np.array([math.cos(angle), 0, .86*math.sin(angle)])*tree.aspect
        length = spread*tree.rng.uniform(.70, 1.13)
        down = tree.rng.uniform(.904, .956) if record != 12 else tree.rng.uniform(.855, .920)
        points = [tree.stem_at(down), tree.stem_at(.959)+ray*length*.28,
                  tree.stem_at(.988)+ray*length*.64, tree.stem_at(1.001)+ray*length]
        limb(tree, points, [spread*.17, spread*.18, spread*.093, .0025], 5)
    return np.array(anchors), projected


def foliage(tree, record, skirt, *, autumn=False):
    rgb = tree.pixels[..., :3].astype(float)
    solid = tree.pixels[..., 3]>128
    green = (rgb[..., 1] >= rgb[..., 0]*1.02) & (rgb[..., 1] >= rgb[..., 2]*.96)
    green &= solid
    if record == 17 or autumn:
        keep = solid.copy()
    else:
        # Orange sunlit tips and the small red fruit sit among the green paint.
        nearby = distance_transform_edt(~green) < max(2, tree.pixels.shape[1]*(.033 if record in PADS else .017))
        keep = solid & nearby
    keep[round(len(keep)*skirt):] = False
    image = tree.pixels.copy()
    image[..., 3] = 255*keep
    return Image.fromarray(image), keep


def connect(tree, anchors, node, radius):
    start = anchors[np.argmin(np.linalg.norm(anchors-node, axis=1))]
    if np.linalg.norm(node-start)>.009:
        limb(tree, [start, start+(node-start)*.60-[0, .018, 0], node],
             [radius, radius*.68, .004], 5)


def leaf_pads(tree, record, anchors):
    image, _ = foliage(tree, record, .89)
    w, h = image.size
    for x, y, z, width, height in PADS[record]:
        node = tree.point(x, y, z)
        connect(tree, anchors, node, .014 if record != 14 else .020)
        box = (round((x-width)*w), round((y-height)*h), round((x+width)*w), round((y+height)*h))
        patch = np.array(image.crop(box).resize((64, 56), Image.Resampling.NEAREST))
        v, u = np.indices(patch.shape[:2])
        u, v = (u-31.5)/32, (v-27.5)/28
        az = np.arctan2(v, u)
        lit = patch[..., :3]@[.21, .72, .07]
        solid = patch[..., 3]>128
        lo, hi = np.quantile(lit[solid], [.12, .81]) if solid.any() else (0, 1)
        edge = .77+.17*np.clip((lit-lo)/max(1, hi-lo), 0, 1)+.055*np.sin(7*az+x*13)+.052*np.cos(11*az-y*8)
        patch[..., 3] *= u*u+v*v < edge**2
        acacia_pad(tree, Image.fromarray(patch), node, width*tree.aspect, height*1.18)
    return tree.cluster_atlas()


# down the painting, horizontal centre, radius across the painting, vertical
# radius, depth radius and number of boughs around that level.
CROWNS = {
    12: ((.192, .485, .392, .136, .329, 4), (.308, .508, .472, .171, .421, 6), (.456, .492, .416, .138, .386, 5)),
    16: ((.143, .568, .323, .156, .312, 5), (.297, .505, .467, .185, .438, 7), (.439, .486, .454, .158, .415, 6)),
    17: ((.150, .419, .330, .168, .321, 5), (.306, .477, .436, .167, .398, 6), (.451, .525, .464, .150, .428, 6)),
    18: ((.157, .472, .262, .169, .280, 4), (.310, .462, .389, .187, .353, 5),
         (.501, .522, .460, .176, .424, 6), (.650, .475, .442, .166, .380, 6)),
}
SKIRTS = {12:.657, 16:.602, 17:.625, 18:.811}

CROWN_TIPS = {
    12: ((.307, .112, -.050, .133, .105), (.580, .098, .063, .108, .100), (.214, .552, .083, .145, .060)),
    16: ((.164, .390, -.100, .144, .090), (.728, .100, .031, .136, .083)),
    17: ((.229, .371, .079, .156, .085), (.809, .492, -.089, .165, .077)),
    18: ((.737, .191, .045, .124, .056), (.204, .372, -.085, .182, .063), (.182, .645, .064, .162, .084),
         (.830, .521, -.042, .143, .060)),
}


class Boughs:
    """A painted, shaded interior and cutout sprays for each bough, drawn in one leaf material."""
    def __init__(self, tree, record, skirt, *, autumn=False):
        self.tree = tree
        self.paint, mask = foliage(tree, record, skirt, autumn=autumn)
        interior = np.array(filled(tree.pixels, mask))
        # A side or top projection can land outside the painted outline.
        # Continue the neighbouring leaf pattern there at the original scale.
        h, w = mask.shape
        yy, xx = np.indices(mask.shape)
        sx = round(.36*w) + abs(xx % max(4, round(.32*w)) - round(.16*w))
        sy = round(skirt*.27*h) + abs(yy % max(4, round(skirt*.29*h)) - round(skirt*.145*h))
        outside = distance_transform_edt(~mask)>2
        interior[outside] = interior[sy[outside], sx[outside]]
        self.interior = Image.fromarray(interior)
        self.cores = part("leaves")

    def grow(self, x, down, z, width, height, depth, az, *, sprays=4):
        tree = self.tree
        centre = tree.point(x, down, z)
        rx, rz = width*tree.aspect, depth*tree.aspect
        compact = sprays == 3
        a = np.linspace(0, math.tau, 6 if compact else 7)
        outline = 1+.095*np.cos(3*a+az)+.045*np.sin(5*a)
        rings, levels = [], []
        profile = ((-.39, .09), (-.14, .57), (.39, .55), (.66, .08)) if compact else (
            (-.42, .055), (-.23, .49), (.12, .63), (.48, .48), (.66, .065))
        for y, r in profile:
            rings.append(centre+np.stack([r*rx*outline*np.cos(a),
                height*(y+.096*r*np.cos(3*a+az)), r*rz*outline*np.sin(a)], axis=1))
            levels.append(centre+[0, height*y, 0])
        tree.painted_grid(self.cores, np.array(rings), np.array(levels), depth_scale=.64)
        # Retain the actual painted leaf shapes at a patch edge. Sunlit leaves
        # project farther past the bough than the dark gaps between them.
        w, h = self.paint.size
        box = (round((x-.59*width)*w), round((down-.65*height)*h), round((x+.59*width)*w), round((down+.65*height)*h))
        patch = np.array(self.paint.crop(box).resize((64, 56), Image.Resampling.NEAREST))
        lit = patch[..., :3]@[.21, .72, .07]
        solid = patch[..., 3]>128
        if solid.any():
            lo, hi = np.quantile(lit[solid], [.13, .83])
            shade = np.clip((lit-lo)/max(hi-lo, 1), 0, 1)
            yy, xx = np.indices((56, 64))
            u, v = (xx-31.5)/32, (yy-27.5)/28
            angle = np.arctan2(v, u)
            edge = .65+.29*shade+.07*np.sin(7*angle+az)+.048*np.cos(13*angle)
            patch[..., 3] *= u*u+v*v<edge*edge
        index = len(tree.patches)
        tree.patches.append(Image.fromarray(patch))
        for j in range(sprays):
            bearing = az+(j+tree.rng.uniform(-.12, .12))*math.tau/sprays
            ray = np.array([math.cos(bearing)*rx, 0, math.sin(bearing)*rz])
            side = np.array([-math.sin(bearing)*rx, 0, math.cos(bearing)*rz])
            rows, uv = [], []
            # The spray bows outward through its middle and curls back at the
            # tips. This gives a cluster a leafy rim even when seen edge-on.
            tilt = tree.rng.uniform(-.18, .18)
            skew = tree.rng.uniform(-.20, .20)
            outline = ((0, .12, .94, .65), (.55, .96, .22, .87), (1, .83, -.69, .61))
            if not compact and j%2 == 0:
                outline = ((0, .12, .99, .64), (.40, .83, .53, .87),
                           (.70, 1.01, .04, .79), (1, .80, -.70, .61))
            for t, reach, lift, span in outline:
                rows.append([centre+ray*(reach-.28*k*k)+side*(k*span+skew*t)+
                             [0, height*(lift+tilt*k-.16*k*k), 0] for k in (1, 0, -1)])
                uv.append([[(index%8+u)/8, index//8+.01+.98*t] for u in (.005, .5, .995)])
            add_grid(tree.leaves, np.array(rows), uv, double=True)

    def atlas(self):
        # The core uses the original painting's projections. The sprays use
        # individually cropped patches; combine the two charts and index sets.
        tree = self.tree
        patches = tree.cluster_atlas()
        w = self.interior.width+patches.width
        h = max(self.interior.height, patches.height)
        atlas = Image.new("RGBA", (w, h))
        atlas.paste(self.interior)
        atlas.paste(patches, (self.interior.width, 0))
        leaves = tree.leaves
        uv = leaves["uvs"]
        uv[::2] = [(self.interior.width+u*patches.width)/w for u in uv[::2]]
        uv[1::2] = [v*patches.height/h for v in uv[1::2]]
        uv = self.cores["uvs"]
        uv[::2] = [u*self.interior.width/w for u in uv[::2]]
        uv[1::2] = [v*self.interior.height/h for v in uv[1::2]]
        first = len(leaves["positions"])//3
        leaves["indices"].extend(first+i for i in self.cores["indices"])
        for field in ("positions", "normals", "colors", "uvs"):
            leaves[field].extend(self.cores[field])
        return atlas


def broadleaf(tree, record, anchors):
    boughs = Boughs(tree, record, SKIRTS[record])
    tiers = CROWNS[record]
    for tier, (down, x, width, height, depth, count) in enumerate(tiers):
        ring = .54 if tier else .42
        for i in range(count):
            az = (i+.37*tier+tree.rng.uniform(-.14, .14))*math.tau/count
            cx = x+ring*width*math.cos(az)
            y = down+.16*height*math.sin(3*az+.8)+tree.rng.uniform(-.023, .023)
            z = ring*depth*math.sin(az)
            if tier == len(tiers)-1:
                connect(tree, anchors, tree.point(cx, y, z), .025)
            size = (1.13-ring)*tree.rng.uniform(.94, 1.13)
            boughs.grow(cx, y, z, width*size, height*1.12, depth*size, az)
        if tier == 0 and record != 12:
            boughs.grow(x, down-.035, 0, width*.61, height*1.03, depth*.61, .4)
    # These outlying tips break the dome at landmarks in the front silhouette:
    # the fruit tree's paired peaks, the low left branch on the leaning tree,
    # the amber crown's right skirt, and the blue-green tree's tier ends.
    for x, y, z, width, height in CROWN_TIPS[record]:
        boughs.grow(x, y, z, width, height, width*.74, x*math.tau, sprays=3)
    return boughs.atlas()


def open_evergreen(tree):
    boughs = Boughs(tree, 13, .833)
    # The boughs turn up from the stem before spreading sideways. The lower
    # tiers have a long side and several short, high branch tips.
    for tier, (down, width, height, count) in enumerate((
            (.752, .49, .066, 5), (.618, .451, .084, 5), (.485, .424, .064, 4),
            (.373, .315, .062, 4), (.268, .295, .069, 4), (.167, .215, .056, 3), (.077, .154, .071, 3))):
        centre_x = .506+.036*math.sin(tier*1.8)
        for i in range(count):
            az = (i+.43*tier+tree.rng.uniform(-.16, .16))*math.tau/count
            reach = width*(.51+.11*math.sin(az+1.7*tier))
            x = centre_x+reach*math.cos(az)
            z = .92*reach*math.sin(az)
            y = down+tree.rng.uniform(-.026, .025)
            node = tree.point(x, y, z)
            origin = tree.stem_at(min(.84, down+.061))
            limb(tree, [origin, origin+(node-origin)*.63-[0, .017, 0], node],
                 [max(.006, .043*width), max(.004, .028*width), .003], 4)
            boughs.grow(x, y, z, width*.58, height, width*.52, az, sprays=3)
    return boughs.atlas()


# Attachment, end of hanging spray, half-width, and number of radial boughs.
SPRUCE_WHORLS = ((.565, .819, .472, 9), (.474, .717, .444, 8), (.365, .614, .387, 8), (.297, .501, .322, 7),
                 (.218, .392, .251, 7), (.144, .299, .200, 6), (.096, .211, .140, 5), (.055, .140, .098, 4), (.015, .079, .049, 4))


def spruce(tree):
    leaves, _ = foliage(tree, 25, .845)
    w, h = leaves.size
    for tier, (attach, tip, width, count) in enumerate(SPRUCE_WHORLS):
        # Each tier carries the paint on the flank of its hanging needle skirt.
        chip_height = max(.042, (tip-attach)*.60)
        box = (round((.50-width)*w), round((tip-chip_height*1.45)*h),
             round((.50-width*.20)*w)+1, round((tip+.024)*h)+1)
        patch = np.array(leaves.crop(box).resize((64, 56), Image.Resampling.NEAREST))
        py, px = np.indices((56, 64))
        across = (px-31.5)/32
        along = py/55
        # Several needle sprays terminate along each edge and at the end.
        reach = 1-.18*np.abs(across)+.09*np.cos(11*across+tier*.5)
        span = .25+.78*np.sin(np.pi*(along*.84+.04))**.65+.075*np.sin(along*33+tier)
        patch[..., 3] *= (np.abs(across)<span) & (along<reach)
        index = len(tree.patches)
        tree.patches.append(Image.fromarray(patch))
        hub = tree.stem_at(attach)
        for i in range(count):
            az = (i+.47*tier+tree.rng.uniform(-.15, .15))*math.tau/count
            ray = np.array([math.cos(az), 0, math.sin(az)])
            side = np.cross(ray, [0, 1, 0])
            length = width*tree.aspect*tree.rng.uniform(.88, 1.05)
            delta = (tip-attach)*tree.rng.uniform(.84, 1.06)
            half = length*.48
            # The lower boughs expose their wooden axes to a viewer below the skirt.
            if tier == 0:
                limb(tree, [hub, hub+ray*length*.39-[0, delta*.36, 0], hub+ray*length*.82-[0, delta*.45, 0]],
                     [max(.004, width*.036), max(.003, width*.020), .0025], 4)
            rows, uv = [], []
            for t, radial, side_span in ((0, -.02, .16), (.36, .37, .69), (.72, .75, 1), (1, 1.06, .69)):
                drop = delta*t**1.25
                centre = hub+ray*radial*length-[0, drop, 0]
                rows.append([centre+side*k*half*side_span-ray*k*k*.17*length-
                             [0, k*k*(.017+delta*.28)*side_span, 0] for k in (1, 0, -1)])
                uv.append([[(index%8+u)/8, index//8+.01+.98*t] for u in (.005, .5, .995)])
            add_grid(tree.leaves, np.array(rows), uv, double=True)
    return tree.cluster_atlas()


def build(record, rgba):
    tree = Tree(record, rgba, STEMS[record])
    parts = [tree.bark, tree.leaves]
    if record == 10:
        limb_points = [tree.point(x, y, z) for x, y, z, r in tree.stem]
        tree.tube(limb_points, tree.stem[:, 3], 10, cap=True)
        stone, tile = rock_grove(tree, moss_line=.39)
        rgb = tree.pixels[..., :3].astype(float)
        wood = (tree.pixels[..., 3]>128) & (rgb[..., 0]>rgb[..., 1]*1.03) & (rgb[..., 0]>rgb[..., 2]*1.1)
        parts.append(stone)
        images = {"stone":tile, "bark":filled(tree.pixels, wood), "leaves":tree.cluster_atlas()}
    else:
        anchors, projected = branchwork(tree, record)
        if record == 13:
            leaves = open_evergreen(tree)
        elif record == 25:
            leaves = spruce(tree)
        elif record in CROWNS:
            leaves = broadleaf(tree, record, anchors)
        else:
            leaves = leaf_pads(tree, record, anchors)
        images = {"bark":bark_texture(tree, BARK_BANDS[record], projected), "leaves":leaves}
    return {"height":max(max(p["positions"][1::3]) for p in parts), "parts":parts}, images
