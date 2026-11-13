#!/usr/bin/env python3
"""Run a commit's heavy checks on the build workers through the router (`den-tool build run`), from den-agents.

    scripts/route.py ruleset [REF]           the ruleset suite in parts, one worker each, joined into one result
    scripts/route.py ruleset --whole [REF]   the ruleset suite on one worker
    scripts/route.py import-suite [REF]      the import suite
    scripts/route.py verify [REF]            scripts/verify.sh --import --play
    scripts/route.py data [REF]              imports the commit on a worker and publishes its data to the store under its
                                             importer revision (nothing to do when the store holds it already)
    ... --notify ADDRESS --task N            send and leave: return at once; the result goes to that crew address
                                             (yours is in your session's start) and as a line on Den task N
    scripts/route.py main                    one round of main's own check (below)
    scripts/route.py main-status             the latest round's outcome, and whether main is overdue for one

REF defaults to HEAD and may be any commit in den-agents' repository, pushed or not; uncommitted changes are not
in it. The lanes are scripts/lane.sh; the router's config (--config, DEN_BUILD_CONFIG_PATH, else the installed one)
names them. The test lanes take the data of the commit's importer revision from the store and refuse when it has
none: route `data` for that commit first. It first says what each worker is doing. A part waits until a worker frees
(--wait MINUTES gives up), and a long lane (verify, --whole, import-suite) leaves the last worker the router can use
to split runs unless --take-last; long lanes sent from den-agents are admitted one at a time, so together they leave
it too. `data` is not a long lane: the commit's suites wait on it. Stopping the command stops
its lanes. Each lane's log is written under local/build/route/, and the command exits 0 only when every part passed.

A round of `main` does nothing when the newest origin/main commit already has a result. Otherwise it waits, up to
ROUND_WAIT_MINUTES, until two workers the router can use are free (a long lane leaves the last one to split runs, and
it looks every minute while a waiting part looks every 20 seconds), then runs the verify lane on whatever commit is
newest at that moment; a commit it waited for that is no longer newest is superseded. A pass is noted on the commit in
refs/notes/verify, as verify.sh --record does; a failure in refs/notes/verify-failures, so a verify note still means
green, and the note's first line says which part failed: the product (scripts/verify.sh) or the tooling (route.py's
own tests, the lane or the router). Both are pushed from den-agents. The first failure after a pass is posted on the
crew board rusty-dagger, naming the first commit that failed and the last that passed. One round waits or runs at a
time: another that finds it does nothing. Each round's states (waiting, running, passed, failed, did not start and
why, superseded) are lines in the shared checkout's local/build/route/main-rounds.jsonl, which main-status reads.
"""
import argparse, datetime, fcntl, json, os, pathlib, queue, re, signal, subprocess, sys, threading, time

REPO = 'rusty-dagger'
# The den-tool runs in flight; stopping this command stops them, and den-tool then stops the lane on its worker.
children = set()
BOARD = 'rusty-dagger'
root = pathlib.Path(__file__).resolve().parent.parent
routes = root / 'local/build/route'
# What a result says of itself: verify.sh's own summary lines, the lane's, and the tests that failed.
SUMMARY = re.compile(r'\s*(Passed!|Failed!|Failed [A-Z]\w*\.|FAIL: |play-smoke .* passed:|Verified Engine pair|skipped )'
                     r'|lane: (run |refusing|the import rebaked|the store already|.* took |.* failed \(|.* stopped:)'
                     r'|data-sync: (published|refusing|the store already)')
# A round of main waits at most this long for a worker, so that the wait and a verify (35 to 45 minutes, with the
# lane's setup) fit the timer unit's two-hour TimeoutStartSec; the next round, half an hour later, waits again.
ROUND_WAIT_MINUTES = 60
POLL_SECONDS = 60
# main-status calls main overdue once a commit has gone this long without a recorded round: one landing just after a
# round started waits for it (45 minutes), for the next tick (30), for a worker (60) and for its own verify (45).
OVERDUE_MINUTES = 180


def git(*args, check=True):
    return subprocess.run(['git', '-C', str(root), *args], check=check, capture_output=True, text=True).stdout.strip()


def resources():
    """The Resources service's state: each host's telemetry and apps, and the work checked in."""
    listing = subprocess.run(['den-tool', 'resources', '--json', 'status'], capture_output=True, text=True).stdout
    try:
        return json.loads(listing)
    except json.JSONDecodeError:
        return {}


