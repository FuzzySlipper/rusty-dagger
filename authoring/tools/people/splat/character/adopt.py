"""Adopt existing characters into the factory folders (2026-11-15), so the page shows real state on day one. Files are HARD-LINKED from
live-evidence/ (same disk as the root, so they cost nothing; Patch: do not copy large trees); a file on another disk is copied (small
ones only, e.g. 385's review images from /data). Nothing in live-evidence/ is moved or changed. Named outputs are copied; work in
progress (rings, VACE, other seeds, candidate clips) is recorded by path in each stage's provenance instead. Patch's marks are read from
the Workbench review API (the batches named below) at adoption time. A Patch gate (concept, clip, final) is approved only where his mark
says Good; adopted stages without a mark of his say so."""
import ast, glob, json, os, shutil, subprocess, urllib.request
from pathlib import Path
import dagger_char as dc

REPO = dc.REPO; LE = dc.AP / 'live-evidence'; B1 = LE / 'villagers-batch1-20261113-r01'; T385 = LE / 'town385-hires-20261112-r01'
VILLAGE = LE / 'h3-orbit-control-20261114-r01/village'; NEWB = LE / 'townsperson-all24-ab-20261108/new-b'
WB = 'http://192.168.1.10:8960'

# Patch's marks per character and stage: (batch, item title). Order matters: the last mark with a verdict decides.
MARKS = {
    '383-A': {'01-concept': [('review-a4feae658f73', '383-A.png'), ('review-63106dad7da7', '383-A-square.png')],
            '03-clip': [('review-614a0d712eb4', '383-A-orbit.mp4'), ('review-0e09455229c4', '383-A-old-vs-new.mp4'),
                        ('review-311068b6c522', '383-A-2-orbit.mp4')]},
    '386-C': {'01-concept': [('review-a4feae658f73', '386-C.png'), ('review-63106dad7da7', '386-C-square.png'), ('review-6dd9076dd7c1', '386-C-square.png')],
            '03-clip': [('review-614a0d712eb4', '386-C-orbit.mp4'), ('review-311068b6c522', '386-C-2-orbit.mp4')]},
    '387-A': {'01-concept': [('review-a4feae658f73', '387-A.png'), ('review-63106dad7da7', '387-A-square.png'), ('review-6dd9076dd7c1', '387-A-square.png')],
            '03-clip': [('review-db17eaba065b', '387-A-orbit.mp4'), ('review-f27fcb7e4dbe', '387-A-old-vs-new.mp4')]},
    '384-C': {'01-concept': [('review-a4feae658f73', '384-C.png'), ('review-63106dad7da7', '384-C-square.png'), ('review-6dd9076dd7c1', '384-C-square.png')],
            '03-clip': [('review-614a0d712eb4', '384-C-orbit.mp4'), ('review-f27fcb7e4dbe', '384-C-old-vs-new.mp4'),
                        ('review-28fea30fd0bc', '1-384-C-candidate-three-guides.mp4'), ('review-2fac1040a152', '384-C-2-orbit.mp4')]},
    '390-B': {'01-concept': [('review-a4feae658f73', '390-B.png'), ('review-63106dad7da7', '390-B-square.png'), ('review-6dd9076dd7c1', '390-B-square.png')],
            '03-clip': [('review-614a0d712eb4', '390-B-orbit.mp4'), ('review-f27fcb7e4dbe', '390-B-old-vs-new.mp4'),
                        ('review-2fac1040a152', '390-B-1-orbit-tunic-closed.mp4')]},
}
BAKED = {'383-A': '383/A/bake-m3', '386-C': '386/C/bake-m2', '387-A': '387/A/bake-m2'}
ON_HOLD = ['384-C', '390-B']
# Short display names from each describing text (the manifest holds them; `dagger-char name ID TEXT` changes one).
NAMES = {'381': 'white linen tunic, red-orange zigzag hem', '382': 'bare-chested, white wrap kilt, steel bracers',
         '383-A': 'teal tunic, yellow zigzag bands', '384-C': 'yellow and grey striped tunic and kilt', '385': 'townsman: teal tunic, orange hood, striped hose',
         '386-C': 'sleeveless teal tunic, pale blue mantle', '387-A': 'green tunic, yellow-purple plaid kilt, fur-cuffed boots',
         '388': 'green hood, orange tunic, striped hose', '389': 'green hood, teal and pale blue tunic, striped hose',
         '390-B': 'teal and pale blue tunic, chequered hose', '391': 'green tunic, blue-green hood collar, blue hose',
         '392': 'light blue gown, red hip band', '393': 'blue hood, laced bodice, striped skirt', '394': 'rust-orange robe, green hood collar',
         '395': 'cream cape and wrap skirt, yellow sash', '396': 'orange halter dress', '397': 'cream hood, sea-green dagged skirt',
         '398': 'teal hood, blue skirt with a flame hem', '451': 'green hood, plaid skirt, green gloves', '452': 'teal hooded cloak, white gown',
         '453': 'orange bodice, rust-red skirt', '454': 'rust jacket, blue skirt', '455': 'rust hood, laced bodice, rust skirt',
         '456': 'bell sleeves, half-chequered skirt'}
