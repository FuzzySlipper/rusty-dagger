"""dagger-char bound ID [--step adopt|playable|publish|choose|glb|fit|eyes]: the bound-scaffold route under a character's stage folders
(docs/splat-scaffold.md: the baked look held to a meshed, rigged clay scaffold, so it moves on the scaffold's rig).

The bound route never replaces the free route's files. It sits beside them, in a named folder of the stage it belongs to:

  02-guide/scaffold/            clay-front.png, clay-back.png, scaffold.glb (the rigged scaffold: what the bake is held to and what
                                animates it), scaffold.json, transform.json (scaffold to bake units), outline-score.json, outline-overlay.png
  08-final/bound/               splat.npz (the full-size bound bake), score.json, binding.npz, shell.npz, review/,
                                playable/ (the full bake as the Workbench plays it)
  09-game/bound/sizes/NAME/     splat.npz, score.json, binding.npz - one folder per size made from the full bake
  09-game/bound/clips/          the retargeted clip poses every size plays
  09-game/bound/playable/       character.json, NAME.vsplat, scaffold.glb - the sizes as the Workbench plays them
  09-game/bound/scores.json, review/

  adopt     file an existing scaffold run (RUNS below) there by hard link, and write every item's role into character.json: which
            is the source, which are candidates, which are superseded, which are working files (left in the run folder, by path)
  playable  write the playable sets from the filed sizes: clip_compare.py prep (poses of each clip on the scaffold's rig) and
            export_workbench.py (bodies as .vsplat, the clips as animations in a copy of the scaffold GLB). CPU only, about a minute
  choose    record the size that is the character's runtime body (--size NAME --patch-said TEXT): it becomes the final, the others
            alternates, and 09-game/bound/final/ gets the body as files (splat, binding, .vsplat with the rig and clips)
  publish   import the playable sets into the Workbench (asset import; a published set is never replaced, so each publish takes the
            next free -rNN) and record the links

The default runtime finish is FINISH (Patch, 2026-11-15: "The 16k-face-share seems a very good compromise of detail and size and if so
should become the default runtime finish"): 16,000 Gaussians with a quarter of the seed on the face (bone_region.py --bones Head --front 0,
budget_seed.py --region --share 0.25), seed share only. Other sizes are made when asked for; each is a 15-minute distil of the full bake.

A size here is already a runtime body: it was distilled to its count on the lab and bound to the rig. Packaging it for a viewer or
the game changes its file format, not its Gaussians. Only the full-size bake is not a runtime size."""
import datetime, hashlib, json, os, shutil, subprocess
from pathlib import Path
import dagger_char as dc
from adopt import _link
_hard_link = _link

REPO = dc.REPO; LE = dc.AP / 'live-evidence'; T = REPO / 'authoring/tools/people/splat/scaffold'   # LE: the runs before the move live in asset-pipeline
WB = 'http://192.168.1.10:8960/#?root=active-evidence&id=import%3A'
FINISH = {'size': '16k-face-share', 'gaussians': 16000, 'face_share': 0.25}   # the default runtime body of a bound character
# One entry per character with a scaffold run. Paths are inside the run folder. sizes: name -> (folder, what it is).
RUNS = {
    '385': {
        'run': LE / 'town385-scaffold-20261115-r01',
        'metres_per_bake_unit': 1.6400571191959845,   # the free bake's rig scale (09-game), so both routes stand the same height
        'clay': ('clay-lab/front.png', 'clay-lab/back.png'),
        'scaffold': 'hands/scaffold-hands2.glb', 'scaffold_json': 'hands/scaffold-hands2.json',
        'transform': 'score-rigged/transform.json', 'outline': ('score-rigged/scores.json', 'score-rigged/overlay.png'),
        'bake': ('bound2/r1/bound2', 'bound2/binding.npz', 'bound2/data/shell.npz'),
        'bake_said': ('running side by side the bound bake shows clear improvements ... the bound version is dramatically better in motion; '
                      'I\'m pretty happy with this workflow so the bound bake can now become the default', '2026-11-15'),
        'bake_what': '400,000 Gaussians held within 2.5 cm of the scaffold (6 cm round the hands), on his final fit\'s data and settings; '
                     'every bound size is made from it. Trained against the first placement of the donor hands, bound to the corrected one',
        'superseded': [
            ('02-guide', 'scaffold, first hand placement', 'hands/scaffold-hands.glb', 'donor wrists at the rig\'s joints; the bound bake was trained against this one'),
            ('02-guide', 'scaffold, the mesher\'s own hands', 'rig-meshy/rigged-glb.glb', 'Meshy mesh and 24-bone rig before the hand transplant; bound bake 1 used it'),
            ('02-guide', 'mesh from the painted keyframes', 'mesh-painted/model.glb', 'made for comparison with the clay mesh; not used'),
            ('08-final', 'bound bake 1', 'bound/r1/bound/splat.npz', 'first bound bake, 2.5 cm reach everywhere: soft hands (31.26 dB held back)'),
        ],
        'working': [
            ('02-guide', 'clay edits on a Strix Halo', 'clay', 'the same two clay edits made on a Halo for timing'),
            ('02-guide', 'Meshy mesh, remesh and rig tasks', 'rig-meshy', 'the mesher\'s and rigger\'s raw downloads'),
            ('09-game', 'training logs of the sizes', 'compare/ladder2', 'seed and training logs; the renders and seeds themselves are on the GPU lab'),
            ('09-game', 'face views and face-share logs', 'compare/face', 'the 27 close face views\' cameras, the face mask, logs'),
            ('09-game', 'renders and videos of the comparisons', 'compare', 'data2..data5, renders2..renders5: frames behind the Workbench video sets'),
        ],
        'final_review': [('compare/data2/videos/hands-walking-f010-quarter.png', 'free-bound1-bound2-walking-quarter.png'),
                         ('compare/data4/videos/handsclose-walking-f010-hands.png', 'free-bound1-bound2-hands.png')],
        'final_links': {'videos: free and bound on the same clips': 'town385-free-vs-bound-clips-20261115-r01',
                        'videos: transplanted hands': 'town385-bound-hands-clips-20261115-r01'},
        'sizes': {
            '96k': ('compare/ladder2/b96000', 'bound 96k'), '48k': ('compare/ladder2/b48000', 'bound 48k'),
            '24k': ('compare/ladder2/b24000', 'bound 24k'), '16k': ('compare/ladder2/b16000', 'bound 16k'),
            '16k-face-share': ('compare/face/ladder2/fa16000', 'bound 16k, a quarter of the seed on the face'),
            '16k-face-share-with-face-views': ('compare/face/ladder2/fb16000', 'bound 16k, a quarter of the seed on the face, close face views added to training'),
            '8k': ('compare/ladder2/b8000', 'bound 8k'),
            '8k-face-share': ('compare/face/ladder2/fa8000', 'bound 8k, a quarter of the seed on the face'),
            '8k-face-share-with-face-views': ('compare/face/ladder2/fb8000', 'bound 8k, a quarter of the seed on the face, close face views added to training'),
            '4k': ('compare/ladder2/b4000', 'bound 4k'),
        },
        'default_size': '16k-face-share',
        'scores': ('compare/face/scores.json', {'96k': 'b96000', '48k': 'b48000', '24k': 'b24000', '16k': 'b16000', '16k-face-share': 'fa16000',
                                                '16k-face-share-with-face-views': 'fb16000', '8k': 'b8000', '8k-face-share': 'fa8000',
                                                '8k-face-share-with-face-views': 'fb8000', '4k': 'b4000'}),
        'clips': {'walking': ('compare/clips/walking.json', None), 'idle': ('compare/clips/idle.json', 150), 'around': ('compare/clips/around.json', None)},
        'game_review': [('compare/data5/videos/face-walking-f010-close.png', 'sizes-upper-body.png'),
                        ('compare/data5/videos/face16-walking-f010-face.png', '16k-face-share-face.png'),
                        ('compare/data5/videos/face8-walking-f010-face.png', '8k-face-share-face.png'),
                        ('compare/data3/videos/ladder-walking-f010-quarter.png', 'sizes-whole-figure.png')],
        'game_links': {'videos: 400k down to 4k, and against the free bake': 'town385-bound-ladder-clips-20261115-r01',
                       'videos: face share at 16k and 8k': 'town385-bound-face-share-clips-20261115-r01'},
        'free_final': ('08-final/final/splat.npz', 'the accepted free bake, 400,000 Gaussians; the free route\'s game files are made from it', {'held_back_db': 31.47}),
        'free_game': ('/data/assets/splat-characters/town385', 'the free bake\'s 96k, 48k and 16k skinned-splat GLBs on the Mixamo and 441-bone rigs: what the Engine loads '
                                                               'today, until the bound body is written as an Engine GLB'),
    },
}


