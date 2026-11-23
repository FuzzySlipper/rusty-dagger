"""dagger-char review ID STAGE: put a stage in front of Patch as a Workbench review batch and record the batch in the manifest.

08-final (his look before 09-game): the levelled final (08-final/final-level/splat.npz, written by level_splat.py) is rendered on the
GPU lab by loop_review.py (views, steep and close-ups; one lab lock, asset-resources run), the close-ups are cut into one image each,
the old-against-new sheet is added where the bake made one, and `workbench review create --request` makes the batch, whose description
lists the known faults. The stage becomes needs-Patch, waiting since now; `dagger-char approve` later reads his marks back."""
import json, os, re, shlex, subprocess, tempfile
from pathlib import Path
import dagger_char as dc

LAB = Path('~/server-access/gpu-lab/connect').expanduser()
PY = '/workspace/splat-probe/venv/bin/python'
DETAILS = [('face', 'Face:0,0.39,0.06:0:5:6'), ('face-above', 'Face-above:0,0.39,0.04:0:40:8'), ('hand-right', 'Hand-r:-0.17,0.0,0.02:-20:0:6'),
           ('hand-left', 'Hand-l:0.17,0.0,0.02:20:0:6'), ('eye-level-front', 'Eye-az0:0,0,0:0:0:26'), ('eye-level-side', 'Eye-az90:0,0,0:90:0:26'),
           ('eye-level-back', 'Eye-az180:0,0,0:180:0:26'), ('crown', 'Crown:0,0.44,0:0:88:12'), ('under-hem', 'Under-hem:0,-0.2,0:0:-70:18'),
           ('soles', 'Soles:0,-0.49,0:20:-80:14')]
DEFAULT_DETAILS = ['head', 'hand', 'feet', 'rear-hem']   # loop_review.py's own close-ups come first in details.png


def sh(cmd, **kw):
    return subprocess.run(cmd, shell=True, text=True, capture_output=True, **kw)


def render_final(cid):
    """Levelled review sheets of the final, from the lab, into 08-final/review-level/."""
    d = dc.char_dir(cid) / '08-final'; sp = d / 'final-level/splat.npz'
    if not sp.exists():
        raise SystemExit(f'{cid}: no levelled final ({sp}); level_splat.py first')
    lab = f'/workspace/dagger-review-{cid}'; det = ' '.join(f'--detail {x}' for _, x in DETAILS)
    with tempfile.TemporaryDirectory() as t:
        os.makedirs(f'{t}/final'); os.link(sp, f'{t}/final/splat.npz') if os.stat(sp).st_dev == os.stat(t).st_dev else sh(f'cp {sp} {t}/final/')
        sh(f'cp {dc.REPO}/tools/solv3r-mage/loop_review.py {t}/')
        run = (f"{LAB} 'gpu-lab exec bash -c \"rm -rf {lab}; mkdir -p {lab}\"' && tar czf - -C {t} . | {LAB} 'gpu-lab exec tar xzf - -C {lab}' && "
               f"asset-resources run --host gpu-lab --resource lab-gsplat --owner claude --purpose 'dagger-char review {cid} 08-final (levelled sheets)' -- "
               f"{LAB} \"gpu-lab exec bash -c 'cd {lab} && {PY} loop_review.py review \\\"Final (levelled)=final\\\" --orbit 72 --orbit-swing 40 {det}'\" && "
               f"{LAB} 'gpu-lab exec tar czf - -C {lab} review' | tar xzf - -C {t} && {LAB} 'gpu-lab exec rm -rf {lab}'")
        r = sh(f"flock -w 7200 /tmp/gpu-lab-claude.lock bash -c {shlex.quote(run)}")
        if not Path(f'{t}/review/views.png').exists():
            raise SystemExit(f'{cid}: the lab review made no sheets\n{r.stdout[-800:]}\n{r.stderr[-800:]}')
        out = d / 'review-level'; out.mkdir(exist_ok=True)
        sh(f'cp -r {t}/review/. {out}/')
    return out


