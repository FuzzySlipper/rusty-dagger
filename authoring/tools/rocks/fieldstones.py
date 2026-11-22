"""Nature rocks. Assemble closed stones in loose groups, then bake the stone, moss
and snow into one atlas. Coordinates are in units of the pile's standing height."""
from dataclasses import dataclass
import hashlib
import math

import numpy as np
from PIL import Image
from scipy.ndimage import map_coordinates

import rocks


# A piece's x and depth, base height, width, height, depth span and lean (tip displacement in x).
# The small stones in front and behind a group break the join at ground level.
def stone(x, z, base, width, height, depth, lean=0, kind="block"):
    return (x, z, base, width, height, depth, lean, kind)


def cairn(width, bands):
    """Each bearing face overlaps the seat below it. Offset and tilt individual capstones."""
    pieces = []
    base = -.025
    for i, (height, scale, offset) in enumerate(bands):
        span = width*scale
        pieces.append(stone(width*offset, .032*width*math.sin(i*2), base, span, height, span*.88,
                            .04*span*math.sin(i+1), "slab"))
        base += .86*height
    return pieces


def outcrop(width, lean, flanks):
    pieces = [stone(-.08*width, -.06*width, -.045, .68*width, 1.045, .66*width, lean, "blade")]
    for x, z, w, h, tilt in flanks:
        pieces.append(stone(x*width, z*width, -.035, w*width, h, w*width*.87, tilt, "blade"))
    return pieces


def heap(width, stones):
    return [stone(x*width, z*width, base, w*width, h, d*width, lean*width)
            for x, z, base, w, h, d, lean in stones]


LOW = ((.10,-.08,-.04,.68,1.05,.56,-.06), (-.27,.12,-.03,.43,.55,.38,.04),
       (-.05,.27,-.04,.32,.40,.27,-.04), (.34,.19,-.04,.30,.37,.27,.02),
       (-.34,-.11,-.03,.28,.37,.30,0))
SHOULDER = ((-.12,-.08,-.04,.69,1.04,.59,.08), (.25,.05,-.04,.47,.73,.43,-.06),
            (-.26,.20,-.04,.41,.54,.32,.04), (.05,.24,-.04,.35,.39,.24,0),
            (.39,.18,-.025,.19,.32,.18,0))
ROUND = ((0,-.03,-.045,.88,1.05,.73,-.04), (-.36,.14,-.03,.30,.32,.29,.03),
         (.34,.06,-.02,.27,.28,.24,-.02))
TALL = ((-.07,-.09,-.045,.78,1.05,.69,.08), (.30,.10,-.03,.40,.58,.43,-.07),
        (-.26,.19,-.04,.43,.52,.35,.04), (.05,.24,-.03,.33,.37,.28,-.04))
FINS = ((-.28,.12,.36,.60,.08), (.25,-.08,.44,.76,.13), (.26,.19,.32,.44,.04))

