"""09-game rig and checks (dagger-char run ID 09-game --step rig | checks), as townsman 385's runtime/rig.sh and its checks.

  rig     humanoid_splat_rig.py on each distilled tier, on a Strix Halo (authoring/tools/people/splat/hosts/strix-cpu/run.sh, asset-resources run), from the
          character's landmarks (09-game/wip/rig/landmarks.json, landmarks.py, checked by eye on landmarks.png) and its costume options
          (COSTUME below, recorded in the manifest): the 441-joint canonical skeleton, legs straightened onto the rest, the tunic or
          kilt weighted from the belt to the hem (--tunic), pouches / sporran hung from the pelvis (--hang, leather only), wide arm
          capsules. Writes 09-game/wip/rig/ID-TIER.vsplat, .json and -rig.npz.
  checks  on the 48k: weight totals (every Gaussian's weights sum to 1), the rest tear count (Gaussians more than 3 cm from all 8 of
          their static neighbours after re-posing), clip_fit.py (pelvis fit k, A and planted-foot drift on PINOC's free Walking and
          Standing Idle), shoe_track.py, and hem_check.py over one walk cycle (legs through the cloth, a torn hem, hands in the cloth).
"""
import json, os, shutil, subprocess
from pathlib import Path
import numpy as np
import dagger_char as dc

CRE = dc.REPO / 'authoring/tools/people/splat/gsplat'
SKEL = dc.REPO / 'tests/splats/extracted/sdk-source/src/loader/skeleton/skeleton-data-441.ts'
CLIPS = Path('/data/assets/splat-characters/pinoc-clips/splat-skeleton')
HALO = 'den-m5'
COMMON = ['--height', '1.72', '--repose-legs', '--level-hips', '--legs-by-bone', '--shoe-damp', '0', '--tunic',
          '--arm-radius', '1.2', '--arm-core', '1.8', '--arm-reach', '1.8', '--arm-to-tip', '--drop-stranded']
# --arm-no-cloth is per costume: on 387-A it left his dark glove and forearm undersides on the body beside the kilt (rest shards;
# hem check torn 21 -> 5 front, hands in 70 -> 0 without it). Each costume's options (lead, 2026-11-15): boxes are in the levelled final's frame, set by eye on the projections (hang.png).
COSTUME = {
    '383-A': {'what': 'thigh-length tunic with a yellow zigzag hem, grey cloth belt, olive pouch at the front of his right hip, slippers',
              'opts': ['--hang=-0.125,-0.045,0.0,0.13,0.02,0.11', '--hang-warm', '--arm-no-cloth', '--arm-no-cloth-yellow', '--hand-clusters', '0.02']},   # hem pieces and a trim clump went with his hands
    '386-C': {'what': 'knee-length sleeveless tunic, pale blue shoulder mantle (on the trunk), belt with a khaki pouch at his right hip, tall green boots',
              'opts': ['--hang=-0.115,-0.04,-0.01,0.10,0.05,0.125', '--hang-warm', '--arm-no-cloth']},   # teal tunic shards at the hands
    '387-A': {'what': 'green short-sleeved tunic, knee-length yellow-purple plaid kilt, leather sporran at the centre front, brown gloves, fur-cuffed boots',
              'opts': ['--hang=-0.065,0.065,-0.08,0.11,0.075,0.135', '--hang-warm']},
}


def sh(cmd, **kw):
    return subprocess.run(cmd, shell=isinstance(cmd, str), text=True, capture_output=True, **kw)


