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

  - `items`: every inventory icon the import publishes (`inventoryIcons` in its staged classic manifest) as
    `inventory.icon.ITEM`, from the accepted final of the record it draws: a worn layer for the paper-doll
    archives (equipment icons are those images), else the `items` final. An icon that is a classic dye or metal
    variant of its record (found by comparing it with the published variants) is the final tinted in that dye or
    metal through its dye mask. An icon whose record has no accepted final stays classic.
  - `spells`: each accepted spell icon `icons--icon-NNN--icon` as `spell.icon.N`, cell N of the classic
    `spell.icons` atlas. The `spells` area's other finals (missiles, impacts, tiles) wait for the screens that
    draw them.

Icons are written at their area's ICON pixels on the longer side and carry no doll rect.

Only finals whose ledger/AREA-notes.json status is `accepted` are promoted. Rerunning an area replaces that
area's entries and files and keeps every other area's.

    promote.py --area paper-doll --area worn --area items --area spells
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
AREAS = ("paper-doll", "worn", "items", "spells")
ICON = {"items": 256, "spells": 128}   # pixels on an icon's longer side: an item slot, a spell glyph of about 3rem
CLASSIC_MANIFEST = REPOSITORY / "local/import/staging/worldrpg/imports/shared/media/classic/manifest.json"
STAGED = REPOSITORY / "local/import/staging"
SPELL_ICON = re.compile(r"icons--icon-(\d+)--icon")
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
    if (m := WORN.fullmatch(key)) and dyed(int(m[1]), int(m[2])):
        entry["dyeMask"] = {"mediaId": f"refreshed.{classic}.dye", **webp(dye_mask(image), f"{MEDIA}/{area}/{key}-dye.webp", lossless=True)}
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


def tinted(image, tint):
    """The image with its dye mask multiplied by a dye's or metal's tint, as the game draws a tinted layer."""
    rgba = np.asarray(image, np.float32)
    weight = np.asarray(dye_mask(image), np.float32)[..., 3:] / 255
    colour = np.array([int(tint["multiply"][at:at + 2], 16) / 255 * tint["brightness"] for at in (1, 3, 5)], np.float32)
    rgba[..., :3] = np.minimum(255, rgba[..., :3] * (1 - weight + weight * colour))
    return Image.fromarray((rgba + .5).astype(np.uint8), "RGBA")


def classic_dye(archive, record, icon_path):
    """The dye or metal a classic icon is drawn in, when it is a published variant of its paper-doll record."""
    pixels = np.asarray(Image.open(icon_path).convert("RGBA"))
    for variant in sorted(CLASSIC.glob(f"paper-doll-{archive}-{record}-*.png")):
        dye = variant.stem.removeprefix(f"paper-doll-{archive}-{record}-")
        other = np.asarray(Image.open(variant).convert("RGBA"))
        if dye != "mask" and other.shape == pixels.shape and (other == pixels).all():
            return dye
    return None


def inventory_icons(tints):
    """An entry for every published inventory icon whose record has an accepted final."""
    paths = {entry["mediaId"]: STAGED / entry["path"] for entry in json.load(open(STAGED / "worldrpg/media/classic-media-inventory.json"))["artifacts"]}
    notes = {area: ledger(area) for area in ("items", "worn")}
    entries, classic = [], []
    for published in json.load(open(CLASSIC_MANIFEST))["inventoryIcons"]:
        archive, record = published["textureArchive"], published["sourceRecordOrdinal"]
        area = "worn" if archive == 234 or 235 <= archive <= 252 or archive in (432, 433) else "items"
        final = accepted_final(area, notes[area], f"texture-{archive}-record-{record:03d}-frame-000")
        if final is None:
            classic.append(published["itemId"])
            continue
        image = icon(final, "items")
        if dye := classic_dye(archive, record, paths[published["mediaId"]]):
            image = tinted(image, tints[dye])
        entries.append(icon_entry(published["mediaId"], image, f"items/{published['itemId']}"))
    if classic:
        print(f"items: {len(classic)} icons stay classic, their records have no accepted final: {', '.join(classic)}")
    return entries


def spell_icons():
    notes = ledger("spells")
    return [icon_entry(f"spell.icon.{int(m[1])}", icon(final, "spells"), f"spells/{key}") for key in sorted(notes)
            if (m := SPELL_ICON.fullmatch(key)) and (final := accepted_final("spells", notes, key))]


def icon_entry(replaces, image, name):
    return {"replaces": replaces, "mediaId": f"refreshed.{replaces}", **webp(image, f"{MEDIA}/{name}.webp"),
            "width": image.width, "height": image.height}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--area", action="append", choices=AREAS, required=True)
    a = ap.parse_args()
    payload = json.loads(PAYLOAD.read_text()) if PAYLOAD.exists() else {"artReplacements": {"images": []}}
    images = payload["artReplacements"]["images"]
    for area in a.area:
        notes = ledger(area)
        accepted = sorted(key for key, note in notes.items() if note.get("status") == "accepted")
        directory = CONTENT / MEDIA / area
        directory.mkdir(parents=True, exist_ok=True)
        for old in directory.glob("*.webp"):
            old.unlink()
        images[:] = [entry for entry in images if not entry["path"].startswith(f"{MEDIA}/{area}/")]
        if area in ("items", "spells"):
            promoted = inventory_icons(payload["artReplacements"].get("dyeTints", {})) if area == "items" else spell_icons()
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
