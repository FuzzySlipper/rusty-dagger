#!/usr/bin/env python3
"""Generate a 4-view orthographic character turnaround (Front | Right Profile | Left Profile | Back)
from an approved front concept using MiniMax H3 Image (p-h3-image_w4a8), auto-slice 4 aligned
832x1216 views, and restore crisp inked-gouache detail with a light Krea2-Turbo New B pass.

Supports both empty-handed rest poses and 90-degree horizontal right-hand weapon holds (--right-weapon).
Batches all MiniMax H3 4-view sheets before all Krea2 New B polish passes so each UNET loads only once.
"""
import argparse
import json
import sys
import time
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
SA_DIR = HERE / "sprite-anchored-wip"
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(SA_DIR))
import back_ph3 as bp
import sprite_front as sf

VIEWS = ("front", "right", "left", "back")


def build_turnaround_prompt(extra: str, pronoun: str, right_weapon: str) -> str:
    pos = "his" if pronoun == "He" else ("her" if pronoun == "She" else "its")
    obj = "him" if pronoun == "He" else ("her" if pronoun == "She" else "it")
    subj = "man" if pronoun == "He" else ("woman" if pronoun == "She" else "character")
    extra_clean = extra.strip()
    extra_clause = f" {extra_clean.rstrip('.')}." if extra_clean else ""

    if right_weapon.strip():
        wep = right_weapon.strip().rstrip(".")
        intro = (
            f"Edit <Picture 1> into a precise four-view orthographic character turnaround sheet of this exact same {subj} on a "
            f"flat solid dark slate-grey background (RGB 48,52,59), adding {wep} held firmly in {pos} right hand.{extra_clause}\n\n"
            "A wide horizontal slate sheet contains four equal vertical columns in a single straight row, all four figures standing "
            "at the exact same head-to-boot height and scale on the same baseline, with clean empty slate between figures:\n\n"
        )
        col1 = (
            f"1. COLUMN 1 — STRAIGHT FRONT VIEW (facing directly toward the viewer): {pronoun}'s arms hang naturally at {pos} sides in "
            f"the exact relaxed A-pose of <Picture 1>. {pronoun}'s left hand (viewer's right) is empty. {pronoun}'s right fist (viewer's left) "
            "grips the weapon aimed STRAIGHT OUT AT THE CAMERA along the line of sight — seen strictly head-on in extreme end-on "
            "perspective so ONLY the wrapped knuckles, vertical crossguard, and tiny foreshortened tip pointing dead-center toward "
            "the viewer are visible. Zero sideways or downward-hanging blade or shaft length is visible in this front view.\n\n"
        )
        col2 = (
            f"2. COLUMN 2 — EXACT RIGHT-SIDE PROFILE (90-degree turn facing page RIGHT, showing {pos} right side closest to the "
            f"camera): {pronoun}'s right arm hangs vertically straight down at {pos} side, and {pos} closed right fist holds {wep} "
            f"extending HORIZONTALLY STRAIGHT FORWARD TO PAGE RIGHT at a strict 90-degree right angle from {pos} vertical forearm "
            "(parallel to the floor, pointing horizontally right). Shows the full un-foreshortened side profile of the weapon jutting "
            f"horizontally forward to page right, along with the exact right-side profile of {pos} costume.{extra_clause}\n\n"
        )
        col3 = (
            f"3. COLUMN 3 — EXACT LEFT-SIDE PROFILE (90-degree turn facing page LEFT, showing {pos} left side closest to the camera): "
            f"{pronoun}'s empty left arm hangs vertically straight down at {pos} left side in a relaxed empty hanging pose. "
            f"{pronoun}'s far-side right arm is hidden behind {pos} torso while the front half of the exact same horizontal weapon "
            f"juts cleanly forward to page LEFT past the front of {pos} thigh (parallel to the floor, pointing horizontally left at "
            f"the exact same hip height as Column 2). Shows the exact left-side profile of {pos} costume.{extra_clause}\n\n"
        )
        col4 = (
            f"4. COLUMN 4 — STRAIGHT BACK VIEW (180-degree turn facing directly away from the viewer):{extra_clause} "
            f"{pronoun}'s left hand (viewer's left) is empty showing the smooth back of {pos} hand with five clean fingers curling "
            f"gently forward away from the camera. {pronoun}'s right hand (viewer's right) holds the weapon aimed STRAIGHT FORWARD "
            "AWAY FROM THE CAMERA — seen strictly from behind so ONLY the smooth back of the fist, the pommel/butt end facing "
            "straight at the viewer, and the top and bottom tips of the vertical crossguard are visible; the blade is completely "
            "hidden behind the fist pointing away from the viewer (zero weapon sticking out sideways or downward).\n\n"
        )
    else:
        intro = (
            f"Edit <Picture 1> into a precise four-view orthographic character turnaround sheet of this exact same {subj} on a "
            f"flat solid dark slate-grey background (RGB 48,52,59).{extra_clause}\n\n"
            "A wide horizontal slate sheet contains four equal vertical columns in a single straight row, all four figures standing "
            "at the exact same head-to-boot height and scale on the same baseline, with clean empty slate between figures:\n\n"
        )
        col1 = (
            f"1. COLUMN 1 — STRAIGHT FRONT VIEW (facing directly toward the viewer): Exact match to <Picture 1>'s standing pose, "
            f"costume, face, and proportions, with both arms hanging naturally at {pos} sides and both empty hands gently curled "
            f"palm-inward, thumbs forward.\n\n"
        )
        col2 = (
            f"2. COLUMN 2 — EXACT RIGHT-SIDE PROFILE (90-degree turn facing page RIGHT, showing {pos} right side closest to the "
            f"camera): {pronoun}'s right arm hangs vertically straight down at {pos} side with an empty relaxed hanging hand "
            f"(fingers gently curled forward), showing the exact right-side silhouette of {pos} head, torso, back garment, and boots.{extra_clause}\n\n"
        )
        col3 = (
            f"3. COLUMN 3 — EXACT LEFT-SIDE PROFILE (90-degree turn facing page LEFT, showing {pos} left side closest to the camera): "
            f"{pronoun}'s left arm hangs vertically straight down at {pos} side with an empty relaxed hanging hand (fingers gently "
            f"curled forward), showing the exact left-side silhouette of {pos} head, torso, back garment, and boots.{extra_clause}\n\n"
        )
        col4 = (
            f"4. COLUMN 4 — STRAIGHT BACK VIEW (180-degree turn facing directly away from the viewer):{extra_clause} "
            f"Both hanging hands match the exact reverse of <Picture 1>'s relaxed pose — viewed strictly from behind showing the "
            "smooth backs of both hands and knuckles facing outward/backward with five clean naturally proportioned fingers "
            "curling gently forward away from the camera and thumbs tucked forward toward the front out of view.\n\n"
        )

    tail = (
        "Preserve <Picture 1>'s exact warm-umber inked gouache brushwork, crisp detail, vivid local pigments, and neutral even "
        "overhead lighting across all four views. Only the four full-length figures on flat slate grey; no text, labels, "
        "floor, or cast shadows."
    )
    return (intro + col1 + col2 + col3 + col4 + tail).replace("He's", "His").replace("She's", "Her").replace("It's", "Its")


