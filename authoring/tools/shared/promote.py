#!/usr/bin/env python3
"""Promote ledger-accepted finals into the tracked refreshed-art content pack.

Each accepted final in local/art/generated/AREA stands in for one classic media id, which the game draws in its place
when the refreshed art set is chosen (classic stays selectable). For each final this:

  - finds the classic media id it replaces, from its art key: a worn layer `texture-A-record-R-frame-000` is
    the paper doll's `paper-doll.A.R`, a design of its own `texture-NAME-record-R-frame-000` over a classic record
    another item keeps is `paper-doll.NAME.R` (the template's refreshedArt), a body `bodies--bodyGNi{0,1}--...` is
    `character.body-{unclothed,clothed}.{gender}.0N.0`, a head `faces--faceGNi0--...-record-RRR-...` is
    `character.head.{gender}.0N.R`, and a vampire head, beast head or beast doll `creatures--NAMEi0--...-record-RRR-...`
    is `character.FAMILY.NAMEi0.R`, FAMILY being the name's first four letters (vamp, were, wolf, boar), and a race
    background `backgrounds--scbgNNi0--...` is `character.scbg.scbgNNi0.0`;
  - takes its doll-space rect from its sidecar, as doll_preview.py draws it, crops transparent margins and moves
    the rect to match;
  - writes it as WebP (lossy colour at QUALITY, lossless alpha) to content/worldrpg/packs/daggerfall.art-replacements/media/AREA/;
  - records it in the pack payload's `artReplacements` section with its size, bytes and hash;
  - for clothing (archives 235-242), and for armour and weapons the classic draws in other metals, also writes
    its dye mask: the neutral cloth or bare steel the classic dye or metal recolours, where the final's colour is
    near grey (saturation under NEUTRAL) and not an outline (DARK), less what is coloured of its own (a green
    vest, a leather belt, a gold buckle). The game multiplies that mask's pixels by the worn dye's or metal's tint.

The pack also states each clothing dye's and metal's tint (`dyeTints`): the colour, and a brightness the multiply
cannot reach on its own, that turn the neutral cloth or steel into the classic dye or metal, measured as the mean
colour of the pixels each classic dye or metal recolours against the mean of the final's neutral pixels (read from
the import's published paper-doll images under local/build/import/staging).

Two areas promote icons rather than their own finals, each standing in for a classic UI image:

  - `items`: the inventory images of the templates the paper doll does not draw (`item.ARCHIVE.RECORD`, named in
    the staged `paperDoll.icons` section), each from the accepted `items` final of its record, and every icon the
    classic media publishes for an item without a template (`inventoryIcons`, `inventory.icon.ITEM`: the potion
    recipes' bottles and gold) from the accepted final of the record it draws. A worn item's icon is its paper-doll
    image, which the `worn` area already replaces. An icon whose record has no accepted final stays classic.
  - `spells`: each accepted spell icon `icons--icon-NNN--icon` as `spell.icon.N`, cell N of the classic
    `spell.icons` atlas, and each accepted targeting and element tile as `spell.target.N` and `spell.element.N`, tile N
    of the classic `spell.selectors` atlas.

The `ui` area promotes the screens and escort portraits the game draws whole (wiki/rusty-dagger/art-conversion): a screen
`screens--NAME--...` stands in for `screen.NAME` (the title, character creation's `screen.character-generation` and
`screen.pick.02`, prison and death) at its own size, and a faction or child escort portrait
`faction-faces--tfac00i0--...-record-NNN-...` or `child-faces--kids00i0--...-record-NNN-...` for
`character.faction-face.NN` or `character.child-face.N`, scaled to PORTRAIT pixels on its longer side.

The `effects` area promotes the effects the game animates: blood and the magic sparkle (TEXTURE.380, from the
`effects` finals), each spell element's missile and impact (TEXTURE.375-379, from the `spells` finals), and the
inventory window's magic and coin glints (TEXTURE.434 records 5 and 6, from the `effects` finals). Each
effect becomes one PNG atlas (the renderer reads PNG) of its frames in classic order, all cropped to one box that
holds every frame's art and the classic frame's place on the generation canvas, so the frames keep their relative
size and position. Its placement states a frame's world size in metres and its pivot (fractions of the frame, from its
left and bottom), the frames in playing order (`sequence`, indices of `frames`), their rate and whether it loops, and
for a glint the part of a frame an inventory slot holds (`slot`, fractions, top-down). An effect missing an accepted
frame stays classic. The area also writes the soft white glow (`effectGlow`) the game's spell trails and impact bursts tint
in each element's colour, and the world meshes (`meshes`): the `effects` final `corpse-bones.glb`, a skull-free bone
pile resting on the floor, stands in for TEXTURE.380 record 1 (the corpse every human enemy class shares, men and
women alike, so it is no one in particular) and record 4 (the bone pile placed in dungeons). Each entry states the
mesh's footprint, its longest side in metres, so the game draws it at the width of the classic sprite it replaces.

The `surfaces` area promotes the walls, floors and ceilings (`surfaces`): each accepted final `texture-AAA-R`, from
its family's folder under local/art/generated/surfaces (tools/surfaces/batch.py), stands in for classic TEXTURE.AAA record R as an
8-bit RGBA PNG at its own size, and the game draws it on the location materials and terrain that draw that record.
The `terrain` area does the same into `surfaces` for the ground tiles, from local/art/generated/terrain (tools/terrain/ground.py). A
record has one entry: a ground tile models also draw is marked superseded in the surfaces ledger.

The `flats` area promotes the world's placed billboards and terrain nature (`flats`): each record in local/art/reference/flats/usage.csv
whose every frame has an accepted final becomes one PNG atlas, as an effect does, and the game draws it at the classic
sprite's size and anchor. A record an item also draws takes its accepted `items` final unless the area has its own.

The `people` area promotes the placed static people (`people`): each record in local/art/reference/people/usage.csv with a placement
becomes a one-frame atlas of its standing figure, the only frame a static person shows, crunched at PEOPLE_CRUNCH pixels to
a classic pixel on the whole of ART_PAL, whose small accents a smaller palette loses. Records with the same frame share the
atlas of whichever has an accepted final, since only one of them was painted. A record without one stays classic. The game
draws each at the classic sprite's size, standing on the atlas's own base.

The `people-rig` area exports the walking test figure (`peopleRig`) from a Workbench directional rig publication
(PEOPLE_RIG, or $PEOPLE_RIG): each view's compiled model and the pages beside it go to people-rigs/FIGURE/VIEW/ as
shipped (ground line at y = 0, springs recorded into the walk, walking shoes keyed by the walk), and the entry states
every file's bytes and hash, the view sets of its four- and eight-direction artifact sets (`directionalRig`), the
clips every view holds, its layer stack (`layers.json`, parts-rig-layers/1) and its standing height in rig units
(PEOPLE_RIG_HEIGHT), by which the game draws it at the height of the person it stands in for.

Icons are written at their area's ICON pixels on the longer side and carry no doll rect.

A held weapon's layer leaves a gap where the doll's hand holds it; its whole image (whole.py, in local/art/generated/worn/whole/)
is published beside it as its `icon`, which the inventory, shops and the ground draw instead.

A worn image and an item icon is also what a dropped item stands as on the ground, and the renderer reads PNG, so
each also has a `world` PNG, crunched as a world flat is at GROUND_CRUNCH pixels to a classic world pixel (an icon's
classic pixels are the treasure pile's, a worn image's DOLL_UNIT to its doll unit), and a worn image's dye mask a `world`
PNG the same size as the image's, clear wherever it is. The menus draw the smooth webp images. An item icon's placement
states its height on the ground in metres (`ground`), as a worn image stands at its doll rect's height. The clothed
body's states the row it is split at for clothing worn above and below (`waist`, doll units from its top).

A placement is the entry's own. A new entry takes its values from the classic original once (the classic effect, the
classic image's size at the treasure piles' scale, the classic body's waist), each marked `original` in its `from`;
after that, promotion keeps every value the payload holds, so a value changed on purpose survives a re-promotion.
`--set ID NAME VALUE` sets one, marked `hand`; `--recompute ID` (or `all`) takes them from the original again.

Only finals whose ledger/AREA-notes.json status is `accepted` are promoted. Rerunning an area replaces that
area's entries and files and keeps every other area's: it deletes the area's files and writes them all again, about ten
minutes for `worn`, so don't interrupt it. `--key KEY` promotes one accepted paper-doll or worn final and leaves the rest.
The import must have run (local/build/import/staging), and a paper-doll or worn final without its rect sidecar is refused.

    promote.py --area paper-doll --area worn --area items --area spells --area effects --area surfaces --area terrain
"""
import argparse, csv, hashlib, io, json, os, re, shutil, sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "paper-doll"))
from doll_preview import ART, AUTHORING, doll_layer, placed, worn_layer