def cut_details(src, out):
    """details.png (one row, 1080-px columns: loop_review's four, then DETAILS) -> one image per close-up."""
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    im = Image.open(src); names = DEFAULT_DETAILS + [n for n, _ in DETAILS]; w = im.height
    for i, n in enumerate(names):
        if (i + 1) * w <= im.width:
            im.crop((i * w, 0, (i + 1) * w, w)).save(out / f'{i + 1:02d}-close-{n}.png')


def review_final(cid):
    m = dc.load(cid); d = dc.char_dir(cid) / '08-final'; lean = (m['stages']['08-final'].get('checks') or {}).get('frame_lean_deg')
    rl = d / 'review-level'
    if not (rl / 'views.png').exists():
        rl = render_final(cid)
    b = d / 'review-batch'; b.mkdir(exist_ok=True)
    for f in b.glob('*'):
        f.unlink()
    os.link(rl / 'views.png', b / '00-views.png'); os.link(rl / 'steep.png', b / '00-steep.png')
    cut_details(rl / 'details.png', b)
    old = next(iter(sorted((d / 'review-vs-old').glob('*.png'))), None) if (d / 'review-vs-old').exists() else None
    if old:
        os.link(old, b / f'90-old-against-new-{old.name}')
    faults = [f'the fit frame leaned {lean} deg before levelling (these sheets are levelled)' if lean is not None else None,
              'glitter / noise inside the hem seen from below (a known limit of the recipe)',
              'eye level a little softer after the repaint round than after round 0 (seen on every accepted bake)']
    desc = (f"{cid} ({m['name']}): the final splat, levelled, before it goes on to the game asset (distil, rig). Mark Good to send it on, "
            f"Bad to hold it, with a note for what to change. Known faults: " + '; '.join(x for x in faults if x) + '.')
    req = {'title': f'{cid} final splat: look before the game asset', 'description': desc, 'dir': str(b)}
    with tempfile.NamedTemporaryFile('w', suffix='.json', delete=False) as f:
        json.dump(req, f)
    r = sh(f'workbench review create --request {f.name}'); os.unlink(f.name)
    try:
        receipt = json.loads(r.stdout[r.stdout.index('{'):])
    except ValueError:
        raise SystemExit(f'{cid}: workbench review create failed\n{r.stdout}\n{r.stderr}')
    bid = receipt.get('id') or receipt.get('batch', {}).get('id'); url = receipt.get('url')
    st = m['stages']['08-final']; st.setdefault('review', {})['batch'] = {'id': bid, 'url': url, 'created': dc.NOW(), 'files': sorted(p.name for p in b.iterdir())}
    dc.set_state(m, '08-final', 'needs-Patch', f'in front of Patch: {url}', by='dagger-char review', why=f'Workbench {bid}, waiting on his mark',
                 waiting_since=dc.NOW())
    dc.save(m)
    return bid, url


