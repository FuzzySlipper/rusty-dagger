#!/usr/bin/env python3
"""Promote ledger-accepted finals into the tracked refreshed-art content pack.

Each accepted final in gen-raw/AREA stands in for one classic media id, which the game draws in its place
when the refreshed art set is chosen (classic stays selectable). For each final this:

  - finds the classic media id it replaces, from its art key: a worn layer `texture-A-record-R-frame-000` is
    the paper doll's `paper-doll.A.R`, a body `bodies--bodyGNi{0,1}--...` is
    `character.body-{unclothed,clothed}.{gender}.0N.0`, a head `faces--faceGNi0--...-record-RRR-...` is
    `character.head.{gender}.0N.R`, and a vampire head, beast head or beast doll `creatures--NAMEi0--...-record-RRR-...`
    is `character.FAMILY.NAMEi0.R`, FAMILY being the name's first four letters (vamp, were, wolf, boar);
  - takes its doll-space rect from its sidecar, as doll_preview.py draws it, crops transparent margins and moves
    the rect to match;
  - writes it as WebP (lossy colour at QUALITY, lossless alpha) to content/worldrpg/media/refreshed/AREA/;
  - records it in the pack payload's `artReplacements` section with its size, bytes and hash;
  - for clothing (archives 235-242), and for armour and weapons the classic draws in other metals, also writes
    its dye mask: the neutral cloth or bare steel the classic dye or metal recolours, where the final's colour is
    near grey (saturation under NEUTRAL) and not an outline (DARK), less what is coloured of its own (a green
    vest, a leather belt, a gold buckle). The game multiplies that mask's pixels by the worn dye's or metal's tint.

The pack also states each clothing dye's and metal's tint (`dyeTints`): the colour, and a brightness the multiply
cannot reach on its own, that turn the neutral cloth or steel into the classic dye or metal, measured as the mean
colour of the pixels each classic dye or metal recolours against the mean of the final's neutral pixels (read from
the import's published paper-doll images under local/import/staging).

Two areas promote icons rather than their own finals, each standing in for a classic UI image:

  - `items`: the inventory images of the templates the paper doll does not draw (`item.ARCHIVE.RECORD`, named in
    the staged `paperDoll.icons` section), each from the accepted `items` final of its record, and every icon the
    classic media publishes for an item without a template (`inventoryIcons`, `inventory.icon.ITEM`: the potion
    recipes' bottles and gold) from the accepted final of the record it draws. A worn item's icon is its paper-doll
    image, which the `worn` area already replaces. An icon whose record has no accepted final stays classic.
  - `spells`: each accepted spell icon `icons--icon-NNN--icon` as `spell.icon.N`, cell N of the classic
    `spell.icons` atlas, and each accepted targeting and element tile as `spell.target.N` and `spell.element.N`, tile N
    of the classic `spell.selectors` atlas.

The `effects` area promotes the effects the game animates: blood and the magic sparkle (TEXTURE.380, from the
`effects` finals), each spell element's missile and impact (TEXTURE.375-379, from the `spells` finals), and the
inventory window's magic and coin glints (TEXTURE.434 records 5 and 6, from the `effects` finals). Each
effect becomes one PNG atlas (the renderer reads PNG) of its frames in classic order, all cropped to one box that
holds every frame's art and the classic frame's place on the generation canvas, so the frames keep their relative
size and position. The entry states where the classic frame sits in that box (`classic`, fractions of the frame,
top-down), from which the game sizes and anchors it as the classic. An effect missing an accepted frame stays
classic. The area also writes the soft white glow (`effectGlow`) the game's spell trails and impact bursts tint
in each element's colour, and the world meshes (`meshes`): the `effects` final `corpse-bones.glb`, a skull-free bone
pile resting on the floor, stands in for TEXTURE.380 record 1 (the corpse every human enemy class shares, men and
women alike, so it is no one in particular) and record 4 (the bone pile placed in dungeons). Each entry states the
mesh's footprint, its longest side in metres, so the game draws it at the width of the classic sprite it replaces.

The `surfaces` area promotes the walls, floors and ceilings (`surfaces`): each accepted final `texture-AAA-R`, from
its family's folder under gen-raw/surfaces (tools/surfaces/batch.py), stands in for classic TEXTURE.AAA record R as an
8-bit RGBA PNG at its own size, and the game draws it on the location materials and terrain that draw that record.

Icons are written at their area's ICON pixels on the longer side and carry no doll rect.

A worn image and an item icon is also what a dropped item stands as on the ground, and the renderer reads PNG, so
each also has a `world` PNG of at most WORLD_ICON pixels on its longer side, and a worn image's dye mask a `world`
PNG the same size as the image's. An item icon's `world` also states its classic image's size in classic pixels (its
opaque box in the import's published image), from which the game sizes it on the ground as a worn image is sized from its doll rect.

Only finals whose ledger/AREA-notes.json status is `accepted` are promoted. Rerunning an area replaces that
area's entries and files and keeps every other area's.

    promote.py --area paper-doll --area worn --area items --area spells --area effects --area surfaces
"""
import argparse, hashlib, io, json, re
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