# Repeated winter and climate depictions use the same arrangement and normalized mesh.
LAYOUTS = {
    "rain-pile": [
        stone(-.07,-.08,-.04,.77,.55,.67,-.09), stone(.24,.08,-.04,.52,.43,.51,-.07),
        stone(-.31,.16,-.04,.38,.32,.34,.03), stone(-.12,-.07,.31,.65,.48,.55,.04),
        stone(-.17,.01,.64,.53,.36,.45,.11), stone(.05,.25,-.04,.45,.38,.34,.03)],
    "vine-spire": outcrop(.59,.11,FINS),
    "moss-bank": heap(1.79,LOW), "moss-shoulder": heap(1.69,SHOULDER),
    "moss-pebble": heap(1.84,ROUND), "moss-tower": heap(.99,(
        (-.12,-.08,-.03,.67,.48,.70,.04), (.23,.08,-.03,.52,.38,.50,-.02),
        (-.09,.13,.31,.69,.38,.63,.04), (.03,-.04,.59,.65,.42,.60,.04))),
    "black-stack": cairn(.49,[(.33,1,0),(.23,.92,.06),(.23,.97,-.08),(.22,.80,.02),(.16,.62,.04)]),
    "round-stack": cairn(1.00,[(.37,.98,0),(.30,1,.02),(.25,.83,-.06),(.25,.68,0)]),
    "thin-stack": cairn(.49,[(.22,1,.02),(.23,.92,-.03),(.19,1,.05),(.22,.91,0),(.18,.84,-.08),(.15,.81,.06)]),
    "green-stack": cairn(.57,[(.30,.98,0),(.28,.91,.04),(.23,.83,-.04),(.22,.91,.06),(.17,.77,.03)]),
    "sand-pebble": [stone(0,0,-.06,1.47,1.06,1.23,.11)],
    "sand-heap": heap(1.83,SHOULDER), "sand-knuckle": [
        stone(.06,.02,-.045,.56,.50,.54,-.12),stone(.13,-.06,.18,.42,.56,.44,-.25),
        stone(.07,-.09,.58,.28,.42,.29,-.15),stone(-.13,.12,.22,.35,.39,.32,.08)],
    "sand-pillar": [
        stone(-.04,-.03,-.04,.47,.63,.44,.05),stone(.10,.07,-.03,.33,.52,.31,.02),
        stone(.01,-.02,.45,.45,.38,.40,-.05,"slab"),stone(-.03,-.03,.73,.38,.28,.36,.07,"slab")],
    "sand-talus": heap(1.27,TALL), "sand-bank": heap(2.08,LOW),
    "flat-pebble": [stone(0,0,-.06,1.69,1.06,1.13,-.08,"slab")],
    "chip": [stone(0,0,-.09,1.54,1.09,1.28,.18)],
    "grey-bank": heap(1.78,LOW), "grey-shoulder": heap(2.02,SHOULDER),
    "grey-knuckle": heap(1.12,TALL), "grey-bedding": heap(1.60,ROUND),
    "crag": [
        stone(.02,-.05,-.04,.47,.74,.51,-.08,"plate"),stone(-.07,-.055,.54,.39,.47,.38,.04),
        stone(.18,.05,-.04,.28,.56,.32,-.035,"plate"),stone(-.19,.10,-.05,.29,.39,.29,.05),
        stone(.035,.19,-.05,.31,.27,.25,-.035)],
    # A broad cap bears on solid shoulders through the original's arch.
    "split-boulder": [
        stone(-.13,-.035,-.03,.46,.69,.48,.09),stone(.13,-.05,-.04,.36,.76,.44,-.07,"plate"),
        stone(.01,-.06,.58,.46,.42,.42,-.07,"slab"),stone(-.19,.15,-.04,.29,.40,.30,-.02),
        stone(.16,.14,-.04,.29,.43,.29,.04),stone(-.015,.19,-.03,.25,.20,.23,.023)],
    "fin-ridge": outcrop(.72,.17,FINS) + [stone(-.20,.17,-.04,.28,.32,.27,.09,"plate")],
    "needle-cluster": outcrop(.67,.085,((-.32,.12,.43,.53,.10),(.20,-.10,.50,.77,.075),(.26,.16,.38,.36,.035)))
                      + [stone(-.12,.23,-.03,.25,.26,.20,.025)],
    "layered-crag": [
        stone(.05,-.06,-.06,.81,.75,.64,-.14),stone(.095,.08,.23,.80,.49,.50,-.14,"bed"),
        stone(-.015,-.06,.52,.73,.44,.51,-.11,"bed"),stone(-.21,.16,-.035,.42,.51,.33,.05,"plate"),
        stone(.28,.14,-.035,.37,.41,.30,-.08)],
    "tilted-slab": [
        stone(-.27,-.04,-.045,.54,1.05,.54,.49,"blade"),stone(.07,-.10,-.04,.48,.79,.39,.25,"plate"),
        stone(-.04,.13,-.04,.38,.56,.33,.25,"plate"),stone(-.29,.13,-.04,.24,.27,.25,.09)],
    "moss-needle": outcrop(.42,.09,((-.27,.05,.42,.63,.11),(.19,-.08,.45,.77,-.055))),
    "fallen-slab": [stone(-.015,-.05,-.035,.53,1.03,.51,-.36,"plate"),
                    stone(.21,.08,-.045,.45,.65,.49,-.27,"plate"),
                    stone(.15,-.10,-.045,.38,.33,.35,-.035)],
    "rust-blade": [stone(-.10,-.015,-.035,.32,1.03,.31,.19,"blade"),
                   stone(.075,.069,-.04,.24,.56,.22,.09,"plate"),
                   stone(-.126,.10,-.034,.19,.36,.19,.065)],
    "grey-stack": cairn(.70,[(.32,1,0),(.27,.88,-.04),(.22,.69,.05),(.19,.61,-.04),(.18,.59,.08)]),
    "rock-sapling": [stone(-.12,-.02,-.04,.46,.59,.52,.095),stone(.15,.05,-.04,.38,.43,.41,.08),
                     stone(-.18,.16,-.025,.27,.26,.30,.04)],
    "egg-clutch": [
        stone(-.45,.16,-.045,.47,.51,.43,.06,"egg"),stone(.39,-.03,-.035,.51,.60,.45,-.13,"egg"),
        stone(.10,-.15,.20,.47,.81,.43,-.07,"egg"),stone(-.14,.01,.04,.54,.60,.51,.13,"egg"),
        stone(.18,.24,-.025,.55,.59,.47,.16,"egg"),stone(.58,.21,-.03,.31,.31,.25,-.03,"egg"),
        stone(-.58,-.10,-.04,.29,.29,.30,.02,"egg")],
}

