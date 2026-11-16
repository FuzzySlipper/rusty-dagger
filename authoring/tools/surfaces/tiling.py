#!/usr/bin/env python3
"""Tiling check for the refreshed surfaces (rusty-dagger #10860): whether each surface the art pack ships is seamless on
every axis the game repeats it.

Where the game repeats a record comes from the published ARCH3D meshes (local/build/import/staging, the importer's
static meshes, whose texture coordinates are in record units), not from the classic: a plane, the triangles of one draw
group joined by shared vertices, repeats a record on an axis where its coordinates run past a whole record by more than
a classic texel, and the game draws it with nearest sampling and repeat wrap, so the record's last column meets its
first there. A mesh names its record in the base set; a placement draws it from the archives the published climate
swaps (exteriors and interiors) or the dungeon's texture table (dungeon blocks) give, so the axes reach those archives too.

On each repeated axis the shipped PNG's wrap edge (last line beside first) is scored with reduce.seam: step (a pixel
seam) and band (a shape or shade cut off at the edge), each how many times the texture's ordinary change the edge makes.
An axis fails at reduce.SEAM or more where the classic record scores under it: a classic that breaks at its edge (a
storey, a frame, a beam) was drawn to, and the refresh keeps its break, sharper. reduce.wraps, by which batch.py makes a
record seamless on an axis, is the same judgement of the classic. A record looked at and found to hold where it scores
as a seam carries a "seam" note in corrections.json, its axis first ("y: why"), and passes on that axis.

    tiling.py                      every shipped surface; report and repeat renders under local/art/review/surfaces/tiling
    tiling.py texture-312-1 ...    only these records
    tiling.py --pack DIR           score this folder's finals (local/art/generated/surfaces) for the records it holds

Writes report.md (failing records first, worst first, with axis, scores, the classic's, and the models and blocks that
place the record most) and, for each failing record, KEY.png: its classic and refreshed repeated 3x3 with nearest sampling.
Exits 1 when a record fails.
"""
import argparse, collections, csv, json, re, struct, sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import reduce  # noqa: E402

REPOSITORY = Path(__file__).resolve().parents[3]
STAGING = REPOSITORY / "local/build/import/staging/worldrpg"
GEOMETRY = STAGING / "imports/shared/geometry"
BLOCKS = STAGING / "imports/world-blocks"
IMPORTED = STAGING / "payloads/daggerfall.imported.json"
PACK = REPOSITORY / "content/worldrpg/packs/daggerfall.art-replacements"
CONTENT = REPOSITORY / "content"
REFERENCE = REPOSITORY / "local/art/reference/surfaces"
# A record whose edge scores as a seam but was looked at and holds (corrections.json "seam": its axis and why).
REVIEWED = {key: note["seam"] for key, note in json.loads((Path(__file__).resolve().parent / "corrections.json").read_text()).items() if "seam" in note}
OUT = REPOSITORY / "local/art/review/surfaces/tiling"
MATERIAL = re.compile(r"material/texture-(\d+)-(\d+)")
KEY = re.compile(r"texture-(\d+)-(\d+)")
SLIVER = 1.0  # classic texels a plane must run past a record edge for the edge to show


def meshes():
    """{mesh artifact id: [(archive, record, u span, v span)]}: for each plane of each published mesh, the record it draws
    and the range its texture coordinates cover."""
    planes = {}
    for path in GEOMETRY.glob("*.rstatmsh"):
        data = path.read_bytes()
        length = struct.unpack_from("<I", data, 8)[0]
        descriptor = json.loads(data[12:12 + length])
        resource = data[12 + length:]
        layout, source = descriptor["payload"]["layout"], descriptor["payload"]["source"]
        if "uvsByteOffset" not in source:
            continue
        uv = np.frombuffer(resource, np.float32, layout["vertexCount"] * 2, source["uvsByteOffset"]).reshape(-1, 2)
        indices = np.frombuffer(resource, np.uint32, layout["indexCount"], source["indicesByteOffset"])
        slots = {slot["slot"]: MATERIAL.fullmatch(slot["material"]) for slot in descriptor["materialSlots"]}
        found = []
        for group in descriptor["payload"]["groups"]:
            texture = slots.get(group["materialSlot"])
            if texture is None:
                continue
            triangles = indices[group["start"]:group["start"] + group["count"]].reshape(-1, 3)
            for vertices in connected(triangles):
                low, high = uv[vertices].min(0), uv[vertices].max(0)
                found.append((int(texture[1]), int(texture[2]), (float(low[0]), float(high[0])), (float(low[1]), float(high[1]))))
        planes[f"geometry/{path.stem}"] = found
    return planes


def connected(triangles):
    """The vertex sets of the triangles joined by shared vertices."""
    parent = {}

    def root(v):
        while parent.setdefault(v, v) != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v

    for a, b, c in triangles.tolist():
        parent[root(b)] = root(a)
        parent[root(c)] = root(a)
    sets = collections.defaultdict(list)
    for v in parent:
        sets[root(v)].append(v)
    return [np.array(v) for v in sets.values()]