def own_run(cid, run, k, clip_what, sizes=None, who='his'):
    """A character started in its own folder and baked with bake.sh and bound_run.sh: the scaffold's paths are absolute (it was made
    in the character folder) and the bake lives in RUN/K (K = NNN/X)."""
    d = dc.char_dir(cid)
    return {
        'run': LE / run,
        'clay': (str(d / '02-guide/wip/clay/front.png'), str(d / '02-guide/wip/clay/back.png')),
        'scaffold': str(d / '02-guide/scaffold/scaffold.glb'), 'scaffold_json': str(d / '02-guide/scaffold/scaffold.json'),
        'transform': f'{k}/bound/score/transform.json', 'outline': (f'{k}/bound/score/scores.json', f'{k}/bound/score/overlay.png'),
        'bake': (f'{k}/bound/bound', f'{k}/bound/binding.npz', f'{k}/bound/bound-data/shell.npz'),
        'bake_what': f"400,000 Gaussians held within 2.5 cm of {who} scaffold (6 cm round the hands), on round 1's pictures, cameras and settings; made from {clip_what}. "
                     'Every runtime size is made from it',
        'working': [
            ('04-cutouts', 'bake folder: cut-outs, cameras, both fits, repaint rings', f'{k}/bake', 'frames, mattes and their review, Solv3R solves, round 0, the three VACE rings, round 1 and its data, review sheets'),
            ('08-final', 'bound bake working files', f'{k}/bound', "start points, distance grid, training log, review sheets, the distil's logs, clip renders"),
        ],
        'final_review': [(f'{k}/bound/review-bound/views.png', 'round1-above-bound-below-views.png'), (f'{k}/bound/review-bound/steep.png', 'round1-above-bound-below-steep.png'),
                         (f'{k}/bound/review-bound/details.png', 'round1-above-bound-below-details.png')],
        'final_review_name': 'free round 1 against the bound bake', 'final_review_what': 'review sheets: the free round-1 fit above, the bound bake below (whole figure, steep views, close-ups)',
        'sizes': sizes or {'16k-face-share': (f'{k}/bound/sizes/16k-face-share', 'bound 16k, a quarter of the seed on the face'),
                           '16k-face-share-cleaned': (f'{k}/bound/sizes/16k-face-share-cleaned', 'the same body after clean_bound.py: pale strays off the hands, green strays off the head, crown streaks recoloured to hair')},
        'default_size': '16k-face-share-cleaned' if not sizes else next(iter(sizes)),
        'clips': {'walking': (str(dc.char_dir('385') / '09-game/bound/clips/walking.json'), None), 'idle': (str(dc.char_dir('385') / '09-game/bound/clips/idle.json'), 150),
                  'around': (str(dc.char_dir('385') / '09-game/bound/clips/around.json'), None)},
    }


RUNS['381'] = own_run('381', 'villager381-bound-20261115-r01', '381/A', 'the stick-guided 1088 px clip')
RUNS['381']['default_size'] = '16k-face-share'   # his first playable set opened on the body as baked
RUNS['454'] = own_run('454', 'villager454-bound-20261116-r01', '454/A', 'the stick-guided 1920 px clip', who='her')
# her long skirt is rebound (bound_run.sh skirt): the full bake and the 16k body with the skirt shared between the legs
RUNS['454']['bake'] = ('454/A/bound/bound-cleaned-skirt', '454/A/bound/bound-cleaned-skirt/binding.npz', '454/A/bound/bound-data/shell.npz')
RUNS['454']['sizes']['16k-face-share-skirt'] = ('454/A/bound/sizes/16k-face-share-cleaned-skirt', 'the cleaned 16k body with the skirt rebound (skirt_weights.py): each skirt Gaussian follows a share of both legs, so the skirt stretches between them instead of tearing')
RUNS['454']['default_size'] = '16k-face-share-skirt'

def static_npc(cid, run, k, clip_what):
    """A static NPC (never walks; Patch 2026-11-22): static_rig.py's short chain Hips..Head with still hands, and static_idle.py's
    procedural idle and look clips in place of the retargeted Mixamo ones (bound_run.sh idle renders them)."""
    r = own_run(cid, run, k, clip_what)
    r['static'] = True
    r['clips'] = {'idle': ('static_idle.py idle', None), 'look': ('static_idle.py look', None)}
    return r


RUNS['021'] = static_npc('021', 'static-npc-021-20261122-r01', '021/A', 'the stick-guided 1920 px clip on hand-written joints (static NPC, GPU lab)')
# Patch 2026-11-22: a lone static figure can afford more than 16k ("Maybe 32k?"); the 32k face is clearly sharper (31.01 dB against the full bake, 16k 29.09)
RUNS['021']['sizes'] = {'32k-face-share-cleaned': ('021/A/bound/sizes/32k-face-share-cleaned', 'bound 32k, a quarter of the seed on the face, cleaned: the default for a static NPC (one figure, never a crowd)'),
                        **RUNS['021']['sizes']}
RUNS['021']['sizes']['32k-face-share'] = ('021/A/bound/sizes/32k-face-share', 'bound 32k, a quarter of the seed on the face')
RUNS['021']['default_size'] = '32k-face-share-cleaned'
V = '021/A/bound/idle/data/videos/npc021-'   # bound_run.sh idle renders, clip_compare.py videos stills: 400k left, 16k right
RUNS['021']['game_review'] = [(V + 'look-f100-close.png', 'close-look-f100-turned-left.png'), (V + 'look-f220-close.png', 'close-look-f220-turned-right.png'),
                              (V + 'idle-f170-face.png', 'face-idle-f170.png'), (V + 'idle-f060-side.png', 'side-idle-f060.png'),
                              (V + 'look-f220-back-quarter.png', 'sbq-look-f220-back-quarter.png')]

def skirted(cid, run, k, clip_what):
    """A character in a long skirt (or a skirt and cloak): the full bake and the 16k body with the skirt rebound (bound_run.sh skirt)."""
    r = own_run(cid, run, k, clip_what, who='her')
    r['bake'] = (f'{k}/bound/bound-cleaned-skirt', f'{k}/bound/bound-cleaned-skirt/binding.npz', f'{k}/bound/bound-data/shell.npz')
    r['sizes']['16k-face-share-skirt'] = (f'{k}/bound/sizes/16k-face-share-cleaned-skirt', 'the cleaned 16k body with the skirt rebound (skirt_weights.py): each skirt Gaussian follows a share of both legs')
    r['default_size'] = '16k-face-share-skirt'
    return r


RUNS['393'] = skirted('393', 'villager393-bound-20261116-r01', '393/A', 'the stick-guided 1920 px clip')