@dataclass(frozen=True)
class Painting:
    layout: str
    finish: str = "slate"
    moss: float = 0
    snow: float = 0


P = Painting
PAINTINGS = {
    "500-4":P("rain-pile","basalt"), "500-17":P("vine-spire","basalt",.61),
    "500-31":P("egg-clutch","egg"), "501-3":P("moss-bank","slate",.86),
    "501-4":P("moss-shoulder","slate",.79), "501-5":P("moss-pebble","slate",.67),
    "501-6":P("moss-tower","slate",.76), "501-10":P("rock-sapling","slate",.47),
    "502-4":P("black-stack","basalt"), "502-5":P("round-stack","basalt"),
    "502-6":P("thin-stack","basalt"), "502-10":P("green-stack","basalt",.89),
    "503-2":P("sand-pebble","sandstone"), "503-3":P("sand-heap","sandstone"),
    "503-4":P("sand-knuckle","sandstone"), "503-18":P("sand-pillar","sandstone"),
    "503-19":P("sand-talus","sandstone"), "503-20":P("sand-bank","sandstone"),
    "503-21":P("flat-pebble","sandstone"), "503-22":P("chip","sandstone"),
    "504-3":P("grey-bank"), "504-4":P("grey-shoulder","slate",.32),
    "504-5":P("grey-knuckle","slate",.15), "504-6":P("grey-bedding"),
    "506-1":P("crag"), "506-3":P("split-boulder","slate",.65),
    "506-4":P("fin-ridge","slate",.20), "506-6":P("needle-cluster"),
    "506-17":P("layered-crag"), "506-18":P("tilted-slab"), "506-28":P("moss-needle","slate",.80),
    "508-1":P("fallen-slab"), "510-14":P("rust-blade","ironstone"),
    "511-12":P("grey-stack"),
}
for winter, summer, cover in (
    ("505-3","504-3",.78),("505-4","504-4",.76),("505-5","504-5",.87),("505-6","504-6",.88),
    ("507-1","506-1",.85),("507-3","506-3",.58),("507-4","506-4",.81),("507-6","506-6",.74),
    ("507-17","506-17",.80),("507-18","506-18",.77),("507-28","506-28",.83),
    ("509-1","508-1",.67),("509-12","501-10",.76),("511-14","510-14",.56)):
    original = PAINTINGS[summer]
    PAINTINGS[winter] = P(original.layout, original.finish, .10*original.moss, cover)

# Identical paintings share every mesh and material, at the receiving flat's own placement.
SHARED = {"502-11":"500-31", "501-23":"501-6", "508-12":"501-10", "510-27":"508-1", "511-27":"509-1",
          **{f"510-{r}": f"506-{r}" for r in (1,3,4,6,17,18,28)},
          **{f"511-{r}": f"507-{r}" for r in (1,3,4,6,17,18,28)},
          **{f"508-{r}": f"506-{s}" for r,s in ((5,1),(6,6),(3,17),(4,18))},
          **{f"509-{r}": f"507-{s}" for r,s in ((5,1),(3,17),(4,18))}, "509-6":"506-6"}

