#!/usr/bin/env python3
"""Build an area's conversion ledger and its static review page.

Joins what already exists on disk into one row per source image:

  - the importer export (`df-raw/<area>`): subject, size, and for items the templates that share a sprite;
  - the contract specs (`authoring/specs/<area>/`): contract text and uncertainty;
  - post-process metrics (`gen-raw/studies/<prefix>-<spec>-bp-*/metrics.json`): back-projection error;
  - the finals directory (`gen-raw/<area>`): whether a final exists and its file;
  - `authoring/ledger/<area>-notes.json`: the only hand-edited input (status overrides, notes).

Areas are `items` (world sprites), `paper-doll`, `spells` and `effects`. Writes `review/index.html`
(items) or `review/<area>/index.html` (cards, filters, detail), the matching `*-ledger.csv` and
`thumbs/`. The notes file is the thing to edit; the page and CSV are regenerated views.

    python3 authoring/tools/items/review_ledger.py [--area items]
"""
import argparse, csv, glob, json, os
from pathlib import Path
from PIL import Image

from art_key import TEXTURE_AREAS, art_key, source_dir

HERE = Path(__file__).resolve().parent
AUTHORING = HERE.parents[1]
# Per-spell copies of icons and effects duplicate icons/ and elements/. Casting hands become a hand mesh.
NOT_CONVERTED_DIRS = ("spells/", "source-atlases")
NOT_CONVERTED_ROLES = ("casting",)
STATUSES = ["unconverted", "converted", "needs-owner-review", "accepted", "redo", "skipped"]


def load_export(area, export):
    """key -> row for every source image the area's conversions can cover."""
    rows = {}
    for mf in sorted(glob.glob(f"{export}/**/manifest.json", recursive=True)):
        m = json.load(open(mf))
        if "images" not in m or "directory" not in m:
            continue
        d = os.path.dirname(mf)
        if area == "worn":
            images = [(e["file"], e) for e in m["images"] if e["role"].startswith("player") and e["file"].endswith("-source.png")]
        elif area == "items":
            images = [(e["file"], e) for e in m["images"] if e["file"].startswith("world/") and e["file"].count("/") == 1 and e["file"].endswith("-source.png")]
        else:
            if m["directory"].startswith(NOT_CONVERTED_DIRS):
                continue
            images = [(e["file"], e) for e in m["images"] if e["role"] not in NOT_CONVERTED_ROLES and e["colors"] in ([], ["source"])]
        for file, e in images:
            k = art_key(area, m["directory"], file)
            row = rows.setdefault(k, dict(key=k, source=os.path.relpath(f"{d}/{file}", export), users=[], categories=[],
                                          directory=m["directory"], record=e["record"], frame=e["frame"]))
            if (m.get("name") or m["directory"]) not in row["users"]:
                row["users"].append(m.get("name") or m["directory"])
            cat = m["directory"].split("/")[0]
            if cat not in row["categories"]:
                row["categories"].append(cat)
    return rows


def load_specs(area, studies):
    """key -> newest (spec name, item, bp dir) that covers it."""
    prefix = "items-icons" if area == "items" else area
    best = {}
    for spec in sorted(glob.glob(str(AUTHORING / f"specs/{area}/{prefix}-*.json"))):
        if spec.endswith("-retro.json"):
            continue
        name = Path(spec).stem[len(prefix) + 1:]
        bps = sorted(glob.glob(f"{studies}/{prefix}-{name}-bp-*"))
        if not bps:
            continue
        bp = bps[-1]
        mt = os.path.getmtime(bp + "/metrics.json") if os.path.exists(bp + "/metrics.json") else 0
        for it in json.load(open(spec))["items"]:
            k = art_key(area, it["directory"], it["image"])
            if k not in best or mt > best[k][3]:
                best[k] = (name, it, bp, mt)
    return best


