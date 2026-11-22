#!/usr/bin/env python3
"""Make refreshed tree meshes and record them in the art set (`artReplacements.trees`).

Nature meshes replace the nearby flora sprites. Author their wood, foliage and stone
in the regional recipes below, from the paintings in the art pack.

Archive 500's palms have their own connected stems, root flares and folded fronds in rainforest_palms.py.
Its other bespoke trees are shaped in rainforest_trees.py. Archive 501 shares matching paintings' meshes
at its own placement. Its distinct shapes are in subtropical_trees.py. Archive 502's rooted broadleaves
and snags are in swamp_trees.py, the desert trees in desert_trees.py and the summer woodland trees in woodland_trees.py.
Mountain conifers and their summer broadleaf variants are in mountain_trees.py, with the snowy mountain recipes in winter_mountain.py.
The haunted broadleaf and its leafless winter counterpart are in haunted_trees.py and winter_haunted.py.
Rebuild a record with trees.py --key 500-12 (repeat --key for a set).

The nature rocks in fieldstones.py are clusters of solid stones, with moss, snow and colour baked into their atlas.
The bushes use bushes.py, and the standing and fallen deadwood use deadwood.py.

The game draws a mesh at the sprite's drawn height (DaggerfallTreeMesh), so each entry's `placement` states that height in
metres, with how high its foot stands: the shares of the sprite's frame measured here, at its flat's own size
(`placement.size.height` of the flat in the payload), so a flat is promoted before its tree. A rebuilt tree keeps the
placement the payload holds, whatever set it (promote.py --set marks one `hand`); `--recompute KEY` (or `all`) measures it again.
It takes each mesh's normals as written, so a mesh with one not of unit length or not finite is refused here by name (the Engine refuses a non-finite one, faulting the update that grows it; #10871).

    trees.py                  every tree, the whole section and its media written anew
    trees.py --key A-R ...    those entries alone, every other entry and file left as it is
    trees.py --recompute A-R  also measure that tree's height and foot again (`all` for every tree built)
"""
import argparse, hashlib, json, shutil, tempfile
from pathlib import Path

import sys

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "rocks"))
import bushes
import deadwood
import fieldstones
import rainforest_palms
import rainforest_trees
import subtropical_trees
import swamp_trees
import desert_trees
import woodland_trees
import winter_woodland
import mountain_trees
import winter_mountain
import haunted_trees
import winter_haunted
import mountain_woods
import winter_mountain_woods

REPOSITORY = Path(__file__).resolve().parents[3]
CONTENT = REPOSITORY / "content"
MEDIA = "worldrpg/packs/daggerfall.art-replacements/media/trees"
PAYLOAD = CONTENT / "worldrpg/packs/daggerfall.art-replacements/payload.json"

# Each authored nature form selects its regional recipe or shared model.
REGIONAL_TREES = {500: ("rainforest", rainforest_trees), 501: ("subtropical", subtropical_trees),
                  502: ("swamp", swamp_trees), 503: ("desert", desert_trees),
                  504: ("woodland", woodland_trees), 505: ("winter-woodland", winter_woodland),
                  506: ("mountain", mountain_trees), 507: ("winter-mountain", winter_mountain),
                  508: ("haunted", haunted_trees), 509: ("winter-haunted", winter_haunted),
                  510: ("mountain-woods", mountain_woods), 511: ("winter-mountain-woods", winter_mountain_woods)}
FORMS = {
    500: {**{r: (form, r) for r, form in rainforest_trees.FORMS.items()},
          12: ("palm", 1), 14: ("palm", 2), 15: ("palm", 3)},
    501: {r: (form, r) for r, form in subtropical_trees.FORMS.items()},
    502: {r: (form, r) for r, form in swamp_trees.FORMS.items()},
    503: {r: (form, r) for r, form in desert_trees.FORMS.items()},
    504: {r: (form, r) for r, form in woodland_trees.FORMS.items()},
    505: {r: (form, r) for r, form in winter_woodland.FORMS.items()},
    506: {r: (form, r) for r, form in mountain_trees.FORMS.items()},
    507: {r: (form, r) for r, form in winter_mountain.FORMS.items()},
    508: {r: (form, r) for r, form in haunted_trees.FORMS.items()},
    509: {r: (form, r) for r, form in winter_haunted.FORMS.items()},
    510: {r: (form, r) for r, form in mountain_woods.FORMS.items()},
    511: {r: (form, r) for r, form in winter_mountain_woods.FORMS.items()},
}
# These flats share paintings with other nature records. Reuse their authored media
# while placing every record at its own size, including when a source is rebuilt by key.
SHARED_TREES = {"501-1": "500-21", "501-12": "500-14", "501-13": "500-15",
                "501-15": "500-13", "501-17": "500-3", "501-24": "501-19", "501-31": "500-21",
                "502-12": "500-18", "502-18": "500-30", "502-19": "501-19", "506-10": "501-20",
                **{f"507-{r}": f"505-{r}" for r in (10, 13, 14, 15, 16, 25)},
                "508-10": "501-20", "508-15": "506-15", "509-10": "505-10", "509-15": "505-15",
                "510-10": "501-20", **{f"510-{r}": f"506-{r}" for r in (5, 11, 12, 13, 15, 25, 30)},
                **{f"511-{r}": f"505-{r}" for r in (10, 13, 15, 25)},
                **{f"511-{r}": f"507-{r}" for r in (5, 11, 23)}}