_batches = {}


def batch(bid):
    if bid not in _batches:
        _batches[bid] = json.load(urllib.request.urlopen(f'{WB}/api/reviews/{bid}', timeout=10))['batch']
    return _batches[bid]


def marks(cid, stage):
    out = []
    for bid, title in MARKS.get(cid, {}).get(stage, []):
        b = batch(bid); it = next((i for i in b['items'] if i['title'] == title), None)
        if it:
            out.append({'batch': bid, 'url': f'{WB}/reviews/{bid}', 'title': title, 'mark': it['mark'], 'note': it.get('note') or '',
                        'source': it.get('source'), 'batch_created': b['createdAt'][:16]})
    return out


def verdict(ms):
    v = [m for m in ms if m['mark'] in ('good', 'bad', 'maybe')]
    return v[-1]['mark'] if v else None


def _link(a, b):
    b.parent.mkdir(parents=True, exist_ok=True)
    if b.exists():
        b.unlink()
    try:
        os.link(a, b)      # same disk: a second name for the same file, no space used
    except OSError:
        shutil.copy2(a, b)


def cp(src, dst):
    src, dst = Path(src), Path(dst)
    if src.is_dir():
        for f in src.rglob('*'):
            if f.is_file():
                _link(f, dst / f.relative_to(src))
    else:
        _link(src, dst)
    return dst


def frames_of(video):
    r = subprocess.run(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v', '-show_entries', 'stream=nb_read_frames', '-of',
                        'csv=p=0', str(video)], capture_output=True, text=True)
    return int(r.stdout.strip()) if r.stdout.strip().isdigit() else None


def score(log):
    try:
        line = [l for l in open(log) if l.startswith('SCORE')][-1]
        s = json.loads(line[6:]); return {'anchor_subject_psnr': s['anchor']['subject_psnr'], 'heldout_subject_psnr': s['heldout']['subject_psnr']}
    except (OSError, IndexError, KeyError, ValueError):
        return {}


def prov(source, note=''):
    return {'adopted_from': str(source), 'adopted_at': dc.NOW(), 'note': note}


A385 = "adopted: townsman 385's run (2026-11-12); accepted before this review list existed - no recorded mark"


def by_patch(ms):
    g = [x for x in ms if x['mark'] == 'good']
    return f"Patch: Good in {g[-1]['batch']} ({g[-1]['batch_created'][:10]})" if g else None


