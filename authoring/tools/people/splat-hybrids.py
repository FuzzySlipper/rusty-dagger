#!/usr/bin/env python3
"""splat-hybrids: the gsplat people's and enemies' hybrid bodies into the art pack.

A hybrid body is a character's shipped splats on the same rig over a skinned base mesh (`extras.splatBaseMesh`) that
fills what the splats leave open and casts their shadow (splat polish, Patch 2026-11-28: take the best of both, the
splats leading). asset-pipeline's tools/splat-hybrid/build_hybrid.sh makes one, ID-hyb.glb, from the character's
final GLB, Meshy's rigged remesh and the bake's training paintings.

For each ID-hyb.glb in DIR whose ID the art pack lists, it writes

  media/splat-people/hybrid/ID.glb          a townsperson's, stated as the character's `hybrid` beside its `body`
  media/splat-enemies/ID.hybrid.glb         an enemy's, stated as its `bodies.hybrid`

and checks each opens in the Engine as the game opens it: `rusty asset check` from the pinned pair's runtime pack (the
`rusty` on the path may be another pair's, whose check refuses splat GLBs). The GLBs are art: they never live in Git. Commit the payload,
then publish them with `scripts/data-sync.sh ship art`.

    authoring/tools/people/splat-hybrids.py DIR [ID...]
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PACK = ROOT / 'content/worldrpg/packs/daggerfall.art-replacements'


def entry(path, root=ROOT / 'content'):
    """A file as the payload states it: its path under the content, length and digest."""
    data = path.read_bytes()
    return {'path': path.relative_to(root).as_posix(), 'byteLength': len(data), 'sha256': hashlib.sha256(data).hexdigest()}


def pinned_rusty():
    """The pinned Engine pair's own rusty, which admits a GLB as the game does."""
    pin = re.search(r'<RustyEnginePackageVersion>([^<]+)<', (ROOT / 'Directory.Build.props').read_text()).group(1)
    cache = Path(os.environ.get('RUSTY_ENGINE_CACHE') or Path(os.environ.get('XDG_CACHE_HOME') or Path.home() / '.cache') / 'rusty-engine')
    rusty = cache / 'pairs' / pin / 'runtime-pack/bin/rusty'
    if not rusty.exists():
        raise SystemExit(f'the pinned pair {pin} is not installed ({rusty}): run rusty install')
    return rusty


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('dir', type=Path, help="build_hybrid.sh's output folder, holding ID-hyb.glb")
    parser.add_argument('ids', nargs='*', help='only these characters (default: every ID-hyb.glb the pack lists)')
    args = parser.parse_args()
    payload_path = PACK / 'payload.json'
    payload = json.loads(payload_path.read_text())
    people = payload['artReplacements']['splatPeople']['characters']
    enemies = payload['artReplacements']['splatEnemies']['characters']
    built = {path.name.removesuffix('-hyb.glb'): path for path in sorted(args.dir.glob('*-hyb.glb'))}
    wanted = args.ids or [cid for cid in built if cid in people or cid in enemies]
    rusty = pinned_rusty()
    for cid in wanted:
        if cid not in built:
            raise SystemExit(f'{cid}: no {cid}-hyb.glb in {args.dir}')
        if cid in people:
            out = PACK / 'media/splat-people/hybrid' / f'{cid}.glb'
        elif cid in enemies:
            out = PACK / 'media/splat-enemies' / f'{cid}.hybrid.glb'
        else:
            raise SystemExit(f'{cid}: the art pack lists no splat person or enemy {cid}')
        check = subprocess.run([str(rusty), 'asset', 'check', str(built[cid])], capture_output=True, text=True)
        if check.returncode:
            raise SystemExit(f'{cid}: the Engine refuses {built[cid]}:\n{check.stdout}{check.stderr}')
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(built[cid], out)
        if cid in people:
            people[cid]['hybrid'] = entry(out)
        else:
            enemies[cid]['bodies']['hybrid'] = entry(out)
        print(f'{cid}: {out.relative_to(ROOT)} ({out.stat().st_size / 1e6:.1f} MB)')
    payload_path.write_text(json.dumps(payload, indent=1, ensure_ascii=False) + '\n')
    print(f'stated {len(wanted)} hybrid bodies; commit the payload, then scripts/data-sync.sh ship art')


if __name__ == '__main__':
    main()
