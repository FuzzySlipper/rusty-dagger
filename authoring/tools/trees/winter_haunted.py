"""The bare haunted tree, with rounded snow deposits on its own branch surfaces."""
import math

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt, map_coordinates

from rainforest_trees import Tree, filled
from winter_oak import traced_limbs, wood_path
from winter_woodland import snow_mask


FORMS = {13: "snow-laden-twisted-tree"}


class FrostedWood:
    def __init__(self, tree):
        self.snow = snow_mask(tree.pixels)
        self.inside = distance_transform_edt(self.snow)
        self.outside = distance_transform_edt(~self.snow)

    def limb(self, tree, centres, radii, sides, *, turn=0):
        h, w = self.snow.shape
        points, widths = [centres[0]], [radii[0]]
        for p, q, r0, r1 in zip(centres, centres[1:], radii, radii[1:]):
            x, y = (tree.uv((p+q)*.5)*[w-1, h-1]).astype(int)
            length = np.linalg.norm(q-p)
            # Give a drift a rounded shoulder even midway along an otherwise
            # straight, tapered limb. Thin, bare tips retain their sparse rings.
            steps = 1
            if self.outside[y, x] < max(r0, r1)*w+length*h*.5:
                steps = min(5, max(1, math.ceil(length/max(.021, (r0+r1)*tree.aspect*.35))))
            for i in range(1, steps+1):
                t = i/steps
                points.append(p+(q-p)*t)
                widths.append(r0+(r1-r0)*t)
        first = len(tree.bark["positions"])//3
        wood_path(tree, points, widths, sides, turn=turn)
        uv = np.array(tree.bark["uvs"][first*2:]).reshape(-1, 2)
        distance = map_coordinates(self.inside, [uv[:, 1]*(h-1), uv[:, 0]*(w-1)], order=1)
        normals = np.array(tree.bark["normals"][first*3:]).reshape(-1, 3)
        positions = np.array(tree.bark["positions"][first*3:]).reshape(-1, 3)
        lift = np.minimum(distance, 5)*(.24+.76*np.maximum(normals[:, 1], 0))/h
        positions += lift[:, None]*(.65*normals+[0, .35, 0])
        tree.bark["positions"][first*3:] = positions.round(6).ravel().tolist()


def build(record, rgba):
    tree = Tree(record, rgba, ((.67, 1, 0, .11), (.45, .40, 0, .07)))
    frost = FrostedWood(tree)
    traced_limbs(tree, draw=frost.limb, spread_forks=True)
    # At the silhouette a side face may sample beyond the original stroke.
    # Extend wood grain into that margin while keeping every painted snow patch.
    solid = tree.pixels[..., 3] > 128
    texture = np.array(filled(tree.pixels, solid & ~frost.snow))
    texture[solid] = tree.pixels[solid]
    return {"height": max(tree.bark["positions"][1::3]), "parts": [tree.bark]}, {
        "bark": Image.fromarray(texture)}
