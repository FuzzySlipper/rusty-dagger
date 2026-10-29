"""Builds Daggerfall-styled first-person arms from rusty-riders' painted arms (run under Blender).

The source is riders' `painted.glb` (the Hands pack's CAT-rigged arms with the old project's hand-painted texture,
built by rusty-riders' scripts/import-fp-arms.sh). Its texture is two regions: the sleeve in the top part, the glove
in the bottom. This recolours them as Daggerfall draws its first-person arm, a teal sleeve and a bronze gauntlet,
by mapping each region's brightness through a colour ramp, and gives the gauntlet a metallic finish. The pack is a
purchased asset, so the output goes to the ignored local/ folder, never into Git.

usage: blender -b --factory-startup --python scripts/fp-arms/build-arms.py -- PAINTED_GLB OUT_GLB
"""
import sys
from pathlib import Path

import bpy
import numpy as np

source, out = (Path(a) for a in sys.argv[sys.argv.index("--") + 1:])

# The texture row (from the top) where the sleeve region ends and the glove's begins.
GLOVE_FROM = 0.61

# Brightness ramps, dark to light. The sleeve is CIF teal; the gauntlet runs from shadowed bronze to gold highlights.
SLEEVE = [(0.0, (0.02, 0.12, 0.12)), (0.65, (0.12, 0.52, 0.50)), (1.0, (0.36, 0.80, 0.74))]
GLOVE = [(0.0, (0.10, 0.05, 0.015)), (0.45, (0.55, 0.33, 0.11)), (1.0, (1.0, 0.84, 0.48))]


def ramp(lum, stops):
    xs = np.array([s[0] for s in stops])
    return np.stack([np.interp(lum, xs, [s[1][c] for s in stops]) for c in range(3)], axis=-1)


def normalised(lum, mask):
    """Spreads a region's brightness over 0..1 by its own percentiles, so each ramp uses its full range."""
    lo, hi = np.percentile(lum[mask], [2, 98])
    return np.clip((lum - lo) / max(hi - lo, 1e-4), 0, 1)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
for obj in list(bpy.data.objects):
    if obj.type == "MESH" and not obj.material_slots:  # the importer's bone-display shape
        bpy.data.objects.remove(obj, do_unlink=True)

material = bpy.data.materials["Arms_Mesh"]
nodes, links = material.node_tree.nodes, material.node_tree.links
bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
texture = next(n for n in nodes if n.type == "TEX_IMAGE")
image = texture.image
w, h = image.size
px = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)  # rows run bottom-up
rgb = px[..., :3]
lum = rgb @ np.array([0.3, 0.59, 0.11], dtype=np.float32)
saturation = rgb.max(-1) - rgb.min(-1)

glove = np.zeros((h, w), bool)
glove[: int(h * (1 - GLOVE_FROM))] = True  # bottom rows of the picture
sleeve = ~glove
studs = glove & (saturation < 0.06) & (lum > 0.25)  # the knuckle studs stay steel

colour = np.where(glove[..., None], ramp(normalised(lum, glove), GLOVE), ramp(normalised(lum, sleeve), SLEEVE))
colour[studs] = np.clip(rgb[studs] * 1.3, 0, 1)
px[..., :3] = colour
image.pixels[:] = px.ravel()
image.pack()

# glTF metallic-roughness: green is roughness, blue is metalness.
mr = np.ones((h, w, 4), np.float32)
mr[..., 1] = np.where(glove, 0.42, 0.85)
mr[..., 2] = np.where(glove, 0.75, 0.0)
mr[studs, 1], mr[studs, 2] = 0.3, 1.0
maps = bpy.data.images.new("Arms_MetalRough", w, h)
maps.colorspace_settings.name = "Non-Color"
maps.pixels[:] = mr.ravel()
maps.pack()
node = nodes.new("ShaderNodeTexImage")
node.image = maps
node.interpolation = texture.interpolation
split = nodes.new("ShaderNodeSeparateColor")
links.new(node.outputs["Color"], split.inputs["Color"])
links.new(split.outputs["Green"], bsdf.inputs["Roughness"])
links.new(split.outputs["Blue"], bsdf.inputs["Metallic"])

out.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out), export_format="GLB", export_animation_mode="ACTIONS",
                          export_image_format="AUTO", export_yup=True)
print("BUILT", out)
