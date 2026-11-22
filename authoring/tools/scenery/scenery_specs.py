#!/usr/bin/env python3
"""Write a scenery batch's specs for whole groups (rusty-dagger #10499): every still, placed or scattered record of each
group that has no final yet, its content said by Qwen3-VL, then make_spec.py with the scenery template. A record an item
already drew stays out: promote.py takes its accepted `items` final. Animated records and winter sets are left to their
own batches.

    scenery_specs.py --host den-nimo nature-desert prop-statue
A group is a nature set by its climate (`nature-CLIMATE`) or a category and kind of the scenery export (`prop-statue`,
`nature-crop`, `creature-animal`). Writes specs/scenery/scenery-GROUP(-retro).json, and keeps captions in local/art/generated/scenery/captions.json.
"""
import argparse, csv, json, os, subprocess, sys, tempfile
from pathlib import Path

from PIL import Image

AUTHORING = Path(__file__).resolve().parents[2]
ART = AUTHORING.parent / "local" / "art"
AP = Path(os.environ.get("ASSET_PIPELINE", Path.home() / "dev/asset-pipeline"))
HOSTS = {"den-m5": "http://192.168.1.24:8188", "den-nimo": "http://192.168.1.23:8188"}
RAW = ART / "reference/scenery"
CAPTIONS = ART / "generated/scenery/captions.json"
CAPTION = ("This is a sprite from an old fantasy game, on a plain grey background. In one sentence of at most 30 words, say what "
           "single object it shows: its shape, parts, materials and main colours. Start with 'One'. Do not mention the background, "
           "style, pixels or that it is a sprite. Reply with the sentence only.")


def group_of(row):
    """A scenery record's batch group: its nature set's climate, or its category and kind."""
    return f"nature-{row['climate']}" if row["climate"] else f"{row['category']}-{row['kind']}"


def caption(comfy, source, key, cache):
    if key in cache:
        return cache[key]
    image = Image.open(source).convert("RGBA")
    scale = max(1, 512 // max(image.size))
    shown = Image.new("RGBA", image.size, (48, 52, 59, 255))
    shown.alpha_composite(image)
    with tempfile.TemporaryDirectory() as work:
        path = Path(work) / f"{key}.png"
        shown.resize((image.width * scale, image.height * scale), Image.Resampling.NEAREST).convert("RGB").save(path)
        name = comfy.upload(str(path))["name"]
        g = {"clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen3vl_8b_int8_convrot.safetensors", "type": "qwen_image", "device": "default"}},
             "image": {"class_type": "LoadImage", "inputs": {"image": name}},
             "text": {"class_type": "TextGenerate", "inputs": {"clip": ["clip", 0], "prompt": CAPTION, "max_length": 90, "sampling_mode": "off", "image": ["image", 0]}},
             "show": {"class_type": "PreviewAny", "inputs": {"source": ["text", 0]}}}
        cache[key] = " ".join(comfy.run_text(g)).strip().replace("\n", " ")
    CAPTIONS.write_text(json.dumps(cache, indent=1) + "\n")
    return cache[key]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", choices=list(HOSTS), required=True)
    ap.add_argument("groups", nargs="+")
    a = ap.parse_args()
    os.environ["COMFY_URL"] = HOSTS[a.host]
    sys.path.insert(0, str(AP / "tools/lora-training"))
    import comfy  # noqa: E402
    cache = json.loads(CAPTIONS.read_text()) if CAPTIONS.exists() else {}
    rows = list(csv.DictReader(open(RAW / "usage.csv")))
    for group in a.groups:
        items = []
        for row in rows:
            archive, record = int(row["archive"]), int(row["record"])
            key = f"texture-{archive:03d}-record-{record:03d}-frame-000"
            if group_of(row) != group or int(row["frames"]) > 1:
                continue
            if (ART / "generated/scenery" / f"{key}.png").exists() or (ART / "generated/items" / f"{key}.png").exists():
                continue
            directory, image = row["directory"], f"sprite/{key}-source.png"
            items.append({"id": f"scenery-{archive}-{record}", "directory": directory, "image": image,
                          "content": caption(comfy, RAW / directory / image, key, cache), "uncertainty": "",
                          "invention": "Add nothing the source does not show."})
        if not items:
            print(group, "nothing to do")
            continue
        with tempfile.NamedTemporaryFile("w", suffix=".json") as listing:
            json.dump(items, listing)
            listing.flush()
            subprocess.run([sys.executable, AUTHORING / "tools/shared/make_spec.py", "scenery", group, listing.name, "--template", "scenery",
                            "--title", f"Scenery: {group}"], check=True)
        print(group, len(items), flush=True)


if __name__ == "__main__":
    main()
