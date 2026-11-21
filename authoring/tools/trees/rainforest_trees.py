"""Archive 500's vine trees, tufted and fruiting stems, and the tree rooted over stone.

Recipes trace the refreshed painting in fractional image coordinates (x across, y down).
Depth and radius are in shares of the painting's width. Branches are continuous tubes;
crowns are clusters of fixed curved sprays or folded fronds radiating from a hub.
"""
import math

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt

from rainforest_palms import add_grid, colour_ramp, frond_atlas, part


FORMS = {3: "vine-tree", 13: "vine-tree", 16: "tufted-tree", 18: "buttress-tree",
         21: "blue-fruited-palm", 25: "rock-rooted-tree", 27: "blue-fruited-palm", 30: "tiered-broadleaf"}

# x, y, z, radius. The first ring is buried, the last reaches inside the crown.
STEMS = {
    3: ((.64, 1, 0, .20), (.60, .87, .01, .13), (.56, .78, .04, .07), (.59, .73, .05, .065),
        (.46, .66, 0, .07), (.45, .62, -.04, .066), (.55, .55, -.07, .063), (.58, .49, -.035, .055),
        (.51, .43, .035, .041), (.54, .36, .055, .030), (.52, .27, 0, .048), (.49, .16, -.01, .027)),
    13: ((.63, 1, 0, .17), (.63, .87, .02, .10), (.59, .75, .06, .064), (.52, .70, .055, .066),
         (.54, .65, 0, .062), (.61, .59, -.05, .057), (.60, .52, -.08, .057), (.65, .46, -.04, .061),
         (.61, .41, .035, .053), (.55, .36, .055, .046), (.54, .25, .03, .041), (.52, .13, 0, .038)),
    16: ((.48, 1, 0, .19), (.48, .87, .01, .125), (.46, .76, .045, .088), (.47, .68, .055, .082),
         (.38, .61, .025, .068), (.34, .55, -.03, .060), (.39, .50, -.05, .062),
         (.45, .46, -.025, .065), (.38, .39, .015, .054), (.41, .31, 0, .048)),
    18: ((.46, 1, 0, .16), (.45, .85, .015, .13), (.47, .70, .025, .087), (.47, .51, 0, .080),
         (.46, .41, -.01, .085), (.43, .31, -.04, .043), (.43, .18, -.06, .022)),
    21: ((.66, 1, 0, .089), (.63, .86, -.015, .069), (.60, .73, -.05, .054), (.52, .61, -.067, .049),
         (.48, .54, -.03, .04), (.51, .43, .022, .039), (.56, .34, .035, .033), (.58, .21, 0, .043)),
    25: ((.57, .70, 0, .13), (.56, .62, .005, .115), (.61, .55, .04, .073),
         (.63, .47, .03, .065), (.61, .39, -.01, .060), (.58, .30, -.025, .047), (.56, .18, 0, .021)),
    27: ((.64, 1, 0, .095), (.61, .88, .012, .065), (.58, .76, -.025, .048), (.48, .64, -.057, .046),
         (.45, .56, -.070, .040), (.48, .49, -.052, .035), (.53, .41, 0, .035), (.58, .31, .035, .034),
         (.58, .21, 0, .042)),
    30: ((.51, 1, 0, .20), (.52, .91, .015, .159), (.51, .81, .035, .122), (.49, .67, .032, .101),
         (.49, .54, .019, .09), (.47, .40, -.006, .071), (.50, .28, -.02, .050), (.49, .12, 0, .020)),
}

# Each crown has an attachment on the stem, a centre, and horizontal/vertical radii.
# Small separate tufts on the twisted trunks are as important to their outline as the top.
CROWNS = {
    3: ((.27, .46, .145, .49, .15),),
    13: ((.26, .49, .108, .52, .109), (.36, .67, .32, .135, .052), (.62, .49, .563, .14, .052)),
    18: ((.43, .46, .11, .32, .12, -.16), (.48, .19, .23, .23, .16, .10), (.47, .78, .208, .26, .14, -.08),
         (.48, .24, .405, .17, .072, -.22), (.42, .75, .37, .18, .084, .22)),
    25: ((.46, .47, .16, .46, .16), (.42, .74, .305, .235, .08, -.12),
         (.54, .75, .465, .225, .108, .07), (.62, .43, .494, .195, .101, .15)),
    # Broad overlapping upper tiers stay full around the trunk. The two smaller
    # low boughs leave daylight at the lower fork, as in the painting.
    30: ((.29, .51, .105, .305, .124, .025), (.38, .46, .226, .388, .124, -.050),
         (.47, .54, .346, .431, .112, .075), (.54, .45, .478, .423, .085, -.026),
         (.63, .315, .595, .140, .050, -.105), (.605, .704, .553, .169, .054, .106)),
}