def cutout_review(d):
    """The cut-out review (matte_review.py: video and sheet) for an adopted bake's mattes, from hard links of its frames and views."""
    w = d / '04-cutouts/wip/review-src'
    if (d / '04-cutouts/review').exists():
        return
    cp(d / '03-clip/approved/orbit/frames', w / 'frames'); cp(d / '04-cutouts/views', w / 'orbit/views')
    if (d / '04-cutouts/holes.json').exists():
        cp(d / '04-cutouts/holes.json', w / 'orbit/holes.json')
    (w / 'clip.txt').write_text('orbit\n')
    subprocess.run(['python3', str(REPO / 'authoring/tools/people/splat/mesh-splat/matte_review.py'), str(w), '--clips', f"orbit:0-{len(list((w / 'orbit/views').glob('*.png'))) - 1}"],
                   capture_output=True, text=True)
    if (w / 'review/mattes/orbit').exists():
        cp(w / 'review/mattes/orbit', d / '04-cutouts/review')
    shutil.rmtree(w, ignore_errors=True)


def gate_state(v, none_state='needs-Patch'):
    return {'good': 'approved', 'bad': 'rejected', 'maybe': 'needs-Patch'}.get(v, none_state)


def adopt_brief(m, num, d, cand_dir=None):
    cid = num; s = '00-brief'; o = d / s
    if (NEWB / cid).exists():
        for f in ('prompt.txt', 'image-1.graph.json', 'image-1.png'):
            if (NEWB / cid / f).exists():
                cp(NEWB / cid / f, o / 'recorded' / f)
        dc.record_output(m, s, 'recorded', o / 'recorded')
    if (VILLAGE / f'{cid}-stB.png.graph.json').exists():
        cp(VILLAGE / f'{cid}-stB.png.graph.json', o / 'refined-prompt.graph.json'); dc.record_output(m, s, 'refined-graph', o / 'refined-prompt.graph.json')
    if cand_dir:
        for f in ('clothing.txt', 'clothing-back.txt', 'back-prompt.txt'):
            if (cand_dir / f).exists():
                cp(cand_dir / f, o / f); dc.record_output(m, s, f.split('.')[0], o / f)
    dc.set_state(m, s, 'approved', 'adopted: recorded prompt and graph' + (', describing texts' if cand_dir else ''),
                 by='adopt', provenance=prov(NEWB / cid), approved_by='adopted: the prompts the concept was made from')


def fresh(cid):
    """Adoption starts each manifest anew (the copied files stay; re-copying overwrites them)."""
    p = dc.char_dir(cid) / 'character.json'
    if p.exists():
        p.unlink()


