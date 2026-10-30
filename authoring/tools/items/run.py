#!/usr/bin/env python3
"""Prepare, run and compare a small source-sprite reinterpretation study.

Uses the existing LoRA generator as a subprocess; product inputs/outputs remain
ordinary files in the requesting repository. A saved plan is immutable so a
rerun cannot silently relabel old images with new prompts. Use a new output
directory when changing a spec. Failed jobs warn and leave their log for retry.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

# The generic ComfyUI generator and LAN tooling stay in the asset-pipeline checkout.
PIPELINE = Path(os.environ.get("ASSET_PIPELINE", Path.home() / "dev/asset-pipeline"))
GENERATOR = PIPELINE / "tools/lora-training/generate.py"


HOSTS = {"den-m5": "http://192.168.1.24:8188", "den-nimo": "http://192.168.1.23:8188"}


def read(path):
    return json.loads(Path(path).read_text())


def write(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + "\n")


def prompt(spec, item, style, reference):
    sections = [
        spec.get("task", "Task: Redraw a very small classic fantasy RPG item as newly illustrated high resolution art. "
                 "Reconstruct plausible missing detail; do not enlarge or trace the pixel blocks."),
        ("Input image: content reference only. Preserve this object's identity, silhouette, orientation, "
         "relative dimensions and major colour regions. Replace the pixel rendering with the treatment below."
         if reference else "Content specification transcribed from the source sprite follows. Follow its proportions and colours."),
        "Subject and composition: " + item["content"],
        "Uncertain source detail: " + item["uncertainty"],
        "Permitted invention: " + item["invention"],
        "Style: " + style["prompt"],
        "Collection rules: " + spec["collection_rules"],
        "Presentation: " + spec["presentation"],
    ]
    return "\n\n".join(sections)


def export_prompts(output, plan=None):
    """Expose literal generator inputs as text, instead of burying them in JSON."""
    plan = plan or read(output / "plan.json")
    lines = ["# Exact image-generation prompts", "",
             "These are the literal positive prompts passed to the generator, including any LoRA trigger. "
             "Each job also has a plain `prompt.txt` for copying. Full text follows the index.", "",
             "| Item | Route / style | Seed | Words | Literal prompt | Image |",
             "| --- | --- | --- | --- | --- | --- |"]
    expanded = []
    for job in plan["jobs"]:
        argv = job["argv"]
        text = argv[argv.index("--prompt") + 1]
        folder = Path(job["output"]).parent
        (folder / "prompt.txt").write_text(text + "\n")
        rel = folder.relative_to(output).as_posix()
        words = len(text.split())
        lines.append(f"| {job['item']} | {job['route']} / {job['style']} | {job['seed']} | {words} | "
                     f"[prompt.txt]({rel}/prompt.txt) | [PNG]({rel}/{Path(job['output']).name}) |")
        expanded += ["", f"## {job['id']}", "", f"{words} words; {len(text)} characters.", "", "```text", text, "```"]
        if "--negative" in argv:
            negative = argv[argv.index("--negative") + 1]
            (folder / "negative.txt").write_text(negative + "\n")
            expanded += ["", "Negative prompt:", "", "```text", negative, "```"]
    (output / "PROMPTS.md").write_text("\n".join(lines + expanded) + "\n")
    print(f"Literal prompts: {output / 'PROMPTS.md'}", flush=True)


def prepare(spec_path, source, output, upstream=None, host=None):
    spec = read(spec_path)
    if host:
        for route in spec["routes"]:
            route["host"], route["url"] = host, HOSTS[host]
    source, output = Path(source).resolve(), Path(output).resolve()
    plan = {"spec": spec, "source_root": str(source), "jobs": []}
    upstream_plan = read(upstream / "plan.json") if upstream else None
    if upstream:
        plan["upstream_study"] = str(upstream.resolve())
    # Describe paths/settings before touching inputs, allowing safe repeat use.
    for route in spec["routes"]:
        styles = route.get("styles", ["painted"])
        for style_id in styles:
            for item in spec["items"]:
                for seed in route.get("seeds", [spec["seed"]]):
                    job_id = f'{route["id"]}--{style_id}--{item["id"]}--{seed}'
                    folder = output / "jobs" / job_id
                    content_conditioned = route["conditioning"] != "text"
                    expanded = route.get("prompt_prefix", "") + prompt(spec, item, spec["styles"][style_id], content_conditioned)
                    argv = [sys.executable, str(GENERATOR), str(folder), "--model", route["model"],
                            "--prompt", expanded,
                            "--size", str(spec["size"]), "--seeds", str(seed), "--tag", "image"]
                    for key in ("unet", "steps", "cfg", "sampler", "scheduler", "denoise", "control_strength"):
                        if key in route:
                            argv += ["--" + key.replace("_", "-"), str(route[key])]
                    for lora in route.get("loras", []):
                        argv += ["--lora", lora]
                    ref = output / "inputs" / item["id"]
                    if route["conditioning"] == "init":
                        init_image = ref / "reference.png"
                        if route.get("upstream_route"):
                            if upstream_plan is None:
                                raise SystemExit("Supply --upstream: otherwise the style pass would silently start from source pixels rather than the specified reconstructed image.")
                            previous = next(j for j in upstream_plan["jobs"] if
                                            (j["route"], j["style"], j["item"], j["seed"]) ==
                                            (route["upstream_route"], route.get("upstream_style", "painted"), item["id"], seed))
                            init_image = Path(previous["output"])
                            if not init_image.exists() or not (init_image.parent / "completed.json").exists():
                                raise SystemExit(f"Finish the upstream reconstruction first; using a missing or incomplete intermediate would produce a different two-pass recipe: {init_image}")
                            if Image.open(init_image).size != (spec["size"], spec["size"]):
                                # Image-to-image keeps the init's size, so a larger style pass needs an
                                # enlarged intermediate; it is written with the other inputs below.
                                suffix = f"-{seed}" if "seeds" in route else ""
                                init_image = ref / f"upstream-{route['upstream_route']}-{spec['size']}{suffix}.png"
                        argv += ["--init", str(init_image)]
                    elif route["conditioning"] == "reference":
                        argv += ["--reference", str(ref / "reference.png")]
                    elif route["conditioning"] == "outline":
                        argv += ["--control", str(ref / "outline.png")]
                    if route.get("negative"):
                        argv += ["--negative", route["negative"]]
                    plan["jobs"].append({"id": job_id, "route": route["id"], "style": style_id,
                                         "item": item["id"], "seed": seed, "host": route["host"],
                                         "url": route["url"], "conditioning": route["conditioning"],
                                         "argv": argv, "output": str(folder / f"image-{seed}.png")})
    plan_path = output / "plan.json"
    if plan_path.exists():
        if read(plan_path) != plan:
            raise SystemExit("Use a new output directory: changing this plan would relabel existing evidence with different prompts or inputs.")
        print(f"Existing unchanged plan: {len(plan['jobs'])} jobs", flush=True)
        export_prompts(output, plan)
        return
    output.mkdir(parents=True, exist_ok=True)
    for item in spec["items"]:
        folder = output / "inputs" / item["id"]
        folder.mkdir(parents=True, exist_ok=True)
        manifest_path = source / item["directory"] / "manifest.json"
        manifest = read(manifest_path)
        entry = next(e for e in manifest["images"] if e["file"] == item["image"])
        path = manifest_path.parent / entry["file"]
        shutil.copy2(path, folder / "source.png")
        shutil.copy2(manifest_path, folder / "manifest.json")
        im = Image.open(path).convert("RGBA")
        # Integer nearest-neighbour scaling supplies shape/colour evidence only;
        # this is an input canvas, never presented as a generated result.
        scale = max(1, int(spec["size"] * spec["occupancy"] / max(im.size)))
        enlarged = im.resize((im.width * scale, im.height * scale), Image.Resampling.NEAREST)
        xy = ((spec["size"] - enlarged.width) // 2, (spec["size"] - enlarged.height) // 2)
        if spec.get("anchor") == "bottom":
            # First-person frames are cut by the screen's lower edge; keep that cut on the canvas edge.
            xy = (xy[0], spec["size"] - enlarged.height)
        reference = Image.new("RGBA", (spec["size"], spec["size"]), spec["background"])
        reference.alpha_composite(enlarged, xy)
        reference.convert("RGB").save(folder / "reference.png")
        mask = Image.new("L", reference.size)
        mask.paste(enlarged.getchannel("A"), xy)
        edge = mask.filter(ImageFilter.FIND_EDGES)
        outline = Image.new("RGB", reference.size, "white")
        outline.paste("black", mask=edge)
        outline.save(folder / "outline.png")
        for job in plan["jobs"]:
            init = job["argv"][job["argv"].index("--init") + 1] if "--init" in job["argv"] else None
            if job["item"] == item["id"] and init and Path(init).parent == folder and not Path(init).exists():
                route = next(r for r in spec["routes"] if r["id"] == job["route"])
                previous = next(j for j in upstream_plan["jobs"] if
                                (j["route"], j["style"], j["item"], j["seed"]) ==
                                (route["upstream_route"], route.get("upstream_style", "painted"), item["id"], job["seed"]))
                Image.open(previous["output"]).convert("RGB").resize(
                    (spec["size"], spec["size"]), Image.Resampling.LANCZOS).save(init)
        write(folder / "input.json", {"source": str(path), "export_entry": entry,
                                       "native_size": list(im.size), "integer_scale": scale,
                                       "placement": xy, "background": spec["background"],
                                       "content": item["content"], "uncertainty": item["uncertainty"]})
    for job in plan["jobs"]:
        folder = Path(job["output"]).parent
        folder.mkdir(parents=True, exist_ok=True)
        write(folder / "request.json", job)
    write(output / "spec.json", spec)
    write(plan_path, plan)
    export_prompts(output, plan)
    print(f"Prepared {len(plan['jobs'])} jobs at {output}", flush=True)


def generate(output, host, routes):
    plan = read(output / "plan.json")
    failures = []
    for job in plan["jobs"]:
        if (host and job["host"] != host) or (routes and job["route"] not in routes):
            continue
        folder = Path(job["output"]).parent
        if (folder / "completed.json").exists() and Path(job["output"]).exists():
            print("Already completed: " + job["id"], flush=True)
            continue
        print("Generating " + job["id"], flush=True)
        env = {**os.environ, "COMFY_URL": job["url"], "PYTHONUNBUFFERED": "1"}
        with (folder / "generator.log").open("w") as log:
            result = subprocess.run(job["argv"], cwd=PIPELINE, env=env, stdout=log, stderr=subprocess.STDOUT)
        if result.returncode or not Path(job["output"]).exists():
            failures.append(job["id"])
            print(f"WARNING: {job['id']} failed; see {folder / 'generator.log'}", flush=True)
            continue
        record = json.loads((folder / "runs.jsonl").read_text().splitlines()[-1])
        write(folder / "completed.json", record)
        print(f"Completed {job['id']} in {record['seconds']} s", flush=True)
    if failures:
        print("Failed jobs retained for retry: " + ", ".join(failures), flush=True)
        return 1
    return 0


def sheets(output):
    plan = read(output / "plan.json")
    spec = plan["spec"]
    font_path = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
    font = ImageFont.truetype(font_path, 15)
    small = ImageFont.truetype(font_path, 12)
    groups = {}
    for route in spec["routes"]:
        groups.setdefault(route["group"], []).append(route["id"])
    for group, route_ids in groups.items():
        jobs = [j for j in plan["jobs"] if j["route"] in route_ids]
        columns = list(dict.fromkeys((j["route"], j["style"], j["seed"]) for j in jobs))
        # Bound both dimensions so a ten-item study remains legible in a chat.
        page = 0
        for start in range(0, len(columns), 4):
            subset = columns[start:start + 4]
            for item_start in range(0, len(spec["items"]), 4):
                visible_items = spec["items"][item_start:item_start + 4]
                page += 1
                canvas = Image.new("RGB", (280 * (len(subset) + 1), 45 + len(visible_items) * 370), "#202329")
                draw = ImageDraw.Draw(canvas)
                draw.text((14, 5), spec["title"], fill="white", font=font)
                draw.text((14, 26), f"{group} / page {page} — study outputs; production recipe provisional", fill="#aebccd", font=small)
                for row, item in enumerate(visible_items):
                    top = 45 + row * 370
                    reference = Image.open(output / "inputs" / item["id"] / "reference.png")
                    canvas.paste(reference.resize((264, 264), Image.Resampling.NEAREST), (8, top + 45))
                    draw.text((8, top + 3), item["name"], font=font, fill="#e4d4ab")
                    draw.text((8, top + 24), "Original source / nearest", font=small, fill="#aebccd")
                    for col, (route_id, style_id, seed) in enumerate(subset, 1):
                        x = 280 * col + 8
                        job = next(j for j in jobs if (j["route"], j["style"], j["seed"], j["item"]) == (route_id, style_id, seed, item["id"]))
                        route = next(r for r in spec["routes"] if r["id"] == route_id)
                        draw.text((x, top + 3), route["label"], fill="white", font=font)
                        draw.text((x, top + 24), spec["styles"][style_id]["label"] + f" / seed {seed}", fill="#aebccd", font=small)
                        path = Path(job["output"])
                        if path.exists() and (path.parent / "completed.json").exists():
                            im = Image.open(path).convert("RGB")
                            im.thumbnail((264, 264), Image.Resampling.LANCZOS)
                            canvas.paste(im, (x + (264 - im.width) // 2, top + 45 + (264 - im.height) // 2))
                            seconds = read(path.parent / "completed.json")["seconds"]
                            caption = f"{job['conditioning']} | {seconds}s | {job['host']}"
                        else:
                            caption = "PENDING / FAILED — see job log"
                        draw.text((x, top + 316), caption, fill="#aebccd", font=small)
                canvas.save(output / f"{group}-{page}.jpg", quality=94)
    summary = [{**{k: job[k] for k in ("id", "route", "style", "item", "seed", "conditioning", "output")},
                "completed": (Path(job["output"]).parent / "completed.json").exists()} for job in plan["jobs"]]
    write(output / "summary.json", summary)
    print(f"{sum(j['completed'] for j in summary)}/{len(summary)} complete; sheets saved in {output}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    prep = sub.add_parser("prepare")
    prep.add_argument("--spec", type=Path, required=True)
    prep.add_argument("--source", type=Path, required=True)
    prep.add_argument("--out", type=Path, required=True)
    prep.add_argument("--upstream", type=Path, help="completed study whose reconstruction feeds a route's upstream_route")
    prep.add_argument("--host", choices=list(HOSTS), help="run every route on this host, overriding the spec")
    gen = sub.add_parser("generate")
    gen.add_argument("out", type=Path)
    gen.add_argument("--host")
    gen.add_argument("--routes", nargs="+")
    view = sub.add_parser("sheets")
    view.add_argument("out", type=Path)
    prompts = sub.add_parser("prompts", help="export all literal prompts from an existing saved plan")
    prompts.add_argument("out", type=Path)
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args.spec, args.source, args.out, args.upstream, args.host)
    elif args.command == "generate":
        return generate(args.out.resolve(), args.host, args.routes)
    elif args.command == "sheets":
        sheets(args.out.resolve())
    else:
        export_prompts(args.out.resolve())
    return 0


if __name__ == "__main__":
    sys.exit(main())