LAYOUTS["egg-clutch"] += [
    stone(-.08,.02,-.05,1.68,.21,.99,-.01,"earth"),
    stone(-.65,.18,-.026,.21,.12,.19,.02,"earth"),stone(.50,.34,-.02,.17,.10,.15,-.02,"earth")]
LAYOUTS["egg-clutch"].append(stone(.09,-.25,-.035,.39,.37,.40,.02,"egg"))
# A few climbing stems interrupt this dark rainforest spire. The rock beneath
# them remains a solid mass even where the painted vine breaks the outline.
LAYOUTS["vine-spire"] += [
    stone(-.13,.17,.05,.031,.23,.032,.055,"twig"),stone(-.075,.17,.26,.034,.19,.032,-.085,"twig"),
    stone(-.16,.17,.43,.028,.20,.028,.067,"twig"),stone(-.095,.14,.61,.024,.15,.024,.069,"twig"),
    stone(-.12,.15,.49,.020,.13,.018,-.093,"twig"),stone(-.21,.15,.60,.075,.046,.066,-.018,"leaf"),
    stone(-.15,.17,.53,.080,.058,.073,.018,"leaf"),stone(-.10,.16,.61,.083,.049,.072,-.014,"leaf"),
    stone(-.047,.14,.72,.063,.052,.05,.007,"leaf"),stone(-.193,.15,.625,.055,.039,.044,.013,"leaf")]
# The little tree is rooted in the cleft, with a bent trunk and three fans of leaves.
# Wood and foliage share a second opaque draw so only the rock contributes to the collider.
LAYOUTS["rock-sapling"] += [
    stone(.01,0,.35,.058,.28,.054,.11,"twig"), stone(.12,0,.60,.047,.17,.044,-.048,"twig"),
    stone(.075,0,.75,.034,.17,.032,.037,"twig"), stone(.10,.01,.66,.033,.13,.031,.145,"twig"),
    stone(.12,0,.62,.033,.15,.030,-.11,"twig"), stone(.236,.02,.78,.021,.09,.019,-.014,"twig"),
    stone(.025,-.015,.757,.12,.076,.10,-.009,"leaf"),stone(.006,.008,.795,.095,.072,.10,.01,"leaf"),
    stone(.213,.025,.816,.165,.085,.159,.013,"leaf"),stone(.248,-.006,.858,.118,.079,.111,-.020,"leaf"),
    stone(.085,-.033,.868,.15,.091,.14,.01,"leaf"),stone(.123,.017,.921,.16,.080,.126,-.015,"leaf"),
    stone(.162,-.022,.902,.112,.071,.125,.02,"leaf")]

# Heights and half-widths of the shoulder, belly and bevels. Small end rings
# close with a fan; both the underside and the back of every rock are modeled.
PROFILES = {
    "block": ((0,.52),(.14,.88),(.43,1),(.72,.89),(.91,.58),(1,.19)),
    "slab":  ((0,.66),(.16,.91),(.46,1),(.75,.93),(.92,.72),(1,.28)),
    "blade": ((0,.67),(.17,.98),(.46,.88),(.73,.72),(.89,.60),(1,.29)),
    "plate": ((0,.61),(.13,.90),(.45,1),(.70,.87),(.90,.63),(1,.42)),
    "egg":   ((0,.19),(.085,.64),(.24,.94),(.43,1),(.64,.92),(.81,.71),(.94,.43),(1,.09)),
    "bed":   ((0,.45),(.18,.88),(.48,1),(.77,.91),(.94,.59),(1,.26)),
    "earth": ((0,.69),(.25,1),(.71,.80),(1,.22)),
    "twig":  ((0,1),(.55,.84),(1,.62)),
    "leaf":  ((0,.24),(.25,.87),(.52,1),(.79,.77),(1,.24)),
}