def workers_now(hosts, state):
    """For each configured worker: why den-tool build would not take a job there now (None when it would), by the
    router's own host check, and what it is running."""
    now = datetime.datetime.now(datetime.timezone.utc)
    samples = {sample['id']: sample for sample in state.get('hosts', [])}
    work = [entry for entry in state.get('work', []) if entry.get('state') in ('running', 'queued') and not entry.get('stale')]
    found = {}
    for host in hosts:
        name, sample, limit = host['id'], samples.get(host['id']), host.get('max_gpu_percent')
        mine = [entry for entry in work if entry.get('host') == name]
        doing = [f'{entry["title"]}{f" ({note})" if (note := entry.get("note", "").replace("Commit ", "")[:12]) else ""} for '
                 f'{int((now - datetime.datetime.fromisoformat(entry["started_at"][:26].rstrip("Z") + "+00:00")).total_seconds() // 60)} min'
                 for entry in mine]
        apps = [app['name'] for app in (sample or {}).get('apps', []) if app.get('online') and not app.get('stale')
                and ((app.get('running') or 0) > 0 or (app.get('queued') or 0) > 0)]
        use = [gpu['utilization_percent'] for gpu in (sample or {}).get('telemetry', {}).get('gpus', [])
               if gpu.get('utilization_percent') is not None]
        if not sample or sample.get('stale') or sample.get('error') or sample.get('status') in (None, '', 'unknown'):
            reason = 'its Resources telemetry is unavailable or stale'
        elif any(entry.get('source') == 'build' for entry in mine):
            reason = 'a build is checked in'
        elif limit is None:
            reason = None
        elif mine:
            reason = 'work is checked in on the shared-memory host'
        elif apps:
            reason = f'{apps[0]} has GPU work running or queued'
        elif not use:
            reason = 'its GPU utilization is unavailable'
        else:
            reason = f'GPU utilization {max(use):.0f}% exceeds {limit:.0f}%' if max(use) > limit else None
        found[name] = (reason, doing)
    return found


def admission():
    """Long lanes on this machine are admitted one at a time, each holding this lock until the router has checked its
    job in, so two of them never count each other's worker as the one left free."""
    common = git('rev-parse', '--path-format=absolute', '--git-common-dir')
    gate = (pathlib.Path(common) / 'route-admission.lock').open('a')
    fcntl.flock(gate, fcntl.LOCK_EX)
    return gate


def launch(argv, output):
    return subprocess.Popen(argv, stdout=output, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)


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
    enabled = [host for host in config['hosts'] if host['enabled']]
    workers = [host['id'] for host in enabled]
    hosts = [host for host in workers if options.host in (None, host)]
    if not hosts:
        sys.exit(f'route: {options.config} enables no worker {options.host}')
    # One long lane (verify, the whole ruleset suite, import-suite) never takes the last free worker, so that a split
    # run, eleven minutes on three workers, can always start; --take-last allows it. The commit's suites wait on the
    # data lane, so it takes the last free worker too.
    long = parts == [lane] and lane != 'data' and not re.fullmatch(r'ruleset-\d+', lane)

    print(f'route: {", ".join(parts)} at {commit} over {", ".join(hosts)}; logs in {run}', flush=True)
    for host, (reason, doing) in workers_now(enabled, resources()).items():
        print(f'route: {host}: {f"busy, {reason}" if reason else "free"}{": " if doing else ""}{"; ".join(doing)}', flush=True)
    pending = queue.Queue()
    for part in parts:
        pending.put(part)
    results, lock = {}, threading.Lock()
    began = time.monotonic()

    def worker(host):
        held = [None]
        try:
            serve(host, held)
        except Exception as error:
            # The part this worker held gets a result, or every other worker would wait for it forever.
            with lock:
                if held[0] and held[0] not in results:
                    results[held[0]] = (host, 1, 0.0, run / f'{held[0]}.log', f'lane: route.py stopped: {error!r}')
            raise

    def attempt(host, part, log, gate):
        """Sends one lane to one worker; returns its exit code and log. A long lane's admission ends only once the
        router has checked this job in on that worker (a live ledger row that was not there before it was sent), or
        den-tool has exited: an older or stale row says nothing about this job."""
        before = {entry.get('id') for entry in resources().get('work', [])} if gate else set()

        def checked_in(entry):
            return (entry.get('id') not in before and not entry.get('stale') and entry.get('state') == 'running'
                    and entry.get('host') == host and entry.get('source') == 'build'
                    and entry.get('title') == f'{REPO} / {part}' and commit in entry.get('note', ''))
        # The installed config may be updated while a split run waits.
        current_config = json.loads(pathlib.Path(options.config).read_text())
        flags = ['--no-wait'] if current_config.get('wait_for_worker') else []
        with log.open('w') as output:
            child = launch(['den-tool', 'build', 'run', '--repo', REPO, '--lane', part, '--ref', commit,
                            '--host', host, '--config', options.config, *flags], output)
            children.add(child)
            if gate:
                while child.poll() is None and not any(map(checked_in, resources().get('work', []))):
                    time.sleep(1)
                gate.close()
            code = child.wait()
            children.discard(child)
        return code, log.read_text(errors='replace')

    def serve(host, held):
        waited_since, said = None, None
        # Until every part has a result: a part another worker had to give back may still come here.
        while len(results) < len(parts):
            try:
                part = held[0] = pending.get(timeout=5)
            except queue.Empty:
                continue
            log = run / f'{part}.log'
            start, text, code, reason, gate = time.monotonic(), '', None, None, None
            try:
                if long and not options.take_last:
                    gate = admission()
                    usable = workers_now(enabled, resources())
                    if not any(usable[other][0] is None for other in workers if other != host):
                        reason = 'it is the last worker the router can use, which a split run needs (--take-last takes it)'
                if not reason:
                    code, text = attempt(host, part, log, gate)
                    # Without the worker's checkout line the router refused the host.
                    if f' at {commit}\n' not in text or 'den-build: ' not in text:
                        reason = text.strip().splitlines()[-1] if text.strip() else f'exit {code}'
            finally:
                if gate:
                    gate.close()
            if reason:
                pending.put(part)
                held[0] = None
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
                held[0] = None
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