def rig(cid):
    m = dc.load(cid); d = dc.char_dir(cid); w = d / '09-game/wip'; r = w / 'rig'
    if not (r / 'landmarks.json').exists():
        raise SystemExit(f'{cid}: no landmarks (landmarks.py first, and check landmarks.png by eye)')
    shutil.copy2(SKEL, r / 'skeleton-data-441.ts')
    c = COSTUME[cid]; opts = COMMON + c['opts']; tiers = [t for t in ('48k', '96k', '16k') if (w / f'fit{t}/splat.npz').exists()]
    cmds = ' && '.join(f"python3 $ASSET_CPU_REPO/gsplat/humanoid_splat_rig.py fit{t}/splat.npz rig/landmarks.json rig/{cid}-{t}.vsplat "
                       f"--skeleton rig/skeleton-data-441.ts {' '.join(opts)} > rig/{cid}-{t}.out 2>&1" for t in tiers)
    res = sh(['asset-resources', 'run', '--host', HALO, '--resource', f"{HALO.split('-')[1]}-asset-cpu", '--owner', 'claude', '--purpose',
              f'dagger-char {cid} 09-game rig ({", ".join(tiers)})', '--', str(dc.REPO / 'authoring/tools/people/splat/hosts/strix-cpu/run.sh'), HALO, os.path.relpath(w, dc.REPO),
              '--', 'bash', '-c', cmds])
    out = {}
    for t in tiers:
        try:
            out[t] = json.loads((r / f'{cid}-{t}.out').read_text().strip().splitlines()[-1])
        except (OSError, IndexError, ValueError):
            out[t] = {'error': (r / f'{cid}-{t}.out').read_text()[-400:] if (r / f'{cid}-{t}.out').exists() else res.stderr[-400:]}
    bad = [t for t, v in out.items() if 'error' in v]

    def upd(m):
        st = m['stages']['09-game']; st.setdefault('checks', {})['rig'] = out
        st['rig_options'] = {'costume': c['what'], 'options': opts, 'landmarks': 'wip/rig/landmarks.json (landmarks.png)', 'host': HALO}
        for t in tiers:
            if t not in bad:
                dc.record_output(m, '09-game', f'vsplat-{t}', r / f'{cid}-{t}.vsplat')
        if bad:
            dc.set_state(m, '09-game', 'failed', f'rig failed on {", ".join(bad)}', by='run 09-game', why=f'rig failed ({", ".join(bad)})')
        else:
            dc.set_state(m, '09-game', 'running', 'rigged: ' + ', '.join(f"{t} {v.get('height_m')} m" for t, v in out.items()), by='run 09-game', why='checks next')
    dc.patch(cid, upd)
    if bad:
        raise SystemExit(f'{cid}: rig failed on {bad}')