def render_faces(cid):
    """Each tier's face (and hands) at 1080 px against the levelled 400k it was distilled from: loop_review.py on the lab with the
    four splats as rows (the distils are in the levelled frame, so the same cameras), close-ups cut to one image per row."""
    d = dc.char_dir(cid); w = d / '09-game/wip'; out = d / '09-game/evidence/faces'; out.mkdir(parents=True, exist_ok=True)
    items = [('400k-final', d / '08-final/final-level/splat.npz')] + [(t, w / f'fit{t}/splat.npz') for t in ('96k', '48k', '16k') if (w / f'fit{t}/splat.npz').exists()]
    lab = f'/workspace/dagger-faces-{cid}'
    import numpy as np   # targets measured on the levelled 400k: face 0.07 below the top of the head, hands at the widest points
    z = np.load(items[0][1]); P = z['means'][1 / (1 + np.exp(-z['opacities'].reshape(-1))) > 0.3]; top = P[:, 1].max()
    hd = P[P[:, 1] > top - 0.12]; fx, fz = np.median(hd[:, 0]), np.median(hd[:, 2]); fy = top - 0.07
    mid = P[(P[:, 1] > -0.25) & (P[:, 1] < 0.25)]
    hr = mid[mid[:, 0] < np.percentile(mid[:, 0], 0.5)].mean(0); hl = mid[mid[:, 0] > np.percentile(mid[:, 0], 99.5)].mean(0)
    f3 = lambda v: ','.join(f'{x:.3f}' for x in v)
    det = (f'--detail Face:{f3((fx, fy, fz))}:0:5:6 --detail Face-quarter:{f3((fx, fy, fz))}:35:5:6 '
           f'--detail Hand-r:{f3(hr)}:-20:0:6 --detail Hand-l:{f3(hl)}:20:0:6')
    labels = ' '.join(f'{n}={n}' for n, _ in items)   # plain names (no spaces), so no quoting through the two shells
    with tempfile.TemporaryDirectory() as t:
        for n, p in items:
            os.makedirs(f'{t}/{n}'); sh(f'cp {p} {t}/{n}/splat.npz')
        sh(f'cp {dc.REPO}/tools/solv3r-mage/loop_review.py {t}/')
        run = (f"{LAB} 'gpu-lab exec bash -c \"rm -rf {lab}; mkdir -p {lab}\"' && tar czf - -C {t} . | {LAB} 'gpu-lab exec tar xzf - -C {lab}' && "
               f"asset-resources run --host gpu-lab --resource lab-gsplat --owner claude --purpose 'dagger-char {cid} 09-game faces per tier' -- "
               f"{LAB} \"gpu-lab exec bash -c 'cd {lab} && {PY} loop_review.py review {labels} {det}'\" && "
               f"{LAB} 'gpu-lab exec tar czf - -C {lab} review' | tar xzf - -C {t} && {LAB} 'gpu-lab exec rm -rf {lab}'")
        r = sh(f"flock -w 14400 /tmp/gpu-lab-claude.lock bash -c {shlex.quote(run)}")
        with open(w / 'faces-lab.log', 'a') as f:
            f.write(f'== {dc.NOW()} rc {r.returncode}\n{r.stdout[-4000:]}\n{r.stderr[-4000:]}\n')
        if not Path(f'{t}/review/details.png').exists():
            raise SystemExit(f'{cid}: no face sheet from the lab (09-game/wip/faces-lab.log)')
        from PIL import Image
        Image.MAX_IMAGE_PIXELS = None
        im = Image.open(f'{t}/review/details.png'); h = 1080; names = ['head', 'hand', 'feet', 'rear-hem', 'face', 'face-quarter', 'hand-right', 'hand-left']
        for r, (n, _) in enumerate(items):
            for c, nm in enumerate(names):
                if nm in ('face', 'face-quarter', 'hand-right', 'hand-left') and (c + 1) * h <= im.width:
                    im.crop((c * h, r * h, (c + 1) * h, (r + 1) * h)).save(out / f'{nm}-{n}.png')
        sh(f'cp {t}/review/details.png {out}/details-all-tiers.png')
    return out


# Faults seen in the evidence and left as they are, in the batch's own words (the lead's reading of 386-C, 2026-11-15).
KNOWN_FAULTS = {
    '386-C': [
        ("The bright patch at the knee during the walk is not his painted hose: it is the under-hem junk from the bake, carried out into view "
         "when the thigh swings. One rig-time try (humanoid_splat_rig.py --under-hem-clean: 2,865 leg Gaussians above the hem at rest recoloured "
         "to the hose's median or dropped) made no visible difference, because most of the bright junk sits below the hem, at and inside the "
         "boot cuff; it was left. Worst phases before and after at full size.",
         ['under-hem-quarter-f022-before-after.png', 'under-hem-side-f016-before-after.png']),
        ("Armpits at rest: pale and teal shards on both sides, front and back (close-up at 2x).", ['armpits-rest.png']),
        ("Torn specks over one walk cycle as the hem check counts them (above), and one or two tiny red specks near the hands.", []),
    ],
    '387-A': [
        ("Under the kilt hem, the tops of his thighs show pale, bright wisps when a leg swings forward: bake material from under the hem, "
         "as on 386-C (worst at frame 22 of the walk video, full frame).", ['thigh-tops-walk-f022.png']),
        ("Sleeve ends at rest: pale and green shards where the short sleeves meet the upper arms, both sides, front and back (close-up at 2x).",
         ['armpits-rest.png']),
        ("The crown of his head is cut flat, an open rim of hair seen from above, in the final itself and at every size (faces, 04).", []),
    ],
    '383-A': [
        ("Under both sleeves at rest: blue and white shards where the sleeve meets the arm, front and back (close-up at 2x).", ['armpits-rest.png']),
        ("Small dark wisps float beside the tunic's sides at rest, a hand's width out (they were there before the hand fix too).", ['trim-sides-before-after.png']),
    ],
}


