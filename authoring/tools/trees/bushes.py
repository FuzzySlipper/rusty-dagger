"""Small nature shrubs. Cropped sprite coordinates are x, down and depth.
The wood carries individual painted sprays; the broad-leaved shrubs also
have folded blades where the sprite resolves individual leaves."""
import math

import numpy as np
from PIL import Image

from desert_trees import upright_tuft
from rainforest_palms import add_grid, colour_ramp
from rainforest_trees import Tree, filled
from winter_woodland import WinterBoughs, close_leaf_ring, snow_mask
from woodland_trees import Boughs, connect, limb


# Shared arrangements, with a separate skin wherever the painting differs.
FORMS = {
    "503-24": "tiered-desert-bush", "504-1": "flowering-cane-bush",
    "504-27": "broadleaf-bush", "504-28": "parasol-bush",
    "505-1": "snowy-cane-bush", "505-27": "snowy-broadleaf-bush", "505-28": "snowy-parasol-bush",
    "506-9": "autumn-bush", "506-27": "broadleaf-bush",
    "508-2": "ochre-bush", "508-28": "parasol-bush", "509-2": "snow-shelf-bush",
}
SHARED = {"507-27": "505-27", "508-27": "506-27", "509-27": "505-27",
          "509-28": "505-28", "510-9": "508-2"}
FAMILY = {24: "desert", 1: "canes", 27: "broadleaf", 28: "parasol", 9: "autumn", 2: "scrub"}

# A continuous stem from foot into crown, with radius as a share of sprite width.
STEMS = {
    "desert": ((.505,1,0,.046),(.490,.84,.010,.040),(.512,.66,.023,.032),
               (.506,.48,-.004,.028),(.501,.30,-.016,.020),(.540,.10,0,.009)),
    "canes": ((.50,1,0,.027),(.48,.80,.016,.025),(.49,.61,0,.018),
              (.45,.42,-.023,.014),(.40,.22,-.025,.009),(.42,.10,-.018,.005)),
    "broadleaf": ((.519,1,0,.056),(.505,.924,.009,.059),(.478,.860,.035,.056),
                  (.511,.797,.027,.043),(.581,.739,.036,.036),(.608,.637,.020,.026),(.590,.44,.006,.013)),
    "parasol": ((.532,1,0,.025),(.504,.930,.008,.029),(.468,.867,.017,.025),(.442,.802,.031,.022),
                (.427,.715,.044,.019),(.403,.630,.052,.014),(.370,.455,.063,.009)),
    "autumn": ((.564,1,0,.050),(.566,.936,.008,.046),(.562,.834,.019,.037),
               (.546,.750,.016,.037),(.511,.698,.004,.029),(.467,.620,-.022,.022),(.443,.471,-.031,.012)),
    "scrub": ((.536,1,0,.069),(.492,.905,.012,.059),(.528,.783,.029,.049),(.491,.635,.011,.044),
              (.507,.492,-.017,.033),(.476,.343,-.015,.024),(.498,.192,.008,.016),(.482,.045,.012,.007)),
}

# Lateral arms on the shrubs with an exposed fork. The other species branch
# directly from the leader into each visible tier or grow from basal canes.
ARMS = {
    "broadleaf": ((.879,((.414,.809,.048,.041),(.291,.787,.097,.029),(.203,.747,.128,.021),(.172,.614,.136,.012))),
                  (.812,((.618,.778,-.020,.029),(.743,.759,-.070,.025),(.823,.698,-.097,.018),(.858,.570,-.094,.008))),
                  (.823,((.491,.739,-.106,.034),(.403,.678,-.186,.022),(.357,.493,-.223,.013))),
                  (.764,((.613,.713,.119,.027),(.709,.643,.200,.021),(.738,.499,.233,.010)))),
    "parasol": ((.955,((.557,.893,-.016,.024),(.594,.798,-.021,.022),(.600,.696,-.026,.018),(.633,.576,-.041,.013))),
                (.783,((.379,.719,-.079,.017),(.309,.704,-.123,.015),(.235,.668,-.170,.010),(.210,.564,-.176,.006))),
                (.725,((.441,.634,.134,.018),(.499,.583,.188,.014),(.514,.452,.205,.009))),
                (.913,((.562,.822,-.138,.021),(.565,.698,-.209,.017),(.536,.531,-.240,.009)))),
    "autumn": ((.749,((.469,.701,.066,.027),(.347,.667,.108,.019),(.287,.536,.120,.010))),
               (.745,((.623,.707,-.012,.027),(.719,.644,-.067,.020),(.770,.518,-.082,.012))),
               (.713,((.532,.641,-.120,.024),(.487,.534,-.212,.017),(.479,.412,-.222,.008)))),
}