SHARED_TREES.update(fieldstones.SHARED)
SHARED_TREES.update(bushes.SHARED)
SHARED_TREES.update(deadwood.SHARED)
for key, form in deadwood.FORMS.items():
    archive, record = map(int, key.split("-"))
    FORMS[archive][record] = ("deadwood-" + form, record)
for key in fieldstones.PAINTINGS:
    archive, record = map(int, key.split("-"))
    FORMS[archive][record] = ("rock", record)
for key, form in bushes.FORMS.items():
    archive, record = map(int, key.split("-"))
    FORMS[archive][record] = (form, record)


def frame(flat):
    image = Image.open(CONTENT / flat["path"]).convert("RGBA")
    f = flat["frames"][0]
    return np.asarray(image.crop((f["x"], f["y"], f["x"] + f["width"], f["y"] + f["height"])), np.float64)


def measures(rgba):
    """The height and bottom row of the solid painting inside its frame."""
    rows = np.flatnonzero((rgba[..., 3] > 128).any(1))
    foot = rows[-1]
    return {"height": foot - rows[0] + 1, "foot": foot}


def write_mesh(path, mesh):
    """Writes a prop mesh, refusing one with a normal not of unit length or not finite, by name: the game draws its normals
    as written."""
    for part in mesh["parts"]:
        normals = np.array(part["normals"], np.float64).reshape(-1, 3)
        wrong = ~np.isfinite(normals).all(1) | (np.abs(np.linalg.norm(normals, axis=1) - 1) > .01)
        if wrong.any():
            raise SystemExit(f"{path.name}: {int(wrong.sum())} of its {part['role']} normals are not of unit length or not finite")
    path.write_text(json.dumps(mesh, separators=(",", ":")))


