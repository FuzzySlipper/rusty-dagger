"""Winter counterparts of the woodland trees. Each bough carries the outline,
shadows and snow palette of its own patch in the refreshed painting."""
import math

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt, uniform_filter

from desert_trees import trunk_paint
import winter_oak
from rainforest_palms import add_grid, colour_ramp, part
from rainforest_trees import Tree, filled
from subtropical_trees import rock_grove
import woodland_trees as summer


FORMS = {**summer.FORMS, 15: "bare-forked-oak"}

# Winter 16 leans to the right. The silver trunk on 14 has a low left arm,
# and 15 is an almost leafless old tree with two large upright forks.
STEMS = {
    **summer.STEMS,
    14: ((.554, 1, 0, .074), (.559, .931, .006, .053), (.547, .838, .021, .040), (.546, .714, .025, .039),
         (.564, .624, .006, .036), (.542, .544, -.023, .029), (.499, .485, -.042, .027),
         (.465, .400, -.040, .023), (.421, .335, -.016, .016), (.393, .206, .016, .009)),
    16: tuple((1-x, y, -z, r) for x, y, z, r in summer.STEMS[16]),
}
ARMS = {
    14: ((.715, ((.476, .660, .046, .029), (.412, .626, .085, .024), (.361, .605, .091, .016),
                (.282, .586, .106, .008))),
         (.665, ((.630, .627, -.062, .027), (.700, .564, -.108, .022), (.718, .486, -.114, .018),
                (.749, .424, -.098, .010))),
         (.551, ((.603, .500, .054, .024), (.623, .406, .087, .017), (.670, .331, .094, .009))),
         (.450, ((.406, .409, .065, .020), (.341, .345, .118, .016), (.305, .251, .135, .008))),
         (.329, ((.481, .277, -.067, .019), (.600, .219, -.135, .013), (.679, .161, -.142, .007)))),
    16: tuple((attach, tuple((1-x, y, -z, r) for x, y, z, r in trace))
              for attach, trace in summer.ARMS[16]),

}

