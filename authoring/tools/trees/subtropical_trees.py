"""The distinct spiky palms and moss-covered forked snag of archive 501.

Stem and limb coordinates follow the painted image, x across, y down. Depth and
radius are fractions of its width, in the same frame as the rainforest recipes.
"""
import math

import numpy as np
from PIL import Image, ImageDraw

from rainforest_palms import add_grid, colour_ramp, frond, frond_atlas, part
from rainforest_trees import Tree, blade_atlas, filled, rosette
from rocks import stone_steps


FORMS = {11: "leaning-palm", 16: "spike-palm", 19: "mossy-snag", 20: "rock-grove",
         27: "forked-palm", 28: "scrub-palm", 30: "arched-palm"}
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
    20: ((.40, .956, 0, .057), (.45, .850, .043, .067), (.563, .775, .035, .051),
         (.602, .690, .017, .037), (.613, .531, -.004, .028), (.584, .445, -.02, .029),
         (.565, .356, -.013, .023), (.610, .270, .012, .020), (.643, .159, .016, .012),
         (.602, .105, .015, .009)),
    27: ((.601, 1, 0, .090), (.595, .935, .007, .066), (.605, .839, .025, .044),
         (.612, .740, .020, .041), (.584, .652, -.016, .048), (.537, .593, -.040, .050),
         (.507, .529, -.036, .051), (.485, .461, -.025, .042)),
    28: ((.622, 1, 0, .102), (.614, .936, .016, .066), (.622, .818, .034, .043),
         (.615, .728, .022, .038), (.595, .627, -.013, .041), (.541, .553, -.038, .055),
         (.522, .470, -.017, .048), (.510, .419, 0, .040)),
    30: ((.872, 1, 0, .089), (.850, .925, .008, .066), (.816, .785, .069, .045),
         (.765, .663, .096, .043), (.702, .551, .078, .046), (.625, .464, -.002, .044),
         (.541, .389, -.022, .055), (.493, .326, -.015, .045)),
}


def scale_bark(tree, band):
    """Wrap the bark scales sampled along the leaning stem. Outside the exposed band,
    reflect the grain under the crown and into the foot."""
    pixels = tree.pixels
    h, w = pixels.shape[:2]
    lo, hi = band
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
    30: (((.492,.338,.025), (.367,.346,.041), (.275,.389,.064), (.209,.465,.068),
          (.172,.552,.049), (.147,.620,.013)),
         ((.518,.327,.035), (.635,.211,.054), (.735,.145,.071), (.808,.131,.056),
          (.890,.167,.039), (.947,.229,.012))),
    27: (((.48,.47,.023), (.36,.486,.033), (.276,.553,.051), (.211,.646,.047),
          (.176,.731,.033), (.155,.833,.015), (.150,.905,.004)),
         ((.52,.447,.029), (.60,.35,.042), (.69,.245,.034), (.784,.183,.019),
          (.870,.142,.012), (.953,.127,.004))),
    28: (((.51,.451,.035), (.397,.487,.043), (.310,.539,.062), (.236,.622,.050),
          (.193,.702,.041), (.173,.790,.023), (.167,.835,.006)),
         ((.54,.412,.029), (.627,.289,.043), (.732,.212,.033), (.814,.163,.024),
          (.899,.135,.013), (.954,.126,.006))),
}


def palm_atlas(tree, traces, *, feathered=False):
    """Straighten painted fronds into atlas cells. The folded 3D blades put the arch back.
    Keep the deep shadows, torn outer leaflets and open spaces visible in the painting."""
    h, w = tree.pixels.shape[:2]
    if feathered:
        atlas = frond_atlas(tree.pixels.astype(float), tree.rng, (.342, .515)).resize((192, 96), Image.Resampling.NEAREST)
    else:
        atlas = blade_atlas(tree, feathered=False)
    for cell, trace in enumerate(traces):
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
        paint_share = .30 if feathered and cell == 1 else .58
        tile[..., :3] = np.rint(paint_share*colour + (1-paint_share)*tile[..., :3]).astype(np.uint8)
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
    texture = palm_atlas(tree, FRONDS[record])
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


