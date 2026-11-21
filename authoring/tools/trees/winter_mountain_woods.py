"""The snow-loaded snag and forked evergreen of the winter mountain woods."""
import math

import numpy as np
from PIL import Image

from mountain_woods import CarvedBark
from rainforest_palms import add_grid, part
from rainforest_trees import Tree
from winter_woodland import WinterBoughs, add_bark_snow, bark_chart, snow_mask, snow_strip
import woodland_trees as woodland


FORMS = {16: "gnarled", 21: "snow-forked-spire-tree"}

# x, down, depth and radius in the cropped paintings.
SNAG = ((.440, 1.002, 0, .074), (.441, .939, .012, .090), (.426, .861, .043, .117),
        (.370, .784, .072, .137), (.315, .728, .083, .155), (.319, .674, .056, .151),
        (.377, .615, .024, .156), (.471, .553, -.012, .140), (.491, .492, -.032, .125),
        (.470, .439, -.041, .116), (.444, .389, -.017, .109), (.478, .331, .009, .091),
        (.461, .279, .045, .078), (.451, .219, .059, .070), (.445, .164, .061, .055))
LIMBS = (
    ((.460, .342, 0, .099), (.541, .281, -.048, .087), (.624, .217, -.086, .061),
     (.670, .133, -.100, .063), (.715, .100, -.092, .056), (.787, .094, -.069, .046),
     (.878, .090, -.044, .035), (.949, .053, -.039, .026), (.993, .005, -.021, .006)),
    ((.456, .322, .030, .086), (.352, .318, .076, .068), (.284, .273, .103, .056),
     (.246, .211, .119, .050), (.205, .168, .125, .041), (.150, .143, .116, .028),
     (.114, .146, .109, .018), (.094, .221, .094, .005)),
    ((.469, .340, -.035, .060), (.441, .271, -.136, .048), (.412, .207, -.186, .032)),
    ((.870, .092, -.044, .019), (.855, .118, -.031, .013), (.860, .172, -.022, .006)),
    ((.542, .282, -.034, .046), (.562, .226, .037, .033), (.569, .199, .079, .012)),
)
ROOTS = (
    ((.400,.853,.024,.084), (.356,.918,.106,.062), (.290,.969,.144,.050),
     (.213,.938,.151,.062), (.177,.904,.142,.043), (.148,.947,.136,.036), (.045,.985,.125,.016)),
    ((.431,.818,.055,.080), (.503,.853,.118,.073), (.563,.898,.152,.055),
     (.622,.902,.168,.053), (.673,.966,.156,.040), (.776,.992,.140,.013)),
    ((.424,.861,-.009,.089), (.491,.924,-.117,.074), (.528,.932,-.201,.050), (.600,.991,-.292,.012)),
    ((.383,.849,-.023,.067), (.302,.909,-.103,.058), (.242,.960,-.179,.041), (.168,.994,-.221,.009)),
    ((.438,.858,.068,.063), (.468,.912,.183,.046), (.452,.973,.240,.035), (.489,1,.290,.008)),
)
# A drift sits on a shelf: centre, width, depth, thickness and rise to the right.
DRIFTS = (
    (.182,.157,.115,.088,.055,.040,-.031), (.445,.170,.063,.071,.076,.035,.002),
    (.569,.220,.058,.042,.046,.029,.023), (.695,.104,-.097,.067,.060,.040,.056),
    (.790,.062,-.078,.113,.046,.023,.005), (.921,.048,-.045,.078,.036,.024,.043),
    (.406,.386,.083,.065,.061,.032,.034), (.253,.641,.146,.107,.098,.056,.066),
    (.394,.697,.196,.112,.068,.038,.013), (.555,.846,.138,.051,.044,.026,-.025),
    (.188,.909,.144,.062,.065,.027,.024), (.082,.943,.133,.057,.050,.023,.016),
    (.637,.886,.160,.059,.058,.027,-.009), (.707,.955,.144,.041,.040,.019,-.014),
    (.458,.960,.237,.031,.038,.018,.012), (.276,.941,-.145,.058,.055,.029,.027),
    (.519,.915,-.187,.062,.058,.031,-.012), (.431,.220,-.168,.052,.048,.032,-.013),
    (.346,.668,-.065,.117,.068,.037,.041),
)


