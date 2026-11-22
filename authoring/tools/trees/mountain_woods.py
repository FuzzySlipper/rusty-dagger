"""The broken snag and the short forked broadleaf of archive 510.
Traces use the cropped painting's x, downward y, depth and radius."""
import math

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

from desert_trees import upright_tuft
from rainforest_palms import add_grid, colour_ramp
from rainforest_trees import Tree, filled
import woodland_trees as woodland


FORMS = {16: "gnarled", 21: "forked-spire-tree"}

SNAG = ((.419, 1.002, 0, .064), (.399, .936, .018, .120), (.384, .846, .041, .134),
        (.358, .759, .071, .146), (.368, .692, .065, .157), (.413, .625, .037, .166),
        (.495, .565, .006, .151), (.525, .499, -.033, .125), (.514, .433, -.048, .109),
        (.488, .373, -.029, .090), (.498, .308, .009, .083), (.497, .249, .028, .077),
        (.489, .208, .032, .059))
# The tall arm ends in a sharp splinter, while the other is shorter and blunter.
BREAKS = (
    ((.493, .349, .005, .087), (.564, .296, -.043, .074), (.630, .259, -.091, .056),
     (.698, .223, -.114, .049), (.729, .173, -.112, .040), (.783, .139, -.093, .031),
     (.846, .140, -.065, .024), (.902, .116, -.054, .020), (.947, .061, -.037, .013), (.994, .006, -.021, .004)),
    ((.476, .341, .012, .083), (.393, .320, .057, .066), (.323, .295, .087, .049),
     (.286, .253, .098, .041), (.258, .219, .118, .033), (.213, .204, .125, .023), (.171, .140, .104, .008)),
    ((.466, .325, -.003, .058), (.489, .290, -.104, .046), (.476, .248, -.174, .032), (.503, .210, -.208, .021)),
    ((.510, .308, .022, .053), (.529, .272, .093, .043), (.518, .227, .125, .028), (.543, .189, .147, .013)),
)
ROOTS = (
    ((.359, .767, .054, .076), (.341, .853, .120, .074), (.308, .910, .164, .056),
     (.250, .923, .156, .045), (.162, .902, .122, .039), (.090, .927, .102, .021), (.008, .986, .100, .005)),
    ((.382, .766, .068, .071), (.431, .829, .159, .068), (.427, .891, .218, .054),
     (.367, .929, .248, .037), (.343, .973, .257, .022), (.294, .995, .232, .004)),
    ((.402, .778, .035, .079), (.469, .815, .087, .079), (.506, .885, .119, .061),
     (.572, .905, .126, .054), (.629, .881, .109, .053), (.692, .903, .099, .044),
     (.723, .961, .074, .032), (.816, .987, .064, .006)),
    ((.384, .803, -.012, .068), (.362, .863, -.112, .065), (.316, .891, -.197, .043),
     (.279, .948, -.261, .030), (.214, .992, -.287, .005)),
    ((.418, .856, .050, .058), (.465, .907, .111, .053), (.457, .969, .157, .043),
     (.483, .994, .197, .009)),
    ((.370, .850, -.037, .052), (.402, .923, -.117, .050), (.395, .980, -.216, .033), (.353, .995, -.264, .005)),
    ((.452, .806, .008, .086), (.495, .866, -.088, .068), (.544, .904, -.181, .050),
     (.532, .946, -.246, .034), (.588, .996, -.280, .008)),
)

SPIRE = ((.507, 1, 0, .095), (.515, .947, .014, .097), (.532, .906, .041, .080),
         (.562, .860, .062, .090), (.539, .828, .050, .092), (.477, .801, .022, .077),
         (.428, .768, -.010, .066), (.422, .725, -.035, .059), (.468, .687, -.041, .057),
         (.508, .646, -.025, .052), (.533, .605, .001, .047), (.529, .550, .024, .041),
         (.501, .477, .036, .033), (.492, .378, .021, .023), (.470, .269, -.004, .014), (.495, .160, -.022, .008))
