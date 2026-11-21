"""Summer mountain trees. Each whorl follows the height and asymmetric reach
of the painted branches. The evergreen boughs are fixed, folded cutout fans."""
import math

import numpy as np
from PIL import Image

from desert_trees import upright_tuft
from rainforest_palms import add_grid
from rainforest_trees import Tree
import woodland_trees as woodland


FORMS = {5: "sparse-mountain-pine", 11: "stepped-mountain-pine", 12: "slender-fir",
         13: "copper-branched-pine", 14: "gold-tipped-broadleaf", 15: "copper-tipped-broadleaf",
         16: "leaning-russet-broadleaf", 25: "open-skirt-spruce", 30: "flared-mountain-fir"}

# Painted stem x, down, depth and radius in the cropped sprite's frame.
STEMS = {
    5: ((.535, 1, 0, .087), (.539, .953, .008, .076), (.520, .860, .021, .055), (.486, .723, .027, .045),
        (.455, .602, .007, .041), (.489, .472, -.016, .037), (.488, .319, -.025, .027),
        (.498, .207, -.015, .022), (.486, .113, .002, .012), (.467, .017, .008, .005)),
    11: ((.608, 1, 0, .095), (.605, .944, .010, .083), (.560, .849, .022, .058), (.520, .728, .035, .048),
         (.446, .612, .013, .036), (.456, .526, -.019, .039), (.508, .438, -.034, .029),
         (.519, .324, -.012, .022), (.506, .186, .008, .015), (.505, .091, .016, .011), (.510, .008, .016, .003)),
    12: ((.551, 1, 0, .106), (.551, .953, .006, .100), (.538, .864, .013, .077), (.533, .702, .024, .054),
         (.511, .518, .017, .044), (.505, .357, -.002, .033), (.512, .206, -.016, .023),
         (.472, .105, -.009, .014), (.462, .014, 0, .004)),
    13: woodland.STEMS[13],
    25: ((.481, 1, 0, .083), (.485, .957, .008, .075), (.482, .834, .013, .048), (.487, .659, .006, .036),
         (.491, .489, -.010, .027), (.483, .304, -.017, .021), (.491, .162, -.010, .012), (.476, .008, 0, .004)),
    30: ((.504, 1, 0, .105), (.506, .942, .010, .088), (.501, .836, .016, .060), (.492, .675, .014, .046),
         (.489, .503, .001, .039), (.472, .348, -.017, .030), (.464, .207, -.019, .019),
         (.463, .101, -.007, .010), (.452, .008, 0, .003)),
}
# Centre of the needle tier (down the image), reach to left and right in image
# widths, number of primary limbs. Some tiers only carry a short limb on one side.
WHORLS = {
    5: ((.813, .460, .473, 5), (.707, .446, .296, 5), (.609, .461, .224, 4), (.505, .420, .299, 5),
        (.419, .105, .240, 3), (.324, .214, .300, 4), (.240, .223, .142, 3), (.163, .086, .176, 3), (.066, .114, .113, 3)),
    11: ((.818, .496, .414, 6), (.734, .404, .460, 5), (.613, .466, .206, 4), (.497, .340, .330, 5),
         (.423, .128, .231, 3), (.342, .195, .271, 4), (.246, .236, .136, 3), (.177, .049, .151, 3), (.092, .121, .118, 3)),
    12: ((.839, .474, .429, 7), (.744, .370, .432, 6), (.632, .498, .397, 7), (.527, .330, .410, 6),
         (.445, .275, .291, 6), (.348, .259, .316, 5), (.256, .212, .178, 5), (.178, .183, .182, 5),
         (.103, .109, .089, 4), (.045, .038, .060, 3)),
    13: ((.764, .483, .457, 6), (.620, .443, .478, 6), (.480, .399, .439, 5), (.379, .325, .285, 5),
         (.263, .322, .286, 5), (.169, .211, .241, 4), (.082, .156, .137, 4)),
    25: ((.746, .427, .498, 7), (.647, .478, .371, 6), (.523, .431, .453, 7), (.417, .346, .299, 6),
         (.322, .237, .236, 6), (.230, .184, .167, 5), (.151, .112, .125, 5), (.082, .069, .065, 4), (.039, .030, .030, 3)),
    30: ((.808, .474, .465, 8), (.713, .401, .445, 7), (.614, .322, .309, 7), (.495, .273, .345, 6),
         (.406, .279, .224, 6), (.303, .206, .190, 5), (.230, .186, .183, 5),
         (.150, .135, .123, 4), (.083, .070, .083, 4), (.036, .034, .035, 3)),
}
# Downward slope of the hanging boughs, in image heights per image width.
DROP = {12:.155, 25:.322, 30:.150}
BARK_BAND = {5:(.864, .965), 11:(.877, .963), 12:(.870, .962), 13:(.815, .958),
             25:(.835, .961), 30:(.855, .954)}


