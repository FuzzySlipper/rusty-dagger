"""
The arms' pixel paint: a pack set's soft maps redrawn as flat texels in a few hand-set ramps, as the classic sprites were.

The pack's roughness and metal maps say which material each texel is, and its normals, baked over the rest pose, give the
light to paint in: the arms stand with their backs up in that pose, so a light from above falls on the side of the arm the
view sees most. Numpy only, so it runs in Blender and out of it.
"""
import numpy as np


def ramp(*colours):
    return np.array(colours, dtype=np.float32) / 255


# Each material's ramp from dark to light, sRGB 0-1. Skin follows the refreshed paper doll's arm, its shadows warmed.
SKIN = ramp((46, 28, 22), (76, 48, 34), (104, 70, 48), (132, 94, 64), (160, 118, 84), (186, 144, 108), (212, 174, 140))
CLOTH = ramp((44, 37, 37), (68, 57, 52), (92, 79, 68), (118, 103, 85), (142, 126, 103), (166, 150, 123), (190, 176, 146))
LEATHER = ramp((24, 15, 13), (42, 26, 20), (62, 40, 28), (84, 56, 37), (108, 75, 49), (134, 98, 66))
METAL = ramp((30, 30, 34), (54, 54, 58), (84, 84, 88), (118, 118, 120), (156, 156, 154), (200, 200, 192))
RAMPS = (SKIN, CLOTH, LEATHER, METAL)
# The light in the rest pose, from above and a little to the thumbs' side.
LIGHT = np.array((0, -.25, 1), dtype=np.float32) / np.linalg.norm((0, -.25, 1))
# Where a texel's roughness (0-1) stops being skin's or leather's and is cloth's, and how dark a rough texel must stay to be
# leather and not skin.
ROUGH_CLOTH = .8
LEATHER_VALUE = .3
WARM = .1
# How much of a texel's surroundings must be metal for it to be mail.
MAIL_SHARE = .2
# How the tone of a texel is made: the light's share, how far the map's own value and its occlusion move it, and how much
# the light's small forms are sharpened against its large ones.
LIGHT_SHARE = .62
LIGHT_FLOOR = .22
VALUE_SHARE = .14
OCCLUSION = .6
SHARPEN = 1.4
DITHER = .1
# The rings of mail, as steps of the ramp in dotted rows that step sideways: anything bolder draws as a chequerboard.
RINGS = np.array(((1, 0, 0, 0), (-1, 0, -1, 0), (0, 0, 1, 0), (0, -1, 0, -1)))
BAYER = np.array(((0, 8, 2, 10), (12, 4, 14, 6), (3, 11, 1, 9), (15, 7, 13, 5)), dtype=np.float32) / 16 - .47


def area(image, size):
    """`image` (a square, side a multiple of `size`) averaged down to `size`."""
    cell = image.shape[0] // size
    return image.reshape(size, cell, size, cell, -1).mean(axis=(1, 3))


def blur(image, radius):
    """Two box blurs of `radius` texels, wrapping at the edges."""
    for _ in range(2):
        for axis in (0, 1):
            image = sum(np.roll(image, shift, axis) for shift in range(-radius, radius + 1)) / (2 * radius + 1)
    return image


def materials(colour, roughness, metal):
    """Each texel's index into RAMPS, its small islands voted away."""
    soft = blur(colour, 3)
    value = soft.mean(axis=2)
    warmth = soft[..., 0] - soft[..., 2]
    rough = blur(roughness, 2)[..., 0]
    kind = np.ones(value.shape, dtype=int)
    kind[(rough < ROUGH_CLOTH) & (warmth > WARM)] = 0
    kind[value < LEATHER_VALUE] = 2
    for _ in range(2):
        votes = [blur((kind == index).astype(np.float32)[..., None], 1)[..., 0] for index in range(len(RAMPS))]
        kind = np.argmax(votes, axis=0)
    kind[(metal[..., 0] > .5) | (blur(metal, 2)[..., 0] > MAIL_SHARE)] = 3
    return kind


def clusters(steps):
    """`steps` with lone texels gone: each takes the median of itself and its four neighbours, which keeps thin lines."""
    around = [steps] + [np.roll(steps, shift, axis) for shift in (-1, 1) for axis in (0, 1)]
    return np.median(around, axis=0).astype(int)


def paint(colour, occlusion, roughness, metal, normals, size):
    """
    The painted texture at `size`, sRGB 0-1, from square maps that divide evenly by it: `colour` sRGB, the rest 0-1 with one
    channel, `normals` baked in object space as colours, black where no mesh covers.
    """
    colour, occlusion, roughness, metal = (area(image, size) for image in (colour, occlusion, roughness, metal))
    normals = area(normals, size) * 2 - 1
    normals /= np.maximum(np.linalg.norm(normals, axis=2, keepdims=True), 1e-3)
    # The weave and pores go first: at this size they would only flicker between two steps of a ramp.
    light = blur(np.clip(normals @ LIGHT, -1, 1)[..., None] * .5 + .5, 1)
    light = (light + (light - blur(light, 3)) * SHARPEN)[..., 0]
    occlusion = blur(occlusion, 1)
    kind = materials(colour, roughness, metal)
    value = blur(colour @ np.array((.3, .59, .11), dtype=np.float32)[:, None], 3)[..., 0]
    dither = np.tile(BAYER, (size // 4, size // 4))
    out = np.zeros((size, size, 3), dtype=np.float32)
    for index, colours in enumerate(RAMPS):
        here = kind == index
        if not here.any():
            continue
        mean = max(value[here].mean(), .05)
        tone = LIGHT_FLOOR + light * LIGHT_SHARE + (value - mean) / mean * VALUE_SHARE - (1 - occlusion[..., 0]) * OCCLUSION
        steps = np.clip(tone * (len(colours) - 1) + dither * DITHER, 0, len(colours) - 1).round().astype(int)
        steps = clusters(steps)
        if colours is METAL:
            steps = np.clip(steps + np.tile(RINGS, (size // 4, size // 4)) * (blur(metal, 2)[..., 0] > MAIL_SHARE), 0, len(colours) - 1)
        out[here] = colours[steps[here]]
    return out


def cloth(colour):
    """`colour` drawn in the cloth ramp by its value, for the frayed cuffs."""
    value = colour @ np.array((.3, .59, .11), dtype=np.float32)
    return CLOTH[np.clip(value * len(CLOTH), 0, len(CLOTH) - 1).astype(int)]