def adopt_villager(cid, bake=None):
    num, cand = cid.split('-')
    fresh(cid); m = dc.init(cid, NAMES.get(cid, f'Villager {cid}'), {'archive_record': num, 'candidate': cand, 'batch': str(B1)}); d = dc.char_dir(cid)
    V = B1 / num / cand; adopt_brief(m, num, d, V)
    # 01 concept: the three candidate pairs, the chosen pair's paintings and the keyframes its clip used
    s = '01-concept'; o = d / s
    for L in 'ABC':
        for f in ('front.png', 'back.png'):
            if (B1 / num / L / f).exists():
                cp(B1 / num / L / f, o / 'candidates' / L / f)
    m['stages'][s]['attempts'] = [{'id': L, 'path': f'{s}/candidates/{L}'} for L in 'ABC' if (o / 'candidates' / L).exists()]
    m['stages'][s]['selected'] = [cand]
    for f in ('front.png', 'back.png'):
        cp(V / f, o / 'approved' / f); dc.record_output(m, s, f[:-4], o / 'approved' / f)
    ms = marks(cid, s); m['stages'][s]['review'] = {'marks': ms}
    # 03 clip
    s3 = '03-clip'; o3 = d / s3; ms3 = marks(cid, s3); m['stages'][s3]['review'] = {'marks': ms3}
    if bake:
        bk = B1 / bake; clip = REPO / (bk / 'clip.txt').read_text().strip(); run = json.load(open(bk / 'clip-run.json'))
        inputs = ast.literal_eval(run['inputs']) if isinstance(run.get('inputs'), str) else run.get('inputs', {})
        for view in ('front', 'back'):
            k = inputs.get(view, {}).get('prepared') or inputs.get(view, {}).get('source')
            if k and Path(k).exists():
                cp(k, o / 'approved' / f'{view}-key.png'); dc.record_output(m, s, f'{view}-key', o / 'approved' / f'{view}-key.png')
        cp(clip / 'frames', o3 / 'approved/orbit/frames'); cp(bk / 'clip-run.json', o3 / 'approved/orbit/run.json')
        vid = next((Path(x['source']) for x in reversed(ms3) if x['mark'] == 'good' and x.get('source') and Path(x['source']).exists()), None)
        if vid:
            cp(vid, o3 / 'approved/orbit/review.mp4')
        n = len(list((o3 / 'approved/orbit/frames').glob('*.png')))
        dc.record_output(m, s3, 'orbit', o3 / 'approved/orbit/frames'); dc.record_input(m, s3, 'front-key', s, 'front-key')
        legs = next((Path(x['source']).with_name(Path(x['source']).name.replace('-2-orbit.mp4', '-3-legs.png').replace('-orbit.mp4', '-legs.png'))
                     for x in reversed(ms3) if x['mark'] == 'good' and x.get('source')), None)
        (o3 / 'approved/orbit/legs.png').unlink(missing_ok=True)
        if legs and legs.suffix == '.png' and legs.exists():   # only a real leg sheet beside the marked video
            cp(legs, o3 / 'approved/orbit/legs.png')
        dc.record_input(m, s3, 'back-key', s, 'back-key')
        m['stages'][s3]['selected'] = [{'name': 'orbit', 'role': 'orbit', 'frames': n, 'size': run.get('width'), 'seed': run.get('seed'),
                                        'route': run.get('profile'), 'guides': run.get('guides')}]
        m['stages'][s3]['checks'] = {'frames': n, 'review_video_frames': frames_of(o3 / 'approved/orbit/review.mp4') if vid else None}
    else:   # on hold: the clips Patch saw, as candidates
        for x in ms3:
            if x.get('source') and Path(x['source']).exists():
                cp(x['source'], o3 / 'candidates' / x['title'])
        m['stages'][s3]['attempts'] = [{'id': x['title'], 'mark': x['mark'], 'note': x['note']} for x in ms3]
    v1 = verdict(ms); dc.set_state(m, s, gate_state(v1), f"Patch: {v1 or 'no mark'}" + (f" - {ms[-1]['note'][:80]}" if ms and ms[-1]['note'] else ''),
                                    by='adopt', provenance=prov(V), why='' if v1 == 'good' else f"Patch: {v1 or 'not marked'}",
                                    approved_by=by_patch(ms))
    dc.set_state(m, '02-guide', 'skipped', 'pinned FL2VA route: the keyframes are the guides (front 0, back 36, front 72); no Blender guide', by='adopt',
                 why='recipe uses the keyframes as guides')
    v3 = verdict(ms3)
    if bake:
        dc.set_state(m, s3, gate_state(v3), f"Patch: {v3} ({ms3[-1]['title']})" if ms3 else 'no mark', by='adopt', provenance=prov(clip),
                     approved_by=by_patch(ms3))
        adopt_bake(m, d, B1 / bake)
    else:
        last = next((x for x in reversed(ms3) if x['mark'] in ('good', 'bad', 'maybe')), None)
        dc.set_state(m, s3, 'on-hold', f"Patch's latest clip mark: {last['mark']} ({last['title']}){' - ' + last['note'][:80] if last and last['note'] else ''}"
                     if last else 'no clip marked', by='adopt', provenance=prov(B1 / num / cand / 'stab'),
                     why=f"Patch marked the clip {last['mark']} ({last['batch_created'][:10]}); on hold" if last else 'no clip marked')
        for k in dc.STAGE_KEYS[4:]:
            m['stages'][k]['note'] = 'waits for an approved clip'
    dc.save(m); return m