def thumb(src, dst, size, nearest):
    if dst.exists() and dst.stat().st_mtime >= os.path.getmtime(src):
        return
    im = Image.open(src).convert("RGBA")
    if nearest:
        k = max(1, min(size // im.width, size // im.height))
        im = im.resize((im.width * k, im.height * k), Image.NEAREST)
    else:
        im = im.resize((size, size), Image.LANCZOS)
    im.save(dst, optimize=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--area", default="items", choices=["items", "worn", "paper-doll", "spells", "effects"])
    ap.add_argument("--studies", default=str(AUTHORING / "gen-raw/studies"))
    args = ap.parse_args()
    area = args.area
    export, finals = AUTHORING / "df-raw" / source_dir(area), AUTHORING / "gen-raw" / area
    review = AUTHORING / "review" if area == "items" else AUTHORING / "review" / area
    notes_path = AUTHORING / "ledger" / f"{area}-notes.json"
    (review / "thumbs").mkdir(parents=True, exist_ok=True)
    notes = json.load(open(notes_path)) if notes_path.exists() else {}
    rows = load_export(area, export)
    specs = load_specs(area, args.studies)
    out = []
    for k in sorted(rows):
        r = rows[k]
        final = finals / f"{k}.png"
        spec = specs.get(k)
        im = Image.open(export / r["source"])
        if area in TEXTURE_AREAS:
            a, rec, f = k.split("-")[1::2]
            r.update(group=f"{a}-{rec}", label=f"texture {int(a)}, record {int(rec)}, frame {int(f)}")
        else:
            r.update(group=r["directory"], label=f'{r["directory"]}, record {r["record"]}, frame {r["frame"]}')
        r.update(size=f"{im.width}x{im.height}",
                 has_final=final.exists(), spec=spec[0] if spec else "", spec_id=spec[1]["id"] if spec else "",
                 contract=spec[1]["content"] if spec else "", uncertainty=spec[1]["uncertainty"] if spec else "",
                 error=None)
        if spec:
            m = json.load(open(spec[2] + "/metrics.json")).get(spec[1]["id"], {}).get("bp-broad-half", {})
            if "broad" in m:
                r["error"] = round(m["broad"] * 100, 1)
        r["guess"] = "guess for owner review" in r["uncertainty"]
        n = notes.get(k, {})
        r["status"] = n.get("status") or ("unconverted" if not r["has_final"] else "needs-owner-review" if r["guess"] else "converted")
        r["note"] = n.get("note", "")
        r["final"] = os.path.relpath(final, review) if r["has_final"] else ""
        r["name"] = r["users"][0] if len(r["users"]) == 1 else f'{r["users"][0]} +{len(r["users"]) - 1}'
        thumb(export / r["source"], review / "thumbs" / f"{k}-src.png", 384, True)
        if r["has_final"]:
            thumb(final, review / "thumbs" / f"{k}.png", 384, False)
        r["thumb_src"] = f"thumbs/{k}-src.png"
        r["thumb"] = f"thumbs/{k}.png" if r["has_final"] else ""
        r["source"] = os.path.relpath(export / r["source"], review)
        out.append(r)

    cols = ["key", "name", "categories", "users", "size", "status", "guess", "error", "spec", "spec_id", "note", "uncertainty"]
    with open(review / f"{area}-ledger.csv", "w", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(cols)
        for r in out:
            w.writerow([("; ".join(r[c]) if isinstance(r[c], list) else r[c]) for c in cols])
    data = dict(rows=out, statuses=STATUSES, notes=f"ledger/{area}-notes.json", area=area)
    html = (HERE / "review_page.html").read_text()
    (review / "index.html").write_text(html.replace("__DATA__", json.dumps(data).replace("</", "<\\/")))
    counts = {s: sum(1 for r in out if r["status"] == s) for s in STATUSES}
    print(f"{area}: {len(out)} source images", counts)


if __name__ == "__main__":
    main()
