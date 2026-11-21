"""Archive 502's spreading crowns, roots over stone and two hollow swamp snags.

The profiles follow the refreshed paintings in Tree's coordinates (x across, y down,
depth and radius in shares of image width). All branches and vines have fixed bearings.
"""
import math

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

from rainforest_palms import add_grid, colour_ramp, part
from rainforest_trees import Tree, filled


FORMS = {13: "flared-broadleaf", 16: "ivy-rooted-broadleaf", 17: "stone-perched-broadleaf",
         24: "vine-hung-snag", 25: "shelf-fungus-snag"}

STEMS = {
    13: ((.503, 1, 0, .159), (.506, .925, .010, .135), (.507, .814, .027, .115), (.497, .709, .020, .092),
         (.498, .570, -.012, .084), (.500, .460, -.021, .096), (.478, .351, 0, .068), (.450, .213, .027, .030)),
    16: ((.495, .849, 0, .139), (.484, .795, .015, .136), (.473, .701, .024, .099),
         (.478, .586, -.015, .079), (.481, .457, -.027, .074), (.489, .371, .007, .067), (.532, .237, .035, .028)),
    17: ((.516, .782, 0, .134), (.514, .703, .024, .118), (.484, .621, .045, .092),
         (.487, .528, .025, .070), (.504, .432, -.010, .067), (.491, .353, -.018, .050), (.506, .251, 0, .021)),
    24: ((.473, 1, 0, .090), (.475, .901, .018, .143), (.430, .815, .034, .117), (.401, .725, .038, .091),
         (.425, .632, -.010, .109), (.479, .524, -.038, .096), (.468, .447, -.026, .070),
         (.445, .359, .004, .074), (.373, .301, .039, .071), (.316, .265, .047, .039),
         (.347, .200, .060, .049), (.306, .167, .049, .034), (.244, .167, .027, .031),
         (.206, .134, .008, .021), (.229, .083, -.005, .014), (.286, .057, .008, .007), (.317, .006, .014, .004)),
    25: ((.635, 1, 0, .083), (.633, .912, .024, .164), (.623, .792, .042, .158), (.593, .677, .050, .153),
         (.554, .585, .031, .147), (.513, .485, .005, .153), (.429, .385, -.023, .144),
         (.364, .298, -.047, .146), (.321, .212, -.036, .130), (.367, .133, -.018, .090),
         (.364, .068, .015, .064), (.325, .032, .026, .038), (.303, .015, .024, .012)),
}

# down the painting, horizontal section radius, root radius outside that section.
# The swamp roots clasp the stone's shoulder before dividing around the exposed faces.
BASES = {
    13: ((.72, .066, .040), (.81, .103, .058), (.90, .181, .054), (.963, .267, .041), (1.00, .341, .009)),
    16: ((.66, .055, .030), (.735, .092, .045), (.812, .144, .054), (.875, .192, .045),
         (.936, .209, .031), (.975, .215, .025), (1.0, .258, .007)),
    17: ((.55, .050, .029), (.624, .077, .037), (.686, .132, .046), (.752, .182, .043),
         (.817, .206, .036), (.909, .206, .026), (.974, .209, .021), (1.0, .226, .007)),
}


def limb(tree, trace, sides=6):
    tree.tube([tree.point(x, y, z) for x, y, z, _ in trace],
              [radius for _, _, _, radius in trace], sides, cap=True)