def adopt_bake(m, d, bk):
    """Cut-outs, cameras, first fit, refine and final from one accepted bake folder."""
    s = '04-cutouts'; o = d / s
    for f in ('views.json', 'holes.json', 'mattes.png'):
        if (bk / 'orbit' / f).exists():
            cp(bk / 'orbit' / f, o / f)
    cp(bk / 'orbit/views', o / 'views'); dc.record_output(m, s, 'views', o / 'views'); dc.record_input(m, s, 'orbit', '03-clip', 'orbit')
    cutout_review(d)
    h = json.load(open(bk / 'orbit/holes.json')) if (bk / 'orbit/holes.json').exists() else {}
    m['stages'][s]['checks'] = {'frames': len(list((o / 'views').glob('*.png'))), 'frames_with_holes_removed': sum(v.get('removed', 0) > 0 for v in h.values()),
                                'removed_px': sum(v.get('removed', 0) for v in h.values())}
    dc.set_state(m, s, 'needs-check', 'flat-rule mattes from before the cut-out review and BiRefNet; not yet looked at as cut-outs', by='adopt',
                 provenance=prov(bk / 'orbit'), why='flat-rule mattes (no BiRefNet); cut-out review made, not looked at')
    s = '05-cameras'; o = d / s
    cp(bk / 'solves', o / 'solves'); cp(bk / 'cloud', o / 'cloud')
    for f in ('views.json', 'cameras.npz'):
        if (bk / 'data' / f).exists():
            cp(bk / 'data' / f, o / 'data' / f)
    dc.record_output(m, s, 'solve', o / 'solves'); dc.record_output(m, s, 'cloud', o / 'cloud'); dc.record_input(m, s, 'views', '04-cutouts', 'views')
    iou = (bk / 'cloud-iou.txt').read_text().strip().splitlines()[-1] if (bk / 'cloud-iou.txt').exists() else ''
    m['stages'][s]['checks'] = {'cloud_iou': iou[:200]}
    dc.set_state(m, s, 'approved', 'adopted from the accepted bake (loop images not copied: provenance)', by='adopt', approved_by='adopted: part of the bake the lead accepted (2026-11-14)', provenance=prov(bk))
    s = '06-first-fit'; o = d / s
    cp(bk / 'r0', o / 'r0'); cp(bk / 'review-r0', o / 'review')
    dc.record_output(m, s, 'r0', o / 'r0'); dc.record_input(m, s, 'cloud', '05-cameras', 'cloud')
    m['stages'][s]['checks'] = score(bk / 'r0.log')
    dc.set_state(m, s, 'approved', 'adopted from the accepted bake', by='adopt', approved_by='adopted: part of the bake the lead accepted (2026-11-14)', provenance=prov(bk / 'r0'))
    s = '07-refine'; o = d / s
    cp(bk / 'r1', o / 'r1'); cp(bk / 'review-r1', o / 'review')
    if (bk / 'summary.txt').exists():
        cp(bk / 'summary.txt', o / 'summary.txt')
    dc.record_output(m, s, 'r1', o / 'r1'); dc.record_input(m, s, 'r0', '06-first-fit', 'r0')
    m['stages'][s]['checks'] = score(bk / 'r1.log')
    dc.set_state(m, s, 'approved', 'adopted: VACE repaint round and round 1 (rings, VACE frames and r1 data stay in the bake folder)', by='adopt', approved_by='adopted: part of the bake the lead accepted (2026-11-14)',
                 provenance=prov(bk, 'wip: rings-*, vace, vace-matched, r1-data'))
    s = '08-final'; o = d / s
    cp(bk / 'final', o / 'final'); cp(bk / 'review-final', o / 'review'); cp(bk / 'data/cameras.npz', o / 'data/cameras.npz')
    if (bk / 'review-vs-old').exists():
        cp(bk / 'review-vs-old', o / 'review-vs-old')
    dc.record_output(m, s, 'splat', o / 'final/splat.npz'); dc.record_input(m, s, 'r1', '07-refine', 'r1')
    lean = level(o)
    m['stages'][s]['checks'] = {'frame_lean_deg': lean.get('old_frame_lean_deg'), 'level_scale': lean.get('scale'),
                                'cloud': 'final/cloud.ply' if (o / 'final/cloud.ply').exists() else None}
    dc.set_state(m, s, 'needs-Patch', f"accepted bake (lead); no Patch mark on the final yet; frame lean {lean.get('old_frame_lean_deg')} deg before levelling",
                 by='adopt', provenance=prov(bk / 'final'), why="Patch's look before 09-game (no mark yet)",
                 waiting_since=dc.datetime.datetime.fromtimestamp((bk / 'final/splat.npz').stat().st_mtime).astimezone().isoformat(timespec='minutes'))
    m['stages']['09-game']['note'] = 'not distilled or rigged; waits for Patch\'s look at the final'


