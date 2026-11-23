#!/usr/bin/env python3
"""Inspect/export this VIGGCHAR v1 fixture using only Python's standard library.

Layout/formulas: @viggle/splat-engine 0.3.2's MIT-licensed
src/loader/character/v1/decode.ts (embedded in binary-gsplat-worker.js.map).
This is a static Gaussian export, not mesh reconstruction or animation baking.
"""

import argparse
import csv
import json
import math
from pathlib import Path
import struct


PROPERTIES = ["x", "y", "z", "nx", "ny", "nz", "f_dc_0", "f_dc_1", "f_dc_2",
              "opacity", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3"]


def read_bundle(path):
    data = path.read_bytes()
    magic = data[:8]
    version, total, json_length = struct.unpack_from("<III", data, 8)
    # Different layouts would turn offsets into plausible but wrong geometry.
    if magic != b"VIGGCHAR" or version != 1:
        raise ValueError("This reader only exports VIGGCHAR v1; another layout needs its own decoder.")
    directory = json.loads(data[20:20 + json_length])
    return data, total, directory, 20 + json_length


def export_bundle(data, directory, start, destination):
    destination.mkdir(parents=True, exist_ok=True)
    n = directory["numSplats"]
    blocks = directory["blocks"]

    def plane(name, component):
        block = blocks[name]
        dtype, size = {"u8": ("B", 1), "u16": ("H", 2), "f32": ("f", 4)}[block["dtype"]]
        return struct.unpack_from(f"<{block['count']}{dtype}", data,
                                  start + block["offset"] + component * block["count"] * size)

    def dequantize(name, component):
        block = blocks[name]
        levels = 65535 if block["dtype"] == "u16" else 255
        lo = block["min"][component]
        step = (block["max"][component] - lo) / levels
        return [lo + value * step for value in plane(name, component)]

    columns = {"nx": [0.0] * n, "ny": [0.0] * n, "nz": [0.0] * n}
    for c, axis in enumerate("xyz"):
        columns[axis] = dequantize("position", c)
        columns[f"scale_{c}"] = dequantize("scale", c)
        columns[f"f_dc_{c}"] = [(value / 255 - 0.5) / 0.28209479177387814 for value in plane("color", c)]
        columns[f"rot_{c + 1}"] = [value / 255 * 2 - 1 for value in plane("rotation", c)]
    columns["rot_0"] = [math.sqrt(max(0, 1 - x*x - y*y - z*z))
                        for x, y, z in zip(columns["rot_1"], columns["rot_2"], columns["rot_3"])]
    columns["opacity"] = dequantize("opacity", 0)
    header = "ply\nformat binary_little_endian 1.0\n"
    header += f"comment Static rest splats decoded from VIGGCHAR v1\nelement vertex {n}\n"
    header += "".join(f"property float {name}\n" for name in PROPERTIES) + "end_header\n"
    with (destination / "rest.ply").open("wb") as output:
        output.write(header.encode())
        for row in zip(*(columns[name] for name in PROPERTIES)):
            output.write(struct.pack("<17f", *row))

    if "boneIndex" in blocks:
        palette = blocks["boneIndex"]["palette"]
        indices = [plane("boneIndex", c) for c in range(4)]
        weights = [plane("boneWeight", c) for c in range(3)]
        with (destination / "skinning.csv").open("w", newline="") as output:
            writer = csv.writer(output)
            writer.writerow(["splat", "bone0", "bone1", "bone2", "bone3", "weight0", "weight1", "weight2", "weight3"])
            for i in range(n):
                stored = [p[i] for p in weights]
                writer.writerow([i, *(palette[p[i]] for p in indices),
                                 *(value / 255 for value in stored), max(0, 255 - sum(stored)) / 255])
    if "joints" in blocks:
        joints = list(map(list, zip(*(plane("joints", c) for c in range(3)))))
        (destination / "joints.json").write_text(json.dumps({
            "description": "Raw per-character joint translations from the file. Names, parents and bind rotations come from the SDK skeleton tables.",
            "joints": joints,
        }, indent=2) + "\n")
    (destination / "directory.json").write_text(json.dumps(directory, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("file", nargs="?", type=Path, default=Path(__file__).with_name("Pixel Sorceress_high.vsplat"))
    parser.add_argument("--export-dir", type=Path, help="Write ordinary rest PLY, skinning CSV, joints and directory JSON")
    args = parser.parse_args()
    data, total, directory, start = read_bundle(args.file)
    print(json.dumps({
        "file": str(args.file), "bytes": len(data), "declaredBytes": total, "version": 1,
        "numSplats": directory["numSplats"], "payloadStart": start,
        "blocks": {name: {k: v for k, v in block.items() if k != "palette"} for name, block in directory["blocks"].items()},
        "bonePaletteEntries": len(directory["blocks"].get("boneIndex", {}).get("palette", [])),
        "embeddedAnimations": False, "embeddedBoneNamesOrParents": False,
    }, indent=2))
    if total != len(data):
        print(f"Warning: declared length {total} differs from file length {len(data)}; reading available blocks.")
    if args.export_dir:
        export_bundle(data, directory, start, args.export_dir)
        print(f"Exported static splats and rig attributes to {args.export_dir.resolve()}")


if __name__ == "__main__":
    main()