def unmixed_hands(cid, run, k, clip_what, bake='bake'):
    """Hands that hang near a hem (bound_run.sh hands): the full bake and the 16k body with hand-and-leg weights split and hand
    pieces left on the hem removed (unmix_hands.py); bake names an older bake folder (bake-m3)."""
    r = own_run(cid, run, k, clip_what)
    r['bake'] = (f'{k}/bound/bound-cleaned-hands', f'{k}/bound/bound-cleaned-hands/binding.npz', f'{k}/bound/bound-data/shell.npz')
    r['sizes']['16k-face-share-hands'] = (f'{k}/bound/sizes/16k-face-share-cleaned-hands', 'the cleaned 16k body with hand-and-leg weights split (unmix_hands.py): no streaks from the hands to the thighs in a walk')
    r['default_size'] = '16k-face-share-hands'
    r['working'][0] = ('04-cutouts', 'bake folder: cut-outs, cameras, both fits, repaint rings', f'{k}/{bake}', r['working'][0][3])
    v = f'{k}/bound/clips-hands/data/videos/{cid}-400k-and-16k'
    r['game_review'] = [(f'{v}-{c}.png', f'{cid}-400k-and-16k-{c}.png') for c in ('walking-f010-quarter', 'walking-f010-close', 'walking-f020-quarter', 'walking-f024-back-quarter', 'around-f120-quarter')]
    return r





def guided_rebake(cid, run, k, bake, clip_what):
    """A character re-clipped with a stick-guided clip and baked into its own folder (BAKE=bake-g1) beside its older bakes."""
    r = own_run(cid, run, k, clip_what)
    r['working'][0] = ('04-cutouts', 'bake folder: cut-outs, cameras, both fits, repaint rings', f'{k}/{bake}', r['working'][0][3])
    v = f'{k}/bound/clips/data/videos/{cid}-400k-and-16k'
    r['game_review'] = [(f'{v}-{c}.png', f'{cid}-400k-and-16k-{c}.png') for c in ('walking-f010-quarter', 'walking-f010-close', 'walking-f020-quarter', 'walking-f024-back-quarter', 'around-f120-quarter', 'walking-f010-face')]
    return r


# 386-C: re-clipped on den-m5 (stick-guided, 1920 px; Patch approved pair and clip 2026-11-16), baked in bake-g1; no hand-body mixes
RUNS['386-C'] = guided_rebake('386-C', 'villagers-batch1-20261113-r01', '386/C', 'bake-g1', 'the stick-guided 1920 px clip made on den-m5')

# 387-A: re-clipped on den-nimo (take 1: Patch kept it over take 2, whose light moves; its floor shadow stayed out of the mattes), baked in bake-g1
RUNS['387-A'] = guided_rebake('387-A', 'villagers-batch1-20261113-r01', '387/A', 'bake-g1', 'the stick-guided 1920 px clip made on den-nimo (take 1)')

# 383-A: bound from the free route's round-1 data (bake-m3, the pinned 2176 px clip), as 385 was (Patch, 2026-11-16)
# Patch rejected that bake (2026-11-16: "kick it back to the video gen stage"); re-clipped on den-m5 and baked in bake-g1 with no free round-1 fit
RUNS['383-A'] = guided_rebake('383-A', 'villagers-batch1-20261113-r01', '383/A', 'bake-g1', 'the stick-guided 1920 px clip made on den-m5, with no free round-1 fit')
RUNS['383-A']['sizes']['16k-face-share-hands'] = ('383/A/bound/sizes/16k-face-share-cleaned-hands', 'the cleaned 16k body with hand-and-leg weights split (unmix_hands.py)')
RUNS['383-A']['default_size'] = '16k-face-share-hands'
RUNS['383-A']['bake'] = ('383/A/bound/bound-cleaned-hands', '383/A/bound/bound-cleaned-hands/binding.npz', '383/A/bound/bound-data/shell.npz')
RUNS['383-A']['final_review_name'] = 'free round 0 against the bound bake'
# Patch 2026-11-17: flecks beside the hem in the walk -> bound_run.sh tidy (drop_loose.py) on the hands bodies
RUNS['383-A']['sizes']['16k-face-share-tidy'] = ('383/A/bound/sizes/16k-face-share-cleaned-hands-tidy', 'the hands body without the Gaussians that came loose from it in the walk (drop_loose.py): no flecks beside the hem')
RUNS['383-A']['default_size'] = '16k-face-share-tidy'
RUNS['383-A']['bake'] = ('383/A/bound/bound-cleaned-hands-tidy', '383/A/bound/bound-cleaned-hands-tidy/binding.npz', '383/A/bound/bound-data/shell.npz')
_v = '383/A/bound/clips-tidy/data/videos/383-A-400k-and-16k'
RUNS['383-A']['game_review'] = [(f'{_v}-{c}.png', f'383-A-400k-and-16k-{c}.png') for c in ('walking-f010-quarter', 'walking-f010-close', 'walking-f020-quarter', 'walking-f024-back-quarter', 'around-f120-quarter', 'walking-f010-face')]


def batch2(cid, part, clip_what):
    """Batch 2 (live-evidence/villagers-batch2-20261117-r01, Patch 2026-11-17: work through the villagers in batches): a stick-guided
    Halo clip, no free round-1 fit, then the skirt or hands step and the tidy step (drop_loose.py). part is 'skirt' or 'hands'."""
    run = cid if '-' in cid else f'{cid}-A'   # 397-B, 384-C: the run folder NNN/X (batch-id.sh); others NNN/A
    k = run.replace('-', '/'); r = own_run(cid, 'villagers-batch2-20261117-r01', k, clip_what)
    full = f'{k}/bound/bound-cleaned-{part}-tidy'
    r['bake'] = (full, f'{full}/binding.npz', f'{k}/bound/bound-data/shell.npz')
    said = {'skirt': 'the skirt rebound (skirt_weights.py): each skirt Gaussian follows a share of both legs',
            'hands': 'hand-and-leg weights split (unmix_hands.py): no streaks from the hands to the thighs'}[part]
    r['sizes'][f'16k-face-share-{part}'] = (f'{k}/bound/sizes/16k-face-share-cleaned-{part}', f'the cleaned 16k body with {said}')
    r['sizes']['16k-face-share-tidy'] = (f'{k}/bound/sizes/16k-face-share-cleaned-{part}-tidy',
                                         f'the {part} body without the Gaussians that came loose from it in the clips (drop_loose.py): no flecks')
    r['default_size'] = '16k-face-share-tidy'
    r['final_review_name'] = 'free round 0 against the bound bake'
    if part == 'skirt' and (LE / 'villagers-batch2-20261117-r01' / k / 'bound/skirt-rig/skirt.json').exists():
        r['skirt_rig'] = f'{k}/bound/skirt-rig'   # the skirt step made skirt bones (SKIRT_BONES; it removes the folder when it does not)
        # a gentler walk for a long skirt (gentle_clip.py --amount 0.75; Patch 2026-11-21: "a slightly less vigorous walk")
        r['clips']['walking'] = (str(dc.ROOT / 'batches/clips-skirt/walking.json'), None)
    v = f'{k}/bound/clips-tidy/data/videos/{run}-400k-and-16k'
    r['game_review'] = [(f'{v}-{c}.png', f'{cid}-400k-and-16k-{c}.png') for c in ('walking-f010-quarter', 'walking-f010-close', 'walking-f020-quarter', 'walking-f024-back-quarter', 'around-f120-quarter', 'walking-f010-face')]
    return r