# What each size's face is like, in one line per tier, written after looking at faces-per-tier.png (the lead, 2026-11-15:
# name the fault so Patch does not have to find it).
FACE_NOTES = {
    '386-C': {'96k': 'as the final.', '48k': 'as the final, a touch softer.',
              '16k': 'soft, with a dark streak down the nose and lip and one eye smudged shut.'},
    '387-A': {'96k': 'as the final, the eyes a little less sharp.', '48k': 'softer, eyes and brow slightly blurred, still clearly him.',
              '16k': 'blurred: the eyes smudge to dark slits and the mouth is lost in the beard.'},
    '383-A': {'96k': 'as the final.', '48k': 'as the final, a touch softer, the eyes a little heavier.',
              '16k': 'soft and older-looking: a dark smear over his left eye, the mouth pulled down at one corner, the neck lacing lost.'},
}


# Faults found in this stage and fixed before the batch, with before and after (shown after the known faults).
FIXES = {
    '383-A': [
        ("Fixed: a clump of hem trim used to travel with each hand, hanging below it at rest and swinging with it in the walk. Hem trim beside "
         "his fingers had been attached to the hand bone because his painted hands hang against the trim; that is a source matter for the "
         "concept gate (the wide A-pose keeps hands off the body). The rig now finds every hand-weighted piece lying apart from the hand "
         "(humanoid_splat_rig.py --hand-clusters: 4-6 cm clear of it at every size) and gives it to the tunic: 235 Gaussians at the 48k, "
         "none from the hand itself, nothing dropped. The hands keep all their fingers and the trim closes at the tunic's sides without a "
         "lump (hands at 3x before and after, at rest and at walk frame 22; the trim before and after; where the pieces sat).",
         ['hands-3x-before-after.png', 'hand-walk-f022-after.png', 'trim-sides-before-after.png', 'hand-clusters-known-case.png']),
    ],
}


