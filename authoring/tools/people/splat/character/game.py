"""dagger-char run ID 09-game --step STEP: the game asset from an approved final, as townsman 385 was made (docs/viggle-splats.md,
"Townsman 385 from the 2176² bake" and "Skinned splat GLBs for the Engine"; live-evidence/town385-hires-20261112-r01/runtime/).

  level    the levelled final (08-final/final-level, level_splat.py) and its transform are recorded as this stage's inputs: the rig
           assumes Y up, and every accepted final leaned 3.8-6.7 deg in its fit frame
  distil   on the GPU lab: 252 RGBA renders of the levelled 400k at 2176² (distill_dataset.py: 7 rings of 36, -55..60 deg, 2.25 units,
           30 deg field, every 12th held back), then per tier (48k, 96k, 16k; the renders reused) budget_seed.py + train_loop_splat.py
           (15000 steps, exact cameras) - each tier its own lab-lock job (asset-resources run), the planned ComfyUI restart rule before
           each (restart only above 20 GB RSS with an empty queue); the held-back score per tier goes in the manifest
The rig, checks, GLBs and evidence steps follow (rig.py)."""
import json, os, re, shlex, subprocess, tempfile, time
from pathlib import Path
import dagger_char as dc

LAB = Path('~/server-access/gpu-lab/connect').expanduser(); PY = '/workspace/splat-probe/venv/bin/python'
CRE = dc.REPO / 'authoring/tools/people/splat/gsplat'
TIERS = [('48k', 48000), ('96k', 96000), ('16k', 16000)]
RESTART_LOG = dc.AP / 'live-evidence/villagers-batch1-20261113-r01/stab-vram.log'   # the log comfy_abort_watch.sh reads for planned restarts
TRAIN = '--steps 15000 --ring 0 --freeze-poses --anchor-weight 1 --opacity-reg 0'


def sh(cmd):
    return subprocess.run(cmd, shell=True, text=True, capture_output=True)


def lab_job(purpose, ws, cmd, log):
    """One command in the character's lab workspace, under our lab lock and the Resource Board; the planned ComfyUI restart rule first."""
    inner = (f"{dc.REPO}/authoring/tools/people/splat/bake/lab-comfy-restart.sh {RESTART_LOG} --in-lock --if-rss-gb 20 >> {log} 2>&1; "
             f"asset-resources run --host gpu-lab --resource lab-gsplat --owner claude --purpose {shlex.quote(purpose)} -- "
             f"{LAB} \"gpu-lab exec bash -c 'echo 1000 > /proc/self/oom_score_adj; export TORCH_EXTENSIONS_DIR=/cache/torch_extensions/py312_cu130 "
             f"OMP_NUM_THREADS=2; cd {ws} && {cmd}'\" 2>&1 | grep -v -e Container -e '^resource-board' >> {log}")
    r = sh(f"flock -w 14400 /tmp/gpu-lab-claude.lock bash -c {shlex.quote(inner)}")
    return r.returncode


def level(cid):
    m = dc.load(cid); d = dc.char_dir(cid); s = '09-game'
    lv = d / '08-final/final-level'
    if not (lv / 'splat.npz').exists():
        raise SystemExit(f'{cid}: no levelled final at {lv}')
    dc.record_output(m, '08-final', 'splat-level', lv / 'splat.npz'); dc.record_input(m, s, 'splat-level', '08-final', 'splat-level')
    tf = json.load(open(lv / 'transform.json'))
    m['stages'][s]['checks']['level_transform'] = tf
    m['stages'][s]['checks']['frame_lean_deg'] = (m['stages']['08-final'].get('checks') or {}).get('frame_lean_deg')
    dc.set_state(m, s, 'running', 'levelled final recorded as the input (Y up, front +Z); distil next', by='run 09-game', why='distil next')
    dc.save(m)