def rock_grove(tree, *, moss_line=.23):
    """Two stems grow between several moss-capped stones, with low, separate pads of leaves."""
    left = [(.46,.835,.036,.048), (.32,.832,.065,.078), (.22,.765,.080,.049),
            (.158,.703,.065,.034), (.150,.627,.042,.030), (.205,.540,.034,.017)]
    tree.tube([tree.point(x, y, z) for x, y, z, _ in left], [r for _, _, _, r in left], 6)
    # The second top of the left stem rises across clear sky.
    left_fork = [tree.point(.15,.63,.043), tree.point(.095,.59,.11), tree.point(.065,.49,.15)]
    tree.tube(left_fork, [.028, .023, .010], 5)
    roots = (
        ((.155,.70,.065,.039), (.152,.776,.093,.038), (.106,.802,.122,.027),
         (.064,.863,.153,.018), (.039,.957,.12,.008), (.015,.981,.10,.003)),
        ((.194,.755,.076,.045), (.235,.792,.134,.036), (.253,.84,.174,.027),
         (.305,.892,.193,.020), (.291,.968,.16,.005)),
        ((.452,.844,.042,.039), (.419,.841,.129,.033), (.389,.874,.226,.026),
         (.363,.918,.243,.016), (.358,.984,.205,.004)),
        ((.578,.750,.031,.047), (.625,.784,.064,.040), (.687,.828,.108,.028),
         (.722,.881,.107,.018), (.727,.974,.073,.006)),
        ((.540,.787,.020,.041), (.526,.791,-.138,.036), (.489,.824,-.198,.026),
         (.441,.908,-.204,.013), (.426,.976,-.157,.005)),
        ((.236,.776,.052,.038), (.280,.755,-.045,.031), (.282,.738,-.133,.022),
         (.267,.845,-.172,.015), (.260,.950,-.111,.004)),
    )
    for root in roots:
        tree.tube([tree.point(x,y,z) for x,y,z,_ in root], [r for _,_,_,r in root], 5)
    # Each pad follows one painted branch. Offsets in depth open a different set of gaps from the side.
    for crown in ((.26,.566,.104,.295,.082,-.026), (.38,.438,.234,.160,.055,-.085),
                  (.49,.824,.332,.177,.064,.070), (.732,.772,.635,.111,.034,-.040)):
        tree.crown(*crown, limb_scale=.57)
    tree.crown(.63,.185,.493,.184,.070,.04, origin=left_fork[0], limb_scale=.57)

    return mossy_stones(tree, moss_line)


