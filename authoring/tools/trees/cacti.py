"""Living desert cacti. Stations follow the refreshed paintings in cropped
sprite pixels (x, down, depth, radius). Each bent arm carries its ribs and
areoles continuously around the elbow to its rounded tip."""
import math

import numpy as np
from PIL import Image
from scipy import ndimage

from rainforest_palms import add_grid, colour_ramp, part
from rainforest_trees import filled


FORMS = {14: "slender-flowering-cactus", 15: "cork-based-cactus", 16: "tiered-flowering-cactus"}

STEMS = {
    14: (
        ((50,307,0,10),(50,290,0,10),(51,259,1,9),(51,218,2,8.5),(51,173,2,8),
         (53,134,0,8),(54,100,-1,7),(55,78,-2,6.4),(55,70,-2,4.6),(55,66,-2,1.6)),
        ((52,137,0,6.9),(43,132,-4,6.2),(33,127,-10,6.0),(27,119,-15,6.0),(26,109,-17,6.3),
         (26,63,-17,6.1),(26,26,-16,5.7),(26,12,-16,4.9),(26,5,-16,2.8),(26,3,-16,1.2)),
        ((50,272,1,7),(62,271,5,6.5),(73,266,12,6.0),(81,257,18,6.0),(83,245,21,6.1),
         (84,207,21,5.8),(84,180,20,5.1),(84,170,20,4.0),(84,166,20,1.2)),
        ((50,221,4,5),(42,220,9,4.6),(36,216,15,4.4),(33,209,19,4.4),(33,190,20,4.0),
         (32,180,20,3.3),(32,176,20,1.0)),
    ),
    15: (
        ((50,245,0,14.5),(50,228,0,13.5),(49,199,2,11.5),(49,165,3,10.5),(50,135,2,9.8),
         (50,102,0,9),(51,65,-2,8.7),(52,31,-3,8),(52,14,-3,6.8),(53,7,-3,4.0),(53,4,-3,1.7)),
        ((50,172,2,8),(63,173,6,7.3),(78,171,12,6.6),(91,166,17,5.8),(100,157,22,5.1),
         (106,145,25,4.8),(109,129,26,4.5),(109,121,26,3.4),(109,118,26,1.4)),
        ((105,146,24,3.2),(116,143,29,2.9),(123,137,32,2.2),(127,131,34,1)),
        ((49,147,1,7.9),(38,146,-4,7.2),(26,141,-12,6.7),(17,135,-18,6),(12,127,-22,5.8),
         (9,119,-24,5.2),(9,113,-24,3.8),(10,111,-24,1.6)),
        ((50,102,0,6.8),(61,101,5,6.2),(71,96,12,6.1),(78,88,18,5.8),(83,79,21,5.3),
         (85,70,22,3.8),(85,67,22,1.2)),
        ((51,65,-2,6.2),(43,65,-8,5.5),(35,63,-14,5.1),(30,57,-18,4.8),(28,48,-20,4.5),
         (29,43,-20,3.1),(29,41,-20,1.3)),
    ),
    16: (
        ((39,217,0,8.9),(39,198,1,8.3),(39,168,2,7.6),(38,137,1,7.4),(37,107,0,7.2),
         (36,78,-2,6.7),(36,52,-2,6.5),(37,39,-3,5.0),(37,34,-3,2.5),(37,32,-3,1.1)),
        ((37,78,-1,6.1),(46,77,-6,5.5),(55,73,-12,5.1),(62,65,-16,5.0),(64,54,-18,5.3),
         (63,28,-19,5.5),(62,12,-19,5.0),(61,5,-19,3.3),(61,2,-19,1)),
        ((39,136,1,6.3),(49,136,5,6),(58,133,11,5.8),(65,126,15,5.5),(67,117,17,5.8),
         (67,96,18,5.8),(67,86,18,4.5),(67,82,18,1.5)),
        ((39,174,2,6.1),(29,174,-2,5.7),(19,173,-8,5.1),(11,168,-11,4.6),(8,161,-12,4.6),
         (7,151,-13,4.0),(8,146,-13,3),(9,144,-13,1.2)),
    ),
}
# The blossoms attach at individual areoles. Angle is about the local limb,
# zero on the right flank, ninety degrees facing the painted view.
BLOOMS = {
    14: ((1,64,32),(0,95,25),(3,177,100),(0,184,25),(2,232,145)),
    16: ((1,11,86),(0,46,149),(0,89,21),(2,105,16),(0,154,146),(0,166,-35)),
}
WIDTH = 96


