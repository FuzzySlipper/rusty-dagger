"""dagger-char run ID 09-game --step arms_down: an extra evidence set (not the standard one) with the arms at the sides, to judge the
shoulders coming down out of the A-pose (Patch via the lead, 2026-11-15: "use a clip where their arms are at their sides so easier
to tell how well the shoulders deform out of the wide A pose").

  clips     PINOC's Walking and Standing Idle with the upper arms brought in to 4 deg from vertical at every keyframe
            (arms_down_clip.py; forward swing, forearms and everything else as authored); PINOC's library has no other plain
            forward walk. Into 09-game/wip/clips/ (git-ignored product data, never committed).
  page      served beside the standard clips as 'walkdown' / 'idledown' (same pelvis fits as walk / idle), and a playtest
            profile dagger-ID-armsdown that opens the page with the arm offset off (&abduct=0&forearm=0)
  angles    arm_angles.py: the upper arm from vertical in the bake, the rig's rest, the walk as captured (offset 10/15), the walk
            and idle as authored and the arms-down clips
  captures  held (never played): 8 walk phases from front, quarter, side and back; the idle from the same four; shoulder
            close-ups (2.5x) from front and back at the phases where the left arm is furthest forward, furthest back and
            straight down; a 93-frame walk video, frame-counted
  checks    hem_check.py with the arms-down walk and no offset (hands go into the tunic here; expected)
Into 09-game/evidence/arms-down/, recorded in the manifest as checks.arms_down."""
import json, os, shutil, subprocess
from pathlib import Path
import numpy as np
import dagger_char as dc
import rig as R

HERE = Path(__file__).parent
GAMES = Path('~/.config/crew-playtest/games.json').expanduser()


def sh(cmd, **kw):
    return subprocess.run([str(c) for c in cmd], text=True, capture_output=True, **kw)


def profile(cid):
    """A playtest profile for the page with the arm offset off (a copy of dagger-ID's, URL + &abduct=0&forearm=0)."""
    games = json.load(open(GAMES)); pid = f'dagger-{cid}-armsdown'
    if any(g['id'] == pid for g in games):
        return pid
    base = next(g for g in games if g['id'] == f'dagger-{cid}')
    g = dict(base, id=pid, url=base['url'] + '&abduct=0&forearm=0',
             description=base['description'] + ' Arms-down evidence: the arm offset off; clips walkdown / idledown (armsdown.py).')
    games.append(g); tmp = GAMES.with_suffix('.json.tmp'); tmp.write_text(json.dumps(games, indent=1)); os.replace(tmp, GAMES)
    sh(['playtest', 'reload'])   # the service loads profiles at start or on reload
    return pid