def rings_of(piece, rng):
    x,z,base,w,h,d,lean,kind = piece
    count = {"egg":12, "twig":5, "leaf":7, "earth":8, "blade":7, "plate":8}.get(kind,9)
    a = math.tau*(np.arange(count)+rng.uniform(-.17,.17,count))/count + rng.uniform(0,math.tau)
    # Clipped shoulders give even the small stones a few readable planes. The egg
    # has a smooth narrower end; stone gets a tilted bedding plane and a broken edge.
    strength = {"egg":.035,"twig":.06,"leaf":.19}.get(kind,.15)
    edge = 1+rng.uniform(-strength,strength,count)
    tilt = rng.uniform(-.13,.13)
    levels = PROFILES[kind]
    rings = []
    for t, radius in levels:
        cx = np.cos(a)*edge
        cz = np.sin(a)*edge
        if kind in ("blade","plate"):
            # A fracture has a broad face and a bevel. Squaring the footprint
            # and cutting the top obliquely avoids a smooth cone on every spire.
            cx = np.sign(cx)*abs(cx)**.74
            cz = np.sign(cz)*abs(cz)**.74
        shift = radius*min(1,4*t)
        ripple = tilt*cx+.065*np.sin(2*a+.7)
        if kind in ("blade","plate"):
            ripple += (.16 if lean >= 0 else -.16)*cx + .105*cz
        if kind == "egg":
            ripple *= .20
        if kind == "twig":
            ripple *= .05
        radius *= 1+rng.uniform(-strength*.25,strength*.25,count)
        level = base+h*(t+shift*ripple)
        if kind == "bed":
            level -= .31*w*radius*cx
        rings.append(np.stack([x+lean*t+w*.50*radius*cx,level,z+d*.50*radius*cz],axis=1))
    return np.array(rings)


def surface(rings, kind):
    rows, sides = rings.shape[:2]
    vertices = list(rings.reshape(-1,3))
    faces, coords = [], []
    # UV seams share positions for lighting. Coordinates have a separate index
    # on either side of the seam, so no face interpolates across a whole atlas.
    for j in range(rows-1):
        for i in range(sides):
            k = (i+1)%sides
            a,b,c,d = j*sides+i,(j+1)*sides+i,j*sides+k,(j+1)*sides+k
            low, high = (j+1)/(rows+1), (j+2)/(rows+1)
            uv = [(i/sides,low),(i/sides,high),((i+1)/sides,low),((i+1)/sides,high)]
            faces.extend(((a,b,c),(c,b,d)))
            coords.extend(((uv[0],uv[1],uv[2]),(uv[2],uv[1],uv[3])))
    for row in (0,rows-1):
        tip = len(vertices)
        vertices.append(rings[row].mean(0))
        for i in range(sides):
            a,b = row*sides+i,row*sides+(i+1)%sides
            faces.append((tip,a,b) if row==0 else (tip,b,a))
            edge = (row+1)/(rows+1)
            left, right = (i/sides,edge), ((i+1)/sides,edge)
            centre = ((i+.5)/sides, 0 if row==0 else 1)
            coords.append((centre,left,right) if row==0 else (centre,right,left))
    vertices, faces = np.array(vertices), np.array(faces)
    if kind in ("egg","leaf"):
        normals = rocks.vertex_normals(vertices,faces)[faces]
    else:
        _, normals = rocks.split_normals(vertices,faces)
    return vertices[faces], normals, np.array(coords)


