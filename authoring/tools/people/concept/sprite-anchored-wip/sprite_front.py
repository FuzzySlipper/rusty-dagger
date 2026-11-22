#!/usr/bin/env python3
"""Sprite-anchored 2-stage portrait wide-A concept front generator (dungeon enemies & creatures).

Stage 1: Qwen-Image 2.1 InkWash (832x1216) anchored on the nearest-neighbour enlarged classic sprite.
Stage 2: Krea2-Turbo New B i2i (Brom 0.35 + Elmore 0.45 + RetroInk 0.45, denoise 0.38..0.46).

Batches all Stage-1 seeds before Stage-2 seeds so each UNET stays warm on gpu-lab ComfyUI (~12s/img).
"""
import argparse
import json
import time
import urllib.request
import uuid
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
CONCEPT = HERE.parent
REPO = CONCEPT.parents[3]
WF_TEMPLATE = HERE / "workflow-new-b-sprite-to-concept-2stage.json"
DEFAULT_OUTPUT = REPO / "local/art/generated/studies/people-concept"


def comfy_upload(comfy: str, path: Path, remote_name: str) -> str:
    boundary = "----SpriteFrontUpload7MA4YWxkTrZu0gW"
    body = (
        f"--{boundary}\r\n"
        f'Content-Disposition: form-data; name="image"; filename="{remote_name}"\r\n'
        f"Content-Type: image/png\r\n\r\n"
    ).encode() + path.read_bytes() + (
        f"\r\n--{boundary}\r\n"
        f'Content-Disposition: form-data; name="overwrite"\r\n\r\ntrue\r\n'
        f"--{boundary}--\r\n"
    ).encode()
    req = urllib.request.Request(
        f"{comfy.rstrip('/')}/upload/image", data=body,
        headers={"Content-Type": f"multipart/form-data; boundary={boundary}"},
    )
    return json.loads(urllib.request.urlopen(req, timeout=120).read())["name"]


def submit_and_fetch(comfy: str, graph: dict, save_node_id: str) -> bytes:
    req = urllib.request.Request(
        f"{comfy.rstrip('/')}/prompt",
        json.dumps({"prompt": graph, "client_id": uuid.uuid4().hex}).encode(),
        {"Content-Type": "application/json"},
    )
    pid = json.loads(urllib.request.urlopen(req, timeout=120).read())["prompt_id"]
    while True:
        hist = json.loads(urllib.request.urlopen(f"{comfy.rstrip('/')}/history/{pid}", timeout=120).read())
        if pid in hist:
            break
        time.sleep(1.5)
    st = hist[pid]["status"]
    if st["status_str"] != "success":
        raise SystemExit(f"ComfyUI job failed: {st}")
    im_meta = hist[pid]["outputs"][save_node_id]["images"][0]
    fn, sub, typ = im_meta["filename"], im_meta["subfolder"], im_meta["type"]
    return urllib.request.urlopen(f"{comfy.rstrip('/')}/view?filename={fn}&subfolder={sub}&type={typ}", timeout=120).read()


