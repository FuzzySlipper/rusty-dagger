"""The summer haunted broadleaf, with crooked outer arms beneath a bronze and green crown."""
import math

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

from desert_trees import bark_tile
from rainforest_palms import add_grid
from rainforest_trees import Tree, filled
import woodland_trees as woodland


FORMS = {13: "crooked-bronze-broadleaf"}

# x, down, depth and radius in the cropped painting.
STEM = ((.425, 1, 0, .107), (.428, .954, .009, .092), (.440, .868, .035, .067),
        (.479, .782, .046, .060), (.515, .706, .034, .054), (.542, .619, .006, .052),
        (.538, .548, -.022, .050), (.511, .476, -.038, .047), (.486, .391, -.033, .037),
        (.502, .297, -.014, .026), (.484, .188, .015, .013))
ARMS = (
    (.721, ((.453, .686, .058, .042), (.412, .608, .088, .034), (.387, .585, .105, .023),
            (.346, .595, .110, .022), (.324, .584, .117, .019), (.317, .545, .117, .013),
            (.349, .461, .121, .008))),
    (.650, ((.593, .610, -.040, .035), (.652, .598, -.081, .025), (.677, .627, -.106, .020),
            (.701, .629, -.125, .018), (.720, .587, -.143, .017), (.774, .569, -.151, .013),
            (.799, .470, -.146, .007))),
    (.563, ((.615, .539, .070, .031), (.678, .520, .118, .024), (.704, .462, .145, .020),
            (.737, .410, .165, .012), (.739, .302, .159, .007))),
    (.532, ((.462, .446, -.096, .037), (.416, .397, -.149, .026), (.397, .306, -.187, .020),
            (.360, .229, -.192, .010))),
)

# Bough centres follow the amber high crown and the greener, lower lobes.
# Alternating depth keeps the open space over the two outer elbows in profile.
BOUGHS = (
    (.422, .117, -.010, .176, .107, .160), (.584, .136, .066, .171, .116, .155),
    (.315, .184, .078, .180, .115, .170), (.716, .209, -.064, .156, .123, .143),
    (.500, .221, -.232, .209, .128, .160), (.487, .234, .192, .215, .123, .168),
    (.211, .278, -.037, .170, .104, .163), (.401, .325, -.185, .203, .127, .155),
    (.664, .311, .214, .193, .122, .178), (.799, .318, -.079, .150, .111, .163),
    (.180, .397, .089, .177, .115, .139), (.380, .391, .188, .185, .129, .160),
    (.574, .395, -.200, .186, .111, .154), (.785, .409, .128, .176, .113, .151),
    (.216, .501, -.108, .162, .083, .143), (.407, .480, .052, .164, .089, .149),
    (.765, .526, -.140, .146, .115, .131), (.881, .471, .049, .114, .085, .117),
)


def limbs(tree):
    points = [tree.point(x, y, z) for x, y, z, _ in STEM]
    tree.tube(points, tree.stem[:, 3], 10, cap=True)
    anchors = list(points)
    for attachment, trace in ARMS:
        pts = [tree.stem_at(attachment)]+[tree.point(x, y, z) for x, y, z, _ in trace]
        tree.tube(pts, [trace[0][3]]+[r*.90 for _, _, _, r in trace], 6, cap=True)
        anchors.extend(pts[1:])
    projected = len(tree.bark["uvs"])
    for i in range(6):
        az = .51+i*math.tau/6
        ray = np.array([math.cos(az), 0, .86*math.sin(az)])*tree.aspect
        length = (.20 if ray[0] < 0 else .15)*tree.rng.uniform(.75, 1.09)
        woodland.limb(tree, [tree.stem_at(.888), tree.stem_at(.951)+ray*length*.29,
                      tree.stem_at(.982)+ray*length*.68, tree.stem_at(1.001)+ray*length],
                      [.039, .031, .019, .003], 5)
    return np.array(anchors), projected


