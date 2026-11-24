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
POSE_LOCK_DIR = HERE / "pose-lock"
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


def build_prompts(brief: str, pronoun: str, hooded_shadow: bool, style_only: bool = False, portrait_only: bool = False, show_both_arms: bool = False, pose_lock: bool = False, pose_garment: str = "auto", style_variant: str = "clean") -> tuple[str, str]:
    pos = "his" if pronoun == "He" else ("her" if pronoun == "She" else "its")
    face_q = (
        "If wearing a hood, paint a real shadowed face inside the deep hood—brow and eyes recessed in dark-umber shadow while "
        "nose, cheekbones and jaw remain clearly sculpted in muted warm tones (never an empty black void). "
        if hooded_shadow else ""
    )
    if pose_lock:
        draped = (
            pose_garment == "draped"
            or (pose_garment == "auto" and any(w in brief.lower() for w in ("robe", "gown", "dress", "cassock", "skirt")))
        )
        leg_clause = (
            "Legs stand straight beneath the long garment with both feet/shoes visible, flat and parallel, pointing straight ahead about hip-width apart."
            if draped
            else "Legs straight, feet flat and parallel pointing straight ahead about hip-width apart with a clear gap between the legs from mid-thigh down."
        )
        pose_text = (
            f"Standing straight and symmetric facing directly forward, head level and looking straight ahead, shoulders level. "
            f"Both arms hang down and slightly away from {pos} sides at about 22 degrees with a clear gap of background between each hand and {pos} hips, "
            f"elbows softly bent slightly forward, wrists straight, palms facing inward toward {pos} thighs, thumbs pointing forward, fingers together "
            f"and gently curled in a relaxed hanging hand (nothing held, empty hands, not spread open). {leg_clause}"
        )
        if style_variant in ("crunchy", "crunchy-warm"):
            task_line = (
                "Task: Paint a full-length high-resolution classic dark-fantasy RPG character sheet for the person whose face is shown in the small portrait reference. "
                "Reconstruct natural high-resolution anatomy and costume detail; do not enlarge or trace the pixel blocks.\n\n"
                if portrait_only
                else "Task: Redraw a very small classic fantasy RPG character sprite as newly illustrated high resolution art. "
                "Reconstruct plausible missing detail; do not enlarge or trace the pixel blocks.\n\n"
            )
            input_line = (
                "Input image: facial likeness, hair, skin tone, age and collar cues ONLY. Follow the exact pose skeleton (22-degree hanging arms, relaxed palm-inward "
                f"thumb-forward curled empty hands, parallel hip-width feet) while keeping the clothing cleanly tailored and uncluttered. {face_q}\n\n"
                if portrait_only
                else "Input image: costume and colour-blocking reference only. Follow the exact pose skeleton (22-degree hanging arms, relaxed palm-inward "
                f"thumb-forward curled empty hands, parallel hip-width feet) while preserving the classic sprite's costume, colours and proportions. {face_q}\n\n"
            )
            if style_variant == "crunchy-warm":
                qwen_p = (
                    f"{task_line}{input_line}"
                    f"Subject and composition: {brief.rstrip('.')}. {pose_text}\n\n"
                    "Style: ART DIRECTION — WARM 1990s TABLETOP DARK FANTASY PEN-AND-INK WITH RICH OCHRE-AND-UMBER GOUACHE WASH\n\n"
                    "Draw the character as a classic hand-painted dark-fantasy RPG illustration in the tradition of Akihiko Yoshida (Vagrant Story), "
                    "Larry Elmore, and classic tabletop RPG manuals, finished with rich warm opaque gouache washes. Build a confident underlying ink drawing "
                    "with fine warm dark-umber contours (never harsh cold black outlines), fine directional crosshatching inside cloth folds and shadows, "
                    "weathered dry-brush fabric grain, and a warm earthy ochre-and-umber colour harmony with rich warm sepia shadows and warm golden-cream "
                    "skin and cloth highlights (no cold grey cast, no glossy 3D shading).\n\n"
                    "Presentation: Full-length isolated figure filling ~88% of the vertical 832x1216 portrait frame on a perfectly flat solid dark slate grey "
                    "background, RGB 48,52,59. Warm even overhead daylight equal on both sides. No floor, no cast shadow, no border."
                )
                krea_p = (
                    "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
                    "Drawn and painted as a vivid, rich warm 1990s PC fantasy role-playing game character illustration: fine warm dark-umber pen-and-ink "
                    "contour lines (never thick pure-black comic outlines), delicate crosshatching inside cloth folds, weathered dry-brush gouache texture, "
                    "and rich warm ochre-and-umber colour harmony with warm golden highlights and deep sepia-umber shadows (4 to 6 painted value groups per "
                    f"material, no cold blue-grey cast). {face_q}"
                    "Warm even overhead daylight equal on both sides. Plain flat solid dark slate-grey background, RGB 48,52,59, no floor or border.\n\n"
                    f"Subject and composition: {brief.rstrip('.')}. {pose_text}"
                )
                return qwen_p, krea_p
            qwen_p = (
                f"{task_line}{input_line}"
                f"Subject and composition: {brief.rstrip('.')}. {pose_text}\n\n"
                "Style: ART DIRECTION — VAGRANT STORY & OLD TABLETOP DARK FANTASY PEN-AND-INK CROSSHATCHING WITH MUTED WASH\n\n"
                "Draw the character as an accomplished pen-and-ink dark-fantasy character sheet in the tradition of Akihiko Yoshida (Vagrant Story) and classic "
                "tabletop RPG manuals, finished with opaque muted gouache washes. Build a confident underlying ink drawing with crisp costume seams, "
                "dark-umber contours, dense fine directional crosshatching inside cloth folds and shadows, weathered dry-brush fabric grain, and opaque local colour "
                "washes without glossy 3D shading or smooth digital cel gradients.\n\n"
                "Presentation: Full-length isolated figure filling ~88% of the vertical 832x1216 portrait frame on a perfectly flat solid dark slate grey "
                "background, RGB 48,52,59. Soft overhead light equal on both sides. No floor, no cast shadow, no border."
            )
            krea_p = (
                "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
                "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character sheet: confident dark-umber pen-and-ink contour lines, "
                "fine crosshatching inside cloth folds and overhangs equally on both sides, weathered dry-brush texture and opaque matte gouache colour planes "
                f"(4 to 6 painted value groups per material). {face_q}"
                "Plain flat dark slate-grey background, RGB 48,52,59, no floor or border.\n\n"
                f"Subject and composition: {brief.rstrip('.')}. {pose_text}"
            )
            return qwen_p, krea_p
        ref_intro = (
            "using Image 1 ONLY for the character's facial likeness, hair, skin tone, age, eyes and head/collar cues. "
            if portrait_only
            else "using Image 1 for costume, colour-blocking, face and character identity cues while strictly following the pose-lock control skeleton. "
        )
        qwen_p = (
            f"Task: Paint a full-length high-resolution dark-fantasy character concept sheet {ref_intro}"
            "Keep the clothing cleanly tailored and uncluttered with natural drape.\n\n"
            "CRITICAL POSE LOCK: Follow the exact body proportions, arm spread, elbow bend, wrist angle, relaxed palm-inward thumb-forward gently curled hands, "
            f"and hip-width parallel forward-pointing feet—do NOT open the palms toward the camera, do NOT spread the fingers, and do NOT bring the feet together. {face_q}\n\n"
            f"Subject: {brief.rstrip('.')}. {pose_text}\n\n"
            "Style: ART DIRECTION — VAGRANT STORY & OLD TABLETOP DARK FANTASY PEN-AND-INK CROSSHATCHING WITH MUTED WASH\n\n"
            "Draw the character as an accomplished pen-and-ink dark-fantasy character sheet in the tradition of Akihiko Yoshida (Vagrant Story) and classic "
            "tabletop RPG manuals, finished with opaque muted gouache washes. Confident dark-umber ink contours, fine directional crosshatching inside folds, "
            "opaque local colour washes without glossy 3D shading.\n\n"
            "Presentation: Full-length isolated standing figure on a perfectly flat solid dark slate grey background, RGB 48,52,59. Soft overhead light equal on both sides. No floor, no cast shadow, no border."
        )
        krea_p = (
            "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
            "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game character sheet: confident dark-umber pen-and-ink contour lines, "
            "fine crosshatching only inside cloth folds and overhangs equally on both sides, and opaque matte gouache colour planes (4 to 6 painted value groups per material). "
            f"{face_q}"
            "Soft even overhead light equal on both sides. Plain flat solid dark slate-grey background, RGB 48,52,59, no floor, no cast shadow, no scenery, no border.\n\n"
            f"Subject and pose: Full-length isolated character from head to feet. {brief.rstrip('.')}. {pose_text}"
        )
        return qwen_p, krea_p
    if style_only and portrait_only:
        qwen_p = (
            "Task: Paint a full-body high-resolution dark-fantasy character illustration showing the character from the front, "
            "head to toe, for the person whose face is shown in the small classic RPG portrait reference. Reconstruct natural "
            "high-resolution anatomy and costume detail; do not enlarge or trace the pixel blocks, and do not crop to a bust or close-up portrait.\n\n"
            "Input image: facial likeness, hair, skin tone, age and ornaments ONLY. Preserve this character's facial likeness, hair, "
            "skin tone and age while clearly incorporating any extra facial details described in the brief, and paint their complete "
            f"full-body figure from the front, head to toe. Keep the clothing cleanly tailored and uncluttered, with a clear silhouette "
            f"and at most one or two restrained personal details. {face_q}\n\n"
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
        if show_both_arms:
            ref_line = (
                "Input image: costume, proportions, props and colour-blocking reference. Faithfully preserve the classic "
                "sprite's costume, face, hair, colours, held objects and natural static character poise, while opening the "
                "shoulders slightly toward the viewer so BOTH arms and BOTH hands are clearly visible in a natural relaxed "
                f"standing pose (do not hide an arm behind the torso, and do not force a stiff turnaround A-pose). {face_q}\n\n"
            )
            krea_tail = (
                "Faithfully preserve the costume, colours, props and natural character poise of the input image, "
                "with BOTH arms and BOTH hands clearly visible."
            )
        else:
            ref_line = (
                "Input image: exact composition, pose, silhouette, props and colour-blocking reference. Faithfully preserve the "
                "classic sprite's exact pose, gesture, proportions, silhouette, held objects, companions and major colour regions—"
                f"do not change the pose into a turnaround stance or remove elements. {face_q}\n\n"
            )
            krea_tail = "Faithfully preserve the pose, composition, props and silhouette of the input image."
        qwen_p = (
            "Task: Redraw a very small classic fantasy RPG sprite as newly illustrated high resolution art. "
            "Reconstruct plausible missing detail; do not enlarge or trace the pixel blocks.\n\n"
            f"{ref_line}"
            f"Subject: {brief.rstrip('.')}.\n\n"
            "Style: ART DIRECTION — VAGRANT STORY & OLD TABLETOP DARK FANTASY PEN-AND-INK CROSSHATCHING WITH MUTED WASH\n\n"
            "Draw the subject as an accomplished pen-and-ink dark-fantasy illustration in the tradition of Akihiko Yoshida "
            "(Vagrant Story) and classic tabletop RPG manuals, finished with opaque muted gouache washes. Build a confident underlying "
            "ink drawing with crisp seams, dark-umber contours, fine directional crosshatching inside folds and shadows, and opaque "
            "local colour washes preserving the sprite's exact colour relationships without glossy 3D shading.\n\n"
            "Presentation: Isolated full-length subject matching the classic sprite's framing on a perfectly flat solid dark "
            "slate grey background, RGB 48,52,59. No floor, no scenery, no cast shadow, no border."
        )
        krea_p = (
            "Painting style of Larry Elmore, Brom style, gothic fantasy illustration, dark_fantasy_illustration.\n\n"
            "Drawn and painted as a vivid, punchy 1990s PC fantasy role-playing game illustration: confident dark-umber pen-and-ink "
            "contour lines, fine crosshatching inside folds and shadows, and opaque matte gouache colour planes (4 to 6 painted "
            f"value groups per material). {face_q}"
            "Plain flat dark slate-grey background, RGB 48,52,59, no floor, no scenery, no border.\n\n"
            f"Subject: {brief.rstrip('.')}. {krea_tail}"
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
    ap.add_argument("--show-both-arms", action="store_true", help="With --style-only, open 3/4 sprites slightly toward the front so both arms and hands are clearly visible for splat/back generation")
    ap.add_argument("--portrait-only", action="store_true", help="Anchor face/hair/identity on a dialogue portrait while generating a full-length wide-A turnaround body from the brief")
    ap.add_argument("--portrait-trim", type=int, default=3, help="Pixels to trim off each edge of a classic framed portrait (default 3)")
    ap.add_argument("--pose-lock", action="store_true", help="Canonical X Bot rest-pose lock (default: universal DWPose stick figure + built-in #30343b slate lock)")
    ap.add_argument("--pose-mode", choices=["stick", "depth-female", "depth-male"], default="stick", help="With --pose-lock: stick (universal DWPose, zero proportion bias; default), depth-female (X Bot depth), or depth-male (Y Bot depth)")
    ap.add_argument("--style-variant", choices=["clean", "crunchy", "crunchy-warm"], default="clean", help="With --pose-lock: clean (smoother ink-gouache, end=0.95, d=0.38), crunchy (cool tabletop-manual crosshatch/dry-brush matching original 128, end=0.60, d=0.46), or crunchy-warm (warm ochre-and-umber book-cover gouache matching static NPCs, end=0.60, d=0.42)")
    ap.add_argument("--pose-garment", choices=["auto", "open", "draped"], default="auto", help="With --pose-lock, leg prompt phrasing (auto detects robes/dresses/gowns)")
    ap.add_argument("--pose-control", type=Path, default=None, help="Override pose control map path for --pose-lock")
    ap.add_argument("--pose-slate", type=Path, default=POSE_LOCK_DIR / "slate-bg-832x1216.png", help="Slate background image for --pose-lock built-in border lock")
    ap.add_argument("--pose-mask", type=Path, default=None, help="Override dilated figure regenerate mask path for --pose-lock")
    ap.add_argument("--pose-s1-strength", type=float, default=None, help="Stage 1 Fun ControlNet Union strength (defaults: stick=1.00, depth-female=0.95, depth-male=0.90)")
    ap.add_argument("--pose-s1-end", type=float, default=None, help="Stage 1 Fun ControlNet Union end_percent (defaults: stick clean=0.95 / crunchy*=0.60, depth-female=0.90, depth-male=0.75)")
    ap.add_argument("--pose-s2-strength", type=float, default=None, help="Stage 2 Krea2 Depth Control-LoRA strength (defaults: stick=0.0, depth-female=0.70, depth-male=0.45)")
    ap.add_argument("--pose-s2-end", type=float, default=None, help="Stage 2 Krea2 Depth Control-LoRA end_percent (defaults: stick=1.0, depth-female=0.75, depth-male=0.65)")
    ap.add_argument("--seeds", default="42201,42202,42203")
    ap.add_argument("--denoise", type=float, default=None, help="Stage 2 Krea2 New B denoise (default 0.46 crunchy, 0.42 crunchy-warm, 0.40 depth-female, else 0.38)")
    ap.add_argument("--width", type=int, default=832)
    ap.add_argument("--height", type=int, default=1216)
    ap.add_argument("--out", type=Path, default=None)
    ap.add_argument("--comfy", default="http://192.168.1.20:8188")
    args = ap.parse_args()
    pose_presets = {
        "stick":        ("dwpose-832x1216.png",     "rusty-poselock-dwpose.png",     "xbot-regen-mask-832x1216.png", "rusty-poselock-regen-mask.png",      1.00, 0.95, 0.00, 1.00, 0.38),
        "depth-female": ("xbot-depth-832x1216.png", "rusty-poselock-xbot-depth.png", "xbot-regen-mask-832x1216.png", "rusty-poselock-regen-mask.png",      0.95, 0.90, 0.70, 0.75, 0.40),
        "depth-male":   ("ybot-depth-832x1216.png", "rusty-poselock-ybot-depth.png", "ybot-regen-mask-832x1216.png", "rusty-poselock-ybot-regen-mask.png", 0.90, 0.75, 0.45, 0.65, 0.38),
    }
    p_ctrl_fn, p_ctrl_rem, p_mask_fn, p_mask_rem, d_s1, d_e1, d_s2, d_e2, d_den = pose_presets[args.pose_mode]
    if args.pose_lock and args.style_variant in ("crunchy", "crunchy-warm"):
        if args.pose_mode == "stick":
            d_e1 = 0.60
        d_den = 0.42 if args.style_variant == "crunchy-warm" else 0.46
    if args.pose_control is None: args.pose_control = POSE_LOCK_DIR / p_ctrl_fn
    if args.pose_mask is None: args.pose_mask = POSE_LOCK_DIR / p_mask_fn
    if args.pose_s1_strength is None: args.pose_s1_strength = d_s1
    if args.pose_s1_end is None: args.pose_s1_end = d_e1
    if args.pose_s2_strength is None: args.pose_s2_strength = d_s2
    if args.pose_s2_end is None: args.pose_s2_end = d_e2
    if args.denoise is None:
        args.denoise = d_den if args.pose_lock else 0.38

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

    qwen_p, krea_p = build_prompts(
        brief, pronoun, args.hooded_shadow,
        style_only=args.style_only, portrait_only=portrait_only, show_both_arms=args.show_both_arms,
        pose_lock=args.pose_lock, pose_garment=args.pose_garment, style_variant=args.style_variant,
    )
    wf2 = json.loads(WF_TEMPLATE.read_text())

    s1_keys = ["1_load_sprite", "2_qwen_unet", "3_qwen_clip", "4_qwen_vae", "5_qwen_cache",
               "6_qwen_prompt", "7_qwen_latent", "8_qwen_sampler", "9_qwen_decode", "10_save_stage1"]
    s1_tpl = {k: json.loads(json.dumps(wf2[k])) for k in s1_keys}
    s1_tpl["7_qwen_latent"]["inputs"]["width"] = args.width
    s1_tpl["7_qwen_latent"]["inputs"]["height"] = args.height

    s2_keys = ["11_krea_unet", "12_krea_clip", "13_krea_vae", "14_lora_brom", "15_lora_elmore", "16_lora_retroink",
               "17_krea_encode", "18_krea_prompt", "19_krea_zero_neg", "20_krea_sampler", "21_krea_decode", "22_save_final"]
    s2_tpl = {k: json.loads(json.dumps(wf2[k])) for k in s2_keys}
    s2_tpl["0_load_s1"] = {"class_type": "LoadImage", "inputs": {"image": "stage1.png"}}
    s2_tpl["17_krea_encode"]["inputs"]["pixels"] = ["0_load_s1", 0]

    if args.pose_lock:
        up_ctrl = comfy_upload(args.comfy, args.pose_control.resolve(), p_ctrl_rem)
        up_slate = comfy_upload(args.comfy, args.pose_slate.resolve(), "rusty-poselock-slate-bg.png")
        up_mask = comfy_upload(args.comfy, args.pose_mask.resolve(), p_mask_rem)
        s1_tpl["2b_patch"] = {"class_type": "ModelPatchLoader", "inputs": {"name": "qwen_image_2.1_fun_controlnet_union_int8_convrot.safetensors"}}
        s1_tpl["2c_ctrl"] = {"class_type": "LoadImage", "inputs": {"image": up_ctrl}}
        s1_tpl["2e_slate"] = {"class_type": "LoadImage", "inputs": {"image": up_slate}}
        s1_tpl["2f_mask_im"] = {"class_type": "LoadImage", "inputs": {"image": up_mask}}
        s1_tpl["2g_mask"] = {"class_type": "ImageToMask", "inputs": {"image": ["2f_mask_im", 0], "channel": "red"}}
        s1_tpl["2d_cn"] = {
            "class_type": "ZImageFunControlnet",
            "inputs": {
                "model": ["5_qwen_cache", 0],
                "model_patch": ["2b_patch", 0],
                "vae": ["4_qwen_vae", 0],
                "image": ["2c_ctrl", 0],
                "inpaint_image": ["2e_slate", 0],
                "mask": ["2g_mask", 0],
                "strength": args.pose_s1_strength,
                "start_percent": 0.0,
                "end_percent": args.pose_s1_end,
            },
        }
        s1_tpl["8_qwen_sampler"]["inputs"]["model"] = ["2d_cn", 0]
        if args.pose_s2_strength > 0:
            s2_tpl["16b_depth"] = {"class_type": "LoadImage", "inputs": {"image": up_ctrl}}
            s2_tpl["16c_enc"] = {"class_type": "Krea2ControlPlusImageEncode", "inputs": {
                "control_image": ["16b_depth", 0], "vae": ["13_krea_vae", 0], "latent": ["17_krea_encode", 0],
                "resize": "match_latent_size", "upscale_method": "lanczos", "crop": "center",
                "channel_mode": "grayscale", "normalize": "per_image_minmax", "invert": False, "batch_mode": "independent_images",
            }}
            s2_tpl["16d_lora"] = {"class_type": "Krea2ControlPlusLoRALoader", "inputs": {
                "model": ["16_lora_retroink", 0], "lora_name": "krea2_depth_control_lora.safetensors",
                "strength": args.pose_s2_strength, "start_percent": 0.0, "end_percent": args.pose_s2_end,
            }}
            s2_tpl["16e_apply"] = {"class_type": "Krea2ControlPlusApply", "inputs": {"model": ["16d_lora", 0], "control_latent": ["16c_enc", 0]}}
            s2_tpl["20_krea_sampler"]["inputs"]["model"] = ["16e_apply", 0]

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