from doll_preview import AUTHORING, doll_layer, placed, worn_layer

REPOSITORY = AUTHORING.parent
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/media/refreshed"
PAYLOAD = CONTENT / "worldrpg/payloads/daggerfall.art-replacements.json"
QUALITY = 90
AREAS = ("paper-doll", "worn", "items", "spells", "effects", "surfaces")
SURFACE = re.compile(r"texture-(\d{3})-(\d+)")
ICON = {"items": 256, "spells": 128}   # pixels on an icon's longer side: an item slot, a spell glyph of about 3rem
CLASSIC_MANIFEST = REPOSITORY / "local/import/staging/worldrpg/imports/shared/media/classic/manifest.json"
IMPORTED = REPOSITORY / "local/import/staging/worldrpg/payloads/daggerfall.imported.json"
ITEM_IMAGE = re.compile(r"item\.(\d+)\.(\d+)")
STAGED = REPOSITORY / "local/import/staging"
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
WORLD_ICON = 128                 # pixels on a dropped item's longer side: a small pile on the floor
EFFECT_FRAME = 256               # pixels on an effect frame's longer side
GLOW = 64                        # pixels on the particle glow's side
CLOTHING = range(235, 243)
NEUTRAL = (14, 32)   # saturation (max less min channel, 0-255) over which a pixel fades from dyed cloth to its own colour
EDGE = 2             # pixels in from the piece's edge where its dye mask starts
DARK = (40, 70)      # brightness (max channel) under which a pixel is an outline, not cloth: a dye barely moves it, and
                     # tinting it rims a piece of its own colour (tan trousers) in the dye
DYES = ["blue", "grey", "red", "dark-brown", "purple", "light-brown", "white", "aquamarine", "yellow", "green"]
METALS = ["iron", "silver", "elven", "dwarven", "mithril", "adamantium", "ebony", "orcish", "daedric"]
CLASSIC = REPOSITORY / "local/import/staging/worldrpg/media/character/paper-doll"

WORN = re.compile(r"texture-(\d+)-record-(\d+)-frame-000")
BODY = re.compile(r"bodies--body(\d)(\d)(i[01])--.*")
FACE = re.compile(r"faces--face(\d)(\d)i0--.*-record-(\d+)-frame-000")
CREATURE = re.compile(r"creatures--((\w{4})\d\di0)--.*-record-(\d+)-frame-000")
GENDER = {"0": "male", "1": "female"}


def replaces(key):
    """The classic media id a final's art key stands in for."""
    if m := WORN.fullmatch(key):
        return f"paper-doll.{int(m[1])}.{int(m[2])}"
    if m := BODY.fullmatch(key):
        return f"character.body-{'unclothed' if m[3] == 'i0' else 'clothed'}.{GENDER[m[1]]}.0{m[2]}.0"
    if m := FACE.fullmatch(key):
        return f"character.head.{GENDER[m[1]]}.0{m[2]}.{int(m[3])}"
    if m := CREATURE.fullmatch(key):
        return f"character.{m[2]}.{m[1]}.{int(m[3])}"
    raise SystemExit(f"No classic media id for art key {key}.")


def rect_of(area, key, path):
    """The final's doll-space rect, from its sidecar as doll_preview.py places it."""
    if area == "worn":
        layer = worn_layer(key, None)
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


def world_size(image):
    """The size of an image's world PNG: at most WORLD_ICON pixels on its longer side."""
    k = min(1, WORLD_ICON / max(image.size))
    return max(1, round(image.width * k)), max(1, round(image.height * k))


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


def promote(area, key, path):
    image = Image.open(path).convert("RGBA")
    x, y, w, h = rect_of(area, key, path)
    k = image.width / w
    box = image.getchannel("A").getbbox()
    if box is None:
        raise SystemExit(f"{path} is fully transparent.")
    image = image.crop(box)
    classic = replaces(key)
    entry = {"replaces": classic, "mediaId": f"refreshed.{classic}", **webp(image, f"{MEDIA}/{area}/{key}.webp"),
             "width": image.width, "height": image.height,
             "doll": {"x": round(x + box[0] / k, 4), "y": round(y + box[1] / k, 4),
                      "width": round(image.width / k, 4), "height": round(image.height / k, 4)}}
    world = world_size(image)
    if area == "worn":
        entry["world"] = world_png(image.resize(world, Image.Resampling.LANCZOS), f"{MEDIA}/{area}/{key}.png")
    if (m := WORN.fullmatch(key)) and dyed(int(m[1]), int(m[2])):
        mask = dye_mask(image)
        entry["dyeMask"] = {"mediaId": f"refreshed.{classic}.dye", **webp(mask, f"{MEDIA}/{area}/{key}-dye.webp", lossless=True)}
        if area == "worn":
            entry["dyeMask"]["world"] = world_png(mask.resize(world, Image.Resampling.LANCZOS), f"{MEDIA}/{area}/{key}-dye.png")
    return entry