sys.path.insert(0, str(AUTHORING / "tools/surfaces"))
import reduce  # noqa: E402

REPOSITORY = AUTHORING.parent
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/packs/daggerfall.art-replacements/media"
PAYLOAD = CONTENT / "worldrpg/packs/daggerfall.art-replacements/payload.json"
QUALITY = 90
AREAS = ("paper-doll", "worn", "items", "spells", "effects", "surfaces", "terrain", "flats", "people", "people-rig", "ui")
PEOPLE_RIG = Path(os.environ.get("PEOPLE_RIG", "/home/agent/dev/asset-pipeline/live-evidence/publications/w453-walk-20261109"))
PEOPLE_RIG_FIGURE = "w453"
# The figure's height in rig units, ground to crown: the front view's walk at 24 fps (rigc render, one pixel a unit)
# draws her crown 778 units above the sole of her planted foot at its highest.
PEOPLE_RIG_HEIGHT = 778
PEOPLE_RIG_SETS = {"4": "w453-walk.artifact-set.json", "8": "w453-walk8.artifact-set.json"}
SURFACE = re.compile(r"texture-(\d{3})-(\d+)")
ICON = {"items": 256, "spells": 128}   # pixels on an icon's longer side: an item slot, a spell glyph of about 3rem
PORTRAIT = 256                         # pixels on an escort portrait's longer side
UI_SCREEN = re.compile(r"screens--([a-z0-9-]+)--.*")
UI_SCREENS = {"title": "screen.title", "character-generation": "screen.character-generation", "pick-02": "screen.pick.02",
              "prison": "screen.prison", "death": "screen.death"}
UI_FACE = re.compile(r"(faction-faces--tfac00i0|child-faces--kids00i0)--.*-record-(\d+)-frame-000")
CLASSIC_MANIFEST = REPOSITORY / "local/build/import/staging/worldrpg/imports/shared/media/classic/manifest.json"
IMPORTED = REPOSITORY / "local/build/import/staging/worldrpg/payloads/daggerfall.imported.json"
ITEM_IMAGE = re.compile(r"item\.(\d+)\.(\d+)")
STAGED = REPOSITORY / "local/build/import/staging"
SPELL_ICON = re.compile(r"icons--icon-(\d+)--icon")
# The classic targeting tiles in range-type order and element tiles in element order (donor SpellIconCollection).
SPELL_TILES = {**{f"targets--{name}--target-icon": f"spell.target.{index}" for index, name in enumerate(
    ("caster-only", "by-touch", "single-target-at-range", "area-around-caster", "area-at-range"))},
    **{f"elements--{name}--element-icon": f"spell.element.{index}" for index, name in enumerate(("fire", "cold", "poison", "shock", "magic"))}}
# Each world effect's classic media id, and the area and art key of its finals (KEY-frame-NNN) and classic frames.
WORLD_EFFECTS = {
    **{f"effect.blood.{record}": ("effects", f"texture-380--effect--texture-380-record-{record:03d}", f"effects/texture-380/effect/texture-380-record-{record:03d}")
       for record in range(3)},
    "effect.sparkle.magic": ("effects", "texture-380--effect--texture-380-record-003", "effects/texture-380/effect/texture-380-record-003"),
    # The inventory window's glints: the magic shimmer over enchanted items and the coin glint behind stealable stock.
    "effect.glint.magic": ("effects", "texture-434--effect--texture-434-record-005", "effects/texture-434/effect/texture-434-record-005"),
    "effect.glint.coins": ("effects", "texture-434--effect--texture-434-record-006", "effects/texture-434/effect/texture-434-record-006"),
    **{f"effect.{kind}.{element}": ("spells", f"elements--{element}--world-effects--{folder}--texture-{archive}-record-{record:03d}",
                                     f"spells/elements/{element}/world-effects/{folder}/texture-{archive}-record-{record:03d}")
       for archive, element in zip(range(375, 380), ("fire", "cold", "poison", "shock", "magic"))
       for kind, folder, record in (("missile", "projectile", 0), ("impact", "impact", 1))},
}
CANVAS, OCCUPANCY = 1024, 0.74   # the generation canvas and the share of it the classic frame filled (run.py)
GROUND_CRUNCH = 2                # a dropped item's pixels to a classic world pixel, as a world flat's
WORLD_PIXEL = .025               # a classic world pixel in metres
DOLL_UNIT = .01 / WORLD_PIXEL    # a paper-doll unit (1 cm on the body) in classic world pixels
# The classic ground-container billboards, whose first stands every dropped item's classic image at its scale.
DUNGEON_MANIFEST = REPOSITORY / "local/build/import/staging/worldrpg/imports/shared/media/dungeon/manifest.json"
TREASURE = 216
WAIST = 40                       # the doll row, from the classic clothed body's top, the donor splits it at
# Where a placement value came from: taken from the classic original at promotion, or set by hand (--set).
ORIGINAL, HAND = "original", "hand"
EFFECT_FRAME = 256               # pixels on an effect frame's longer side
FLAT_CRUNCH = float(os.environ.get("FLAT_CRUNCH", "2"))   # a world billboard's pixels to a classic pixel
PEOPLE_CRUNCH = 3   # a person's pixels to a classic pixel
# The flats that give light, as Daggerfall Unity's emissive list names them, each with an emission map cut from its own art.
# 210-8 is the one blue glass; every other record glows warm.
GLOWING = {(190, 3), (190, 4), (190, 5), (200, 7), (200, 8), (200, 9), (200, 10), (202, 2), (208, 2),
           *((210, record) for record in (0, 1, 2, 3, 4, 5, 6, 8, 9, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29))}
