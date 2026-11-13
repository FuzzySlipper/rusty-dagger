#!/usr/bin/env python3
"""Run a commit's heavy checks on the build workers through the router (`den-tool build run`), from den-agents.

    scripts/route.py ruleset [REF]           the ruleset suite in parts, one worker each, joined into one result
    scripts/route.py ruleset --whole [REF]   the ruleset suite on one worker
    scripts/route.py import [REF]            the import suite
    scripts/route.py verify [REF]            scripts/verify.sh --import --play

REF defaults to HEAD and may be any commit in den-agents' repository, pushed or not; uncommitted changes are not
in it. The lanes are scripts/lane.sh; the router's config (--config, DEN_BUILD_CONFIG_PATH, else the installed one)
names them. A part waits for a worker while every enabled one is busy. Each lane's log is written under
local/build/route/, and the command exits 0 only when every part passed.
"""
import argparse, json, os, pathlib, queue, re, subprocess, sys, threading, time

REPO = 'rusty-dagger'
root = pathlib.Path(__file__).resolve().parent.parent


def git(*args):
    return subprocess.run(['git', '-C', str(root), *args], check=True, capture_output=True, text=True).stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('lane', help='ruleset, import, verify, or another configured rusty-dagger lane')
    parser.add_argument('ref', nargs='?', default='HEAD')
    parser.add_argument('--whole', action='store_true', help='run the ruleset suite on one worker, unsplit')
    parser.add_argument('--host', help='only this worker')
    parser.add_argument('--config', default=os.environ.get('DEN_BUILD_CONFIG_PATH')
                        or os.path.expanduser('~/.config/den-build/config.json'))
    parser.add_argument('--wait', type=int, default=60, help='minutes a part may wait for a worker (default 60)')
    options = parser.parse_args()

    commit = git('rev-parse', '--verify', options.ref + '^{commit}')
    if options.ref == 'HEAD' and git('status', '--porcelain', '--untracked-files=no'):
        print(f'route: uncommitted changes are not in {commit[:12]} and are not tested', file=sys.stderr)
    config = json.loads(pathlib.Path(options.config).read_text())
    lanes = config['repositories'].get(REPO, {}).get('lanes', {})
    if options.lane == 'ruleset' and not options.whole:
        parts = sorted((name for name in lanes if re.fullmatch(r'ruleset-\d+', name)), key=lambda n: int(n[8:]))
    else:
        parts = [options.lane]
    if not parts or any(part not in lanes for part in parts):
        sys.exit(f'route: {options.config} has no {REPO} lane {options.lane}; it has {", ".join(sorted(lanes))}')
    hosts = [host['id'] for host in config['hosts'] if host['enabled'] and options.host in (None, host['id'])]
    if not hosts:
        sys.exit(f'route: {options.config} enables no worker {options.host}')

    run = root / 'local/build/route' / f'{options.lane}-{commit[:12]}-{time.strftime("%Y%m%dT%H%M%S")}'
    run.mkdir(parents=True)
    print(f'route: {", ".join(parts)} at {commit} over {", ".join(hosts)}; logs in {run}', flush=True)
    pending = queue.Queue()
    for part in parts:
        pending.put(part)
    results, lock = {}, threading.Lock()
    began = time.monotonic()

    def worker(host):
        waited_since, said = None, None
        # Until every part has a result: a part another worker had to give back may still come here.
        while len(results) < len(parts):
            try:
                part = pending.get(timeout=20)
            except queue.Empty:
                continue
            log = run / f'{part}.log'
            start = time.monotonic()
            with log.open('w') as output:
                code = subprocess.run(['den-tool', 'build', 'run', '--repo', REPO, '--lane', part, '--ref', commit,
                                       '--host', host, '--config', options.config],
                                      stdout=output, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL).returncode
            text = log.read_text(errors='replace')
            # The worker prints its checkout line once the lane is about to start; without it the router refused the host.
            if f' at {commit}\n' not in text or 'den-build: ' not in text:
                reason = text.strip().splitlines()[-1] if text.strip() else f'exit {code}'
                pending.put(part)
                waited_since = waited_since or time.monotonic()
                if time.monotonic() - waited_since > options.wait * 60:
                    print(f'route: {host} gave up waiting: {reason}', flush=True)
                    return
                if reason != said:
                    said = reason
                    with lock:
                        print(f'route: {host} cannot take {part} yet ({reason}); retrying each minute', flush=True)
                time.sleep(60)
                continue
            waited_since, said = None, None
            minutes = (time.monotonic() - start) / 60
            with lock:
                results[part] = (host, code, minutes, log, text)
                print(f'route: {part} {"passed" if code == 0 else f"FAILED ({code})"} on {host} in {minutes:.1f} min: {log}',
                      flush=True)

    threads = [threading.Thread(target=worker, args=(host,), daemon=True) for host in hosts]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join()

    failed = False
    partitions = set()
    for part in parts:
        if part not in results:
            print(f'route: {part} found no worker within {options.wait} minutes; see {run / (part + ".log")}')
            failed = True
            continue
        host, code, minutes, log, text = results[part]
        summary = [line.strip() for line in text.splitlines()
                   if re.match(r'\s*(Passed!|Failed!)|lane: (run |refusing|.* took )', line)]
        partitions.update(re.findall(r'\(partition ([0-9a-f]+)\)', text))
        print(f'  {part:10} {host:9} {minutes:5.1f} min  {"pass" if code == 0 else "FAIL"}  {log}')
        for line in summary:
            print(f'      {line}')
        failed |= code != 0
    # Every part cuts the suite by the same table, or one part's classes could be missed or run twice.
    if len(partitions) > 1:
        print(f'route: the parts disagree on the partition ({", ".join(sorted(partitions))}); not one result')
        failed = True
    print(f'route: {options.lane} at {commit} {"FAILED" if failed else "passed"} in '
          f'{(time.monotonic() - began) / 60:.1f} min')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