# Centre of the spray and its horizontal and vertical radii. Depth offsets
# distribute the groups around the foot while preserving the gaps seen head-on.
PADS = {
    "desert": ((.528,.095,-.016,.125,.087),(.621,.125,.020,.095,.079),(.384,.177,-.020,.083,.062),
               (.310,.352,.018,.107,.079),(.253,.450,-.038,.103,.084),(.705,.354,-.030,.121,.085),
               (.748,.482,.076,.101,.071),(.477,.408,.157,.087,.067),(.543,.292,-.119,.093,.068),
               (.362,.640,-.107,.133,.086),(.642,.666,-.081,.135,.080),(.171,.766,.070,.176,.103),
               (.853,.761,.061,.146,.108),(.486,.883,.122,.186,.105),(.483,.787,-.178,.140,.088)),
    "canes": ((.402,.101,-.07,.136,.115),(.658,.154,.011,.162,.130),(.266,.267,.102,.150,.132),
              (.464,.336,.183,.185,.148),(.823,.329,-.043,.161,.147),(.670,.385,-.189,.160,.157),
              (.143,.607,.034,.157,.127),(.242,.516,-.152,.152,.126),(.474,.641,-.163,.211,.153),
              (.723,.594,.134,.208,.161),(.869,.644,-.105,.111,.138),(.346,.799,.177,.175,.142),
              (.585,.850,.111,.153,.128),(.842,.837,.036,.133,.142),(.231,.888,-.085,.131,.098)),
    "broadleaf": ((.402,.107,-.041,.181,.115),(.618,.133,.055,.165,.123),(.294,.225,.105,.191,.148),
                  (.485,.255,.184,.193,.153),(.641,.284,-.166,.220,.143),(.778,.269,.026,.176,.145),
                  (.161,.366,-.043,.145,.139),(.266,.483,-.166,.200,.143),(.433,.432,-.216,.171,.162),
                  (.508,.407,.044,.197,.143),(.708,.475,.135,.210,.153),(.845,.432,-.028,.135,.155),
                  (.164,.567,.085,.150,.128),(.360,.576,.150,.165,.151),(.588,.581,-.125,.166,.132),
                  (.790,.577,-.130,.161,.106)),
    "parasol": ((.431,.116,-.075,.156,.115),(.608,.093,.034,.141,.102),(.728,.204,-.092,.169,.125),
                (.284,.237,-.064,.166,.129),(.460,.271,.132,.214,.134),(.577,.279,-.181,.164,.123),
                (.200,.378,.088,.177,.143),(.344,.443,-.151,.165,.129),(.644,.396,.139,.197,.129),
                (.815,.355,.062,.143,.112),(.837,.498,-.070,.156,.132),(.487,.456,.052,.156,.132),
                (.137,.576,-.025,.130,.127),(.266,.535,.096,.143,.118),(.686,.504,-.148,.155,.108)),
    "autumn": ((.391,.093,.014,.183,.099),(.613,.137,-.058,.164,.119),(.259,.226,-.035,.169,.140),
               (.497,.236,.139,.195,.133),(.735,.286,.046,.172,.152),(.426,.331,-.186,.202,.161),
               (.186,.393,.072,.161,.146),(.343,.478,.148,.195,.165),(.615,.459,.176,.169,.130),
               (.641,.398,-.174,.166,.144),(.834,.421,-.045,.160,.138),(.747,.541,.037,.158,.114),
               (.476,.550,-.073,.193,.120),(.218,.532,-.103,.156,.114)),
    "scrub": ((.475,.094,-.028,.165,.099),(.287,.201,.023,.165,.089),(.637,.243,.054,.155,.098),
              (.141,.353,-.011,.147,.080),(.459,.315,-.155,.151,.084),(.764,.431,-.037,.158,.093),
              (.362,.476,.146,.142,.102),(.180,.591,.074,.159,.087),(.616,.596,-.159,.162,.113),
              (.803,.663,.095,.147,.096),(.300,.734,-.091,.151,.092),(.561,.794,.133,.148,.099)),
}