# Each pad names the snow's centre (x, down, depth), its half-width and relief.
# The darker leaf skirts extend below and past the snow. Gaps between pads
# in the painting determine where the main limbs are visible.
PADS = {
 10: ((.45, .083, .025, .205, .059), (.75, .114, -.077, .177, .051), (.401, .232, .115, .128, .052),
      (.837, .318, -.094, .122, .052), (.191, .466, .060, .173, .052), (.090, .545, .149, .074, .034),
      (.776, .620, .068, .085, .037)),
 11: ((.456, .053, .029, .292, .057), (.245, .187, -.108, .209, .047), (.574, .277, .147, .122, .036),
      (.895, .300, -.11, .111, .029), (.744, .411, .078, .214, .043), (.416, .591, .106, .144, .047),
      (.698, .787, -.047, .162, .046)),
 12: ((.212, .102, -.105, .127, .087), (.360, .072, .040, .079, .067), (.556, .081, -.170, .135, .083),
      (.781, .132, -.054, .145, .063), (.124, .292, .038, .156, .075), (.302, .263, -.237, .131, .073),
      (.412, .221, .144, .090, .081), (.535, .259, .278, .108, .071), (.711, .225, .052, .083, .062),
      (.850, .270, .129, .138, .081), (.272, .479, .167, .139, .067), (.636, .424, .172, .165, .071),
      (.385, .440, -.216, .125, .062), (.734, .400, -.187, .118, .067)),
 13: ((.480, .080, .013, .155, .051), (.633, .171, -.075, .110, .041), (.379, .199, .091, .152, .043),
      (.262, .279, -.091, .141, .040), (.651, .256, .122, .169, .052), (.467, .306, -.160, .120, .047),
      (.327, .380, .093, .154, .052), (.650, .369, -.087, .198, .052), (.781, .455, .094, .117, .057),
      (.333, .477, -.099, .248, .053), (.657, .525, .211, .153, .048), (.179, .614, .052, .155, .048),
      (.433, .595, .237, .171, .055), (.799, .617, -.088, .180, .051), (.516, .651, -.261, .190, .055),
      (.182, .734, -.091, .195, .055), (.447, .757, .204, .125, .044), (.777, .715, .088, .194, .057)),
 14: ((.350, .082, -.046, .208, .086), (.519, .144, .104, .199, .072), (.850, .188, -.106, .143, .053),
      (.189, .236, .108, .162, .066), (.629, .313, .183, .166, .060), (.849, .300, -.025, .142, .052),
      (.811, .426, -.123, .156, .063), (.287, .595, .106, .193, .071), (.587, .574, .013, .076, .035)),
 16: ((.285, .194, -.063, .202, .092), (.485, .139, -.210, .155, .076), (.643, .114, .058, .216, .102),
      (.795, .190, -.091, .158, .079), (.143, .280, .014, .136, .075), (.328, .273, .205, .133, .082),
      (.495, .299, .316, .126, .069), (.677, .255, -.231, .145, .072), (.823, .334, .159, .136, .079),
      (.104, .396, -.067, .112, .066), (.286, .424, .094, .174, .081), (.507, .387, -.267, .152, .078),
      (.656, .401, .245, .101, .057), (.801, .426, -.066, .166, .069)),
 17: ((.350, .067, -.071, .142, .064), (.593, .078, .030, .170, .074), (.269, .134, .091, .145, .062),
      (.812, .177, -.072, .114, .064), (.621, .166, -.224, .105, .053), (.209, .223, -.136, .134, .068),
      (.503, .203, .220, .187, .076), (.703, .282, .168, .109, .059), (.838, .287, -.115, .146, .068),
      (.134, .320, .042, .131, .058), (.374, .327, -.254, .145, .063), (.551, .333, .311, .098, .073),
      (.225, .435, -.128, .166, .079), (.376, .426, .165, .140, .071), (.673, .444, -.209, .135, .067),
      (.833, .439, .103, .146, .067), (.862, .515, -.041, .101, .052)),
 18: ((.459, .101, -.049, .226, .084), (.694, .109, .071, .125, .057), (.285, .175, .128, .118, .064),
      (.137, .235, -.022, .130, .053), (.466, .238, -.203, .107, .069), (.740, .231, .133, .151, .065),
      (.279, .246, -.177, .118, .052), (.348, .316, .212, .116, .065), (.718, .379, -.203, .149, .068),
      (.123, .383, .062, .134, .063), (.508, .392, .294, .111, .061), (.896, .465, .070, .127, .064),
      (.231, .468, -.162, .136, .064), (.147, .588, .043, .157, .070), (.417, .593, .216, .128, .075),
      (.636, .572, -.228, .111, .065), (.790, .585, .117, .137, .064), (.850, .690, -.086, .111, .065),
      (.519, .740, .126, .149, .063), (.260, .718, -.061, .136, .064)),
}


COVER = {10:.54, 11:.49, 12:.47, 13:.45, 14:.52, 16:.55, 17:.34, 18:.41}


def snow_mask(pixels):
    rgb = pixels[..., :3].astype(float)
    return (pixels[..., 3] > 128) & (rgb.min(2) > 128) & (np.ptp(rgb, axis=2) < 65)


def snow_texture(tree):
    rgb = tree.pixels[snow_mask(tree.pixels), :3].astype(float)
    ramp = np.array(colour_ramp(rgb, [.04, .20, .43, .66, .87, .99]))
    y, x = np.indices((56, 64))
    az = x*math.tau/64
    relief = .58+.16*np.cos(az*3+np.sin(y/17))+.14*np.cos(az*5-y/9)+.11*np.sin(y/6+az*2)
    band = np.clip(np.rint(5*relief+tree.rng.uniform(-.27, .27, x.shape)).astype(int), 0, 5)
    return Image.fromarray(ramp[band].astype(np.uint8))