def distil(cid, tiers=TIERS):
    m = dc.load(cid); d = dc.char_dir(cid); w = d / '09-game/wip'; w.mkdir(parents=True, exist_ok=True); log = w / 'distil.log'
    ws = f'/workspace/dagger-game-{cid}'
    with tempfile.TemporaryDirectory() as t:
        os.makedirs(f'{t}/src'); sh(f'cp {d}/08-final/final-level/splat.npz {t}/src/splat.npz')
        sh(f'cp {CRE}/budget_seed.py {CRE}/train_loop_splat.py {CRE}/distill_dataset.py {t}/')
        r = sh(f"flock -w 14400 /tmp/gpu-lab-claude.lock bash -c {shlex.quote(f'{LAB} \"gpu-lab exec mkdir -p {ws}\" > /dev/null 2>&1; tar czf - -C {t} . | {LAB} \"gpu-lab exec tar xzf - -C {ws}\"')}")
    for name, count in tiers:
        t0 = time.time()
        cmd = (f"( [ -f dist-data/views.json ] || {PY} distill_dataset.py src/splat.npz dist-data --size 2176 --distance 2.25 --elevations=-55,-35,-15,0,15,35,60 ) "
               f"&& rm -rf fit{name} fit{name}-data && {PY} budget_seed.py dist-data src/splat.npz fit{name}-data --count {count} --link "
               f"&& {PY} -u train_loop_splat.py fit{name}-data fit{name} --cap {count} {TRAIN} > fit{name}.log 2>&1; grep -e SCORE -e rror -e Traceback fit{name}.log")
        with open(log, 'a') as f:
            f.write(f'\n== {cid} {name} ({count}) {dc.NOW()}\n')
        done = sh(f"{LAB} 'gpu-lab exec bash -c \"test -f {ws}/fit{name}/splat.npz && grep -c SCORE {ws}/fit{name}.log\"' 2>/dev/null").stdout.strip().endswith('1')
        if done:   # resume: a tier already trained (its splat and its SCORE line) is collected, not retrained
            rc = 0
            with open(log, 'a') as f:
                f.write('already trained on the lab: collected\n')
        else:
            rc = lab_job(f'dagger-char {cid} 09-game distil {name}', ws, cmd, log)
        sh(f"{LAB} 'gpu-lab exec tar czf - -C {ws} fit{name} fit{name}.log --exclude=*.ply' 2>/dev/null | tar xzf - -C {w}")
        sc = {}
        try:
            line = [l for l in open(w / f'fit{name}.log') if l.startswith('SCORE')][-1]; s_ = json.loads(line[6:])
            h_ = s_.get('heldout') or s_.get('exact')   # a distil's held-back views are its 'exact' role
            sc = {'heldout_subject_psnr': h_['subject_psnr'], 'anchor_subject_psnr': s_['anchor']['subject_psnr']}
        except (OSError, IndexError, ValueError, KeyError):
            pass
        ok = bool(rc == 0 and (w / f'fit{name}/splat.npz').exists() and sc); sec = round(time.time() - t0)

        def upd(m, name=name, count=count, sc=sc, ok=ok, sec=sec, rc=rc):
            m['stages']['09-game'].setdefault('checks', {}).setdefault('distil', {})[name] = dict(sc, count=count, seconds=sec, renders=252, rc=rc)
            if ok:
                dc.record_output(m, '09-game', f'distil-{name}', w / f'fit{name}/splat.npz')
            else:
                dc.set_state(m, '09-game', 'failed', f'distil {name} failed (rc {rc}); see 09-game/wip/distil.log', by='run 09-game', why=f'distil {name} failed')
        dc.patch(cid, upd)
        if not ok:
            raise SystemExit(f'{cid}: distil {name} failed')
    dc.patch(cid, lambda m: dc.set_state(m, '09-game', 'running', 'distilled: ' + ', '.join(
        f"{k} {v.get('heldout_subject_psnr')} dB" for k, v in m['stages']['09-game']['checks']['distil'].items()), by='run 09-game', why='rig next'))