def make_slate_canvas(sprite_png: Path, out_png: Path, width: int, height: int) -> Path:
    im = Image.open(sprite_png).convert("RGBA")
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.where(a > 0)
    crop = im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)) if len(xs) else im
    target_h = int(round(height * 0.87))
    scale = max(1, int(round(target_h / max(crop.height, 1))))
    big = crop.resize((crop.width * scale, crop.height * scale), Image.NEAREST)
    if big.width > width - 32 or big.height > height - 32:
        big.thumbnail((width - 32, height - 32), Image.NEAREST)
    canvas = Image.new("RGBA", (width, height), (48, 52, 59, 255))
    canvas.alpha_composite(big, ((width - big.width) // 2, (height - big.height) // 2))
    out_png.parent.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(out_png)
    return out_png


def make_portrait_ref(portrait_png: Path, out_png: Path, size: int = 512, border_trim: int = 3) -> Path:
    """Trim the classic gold frame border from a dialogue portrait and upscale cleanly for Stage 1."""
    im = Image.open(portrait_png).convert("RGB")
    if border_trim > 0 and im.width > border_trim * 4 and im.height > border_trim * 4:
        im = im.crop((border_trim, border_trim, im.width - border_trim, im.height - border_trim))
    scale = max(1, int(round(size / max(im.width, im.height, 1))))
    big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    out_png.parent.mkdir(parents=True, exist_ok=True)
    big.save(out_png)
    return out_png


def build_prompts(brief: str, pronoun: str, hooded_shadow: bool, style_only: bool = False, portrait_only: bool = False) -> tuple[str, str]:
    pos = "his" if pronoun == "He" else ("her" if pronoun == "She" else "its")
    face_q = (
        "If wearing a hood, paint a real shadowed face inside the deep hood—brow and eyes recessed in dark-umber shadow while "
        "nose, cheekbones and jaw remain clearly sculpted in muted warm tones (never an empty black void). "
        if hooded_shadow else ""
    )
    if style_only and portrait_only:
        qwen_p = (
            "Task: Paint a full-body high-resolution dark-fantasy character illustration showing the character from the front, "
            "head to toe, for the person whose face is shown in the small classic RPG portrait reference. Reconstruct natural "
            "high-resolution anatomy and costume detail; do not enlarge or trace the pixel blocks, and do not crop to a bust or close-up portrait.\n\n"
            "Input image: facial likeness, hair, skin tone, age and ornaments ONLY. Preserve this character's facial likeness, hair, "
            "skin tone and age while clearly incorporating any extra facial details described in the brief, and paint their complete "
            f"full-body figure from the front, head to toe. {face_q}\n\n"
            f"Subject: {brief.rstrip('.')}.\n\n"
            "Style: ART DIRECTION — VAGRANT STORY & OLD TABLETOP DARK FANTASY PEN-AND-INK CROSSHATCHING WITH MUTED WASH\n\n"
            "Draw the character as an accomplished pen-and-ink dark-fantasy illustration in the tradition of Akihiko Yoshida "
            "(Vagrant Story) and classic tabletop RPG manuals, finished with opaque muted gouache washes. Build a confident underlying "
            "ink drawing with crisp seams, dark-umber contours, fine directional crosshatching inside folds and shadows, and opaque "
            "local colour washes without glossy 3D shading.\n\n"
            "Presentation: Full-body isolated character shown from the front, head to toe, filling ~88% of the vertical portrait "
            "frame on a perfectly flat solid dark slate grey background, RGB 48,52,59. No floor, no scenery, no cast shadow, no border."
        )
        krea_p = (
            "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
            "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character illustration: confident dark-umber "
            "pen-and-ink contour lines, fine crosshatching inside cloth folds and shadows, and opaque matte gouache colour planes "
            f"(4 to 6 painted value groups per material). {face_q}"
            "Plain flat dark slate-grey background, RGB 48,52,59, no floor, no scenery, no border.\n\n"
            f"Subject: Full-body character portrait shown from the front, head to toe. {brief.rstrip('.')}."
        )
        return qwen_p, krea_p

    if style_only:
        qwen_p = (
            "Task: Redraw a very small classic fantasy RPG sprite as newly illustrated high resolution art. "
            "Reconstruct plausible missing detail; do not enlarge or trace the pixel blocks.\n\n"
            "Input image: exact composition, pose, silhouette, props and colour-blocking reference. Faithfully preserve the "
            "classic sprite's exact pose, gesture, proportions, silhouette, held objects, companions and major colour regions—"
            f"do not change the pose into a turnaround stance or remove elements. {face_q}\n\n"
            f"Subject: {brief.rstrip('.')}.\n\n"
            "Style: ART DIRECTION — VAGRANT STORY & OLD TABLETOP DARK FANTASY PEN-AND-INK CROSSHATCHING WITH MUTED WASH\n\n"
            "Draw the subject as an accomplished pen-and-ink dark-fantasy illustration in the tradition of Akihiko Yoshida "
            "(Vagrant Story) and classic tabletop RPG manuals, finished with opaque muted gouache washes. Build a confident underlying "
            "ink drawing with crisp seams, dark-umber contours, fine directional crosshatching inside folds and shadows, and opaque "
            "local colour washes preserving the sprite's exact colour relationships without glossy 3D shading.\n\n"
            "Presentation: Isolated subject matching the classic sprite's silhouette and framing on a perfectly flat solid dark "
            "slate grey background, RGB 48,52,59. No floor, no scenery, no cast shadow, no border."
        )
        krea_p = (
            "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
            "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game illustration: confident dark-umber pen-and-ink "
            "contour lines, fine crosshatching inside folds and shadows, and opaque matte gouache colour planes (4 to 6 painted "
            f"value groups per material). {face_q}"
            "Plain flat dark slate-grey background, RGB 48,52,59, no floor, no scenery, no border.\n\n"
            f"Subject: {brief.rstrip('.')}. Faithfully preserve the pose, composition, props and silhouette of the input image."
        )
        return qwen_p, krea_p

    stance = (
        f"Shown full-length as the front view of a costume fitting or character turnaround sheet, in a symmetrical neutral "
        f"standing A-pose facing directly forward, feet slightly apart with both shoes/feet whole. Both arms are held out at a "
        f"wide 30-degree A-pose well away from {pos} sides, leaving a wide clear gap of background between each arm, hand and "
        f"{pos} waist/pouches; hands open and empty (no weapon or staff). "
    )
    if portrait_only:
        qwen_p = (
            "Task: Paint a full-length high-resolution dark-fantasy character turnaround sheet for the character whose face is "
            "shown in the small classic RPG portrait reference. Reconstruct natural high-resolution anatomy and costume detail; "
            "do not enlarge or trace the pixel blocks, and do not crop to a bust or close-up portrait.\n\n"
            "Input image: facial likeness, hair, skin tone, age and collar colour cue ONLY. Preserve this character's "
            "facial likeness, hair, skin tone and age while clearly incorporating any extra facial details described in the brief "
            "(such as scars, markings, or expression), and paint their full standing body from head to toe in a clean symmetrical "
            f"30-degree wide-A turnaround pose with empty open hands. {face_q}\n\n"
            f"Subject and composition: {brief.rstrip('.')}. {stance}\n\n"
            "Style: ART DIRECTION — VAGRANT STORY & OLD TABLETOP DARK FANTASY PEN-AND-INK CROSSHATCHING WITH MUTED WASH\n\n"
            "Draw the character as an accomplished pen-and-ink dark-fantasy character sheet in the tradition of Akihiko Yoshida "
            "(Vagrant Story) and classic tabletop RPG manuals, finished with opaque muted gouache washes. Build a confident underlying "
            "ink drawing with crisp seams, dark-umber contours, fine directional crosshatching inside folds, and opaque local colour "
            "washes without glossy 3D shading.\n\n"
            "Presentation: Full-length isolated standing figure from head to shoes filling ~88% of the vertical portrait frame on a "
            "perfectly flat solid dark slate grey background, RGB 48,52,59. Soft overhead light equal on both sides. No floor, no cast shadow, no border."
        )
        krea_p = (
            "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
            "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character sheet: confident dark-umber pen-and-ink "
            "contour lines, fine crosshatching only inside cloth folds and overhangs equally on both sides, and opaque matte gouache "
            f"colour planes (4 to 6 painted value groups per material). {face_q}"
            "Plain flat dark slate-grey background, RGB 48,52,59, no floor or border.\n\n"
            f"Subject and composition: {brief.rstrip('.')}. {stance}"
        )
        return qwen_p, krea_p

    qwen_p = (
        "Task: Redraw a very small classic fantasy RPG sprite as newly illustrated high resolution art. "
        "Reconstruct plausible missing detail; do not enlarge or trace the pixel blocks.\n\n"
        "Input image: costume, silhouette and colour-blocking reference. Preserve the classic figure's proportions, neckline, "
        "garment overlap and major colour regions while squaring into a clean symmetrical 30-degree wide-A turnaround pose with "
        f"empty open hands. {face_q}\n\n"
        f"Subject and composition: {brief.rstrip('.')}. {stance}\n\n"
        "Style: ART DIRECTION — VAGRANT STORY & OLD TABLETOP DARK FANTASY PEN-AND-INK CROSSHATCHING WITH MUTED WASH\n\n"
        "Draw the character as an accomplished pen-and-ink dark-fantasy character sheet in the tradition of Akihiko Yoshida "
        "(Vagrant Story) and classic tabletop RPG manuals, finished with opaque muted gouache washes. Build a confident underlying "
        "ink drawing with crisp seams, dark-umber contours, fine directional crosshatching inside folds, and opaque local colour "
        "washes preserving the sprite's exact colour relationships without glossy 3D shading.\n\n"
        "Presentation: Full-length isolated figure filling ~88% of the vertical portrait frame on a perfectly flat solid dark "
        "slate grey background, RGB 48,52,59. Soft overhead light equal on both sides. No floor, no cast shadow, no border."
    )
    krea_p = (
        "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
        "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character sheet: confident dark-umber pen-and-ink "
        "contour lines, fine crosshatching only inside cloth folds and overhangs equally on both sides, and opaque matte gouache "
        f"colour planes (4 to 6 painted value groups per material). {face_q}"
        "Plain flat dark slate-grey background, RGB 48,52,59, no floor or border.\n\n"
        f"Subject and composition: {brief.rstrip('.')}. {stance}"
    )
    return qwen_p, krea_p


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("id", help="Character id (looked up in briefs.json unless --brief is given)")
    ap.add_argument("--sprite", type=Path, default=None, help="Classic front sprite PNG (transparent RGBA)")
    ap.add_argument("--portrait", type=Path, default=None, help="Classic dialogue portrait PNG (implies --portrait-only)")
    ap.add_argument("--brief", default="", help="Override costume brief text")
    ap.add_argument("--pronoun", choices=["He", "She", "It"], default="He")
    ap.add_argument("--hooded-shadow", action="store_true", help="Keep brow/eyes shadowed inside deep hood without pitch-black void")
    ap.add_argument("--style-only", action="store_true", help="Direct high-res style transfer preserving the sprite's exact pose, props, and composition (no wide-A turnaround or overhead-lighting prep)")
    ap.add_argument("--portrait-only", action="store_true", help="Anchor face/hair/identity on a dialogue portrait while generating a full-length wide-A turnaround body from the brief")
    ap.add_argument("--portrait-trim", type=int, default=3, help="Pixels to trim off each edge of a classic framed portrait (default 3)")
    ap.add_argument("--seeds", default="42201,42202,42203")
    ap.add_argument("--denoise", type=float, default=0.38, help="Stage 2 Krea2 New B denoise (0.38=grittier ink, 0.46=more Brom/Elmore)")
    ap.add_argument("--width", type=int, default=832)
    ap.add_argument("--height", type=int, default=1216)
    ap.add_argument("--out", type=Path, default=None)
    ap.add_argument("--comfy", default="http://192.168.1.20:8188")
    args = ap.parse_args()

    brief = args.brief
    pronoun = args.pronoun
    if not brief:
        briefs = json.loads((CONCEPT / "briefs.json").read_text())["briefs"]
        if args.id not in briefs:
            raise SystemExit(f"{args.id} not in briefs.json; pass --brief or add entry first")
        brief = briefs[args.id]["brief"]
        pronoun = briefs[args.id].get("pronoun", pronoun)

    portrait_only = args.portrait_only or (args.portrait is not None)
    ref_src = args.portrait or args.sprite
    if ref_src is None:
        raise SystemExit("Pass --sprite PATH or --portrait PATH")

    out = (args.out or (DEFAULT_OUTPUT / f"{args.id}-sa")).resolve()
    out.mkdir(parents=True, exist_ok=True)
    if portrait_only:
        canvas = make_portrait_ref(ref_src.resolve(), out / "portrait_ref.png", border_trim=args.portrait_trim)
    else:
        canvas = make_slate_canvas(ref_src.resolve(), out / f"sprite_{args.width}x{args.height}.png", args.width, args.height)
    up_spr = comfy_upload(args.comfy, canvas, f"sa-{args.id}-{canvas.name}")

    qwen_p, krea_p = build_prompts(brief, pronoun, args.hooded_shadow, style_only=args.style_only, portrait_only=portrait_only)
    wf2 = json.loads(WF_TEMPLATE.read_text())

    s1_keys = ["1_load_sprite", "2_qwen_unet", "3_qwen_clip", "4_qwen_vae", "5_qwen_cache",
               "6_qwen_prompt", "7_qwen_latent", "8_qwen_sampler", "9_qwen_decode", "10_save_stage1"]
    s1_tpl = {k: wf2[k] for k in s1_keys}
    s1_tpl["7_qwen_latent"]["inputs"]["width"] = args.width
    s1_tpl["7_qwen_latent"]["inputs"]["height"] = args.height

    s2_keys = ["11_krea_unet", "12_krea_clip", "13_krea_vae", "14_lora_brom", "15_lora_elmore", "16_lora_retroink",
               "17_krea_encode", "18_krea_prompt", "19_krea_zero_neg", "20_krea_sampler", "21_krea_decode", "22_save_final"]
    s2_tpl = {k: wf2[k] for k in s2_keys}
    s2_tpl["0_load_s1"] = {"class_type": "LoadImage", "inputs": {"image": "stage1.png"}}
    s2_tpl["17_krea_encode"]["inputs"]["pixels"] = ["0_load_s1", 0]

    seeds = [int(x) for x in args.seeds.split(",") if x.strip()]
    s1_paths = []
    for s in seeds:
        g1 = json.loads(json.dumps(s1_tpl))
        g1["1_load_sprite"]["inputs"]["image"] = up_spr
        g1["6_qwen_prompt"]["inputs"]["prompt"] = qwen_p
        g1["8_qwen_sampler"]["inputs"]["seed"] = s
        g1["10_save_stage1"]["inputs"]["filename_prefix"] = f"concept/{args.id}-sa-s1-{s}"
        t0 = time.monotonic()
        p1 = out / f"stage1-s{s}.png"
        p1.write_bytes(submit_and_fetch(args.comfy, g1, "10_save_stage1"))
        s1_paths.append((s, p1))
        print(f"[Stage1 s{s}] {time.monotonic() - t0:.1f}s -> {p1.name}", flush=True)

    d_tag = int(round(args.denoise * 100))
    for s, p1 in s1_paths:
        up_s1 = comfy_upload(args.comfy, p1, f"sa-{args.id}-s1-{s}.png")
        g2 = json.loads(json.dumps(s2_tpl))
        g2["0_load_s1"]["inputs"]["image"] = up_s1
        g2["18_krea_prompt"]["inputs"]["text"] = krea_p
        g2["20_krea_sampler"]["inputs"]["seed"] = s
        g2["20_krea_sampler"]["inputs"]["denoise"] = args.denoise
        g2["22_save_final"]["inputs"]["filename_prefix"] = f"concept/{args.id}-sa-s{s}-d{d_tag}"
        t0 = time.monotonic()
        p2 = out / f"front-s{s}-d{d_tag}.png"
        p2.write_bytes(submit_and_fetch(args.comfy, g2, "22_save_final"))
        (out / f"front-s{s}-d{d_tag}.prompt.txt").write_text(f"=== STAGE 1 ===\n{qwen_p}\n\n=== STAGE 2 (d={args.denoise}) ===\n{krea_p}\n")
        (out / f"front-s{s}-d{d_tag}.graph.json").write_text(json.dumps(g2, indent=1) + "\n")
        print(f"[Stage2 s{s} d={args.denoise}] {time.monotonic() - t0:.1f}s -> {p2.name}", flush=True)


if __name__ == "__main__":
    main()