def sampling_grid(rings, width, height):
    """Sample the painted wrap in 3D, continuously through the rear seam."""
    # Extend the wrap onto the end fans. Clutter viewed from directly above
    # still has stone grain across its broad cap.
    bottom, top = rings[[0,-1]].mean(axis=1)
    rings = np.concatenate((np.broadcast_to(bottom,rings[:1].shape), rings,
                            np.broadcast_to(top,rings[:1].shape)))
    n,m = rings.shape[:2]
    # The rim of each atlas tile pads the wrap on all four edges.
    u = np.clip((np.arange(width)/(width-1)-.01)/.98,0,1)*m
    v = np.clip((np.arange(height)/(height-1)-.01)/.98,0,1)*(n-1)
    i,j = u.astype(int)%m, np.minimum(v.astype(int),n-2)
    across = np.mod(u,1)[None,:,None]
    along = (v-j)[:,None,None]
    def sample(values):
        def row(indices):
            return values[indices[:,None],i]*(1-across)+values[indices[:,None],(i+1)%m]*across
        return row(j)*(1-along)+row(j+1)*along

    point = sample(rings)
    # Paint moss and snow over the edge of a facet. Interpolating the surface
    # normals here avoids rectangular snow lines at the geometry's ring seams.
    around = np.roll(rings,-1,axis=1)-np.roll(rings,1,axis=1)
    slopes = np.cross(np.gradient(rings,axis=0),around)
    slopes /= np.maximum(1e-10,np.linalg.norm(slopes,axis=2,keepdims=True))
    slopes[0], slopes[-1] = (0,-1,0), (0,1,0)
    normal = sample(slopes)
    normal /= np.maximum(1e-10,np.linalg.norm(normal,axis=2,keepdims=True))
    return point,normal


LUMA = np.array([.21,.72,.07])
# Swatches read from the classic references. Exclude soil under the basalt
# and keep the original clutch's cool shell and fine flecks.
CLASSIC_RAMPS = {
    "500-4": [(16,16,19),(25,25,27),(35,35,37),(48,48,50),(63,64,66),(82,82,82)],
    "502-4": [(17,16,19),(24,24,26),(34,33,36),(43,42,45),(57,56,58),(72,71,71)],
    "500-31":[(47,44,52),(73,71,81),(94,92,109),(121,120,137),(146,145,160),(168,168,178)],
}


def ramp_for(key, rgba):
    if key in CLASSIC_RAMPS:
        return np.array(CLASSIC_RAMPS[key],float)
    colours = rgba[rgba[...,3]>128,:3].astype(float)
    light = colours@LUMA
    dry = (light>17)&(colours.min(1)<155)
    green = (colours[:,1]>1.04*colours[:,0])&(colours[:,1]>1.08*colours[:,2])
    rock = colours[dry & ~green]
    if PAINTINGS[key].finish != "sandstone":
        neutral = rock[np.ptp(rock,axis=1) < 32]
        if len(neutral)>20:
            rock = neutral
    if len(rock)<6:
        rock = colours
    rock = rock[np.argsort(rock@LUMA)]
    return rock[(np.array([.045,.26,.51,.72,.89,.98])*(len(rock)-1)).astype(int)]


def noise_at(lattice, points, frequency, offset=0):
    p = points*frequency+offset
    return map_coordinates(lattice,p.transpose(2,0,1),order=1,mode="grid-wrap",prefilter=False)