def cap(mesh, ring, uv, normal):
    start = len(mesh["positions"]) // 3
    rim = ring[:-1]
    mesh["positions"].extend(np.vstack([rim.mean(0),rim]).round(6).ravel().tolist())
    mesh["normals"].extend(normal.round(6).tolist() * (len(rim)+1))
    mesh["uvs"].extend(list(uv) * (len(rim)+1))
    mesh["colors"].extend([1,1,1,0] * (len(rim)+1))
    for i in range(len(rim)):
        mesh["indices"].extend([start,start+1+(i+1)%len(rim),start+1+i])


def ribbed_skin(path, radii, ribs):
    tangents = np.gradient(path,axis=0)
    tangents /= np.linalg.norm(tangents,axis=1,keepdims=True)
    # Transport the section frame through each elbow so the furrows follow
    # the growing tip without a sudden twist on the horizontal part.
    axis = np.array([1.,0.,0.])
    if abs(tangents[0,0])>.88:
        axis = np.array([0.,0.,1.])
    angles = np.linspace(0,math.tau,2*ribs+1)
    rings = []
    for i,(p,r,tangent) in enumerate(zip(path,radii,tangents)):
        axis = axis-tangent*np.dot(axis,tangent)
        axis /= np.linalg.norm(axis)
        phase = angles+.056*math.sin(i*.77)+.030*np.sin(3*angles+i*.41)
        radial = np.outer(np.cos(phase),axis)+np.outer(np.sin(phase),np.cross(axis,tangent))
        edge = 1+.104*np.cos(ribs*angles)+.044*np.sin(3*angles+i*.59)+.025*np.cos(5*angles-i*.8)
        rings.append(p+r*edge[:,None]*radial)
    return np.array(rings),tangents


def remap_lines(lines, stations, along, across):
    """Interpolate the sample coordinates at each texel of an unrolled arm."""
    angles = np.linspace(0,1,lines.shape[1])
    around = np.array([np.interp(across,angles,row) for row in lines])
    return np.column_stack([np.interp(along,stations,col) for col in around.T])


