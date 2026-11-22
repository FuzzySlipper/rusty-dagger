"""Bare nature props. Follow narrow painted branches through their forks.
Build the bulky stumps, fallen timber, and prickly pear as solid forms."""
import math

import numpy as np
from PIL import Image
from scipy import ndimage

import fieldstones
from rainforest_palms import add_grid, colour_ramp, part
from rainforest_trees import Tree, filled
from mountain_woods import CarvedBark
from winter_mountain_woods import drift
from winter_oak import traced_limbs
from winter_woodland import add_bark_snow, snow_mask


FORMS = {
    "502-9":"antler-fork", "502-15":"buttressed-snag", "502-30":"crooked-swamp-oak",
    "503-1":"dead-prickly-pear", "503-6":"stone-cradle", "503-10":"low-desert-snag",
    "503-11":"desert-fork", "503-12":"lightning-snag", "503-28":"spreading-snag", "503-30":"bare-spire",
    "504-19":"split-stump", "504-20":"hollow-stump", "504-24":"silver-snag", "504-30":"bare-spire",
    "504-31":"fallen-limbs", "505-19":"snowy-split-stump", "505-20":"snowy-hollow-stump",
    "505-24":"snowy-silver-snag", "505-30":"snowy-bare-spire", "505-31":"snowy-fallen-limbs",
    "506-19":"split-stump", "506-20":"hollow-stump", "506-24":"silver-snag", "507-2":"snowy-upright-snag",
    "508-16":"witch-fingers", "508-17":"stone-cradle", "508-18":"mossy-fork", "508-31":"fallen-limbs",
    "509-9":"snowy-antler-fork", "509-16":"snowy-witch-fingers", "509-17":"snowy-stone-cradle",
    "509-18":"snowy-mossy-fork",
}
SHARED = {
    "508-9":"502-9", "508-30":"503-30", "511-2":"507-2", "510-31":"508-17", "511-31":"509-17",
    "508-25":"510-16", "509-25":"511-16", "509-30":"505-30", "509-31":"505-31",
    **{f"{a}-{r}": f"505-{r}" for a in (507,509,511) for r in (19,20,24)},
    **{f"{a}-{r}": f"506-{r}" for a in (508,510) for r in (19,20,24)},
}


def shoots(tree, bark, buds):
    """Sparse shoots on otherwise bare wood. Fold each blade along its midrib
    and pack the little touches of leaf colour into the existing bark draw."""
    rgb = tree.pixels[...,:3].astype(float)
    green = (tree.pixels[...,3]>128)&(rgb[...,1]>rgb[...,0]*1.015)&(rgb[...,1]>rgb[...,2]*1.1)
    if not green.any():
        return
    ramp = np.array(colour_ramp(rgb[green],[.09,.27,.49,.72,.93,.995]))
    y,x = np.indices((56,48))
    u,v = (x-23.5)/24,y/55
    tone = .47+.17*(u<0)+.20*(abs(u)<.085)-.18*abs(u)+.11*np.sin((v-abs(u)*.29)*24)
    slot = len(bark.tiles)
    bark.tiles.append(Image.fromarray(ramp[np.clip((6*tone).astype(int),0,5)].astype(np.uint8)))
    for i,(x,y,z,length) in enumerate(buds):
        base = tree.point(x,y,z)
        for j in range(4):
            az = i*2.4+j*1.66
            ray = np.array([math.cos(az),0,math.sin(az)])*length
            side = np.cross(ray,[0,1,0])
            rise = length*(.55+.33*math.cos(az+i))
            rows,uv = [],[]
            for t,w in ((0,.015),(.49,.22),(1,.015)):
                node = base+t*ray+[0,rise*t+.22*length*math.sin(t*math.pi),0]
                rows.append([node+side*w*k-[0,abs(k)*w*length*.32,0] for k in (1,0,-1)])
                uv.append([[u,slot+.02+.96*t] for u in (.02,.5,.98)])
            add_grid(tree.bark,np.array(rows),uv,double=True)


def solid_cap(tree, ring, direction, slot):
    start = len(tree.bark["uvs"])
    tree.cap(tree.bark, ring, np.array(direction, dtype=float))
    tree.bark["uvs"][start:] = [.50, slot+.96]*len(ring)