def arms_down(cid, tier='48k'):
    d = dc.char_dir(cid); w = d / '09-game/wip'; r = w / 'rig'; e = d / '09-game/evidence/arms-down'; e.mkdir(parents=True, exist_ok=True)
    rn = r / f'{cid}-{tier}-rig.npz'; skel = r / 'skeleton-data-441.ts'; lm = r / 'landmarks.json'
    # Clips.
    clips = {}
    for src, dst in (('walking.glb', 'walking-arms-down.glb'), ('standing-idle.glb', 'standing-idle-arms-down.glb')):
        o = sh(['python3', HERE / 'arms_down_clip.py', w / 'clips' / src, w / 'clips' / dst, rn, '--skeleton', skel])
        if o.returncode:
            raise SystemExit(o.stderr[-800:])
        clips[dst] = json.loads(o.stdout)['arms']
    # Page: the standard files first (page_files rewrites character.json), then the arms-down clips beside them.
    wd = R.page_files(cid, tier)
    for f in ('walking-arms-down.glb', 'standing-idle-arms-down.glb'):
        shutil.copy2(w / 'clips' / f, wd / f)
    cj = json.load(open(wd / 'character.json'))
    cj['clips']['walkdown'] = dict(cj['clips']['walk'], file='walking-arms-down.glb', label='Walking, arms down')
    cj['clips']['idledown'] = dict(cj['clips']['idle'], file='standing-idle-arms-down.glb', label='Standing Idle, arms down')
    (wd / 'character.json').write_text(json.dumps(cj, indent=1))
    pid = profile(cid)
    # Angles.
    ang = {}
    for name, clip, t0, per, n, off in (('walk-as-captured', 'walking.glb', 2.333, 1.033, 32, (10, 15)), ('walk-authored', 'walking.glb', 2.333, 1.033, 32, (0, 0)),
                                       ('walk-arms-down', 'walking-arms-down.glb', 2.333, 1.033, 32, (0, 0)), ('idle-authored', 'standing-idle.glb', 0, 4, 16, (0, 0)),
                                       ('idle-arms-down', 'standing-idle-arms-down.glb', 0, 4, 16, (0, 0))):
        sh(['python3', HERE / 'arm_angles.py', rn, lm, w / 'clips' / clip, t0, per, '--n', n, '--abduct', off[0], '--forearm', off[1], '--skeleton', skel,
            '--json', e / f'angles-{name}.json'])
        ang[name] = json.load(open(e / f'angles-{name}.json'))
    fw = [p[2] for p in ang['walk-arms-down']['clip']['l']['per_phase']]
    ks = {'arm-forward': int(np.argmax(fw)), 'arm-back': int(np.argmin(fw)), 'arm-down': int(np.argmin(np.abs(fw)))}
    tk = {k: round(2.333 + v * 1.033 / 32, 4) for k, v in ks.items()}
    # Captures.
    sc = e / 'scripts'; sc.mkdir(exist_ok=True)
    env = dict(os.environ, CAPTURE_SID=str(e / '.capture-sid'))
    run = lambda *c: subprocess.run([str(x) for x in c], cwd=e, env=env, text=True, capture_output=True)   # noqa: E731
    st = run('bash', R.CAP / 'start.sh', pid, 1920, 1080)
    if 'unknown game' in st.stderr:
        sh(['playtest', 'reload']); st = run('bash', R.CAP / 'start.sh', pid, 1920, 1080)
    run('bash', R.CAP / 'mkcycle.sh', 'scripts/cycle.js', 'front,quarter,side,back', 'native', '', 'walkdown', 2.333, 1.033, 8)
    (sc / 'idle.js').write_text(IDLE); (sc / 'shoulders.js').write_text(SHOULDERS.replace('$TIMES', json.dumps(tk)))
    out = {}
    for name in ('cycle', 'idle', 'shoulders'):
        shutil.rmtree(e / name, ignore_errors=True)
        rr = run('bash', R.CAP / 'run.sh', f'scripts/{name}.js', name); out[name] = rr.stdout.strip().splitlines()[-2:]
        if not (e / name).is_dir():
            raise SystemExit(f'{cid} arms_down: capture {name} failed: {(rr.stderr or rr.stdout)[-600:]}')
    for p in range(4):
        (sc / f'vid-{p}.js').write_text(R.VID.replace('$P', str(p)).replace("value: 'walk'}", "value: 'walkdown'}"))
    shutil.rmtree(e / 'vid', ignore_errors=True)
    for p in range(4):
        rr = run('bash', R.CAP / 'run.sh', f'scripts/vid-{p}.js', 'vid'); out[f'vid-{p}'] = rr.stdout.strip().splitlines()[-1:]
    frames = sorted((e / 'vid').glob('f*.png')); mp4 = e / f'{cid}-walk-arms-down-quarter.mp4'
    sh(['ffmpeg', '-loglevel', 'error', '-y', '-framerate', '30', '-pattern_type', 'glob', '-i', e / 'vid/f*.png', '-c:v', 'libx264', '-crf', '16',
        '-pix_fmt', 'yuv420p', '-vf', 'crop=1000:1080:460:0', mp4])
    nf = sh(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v', '-show_entries', 'stream=nb_read_frames', '-of', 'csv=p=0', mp4]).stdout.strip()
    out['video'] = {'frames_captured': len(frames), 'frames_encoded': nf, 'expected': 93}
    # Sheets and close-ups.
    for nm, cols, rows, pat in (('cycle-sheet', 'p0,p1,p2,p3,p4,p5,p6,p7', 'front,quarter,side,back', 'cycle/{row}-{col}.png'),
                                ('idle-sheet', 'front,quarter,side,back', 'idle', 'idle/{row}-{col}.png')):
        rr = sh(['python3', R.SHEET, e / f'{nm}.png', cols, rows, e / pat])
        out[nm] = {'rc': rr.returncode, 'touching_edge': json.load(open(e / f'{nm}-tiles.json'))['touching_edge']}
    shoulders(e, list(ks))
    # Hem check with the arms-down walk and no offset.
    L = json.load(open(lm)); env2 = dict(os.environ, ASSET_CPU_REPO=str(dc.REPO / 'authoring/tools/people/splat'))
    hc = sh(['python3', r / 'hem_check.py', rn, w / f'fit{tier}/splat.npz', w / 'clips/walking-arms-down.glb', 2.333, 1.033, 8, '--waist', L['skirt']['waist'],
             '--hem', L['skirt']['hem'], '--skeleton', skel], env=env2)
    hem = [l.strip() for l in hc.stdout.splitlines() if l.startswith('  ') and ('per phase' in l or 'torn' in l or 'hands' in l)]
    (e / 'hem-check.out').write_text(hc.stdout + hc.stderr)

    def brief(a_):
        c = a_['clip']; return {s: [c[s]['sideways_min'], c[s]['sideways_max'], c[s]['forward_min'], c[s]['forward_max']] for s in 'lr'}
    rec = {'when': dc.NOW(), 'folder': '09-game/evidence/arms-down', 'profile': pid, 'clips': clips,
           'upper_arm_deg': {'bake_sideways': {s: ang['walk-authored']['bake'][s][1] for s in 'lr'}, 'rest_sideways': ang['walk-authored']['rest']['l'][1],
                             'clips_sideways_min_max_forward_min_max': {k: brief(v) for k, v in ang.items()}},
           'shoulder_phases': tk, 'captures': out, 'hem_check_arms_down': hem}
    dc.patch(cid, lambda m: m['stages']['09-game'].setdefault('checks', {}).__setitem__('arms_down', rec))
    print(json.dumps(rec, indent=1)[:3000])


def shoulders(e, phases):
    """Both shoulders at 2.5x from front and back at each phase: one row per phase, front left/right then back left/right."""
    from PIL import Image, ImageDraw
    rows = []
    for ph in phases:
        tiles = []
        for view in ('front', 'back'):
            im = Image.open(e / f'shoulders/{view}-{ph}.png').convert('RGB'); W, H = im.size
            for side, cx in (('left of frame', W // 2 - 150), ('right of frame', W // 2 + 150)):
                t = im.crop((cx - 140, int(H * 0.30) - 140, cx + 140, int(H * 0.30) + 140)).resize((700, 700), Image.LANCZOS)
                ImageDraw.Draw(t).text((8, 6), f'{ph}, {view}, {side} (2.5x)', fill=(255, 255, 255)); tiles.append(t)
        row = Image.new('RGB', (706 * 4 - 6, 700), (255, 255, 255))
        for i, t in enumerate(tiles):
            row.paste(t, (706 * i, 0))
        rows.append(row)
    sheet = Image.new('RGB', (rows[0].width, 706 * len(rows) - 6), (255, 255, 255))
    for i, row in enumerate(rows):
        sheet.paste(row, (0, 706 * i))
    sheet.save(e / 'shoulders-2.5x.png')


IDLE = """// Standing Idle with the arms down (idledown), held at t 2 from four views (#hold 1, never #play).
const out = [];
await browser({op: 'fill', selector: '#hold', value: '1'});
await browser({op: 'fill', selector: '#clip', value: 'idledown'});
await browser({op: 'fill', selector: '#fx', value: 'native'});
for (const view of ['front', 'quarter', 'side', 'back']) {
  await browser({op: 'fill', selector: '#view', value: view});
  await browser({op: 'fill', selector: '#time', value: '2'});
  await browser({op: 'fill', selector: '#tag', value: `idle-${view}`});
  await browser({op: 'click', selector: '#apply'});
  await sleep(700);
  out.push([`idle-${view}`, (await capture({label: `idle-${view}`})).path]);
}
await browser({op: 'fill', selector: '#tag', value: 'end'}); await browser({op: 'click', selector: '#apply'}); await sleep(300);
const page = await browser({op: 'inspect'});
checkpoint('frames', {frames: out, console: (page.console || []).map((m) => m.text).filter((t) => /^(seen|applied) /.test(t))});
"""

SHOULDERS = """// Shoulders: the arms-down walk held where the left arm is furthest forward, furthest back and straight down, front and back.
const times = $TIMES;
const out = [];
await browser({op: 'fill', selector: '#hold', value: '1'});
await browser({op: 'fill', selector: '#clip', value: 'walkdown'});
await browser({op: 'fill', selector: '#fx', value: 'native'});
for (const view of ['front', 'back']) {
  await browser({op: 'fill', selector: '#view', value: view});
  for (const [ph, t] of Object.entries(times)) {
    const label = `${view}-${ph}`;
    await browser({op: 'fill', selector: '#time', value: String(t)});
    await browser({op: 'fill', selector: '#tag', value: label});
    await browser({op: 'click', selector: '#apply'});
    await sleep(500);
    out.push([label, (await capture({label})).path]);
  }
}
await browser({op: 'fill', selector: '#tag', value: 'end'}); await browser({op: 'click', selector: '#apply'}); await sleep(300);
const page = await browser({op: 'inspect'});
checkpoint('frames', {frames: out, console: (page.console || []).map((m) => m.text).filter((t) => /^(seen|applied) /.test(t))});
"""


# The lead (2026-11-15), second pass: the same PINOC walk with a constant inward offset (set A), beside the arms-down edit (set B),
# and one comparison per character of his left shoulder: standard offset, inward offset and arms-down at the same three phases.
def profile_q(cid, suffix, query, what):
    games = json.load(open(GAMES)); pid = f'dagger-{cid}-{suffix}'
    if not any(g['id'] == pid for g in games):
        base = next(g for g in games if g['id'] == f'dagger-{cid}')
        games.append(dict(base, id=pid, url=base['url'] + query, description=base['description'] + ' ' + what))
        tmp = GAMES.with_suffix('.json.tmp'); tmp.write_text(json.dumps(games, indent=1)); os.replace(tmp, GAMES); sh(['playtest', 'reload'])
    return pid


def capture(e, pid, scripts, video_clip=None, vid_name=None):
    """Run capture scripts {name: js} in a fresh session of profile pid into e/name; the 93-frame video of video_clip when given."""
    sc = e / 'scripts'; sc.mkdir(parents=True, exist_ok=True); env = dict(os.environ, CAPTURE_SID=str(e / '.capture-sid'))
    run = lambda *c: subprocess.run([str(x) for x in c], cwd=e, env=env, text=True, capture_output=True)   # noqa: E731
    st = run('bash', R.CAP / 'start.sh', pid, 1920, 1080)
    if 'unknown game' in st.stderr:
        sh(['playtest', 'reload']); run('bash', R.CAP / 'start.sh', pid, 1920, 1080)
    out = {}
    for name, js in scripts.items():
        if js is not None:
            (sc / f'{name}.js').write_text(js)
        shutil.rmtree(e / name, ignore_errors=True)
        rr = run('bash', R.CAP / 'run.sh', f'scripts/{name}.js', name); out[name] = rr.stdout.strip().splitlines()[-2:]
        if not (e / name).is_dir():
            raise SystemExit(f'capture {e}/{name} failed: {(rr.stderr or rr.stdout)[-600:]}')
    if video_clip:
        for p in range(4):
            (sc / f'vid-{p}.js').write_text(R.VID.replace('$P', str(p)).replace("value: 'walk'}", f"value: '{video_clip}'}}"))
        shutil.rmtree(e / 'vid', ignore_errors=True)
        for p in range(4):
            rr = run('bash', R.CAP / 'run.sh', f'scripts/vid-{p}.js', 'vid'); out[f'vid-{p}'] = rr.stdout.strip().splitlines()[-1:]
        frames = sorted((e / 'vid').glob('f*.png')); mp4 = e / vid_name
        sh(['ffmpeg', '-loglevel', 'error', '-y', '-framerate', '30', '-pattern_type', 'glob', '-i', e / 'vid/f*.png', '-c:v', 'libx264', '-crf', '16',
            '-pix_fmt', 'yuv420p', '-vf', 'crop=1000:1080:460:0', mp4])
        nf = sh(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v', '-show_entries', 'stream=nb_read_frames', '-of', 'csv=p=0', mp4]).stdout.strip()
        out['video'] = {'frames_captured': len(frames), 'frames_encoded': nf, 'expected': 93}
    return out


def arms_offset(cid, tier='48k', abduct=-10):
    """Set A: PINOC's Walking and Standing Idle as authored, the page's arm offset turned inward (abduct ABDUCT, forearm 0)."""
    d = dc.char_dir(cid); w = d / '09-game/wip'; r = w / 'rig'; e = d / '09-game/evidence/arms-down-offset'; e.mkdir(parents=True, exist_ok=True)
    rn = r / f'{cid}-{tier}-rig.npz'; skel = r / 'skeleton-data-441.ts'; lm = r / 'landmarks.json'
    tk = dc.load(cid)['stages']['09-game']['checks']['arms_down']['shoulder_phases']
    R.page_files(cid, tier)
    pid = profile_q(cid, 'armsoff', f'&abduct={abduct}&forearm=0', f'Arms-down evidence set A: the arm offset turned inward ({abduct} deg).')
    ang = {}
    for name, clip, t0, per, n in (('walk', 'walking.glb', 2.333, 1.033, 32), ('idle', 'standing-idle.glb', 0, 4, 16)):
        sh(['python3', HERE / 'arm_angles.py', rn, lm, w / 'clips' / clip, t0, per, '--n', n, '--abduct', abduct, '--skeleton', skel, '--json', e / f'angles-{name}.json'])
        ang[name] = json.load(open(e / f'angles-{name}.json'))['clip']
    tmp = e / 'scripts'; tmp.mkdir(exist_ok=True)
    sh(['bash', R.CAP / 'mkcycle.sh', tmp / 'cycle.js', 'front,quarter,side,back', 'native', '', 'walk', 2.333, 1.033, 8])
    out = capture(e, pid, {'cycle': None, 'idle': IDLE.replace("'idledown'", "'idle'").replace('(idledown)', '(idle, offset inward)'),
                           'shoulders': SHOULDERS.replace('$TIMES', json.dumps(tk)).replace("'walkdown'", "'walk'")},
                  video_clip='walk', vid_name=f'{cid}-walk-offset{abduct}-quarter.mp4')
    for nm, cols, rows, pat in (('cycle-sheet', 'p0,p1,p2,p3,p4,p5,p6,p7', 'front,quarter,side,back', 'cycle/{row}-{col}.png'),
                                ('idle-sheet', 'front,quarter,side,back', 'idle', 'idle/{row}-{col}.png')):
        rr = sh(['python3', R.SHEET, e / f'{nm}.png', cols, rows, e / pat])
        out[nm] = {'rc': rr.returncode, 'touching_edge': json.load(open(e / f'{nm}-tiles.json'))['touching_edge']}
    shoulders(e, list(tk))
    L = json.load(open(lm)); env2 = dict(os.environ, ASSET_CPU_REPO=str(dc.REPO / 'authoring/tools/people/splat'))
    hc = sh(['python3', r / 'hem_check.py', rn, w / f'fit{tier}/splat.npz', w / 'clips/walking.glb', 2.333, 1.033, 8, '--waist', L['skirt']['waist'],
             '--hem', L['skirt']['hem'], '--skeleton', skel, '--abduct', abduct], env=env2)
    hem = [l.strip() for l in hc.stdout.splitlines() if l.startswith('  ') and ('per phase' in l or 'torn' in l or 'hands' in l)]
    sw = {s: [p[1] for p in ang['walk'][s]['per_phase']] for s in 'lr'}
    rec = {'when': dc.NOW(), 'folder': '09-game/evidence/arms-down-offset', 'profile': pid, 'abduct': abduct,
           'walk_sideways': {s: [min(v), max(v), sum(0 <= x <= 8 for x in v), sum(x < 0 for x in v)] for s, v in sw.items()},
           'idle_sideways': {s: [ang['idle'][s]['sideways_min'], ang['idle'][s]['sideways_max']] for s in 'lr'},
           'shoulder_phases': tk, 'captures': out, 'hem_check': hem}
    dc.patch(cid, lambda m: m['stages']['09-game']['checks'].__setitem__('arms_down_offset', rec))
    print(json.dumps(rec, indent=1)[:2500])


def shoulder_compare(cid):
    """His left shoulder at 2.5x, front and back, at the three phases: the standard offset (10/15), set A (inward offset) and set B
    (arms-down edit). The standard frames are captured here with the standard profile at the same times."""
    from PIL import Image, ImageDraw
    d = dc.char_dir(cid); e = d / '09-game/evidence/arms-down'; tk = dc.load(cid)['stages']['09-game']['checks']['arms_down']['shoulder_phases']
    R.page_files(cid)
    std = e / 'standard'; std.mkdir(exist_ok=True)
    capture(std, f'dagger-{cid}', {'shoulders': SHOULDERS.replace('$TIMES', json.dumps(tk)).replace("'walkdown'", "'walk'")})
    sets = [('standard walk, offset 10/15', std / 'shoulders'), ('same walk, offset -10', d / '09-game/evidence/arms-down-offset/shoulders'),
            ('arms-down walk', e / 'shoulders')]
    tiles = []
    for ph in tk:
        row = []
        for view, cx in (('front', 960 + 150), ('back', 960 - 150)):   # his left: right of frame from the front, left from the back
            for name, folder in sets:
                im = Image.open(folder / f'{view}-{ph}.png').convert('RGB')
                t = im.crop((cx - 140, 324 - 140, cx + 140, 324 + 140)).resize((700, 700), Image.LANCZOS)
                ImageDraw.Draw(t).text((8, 6), f'{ph}, {view}: {name}', fill=(255, 255, 255)); row.append(t)
        tiles.append(row)
    sheet = Image.new('RGB', (706 * 6 - 6, 706 * len(tiles) - 6), (255, 255, 255))
    for i, row in enumerate(tiles):
        for j, t in enumerate(row):
            sheet.paste(t, (706 * j, 706 * i))
    sheet.save(e / 'shoulder-compare-2.5x.png')
    dc.patch(cid, lambda m: m['stages']['09-game']['checks']['arms_down'].__setitem__('shoulder_compare', '09-game/evidence/arms-down/shoulder-compare-2.5x.png'))