def paint_piece(rings, piece, neighbours, painting, ramp, lattice, tile, metres, seed):
    w,h = tile
    point, normal = sampling_grid(rings,w,h)
    x,y,z = point.transpose(2,0,1)
    kind = piece[-1]
    # Leave a readable hand-painted mark even on a pebble. Tall standing slabs
    # gain finer marks per metre rather than stretching a pebble's grain over them.
    p = point*np.clip(metres**.65,.72,2.8)
    broad = noise_at(lattice,p,4.6,seed)
    mid = noise_at(lattice,p,12.0,seed+7)
    grit = noise_at(lattice,p,35,seed+17)
    speck = noise_at(lattice,p,74,seed+23)
    strata = (p[...,1]*1.40+p[...,0]*.62+p[...,2]*.31)
    if painting.layout in ("rust-blade","moss-needle","fin-ridge","tilted-slab","needle-cluster"):
        strata = p[...,0]*1.48 - p[...,1]*.46 + p[...,2]*.89
    vein = np.abs(np.sin(strata*14+3.4*(broad-.5)+1.18*(mid-.5)+seed))
    tone = .52+.48*(broad-.5)+.32*(mid-.5)+.20*(grit-.5)+.12*normal[...,1]
    if painting.finish == "sandstone":
        pits = (.68*mid+.32*grit)>.646
        tone += .10*(vein<.38)-.33*pits+.19*(np.roll(pits,1,axis=0)&~pits)
    else:
        split = (vein < .19+.13*grit)&(mid>.28)
        tone += .09*(vein<.36)-.39*split+.12*((grit>.78)&(speck>.59))
    if kind == "egg":
        tone = .59+.14*normal[...,1]+.29*(broad-.5)+.13*(grit-.5)
        tone -= .42*((mid>.60)&(speck>.63))+.14*(grit>.73)
    if kind == "twig":
        ramp = np.array([(35,23,21),(54,34,27),(74,44,30),(95,57,34),(119,79,46),(148,100,62)])
    elif kind == "earth":
        ramp = np.array([(37,27,24),(49,36,29),(63,46,34),(85,62,42),(107,81,55),(126,97,64)])
    # A soft step between adjacent painted colours preserves broad planes,
    # with broken bright and dark flecks visible at arm's length.
    level = np.clip(tone*5+.54*(grit-.5)+.31*(speck-.5),0,4.999)
    lower = level.astype(int)
    blend = np.clip((level-lower-.23)/.54,0,1)[...,None]
    rgb = ramp[lower]*(1-blend)+ramp[lower+1]*blend
    if painting.layout == "sand-pillar":
        amber = np.array([148,103,54])*(.64+.65*mid[...,None])
        rgb += (.72*np.clip((y-.26)*2.8,0,1))[...,None]*(amber-rgb)
    if painting.layout == "vine-spire":
        rgb = rgb*.91+np.array([19,16,14])
    if kind == "egg":
        spots = noise_at(lattice,p,14.5,seed+8)
        rgb *= (1-.32*np.clip((spots-.66)*8,0,1))[...,None]
    if painting.finish == "ironstone":
        ochre = (broad+.25*mid>.71)&(vein<.77)
        rgb[ochre] = np.array([107,73,40])*(.68+.68*grit[ochre,None])
    if kind in ("plate","blade"):
        # Dry fractured stone catches slender pale marks beside the deep seams.
        stripe = (vein>.24)&(vein<.44)&(mid>.51)
        rgb[stripe] = .81*rgb[stripe]+.19*ramp[-1]
    # CraftSurvive surface-moss.wgsl: two scales of clumps, frayed edges,
    # damp stain outside the mat, and rock fractures visible through it.
    up = normal[...,1]
    wet = .68*broad+.32*mid+.12*(speck-.5)+.22*up-.065*normal[...,2]
    if painting.layout == "split-boulder":
        wet += painting.moss*(.14-.16*normal[...,0])
    if painting.layout == "vine-spire":
        wet += .11*noise_at(lattice,p*[1.8,.53,1.8],6.0,seed+33)
    if painting.moss or kind=="leaf":
        threshold = .94-.45*painting.moss
        stain = np.clip((wet-threshold+.14)*5,0,1)
        rgb *= 1-stain[...,None]*np.array([.27,.16,.34])
        moss = wet>threshold
        if kind=="leaf":
            moss = np.ones_like(moss)
        moss_ramp = np.array([(27,42,30),(42,57,36),(54,72,40),(70,86,46),(89,104,57),(111,121,69)])
        moss_tone = .47+.46*(mid-.5)+.61*(grit-.5)+.24*up-.19*(vein<.19)
        moss_colour = moss_ramp[np.clip((moss_tone*6).astype(int),0,5)]
        rgb[moss] = moss_colour[moss]
    if painting.snow and kind!="twig":
        drift = .60*up+.42*broad+.20*mid+.13*(grit-.5)
        threshold = .88-.57*painting.snow
        if painting.layout == "rust-blade":
            drift += .32*np.clip((y-.62)/.3,0,1)
        cover = drift>threshold
        icy = np.array([(124,123,139),(149,149,165),(172,173,187),(195,196,208),(217,216,226),(230,230,237)])
        light = .42+.30*up+.49*(mid-.5)+.27*(grit-.5)-.20*(drift<threshold+.04)
        rgb[cover] = icy[np.clip((light[cover]*6).astype(int),0,5)]
    # Contact darkens the foot of the formation and each bearing face.
    relative = (y-piece[2])/piece[4]
    shade = .81+.19*np.clip(relative/.35,0,1)
    shade *= .76+.24*np.clip((y+.03)/.25,0,1)
    if kind not in ("leaf","twig"):
        # Nearby stones shade the crevice where they meet. Estimate distance
        # to their slanted ellipsoids once here, entirely offline.
        contact = np.zeros_like(y)
        for other in neighbours:
            if other is piece or other[-1] in ("leaf","twig"):
                continue
            cx,cz,bottom,width,height,depth,lean,_ = other
            fraction = np.clip((y-bottom)/height,0,1)
            distance = ((x-cx-lean*fraction)/(.52*width))**2 + ((z-cz)/(.52*depth))**2
            distance += ((y-bottom-.48*height)/(.53*height))**2
            contact = np.maximum(contact,np.clip((1.48-np.sqrt(distance))/.5,0,1))
        shade *= 1-.34*contact
    return Image.fromarray(np.clip(rgb*shade[...,None],0,255).astype(np.uint8)).convert("RGBA")