def checks(cid, tier='48k'):
    """clip_fit, shoe_track and hem_check (arms at rest, and with 385's arm offset) on one tier, on the Halo; weights and rest tears here."""
    m = dc.load(cid); d = dc.char_dir(cid); w = d / '09-game/wip'; r = w / 'rig'; L = json.load(open(r / 'landmarks.json'))
    (w / 'clips').mkdir(exist_ok=True)
    for c in ('walking.glb', 'standing-idle.glb'):
        shutil.copy2(CLIPS / c, w / 'clips' / c)      # PINOC's free library clips: product data, never committed (outputs/ is git-ignored)
    shutil.copy2(Path(__file__).with_name('hem_check.py'), r / 'hem_check.py')
    rn = f'rig/{cid}-{tier}-rig.npz'; hem = f"--waist {L['skirt']['waist']} --hem {L['skirt']['hem']}"
    below = round(0.10 * 1.72 / (L['joints']['head_top'][1] - L['sole']) * (L['skirt']['hem'] - L['sole']) / 1.72 + 0.0, 3)
    cmd = (f"python3 $ASSET_CPU_REPO/gsplat/clip_fit.py {rn} rig/clip-fit-{tier}.json walk=clips/walking.glb idle=clips/standing-idle.glb "
           f"--skeleton rig/skeleton-data-441.ts > rig/clip-fit-{tier}.out 2>&1; "
           f"python3 $ASSET_CPU_REPO/gsplat/shoe_track.py {rn} rig/track-{tier}.json --below 0.10 --skeleton rig/skeleton-data-441.ts > rig/shoe-track-{tier}.out 2>&1; "
           f"python3 rig/hem_check.py {rn} fit{tier}/splat.npz clips/walking.glb 2.333 1.033 8 {hem} --skeleton rig/skeleton-data-441.ts > rig/hem-{tier}-arms0.out 2>&1; "
           f"python3 rig/hem_check.py {rn} fit{tier}/splat.npz clips/walking.glb 2.333 1.033 8 {hem} --skeleton rig/skeleton-data-441.ts --abduct 10 --forearm 15 > rig/hem-{tier}-arms10-15.out 2>&1; true")
    sh(['asset-resources', 'run', '--host', HALO, '--resource', f"{HALO.split('-')[1]}-asset-cpu", '--owner', 'claude', '--purpose',
        f'dagger-char {cid} 09-game checks ({tier})', '--', str(dc.REPO / 'authoring/tools/people/splat/hosts/strix-cpu/run.sh'), HALO, os.path.relpath(w, dc.REPO), '--', 'bash', '-c', cmd])
    z = np.load(r / f'{cid}-{tier}-rig.npz'); wt = z['weights'] if 'weights' in z.files else None
    res = {'weights_sum_min': round(float(wt.sum(1).min()), 4) if wt is not None else None, 'weights_sum_max': round(float(wt.sum(1).max()), 4) if wt is not None else None}
    for f in ('clip-fit', 'shoe-track'):
        o = r / f'{f}-{tier}.out'; res[f] = o.read_text().strip().splitlines()[-3:] if o.exists() else 'missing'
    for f in ('arms0', 'arms10-15'):
        o = r / f'hem-{tier}-{f}.out'; res[f'hem_{f}'] = [l.strip() for l in o.read_text().splitlines() if l.startswith('  ') and ('per phase' in l or 'torn' in l or 'hands' in l)] if o.exists() else 'missing'
    try:
        res['rig_summary'] = json.loads((r / f'{cid}-{tier}.json').read_text()).get('rest_tears') if (r / f'{cid}-{tier}.json').exists() else None
    except ValueError:
        pass
    dc.patch(cid, lambda m: m['stages']['09-game'].setdefault('checks', {}).__setitem__(f'checks_{tier}', res))
    print(json.dumps(res, indent=1)[:3000])