def crosses(span, texels):
    """Whether a coordinate range runs past a whole record by more than SLIVER texels: a record edge lies inside it."""
    margin = SLIVER / texels
    low, high = span
    return np.floor(high - margin) >= np.ceil(low + margin)


def reach():
    """(climate swaps {(archive, record): {archive}}, dungeon remaps {archive: {archive}}), as the import publishes them."""
    imported = json.loads(IMPORTED.read_text())
    swaps = collections.defaultdict(set)
    for swap in imported["climate"]["swaps"]:
        last = swap["lastRecord"] if swap["lastRecord"] is not None else swap["firstRecord"] + 255
        for record in range(swap["firstRecord"], last + 1):
            swaps[(swap["archive"], record)].add(swap["targetArchive"])
    remaps = collections.defaultdict(set)
    for dungeon in imported["locations"]["dungeons"]:
        for remap in (dungeon.get("textureTable") or {}).get("remaps", []):
            remaps[remap["archive"]].add(remap["targetArchive"])
    return swaps, remaps


def placements():
    """[(block key, kind, mesh artifact id)] for every placed model."""
    placed = []
    for block in json.loads((BLOCKS / "blocks.json").read_text())["blocks"]:
        document = json.loads((BLOCKS / block["document"]).read_text())
        for model in document.get("models", []):
            if model.get("meshArtifactId"):
                placed.append((block["key"], block["kind"], model["meshArtifactId"]))
    return placed


def usage(sizes):
    """{(archive, record): {"x"|"y": planes repeating it, "models": Counter, "blocks": Counter}} for the records in
    `sizes` ({(archive, record): classic (width, height)}), as the placed models draw them."""
    planes = meshes()
    swaps, remaps = reach()
    found = collections.defaultdict(lambda: {"x": 0, "y": 0, "models": collections.Counter(), "blocks": collections.Counter()})
    for block, kind, mesh in placements():
        for archive, record, u, v in planes.get(mesh, []):
            archives = {archive} | (remaps.get(archive, set()) if kind == "rdb" else swaps.get((archive, record), set()))
            for drawn in archives:
                if (drawn, record) not in sizes:
                    continue
                width, height = sizes[(drawn, record)]
                entry = found[(drawn, record)]
                x, y = crosses(u, width), crosses(v, height)
                entry["x"] += x
                entry["y"] += y
                if x or y:
                    entry["models"][mesh.removeprefix("geometry/mesh-")] += 1
                    entry["blocks"][block] += 1
    return found


def fails(refreshed, classic):
    """Whether a refreshed edge is a seam where the classic's is not (a classic that breaks at its edge, a storey, a frame
    or a beam, was drawn to, and the refresh draws the break sharper)."""
    return max(refreshed) >= reduce.SEAM and (classic is None or max(classic) < reduce.SEAM)


def classic(archive, record):
    path = next(REFERENCE.glob(f"*/texture-{archive:03d}/surface/texture-{archive:03d}-record-{record:03d}-frame-000-source.png"), None)
    return Image.open(path).convert("RGB") if path else None


def classic_seam(archive, record, axis):
    """The classic's own score on an axis. A winter record that is its summer under snow (reduce.snowed, as batch.py
    makes it) keeps the summer's edge, so it is judged by the higher of the two classics' scores."""
    own = classic(archive, record)
    if own is None:
        return None
    score = reduce.seam(own, axis)
    winter = next(REFERENCE.glob(f"*-snow/texture-{archive:03d}"), None)
    summer = classic(archive - 1, record) if winter else None
    if summer is not None and reduce.snowed(summer, own):
        score = tuple(max(pair) for pair in zip(score, reduce.seam(summer, axis)))
    return score


def repeated(image, x, y, size):
    """The image repeated 3 times on each repeating axis, each texel a square block (nearest sampling), `size` wide."""
    a = np.tile(np.asarray(image.convert("RGB")), (3 if y else 1, 3 if x else 1, 1))
    scale = size / a.shape[1]
    rows = (np.arange(round(a.shape[0] * scale)) / scale).astype(int)
    columns = (np.arange(size) / scale).astype(int)
    return Image.fromarray(a[rows][:, columns])