def bough_profile(tree, down, width, direction, drop, gap):
    """Sample a whole side bough, at its painted scale. Leave transparent
    space above the upper shoots and between the long tips below them."""
    pixels = tree.pixels
    h, w = pixels.shape[:2]
    along = np.linspace(0, 1, 64)
    v = np.linspace(0, 1, 56)[:, None]
    axis = tree.uv(tree.stem_at(down))[0]
    x = np.rint((axis+direction*width*(.025+1.07*along))*(w-1)).astype(int).clip(0, w-1)
    centre = down+drop*(along-.67)
    high = centre-gap*.61
    low = centre+gap*.67
    y = np.rint((high+v*(low-high))*(h-1)).astype(int).clip(0, h-1)
    tile = pixels[y, x].copy()
    rgb = tile[..., :3].astype(float)
    leaf = (rgb[..., 1] >= rgb[..., 0]*.96) & (rgb[..., 1] >= rgb[..., 2]*.94)
    paint = (tile[..., 3] > 128)&(leaf | (along < .35))
    light = rgb@[.21, .72, .07]
    lo, hi = np.quantile(light[paint], [.15, .82]) if paint.any() else (0, 1)
    shade = np.clip((light-lo)/max(hi-lo, 1), 0, 1)
    # Split the outer quarter into visible branchlets. Across the top and
    # underside of a tier, overlapping foliage ends in individual painted tips.
    top = .035+.085*(1-shade)+.042*np.cos(38*along)
    bottom = .84+.095*shade+.065*np.sin(42*along+direction)
    tips = .975-.13*abs(2*v-1)+.071*np.cos(15*v+direction)
    tile[..., 3] = 255*(paint & (v > top) & (v < bottom) & (along < tips))
    return Image.fromarray(tile), high, low


def shelf_pine(tree, record):
    crowns = woodland.Boughs(tree, record, .91)
    for tier, (down, left, right, count) in enumerate(WHORLS[record]):
        for i in range(count):
            angle = (i+.31*(tier%2)+tree.rng.uniform(-.17, .17))*math.tau/count
            radial = np.array([math.cos(angle), 0, math.sin(angle)])
            blend = .5+.5*radial[0]
            width = (left*(1-blend)+right*blend)*tree.rng.uniform(.91, 1.08)
            reach = width*tree.aspect
            stagger = tree.rng.uniform(-.024, .024)*min(1, width/.22)
            level = down+stagger
            # The bough lifts out of the fork, then levels off. Its outer clump
            # has thickness even when viewed from directly underneath or across.
            hub = tree.stem_at(level+.025+.052*width)
            node = tree.stem_at(level)+radial*reach*.568
            elbow = hub+(node-hub)*.51-[0, .013, 0]
            woodland.limb(tree, [hub, elbow, node], [max(.005, .045*width), max(.003, .031*width), .003], 4)
            x, y = tree.uv(node)
            height = min(.077, .017+.095*width)*tree.rng.uniform(.81, 1.15)
            crowns.grow(x, y, node[2]/tree.aspect, width*.51, height, width*.47, angle, sprays=3)
    if record == 5:
        # A small upright shoot and dark branch grow out of the right side of the foot.
        stub = [tree.point(x, y, z) for x, y, z in ((.55, .978, .012), (.617, .966, .041),
                (.684, .934, .051), (.746, .914, .076))]
        woodland.limb(tree, stub, [.026, .017, .010, .004], 4)
        image = Image.fromarray(tree.pixels)
        w, h = image.size
        chip = image.crop((round(.63*w), round(.90*h), round(.84*w), round(.977*h)))
        upright_tuft(tree, chip.resize((64, 56), Image.Resampling.NEAREST), tree.point(.710, .951, .073),
                     .075*tree.aspect, .036)
    elif record == 13:
        crowns.grow(.490, .045, 0, .081, .032, .074, .41, sprays=3)
    atlas = crowns.atlas()
    normals = np.array(tree.leaves["normals"]).reshape(-1, 3)
    height = np.array(tree.leaves["positions"][1::3])
    shade = .61+.31*np.clip(normals[:, 1], 0, 1)+.08*height
    colours = np.array(tree.leaves["colors"], dtype=float).reshape(-1, 4)
    colours[:, :3] = shade[:, None]
    tree.leaves["colors"] = colours.ravel().tolist()
    return atlas