def glbs(cid):
    """Skinned splat GLBs for the Engine on both rigs for every rigged tier, as authoring/tools/people/splat/gsplat/town385_glbs.sh: surface
    normals and albedo from the levelled 400k (splat_surface_normals.py), the rest decoded back (inspect_vsplat.py), vsplat_to_glb.py
    --rig viggle441 and --rig mixamo (framed on PINOC's mixamo Walking), the Mixamo hips fit (splat_glb_clip_fit.py), and the glTF
    validator when it is in the npx cache. Into 09-game/game/{viggle441,mixamo,checks}/; nothing is published."""
    m = dc.load(cid); d = dc.char_dir(cid); w = d / '09-game/wip'; r = w / 'rig'; g = d / '09-game/game'
    mix = Path('/data/assets/splat-characters/pinoc-clips/mixamo'); res = {}
    for sub in ('viggle441', 'mixamo', 'checks', 'surface', 'rest-check'):
        (g / sub).mkdir(parents=True, exist_ok=True)
    for t in ('16k', '48k', '96k'):
        vs = r / f'{cid}-{t}.vsplat'
        if not vs.exists():
            continue
        surf = g / 'surface' / f'{cid}-{t}-surface.npz'
        a = sh(['python3', '-I', str(CRE / 'splat_surface_normals.py'), str(d / '08-final/final-level/splat.npz'), str(w / f'fit{t}/splat.npz'),
                str(r / f'{cid}-{t}-rig.npz'), str(surf), '--png', str(g / 'checks' / f'surface-{t}.png')])
        (g / 'checks' / f'surface-{t}.json').write_text(a.stdout)
        sh(['python3', '-I', str(dc.REPO / 'authoring/tools/people/splat/gsplat/inspect_vsplat.py'), str(vs), '--export-dir', str(g / 'rest-check' / t)])
        S = ['--surface', str(surf)] if surf.exists() else []
        for rig_, n in (('viggle441', None), ('mixamo', 22)):
            extra = ['--rig', rig_] + (['--expect-joints', str(n), '--mixamo-reference', str(mix / 'walking.glb')] if rig_ == 'mixamo' else [])
            o = sh(['python3', '-I', str(CRE / 'vsplat_to_glb.py'), str(vs), str(g / rig_ / f'{cid}-{t}.glb'), '--skeleton', str(SKEL),
                    '--check-ply', str(g / 'rest-check' / t / 'rest.ply')] + extra + S)
            (g / rig_ / f'{cid}-{t}.json').write_text(o.stdout + o.stderr)
            res[f'{rig_}-{t}'] = 'ok' if o.returncode == 0 and (g / rig_ / f'{cid}-{t}.glb').exists() else f'failed: {(o.stderr or o.stdout)[-300:]}'
        f = sh(['python3', '-I', str(CRE / 'splat_glb_clip_fit.py'), str(g / 'mixamo' / f'{cid}-{t}.glb'), str(g / 'checks' / f'mixamo-clip-fit-{t}.json'),
                f"walk={mix / 'walking.glb'}", f"idle={mix / 'standing-idle.glb'}:leg"])
        res[f'mixamo-fit-{t}'] = f.stdout.strip()[-300:]
    val = sh('ls -d "$HOME"/.npm/_npx/*/node_modules/gltf-validator 2>/dev/null | head -1').stdout.strip()
    if val:
        js = ("const v=require(process.argv[1]);const fs=require('fs');(async()=>{for(const f of process.argv.slice(2)){const r=await v.validateBytes("
              "new Uint8Array(fs.readFileSync(f)));const i=r.issues;console.log(`${f.split('/').pop()}: ${i.numErrors} errors, ${i.numWarnings} warnings`);}})();")
        v = sh(['node', '-e', js, val] + [str(p) for p in sorted(g.glob('*/*.glb'))]); res['gltf_validator'] = v.stdout.strip().splitlines()
    def upd(m):
        m['stages']['09-game'].setdefault('checks', {})['glbs'] = res
        for p in sorted(g.glob('*/*.glb')):
            dc.record_output(m, '09-game', f'{p.parent.name}-{p.stem}', p)
    dc.patch(cid, upd); print(json.dumps(res, indent=1)[:3000])


WEB = dc.AP / 'live-evidence/mobile30-splat-20261111-r01/runtime/web-dist/pinoc'   # what vite preview serves on 8990 (no rebuild)
CAP = dc.REPO / 'tests/splats/capture'
SHEET = Path(__file__).with_name('sheet.py')   # tiles centred on the figure; exit 3 when a figure touches its tile edge