def append_surface(part, positions, normals, uv):
    """Index corners shared by adjacent triangles; split only on a UV or normal seam."""
    flat = np.column_stack([positions.reshape(-1,3),normals.reshape(-1,3),uv.reshape(-1,2)]).round(5)
    data, indices = np.unique(flat,axis=0,return_inverse=True)
    first = len(part["positions"])//3
    part["indices"].extend((first+indices.ravel()).tolist())
    part["positions"].extend(data[:,:3].ravel().tolist())
    part["normals"].extend(data[:,3:6].ravel().tolist())
    part["uvs"].extend(data[:,6:].ravel().tolist())
    part["colors"].extend([1,1,1,0]*len(data))


def pack_wraps(pieces):
    """Allocate atlas space in proportion to girth and height. Use the same
    layout at all texture resolutions so a summer/winter pair can share UVs."""
    sizes = []
    for _, _, _, w, h, d, _, _ in pieces:
        sizes.append((max(3, math.ceil(7*math.pi*(w+d)/2)), max(3, math.ceil(7*(h+.42*(w+d))))))
    order = sorted(range(len(sizes)), key=lambda i: -sizes[i][1])
    best = None
    for width in range(max(w for w, _ in sizes), sum(w for w, _ in sizes)+1):
        x = y = row_height = 0
        boxes = {}
        for i in order:
            w,h = sizes[i]
            if x+w>width:
                y += row_height
                x = row_height = 0
            boxes[i] = (x,y,w,h)
            x += w
            row_height = max(row_height,h)
        height = y+row_height
        score = width*height + .04*(width-height)**2
        if best is None or score < best[0]:
            best = score, (width,height), boxes
    return best[1:]


def build(key, rgba, metres):
    painting = PAINTINGS[key]
    seed = int.from_bytes(hashlib.sha256(painting.layout.encode()).digest()[:4],"little")
    rng = np.random.default_rng(seed)
    pieces = LAYOUTS[painting.layout]
    # Each piece has a full wrap of its own, including its back. Size in the
    # world sets the atlas resolution; UVs stay identical across seasonal skins.
    unit = int(np.clip(round(8*metres**.48),7,17))
    (width,height), boxes = pack_wraps(pieces)
    atlas = Image.new("RGBA",(width*unit,height*unit))
    lattice = rng.random((32,32,32))
    ramp = ramp_for(key,rgba)
    parts = {}
    for i,piece in enumerate(pieces):
        rings = rings_of(piece,rng)
        kind = piece[-1]
        pos, normals, uv = surface(rings,kind)
        x,y,w,h = boxes[i]
        tile = paint_piece(rings,piece,pieces,painting,ramp,lattice,(w*unit,h*unit),metres,i*3.71)
        atlas.paste(tile,(x*unit,y*unit))
        uv = (np.array([x,y])+np.array([w,h])*(.01+.98*uv))/np.array([width,height])
        role = "bark" if kind in ("twig","leaf") else "stone"
        if role not in parts:
            parts[role] = dict(role=role,positions=[],normals=[],uvs=[],indices=[],colors=[])
        append_surface(parts[role],pos,normals,uv)
    height = max(max(p["positions"][1::3]) for p in parts.values())
    return dict(height=height,parts=list(parts.values())), atlas