# Cross-sections shared by the rock and the clinging roots of record 25.
STONE_DOWN = (.49, .55, .65, .76, .89, .97, 1.003)
STONE_WIDTH = (.035, .20, .31, .34, .31, .29, .245)


def filled(pixels, valid):
    """Extend the paint through transparent margins so a curved surface never samples black padding."""
    nearest = distance_transform_edt(~valid, return_distances=False, return_indices=True)
    result = pixels[tuple(nearest)].copy()
    result[..., 3] = 255
    return Image.fromarray(result)


class Tree:
    def __init__(self, record, rgba, stem=None):
        image = Image.fromarray(rgba.astype(np.uint8))
        self.pixels = np.asarray(image.crop(image.getbbox()))
        h, w = self.pixels.shape[:2]
        self.aspect = w / h
        self.stem = np.array(STEMS[record] if stem is None else stem)
        self.foot_x = self.stem[0, 0]
        self.rng = np.random.default_rng(record)
        self.bark, self.leaves = part("bark"), part("leaves")
        self.patches = []

    def point(self, x, y, z):
        return np.array([(x - self.foot_x) * self.aspect, 1 - y, z * self.aspect])

    def uv(self, vertices):
        return np.stack([self.foot_x + vertices[..., 0] / self.aspect, 1 - vertices[..., 1]], axis=-1).clip(.002, .998)

    def painted_grid(self, out, grid, centres, depth_scale=1):
        """Use the painting on the nearest facing plane of each quad. A front projection alone
        turns the entire side of a crown into horizontal streaks. Split UVs only at plane changes;
        keep the continuous normals of the ring surface across those seams.
        """
        front = self.uv(grid)
        side, top = front.copy(), front.copy()
        depth = grid[..., 2] - centres[:, None, 2]
        side[..., 0] = self.foot_x + (centres[:, None, 0] + depth) / self.aspect
        top[..., 1] = 1 - centres[:, None, 1] - depth * depth_scale
        charts = [side.clip(.002, .998), top.clip(.002, .998), front]
        patch = part(out["role"])
        add_grid(patch, grid, front, seam=True)
        pos = np.array(patch["positions"]).reshape(-1, 3)
        ids = {}
        for quad in np.array(patch["indices"]).reshape(-1, 6):
            a, b, c = pos[quad[:3]]
            axis = np.argmax(np.abs(np.cross(b-a, c-a)))
            for vertex in quad:
                key = (vertex, axis)
                if key not in ids:
                    ids[key] = len(out["positions"]) // 3
                    out["positions"].extend(pos[vertex].tolist())
                    out["normals"].extend(patch["normals"][3*vertex:3*vertex+3])
                    out["colors"].extend(patch["colors"][4*vertex:4*vertex+4])
                    out["uvs"].extend(charts[axis].reshape(-1, 2)[vertex].round(6).tolist())
                out["indices"].append(ids[key])

    def stem_at(self, y):
        path = self.stem[::-1]
        return self.point(np.interp(y, path[:, 1], path[:, 0]), y, np.interp(y, path[:, 1], path[:, 2]))

    def cap(self, out, ring, direction):
        """Close exposed ends with an outward fan, independent of the texture seams."""
        start = len(out["positions"]) // 3
        rim = ring[:-1]
        centre = rim.mean(axis=0)
        vertices = np.vstack([centre, rim])
        out["positions"].extend(vertices.round(6).ravel().tolist())
        out["normals"].extend(direction.round(6).tolist() * len(vertices))
        out["uvs"].extend(self.uv(vertices).round(6).ravel().tolist())
        out["colors"].extend([1, 1, 1, 0] * len(vertices))
        for i in range(len(rim)):
            out["indices"].extend([start, start + 1 + (i+1) % len(rim), start + 1 + i])

    def tube(self, centres, radii, sides, *, cap=False, root_grain=False, wrap_height=None):
        """Transport the ring frame along the branch, including around sideways bends."""
        centres = np.array(centres)
        tangent = np.gradient(centres, axis=0)
        tangent /= np.linalg.norm(tangent, axis=1, keepdims=True)
        axis = np.eye(3)[np.argmin(np.abs(tangent[0]))]
        rows = []
        angles = np.linspace(0, math.tau, sides + 1)[:, None]
        for p, direction, radius in zip(centres, tangent, radii):
            axis = axis - direction * np.dot(axis, direction)
            axis /= np.linalg.norm(axis)
            ring = np.cos(angles) * axis + np.sin(angles) * np.cross(axis, direction)
            rows.append(p + ring * radius * self.aspect)
        grid = np.array(rows)
        if wrap_height is not None:
            uv = np.zeros((*grid.shape[:2], 2))
            uv[..., 0] = angles[:, 0] / math.tau
            uv[..., 1] = np.clip(1 - grid[..., 1] / wrap_height, .002, .998)
            add_grid(self.bark, grid, uv, seam=True)
        elif root_grain:
            # Roots on rock need bark all around, including where the front painting
            # shows bare stone. Carry the exposed trunk grain down the root.
            uv = []
            for down in np.linspace(.41, .58, len(centres)):
                axis_x = self.uv(self.stem_at(down))[0]
                across = axis_x + .053 * np.sin(angles[:, 0] + .5)
                uv.append(np.stack([across, np.full_like(across, down)], axis=-1))
            add_grid(self.bark, grid, uv, seam=True)
        else:
            self.painted_grid(self.bark, grid, centres)
        if cap:
            self.cap(self.bark, grid[-1], tangent[-1])

    def roots(self, record):
        # Buttresses descend from the trunk and run out into the soil. On 25 they follow
        # the outside of the boulder before turning back toward its foot.
        if record == 25:
            for i in range(7):
                angle = math.tau * (i + .12) / 7
                levels = (.50, .55, .64, .73, .83, .94)
                centres = [self.stem_at(levels[0])]
                for j, down in enumerate(levels[1:]):
                    az = angle + .22 * math.sin(i + j)
                    rx = np.interp(down, STONE_DOWN, STONE_WIDTH)
                    centres.append(self.point(.51 + rx * math.cos(az), down, -.025 + .79 * rx * math.sin(az)))
                self.tube(centres, [.058, .054, .041, .030, .019, .005], 5, root_grain=True)
            return
        spread = {3: .45, 13: .34, 16: .36, 18: .41, 21: .20, 27: .20, 30: .45}[record]
        count = 4 if record in (21, 27) else 7
        self.root_flare(spread, count, shoulder=.69 if record == 30 else .81)

    def root_flare(self, spread, count, shoulder=.81, wrap_height=None):
        for i in range(count):
            angle = math.tau * (i + self.rng.uniform(-.20, .20)) / count
            ray = np.array([math.cos(angle), 0, math.sin(angle)]) * self.aspect
            length = spread * self.rng.uniform(.80, 1.12)
            start = self.stem_at(shoulder)
            foot = self.point(self.foot_x, 1, 0)
            centres = [start, self.stem_at(.90) + ray * length * .34,
                       foot + ray * length * .64 + [0, .035, 0], foot + ray * length]
            self.tube(centres, np.linspace(.034 if count == 4 else .055, .008, len(centres)), 4, wrap_height=wrap_height)

    def vine(self, record):
        # A secondary strand winds in and out of sight around the bending centreline.
        for phase in (0, math.pi):
            points, radii = [], []
            for t in np.linspace(0, 1, 14):
                y = .91 - t * (.61 if record == 3 else .67)
                theta = phase + t * math.tau * 1.8
                stem_r = np.interp(y, self.stem[::-1, 1], self.stem[::-1, 3])
                offset = (stem_r + .014 + .027 * math.sin(math.pi * t)) * self.aspect
                points.append(self.stem_at(y) + offset * np.array([math.cos(theta), 0, math.sin(theta)]))
                radii.append(.022 - .011 * t)
            self.tube(points, radii, 4)

    def fruit(self, centre, radius):
        theta = np.linspace(0, math.tau, 6)
        rows = []
        for pitch in (-1.49, -.14, 1.49):
            w = np.cos(pitch)
            rows.append(centre + np.stack([w*np.cos(theta), np.full_like(theta, np.sin(pitch)),
                                          w*np.sin(theta)], axis=1) * radius)
        # Last atlas cell is the painted blue skin. It wraps around each fruit.
        u, v = np.meshgrid(np.linspace(129/192, 191/192, len(theta)), np.linspace(.97, .03, len(rows)))
        add_grid(self.leaves, np.array(rows), np.stack([u, v], axis=-1), seam=True)

    def leaf_cluster(self, node, width, height, phase):
        # One source patch per branch end: low, shaded boughs have different paint and
        # gaps from the sunlit top. The atlas is packed after all clusters are placed.
        tile = self.paint_cluster(node, width, height)
        index = len(self.patches)
        self.patches.append(tile)
        # Three fixed, dished sprays around a branch end. Their tips hang beyond the
        # solid painted centre, leaving leaf-shaped gaps in the outline from every yaw.
        for face in range(3):
            angle = phase + face * math.tau / 3
            ray = np.array([math.cos(angle), 0, math.sin(angle)])
            side = np.cross(ray, [0, 1, 0])
            rows, uv = [], []
            for t, reach, y in ((0, -.38, .87), (.49, .57, .38), (1, .94, -.85)):
                centre = node + ray * width * reach + [0, y * height, 0]
                span = width * (.69 + .34 * math.sin(math.pi*t))
                rows.append([centre + side * k * span - ray * k*k * width * .28
                             - [0, k*k * height * .25, 0] for k in (1, 0, -1)])
                uv.append([[(index % 8 + u)/8, index // 8 + .01 + .98*t] for u in (.005, .5, .995)])
            add_grid(self.leaves, np.array(rows), uv, double=True)

    def boulder(self):
        stone = part("stone")
        angles = np.linspace(0, math.tau, 11)
        wave = 1 + .062 * np.sin(3 * angles + .6) + .037 * np.cos(5 * angles)
        centres, rings = [], []
        for down, radius in zip(STONE_DOWN[::-1], STONE_WIDTH[::-1]):
            centre = self.point(.51, down, -.025)
            centres.append(centre)
            rings.append(centre + self.aspect * radius * np.stack(
                [wave * np.cos(angles), .045 * np.sin(3 * angles), .79 * wave * np.sin(angles)], axis=-1))
        self.painted_grid(stone, np.array(rings), np.array(centres))
        self.cap(stone, rings[-1], np.array([0., 1., 0.]))
        return stone

    def crown_core(self, centre, width, height):
        """A shaded mass under the top sprays. It keeps an upward view from exposing their backs."""
        rgb = self.pixels[..., :3].astype(float)
        green = (self.pixels[..., 3] > 128) & (rgb[..., 1] > rgb[..., 0]) & (rgb[..., 1] > rgb[..., 2])
        image = filled(self.pixels, green)
        u, v = self.uv(centre)
        w, h = image.size
        box = (round((u-width*.42)*w), round((v-height*.7)*h),
               round((u+width*.42)*w), round((v+height*.3)*h))
        tile = image.crop(box).resize((64, 56), Image.Resampling.NEAREST)
        index = len(self.patches)
        self.patches.append(tile)
        ring = np.linspace(0, math.tau, 9)
        rows, uv = [], []
        for y, radius in ((-.54, .19), (-.22, .65), (.27, .73), (.60, .53), (.73, .035)):
            rows.append(centre + np.stack([width*self.aspect*radius*np.cos(ring), np.full_like(ring, height*y),
                                          width*self.aspect*radius*.88*np.sin(ring)], axis=-1))
            # A ping-pong wrap keeps some texture width on the sides as well as the front.
            across = .08 + .84 * abs((ring/math.pi + .25) % 1 * 2 - 1)
            uv.append(np.stack([(index % 8 + across)/8, np.full_like(ring, index//8 + .55 - .55*y)], axis=-1))
        add_grid(self.leaves, np.array(rows), uv, seam=True)

    def crown(self, attachment, x, y, width, height, depth=0, *, origin=None, limb_scale=1):
        if origin is None:
            origin = self.stem_at(attachment)
        centre = self.point(x, y, depth)
        large = width > .30
        count = 5 if large else 2
        offset = self.rng.uniform(0, math.tau)
        fork = centre - [0, height*.40, 0]
        elbow = origin + (fork - origin) * .58 - [0, height*.15, 0]
        self.tube([origin, elbow, fork], np.array([min(.053, width*.22), min(.036, width*.15), .022]) * limb_scale, 6)
        if large:
            self.crown_core(centre, width, height)
        for i in range(count + 1):
            angle = offset + math.tau * i / count
            radial = np.array([math.cos(angle), 0, .86 * math.sin(angle)]) * self.aspect
            core = i == count
            node = centre + (0 if core else .54 * width) * radial
            node[1] += height * (.19 if core else self.rng.uniform(-.29, .20))
            self.tube([fork, node], np.array([min(.030, width*.13), .009]) * limb_scale, 5)
            self.leaf_cluster(node, width * self.aspect * .61, height * 1.09, angle)


    def paint_cluster(self, node, width, height):
        w, h = self.pixels.shape[1], self.pixels.shape[0]
        u, v = self.uv(node)
        box = (round((u-width/self.aspect)*w), round((v-height)*h),
               round((u+width/self.aspect)*w), round((v+height)*h))
        crop = Image.fromarray(self.pixels).crop(box).resize((64, 56), Image.Resampling.NEAREST)
        patch = np.array(crop)
        rgb = patch[..., :3].astype(float)
        # Keep the places the painting leaves open between branches. Wood has its own mesh.
        leaf = (patch[..., 3] > 128) & (rgb[..., 1] >= rgb[..., 0]*.97)
        edge = Image.new("L", crop.size)
        draw = ImageDraw.Draw(edge)
        for ring, count in ((1, 18), (.61, 12), (.24, 5)):
            for i in range(count):
                a = (i + self.rng.uniform(-.25, .25)) * math.tau / count
                x, y = 31 + ring*23*math.cos(a), 26 + ring*18*math.sin(a)
                rx, ry = self.rng.uniform(4, 9), self.rng.uniform(5, 10)
                draw.ellipse((x-rx, y-ry*.65, x+rx, y+ry), fill=255)
                draw.polygon([(x-rx, y), (x-rx*.52, y+ry+3), (x+rx*.38, y)], fill=255)
        patch[..., 3] = leaf * np.asarray(edge)
        return Image.fromarray(patch)

    def cluster_atlas(self):
        rows = math.ceil(len(self.patches)/8)
        atlas = Image.new("RGBA", (8*64, rows*56))
        for i, tile in enumerate(self.patches):
            atlas.paste(tile, (i % 8 * 64, i // 8 * 56))
        uv = self.leaves["uvs"]
        uv[1::2] = [round(v/rows, 6) for v in uv[1::2]]
        return atlas


def blade_atlas(tree, feathered):
    pixels = tree.pixels.astype(float)
    rgb = pixels[..., :3]
    greens = rgb[(pixels[..., 3] > 128) & (rgb[..., 1] > rgb[..., 0] * 1.05) & (rgb[..., 1] > rgb[..., 2])]
    atlas = Image.new("RGBA", (192, 96))
    draw = ImageDraw.Draw(atlas)
    if feathered:
        fronds = frond_atlas(pixels, tree.rng)
        atlas.paste(fronds.crop((0, 0, 128, 128)).resize((128, 96), Image.Resampling.NEAREST))
    else:
        for cell in (0, 1):
            ramp = colour_ramp(greens, (.10, .31, .54, .79, .93))
            cx = 32 + 64 * cell
            # A long solid blade with a bright crease and a few splits in the ragged tip.
            left, right = [(cx, 95)], [(cx + 1, 95)]
            for i in range(1, 14):
                t = i / 14
                radius = 28 * math.sin(math.pi * t) ** .74
                left.append((cx - radius * tree.rng.uniform(.78, 1), 95 - 91 * t))
                right.append((cx + radius * tree.rng.uniform(.78, 1), 95 - 91 * t))
            tip = (cx + (6 if cell else -5), 1)
            draw.polygon(left + [tip] + right[::-1], fill=ramp[2 + cell])
            draw.polygon(left + [tip, (cx - 2, 41), (cx + 2, 67)], fill=ramp[cell])
            draw.line([(cx, 93), (cx + 1, 60), (cx - 2, 34), tip], fill=ramp[3 + cell], width=2)
            # Sparse painted marks break the long plane of the blade.
            for _ in range(28):
                y = tree.rng.uniform(18, 81)
                dx = tree.rng.uniform(-.70, .70) * 27 * math.sin(math.pi*y/96)
                x = cx + dx
                draw.line([(x, y), (x + dx*.14, y - tree.rng.uniform(2, 6))],
                          fill=ramp[int(tree.rng.integers(1, 5))], width=2)
            for j in range(3):
                side = 1 if j % 2 else -1
                y = 15 + j * 13
                draw.polygon([(cx + side * 32, y - 10), (cx + side * 9, y + 13),
                              (cx + side * 32, y + 2)], fill=(0, 0, 0, 0))
    blue = rgb[(pixels[..., 3] > 128) & (rgb[..., 2] > rgb[..., 1] * 1.12) & (rgb[..., 2] > rgb[..., 0] * 1.30)]
    if len(blue):
        ramp = colour_ramp(blue, (.10, .38, .67, .90, .99))
        draw.rectangle((128, 0, 191, 95), fill=ramp[1])
        draw.polygon([(128, 24), (149, 10), (186, 13), (182, 48), (154, 65), (128, 60)], fill=ramp[2])
        draw.polygon([(140, 31), (154, 17), (171, 19), (168, 37), (147, 43)], fill=ramp[4])
        draw.polygon([(128, 78), (153, 85), (191, 65), (191, 95), (128, 95)], fill=ramp[0])
    return atlas


def rosette(tree, hub, reach, lift, fall, width, count):
    for i in range(count):
        az = (i + tree.rng.uniform(-.23, .23)) * math.tau / count
        ray = np.array([math.cos(az), 0, math.sin(az)])
        side = np.cross(ray, [0, 1, 0])
        length = reach * tree.aspect * tree.rng.uniform(.78, 1.10)
        end = fall * tree.rng.uniform(.42, 1.12)
        if reach < .32 and i % 3 == 0:
            end *= -.6
        curve = lift * tree.rng.uniform(.50, 1.14)
        twist = tree.rng.uniform(-.09, .09) * tree.aspect
        rows, uv = [], []
        steps = (0, .32, .65, 1) if reach < .32 else (0, .24, .50, .76, 1)
        for t in steps:
            spine = hub + ray * length * (1 - (1-t)**1.35) + side * twist * t*t + [0, curve * 4*t*(1-t) - end*t*t, 0]
            span = width * tree.aspect * (.42 + .58 * math.sin(math.pi*t))
            rows.append([spine + k*span*side - [0, abs(k)*span*.55, 0] for k in (1, 0, -1)])
            uv.append([[((i % 2)*64 + 32 - 31*k)/192, .99 - .98*t] for k in (1, 0, -1)])
        add_grid(tree.leaves, np.array(rows), uv, double=True)


def build(record, rgba):
    tree = Tree(record, rgba)
    tree.tube([tree.point(x, y, z) for x, y, z, _ in tree.stem], tree.stem[:, 3], 9, cap=True)
    tree.roots(record)
    parts = [tree.bark, tree.leaves]
    wood = tree.pixels[..., 3] > 128
    if record in (18, 25, 30):
        rgb = tree.pixels[..., :3].astype(float)
        brown = wood & (rgb[..., 0] > rgb[..., 1]) & (rgb[..., 1] >= rgb[..., 2])
        wood &= brown | ((distance_transform_edt(~brown) <= 2) & (rgb.max(axis=2) < 47))
    images = {"bark": filled(tree.pixels, wood)}
    if record in (3, 13):
        tree.vine(record)
    if record in CROWNS:
        for crown in CROWNS[record]:
            tree.crown(*crown)
        images["leaves"] = tree.cluster_atlas()
    else:
        hub = tree.stem_at(tree.stem[-1, 1])
        rosette(tree, hub, .56, .11, .16, .063 if record == 16 else .126, 12 if record == 16 else 8)
        if record == 16:
            rosette(tree, hub, .37, .092, -.23, .050, 8)
        else:
            rosette(tree, hub, .39, .15, .030, .085, 4)
        if record == 16:
            for y, x, z in ((.54, .28, .055), (.67, .61, -.035)):
                tip = tree.point(x, y, z)
                tree.tube([tree.stem_at(y + .09), tip], [.046, .032], 6)
                rosette(tree, tip, .27, .116, .078, .054, 8)
        else:
            # Blue fruits set around the hub, half-hidden under the green blades.
            for i in range(7):
                a = i * math.tau / 7
                centre = hub + np.array([.109*math.cos(a)*tree.aspect, .023 - .028*(i % 3), .109*math.sin(a)*tree.aspect])
                tree.fruit(centre, np.array([.043 * tree.aspect, .037, .043 * tree.aspect]) * tree.rng.uniform(.84, 1.12))
            if record == 21:
                rosette(tree, tree.stem_at(.46), .23, .086, .11, .05, 7)
        images["leaves"] = blade_atlas(tree, feathered=record != 16)
    if record == 25:
        parts.append(tree.boulder())
        rgb = tree.pixels[..., :3].astype(float)
        dark = (tree.pixels[..., 3] > 128) & (rgb[..., 2] >= rgb[..., 0] * .98)
        dark[:int(.55 * len(rgb))] = False
        images["stone"] = filled(tree.pixels, dark)
    height = max(max(p["positions"][1::3]) for p in parts)
    return {"height": height, "parts": parts}, images