def skirted_fir(tree, record):
    tiers = WHORLS[record]
    for tier, (down, left, right, count) in enumerate(tiers):
        neighbour = tiers[tier-1] if tier else tiers[1]
        gap = max(abs(down-neighbour[0]), .041)
        base = len(tree.patches)
        profiles = []
        for direction, width in ((-1, left), (1, right)):
            tile, high, low = bough_profile(tree, down, width, direction, DROP[record]*width, gap)
            tree.patches.append(tile)
            profiles.append((high, low))

        for i in range(count):
            az = (i+.19*(tier%3)+tree.rng.uniform(-.13, .13))*math.tau/count
            ray = np.array([math.cos(az), 0, math.sin(az)])
            side = np.cross(ray, [0, 1, 0])
            mix = .5+.5*ray[0]
            width = (left*(1-mix)+right*mix)*tree.rng.uniform(.90, 1.11)
            length = width*tree.aspect*1.14
            variant = int(ray[0] >= 0)
            high, low = profiles[variant]
            shift = tree.rng.uniform(-.022, .022)*min(1, width/.18)
            axis = tree.stem_at(max(.012, (high[0]+low[0])*.50+shift))
            bend = tree.rng.uniform(-.19, .19)
            # Mirror the painted side over an arch in depth. Each branch
            # has a crown, sides and a shaded underside even when seen end-on.
            for flank in (-1, 1):
                rows, uv = [], []
                for t in (0, .29, .56, .79, 1):
                    col = round(t*63)
                    top = high[col]+shift
                    depth = max(.012, low[col]-high[col])
                    ridge = axis+ray*t*length+side*bend*length*t*t
                    ridge[1] = 1-top
                    wing = (.09+.91*math.sin(math.pi*t)**.81)*length*.28
                    rows.append([ridge+side*flank*wing*q-ray*.11*length*q*t-
                                 [0, v*depth, 0] for q, v in ((.0, 0), (.85, .40), (.74, 1))])
                    uv.append([[((base+variant)%8+.01+.98*t)/8, (base+variant)//8+.01+.98*v] for v in (0, .40, 1)])
                if flank > 0:
                    rows = [row[::-1] for row in rows]
                    uv = [row[::-1] for row in uv]
                start = len(tree.leaves["colors"])
                add_grid(tree.leaves, np.array(rows), uv, double=True)
                colours = np.array(tree.leaves["colors"][start:], dtype=float).reshape(-1, 3, 4)
                colours[..., :3] = np.array([.58, .83, .97] if flank > 0 else [.97, .83, .58])[None, :, None]
                tree.leaves["colors"][start:] = colours.ravel().tolist()
            # The lowest axes can be seen against the sky below the skirt.
            if tier < 3:
                tip = axis+ray*length*.82
                tip[1] = 1-low[49]-shift+.008
                woodland.limb(tree, [axis, (axis+tip)*.5-[0, .010, 0], tip],
                              [max(.005, width*.044), max(.003, width*.025), .0025], 4)
    image = Image.fromarray(tree.pixels)
    w, h = image.size
    tip_x = tree.stem[-1, 0]
    patch = image.crop((round((tip_x-.066)*w), 0, round((tip_x+.066)*w)+1, round(.080*h)))
    upright_tuft(tree, patch.resize((64, 56), Image.Resampling.NEAREST), tree.stem_at(.043),
                 .054*tree.aspect, .037)
    return tree.cluster_atlas()


def build(record, rgba):
    if record in (14, 15, 16):
        # Same fork pattern as the summer woodland tree, repainted by the
        # regional recipe from this mountain sprite, including its copper foliage.
        return woodland.build(record, rgba)
    tree = Tree(record, rgba, STEMS[record])
    points = [tree.point(x, y, z) for x, y, z, _ in tree.stem]
    tree.tube(points, tree.stem[:, 3], 9, cap=True)
    projected = len(tree.bark["uvs"])
    spread = {5:.19, 11:.20, 12:.34, 13:.16, 25:.25, 30:.32}[record]
    for i in range(6):
        a = (i+tree.rng.uniform(-.18, .18))*math.tau/6
        ray = np.array([math.cos(a), 0, .85*math.sin(a)])*tree.aspect
        length = spread*tree.rng.uniform(.69, 1.09)
        foot = tree.stem_at(1.002)
        points = [tree.stem_at(.915 if record in (12, 30) else .946),
                  tree.stem_at(.961)+ray*length*.33, foot+ray*length*.74+[0, .011, 0], foot+ray*length]
        woodland.limb(tree, points, [spread*.195, spread*.148, spread*.071, .0025], 5)
    if record in (12, 30):
        for i in range(4):
            a = i*math.pi/2+.4
            ray = tree.aspect*np.array([math.cos(a), 0, math.sin(a)])
            woodland.limb(tree, [tree.stem_at(.78)+ray*.034, tree.stem_at(.889)+ray*.063,
                                tree.stem_at(.957)+ray*.105, tree.stem_at(1)+ray*.195],
                          [.011, .020, .026, .004], 5)
    if record in (25, 30):
        # Exposed lower branch stubs silhouette against the gaps under the skirt.
        for y, a, lift in ((.778, .24, -.021), (.848, 2.72, -.014), (.883, 5.17, .016)):
            hub = tree.stem_at(y)
            ray = np.array([math.cos(a), 0, math.sin(a)])*tree.aspect
            woodland.limb(tree, [hub, hub+ray*.079+[0, lift, 0], hub+ray*.129+[0, lift+.009, 0]],
                          [.014, .009, .003], 4)
    leaves = skirted_fir(tree, record) if record in DROP else shelf_pine(tree, record)
    return {"height":max(max(p["positions"][1::3]) for p in (tree.bark, tree.leaves)),
            "parts":[tree.bark, tree.leaves]}, {"bark":woodland.bark_texture(tree, BARK_BAND[record], projected),
                                              "leaves":leaves}