RUNS['382'] = batch2('382', 'skirt', 'the stick-guided 1920 px clip made on a Strix Halo')
RUNS['388'] = batch2('388', 'hands', 'the stick-guided 1920 px clip made on a Strix Halo')
RUNS['389'] = batch2('389', 'hands', 'the stick-guided 1920 px clip made on a Strix Halo')
# 391: the two-view (front/back) Meshy scaffold had the arms forward of the splat's (mesh IoU 0.80, no Gaussians on the hands); remade with a third, side clay (frame 19): IoU 0.92
RUNS['391'] = batch2('391', 'hands', 'the stick-guided 1920 px clip made on a Strix Halo')
RUNS['392'] = batch2('392', 'skirt', 'the stick-guided 1920 px clip made on the 5090 (fitted-sleeve redesign); three-view scaffold')
RUNS['392']['bake_what'] = RUNS['392']['bake_what'].replace(' his ', ' her ')
# 392 was redesigned with fitted sleeves after the bell sleeves tore in motion (Patch 2026-11-18); the bell-sleeve bake is RUN/392/A-bell-sleeves-20261118
RUNS['394'] = batch2('394', 'skirt', 'the stick-guided 1920 px clip made on a Strix Halo')
RUNS['395'] = batch2('395', 'skirt', 'the stick-guided 1920 px clip made on the 5090 (shoulder-capelet redesign); three-view scaffold')
RUNS['395']['bake_what'] = RUNS['395']['bake_what'].replace(' his ', ' her ')
RUNS['397-B'] = batch2('397-B', 'skirt', "the stick-guided 1920 px clip of her bare body (no hood or shawl) made on the 5090, same guide and seed as 397's; three-view scaffold")
RUNS['397-B']['bake_what'] = RUNS['397-B']['bake_what'].replace(' his ', ' her ')
# long skirts on skirt bones (skirt_bones.py; Patch 2026-11-19: 395's and 397's skirts tore between the
# legs; 2026-11-20: the standard; later that day two tiers on an XPBD cloth sheet). batch2 sets skirt_rig when the run has bound/skirt-rig; adopt links the rig as
# 02-guide/scaffold/scaffold-skirt.glb + skirt-bones.json; playable drives the bones per clip
# 384-C: skirt rerun with --hem -0.20 (his folded boot cuffs had been found as the hem and broke off; 2026-11-19)
# no shawl layer (Patch 2026-11-19: the cut hood sat awkwardly; hoods stay in the body bake, and 397 ships bare); skirt rebound
# with --width 3 --hips 0.5 --solid 3 --dark-under 0.35 and the hem found above the ankles (it walked as two trouser legs, the shins webbed, the back see-through)
RUNS['396'] = batch2('396', 'skirt', 'the stick-guided 1920 px clip made on a Strix Halo; three-view scaffold')
RUNS['396']['bake_what'] = RUNS['396']['bake_what'].replace(' his ', ' her ')
RUNS['397'] = batch2('397', 'skirt', 'the stick-guided 1920 px clip made on a Strix Halo; three-view scaffold')
RUNS['397']['bake_what'] = RUNS['397']['bake_what'].replace(' his ', ' her ')
# 2026-11-19 grind (prep.py, scaffold.sh, batch-id.sh lanes on the Halos); 452 and 452-B: the cloak as a layer like 397's shawl
for _c, _w, _who in (('384-C', 'the stick-guided 1920 px retake made on a Strix Halo; three-view scaffold', 'his'),
                     ('390-B', 'the stick-guided 1920 px retake made on a Strix Halo; three-view scaffold', 'his'),
                     ('451', 'the stick-guided 1920 px clip made on a Strix Halo; three-view scaffold', 'her'),
                     ('453', 'the stick-guided 1920 px clip made on a Strix Halo; three-view scaffold', 'her'),
                     ('455', 'the stick-guided 1920 px clip made on a Strix Halo; three-view scaffold', 'her'),
                     ('398', 'the stick-guided 1920 px clip made on the 5090 (short hooded shoulder-cape redesign); three-view scaffold', 'her'),
                     ('456', 'the stick-guided 1920 px clip made on the 5090 (fitted-sleeve redesign); three-view scaffold', 'her'),
                     ('452', 'the stick-guided 1920 px clip of her in the hooded cloak, the source of the cloak layer; three-view scaffold', 'her'),
                     ('452-B', "the stick-guided 1920 px clip of her with the hood kept and the cloak tail off, same guide and seed as 452's; three-view scaffold", 'her')):
    RUNS[_c] = batch2(_c, 'skirt', _w); RUNS[_c]['bake_what'] = RUNS[_c]['bake_what'].replace(' his ', f' {_who} ')
# 2026-11-21 Patch: "The 452 cloak doesn't work very well ... A blue cloak on a white shirt is going to be dreadful to make work ...
# she looks fine and is a minor character so just exporting without a cloak is fine". The cut stays here, off (set CLOAK_452B=1 to cut it).
if os.environ.get('CLOAK_452B'):
  RUNS['452-B']['layer'] = ('cloak', '452', '--no-hood', '--green', '1', '--link', '0.04', '--off-body', '0.008', '--trim-colour', '0.5',
                            '--despeckle', '0.3')   # only the hanging cloak, cut from 452's bake (the hood stays in both bodies). Patch
# 2026-11-20 found it dissolved: the green rule (397's sea-green skirt) dropped the teal cloak, 16k spacing broke the back into dropped
# 'pieces', the upper back lies where the scaffolds meet; a band of 452's skirt hem came along and its unseen inside was speckled


def pub_name(cid):
    """A character's name in the gallery: npc-NNN as it is (Patch 2026-11-22: the CMS id, clearer), townNNN for the villagers before it."""
    return cid if cid.startswith('npc-') else f'town{cid}'


def run_of(cid):
    npc = dc.char_dir(cid) / '00-brief/npc.json'
    if cid not in RUNS and npc.exists():   # a static NPC made with authoring/tools/people/splat/static-npc/static-npc: its run folder is the character's work/
        # since 2026-11-22 (Patch) a new character is named by its CMS id, npc-NNN; the run under work/ keeps the bare number, NNN/A,
        # because the batch scripts read a run id as NNN-X
        cfg = json.loads(npc.read_text()); k = f"{cid.removeprefix('npc-')}/A"
        r = static_npc(cid, str(dc.char_dir(cid) / 'work'), k, f"the stick-guided {cfg.get('size', 1920)} px clip (static-npc)")
        names = [f'{n // 1000}k-face-share-cleaned' for n in cfg.get('sizes', [32000])]
        r['sizes'] = {n: (f'{k}/bound/sizes/{n}', f"bound {n.split('-')[0]}, a quarter of the seed on the face, cleaned") for n in names}
        r['default_size'] = names[0]
        r['game_review'] = [(f'{k}/bound/review-bound/views.png', 'views.png')]
        r['who'] = 'NPC'
        RUNS[cid] = r
    if cid not in RUNS:
        raise SystemExit(f'{cid}: no scaffold run listed in bound.py RUNS (385, 381 and 454 so far; add it with own_run)')
    return RUNS[cid]


