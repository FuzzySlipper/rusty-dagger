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
    of the classic `spell.selectors` atlas, whose placement states the tile's shape (`aspect`, width over height).

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
women alike, so it is no one in particular) and record 4 (the bone pile placed in dungeons). WORLD_MESHES names each such
sprite mesh, which naming accepts; each entry is keyed by the record it replaces (`380-4`) and states the GLB material
slots that carry an emissive map (`glow`), which the game lights at the lamps' level as it does a refreshed lamp's
emission map: a lamp post's glass and flame. Its placement states the world size in metres it stands at (`size`, which the
classic sprite it replaces gave once), and the game draws the mesh that wide from the GLB's own width across (x), as the Engine
admits it. A hanging root, chain, cage or lantern (tools/hanging/hanging.py) also states what it hangs as (`hangs`, as its GLB's
scene states it): the game draws it from its sprite's top at its `size`'s height and swings it when an actor walks through it.
Where the mesh stands on the floor is the world's, not the art's: the import places a sprite at its pivot, and the game lowers the
mesh by the classic pivot's share of the sprite's height.

The `surfaces` area promotes the walls, floors and ceilings (`surfaces`): each accepted final `texture-AAA-R`, from
its family's folder under local/art/generated/surfaces (tools/surfaces/batch.py), stands in for classic TEXTURE.AAA record R as an
8-bit RGBA PNG at its own size, and the game draws it on the location materials and terrain that draw that record.
The `terrain` area does the same into `surfaces` for the ground tiles, from local/art/generated/terrain (tools/terrain/ground.py). A
record has one entry: a ground tile models also draw is marked superseded in the surfaces ledger.

The `flats` area promotes the world's placed billboards and terrain nature (`flats`): each record in local/art/reference/flats/usage.csv
whose every frame has an accepted final becomes one PNG atlas, as an effect does, with its own placement: a frame's size in
metres, its pivot and its foot (fractions of the frame, from its left and bottom: where the sprite hangs from its position, and
where it stands on the ground), and for several frames the rate they play at, in order. The game draws the flat from these
alone, and so a nature flat, whose tree meshes (`trees`, tools/trees) state their height and foot in metres at the flat's own size.
A record an item also draws takes its accepted `items` final unless the area has its own.

