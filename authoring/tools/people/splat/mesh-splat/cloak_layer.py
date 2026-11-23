#!/usr/bin/env python3
"""Cut a garment layer out of a clothed splat registered onto the same figure's bare splat.

  python3 cloak_layer.py CLOTHED.npz BASE.npz OUT_DIR [--below 0.26] [--shoulder 0.31] [--neck-x 0.08]
      [--lowest -0.42] [--far 0.02]
      [--margin 0.03] [--tolerance 0.3] [--hand-x 0.17] [--back-z -0.03] [--on-body-low -0.15] [--arm-x 0.15] [--outside 0.004] [--take-behind 1] [--deep 0.02] [--noise-tolerance 0.25] [--noise-depth 0.004] [--k 32]
      [--outer-weights OUT.npz [--inside-weights IN.npz --inside-ratio 1] --outer-threshold 0.02
       --lining-shade 0.25 --lining-opacity 0] [--min-opacity 0.3] [--vote 24]
      [--vote-passes 3] [--speck 300] [--cell 0.006] [--inner-shade 0.4] [--inner-depth 0.015] [--inner-opacity 0]
      [--faint-inner]
      [--patch-low 0.10] [--patch-offset 0.004] [--patch-shade 0.85] [--backing-shade 0] [--patch-select tunic|light|none]
      [--around [--shell 0.04] [--largest] [--backing DEPTH [--backing-colour 0.9] [--lining-gap 0.005] [--clear-base C [--clear-max 0.05] [--clear-select white|all]] [--backing-top Y] [--surface-opacity O] [--tuck H]] [--hem-ratio 0.7] [--hand-margin M] [--hand-low -0.22] [--hand-bump B] [--hand-dark D] [--hand-own T]]

CLOTHED is the cloaked splat already in BASE's frame (register_splats.py).
Both are in the loop's frame: about 1 tall, Y up, front toward +Z. The base
keeps the hood and the shoulder mantle; the garment is the cloth that hangs
from under the mantle. The rule is geometric first, colour second:

1. Region. Only Gaussians between LOWEST (the hem; boots below it are never
   cloak) and BELOW (the underside of the mantle), and outside the hand
   zones (|x| > HAND_X between hip and wrist height), can be garment. Behind
   the body (z < BACK_Z) and outside the neck (|x| > NECK_X) the limit rises
   to SHOULDER: the cloak's top corners reach the shoulders beside a V-shaped
   mantle, and cutting them at BELOW leaves triangles of tunic showing.
2. Standing off the body. A Gaussian further than FAR from every opaque BASE
   Gaussian is garment, unless its colour is far from cloth (more than
   TOLERANCE from the garment's reference colour), which catches a sleeve or
   leg that sits elsewhere than in BASE.
3. Lying on the body, at the back. A Gaussian within FAR of the base,
   behind BACK_Z and above ON_BODY_LOW (the hips; lower down the cloak hangs
   clear of the legs, and a leg set back in the stride would pass) is garment when its colour is closer to the garment's than
   to the base surface beside it by MARGIN: the cloak over the back of the
   tunic just under the mantle. In front, cloth on the body is never garment:
   averaged colours are too noisy there to tell a teal tunic or tan breeches
   from the greyish-brown cloak, and the cloak's front edges stand off the
   body anyway.
4. Outside the base, at the back. Behind BACK_Z and inside |x| < ARM_X, a
   cloth-coloured Gaussian further from the body's vertical axis than the
   base's own outer surface (90th percentile of the base's radius in its
   2-degree, 1 cm cell of angle and height) by OUTSIDE is garment: the base
   has nothing there. This keeps the cloak where it hangs within FAR of a leg
   set back in the stride, which rules 2 and 3 miss and which then shows as
   holes in the cloak's back. It applies only above the hem (1st percentile
   height of the rule-2 cloth behind the body, less 1 cm): below it, a leg
   set further back than in BASE would pass.
5. Behind the body (--take-behind 1, the default). Behind BACK_Z, inside
   |x| < ARM_X, above the hem and below the top limit, every Gaussian that is
   not more than DEEP inside the base's outer surface and not sleeve-white
   (averaged colour under 0.6) is garment. There the cloaked splat holds only
   cloak and the hidden space under it, and the cloak's smooth look depends
   on large semi-transparent Gaussians centred close to the body that rules
   2-4 miss; without them the cloak's back is mottled with holes. The hidden
   interior that comes along is darkened by the lining rule below.

Colours are averaged over each Gaussian's K nearest opaque neighbours, since
single Gaussians are too noisy to judge; the base's local colour is the mean
of its K nearest opaque BASE Gaussians. The reference colour is the median of
those standing off the base behind the body (z < 0) between the knees and the
shoulder blades, which on a cloaked figure is the cloak's back.

The labels are then settled with hysteresis, VOTE_PASSES times, by the share
of garment among each Gaussian's VOTE nearest opaque neighbours: above 0.5 a
Gaussian joins (holes in the cloth fill), under 0.2 it leaves (strays on legs
drop), in between it keeps its label. Last, connected pieces smaller than
SPECK Gaussians (neighbours within CELL) are dropped.

The cloaked splat's unseen inside, between body and cloak, is noise in body
colours, and it shows whenever the layer is seen from inside. Every garment
Gaussian lying more than INNER_DEPTH inside its cell's outer surface (the 90th
percentile of distance from the body's vertical axis among the opaque garment
Gaussians in its 2-degree, 1 cm cell of angle and height; the maximum lets one
stray Gaussian darken a patch of the outer face) is recoloured to the
reference colour times INNER_SHADE, a dark lining. So is every garment
Gaussian more than NOISE_DEPTH inside that outer surface whose own colour
(not averaged) is further than NOISE_TOLERANCE from the cloth's: the hidden
interior's noise, which averaging over neighbours pulls back towards cloth. --inner-opacity raises the
lining's opacity to at least that value; --faint-inner also darkens every
faint garment Gaussian, which darkens the outer face too and is not advised.

Under the mantle the cloaked video never saw the cloak's top, which comes out
thin there, so the base's tunic shows through the join. A plain patch of cloth
is added to the layer: one opaque Gaussian per CELL/1.5 grid cell of the
base's back surface (z < BACK_Z, |x| < ARM_X, PATCH_LOW < y < SHOULDER)
wherever that surface is tunic-coloured (green over red and darker than the
bluish-white sleeves, averaged over its K neighbours), pushed PATCH_OFFSET out from the body's vertical axis and
coloured with the reference times PATCH_SHADE. It stays clear of the base's
mantle, which is brown, so the mantle still overlaps the cloak's top.
--backing-shade S continues the patch down the base's whole back to the
cloak's hem, coloured with the reference times S (a dark backing). Where the
cloak is semi-transparent, the base's teal and tan back otherwise shows
through and mottles it; in the cloaked splat its own interior sat there.

Second pass, a plain lining (--outer-weights). The cloak is a thin shell, so
its inside and outside faces lie within millimetres of each other and no
distance rule separates them. Run the tool once, measure the layer's
splat.npz from the sides and back with visible_weight.py (azimuths 90-270),
then run it again with the same settings and --outer-weights: every Gaussian
whose largest per-camera weight is under OUTER_THRESHOLD is never seen from
outside and is recoloured to the reference colour times LINING_SHADE, with
its opacity raised to at least LINING_OPACITY when that is set. The outer
face keeps its colours. The layer's order is deterministic, so the weights
line up with the rerun.
The shell is also partly transparent, so much of the inside still has some
weight from outside cameras and shows from the front as bright flecks. With
--inside-weights (visible_weight.py from the front, azimuths -60 to 60), a
Gaussian is also lining when its mean weight per inside camera is over 0.002
and over INSIDE_RATIO times its mean weight per outside camera: the front sees
it more than the sides and back do. Both weight files must use the same
elevations.

Garments all the way round (--around), such as a bell skirt hung under a
peplum. Rules 1-5 assume cloth behind the body; a skirt surrounds it, so
with --around the region is every Gaussian between LOWEST (the bottom of the
hem) and BELOW (the underside of whatever overlaps the garment's top) at every
angle, and the first pass is geometric: a Gaussian is garment when it lies
within SHELL of CLOTHED's own outer surface (90th percentile of distance from
the garment's vertical axis - with --around, the middle of CLOTHED's x and z
extent in the band, 2nd to 98th percentile, rather than the base's median - among CLOTHED's opaque region Gaussians in its
2-degree, 1 cm cell; hands left out of it). The legs, the underskirt and the
unseen noise inside the bell lie further in and are left behind, wherever the
clothed video put them, so a stride or lean that differs from BASE does not
matter. Hands are not cut by a box (the skirt is wider than the hands' zone):
inside the hand zone only Gaussians whose averaged colour is further than
TOLERANCE from the cloth's and whose green is over the cloth's by 0.1
(skin and sleeves are lighter; the cloth's own shadowed folds are darker)
leave. The zone runs from HAND_LOW (default -0.22) to 0.08 high, or to BELOW when
the garment reaches higher (sleeves hang beside a dress cut at the belt); raise
HAND_LOW where the hem rides up beside the hands, or its flounce, lighter than
the cloth, leaves with them. --hand-margin M also takes out the outermost Gaussians (within 1.5 cm
of the surface) lying within M of those, in the hand zone: the shadowed side
of a hand is as dark as the cloth and is otherwise left on the skirt.
--hand-own T also takes out hand-zone Gaussians by their own colour: further
than T from the cloth's and with green or blue over the cloth's by 0.15
(yellow cuffs, skin, a bluish stray), where averaging over neighbours had
blended them into the cloth. --hand-dark D takes out Gaussians in the hand zone within 1.5 cm of the
hand Gaussians already left out whose own colour's brightest channel is under
D: the shadowed side of a hand that sank into the cloth (registration leaves
the clothed figure's hands a few cm from the base's, so they lie in the
skirt's surface). --hand-bump B finds that side by shape instead: in the hand zone, cells
(2 degrees by 1 cm) whose outer radius stands more than B above the median
of their 13 x 5 neighbourhood are a hand lying on the cloth, and their
Gaussians more than B/2 outside that median leave, whatever their colour. --largest keeps only the largest connected
piece. --hem-ratio R ends each 2-degree column of the garment at its hem:
below the column's widest 1 cm cell in the band's lower half, a cell whose
outer radius is under R times that width is a leg or shoe passing out under the hem, not cloth, and
its Gaussians leave (the clothed figure's legs need not stand where BASE's
do, and where they lean out past the hem line they are the outermost thing
in their cells). With --around, a Gaussian whose own extent (two standard
deviations along y, from its scales and rotation) reaches more than 1 cm
below LOWEST also leaves: a few long Gaussians of a leg centred just inside
the hem otherwise hang below it as a stocking fragment.
A skirt's cloth can be faint (the clothed video saw it only through its own
semi-transparent Gaussians), and on its own the layer then shows the base's
underskirt and legs through it. --backing DEPTH closes it: CLOTHED's outer
surface is gridded (2 degrees by 1 cm, 90th percentile radius, gaps filled
along the circle and median-smoothed over 3 x 3 cells); one opaque Gaussian
per grid cell is placed DEPTH inside it, coloured with the cell's own cloth
colour (median averaged colour of its Gaussians within 1 cm of the surface)
times BACKING_COLOUR, and a second one LINING_GAP further in, coloured with
the reference times LINING_SHADE (a dark lining, seen from inside the bell).
Garment Gaussians deeper than the lining are dropped: hidden from outside by
the backing, they would only show from below as noise inside the lining.
Cells are filled between the lowest and highest garment Gaussian of their
column, so the hem's own line is kept and the underside stays open.
--backing-top Y continues every column's backing and lining up from its
highest cell to Y in that cell's colour, wrapped round the base's white cloth
CLEAR_BASE out from it (cells with no white base cloth stay empty; with no
--clear-base, at the highest cell's radius): where the clothed figure's
cloth starts lower than the base's peplum ends, this closes the gap under
the peplum without standing out past it. The backing's colours are averaged
over 5 x 3 neighbouring cells so the grid does not show as stripes.
--tuck H draws the garment's top in under the base's overlapping piece: in
each column, the 90th percentile radius of the base's Gaussians in the 4 cm
above BELOW (a peplum's hem; nothing further out than HAND_X plus 4 cm, where the
forearms hang), less 6 mm, is the most the garment may stand
out at BELOW; cells over H below it are drawn in by a share falling to none
at H, cloth, backing and lining alike (then --clear-base still applies).
--surface-opacity O raises the opacity of the garment Gaussians within 1 cm
of the outer surface to at least O, so faint cloth covers its own backing
instead of showing it as flat patches.
--clear-base C (with --backing) makes room for the base where the clothed
figure was slimmer. First the backing comes up from DEPTH (down to 2 cm deep)
in cells where the base's white cloth lies closer than that under the
garment's surface, so the lining keeps C clear of it without moving the
cloth. The base's white cloth and stockings (opaque Gaussians whose every
channel is over 0.6, so not the peplum or skin; with --clear-select all, every
opaque base Gaussian, for a garment that also covers the base's peplum) no
further out than HAND_X plus 4 cm (the arms) are gridded the same way
(95th percentile radius, widened by a 3 x 3 maximum). Where they still come
within C of the lining at the shallowest backing, every garment Gaussian of
that cell, and its backing and lining, moves straight out by the shortfall
(smoothed over neighbouring cells so the cloth does not step; at most
CLEAR_MAX). White Gaussians within 3 cm of skin-coloured ones further out than HAND_X
plus 4 cm (cuffs at the hands) are left out.
The vote and the dark inside (INNER_DEPTH, the lining pass) work as
for a cloak; the lining pass is how the inside of the bell is darkened, and
the underside is left open. --patch-select chooses which base surface under
the garment's top gets the plain patch: tunic (teal, the cloak default),
light (every averaged channel over 0.6: a white underskirt, not skin) or none; with --around the
patch goes all the way round.

OUT_DIR gets splat.npz (the garment layer), rest.npz (CLOTHED without it, to
see what was left behind), labels.npy (True for garment Gaussians of
CLOTHED), kinds.npy (per layer Gaussian: 0 cloth, 1 join patch, 2 backing,
3 lining), with --around hands.npy (True for CLOTHED's hand Gaussians that were
left out) and report.json with the counts and colours.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from scipy.spatial import cKDTree


def load(path):
    with np.load(path) as src:
        return {k: src[k] for k in src.files}


def subset(splat, mask):
    n = len(splat['means'])
    return {k: (v[mask] if len(v) == n else v) for k, v in splat.items()}


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('clothed', type=Path)
    p.add_argument('base', type=Path)
    p.add_argument('out', type=Path)
    p.add_argument('--below', type=float, default=0.26)
    p.add_argument('--lowest', type=float, default=-0.42)
    p.add_argument('--far', type=float, default=0.02)
    p.add_argument('--margin', type=float, default=0.03)
    p.add_argument('--tolerance', type=float, default=0.3)
    p.add_argument('--hand-x', type=float, default=0.17)
    p.add_argument('--back-z', type=float, default=-0.03)
    p.add_argument('--on-body-low', type=float, default=-0.15)
    p.add_argument('--arm-x', type=float, default=0.15)
    p.add_argument('--outside', type=float, default=0.004)
    p.add_argument('--take-behind', type=int, default=1)
    p.add_argument('--deep', type=float, default=0.02)
    p.add_argument('--noise-tolerance', type=float, default=0.25)
    p.add_argument('--noise-depth', type=float, default=0.004)
    p.add_argument('--shoulder', type=float, default=0.31)
    p.add_argument('--neck-x', type=float, default=0.08)
    p.add_argument('--inner-shade', type=float, default=0.4)
    p.add_argument('--inner-depth', type=float, default=0.015)
    p.add_argument('--inner-opacity', type=float, default=0.0)
    p.add_argument('--faint-inner', action='store_true')
    p.add_argument('--patch-low', type=float, default=0.10)
    p.add_argument('--patch-offset', type=float, default=0.004)
    p.add_argument('--patch-shade', type=float, default=0.85)
    p.add_argument('--backing-shade', type=float, default=0.0)
    p.add_argument('--outer-weights', type=Path)
    p.add_argument('--inside-weights', type=Path)
    p.add_argument('--inside-ratio', type=float, default=1.0)
    p.add_argument('--outer-threshold', type=float, default=0.02)
    p.add_argument('--lining-shade', type=float, default=0.25)
    p.add_argument('--lining-opacity', type=float, default=0.0)
    p.add_argument('--patch-select', choices=('tunic', 'light', 'none'), default='tunic')
    p.add_argument('--around', action='store_true')
    p.add_argument('--shell', type=float, default=0.04)
    p.add_argument('--largest', action='store_true')
    p.add_argument('--hem-ratio', type=float, default=None)
    p.add_argument('--backing', type=float, default=0.0)
    p.add_argument('--backing-colour', type=float, default=0.9)
    p.add_argument('--lining-gap', type=float, default=0.005)
    p.add_argument('--clear-base', type=float, default=None)
    p.add_argument('--clear-max', type=float, default=0.05)
    p.add_argument('--clear-select', choices=('white', 'all'), default='white')
    p.add_argument('--backing-top', type=float, default=None)
    p.add_argument('--surface-opacity', type=float, default=None)
    p.add_argument('--tuck', type=float, default=0.0)
    p.add_argument('--hand-margin', type=float, default=0.0)
    p.add_argument('--hand-low', type=float, default=-0.22)
    p.add_argument('--hand-bump', type=float, default=0.0)
    p.add_argument('--hand-dark', type=float, default=0.0)
    p.add_argument('--hand-own', type=float, default=0.0)
    p.add_argument('--k', type=int, default=32)
    p.add_argument('--min-opacity', type=float, default=0.3)
    p.add_argument('--vote', type=int, default=24)
    p.add_argument('--vote-passes', type=int, default=3)
    p.add_argument('--speck', type=int, default=300)
    p.add_argument('--cell', type=float, default=0.006)
    a = p.parse_args()
    cl, base = load(a.clothed), load(a.base)
    sig = lambda s: 1 / (1 + np.exp(-s['opacities'].ravel()))
    cm = cl['means'].astype(np.float64)
    cc = np.clip(cl['rgb'].reshape(len(cm), -1)[:, :3], 0, 1)
    co = sig(cl) > a.min_opacity
    bo = sig(base) > a.min_opacity
    bm = base['means'][bo].astype(np.float64)
    bc = np.clip(base['rgb'].reshape(len(base['means']), -1)[bo][:, :3], 0, 1)

    opaque_idx = np.nonzero(co)[0]
    _, idx = cKDTree(cm[co]).query(cm, k=a.k)
    smooth = cc[opaque_idx][idx].mean(1)
    bdist, bidx = cKDTree(bm).query(cm, k=a.k)
    dist = bdist[:, 0]
    base_local = bc[bidx].mean(1)

    ref_mask = co & (dist > a.far) & (cm[:, 2] < 0) & (cm[:, 1] < a.below - 0.1) & (cm[:, 1] > -0.35)
    ref = np.median(smooth[ref_mask], 0)
    to_ref = np.linalg.norm(smooth - ref, axis=1)
    to_base = np.linalg.norm(smooth - base_local, axis=1)

    y = cm[:, 1]
    hands = (np.abs(cm[:, 0]) > a.hand_x) & (y > -0.22) & (y < 0.08)
    top = np.where((cm[:, 2] < a.back_z) & (np.abs(cm[:, 0]) > a.neck_x), a.shoulder, a.below)
    region = (y < top) & (y > a.lowest) & ~hands
    off_body = (dist > a.far) & (to_ref < a.tolerance)
    on_body = ((dist <= a.far) & (cm[:, 2] < a.back_z) & (y > a.on_body_low) & (np.abs(cm[:, 0]) < a.arm_x + (y > a.below) * 0.03)
               & (to_ref < a.tolerance))
    # 4. outside the base's own outer surface, at the back
    centre = np.median(bm[:, [0, 2]], 0)
    if a.around:   # the garment's own vertical axis: the middle of the clothed figure's extent in the band
        bandc = co & (cm[:, 1] > a.lowest) & (cm[:, 1] < a.below)
        lo_, hi_c = np.percentile(cm[bandc][:, [0, 2]], [2, 98], axis=0)
        centre = (lo_ + hi_c) / 2

    def polar(points):
        rel = points[:, [0, 2]] - centre
        ang = np.arctan2(rel[:, 0], rel[:, 1])
        key = (np.floor((ang + np.pi) / (2 * np.pi) * 180).astype(np.int64) * 100000
               + np.floor((points[:, 1] + 1) / 0.01).astype(np.int64))
        return np.hypot(rel[:, 0], rel[:, 1]), key

    def percentile_by_cell(r, key, q):
        order = np.lexsort((r, key))
        ks = key[order]
        st = np.r_[0, np.nonzero(np.diff(ks))[0] + 1]
        en = np.r_[st[1:], len(ks)]
        return dict(zip(ks[st].tolist(), r[order][st + np.floor(q * (en - st - 1)).astype(int)].tolist()))

    radius, cell = polar(cm)
    b_radius, b_cell = polar(bm)
    b_outer = percentile_by_cell(b_radius, b_cell, 0.9)
    base_outer = np.array([b_outer.get(c_, np.inf) for c_ in cell.tolist()])
    hem = np.percentile(y[region & off_body & (cm[:, 2] < a.back_z)], 1) - 0.01
    outside_base = ((cm[:, 2] < a.back_z) & (np.abs(cm[:, 0]) < a.arm_x) & (radius > base_outer + a.outside) & (y > hem)
                    & (to_ref < a.tolerance))
    # 5. everything behind the body between hem and shoulders, short of deep inside the base or sleeve-white
    behind = ((cm[:, 2] < a.back_z) & (np.abs(cm[:, 0]) < a.arm_x) & (y > hem) & (radius > base_outer - a.deep)
              & (smooth.mean(1) < 0.6)) if a.take_behind else np.zeros(len(cm), bool)
    first = region & (off_body | on_body | outside_base | behind)
    rule_counts = {'off_body': int((region & off_body).sum()), 'on_body': int((region & on_body).sum()),
                   'outside_base': int((region & outside_base).sum()), 'behind': int((region & behind).sum()), 'hem_y': round(float(hem), 3)}
    if a.around:   # a garment all the way round: the shell under CLOTHED's own outer surface, hands out by colour
        band = (y < a.below) & (y > a.lowest)
        own = np.nonzero(band & co)[0]
        own_outer = percentile_by_cell(radius[own], cell[own], 0.9)
        own_r = np.array([own_outer.get(c_, np.inf) for c_ in cell.tolist()])
        shell = band & (radius > own_r - a.shell)
        mid = shell & co & (y < a.below - 0.05) & (y > (a.below + a.lowest) / 2)
        ref = np.median(smooth[mid], 0)
        to_ref = np.linalg.norm(smooth - ref, axis=1)
        hand_top = max(0.08, a.below)   # sleeves hang beside a garment that reaches up to the belt
        hands = ((np.abs(cm[:, 0]) > a.hand_x) & (y > a.hand_low) & (y < hand_top) & (to_ref > a.tolerance)
                 & (smooth[:, 1] > ref[1] + 0.1))   # skin and sleeves are lighter than the cloth; its shadowed folds are not
        q = cl['quats'].astype(np.float64); q = q / np.linalg.norm(q, axis=1, keepdims=True)
        w_, x_, y_, z_ = q.T   # second row of each rotation matrix
        row = np.column_stack([2 * (x_ * y_ + w_ * z_), 1 - 2 * (x_ * x_ + z_ * z_), 2 * (y_ * z_ - w_ * x_)])
        sigma_y = np.sqrt(((row * np.exp(cl['scales'].astype(np.float64))) ** 2).sum(1))
        hangs = y - 2 * sigma_y < a.lowest - 0.01
        if a.hand_margin and hands.any():   # the hand's own shadowed side is cloth-dark: take what lies against it too
            near_hand = cKDTree(cm[hands & co]).query(cm, distance_upper_bound=a.hand_margin)[0] < np.inf
            hands = hands | (near_hand & (np.abs(cm[:, 0]) > a.hand_x) & (y > a.hand_low) & (y < hand_top) & (radius > own_r - 0.015))
        if a.hand_own:   # single sleeve and cuff Gaussians whose neighbours' average blends into the cloth
            own_to_ref = np.linalg.norm(cc - ref, axis=1)
            hands = hands | ((np.abs(cm[:, 0]) > a.hand_x) & (y > a.hand_low) & (y < hand_top)
                             & (own_to_ref > a.hand_own) & ((cc[:, 1] > ref[1] + 0.15) | (cc[:, 2] > ref[2] + 0.15)))
        if a.hand_dark and hands.any():   # a hand's shadowed side, sunk into the cloth: dark and right against the hand
            own_max = cc.max(1)
            near_hand = cKDTree(cm[hands & co]).query(cm, distance_upper_bound=0.015)[0] < np.inf
            dark_hand = near_hand & (own_max < a.hand_dark) & (np.abs(cm[:, 0]) > a.hand_x) & (y > a.hand_low) & (y < hand_top)
            hands = hands | dark_hand
            rule_counts_dark = int((shell & dark_hand).sum())
        if a.hand_bump:   # what stands proud of the cloth in the hand zone is the clothed figure's hand, whatever its colour
            from scipy.ndimage import median_filter as _mf
            hrow = np.floor((y - a.lowest) / 0.01).astype(int); nrow = int(np.ceil((a.below - a.lowest) / 0.01)) + 1
            acol = np.clip(cell // 100000, 0, 179).astype(int)
            g90 = np.zeros((180, nrow)); okb = band & co & (hrow >= 0) & (hrow < nrow)
            idx = np.nonzero(okb)[0]; key = acol[idx] * nrow + hrow[idx]
            order = np.lexsort((radius[idx], key)); key, rr = key[order], radius[idx][order]
            st = np.r_[0, np.nonzero(np.diff(key))[0] + 1]; en = np.r_[st[1:], len(key)]
            g90.flat[key[st]] = rr[st + np.floor(0.9 * (en - st - 1)).astype(int)]
            pad = 8
            smooth_g = _mf(np.concatenate([g90[-pad:], g90, g90[:pad]]), size=(13, 5), mode='nearest')[pad:-pad]
            inb = (hrow >= 0) & (hrow < nrow)
            cell_bump = np.zeros(len(cm), bool); s_r = np.zeros(len(cm))
            cell_bump[inb] = (g90 - smooth_g)[acol[inb], hrow[inb]] > a.hand_bump
            s_r[inb] = smooth_g[acol[inb], hrow[inb]]
            hand_zone = (np.abs(cm[:, 0]) > a.hand_x) & (y > a.hand_low) & (y < hand_top)
            bump = hand_zone & cell_bump & (radius > s_r + a.hand_bump / 2)
            hands = hands | bump
            rule_counts_bump = int((shell & bump).sum())
        region = band & ~hands & ~hangs
        if a.hem_ratio is not None:   # cut each column at its hem: narrow cells under the widest one are legs
            hrow = np.floor((y - a.lowest) / 0.01).astype(int)
            acol = np.clip(cell // 100000, 0, 179).astype(int)
            nrow = int(np.ceil((a.below - a.lowest) / 0.01)) + 1
            outer_grid = np.zeros((180, nrow))
            sh = np.nonzero(shell & co & ~hands)[0]
            np.maximum.at(outer_grid, (acol[sh], np.clip(hrow[sh], 0, nrow - 1)), radius[sh])
            low_half = outer_grid[:, :max(nrow // 2, 1)]   # the bell's flare, not a bulge under the waist
            widest = low_half.argmax(1); wmax = low_half.max(1)
            legs_cell = (np.arange(nrow)[None, :] < widest[:, None]) & (outer_grid < a.hem_ratio * wmax[:, None])
            in_band = (hrow >= 0) & (hrow < nrow)
            leg = np.zeros(len(cm), bool)
            leg[in_band] = legs_cell[acol[in_band], hrow[in_band]]
            region &= ~leg
            rule_counts_hem = int((shell & leg).sum())
        first = region & shell
        rule_counts = {'shell': int(shell.sum()), 'hands_left_out': int((shell & hands).sum()), 'reference_from_shell': int(mid.sum()),
                       'hanging_below_hem_left_out': int((shell & hangs).sum())}
        if a.hem_ratio is not None:
            rule_counts['below_hem_left_out'] = rule_counts_hem
        if a.hand_bump:
            rule_counts['hand_bump_left_out'] = rule_counts_bump
        if a.hand_dark:
            rule_counts['hand_dark_left_out'] = rule_counts_dark

    garment = first.copy()
    _, vidx = cKDTree(cm[co]).query(cm, k=a.vote)
    vidx = opaque_idx[vidx]
    for _ in range(a.vote_passes):
        share = garment[vidx].mean(1)
        garment = region & np.where(share > 0.5, True, np.where(share < 0.2, False, garment))

    pts = cm[garment]
    pairs = cKDTree(pts).query_pairs(a.cell, output_type='ndarray')
    graph = coo_matrix((np.ones(len(pairs)), (pairs[:, 0], pairs[:, 1])), shape=(len(pts), len(pts)))
    count, comp = connected_components(graph, directed=False)
    sizes = np.bincount(comp, minlength=count)
    labels = np.zeros(len(cm), bool)
    keep = sizes >= a.speck
    if a.largest:
        keep = np.arange(count) == np.argmax(sizes)
    labels[np.nonzero(garment)[0][keep[comp]]] = True

    surfaces = None
    if a.around and a.backing:   # closed backing and lining surfaces inside the garment's own outer surface
        from scipy.ndimage import median_filter
        ang = np.arctan2(cm[:, 0] - centre[0], cm[:, 2] - centre[1])
        ai = np.floor((ang + np.pi) / (2 * np.pi) * 180).astype(int) % 180
        y0 = a.lowest
        hi_ = np.floor((y - y0) / 0.01).astype(int)
        top_y = max(a.below, a.backing_top) if a.backing_top is not None else a.below
        nh = int(np.ceil((top_y - y0) / 0.01)) + 1
        sel = labels & co & (hi_ >= 0) & (hi_ < nh)
        grid = np.full((180, nh), np.nan)
        order = np.lexsort((radius[sel], hi_[sel], ai[sel]))
        ka, kh, kr = ai[sel][order], hi_[sel][order], radius[sel][order]
        key = ka * nh + kh
        st = np.r_[0, np.nonzero(np.diff(key))[0] + 1]; en = np.r_[st[1:], len(key)]
        grid.flat[key[st]] = kr[st + np.floor(0.9 * (en - st - 1)).astype(int)]
        filled = ~np.isnan(grid)
        lo_h = np.where(filled.any(1), np.argmax(filled, 1), nh)
        hi_h = np.where(filled.any(1), nh - 1 - np.argmax(filled[:, ::-1], 1), -1)
        for h in range(nh):   # fill gaps along the circle from the nearest filled angle
            col = grid[:, h]; ok = ~np.isnan(col)
            if ok.any() and not ok.all():
                idx_ok = np.nonzero(ok)[0]
                d = np.abs(np.arange(180)[:, None] - idx_ok[None, :]); d = np.minimum(d, 180 - d)
                col[~ok] = col[idx_ok[np.argmin(d[~ok], 1)]]
        g = np.nan_to_num(grid, nan=0.0)   # wrap round the circle, nearest at the top and bottom
        grid = median_filter(np.concatenate([g[-1:], g, g[:1]]), size=3, mode='nearest')[1:-1]
        top_row = hi_h.copy()
        if a.backing_top is not None:   # continue each column straight up to the backing top
            ht = int(np.floor((a.backing_top - y0) / 0.01))
            for i in range(180):
                if lo_h[i] <= hi_h[i] < ht:
                    grid[i, hi_h[i] + 1:ht + 1] = grid[i, hi_h[i]]
            hi_h = np.where(lo_h <= hi_h, np.maximum(hi_h, ht), hi_h)
        delta = np.zeros_like(grid)
        depth_grid = np.full_like(grid, a.backing)
        tuck = np.zeros_like(grid)
        if a.tuck:   # draw the garment's top in under whatever overlaps it (the base's peplum)
            over = (bm[:, 1] > a.below) & (bm[:, 1] < a.below + 0.04) & (np.abs(bm[:, 0] - centre[0]) < a.hand_x + 0.04)   # not the forearms
            oang = np.arctan2(bm[over, 0] - centre[0], bm[over, 2] - centre[1])
            oai = np.floor((oang + np.pi) / (2 * np.pi) * 180).astype(int) % 180
            orad = np.hypot(bm[over, 0] - centre[0], bm[over, 2] - centre[1])
            pep = np.zeros(180)
            for i in range(180):
                if (oai == i).sum() >= 3:
                    pep[i] = np.percentile(orad[oai == i], 90)
            okp = pep > 0
            if okp.any():
                idx_ok = np.nonzero(okp)[0]; d = np.abs(np.arange(180)[:, None] - idx_ok[None, :]); d = np.minimum(d, 180 - d)
                pep = pep[idx_ok[np.argmin(d, 1)]]
                ys = y0 + (np.arange(nh) + 0.5) * 0.01
                w = np.clip((ys - (a.below - a.tuck)) / a.tuck, 0, 1)
                tuck = np.minimum(pep[:, None] - 0.006 - grid, 0) * w[None, :] * (grid > 0)
        gt = grid + tuck
        if a.clear_base is not None:
            from scipy.ndimage import gaussian_filter, maximum_filter
            bwhite = (bc.min(1) > 0.6) if a.clear_select == 'white' else np.ones(len(bm), bool)
            bwhite &= (bm[:, 1] > y0) & (bm[:, 1] < top_y) & (np.abs(bm[:, 0] - centre[0]) < a.hand_x + 0.04)
            skin = ((bc[:, 0] > 0.6) & (bc[:, 0] > bc[:, 1] + 0.08) & (bc[:, 1] > bc[:, 2]) & (bc[:, 2] < 0.6)
                    & (np.abs(bm[:, 0] - centre[0]) > a.hand_x + 0.04))   # hands hang out beside the hips
            if skin.any():   # white cuffs and lace at the hands are not cloth the skirt must cover
                bwhite &= cKDTree(bm[skin]).query(bm, distance_upper_bound=0.03)[0] == np.inf
            bang = np.arctan2(bm[bwhite, 0] - centre[0], bm[bwhite, 2] - centre[1])
            bai = np.floor((bang + np.pi) / (2 * np.pi) * 180).astype(int) % 180
            bhi = np.clip(np.floor((bm[bwhite, 1] - y0) / 0.01).astype(int), 0, nh - 1)
            brad = np.hypot(bm[bwhite, 0] - centre[0], bm[bwhite, 2] - centre[1])
            bgrid = np.zeros((180, nh))
            bkey = bai * nh + bhi; order = np.lexsort((brad, bkey)); bkey, brad = bkey[order], brad[order]
            st = np.r_[0, np.nonzero(np.diff(bkey))[0] + 1]; en = np.r_[st[1:], len(bkey)]
            keep_cells = (en - st) >= 3
            bgrid.flat[bkey[st][keep_cells]] = brad[(st + np.floor(0.95 * (en - st - 1)).astype(int))][keep_cells]
            bgrid = maximum_filter(np.concatenate([bgrid[-1:], bgrid, bgrid[:1]]), size=3, mode='nearest')[1:-1]
            # the backing sits up to BACKING deep, but no deeper than leaves the base's white cloth CLEAR_BASE inside the lining
            room = gt - bgrid - a.clear_base - a.lining_gap
            depth_grid = np.where(bgrid > 0, np.clip(room, min(0.02, a.backing), a.backing), a.backing)
            pad = 6
            depth_grid = gaussian_filter(np.concatenate([depth_grid[-pad:], depth_grid, depth_grid[:pad]]), 1.0, mode='nearest')[pad:-pad]
            depth_grid = np.where(bgrid > 0, np.minimum(depth_grid, np.clip(room, min(0.02, a.backing), a.backing)), depth_grid)
            need = np.maximum(bgrid + a.clear_base - (gt - depth_grid - a.lining_gap), 0) * (grid > 0)
            pad = 6
            delta = gaussian_filter(np.concatenate([need[-pad:], need, need[:pad]]), 1.5, mode='nearest')[pad:-pad]
            delta = np.minimum(np.maximum(delta, need * 0.8), a.clear_max)
        delta = delta + tuck
        push = np.where(labels, delta[ai, np.clip(hi_, 0, nh - 1)], 0.0)
        surf = grid[ai, np.clip(hi_, 0, nh - 1)]
        near = sel & (radius > surf - 0.01)
        col_sum = {}
        cell_rgb = np.full((180, nh, 3), np.nan)
        nk = ai[near] * nh + hi_[near]; order = np.argsort(nk); nk = nk[order]; sm = smooth[near][order]
        st = np.r_[0, np.nonzero(np.diff(nk))[0] + 1]; en = np.r_[st[1:], len(nk)]
        for s0, e0 in zip(st, en):
            cell_rgb.reshape(-1, 3)[nk[s0]] = np.median(sm[s0:e0], 0)
        from scipy.ndimage import uniform_filter
        okc = ~np.isnan(cell_rgb[..., 0])
        val = np.where(okc[..., None], cell_rgb, 0.0); cnt = okc.astype(float)
        wrap = lambda g: np.concatenate([g[-2:], g, g[:2]])
        sval = np.stack([uniform_filter(wrap(val[..., k]), size=(5, 3), mode='nearest')[2:-2] for k in range(3)], -1)
        scnt = uniform_filter(wrap(cnt), size=(5, 3), mode='nearest')[2:-2]
        cell_rgb = np.where(scnt[..., None] > 1e-6, sval / np.maximum(scnt[..., None], 1e-6), np.nan)
        means, rgbs, kinds = [], [], []
        for i in range(180):
            if lo_h[i] > hi_h[i]:
                continue
            th = (i + 0.5) / 180 * 2 * np.pi - np.pi
            for h in range(lo_h[i], hi_h[i] + 1):
                r0 = grid[i, h] + delta[i, h]
                if h > top_row[i] and a.clear_base is not None:   # extension under the peplum: wrap the base's white cloth
                    r0 = bgrid[i, h] + a.clear_base + depth_grid[i, h] + a.lining_gap if bgrid[i, h] > 0 else 0.0
                if r0 <= 0:
                    continue
                rgb = cell_rgb[i, min(h, top_row[i])] if top_row[i] >= 0 else ref
                if np.isnan(rgb).any():
                    rgb = ref
                dg = depth_grid[i, h]
                for depth, colour, kind in ((dg, rgb * a.backing_colour, 0), (dg + a.lining_gap, ref * a.lining_shade, 1)):
                    rr = max(r0 - depth, 0.0)
                    means.append([centre[0] + rr * np.sin(th), y0 + (h + 0.5) * 0.01, centre[1] + rr * np.cos(th)])
                    rgbs.append(colour); kinds.append(kind)
        labels &= ~(radius < surf - depth_grid[ai, np.clip(hi_, 0, nh - 1)] - a.lining_gap - 0.002)
        surface_cloth = labels & (radius > surf - 0.01)
        push = np.where(labels, push, 0.0)
        surfaces = (np.array(means), np.clip(np.array(rgbs), 0, 1), np.array(kinds))
    # dark lining on the inside: radial depth from the body's vertical axis, per angle-and-height cell
    lab_idx = np.nonzero(labels & co)[0]
    outer = percentile_by_cell(radius[lab_idx], cell[lab_idx], 0.9)   # robust outermost: 90th percentile
    outer_r = np.array([outer.get(c_, -1.0) for c_ in cell.tolist()])
    inner = labels & (radius < outer_r - a.inner_depth)
    own_to_ref = np.linalg.norm(cc - ref, axis=1)
    inner |= labels & (radius < outer_r - a.noise_depth) & (own_to_ref > a.noise_tolerance)
    if a.faint_inner:
        inner |= labels & ~co
    out_splat = dict(cl)
    out_splat['rgb'] = cl['rgb'].copy()
    if surfaces is not None and push.any():   # move the garment out where the base needed room
        radial = np.column_stack([cm[:, 0] - centre[0], np.zeros(len(cm)), cm[:, 2] - centre[1]])
        radial /= np.maximum(np.linalg.norm(radial, axis=1, keepdims=True), 1e-6)
        out_splat['means'] = (cm + push[:, None] * radial).astype(cl['means'].dtype)
    out_splat['rgb'][inner] = (ref * a.inner_shade).astype(cl['rgb'].dtype)
    if surfaces is not None and a.surface_opacity:
        out_splat['opacities'] = cl['opacities'].copy()
        floor = np.log(a.surface_opacity / (1 - a.surface_opacity))
        out_splat['opacities'][surface_cloth] = np.maximum(out_splat['opacities'][surface_cloth], floor)
    if a.inner_opacity:
        out_splat['opacities'] = out_splat.get('opacities', cl['opacities']).copy()
        floor = np.log(a.inner_opacity / (1 - a.inner_opacity))
        out_splat['opacities'][inner] = np.maximum(out_splat['opacities'][inner], floor)
    layer = subset(out_splat, labels)
    # plain cloth patch over the base's tunic back under the mantle
    _, bk = cKDTree(bm).query(bm, k=a.k)
    bsmooth = bc[bk].mean(1)
    tunic = (bsmooth[:, 1] > bsmooth[:, 0] + 0.02) & (bsmooth.mean(1) < 0.5)   # teal, not the bluish-white sleeves
    if a.patch_select == 'light':
        tunic = bsmooth.min(1) > 0.6   # white or cream on every channel, not skin
    elif a.patch_select == 'none':
        tunic = np.zeros(len(bm), bool)
    hem = cm[labels & co, 1].min() if (labels & co).any() else a.patch_low
    join = (bm[:, 1] > a.patch_low) & (bm[:, 1] < (a.below if a.around else a.shoulder)) & tunic
    backing = (bm[:, 1] > hem + 0.01) & (bm[:, 1] <= a.patch_low) if a.backing_shade else np.zeros(len(bm), bool)
    zone = (join | backing) if a.around else (bm[:, 2] < a.back_z) & (np.abs(bm[:, 0]) < a.arm_x) & (join | backing)
    cellp = a.cell / 1.5
    keys = np.floor(bm[zone] / cellp).astype(np.int64)
    _, first_idx = np.unique(keys, axis=0, return_index=True)
    pp = bm[zone][first_idx]
    shade = np.where(bm[zone][first_idx][:, 1] > a.patch_low, a.patch_shade, a.backing_shade)
    out_dir = np.column_stack([pp[:, 0] - centre[0], np.zeros(len(pp)), pp[:, 2] - centre[1]])
    out_dir /= np.maximum(np.linalg.norm(out_dir, axis=1, keepdims=True), 1e-6)
    pp = pp + a.patch_offset * out_dir
    n = len(pp)
    patch = {'means': pp.astype(layer['means'].dtype),
             'scales': np.full((n, 3), np.log(cellp), layer['scales'].dtype),
             'quats': np.tile(np.array([1, 0, 0, 0], layer['quats'].dtype), (n, 1)),
             'opacities': np.full(layer['opacities'].shape[1:] and (n,) + layer['opacities'].shape[1:] or (n,),
                                  np.log(0.97 / 0.03), layer['opacities'].dtype),
             'rgb': (shade[:, None] * ref[None, :]).astype(layer['rgb'].dtype)}
    for key in patch:
        layer[key] = np.concatenate([layer[key], patch[key]])
    n_backing = 0
    if surfaces is not None:
        bmeans, brgb, bkind = surfaces
        nb = len(bmeans); n_backing = nb
        sc = np.log(0.006)
        add = {'means': bmeans.astype(layer['means'].dtype),
               'scales': np.full((nb, 3), sc, layer['scales'].dtype),
               'quats': np.tile(np.array([1, 0, 0, 0], layer['quats'].dtype), (nb, 1)),
               'opacities': np.full((nb,) + layer['opacities'].shape[1:], np.log(0.97 / 0.03), layer['opacities'].dtype),
               'rgb': brgb.astype(layer['rgb'].dtype)}
        for key in add:
            layer[key] = np.concatenate([layer[key], add[key]])
    lining = 0
    if a.outer_weights:
        weight = np.load(a.outer_weights)['weight']
        if len(weight) != len(layer['means']):
            raise SystemExit(f'--outer-weights has {len(weight)} Gaussians, the layer {len(layer["means"])}: '
                             'measure the layer made with the same settings')
        hidden = weight < a.outer_threshold
        if a.inside_weights:
            out_w, in_w = np.load(a.outer_weights), np.load(a.inside_weights)
            t_out = out_w['total'] / len(out_w['cameras'])
            t_in = in_w['total'] / len(in_w['cameras'])
            hidden |= (t_in > 0.002) & (t_in > a.inside_ratio * t_out)
        layer['rgb'][hidden] = (ref * a.lining_shade).astype(layer['rgb'].dtype)
        if a.lining_opacity:
            floor = np.log(a.lining_opacity / (1 - a.lining_opacity))
            layer['opacities'][hidden] = np.maximum(layer['opacities'][hidden], floor)
        lining = int(hidden.sum())
    a.out.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(a.out / 'splat.npz', **{k: layer[k] for k in patch})
    np.savez_compressed(a.out / 'rest.npz', **subset(cl, ~labels))
    np.save(a.out / 'labels.npy', labels)
    kinds = np.concatenate([np.zeros(int(labels.sum()), np.int8), np.full(n, 1, np.int8),
                            (2 + surfaces[2]).astype(np.int8) if surfaces is not None else np.zeros(0, np.int8)])
    np.save(a.out / 'kinds.npy', kinds)
    if a.around:
        np.save(a.out / 'hands.npy', hands)   # CLOTHED's hand Gaussians left out (to render it without them)
    lm = cm[labels & co]
    report = {'clothed': str(a.clothed), 'base': str(a.base),
              'settings': {k: getattr(a, k) for k in ('below', 'lowest', 'far', 'margin', 'tolerance', 'hand_x', 'back_z', 'on_body_low', 'arm_x', 'outside', 'take_behind', 'deep', 'noise_tolerance', 'noise_depth', 'outer_threshold', 'inside_ratio', 'lining_shade', 'lining_opacity', 'shoulder', 'neck_x', 'patch_low',
                                                       'patch_offset', 'patch_shade', 'inner_shade', 'inner_depth', 'k', 'around', 'shell', 'largest', 'patch_select',
                                                       'min_opacity', 'vote', 'vote_passes', 'speck', 'cell')},
              'reference_rgb': [round(float(v), 3) for v in ref], 'reference_samples': int(ref_mask.sum()),
              'clothed_gaussians': len(cm), 'rules': rule_counts, 'first_pass': int(first.sum()), 'after_vote': int(garment.sum()),
              'pieces': int(count), 'pieces_kept': int(keep.sum()),
              'garment_gaussians': int(labels.sum()), 'garment_opaque': int(len(lm)),
              'garment_inner_shaded': int(inner.sum()), 'lining_from_weights': lining, 'join_patch_gaussians': int(n),
              'backing_and_lining_gaussians': n_backing,
              'pushed_out': int((push > 1e-4).sum()) if surfaces is not None else 0,
              'drawn_in': int((push < -1e-4).sum()) if surfaces is not None else 0,
              'push_max_cm': round(float(push.max()) * 100, 2) if surfaces is not None else 0,
              'garment_off_body': int((labels & (dist > a.far)).sum()),
              'garment_on_body': int((labels & (dist <= a.far)).sum()),
              'garment_extent_min': [round(float(v), 3) for v in lm.min(0)] if len(lm) else None,
              'garment_extent_max': [round(float(v), 3) for v in lm.max(0)] if len(lm) else None}
    (a.out / 'report.json').write_text(json.dumps(report, indent=1) + '\n')
    print(json.dumps({k: v for k, v in report.items() if k not in ('settings', 'clothed', 'base')}))


if __name__ == '__main__':
    main()
