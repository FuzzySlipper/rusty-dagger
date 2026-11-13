#!/usr/bin/env python3
"""Run a commit's heavy checks on the build workers through the router (`den-tool build run`), from den-agents.

    scripts/route.py ruleset [REF]           the ruleset suite in parts, one worker each, joined into one result
    scripts/route.py ruleset --whole [REF]   the ruleset suite on one worker
    scripts/route.py import [REF]            the import suite
    scripts/route.py verify [REF]            scripts/verify.sh --import --play
    ... --notify ADDRESS --task N            send and leave: return at once; the result goes to that crew address
                                             (yours is in your session's start) and as a line on Den task N
    scripts/route.py main                    one round of main's own check (below)

REF defaults to HEAD and may be any commit in den-agents' repository, pushed or not; uncommitted changes are not
in it. The lanes are scripts/lane.sh; the router's config (--config, DEN_BUILD_CONFIG_PATH, else the installed one)
names them. It first says what each worker is doing. A part waits until a worker frees (--wait MINUTES gives up),
and a long lane (verify, --whole, import) leaves the last free worker to split runs unless --take-last. Stopping
the command stops its lanes. Each lane's log is written under local/build/route/, and the command exits 0 only
when every part passed.

A round of `main` takes the newest origin/main commit unless it already has a result, and runs the verify lane on
it if a worker is free now (otherwise the next round does). A pass is noted on the commit in refs/notes/verify, as
verify.sh --record does; a failure in refs/notes/verify-failures, so a verify note still means green. Both are pushed
from den-agents. The first failure after a pass is posted on the crew board rusty-dagger, naming the first commit
that failed and the last that passed. One round runs at a time.
"""
import argparse, datetime, fcntl, json, os, pathlib, queue, re, signal, subprocess, sys, threading, time

REPO = 'rusty-dagger'
# The den-tool runs in flight; stopping this command stops them, and den-tool then stops the lane on its worker.
children = set()
BOARD = 'rusty-dagger'
root = pathlib.Path(__file__).resolve().parent.parent
routes = root / 'local/build/route'
# What a result says of itself: verify.sh's own summary lines, the lane's, and the tests that failed.
SUMMARY = re.compile(r'\s*(Passed!|Failed!|Failed [A-Z]\w*\.|play-smoke .* passed:|Verified Engine pair|skipped )'
                     r'|lane: (run |refusing|.* took |.* stopped:)')


def git(*args, check=True):
    return subprocess.run(['git', '-C', str(root), *args], check=check, capture_output=True, text=True).stdout.strip()


def activity():
    """What each worker is doing now, from the Resources ledger: {host: ['TITLE (NOTE) for N min', ...]}."""
    listing = subprocess.run(['den-tool', 'resources', '--json', 'work'], capture_output=True, text=True).stdout
    try:
        work = json.loads(listing).get('work', [])
    except json.JSONDecodeError:
        return {}
    now, busy = datetime.datetime.now(datetime.timezone.utc), {}
    for entry in work:
        if entry.get('state') in ('running', 'queued') and not entry.get('stale'):
            began = datetime.datetime.fromisoformat(entry['started_at'][:26].rstrip('Z') + '+00:00')
            note = entry.get('note', '').replace('Commit ', '')[:12]
            busy.setdefault(entry['host'], []).append(
                f'{entry["title"]}{f" ({note})" if note else ""} for {int((now - began).total_seconds() // 60)} min')
    return busy