def drift(tree, snow, x, y, z, width, depth, height, rise):
    # Roll the lower lip into the supporting wood. Sloped caps follow the
    # broken tips; the same rings make broader shelves on the belly and roots.
    angle = np.linspace(0, math.tau, 11)
    phase = tree.rng.uniform(0, math.tau)
    edge = 1+.09*np.sin(3*angle+phase)+.063*np.cos(5*angle-phase)
    centre = tree.point(x, y, z)
    rings, uv = [], []
    for radius, lift in ((.64, -.64), (1, -.29), (.91, .29), (.59, .76), (.06, 1)):
        cx, cz = radius*edge*np.cos(angle), radius*edge*np.sin(angle)
        rings.append(centre+np.stack([tree.aspect*width*cx,
            rise*cx+height*(lift+.34*radius*np.sin(4*angle+phase)), tree.aspect*depth*cz], axis=1))
        uv.append(np.stack([.50+.40*cx, .50+.39*cz], axis=1))
    add_grid(snow, np.array(rings), uv, seam=True)
    tree.cap(snow, rings[-1], np.array([0., 1., 0.]))
    snow["uvs"][-2*len(angle):] = [.5, .5]*len(angle)


def snowy_snag(rgba):
    tree = Tree(16, rgba, SNAG)
    wood = CarvedBark(tree, (tree.pixels[..., 3] > 128) & ~snow_mask(tree.pixels))
    angle = np.linspace(0, math.tau, 15)
    centres, rings = [], []
    for x, y, z, r in SNAG:
        centre = tree.point(x, y, z)
        twist = 4.4*(1-y)
        edge = r*(1+.14*np.cos(4*(angle+twist))+.07*np.sin(7*(angle+twist)))
        rings.append(centre+np.stack([tree.aspect*edge*np.cos(angle), .012*np.sin(3*angle+twist),
                                      tree.aspect*.85*edge*np.sin(angle)], axis=1))
        centres.append(centre)
    rings = np.array(rings)
    add_grid(tree.bark, rings, np.zeros((*rings.shape[:2], 2)), seam=True)
    wood.map_rings(rings, np.array(centres), 0)
    tree.cap(tree.bark, rings[-1], np.array([0., 1., 0.]))
    tree.bark["uvs"][-2*len(angle):] = [.42, .96]*len(angle)
    for trace in (*LIMBS, *ROOTS):
        wood.tube([tree.point(x, y, z) for x, y, z, _ in trace], [r for _, _, _, r in trace], 7)

    # Ridges wrap around the swollen S-bend; the side view exposes the same
    # wound grain and gives the central pocket of snow a physical shelf.
    for i in range(4):
        path, radii = [], []
        for t in np.linspace(0, 1, 13):
            y = .91-.55*t
            a = i*math.tau/4+.8-4.6*t
            r = np.interp(y, tree.stem[::-1, 1], tree.stem[::-1, 3])
            edge = r*(1+.14*math.cos(4*(a+4.4*(1-y)))+.07*math.sin(7*(a+4.4*(1-y))))
            path.append(tree.stem_at(y)+.95*tree.aspect*edge*np.array([math.cos(a), 0, .85*math.sin(a)]))
            radii.append(.005+.028*math.sin(math.pi*t)**.8)
        wood.tube(path, radii, 5)
    snow = part("bark")
    for cap in DRIFTS:
        drift(tree, snow, *cap)
    atlas = add_bark_snow(tree, wood.atlas(), snow)
    return {"height": max(tree.bark["positions"][1::3]), "parts": [tree.bark]}, {"bark": atlas}


SPIRE = ((.459, 1.000, 0, .086), (.470,.940,.015,.101), (.474,.871,.034,.078),
         (.460,.810,.040,.074), (.431,.765,.029,.082), (.467,.714,.012,.076),
         (.493,.655,-.016,.055), (.492,.552,-.027,.045), (.475,.461,-.013,.035),
         (.482,.357,.006,.028), (.455,.265,.021,.021), (.444,.161,.011,.013), (.450,.040,0,.004))