def root_base(tree, record):
    profile = BASES[record]
    for i in range(8):
        angle = math.tau * (i + tree.rng.uniform(-.23, .23)) / 8
        reach = tree.rng.uniform(.92, 1.09)
        twist = tree.rng.uniform(-.60, .60)
        points, radii = [], []
        for j, (down, radius, thickness) in enumerate(profile):
            t = j / (len(profile)-1)
            az = angle + twist*t + .16*math.sin(2*math.pi*t + i)
            centre = tree.stem_at(down)
            offset = np.array([math.cos(az), 0, .86*math.sin(az)])
            points.append(centre + offset * radius * reach * tree.aspect)
            radii.append(thickness)
        tree.tube(points, radii, 6, root_grain=True)
    if record == 13:
        return None

    stone = part("stone")
    angle = np.linspace(0, math.tau, 13)
    outline = 1 + .071*np.cos(3*angle + .9) + .041*np.sin(5*angle)
    rings, centres = [], []
    for down, radius, _ in reversed(profile[1:]):
        centre = tree.stem_at(down)
        centres.append(centre)
        radius *= np.interp(down, (.65, .86, .96, 1), (.93, .97, .91, .69))
        rings.append(centre + tree.aspect*np.stack([radius*outline*np.cos(angle),
                     .012*np.sin(3*angle)*np.clip((1-down)*10, 0, 1),
                     .86*radius*outline*np.sin(angle)], axis=1))
    tree.painted_grid(stone, np.array(rings), np.array(centres))
    tree.cap(stone, rings[-1], np.array([0., 1., 0.]))
    return stone


# The trunk forks high on 17. Its lower crown is cut away between the arms.
CROWNS = {
    13: (.496, .477, .46, (.508, .50, .54), ((.458, .18, .204, .11), (.486, .77, .187, -.08), (.492, .34, .326, -.15))),
    16: (.493, .481, .396, (.487, .43, .43), ((.426, .210, .216, .11), (.428, .774, .168, -.10), (.444, .642, .259, .19))),
    17: (.501, .479, .312, (.493, .37, .28), ((.373, .232, .212, .10), (.372, .786, .174, -.11), (.361, .558, .183, -.18))),
}


def broad_crown(tree, record):
    x, radius, depth, (fork_x, fork_y, skirt), branches = CROWNS[record]
    for attach, tip_x, tip_y, z in branches:
        start = tree.stem_at(attach)
        tip = tree.point(tip_x, tip_y, z)
        elbow = start + (tip-start)*.58 - [0, .035, 0]
        tree.tube([start, elbow, tip, tip+[0, .060, 0]], [.053, .039, .025, .010], 6)
    # Smaller limbs open the fork when a player looks up into the canopy.
    for i in range(5):
        az = .41 + i*math.tau/5
        ray = np.array([math.cos(az), 0, math.sin(az)])*tree.aspect
        start = tree.point(fork_x, fork_y, 0)
        elbow = start + ray*.143 + [0, .065, 0]
        tree.tube([start, elbow, elbow+ray*.122+[0, .109, 0]], [.041, .027, .010], 5)

    # Overlapping boughs keep the dome full from below. Separate lobes interrupt
    # the skyline and leave pockets above the outward-reaching lower branches.
    for i in range(8):
        az = i*math.tau/8+.15
        offset = .55+.06*math.sin(3*az)
        down = skirt*(.48+.075*math.sin(3*az+.8))
        centre = tree.point(x+radius*offset*math.cos(az), down, depth*offset*math.sin(az))
        lobe(tree, centre, radius*.406, skirt*.28, depth*.45, az, i)
    for i in range(4):
        az = i*math.pi/2+.5
        centre = tree.point(x+radius*.26*math.cos(az), skirt*(.269+.024*math.cos(az)), depth*.26*math.sin(az))
        lobe(tree, centre, radius*.43, skirt*.237, depth*.44, az, i+8)
    if record == 17:
        for i, az in enumerate((.15, 2.88, 4.55)):
            centre = tree.point(x+radius*.59*math.cos(az), skirt*.84, depth*.51*math.sin(az))
            lobe(tree, centre, radius*.27, skirt*.22, depth*.32, az, i+12)
    else:
        for i, az in enumerate((-.42, 2.62, 4.56)):
            centre = tree.point(x+radius*.53*math.cos(az), skirt*.74, depth*.45*math.sin(az))
            lobe(tree, centre, radius*.32, skirt*.16, depth*.32, az, i+12)