def level(o):
    """level_splat.py on the final (writes 08-final/final-level/; needs 08-final/data/cameras.npz); returns its numbers (the frame lean)."""
    if not (o / 'data/cameras.npz').exists():
        return {}
    r = subprocess.run(['python3', str(REPO / 'authoring/tools/people/splat/bake/level_splat.py'), str(o), 'final'], capture_output=True, text=True)
    try:
        return json.loads(r.stdout.strip().splitlines()[-1])
    except (IndexError, ValueError):
        return {}


def adopt_385():
    cid = '385'; fresh(cid); m = dc.init(cid, NAMES['385'], {'archive_record': '385', 'run': str(T385)}); d = dc.char_dir(cid)
    s = '00-brief'; o = d / s
    for f in ('original-prompt.txt', 'clothing.txt', 'clothing-v2.txt', 'clothing-back.txt', 'back-prompt.txt', 'front.png.graph.json', 'back.png.graph.json'):
        if (T385 / 'art' / f).exists():
            cp(T385 / 'art' / f, o / f); dc.record_output(m, s, f, o / f)
    dc.set_state(m, s, 'approved', 'adopted: prompts, describing texts, graphs', by='adopt', provenance=prov(T385 / 'art'), approved_by=A385)
    s = '01-concept'; o = d / s
    for f in ('front.png', 'back.png'):
        cp(T385 / 'art' / f, o / 'approved' / f); dc.record_output(m, s, f[:-4], o / 'approved' / f)
        cp(T385 / 'keys-2176' / f, o / 'approved' / f.replace('.png', '-key.png')); dc.record_output(m, s, f[:-4] + '-key', o / 'approved' / f.replace('.png', '-key.png'))
    dc.set_state(m, s, 'approved', 'adopted (front seed 5, back fixed with the leather case); the review predates the batch list', by='adopt',
                 provenance=prov(T385 / 'art', 'other seeds and the rejected back: art/try, rejected/'), approved_by=A385)
    dc.set_state(m, '02-guide', 'skipped', 'pinned FL2VA route: the keyframes are the guides', by='adopt', why='recipe uses the keyframes as guides')
    s = '03-clip'; o = d / s; clip = T385 / 'orbits/t385b/guide-back@36/attempt-610381670'
    cp(clip / 'frames', o / 'approved/orbit/frames')
    if (clip / 'run.json').exists():
        cp(clip / 'run.json', o / 'approved/orbit/run.json')
    n = len(list((o / 'approved/orbit/frames').glob('*.png'))); dc.record_output(m, s, 'orbit', o / 'approved/orbit/frames')
    m['stages'][s]['selected'] = [{'name': 'orbit', 'role': 'orbit', 'frames': n, 'size': 2176, 'seed': 610381670, 'route': 'fl2va sprite-anchored'}]
    m['stages'][s]['attempts'] = [{'id': p.name} for p in sorted((T385 / 'orbits/t385b/guide-back@36').glob('attempt-*'))]
    m['stages'][s]['checks'] = {'frames': n}
    dc.set_state(m, s, 'approved', 'adopted: seed 610381670 (best median and reserved IoU, most even turn)', by='adopt', provenance=prov(clip), approved_by=A385)
    for k in ('04-cutouts', '05-cameras', '06-first-fit'):
        dc.set_state(m, k, 'approved', 'baked in the run folder before this layout; not copied (provenance)', by='adopt', provenance=prov(T385), approved_by=A385)
    s = '07-refine'; o = d / s
    cp(T385 / 'r1', o / 'r1'); cp(T385 / 'review-r1', o / 'review'); dc.record_output(m, s, 'r1', o / 'r1')
    dc.set_state(m, s, 'approved', 'adopted: round 1', by='adopt', provenance=prov(T385 / 'r1'), approved_by=A385)
    s = '08-final'; o = d / s
    cp(T385 / 'final', o / 'final'); cp(T385 / 'review-final', o / 'review'); cp(T385 / 'export', o / 'export')
    cp(T385 / 'r1/fit-data/cameras.npz', o / 'data/cameras.npz')   # the final's frame is round 1's
    dc.record_output(m, s, 'splat', o / 'final/splat.npz')
    lean = level(o); m['stages'][s]['checks'] = {'frame_lean_deg': lean.get('old_frame_lean_deg'), 'level_scale': lean.get('scale')}
    dc.set_state(m, s, 'approved', 'adopted: final splat and the exported clouds (export/)', by='adopt', provenance=prov(T385 / 'final'),
                 approved_by=A385 + '; it went on to the rig')
    s = '09-game'; o = d / s; g = Path('/data/assets/splat-characters/town385')
    glbs = sorted(str(p.relative_to(g)) for p in g.rglob('*.glb'))
    (o / 'game-files.txt').write_text(f"Townsman 385's game files live outside this folder (another disk; not moved):\n{g}/\n\n" + '\n'.join(glbs) + '\n')
    for f in sorted((g / 'checks').glob('*.png')):
        cp(f, o / 'review' / f.name)      # copies, so the images show over the share
    m['stages'][s]['outputs'] = {'game-files': {'path': '09-game/game-files.txt', 'sha256': dc.path_hash(o / 'game-files.txt'), 'points_to': str(g)}}
    m['stages'][s]['checks'] = {'glbs': glbs}
    dc.set_state(m, s, 'approved', f'game files at {g} ({len(glbs)} GLBs, mixamo and 441-bone rigs); pointer: game-files.txt', by='adopt', provenance=prov(g),
                 approved_by=A385)
    dc.save(m); return m