def review_game(cid):
    """One Workbench batch for the game asset (not one of Patch's gates; he asked to test with these): rest and idle sheet, held walk
    cycle sheet, the walk video, each tier's face at 1080 px against the 400k, and the check numbers in the description."""
    m = dc.load(cid); d = dc.char_dir(cid); e = d / '09-game/evidence'; ck = m['stages']['09-game'].get('checks', {})
    if not (e / 'faces').exists():
        render_faces(cid)
    b = d / '09-game/review-batch'; b.mkdir(exist_ok=True)
    for f in b.glob('*'):
        f.unlink()
    files = [(e / 'rest-sheet.png', '01-rest-and-idle.png'), (e / 'cycle-sheet.png', '02-walk-cycle-held.png'), (e / f'{cid}-walk-quarter.mp4', '03-walk.mp4')]
    from PIL import Image   # the tiers side by side at 1080 px each: face, quarter face (rows), 400k / 96k / 48k / 16k (columns)
    tiers = [n for n in ('400k-final', '96k', '48k', '16k') if (e / 'faces' / f'face-{n}.png').exists()]
    cmp_ = Image.new('RGB', (1080 * len(tiers), 2160))
    for c, n in enumerate(tiers):
        for r, k in enumerate(('face', 'face-quarter')):
            cmp_.paste(Image.open(e / 'faces' / f'{k}-{n}.png').convert('RGB'), (1080 * c, 1080 * r))
    cmp_.save(e / 'faces' / 'faces-per-tier.png'); files.append((e / 'faces' / 'faces-per-tier.png', '04-faces-per-tier.png'))
    for i, n in enumerate(tiers):
        files += [(e / 'faces' / f'face-{n}.png', f'1{i}-face-{n}.png')]
    faults = KNOWN_FAULTS.get(cid, [])
    for i, (_, imgs) in enumerate(faults):
        files += [(e / im, f'2{i}{chr(97 + j)}-fault-{im}') for j, im in enumerate(imgs)]
    fixes = FIXES.get(cid, [])
    for i, (_, imgs) in enumerate(fixes):
        files += [(e / im, f'3{i}{chr(97 + j)}-fixed-{im}') for j, im in enumerate(imgs)]
    for src, dst in files:
        if src.exists():
            os.link(src, b / dst)
    dist = ck.get('distil', {}); hem = ck.get('checks_48k', {}).get('hem_arms10-15', []); fit = ck.get('checks_48k', {}).get('clip-fit', [''])[0]
    walk = json.loads(fit[5:]) if fit.startswith('walk ') else {}
    gv = ck.get('glbs', {}).get('gltf_validator', [])
    nums = lambda line: [int(x) for x in re.findall(r'-?\d+', line.split('[', 1)[1].split(']', 1)[0])] if '[' in line else []
    hl = {k: next((h for h in hem if k in h), '') for k in ('legs out', 'hem torn', 'hands in')}
    legs = max(nums(hl['legs out']) or [0]); hands = max(nums(hl['hands in']) or [0])
    tf = nums(hl['hem torn']); tb = nums(hl['hem torn'].split('back', 1)[1]) if 'back' in hl['hem torn'] else []
    cloth = {'387-A': 'kilt'}.get(cid, 'tunic')
    gap = re.search(r'worst gap ([\d.]+) cm', hl['hem torn']); drift = walk.get('his', {})
    notes = FACE_NOTES.get(cid)
    if not notes:
        raise SystemExit(f'{cid}: write FACE_NOTES[{cid!r}] (one line per tier on what the face is like) after looking at the faces')
    sentences = [
        f"{cid} ({m['name']}) as a game character, for testing (Patch, 2026-11-15): rest pose and Standing Idle (01), one walk cycle held at eight "
        f"phases (02), a three-cycle walk video built from held frames (03), and the face at each size against the 400k final (04 side by side, 10-13).",
        "Sizes, scored on held-back views of the final: " + ', '.join(f"{t} {v.get('heldout_subject_psnr')} dB" for t, v in dist.items()) + '.',
        "Faces: " + ' '.join(f'{t}: {n}' for t, n in notes.items()),
        f"Walk: the feet stay planted (drift {drift.get('drift_end_cm_median')} cm typical, {drift.get('drift_end_cm_max')} cm at worst). "
        + (f'No leg comes through the {cloth}' if legs == 0 else f'Up to {legs} leg splats come through the {cloth}') + ' and '
        + ('no hand goes into it' if hands == 0 else f'up to {hands} hand splats go into it') + '. '
        + (f"About {max(tf or [0]) + max(tb or [0])} specks tear off the {cloth} as he walks ({max(tf or [0])} at the front, {max(tb or [0])} at the back), "
           f"the worst flying {float(gap.group(1)):.0f} cm." if gap and (max(tf or [0]) + max(tb or [0])) else f'Nothing tears off the {cloth}.'),
        f"Engine files: skinned GLBs at each size on both rigs, {len(gv)} files, all valid (glTF validator: "
        f"{sum(int(x.split(': ')[1].split(' ')[0]) for x in gv)} errors, {sum(int(x.split(', ')[1].split(' ')[0]) for x in gv)} warnings); not published.",
    ]
    if faults:
        sentences.append('Known faults: ' + ' '.join(f'({i + 1}) {t}' for i, (t, _) in enumerate(faults)))
    sentences += [t for t, _ in fixes]
    desc = ' '.join(sentences)
    req = {'title': f'{cid} game character: rest, walk and faces per tier', 'description': desc, 'dir': str(b)}
    with tempfile.NamedTemporaryFile('w', suffix='.json', delete=False) as f:
        json.dump(req, f)
    r = sh(f'workbench review create --request {f.name}'); os.unlink(f.name)
    receipt = json.loads(r.stdout[r.stdout.index('{'):])
    bid = receipt.get('id') or receipt.get('batch', {}).get('id'); url = receipt.get('url')

    def upd(m):
        st = m['stages']['09-game']; st['known_faults'] = [{'fault': t, 'images': [f'09-game/evidence/{i}' for i in im]} for t, im in faults]
        st.setdefault('review', {})['batch'] = {'id': bid, 'url': url, 'created': dc.NOW(), 'files': sorted(p.name for p in b.iterdir())}
        dc.set_state(m, '09-game', 'needs-Patch', f'in front of Patch for testing: {url}', by='dagger-char review', why=f'Workbench {bid} (testing, not a gate)',
                     waiting_since=dc.NOW())
    dc.patch(cid, upd)
    return bid, url


