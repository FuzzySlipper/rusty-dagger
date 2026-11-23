#!/usr/bin/env python3
"""dagger-char: the dagger character factory - one folder per character, numbered stages, one manifest, one status page
(Patch, 2026-11-15: "each character has a directory, source images, wip raw assets, prompts, approved concept, approved video, etc all
the way to final splat, final splat bake ... a static page with a script refresh showing the roughly 8 steps").

    dagger-char init ID [--name NAME]          make ROOT/ID/ with the stage folders and character.json
    dagger-char status [ID]                    the state of every stage (one line per character, or one per stage)
    dagger-char set ID STAGE STATE [--note T]  change a stage's state by hand (recorded in the history with who and why)
    dagger-char adopt [ID ...]                 bring existing work in from live-evidence/ (hard links; nothing there is moved)
    dagger-char run ID 09-game --step STEP     level | distil | ... (game.py): the game asset from an approved final
    dagger-char review ID STAGE                put a stage in front of Patch as a Workbench batch (08-final: levelled sheets), record it
    dagger-char approve ID STAGE [--by WHO]    a gate takes Patch's marks from its recorded batch (refuses without one); others: --by
                [--patch-said TEXT --qualifier testing --on DATE]   his decision in words where the marks are a Maybe (never over a Bad)
    dagger-char name ID TEXT | recipe ID R     change a display name or the recipe a character is on
    dagger-char bound ID [--step adopt|playable|publish|glb|fit|eyes|choose --size NAME --patch-said TEXT]
                                               the bound-scaffold route (bound.py): file a scaffold run under the stages, write the
                                               playable sets, publish them to the Workbench, record the size chosen as the final
    dagger-char marks [ID ...]                 re-read Patch's marks on the shared concept batch for characters still at the concept
    dagger-char page                           write ROOT/index.html and ROOT/ID/index.html (also: refresh)

ROOT is config.json's 'root' (local/art/generated/people/dagger-characters in this repository, git-ignored; DAGGER_CHAR_ROOT overrides). Every stage folder has wip/ (raw attempts; nothing downstream
reads it) and its named outputs. character.json is the single source of status: per stage a state, its attempts and the selected
one(s), inputs with hashes (a changed input makes the stage stale), outputs with hashes, check numbers, review batch and Patch's
marks, times, host, tool commit, provenance and a one-line note. A stage also lists its items: every file or folder worth naming, each with a
role (ROLES below: final, candidate, source, intermediate, superseded, evidence) and the route it belongs to, so the page can say what is a final
and what is working material. Stale is computed from the hashes, never set. Stage tools write it;
people change states only through this CLI. This tool reads and writes files; it imports neither Rusty Dagger nor the Workbench."""
import argparse, datetime, getpass, hashlib, json, os, subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[5]   # rusty-dagger (moved from asset-pipeline 2026-11-22, Patch)
AP = Path(os.environ.get('ASSET_PIPELINE') or Path.home() / 'dev/asset-pipeline')   # the Workbench importer and gallery, the older runs' live-evidence
CONFIG = json.load(open(Path(__file__).with_name('config.json')))
ROOT = Path(os.environ.get('DAGGER_CHAR_ROOT') or (REPO / CONFIG['root'])).expanduser()   # the one setting: config.json 'root'
SCHEMA = 1
# key, title, Patch's gate?, what the next stage reads
STAGES = [
    ('00-brief', 'Brief', False, 'prompt text, describing text, seeds, recorded graph'),
    ('01-concept', 'Concept', True, 'approved front and back paintings, their keyframes at clip size, pair-check numbers'),
    ('02-guide', 'Scaffold', False, 'the rigged clay scaffold (02-guide/scaffold): clay front and back, mesh, rig, hands; the bake is held to it and animated by it'),
    ('03-clip', 'Clip', True, 'approved clip(s), each with a role (sweep, head, ...)'),
    ('04-cutouts', 'Cut-outs', False, 'mattes per clip, holes.json, the cut-out review (a glance, not a gate)'),
    ('05-cameras', 'Cameras', False, 'Solv3R solve, fused cloud, loop data; clip agreement and registration for several clips'),
    ('06-first-fit', 'First fit', False, 'round-0 splat, scores, residuals, levelled review'),
    ('07-refine', 'Refine', False, 'VACE repaint round with colour match and ignore masks, then round 1 (part of the accepted recipe)'),
    ('08-final', 'Final bake', True, 'the full-size bake held to the scaffold (08-final/bound), its binding and review (Patch looks before 09-game starts)'),
    ('09-game', 'Game', False, 'the runtime body (09-game/bound): 16k with a quarter of the Gaussians on the face unless another size is chosen'),
]
STAGE_KEYS = [s[0] for s in STAGES]
# The recipe a character is on (the page says which). accepted-2176-repaint made the accepted bakes: a 2176^2 pinned FL2VA clip
# (keyframes as guides), then the VACE repaint round with colour match and ignore masks. experimental-sweep-head: Blender-guided lit-clay
# sweep (141 frames at 1088^2) plus a head path, region-owned joint fit - an alternative clip route, not the default.
# bound-scaffold (the default since Patch, 2026-11-15: "I'm pretty happy with this workflow so the bound bake can now become the default"): the same
# clip, cut-outs, cameras, first fit and repaint round, then the full-size bake is held to a rigged clay scaffold made from the approved paintings
# (docs/splat-scaffold.md, bound.py) and the runtime body is distilled from it. accepted-2176-repaint is the free bake those characters were made with.
RECIPES = {'bound-scaffold': 'bound: 2176² pinned clip + repaint round, baked onto a rigged clay scaffold',
           'accepted-2176-repaint': 'free bake: 2176² pinned clip + VACE repaint round (before the bound route)',
           'experimental-sweep-head': 'experimental: guided sweep + head path'}
