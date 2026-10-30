#!/usr/bin/env python3
"""Build the item-art conversion ledger and its static review page.

Joins what already exists on disk into one row per world sprite frame
(archive, record, frame):

  - the item export (`<export>/*/*/manifest.json`, `world/*-source.png`): categories, sharing templates, size;
  - the contract specs (`authoring/specs/items/items-icons-*.json`): contract text and uncertainty;
  - post-process metrics (`gen-raw/studies/items-icons-<spec>-bp-*/metrics.json`): back-projection error;
  - the finals directory (`gen-raw/items`): whether a final exists and its file;
  - `authoring/ledger/items-notes.json`: the only hand-edited input (status overrides, notes).

Writes `review/index.html` (cards, filters, detail), `review/items-ledger.csv`
(spreadsheet view) and `review/thumbs/`. The notes file is the thing to edit;
the page and CSV are regenerated views. All paths default to the authoring root:

    python3 authoring/tools/items/review_ledger.py
"""
import argparse, csv, glob, json, os, re
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
AUTHORING = HERE.parents[1]
KEY = re.compile(r"texture-(\d+)-record-(\d+)-frame-(\d+)")
STATUSES = ["unconverted", "converted", "needs-owner-review", "accepted", "redo", "skipped"]


def key_of(text):
    a, r, f = KEY.search(text).groups()
    return f"{a}-{r}-{f}"


def load_export(export):
    rows = {}
    for mf in sorted(glob.glob(f"{export}/*/*/manifest.json")):
        m = json.load(open(mf))
        d = os.path.dirname(mf)
        for src in sorted(glob.glob(f"{d}/world/*-source.png")):
            k = key_of(src)
            row = rows.setdefault(k, dict(key=k, source=os.path.relpath(src, export), users=[], categories=[]))
            row["users"].append(m["name"])
            cat = m["directory"].split("/")[0]
            if cat not in row["categories"]:
                row["categories"].append(cat)
    return rows


def load_specs(studies):
    """key -> newest (spec name, item, bp dir) that covers it."""
    best = {}
    for spec in sorted(glob.glob(str(AUTHORING / "specs/items/items-icons-*.json"))):
        if spec.endswith("-retro.json"):
            continue
        name = Path(spec).stem[len("items-icons-"):]
        bps = glob.glob(f"{studies}/items-icons-{name}-bp-*")
        if not bps:
            continue
        bp = bps[0]
        mt = os.path.getmtime(bp + "/metrics.json") if os.path.exists(bp + "/metrics.json") else 0
        for it in json.load(open(spec))["items"]:
            k = key_of(it["image"])
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
    ap.add_argument("--export", default=str(AUTHORING / "df-raw/items"))
    ap.add_argument("--studies", default=str(AUTHORING / "gen-raw/studies"))
    ap.add_argument("--finals", default=str(AUTHORING / "gen-raw/items"))
    ap.add_argument("--review", default=str(AUTHORING / "review"))
    ap.add_argument("--notes", default=str(AUTHORING / "ledger/items-notes.json"))
    args = ap.parse_args()
    review = Path(args.review)
    (review / "thumbs").mkdir(parents=True, exist_ok=True)
    notes = json.load(open(args.notes)) if os.path.exists(args.notes) else {}
    rows = load_export(args.export)
    specs = load_specs(args.studies)
    out = []
    for k in sorted(rows):
        r = rows[k]
        a, rec, f = k.split("-")
        final = Path(args.finals) / f"texture-{a}-record-{rec}-frame-{f}.png"
        spec = specs.get(k)
        im = Image.open(Path(args.export) / r["source"])
        r.update(archive=int(a), record=int(rec), frame=int(f), size=f"{im.width}x{im.height}",
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
        thumb(Path(args.export) / r["source"], review / "thumbs" / f"{k}-src.png", 384, True)
        if r["has_final"]:
            thumb(final, review / "thumbs" / f"{k}.png", 384, False)
        r["thumb_src"] = f"thumbs/{k}-src.png"
        r["thumb"] = f"thumbs/{k}.png" if r["has_final"] else ""
        r["source"] = os.path.relpath(Path(args.export) / r["source"], review)
        out.append(r)

    cols = ["key", "name", "categories", "users", "size", "status", "guess", "error", "spec", "spec_id", "note", "uncertainty"]
    with open(review / "items-ledger.csv", "w", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(cols)
        for r in out:
            w.writerow([("; ".join(r[c]) if isinstance(r[c], list) else r[c]) for c in cols])
    html = (HERE / "review_page.html").read_text()
    (review / "index.html").write_text(html.replace("__DATA__", json.dumps(dict(rows=out, statuses=STATUSES)).replace("</", "<\\/")))
    counts = {s: sum(1 for r in out if r["status"] == s) for s in STATUSES}
    print(f"{len(out)} sprite frames", counts)


if __name__ == "__main__":
    main()