# Arms-down evidence (lead for Patch, 2026-11-15): what the shoulders do coming down out of the A-pose, per character, written after
# looking at shoulders-2.5x.png, the cycle sheet and the video.
ARMS_DOWN_NOTES = {}
WIDE_A = {'deg': 25, 'image': 'wide-A-front-383A-stB-s5-upper-arm.png',
          'what': "the wide A chosen for new fronts (383-A's stB seed 5 front, 2026-11-14) holds the upper arms about 25 deg out, measured by eye on the painting"}


def review_arms_down(cids=('386-C', '387-A', '383-A')):
    """One Workbench batch for the three: each character's arms-down walk (held phases from four views), its video, the idle with
    the arms down, and both shoulders at 2.5x where the arm is furthest forward, furthest back and straight down."""
    b = dc.ROOT / 'batches/arms-down-2026-11-15'; b.mkdir(parents=True, exist_ok=True)
    for f in b.glob('[0-9]*'):
        f.unlink()
    files = [(b / WIDE_A['image'], f"00-{WIDE_A['image']}")]; parts = []
    for i, cid in enumerate(cids):
        m = dc.load(cid); e = dc.char_dir(cid) / '09-game/evidence/arms-down'; ad = m['stages']['09-game']['checks']['arms_down']
        files += [(e / 'shoulders-2.5x.png', f'{i + 1}1-{cid}-shoulders-2.5x.png'), (e / 'cycle-sheet.png', f'{i + 1}2-{cid}-walk-arms-down-held.png'),
                  (e / f'{cid}-walk-arms-down-quarter.mp4', f'{i + 1}3-{cid}-walk-arms-down.mp4'), (e / 'idle-sheet.png', f'{i + 1}4-{cid}-idle-arms-down.png')]
        ua = ad['upper_arm_deg']; cs = ua['clips_sideways_min_max_forward_min_max']
        hem = ad['hem_check_arms_down']; hands = [int(x) for x in re.findall(r'-?\d+', next(h for h in hem if 'hands in' in h).split('[', 1)[1].split(']')[0])]
        deep = re.search(r'deepest ([\d.]+) cm', next(h for h in hem if 'hands in' in h)).group(1)
        parts.append(f"{cid} ({m['name']}): painted with the upper arms {ua['bake_sideways']['l']:.0f} and {ua['bake_sideways']['r']:.0f} deg out "
                     f"(left, right); the rig lifts them to the rest's {ua['rest_sideways']:.0f} and the arms-down walk brings them back to 4. "
                     f"Hands go into the tunic here, as expected with the offset off (up to {max(hands)} splats, {deep} cm deep). "
                     + ARMS_DOWN_NOTES.get(cid, ''))
    wa = cs   # the clip numbers are the same for every character (the clip sets the rotations)
    desc = ("Arms at the sides, to judge how the shoulders come down out of the A-pose (Patch, 2026-11-15). PINOC's library has no plain "
            "walk with the arms down: its Walking holds the upper arms " f"{wa['walk-authored']['l'][0]:.0f}-{wa['walk-authored']['l'][1]:.0f} deg out "
            "(about 18 through the middle of the swing) and our standard captures add 10 more. So these use a copy of the same Walking with "
            "only the upper arms turned in to 4 deg from vertical at every frame; the forward swing, forearms and legs are as authored "
            "(authoring/tools/people/splat/character/arms_down_clip.py). Standing Idle the same way (authored 9-14 deg out). The page's arm offset is off. "
            "Per character: both shoulders at 2.5x from front and back where the arm is furthest forward, furthest back and straight "
            "down (1), eight held walk phases from front, quarter, side and back (2), a 93-frame walk video from held frames (3), the idle "
            "(4). These three were painted with moderate A-pose arms, their upper arms close to the body; "
            f"{WIDE_A['what']} (00), so a wide-A character's shoulders have about {WIDE_A['deg']} deg further to come down than in its painting. "
            + ' '.join(parts))
    for src, dst in files:
        if src.exists():
            os.link(src, b / dst)
    req = {'title': 'Arms at the sides: shoulders out of the A-pose (386-C, 387-A, 383-A)', 'description': desc, 'dir': str(b)}
    with tempfile.NamedTemporaryFile('w', suffix='.json', delete=False) as f:
        json.dump(req, f)
    r = sh(f'workbench review create --request {f.name}'); os.unlink(f.name)
    receipt = json.loads(r.stdout[r.stdout.index('{'):]); bid = receipt.get('id') or receipt.get('batch', {}).get('id'); url = receipt.get('url')
    for cid in cids:
        dc.patch(cid, lambda m: m['stages']['09-game']['checks']['arms_down'].__setitem__('batch', {'id': bid, 'url': url, 'created': dc.NOW()}))
    return bid, url