# Snow accumulates on a few broad shelves; the winter paintings expose much
# more of the underlying wood than the summer radius alone would suggest.
DRIFTS = {
    "canes": ((.401,.133,-.089,.153,.104),(.535,.157,.016,.146,.095),(.746,.153,-.03,.122,.095),
              (.196,.385,.027,.156,.095),(.455,.423,.106,.143,.121),(.809,.352,.059,.176,.122),
              (.625,.349,-.171,.137,.099),(.251,.571,-.111,.154,.084),(.194,.658,.019,.182,.104),
              (.664,.649,.156,.128,.084),(.868,.561,-.080,.116,.089),(.341,.815,.148,.132,.080)),
    "broadleaf": ((.253,.226,.013,.199,.153),(.424,.123,-.113,.180,.119),(.568,.159,.045,.180,.115),
                  (.742,.181,-.041,.149,.135),(.503,.343,-.177,.205,.151),(.761,.365,.089,.184,.145),
                  (.865,.443,-.036,.107,.108),(.301,.472,.157,.180,.154),(.153,.530,-.076,.142,.141),
                  (.434,.546,-.092,.173,.133),(.685,.501,-.165,.182,.132)),
    "parasol": ((.471,.154,-.061,.231,.134),(.611,.165,.025,.199,.123),(.727,.209,-.085,.160,.110),
                (.295,.259,.051,.147,.119),(.192,.350,-.094,.148,.128),(.781,.336,.116,.156,.139),
                (.560,.362,-.150,.198,.145),(.430,.470,.156,.159,.135),(.165,.535,.013,.151,.135),
                (.667,.440,.015,.175,.121),(.866,.502,-.035,.127,.125)),
    "scrub": ((.523,.169,-.040,.260,.101),(.687,.254,.025,.171,.101),
              (.241,.324,-.030,.167,.099),(.509,.438,.063,.323,.078),
              (.592,.566,-.071,.367,.084),(.218,.695,.035,.146,.101),
              (.545,.709,.108,.276,.100),(.803,.752,-.039,.172,.085)),
}


def woodwork(tree, family, pads):
    thickness = {"canes":.56, "parasol":.63, "broadleaf":.80, "desert":.69, "scrub":.82, "autumn":.90}[family]
    stem = tree.stem
    anchors = [tree.point(x, y, z) for x, y, z, _ in stem]
    tree.tube(anchors, stem[:, 3]*thickness, 7, cap=True)
    for down, trace in ARMS.get(family, ()):
        path = [tree.stem_at(down)] + [tree.point(x, y, z) for x, y, z, _ in trace]
        radii = np.array([trace[0][3]*1.15] + [r for _, _, _, r in trace])
        tree.tube(path, radii*thickness, 6, cap=True)
        anchors.extend(path[1:])
    projected = len(tree.bark["uvs"])
    if family == "canes":
        # Separate shoots spring from the stool, each leaning in its own direction.
        for x, z, tip in ((.12,.04,.53),(.24,-.13,.29),(.33,.13,.18),(.62,-.16,.23),
                          (.73,.10,.16),(.88,-.01,.36),(.50,.20,.46),(.54,-.23,.43)):
            root = tree.point(.5+(x-.5)*.36, 1.002, z*.46)
            head = tree.point(x, tip, z)
            elbow = root*.48+head*.52 + [0,-.095,0]
            limb(tree, [root, elbow, head], [.010,.008,.003], 5)
            anchors.extend((elbow, head))
    else:
        spread = {"broadleaf":.16,"parasol":.062,"autumn":.176,"desert":.07,"scrub":.14}[family]
        for i in range(5):
            az = (i+.16)*math.tau/5
            ray = tree.aspect*np.array([math.cos(az), 0, math.sin(az)])
            length = spread*tree.rng.uniform(.70, 1)
            path = [tree.stem_at(.944), tree.stem_at(.977)+ray*length*.32,
                    tree.stem_at(.990)+ray*length*.70, tree.stem_at(1.003)+ray*length]
            radius = stem[0,3]*thickness
            limb(tree, path, radius*np.array([.63,.55,.32,.065]), 5)
    anchors = np.array(anchors)
    if family == "scrub":
        for down, x, tip_y, z in ((.91,.22,.874,.070),(.85,.81,.824,-.045),(.725,.91,.791,.065),
                                  (.70,.20,.638,-.075),(.482,.722,.456,.129)):
            start = tree.stem_at(down)
            tip = tree.point(x,tip_y,z)
            elbow = .43*start+.57*tip+[0,.069,0]
            limb(tree,[start,elbow,tip],[.028,.018,.004],5)
    for x, down, z, width, height in pads:
        node = tree.point(x, down+.18*height, z)
        if family in ("desert","scrub"):
            # Visible horizontal twig before each spaced tuft or snowy shelf.
            start = tree.stem_at(min(.92, down+.095))
            elbow = start+(node-start)*.62 - [0,.024,0]
            limb(tree, [start, elbow, node], [.014,.009,.0035], 5)
        else:
            connect(tree, anchors, node, min(.014,width*.078)*thickness)
    return projected