def mossy_stones(tree, moss_line):
    stone = part("stone")
    # Wrap each boulder in its own cracked facets using the stone and moss ramps from the painting.
    pixels = tree.pixels
    rgb = pixels[..., :3].astype(float)
    base = pixels[..., 3] > 128
    base[:round(len(rgb)*.596)] = False
    grey = rgb[base & (rgb[..., 2] >= rgb[..., 0]*.96) & (rgb[..., 2] > 13)]
    green = rgb[base & (rgb[..., 1] > rgb[..., 0]*1.08) & (rgb[..., 1] > rgb[..., 2]*1.20)]
    stone_ramp = np.array(colour_ramp(grey, [.025, .14, .44, .72, .95]))
    moss_ramp = np.array(colour_ramp(green, [.10, .24, .49, .77, .94]))
    atlas = Image.new("RGBA", (256, 48))
    rocks = ((.410,-.06,.173,.145,.605,.942), (.175,.070,.165,.14,.792,1.01),
             (.481,.132,.148,.12,.860,1.007), (.611,.010,.141,.14,.774,1.002))
    for index, (x, z, rx, rz, top, foot) in enumerate(rocks):
        tones, lichen = stone_steps(50120 + index)
        v, u = np.indices(tones.shape) / len(tones)
        # The stone facets break through the moss in patches on the shoulder.
        edge = moss_line + .16*np.cos(math.tau*u + index*1.7) + .65*(lichen-.5)
        steps = np.clip(tones + (v < .19) - (v > .76), 0, 4)
        colours = np.where((v < edge)[...,None], moss_ramp[tones], stone_ramp[steps])
        tile = Image.fromarray(colours.astype(np.uint8)).resize((64,48), Image.Resampling.NEAREST)
        atlas.paste(tile, (index*64, 0))
        angles = np.linspace(0, math.tau, 11)
        phase = tree.rng.uniform(0, math.tau)
        outline = 1 + .085*np.sin(3*angles + phase) + .045*np.cos(5*angles)
        rings, uvs = [], []
        for t, radius in ((0,.73),(.16,1),(.62,.94),(.88,.69),(.975,.42),(1,.027)):
            centre = tree.point(x + .018*t, foot + (top-foot)*t, z)
            rings.append(centre + radius*tree.aspect*np.stack(
                [rx*outline*np.cos(angles), .013*np.sin(angles*3+phase), rz*outline*np.sin(angles)],axis=-1))
            across = .008 + .984 * angles / math.tau
            uvs.append(np.stack([(index+across)/4, np.full_like(angles, .985-.97*t)],axis=1))
        add_grid(stone, np.array(rings), uvs, seam=True)
        # Cap the very small top ring in its own atlas cell.
        start = len(stone["uvs"])
        tree.cap(stone, rings[-1], np.array([0.,1.,0.]))
        stone["uvs"][start:] = [(index + .50)/4, .015] * ((len(stone["uvs"])-start)//2)
    return stone, atlas


def spiky_fan(tree, hub, count, controls, width, cell):
    """Curve each rib from the hub through two control points to the pointed tip.
    Bend the two wings down from it so even a near edge-on blade has thickness."""
    for i in range(count):
        az = (i + cell*.43 + tree.rng.uniform(-.25, .25))*math.tau/count
        ray = np.array([math.cos(az), 0, math.sin(az)])
        side = np.cross(ray, [0,1,0])
        reach = tree.aspect * tree.rng.uniform(.76, 1.10)
        rise = tree.rng.uniform(.74, 1.14)
        skew = tree.rng.uniform(-.082, .082) * tree.aspect
        profile = np.array(controls)
        profile[1, 0] *= tree.rng.uniform(.87, 1.15)
        profile[-1, 1] += tree.rng.uniform(-.068, .062)
        rows, uvs = [], []
        for t in (0, .22, .44, .66, .84, 1):
            weights = np.array([3*(1-t)**2*t, 3*(1-t)*t*t, t**3])
            radial, y = weights @ profile
            centre = hub + ray*radial*reach + side*skew*t*t + [0, y*rise, 0]
            span = width*tree.aspect*(.28+.72*math.sin(math.pi*t)**.7)
            rows.append([centre + side*k*span - [0,abs(k)*span*.48,0] for k in (1,0,-1)])
            uvs.append([[(cell*64+32-k*31)/192, .99-.98*t] for k in (1,0,-1)])
        add_grid(tree.leaves, np.array(rows), uvs, double=True)


def small_palm(tree, record):
    """Short palms with knobbly leaf scars and fans of narrow upright and drooping fronds."""
    tall = record == 27
    hub = tree.stem_at(tree.stem[-1, 1])
    top = hub[1]
    tree.root_flare(.16 if tall else .21, 5, shoulder=.90, wrap_height=top)
    # A little broken fork grows beside the crown, visible in both silhouettes.
    twig = [tree.stem_at(.70), tree.point(.652,.604,.037), tree.point(.678,.526,.040)]
    tree.tube(twig, [.031,.023,.007], 5, cap=True, wrap_height=top)
    for i in range(5):
        az = .73 + i*math.tau/5
        ray = np.array([math.cos(az), 0, math.sin(az)])*tree.aspect
        branch = [hub, hub + ray*.074 - [0,.076,0], hub + ray*.106 - [0,.178 if i==2 else .115,0]]
        tree.tube(branch, [.029,.030,.004], 5, cap=True, wrap_height=top)
    spiky_fan(tree, hub, 9 if tall else 7, ((.22,.18),(.49,.075),(.45,-.36)), .084 if tall else .066, 0)
    spiky_fan(tree, hub, 8 if tall else 6, ((.14,.22),(.39,.34),(.54,.025)), .061 if tall else .054, 1)
    spiky_fan(tree, hub, 6 if tall else 5, ((.04,.18),(.10,.42),(.32,.44)), .033, 2)
    texture = palm_atlas(tree, FRONDS[record])
    spear = np.array(texture.crop((64,0,128,96)))
    if not tall:
        # Sun-dried spear tips on 28 carry the tawny paint at the top of its sprite.
        top_paint = tree.pixels[:round(len(tree.pixels)*.38)].astype(float)
        warm = top_paint[(top_paint[...,3]>128) & (top_paint[...,0] > top_paint[...,1]*1.10), :3]
        if len(warm):
            ramp = np.array(colour_ramp(warm, [.12,.38,.66,.91]))
            section = spear[:34]
            light = section[...,:3] @ [.2126,.7152,.0722]
            lit = section[...,3] > 128
            boundaries = np.quantile(light[lit], [.25,.60,.82])
            section[lit] = ramp[np.digitize(light[lit], boundaries)]
    texture.paste(Image.fromarray(spear), (128,0))
    return texture


def arched_palm(tree):
    hub = tree.stem_at(tree.stem[-1, 1])
    tree.root_flare(.16, 6, shoulder=.88, wrap_height=hub[1])
    # Wide, layered green fans arch from the low end of the leaning trunk.
    for count, reach, lift, drop, width, cell in ((10,.50,.14,.24,.118,0), (8,.446,.31,.07,.118,1)):
        for i in range(count):
            az = (i + .42*cell + tree.rng.uniform(-.19,.19))*math.tau/count
            frond(tree.leaves, hub, az, tree.aspect*reach*tree.rng.uniform(.82,1.09),
                  lift*tree.rng.uniform(.74,1.17), drop*tree.rng.uniform(.72,1.19),
                  width*tree.aspect, tree.rng.uniform(-.066,.066), cell)
    for i in range(7):
        az = i*math.tau/7
        frond(tree.leaves, hub, az, .070, .018, tree.rng.uniform(.10,.22), .050,
              tree.rng.uniform(-.02,.02), 2, hanging=True)
    return palm_atlas(tree, FRONDS[30], feathered=True)


def build(record, rgba):
    tree = Tree(record, rgba, STEMS[record])
    palm = record in (11, 16, 27, 28, 30)
    wrapped = 1 - tree.stem[-1, 1] if palm else None
    tree.tube([tree.point(x, y, z) for x, y, z, _ in tree.stem], tree.stem[:, 3], 10, cap=True, wrap_height=wrapped)
    parts = [tree.bark]
    if palm:
        bands = {11: (.665,.865), 16: (.595,.845), 27: (.695,.867), 28: (.695,.878), 30: (.555,.915)}
        images = {"bark": scale_bark(tree, bands[record])}
        if record == 30:
            images["leaves"] = arched_palm(tree)
        elif record in (27, 28):
            images["leaves"] = small_palm(tree, record)
        else:
            images["leaves"] = spiky_palm(tree, record)
        parts.append(tree.leaves)
    else:
        pixels = tree.pixels
        wood = pixels[..., 3] > 128
        if record == 20:
            rgb = pixels[..., :3].astype(float)
            wood &= (rgb[..., 0] > rgb[..., 1]*1.03) & (rgb[..., 0] > rgb[..., 2]*1.1)
        images = {"bark": filled(pixels, wood)}
        if record == 19:
            snag(tree)
        else:
            stone, images["stone"] = rock_grove(tree)
            parts.extend([tree.leaves, stone])
            images["leaves"] = tree.cluster_atlas()
    height = max(max(p["positions"][1::3]) for p in parts)
    return {"height": height, "parts": parts}, images