def skin_tile(pixels, green, cork, rings, centres, distances, foot, height, ribs, record, rng):
    length = distances[-1]
    tile_h = max(24,round(length*height))
    t = np.linspace(0,1,tile_h)
    u = np.linspace(0,1,WIDTH)
    # Sample inside the silhouette; the wrap supplies fresh grain on grazing
    # faces. Dark marks and highlights in the actual painting survive up front.
    sample = centres[:,None]+.68*(rings-centres[:,None])
    sx = remap_lines(foot+sample[...,0]*height,distances/length,t,u)
    sy = remap_lines((1-sample[...,1])*height,distances/length,t,u)
    source = pixels[np.rint(sy).astype(int).clip(0,height-1),
                    np.rint(sx).astype(int).clip(0,pixels.shape[1]-1),:3]
    y,x = np.indices((tile_h,WIDTH))
    angle = math.tau*x/(WIDTH-1)
    ripple = ndimage.gaussian_filter(rng.normal(size=(tile_h,WIDTH)),(5.0,1.6),mode="wrap")
    phase = angle+.064*np.sin(y/16+angle*2)+.030*np.cos(y/8+3*angle)+.06*ripple
    rib = .5+.5*np.cos(ribs*phase)
    noise = ndimage.gaussian_filter(rng.normal(size=rib.shape),(1.35,1.1),mode="wrap")
    tone = .14+.63*rib+.10*np.cos(2*angle-.6)+.26*noise+.05*np.sin(6*angle+y/22)
    dry_edge = {14:.947,15:.695,16:.948}[record]+.028*np.sin(3*angle)+.012*np.sin(7*angle+y*.04)+.025*noise
    dry = sy/height>dry_edge
    rank = np.clip((tone*len(green)).astype(int),0,len(green)-1)
    paint = np.where(dry[...,None],cork[rank],green[rank])
    # Keep the original value changes on both broad faces. At grazing
    # angles use more of the unrolled chart to avoid a stretched edge texel.
    facing = (rings-centres[:,None])[...,2]
    facing /= np.maximum(np.linalg.norm(rings-centres[:,None],axis=2),1e-9)
    front = abs(remap_lines(facing,distances/length,t,u))
    direct = (.22+.56*front**1.4)[...,None]
    paint = (1-direct)*paint+direct*source
    # Dark longitudinal seams and broken sunlit ridges are larger strokes
    # than the areoles. Carry them around the elbow at the same rib phase.
    valley = np.clip((.36-rib)*3.65,0,1)
    gleam = (rib>.79)&(np.sin(angle*3+y/13)+noise*3>-.42)
    interruption = np.clip(.45+.35*np.cos(3*angle+y/18)+.65*noise,0,1)
    paint *= (1-(.22+.29*interruption)*valley)[...,None]
    paint += gleam[...,None]*np.where(dry[...,None],[14,9,3],[17,23,8])
    # Chunky patches of callus interrupt the grooves near old arm sockets.
    scar = (.42*np.cos(5*angle+y/13)+.38*np.sin(3*angle-y/18)+1.3*noise>.80)&(rib<.76)
    paint[scar] = .52*paint[scar]+.48*cork[rank[scar]]
    # The soft crowns carry younger, brighter skin between sparse needles.
    near_tip = np.clip((t-.91)/.09,0,1)[:,None]*(.48+.45*rib)
    paint += near_tip[...,None]*(green[-2]-paint)*.52
    foot_shade = .60+.40*np.clip((1-sy/height)/.30,0,1)
    paint *= foot_shade[...,None]
    # The two faces meeting at a rib carry staggered rows of areoles. Tiny
    # spine clusters are painted, with just a few actual spines on the outline.
    for j in range(ribs):
        col = j*(WIDTH-1)/ribs
        step = rng.uniform(8.3,11.5)
        for row in np.arange(rng.uniform(2,step),tile_h,step):
            xx = int(round(col+rng.uniform(-.9,.9)))%(WIDTH-1)
            yy = int(row)
            paint[yy:yy+2,xx:xx+2] = (43,48,24)
            paint[yy,xx] = (146,143,76)
    paint[:,-1] = paint[:,0]
    return Image.fromarray(np.clip(paint,0,255).astype(np.uint8)).convert("RGBA")


def spike(mesh, base, direction, radius, length, uv):
    direction = direction / np.linalg.norm(direction)
    side = np.cross(direction,[0,1,0])
    side /= np.linalg.norm(side)
    up = np.cross(direction,side)
    angle = np.arange(3)*math.tau/3
    rim = base+radius*(np.outer(np.cos(angle),side)+np.outer(np.sin(angle),up))
    points = np.vstack([rim,base+length*direction])
    start = len(mesh["positions"])//3
    mesh["positions"].extend(points.round(6).ravel().tolist())
    normals = np.vstack([(rim-base)/radius,direction])
    mesh["normals"].extend(normals.round(6).ravel().tolist())
    mesh["uvs"].extend(list(uv)*4)
    mesh["colors"].extend([1,1,1,0]*4)
    for j in range(3):
        mesh["indices"].extend([start+j,start+(j+1)%3,start+3])


def flower(mesh, node, outward, size, palette_uv):
    # A compact cup of orange petals and an ochre centre. Lean it out from
    # the areole so the rim reads from the side as well as the front.
    normal = outward + [0,.65,.50]
    normal /= np.linalg.norm(normal)
    right = np.cross(normal,[0,1,0])
    right /= np.linalg.norm(right)
    up = np.cross(right,normal)
    angles = np.linspace(0,math.tau,15)
    radial = np.outer(np.cos(angles),right)+np.outer(np.sin(angles),up)
    lobes = .78+.22*np.cos(7*angles)
    rows,uv = [],[]
    for r,lift,colour in ((.18,-.12,1),(.66,.20,2),(1,.54,3),(.60,.32,2),(.24,.17,1)):
        rows.append(node+size*(radial*lobes[:,None]*r+np.outer(lift+.11*r*lobes,normal)))
        uv.append([palette_uv(colour)]*len(angles))
    add_grid(mesh,np.array(rows),uv,seam=True)
    cap(mesh,rows[-1],palette_uv(1),normal)