def adopt_concept_only(cid):
    fresh(cid); m = dc.init(cid, NAMES.get(cid, f'Villager {cid}'), {'archive_record': cid}); d = dc.char_dir(cid)
    adopt_brief(m, cid, d)
    s = '01-concept'; o = d / s; att = []
    for tag, label in (('recorded', 'recorded prompt'), ('stB', 'refined prompt (turnaround wording, sideless shading)')):
        f = VILLAGE / f'{cid}-{tag}.png'
        if f.exists():
            cp(f, o / 'candidates' / tag / 'front.png')
            if Path(str(f) + '.graph.json').exists():
                cp(str(f) + '.graph.json', o / 'candidates' / tag / 'front.png.graph.json')
            att.append({'id': tag, 'path': f'{s}/candidates/{tag}', 'what': label})
    b = batch('review-0e53707b7e84'); its = [i for i in b['items'] if i['title'].split('.')[0].split('-')[0] == cid]
    m['stages'][s]['attempts'] = att
    m['stages'][s]['review'] = {'marks': [{'batch': b['id'], 'url': f"{WB}/reviews/{b['id']}", 'title': i['title'], 'mark': i['mark'],
                                           'note': i.get('note') or ''} for i in its]}
    dc.set_state(m, s, 'needs-Patch', 'fronts only (recorded and refined prompt), unreviewed; no backs yet', by='adopt', provenance=prov(VILLAGE),
                 why='fronts unreviewed; no backs yet', waiting_since=b['createdAt'][:16])
    dc.save(m); return m