def publish(name, path):
    data = path.read_bytes()
    return {"path": f"{MEDIA}/{name}", "byteLength": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def placement(flat, rgba, m, old, recompute=False):
    """A tree's own placement: the sprite's drawn height and where its foot stands, as shares of its frame height, in metres
    at its flat's own size. An entry the payload already places keeps every value it has, whatever its provenance, and takes
    only a missing one from this measure, so a rebuilt tree, a redone flat or a changed sprite does not resize or lift it;
    `recompute` (--recompute) measures them all again, each marked `art`."""
    h, metres = rgba.shape[0], flat["placement"]["size"]["height"]
    measured = {"height": round(m["height"] / h * metres, 6), "foot": round((h - 1 - m["foot"]) / h * metres, 6)}
    kept = {} if recompute else (old or {}).get("placement", {})
    given = {name: kept[name] for name in measured if name in kept}
    marks = kept.get("from", {})
    return {**{name: given.get(name, value) for name, value in measured.items()},
            "from": {name: marks.get(name, "art") if name in given else "art" for name in measured}}


def shared_tree(source, flat, old=None, recompute=False):
    """The same authored mesh and materials, standing at the receiving record's own height and foot."""
    rgba = frame(flat)
    return {**source, "placement": placement(flat, rgba, measures(rgba), old, recompute)}


def rock(flat, rgba, key, folder, old, recompute):
    placed = placement(flat, rgba, measures(rgba), old, recompute)
    mesh, atlas = fieldstones.build(key, rgba, placed["height"])
    # Summer and winter share the same sculpt. A changed sculpt gets its own
    # path so a scoped rebuild cannot replace geometry another skin still uses.
    shape = hashlib.sha256(json.dumps(mesh, separators=(",", ":")).encode()).hexdigest()[:16]
    mesh_name, stone_name = f"rock-{shape}.prop-mesh.json", f"rock-{key}-stone.png"
    write_mesh(folder / mesh_name, mesh)
    atlas.save(folder / stone_name)
    stone = publish(stone_name, folder / stone_name)
    entry = {"mesh": publish(mesh_name, folder / mesh_name) | {"height": mesh["height"]},
             "placement": placed, "stone": stone}
    if any(part["role"] == "bark" for part in mesh["parts"]):
        entry["bark"] = stone
    return entry


def tree(flat, archive, record, species, variant, folder, old=None, recompute=False):
    """Build and publish one nature mesh with its painted materials."""
    rgba = frame(flat)
    if species == "rock":
        return rock(flat, rgba, f"{archive}-{record}", folder, old, recompute)
    m = measures(rgba)
    climate, recipes = REGIONAL_TREES[archive]
    key = f"{archive}-{record}"
    if key in deadwood.FORMS:
        mesh, images = deadwood.build(key, rgba)
    elif key in bushes.FORMS:
        mesh, images = bushes.build(key, rgba)
    elif archive == 500 and record in rainforest_palms.PALMS:
        mesh, bark, leaves = rainforest_palms.build(record, rgba)
        images = {"bark": bark, "leaves": leaves}
    else:
        mesh, images = recipes.build(record, rgba)
    mesh_name = f"{species}-p{variant}-{climate}.prop-mesh.json"
    write_mesh(folder / mesh_name, mesh)
    entry = {"mesh": publish(mesh_name, folder / mesh_name) | {"height": mesh["height"]},
             "placement": placement(flat, rgba, m, old, recompute)}
    for role, image in images.items():
        filename = f"tree-{key}-{role}.png"
        image.save(folder / filename)
        entry[role] = publish(filename, folder / filename)
    return entry


def paths(entry):
    """The content paths an entry names."""
    return [value["path"] for value in entry.values() if isinstance(value, dict) and "path" in value]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--key", action="append", default=[], help="ARCHIVE-RECORD: rebuild this entry alone (repeatable)")
    ap.add_argument("--recompute", action="append", default=[], metavar="ARCHIVE-RECORD",
                    help="measure this tree's height and foot again (repeatable; `all` for every tree built); without it a rebuilt "
                    "tree keeps the placement the payload holds")
    args = ap.parse_args()
    keys, recompute = args.key, set(args.recompute)
    payload = json.loads(PAYLOAD.read_text())
    flats = {(f["replaces"]["archive"], f["replaces"]["record"]): f for f in payload["artReplacements"]["flats"]}
    folder = CONTENT / MEDIA
    wanted = {f"{archive}-{record}": (archive, record, form) for archive, forms in FORMS.items() for record, form in forms.items()}
    if unknown := [key for key in keys if key not in wanted and key not in SHARED_TREES]:
        raise SystemExit(f"no tree is made for {', '.join(unknown)}")
    # The shader that lays the winter trees' snow on their leaves, written by hand beside them.
    snow = (folder / "snow.wgsl").read_text()
    trees = dict(payload["artReplacements"]["trees"]) if keys else {}
    # Build separately so a failed recipe leaves the published trees intact.
    with tempfile.TemporaryDirectory() as built:
        built = Path(built)
        build_keys = [key for key in keys if key in wanted] if keys else list(wanted)
        for key in keys:
            source = SHARED_TREES.get(key)
            if source and source not in trees and source not in build_keys:
                build_keys.append(source)
        for key in build_keys:
            archive, record, (species, variant) = wanted[key]
            trees[key] = tree(flats[(archive, record)], archive, record, species, variant, built, payload["artReplacements"]["trees"].get(key), bool({"all", key} & recompute))
        shared = [key for key, source in SHARED_TREES.items() if not keys or key in keys or source in build_keys]
        for key in shared:
            archive, record = map(int, key.split("-"))
            flat = flats[archive, record]
            old = payload["artReplacements"]["trees"].get(key)
            trees[key] = shared_tree(trees[SHARED_TREES[key]], flat, old, bool({"all", key} & recompute))
        if keys:
            # A file another entry names too (a rock several climates share) changes under it unless that entry is rebuilt.
            written = {f"{MEDIA}/{file.name}" for file in built.iterdir()
                       if not (folder / file.name).exists() or file.read_bytes() != (folder / file.name).read_bytes()}
            if sharing := sorted(key for key, entry in trees.items() if key not in build_keys and key not in shared and written & set(paths(entry))):
                raise SystemExit(f"{', '.join(keys)} write files that {', '.join(sharing)} also name: give those keys too")
        else:
            (built / "snow.wgsl").write_text(snow)
            payload["artReplacements"]["treeSnow"] = publish("snow.wgsl", built / "snow.wgsl")
        named = {Path(path).name for entry in trees.values() for path in paths(entry)} | {"snow.wgsl"}
        for old in folder.glob("*"):
            if not keys or old.name not in named:
                old.unlink()
        for file in built.iterdir():
            shutil.copyfile(file, folder / file.name)
    payload["artReplacements"]["trees"] = trees
    PAYLOAD.write_text(json.dumps(payload, indent=1) + "\n")
    print(f"{len(build_keys) + len(shared)} trees, {len(list(folder.glob('*.prop-mesh.json')))} meshes")

if __name__ == "__main__":
    main()