def adopt(cid):
    r = run_of(cid); E0 = r['run']; d = dc.char_dir(cid); m = dc.load(cid)
    g = d / '02-guide/scaffold'; f = d / '08-final/bound'; s = d / '09-game/bound'

    class _Run:   # a path in RUNS is relative to the run folder, or absolute (a file already in the character folder)
        def __truediv__(self, p):
            return Path(p) if Path(p).is_absolute() else E0 / p
        def relative_to(self, x):
            return E0.relative_to(x)
        def __str__(self):
            return str(E0)
    E = _Run()

    def _link(a, b):   # the scaffold of a character started in its own folder is already where it belongs
        if Path(a).resolve() != Path(b).resolve():
            _hard_link(a, b)
    # 02-guide: the rigged scaffold
    _link(E / r['clay'][0], g / 'clay-front.png')
    if (E / r['clay'][1]).exists():   # a front-only character (npc-023) has no back clay
        _link(E / r['clay'][1], g / 'clay-back.png')
    _link(E / r['scaffold'], g / 'scaffold.glb'); _link(E / r['scaffold_json'], g / 'scaffold.json'); _link(E / r['transform'], g / 'transform.json')
    _link(E / r['outline'][0], g / 'outline-score.json'); _link(E / r['outline'][1], g / 'outline-overlay.png')
    sj = json.load(open(g / 'scaffold.json')); oj = json.load(open(g / 'outline-score.json'))['mean']
    it = lambda stage, name, role, path, what, **k: dc.record_item(m, stage, name, role, path, what, route=k.pop('route', 'bound'), **k)
    it('02-guide', 'rigged scaffold', 'final', g / 'scaffold.glb',
       (f"clay mesh from the clay pair with static_rig.py's short chain (Hips..Head, still hands at the clasp): {sj['joints']} joints, {sj['triangles']:,} triangles. "
        if r.get('static') else f"clay mesh from the clay pair, API rig, donor hands transplanted: {sj['joints']} joints, {sj['triangles']:,} triangles. ")
       + "The bound bake is held to it and animated by it",
       numbers={'outline_iou': oj['mesh']['iou'], 'finished_splat_own_iou': oj['splat']['iou']}, made_from='01-concept approved paintings (clay edits of both)')
    it('02-guide', 'clay front and back' if (g / 'clay-back.png').exists() else 'clay front', 'intermediate', g / 'clay-front.png', 'the approved paintings edited to plain clay; what the mesher saw' + (' (clay-back.png beside it)' if (g / 'clay-back.png').exists() else ' (front only: no back painting)'))
    it('02-guide', 'scaffold to bake transform', 'intermediate', g / 'transform.json', 'places the scaffold in the bake\'s units (outline fit)')
    it('02-guide', 'outline score', 'evidence', g / 'outline-overlay.png', 'scaffold outline over the bake\'s cut-outs, 12 views (numbers: outline-score.json)',
       numbers={'iou': oj['mesh']['iou'], 'covered': oj['mesh']['covered'], 'spill': oj['mesh']['spill']})
    dc.record_output(m, '02-guide', 'scaffold', g / 'scaffold.glb')
    if r.get('skirt_rig'):   # the scaffold with the skirt bones added (skirt_bones.py rig)
        _link(E / r['skirt_rig'] / 'scaffold.glb', g / 'scaffold-skirt.glb'); _link(E / r['skirt_rig'] / 'skirt.json', g / 'skirt-bones.json')
        sj = json.loads((g / 'skirt-bones.json').read_text())
        what = (f"the rigged scaffold with {len(sj['joints'])} skirt bones under Hips, an upper and a lower bone per column (skirt_bones.py, two tiers); "
                "each clip solves a cloth sheet pushed out by the legs and sets the bones from it (skirt-bones.json)") if sj.get('tiers') == 2 else \
               (f"the rigged scaffold with a ring of {len(sj['joints'])} skirt bones under Hips (skirt_bones.py); the clips turn them out about the hips "
                "so the legs stay inside the skirt (skirt-bones.json)")
        it('02-guide', 'scaffold with skirt bones', 'final', g / 'scaffold-skirt.glb', what, made_from='02-guide rigged scaffold')
    # 08-final: the full-size bound bake, beside the free one
    bk, binding, shell = r['bake']
    _link(E / bk / 'splat.npz', f / 'splat.npz'); _link(E / bk / 'score.json', f / 'score.json'); _link(E / binding, f / 'binding.npz'); _link(E / shell, f / 'shell.npz')
    for src, name in r['final_review']:
        _link(E / src, f / 'review' / name)
    sc = json.load(open(f / 'score.json'))
    if r.get('free_final') and (d / r['free_final'][0]).exists():
        ff, fw, fn = r['free_final']; it('08-final', 'final splat (free bake)', 'source', d / ff, fw, route='free', numbers=fn)
    it('08-final', 'bound bake', 'source', f / 'splat.npz', r['bake_what'],
       numbers={'gaussians': sc['gaussians'], 'held_back_db': sc['heldout']['psnr']}, made_from='07-refine round 1 data and settings; 02-guide rigged scaffold')
    it('08-final', 'binding to the scaffold\'s rig', 'intermediate', f / 'binding.npz', 'bones and weights of every Gaussian (export_bound.py); shell.npz is the distance grid the bake was held to')
    if (f / 'review').exists():
        it('08-final', r.get('final_review_name', 'free and bound on the same clips'), 'evidence', f / 'review', r.get('final_review_what', 'stills of the comparison videos'),
           links={k: WB + v for k, v in r.get('final_links', {}).items()})
    dc.record_output(m, '08-final', 'bound-splat', f / 'splat.npz')
    # 09-game: the sizes
    scores = tags = None
    if r.get('scores'):
        sp, tags = r['scores']; _link(E / sp, s / 'scores.json'); scores = json.load(open(s / 'scores.json'))['sizes']
    for name, (src, what) in r['sizes'].items():
        for fl in ('splat.npz', 'score.json', 'binding.npz'):
            _link(E / src / fl, s / 'sizes' / name / fl)
        chosen = (m['stages']['09-game'].get('bound_final') or {}).get('size')
        if scores:
            n = scores[tags[name]]; nums = {'gaussians': n['gaussians'], 'body_db': n['body_psnr_db'], 'face_db': n['face_psnr_db'], 'on_the_face': n['in_face_region']}
        else:   # no face views were scored: the trainer's own held-back whole-figure score
            n = json.load(open(s / 'sizes' / name / 'score.json')); nums = {'gaussians': n['gaussians'], 'body_db': n['exact']['subject_psnr']}
        it('09-game', f'bound {name}', 'final' if name == chosen else 'alternate' if chosen else 'candidate', s / 'sizes' / name,
           what + ('. The runtime body' if name == chosen else '. A runtime body as it is: distilled to this count and bound to the rig'),
           numbers=nums, made_from='08-final bound bake (252 renders at 2176 px, 15,000 steps)')
        dc.record_output(m, '09-game', f'bound-{name}', s / 'sizes' / name / 'splat.npz')
    if not r.get('static'):   # a static NPC's clips are made by static_idle.py when the playable sets are written
        for name, (src, _) in r['clips'].items():
            _link(E / src, s / 'clips' / f'{name}.json')
    for src, name in r.get('game_review', []):
        _link(E / src, s / 'review' / name)
    if r.get('static'):
        it('09-game', 'procedural clips', 'intermediate', T / 'static_idle.py', ', '.join(r['clips']) + ': breathing, a sway about the floor and head turns '
           'on the short rig, made by static_idle.py into each playable set (no files of their own)')
    else:
        it('09-game', 'retargeted clips', 'intermediate', s / 'clips', ', '.join(r['clips']) + ': Mixamo clips retargeted to the scaffold\'s rig (retarget_clip.py); the files stay out of Git')
    if scores:
        it('09-game', 'scores of the sizes', 'evidence', s / 'scores.json', 'face and body score of every size against the full bake')
    if (s / 'review').exists():
        it('09-game', 'sizes on the same clips', 'evidence', s / 'review', 'stills of the comparison videos', links={k: WB + v for k, v in r.get('game_links', {}).items()})
    if r.get('free_game') and Path(r['free_game'][0]).exists():
        fg, fgw = r['free_game']; it('09-game', 'game files (free bake)', 'final', fg, fgw, route='free')
    for stage, name, src, what in r.get('superseded', []):
        it(stage, name, 'superseded', E / src, what)
    for stage, name, src, what in r.get('working', []):
        it(stage, name, 'intermediate', E / src, what)
    m.pop('routes', None); m['meta']['scaffold_run'] = str(E)
    if m.get('recipe') != 'bound-scaffold':
        dc.log(m, '-', f"recipe: {m.get('recipe')} -> bound-scaffold", 'the bound bake is the default (Patch, 2026-11-15)', by='bound adopt'); m['recipe'] = 'bound-scaffold'
    if m['stages']['02-guide']['state'] != 'approved':
        dc.set_state(m, '02-guide', 'approved', f"rigged clay scaffold with transplanted hands; outline IoU {oj['mesh']['iou']} against the bake's cut-outs "
                     f"(the finished splat's own: {oj['splat']['iou']})", by='bound adopt', approved_by='its outline score (not one of Patch\'s gates)')
    said, on = r.get('bake_said') or (None, None)
    if not said and m['stages']['08-final']['state'] in ('empty', 'running'):   # Patch's gate: his look comes with a review batch
        dc.set_state(m, '08-final', 'needs-check', f'bound bake made, {sc["gaussians"]:,} Gaussians, {sc["heldout"]["psnr"]:.2f} dB held back; not yet in front of Patch',
                     by='bound adopt', why='to put in front of Patch')
    if said and 'bound' not in (m['stages']['08-final'].get('note') or ''):
        dc.set_state(m, '08-final', 'approved', f'bound bake, {sc["gaussians"]:,} Gaussians, {sc["heldout"]["psnr"]:.2f} dB held back - Patch ({on}): "{said}"',
                     by='bound adopt', approved_by=f'Patch, {on} (his words on the side-by-side videos)')
    dc.log(m, '-', 'bound route filed', f"scaffold in 02-guide/scaffold, bound bake in 08-final/bound, {len(r['sizes'])} sizes in 09-game/bound (hard links from {E.relative_to(REPO)})", by='bound adopt')
    dc.save(m)