def shared_routes():
    """main's rounds belong to the shared checkout, whichever checkout runs or reads them, so that one lock and one
    record hold for all."""
    return pathlib.Path(git('rev-parse', '--path-format=absolute', '--git-common-dir')).parent / 'local/build/route'


def note_round(rounds, commit, state, detail='', run=None):
    """One line of main-rounds.jsonl: a round's state for a commit, and why."""
    entry = {'at': datetime.datetime.now().astimezone().isoformat(timespec='seconds'), 'commit': commit,
             'state': state, 'detail': detail, 'pid': os.getpid(), **({'run': str(run)} if run else {})}
    with (rounds / 'main-rounds.jsonl').open('a') as output:
        output.write(json.dumps(entry) + '\n')
    print(f'route: {commit[:12]} {state}{f": {detail}" if detail else ""}', flush=True)


def failed_part(text):
    """Which part of a verify lane failed, as a failure note's first line says it: the product (scripts/verify.sh)
    or the tooling around it (route.py's own tests in the lane's tooling step, the lane's setup, the router)."""
    product, tooling = 'lane: verify failed (' in text, 'lane: tooling failed (' in text
    if product and tooling:
        return 'product and tooling', 'scripts/verify.sh and the lane\'s tooling step (scripts/test_*.py) failed'
    if product:
        return 'product', 'scripts/verify.sh failed'
    if tooling:
        return 'tooling', ("the lane's tooling step (scripts/test_*.py) failed"
                           f"{'; the product passed scripts/verify.sh' if 'lane: verify took ' in text else ''}")
    last = next((line.strip() for line in reversed(text.splitlines()) if line.strip()), 'no output')
    return 'tooling', f'the lane or the router stopped before scripts/verify.sh finished: {last}'


def newest_main():
    git('fetch', '-q', 'origin', 'main')
    for name in ('verify', 'verify-failures'):
        git('fetch', '-q', 'origin', f'+refs/notes/{name}:refs/notes/route/{name}', check=False)
    return git('rev-parse', 'origin/main')


def has_result(commit):
    return noted('route/verify', commit) or noted('route/verify-failures', commit)


def usable_workers(options):
    """The workers the router would take a job on now, and what the others are doing, without for how long, so that it
    reads the same until something changes."""
    hosts = [host for host in json.loads(pathlib.Path(options.config).read_text())['hosts'] if host['enabled']]
    free, busy = [], []
    for host, (reason, doing) in workers_now(hosts, resources()).items():
        if reason is None:
            free.append(host)
        else:
            busy.append(f'{host}: {reason}' + (f' ({doing[0].rsplit(" for ", 1)[0]})' if doing else ''))
    return free, '; '.join(busy)


def main_round(options):
    rounds = shared_routes()
    rounds.mkdir(parents=True, exist_ok=True)
    with (rounds / 'main.lock').open('w') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            print('route: a round of main is already waiting or running; this one does nothing')
            return 0
        return held_round(options, rounds)