def painted_bark(tree, projected):
    # The ochre streak on the leaning bole, dark elbows and patches of lichen
    # are already in the painting. Project those marks onto the large limbs.
    px = tree.pixels
    rgb = px[..., :3].astype(float)
    solid = px[..., 3] > 128
    h, w = solid.shape
    guide = np.zeros_like(solid)
    for trace in (STEM, *(arm for _, arm in ARMS)):
        for (x, y, _, r), (xx, yy, _, rr) in zip(trace, trace[1:]):
            steps = 1+round(math.hypot((xx-x)*w, (yy-y)*h))
            for t in np.linspace(0, 1, steps):
                cy = int(np.clip(round((y+(yy-y)*t)*(h-1)), 0, h-1))
                cx = round((x+(xx-x)*t)*(w-1))
                span = max(1, round((r+(rr-r)*t)*w))
                guide[cy, max(0, cx-span):min(w, cx+span+1)] = True
    wood = solid & guide & ((rgb[..., 0] >= rgb[..., 1]*.99) | (np.arange(h)[:, None] > .77*h))
    painted = np.array(filled(px, wood))
    grain = bark_tile(tree, 13)
    yy, xx = np.indices(solid.shape)
    repeat = np.asarray(grain)[yy%grain.height, xx%grain.width]
    share = np.clip(distance_transform_edt(~wood)/6, 0, .81)[..., None]
    painted[..., :3] = np.rint(painted[..., :3]*(1-share)+repeat[..., :3]*share)
    atlas = Image.new("RGBA", (w+grain.width, max(h, grain.height)))
    atlas.paste(Image.fromarray(painted))
    atlas.paste(grain, (w, 0))
    uv = tree.bark["uvs"]
    uv[:projected:2] = [u*w/atlas.width for u in uv[:projected:2]]
    uv[1:projected:2] = [v*h/atlas.height for v in uv[1:projected:2]]
    uv[projected::2] = [(w+u*grain.width)/atlas.width for u in uv[projected::2]]
    uv[projected+1::2] = [v*grain.height/atlas.height for v in uv[projected+1::2]]
    return atlas


def fringe(tree, paint):
    # Small outer sprays carry the original loose leaves against the sky.
    # Their upper edges stand up past the core, while the lower sprays droop.
    tips = ((.356, .054, .004, .113, .082), (.530, .061, -.032, .127, .076),
          (.215, .162, .032, .130, .092), (.725, .166, .060, .105, .080),
          (.130, .287, -.011, .104, .090), (.066, .392, .054, .094, .070),
          (.884, .339, -.060, .104, .089), (.915, .448, .079, .092, .088),
          (.846, .548, -.095, .108, .081), (.219, .492, -.090, .129, .079),
          (.441, .147, .254, .156, .095), (.565, .279, -.326, .153, .103))
    w, h = paint.size
    for i, (x, y, z, r, ry) in enumerate(tips):
        patch = np.array(paint.crop((round((x-r)*w), round((y-ry)*h), round((x+r)*w)+1,
                                  round((y+ry)*h)+1)).resize((64, 56), Image.Resampling.NEAREST))
        yy, xx = np.indices((56, 64))
        u, v = (xx-31.5)/32, (yy-27.5)/28
        light = patch[..., :3]@[.21, .72, .07]
        edge = .59+.33*np.clip((light-20)/95, 0, 1)+.10*np.sin(7*np.arctan2(v, u)+i)
        patch[..., 3] *= u*u+v*v < edge**2
        slot = len(tree.patches)
        tree.patches.append(Image.fromarray(patch))
        centre = tree.point(x, y, z)
        for angle in (.24+i*.76, 2.31+i*.76):
            ray = np.array([math.cos(angle), 0, math.sin(angle)])*r*tree.aspect
            side = np.cross(ray, [0, 1, 0])
            rows, uv = [], []
            for t, reach, lift in ((0, .06, .90), (.48, .32, .15), (1, .52, -.88)):
                rows.append([centre+side*k+ray*(reach-.24*k*k)+[0, ry*(lift-.12*k*k), 0] for k in (1, 0, -1)])
                uv.append([[(slot%8+u)/8, slot//8+.01+.98*t] for u in (.006, .50, .994)])
            add_grid(tree.leaves, np.array(rows), uv, double=True)


def build(record, rgba):
    tree = Tree(record, rgba, STEM)
    anchors, projected = limbs(tree)
    crown = woodland.Boughs(tree, record, .656, autumn=True)
    # Beyond the painted silhouette the core supplies dappled shade. Allow
    # gaps between its darker outer leaves, including on side/top projections.
    interior = np.array(crown.interior)
    solid = np.array(crown.paint)[..., 3] > 128
    yy, xx = np.indices(solid.shape)
    shade = interior[..., :3]@[.21, .72, .07]
    threshold = np.quantile(shade[solid], .40)
    edge = (distance_transform_edt(~solid) > 1.4) & (shade < threshold+7*np.sin(xx*.41+yy*.29))
    interior[edge, 3] = 0
    crown.interior = Image.fromarray(interior)
    for i, (x, down, z, width, height, depth) in enumerate(BOUGHS):
        if down > .30:
            woodland.connect(tree, anchors, tree.point(x, down, z), min(.024, width*.14))
        crown.grow(x, down, z, width, height, depth, i*math.tau*.382,
                   sprays=3 if width < .16 else 4)
    fringe(tree, crown.paint)
    images = {"bark": painted_bark(tree, projected), "leaves": crown.atlas()}
    parts = [tree.bark, tree.leaves]
    return {"height": max(max(p["positions"][1::3]) for p in parts), "parts": parts}, images
