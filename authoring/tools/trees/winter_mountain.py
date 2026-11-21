"""The four winter mountain paintings with their own branchwork. The remaining
records use the identical winter woodland paintings at their own placement sizes.
"""
import math

import numpy as np
from PIL import Image

import mountain_trees as mountain
import woodland_trees as woodland
from rainforest_palms import add_grid, part
from rainforest_trees import Tree
from winter_woodland import WinterBoughs, add_bark_snow, bark_chart, snow_mask, snow_strip


FORMS = {5: "snow-tufted-pine", 11: "snow-laden-stepped-pine",
         12: "narrow-snow-fir", 23: "snow-capped-umbrella-tree"}

# x, down, depth and radius in the cropped painting. The snowy pines expose
# long intervals of trunk; 23 forks just below its low umbrella of leaves.
STEMS = {
    5: ((.517, 1, 0, .087), (.516, .939, .010, .076), (.515, .816, .020, .056),
        (.507, .665, .012, .044), (.486, .515, -.012, .036), (.490, .351, -.020, .028),
        (.477, .209, -.007, .018), (.484, .089, .008, .011), (.474, .018, .011, .004)),
    11: ((.564, 1, 0, .084), (.552, .946, .010, .072), (.548, .837, .024, .059),
         (.527, .680, .031, .049), (.495, .516, .016, .035), (.495, .358, -.016, .028),
         (.491, .237, -.028, .021), (.508, .123, -.008, .012), (.505, .034, .008, .005)),
    12: ((.579, 1, 0, .101), (.573, .941, .015, .093), (.560, .818, .018, .068),
         (.539, .680, .006, .051), (.527, .532, -.011, .042), (.504, .394, -.014, .031),
         (.496, .253, -.009, .025), (.479, .129, .003, .012), (.469, .013, .006, .004)),
    23: ((.553, 1, 0, .063), (.553, .950, .008, .055), (.553, .838, .012, .035),
         (.552, .737, .005, .034), (.535, .683, -.008, .036), (.488, .632, -.030, .029),
         (.447, .552, -.044, .022), (.414, .467, -.038, .018), (.413, .331, -.018, .013),
         (.432, .185, -.003, .007)),
}
# Centre, depth, half-width and relief of the snow on the umbrella crown.
# Several lobes meet under the continuous drift on its upper fork.
UMBRELLA_PADS = ((.287, .142, -.058, .244, .117), (.494, .088, -.134, .280, .096),
         (.722, .142, -.071, .233, .083),
         (.179, .343, .112, .182, .102), (.339, .292, .226, .168, .105),
         (.582, .290, .144, .168, .093), (.779, .309, .131, .213, .108),
         (.308, .309, -.183, .155, .092), (.707, .258, -.192, .166, .090))

UMBRELLA_ARMS = (
    (.724, ((.611, .661, .033, .031), (.663, .572, .074, .027), (.708, .504, .087, .020),
            (.730, .410, .091, .012), (.746, .309, .096, .007))),
    (.686, ((.478, .642, .078, .028), (.390, .605, .119, .021), (.322, .576, .132, .018),
            (.258, .483, .142, .014), (.218, .382, .136, .007))),
    (.606, ((.481, .503, -.103, .023), (.491, .391, -.158, .019), (.510, .308, -.192, .011),
            (.493, .156, -.173, .006))),
    (.672, ((.604, .618, -.068, .024), (.661, .527, -.106, .020), (.693, .394, -.152, .014),
            (.710, .228, -.140, .007))),
)

# Centre height and left/right reach of each snow-bearing tier, traced in
# the winter painting. The narrow fir has deeper hanging branches than the pines.
TIERS = {
    5: ((.803, .494, .446, 5), (.686, .488, .267, 5), (.586, .318, .241, 4),
        (.483, .373, .262, 5), (.396, .209, .265, 3), (.298, .187, .248, 4),
        (.217, .161, .070, 3), (.148, .107, .090, 3), (.044, .071, .068, 3)),
    11: ((.810, .475, .445, 6), (.736, .294, .465, 4), (.686, .456, .251, 5),
         (.589, .412, .279, 5), (.461, .395, .296, 5), (.390, .126, .291, 3),
         (.321, .225, .310, 4), (.245, .230, .119, 3), (.168, .123, .150, 3), (.075, .097, .103, 3)),
    12: ((.804, .437, .418, 7), (.715, .514, .425, 7), (.615, .432, .384, 6),
         (.507, .369, .326, 6), (.428, .262, .335, 6), (.341, .184, .207, 5),
         (.243, .172, .192, 5), (.161, .147, .145, 4), (.091, .059, .073, 4), (.040, .026, .037, 3)),
}
SLOPE = {5:.023, 11:.044, 12:.176}
BARK_BAND = {5:(.873, .965), 11:(.857, .962), 12:(.845, .962), 23:(.713, .948)}