def render(key, old, new, x, y):
    """Classic and refreshed side by side, each repeated 3x3 on its repeating axes at one size."""
    right = repeated(new, x, y, 576)
    left = repeated(old, x, y, 576) if old else None
    sheet = Image.new("RGB", ((right.width + 8) * 2, right.height + 24), "black")
    if left:
        sheet.paste(left, (0, 24))
    sheet.paste(right, (right.width + 8, 24))
    ImageDraw.Draw(sheet).text((4, 4), f"{key}  classic | refreshed", fill="white")
    sheet.save(OUT / f"{key}.png")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("keys", nargs="*")
    ap.add_argument("--pack", type=Path, help="score the finals SET/texture-AAA-R.png in this folder instead of the shipped PNGs")
    a = ap.parse_args()
    entries = json.loads((PACK / "payload.json").read_text())["artReplacements"]["surfaces"]
    shipped = {(e["replaces"]["archive"], e["replaces"]["record"]): CONTENT / e["path"] for e in entries}
    if a.keys:
        wanted = {tuple(map(int, KEY.fullmatch(k).groups())) for k in a.keys}
        shipped = {k: v for k, v in shipped.items() if k in wanted}
    if a.pack:
        finals = {tuple(map(int, KEY.fullmatch(p.stem).groups())): p for p in a.pack.glob("*/texture-*.png") if p.parent.name != "raw"}
        shipped = {k: p for k, p in finals.items() if k in shipped}
    images = {k: Image.open(p).convert("RGB") for k, p in shipped.items() if p.exists()}
    classics = {k: classic(*k) for k in images}
    # A crossing is judged in the classic's texels, which the refreshed record keeps at reduce.SCALE times.
    sizes = {k: (c.size if c else (images[k].width // reduce.SCALE, images[k].height // reduce.SCALE)) for k, c in classics.items()}
    found = usage(sizes)
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []
    for k, image in sorted(images.items()):
        laid = found.get(k)
        if not laid or not (laid["x"] or laid["y"]):
            continue
        key = f"texture-{k[0]:03d}-{k[1]}"
        row = {"key": key, "x": laid["x"], "y": laid["y"], "models": laid["models"], "blocks": laid["blocks"]}
        for axis, name in ((1, "x"), (0, "y")):
            if laid[name]:
                row[name + "_score"] = reduce.seam(image, axis)
                row[name + "_classic"] = classic_seam(*k, axis)
        row["worst"] = max(max(row.get(n + "_score", (0, 0))) for n in "xy")
        row["fails"] = [n for n in "xy" if laid[n] and fails(row[n + "_score"], row[n + "_classic"])
                        and not REVIEWED.get(key, "").startswith(n + ":")]
        rows.append(row)
    failing = [r for r in rows if r["fails"]]
    failing.sort(key=lambda r: -r["worst"])
    for old in OUT.glob("texture-*.png"):
        old.unlink()
    for r in failing:
        k = tuple(map(int, KEY.fullmatch(r["key"]).groups()))
        render(r["key"], classics[k], images[k], r["x"] > 0, r["y"] > 0)

    def fmt(s):
        return "-" if s is None else f"{s[0]:.1f}/{s[1]:.1f}"

    lines = [f"# Surface tiling check", "",
             f"{len(images)} shipped surfaces, {len(rows)} repeated by a placed model, {len(failing)} fail.",
             f"Scores are step/band: the wrap edge's change over the texture's ordinary change across that axis (its {reduce.ORDINARY}th "
             f"percentile). An axis fails at {reduce.SEAM} or more where the classic's is under {reduce.SEAM}.", "",
             "| record | fails | x planes | x refreshed | x classic | y planes | y refreshed | y classic | placed most by models | blocks |",
             "|---|---|---|---|---|---|---|---|---|---|"]
    for r in failing + sorted((r for r in rows if r not in failing), key=lambda r: -r["worst"]):
        models = ", ".join(f"{m} ({n})" for m, n in r["models"].most_common(3))
        blocks = ", ".join(f"{b} ({n})" for b, n in r["blocks"].most_common(3))
        lines.append(f"| {r['key']} | {' '.join(r['fails'])} | {r['x']} | {fmt(r.get('x_score'))} | {fmt(r.get('x_classic'))} "
                     f"| {r['y']} | {fmt(r.get('y_score'))} | {fmt(r.get('y_classic'))} | {models} | {blocks} |")
    (OUT / "report.md").write_text("\n".join(lines) + "\n")
    with open(OUT / "report.csv", "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["key", "fails", "x_planes", "x_step", "x_band", "x_classic_step", "x_classic_band",
                    "y_planes", "y_step", "y_band", "y_classic_step", "y_classic_band", "models", "blocks"])
        for r in rows:
            cell = lambda name, i: f"{r[name][i]:.2f}" if r.get(name) else ""
            w.writerow([r["key"], "".join(r["fails"]), r["x"], cell("x_score", 0), cell("x_score", 1), cell("x_classic", 0), cell("x_classic", 1),
                        r["y"], cell("y_score", 0), cell("y_score", 1), cell("y_classic", 0), cell("y_classic", 1),
                        " ".join(f"{m}:{n}" for m, n in r["models"].most_common(5)), " ".join(f"{b}:{n}" for b, n in r["blocks"].most_common(5))])
    print(f"{len(images)} surfaces, {len(rows)} repeated, {len(failing)} fail; {OUT / 'report.md'}")
    for r in failing:
        print(f"  {r['key']}: x {fmt(r.get('x_score'))} (classic {fmt(r.get('x_classic'))}), y {fmt(r.get('y_score'))} (classic {fmt(r.get('y_classic'))})")
    return 1 if failing else 0


if __name__ == "__main__":
    sys.exit(main())