def page_files(cid, tier='48k'):
    """The character on character.html (preview on 8990): the tier's .vsplat, the shoe track, PINOC's free clips and character.json
    with the clip fits and the arm offset; into the served web-dist only (its public/ twin is not git-ignored, and the clips must
    never be committed). A rebuild of the preview empties web-dist; this step puts them back."""
    m = dc.load(cid); w = dc.char_dir(cid) / '09-game/wip'; r = w / 'rig'; wd = WEB / f'dagger-{cid}'; wd.mkdir(parents=True, exist_ok=True)
    for f in (r / f'{cid}-{tier}.vsplat', r / f'track-{tier}.json', w / 'clips/walking.glb', w / 'clips/standing-idle.glb'):
        shutil.copy2(f, wd / f.name)
    for t in ('96k', '16k'):
        if (r / f'{cid}-{t}.vsplat').exists():
            shutil.copy2(r / f'{cid}-{t}.vsplat', wd)
    walk = json.load(open(r / f'clip-fit-{tier}.json'))['clips']['walk']
    if not (r / f'clip-fit-{tier}-idle-leg.json').exists():
        sh(['python3', '-I', str(CRE / 'clip_fit.py'), str(r / f'{cid}-{tier}-rig.npz'), str(r / f'clip-fit-{tier}-idle-leg.json'),
            f"idle={w / 'clips/standing-idle.glb'}", '--leg-ratio', '--skeleton', str(SKEL)])
    idle = json.load(open(r / f'clip-fit-{tier}-idle-leg.json'))['clips']['idle']
    rig_ = json.loads((r / f'{cid}-{tier}.out').read_text().strip().splitlines()[-1])
    cj = {'title': f"{cid}: {m['name']}", 'body': f'{cid}-{tier}.vsplat', 'height': round(rig_['height_m'], 3),
          'clips': {'idle': {'file': 'standing-idle.glb', 'label': 'Standing Idle', 'k': idle['k'], 'A': idle['A']},
                    'walk': {'file': 'walking.glb', 'label': 'Walking', 'k': walk['k'], 'A': walk['A']}},
          'defaultClip': 'walk', 'defaultSpeed': 1, 'defaultFinish': 'native', 'track': f'track-{tier}.json', 'arms': {'abduct': 10, 'forearm': 15},
          'about': f"Dagger character {cid} (local/art/generated/people/dagger-characters/{cid}): the levelled 2176 bake final distilled from 252 renders (game.py), "
                   f"rigged by humanoid_splat_rig.py with landmarks.py's landmarks and the options in character.json rig_options; walk k, A by "
                   f"clip_fit.py (least planted-foot drift), idle by leg ratio; arms 10 / 15 as townsman 385 (hem_check: no hands in the cloth).",
          'variants': {t: f'{cid}-{t}.vsplat' for t in ('96k', '16k') if (wd / f'{cid}-{t}.vsplat').exists()}}
    (wd / 'character.json').write_text(json.dumps(cj, indent=1))
    return wd


def evidence(cid, tier='48k'):
    """Held captures in the browser (tests/splats/capture: run.sh fails on a frame that played): the rest pose and Standing Idle from
    six views, eight phases of one walk cycle from three views, and a walk video built frame by frame (1/30 s steps over three
    cycles, root motion with the camera following, held per frame), frame-counted after encoding; sheets of each."""
    page_files(cid, tier); e = dc.char_dir(cid) / '09-game/evidence'; (e / 'scripts').mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, CAPTURE_SID=str(e / '.capture-sid'))
    run = lambda *c: subprocess.run([str(x) for x in c], cwd=e, env=env, text=True, capture_output=True)
    shutil.copy2(Path(__file__).with_name('rest.js'), e / 'scripts/rest.js')
    run('bash', CAP / 'start.sh', f'dagger-{cid}', 1920, 1080)
    out = {}
    for name, script in (('rest', 'scripts/rest.js'), ('cycle', 'scripts/cycle.js')):
        if name == 'cycle':
            run('bash', CAP / 'mkcycle.sh', 'scripts/cycle.js', 'quarter,side,back', 'native')
        shutil.rmtree(e / name, ignore_errors=True)
        r = run('bash', CAP / 'run.sh', script, name); out[name] = r.stdout.strip().splitlines()[-2:]
    for p in range(4):   # 93 frames in parts of 24 (the playtest budget)
        (e / 'scripts' / f'vid-{p}.js').write_text(VID.replace('$P', str(p)))
    shutil.rmtree(e / 'vid', ignore_errors=True)
    for p in range(4):
        r = run('bash', CAP / 'run.sh', f'scripts/vid-{p}.js', 'vid'); out[f'vid-{p}'] = r.stdout.strip().splitlines()[-1:]
    frames = sorted((e / 'vid').glob('f*.png'))
    sh(['ffmpeg', '-loglevel', 'error', '-y', '-framerate', '30', '-pattern_type', 'glob', '-i', str(e / 'vid/f*.png'), '-c:v', 'libx264', '-crf', '16',
        '-pix_fmt', 'yuv420p', '-vf', 'crop=1000:1080:460:0', str(e / f'{cid}-walk-quarter.mp4')])
    n = sh(['ffprobe', '-v', 'error', '-count_frames', '-select_streams', 'v', '-show_entries', 'stream=nb_read_frames', '-of', 'csv=p=0',
            str(e / f'{cid}-walk-quarter.mp4')]).stdout.strip()
    out['video'] = {'frames_captured': len(frames), 'frames_encoded': n, 'expected': 93}
    for sh_name, cols, rows, pat in (('rest-sheet', 'front,quarter,three-quarter,side,right-side,back', 'rest,idle', 'rest/{row}-{col}.png'),
                                     ('cycle-sheet', 'p0,p1,p2,p3,p4,p5,p6,p7', 'quarter,side,back', 'cycle/{row}-{col}.png')):
        r = sh(['python3', str(SHEET), str(e / f'{sh_name}.png'), cols, rows, str(e / pat)])
        out[sh_name] = {'rc': r.returncode, 'touching_edge': json.load(open(e / f'{sh_name}-tiles.json'))['touching_edge']}
    armpits(e)
    dc.patch(cid, lambda m: m['stages']['09-game'].setdefault('checks', {}).__setitem__('evidence', out))
    print(json.dumps(out, indent=1))