def choose(cid, size, said=None, on=None):
    """The size that is the character's runtime body. Patch's words go in the record when the choice is his."""
    r = run_of(cid); d = dc.char_dir(cid); s = d / '09-game/bound'; g = d / '02-guide/scaffold'; fin = s / 'final'
    if not size or not (s / 'sizes' / size / 'splat.npz').exists():
        raise SystemExit(f'{cid}: no size {size} in {s / "sizes"} ({", ".join(sorted(x.name for x in (s / "sizes").iterdir()))})')
    subprocess.run(['rm', '-rf', str(fin)])
    for fl in ('splat.npz', 'score.json', 'binding.npz'):
        _link(s / 'sizes' / size / fl, fin / fl)
    engine = 'Not yet written as the Engine\'s skinned-splat GLB'
    if (s / 'playable' / f'{size}.vsplat').exists():   # the body as a viewer plays it: one .vsplat, the rig with its clips, a manifest of just this body
        man = json.load(open(s / 'playable/character.json'))
        _link(s / 'playable' / f'{size}.vsplat', fin / f'{cid}-{size}.vsplat'); _link(s / 'playable' / man['rig'], fin / man['rig'])
        (fin / 'character.json').write_text(json.dumps({'body': f'{cid}-{size}.vsplat', 'rig': man['rig'], 'clips': man['clips']}, indent=2) + '\n')
    on = on or dc.dc_date()

    def upd(m):
        st = m['stages']['09-game']; st['bound_final'] = {'size': size, 'said': said or '', 'on': on, 'recorded': dc.NOW()}
        for it in st.get('items', []):
            if it.get('route') == 'bound' and it['role'] in ('final', 'alternate', 'candidate') and it['name'].startswith('bound '):
                it['role'] = 'final' if it['name'] == f'bound {size}' else 'alternate'
        n = next((it.get('numbers') or {} for it in st.get('items', []) if it['name'] == f'bound {size}'), {})
        dc.record_item(m, '09-game', 'runtime body', 'final', fin, f'bound {size}: the body as files - splat and binding, and the .vsplat with the rig and clips as a viewer plays it. {engine}',
                       route='bound', numbers=n, made_from=f'09-game/bound/sizes/{size}')
        dc.record_output(m, '09-game', 'bound-final', fin / 'splat.npz')
        dc.set_state(m, '09-game', 'approved', f'runtime body: bound {size}' + (f' - Patch ({on}): "{said}"' if said else '') + f'. {engine}; the Engine still loads the free bake\'s GLBs',
                     by='bound choose', approved_by=f'Patch, {on}' if said else 'bound choose')
    dc.patch(cid, upd)
    glb(cid)


# The clips every character is fitted to (splat_glb_clip_fit.py), in /data/assets/splat-characters/pinoc-clips/mixamo:
# (fit name, file, leg-ratio fit for an idle, long skirts only). Walks get k for the least planted-toe drift; idles the leg ratio.
FIT_CLIPS = [
    ('walk', 'walking.glb', False, False),
    ('idle', 'standing-idle.glb', True, False),
    # long skirts walk gentler (Patch 2026-11-21); gentle_glb.py's 0.75 copy, its own k (rusty-dagger #10968)
    ('gentle', 'walking-gentle.glb', False, True),
    # walk and idle variety (rusty-dagger #10978, Patch: mild variety for the gsplat townsfolk): PINOC generated motions
    # (all PINOC clips, library ones too, are fine to ship: Patch 2026-11-22), loop_clip.py then clean_clip_noise.py; stroll-easy is
    # gentle_glb.py at 0.85 and stroll-gentle at 0.75, walking-gentle's amount
    ('stroll', 'relaxed-stroll.glb', False, False),
    ('stroll-easy', 'relaxed-stroll-easy.glb', False, False),
    ('stroll-gentle', 'relaxed-stroll-gentle.glb', False, True),
    ('idle-relaxed', 'relaxed-idle.glb', True, False),
    ('idle-crossed', 'crossed-arms-idle.glb', True, False),
    ('idle-hips', 'hands-on-hips-idle.glb', True, False),
]


def clip_fit(glb_path, out_json, skirted):
    """Fit FIT_CLIPS to a character's Engine GLB; returns the CompletedProcess. A clip file that's missing is left out."""
    mix = Path('/data/assets/splat-characters/pinoc-clips/mixamo')
    specs = [f"{name}={mix / f}" + (':leg' if leg else '') for name, f, leg, skirt_only in FIT_CLIPS
             if (mix / f).exists() and (skirted or not skirt_only)]
    return subprocess.run(['python3', '-I', str(dc.REPO / 'authoring/tools/people/splat/gsplat/splat_glb_clip_fit.py'), str(glb_path), str(out_json), *specs],
                          capture_output=True, text=True)


def eyes(cid, out=None, fin=None):
    """The eye mask for blinks (rusty-dagger #10977): eye_mask.py writes _GROUP = 1 on the eye Gaussians and asset.extras.eyes
    into the Engine GLB, from the eye centres in authoring/tools/people/splat/character/eye_picks.json; reports ID-eyes.png/.json beside it. A
    character with no picks is reported, not skipped silently: pick its eyes on the face render (eye_mask.py REPORT.png) first."""
    standalone = out is None
    fin = fin or dc.char_dir(cid) / '09-game/bound/final'; out = out or fin / f'{cid}.glb'
    src = out
    if standalone:   # redo on a shipped GLB: from the copy kept without eyes (ID.pre-eyes.glb), made the first time
        pre = fin / f'{cid}.pre-eyes.glb'
        if not pre.exists():
            shutil.copy2(out, pre)
        src = pre
    picks = json.loads((dc.REPO / 'authoring/tools/people/splat/character/eye_picks.json').read_text())['picks']
    if cid not in picks:
        print(f'{cid}: NO EYE MASK - no eye centres in authoring/tools/people/splat/character/eye_picks.json'); return
    tmp = out.with_name(out.name + '.eyes')
    r = subprocess.run(['python3', str(dc.REPO / 'authoring/tools/people/splat/scaffold/eye_mask.py'), str(src), str(tmp), str(fin / f'{cid}-eyes.png'),
                        '--eyes', ','.join(map(str, picks[cid][:4])), '--write-anyway'], capture_output=True, text=True)
    if r.returncode or not tmp.exists():
        raise SystemExit(f'{cid}: eye_mask failed\n{r.stderr[-1500:]}')
    os.replace(tmp, out)
    print(f'{cid} eyes: {r.stdout.strip()}')


def fit(cid):
    """Refit the chosen body's Engine GLB to FIT_CLIPS only: 09-game/bound/final/ID-clip-fit.json, written new and then moved into
    place; the GLB is not touched. Prints each clip's k and drift, and any k that moved from the old file (rusty-dagger #10978)."""
    fin = dc.char_dir(cid) / '09-game/bound/final'; out = fin / f'{cid}.glb'; js = fin / f'{cid}-clip-fit.json'
    if not out.exists():
        raise SystemExit(f'{cid}: no {out}; run bound --step glb first')
    old = json.loads(js.read_text())['clips'] if js.exists() else {}
    tmp = js.with_name(js.name + '.new')
    r = clip_fit(out, tmp, (dc.char_dir(cid) / '02-guide/scaffold/skirt-bones.json').exists())
    if r.returncode or not tmp.exists():
        raise SystemExit(f'{cid}: clip fit failed\n{r.stderr[-1500:]}')
    new = json.loads(tmp.read_text())['clips']; os.replace(tmp, js)
    for name, c in new.items():
        f = c['fitted']; was = old.get(name, {}).get('k')
        print(f"{cid} {name}: k {c['k']}" + (f' (was {was})' if was is not None and abs(was - c['k']) > 0.005 else '')
              + f", {f.get('speed_m_s')} m/s, toe drift median {f.get('drift_end_cm_median')} cm, max {f.get('drift_end_cm_max')} cm")