def build_krea_polish_prompt(extra: str, style_variant: str) -> str:
    extra_clause = f" ({extra.strip().rstrip('.')})" if extra.strip() else ""
    if style_variant == "crunchy-warm":
        style_block = (
            "Drawn and painted as a vivid, rich warm 1990s PC fantasy role-playing game character illustration: fine warm "
            "dark-umber pen-and-ink contour lines (never thick pure-black comic outlines), delicate crosshatching inside cloth "
            "folds, weathered dry-brush gouache texture, and rich warm ochre-and-umber colour harmony (4 to 6 painted value "
            "groups per material). Warm even overhead daylight equal on both sides."
        )
    elif style_variant == "crunchy":
        style_block = (
            "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character sheet: confident dark-umber "
            "pen-and-ink contour lines, fine crosshatching inside cloth folds, weathered dry-brush texture and opaque matte "
            "gouache colour planes (4 to 6 painted value groups per material)."
        )
    elif style_variant == "clean":
        style_block = (
            "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character sheet: confident dark-umber "
            "pen-and-ink contour lines, fine crosshatching only inside cloth folds, and opaque matte gouache colour planes "
            "(4 to 6 painted value groups per material). Soft even overhead light equal on both sides."
        )
    else:  # painterly-vivid (default)
        style_block = (
            "Drawn and painted as a vivid, richly textured 1990s PC fantasy role-playing game character illustration: fine warm "
            "dark-umber pen-and-ink contour lines (never thick pure-black comic outlines), delicate crosshatching inside cloth "
            "folds, lush weathered dry-brush gouache brushwork, and clean, vivid local pigments true to each material—true cool "
            "steel, deep teal, rich green, warm skin and leather—with warm dark-umber shadow contours and no global ochre or "
            "sepia tint over cool materials (4 to 6 painted value groups per material). Clean neutral even overhead daylight "
            "equal on both sides."
        )
    return (
        "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
        f"{style_block} Plain flat solid dark slate-grey background, RGB 48,52,59, no floor or border.\n\n"
        "Subject: Faithfully preserve the exact character, view angle, pose, framing, silhouette, weapon angle, hand position, "
        f"costume and colours of the input image{extra_clause}—only enhance hand-painted gouache texture and fine dark-umber ink linework."
    )