COOL_GLOW = {(210, 8)}
GLOW = 64                        # pixels on the particle glow's side
CLOTHING = range(235, 243)
NEUTRAL = (14, 32)   # saturation (max less min channel, 0-255) over which a pixel fades from dyed cloth to its own colour
EDGE = 2             # pixels in from the piece's edge where its dye mask starts
DARK = (40, 70)      # brightness (max channel) under which a pixel is an outline, not cloth: a dye barely moves it, and
                     # tinting it rims a piece of its own colour (tan trousers) in the dye
DYES = ["blue", "grey", "red", "dark-brown", "purple", "light-brown", "white", "aquamarine", "yellow", "green"]
METALS = ["iron", "silver", "elven", "dwarven", "mithril", "adamantium", "ebony", "orcish", "daedric"]
CLASSIC = REPOSITORY / "local/build/import/staging/worldrpg/media/character/paper-doll"

WORN = re.compile(r"texture-(\d+)-record-(\d+)-frame-000")
# A design of its own over classic records another item keeps (the flanged mace over the club's), named by the
# template's refreshedArt, and the classic archive it stands over.
NAMED = re.compile(r"texture-([a-z]+)-record-(\d+)-frame-000")
NAMED_ARCHIVE = {"mace": 234}
BODY = re.compile(r"bodies--body(\d)(\d)(i[01])--.*")
FACE = re.compile(r"faces--face(\d)(\d)i0--.*-record-(\d+)-frame-000")
CREATURE = re.compile(r"creatures--((\w{4})\d\di0)--.*-record-(\d+)-frame-000")
BACKGROUND = re.compile(r"backgrounds--(scbg\d\di0)--.*-record-(\d+)-frame-000")
GENDER = {"0": "male", "1": "female"}


def replaces(key):
    """The classic media id a final's art key stands in for."""
    if m := WORN.fullmatch(key):
        return f"paper-doll.{int(m[1])}.{int(m[2])}"
    if m := NAMED.fullmatch(key):
        return f"paper-doll.{m[1]}.{int(m[2])}"
    if m := BODY.fullmatch(key):
        return f"character.body-{'unclothed' if m[3] == 'i0' else 'clothed'}.{GENDER[m[1]]}.0{m[2]}.0"
    if m := FACE.fullmatch(key):
        return f"character.head.{GENDER[m[1]]}.0{m[2]}.{int(m[3])}"
    if m := CREATURE.fullmatch(key):
        return f"character.{m[2]}.{m[1]}.{int(m[3])}"
    if m := BACKGROUND.fullmatch(key):
        return f"character.scbg.{m[1]}.{int(m[2])}"
    if m := UI_SCREEN.fullmatch(key):
        return UI_SCREENS[m[1]]
    if m := UI_FACE.fullmatch(key):
        return f"character.faction-face.{int(m[2]):02d}" if m[1].startswith("faction") else f"character.child-face.{int(m[2])}"
    raise SystemExit(f"No classic media id for art key {key}.")


def rect_of(area, key, path):
    """The final's doll-space rect, from its sidecar as doll_preview.py places it."""
    if area == "worn":
        # A named design is painted on the canvas of the classic record it stands over, so it takes that record's place.
        named = NAMED.fullmatch(key)
        layer = worn_layer(f"texture-{NAMED_ARCHIVE[named[1]]}-record-{named[2]}-frame-000" if named else key, None)
    else:
        family, name, rest = key.split("--")
        layer = doll_layer(family, name, int(re.search(r"record-(\d+)", rest)[1]), key)
    return placed(layer, "canvas")[1]


