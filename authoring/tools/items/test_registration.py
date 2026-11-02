#!/usr/bin/env python3
"""A final lands in the doll where its generation put it, whatever the spec's canvas size and occupancy.

    python3 -m unittest test_registration     # from authoring/tools/items; needs df-raw/items exported

Prepares one held weapon (a worn final drawn alone, with no dressed sidecar) at a non-default canvas size and
occupancy, publishes a stand-in cut-out through finalize.py, and checks that doll_preview.py and promote.py place
it at the rect the generation's input transform gives.
"""
import json, subprocess, sys, tempfile, unittest
from pathlib import Path

from PIL import Image

import doll_preview
from doll_preview import DF_ITEMS, ORIGIN, placed, worn_layer
from promote import rect_of

HERE = Path(__file__).resolve().parent
KEY = "texture-234-record-013-frame-000"
SIZE, OCCUPANCY = 768, 0.6


class Registration(unittest.TestCase):
    def test_a_worn_final_without_a_dressed_sidecar_lands_where_its_generation_put_it(self):
        _, entry = doll_preview.worn_sources(None)[KEY]
        directory = next(p.parent.relative_to(DF_ITEMS) for p in DF_ITEMS.glob("*/*/manifest.json")
                         if any(e["file"] == entry["file"] for e in json.load(open(p))["images"]))
        with tempfile.TemporaryDirectory() as temp:
            temp = Path(temp)
            spec = {"seed": 1, "size": SIZE, "occupancy": OCCUPANCY, "background": "#30343b", "styles": {"painted": {"label": "p", "prompt": "p"}},
                    "collection_rules": "", "presentation": "", "title": "t", "task": "t",
                    "routes": [{"id": "qwen-base", "label": "q", "group": "worn", "model": "qwen-edit", "host": "den-m5",
                                "url": "http://127.0.0.1:1", "conditioning": "reference"}],
                    "items": [{"id": "weapon", "directory": str(directory), "image": entry["file"], "content": "c", "uncertainty": "u",
                               "invention": "i", "name": "weapon"}]}
            (temp / "spec.json").write_text(json.dumps(spec))
            subprocess.run([sys.executable, HERE / "run.py", "prepare", "--spec", temp / "spec.json", "--source", DF_ITEMS,
                            "--out", temp / "study"], check=True, capture_output=True)
            (temp / "cutout").mkdir()
            Image.new("RGBA", (SIZE, SIZE), (200, 200, 200, 255)).save(temp / "cutout" / "weapon.png")
            subprocess.run([sys.executable, HERE / "finalize.py", temp / "spec.json", temp / "cutout", "--area", "worn",
                            "--finals", temp / "finals", "--study", temp / "study"], check=True, capture_output=True)

            # The source, scaled by the largest whole number within the occupancy, is centred on the canvas.
            scale = int(SIZE * OCCUPANCY / max(entry["width"], entry["height"]))
            at = ((SIZE - entry["width"] * scale) // 2, (SIZE - entry["height"] * scale) // 2)
            x, y = entry["xOffset"] - ORIGIN[0], entry["yOffset"] - ORIGIN[1]
            expected = (x - at[0] / scale, y - at[1] / scale, SIZE / scale, SIZE / scale)

            doll_preview.FINALS["worn"].insert(0, temp / "finals")
            try:
                layer = worn_layer(KEY, None)
                self.assertEqual(temp / "finals" / f"{KEY}.png", layer.final)
                self.assertEqual(expected, placed(layer, "canvas")[1])
                self.assertEqual(expected, rect_of("worn", KEY, layer.final))
            finally:
                doll_preview.FINALS["worn"].pop(0)

    def test_a_final_without_a_sidecar_is_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            Image.new("RGBA", (8, 8)).save(Path(temp) / f"{KEY}.png")
            doll_preview.FINALS["worn"].insert(0, Path(temp))
            try:
                with self.assertRaises(SystemExit):
                    placed(worn_layer(KEY, None), "canvas")
            finally:
                doll_preview.FINALS["worn"].pop(0)


if __name__ == "__main__":
    unittest.main()