FORKS = (
    (.715, ((.355, .679, .044, .039), (.289, .662, .081, .033), (.246, .627, .100, .025),
            (.237, .573, .107, .021), (.235, .480, .116, .011))),
    (.754, ((.526, .731, .022, .041), (.609, .719, .054, .035), (.682, .693, .097, .026),
            (.744, .666, .124, .023), (.756, .604, .122, .013))),
    (.608, ((.465, .580, -.073, .032), (.382, .569, -.114, .026), (.329, .536, -.156, .020),
            (.292, .467, -.188, .012))),
    (.610, ((.567, .561, -.058, .031), (.622, .512, -.102, .027), (.656, .450, -.125, .019),
            (.642, .363, -.143, .011))),
)
# Centres, horizontal radius, height and depth of the individual leafy masses.
# The lowest boughs hang to either side, exposing the painted Y above the bend.
CROWN = ((.500, .105, -.014, .155, .124, .137), (.400, .173, .038, .170, .105, .144),
         (.599, .187, -.045, .171, .112, .145), (.399, .268, -.128, .224, .135, .170),
         (.575, .288, .149, .216, .140, .175), (.276, .318, .091, .221, .129, .181),
         (.719, .343, -.078, .191, .119, .179), (.223, .436, -.089, .202, .136, .174),
         (.440, .414, .157, .258, .141, .204), (.583, .421, -.163, .248, .150, .211),
         (.752, .465, .115, .213, .131, .179), (.186, .563, .125, .196, .126, .169),
         (.328, .548, -.117, .177, .105, .165), (.797, .577, -.088, .198, .124, .154),
         (.877, .531, .062, .111, .093, .127), (.419, .582, -.006, .191, .106, .158),
         (.555, .514, .141, .150, .106, .134), (.740, .617, .059, .146, .086, .129))