def root_paint(cork, rng):
    y,x = np.indices((64,WIDTH))
    angle = x*math.tau/(WIDTH-1)
    drift = angle+.095*np.sin(y/13+angle*2)+.045*np.cos(y/6-angle*4)
    grooves = np.cos(9*drift)
    tone = .45+.27*grooves+.105*np.sin(17*drift)+.05*rng.normal(size=x.shape)
    paint = cork[np.clip((tone*len(cork)).astype(int),0,len(cork)-1)]*.71
    return Image.fromarray(paint.astype(np.uint8)).convert("RGBA")


def roots(mesh, record, trunk_radius, atlas, tile_x, rng):
    # Old cork peels from the boot in dark, tapering tongues. At the ground
    # they turn outward and fork. The crown of each root overlaps the column.
    count = 6 if record==15 else 7
    left, right = (tile_x+.5)/atlas.width, (tile_x+WIDTH-.5)/atlas.width
    for i in range(count):
        az = (i+rng.uniform(-.18,.18))*math.tau/count
        ray = np.array([math.cos(az),0,math.sin(az)])
        side = np.cross(ray,[0,1,0])
        reach = rng.uniform(.073,.124) if record!=15 else rng.uniform(.081,.130)
        shoulder = rng.uniform(.072,.176) if record==15 else rng.uniform(.031,.071)
        path = np.array([ray*trunk_radius*.84+[0,shoulder,0],
                         ray*trunk_radius*.98+[0,shoulder*.49,0],
                         ray*(trunk_radius+.012)+[0,.026,0]+side*.004,
                         ray*reach*.82+[0,.010,0]+side*.012,
                         ray*reach+[0,-.003,0]+side*.018])
        radii = np.array([.004,.009,.013,.008,.002])*(1.25 if record==15 else 1)
        rings,tangents = ribbed_skin(path,radii,3)
        # A shallow root has just six facets along its length.
        coords = np.stack(np.meshgrid(np.linspace(left,right,rings.shape[1]),
                              (.5+np.linspace(0,63,len(path)))/atlas.height),axis=-1)
        add_grid(mesh,rings,coords,seam=True)
        cap(mesh,rings[-1],coords[-1,3],tangents[-1])
    if record == 15:
        return
    for i in range(13):
        az = i*2.4
        reach = rng.uniform(.037,.158)
        x,z = reach*np.cos(az),reach*.65*np.sin(az)
        size = rng.uniform(.005,.022)*(1.23 if reach<.1 else .74)
        angle = np.linspace(0,math.tau,6)
        edge = 1+.20*np.sin(3*angle+az)
        grid,uv = [],[]
        for lift,r in ((-.23,.82),(.14,1),(.65,.41)):
            grid.append(np.stack([x+size*r*edge*np.cos(angle),
                                  size*(lift+.09*r*np.sin(2*angle)),z+size*r*.79*edge*np.sin(angle)],axis=1))
            uv.append(np.stack([(tile_x+24+11*np.cos(angle)*r)/atlas.width,
                                (32+11*np.sin(angle)*r)/atlas.height],axis=1))
        add_grid(mesh,np.array(grid),uv,seam=True)
        cap(mesh,grid[-1],((tile_x+24)/atlas.width,32/atlas.height),np.array([0.,1.,0.]))