def webp(image, target, lossless=False):
    """Writes the image as WebP at a content path, and states its path, length and hash."""
    buffer = io.BytesIO()
    image.save(buffer, "WEBP", lossless=True) if lossless else image.save(buffer, "WEBP", quality=QUALITY, method=6)
    data = buffer.getvalue()
    (CONTENT / target).write_bytes(data)
    return {"path": target, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def world_png(image, target):
    """Writes the image as a PNG at a content path, as the renderer reads it (8-bit RGBA, which it requires, so not a
    palette), and states its path, length, hash and size."""
    buffer = io.BytesIO()
    image.convert("RGBA").save(buffer, "PNG", optimize=True)
    data = buffer.getvalue()
    (CONTENT / target).write_bytes(data)
    return {"path": target, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest(), "width": image.width, "height": image.height}


def world_image(image, height, doll=False):
    """An item's ground image, crunched as a world flat is: GROUND_CRUNCH pixels to a classic world pixel, for an image
    `height` classic world pixels tall (an icon), or with `doll`, doll units tall (a worn image)."""
    k = GROUND_CRUNCH * (DOLL_UNIT if doll else 1) * height / image.height
    return reduce.crunch_sprites([image], (max(1, round(image.width * k)), max(1, round(image.height * k))))[0]


# The placement each entry had in the payload before this run, by what it replaces, and its image's hash; and what
# --recompute names (an id, or "all"), which takes those values from the original again.
KEPT, KEPT_ART, RECOMPUTE = {}, {}, set()


def own(replaces, names, original):
    """An entry's placement: the values that size, anchor and time it, which are its own once written. A new entry, or
    one --recompute names, takes them from the classic original (`original()`), each marked ORIGINAL in `from`; an entry
    the payload already places keeps every value it has, however it was set, and takes only a missing one from the original."""
    old = None if {replaces, "all"} & RECOMPUTE else KEPT.get(replaces)
    if old is not None and all(name in old for name in names):
        return old
    values = original()
    fresh = {**values, "from": {name: ORIGINAL for name in values}}
    if old is None:
        return fresh
    return {**fresh, **{name: value for name, value in old.items() if name != "from"}, "from": {**fresh["from"], **old.get("from", {})}}


def classic_layer(media):
    """A classic character layer's doll rect, as the import publishes it."""
    for layer in json.load(open(IMPORTED))["characterPresentation"]["layers"]:
        if layer["mediaId"] == media:
            return layer["placement"]
    raise SystemExit(f"The import publishes no character layer {media}: run the import first.")


def waist(media, doll):
    """Where the clothed body is split for clothing worn above and below: the classic body's WAIST row, in doll units from
    the top of the refreshed image."""
    classic = classic_layer(media)
    return round(min(max(classic["y"] + min(WAIST, classic["height"]) - doll["y"], 0), doll["height"]), 4)


def metres_per_classic_pixel():
    """The scale of a dropped item's classic image: the first treasure pile's world height over its frame's pixels."""
    manifest = json.load(open(DUNGEON_MANIFEST))
    pile = min((billboard for billboard in manifest["billboards"] if billboard["textureArchive"] == TREASURE), key=lambda billboard: billboard["textureRecord"])
    return pile["worldSize"]["y"] / pile["frames"][0]["atlasFrame"]["height"]


def classic_effect(media):
    """A classic effect as the game reads it from the import's classic presentation: its world size (a metre on its
    longer side, unless stated) and pivot (its middle, unless stated, from the frame's left and bottom), its frames in
    playing order as indices of its frames, and its rate and loop."""
    manifest = json.load(open(CLASSIC_MANIFEST))
    resource = next(resource for resource in manifest["media"]["resources"] if resource["id"] == media)
    timing = next(effect["timing"] for effect in manifest["effects"] if effect["mediaId"] == media)
    ids = sorted(frame["frameIndex"] for frame in resource["frames"])
    first = next(frame for frame in resource["frames"] if frame["frameIndex"] == 0)
    longer = max(first["width"], first["height"])
    size = resource["displaySize"] or {"x": first["width"] / longer, "y": first["height"] / longer}
    pivot = resource["pivot"] or {"x": .5, "y": .5}
    return size, pivot, [ids.index(frame) for frame in resource["sequence"] or ids], timing["framesPerSecond"], timing["loop"]


def effect_placement(media, classic):
    """An effect atlas frame's own size in metres and pivot, from where the classic frame sits in it (`classic`, fractions
    of the frame, top-down): the classic size grows by the share that frame takes, and the classic pivot moves into it. A
    glint also states that share as its `slot`, the part of a frame an inventory slot holds."""
    size, pivot, sequence, rate, loops = classic_effect(media)
    values = {"size": {"width": round(size["x"] / classic["width"], 6), "height": round(size["y"] / classic["height"], 6)},
              "pivot": {"x": round(classic["x"] + pivot["x"] * classic["width"], 6),
                        "y": round(1 - classic["y"] - classic["height"] + pivot["y"] * classic["height"], 6)},
              "sequence": sequence, "framesPerSecond": rate, "loops": loops}
    if media.startswith("effect.glint."):
        values["slot"] = classic
    return values


def world_mask(mask, world):
    """A dye mask's ground image: the size of the item's, and clear wherever the item is."""
    small = mask.resize(world.size, Image.Resampling.LANCZOS)
    small.putalpha(Image.fromarray((np.asarray(small.getchannel("A"), np.float32) * (np.asarray(world.getchannel("A")) > 0)).astype(np.uint8)))
    return small


def dye_mask(image):
    """Where the final is the neutral cloth a dye recolours: opaque white, fading out with saturation and darkness."""
    rgba = np.asarray(image, np.float32)
    saturation = rgba[..., :3].max(-1) - rgba[..., :3].min(-1)
    light = np.clip((rgba[..., :3].max(-1) - DARK[0]) / (DARK[1] - DARK[0]), 0, 1)
    # The cut-out's soft rim is unmixed toward grey whatever the piece's colour, so the mask starts EDGE pixels in.
    inside = ndimage.minimum_filter(rgba[..., 3], size=2 * EDGE + 1) / 255
    cloth = np.clip((NEUTRAL[1] - saturation) / (NEUTRAL[1] - NEUTRAL[0]), 0, 1) * light * inside
    return Image.fromarray(np.dstack([np.full(cloth.shape + (3,), 255, np.uint8), (cloth * 255 + .5).astype(np.uint8)]), "RGBA")


def dyed(archive, record):
    """Whether the classic draws the record in other dyes or metals: clothing, and armour or weapons with bare steel."""
    return archive in CLOTHING or (CLASSIC / f"paper-doll-{archive}-{record}-daedric.png").exists()


def dye_tints(promoted, dyes, probe):
    """Each dye's or metal's tint, from the classic images' recoloured pixels (those the probe dye changes) against
    the promoted finals' neutral pixels."""
    def read(path):
        return np.asarray(Image.open(path).convert("RGBA"), np.float32)
    recolours, cloth = {dye: [] for dye in dyes}, []
    for key, path in promoted:
        m = WORN.fullmatch(key)
        archive, record = int(m[1]), int(m[2])
        base, changed = CLASSIC / f"paper-doll-{archive}-{record}.png", CLASSIC / f"paper-doll-{archive}-{record}-{probe}.png"
        if not changed.exists():
            continue
        classic = read(base)
        recoloured = (np.abs(classic[..., :3] - read(changed)[..., :3]).sum(-1) > 0) & (classic[..., 3] > 200)
        for dye in dyes:
            variant = CLASSIC / f"paper-doll-{archive}-{record}-{dye}.png"
            recolours[dye].append((read(variant) if variant.exists() else classic)[..., :3][recoloured])
        final = read(path)
        neutral = (final[..., 3] > 200) & (final[..., :3].max(-1) - final[..., :3].min(-1) < NEUTRAL[0])
        cloth.append(final[..., :3][neutral])
    if not cloth:
        raise SystemExit(f"No classic {probe} paper-doll images under {CLASSIC}: run the import first.")
    neutral = np.concatenate(cloth).mean(0)
    tints = {}
    for dye in dyes:
        ratio = np.concatenate(recolours[dye]).mean(0) / neutral
        brightness = max(1.0, float(ratio.max()))
        tints[dye] = {"multiply": "#%02x%02x%02x" % tuple(round(v * 255) for v in ratio / brightness), "brightness": round(brightness, 3)}
    return tints


def promote(area, key, path, rect=None, mediaId=None, name=None):
    """A final's entry. A worn final with a whole image in local/art/generated/worn/whole/ (whole.py) also carries it as its `icon`,
    on the same canvas and rect, which is what the inventory and the ground draw instead of the doll's layer."""
    image = Image.open(path).convert("RGBA")
    x, y, w, h = rect or rect_of(area, key, path)
    k = image.width / w
    box = image.getchannel("A").getbbox()
    if box is None:
        raise SystemExit(f"{path} is fully transparent.")
    image = image.crop(box)
    classic = replaces(key)
    media, name = mediaId or f"refreshed.{classic}", name or key
    entry = {"replaces": classic, "mediaId": media, **webp(image, f"{MEDIA}/{area}/{name}.webp"),
             "width": image.width, "height": image.height,
             "doll": {"x": round(x + box[0] / k, 4), "y": round(y + box[1] / k, 4),
                      "width": round(image.width / k, 4), "height": round(image.height / k, 4)}}
    if classic.startswith("character.body-clothed."):
        entry["placement"] = own(classic, ["waist"], lambda: {"waist": waist(classic, entry["doll"])})
    if area == "worn":
        ground = world_image(image, image.height / k, doll=True)
        entry["world"] = world_png(ground, f"{MEDIA}/{area}/{name}.png")
    m = WORN.fullmatch(key) or NAMED.fullmatch(key)
    if m and dyed(NAMED_ARCHIVE.get(m[1]) or int(m[1]), int(m[2])):
        mask = dye_mask(image)
        entry["dyeMask"] = {"mediaId": f"{media}.dye", **webp(mask, f"{MEDIA}/{area}/{name}-dye.webp", lossless=True)}
        if area == "worn":
            entry["dyeMask"]["world"] = world_png(world_mask(mask, ground), f"{MEDIA}/{area}/{name}-dye.png")
    whole = path.parent / "whole" / f"{key}.png"
    if area == "worn" and mediaId is None and whole.exists():
        icon = promote(area, key, whole, (x, y, w, h), f"{media}.icon", f"{key}-icon")
        del icon["replaces"]
        entry["icon"] = icon
    return entry


def ledger(area):
    return json.load(open(AUTHORING / "ledger" / f"{area}-notes.json"))


def accepted_final(area, notes, key):
    path = ART / "generated" / area / f"{key}.png"
    return path if notes.get(key, {}).get("status") == "accepted" and path.exists() else None


def icon(final, area):
    """A final cropped to its content and scaled to its area's ICON pixels on its longer side."""
    image = Image.open(final).convert("RGBA")
    image = image.crop(image.getchannel("A").getbbox())
    image.thumbnail((ICON[area], ICON[area]), Image.Resampling.LANCZOS)
    return image


def inventory_icons():
    """An entry for every published inventory image of the `items` area whose record has an accepted final."""
    notes = ledger("items")
    classic, entries = [], []
    images = [(published["mediaId"], published["textureArchive"], published["sourceRecordOrdinal"], f"items/{published['itemId']}")
              for published in json.load(open(CLASSIC_MANIFEST))["inventoryIcons"]]
    for media in sorted({media for icon in json.load(open(IMPORTED))["paperDoll"]["icons"] for media in icon["mediaIds"]}):
        archive, record = map(int, ITEM_IMAGE.fullmatch(media).groups())
        images.append((media, archive, record, f"items/{media}"))
    for media, archive, record, name in images:
        final = accepted_final("items", notes, f"texture-{archive}-record-{record:03d}-frame-000")
        if final is None:
            classic.append(media)
            continue
        ground = own(media, ["ground"], lambda: {"ground": round(classic_size(media)["height"] * metres_per_classic_pixel(), 6)})
        entries.append(icon_entry(media, icon(final, "items"), name, ground))
    if classic:
        print(f"items: {len(classic)} icons stay classic, their records have no accepted final: {', '.join(classic)}")
    return entries


def spell_icons():
    notes = ledger("spells")
    replaced = {key: f"spell.icon.{int(m[1])}" for key in notes if (m := SPELL_ICON.fullmatch(key))} | {key: media for key, media in SPELL_TILES.items() if key in notes}
    return [icon_entry(media, icon(final, "spells"), f"spells/{key}") for key, media in sorted(replaced.items())
            if (final := accepted_final("spells", notes, key))]


def ui_images():
    """An entry for every accepted `ui` final: a screen at its own size, an escort portrait at PORTRAIT pixels."""
    notes = ledger("ui")
    entries = []
    for key in sorted(key for key, note in notes.items() if note.get("status") == "accepted"):
        final = accepted_final("ui", notes, key)
        if final is None:
            raise SystemExit(f"{key} is accepted in ledger/ui-notes.json but has no final in local/art/generated/ui.")
        image = Image.open(final).convert("RGBA")
        if UI_FACE.fullmatch(key):
            image = image.crop(image.getchannel("A").getbbox())
            image.thumbnail((PORTRAIT, PORTRAIT), Image.Resampling.LANCZOS)
        entries.append(icon_entry(replaces(key), image, f"ui/{key}"))
    return entries


def classic_box(source):
    """Where run.py placed a classic frame on the generation canvas: an integer nearest-neighbour scale, centred."""
    w, h = Image.open(source).size
    scale = max(1, int(CANVAS * OCCUPANCY / max(w, h)))
    x, y = (CANVAS - w * scale) // 2, (CANVAS - h * scale) // 2
    return x, y, x + w * scale, y + h * scale


def png_file(image, target):
    """Writes an image as PNG content and names it: its path, length and hash."""
    buffer = io.BytesIO()
    image.save(buffer, "PNG", optimize=True)
    data = buffer.getvalue()
    (CONTENT / target).parent.mkdir(parents=True, exist_ok=True)
    (CONTENT / target).write_bytes(data)
    return {"path": target, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def frame_atlas(name, sources, finals, target, longest=None, crunch=None, colours=reduce.COLOURS, glow=None):
    """One PNG atlas of a classic record's frames in order, all cropped to one box that holds every frame's art and the
    classic frame's place on the generation canvas, and its entry without `replaces`. Smooth at most `longest` pixels a
    side, or with `crunch`, reduce's sprite crunch at that many pixels to a classic pixel, on `colours` of ART_PAL (None for
    all of it). `glow`, a function of a tile, also writes an emission map of the same layout beside it."""
    boxes = {classic_box(source) for source in sources}
    if len(boxes) != 1:
        raise SystemExit(f"{name}: the classic frames differ in size, so no one box places them.")
    region = boxes.pop()
    frames = [Image.open(final).convert("RGBA") for final in finals]
    box = region
    edge = 127 if crunch else 0   # the crunch hardens alpha at half, so fainter haze holds no art
    for frame in frames:
        if (art := frame.getchannel("A").point(lambda alpha: alpha > edge and 255).getbbox()) is not None:
            box = (min(box[0], art[0]), min(box[1], art[1]), max(box[2], art[2]), max(box[3], art[3]))
    width, height = box[2] - box[0], box[3] - box[1]
    k = crunch * Image.open(sources[0]).width / (region[2] - region[0]) if crunch else min(1, longest / max(width, height))
    size = (max(1, round(width * k)), max(1, round(height * k)))
    crops = [frame.crop(box) for frame in frames]
    tiles = reduce.crunch_sprites(crops, size, colours) if crunch else [crop.resize(size, Image.Resampling.LANCZOS) for crop in crops]
    columns = int(np.ceil(np.sqrt(len(frames))))
    atlas = Image.new("RGBA", (columns * size[0], int(np.ceil(len(frames) / columns)) * size[1]))
    glowing = atlas.copy()
    rects = []
    for index, tile in enumerate(tiles):
        x, y = index % columns * size[0], index // columns * size[1]
        atlas.paste(tile, (x, y))
        if glow:
            glowing.paste(glow(tile), (x, y))
        rects.append({"x": x, "y": y, "width": size[0], "height": size[1]})
    emission = {}
    if glow:
        if glowing.getbbox() is None:
            print(f"{name}: no pixel of this light glows, so it stays dark at night")
        else:
            emission = {"emission": png_file(glowing, target.removesuffix(".png") + "-glow.png")}
    return {**png_file(atlas, target), **emission, "width": atlas.width, "height": atlas.height, "frames": rects,
            "classic": {"x": round((region[0] - box[0]) / width, 4), "y": round((region[1] - box[1]) / height, 4),
                        "width": round((region[2] - region[0]) / width, 4), "height": round((region[3] - region[1]) / height, 4)}}


def world_effects():
    """An atlas entry for every world effect whose every frame has an accepted final."""
    entries, classic = [], []
    for media, (area, key, raw) in sorted(WORLD_EFFECTS.items()):
        notes = ledger(area)
        sources = sorted((ART / "reference").glob(f"{raw}-frame-*.png"))
        finals = [accepted_final(area, notes, f"{key}-frame-{frame:03d}") for frame in range(len(sources))]
        if not sources or None in finals:
            classic.append(media)
            continue
        entry = {"replaces": media, **frame_atlas(media, sources, finals, f"{MEDIA}/effects/{media.replace('.', '-')}.png", EFFECT_FRAME)}
        frame = entry.pop("classic")
        names = ["size", "pivot", "sequence", "framesPerSecond", "loops", *(["slot"] if media.startswith("effect.glint.") else [])]
        entry["placement"] = own(media, names, lambda: effect_placement(media, frame))
        if any(not 0 <= index < len(entry["frames"]) for index in entry["placement"]["sequence"]):
            raise SystemExit(f"{media}: its sequence {entry['placement']['sequence']} names a frame its {len(entry['frames'])} frames lack; "
                             f"set it (--set {media} sequence '[...]') or --recompute {media}.")
        entries.append(entry)
    if classic:
        print(f"effects: {len(classic)} effects stay classic, a frame has no accepted final: {', '.join(classic)}")
    return entries


def effect_glow():
    """A soft round white glow, opaque at its centre and fading to nothing at its edge."""
    y, x = np.mgrid[:GLOW, :GLOW]
    r = np.hypot(x - (GLOW - 1) / 2, y - (GLOW - 1) / 2) / (GLOW / 2)
    alpha = (np.clip(1 - r, 0, 1) ** 2 * 255).round().astype(np.uint8)
    image = Image.merge("RGBA", (*[Image.new("L", (GLOW, GLOW), 255)] * 3, Image.fromarray(alpha)))
    buffer = io.BytesIO()
    image.save(buffer, "PNG", optimize=True)
    data = buffer.getvalue()
    target = f"{MEDIA}/effects/glow.png"
    (CONTENT / target).write_bytes(data)
    return {"path": target, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


# The world meshes: each final, the classic TEXTURE records it stands in for, and its footprint (longest side, metres).
WORLD_MESHES = [("corpse-bones.glb", [(380, 1), (380, 4)], 2.15)]


def world_meshes():
    """Copies each world mesh final into the pack, with an entry for every classic record it replaces."""
    entries = []
    for name, records, footprint in WORLD_MESHES:
        final = ART / "generated" / "effects" / name
        if not final.exists():
            print(f"effects: no {name} final; {', '.join(f'{a}/{r}' for a, r in records)} stay sprites")
            continue
        data = final.read_bytes()
        target = f"{MEDIA}/effects/{name}"
        (CONTENT / target).write_bytes(data)
        entries += [{"replaces": {"archive": archive, "record": record}, "path": target, "byteLength": len(data),
                     "sha256": hashlib.sha256(data).hexdigest(), "footprint": footprint} for archive, record in records]
    return entries


def classic_size(media):
    """The opaque box of the classic image the game draws for an icon, in classic pixels, read from the import's
    published media: a template's image under the paper doll's, an icon of the classic media by its inventory. On the
    ground it stands at the treasure piles' metres per classic pixel."""
    if m := ITEM_IMAGE.fullmatch(media):
        path = CLASSIC / f"item-{m[1]}-{m[2]}.png"
    else:
        published = {artifact["mediaId"]: artifact["path"] for artifact in json.load(open(STAGED / "worldrpg/media/classic-media-inventory.json"))["artifacts"]}
        path = STAGED / published[media]
    left, top, right, bottom = Image.open(path).convert("RGBA").getchannel("A").getbbox()
    return {"width": right - left, "height": bottom - top}


def surfaces(area):
    """Each accepted final's entry in the area, by the classic texture record it replaces."""
    finals = {path.stem: path for path in (ART / "generated" / area).glob("*/texture-*.png") if path.parent.name != "raw"}
    promoted = []
    for key, note in ledger(area).items():
        if note.get("status") != "accepted":
            continue
        if key not in finals:
            raise SystemExit(f"{key} is accepted in ledger/{area}-notes.json but has no final under local/art/generated/{area}.")
        archive, record = map(int, SURFACE.fullmatch(key).groups())
        promoted.append({"replaces": {"archive": archive, "record": record},
                         **world_png(Image.open(finals[key]), f"{MEDIA}/{area}/{key}.png")})
    return promoted


def glow_of(archive, record):
    """How to cut a flat's emission map from a tile, for a flat that gives light."""
    if (archive, record) in GLOWING:
        return lambda tile: reduce.glow(tile, cool=(archive, record) in COOL_GLOW)
    return None


def flats():
    """An atlas entry for every placed classic record whose every frame has an accepted final: from the `flats` area, or,
    for a record an item also draws, its accepted `items` final."""
    entries, classic = [], []
    area_notes = {area: ledger(area) for area in ("flats", "items")}
    for row in csv.DictReader(open(ART / "reference/flats/usage.csv")):
        archive, record = int(row["archive"]), int(row["record"])
        stem = f"texture-{archive:03d}-record-{record:03d}"
        sources = sorted((ART / "reference/flats" / row["family"] / f"texture-{archive:03d}/flat").glob(f"{stem}-frame-*-source.png"))
        finals = [accepted_final("flats", area_notes["flats"], f"{stem}-frame-{frame:03d}")
                  or accepted_final("items", area_notes["items"], f"{stem}-frame-{frame:03d}") for frame in range(len(sources))]
        if not sources or None in finals:
            classic.append(stem)
            continue
        entries.append({"replaces": {"archive": archive, "record": record},
                        **frame_atlas(stem, sources, finals, f"{MEDIA}/flats/{stem}.png", crunch=FLAT_CRUNCH, glow=glow_of(archive, record))})
    print(f"flats: {len(entries)} records refreshed, {len(classic)} stay classic")
    return entries


def people():
    """An atlas entry for every placed person whose standing frame, or a record's with the same frame, has an accepted final."""
    notes = ledger("people")
    placed_people = []
    for row in csv.DictReader(open(ART / "reference/people/usage.csv")):
        if not any(int(row[f"{place}_placements"]) for place in ("exterior", "interior", "dungeon")):
            continue
        stem = f"texture-{int(row['archive']):03d}-record-{int(row['record']):03d}"
        source = ART / "reference/people" / row["family"] / f"texture-{int(row['archive']):03d}/person/{stem}-frame-000-source.png"
        image = Image.open(source).convert("RGBA")
        placed_people.append((row, stem, source, (image.size, image.tobytes())))
    atlases = {}
    for _, stem, source, frame in placed_people:
        if frame not in atlases and (final := accepted_final("people", notes, f"{stem}-frame-000")):
            atlases[frame] = frame_atlas(stem, [source], [final], f"{MEDIA}/people/{stem}.png", crunch=PEOPLE_CRUNCH, colours=None)
    entries = [{"replaces": {"archive": int(row["archive"]), "record": int(row["record"])}, **atlases[frame]}
               for row, _, _, frame in placed_people if frame in atlases]
    print(f"people: {len(entries)} records refreshed by {len(atlases)} atlases, {len(placed_people) - len(entries)} stay classic, "
          f"{sum(entry['byteLength'] for entry in atlases.values()) / 1e6:.1f} MB", flush=True)
    return entries


def people_rig():
    """Copies the publication's views into the pack and states them, their view sets, clips and layer stack."""
    def copied(source, target):
        data = source.read_bytes()
        (CONTENT / target).parent.mkdir(parents=True, exist_ok=True)
        (CONTENT / target).write_bytes(data)
        return {"path": target, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}

    sets = {name: json.loads((PEOPLE_RIG / manifest).read_text()) for name, manifest in PEOPLE_RIG_SETS.items()}
    members = {artifact["id"]: artifact["path"] for publication in sets.values() for artifact in publication["artifacts"]}
    views, clips = {}, None
    for view in sorted({direction["rig"] for publication in sets.values() for direction in publication["directionalRig"]["directions"]}):
        source = PEOPLE_RIG.parent / members[view]
        model = json.loads(source.read_text())
        if model["spec"] != "rigc-compiled/3":
            raise SystemExit(f"{source} is {model['spec']}, not rigc-compiled/3.")
        held = sorted(animation["name"] for animation in model["animations"])
        if clips is not None and held != clips:
            raise SystemExit(f"{view} holds clips {held}, the other views {clips}: every view plays the same clips.")
        clips = held
        target = f"{MEDIA}/people-rigs/{PEOPLE_RIG_FIGURE}/{view}"
        views[view] = {"model": copied(source, f"{target}/{source.name}"),
                       "pages": [copied(source.parent / page["name"], f"{target}/{page['name']}") for page in model["pages"]]}
    layers = json.loads((PEOPLE_RIG / "layers.json").read_text())
    if layers["spec"] != "parts-rig-layers/1":
        raise SystemExit(f"{PEOPLE_RIG / 'layers.json'} is {layers['spec']}, not parts-rig-layers/1.")
    print(f"people-rig: {len(views)} views, clips {', '.join(clips)}", flush=True)
    return {"figure": PEOPLE_RIG_FIGURE, "height": PEOPLE_RIG_HEIGHT, "walk": "walk", "idle": "idle", "crossfade": .2,
            "clips": clips, "layers": {"spec": layers["spec"], "layers": layers["layers"]}, "views": views,
            "directions": {name: [{"label": direction["label"], "angle": direction["previewAngleDegrees"], "view": direction["rig"],
                                   **({"mirror": True} if direction.get("mirror") else {})}
                                  for direction in publication["directionalRig"]["directions"]]
                           for name, publication in sets.items()}}


def icon_entry(replaces, image, name, placement=None):
    """An icon's entry; with a placement, which states its height on the ground in metres, also its world PNG."""
    entry = {"replaces": replaces, "mediaId": f"refreshed.{replaces}", **webp(image, f"{MEDIA}/{name}.webp"),
             "width": image.width, "height": image.height}
    if placement is not None:
        entry["world"] = world_png(world_image(image, round(placement["ground"] / WORLD_PIXEL, 4)), f"{MEDIA}/{name}.png")
        entry["placement"] = placement
    return entry


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--area", action="append", choices=AREAS, default=[])
    ap.add_argument("--key", action="append", help="promote only this accepted final of a paper-doll or worn --area, "
                    "leaving the area's other files and its tints as they are; a whole area deletes and rewrites its media")
    ap.add_argument("--recompute", action="append", default=[], metavar="ID",
                    help="take the placement of the entry replacing ID (or every promoted entry, with `all`) from the original again")
    ap.add_argument("--set", action="append", nargs=3, default=[], metavar=("ID", "NAME", "VALUE"),
                    help="set placement value NAME (size.width, framesPerSecond, ground, waist, ...) of the entry replacing ID "
                    "to the JSON VALUE, marked as set by hand, after any --area")
    a = ap.parse_args()
    if not a.area and not a.set:
        ap.error("give an --area to promote or a --set")
    RECOMPUTE.update(a.recompute)
    payload = json.loads(PAYLOAD.read_text()) if PAYLOAD.exists() else {"artReplacements": {"images": []}}
    images = payload["artReplacements"]["images"]
    placed = lambda: [entry for entry in [*images, *payload["artReplacements"].get("effects", [])] if "placement" in entry]
    for entry in placed():
        KEPT[entry["replaces"]], KEPT_ART[entry["replaces"]] = entry["placement"], entry["sha256"]
    for area in a.area:
        if a.key:
            notes = ledger(area)
            for key in a.key:
                if notes.get(key, {}).get("status") != "accepted":
                    raise SystemExit(f"{key} is not accepted in ledger/{area}-notes.json.")
                images[:] = [entry for entry in images if entry["replaces"] != replaces(key)]
                images.append(promote(area, key, ART / "generated" / area / f"{key}.png"))
                print(f"{area}: {key}", flush=True)
            continue
        if area == "effects":
            directory = CONTENT / MEDIA / "effects"
            directory.mkdir(parents=True, exist_ok=True)
            # Files are rewritten in place and the stale ones removed after, so a refused effect leaves the area as it was.
            effects = payload["artReplacements"]["effects"] = world_effects()
            payload["artReplacements"]["effectGlow"] = effect_glow()
            payload["artReplacements"].pop("corpseMesh", None)
            payload["artReplacements"]["meshes"] = world_meshes()
            written = {entry["path"] for entry in [*effects, payload["artReplacements"]["effectGlow"], *payload["artReplacements"]["meshes"]]}
            for old in [*directory.glob("*.png"), *directory.glob("*.glb")]:
                if f"{MEDIA}/effects/{old.name}" not in written:
                    old.unlink()
            print(f"effects: {len(effects)} effects, {sum(entry['byteLength'] for entry in effects) / 1e6:.1f} MB", flush=True)
            continue
        if area == "flats":
            shutil.rmtree(CONTENT / MEDIA / "flats", ignore_errors=True)
            payload["artReplacements"]["flats"] = flats()
            continue
        if area == "people":
            shutil.rmtree(CONTENT / MEDIA / "people", ignore_errors=True)
            payload["artReplacements"]["people"] = people()
            continue
        if area == "people-rig":
            shutil.rmtree(CONTENT / MEDIA / "people-rigs", ignore_errors=True)
            payload["artReplacements"]["peopleRig"] = people_rig()
            continue
        if area in ("surfaces", "terrain"):
            directory = CONTENT / MEDIA / area
            directory.mkdir(parents=True, exist_ok=True)
            for old in directory.glob("*.png"):
                old.unlink()
            promoted = surfaces(area)
            kept = [entry for entry in payload["artReplacements"].get("surfaces", []) if not entry["path"].startswith(f"{MEDIA}/{area}/")]
            entries = sorted(kept + promoted, key=lambda entry: (entry["replaces"]["archive"], entry["replaces"]["record"]))
            records = [(entry["replaces"]["archive"], entry["replaces"]["record"]) for entry in entries]
            if len(set(records)) != len(records):
                raise SystemExit(f"Two areas replace one record: {sorted(r for r in set(records) if records.count(r) > 1)}.")
            payload["artReplacements"]["surfaces"] = entries
            print(f"{area}: {len(promoted)} finals, {sum(entry['byteLength'] for entry in promoted) / 1e6:.1f} MB", flush=True)
            continue
        notes = ledger(area)
        accepted = sorted(key for key, note in notes.items() if note.get("status") == "accepted")
        directory = CONTENT / MEDIA / area
        directory.mkdir(parents=True, exist_ok=True)
        for old in [*directory.glob("*.webp"), *directory.glob("*.png")]:
            old.unlink()
        images[:] = [entry for entry in images if not entry["path"].startswith(f"{MEDIA}/{area}/")]
        if area in ("items", "spells", "ui"):
            promoted = inventory_icons() if area == "items" else spell_icons() if area == "spells" else ui_images()
            images.extend(promoted)
            print(f"{area}: {len(promoted)} icons, {sum(entry['byteLength'] for entry in promoted) / 1e6:.1f} MB", flush=True)
            continue
        for key in accepted:
            path = ART / "generated" / area / f"{key}.png"
            if not path.exists():
                raise SystemExit(f"{key} is accepted in ledger/{area}-notes.json but {path} does not exist.")
            images.append(promote(area, key, path))
        if area == "worn":
            # A named design is left out: the tints are measured against the classic images of each record.
            finals = [(key, ART / "generated" / area / f"{key}.png") for key in accepted if WORN.fullmatch(key)]
            clothing = [final for final in finals if int(WORN.fullmatch(final[0])[1]) in CLOTHING]
            metal = [final for final in finals if int(WORN.fullmatch(final[0])[1]) not in CLOTHING]
            payload["artReplacements"]["dyeTints"] = dye_tints(clothing, DYES, "red") | dye_tints(metal, METALS, "daedric")
        size = sum(entry["byteLength"] + entry.get("dyeMask", {}).get("byteLength", 0) for entry in images if entry["path"].startswith(f"{MEDIA}/{area}/"))
        print(f"{area}: {len(accepted)} finals, {size / 1e6:.1f} MB", flush=True)
    for entry in placed():
        if entry["replaces"] in KEPT_ART and entry["sha256"] != KEPT_ART[entry["replaces"]] and entry["placement"] is KEPT[entry["replaces"]]:
            print(f"{entry['replaces']}: new art kept its placement; check it, or --recompute {entry['replaces']}", flush=True)
    for media, name, value in a.set:
        entry = next((entry for entry in placed() if entry["replaces"] == media), None)
        if entry is None:
            raise SystemExit(f"No promoted entry replacing {media} has a placement.")
        *path, last = name.split(".")
        target = entry["placement"]
        for step in path:
            target = target[step]
        if path and last not in target or not path and name not in entry["placement"]["from"]:
            raise SystemExit(f"{media} has no placement value {name}: it has {', '.join(entry['placement']['from'])}.")
        target[last] = json.loads(value)
        entry["placement"]["from"][path[0] if path else name] = HAND
        print(f"{media}: {name} = {value}, set by hand", flush=True)
    images.sort(key=lambda entry: entry["replaces"])
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")


if __name__ == "__main__":
    main()