class CarvedBark:
    """Unroll each limb along its own rings. Even the back of a twisted root
    gets continuous grain across its circumference, with no change of chart."""
    def __init__(self, tree, wood=None, frost=None):
        self.tree = tree
        pixels = tree.pixels
        if wood is None:
            wood = pixels[..., 3] > 128
        self.wood = np.array(filled(pixels, wood))
        self.distance = distance_transform_edt(pixels[..., 3] <= 128)
        self.frost = (distance_transform_edt(~frost, return_indices=True)
                      if frost is not None and frost.any() else None)
        self.tiles = []
        colours = pixels[wood, :3].astype(float)
        self.ramp = np.array(colour_ramp(colours, [.03, .12, .26, .43, .60, .76, .90, .98]))

    def map_rings(self, rings, centres, start):
        tree = self.tree
        cols = rings.shape[1]
        path = np.r_[0., np.cumsum(np.linalg.norm(np.diff(centres, axis=0), axis=1))]
        path /= path[-1]
        around = np.linspace(0, 1, cols)
        # Sample a little inside the painted outline. A neighbouring patch
        # of grain continues through any sky outside the traced side of the limb.
        mapped = tree.uv(centres[:, None, :]+.82*(rings-centres[:, None, :]))
        width, height = (96, 112) if not self.tiles else (48, 56)
        across = np.linspace(0, 1, width)
        along = np.linspace(0, 1, height)
        strips = np.array([[np.interp(across, around, row[:, k]) for k in (0, 1)] for row in mapped])
        u, v = [np.column_stack([np.interp(along, path, strips[:, k, j]) for j in range(width)]) for k in (0, 1)]
        h, w = self.wood.shape[:2]
        x = np.rint(u*(w-1)).astype(int)
        y = np.rint(v*(h-1)).astype(int)
        pixels = self.wood[y, x].copy()
        yy, xx = np.indices(x.shape)
        # An edge-on projection compresses the whole side into a single
        # column of the painting. Carry the bark's fine longitudinal furrows
        # through those faces, following the same turn as the modeled ridges.
        radial = rings-centres[:, None, :]
        radial /= np.maximum(np.linalg.norm(radial, axis=2, keepdims=True), 1e-9)
        facing = np.array([np.interp(across, around, row[:, 2]) for row in radial])
        face = np.column_stack([np.interp(along, path, facing[:, j]) for j in range(width)])
        angle = math.tau*xx/(width-1)
        down = yy/(height-1)
        twist = (3.1 if not self.tiles else .77)*down
        flow = angle+twist+.19*np.sin(2*angle+down*4.44)+.055*np.cos(5*angle-down*8.54)
        tone = .46+.20*np.sin(5*flow)+.13*np.cos(11*flow+.7)+.09*np.sin(18*flow-down*3.58)
        crack = np.sin(8*flow) > .83+.12*np.sin(down*6.94-2*angle)
        level = np.clip(np.rint(7*(tone-.22*crack)).astype(int), 0, 7)
        direct = (.18+.76*np.clip((abs(face)-.18)/.62, 0, 1))*(1-.76*np.clip(self.distance[y, x]/5, 0, 1))
        pixels[..., :3] = np.rint(direct[..., None]*pixels[..., :3]+(1-direct[..., None])*self.ramp[level, :3])
        if self.frost is not None:
            # The light rim of a skinny icy twig is often only one painted
            # texel wide. Carry it around the edge independently of the dry
            # wood palette, including at the end where a twig is snapped.
            distance, nearest = self.frost
            rim = distance[y,x] <= max(1.25,h*.006)
            fy,fx = nearest[:,y,x]
            pixels[rim,:3] = tree.pixels[fy[rim],fx[rim],:3]
        slot = len(self.tiles)
        self.tiles.append(Image.fromarray(pixels))
        u, v = np.meshgrid(around, path)
        uv = np.stack([.005+.99*u, slot+.005+.99*v], axis=-1)
        tree.bark["uvs"][start:start+uv.size] = uv.round(6).ravel().tolist()
        return slot

    def tube(self, centres, radii, sides):
        tree = self.tree
        start = len(tree.bark["uvs"])
        tree.tube(centres, radii, sides, cap=True, wrap_height=1)
        n = len(centres)*(sides+1)
        first = start//2*3
        rings = np.array(tree.bark["positions"][first:first+3*n]).reshape(len(centres), sides+1, 3)
        slot = self.map_rings(rings, np.array(centres), start)
        tree.bark["uvs"][start+n*2:] = [.46, slot+.97]*(sides+1)

    def atlas(self):
        # Keep the bole at full resolution; the smaller limbs need a quarter
        # of its texels. All of them still share the single opaque bark draw.
        height = 112+56*math.ceil((len(self.tiles)-1)/4)
        image = Image.new("RGBA", (192, height))
        uv = np.array(self.tree.bark["uvs"]).reshape(-1, 2)
        slots = np.floor(uv[:, 1]).astype(int)
        for i, tile in enumerate(self.tiles):
            x, y = (0, 0) if i == 0 else (48*((i-1)%4), 112+56*((i-1)//4))
            image.paste(tile, (x, y))
            group = slots == i
            uv[group, 0] = (x+uv[group, 0]*tile.width)/image.width
            uv[group, 1] = (y+(uv[group, 1]-i)*tile.height)/image.height
        self.tree.bark["uvs"] = uv.round(6).ravel().tolist()
        return image


def snag(rgba):
    tree = Tree(16, rgba, SNAG)
    bark = CarvedBark(tree)
    # A changing, fluted cross section follows the sweeping grain from root
    # to shoulder. The broken rim has raised splinters and a recessed centre.
    angle = np.linspace(0, math.tau, 17)
    rings, centres = [], []
    for x, down, z, r in SNAG:
        centre = tree.point(x, down, z)
        twist = (1-down)*4.8+.32*math.sin(down*math.tau)
        wave = 1+.112*np.cos(4*(angle+twist))+.065*np.sin(7*(angle+twist))
        jag = max(0, 1-abs(down-.208)/.070)*(.017*np.cos(3*angle)+.021*np.sin(7*angle))
        if down > .99:
            jag += .009+.011*np.sin(3*angle+.5)
        rings.append(centre+np.stack([tree.aspect*r*wave*np.cos(angle), jag,
                                     tree.aspect*.87*r*wave*np.sin(angle)], axis=1))
        centres.append(centre)
    rings = np.array(rings)
    add_grid(tree.bark, rings, np.zeros((*rings.shape[:2], 2)), seam=True)
    bark.map_rings(rings, np.array(centres), 0)
    inner = centres[-1]+(rings[-1]-centres[-1])*.52-[0, .031, 0]
    rim_start = len(tree.bark["uvs"])
    tree.painted_grid(tree.bark, np.array([rings[-1], inner]), np.array([centres[-1], centres[-1]]))
    tree.cap(tree.bark, inner, np.array([0., 1., 0.]))
    # A darker recessed face under the jagged rim, inside the first bark chart.
    uv = tree.bark["uvs"]
    uv[rim_start::2] = [.14+u*.45 for u in uv[rim_start::2]]
    uv[rim_start+1::2] = [.82+v*.12 for v in uv[rim_start+1::2]]
    colours = tree.bark["colors"]
    for channel in range(3):
        colours[rim_start*2+channel::4] = [.44]*len(colours[rim_start*2+channel::4])
    for trace in (*BREAKS, *ROOTS):
        bark.tube([tree.point(x, y, z) for x, y, z, _ in trace], [r for _, _, _, r in trace], 7)
    for trace in (
        ((.231, .221, .120, .020), (.201, .192, .139, .014), (.193, .155, .142, .003)),
        ((.291, .266, .095, .027), (.266, .233, .067, .019), (.230, .211, .058, .005)),
        ((.732, .187, -.11, .027), (.737, .148, -.136, .020), (.763, .105, -.141, .004)),
        ((.815, .143, -.074, .022), (.853, .126, -.120, .013), (.878, .094, -.141, .003)),
    ):
        bark.tube([tree.point(x, y, z) for x, y, z, _ in trace], [r for _, _, _, r in trace], 5)
    # Shallow raised ribs remain embedded in the bole. The texture supplies
    # the finer cracks, including the large ochre sweep across the front.
    for i in range(5):
        centres, radii = [], []
        for t in np.linspace(0, 1, 13):
            down = .945-.610*t
            az = i*math.tau/5+.61-t*4.55+.26*math.sin(2*math.pi*t)
            r = np.interp(down, tree.stem[::-1, 1], tree.stem[::-1, 3])
            phase = az+(1-down)*4.8+.32*math.sin(down*math.tau)
            edge = 1+.112*math.cos(4*phase)+.065*math.sin(7*phase)
            centre = tree.stem_at(down)+.91*edge*r*tree.aspect*np.array([math.cos(az), 0, .87*math.sin(az)])
            centres.append(centre)
            radii.append(.005+.027*math.sin(math.pi*t)**.74)
        bark.tube(centres, radii, 5)
    return {"height":max(tree.bark["positions"][1::3]), "parts":[tree.bark]}, {
        "bark":bark.atlas()}


def spire(rgba):
    tree = Tree(21, rgba, SPIRE)
    tree.tube([tree.point(x, y, z) for x, y, z, _ in SPIRE], tree.stem[:, 3], 8, cap=True)
    anchors = [tree.point(x, y, z) for x, y, z, _ in SPIRE]
    for down, trace in FORKS:
        centres = [tree.stem_at(down)]+[tree.point(x, y, z) for x, y, z, _ in trace]
        tree.tube(centres, [trace[0][3]*1.18]+[r for _, _, _, r in trace], 6, cap=True)
        anchors.extend(centres[1:])
    projected = len(tree.bark["uvs"])
    for i in range(6):
        az = i*math.tau/6+.47
        ray = np.array([math.cos(az), 0, .87*math.sin(az)])*tree.aspect
        extent = tree.rng.uniform(.218, .320)
        woodland.limb(tree, [tree.stem_at(.881), tree.stem_at(.947)+ray*extent*.33,
                            tree.stem_at(.980)+ray*extent*.70, tree.stem_at(1.002)+ray*extent],
                      [.052, .045, .022, .003], 5)
    boughs = woodland.Boughs(tree, 21, .689)
    # Keep the russet fork in the wood; an upper branch visible between
    # the painted leaves must not dangle on a leaf cutout from the back.
    leaf_paint = np.array(boughs.paint)
    pigment = leaf_paint[..., :3].astype(float)
    green = pigment[..., 1] >= pigment[..., 0]*1.015
    leaf_paint[..., 3] *= green
    boughs.paint = Image.fromarray(leaf_paint)
    core = np.array(boughs.interior)
    clipped = distance_transform_edt(leaf_paint[..., 3] == 0) > 2
    core[clipped, 3] *= (core[..., :3]@[.21, .72, .07])[clipped] > 53
    boughs.interior = Image.fromarray(core)
    anchors = np.array(anchors)
    for i, (x, y, z, width, height, depth) in enumerate(CROWN):
        if y > .37:
            woodland.connect(tree, anchors, tree.point(x, y, z), min(.017, width*.09))
        boughs.grow(x, y, z, width, height, depth, i*math.tau*.382, sprays=3)
    # Small sprays carry the actual jagged foliage boundary, including the
    # hanging tips on either side of the open fork and the point at the top.
    w, h = boughs.paint.size
    for x, y, z, rx, ry in ((.493, .061, .006, .099, .074), (.348, .199, -.057, .139, .104),
                         (.206, .341, .059, .133, .105), (.125, .559, .116, .111, .088),
                         (.857, .553, .046, .127, .098), (.782, .627, -.051, .141, .083),
                         (.491, .374, .239, .170, .111), (.515, .310, -.209, .136, .100)):
        patch = boughs.paint.crop((round((x-rx)*w), round((y-ry)*h),
                  round((x+rx)*w)+1, round((y+ry)*h)+1)).resize((64, 56), Image.Resampling.NEAREST)
        chip = np.array(patch)
        py, px = np.indices(chip.shape[:2])
        u, v = (px-31.5)/32, (py-27.5)/28
        lit = chip[..., :3]@[.21, .72, .07]
        edge = .64+.27*np.clip((lit-19)/91, 0, 1)+.076*np.sin(8*np.arctan2(v, u)+x*12)
        chip[..., 3] *= (u*u+v*v) < edge**2
        upright_tuft(tree, Image.fromarray(chip), tree.point(x, y, z), 1.12*rx*tree.aspect, ry)
    leaves = boughs.atlas()
    normals = np.array(tree.leaves["normals"]).reshape(-1, 3)
    height = np.array(tree.leaves["positions"][1::3])
    shade = .54+.28*np.clip(normals[:, 1], 0, 1)+.18*np.clip((height-.32)/.65, 0, 1)
    colours = np.array(tree.leaves["colors"], dtype=float).reshape(-1, 4)
    colours[:, :3] = shade[:, None].round(4)
    tree.leaves["colors"] = colours.ravel().tolist()
    bark = woodland.bark_texture(tree, (.753, .965), projected)
    rgb = tree.pixels[..., :3].astype(float)
    solid = tree.pixels[..., 3] > 128
    warm = solid & (rgb[..., 0] > rgb[..., 1]*1.05) & (np.arange(h)[:, None] > .37*h)
    wood = solid & (distance_transform_edt(~warm) < 1.65)
    bark.paste(filled(tree.pixels, wood), (96, 0))
    parts = [tree.bark, tree.leaves]
    return {"height":max(max(p["positions"][1::3]) for p in parts), "parts":parts}, {
        "bark":bark, "leaves":leaves}


def build(record, rgba):
    return snag(rgba) if record == 16 else spire(rgba)