def glb(cid):
    """The Engine's skinned splat GLB of the chosen runtime body (Patch 2026-11-19: the Engine reads GLB, not .vsplat):
    authoring/tools/people/splat/scaffold/bound_to_glb.py writes 09-game/bound/final/ID.glb on the 22 Mixamo body bones (the scaffold's 36
    joints mapped onto them, fingers into the hands) with every vsplat_to_glb.py check, then splat_glb_clip_fit.py on
    PINOC's Mixamo Walking and Standing Idle, and the glTF validator when it is in the npx cache. Reports beside it."""
    d = dc.char_dir(cid); fin = d / '09-game/bound/final'
    if not (fin / 'character.json').exists():
        raise SystemExit(f'{cid}: no {fin}/character.json; choose a size first (bound --step choose)')
    out = fin / f'{cid}.glb'
    surf = fin / f'{cid}-surface.npz'   # normals from the scaffold mesh and the de-lit albedo, for the Engine's lighting (Patch 2026-11-19)
    s_ = subprocess.run(['python3', str(dc.REPO / 'authoring/tools/people/splat/scaffold/bound_surface.py'), str(fin), str(surf)], capture_output=True, text=True)
    if s_.returncode:
        raise SystemExit(f'{cid}: bound_surface failed\n{s_.stderr[-1500:]}')
    sk = d / '02-guide/scaffold/skirt-bones.json'   # skirt bones go in after the 22, with the cloth driver's data in the extras (Patch 2026-11-20)
    r = subprocess.run(['python3', str(dc.REPO / 'authoring/tools/people/splat/scaffold/bound_to_glb.py'), str(fin), str(out), '--surface', str(surf)]
                       + (['--skirt', str(sk)] if sk.exists() else []), capture_output=True, text=True)
    if r.returncode or not out.exists():
        raise SystemExit(f'{cid}: bound_to_glb failed\n{r.stderr[-1500:]}')
    rep = json.loads(r.stdout); (fin / f'{cid}.glb.json').write_text(json.dumps(rep, indent=1) + '\n')
    eyes(cid, out, fin)
    static = rep.get('static')
    if static:   # a static NPC (short chain): its own idle and look as clip packs, no Mixamo fit (2026-11-22)
        c = subprocess.run(['python3', str(dc.REPO / 'authoring/tools/people/splat/scaffold/static_clips.py'), str(out), str(fin / json.load(open(fin / 'character.json'))['rig']),
                            str(fin), cid], capture_output=True, text=True)
        if c.returncode:
            raise SystemExit(f'{cid}: static_clips failed\n{c.stderr[-1500:]}')
    else:
        clip_fit(out, fin / f'{cid}-clip-fit.json', sk.exists())
    val = subprocess.run('ls -d "$HOME"/.npm/_npx/*/node_modules/gltf-validator 2>/dev/null | head -1', shell=True, capture_output=True, text=True).stdout.strip()
    vr = ''
    if val:
        js = ("const v=require(process.argv[1]);const fs=require('fs');(async()=>{const r=await v.validateBytes(new Uint8Array(fs.readFileSync(process.argv[2])));"
              "console.log(`${r.issues.numErrors} errors, ${r.issues.numWarnings} warnings`);})();")
        vr = subprocess.run(['node', '-e', js, val, str(out)], capture_output=True, text=True).stdout.strip()
    walk = {}
    try:
        walk = json.load(open(fin / f'{cid}-clip-fit.json'))['clips']['walk']['fitted']
    except Exception:
        pass
    nums = {'joints': rep['joints'], 'splats': rep['splats'], 'normals': 'scaffold mesh', 'rest_normal_max_angle_deg': rep.get('rest_normal_max_angle_deg'), 'MB': round(rep['bytes'] / 1e6, 2), 'validator': vr or 'not run'}
    if static:
        nums['clips'] = {k: {x: c[x] for x in ('file', 'seconds', 'hand_error_mm_max')} for k, c in json.load(open(fin / f'{cid}-clips.json'))['clips'].items()}
    else:
        nums['walk_toe_lowest_cm'] = [round(100 * walk.get('toe_l_lowest_m', float('nan')), 1), round(100 * walk.get('toe_r_lowest_m', float('nan')), 1)]

    def upd(m):
        dc.record_item(m, '09-game', 'engine glb', 'final', out, ('the runtime body as the Engine\'s skinned splat GLB: 22 Mixamo joint names, the short chain '
                       '(Hips..Head and the hands) weighted, arms and legs placed with no weight; it plays its own clips ID-idle.glb and ID-look.glb '
                       '(static_clips.py), not the Mixamo walks and idles; report ID-clips.json') if static else 'the runtime body as the Engine\'s skinned splat GLB: 22 Mixamo body bones (scaffold joints merged, '
                       'fingers into the hands), Mixamo clips play by name' + (f", then {rep.get('skirt_joints')} skirt bones that ride with the hips until the "
                       'Engine drives them (their cloth sheet is in the GLB extras)' if rep.get('skirt_joints') else '') + '; reports ID.glb.json, ID-clip-fit.json', route='bound', numbers=nums,
                       made_from='09-game/bound/final (.vsplat and scaffold.glb)')
    dc.patch(cid, upd)
    print(cid, json.dumps(nums))
    return nums


def sh(cmd, log):
    with open(log, 'a') as fh:
        fh.write(f"\n== {dc.NOW()} {' '.join(map(str, cmd))}\n"); fh.flush()
        rc = subprocess.run(list(map(str, cmd)), stdout=fh, stderr=subprocess.STDOUT, cwd=REPO).returncode
    if rc:
        raise SystemExit(f'{cmd[1]} failed (rc {rc}); see {log}')