FORKS = (
    (.746, ((.354,.707,.052,.041), (.284,.684,.092,.032), (.213,.664,.117,.026),
            (.184,.606,.130,.021), (.202,.509,.122,.009))),
    (.780, ((.540,.762,.043,.045), (.602,.717,.092,.034), (.673,.699,.117,.024),
            (.745,.665,.135,.018), (.765,.594,.141,.009))),
    (.712, ((.391,.655,-.062,.033), (.353,.596,-.125,.024), (.359,.484,-.160,.014))),
    (.695, ((.576,.633,-.070,.035), (.624,.571,-.119,.022), (.648,.421,-.161,.009))),
)
# The lower tips hang well below the snow line, leaving the two forks
# visible. Vary both the height and the depth of each bough within a tier.
BOUGHS = (
    (.441,.078, .001,.094,.075,.69), (.462,.146,-.039,.146,.084,.64),
    (.550,.160, .065,.157,.075,.56), (.390,.203, .039,.123,.079,.43),
    (.300,.292, .065,.180,.085,.63), (.453,.280,-.173,.180,.091,.62),
    (.513,.293, .177,.175,.100,.64), (.668,.308,-.081,.178,.083,.63),
    (.730,.343, .055,.120,.077,.64), (.292,.358,-.088,.157,.101,.30),
    (.496,.452,-.070,.203,.086,.74), (.428,.458, .177,.157,.085,.70),
    (.284,.483,-.171,.180,.082,.54), (.174,.520, .087,.152,.092,.61),
    (.138,.569,-.041,.123,.083,.48), (.637,.478, .189,.189,.093,.62),
    (.758,.475,-.131,.150,.090,.68), (.828,.544, .071,.158,.093,.63),
)


def forked_spire(rgba):
    tree = Tree(21, rgba, SPIRE)
    anchors = [tree.point(x, y, z) for x, y, z, _ in SPIRE]
    tree.tube(anchors, tree.stem[:, 3], 8, cap=True)
    snow = part("bark")
    for down, trace in FORKS:
        path = [tree.stem_at(down)]+[tree.point(x, y, z) for x, y, z, _ in trace]
        radii = [trace[0][3]*1.18]+[r for _, _, _, r in trace]
        tree.tube(path, radii, 6, cap=True)
        snow_strip(tree, path[:4], radii[:4], snow)
        anchors.extend(path[1:])
    projected = len(tree.bark["uvs"])
    for i in range(5):
        a = i*math.tau/5+.41
        ray = np.array([math.cos(a), 0, .88*math.sin(a)])*tree.aspect
        length = tree.rng.uniform(.185, .297)
        path = [tree.stem_at(.903), tree.stem_at(.953)+ray*.28*length,
                tree.stem_at(.981)+ray*.66*length, tree.stem_at(1.001)+ray*length]
        radii = [.055, .048, .028, .005]
        woodland.limb(tree, path, radii, 5)
        if i in (1, 2, 4):
            snow_strip(tree, path, radii, snow)
    paint = WinterBoughs(tree, skirt=.735)
    # Stems visible between the painted boughs have their own wood geometry.
    # Exclude that russet from the cutout fringe around each snowy mass.
    fringe = np.array(paint.painted)
    rgb = fringe[..., :3].astype(float)
    fringe[..., 3] *= ~((rgb[..., 0] > 1.12*rgb[..., 1]) & ~paint.snow)
    paint.painted = Image.fromarray(fringe)
    anchors = np.array(anchors)
    for x, y, z, r, h, cover in BOUGHS:
        if y > .23:
            woodland.connect(tree, anchors, tree.point(x, y+.30*h, z), min(.017, r*.091))
        paint.grow(x, y, z, r, h, cover)
    bark = add_bark_snow(tree, bark_chart(tree, (.783, .945), projected), snow)
    parts = [tree.bark, tree.leaves]
    return {"height": max(max(p["positions"][1::3]) for p in parts), "parts": parts}, {
        "bark": bark, "leaves": tree.cluster_atlas()}


def build(record, rgba):
    return snowy_snag(rgba) if record == 16 else forked_spire(rgba)