def bark(tree, family, projected):
    rgb = tree.pixels[..., :3].astype(float)
    y = np.linspace(0, 1, len(rgb))[:, None]
    solid = tree.pixels[..., 3] > 128
    warm = (rgb[..., 0] >= .99*rgb[..., 1]) & (rgb[..., 2] < rgb[..., 0]*1.14)
    wood = solid & ~snow_mask(tree.pixels) & warm & (y > (.36 if family in ("canes","scrub") else .64))
    if not np.any(wood):
        wood = solid & (y>.66)
    colours = rgb[wood]
    ramp = np.array(colour_ramp(colours, [.08,.23,.44,.65,.82,.95,.99]))
    y, x = np.indices((64,48))
    angle = math.tau*x/48 + .18*np.sin(y/13) + .09*np.sin(y/5+x/5)
    tone = .47+.22*np.sin(4*angle)+.18*np.sin(9*angle+.4)+.10*np.cos(13*angle-y/11)
    grain = ramp[np.clip(np.rint(6*tone).astype(int),0,6)].copy()
    grain[..., :3] = .58*grain[..., :3]+.42*np.median(colours,axis=0)
    # Use the actual fork's bark on its modeled faces. Small twigs have a
    # separate wrapping chart, so a thin branch retains grain on its sides.
    h, w = wood.shape
    atlas = Image.new("RGBA", (48+w,max(64,h)))
    atlas.paste(filled(tree.pixels, wood), (48,0))
    atlas.paste(Image.fromarray(grain.astype(np.uint8)))
    uv = tree.bark["uvs"]
    uv[:projected:2] = [(48+u*w)/atlas.width for u in uv[:projected:2]]
    uv[1:projected:2] = [v*h/atlas.height for v in uv[1:projected:2]]
    uv[projected::2] = [u*48/atlas.width for u in uv[projected::2]]
    uv[projected+1::2] = [v*64/atlas.height for v in uv[projected+1::2]]
    return atlas