def hanging_twigs(tree, anchors):
    """Short bare twigs and ragged cutouts hang from the lip of the umbrella."""
    image = Image.fromarray(tree.pixels)
    w, h = image.size
    traces = ((.17, .373, .096, .136, .168), (.293, .405, .171, .122, .186),
              (.402, .328, -.135, .113, .138), (.619, .363, .122, .119, .141),
              (.792, .365, .097, .140, .180), (.846, .332, -.088, .102, .165),
              (.329, .444, -.171, .082, .163), (.699, .420, -.199, .081, .149))
    for i, (x, y, z, width, fall) in enumerate(traces):
        top = tree.point(x, y, z)
        bend = tree.point(x+.025*math.sin(i), y+fall*.52, z+.031)
        end = tree.point(x+.030*math.cos(i), y+fall, z+.050)
        woodland.connect(tree, anchors, top, .012)
        woodland.limb(tree, [top, bend, end], [.013, .009, .003], 4)
        box = (round((x-width)*w), round(y*h), round((x+width)*w)+1, round((y+fall*1.5)*h)+1)
        patch = np.array(image.crop(box).resize((64, 56), Image.Resampling.NEAREST))
        v, u = np.indices((56, 64))
        u, v = (u-31.5)/32, v/55
        patch[..., 3] *= (abs(u) < .90-.21*v+.06*np.sin(v*17+i)) & (v < .91+.075*np.cos(u*9+i))
        slot = len(tree.patches)
        tree.patches.append(Image.fromarray(patch))
        for az in (i*2.4, i*2.4+1.9):
            ray = np.array([math.cos(az), 0, math.sin(az)])*width*tree.aspect
            side = np.cross(ray, [0, 1, 0])
            rows, uv = [], []
            for t, spread, reach in ((0, .51, -.16), (.46, .92, .29), (1, .68, .43)):
                centre = top+ray*reach-[0, t*fall*1.42, 0]
                rows.append([centre+k*side*spread-ray*.22*k*k for k in (1, 0, -1)])
                uv.append([[(slot%8+u)/8, slot//8+.01+.98*t] for u in (.006, .5, .994)])
            add_grid(tree.leaves, np.array(rows), uv, double=True)


def snow_on_leader(tree, strips, record):
    # The upright shoot carries a little cap right up to its point. The fir
    # has a longer white stripe down one side of the shoot.
    base = {5:.046, 11:.072, 12:.128}[record]
    sides = np.linspace(0, math.tau, 7)
    rows, uv = [], []
    for t, r in ((0, .005), (.34, .013), (.80, .009), (1, .002)):
        centre = tree.stem_at(base*(1-t)+.002)
        centre[0] -= (.007-.003*t)*tree.aspect
        centre[2] += .004*tree.aspect
        rows.append(centre+tree.aspect*r*np.stack([np.cos(sides), np.zeros_like(sides), np.sin(sides)], axis=1))
        uv.append(np.stack([.05+.90*sides/math.tau, np.full_like(sides, .89-.78*t)], axis=1))
    add_grid(strips, np.array(rows), uv, seam=True)
    first = len(strips["uvs"])
    tree.cap(strips, rows[-1], np.array([0., 1., 0.]))
    strips["uvs"][first:] = [.50, .12]*(len(sides))


def umbrella_drift(tree):
    # One joined drift bridges the two high forks. The actual painted lip
    # determines where it ends and exposes the dark twigs underneath.
    image = Image.fromarray(tree.pixels)
    w, h = image.size
    left, top, right, bottom = .023, .002, .898, .265
    patch = np.array(image.crop((round(w*left), round(h*top), round(w*right)+1, round(h*bottom)+1))
                     .resize((64, 56), Image.Resampling.NEAREST))
    patch[..., 3] *= snow_mask(patch)
    slot = len(tree.patches)
    tree.patches.append(Image.fromarray(patch))
    a = np.linspace(0, math.tau, 13)
    outline = 1+.048*np.cos(3*a+.8)+.032*np.sin(5*a)
    rows, uv = [], []
    for down, radius in ((.204, .91), (.135, 1), (.073, .89), (.040, .65), (.022, .29), (.022, .024)):
        xs = .460+.437*radius*outline*np.cos(a)
        zs = -.081+.250*radius*outline*np.sin(a)
        ys = down+radius*(.014*np.sin(a*3+.6)-.025*np.cos(a))
        rows.append(np.array([tree.point(x, y, z) for x, y, z in zip(xs, ys, zs)]))
        u = ((xs-left)/(right-left)).clip(.01, .99)
        v = ((ys-top+.023*np.sin(a)*radius)/(bottom-top)).clip(.01, .99)
        uv.append(np.stack([(slot%8+u)/8, slot//8+v], axis=1))
    add_grid(tree.leaves, np.array(rows), uv, seam=True)


def build(record, rgba):
    tree = Tree(record, rgba, STEMS[record])
    tree.tube([tree.point(x, y, z) for x, y, z, _ in tree.stem], tree.stem[:, 3], 9, cap=True)
    projected = len(tree.bark["uvs"])
    strips = part("bark")
    spread = {5:.201, 11:.243, 12:.325, 23:.152}[record]
    for i in range(5):
        az = math.tau*(i+tree.rng.uniform(-.22, .22))/5
        ray = np.array([math.cos(az), 0, .86*math.sin(az)])*tree.aspect
        length = tree.rng.uniform(.71, 1.09)*spread
        pts = [tree.stem_at(.905), tree.stem_at(.955)+ray*length*.34,
               tree.stem_at(.985)+ray*length*.67, tree.stem_at(1.002)+ray*length]
        radii = [spread*.207, spread*.173, spread*.092, .003]
        woodland.limb(tree, pts, radii, 5)
        if record != 5 and i%2 == 0:
            snow_strip(tree, pts[-3:], radii[-3:], strips)
    if record != 23:
        paint = WinterBoughs(tree, skirt=.89)
        levels = ((.50, .818, -.01, .292, .051), (.42, .698, -.03, .238, .045))
        if record == 12:
            levels = ((.538, .796, -.01, .295, .063), (.511, .624, .02, .212, .049))
        for clump in levels:
            paint.undergrowth(*clump)
        leaves = mountain.skirted_fir(tree, TIERS[record], SLOPE[record], snowy=True)
        snow_on_leader(tree, strips, record)
    else:
        paint = WinterBoughs(tree, skirt=.69, broad=True, brown=True)
        anchors = [tree.point(x, y, z) for x, y, z, _ in tree.stem]
        for attach, trace in UMBRELLA_ARMS:
            pts = [tree.stem_at(attach)]+[tree.point(x, y, z) for x, y, z, _ in trace]
            radii = [trace[0][3]*1.16]+[r for _, _, _, r in trace]
            woodland.limb(tree, pts, radii, 6)
            anchors.extend(pts[1:])
        anchors = np.array(anchors)
        paint.undergrowth(.44, .282, -.041, .285, .151)
        paint.undergrowth(.655, .345, .017, .224, .151)
        hanging_twigs(tree, anchors)
        for x, y, z, r, h in UMBRELLA_PADS:
            woodland.connect(tree, anchors, tree.point(x, y+.60*h, z), .016)
            paint.grow(x, y, z, r, h, .68)
        umbrella_drift(tree)
        leaves = tree.cluster_atlas()
    bark = bark_chart(tree, BARK_BAND[record], projected)
    images = {"bark": add_bark_snow(tree, bark, strips), "leaves": leaves}
    parts = [tree.bark, tree.leaves]
    height = max(max(p["positions"][1::3]) for p in parts)
    return {"height": height, "parts": parts}, images