def playable(cid):
    """Two sets: 09-game/bound/playable (every size, the default first) and 08-final/bound/playable (the full bake, for reference)."""
    r = run_of(cid); d = dc.char_dir(cid); g = d / '02-guide/scaffold'; f = d / '08-final/bound'; s = d / '09-game/bound'
    metres = r.get('metres_per_bake_unit') or 1 / json.load(open(g / 'transform.json'))['scale']   # a scaffold in metres: its own scale
    order = [r['default_size']] + [n for n in r['sizes'] if n != r['default_size']]
    sets = [(s / 'playable', d / '09-game/wip/bound-playable-prep', [(n, s / 'sizes' / n / 'splat.npz', s / 'sizes' / n / 'binding.npz') for n in order]),
            (f / 'playable', d / '08-final/wip/bound-playable-prep', [('400k-full-bake', f / 'splat.npz', f / 'binding.npz')])]
    rig = g / ('scaffold-skirt.glb' if r.get('skirt_rig') else 'scaffold.glb')
    for out, prep, bodies in sets:
        log = prep.parent / 'bound-playable.log'; subprocess.run(['rm', '-rf', str(prep), str(out)])
        cmd = ['python3', T / 'clip_compare.py', 'prep', prep, '--metres', metres, '--views', 'quarter']
        for n, splat, binding in bodies:
            cmd += ['--bound', f"{n}={splat}:{binding}:{rig}:{g / 'transform.json'}"]
        if not r.get('static'):
            for c, (_, frames) in r['clips'].items():
                cmd += ['--clip', f"{c}={s / 'clips' / (c + '.json')}" + (f':{frames}' if frames else '')]
        sh(cmd, log)
        if r.get('static'):   # a static NPC's short rig has no legs to retarget onto: static_idle.py's procedural clips instead
            sh(['python3', T / 'static_idle.py', prep, rig, g / 'transform.json'] + sum((['--clip', c] for c in r['clips']), []), log)
        if r.get('skirt_rig'):   # the skirt bones' turn per frame, baked into the clips
            sh(['python3', T / 'skirt_bones.py', 'drive', prep, g / 'skirt-bones.json', '--tags', ','.join(n for n, _, _ in bodies)], log)
        sh(['python3', T / 'export_workbench.py', rig, g / 'transform.json', prep, out] + sum((['--body', f'{n}={n}'] for n, _, _ in bodies), []), log)
        if r.get('layer'):   # a garment baked on the clothed character and cut onto this bare one (bound_layer.py; Patch 2026-11-19)
            name, src, *opts = r['layer']; clothed = dc.char_dir(src) / out.relative_to(d) / f'{bodies[0][0]}.vsplat'
            sh(['python3', T / 'bound_layer.py', clothed, out, name] + opts, log)

    def upd(m):
        dc.record_item(m, '09-game', 'playable set of the sizes', 'evidence', s / 'playable', 'every size as a .vsplat on the scaffold\'s rig with the clips, as the Workbench plays them',
                       route='bound', made_from='09-game/bound/sizes, 09-game/bound/clips, 02-guide rigged scaffold')
        dc.record_item(m, '08-final', 'playable full bake', 'evidence', f / 'playable', 'the 400k bound bake as a .vsplat on the same rig and clips, for reference beside the sizes',
                       route='bound', made_from='08-final bound bake')
        dc.log(m, '09-game', 'bound playable sets written', f'{len(sets[0][2])} sizes and the full bake; clips: ' + ', '.join(r['clips']), by='bound playable')
    dc.patch(cid, upd)


def member(mid, path, roles, root, media=None):
    p = Path(path); mt = media or {'.vsplat': 'application/octet-stream', '.glb': 'model/gltf-binary', '.png': 'image/png', '.json': 'application/json'}[p.suffix]
    return {'id': mid, 'roles': roles, 'publishedPath': p.name if mid != 'still' else 'still.png',
            'source': {'path': str(p.relative_to(root)), 'mediaType': mt, 'byteSize': p.stat().st_size, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}}


def publish(cid, only=None):
    """only = 'sizes' or 'full-bake' publishes that set alone (a published set is never replaced, so do not republish an unchanged one)."""
    r = run_of(cid); d = dc.char_dir(cid); f = d / '08-final/bound'; s = d / '09-game/bound'; m = dc.load(cid)
    root = REPO if REPO in d.resolve().parents else dc.ROOT; pubs = LE / 'publications'
    who = r.get('who') or (m['name'].split(':')[0].capitalize() if ':' in m['name'] else 'Villager')
    day = datetime.date.today().strftime('%Y%m%d')
    if r.get('scores'):
        scores = json.load(open(s / 'scores.json'))['sizes']; tags = r['scores'][1]
        table = '; '.join(f"{n}: face {scores[tags[n]]['face_psnr_db']:.2f} dB, body {scores[tags[n]]['body_psnr_db']:.2f} dB" for n in r['sizes'])
    else:
        table = '; '.join(f"{n}: body {json.load(open(s / 'sizes' / n / 'score.json'))['exact']['subject_psnr']:.2f} dB" for n in r['sizes'])
    still = lambda folder: sorted(folder.glob('*.png'))[0]   # the set's picture: the first review still of its stage
    jobs = [('sizes', s / 'playable', still(s / 'review'), '09-game',
             f"{who} {cid}: every bound size, playable ({', '.join(r['sizes'])})",
             f"The bound bake of {cid} at every size made so far, on the scaffold's own rig, with the clips {', '.join(r['clips'])}. Pick a size under Preview member; "
             f"it opens on {r['default_size']}. Every size here is a runtime body as it is (distilled to its count, bound to the rig). Scores against the 400k full bake "
             f"(body = held-back whole-figure views; face = close face views where given): {table}. The full 400k bake is a separate set. Files: "
             f"local/art/generated/people/dagger-characters/{cid}/09-game/bound/."),
            ('full-bake', f / 'playable', still(f / 'review'), '08-final',
             f"{who} {cid}: the full 400k bound bake, playable",
             f"The full-size bound bake of {cid} (400,000 Gaussians) on the scaffold's rig with the same clips, for reference beside the sizes made from it. "
             f"It is the source of the sizes, not a runtime size. Files: local/art/generated/people/dagger-characters/{cid}/08-final/bound/.")]
    for key, out, still, stage, title, notes in [j for j in jobs if only in (None, j[0])]:
        man = json.load(open(out / 'character.json')); n = 1
        while (pubs / 'imports' / f'{pub_name(cid)}-bound-{key}-playable-{day}-r{n:02d}').exists():
            n += 1
        rid = f'{pub_name(cid)}-bound-{key}-playable-{day}-r{n:02d}'
        ms = [member('character', out / 'character.json', ['primary'], root, 'application/vnd.viggle.splat-character+json'),
              member('scaffold', out / man['rig'], ['rig', 'animation'], root), member('still', still, ['image', 'representative'], root)]
        ms += [member(Path(b).stem, out / b, ['model'], root) for b in [man['body']] + list(man.get('variants', {}).values())]
        ms += [member(Path(f).stem, out / f, ['model'], root) for L in man.get('layers', {}).values() for f in L['modes'].values()]   # garment layers (bound_layer.py)
        ms += [member(Path(f).stem, out / f, ['model'], root) for f in (man.get('under'), man.get('bodyWhole')) if f and f not in man.get('variants', {}).values()]   # a hood's covered hair
        req = {'schemaVersion': 1, 'id': rid, 'kind': 'splat-character', 'title': title, 'primary': 'character', 'representative': 'still', 'members': ms,
               'comparison': {'runFamily': f'{pub_name(cid)}-splat-scaffold', 'subjectId': pub_name(cid), 'variantId': rid},
               'provenance': {'producer': 'dagger-char-bound-publish', 'settings': {'rig': f"02-guide/scaffold/scaffold.glb ({json.load(open(d / '02-guide/scaffold/scaffold.json'))['joints']} joints)",
                                                                                   'clips': ', '.join(r['clips']) + (' (procedural, static_idle.py, 30 fps)' if r.get('static') else ' (retargeted Mixamo clips, 30 fps)')}},
               'properties': {'notes': notes, 'evidenceRoot': str(out.parent) + '/'}}
        w = d / stage / 'wip' / f'bound-publish-{key}'; w.mkdir(parents=True, exist_ok=True); (w / 'import-request.json').write_text(json.dumps(req, indent=1))
        rev = subprocess.run(['git', '-C', str(REPO), 'rev-parse', 'HEAD'], capture_output=True, text=True).stdout.strip()
        x = subprocess.run([str(dc.AP / 'legacy/tools/asset-cli/target/debug/asset'), 'import', '--source-root', str(root), '--workspace', str(w / f'ws-{rid}'),
                            '--request', str(w / 'import-request.json'), '--run-id', rid, '--importer-revision', rev, '--gallery-root', str(pubs)], capture_output=True, text=True)
        if x.returncode:
            raise SystemExit(f'publish {rid} failed: {x.stdout[-800:]} {x.stderr[-800:]}')
        (w / f'receipt-{rid}.json').write_text(x.stdout); print(rid, WB + rid)
        item = 'playable set of the sizes' if key == 'sizes' else 'playable full bake'

        def upd(m, stage=stage, item=item, rid=rid):
            for it in m['stages'][stage].get('items', []):
                if it['name'] == item:
                    it['links'] = {'play in the Workbench': WB + rid}
            dc.log(m, stage, f'published {rid}', WB + rid, by='bound publish')
        dc.patch(cid, upd)