def ledger(area):
    return json.load(open(AUTHORING / "ledger" / f"{area}-notes.json"))


def accepted_final(area, notes, key):
    path = AUTHORING / "gen-raw" / area / f"{key}.png"
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
        entries.append(icon_entry(media, icon(final, "items"), name, world=classic_size(media)))
    if classic:
        print(f"items: {len(classic)} icons stay classic, their records have no accepted final: {', '.join(classic)}")
    return entries


def spell_icons():
    notes = ledger("spells")
    replaced = {key: f"spell.icon.{int(m[1])}" for key in notes if (m := SPELL_ICON.fullmatch(key))} | {key: media for key, media in SPELL_TILES.items() if key in notes}
    return [icon_entry(media, icon(final, "spells"), f"spells/{key}") for key, media in sorted(replaced.items())
            if (final := accepted_final("spells", notes, key))]


def classic_box(source):
    """Where run.py placed a classic frame on the generation canvas: an integer nearest-neighbour scale, centred."""
    w, h = Image.open(source).size
    scale = max(1, int(CANVAS * OCCUPANCY / max(w, h)))
    x, y = (CANVAS - w * scale) // 2, (CANVAS - h * scale) // 2
    return x, y, x + w * scale, y + h * scale


def world_effects():
    """An atlas entry for every world effect whose every frame has an accepted final."""
    entries, classic = [], []
    for media, (area, key, raw) in sorted(WORLD_EFFECTS.items()):
        notes = ledger(area)
        sources = sorted((AUTHORING / "df-raw").glob(f"{raw}-frame-*.png"))
        finals = [accepted_final(area, notes, f"{key}-frame-{frame:03d}") for frame in range(len(sources))]
        if not sources or None in finals:
            classic.append(media)
            continue
        boxes = {classic_box(source) for source in sources}
        if len(boxes) != 1:
            raise SystemExit(f"{media}: the classic frames differ in size, so no one box places them.")
        region = boxes.pop()
        frames = [Image.open(final).convert("RGBA") for final in finals]
        box = region
        for frame in frames:
            if (art := frame.getchannel("A").getbbox()) is not None:
                box = (min(box[0], art[0]), min(box[1], art[1]), max(box[2], art[2]), max(box[3], art[3]))
        width, height = box[2] - box[0], box[3] - box[1]
        k = min(1, EFFECT_FRAME / max(width, height))
        size = (max(1, round(width * k)), max(1, round(height * k)))
        columns = int(np.ceil(np.sqrt(len(frames))))
        atlas = Image.new("RGBA", (columns * size[0], int(np.ceil(len(frames) / columns)) * size[1]))
        rects = []
        for index, frame in enumerate(frames):
            x, y = index % columns * size[0], index // columns * size[1]
            atlas.paste(frame.crop(box).resize(size, Image.Resampling.LANCZOS), (x, y))
            rects.append({"x": x, "y": y, "width": size[0], "height": size[1]})
        buffer = io.BytesIO()
        atlas.save(buffer, "PNG", optimize=True)
        data = buffer.getvalue()
        target = f"{MEDIA}/effects/{media.replace('.', '-')}.png"
        (CONTENT / target).write_bytes(data)
        entries.append({"replaces": media, "path": target, "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest(),
                        "width": atlas.width, "height": atlas.height, "frames": rects,
                        "classic": {"x": round((region[0] - box[0]) / width, 4), "y": round((region[1] - box[1]) / height, 4),
                                    "width": round((region[2] - region[0]) / width, 4), "height": round((region[3] - region[1]) / height, 4)}})
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
        final = AUTHORING / "gen-raw" / "effects" / name
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
    published media: a template's image under the paper doll's, an icon of the classic media by its inventory."""
    if m := ITEM_IMAGE.fullmatch(media):
        path = CLASSIC / f"item-{m[1]}-{m[2]}.png"
    else:
        published = {artifact["mediaId"]: artifact["path"] for artifact in json.load(open(STAGED / "worldrpg/media/classic-media-inventory.json"))["artifacts"]}
        path = STAGED / published[media]
    left, top, right, bottom = Image.open(path).convert("RGBA").getchannel("A").getbbox()
    return {"width": right - left, "height": bottom - top}


def surfaces():
    """Each accepted surface final's entry, by the classic texture record it replaces."""
    finals = {path.stem: path for path in (AUTHORING / "gen-raw" / "surfaces").glob("*/texture-*.png") if path.parent.name != "raw"}
    promoted = []
    for key, note in ledger("surfaces").items():
        if note.get("status") != "accepted":
            continue
        if key not in finals:
            raise SystemExit(f"{key} is accepted in ledger/surfaces-notes.json but has no final under gen-raw/surfaces.")
        archive, record = map(int, SURFACE.fullmatch(key).groups())
        promoted.append({"replaces": {"archive": archive, "record": record},
                         **world_png(Image.open(finals[key]), f"{MEDIA}/surfaces/{key}.png")})
    return sorted(promoted, key=lambda entry: (entry["replaces"]["archive"], entry["replaces"]["record"]))