def lobe(tree, centre, rx, height, rz, bearing, index):
    """A shaded core underneath three fixed sprays that carry the painted fringe."""
    rx, rz = rx*tree.aspect, rz*tree.aspect
    a = np.linspace(0, math.tau, 9)
    phase = bearing+.54*index
    outline = 1+.08*np.cos(3*a+phase)+.044*np.sin(5*a)
    rings, levels = [], []
    for y, radius in ((-.70, .08), (-.51, .64), (.08, .88), (.52, .71), (.79, .15)):
        rings.append(centre+np.stack([rx*radius*outline*np.cos(a), height*(y+.09*radius*np.sin(3*a+phase)),
                                      rz*radius*outline*np.sin(a)], axis=1))
        levels.append(centre + [0, height*y, 0])
    # Project the painting onto the facing plane of each patch, keeping painted
    # leaf size and the lighting gradient across neighbouring boughs.
    start = len(tree.leaves["uvs"])
    tree.painted_grid(tree.leaves, np.array(rings), np.array(levels), depth_scale=.68)
    uv = tree.leaves["uvs"]
    uv[start::2] = [(1+u)/3 for u in uv[start::2]]
    for j in range(3):
        az = bearing+j*math.tau/3+tree.rng.uniform(-.22, .22)
        ray = np.array([math.cos(az), 0, math.sin(az)])
        side = np.cross(ray, [0, 1, 0])
        rows, uv = [], []
        cell = (index+j)%4
        for t, reach, lift in ((0, .04, 1.10), (.5, .83, .69), (1, 1.25, -.67)):
            rows.append([centre+ray*(reach-.26*k*k)*rx+side*k*rx*.79
                         + [0, height*(lift-.22*k*k), 0] for k in (1, 0, -1)])
            uv.append([[(2+(cell%2+.5+.49*k)/2)/3, (cell//2+.01+.98*t)/2] for k in (1, 0, -1)])
        add_grid(tree.leaves, np.array(rows), uv, double=True)


def canopy_texture(tree, original, green, skirt):
    # The first chart retains holes for hanging ivy. The second supplies solid
    # leaves under the boughs; the four outer sprays retain real painted gaps.
    top = green.copy()
    top[round(len(top)*skirt):] = False
    interior = filled(tree.pixels, top)
    w, h = original.size
    atlas = Image.new("RGBA", (3*w, h))
    atlas.paste(original)
    # Large gaps beyond the original silhouette need fresh leaves around the sides
    # of the crown. Tile a dense, painted swatch there at its original texel scale.
    interior = np.array(interior)
    yy, xx = np.indices((h, w))
    sx = round(.34*w) + abs((xx % max(4, round(.32*w))) - round(.16*w))
    sy = round(skirt*.24*h) + abs((yy % max(4, round(skirt*.32*h))) - round(skirt*.16*h))
    outside = distance_transform_edt(~top)>2.5
    interior[outside] = interior[sy[outside], sx[outside]]
    atlas.paste(Image.fromarray(interior), (w, 0))
    # End the outer sprays in actual cutout leaf silhouettes. Two come from the
    # skirt and two turn the sunlit, broken top edge out toward the branch tips.
    for i, (left, right, start, end) in enumerate(((.07, .36, .43, 1.04), (.60, .90, .46, 1.05),
                                                (.15, .43, -.08, .45), (.51, .82, -.07, .48))):
        patch = original.crop((round(left*w), round(start*skirt*h), round(right*w), round(end*skirt*h)))
        if i >= 2:
            patch = patch.transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        # Where the source continues into another bough, trim the crop to a spray.
        # Otherwise its straight crop boundary becomes visible above the skyline.
        pixels = np.array(patch)
        py, px = np.indices(pixels.shape[:2])
        px = (2*px+1)/patch.width-1
        py = (2*py+1)/patch.height-1
        angle = np.arctan2(py, px)
        edge = .86+.061*np.sin(9*angle+i)+.048*np.cos(15*angle-i)
        pixels[..., 3] *= px*px+py*py < edge**2
        patch = Image.fromarray(pixels)
        x0, x1 = i%2*w//2, (i%2+1)*w//2
        y0, y1 = i//2*h//2, (i//2+1)*h//2
        atlas.paste(patch.resize((x1-x0, y1-y0), Image.Resampling.NEAREST), (2*w+x0, y0))
    return atlas


def ivy(tree, start, end, width, source):
    """A sagging strip of small leaves, with a fold so it stays visible as the view turns."""
    x0, y0, x1, y1 = source
    dx = end-start
    side = np.array([math.cos(start[1]*22), 0, math.sin(start[1]*22)])*width*tree.aspect
    ridge = np.cross(side, [0, 1, 0])*.65
    rows, uvs = [], []
    for t in (0, .27, .56, .81, 1):
        p = start+t*dx + .50*side*math.sin(math.tau*t)
        span = .93-.40*t
        rows.append([p+side*span, p+ridge*span, p-side*span])
        uvs.append([[u, y0+t*(y1-y0)] for u in (x0, (x0+x1)*.5, x1)])
    add_grid(tree.leaves, np.array(rows), uvs, double=True)


# Each fork begins inside the preceding section. Depth offsets separate the
# antler-like branches on 24 and the three broken points on 25 in profile.
FORKS = {
    24: (
        ((.434, .434, 0, .077), (.567, .344, -.028, .067), (.623, .298, -.041, .053), (.645, .223, -.047, .039),
         (.608, .179, -.070, .029), (.560, .152, -.087, .026), (.561, .094, -.094, .017), (.588, .075, -.103, .008)),
        ((.599, .315, -.04, .055), (.733, .255, .036, .045), (.778, .206, .077, .031), (.85, .198, .103, .023),
         (.897, .156, .136, .017), (.911, .109, .144, .006)),
        ((.554, .140, -.088, .026), (.490, .132, -.117, .023), (.480, .091, -.137, .019), (.460, .047, -.139, .012), (.425, .028, -.153, .004)),
        ((.553, .142, -.082, .023), (.603, .110, -.113, .018), (.662, .096, -.135, .013), (.693, .035, -.142, .004)),
        ((.785, .218, .08, .024), (.775, .166, .141, .016), (.790, .113, .149, .006)),
        ((.897, .154, .134, .014), (.942, .150, .157, .011), (.993, .124, .161, .004)),
        ((.397, .337, .026, .057), (.302, .306, .105, .041), (.229, .288, .132, .031), (.157, .252, .14, .023),
         (.081, .231, .126, .018), (.055, .168, .119, .009), (.009, .130, .127, .004)),
        ((.218, .120, 0, .020), (.195, .068, -.026, .015), (.173, .056, -.035, .007)),
        ((.444, .401, .002, .046), (.501, .336, .13, .033), (.503, .251, .217, .024), (.458, .209, .257, .010)),
        ((.382, .298, .038, .039), (.315, .253, -.106, .024), (.278, .213, -.184, .011)),
    ),
    25: (
        ((.350, .319, -.045, .111), (.222, .245, -.054, .078), (.150, .198, -.074, .045), (.105, .141, -.087, .032),
         (.056, .078, -.080, .019), (.009, .014, -.068, .010)),
        ((.512, .484, 0, .116), (.651, .427, -.015, .089), (.674, .357, -.035, .047), (.651, .292, -.047, .040),
         (.669, .237, -.072, .026), (.740, .170, -.091, .024), (.776, .095, -.094, .023), (.846, .024, -.088, .007)),
        ((.738, .180, -.083, .026), (.711, .131, -.114, .023), (.679, .082, -.139, .012)),
        ((.576, .524, .031, .090), (.738, .462, .091, .077), (.778, .398, .124, .050), (.850, .367, .156, .040),
         (.897, .324, .164, .026), (.947, .245, .152, .015)),
        ((.366, .166, -.036, .091), (.421, .140, .065, .067), (.423, .077, .088, .039), (.397, .049, .103, .019)),
    ),
}


def shelves(tree):
    # Brackets project from one face of the trunk. Their thin ochre rims
    # turn up towards the tips; the bottom of each shelf meets the bole.
    for x, down, width, depth, az in ((.346, .381, .189, .139, -.34),
            (.400, .475, .198, .145, .18), (.459, .584, .227, .138, -.11)):
        a = np.linspace(0, math.tau, 17)
        ripple = 1+.047*np.sin(3*a+az)+.027*np.cos(5*a)
        centre = tree.point(x, down, .073)
        rings, uv = [], []
        for radius, lift, v in ((.07, -.042, .01), (.68, -.026, .22), (.98, .001, .40),
                              (1, .012, .49), (.83, .022, .65), (.37, .034, .86), (.045, .038, .99)):
            xx, zz = width*radius*ripple*np.cos(a), depth*radius*ripple*np.sin(a)
            rings.append(centre+np.stack([(xx*math.cos(az)-zz*math.sin(az))*tree.aspect,
                         lift+.025*radius*np.cos(a)**2+.007*radius*np.cos(3*a+.5),
                         (xx*math.sin(az)+zz*math.cos(az))*tree.aspect], axis=1))
            uv.append(np.stack([.006+.988*a/math.tau, np.full_like(a, v)], axis=1))
        add_grid(tree.bark, np.array(rings), uv, seam=True)


def shelf_texture(tree, bark):
    pixels = tree.pixels
    h, w = pixels.shape[:2]
    band = pixels[round(.36*h):round(.61*h), round(.17*w):round(.49*w)].astype(float)
    warm = band[(band[..., 3]>128) & (band[..., 0]>band[..., 1]*1.10), :3]
    ramp = np.array(colour_ramp(warm, [.07, .19, .33, .49, .67, .82, .94]))
    v, u = np.indices((64, 96))
    az = math.tau*u/96
    ring = v+.97*np.sin(3*az)+.67*np.cos(7*az)+.37*np.sin(15*az)
    tone = np.interp(ring, [0, 10, 19, 24, 27, 30, 32, 35, 40, 45, 50, 56, 63],
                          [1, 0, 1, 2, 4, 6, 5, 4, 3, 4, 2, 2, 1])
    grain = .43*np.cos(13*az+v*.12)+tree.rng.uniform(-.49, .49, v.shape)
    tile = Image.fromarray(ramp[np.clip(np.rint(tone+grain).astype(int), 0, 6)].astype(np.uint8))
    atlas = Image.new("RGBA", (bark.width+tile.width, bark.height))
    atlas.paste(bark)
    atlas.paste(tile.resize((tile.width, bark.height), Image.Resampling.NEAREST), (bark.width, 0))
    # Wood already occupies the first chart. Add all three shelves to the same
    # bark draw, mapping their circumference and growth rings into the second.
    split = bark.width/atlas.width
    uvs = tree.bark["uvs"]
    uvs[::2] = [u*split for u in uvs[::2]]
    start = len(uvs)
    shelves(tree)
    uvs[start::2] = [split+(1-split)*u for u in uvs[start::2]]
    return atlas


def snag(tree, record):
    for trace in FORKS[record]:
        limb(tree, trace, 6 if trace[0][3]>.04 else 5)
    for i in range(7):
        az = i*math.tau/7 + .25
        ray = np.array([math.cos(az), 0, .88*math.sin(az)])*tree.aspect
        side = np.cross(ray, [0, 1, 0])
        reach = tree.rng.uniform(.30, .43)
        points = [tree.stem_at(.72), tree.stem_at(.84)+ray*.094, tree.stem_at(.916)+ray*reach*.49,
                  tree.stem_at(.975)+ray*reach*.80+side*.034, tree.stem_at(1.003)+ray*reach]
        tree.tube(points, [.074, .081, .059, .031, .006], 6)
    # Raised rope roots twist up the bark, catching light even on the shaded side.
    for i in range(4):
        points = []
        for t in np.linspace(0, 1, 10):
            down = .97 - (.54 if record == 24 else .62)*t
            radius = np.interp(down, tree.stem[::-1, 1], tree.stem[::-1, 3])
            angle = i*math.pi/2 + (4.7 if record == 24 else 2.1)*t
            points.append(tree.stem_at(down)+np.array([math.cos(angle), 0, math.sin(angle)])*.91*radius*tree.aspect)
        tree.tube(points, np.linspace(.031, .011, len(points)), 5)

    if record == 24:
        for phase, (bottom, top) in enumerate(((.955, .47), (.740, .305))):
            points = []
            for t in np.linspace(0, 1, 18):
                down = bottom+(top-bottom)*t
                az = phase*2.8 + t*math.tau*1.25
                radius = np.interp(down, tree.stem[::-1, 1], tree.stem[::-1, 3])
                radius += .016 + .095*math.sin(math.pi*t)**2
                points.append(tree.stem_at(down)+tree.aspect*radius*np.array([math.cos(az), 0, math.sin(az)]))
            tree.tube(points, np.linspace(.020, .009, len(points)), 5)

    source = (.074, .234, .142, .448) if record == 24 else (.864, .295, .958, .600)
    hangs = ((.106, .235, .126, .093, .383), (.222, .281, .13, .236, .47), (.342, .216, .064, .365, .36),
             (.642, .228, -.044, .633, .46), (.774, .212, .084, .759, .357), (.902, .152, .139, .921, .353),
             (.50, .300, .188, .508, .512), (.478, .417, -.088, .494, .625)) if record == 24 else (
             (.100, .141, -.085, .095, .270), (.764, .107, -.09, .785, .209), (.903, .312, .160, .905, .613),
             (.798, .392, .129, .779, .522), (.627, .458, -.12, .643, .624))
    for x, y, z, tip_x, tip_y in hangs:
        start = tree.point(x, y, z)
        tip = tree.point(tip_x, tip_y, z+.014)
        tree.tube([start, start*.55+tip*.45+[.004, 0, .008], tip], [.006, .004, .0018], 4)
        ivy(tree, start, tip, .041, source)


def build(record, rgba):
    tree = Tree(record, rgba, STEMS[record])
    limb(tree, STEMS[record], 10)
    rgb = tree.pixels[..., :3].astype(float)
    solid = tree.pixels[..., 3]>128
    brown = solid & (rgb[..., 0]>rgb[..., 1]*1.02) & (rgb[..., 1]>rgb[..., 2]*1.03)
    green = solid & (rgb[..., 1]>rgb[..., 0]*.99) & (rgb[..., 1]>rgb[..., 2]*1.15)
    wood = brown | (solid & (distance_transform_edt(~brown)<2.8))
    if record in (16, 24, 25):
        # Retain the moss painted on the bole and limbs, including the far side.
        wood |= green & (distance_transform_edt(~brown)<(7 if record == 24 else 5))
    leaves = tree.pixels.copy()
    leaves[..., 3] = 255*(green | (solid & (distance_transform_edt(~green)<1.6)))
    parts = [tree.bark, tree.leaves]
    images = {"bark":filled(tree.pixels, wood), "leaves":Image.fromarray(leaves)}
    if record in CROWNS:
        stone = root_base(tree, record)
        if stone is not None:
            parts.append(stone)
            grey = solid & (rgb[..., 2]>=rgb[..., 0]*.96) & (rgb[..., 2]>=rgb[..., 1]*.95)
            grey[:int(len(rgb)*.69)] = False
            images["stone"] = filled(tree.pixels, grey)
        broad_crown(tree, record)
        if record == 16:
            for x, y, z in ((.435, .53, .058), (.532, .56, .065), (.47, .69, .107), (.589, .819, .09), (.36, .853, .10)):
                ivy(tree, tree.point(x, y, z), tree.point(x+.019, y+.093, z+.012), .044,
                    (.367/3, .493, .463/3, .600))
        images["leaves"] = canopy_texture(tree, images["leaves"], green, CROWNS[record][3][2])
    else:
        snag(tree, record)
        if record == 25:
            images["bark"] = shelf_texture(tree, images["bark"])
    height = max(max(p["positions"][1::3]) for p in parts)
    return {"height":height, "parts":parts}, images