# Mocap arms-down batch (lead for Patch, 2026-11-15): Mixamo's Walking at a lower Character Arm-Space, retargeted, per character.
MOCAP_NOTES = {}


def review_mocap(cids=('386-C', '387-A', '383-A'), source='as35-walking', also=('386-C', 'patch-walking')):
    import mocap
    b = dc.ROOT / 'batches/arms-down-2026-11-15'; b.mkdir(parents=True, exist_ok=True)
    for f in b.glob('[0-9]*'):
        f.unlink()
    files = [(b / WIDE_A['image'], f"00-{WIDE_A['image']}")]; parts = []
    st = json.load(open(b / 'mixamo-armspace/settings.json'))
    for i, cid in enumerate(cids):
        d = dc.char_dir(cid); m = dc.load(cid); ck = m['stages']['09-game']['checks']; rec = ck['mocap_' + source.replace('-', '_')]
        e = d / f'09-game/evidence/mocap/{source}'
        files += [(e / 'shoulder-compare-2.5x.png', f'{i + 1}1-{cid}-shoulder-standard-vs-arms-down.png'), (e / 'cycle-sheet.png', f'{i + 1}2-{cid}-walk-arms-down-held.png'),
                  (e / f'{cid}-{source}-quarter.mp4', f'{i + 1}3-{cid}-walk-arms-down.mp4'), (e / 'shoulders-2.5x.png', f'{i + 1}4-{cid}-shoulders-arms-down-2.5x.png'),
                  (d / '09-game/wip/rig/landmarks.png', f'{i + 1}5-{cid}-landmarks-painted-pose.png')]
        if also and cid == also[0]:
            ea = d / f'09-game/evidence/mocap/{also[1]}'
            files += [(ea / 'cycle-sheet.png', f'{i + 1}6-{cid}-walk-as-Patch-downloaded-held.png'), (ea / f'{cid}-{also[1]}-quarter.mp4', f'{i + 1}7-{cid}-walk-as-Patch-downloaded.mp4'),
                      (ea / 'shoulder-compare-2.5x.png', f'{i + 1}8-{cid}-shoulder-standard-vs-Patch-download.png')]
        ad = ck.get('arms_down', {}).get('upper_arm_deg', {}); bk = ad.get('bake_sideways', {})
        hem = rec['hem_check']; nums = lambda line: [int(x) for x in re.findall(r'-?\d+', line.split('[', 1)[1].split(']', 1)[0])]   # noqa: E731
        hands = next(h for h in hem if 'hands in' in h); deep = re.search(r'deepest ([\d.]+) cm', hands).group(1)
        torn = next(h for h in hem if 'torn' in h); gap = re.search(r'worst gap ([\d.]+) cm', torn).group(1)
        parts.append(f"{cid} ({m['name']}): painted with the upper arms {bk.get('l', 0):.0f} and {bk.get('r', 0):.0f} deg out (left, right; landmark "
                     f"drawing {i + 1}5). On this walk his hands go into the tunic at most {max(nums(hands))} splats deep {deep} cm, and about "
                     f"{max(nums(torn))} specks tear off the front, the worst {float(gap):.0f} cm. " + MOCAP_NOTES.get(cid, ''))
    a35 = cks = dc.load(cids[0])['stages']['09-game']['checks']['mocap_' + source.replace('-', '_')]['upper_arm_sideways_deg']
    pw = dc.load(cids[0])['stages']['09-game']['checks'].get('mocap_patch_walking', {}).get('upper_arm_sideways_deg', {})
    desc = ("Arms closer to the sides, on natural motion, to judge the shoulders coming down out of the A-pose (Patch, 2026-11-15). "
            "The clip is Mixamo's Walking on X Bot, the same animation Patch downloaded, taken again from his account with Mixamo's own "
            "Character Arm-Space at 35 instead of the default 50 (FBX without skin, 30 fps, in place). Measured from shoulder to elbow, the "
            f"upper arms are {pw.get('l', [0, 0])[0]:.0f} to {pw.get('l', [0, 0])[1]:.0f} deg out (left) and {pw.get('r', [0, 0])[0]:.0f} to "
            f"{pw.get('r', [0, 0])[1]:.0f} (right) in Patch's download, and {a35['l'][0]:.0f} to {a35['l'][1]:.0f} and {a35['r'][0]:.0f} to "
            f"{a35['r'][1]:.0f} at 35. No setting keeps both arms within 10 deg of the body: the clip's left arm swings about 26 deg sideways "
            "at every setting, and settings below 35 bring the arms into the body (24, 29, 35, 40 and 50 were measured). "
            "The clip is retargeted from X Bot to our skeleton (each joint takes X Bot's change from its own rest; the arms are first turned "
            "to X Bot's T) and is otherwise unedited; the only fitting is the hips, scaled to his leg length and set so the toes rest on the "
            "ground. It is played on the character's Mixamo-rig file and shown in our character page frame by frame, with the same cameras "
            "as the other captures. "
            "These three were painted with moderate A-pose arms whose upper arms already hang nearly straight down; the rig's rest lifts "
            "them to 37 deg out and the standard walk holds them 10 to 29 deg out, so this walk shows the shoulders much closer to how they "
            f"were painted. {WIDE_A['what'][0].upper() + WIDE_A['what'][1:]} (00), so a wide-A character's shoulders have about "
            f"{WIDE_A['deg']} deg further to come down. "
            "Per character: his left shoulder on the standard walk against this one at the same three moments (arm furthest forward, "
            "furthest back, nearest the body), front and back, at 2.5x (1); eight held walk phases from front, quarter, side and back (2); "
            "a three-cycle walk video from held frames (3); both shoulders at 2.5x (4); the landmark drawing of the painted pose (5). For "
            f"{also[0]} also Patch's download as it came (6-8). No idle: no mocap idle with the arms down was at hand. " + ' '.join(parts))
    for src, dst in files:
        if src.exists():
            os.link(src, b / dst)
    req = {'title': 'Arms near the sides on Mixamo mocap: shoulders out of the A-pose (386-C, 387-A, 383-A)', 'description': desc, 'dir': str(b)}
    with tempfile.NamedTemporaryFile('w', suffix='.json', delete=False) as f:
        json.dump(req, f)
    r = sh(f'workbench review create --request {f.name}'); os.unlink(f.name)
    receipt = json.loads(r.stdout[r.stdout.index('{'):]); bid = receipt.get('id') or receipt.get('batch', {}).get('id'); url = receipt.get('url')
    for cid in cids:
        dc.patch(cid, lambda m: m['stages']['09-game']['checks']['mocap_' + source.replace('-', '_')].__setitem__('batch', {'id': bid, 'url': url, 'created': dc.NOW()}))
    return bid, url