def concept_marks(ids=None):
    """Characters still at the concept with fronts in the shared village batch (review-0e53707b7e84: recorded prompt on the left,
    refined on the right, "Mark Bad any villager where the refined version loses something you care about"): read Patch's marks
    again. Good -> the refined front is the approved front (01-concept/approved/front.png); the back is made and pair-checked when
    the character starts, so the stage is approved with that said. Bad -> rejected with his note. Otherwise still needs-Patch."""
    _batches.clear(); bid = 'review-0e53707b7e84'; b = batch(bid); out = []
    for cid in ids or dc.characters():
        m = dc.load(cid); s = '01-concept'; st = m['stages'][s]
        if st['state'] not in ('needs-Patch', 'rejected') or (dc.char_dir(cid) / s / 'approved/back.png').exists():
            continue
        its = [i for i in b['items'] if i['title'].split('.')[0] == cid]
        if not its:
            continue
        ms = [{'batch': bid, 'url': f'{WB}/reviews/{bid}', 'title': i['title'], 'mark': i['mark'], 'note': i.get('note') or '', 'batch_created': b['createdAt'][:16]} for i in its]
        st['review'] = {'batch': {'id': bid, 'url': f'{WB}/reviews/{bid}', 'created': b['createdAt'][:16], 'titles': [i['title'] for i in its]},
                        'marks': [x for x in (st.get('review') or {}).get('marks', []) if x.get('batch') != bid] + ms}
        v = verdict(ms); d = dc.char_dir(cid); front = d / s / 'candidates/stB/front.png'
        if v == 'good' and front.exists():
            _link(front, d / s / 'approved/front.png'); dc.record_output(m, s, 'front', d / s / 'approved/front.png'); st['selected'] = ['stB']
            dc.set_state(m, s, 'approved', f'front (refined prompt) approved by Patch: Good in {bid}; the back is made and pair-checked when the character starts',
                         by='marks', approved_by=f'Patch: Good in {bid} (front; read {dc.dc_date()})')
        elif v == 'bad':
            dc.set_state(m, s, 'rejected', f"Patch: Bad on {ms[-1]['title']}" + (f" - {ms[-1]['note']}" if ms[-1]['note'] else ''), by='marks', why=f'Patch: Bad ({bid})' + (f" - {ms[-1]['note']}" if ms[-1]['note'] else ''))
        elif v == 'maybe':
            dc.set_state(m, s, 'needs-Patch', f'Patch: Maybe in {bid}', by='marks', why=f'Patch marked Maybe ({bid})', waiting_since=b['createdAt'][:16])
        else:
            continue
        dc.save(m); out.append((cid, m['stages'][s]['state']))
    print(' '.join(f'{c}:{v}' for c, v in out) or 'no marks to apply'); return out


def run(ids):
    done = []
    nums = {c.split('-')[0] for c in list(BAKED) + ON_HOLD} | {'385'}
    all_ids = ['385'] + sorted(BAKED) + sorted(ON_HOLD) + sorted({p.name.split('-')[0] for p in VILLAGE.glob('*-recorded.png')} - nums)
    for cid in ids or all_ids:
        if cid == '385':
            adopt_385()
        elif cid in BAKED:
            adopt_villager(cid, BAKED[cid])
        elif cid in ON_HOLD:
            adopt_villager(cid)
        else:
            adopt_concept_only(cid)
        done.append(cid); print('adopted', cid, flush=True)
    return done