def icon_entry(replaces, image, name, world=None):
    """An icon's entry; with its classic size, also its world PNG."""
    entry = {"replaces": replaces, "mediaId": f"refreshed.{replaces}", **webp(image, f"{MEDIA}/{name}.webp"),
             "width": image.width, "height": image.height}
    if world is not None:
        entry["world"] = world_png(image.resize(world_size(image), Image.Resampling.LANCZOS), f"{MEDIA}/{name}.png") | {"classic": world}
    return entry


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--area", action="append", choices=AREAS, required=True)
    a = ap.parse_args()
    payload = json.loads(PAYLOAD.read_text()) if PAYLOAD.exists() else {"artReplacements": {"images": []}}
    images = payload["artReplacements"]["images"]
    for area in a.area:
        if area == "effects":
            directory = CONTENT / MEDIA / "effects"
            directory.mkdir(parents=True, exist_ok=True)
            for old in [*directory.glob("*.png"), *directory.glob("*.glb")]:
                old.unlink()
            effects = payload["artReplacements"]["effects"] = world_effects()
            payload["artReplacements"]["effectGlow"] = effect_glow()
            payload["artReplacements"].pop("corpseMesh", None)
            payload["artReplacements"]["meshes"] = world_meshes()
            print(f"effects: {len(effects)} effects, {sum(entry['byteLength'] for entry in effects) / 1e6:.1f} MB", flush=True)
            continue
        if area == "surfaces":
            directory = CONTENT / MEDIA / "surfaces"
            directory.mkdir(parents=True, exist_ok=True)
            for old in directory.glob("*.png"):
                old.unlink()
            promoted = payload["artReplacements"]["surfaces"] = surfaces()
            print(f"surfaces: {len(promoted)} finals, {sum(entry['byteLength'] for entry in promoted) / 1e6:.1f} MB", flush=True)
            continue
        notes = ledger(area)
        accepted = sorted(key for key, note in notes.items() if note.get("status") == "accepted")
        directory = CONTENT / MEDIA / area
        directory.mkdir(parents=True, exist_ok=True)
        for old in [*directory.glob("*.webp"), *directory.glob("*.png")]:
            old.unlink()
        images[:] = [entry for entry in images if not entry["path"].startswith(f"{MEDIA}/{area}/")]
        if area in ("items", "spells"):
            promoted = inventory_icons() if area == "items" else spell_icons()
            images.extend(promoted)
            print(f"{area}: {len(promoted)} icons, {sum(entry['byteLength'] for entry in promoted) / 1e6:.1f} MB", flush=True)
            continue
        for key in accepted:
            path = AUTHORING / "gen-raw" / area / f"{key}.png"
            if not path.exists():
                raise SystemExit(f"{key} is accepted in ledger/{area}-notes.json but {path} does not exist.")
            images.append(promote(area, key, path))
        if area == "worn":
            finals = [(key, AUTHORING / "gen-raw" / area / f"{key}.png") for key in accepted]
            clothing = [final for final in finals if int(WORN.fullmatch(final[0])[1]) in CLOTHING]
            metal = [final for final in finals if int(WORN.fullmatch(final[0])[1]) not in CLOTHING]
            payload["artReplacements"]["dyeTints"] = dye_tints(clothing, DYES, "red") | dye_tints(metal, METALS, "daedric")
        size = sum(entry["byteLength"] + entry.get("dyeMask", {}).get("byteLength", 0) for entry in images if entry["path"].startswith(f"{MEDIA}/{area}/"))
        print(f"{area}: {len(accepted)} finals, {size / 1e6:.1f} MB", flush=True)
    images.sort(key=lambda entry: entry["replaces"])
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")


if __name__ == "__main__":
    main()