def slice_4view_sheet(sheet_path: Path, out_dir: Path, prefix: str, slice_w: int = 832, slice_h: int = 1216) -> dict[str, Path]:
    """Slice a 4-column 1-row turnaround sheet into 4 aligned views sharing exact baseline & height."""
    im = Image.open(sheet_path).convert("RGB")
    arr = np.asarray(im, dtype=np.float32)
    bg = arr[:20, :20].mean(axis=(0, 1))
    bg_rgb = tuple(int(round(v)) for v in bg)
    fg_all = np.abs(arr - bg).mean(axis=2) > 14
    ys_all = np.where(fg_all.any(axis=1))[0]
    if not len(ys_all):
        raise SystemExit(f"Empty turnaround sheet: {sheet_path}")
    gy0, gy1 = int(ys_all.min()), int(ys_all.max())
    gh = max(gy1 - gy0, 1)
    target_h = int(round(slice_h * 0.92))
    base_scale = target_h / gh
    top_pad = (slice_h - target_h) // 2

    col_w = im.width // 4
    results: dict[str, Path] = {}
    for idx, view in enumerate(VIEWS):
        x_start = idx * col_w
        x_end = im.width if idx == 3 else (idx + 1) * col_w
        col = im.crop((x_start, 0, x_end, im.height))
        carr = np.asarray(col, dtype=np.float32)
        cfg = np.abs(carr - bg).mean(axis=2) > 14
        cxs = np.where(cfg.any(axis=0))[0]
        if len(cxs):
            cx0, cx1 = int(cxs.min()), int(cxs.max())
            cw = cx1 - cx0 + 1
            sc = base_scale if view in ("front", "back") else min(base_scale, (slice_w - 32) / max(cw, 1))
            fig_cx = int(round(((cx0 + cx1) / 2) * sc))
        else:
            sc = base_scale
            fig_cx = int(round((col.width / 2) * sc))
        nw, nh = int(round(col.width * sc)), int(round(col.height * sc))
        res = col.resize((nw, nh), Image.LANCZOS)
        canvas = Image.new("RGB", (slice_w, slice_h), bg_rgb)
        dx = (slice_w // 2) - fig_cx
        dy = top_pad - int(round(gy0 * sc))
        canvas.paste(res, (dx, dy))
        out_p = out_dir / f"{prefix}-{view}-raw.png"
        canvas.save(out_p)
        results[view] = out_p
    return results


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--front", required=True, type=Path, help="Approved front concept PNG (e.g. 832x1216)")
    ap.add_argument("--out", required=True, type=Path, help="Output directory")
    ap.add_argument("--extra", default="", help="Back/side costume note (cape length, back armor/corset, hair, scabbard, etc.)")
    ap.add_argument("--pronoun", choices=["He", "She", "It"], default="He")
    ap.add_argument("--right-weapon", default="", help="Optional weapon held in right hand at a 90-degree horizontal forward hold (omit for empty-handed rest pose)")
    ap.add_argument("--seeds", default="910001,910002,910003,910004")
    ap.add_argument("--width", type=int, default=4096, help="4-view sheet width (default 4096 = 4 x 1024)")
    ap.add_argument("--height", type=int, default=1536, help="4-view sheet height (default 1536)")
    ap.add_argument("--slice-width", type=int, default=832)
    ap.add_argument("--slice-height", type=int, default=1216)
    ap.add_argument("--krea-denoise", type=float, default=0.22, help="Light Stage 2 Krea2 New B detail-polish denoise (default 0.22; set 0 to skip)")
    ap.add_argument("--krea-style", choices=["painterly-vivid", "clean", "crunchy", "crunchy-warm"], default="painterly-vivid")
    ap.add_argument("--comfy", default="http://192.168.1.20:8188")
    args = ap.parse_args()

    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    prompt = build_turnaround_prompt(args.extra, args.pronoun, args.right_weapon)
    (out / "turnaround.prompt.txt").write_text(prompt + "\n")

    up_front = bp.comfy_upload(args.comfy, args.front.resolve(), f"turn4-{out.name}-front.png")
    h3_tpl = {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": "p-h3-image_w4a8.safetensors", "weight_dtype": "default"}},
        "2": {"class_type": "CLIPLoader", "inputs": {"clip_name": "p-h3-image_text_encoder_w4a8.safetensors", "type": "minimax", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": "p-h3-image_vae_int8_convrot.safetensors"}},
        "13": {"class_type": "LoadImage", "inputs": {"image": up_front}},
        "4": {
            "class_type": "PH3ImageConditioning",
            "inputs": {
                "clip": ["2", 0], "vae": ["3", 0], "prompt": prompt,
                "width": args.width, "height": args.height, "images.image_1": ["13", 0],
            },
        },
        "5": {"class_type": "MiniMaxH3SigmaShift", "inputs": {"model": ["1", 0], "shift_video": 1.0, "shift_audio": 1.0}},
        "6": {"class_type": "BasicGuider", "inputs": {"model": ["5", 0], "conditioning": ["4", 0]}},
        "7": {"class_type": "ManualSigmas", "inputs": {"sigmas": "1.0, 0.984753, 0.967905, 0.951688, 0.925372, 0.881923, 0.810083, 0.528718, 0.0"}},
        "8": {"class_type": "KSamplerSelect", "inputs": {"sampler_name": "euler"}},
        "9": {"class_type": "RandomNoise", "inputs": {"noise_seed": 910001}},
        "10": {"class_type": "SamplerCustomAdvanced", "inputs": {"noise": ["9", 0], "guider": ["6", 0], "sampler": ["8", 0], "sigmas": ["7", 0], "latent_image": ["4", 1]}},
        "11": {"class_type": "VAEDecode", "inputs": {"samples": ["10", 0], "vae": ["3", 0]}},
        "12": {"class_type": "SaveImage", "inputs": {"images": ["11", 0], "filename_prefix": f"concept/turn4-{out.name}"}},
    }

    seeds = [int(x) for x in args.seeds.split(",") if x.strip()]
    sheet_items = []
    for i, s in enumerate(seeds, 1):
        sdir = out / f"s{i}"
        sdir.mkdir(parents=True, exist_ok=True)
        g = json.loads(json.dumps(h3_tpl))
        g["9"]["inputs"]["noise_seed"] = s
        g["12"]["inputs"]["filename_prefix"] = f"concept/turn4-{out.name}-s{i}"
        t0 = time.monotonic()
        sp = sdir / f"sheet-s{i}.png"
        sp.write_bytes(bp.submit_and_fetch(args.comfy, g, "12"))
        (sdir / f"sheet-s{i}.png.graph.json").write_text(json.dumps(g, indent=1) + "\n")
        sliced = slice_4view_sheet(sp, sdir, f"s{i}", args.slice_width, args.slice_height)
        sheet_items.append((i, s, sdir, sp, sliced))
        print(f"[H3 4-view s{i}] seed={s} {args.width}x{args.height} {time.monotonic() - t0:.1f}s -> {sp.name}", flush=True)

    # Optional Stage 2 Krea2-Turbo New B detail-restoration pass (batched after all H3 sheets so UNET loads once)
    d_tag = int(round(args.krea_denoise * 100))
    final_views: dict[int, dict[str, Path]] = {}
    if args.krea_denoise > 0:
        wf2 = json.loads(sf.WF_TEMPLATE.read_text())
        s2_keys = ["11_krea_unet", "12_krea_clip", "13_krea_vae", "14_lora_brom", "15_lora_elmore", "16_lora_retroink",
                   "17_krea_encode", "18_krea_prompt", "19_krea_zero_neg", "20_krea_sampler", "21_krea_decode", "22_save_final"]
        s2_tpl = {k: json.loads(json.dumps(wf2[k])) for k in s2_keys}
        s2_tpl["0_load_s1"] = {"class_type": "LoadImage", "inputs": {"image": "stage1.png"}}
        s2_tpl["17_krea_encode"]["inputs"]["pixels"] = ["0_load_s1", 0]
        krea_p = build_krea_polish_prompt(args.extra, args.krea_style)
        (out / "krea_polish.prompt.txt").write_text(krea_p + "\n")

        for i, s, sdir, sp, sliced in sheet_items:
            fmap = {}
            for v_idx, view in enumerate(VIEWS):
                up = sf.comfy_upload(args.comfy, sliced[view], f"turn4-{out.name}-s{i}-{view}.png")
                g2 = json.loads(json.dumps(s2_tpl))
                g2["0_load_s1"]["inputs"]["image"] = up
                g2["18_krea_prompt"]["inputs"]["text"] = krea_p
                g2["20_krea_sampler"]["inputs"]["seed"] = s + v_idx
                g2["20_krea_sampler"]["inputs"]["denoise"] = args.krea_denoise
                g2["22_save_final"]["inputs"]["filename_prefix"] = f"concept/turn4-{out.name}-s{i}-{view}-d{d_tag}"
                t0 = time.monotonic()
                fp = sdir / f"s{i}-{view}-d{d_tag}.png"
                fp.write_bytes(sf.submit_and_fetch(args.comfy, g2, "22_save_final"))
                (sdir / f"s{i}-{view}-d{d_tag}.graph.json").write_text(json.dumps(g2, indent=1) + "\n")
                fmap[view] = fp
                print(f"[Krea d={args.krea_denoise} s{i} {view}] {time.monotonic() - t0:.1f}s -> {fp.name}", flush=True)
            final_views[i] = fmap
    else:
        for i, s, sdir, sp, sliced in sheet_items:
            final_views[i] = sliced

    # Write per-seed CMS batch manifest & 4-view strip + combined overview sheet
    pw, ph, hdr = 1440, int(round(1440 * args.height / args.width)), 26
    ov = Image.new("RGB", (pw, (ph + hdr) * len(sheet_items)), (18, 20, 24))
    od = ImageDraw.Draw(ov)

    vw, vh = 320, int(round(320 * args.slice_height / args.slice_width))
    for r, (i, s, sdir, sp, sliced) in enumerate(sheet_items):
        ov.paste(Image.open(sp).convert("RGB").resize((pw, ph), Image.LANCZOS), (0, r * (ph + hdr) + hdr))
        od.text((10, r * (ph + hdr) + 5), f"s{i} (seed {s}) — raw H3 {args.width}x{args.height}", fill=(245, 225, 165))

        strip = Image.new("RGB", (vw * 4, vh + hdr), (18, 20, 24))
        sd = ImageDraw.Draw(strip)
        fmap = final_views[i]
        for c, view in enumerate(VIEWS):
            strip.paste(Image.open(fmap[view]).convert("RGB").resize((vw, vh), Image.LANCZOS), (c * vw, hdr))
            sd.text((c * vw + 8, 5), f"s{i} {view.upper()}" + (f" (Krea d={args.krea_denoise})" if args.krea_denoise > 0 else ""), fill=(190, 245, 205))
        strip.save(sdir / f"s{i}_views.jpg", quality=93)

        tag = f"krea-d{d_tag}" if args.krea_denoise > 0 else "raw"
        manifest = {
            "batch": f"turnaround-4view-s{i}-{tag}",
            "defaults": {
                "model": f"MiniMax H3 Image ({args.width}x{args.height} 4-view turnaround)" + (f" -> Krea2-Turbo New B {args.krea_style} (d={args.krea_denoise})" if args.krea_denoise > 0 else ""),
                "prompt": prompt,
                "seed": str(s),
            },
            "candidates": [
                {
                    "name": f"4view-s{i}-{view}-{tag}",
                    "image": fmap[view].name,
                    "view": view,
                }
                for view in VIEWS
            ],
        }
        (sdir / "batch.json").write_text(json.dumps(manifest, indent=2) + "\n")

    ov.save(out / "sheets_overview.jpg", quality=92)
    print("Wrote", out / "sheets_overview.jpg", flush=True)


if __name__ == "__main__":
    main()
