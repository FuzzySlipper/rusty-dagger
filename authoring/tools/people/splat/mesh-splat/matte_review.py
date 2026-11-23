#!/usr/bin/env python3
"""matte_review.py BAKE_DIR [--clips NAME:FIRST-LAST,...] [--every 6] [--fps 24]: a glance at the cut-out frames (Patch, 2026-11-15: "put
the post-cutout frames in a review round just so I can glance and make sure nothing is going awry there"). Not a gate.

Reads BAKE_DIR/frames (the clip, in order) and BAKE_DIR/orbit (orbit_views.py's views/NNN.png RGBA and holes.json). Per clip (default one
clip named after clip.txt; multi-clip bakes pass their frame ranges) writes BAKE_DIR/review/mattes/NAME/:
  mattes.mp4  every matted frame, side by side: KEPT = the cut-out over magenta (background wrongly kept shows grey), CUT = the frame with
              the kept figure painted magenta (cloth that was eaten shows in its own colour); frame-counted after encoding
  sheet.png   every EVERY-th frame's pair at full resolution (cropped to the figure, so a 10 px bite is 10 px), each labelled with the
              frame, pixels removed by --open-holes, holes kept (count / total px) and EDGE when the matte touches the frame's border
  numbers.json  the same per frame for every frame (per-frame edge sides), plus flags: how many frames touch each edge, frames whose matte area jumps by more
              than 4% against both neighbours (a bite or a filled gap shows as a jump)
Prints one JSON summary line per clip."""
import argparse, json, subprocess, tempfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ap = argparse.ArgumentParser(); ap.add_argument('bake', type=Path); ap.add_argument('--clips', default=''); ap.add_argument('--every', type=int, default=6)
ap.add_argument('--fps', type=int, default=24); a = ap.parse_args()
MAG = np.array([255, 0, 255], np.float32)
views = sorted((a.bake / 'orbit/views').glob('*.png')); frames = sorted((a.bake / 'frames').glob('*.png'))
holes = json.loads((a.bake / 'orbit/holes.json').read_text()) if (a.bake / 'orbit/holes.json').exists() else {}
name0 = Path((a.bake / 'clip.txt').read_text().strip()).name if (a.bake / 'clip.txt').exists() else 'clip'
clips = [(c.split(':')[0], *map(int, c.split(':')[1].split('-'))) for c in a.clips.split(',') if c] or [(name0, 0, len(views) - 1)]
try: font = ImageFont.truetype('DejaVuSans-Bold.ttf', 22)
except OSError: font = ImageFont.load_default()

def pair(i):
    v = np.asarray(Image.open(views[i]).convert('RGBA')).astype(np.float32); al = v[..., 3:] / 255
    f = np.asarray(Image.open(frames[i]).convert('RGB').resize(v.shape[1::-1])).astype(np.float32)
    kept = v[..., :3] * al + MAG * (1 - al); cut = f * (1 - al) + MAG * al
    m = al[..., 0] > 0.5; edge = [s for s, e in (('top', m[0].any()), ('bottom', m[-1].any()), ('left', m[:, 0].any()), ('right', m[:, -1].any())) if e]
    return np.concatenate([kept, cut], 1).astype(np.uint8), m, edge

for name, lo, hi in clips:
    out = a.bake / 'review/mattes' / name; out.mkdir(parents=True, exist_ok=True); rows = []; tiles = []; areas = []
    with tempfile.TemporaryDirectory() as tmp:
        for k, i in enumerate(range(lo, hi + 1)):
            img, m, edge = pair(i); h = holes.get(f'{i:03d}', {}); kept = h.get('kept', [])
            row = {'frame': i, 'removed_px': h.get('removed', 0), 'holes_kept': len(kept), 'holes_kept_px': int(sum(x['area'] for x in kept)),
                   'matte_px': int(m.sum()), 'edge': edge}; rows.append(row); areas.append(m.sum())
            Image.fromarray(img).save(f'{tmp}/{k:04d}.png')
            if k % a.every == 0:
                ys, xs = np.nonzero(m); W = m.shape[1]
                y0, y1 = max(ys.min() - 16, 0), min(ys.max() + 16, m.shape[0]); x0, x1 = max(xs.min() - 16, 0), min(xs.max() + 16, W)
                t = Image.fromarray(np.concatenate([img[y0:y1, x0:x1], np.full((y1 - y0, 8, 3), 255, np.uint8), img[y0:y1, W + x0:W + x1]], 1))
                tw_ = max(t.width, 560); lab = Image.new('RGB', (tw_, 100), (255, 255, 255)); d = ImageDraw.Draw(lab)
                d.text((8, 2), f"{name} frame {i}", fill=(0, 0, 0), font=font)
                d.text((8, 34), f"removed {row['removed_px']} px | holes kept {row['holes_kept']} / {row['holes_kept_px']} px", fill=(0, 0, 0), font=font)
                d.text((8, 66), 'KEPT on magenta | CUT' + (f"   EDGE: {','.join(edge)}" if edge else ''), fill=(200, 0, 0) if edge else (90, 90, 90), font=font)
                tt = Image.new('RGB', (tw_, t.height + 100), (255, 255, 255)); tt.paste(lab, (0, 0)); tt.paste(t, (0, 100)); tiles.append(tt)
        n = hi - lo + 1
        subprocess.run(['ffmpeg', '-loglevel', 'error', '-y', '-framerate', str(a.fps), '-i', f'{tmp}/%04d.png', '-c:v', 'libx264', '-crf', '16',
                        '-pix_fmt', 'yuv420p', '-vf', 'scale=trunc(iw/2)*2:trunc(ih/2)*2', str(out / 'mattes.mp4')], check=True)
    got = int(subprocess.run(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v', '-show_entries', 'stream=nb_read_frames', '-of',
                              'csv=p=0', str(out / 'mattes.mp4')], capture_output=True, text=True).stdout.strip())
    cols = 4; tw = max(t.width for t in tiles); th = max(t.height for t in tiles); sheet = Image.new('RGB', (cols * tw, -(-len(tiles) // cols) * th), (255, 255, 255))
    for k, t in enumerate(tiles): sheet.paste(t, ((k % cols) * tw, (k // cols) * th))
    sheet.save(out / 'sheet.png')
    ar = np.array(areas, float); jump = [rows[k]['frame'] for k in range(1, len(ar) - 1)
                                          if abs(ar[k] / ar[k - 1] - 1) > 0.04 and abs(ar[k] / ar[k + 1] - 1) > 0.04 and np.sign(ar[k] - ar[k - 1]) == np.sign(ar[k] - ar[k + 1])]
    summary = {'clip': name, 'frames': n, 'video_frames': got, 'video_frames_ok': got == n, 'sheet_tiles': len(tiles),
               'frames_with_removed': sum(r['removed_px'] > 0 for r in rows), 'removed_px_max': max(r['removed_px'] for r in rows),
               'edge_frames': len([r for r in rows if r['edge']]), 'edge_sides': {k: sum(k in r['edge'] for r in rows) for k in ('top', 'bottom', 'left', 'right') if any(k in r['edge'] for r in rows)}, 'area_jump_frames': jump, 'folder': str(out)}
    (out / 'numbers.json').write_text(json.dumps({'summary': summary, 'frames': rows}, indent=1) + '\n'); print(json.dumps(summary))