def armpits(e):
    """Both armpits at rest, front and back, at full capture resolution: the pale and teal shards there are a known fault."""
    from PIL import Image, ImageDraw
    tiles = []
    for view in ('front', 'back'):
        im = Image.open(e / f'rest/rest-{view}.png').convert('RGB'); W, H = im.size
        for side, cx in (('left of frame', W // 2 - 135), ('right of frame', W // 2 + 135)):   # each armpit centred (1.72 m, the rest camera)
            t = im.crop((cx - 130, int(H * 0.325) - 130, cx + 130, int(H * 0.325) + 130)).resize((560, 560), Image.LANCZOS)
            ImageDraw.Draw(t).text((8, 6), f'rest {view}, {side} (2x)', fill=(255, 255, 255)); tiles.append(t)
    sheet = Image.new('RGB', (566 * 4 - 6, 560), (255, 255, 255))
    for i, t in enumerate(tiles):
        sheet.paste(t, (566 * i, 0))
    sheet.save(e / 'armpits-rest.png')


VID = """// Walking video frames, part $P: 1/30 s steps from t 2.333 (93 frames: three cycles at 1x), quarter view, native, root motion on
// with the camera following, held per frame (#hold 1, never #play), as townsman 385's mkvid.sh.
const out = [];
await browser({op: 'fill', selector: '#hold', value: '1'});
if ($P === 0) await browser({op: 'click', selector: '#root'});
await browser({op: 'fill', selector: '#clip', value: 'walk'});
await browser({op: 'fill', selector: '#fx', value: 'native'});
await browser({op: 'fill', selector: '#view', value: 'quarter'});
for (let i = $P * 24; i < Math.min(93, ($P + 1) * 24); i++) {
  const label = 'f' + String(i).padStart(3, '0');
  await browser({op: 'fill', selector: '#time', value: (2.333 + i / 30).toFixed(4)});
  await browser({op: 'fill', selector: '#tag', value: label});
  await browser({op: 'click', selector: '#apply'});
  await sleep(300);
  out.push([label, (await capture({label})).path]);
}
const page = await browser({op: 'inspect'});
checkpoint('frames', {frames: out, console: (page.console || []).map((m) => m.text).filter((t) => /^(seen|applied) /.test(t))});
"""


NO_FLOATER_PASS = {'387-A'}   # no floaters; the 3 cm rule took only lone points and chest-front bits at 16k


def boundclips(cid, tiers=('48k', '96k', '16k')):
    """The free body on the bound route's clips (Patch 2026-11-16: the PINOC walk with arms out made rig quality hard to judge):
    385's retargeted Walking (Mixamo, Patch's download), Idle (150 frames) and Around, through clip_compare.py prep on each tier's
    Mixamo-rig GLB (09-game/game/mixamo) and export_workbench.py, into 09-game/wip/free-boundclip-playable. The tiers were rigged
    separately and their skeletons differ by under 4 mm, so every tier plays the first tier's poses on its rig (a set has one rig)."""
    import numpy as np
    d = dc.char_dir(cid); g = d / '09-game/game/mixamo'; w = d / '09-game/wip'; prep = w / 'free-boundclip-prep'; out = w / 'free-boundclip-playable'
    C = dc.char_dir('385') / '09-game/bound/clips'; T = dc.REPO / 'authoring/tools/people/splat/scaffold'
    shutil.rmtree(prep, ignore_errors=True); shutil.rmtree(out, ignore_errors=True)
    (w / 'unit-transform.json').write_text('{"scale": 1.0}\n')   # the Mixamo GLBs are in metres already
    cmd = ['python3', T / 'clip_compare.py', 'prep', prep, '--metres', '1', '--views', 'quarter',
           '--clip', f'walking={C / "walking.json"}', '--clip', f'idle={C / "idle.json"}:150', '--clip', f'around={C / "around.json"}']
    cmd += sum((['--free', f'{t}={g / f"{cid}-{t}.glb"}'] for t in tiers), [])
    r = sh(cmd)
    from scipy.spatial import cKDTree
    from scipy.sparse.csgraph import connected_components
    from scipy.sparse import coo_matrix
    dropped = {}
    for t in (() if cid in NO_FLOATER_PASS else tiers):   # floaters (Patch 2026-11-16, 383-A and 386-C): every Gaussian not linked to the body by steps under 3 cm at rest
        sp = dict(np.load(prep / f'{t}.npz')); bd = dict(np.load(prep / f'{t}-binding.npz')); n = len(sp['means'])
        pairs = cKDTree(sp['means']).query_pairs(0.03, output_type='ndarray')
        _, lab = connected_components(coo_matrix((np.ones(len(pairs)), (pairs[:, 0], pairs[:, 1])), shape=(n, n)), directed=False)
        keep = lab == np.bincount(lab).argmax(); dropped[t] = int(n - keep.sum())
        np.savez(prep / f'{t}.npz', **{k: v[keep] for k, v in sp.items()}); np.savez(prep / f'{t}-binding.npz', **{k: v[keep] for k, v in bd.items()})
    (prep / 'floaters.json').write_text(json.dumps({'rule': 'not in the largest 3 cm-linked component at rest', 'dropped': dropped}) + '\n')
    for t in tiers[1:]:
        for c in ('walking', 'idle', 'around'):
            src = dict(np.load(prep / f'poses-{tiers[0]}-{c}.npz')); np.savez_compressed(prep / f'poses-{t}-{c}.npz', **src)
    sh(['python3', T / 'export_workbench.py', g / f'{cid}-{tiers[0]}.glb', w / 'unit-transform.json', prep, out] + sum((['--body', f'{t}={t}'] for t in tiers), []))
    print(r.stdout.strip(), 'floaters dropped:', dropped); return out


def publish(cid, tier='48k'):
    """The free (unbound) body as a playable Workbench set (dagger-char run ID 09-game --step publish): boundclips' folder (the
    bound route's clips on the free body's Mixamo rig, 48k with 96k and 16k variants) and the review batch's rest still.
    A published set is never replaced: each call makes the next -rNN."""
    import datetime, hashlib
    m = dc.load(cid); d = dc.char_dir(cid); out = d / '09-game/wip/free-boundclip-playable'
    if not (d / '09-game/wip/free-boundclip-prep/floaters.json').exists():
        boundclips(cid)
    cj = json.load(open(out / 'character.json'))
    rb = d / '09-game/review-batch'; root = dc.REPO; pubs = dc.AP / 'live-evidence/publications'
    def member(mid, p, roles, media):
        return {'id': mid, 'roles': roles, 'publishedPath': p.name if p.parent == out else 'still.png',
                'source': {'path': str(p.relative_to(root)), 'mediaType': media, 'byteSize': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}}
    ms = [member('character', out / 'character.json', ['primary'], 'application/vnd.viggle.splat-character+json'),
          member('rig', out / cj['rig'], ['rig', 'animation'], 'model/gltf-binary')]
    ms += [member(Path(b).stem, out / b, ['model'], 'application/octet-stream') for b in [cj['body']] + list(cj.get('variants', {}).values())]
    ms += [member('still', rb / '01-rest-and-idle.png', ['image', 'representative'], 'image/png')]
    faults = sorted(p.stem.split('-', 1)[1] for p in rb.glob('2*.png'))
    day = datetime.date.today().strftime('%Y%m%d'); n = 1; slug = 'town' + cid.lower()
    while (pubs / 'imports' / f'{slug}-free-playable-{day}-r{n:02d}').exists():
        n += 1
    rid = f'{slug}-free-playable-{day}-r{n:02d}'
    req = {'schemaVersion': 1, 'id': rid, 'kind': 'splat-character', 'title': f"Villager {cid}: free bake on the bound clips, playable (48k; 96k and 16k)",
           'primary': 'character', 'representative': 'still', 'members': ms,
           'comparison': {'runFamily': f'{slug}-runtime', 'subjectId': slug, 'variantId': rid},
           'provenance': {'producer': 'dagger-char-free-publish', 'settings': {'rig': 'the free body\'s Mixamo rig (22 bones, vsplat_to_glb.py --rig mixamo)',
                                                                               'clips': 'the bound route\'s Walking, Idle and Around (Mixamo, retargeted by retarget_clip.py, 30 fps)'}},
           'properties': {'notes': f"{cid} ({m['name']}) as the free (unbound) route left it: the 400k levelled bake distilled to 48k, 96k and 16k and rigged by "
                                   f"humanoid_splat_rig.py, on its Mixamo rig with the same Walking, Idle and Around the bound characters play (no arm offset). " +
                                   (f"Floaters removed (Gaussians not linked to the body by steps under 3 cm: {json.load(open(d / '09-game/wip/free-boundclip-prep/floaters.json'))['dropped']}). " if cid not in NO_FLOATER_PASS else 'No floater pass (none seen). ') +
                                   f"Pick the 96k or 16k body under Preview member; they play the 48k's skeleton (theirs differ by under 4 mm). "
                                   f"Faults in its 2026-11-15 review batch (before this): {', '.join(faults) or 'none listed'}. The still is from that batch (PINOC clips). "
                                   f"Files: local/art/generated/people/dagger-characters/{cid}/09-game/.", 'evidenceRoot': str(d / '09-game') + '/'}}
    w = d / '09-game/wip' / 'free-publish'; w.mkdir(parents=True, exist_ok=True); (w / 'import-request.json').write_text(json.dumps(req, indent=1))
    rev = subprocess.run(['git', '-C', str(root), 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
    x = subprocess.run([str(root / 'legacy/tools/asset-cli/target/debug/asset'), 'import', '--source-root', str(root), '--workspace', str(w / f'ws-{rid}'),
                        '--request', str(w / 'import-request.json'), '--run-id', rid, '--importer-revision', rev, '--gallery-root', str(pubs)], capture_output=True, text=True)
    if x.returncode:
        raise SystemExit(f'publish {rid} failed: {x.stdout[-800:]} {x.stderr[-800:]}')
    (w / f'receipt-{rid}.json').write_text(x.stdout)
    url = 'http://192.168.1.10:8960/#?root=active-evidence&id=import%3A' + rid; print(rid, url)

    def upd(m):
        dc.log(m, '09-game', f'published {rid}', url, by='rig publish')
    dc.patch(cid, upd)