def held_round(options, rounds):
    """A round of main, under its lock."""
    newest = newest_main()
    if has_result(newest):
        print(f'route: {newest} already has a result')
        return 0
    options.whole, options.host, options.wait, options.take_last = False, None, 0, False
    waited_for, deadline, said = newest, time.monotonic() + ROUND_WAIT_MINUTES * 60, None
    while True:
        free, busy = usable_workers(options)
        # A long lane leaves the last usable worker to split runs, so the round needs two.
        if len(free) >= 2:
            newest = newest_main()
            if waited_for != newest and not has_result(waited_for):
                note_round(rounds, waited_for, 'superseded', f'{newest[:12]} landed before a worker freed')
            waited_for = newest
            if has_result(newest):
                print(f'route: {newest} already has a result')
                return 0
            run = rounds / f'main-{newest[:12]}-{time.strftime("%Y%m%dT%H%M%S")}'
            run.mkdir(parents=True)
            note_round(rounds, newest, 'running', f'the verify lane, with {", ".join(free)} free', run)
            passed, report = route('verify', newest, options, run)
            if 'found no free worker' not in report:
                break
            busy = 'another job took the free workers first'
        if time.monotonic() >= deadline:
            note_round(rounds, waited_for, 'did not start', f'no two workers were free within {ROUND_WAIT_MINUTES} '
                                                            f'minutes ({busy}); the next round waits again')
            return 0
        if busy != said:
            said = busy
            note_round(rounds, waited_for, 'waiting', f'for two free workers: {busy}')
        time.sleep(POLL_SECONDS)
    if 'lane: refusing' in report:
        note_round(rounds, newest, 'did not start', f'the store has no data for its importer revision; '
                                                    f'scripts/route.py data {newest} imports it', run)
        return 0
    log = (run / 'verify.log').read_text(errors='replace') if (run / 'verify.log').exists() else ''
    part, why = failed_part(log) if not passed else (None, None)
    headline = 'passed' if passed else f'FAILED ({part}): {why}'
    note_round(rounds, newest, 'passed' if passed else f'failed ({part})', '' if passed else why, run)
    record('verify' if passed else 'verify-failures', newest, f'scripts/route.py main: {headline}\n{report}')
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
    text = (f'main fails the full verify ({part}) at {newest} ("{subject}"), the first commit that failed: {why}. '
            f'The last that passed is {last or "none within 2000 commits"}'
            f'{f"; the {unchecked} commits between were not checked" if unchecked else ""}.\n\n{report}')
    subprocess.run(['crew-claude', 'board', 'new', BOARD, f'main fails the full verify ({part}) at {newest[:12]}', text],
                   check=False)
    return 1


def main_status():
    """The latest round of main and the newest result on main; exits 1 when main is overdue for a round."""
    rounds = shared_routes()
    newest = newest_main()
    lines = [line for line in (rounds / 'main-rounds.jsonl').read_text().splitlines() if line.strip()] \
        if (rounds / 'main-rounds.jsonl').exists() else []
    if lines:
        latest = json.loads(lines[-1])
        state = latest['state']
        # Its process, not the round's lock, says whether it still waits or runs: taking the lock to look would make
        # a round starting at that moment do nothing.
        command = pathlib.Path(f'/proc/{latest.get("pid")}/cmdline')
        if state in ('waiting', 'running') and not (command.exists() and b'route.py' in command.read_bytes()):
            state = f'stopped while {state}, without an outcome (killed, or past the unit\'s timeout)'
        print(f'latest round: {latest["at"]} {latest["commit"][:12]} {state}'
              f'{": " + latest["detail"] if latest["detail"] else ""}{"; " + latest["run"] if latest.get("run") else ""}')
    else:
        print(f'latest round: none recorded in {rounds}/main-rounds.jsonl')
    history = git('rev-list', '--first-parent', '--max-count=2000', newest).split()
    recorded = next((commit for commit in history if has_result(commit)), None)
    if recorded:
        ref = 'route/verify' if noted('route/verify', recorded) else 'route/verify-failures'
        note = git('notes', f'--ref={ref}', 'show', recorded).splitlines()[0]
        print(f'newest result on main: {recorded[:12]}, {"passed" if ref == "route/verify" else "failed"} '
              f'(refs/notes/{ref[6:]}): {note}')
    unrecorded = history[:history.index(recorded)] if recorded else history
    if not unrecorded:
        print(f'main ({newest[:12]}) has its result')
        return 0
    landed = int(git('log', '-1', '--format=%ct', unrecorded[-1]))
    minutes = int((time.time() - landed) // 60)
    print(f'main ({newest[:12]}) has {len(unrecorded)} commit(s) without a recorded round, the oldest '
          f'{unrecorded[-1][:12]} from {minutes} minutes ago')
    if minutes > OVERDUE_MINUTES:
        print(f'OVERDUE: no round recorded within {OVERDUE_MINUTES} minutes of {unrecorded[-1][:12]}; '
              f'journalctl --user -u rusty-dagger-main-check.service says why')
        return 1
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('lane', help='ruleset, import-suite, verify, data, another configured rusty-dagger lane, main or main-status')
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
    if options.lane == 'main-status':
        return main_status()

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