def close_leaf_ring(tree, ring, direction, slot, v):
    start = len(tree.leaves["uvs"])
    tree.cap(tree.leaves, ring, np.array(direction))
    count = (len(tree.leaves["uvs"])-start)//2
    tree.leaves["uvs"][start:] = [(slot%8+.45)/8, slot//8+v]*count
    tree.leaves["colors"][-4*count+3::4] = [1]*count


class WinterBoughs:
    def __init__(self, tree, record):
        self.tree = tree
        self.powder = snow_texture(tree)
        snow = snow_mask(tree.pixels)
        rgb = tree.pixels[..., :3].astype(float)
        solid = tree.pixels[..., 3] > 128
        if record == 17:
            leaf = solid & ~snow
        else:
            green = (rgb[..., 1] >= rgb[..., 0]*.97) & (rgb[..., 1] >= rgb[..., 2]*.96) & solid & ~snow
            leaf = solid & ~snow & (distance_transform_edt(~green) < 2.1)
        limit = .84 if record in (11, 13, 25) else .80 if record == 18 else .66
        leaf[round(len(leaf)*limit):] = False
        # Beyond the leaf edge, continue real brushstrokes instead of stretching
        # one nearest pixel into a streak across an entire card.
        interior = np.array(filled(tree.pixels, leaf))
        h, w = leaf.shape
        extent = max(5, min(w, h)//12)
        density = uniform_filter(leaf.astype(float), size=extent)
        sy, sx = np.unravel_index(density.argmax(), density.shape)
        sy, sx = np.clip(sy, extent, h-extent-1), np.clip(sx, extent, w-extent-1)
        yy, xx = np.indices(leaf.shape)
        far = distance_transform_edt(~leaf) > 2.1
        tx = sx-extent//2+abs(xx%(2*extent)-extent)
        ty = sy-extent//2+abs(yy%(2*extent)-extent)
        interior[far] = interior[ty[far], tx[far]]
        self.leaf_paint = Image.fromarray(interior)
        self.snow = snow
        self.record = record
        painted = tree.pixels.copy()
        painted[..., 3] = 255*(leaf | snow)
        self.painted = Image.fromarray(painted)

    def patch(self, x, y, rx, ry):
        w, h = self.leaf_paint.size
        box = (round((x-rx)*w), round((y-.3*ry)*h), round((x+rx)*w)+1, round((y+1.85*ry)*h)+1)
        return self.leaf_paint.crop(box).resize((64, 56), Image.Resampling.NEAREST)

    def grow(self, x, down, z, radius, height, cover):
        tree = self.tree
        centre = tree.point(x, down, z)
        az = tree.rng.uniform(0, math.tau)
        broad = self.record in (12, 16, 17, 18)
        rx = tree.aspect*radius*(1.26 if broad else 1.08)
        rz = rx*tree.rng.uniform(.70, .95)
        height *= 1.27 if broad else 1.00
        woodside = np.array(self.patch(x, down, radius*.84, height))
        # Read the lip and ice colours from this particular painted snow patch.
        # Repeat the painted front around the back, leaving breaks in the rim
        # where green leaves show through and a shaded skirt below the drift.
        v, u = np.indices((56, 64))
        angle = u*math.tau/64
        h, w = self.snow.shape
        across = .91*np.cos(angle)+.07*np.sin(3*angle+az)
        sx = np.rint((x+across*radius)*(w-1)).astype(int).clip(0, w-1)
        sy = np.rint((down+height*(v/55*2.60-.97))*(h-1)).astype(int).clip(0, h-1)
        actual = self.snow[sy, sx]
        edge = cover-.15+.095*np.sin(3*angle+az)+.075*np.cos(5*angle-az)+.056*np.sin(9*angle+az)
        white = (actual & (v/55 < cover+.19)) | (v/55 < edge)
        snow = np.roll(np.array(self.powder), round(az*10), axis=1)
        snow[actual] = tree.pixels[sy[actual], sx[actual]]
        tile = np.where(white[..., None], snow, woodside)
        slot = len(tree.patches)
        tree.patches.append(Image.fromarray(tile.astype(np.uint8)))
        a = np.linspace(0, math.tau, 9)
        outline = 1+.113*np.cos(3*a+az)+.084*np.sin(5*a-az)
        rings, uv = [], []
        for y, r, t in ((-1.31, .06, .985), (-.90, .72, .84), (-.41, 1.0, .63), (.10, .91, .40),
                      (.57, .64, .15), (.83, .04, .01)):
            rings.append(centre+np.stack([rx*r*.74*outline*np.cos(a),
                height*(y+r*(.28*np.cos(3*a+az)+.15*np.sin(5*a))), rz*r*.74*outline*np.sin(a)], axis=1))
            uv.append(np.stack([(slot%8+.01+.98*a/math.tau)/8, np.full_like(a, slot//8+t)], axis=1))
        add_grid(tree.leaves, np.array(rings), uv, seam=True)
        for ring, normal, t in ((rings[-1], [0, 1, 0], .012), (rings[0][::-1], [0, -1, 0], .983)):
            close_leaf_ring(tree, ring, normal, slot, t)

        # The outside is the actual snowy edge of this branch, including holes
        # and dangling leaves. Its folds turn the painting up over the crown and
        # back under the lip. The smaller shaded core fills the spaces beneath.
        box = (round((x-radius*1.12)*w), round((down-height*1.03)*h),
               round((x+radius*1.12)*w)+1, round((down+height*1.91)*h)+1)
        raw = np.array(self.painted.crop(box).resize((64, 56), Image.Resampling.NEAREST))
        yy, xx = (v-27.5)/28, (u-31.5)/32
        ray_angle = np.arctan2(yy, xx)
        edge = .92+.049*np.sin(ray_angle*9+az)+.056*np.cos(ray_angle*6-az)
        raw[..., 3] *= xx*xx+yy*yy < edge*edge
        slot = len(tree.patches)
        tree.patches.append(Image.fromarray(raw))
        for i in range(4):
            bearing = az+(i+tree.rng.uniform(-.10, .10))*math.tau/4
            ray = np.array([rx*np.cos(bearing), 0, rz*np.sin(bearing)])
            side = np.array([-rx*np.sin(bearing), 0, rz*np.cos(bearing)])
            rows, uv = [], []
            for t, reach, lift, span in ((0, -.13, .96, .65), (.46, .72, .23, .89), (1, 1.10, -1.33, .74)):
                rows.append([centre+ray*(reach-.23*k*k)+side*k*span+[0, height*(lift-.26*k*k), 0]
                             for k in (1, 0, -1)])
                uv.append([[(slot%8+u)/8, slot//8+.01+.98*t] for u in (.007, .50, .993)])
            add_grid(tree.leaves, np.array(rows), uv, double=True)


    def undergrowth(self, x, down, z, width, height):
        tree = self.tree
        node = tree.point(x, down, z)
        patch = self.patch(x, down, width*.74, height*.74)
        slot = len(tree.patches)
        tree.patches.append(patch)
        a = np.linspace(0, math.tau, 9)
        phase = tree.rng.uniform(0, math.tau)
        outline = 1+.087*np.sin(3*a+phase)+.059*np.cos(5*a)
        rows, uv = [], []
        for r, y, v in ((.075, -.72, .97), (.74, -.49, .83), (1, .07, .54), (.84, .49, .30), (.32, .81, .07)):
            rows.append(node+np.stack([width*tree.aspect*r*outline*np.cos(a),
                height*(y+.13*r*np.sin(3*a+phase)), width*.85*tree.aspect*r*outline*np.sin(a)], axis=1))
            uv.append(np.stack([(slot%8+.01+.98*a/math.tau)/8, np.full_like(a, slot//8+v)], axis=1))
        add_grid(tree.leaves, np.array(rows), uv, seam=True)
        close_leaf_ring(tree, rows[-1], [0, 1, 0], slot, .07)
        close_leaf_ring(tree, rows[0][::-1], [0, -1, 0], slot, .97)


# Darker inner and lower branches carry the weight of the overlapping snowy
# boughs. Their silhouettes and the length of exposed trunk differ by painting.
BACKING = {
    12: ((.33, .24, -.06, .245, .116), (.58, .23, .01, .276, .130), (.22, .385, -.010, .181, .145),
         (.47, .405, .063, .298, .127), (.725, .335, -.015, .228, .139), (.620, .485, -.038, .155, .093)),
    13: ((.50, .736, 0, .34, .062), (.52, .595, -.025, .29, .055), (.49, .469, .015, .245, .061),
         (.49, .338, -.022, .194, .050), (.493, .220, .034, .132, .046)),
    16: ((.418, .164, -.021, .266, .139), (.64, .172, .024, .228, .146),
         (.252, .319, -.011, .228, .163), (.499, .331, .045, .303, .160), (.748, .326, -.055, .217, .151),
         (.351, .441, .025, .207, .091), (.693, .430, .024, .209, .094)),
    17: ((.437, .137, -.032, .282, .140), (.630, .194, .025, .203, .159), (.292, .308, .044, .245, .181),
         (.55, .331, -.068, .300, .185), (.735, .342, .060, .199, .149), (.250, .480, -.010, .20, .112),
         (.48, .471, .037, .272, .128), (.690, .475, -.054, .220, .139)),
    18: ((.496, .147, .012, .240, .114), (.350, .286, -.030, .24, .131), (.608, .280, .031, .259, .129),
         (.254, .451, .063, .228, .141), (.511, .432, -.055, .304, .174), (.717, .462, .023, .23, .139),
         (.327, .646, -.014, .238, .126), (.560, .633, .051, .252, .155), (.735, .618, -.067, .18, .124)),
}


def laden_spruce(tree, paint):
    for tier, (attach, tip, width, count) in enumerate(summer.SPRUCE_WHORLS):
        # Trace a stripe along a painted bough. Its upper, snowy edge and
        # its underside stay separate as the folded wing turns away from light.
        for variant in range(2):
            yy, xx = np.indices((56, 64))
            t = yy/55
            k = (xx-31.5)/32
            wing = np.sin(math.pi*(t*.87+.04))**.64
            x = .50+(1 if variant else -1)*width*(.17+.88*t)
            y = attach+(tip-attach)*(.08+.91*t**1.15)+k*max(.017, (tip-attach)*.29)*wing
            sx = np.rint(x*(tree.pixels.shape[1]-1)).astype(int).clip(0, tree.pixels.shape[1]-1)
            sy = np.rint(y*(tree.pixels.shape[0]-1)).astype(int).clip(0, tree.pixels.shape[0]-1)
            patch = np.array(paint.patch(.50+(-1 if variant else 1)*width*.56, tip-.065, width*.31, .058))
            source = tree.pixels[sy, sx]
            ink = (source[..., 3] > 128)&~paint.snow[sy, sx]
            patch[ink] = source[ink]
            light = paint.snow[sy, sx] & (abs(k+.17*np.sin(7*t+tier)) < (.43 if variant else .32)*wing+.08)
            patch[light] = source[light]
            edge = .22+.73*wing+.10*np.sin(31*t+tier)+.055*np.cos(55*t)
            patch[..., 3] = 255*((abs(k) < edge)&(t < .97-.20*abs(k)+.08*np.cos(12*k+tier)))
            tree.patches.append(Image.fromarray(patch))
        hub = tree.stem_at(attach)
        for i in range(count):
            az = (i+tier*.42+tree.rng.uniform(-.17, .17))*math.tau/count
            ray = np.array([math.cos(az), 0, math.sin(az)])
            side = np.cross(ray, [0, 1, 0])
            span = width*tree.aspect*tree.rng.uniform(.88, 1.11)
            drop = (tip-attach)*tree.rng.uniform(.85, 1.14)
            rows, uv = [], []
            slot = 2*tier+i%2
            for t, extent, wing in ((0, -.02, .08), (.38, .41, .38), (.74, .82, .50), (1, 1.06, .31)):
                mid = hub+ray*extent*span-[0, drop*t**1.25, 0]
                rows.append([mid+side*k*wing*span-ray*(k*k*.13)*span-
                             [0, k*k*(.022+drop*.25)*wing*2, 0] for k in (1, 0, -1)])
                uv.append([[(slot%8+u)/8, slot//8+.01+.98*t] for u in (.006, .5, .994)])
            add_grid(tree.leaves, np.array(rows), uv, double=True)
            if tier == 0:
                summer.limb(tree, [hub, hub+ray*span*.43-[0, drop*.36, 0], hub+ray*span*.85-[0, drop*.68, 0]],
                            [.018, .010, .003], 4)


def snow_strip(tree, centres, radii, strips):
    """A thin ridge on a bent limb's upper surface; taper its two ends into the bark."""
    for p, q, r0, r1 in zip(centres, centres[1:], radii, radii[1:]):
        tangent = q-p
        tangent = tangent/np.linalg.norm(tangent)
        up = np.array([0., 1., 0.])-tangent*tangent[1]
        length = np.linalg.norm(up)
        if length < .50:
            continue
        up /= length
        side = np.cross(tangent, up)
        rows, uv = [], []
        for t, spread in ((.03, .41), (.46, .94), (.98, .53)):
            centre = p+(q-p)*t
            radius = tree.aspect*(r0+(r1-r0)*t)
            rows.append([centre+radius*((1.12-.45*k*k*spread)*up+k*spread*side) for k in (1, 0, -1)])
            uv.append([[u, .10+.77*t] for u in (.13, .41, .78)])
        add_grid(strips, np.array(rows), uv, double=True)


def add_bark_snow(tree, bark, strips):
    if not strips["indices"]:
        return bark
    tile = snow_texture(tree)
    atlas = Image.new("RGBA", (bark.width+tile.width, max(bark.height, tile.height)))
    atlas.paste(bark)
    atlas.paste(tile, (bark.width, 0))
    uvs = tree.bark["uvs"]
    uvs[::2] = [u*bark.width/atlas.width for u in uvs[::2]]
    uvs = strips["uvs"]
    uvs[::2] = [(bark.width+u*tile.width)/atlas.width for u in uvs[::2]]
    uvs[1::2] = [v*tile.height/atlas.height for v in uvs[1::2]]
    start = len(tree.bark["positions"])//3
    tree.bark["indices"].extend(start+i for i in strips["indices"])
    for field in ("positions", "uvs", "normals", "colors"):
        tree.bark[field].extend(strips[field])
    return atlas


def snowy_grove(tree):
    pixels = tree.pixels
    frozen = snow_mask(pixels)
    # Keep white out of the stone and moss ramps; it belongs on the
    # upper shoulders of the boulders, not inside their darkest cracks.
    tree.pixels = np.array(filled(pixels, (pixels[..., 3] > 128)&~frozen))
    tree.pixels[..., 3] = pixels[..., 3]
    stone, stone_tile = rock_grove(tree, moss_line=.22)
    tree.pixels = pixels
    # The summer recipe supplies the clinging roots and their attachment
    # points. The winter painting supplies a different set of weighted pads.
    tree.leaves = part("leaves")
    tree.patches.clear()
    rgb = tree.pixels[..., :3].astype(float)
    wood = (tree.pixels[..., 3] > 128)&(rgb[..., 0] > rgb[..., 1]*1.01)&(rgb[..., 0] > rgb[..., 2]*1.06)
    wood |= (tree.pixels[..., 3] > 128)&(distance_transform_edt(~wood) < 2.3)
    bark = filled(tree.pixels, wood)
    rock = np.array(stone_tile)
    y, x = np.indices((48, 64))
    angle = x*math.tau/64
    relief = np.array(snow_texture(tree).resize((64, 48), Image.Resampling.NEAREST))
    # Sample the four separate painted stone shoulders. The gray snow edge
    # crosses the lichen at a different height on each one.
    for i, (cx, width, top, foot) in enumerate(((.354, .148, .608, .838), (.152, .146, .815, 1.0),
            (.464, .132, .842, .998), (.613, .114, .783, .974))):
        h, w = tree.pixels.shape[:2]
        sx = np.rint((cx+width*.88*np.cos(angle))*(w-1)).astype(int)
        sy = np.rint((top+(foot-top)*y/47)*(h-1)).astype(int)
        source = tree.pixels[sy, sx]
        painted = frozen[sy, sx]
        lip = .19+.07*np.cos(3*angle+i)+.045*np.sin(5*angle)
        tile = rock[:, i*64:(i+1)*64]
        tile[y/48 < lip] = relief[y/48 < lip]
        high = painted&(y/48 < .55)
        tile[high] = source[high]
    return stone, bark, Image.fromarray(rock)


def build(record, rgba):
    if record == 15:
        return winter_oak.build(rgba)
    tree = Tree(record, rgba, STEMS[record])
    parts = [tree.bark, tree.leaves]
    strips = part("bark")
    images = {}
    if record == 10:
        tree.tube([tree.point(x, y, z) for x, y, z, _ in tree.stem], tree.stem[:, 3], 10, cap=True)
        stone, bark, images["stone"] = snowy_grove(tree)
        parts = [tree.bark, tree.leaves, stone]
    else:
        arms = ARMS.get(record, summer.ARMS.get(record, ()))
        anchors, projected = summer.branchwork(tree, record, arms=arms)
        # Pack the bark chart after the crown has added all its supporting twigs.
        for attach, trace in arms:
            pts = [tree.stem_at(attach)]+[tree.point(x, y, z) for x, y, z, _ in trace]
            radii = [trace[0][3]*1.17]+[r for _, _, _, r in trace]
            snow_strip(tree, pts, radii, strips)
    paint = WinterBoughs(tree, record)
    if record == 25:
        laden_spruce(tree, paint)
    else:
        for clump in BACKING.get(record, ()):
            paint.undergrowth(*clump)
        for x, y, z, r, h in PADS[record]:
            node = tree.point(x, y+.55*h, z)
            if record != 10:
                summer.connect(tree, anchors, node, min(.022, .11*r))
            paint.grow(x, y, z, r, h, COVER[record])
    if record == 13:
        tip = tree.point(.486, .012, -.012)
        summer.limb(tree, [tree.stem_at(.110), tree.stem_at(.062), tip], [.012, .009, .003], 5)
        paint.grow(.489, .049, -.012, .082, .051, .33)
    if record != 10:
        pixels = tree.pixels
        # Snow on the shaft keeps its painted placement. Snow on a horizontal
        # twig has its own upper ridge; strip it from the repeating grain there.
        band = summer.BARK_BANDS[record]
        bark = summer.bark_texture(tree, band, projected)
        rgb = pixels[..., :3].astype(float)
        wood = (pixels[..., 3] > 128)&~snow_mask(pixels)&(rgb[..., 0] >= rgb[..., 1]*.98)
        dry = np.array(filled(pixels, wood))
        dry[..., 3] = pixels[..., 3]
        tree.pixels = dry
        wrapped = trunk_paint(tree, (96, 160), band).astype(np.uint8)
        wrapped[..., 3] = 255
        bark.paste(Image.fromarray(wrapped), (0, 0))
        tree.pixels = pixels
    images["bark"] = add_bark_snow(tree, bark, strips)
    images["leaves"] = tree.cluster_atlas()
    return {"height":max(max(p["positions"][1::3]) for p in parts), "parts":parts}, images