def cactus(rgba):
    tree = Tree(1, rgba, ((.58,1,0,.07),(.43,.58,.02,.16)))
    # The classic flat has two entire pads. The large one leans left, and its
    # thickness is greatest near the middle, leaving a narrow but solid edge.
    pads = ((.575,1.003,0,-.30,.99,.350,.120,.07),(.603,.925,.015,.205,.422,.177,.099,-.87))
    spines = []
    for i,(x,base,z,lean,height,width,depth,yaw) in enumerate(pads):
        a = np.linspace(0, math.tau, 21)
        rings, uv = [], []
        for t,r in ((0,.10),(.075,.30),(.23,.62),(.46,.92),(.69,1),(.85,.90),(.95,.62),(1,.20)):
            c = tree.point(x+t*lean,base-height*t,z+.033*math.sin(t*math.pi)+i*.095*t)
            ridge = 1+.045*np.cos(9*a+.55*t)+.018*np.sin(5*a-1.7*t)
            dx = tree.aspect*width*r*ridge*np.cos(a)
            dz = tree.aspect*depth*r*ridge*np.sin(a)
            rings.append(c+np.stack([dx*math.cos(yaw)-dz*math.sin(yaw),
                    .012*r*np.cos(3*a+i), dx*math.sin(yaw)+dz*math.cos(yaw)],axis=1))
            uv.append(np.stack([.005+.99*a/math.tau,np.full_like(a,.99-.98*t)],axis=1))
        for j in range(2,8):
            for k in range((2*j+i)%4,20,5):
                # Brittle spine pairs stand just proud of the puckered hide.
                start = rings[j][k]
                normal = start-rings[j][:-1].mean(0)
                normal /= np.linalg.norm(normal)
                spines.append((start,normal,i))
        add_grid(tree.bark,np.array(rings),uv,seam=True)
        solid_cap(tree,rings[-1],(0,1,0),0)
        solid_cap(tree,rings[0][::-1],(0,-1,0),0)
    # Dry olive hide, raised ribs, staggered brown areoles and bleached spine
    # stubs baked at sprite scale. No cutout or extra draw for the spines.
    v,u = np.indices((112,144))
    az = math.tau*u/144
    rng = tree.rng
    broad = ndimage.gaussian_filter(rng.normal(size=u.shape),3,mode="wrap")
    mottled = np.sin(6*az+.7*np.sin(v/24))*.10 + np.cos(11*az+.23*v/12)*.059
    tone = .55+.11*np.sin(az*2+.8)+mottled+1.24*broad+.065*rng.normal(size=u.shape)-.0017*v
    ramp = np.array([(42,38,26),(55,51,31),(69,66,35),(79,79,38),(90,91,44),(107,105,55),(124,116,67)])
    skin = ramp[np.clip((tone*7).astype(int),0,6)].astype(np.uint8)
    dry = ndimage.gaussian_filter(rng.normal(size=u.shape),5,mode="wrap")
    cork = (dry+.0019*v>.165) | ((broad>.105)&(v>27))
    skin[cork] = (skin[cork]*[.89,.69,.57]+[18,9,7]).astype(np.uint8)
    for row in range(9):
        for col in range(15):
            x0 = int((col+.5*(row%2))*144/15+rng.uniform(-2,2))%144
            y0 = int(6+row*12+rng.uniform(-3,3))
            skin[y0:y0+2,x0:x0+2] = (49,38,24)
            skin[y0-1,x0] = (135,116,71)
    tex = Image.new("RGBA",(148,112),(145,123,69,255))
    tex.paste(Image.fromarray(skin))
    uv = tree.bark["uvs"]
    uv[::2] = [x*144/148 for x in uv[::2]]
    for start,normal,i in spines:
        for length,tilt in ((.018,.014),(.012,-.010)):
            end = start+(normal*length+[0,tilt,0])*(1-.24*i)
            old = len(uv)
            tree.tube([start-normal*.003,end],[.0034,.0005],3,cap=True,wrap_height=1)
            uv[old:] = [.989,.5]*((len(uv)-old)//2)
    return finish(tree, {"bark":tex})


def finish(tree, images, extra=()):
    parts = [tree.bark,*extra]
    return {"height":max(max(p["positions"][1::3]) for p in parts), "parts":parts}, images


def painted_limbs(rgba, record, winter):
    tree = Tree(record,rgba,((.5,1,0,.05),(.5,.5,0,.04)))
    # The established winter oak tracer preserves fork junctions, taper and
    # the open spaces between twigs. Space the surviving boughs in depth.
    solid = tree.pixels[...,3]>100
    distance = ndimage.distance_transform_edt(np.pad(solid,1))[1:-1,1:-1]
    h,w = solid.shape
    row = round(.885*(h-1))
    x0 = distance[row].argmax()
    radius = max(1., distance[row,x0])
    # The painted soil, debris and dark shadows at the foot are not limbs.
    # Trace a continuous bole through that apron, then splay its roots in 3D.
    silhouette = tree.pixels.copy()
    for y in range(row+1,h):
        fraction = (y-row)/(h-1-row)
        silhouette[y,:,3] = 255*(abs(np.arange(w)-x0) <= radius*(1+.28*fraction))
    frost = snow_mask(tree.pixels) if winter else None
    dried = solid & ~frost if winter else solid
    bark = CarvedBark(tree,dried,frost)
    bark.wood[solid] = tree.pixels[solid]
    axis = None
    def draw(_, centres, radii, sides):
        nonlocal axis
        path = np.array(centres)
        if axis is None:
            # A bent leader can double back through a height. Follow its
            # broad lean, without pulling a child twig through those bends.
            levels = np.linspace(0,1,9)
            axis = np.array([[np.average(path[:,2],weights=np.exp(-((path[:,1]-y)/.13)**2)),y]
                             for y in levels])
        # Keep the principal shaft over its roots. Apply the same continuous
        # offset to every child at its attachment, including the skinny tips.
        path[:,2] -= .70*np.interp(path[:,1],axis[:,1],axis[:,0])
        bark.tube(path,radii,sides)
    traced_limbs(tree,draw=draw,spread_forks=True,silhouette=silhouette)
    base = tree.point(x0/(w-1),1,0)
    foot = np.nonzero(solid[row:].any(0))[0]
    spread = min(.30,max(.058,(foot[-1]-foot[0])/(w-1)*.42))*tree.aspect
    for i in range(6):
        a = (i+tree.rng.uniform(-.20,.20))*math.tau/6
        ray = np.array([math.cos(a),0,.83*math.sin(a)])
        reach = spread*tree.rng.uniform(.66,1.10)
        path = [base+[0,.101,0],base+reach*.29*ray+[0,.052,0],
                base+reach*.58*ray+[0,.019,0],base+reach*ray-[0,.003,0]]
        bark.tube(path,np.array([.72,.83,.46,.055])*radius/w,5)
    if not winter and record in (11,18):
        shoots(tree,bark,((tree.foot_x-.05,.965,.03,.031),(tree.foot_x+.045,.948,.02,.042),
                          (tree.foot_x+.102,.988,-.025,.035)))
    return finish(tree,{"bark":bark.atlas()})


# Centre-line points in the cropped painting: x, down, depth, radius.
SPLIT = ((.453,1.006,0,.197),(.447,.913,.015,.204),(.443,.822,.025,.168),
         (.453,.681,.045,.143),(.482,.555,.020,.155),(.524,.443,-.016,.160),
         (.582,.337,-.043,.155),(.642,.252,-.059,.139),(.689,.178,-.050,.113),(.727,.097,-.034,.089))
HOLLOW = ((.447,1.006,0,.226),(.453,.908,.013,.195),(.440,.801,.022,.165),
          (.416,.673,.028,.147),(.400,.548,.017,.155),(.393,.425,-.015,.167),
          (.386,.331,-.028,.161),(.382,.243,-.016,.141))
SPLIT_ARMS = (
    ((.470,.549,.016,.104),(.405,.445,.081,.084),(.350,.372,.113,.052),(.310,.277,.124,.037),(.251,.220,.135,.013)),
    ((.568,.349,-.017,.098),(.493,.280,-.111,.068),(.457,.192,-.148,.046),(.407,.144,-.169,.031),(.363,.055,-.153,.009)),
    ((.650,.256,-.026,.081),(.738,.209,.054,.070),(.808,.147,.098,.038),(.898,.119,.109,.021),(.971,.026,.116,.005)),
    ((.459,.738,.011,.075),(.342,.712,-.040,.045),(.276,.666,-.089,.030),(.245,.593,-.107,.008)),
    ((.651,.253,-.055,.080),(.668,.195,-.162,.057),(.636,.130,-.205,.028),(.645,.071,-.223,.010)),
)
HOLLOW_ARMS = (
    ((.402,.446,.002,.114),(.302,.393,.056,.083),(.251,.359,.107,.057),
     (.168,.388,.145,.040),(.091,.379,.157,.031),(.055,.318,.142,.018),(.005,.289,.132,.006)),
    ((.412,.511,.006,.111),(.515,.452,-.033,.093),(.602,.385,-.074,.067),(.665,.360,-.103,.046),
     (.746,.368,-.107,.039),(.804,.328,-.104,.029),(.857,.255,-.088,.017),(.960,.245,-.067,.004)),
    ((.432,.387,-.022,.107),(.484,.292,-.106,.082),(.507,.213,-.155,.054),
     (.557,.155,-.173,.040),(.562,.092,-.166,.022),(.600,.026,-.153,.006)),
    ((.347,.334,.013,.084),(.296,.281,.099,.067),(.275,.224,.151,.044),(.225,.164,.179,.009)),
    ((.423,.368,.004,.073),(.465,.292,.121,.058),(.476,.226,.162,.040),(.508,.201,.196,.027)),
    ((.616,.381,-.082,.039),(.662,.318,-.149,.024),(.687,.286,-.190,.012),(.737,.263,-.205,.004)),
)


def limb(tree, bark, trace, sides=7):
    points = [tree.point(x,y,z) for x,y,z,_ in trace]
    radii = [r for _,_,_,r in trace]
    bark.tube(points,radii,sides)
    return points,radii


def trunk(tree, bark, stem):
    # A recessed broken end, with splinters standing at different heights
    # around its rim. Deep flutes merge with the roots at the bottom.
    a = np.linspace(0,math.tau,19)
    rings, centres = [], []
    for x,y,z,r in stem:
        centre = tree.point(x,y,z)
        twist = 1.85*(1-y)
        edge = 1+.115*np.cos(5*a+twist)+.065*np.sin(8*a+twist)
        jag = np.clip(1-(y-stem[-1][1])/.14,0,1)*(.049*np.sin(4*a)+.024*np.sin(8*a+.43))
        rings.append(centre+np.stack([r*tree.aspect*edge*np.cos(a),jag,.88*r*tree.aspect*edge*np.sin(a)],axis=1))
        centres.append(centre)
    rings = np.array(rings)
    add_grid(tree.bark,rings,np.zeros((*rings.shape[:2],2)),seam=True)
    slot = bark.map_rings(rings,np.array(centres),0)
    inner = centres[-1]+(rings[-1]-centres[-1])*.54-[0,.048,0]
    start = len(tree.bark["uvs"])
    tree.painted_grid(tree.bark,np.array([rings[-1],inner]),np.array([centres[-1],centres[-1]]))
    solid_cap(tree,inner,(0,1,0),slot)
    tree.bark["uvs"][start:] = [.48,.95]*((len(tree.bark["uvs"])-start)//2)
    tree.bark["colors"][start*2:] = [.49,.41,.34,0]*((len(tree.bark["uvs"])-start)//2)


def stump(rgba, record, winter):
    split = record==19
    stem = SPLIT if split else HOLLOW
    tree = Tree(record,rgba,stem)
    dry = (tree.pixels[...,3]>128) & ~snow_mask(tree.pixels)
    bark = CarvedBark(tree,dry)
    snow = part("bark")
    trunk(tree,bark,stem)
    for i,trace in enumerate(SPLIT_ARMS if split else HOLLOW_ARMS):
        points,radii = limb(tree,bark,trace)
        if winter and i in (0,1,2):
            snow_ridge(tree,points[1:],radii[1:],snow)
    for i in range(8):
        a = i*math.tau/8+.21
        ray = np.array([math.cos(a),0,.86*math.sin(a)])*tree.aspect
        reach = tree.rng.uniform(.33,.51)
        path = [tree.stem_at(.73),tree.stem_at(.848)+ray*reach*.32,tree.stem_at(.929)+ray*reach*.61,
                tree.stem_at(.974)+ray*reach*.84,tree.stem_at(1.002)+ray*reach]
        radii = [.102,.105,.079,.042,.008]
        bark.tube(path,radii,7)
        # Root tips branch and turn along the ground, past the flared bole.
        start = path[-3]
        tangent = np.cross(ray,[0,1,0])
        bark.tube([start,path[-2]+tangent*.040,path[-1]+tangent*.111+ray*.051],
                  [.044,.026,.004],5)
        if winter and i in (0,3,5):
            snow_ridge(tree,path[2:],radii[2:],snow,.88)
    for i in range(5):
        path, radii = [], []
        for t in np.linspace(0,1,10):
            down = .95-t*(.66 if split else .52)
            a = i*math.tau/5+.36+1.7*t
            radius = np.interp(down,tree.stem[::-1,1],tree.stem[::-1,3])
            edge = 1+.115*math.cos(5*a+1.85*(1-down))+.065*math.sin(8*a+1.85*(1-down))
            path.append(tree.stem_at(down)+.94*tree.aspect*radius*edge*np.array([math.cos(a),0,.88*math.sin(a)]))
            radii.append(.004+.024*math.sin(math.pi*t)**.68)
        bark.tube(path,radii,5)
    limbs = SPLIT_ARMS if split else HOLLOW_ARMS
    for index,sign in ((0,-1),(1,1),(2,1)):
        x,y,z,r = limbs[index][-3]
        limb(tree,bark,((x,y,z,r*.71),(x+.054*sign,y-.048,z+.05,.017),
                        (x+.071*sign,y-.095,z+.06,.010),(x+.109*sign,y-.115,z+.046,.003)),5)
    if winter:
        x,y,z,r = stem[-1]
        drift(tree,snow,x,y-.024,z,r*1.40,r*1.19,.058,.017)
        for trace in (SPLIT_ARMS if split else HOLLOW_ARMS)[:3]:
            x,y,z,r = trace[-2]
            drift(tree,snow,x,y-r*.63,z,r*1.23,r*1.15,r*.73,.009)
    return finish(tree,{"bark":add_bark_snow(tree,bark.atlas(),snow)})


# Interlocking windfall. The two near limbs present fractured ends; the long
# beam and the splayed branches behind them keep their depth in a side view.
LOGS = (
    ((.156,.559,-.11,.030),(.243,.505,-.075,.098),(.377,.518,-.042,.130),
     (.513,.576,.012,.133),(.633,.635,.065,.142),(.742,.758,.150,.133),(.839,.852,.245,.112)),
    ((.342,.904,.247,.105),(.386,.778,.150,.111),(.476,.621,.079,.112),(.567,.516,.010,.116),
     (.636,.411,-.045,.097),(.665,.311,-.103,.082),(.692,.273,-.126,.088),(.708,.229,-.137,.067)),
    ((.423,.542,-.045,.088),(.347,.437,-.115,.074),(.302,.316,-.153,.056),(.280,.251,-.148,.036),(.249,.150,-.162,.010)),
    ((.363,.514,-.052,.096),(.257,.461,.038,.064),(.193,.410,.091,.050),(.131,.372,.126,.032),
     (.089,.309,.141,.019),(.022,.266,.146,.005)),
    ((.257,.482,.022,.063),(.184,.527,.081,.045),(.131,.541,.104,.034),(.094,.589,.125,.022),(.026,.637,.129,.005)),
    ((.607,.435,-.047,.079),(.739,.454,-.079,.068),(.800,.492,-.113,.046),(.886,.495,-.126,.029),(.953,.546,-.115,.006)),
    ((.748,.449,-.085,.048),(.818,.374,-.091,.037),(.854,.300,-.092,.020),(.902,.274,-.082,.007)),
    ((.646,.320,-.101,.059),(.611,.228,-.167,.043),(.624,.159,-.204,.029),(.609,.071,-.224,.008),(.631,.039,-.215,.003)),
    ((.686,.296,-.114,.037),(.743,.242,-.139,.026),(.778,.159,-.150,.017),(.781,.108,-.142,.013),
     (.802,.073,-.133,.009),(.792,.034,-.130,.006),(.777,.009,-.124,.002)),
    ((.366,.504,-.045,.073),(.408,.418,-.075,.049),(.416,.369,-.117,.036),
     (.474,.322,-.144,.027),(.509,.286,-.172,.021),(.547,.211,-.184,.014),
     (.595,.180,-.195,.009),(.631,.163,-.192,.003)),
)


def snow_ridge(tree, points, radii, snow, width=1.05, extent=(.055,.95)):
    """A connected drift follows the upper surface through bends in the wood."""
    path = np.asarray(points)
    lengths = np.r_[0, np.cumsum(np.linalg.norm(np.diff(path,axis=0),axis=1))]
    lengths /= lengths[-1]
    t = np.linspace(*extent,max(6,len(path)+2))
    centres = np.array([np.interp(t,lengths,path[:,axis]) for axis in range(3)]).T
    radii = tree.aspect*np.interp(t,lengths,radii)
    forward = np.gradient(centres,axis=0)
    forward /= np.linalg.norm(forward,axis=1,keepdims=True)
    up = np.array([0.,1.,0.])-forward*forward[:,1,None]
    up /= np.maximum(np.linalg.norm(up,axis=1,keepdims=True),1e-8)
    side = np.cross(up,forward)
    rows,uv = [],[]
    k = np.linspace(-1,1,5)
    phase = tree.rng.uniform(0,math.tau)
    for i,u in enumerate(np.linspace(0,1,len(t))):
        spread = width*(.32+.76*math.sin(math.pi*u)**.48)
        phi = spread*k+.13*np.sin(6*u+phase)+.085*np.sin(13*u+k*2)
        shell = np.outer(np.cos(phi),up[i])+np.outer(np.sin(phi),side[i])
        relief = .99+.18*(1-k*k)*math.sin(math.pi*u)**.45
        rows.append(centres[i]+radii[i]*shell*relief[:,None])
        uv.append(np.stack([.5+.45*k,np.full_like(k,.04+.92*u)],axis=1))
    add_grid(snow,np.array(rows),uv)


def end_grain(bark, tree, ring, direction):
    """Place split heartwood inside the bark rim on the broken log end."""
    y,x = np.indices((56,48))
    u,v = (x-23.5)/24,(y-27.5)/28
    angle = np.arctan2(v,u)
    radius = np.hypot(u+.10,v-.08)+.042*np.sin(3*angle)+.025*np.cos(5*angle)
    rings = .5+.5*np.sin(radius*38+1.2*np.sin(2*angle))
    shade = .43+.14*rings+.11*np.sin(angle*2)+.11*radius
    shade -= .34*((np.sin(4*angle+1.5*radius)>.90)&(radius>.44))
    ramp = bark.ramp[np.array([1,2,3,4,5,6,7])]
    pixels = ramp[np.clip(np.rint(6*shade).astype(int),0,6)].copy()
    pixels[...,:3] = np.clip(pixels[...,:3]*.75+[31,23,12],0,255)
    slot = len(bark.tiles)
    bark.tiles.append(Image.fromarray(pixels.astype(np.uint8)))
    start = len(tree.bark["uvs"])
    tree.cap(tree.bark,ring,direction)
    a = np.linspace(0,math.tau,len(ring))[:-1]
    uv = np.vstack([[.50,slot+.50],np.stack([.5+.47*np.cos(a),slot+.5+.47*np.sin(a)],axis=1)])
    tree.bark["uvs"][start:] = uv.ravel().tolist()


def timber(tree,bark,trace,sides):
    path = np.array([tree.point(x,y,z) for x,y,z,_ in trace])
    radii = np.array([r for _,_,_,r in trace])*.72
    directions = np.gradient(path,axis=0)
    directions /= np.linalg.norm(directions,axis=1,keepdims=True)
    axis = np.eye(3)[np.argmin(abs(directions[0]))]
    a = np.linspace(0,math.tau,sides+1)
    phase = tree.rng.uniform(0,math.tau)
    rings = []
    for i,(centre,forward,radius) in enumerate(zip(path,directions,radii)):
        t = i/(len(path)-1)
        axis -= forward*np.dot(axis,forward)
        axis /= np.linalg.norm(axis)
        radial = np.outer(np.cos(a),axis)+np.outer(np.sin(a),np.cross(axis,forward))
        grain = 1+.115*np.cos(5*a+phase+1.4*t)+.068*np.sin(3*a-phase-1.7*t)
        broken = (abs(2*t-1)**6)*(.15*np.sin(3*a+phase)+.11*np.cos(7*a))
        rings.append(centre+tree.aspect*radius*(grain[:,None]*radial+np.outer(broken,forward)))
    rings = np.array(rings)
    start = len(tree.bark["uvs"])
    add_grid(tree.bark,rings,np.zeros((*rings.shape[:2],2)),seam=True)
    slot = bark.map_rings(rings,path,start)
    # Follow the axis of this log with long splits and eroded ridges. Source
    # colours sit behind the ridges even when the limb faces away from camera.
    tile = np.array(bark.tiles[slot])
    yy,xx = np.indices(tile.shape[:2])
    ang = math.tau*xx/(tile.shape[1]-1)
    run = yy/(tile.shape[0]-1)
    flow = ang+.27*np.sin(4*run+phase)+.12*np.sin(9*run+2*ang)+.38*run
    vein = np.sin(8*flow+phase)+.28*np.sin(3*ang+8*run)
    tone = .52+.20*np.cos(4*flow)+.13*np.cos(12*flow)-.34*(vein>.92)+.16*((vein>.66)&(vein<.87))
    grain = bark.ramp[np.clip((tone*8).astype(int),0,7)]
    tile[...,:3] = np.rint(.32*tile[...,:3]+.68*grain[...,:3])
    bark.tiles[slot] = Image.fromarray(tile)
    if sides>=12:
        # Raised peeling lengths break up the large faces beside the split end.
        for rib in (1,4,7,10):
            rows,uv = [],[]
            for j in range(1,len(path)):
                index = rib+(.52*math.sin(j*.74+rib))
                width = .63 if j>1 else .18
                row,coords = [],[]
                for k in (-1,0,1):
                    q = (index+k*width)%sides
                    lo = int(q)
                    skin = rings[j,lo]+(rings[j,lo+1]-rings[j,lo])*(q-lo)
                    row.append(path[j]+(skin-path[j])*(1.12-.125*abs(k)))
                    coords.append([.005+.99*q/sides,slot+.005+.99*j/(len(path)-1)])
                rows.append(row)
                uv.append(coords)
            add_grid(tree.bark,np.array(rows),uv)
    end_grain(bark,tree,rings[0][::-1],-directions[0])
    end_grain(bark,tree,rings[-1],directions[-1])
    return path,radii


def windfall(rgba, winter):
    tree = Tree(31,rgba,((.50,1,0,.08),(.5,.5,0,.06)))
    bark = CarvedBark(tree,(tree.pixels[...,3]>128)&~snow_mask(tree.pixels))
    powder = part("bark")
    for i,trace in enumerate(LOGS):
        path,radii = timber(tree,bark,trace,14 if i<2 else 8)
        if winter and i not in (4,7):
            extent = (.12,.63) if i == 0 else (.31,.99) if i == 1 else (.14,.85)
            snow_ridge(tree,path,radii,powder,.94 if i<2 else .85,extent)
            if i == 0:
                snow_ridge(tree,path,radii,powder,.79,(.72,.93))
    # Broken side splinters and two supporting lengths under the crossed arms.
    for trace in (
        ((.612,.680,.072,.081),(.552,.786,-.020,.082),(.474,.897,-.117,.070),(.418,.955,-.161,.040)),
        ((.324,.552,-.063,.075),(.283,.690,-.121,.065),(.275,.824,-.169,.048),(.225,.933,-.174,.027)),
        ((.377,.501,-.052,.058),(.353,.421,.040,.037),(.368,.344,.067,.012)),
        ((.681,.635,.065,.075),(.734,.591,.139,.049),(.774,.556,.151,.019)),
    ):
        p,r = timber(tree,bark,trace,7)
        if winter:
            snow_ridge(tree,p,r,powder,.87)
    # The far branches narrow into crooked twigs beyond the heavy timber.
    for trace in (
        ((.160,.396,.108,.032),(.118,.386,.127,.022),(.083,.358,.135,.014),(.048,.372,.135,.004)),
        ((.304,.333,-.151,.035),(.257,.294,-.178,.023),(.242,.222,-.181,.014),(.207,.180,-.193,.004)),
        ((.790,.480,-.105,.040),(.837,.431,-.055,.030),(.872,.427,-.035,.021),(.908,.378,-.021,.005)),
        ((.693,.277,-.11,.038),(.746,.244,-.18,.022),(.779,.170,-.207,.012),(.809,.149,-.19,.003))):
        limb(tree,bark,trace,5)
    if winter:
        for pad in ((.342,.969,.205,.172,.125,.047,.009),(.239,.976,.093,.138,.107,.037,.009),
                    (.425,.982,.208,.114,.080,.031,-.011),(.732,.978,.138,.104,.082,.030,-.008)):
            drift(tree,powder,*pad)
    return finish(tree,{"bark":add_bark_snow(tree,bark.atlas(),powder)})


# Two crooked stems embrace a low heap of greystone, with roots crossing the
# visible seams. A little winter snow sits on the same stones and twig tips.
CRADLE = (
    ((.721,1.0,.042,.038),(.646,.905,.070,.058),(.618,.808,.054,.055),(.620,.715,.018,.043),
     (.660,.615,-.016,.041),(.675,.514,-.028,.037),(.671,.413,-.025,.031),(.643,.323,-.012,.028),
     (.626,.231,.008,.024),(.662,.180,.022,.021),(.699,.101,.034,.014),(.697,.040,.037,.005)),
    ((.657,.643,-.014,.040),(.529,.683,.025,.039),(.420,.710,.069,.033),
     (.308,.725,.078,.031),(.210,.696,.069,.030),(.187,.619,.059,.023),(.141,.563,.053,.018),(.103,.546,.057,.007)),
    ((.192,.632,.06,.022),(.202,.558,.087,.019),(.245,.522,.105,.014),(.292,.486,.101,.012),(.315,.424,.087,.005)),
    ((.675,.485,-.026,.032),(.731,.446,.007,.027),(.791,.439,.045,.023),(.828,.407,.061,.019),
     (.841,.334,.068,.014),(.830,.260,.062,.005)),
    ((.639,.305,-.009,.022),(.578,.267,-.062,.018),(.562,.220,-.081,.011),(.523,.193,-.089,.004)),
    ((.636,.708,0,.036),(.724,.658,.091,.029),(.787,.623,.117,.022),(.883,.618,.137,.013),(.948,.562,.139,.004)),
    ((.608,.801,.053,.040),(.525,.832,.143,.030),(.446,.918,.222,.026),(.357,.954,.248,.008)),
    ((.634,.866,.060,.043),(.694,.904,.152,.030),(.747,.967,.203,.022),(.848,.981,.191,.006)),
    ((.633,.845,.025,.039),(.602,.887,-.089,.031),(.524,.925,-.164,.027),(.470,.989,-.196,.006)),
)


def stone_cradle(rgba, record, winter):
    tree = Tree(record,rgba,CRADLE[0])
    # Center the whole assembly on the record's foot, between stone and wood.
    tree.foot_x = .50
    rgb = tree.pixels[...,:3].astype(float)
    solid = tree.pixels[...,3]>128
    snow = snow_mask(tree.pixels)
    warm = solid & (rgb[...,0]>rgb[...,2]*1.13) & ~snow
    bark = CarvedBark(tree,warm)
    strips = part("bark")
    for i,trace in enumerate(CRADLE):
        p,r = limb(tree,bark,trace,7 if i<2 else 5)
        if winter and i in (0,2,3,4,5):
            snow_ridge(tree,p[1:],r[1:],strips,.86)
            x,y,z,rad = trace[-2]
            drift(tree,strips,x,y,z,rad*1.5,rad*1.25,.016,.006)
    # Coordinates below are x, z, base, spans and lean, in units of height.
    aspect = tree.aspect
    stones = [fieldstones.stone((x-.5)*aspect,z*aspect,base,w*aspect,h,d*aspect,lean*aspect,"slab")
              for x,z,base,w,h,d,lean in (
                  (.36,-.065,-.024,.52,.314,.48,.065),(.42,-.084,.238,.328,.205,.36,-.04),
                  (.177,.139,-.022,.319,.172,.235,-.017),(.426,.186,-.021,.294,.187,.253,.035),
                  (.589,.043,-.024,.228,.286,.265,-.018),(.722,-.119,-.018,.190,.164,.200,.033))]
    if record == 6:
        shoots(tree,bark,((.523,.193,-.089,.049),(.626,.230,.008,.043),(.830,.26,.062,.042),
                          (.783,.625,.118,.036),(.140,.568,.052,.040),(.845,.979,.165,.067),
                          (.357,.954,.248,.044),(.592,.908,.065,.036)))
    grey = solid & ~snow & (rgb[...,2]>=rgb[...,0]*.97) & (rgb[...,0]>25)
    ramp = np.array(colour_ramp(rgb[grey],[.05,.25,.48,.66,.84,.98]))[:,:3]
    mesh, atlas = fieldstones.assembly(fieldstones.Painting("stone-cradle",moss=.15,snow=.82 if winter else 0),
                                        stones,ramp,1.5)
    return finish(tree,{"bark":add_bark_snow(tree,bark.atlas(),strips),"stone":atlas},mesh["parts"])


def branched_snag(rgba, swamp):
    # A high flaring pollard in the swamp; a low, radially branched snag in
    # the desert. Both have short forks at the ends of their living-size wood.
    if swamp:
        stem = ((.502,1.002,0,.083),(.503,.875,.008,.113),(.487,.728,.013,.106),
                (.498,.588,.003,.093),(.505,.431,-.010,.102),(.511,.306,-.014,.098),(.534,.256,.001,.056))
        branches = (
            ((.499,.345,-.012,.090),(.404,.296,.028,.070),(.312,.252,.075,.047),(.217,.230,.104,.035),(.123,.235,.116,.016),(.034,.191,.115,.004)),
            ((.515,.317,-.012,.086),(.599,.267,-.049,.073),(.659,.184,-.092,.053),(.640,.121,-.108,.031),(.680,.068,-.109,.013),(.671,.017,-.104,.004)),
            ((.500,.330,0,.068),(.454,.237,-.13,.047),(.412,.164,-.194,.031),(.393,.095,-.222,.015),(.348,.049,-.231,.004)),
            ((.597,.261,-.048,.059),(.709,.271,.042,.047),(.787,.236,.096,.029),(.854,.183,.114,.018),(.931,.164,.108,.004)),
            ((.437,.295,.030,.043),(.366,.246,.151,.036),(.324,.182,.195,.021),(.267,.151,.216,.006)))
    else:
        stem = ((.456,1.003,0,.102),(.455,.850,.007,.097),(.472,.706,.012,.089),
                (.491,.563,-.007,.075),(.526,.444,-.021,.057),(.553,.337,-.014,.041),
                (.558,.224,.004,.027),(.598,.131,.026,.010))
        branches = (
            ((.476,.722,.026,.080),(.309,.622,.076,.058),(.228,.542,.117,.047),(.186,.401,.127,.027),(.107,.345,.114,.013),(.084,.237,.117,.004)),
            ((.482,.598,.007,.071),(.425,.447,-.081,.051),(.349,.331,-.134,.034),(.319,.210,-.143,.021),(.241,.147,-.143,.005)),
            ((.482,.644,.012,.074),(.607,.601,.087,.056),(.669,.510,.128,.045),(.756,.423,.136,.033),(.826,.347,.124,.016),(.831,.259,.126,.005)),
            ((.499,.543,-.004,.054),(.583,.464,-.093,.043),(.686,.354,-.167,.029),(.697,.206,-.200,.015),(.755,.091,-.214,.005)),
            ((.462,.758,.009,.064),(.355,.765,-.115,.046),(.248,.713,-.193,.030),(.134,.730,-.218,.016),(.047,.669,-.232,.004)),
            ((.466,.736,.020,.062),(.583,.745,.087,.048),(.718,.709,.157,.033),(.819,.645,.181,.020),(.928,.603,.192,.006)),
            ((.535,.404,-.013,.041),(.453,.315,.107,.034),(.423,.230,.162,.019),(.444,.128,.194,.010),(.401,.061,.204,.004)))
    tree = Tree(15 if swamp else 10,rgba,stem)
    rgb = tree.pixels[...,:3].astype(float)
    wood = (tree.pixels[...,3]>128)&(rgb[...,0]>=rgb[...,1]*.95)
    bark = CarvedBark(tree,wood)
    trunk(tree,bark,stem)
    for i,trace in enumerate(branches):
        if not swamp:
            trace = tuple((x,y,z,r*.77) for x,y,z,r in trace)
        limb(tree,bark,trace)
        for j in (2,3):
            x,y,z,r = trace[j]
            sign = 1 if i%2 else -1
            reach = .065 if swamp else .103
            tip = ((x,y,z,r*.70),(x+sign*reach*.4,y-.048,z+.039,r*.44),
                   (x+sign*reach,y-.087,z+.052,r*.24),
                   (x+sign*reach*.91,y-.135,z+.059,.003))
            limb(tree,bark,tip,5)
        x,y,z,r = trace[-2]
        limb(tree,bark,((x,y,z,r*.83),(x-.046*sign,y-.040,z-.035,.012),
                        (x-.062*sign,y-.080,z-.061,.006),(x-.10*sign,y-.100,z-.070,.0025)),5)
    for i in range(9):
        a = i*math.tau/9+.29
        ray = np.array([math.cos(a),0,.83*math.sin(a)])*tree.aspect
        tangent = np.cross(ray,[0,1,0])
        reach = tree.rng.uniform(.28,.55)
        start = tree.stem_at(.59 if swamp else .74)
        base = tree.stem_at(.996)
        elbow = tree.rng.uniform(.18,.26) if swamp else tree.rng.uniform(.08,.137)
        path = [start, base+ray*reach*.34+[0,elbow,0], base+ray*reach*.65+tangent*.06+[0,elbow*.69,0],
                base+ray*reach*.73+[0,.037,0],base+ray*reach+tangent*.06-[0,.006,0]]
        size = 1 if swamp else .66
        bark.tube(path,np.array([.075,.074,.049,.034,.006])*size,7)
    if not swamp:
        shoots(tree,bark,((.428,.856,.079,.068),(.469,.786,.069,.046),(.759,.975,.083,.080),
                          (.831,.985,.012,.092),(.145,.985,-.056,.050),(.329,.966,.091,.067)))
    return finish(tree,{"bark":bark.atlas()})


def antler_fork(rgba, winter):
    stem = ((.386,1.002,0,.062),(.371,.780,.012,.057),(.388,.625,.032,.045),
            (.360,.547,.020,.061),(.328,.497,.011,.036),(.312,.412,.017,.026),
            (.276,.352,.034,.031),(.226,.272,.040,.033),(.190,.181,.025,.023),
            (.114,.132,.009,.016),(.077,.066,-.008,.012),(.029,.019,-.017,.004))
    tree = Tree(9,rgba,stem)
    bark = CarvedBark(tree,(tree.pixels[...,3]>128)&~snow_mask(tree.pixels))
    snow = part("bark")
    branches = (
        stem,
        ((.366,.569,.021,.056),(.462,.539,.065,.042),(.494,.451,.098,.034),
         (.574,.402,.102,.043),(.601,.330,.087,.041),(.639,.237,.055,.037),
         (.710,.184,.026,.031),(.806,.134,.010,.027),(.855,.083,-.003,.014),(.973,.012,-.014,.004)),
        ((.587,.364,.090,.041),(.671,.365,.011,.036),(.739,.395,-.062,.027),
         (.786,.436,-.103,.023),(.764,.495,-.123,.004)),
        ((.288,.383,.038,.035),(.221,.405,.102,.027),(.182,.451,.139,.023),(.184,.495,.170,.010)),
        ((.373,.551,.025,.042),(.328,.501,-.080,.029),(.321,.431,-.142,.019),(.397,.373,-.174,.013)),
        ((.806,.136,.011,.024),(.884,.157,.072,.016),(.960,.136,.094,.004)),
        ((.379,.936,.004,.020),(.443,.917,.036,.016),(.476,.874,.045,.007)))
    for i,trace in enumerate(branches):
        pts,rs = limb(tree,bark,trace)
        if winter and i in (1,2,3,5):
            snow_ridge(tree,pts,rs,snow)
    # Broken bark, crossed twigs and a knot caught in the open fork. Keep the
    # opening below it visible from the front, with crossed wood in depth.
    for i in range(6):
        offset = (i%3-1)*.026
        z = (i//3-.5)*.115
        limb(tree,bark,((.232,.397+offset,z+.040,.019),(.312,.363+offset,z,.043),
                        (.397,.356+offset,z+.025,.060),(.486,.370+offset,z+.066,.044),
                        (.575,.368+offset,z+.074,.021)),6)
    if not winter:
        shoots(tree,bark,((.275,.354,.035,.032),(.245,.397,.043,.027),(.302,.350,-.018,.019)))
    if winter:
        for x,y,z,w,d,h in ((.370,.326,.017,.106,.079,.030),(.476,.328,.040,.093,.074,.039),
                            (.836,.109,.012,.065,.035,.021)):
            drift(tree,snow,x,y,z,w,d,h,.009)
    return finish(tree,{"bark":add_bark_snow(tree,bark.atlas(),snow)})


def build(key, rgba):
    archive,record = map(int,key.split("-"))
    winter = archive in (505,507,509,511)
    if record==9:
        return antler_fork(rgba,winter)
    if key in ("502-15","503-10"):
        return branched_snag(rgba,key=="502-15")
    if key=="503-1":
        return cactus(rgba)
    if FORMS[key].endswith("stone-cradle"):
        return stone_cradle(rgba,record,winter)
    if record in (19,20):
        return stump(rgba,record,winter)
    if record==31:
        return windfall(rgba,winter)
    return painted_limbs(rgba,record,winter)