def route(lane, commit, options, run):
    """Runs a lane, or the ruleset suite's parts, on the workers; returns (passed, report)."""
    config = json.loads(pathlib.Path(options.config).read_text())
    lanes = config['repositories'].get(REPO, {}).get('lanes', {})
    if lane == 'ruleset' and not options.whole:
        parts = sorted((name for name in lanes if re.fullmatch(r'ruleset-\d+', name)), key=lambda n: int(n[8:]))
    else:
        parts = [lane]
    if not parts or any(part not in lanes for part in parts):
        sys.exit(f'route: {options.config} has no {REPO} lane {lane}; it has {", ".join(sorted(lanes))}')
    workers = [host['id'] for host in config['hosts'] if host['enabled']]
    hosts = [host for host in workers if options.host in (None, host)]
    if not hosts:
        sys.exit(f'route: {options.config} enables no worker {options.host}')
    # One long lane (verify, the whole ruleset suite, import) never takes the last free worker, so that a split
    # run, eleven minutes on three workers, can always start; --take-last allows it.
    long = parts == [lane] and not re.fullmatch(r'ruleset-\d+', lane)

    print(f'route: {", ".join(parts)} at {commit} over {", ".join(hosts)}; logs in {run}', flush=True)
    busy = activity()
    for host in workers:
        print(f'route: {host}: {"; ".join(busy.get(host, [])) or "free"}', flush=True)
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
            start, text, code, reason = time.monotonic(), '', None, None
            if long and not options.take_last:
                busy = activity()
                if not any(other != host and not busy.get(other) for other in workers):
                    reason = 'it is the last free worker, which a split run needs (--take-last takes it)'
            if not reason:
                with log.open('w') as output:
                    child = subprocess.Popen(['den-tool', 'build', 'run', '--repo', REPO, '--lane', part, '--ref', commit,
                                              '--host', host, '--config', options.config],
                                             stdout=output, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
                    children.add(child)
                    code = child.wait()
                    children.discard(child)
                text = log.read_text(errors='replace')
                # The worker prints its checkout line once the lane is about to start; without it the router refused the host.
                if f' at {commit}\n' not in text or 'den-build: ' not in text:
                    reason = text.strip().splitlines()[-1] if text.strip() else f'exit {code}'
            if reason:
                pending.put(part)
                waited_since = waited_since or time.monotonic()
                if options.wait is not None and time.monotonic() - waited_since >= options.wait * 60:
                    with lock:
                        print(f'route: {host} gave up waiting: {reason}', flush=True)
                    return
                if reason != said:
                    said = reason
                    with lock:
                        print(f'route: {host} cannot take {part} yet ({reason}); trying again', flush=True)
                # A part tries every 20 seconds and a long lane every minute, so a waiting part takes a freed worker first.
                time.sleep(60 if long else 20)
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

    failed, report, partitions = False, [], set()
    for part in parts:
        if part not in results:
            report.append(f'{part}: found no free worker within {options.wait} minutes; see {run}')
            failed = True
            continue
        host, code, minutes, log, text = results[part]
        partitions.update(re.findall(r'\(partition ([0-9a-f]+)\)', text))
        report.append(f'{part} on {host}, {minutes:.1f} min, {"pass" if code == 0 else "FAIL"}: {log}')
        report += [f'    {line.strip()}' for line in text.splitlines() if SUMMARY.match(line)][:40]
        failed |= code != 0
    # Every part cuts the suite by the same table, or one part's classes could be missed or run twice.
    if len(partitions) > 1:
        report.append(f'the parts disagree on the partition ({", ".join(sorted(partitions))}); not one result')
        failed = True
    outcome = 'not run' if not results else 'FAILED' if failed else 'passed'
    report.insert(0, f'rusty-dagger {lane} at {commit} {outcome} in '
                     f'{(time.monotonic() - began) / 60:.1f} min')
    print('\n'.join(report), flush=True)
    return not failed, '\n'.join(report)


def deliver(options, text):
    """The result of a sent run, where its sender reads it: their crew address, and the Den task they named."""
    if options.notify:
        subprocess.run(['crew-claude', 'send', '--to', options.notify, text], check=False)
    if options.task:
        subprocess.run(['den-tool', 'den', 'send_message', '--project-id', REPO, '--task-id', str(options.task),
                        '--sender', 'claude-code', '--content', text], check=False, stdout=subprocess.DEVNULL)


def noted(ref, commit):
    return subprocess.run(['git', '-C', str(root), 'notes', f'--ref={ref}', 'show', commit],
                          capture_output=True).returncode == 0


def record(name, commit, text):
    """Notes the result on the commit in refs/notes/NAME and pushes it, on top of origin's notes."""
    local = f'route/{name}'
    for _ in range(3):
        git('fetch', '-q', 'origin', f'+refs/notes/{name}:refs/notes/{local}', check=False)
        git('notes', f'--ref={local}', 'add', '-f', '-m', text, commit)
        if subprocess.run(['git', '-C', str(root), 'push', '-q', 'origin', f'refs/notes/{local}:refs/notes/{name}'],
                          capture_output=True).returncode == 0:
            return
    print(f'route: could not push refs/notes/{name}; the note is in refs/notes/{local} here', file=sys.stderr)


def main_round(options):
    routes.mkdir(parents=True, exist_ok=True)
    lock = (routes / 'main.lock').open('w')
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        print('route: a round of main is already running')
        return 0
    git('fetch', '-q', 'origin', 'main')
    for name in ('verify', 'verify-failures'):
        git('fetch', '-q', 'origin', f'+refs/notes/{name}:refs/notes/route/{name}', check=False)
    newest = git('rev-parse', 'origin/main')
    if noted('route/verify', newest) or noted('route/verify-failures', newest):
        print(f'route: {newest} already has a result')
        return 0
    options.whole, options.host, options.wait, options.take_last = False, None, 0, False
    run = routes / f'main-{newest[:12]}-{time.strftime("%Y%m%dT%H%M%S")}'
    run.mkdir(parents=True)
    passed, report = route('verify', newest, options, run)
    if 'found no free worker' in report:
        print(f'route: no worker is free; the next round takes {newest[:12]}')
        return 0
    if 'lane: refusing' in report:
        print(f'route: den-agents has not imported the importer revision of {newest[:12]} yet; the next round takes it')
        return 0
    record('verify' if passed else 'verify-failures', newest, f'scripts/route.py main\n{report}')
    if passed:
        return 0
    # The last commit with a green note, and the first failure noted after it.
    history = git('rev-list', '--first-parent', '--max-count=2000', newest).split()
    last = next((commit for commit in history if noted('route/verify', commit)), None)
    since = history[:history.index(last)] if last else history
    failures = [commit for commit in since if noted('route/verify-failures', commit)]
    if failures != [newest]:
        print(f'route: main was already failing since {failures[-1][:12]}; recorded, not posted again')
        return 1
    subject = git('log', '-1', '--format=%s', newest)[:120]
    unchecked = len(since) - 1
    text = (f'main fails the full verify at {newest} ("{subject}"), the first commit that failed. '
            f'The last that passed is {last or "none within 2000 commits"}'
            f'{f"; the {unchecked} commits between were not checked" if unchecked else ""}.\n\n{report}')
    subprocess.run(['crew-claude', 'board', 'new', BOARD, f'main fails the full verify at {newest[:12]}', text],
                   check=False)
    return 1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('lane', help='ruleset, import, verify, another configured rusty-dagger lane, or main')
    parser.add_argument('ref', nargs='?', default='HEAD')
    parser.add_argument('--whole', action='store_true', help='run the ruleset suite on one worker, unsplit')
    parser.add_argument('--host', help='only this worker')
    parser.add_argument('--config', default=os.environ.get('DEN_BUILD_CONFIG_PATH')
                        or os.path.expanduser('~/.config/den-build/config.json'))
    parser.add_argument('--wait', type=int, help='minutes a part may wait for a worker (default: until one frees)')
    parser.add_argument('--take-last', action='store_true', help='let a long lane take the last free worker')
    parser.add_argument('--notify', metavar='ADDRESS', help='send and leave; the result goes to this crew address')
    parser.add_argument('--task', type=int, help='send and leave; the result goes on this Den task as a line')
    options = parser.parse_args()
    for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, lambda received, frame: (
            [child.terminate() for child in list(children)], os._exit(128 + received)))
    if options.lane == 'main':
        return main_round(options)

    commit = os.environ.get('ROUTE_COMMIT') or git('rev-parse', '--verify', options.ref + '^{commit}')
    if not os.environ.get('ROUTE_COMMIT') and options.ref == 'HEAD' and git('status', '--porcelain', '--untracked-files=no'):
        print(f'route: uncommitted changes are not in {commit[:12]} and are not tested', file=sys.stderr)
    run = pathlib.Path(os.environ.get('ROUTE_RUN') or
                       routes / f'{options.lane}-{commit[:12]}-{time.strftime("%Y%m%dT%H%M%S")}')
    if (options.notify or options.task) and not os.environ.get('ROUTE_RUN'):
        # Send and leave: the same command goes on in its own session, which outlives this one's shell.
        run.mkdir(parents=True)
        with (run / 'route.out').open('w') as output:
            subprocess.Popen([sys.executable, __file__, *sys.argv[1:]], stdin=subprocess.DEVNULL, stdout=output,
                             stderr=subprocess.STDOUT, start_new_session=True,
                             env={**os.environ, 'ROUTE_COMMIT': commit, 'ROUTE_RUN': str(run)})
        where = ' and '.join(filter(None, [options.notify and f'crew address {options.notify}',
                                           options.task and f'Den task #{options.task}']))
        print(f'route: sent {options.lane} at {commit}; the result goes to {where}; progress in {run}/route.out')
        return 0
    run.mkdir(parents=True, exist_ok=True)
    passed, report = route(options.lane, commit, options, run)
    deliver(options, report)
    return 0 if passed else 1


if __name__ == '__main__':
    sys.exit(main())