DEFAULT_RECIPE = 'bound-scaffold'
# What an item at a stage is (Patch, 2026-11-15: "I do lose track sometimes of what are intermediate and finals"). The page shows the role beside
# every item; the words under each are what the page's legend says.
ROLES = {'final': 'what the game or the next stage uses',
         'alternate': 'a finished option that was not chosen; can be switched to',
         'candidate': 'a finished option; one becomes the final when it is chosen',
         'source': 'a full-size result that others are made from; kept, not a game file',
         'intermediate': 'working files a later step needed; nothing to judge',
         'superseded': 'replaced by a later attempt; kept for the record',
         'evidence': 'sheets, videos and scores to judge by'}
ROUTES = {'free': 'free bake (the accepted recipe)', 'bound': 'bound to a rigged clay scaffold (experiment)'}
STATES = ['empty', 'running', 'failed', 'needs-check', 'needs-Patch', 'approved', 'rejected', 'on-hold', 'skipped']
NOW = lambda: datetime.datetime.now().astimezone().isoformat(timespec='seconds')


def tool_commit():
    try:
        return subprocess.run(['git', '-C', str(REPO), 'rev-parse', '--short', 'HEAD'], capture_output=True, text=True).stdout.strip()
    except OSError:
        return None


def file_hash(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for b in iter(lambda: f.read(1 << 20), b''):
            h.update(b)
    return h.hexdigest()


def path_hash(p):
    """sha256 of a file, or of a folder's sorted (relative path, file hash) list."""
    p = Path(p)
    if p.is_file():
        return file_hash(p)
    h = hashlib.sha256()
    for f in sorted(x for x in p.rglob('*') if x.is_file()):
        h.update(f'{f.relative_to(p)}\0{file_hash(f)}\n'.encode())
    return h.hexdigest()


def char_dir(cid):
    return ROOT / cid


def blank_stage(key):
    return {'state': 'empty', 'note': '', 'attempts': [], 'selected': [], 'inputs': {}, 'outputs': {}, 'checks': {}, 'review': {},
            'times': {}, 'host': None, 'tool_commit': None, 'provenance': None, 'updated': None}


def init(cid, name=None, meta=None):
    d = char_dir(cid)
    for k in STAGE_KEYS:
        (d / k / 'wip').mkdir(parents=True, exist_ok=True)
    mp = d / 'character.json'
    if mp.exists():
        return load(cid)
    m = {'schema': SCHEMA, 'id': cid, 'name': name or cid, 'recipe': DEFAULT_RECIPE, 'created': NOW(), 'meta': meta or {},
         'stages': {k: blank_stage(k) for k in STAGE_KEYS}, 'history': []}
    save(m)
    return m


def load(cid):
    return json.load(open(char_dir(cid) / 'character.json'))


def save(m):
    p = char_dir(m['id']) / 'character.json'; t = p.with_suffix('.json.tmp')
    t.write_text(json.dumps(m, indent=1) + '\n'); t.replace(p)   # atomic: the page never reads a half-written manifest


def patch(cid, fn):
    """Change a manifest safely while other steps run: under an exclusive lock on ID/character.json.lock, load the LATEST manifest,
    apply fn(m) and save. Long steps (a distil, a rig) compute first and patch at the end, so one step never overwrites another's
    fields with a copy it loaded earlier (2026-11-15: the distil chain's saves wiped the rig's records)."""
    import fcntl
    with open(char_dir(cid) / 'character.json.lock', 'w') as lk:
        fcntl.flock(lk, fcntl.LOCK_EX)
        m = load(cid); r = fn(m); save(m)
    return r


def log(m, stage, event, note='', by=None):
    m['history'].append({'time': NOW(), 'stage': stage, 'event': event, 'by': by or getpass.getuser(), 'note': note})


def set_state(m, stage, state, note='', by=None, why=None, **fields):
    """why: the few words the page shows on a stage that is not approved (the failing check and its number, what it waits on)."""
    assert stage in STAGE_KEYS, stage
    assert state in STATES, state
    s = m['stages'][stage]; old = s['state']; s['state'] = state; s['updated'] = NOW(); s['why'] = why or ''
    if note:
        s['note'] = note
    s.update(fields)
    log(m, stage, f'{old} -> {state}', note, by)


def record_output(m, stage, name, path):
    """A named output the next stage reads, with its hash (paths relative to the character folder)."""
    p = Path(path); rel = os.path.relpath(p, char_dir(m['id']))
    m['stages'][stage]['outputs'][name] = {'path': rel, 'sha256': path_hash(p)}


def record_item(m, stage, name, role, path, what, route=None, numbers=None, links=None, made_from=None):
    """One line of a stage's ledger: a file or folder (relative to the character folder when it is inside it), its role (ROLES), the
    route it belongs to (ROUTES), what it is in a few words, its key numbers, links (label -> URL) and what it was made from.
    Recording a name again replaces its line."""
    assert role in ROLES, role
    assert route is None or route in ROUTES, route
    p = Path(path); d = char_dir(m['id'])
    if not p.is_absolute():
        p = d / p
    if not p.exists():
        raise SystemExit(f'{m["id"]} {stage} item {name}: {p} does not exist')
    inside = d.resolve() in p.resolve().parents or p.resolve() == d.resolve()
    it = {'name': name, 'role': role, 'route': route, 'path': os.path.relpath(p, d) if inside else str(p), 'what': what,
          'numbers': numbers or {}, 'links': links or {}, 'made_from': made_from or '', 'recorded': NOW()}
    items = m['stages'][stage].setdefault('items', [])
    items[:] = [x for x in items if x['name'] != name] + [it]


def record_input(m, stage, name, from_stage, output):
    """An input taken from an upstream stage's named output; its hash at the time is kept for the stale check."""
    o = m['stages'][from_stage]['outputs'][output]
    m['stages'][stage]['inputs'][name] = {'from': f'{from_stage}:{output}', 'path': o['path'], 'sha256': o['sha256']}


def stale(m, stage):
    """Reasons this stage is stale: an input whose upstream output has changed (or gone) since the stage used it."""
    why = []
    for n, i in m['stages'][stage].get('inputs', {}).items():
        if 'from' not in i:
            continue
        fs, fo = i['from'].split(':', 1); o = m['stages'][fs]['outputs'].get(fo)
        if o is None:
            why.append(f'{n}: {i["from"]} no longer exists')
        elif o['sha256'] != i['sha256']:
            why.append(f'{n}: {i["from"]} changed since it was used')
    return why


def disk():
    """(GB the folder shows, GB only it holds - files not hard-linked from elsewhere, GB free on its disk)."""
    import shutil as sh
    shown = own = 0
    if ROOT.exists():
        for f in ROOT.rglob('*'):
            if f.is_file() and not f.is_symlink():
                st = f.stat(); shown += st.st_size; own += st.st_size if st.st_nlink == 1 else 0
    g = lambda x: round(x / 2 ** 30, 1)
    return g(shown), g(own), g(sh.disk_usage(ROOT if ROOT.exists() else ROOT.parent).free)


def characters():
    return sorted((p.name for p in ROOT.iterdir() if (p / 'character.json').exists()), key=lambda x: (len(x.split('-')[0]), x)) if ROOT.exists() else []


def cmd_status(a):
    size, own, free = disk()
    if free < CONFIG.get('warn_free_gb', 100):
        print(f'WARNING: {free} GB free on the disk holding {ROOT} (under {CONFIG.get("warn_free_gb", 100)} GB)')
    ids = [a.id] if a.id else characters()
    if a.id:
        m = load(a.id)
        print(f"{m['id']}  {m['name']}  [{m.get('recipe')}]")
        for k, t, gate, _ in STAGES:
            s = m['stages'][k]; st = stale(m, k)
            print(f"  {k:13s} {s['state']:12s}{' STALE' if st else ''}{' (gate)' if gate else ''}  {s.get('note', '')[:90]}")
        return
    print('id      ' + ' '.join(f'{t[:9]:9s}' for _, t, _, _ in STAGES))
    for cid in ids:
        m = load(cid)
        print(f'{cid:7s} ' + ' '.join(f"{m['stages'][k]['state'][:9]:9s}" for k in STAGE_KEYS))
    print(f'{ROOT}: {size} GB shown, {own} GB its own (the rest hard-linked from live-evidence), {free} GB free')


def cmd_approve(a):
    """Patch's gates (concept, clip, final) take their state from his marks on the stage's recorded Workbench batch: any Bad ->
    rejected, a Good with no Bad -> approved (by Patch, batch, date), otherwise still needs-Patch. Other stages: approved by --by."""
    import urllib.request
    m = load(a.id); st = m['stages'][a.stage]; gate = dict((k, g) for k, _, g, _ in STAGES)[a.stage]
    if not gate:
        set_state(m, a.stage, 'approved', a.note or '', by=a.by, approved_by=a.by or getpass.getuser()); save(m); return
    b = (st.get('review') or {}).get('batch')
    if not b:
        raise SystemExit(f'{a.id} {a.stage} is one of Patch\'s gates and has no review batch recorded: run `dagger-char review` first')
    batch = json.load(urllib.request.urlopen(f"http://192.168.1.10:8960/api/reviews/{b['id']}", timeout=10))['batch']
    marks = [{'batch': b['id'], 'url': b['url'], 'batch_created': batch['createdAt'][:16], 'title': i['title'], 'mark': i['mark'], 'note': i.get('note') or ''}
             for i in batch['items'] if not b.get('titles') or i['title'] in b['titles']]   # a batch shared by several characters names this one's items
    st.setdefault('review', {})['marks'] = [x for x in st['review'].get('marks', []) if x.get('batch') != b['id']] + marks
    bad = [x for x in marks if x['mark'] == 'bad']; good = [x for x in marks if x['mark'] == 'good']; maybe = [x for x in marks if x['mark'] == 'maybe']
    marked = '; '.join(f"{x['title']}: {x['mark']}" + (f" ({x['note']})" if x['note'] else '') for x in marks if x['mark'] != 'unreviewed')
    if a.patch_said and not bad:   # his decision in words, relayed by the lead, where the marks alone would not approve (a Maybe)
        set_state(m, a.stage, 'approved', f"Patch ({a.on or dc_date()}): \"{a.patch_said}\" - marks: {marked}" + (' - note the Maybe' if maybe else ''),
                  by='approve', approved_by=f"Patch{' for ' + a.qualifier if a.qualifier else ''}, {a.on or dc_date()} ({b['id']}: {marked})",
                  approval=a.qualifier or '')
    elif bad:
        set_state(m, a.stage, 'rejected', f"Patch: Bad on {bad[0]['title']}" + (f" - {bad[0]['note']}" if bad[0]['note'] else ''), by='approve',
                  why=f"Patch: Bad ({b['id']})")
    elif good:
        set_state(m, a.stage, 'approved', f"Patch: Good in {b['id']}" + (f" - {good[0]['note']}" if good[0]['note'] else ''), by='approve',
                  approved_by=f"Patch: Good in {b['id']} ({dc_date()})")
    else:
        print(f"{a.id} {a.stage}: no Good or Bad from Patch yet in {b['id']} ({sum(x['mark'] == 'unreviewed' for x in marks)} unreviewed)")
    save(m)


def dc_date():
    return NOW()[:10]


def cmd_set(a):
    m = load(a.id); set_state(m, a.stage, a.state, a.note or '', by=a.by, why=a.why); save(m)
    if not a.no_page:
        import page; page.write_all(ROOT)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sp = ap.add_subparsers(dest='cmd', required=True)
    p = sp.add_parser('init'); p.add_argument('id'); p.add_argument('--name')
    p = sp.add_parser('status'); p.add_argument('id', nargs='?')
    p = sp.add_parser('set'); p.add_argument('id'); p.add_argument('stage', choices=STAGE_KEYS); p.add_argument('state', choices=STATES)
    p.add_argument('--note'); p.add_argument('--why'); p.add_argument('--by'); p.add_argument('--no-page', action='store_true')
    p = sp.add_parser('name'); p.add_argument('id'); p.add_argument('name')
    p = sp.add_parser('recipe'); p.add_argument('id'); p.add_argument('recipe', choices=sorted(RECIPES))
    p = sp.add_parser('adopt'); p.add_argument('ids', nargs='*')
    p = sp.add_parser('review'); p.add_argument('id'); p.add_argument('stage', choices=STAGE_KEYS)
    p = sp.add_parser('run'); p.add_argument('id'); p.add_argument('stage', choices=STAGE_KEYS); p.add_argument('--step', required=True)
    p = sp.add_parser('approve'); p.add_argument('id'); p.add_argument('stage', choices=STAGE_KEYS); p.add_argument('--by'); p.add_argument('--note')
    p.add_argument('--patch-said', help="Patch's words when they decide a gate his marks alone do not (no Bad allowed)")
    p.add_argument('--qualifier', help="e.g. 'testing': shown as 'approved (for testing)'"); p.add_argument('--on', help='date of his decision')
    p = sp.add_parser('bound'); p.add_argument('id'); p.add_argument('--step', default='adopt', choices=['adopt', 'playable', 'publish', 'choose', 'glb', 'fit', 'eyes'])
    p.add_argument('--size'); p.add_argument('--patch-said'); p.add_argument('--on'); p.add_argument('--only', choices=['sizes', 'full-bake'], help='publish: that set alone')
    p = sp.add_parser('marks'); p.add_argument('ids', nargs='*')
    sp.add_parser('page'); sp.add_parser('refresh')
    a = ap.parse_args()
    sys.path.insert(0, str(Path(__file__).parent))
    if a.cmd == 'init':
        init(a.id, a.name); print(char_dir(a.id))
    elif a.cmd == 'status':
        cmd_status(a)
    elif a.cmd == 'set':
        cmd_set(a)
    elif a.cmd in ('name', 'recipe'):
        m = load(a.id); old = m.get(a.cmd); m[a.cmd] = getattr(a, a.cmd); log(m, '-', f'{a.cmd}: {old} -> {m[a.cmd]}'); save(m)
        import page; page.write_all(ROOT)
    elif a.cmd == 'adopt':
        import adopt, page; adopt.run(a.ids); page.write_all(ROOT)
    elif a.cmd == 'run':
        if a.stage != '09-game':
            raise SystemExit('run: 09-game is the first stage built (the others follow)')
        import game, rig, page
        import armsdown
        fn = getattr(game, a.step, None) or getattr(rig, a.step, None) or getattr(armsdown, a.step, None)
        if fn is None:
            raise SystemExit(f'run 09-game: no step {a.step} (level, distil, rig, checks, glbs, evidence, arms_down, publish)')
        fn(a.id); page.write_all(ROOT)
    elif a.cmd == 'approve':
        cmd_approve(a); import page; page.write_all(ROOT)
    elif a.cmd == 'review':
        import review, page
        if a.stage not in ('08-final', '09-game'):
            raise SystemExit('review: 08-final and 09-game have review builders (concept and clip batches come with run)')
        bid, url = (review.review_final if a.stage == '08-final' else review.review_game)(a.id); print(bid, url); page.write_all(ROOT)
    elif a.cmd == 'bound':
        import bound, page
        if a.step == 'choose':
            bound.choose(a.id, a.size, a.patch_said, a.on)
        elif a.step == 'publish':
            bound.publish(a.id, a.only)
        else:
            getattr(bound, a.step)(a.id)
        page.write_all(ROOT)
    elif a.cmd == 'marks':
        import adopt, page; adopt.concept_marks(a.ids); page.write_all(ROOT)
    elif a.cmd in ('page', 'refresh'):
        import page; print(page.write_all(ROOT))


if __name__ == '__main__':
    main()