The `people` area promotes the placed static people (`people`): each record in local/art/reference/people/usage.csv with a placement
becomes a one-frame atlas of its standing figure, the only frame a static person shows, crunched at PEOPLE_CRUNCH pixels to
a classic pixel on the whole of ART_PAL, whose small accents a smaller palette loses. Records with the same frame share the
atlas of whichever has an accepted final, since only one of them was painted. A record without one stays classic. The game
draws each at its own size (`placement`, as a flat's), standing on the atlas's own base (its `foot`).

The `people-rig` area exports the walking test figure (`peopleRig`) from a Workbench directional rig publication
(PEOPLE_RIG, or $PEOPLE_RIG): each view's compiled model and the pages beside it go to people-rigs/FIGURE/VIEW/ as
shipped (ground line at y = 0, springs recorded into the walk, walking shoes keyed by the walk), and the entry states
every file's bytes and hash, the view sets of its four- and eight-direction artifact sets (`directionalRig`), the
clips every view holds, its layer stack (`layers.json`, parts-rig-layers/1) and its standing height in rig units
(PEOPLE_RIG_HEIGHT), by which the game draws it at the height of the person it stands in for.

Icons are written at their area's ICON pixels on the longer side and carry no doll rect.

The `models` area promotes the remodels of classic ARCH3D models (`models`, keyed by ARCH3D record id): each final
local/art/generated/models/ID.glb that ledger/models-notes.json accepts, in the classic model's own frame, with its
materials named `texture-A-R` for the classic texture they draw or else carrying their own PNG. The source tool's
`model-mesh` writes it as the Engine static mesh the game draws in the classic's place, with the PNGs beside it, and
refuses one standing out of the classic's convex hull, or, where its note states `"fit": "collision"` for an open
classic, out of the classic's own faces. It reads the classic from local/inputs/arena2. A material with glTF's alphaMode
MASK draws its PNG cut out at half its alpha (`"alpha": "mask"`); one whose glTF extras state `frames` and
`framesPerSecond` is a strip of that many frames side by side that the game steps through, its faces' uvs covering one
frame's width of [0, 1], as a hearth's flames are; and one under KHR_materials_unlit draws at its own colours whatever the
light (`"unlit": true`).

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
`--set ID NAME VALUE` sets one, marked `hand`; `--recompute ID` (or `all`) takes them from the original again. An entry's
id is what it replaces (an effect or image), or `flat.ARCHIVE.RECORD`, `person.ARCHIVE.RECORD`, `mesh.ARCHIVE.RECORD` or `tree.ARCHIVE.RECORD`.

Every area promotes the same way (promote_area). Its units are what an entry replaces, keyed as the area keys them: an
art key (paper-doll, worn, spells, ui, surfaces, terrain), an item's media id (items), an effect's media id, a sprite
mesh's ARCHIVE-RECORD and `effectGlow` (effects), a classic record `texture-AAA-record-RRR` (flats, people), an ARCH3D
record id (models) or the rig figure (people-rig); each unit draws finals, which ledger/AREA-notes.json must mark
`accepted`. Only the shared checkout holds every final (local/art/generated is ignored), so:

  - `--key KEY` promotes the units KEY names, by their key or a final they draw, with every unit that draws one of those
    finals (records sharing an atlas, a mesh's records), and keeps every other entry and file as it is. It refuses a unit
    whose finals the ledger doesn't accept or this checkout doesn't hold. It leaves the worn tints as they are.
  - A whole-area run (no `--key`) refuses while the ledger accepts a final this checkout doesn't hold, naming them, since it
    would drop their entries; it names every entry it removes because the ledger no longer accepts all it draws, and
    leaves in the area's folders only the files its entries name.
  - Every file is written to a staging folder first and moves into content, and the payload is written, only once every
    area named has built, so a refused run changes nothing.

The import must have run (local/build/import/staging); effects, flats and people also read local/art/reference, and a
paper-doll or worn final without its rect sidecar is refused.

    promote.py --area worn --key texture-235-record-000-frame-000
    promote.py --area paper-doll --area worn --area items --area spells --area effects --area surfaces --area terrain
"""
import argparse, csv, functools, hashlib, io, json, os, re, shutil, subprocess, sys, tempfile
from collections import namedtuple
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
# The classic identities the pack replaces, which a player's import leaves out of its data container (an importer input).
REPLACED = REPOSITORY / "src/Daggerfall.Import.Tool/inputs/replaced-classic.json"
QUALITY = 90
ARENA2 = REPOSITORY / "local/inputs/arena2"
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
# Where a developer's --classic-art import moves the classic media a player's import leaves out (#10763), at the same paths.
CLASSIC_ART = REPOSITORY / "local/build/import/classic-art"
SPELL_ICON = re.compile(r"icons--icon-(\d+)--icon")
# The classic `spell.selectors` atlas (MASK04I0.IMG, donor SpellIconCollection) lays a targeting tile and an element tile in a row
# each; their sizes are the ruleset's (DaggerfallSpellConstruction), the one home of that layout, so a refreshed tile's aspect,
# taken from it once, is read there.
def spell_tile_shape():
    source = (REPOSITORY / "src/WorldRpg.Rulesets.Daggerfall/Magic/DaggerfallSpellConstruction.cs").read_text()
    size = {name: int(re.search(rf"const int {name} = (\d+);", source)[1]) for name in ("TargetTileWidth", "ElementTileWidth", "SelectorHeight")}
    return {"spell.target": size["TargetTileWidth"] / size["SelectorHeight"], "spell.element": size["ElementTileWidth"] / size["SelectorHeight"]}


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
# Where a placement value came from: taken from the classic original at promotion, or set by hand (--set). A tree's, which
# trees.py measures from the refreshed art against its flat's own size, are marked `art` there.
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
CLASSIC = "worldrpg/media/character/paper-doll"   # the classic paper-doll images, under the staging or classic-art tree

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


# Where this run writes: every file lands here first, under its content path, and moves into content only once every
# area has built (commit), so a refused run leaves the pack as it was.
STAGE = None


def write(target, data):
    """Writes a file at a content path, into this run's staging folder, and states its path, length and hash."""
    path = STAGE / target
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return {"path": target, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def webp(image, target, lossless=False):
    """Writes the image as WebP at a content path, and states its path, length and hash."""
    buffer = io.BytesIO()
    image.save(buffer, "WEBP", lossless=True) if lossless else image.save(buffer, "WEBP", quality=QUALITY, method=6)
    return write(target, buffer.getvalue())


def world_png(image, target):
    """Writes the image as a PNG at a content path, as the renderer reads it (8-bit RGBA, which it requires, so not a
    palette), and states its path, length, hash and size."""
    buffer = io.BytesIO()
    image.convert("RGBA").save(buffer, "PNG", optimize=True)
    return {**write(target, buffer.getvalue()), "width": image.width, "height": image.height}


def world_image(image, height, doll=False):
    """An item's ground image, crunched as a world flat is: GROUND_CRUNCH pixels to a classic world pixel, for an image
    `height` classic world pixels tall (an icon), or with `doll`, doll units tall (a worn image)."""
    k = GROUND_CRUNCH * (DOLL_UNIT if doll else 1) * height / image.height
    return reduce.crunch_sprites([image], (max(1, round(image.width * k)), max(1, round(image.height * k))))[0]


# The placement each entry had in the payload before this run, by what it replaces, its image's hash and its own mask;
# and what --recompute names (an id, or "all"), which takes those values from the original again.
KEPT, KEPT_ART, KEPT_MASK, RECOMPUTE = {}, {}, {}, set()


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


BILLBOARDS = {}


def billboard(archive, record):
    """A classic billboard sprite as the import publishes it (its world size, pivot, frames and playback), by texture record."""
    if not BILLBOARDS:
        BILLBOARDS.update({(b["textureArchive"], b["textureRecord"]): b for b in json.load(open(DUNGEON_MANIFEST))["billboards"]})
    if (archive, record) not in BILLBOARDS:
        raise SystemExit(f"The import publishes no billboard {archive}/{record}: run the import first.")
    return BILLBOARDS[archive, record]


def flat_placement(archive, record, classic, frames, person=False):
    """A flat's or person's own placement, from where the classic frame sits in the atlas frame (`classic`, fractions of the
    frame, top-down): the classic world size grows by the share that frame takes, and the classic pivot, and the point at the
    foot's middle (`foot`), move into the frame (fractions from its left and bottom). A person stands on the art's own base: a
    painted hem or foot can reach below the classic frame, so its foot is the classic frame's middle mapped into the atlas frame,
    on the atlas frame's bottom row (y 0). An atlas of
    several frames states the rate the classic plays at, which only a flat plays."""
    sprite = billboard(archive, record)
    into = lambda x, y: {"x": round(classic["x"] + x * classic["width"], 6),
                         "y": round(1 - classic["y"] - classic["height"] + y * classic["height"], 6)}
    values = {"size": {"width": round(sprite["worldSize"]["x"] / classic["width"], 6),
                       "height": round(sprite["worldSize"]["y"] / classic["height"], 6)},
              "pivot": into(sprite["pivot"]["x"], sprite["pivot"]["y"]), "foot": into(.5, 0)}
    if person:
        values["foot"]["y"] = 0
    if frames > 1 and not person:
        if not sprite["playback"]:
            raise SystemExit(f"Billboard {archive}/{record} has {frames} frames but the import publishes no playback to take its rate from.")
        values["framesPerSecond"] = sprite["playback"]["framesPerSecond"]
    return values


def mesh_placement(archive, record):
    """A sprite mesh's own placement: the world size, in metres, of the classic sprite it stands in for, which draws it across
    that width (x), or, hung from the top of its sprite, to that height (y)."""
    size = billboard(archive, record)["worldSize"]
    return {"size": {"width": round(size["x"], 6), "height": round(size["y"], 6)}}


def with_placement(identity, entry, original):
    """An atlas entry whose `classic` rect (where the classic frame sits in its frames) is turned into its own placement,
    taken from the original once (`original(classic)`) and kept after, so the entry no longer states the rect."""
    classic = entry.pop("classic")
    names = ["size", "pivot", "foot", *(["framesPerSecond"] if len(entry["frames"]) > 1 and identity.startswith("flat.") else [])]
    entry["placement"] = own(identity, names, lambda: original(classic))
    return entry


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


def staged(relative):
    """A file the import staged: in the staging tree, or in the classic-art tree for classic media the art pack replaces."""
    path = STAGED / relative
    return path if path.exists() else CLASSIC_ART / relative


@functools.lru_cache(None)
def paper_doll_items():
    """The import's paper-doll catalogue, by classic archive and record."""
    return {(item["archive"], item["record"]): item for item in json.load(open(IMPORTED))["paperDoll"]["items"]}


def dyed(archive, record):
    """Whether the classic draws the record in other dyes or metals: clothing, and armour or weapons with bare steel, whose
    daedric image the paper-doll catalogue names apart from the undyed one."""
    item = paper_doll_items()[(archive, record)]
    return archive in CLOTHING or item["dyes"].get("daedric", item["dyes"].get("unchanged")) != f"paper-doll.{archive}.{record}"


def dye_tints(promoted, dyes, probe):
    """Each dye's or metal's tint, from the classic images' recoloured pixels (those the probe dye changes) against
    the promoted finals' neutral pixels."""
    def read(path):
        return np.asarray(Image.open(path).convert("RGBA"), np.float32)
    recolours, cloth = {dye: [] for dye in dyes}, []
    for key, path in promoted:
        m = WORN.fullmatch(key)
        archive, record = int(m[1]), int(m[2])
        base, changed = staged(f"{CLASSIC}/paper-doll-{archive}-{record}.png"), staged(f"{CLASSIC}/paper-doll-{archive}-{record}-{probe}.png")
        if not changed.exists():
            continue
        classic = read(base)
        recoloured = (np.abs(classic[..., :3] - read(changed)[..., :3]).sum(-1) > 0) & (classic[..., 3] > 200)
        for dye in dyes:
            variant = staged(f"{CLASSIC}/paper-doll-{archive}-{record}-{dye}.png")
            recolours[dye].append((read(variant) if variant.exists() else classic)[..., :3][recoloured])
        final = read(path)
        neutral = (final[..., 3] > 200) & (final[..., :3].max(-1) - final[..., :3].min(-1) < NEUTRAL[0])
        cloth.append(final[..., :3][neutral])
    if not cloth:
        raise SystemExit(f"No classic {probe} paper-doll images under {STAGED} or {CLASSIC_ART}: run the import with --classic-art first.")
    neutral = np.concatenate(cloth).mean(0)
    tints = {}
    for dye in dyes:
        ratio = np.concatenate(recolours[dye]).mean(0) / neutral
        brightness = max(1.0, float(ratio.max()))
        tints[dye] = {"multiply": "#%02x%02x%02x" % tuple(round(v * 255) for v in ratio / brightness), "brightness": round(brightness, 3)}
    return tints


MASKS = ART / "generated/worn/mask"   # hairmask.py's finals: KEY.png and KEY.json with its rect


def masking():
    """The paper-doll records the classic draws with a mask that clears what lies beneath (a helmet over hair)."""
    return {f"paper-doll.{archive}.{record}" for (archive, record), item in paper_doll_items().items() if item.get("maskMediaId")}


def hair_mask(key, classic, media, name):
    """A masking record's own mask, from its hairmask.py final: the image as `mask` and its rect in doll units as
    `placement.mask`, set by hand, a null rect where it hides nothing. Without a final the entry keeps the mask it has."""
    sidecar = MASKS / f"{key}.json"
    if sidecar.exists():
        rect = json.loads(sidecar.read_text())["rect"]
        own = {"placement": {"mask": None if rect is None else dict(zip(("x", "y", "width", "height"), rect)), "from": {"mask": HAND}}}
        if rect is not None:
            own["mask"] = {"mediaId": f"{media}.mask",
                           **webp(Image.open(MASKS / f"{key}.png").convert("RGBA"), f"{MEDIA}/worn/{name}-mask.webp", lossless=True)}
        return own
    if "mask" in KEPT.get(classic, {}):
        return {"placement": KEPT[classic], **({"mask": KEPT_MASK[classic]} if KEPT_MASK.get(classic) else {})}
    raise SystemExit(f"{key} replaces {classic}, which clears what lies beneath it: draw its mask with "
                     "authoring/tools/paper-doll/hairmask.py first.")


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
    if area == "worn" and mediaId is None and classic in masking():
        entry.update(hair_mask(key, classic, media, name))
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
    return json.loads((AUTHORING / "ledger" / f"{area}-notes.json").read_text())


def generated(area, key, suffix=".png"):
    """A final of an area's ledger, where local/art/generated keeps it."""
    return Final(area, key, ART / "generated" / area / f"{key}{suffix}")


def icon(final, area):
    """A final cropped to its content and scaled to its area's ICON pixels on its longer side."""
    image = Image.open(final).convert("RGBA")
    image = image.crop(image.getchannel("A").getbbox())
    image.thumbnail((ICON[area], ICON[area]), Image.Resampling.LANCZOS)
    return image


def doll_units(area):
    """The paper doll's and the worn layers' units: each final is its own image."""
    return {key: Unit([generated(area, key)], lambda key=key: [("images", promote(area, key, ART / "generated" / area / f"{key}.png"))])
            for key in sorted(ledger(area))}


def item_units():
    """The `items` area's units, by the media id each replaces: every published inventory image, drawn from the final of
    its record."""
    images = [(published["mediaId"], published["textureArchive"], published["sourceRecordOrdinal"], f"items/{published['itemId']}")
              for published in json.load(open(CLASSIC_MANIFEST))["inventoryIcons"]]
    for media in sorted({media for icon in json.load(open(IMPORTED))["paperDoll"]["icons"] for media in icon["mediaIds"]}):
        archive, record = map(int, ITEM_IMAGE.fullmatch(media).groups())
        images.append((media, archive, record, f"items/{media}"))

    def build(media, final, name):
        ground = own(media, ["ground"], lambda: {"ground": round(classic_size(media)["height"] * metres_per_classic_pixel(), 6)})
        return [("images", icon_entry(media, icon(final.path, "items"), name, ground))]
    return {media: Unit([final], lambda media=media, final=final, name=name: build(media, final, name))
            for media, archive, record, name in images
            for final in [generated("items", f"texture-{archive}-record-{record:03d}-frame-000")]}


def spell_tile(media):
    """`spell.target` or `spell.element` for a targeting or element tile's media id, else None."""
    return media.rsplit(".", 1)[0] if media.rsplit(".", 1)[0] in ("spell.target", "spell.element") else None


def spell_units():
    """The `spells` area's units: each spell icon, targeting tile and element tile final."""
    notes = ledger("spells")
    replaced = {key: f"spell.icon.{int(m[1])}" for key in notes if (m := SPELL_ICON.fullmatch(key))} | {key: media for key, media in SPELL_TILES.items() if key in notes}
    def build(media, final):
        entry = icon_entry(media, icon(final.path, "spells"), f"spells/{final.key}")
        if tile := spell_tile(media):
            entry["placement"] = own(media, ["aspect"], lambda: {"aspect": spell_tile_shape()[tile]})
        return [("images", entry)]
    return {key: Unit([final], lambda media=media, final=final: build(media, final))
            for key, media in sorted(replaced.items()) for final in [generated("spells", key)]}


def ui_units():
    """The `ui` area's units: each final, a screen at its own size or an escort portrait at PORTRAIT pixels."""
    def build(final):
        image = Image.open(final.path).convert("RGBA")
        if UI_FACE.fullmatch(final.key):
            image = image.crop(image.getchannel("A").getbbox())
            image.thumbnail((PORTRAIT, PORTRAIT), Image.Resampling.LANCZOS)
        return [("images", icon_entry(replaces(final.key), image, f"ui/{final.key}"))]
    return {key: Unit([final], lambda final=final: build(final)) for key in sorted(ledger("ui")) for final in [generated("ui", key)]}


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
    return write(target, buffer.getvalue())


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


def effect_entry(media, sources, finals):
    """A world effect's atlas entry, from its classic frames and their finals."""
    entry = {"replaces": media, **frame_atlas(media, sources, finals, f"{MEDIA}/effects/{media.replace('.', '-')}.png", EFFECT_FRAME)}
    frame = entry.pop("classic")
    names = ["size", "pivot", "sequence", "framesPerSecond", "loops", *(["slot"] if media.startswith("effect.glint.") else [])]
    entry["placement"] = own(media, names, lambda: effect_placement(media, frame))
    if any(not 0 <= index < len(entry["frames"]) for index in entry["placement"]["sequence"]):
        raise SystemExit(f"{media}: its sequence {entry['placement']['sequence']} names a frame its {len(entry['frames'])} frames lack; "
                         f"set it (--set {media} sequence '[...]') or --recompute {media}.")
    return entry


def effect_glow():
    """A soft round white glow, opaque at its centre and fading to nothing at its edge."""
    y, x = np.mgrid[:GLOW, :GLOW]
    r = np.hypot(x - (GLOW - 1) / 2, y - (GLOW - 1) / 2) / (GLOW / 2)
    alpha = (np.clip(1 - r, 0, 1) ** 2 * 255).round().astype(np.uint8)
    image = Image.merge("RGBA", (*[Image.new("L", (GLOW, GLOW), 255)] * 3, Image.fromarray(alpha)))
    buffer = io.BytesIO()
    image.save(buffer, "PNG", optimize=True)
    return write(f"{MEDIA}/effects/glow.png", buffer.getvalue())


# The hanging roots, chains, cage and lanterns hanging.py builds, which the game hangs from their sprites' tops and swings
# when an actor walks through them; each GLB states what it hangs as (its scene's `hangs`), which its entry carries.
HANGING = [(213, 7), (213, 8), (213, 9), (213, 10), (100, 20), (100, 21), (211, 4), (211, 5), (211, 6), (211, 7), (211, 34),
           (210, 22), (210, 24), (210, 25), (210, 26)]

# The sprite meshes: each final in local/art/generated/effects and the classic TEXTURE records it stands in for, each
# built across its sprite's width. Naming a final here is what accepts it.
WORLD_MESHES = [("corpse-bones.glb", [(380, 1), (380, 4)]),
                # rocks.py's placed flats (#10849): the fallen logs towns lay about, and the dungeons' stalagmites.
                ("logs-213-11.glb", [(213, 11)]), ("logs-213-12.glb", [(213, 12)]),
                *((f"stalagmite-300-{record}.glb", [(300, record)]) for record in range(6, 16)),
                # the barrel flat streets, houses and dungeons place (#10848), staves painted from its refreshed flat
                ("barrel-205-0.glb", [(205, 0)]),
                # the common street flats (#10852), in metres at their sprites' size: the lamp posts and the haystack
                # lathed from their refreshed sprites and the well built from parts measured off its sprite (lamps.py,
                # well.py in the task's evidence); the pumps from H3 orbits of their sprites and the fountains from
                # their sprites, through Pixal3D; and #10851's cart 41214 for the handcart
                ("lamp-wooden-lantern.glb", [(210, 14)]), ("lamp-copper-globe.glb", [(210, 15)]),
                ("lamp-iron-globe.glb", [(210, 28)]), ("lamp-iron-lantern.glb", [(210, 29)]),
                ("well.glb", [(212, 0)]), ("haystack.glb", [(212, 1)]), ("fountain-spire.glb", [(212, 2)]),
                ("fountain-column.glb", [(212, 3)]), ("pump-green.glb", [(212, 8)]), ("pump-iron.glb", [(212, 9)]),
                ("handcart.glb", [(205, 27)]),
                # the remaining town flats (#10869): the woodpiles stacked from rocks.py's logs; the signposts, posts and
                # cages built from parts measured off their sprites (town_flats.py in the task's evidence)
                ("woodpile-212-11.glb", [(212, 11)]), ("woodpile-212-12.glb", [(212, 12)]),
                ("signpost-ivy.glb", [(212, 4)]), ("signpost.glb", [(212, 5)]), ("fingerpost.glb", [(212, 6)]),
                ("post.glb", [(212, 13)]), ("post-grass.glb", [(212, 14)]), ("cage.glb", [(212, 15)]), ("cage-pole.glb", [(212, 16)]),
                # hanging.py's roots, chains, cage and lanterns (#10865), which swing as an actor walks through them
                *((f"hanging-{archive}-{record}.glb", [(archive, record)]) for archive, record in HANGING)]


def glb_normals(gltf, data):
    """Every normal a GLB's meshes carry, a row each."""
    start = 20 + int.from_bytes(data[12:16], "little")
    binary = data[start + 8:start + 8 + int.from_bytes(data[start:start + 4], "little")]
    rows = [np.zeros((0, 3), np.float32)]
    for primitive in (primitive for mesh in gltf.get("meshes", []) for primitive in mesh["primitives"]):
        if (index := primitive["attributes"].get("NORMAL")) is not None:
            accessor = gltf["accessors"][index]
            view = gltf["bufferViews"][accessor["bufferView"]]
            rows.append(np.ndarray((accessor["count"], 3), np.float32, binary, view.get("byteOffset", 0) + accessor.get("byteOffset", 0),
                                   (view.get("byteStride", 12), 4)))
    return np.concatenate(rows)


def world_mesh_entry(final, archive, record):
    """A sprite mesh's entry: the GLB as it is, its material slots with an emissive map (`glow`), and what a hanging
    piece hangs as (`hangs`), as its scene states. A normal not of unit length or not finite is refused by name: the
    Engine draws the GLB's normals as they are, and refuses a non-finite one, faulting the site that places it (#10871)."""
    data = final.path.read_bytes()
    gltf = json.loads(data[20:20 + int.from_bytes(data[12:16], "little")])
    normals = glb_normals(gltf, data)
    if (wrong := ~np.isfinite(normals).all(1) | (np.abs(np.linalg.norm(normals, axis=1) - 1) > .01)).any():
        raise SystemExit(f"{final.path.name}: {int(wrong.sum())} of its normals are not of unit length or not finite")
    glow = [slot for slot, material in enumerate(gltf.get("materials", [])) if "emissiveTexture" in material]
    hangs = gltf.get("scenes", [{}])[gltf.get("scene", 0)].get("extras", {}).get("hangs")
    if ((archive, record) in HANGING) != (hangs is not None):
        sys.exit(f"{final.path} is {'a hanging piece whose scene states no' if hangs is None else 'no hanging piece, yet its scene states'} `hangs`.")
    return {"replaces": {"archive": archive, "record": record}, **write(f"{MEDIA}/effects/{final.key}", data), **({"glow": glow} if glow else {}),
            **({"hangs": hangs} if hangs is not None else {}), "placement": own(f"mesh.{archive}.{record}", ["size"], lambda: mesh_placement(archive, record))}


def effect_units():
    """The `effects` area's units: each world effect by its media id, whose finals are its classic frames'; each sprite
    mesh by the classic record it replaces (ARCHIVE-RECORD); and the effect glow (`effectGlow`), made here."""
    units = {}
    for media, (area, key, raw) in sorted(WORLD_EFFECTS.items()):
        sources = sorted((ART / "reference").glob(f"{raw}-frame-*.png"))
        if not sources:
            raise SystemExit(f"effects: no classic frames of {media} under local/art/reference/{raw}: export or link the reference.")
        finals = [generated(area, f"{key}-frame-{frame:03d}") for frame in range(len(sources))]
        units[media] = Unit(finals, lambda media=media, sources=sources, finals=finals: [("effects", effect_entry(media, sources, [final.path for final in finals]))])
    for name, records in WORLD_MESHES:
        final = Final(None, name, ART / "generated" / "effects" / name)
        for archive, record in records:
            units[f"{archive}-{record}"] = Unit([final], lambda final=final, archive=archive, record=record: [
                ("meshes", world_mesh_entry(final, archive, record))])
    units["effectGlow"] = Unit([], lambda: [("effectGlow", effect_glow())])
    return units


def model_units():
    """The `models` area's units, by ARCH3D record id: each remodel written through the source tool's model-mesh, which
    holds it to the classic's volume."""
    notes = ledger("models")

    def build(final):
        written = STAGE / MEDIA / "models"
        written.mkdir(parents=True, exist_ok=True)
        run = subprocess.run(["dotnet", "run", "--project", str(REPOSITORY / "src/Daggerfall.Source.Tool"), "--", "model-mesh",
                              "--arena2", str(ARENA2), "--model", final.key, "--glb", str(final.path), "--output", str(written),
                              "--content-path", f"{MEDIA}/models", "--fit", notes[final.key].get("fit", "hull")], capture_output=True, text=True)
        if run.returncode:
            raise SystemExit(f"models: {final.key}: {(run.stderr or run.stdout).strip()}")
        entry = json.loads(run.stdout.strip().splitlines()[-1])
        print(f"models: {final.key}, {entry['triangles']} triangles", flush=True)
        return [("models", entry)]
    return {key: Unit([final], lambda final=final: build(final)) for key in sorted(notes, key=int) for final in [generated("models", key, ".glb")]}


def classic_size(media):
    """The opaque box of the classic image the game draws for an icon, in classic pixels, read from the import's
    published media: a template's image under the paper doll's, an icon of the classic media by its inventory. On the
    ground it stands at the treasure piles' metres per classic pixel."""
    if m := ITEM_IMAGE.fullmatch(media):
        path = staged(f"{CLASSIC}/item-{m[1]}-{m[2]}.png")
    else:
        published = {artifact["mediaId"]: artifact["path"] for artifact in json.load(open(STAGED / "worldrpg/media/classic-media-inventory.json"))["artifacts"]}
        path = staged(published[media])
    left, top, right, bottom = Image.open(path).convert("RGBA").getchannel("A").getbbox()
    return {"width": right - left, "height": bottom - top}


def surface_units(area):
    """The `surfaces` or `terrain` area's units: each final `texture-AAA-R`, from its family's folder, by the classic
    texture record it replaces."""
    held = {path.stem: path for path in (ART / "generated" / area).glob("*/texture-*.png") if path.parent.name != "raw"}

    def build(final):
        archive, record = map(int, SURFACE.fullmatch(final.key).groups())
        return [("surfaces", {"replaces": {"archive": archive, "record": record}, **world_png(Image.open(final.path), f"{MEDIA}/{area}/{final.key}.png")})]
    return {key: Unit([final], lambda final=final: build(final))
            for key in sorted(ledger(area)) for final in [Final(area, key, held.get(key, ART / "generated" / area / f"{key}.png"))]}


def glow_of(archive, record):
    """How to cut a flat's emission map from a tile, for a flat that gives light."""
    if (archive, record) in GLOWING:
        return lambda tile: reduce.glow(tile, cool=(archive, record) in COOL_GLOW)
    return None


def flat_units():
    """The `flats` area's units, by classic record (`texture-AAA-record-RRR`): every placed record with classic frames,
    each frame drawn from its `flats` final, or, for a record an item also draws and the area has no accepted final of
    its own for, its `items` final."""
    notes = {area: ledger(area) for area in ("flats", "items")}

    def final(key):
        own_final = notes["flats"].get(key, {}).get("status") == "accepted" or notes["items"].get(key, {}).get("status") != "accepted"
        return generated("flats" if own_final else "items", key)

    def build(archive, record, stem, sources, finals):
        entry = {"replaces": {"archive": archive, "record": record},
                 **frame_atlas(stem, sources, [final.path for final in finals], f"{MEDIA}/flats/{stem}.png", crunch=FLAT_CRUNCH,
                               glow=glow_of(archive, record))}
        return [("flats", with_placement(f"flat.{archive}.{record}", entry, lambda classic: flat_placement(archive, record, classic, len(entry["frames"]))))]
    units = {}
    for row in csv.DictReader(open(ART / "reference/flats/usage.csv")):
        archive, record = int(row["archive"]), int(row["record"])
        stem = f"texture-{archive:03d}-record-{record:03d}"
        sources = sorted((ART / "reference/flats" / row["family"] / f"texture-{archive:03d}/flat").glob(f"{stem}-frame-*-source.png"))
        if sources:
            finals = [final(f"{stem}-frame-{frame:03d}") for frame in range(len(sources))]
            units[stem] = Unit(finals, lambda archive=archive, record=record, stem=stem, sources=sources, finals=finals: build(archive, record, stem, sources, finals))
    return units


def people_units():
    """The `people` area's units, by classic record (`texture-AAA-record-RRR`): every placed person, drawn by the final of
    the first record with the same standing frame that the ledger accepts, so records with one frame share its atlas."""
    notes = ledger("people")
    placed_people = []
    for row in csv.DictReader(open(ART / "reference/people/usage.csv")):
        if not any(int(row[f"{place}_placements"]) for place in ("exterior", "interior", "dungeon")):
            continue
        stem = f"texture-{int(row['archive']):03d}-record-{int(row['record']):03d}"
        source = ART / "reference/people" / row["family"] / f"texture-{int(row['archive']):03d}/person/{stem}-frame-000-source.png"
        image = Image.open(source).convert("RGBA")
        placed_people.append((int(row["archive"]), int(row["record"]), stem, source, (image.size, image.tobytes())))
    painted = {}
    for _, _, stem, source, frame in placed_people:
        if frame not in painted and notes.get(f"{stem}-frame-000", {}).get("status") == "accepted":
            painted[frame] = (stem, source)
    atlases = {}

    def build(archive, record, stem, source):
        if stem not in atlases:
            atlases[stem] = frame_atlas(stem, [source], [ART / "generated/people" / f"{stem}-frame-000.png"], f"{MEDIA}/people/{stem}.png",
                                        crunch=PEOPLE_CRUNCH, colours=None)
        entry = {"replaces": {"archive": archive, "record": record}, **atlases[stem]}
        return [("people", with_placement(f"person.{archive}.{record}", entry, lambda classic: flat_placement(archive, record, classic, 1, person=True)))]
    return {stem: Unit([generated("people", f"{by[0]}-frame-000")], lambda archive=archive, record=record, by=by: build(archive, record, *by))
            for archive, record, stem, source, frame in placed_people for by in [painted.get(frame, (stem, source))]}


def people_rig():
    """Copies the publication's views into the pack and states them, their view sets, clips and layer stack."""
    def copied(source, target):
        return write(target, source.read_bytes())

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


# A final an entry draws: its ledger area (None for one this file names, which accepts it) and art key, and where this
# checkout would hold it.
Final = namedtuple("Final", "ledger key path")
# What one entry or a few replace, keyed as its area keys them: the finals it draws, and how to build its entries,
# [(section, entry)], into the staging folder.
Unit = namedtuple("Unit", "finals build")
# An area: the media folders its files are in, the payload sections its entries are in, its units by key in the order its
# sections keep them, and what a whole-area run also writes once every unit is promoted (`finish(payload, units)`). A
# section maps to the unit an entry is (a list of entries), to KEYED (an object of entries by unit) or to (ONE, unit) (the
# entry itself).
Area = namedtuple("Area", "folders sections units finish", defaults=(None,))
KEYED, ONE = "keyed", "one"


def stem(entry):
    return Path(entry["path"]).stem


def record_stem(entry):
    return f"texture-{entry['replaces']['archive']:03d}-record-{entry['replaces']['record']:03d}"


def worn_tints(payload, units):
    """The dye and metal tints, from every worn final of a classic record (a named design is left out: the tints are
    measured against the classic images of each record)."""
    finals = [(key, ART / "generated/worn" / f"{key}.png") for key in units if WORN.fullmatch(key)]
    clothing = [final for final in finals if int(WORN.fullmatch(final[0])[1]) in CLOTHING]
    metal = [final for final in finals if int(WORN.fullmatch(final[0])[1]) not in CLOTHING]
    payload["artReplacements"]["dyeTints"] = dye_tints(clothing, DYES, "red") | dye_tints(metal, METALS, "daedric")


AREAS = {
    "paper-doll": Area(["paper-doll"], {"images": stem}, lambda: doll_units("paper-doll")),
    "worn": Area(["worn"], {"images": stem}, lambda: doll_units("worn"), worn_tints),
    "items": Area(["items"], {"images": lambda entry: entry["replaces"]}, item_units),
    "spells": Area(["spells"], {"images": stem}, spell_units),
    "effects": Area(["effects"], {"effects": lambda entry: entry["replaces"],
                                  "meshes": lambda entry: f"{entry['replaces']['archive']}-{entry['replaces']['record']}",
                                  "effectGlow": (ONE, "effectGlow")}, effect_units),
    "surfaces": Area(["surfaces"], {"surfaces": stem}, lambda: surface_units("surfaces")),
    "terrain": Area(["terrain"], {"surfaces": stem}, lambda: surface_units("terrain")),
    "flats": Area(["flats"], {"flats": record_stem}, flat_units),
    "people": Area(["people"], {"people": record_stem}, people_units),
    "people-rig": Area(["people-rigs"], {"peopleRig": (ONE, PEOPLE_RIG_FIGURE)}, lambda: {PEOPLE_RIG_FIGURE: Unit([], lambda: [("peopleRig", people_rig())])}),
    "ui": Area(["ui"], {"images": stem}, ui_units),
    "models": Area(["models"], {"models": KEYED}, model_units),
}
# The order of a section several areas share, whatever area wrote last.
SECTION_ORDER = {"images": lambda entry: entry["replaces"],
                 "surfaces": lambda entry: (entry["replaces"]["archive"], entry["replaces"]["record"])}


def paths(value):
    """Every content path an entry names, its own and its parts'."""
    if isinstance(value, dict):
        return [*([value["path"]] if isinstance(value.get("path"), str) else []), *(p for part in value.values() for p in paths(part))]
    if isinstance(value, list):
        return [p for part in value for p in paths(part)]
    return []


def owned(area, entry):
    """Whether an entry is the area's: its files are in the area's folders."""
    return any(path.startswith(f"{MEDIA}/{folder}/") for path in paths(entry)[:1] for folder in area.folders)


def gather(payload, area):
    """The area's entries in the payload, as (section, unit, entry)."""
    found = []
    for section, unit in area.sections.items():
        value = payload["artReplacements"].get(section)
        if value is None:
            continue
        pairs = value.items() if unit == KEYED else [(unit[1], value)] if isinstance(unit, tuple) else [(unit(entry), entry) for entry in value]
        found += [(section, key, entry) for key, entry in pairs if owned(area, entry)]
    return found


def scatter(payload, area, entries, order):
    """Places the area's entries, (section, unit, entry), in the payload in their units' order, keeping every other area's."""
    art = payload["artReplacements"]
    for section, unit in area.sections.items():
        mine = [(key, entry) for s, key, entry in sorted(entries, key=lambda e: order.get(e[1], len(order))) if s == section]
        if unit == KEYED:
            art[section] = dict(mine)
        elif isinstance(unit, tuple):
            if mine:
                art[section] = mine[0][1]
            else:
                art.pop(section, None)
        else:
            entries_ = [entry for entry in art.get(section, []) if not owned(area, entry)] + [entry for _, entry in mine]
            art[section] = sorted(entries_, key=SECTION_ORDER[section]) if section in SECTION_ORDER else entries_


def promote_area(payload, name, keys):
    """Promotes an area into the payload, its files into the staging folder, and returns the content files it no longer
    names, and the keys that named its units.

    With `keys`, it promotes the units they name, by the unit's key or a final it draws, and every unit that draws a final
    of theirs (records sharing an atlas, a mesh's records), replacing just their entries and files; it refuses a unit the
    ledger doesn't accept or this checkout has no final for. With none it promotes every unit whose finals the ledger
    accepts, and refuses while it accepts one this checkout has no final for, since the run would drop its entry; it names
    any entry it removes."""
    area = AREAS[name]
    units = area.units()
    notes = {}

    def accepted(final):
        if final.ledger is None:
            return True
        if final.ledger not in notes:
            notes[final.ledger] = ledger(final.ledger)
        return notes[final.ledger].get(final.key, {}).get("status") == "accepted"

    def ready(key):
        return all(accepted(final) and final.path.exists() for final in units[key].finals)
    if keys:
        named = [key for key, unit in units.items() if key in keys or any(final.key in keys for final in unit.finals)]
        if not named:
            return set(), set()
        refused = [key for key in named if not ready(key)]
        if refused:
            raise SystemExit(f"{name}: {', '.join(refused)} draws a final the ledger doesn't accept or this checkout has no final for.")
        drawn = {final.path for key in named for final in units[key].finals}
        chosen = [key for key, unit in units.items() if key in named or ready(key) and drawn & {final.path for final in unit.finals}]
    else:
        missing = sorted({final.key for unit in units.values() for final in unit.finals if accepted(final) and not final.path.exists()})
        if missing:
            listed = ", ".join(missing[:12]) + (f" and {len(missing) - 12} more" if len(missing) > 12 else "")
            raise SystemExit(f"{name}: this checkout has no final for {listed}, which the ledger accepts and a whole-area run would drop; "
                             "promote this checkout's own finals with --key.")
        chosen = [key for key in units if ready(key)]
    built = [(section, key, entry) for key in chosen for section, entry in units[key].build()]
    old = gather(payload, area)
    replaced = set(chosen) if keys else {key for _, key, _ in old} | set(chosen)
    entries = [(section, key, entry) for section, key, entry in old if key not in replaced] + built
    removed = sorted({key for _, key, _ in old} - {key for _, key, _ in entries})
    if removed:
        print(f"{name}: removes {', '.join(removed)}, whose finals the ledger no longer accepts", flush=True)
    scatter(payload, area, entries, {key: index for index, key in enumerate(units)})
    if not keys and area.finish:
        area.finish(payload, chosen)
    named_now = {path for _, _, entry in entries for path in paths(entry)}
    if keys:
        candidates = {path for _, key, entry in old if key in replaced for path in paths(entry)}
    else:
        candidates = {file.relative_to(CONTENT).as_posix() for folder in area.folders for file in (CONTENT / MEDIA / folder).rglob("*") if file.is_file()}
    size = sum(file.stat().st_size for file in STAGE.rglob("*") if file.is_file() and any(file.relative_to(STAGE).as_posix().startswith(f"{MEDIA}/{folder}/") for folder in area.folders))
    print(f"{name}: {len(chosen)} of {len(units)} promoted, {size / 1e6:.1f} MB written", flush=True)
    return candidates - named_now, keys & {name for key in chosen for name in [key, *(final.key for final in units[key].finals)]}


def commit(stale, folders):
    """Moves the staged files into content, and removes the files no entry names any more and the folders left empty."""
    for target in stale:
        (CONTENT / target).unlink(missing_ok=True)
    for file in sorted(STAGE.rglob("*")):
        if file.is_file():
            target = CONTENT / file.relative_to(STAGE)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(file, target)
    for folder in folders:
        for directory in sorted((CONTENT / MEDIA / folder).rglob("*"), reverse=True):
            if directory.is_dir() and not any(directory.iterdir()):
                directory.rmdir()


def placed_entries(payload):
    """Every entry of the pack that may state a placement, with the id `--set` and `--recompute` name it by: what it replaces
    for an image or effect, `flat.ARCHIVE.RECORD`, `person.ARCHIVE.RECORD`."""
    art = payload["artReplacements"]
    named = {"flats": "flat", "people": "person", "meshes": "mesh"}
    found = [(entry["replaces"], entry) for section in ("images", "effects") for entry in art.get(section, [])]
    for section, kind in named.items():
        found += [(f"{kind}.{entry['replaces']['archive']}.{entry['replaces']['record']}", entry) for entry in art.get(section, [])]
    found += [(f"tree.{key.replace('-', '.')}", entry) for key, entry in art.get("trees", {}).items()]
    return found


def replaced_classic(payload):
    """The classic identities the pack replaces, by kind and sorted: what an entry stands in for, nothing of how it looks,
    so the list changes only when an entry is added or withdrawn, never when one is redone."""
    art = payload["artReplacements"]
    record = lambda entry: f"{entry['replaces']['archive']}-{entry['replaces']['record']}"
    return {
        "kind": "worldrpg.replaced-classic",
        "media": sorted({entry["replaces"] for entry in [*art.get("images", []), *art.get("effects", [])]}),
        **{kind: sorted({record(entry) for entry in art.get(kind, [])}, key=lambda key: tuple(map(int, key.split("-"))))
           for kind in ("surfaces", "flats", "people", "meshes")},
        "models": sorted(art.get("models", {}), key=int),
    }


def main(argv=None):
    global STAGE
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--area", action="append", choices=list(AREAS), default=[])
    ap.add_argument("--key", action="append", default=[],
                    help="promote only the units this names, by their key or a final they draw, leaving the rest of the area "
                    "(and the worn tints) as it is")
    ap.add_argument("--recompute", action="append", default=[], metavar="ID",
                    help="take the placement of the entry replacing ID (or every promoted entry, with `all`) from the original again")
    ap.add_argument("--set", action="append", nargs=3, default=[], metavar=("ID", "NAME", "VALUE"),
                    help="set placement value NAME (size.width, framesPerSecond, ground, waist, ...) of the entry replacing ID "
                    "to the JSON VALUE, marked as set by hand, after any --area")
    a = ap.parse_args(argv)
    if not a.area and not a.set:
        ap.error("give an --area to promote or a --set")
    if a.key and not a.area:
        ap.error("--key promotes within the --area named")
    KEPT.clear(), KEPT_ART.clear(), KEPT_MASK.clear(), RECOMPUTE.clear()
    RECOMPUTE.update(a.recompute)
    payload = json.loads(PAYLOAD.read_text()) if PAYLOAD.exists() else {"artReplacements": {"images": []}}
    placed = lambda: [(identity, entry) for identity, entry in placed_entries(payload) if "placement" in entry]
    for identity, entry in placed():
        KEPT[identity], KEPT_ART[identity], KEPT_MASK[identity] = entry["placement"], entry.get("sha256"), entry.get("mask")
    with tempfile.TemporaryDirectory() as stage:
        STAGE = Path(stage)
        stale, matched = set(), set()
        for area in a.area:
            gone, keys = promote_area(payload, area, set(a.key))
            stale |= gone
            matched |= keys
        if unmatched := sorted(set(a.key) - matched):
            raise SystemExit(f"No unit of {', '.join(a.area)} is or draws {', '.join(unmatched)}.")
        records = [(entry["replaces"]["archive"], entry["replaces"]["record"]) for entry in payload["artReplacements"].get("surfaces", [])]
        if len(set(records)) != len(records):
            raise SystemExit(f"Two areas replace one record: {sorted(r for r in set(records) if records.count(r) > 1)}.")
        for identity, entry in placed():
            if KEPT_ART.get(identity) and entry["sha256"] != KEPT_ART[identity] and entry["placement"] is KEPT[identity]:
                print(f"{identity}: new art kept its placement; check it, or --recompute {identity}", flush=True)
        for media, name, value in a.set:
            entry = next((entry for identity, entry in placed() if identity == media), None)
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
        commit(stale, [folder for area in a.area for folder in AREAS[area].folders])
        STAGE = None
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    REPLACED.write_text(json.dumps(replaced_classic(payload), indent=1) + "\n")


if __name__ == "__main__":
    main()