def build(record, rgba):
    image = Image.fromarray(rgba.astype(np.uint8))
    pixels = np.array(image.crop(image.getbbox()))
    height = len(pixels)
    rgb = pixels[...,:3].astype(float)
    r,g,b = rgb.transpose(2,0,1)
    solid = pixels[...,3]>128
    greens = solid & (g>1.04*r) & (g>1.15*b)
    browns = solid & (r>1.13*g) & (g>1.20*b) & (np.arange(height)[:,None]>.67*height)
    ranks = [.03,.13,.27,.40,.54,.67,.80,.91,.98]
    green = np.array(colour_ramp(rgb[greens],ranks))[:,:3]
    cork = np.array(colour_ramp(rgb[browns],ranks))[:,:3]
    dark = rgb.max(axis=2)<54
    old_skin = (r>g*.92)&(r<g*1.7)&(b<g*.88)
    flowers = (r>g*1.20)&(g>b*1.60)&(r>95)&(np.arange(height)[:,None]<.81*height)
    pigment = np.array(filled(pixels,solid & (greens|browns|dark|old_skin) & ~flowers))
    rng = np.random.default_rng(record)
    foot = STEMS[record][0][0][0]
    mesh = part("bark")
    limbs,tiles = [],[]
    for i,trace in enumerate(STEMS[record]):
        x,y,z,radius = np.array(trace).T
        path = np.stack([x-foot,height-y,z],axis=1)/height
        if i==0:
            path[0,1] = -.006
        radii = radius/height
        ribs = 8 if i==0 else 7
        rings,tangents = ribbed_skin(path,radii,ribs)
        distances = np.r_[0.,np.cumsum(np.linalg.norm(np.diff(path,axis=0),axis=1))]
        tiles.append(skin_tile(pigment,green,cork,rings,path,distances,foot,height,ribs,record,rng))
        limbs.append((rings,path,radii,tangents,distances))
    root_x = WIDTH*len(tiles)
    swatches = root_x+WIDTH
    atlas = Image.new("RGBA",(swatches+12,max(im.height for im in tiles)+2))
    atlas.paste(root_paint(cork,rng),(root_x,0))
    for i,tile in enumerate(tiles):
        atlas.paste(tile,(WIDTH*i,0))
    colours = [(161,153,87),(93,62,27),(182,100,27),(238,156,45),(250,198,72),(73,67,36)]
    for i,colour in enumerate(colours):
        atlas.paste(colour+(255,), (swatches+2*i,0,swatches+2*i+2,4))
    def palette_uv(i):
        return ((swatches+2*i+1)/atlas.width,1.5/atlas.height)
    for i,(rings,path,radii,tangents,distances) in enumerate(limbs):
        sides = rings.shape[1]-1
        u = (WIDTH*i+.5+np.linspace(0,WIDTH-1,sides+1))/atlas.width
        v = (.5+distances/distances[-1]*(tiles[i].height-1))/atlas.height
        uv = np.stack(np.meshgrid(u,v),axis=-1)
        add_grid(mesh,rings,uv,seam=True)
        cap(mesh,rings[-1],(u[sides//2],v[-1]),tangents[-1])
        cap(mesh,rings[0][::-1],(u[0],v[0]),-tangents[0])
        for j,d in enumerate(np.arange(.026,distances[-1]-.009,.038)):
            n = np.searchsorted(distances,d)-1
            t = (d-distances[n])/(distances[n+1]-distances[n])
            section = rings[n]*(1-t)+rings[n+1]*t
            centre = path[n]*(1-t)+path[n+1]*t
            for k in range(j%3*2,sides,6):
                radial = section[k]-centre
                normal = radial/np.linalg.norm(radial)
                spike(mesh,section[k]-.0011*normal,normal+[0,rng.uniform(-.65,.70),0],
                      .0025,rng.uniform(.009,.017),palette_uv(0 if (j+k)%3 else 5))
    roots(mesh,record,limbs[0][2][0],atlas,root_x,rng)
    for arm,down,degrees in BLOOMS.get(record,()):
        rings,path,*_ = limbs[arm]
        y = height*(1-path[:,1])
        # These flowers all grow on the upright portions of their arms.
        n = np.where((y[:-1]>=down)&(y[1:]<=down))[0][-1]
        t = (y[n]-down)/(y[n]-y[n+1])
        centre = path[n]*(1-t)+path[n+1]*t
        direction = np.array([math.cos(math.radians(degrees)),0,math.sin(math.radians(degrees))])
        section = (1-t)*rings[n]+t*rings[n+1]
        node = section[np.argmax((section-centre)@direction)]
        flower(mesh,node,direction,.018 if record==14 else .020,palette_uv)
    return {"height":max(mesh["positions"][1::3]),"parts":[mesh]}, {"bark":atlas}