class LeafSprays:
    """Wrap each twig's own patch of the painting over a tuft. UVs and cutout
    margins follow the whole tuft, including the top and the shaded underside."""

    def __init__(self, tree, family, skirt, snowy):
        self.tree = tree
        self.winter = WinterBoughs(tree, skirt=skirt, brown=family in ("canes","scrub")) if snowy else None
        self.paint = tree.pixels.copy()
        rgb = self.paint[..., :3].astype(float)
        if family in ("parasol", "broadleaf", "desert"):
            green = rgb[..., 1] >= rgb[..., 0]*.97
            self.paint[..., 3] *= green | snow_mask(self.paint)
        self.paint[round(len(self.paint)*skirt):, :, 3] = 0
        self.image = Image.fromarray(self.paint)

    def grow(self, x, down, z, width, height, depth, phase):
        tree = self.tree
        # The doubled-back brushstrokes continue across the rear. The depth
        # and lower lip turn in and out around the twig, breaking up the edge.
        levels = np.array([1., .72, .43, .16, 0.])
        radii = np.array([.06, .91, 1., .72, .045])
        angles = np.linspace(0, math.tau, 7)
        def outline(a):
            return 1+.09*np.cos(3*a+phase)+.055*np.sin(5*a-phase)
        def lip(a):
            return .16*np.sin(3*a+phase)+.11*np.cos(5*a-phase)
        rings, uv = [], []
        slot = len(tree.patches)
        for t, r in zip(levels,radii):
            edge = r*outline(angles)
            rings.append(tree.point(x,down,z)+np.stack([
                width*tree.aspect*edge*np.cos(angles),
                -height*(2*t-1+r*lip(angles)),
                depth*tree.aspect*edge*np.sin(angles)],axis=1))
            uv.append(np.stack([(slot%8+.008+.984*angles/math.tau)/8, np.full_like(angles,slot//8+.01+.98*t)],axis=1))
        add_grid(tree.leaves,np.array(rings),uv,seam=True)
        close_leaf_ring(tree,rings[-1],[0,1,0],slot,.012)
        close_leaf_ring(tree,rings[0][::-1],[0,-1,0],slot,.985)

        v,u = np.indices((56,64))
        a = u*math.tau/63
        t = v/55
        r = np.interp(t, levels[::-1], radii[::-1])
        h,w = self.paint.shape[:2]
        # Bring a whole patch around each quarter turn. A single front
        # projection collapses the leaf pattern into stripes at either side.
        across = 2/math.pi*np.arcsin(np.sin(2*a))
        sx = x+width*r*outline(a)*across
        sy = down+height*(2*t-1+r*lip(a)+.22*r*np.sin(a)*np.maximum(0,1-3*t))
        cols = np.rint(sx*(w-1)).astype(int).clip(0,w-1)
        rows = np.rint(sy*(h-1)).astype(int).clip(0,h-1)
        tile = self.paint[rows,cols].copy()
        tile[...,3] *= (sx>=0)&(sx<=1)&(sy>=0)&(sy<=1)
        light = tile[..., :3] @ [.21,.72,.07]
        fringe = .83+.075*np.cos(8*a+phase)+.055*np.sin(17*a-phase)
        tile[...,3] *= (t < fringe) | (light > 55)
        if self.winter is not None and down < .80:
            # Give the top a drift with a broken, painted lip. Let exposed
            # leaves darken below it, even when the whole source crop is white.
            frosted = snow_mask(tile)
            under = np.asarray(self.winter.leaf_paint)[rows,cols]
            roof = .46+.10*np.sin(3*a+phase)+.055*np.cos(7*a-phase)
            below = (t>roof) & frosted
            tile[below,:3] = under[below,:3]
            powder = np.asarray(self.winter.powder)[v,u,:3]
            cap = (t < roof-.19) & ~frosted & (tile[...,3]>0)
            tile[cap,:3] = powder[cap]
        tree.patches.append(Image.fromarray(tile))

        # Short, bowed sprays extend through the shell. Their real painted
        # borders resolve the small leaf silhouettes where the bush meets sky.
        box = (round((x-width)*w), round((down-height)*h),
               round((x+width)*w)+1, round((down+height)*h)+1)
        patch = np.array(self.image.crop(box).resize((64,56),Image.Resampling.NEAREST))
        fx, fy = (u-31.5)/32, (v-27.5)/28
        lit = patch[..., :3] @ [.21,.72,.07]
        angle = np.arctan2(fy,fx)
        edge = .68+.22*np.clip(lit/105,0,1)+.08*np.sin(angle*7+phase)+.06*np.cos(12*angle)
        patch[...,3] *= fx*fx+fy*fy < edge*edge
        slot = len(tree.patches)
        tree.patches.append(Image.fromarray(patch))
        for az in (phase,phase+2.60):
            ray = tree.aspect*np.array([width*math.cos(az),0,depth*math.sin(az)])
            side = tree.aspect*np.array([-width*math.sin(az),0,depth*math.cos(az)])
            grid, uv = [], []
            for t,reach,fall,span in ((0,.20,-.77,.65),(.49,.76,-.12,.86),(1,1.11,1.15,.72)):
                grid.append([tree.point(x,down,z)+ray*(reach-.24*k*k)+side*k*span-
                             [0,(fall+.16*k*k)*height,0] for k in (1,0,-1)])
                uv.append([[(slot%8+s)/8,slot//8+.01+.98*t] for s in (.01,.50,.99)])
            add_grid(tree.leaves,np.array(grid),uv,double=True)


def leaf_fans(tree, family, pads):
    # Folded blades share a growing tip. The midrib runs from that tip
    # to the drooping end of each blade and stays visible from above.
    rgb = tree.pixels[..., :3].astype(float)
    y = np.linspace(0, 1, len(rgb))[:, None]
    solid = (tree.pixels[..., 3] > 128) & (y < .62)
    green = solid & (rgb[..., 1] > rgb[..., 0]*1.025)
    pigments = [rgb[green]]
    if family == "autumn":
        pigments += [rgb[solid & (rgb[..., 0] > rgb[..., 1]*1.16) & (rgb[..., 2] > rgb[..., 1])],
                     rgb[solid & (rgb[..., 0] > rgb[..., 1]*1.12) & (rgb[..., 1] > rgb[..., 2]*1.42)]]
    slots = []
    yy, xx = np.indices((56,64))
    u, v = (xx-31.5)/32, yy/55
    for pigment in pigments:
        if len(pigment) == 0:
            continue
        ramp = np.array(colour_ramp(pigment,[.04,.16,.35,.55,.74,.89,.97]))
        vein = np.abs(np.sin((v-.30*abs(u))*math.pi*7)) < .17
        tone = .48+.14*(u<0)+.12*np.sin(v*4)-.27*abs(u)-.20*v+.17*vein*(1-abs(u))+.21*(abs(u)<.07)
        slots.append(len(tree.patches))
        pixels = ramp[np.clip(np.rint(6*tone).astype(int),0,6)].astype(np.uint8)
        # Break the outermost texels on each side of the blade.
        pixels[...,3] *= abs(u) < .94+.05*np.sin(yy*.55+2*v)
        tree.patches.append(Image.fromarray(pixels))
    for i, (x, down, z, width, height) in enumerate(pads):
        facing = math.atan2(z, x-.5)
        outward = np.array([math.cos(facing),0,math.sin(facing)])
        node = tree.point(x,down-.70*height,z) + outward*tree.aspect*width*.24
        for j in range(2):
            az = facing+(j-.5)*1.35+tree.rng.uniform(-.26,.26)
            ray = tree.aspect*width*np.array([math.cos(az),0,math.sin(az)])
            side = np.cross(ray,[0,1,0])
            length = tree.rng.uniform(.68,1.02)
            fall = tree.rng.uniform(.84,1.22)*height
            slot = slots[(i+2*j)%len(slots)]
            rows, uv = [], []
            for t, reach, lift, breadth in ((0,0,0,.015),(.26,.32,.045,.154),(.56,.63,-.15,.205),
                                           (.80,.84,-.45,.145),(1,.96,-.84,.012)):
                centre = node+ray*length*reach+[0,fall*lift,0]
                rows.append([centre+k*breadth*side-[0,abs(k)*breadth*height*.55,0] for k in (1,0,-1)])
                uv.append([[(slot%8+s)/8,slot//8+.02+.96*t] for s in (.02,.5,.98)])
            add_grid(tree.leaves,np.array(rows),uv,double=True)


def flowers(tree):
    # Three real corollas from the painting. Remove the surrounding greenery
    # and cup the petal faces toward the light around the outside of the canes.
    slots = []
    image = Image.fromarray(tree.pixels)
    for box in ((18,21,33,33),(32,12,46,24),(73,8,87,20)):
        tile = np.array(image.crop(box).resize((64,56),Image.Resampling.NEAREST))
        r,g,b = tile[..., :3].astype(float).transpose(2,0,1)
        tile[...,3] *= (r>g*1.09)&(b>g*1.03)
        slots.append(len(tree.patches))
        tree.patches.append(Image.fromarray(tile))
    for i, (x, down, z, width, height) in enumerate(PADS["canes"]):
        for j in range(2):
            angle = i*2.4+(j+.17)*math.pi
            ray = np.array([math.cos(angle),0,math.sin(angle)])
            side = np.cross(ray,[0,1,0])
            centre = tree.point(x,down-.31*height,z)+ray*width*tree.aspect*.89
            radius = tree.rng.uniform(.037,.054)
            slot = slots[(i+j)%3]
            rows,uv = [],[]
            for t in (0,1):
                rows.append([centre+side*k*radius+[0,(.5-t)*radius*1.8,0]+
                             ray*radius*(.65*(t-.5)+.20*abs(k)) for k in (-1,0,1)])
                uv.append([[(slot%8+s)/8,slot//8+.02+.96*t] for s in (.02,.5,.98)])
            add_grid(tree.leaves,np.array(rows),uv,double=True)


def build(key, rgba):
    archive, record = map(int, key.split("-"))
    family = FAMILY[record]
    snow = archive in (505,509)
    pads = (DRIFTS if snow else PADS)[family]
    if family == "canes":
        base = ((.250,.823,-.102,.231,.137),(.504,.793,-.048,.242,.162),
                (.743,.844,.034,.224,.135),(.452,.898,.109,.200,.113),(.618,.690,-.217,.172,.151))
    elif family == "scrub":
        base = ((.42,.826,-.093,.195,.108),(.659,.821,.044,.157,.102))
    else:
        base = ()
    if family == "canes" and snow:
        base += ((.414,.519,-.050,.195,.183),(.630,.491,.095,.193,.169),
                 (.687,.714,-.053,.171,.125))
    tree = Tree(record, rgba, STEMS[family])
    projected = woodwork(tree, family, pads+base)
    skirt = {"canes":1, "desert":1, "broadleaf":.77, "parasol":.75, "autumn":.69, "scrub":.90}[family]
    leafy = family in ("parasol", "broadleaf", "autumn")
    if leafy and not snow:
        boughs = Boughs(tree, record, skirt, autumn=family == "autumn")
        for i, (x, down, z, width, height) in enumerate(pads):
            boughs.grow(x, down+.03*height, z*1.2, width*1.10, height*1.22, width, i*2.4, sprays=2)
        leaf_fans(tree, family, pads)
        leaves = boughs.atlas()
    else:
        sprays = LeafSprays(tree, family, skirt, snow)
        for i, (x, down, z, width, height) in enumerate(pads):
            if family == "desert":
                z *= 1.40
            if leafy:
                z *= 1.25
                down += .10*height
                height *= 1.30
            radius = width*(1.12 if leafy or family == "desert" else 1.04)
            sprays.grow(x, down, z, radius, height, width*1.05, i*2.40+.16)
        # At ground level the canes and dead leaves are mostly negative space.
        # Carry their ragged border around the stool on individual bowed sprays.
        w, h = sprays.image.size
        for x, down, z, width, height in base:
            box = (round((x-width)*w),round((down-height)*h),
                   round((x+width)*w)+1,round((down+height)*h)+1)
            patch = np.array(sprays.image.crop(box).resize((64,56),Image.Resampling.NEAREST))
            row,col = np.indices((56,64))
            u,v = (col-31.5)/32,(row-27.5)/28
            edge = .87+.08*np.sin(np.arctan2(v,u)*9+x*12)
            patch[...,3] *= u*u+v*v<edge*edge
            upright_tuft(tree,Image.fromarray(patch),tree.point(x,down,z),width*tree.aspect,height*1.22)
        if family == "canes" and not snow:
            flowers(tree)
        leaves = tree.cluster_atlas()
    # Deepen the interior and the hanging undersides with vertex colour.
    pos = np.array(tree.leaves["positions"]).reshape(-1, 3)
    normals = np.array(tree.leaves["normals"]).reshape(-1, 3)
    shade = .65+.20*np.clip(normals[:,1],0,1)+.15*np.clip(pos[:,1],0,1)
    if snow:
        shade = .87+.13*shade
    colours = np.array(tree.leaves["colors"], dtype=float).reshape(-1, 4)
    colours[:,:3] *= shade[:,None]
    tree.leaves["colors"] = colours.round(4).ravel().tolist()
    parts = [tree.bark, tree.leaves]
    return {"height": max(max(p["positions"][1::3]) for p in parts), "parts": parts}, {
        "bark": bark(tree, family, projected), "leaves": leaves}
