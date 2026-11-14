"""route.py's admission of lanes to workers and its rounds of main, with den-tool, the Resources service and git faked.

    python3 -m unittest discover -s scripts -p 'test_*.py'
"""
import contextlib, fcntl, io, itertools, json, pathlib, tempfile, threading, time, types, unittest

import route

HOSTS = [{'id': 'den-k8', 'enabled': True}, {'id': 'den-nimo', 'enabled': True, 'max_gpu_percent': 10},
         {'id': 'den-m5', 'enabled': True, 'max_gpu_percent': 10}]
LANES = ['verify', 'data', 'ruleset-1', 'ruleset-2', 'ruleset-3']
COMMIT = 'f' * 40


class Job:
    """A den-tool build run as route.py holds it."""

    def __init__(self, poll, wait):
        self.poll, self.wait = poll, wait

    def terminate(self):
        pass


class Workers:
    """The Resources state the router reads, and den-tool build run against it: a job on a worker the router would
    refuse is refused, and a job takes a slot until it ends. A stale row is what an earlier run left behind when its
    check-out failed. A held job runs until the test ends it, as a verify outlasts its admission."""

    ids = itertools.count()

    def __init__(self, building=(), gpu_busy=(), stale=(), held=False, hosts=HOSTS):
        self.lock, self.started, self.ended = threading.Lock(), [], threading.Event()
        self.config, self.launched = hosts, []
        if not held:
            self.ended.set()
        self.work = [self.entry(host) for host in building] + [self.entry(host, 'verify', stale=True) for host in stale]
        self.hosts = [{'id': host['id'], 'status': 'working' if host['id'] in gpu_busy else 'quiet',
                       'stale': False, 'error': None,
                       'telemetry': {'gpus': [{'utilization_percent': 0}]},
                       'apps': [{'name': 'ComfyUI', 'online': True, 'stale': False,
                                 'running': 1 if host['id'] in gpu_busy else 0, 'queued': 0}]} for host in hosts]

    @classmethod
    def entry(cls, host, lane='x', stale=False):
        return {'id': next(cls.ids), 'host': host, 'source': 'build', 'state': 'running', 'stale': stale,
                'title': f'rusty-dagger / {lane}', 'note': f'Commit {COMMIT}', 'started_at': '2026-11-13T00:00:00.000000Z'}

    def state(self):
        with self.lock:
            return {'hosts': self.hosts, 'work': list(self.work)}

    def launch(self, argv, output):
        explicit = argv[argv.index('--host') + 1] if '--host' in argv else None
        lane = argv[argv.index('--lane') + 1]
        with self.lock:
            self.launched.append(explicit)
            state = {'hosts': self.hosts, 'work': list(self.work)}
            usable = route.workers_now(self.config, state)
            order = sorted(self.hosts, key=lambda sample: sample['status'] != 'quiet')
            host = next((sample['id'] for sample in order
                         if explicit in (None, sample['id']) and not usable[sample['id']][0]), None)
            if host is None:
                output.write('den-tool: no eligible host\n')
                return Job(lambda: 1, lambda: 1)
            self.started.append((lane, host))
            job = self.entry(host, lane)
            self.work.append(job)
        output.write(f'den-tool build: rusty-dagger/{lane} at {COMMIT} on {host} (8 jobs)\n')
        output.write(f'den-build: /checkout at {COMMIT}\n')
        output.flush()
        done = time.monotonic() + 0.6

        def wait():
            time.sleep(max(0, done - time.monotonic()))
            self.ended.wait()
            with self.lock:
                self.work.remove(job)
            return 0
        return Job(lambda: 0 if time.monotonic() > done and self.ended.is_set() else None, wait)


class AdmissionTest(unittest.TestCase):
    def setUp(self):
        # Once for the test: sends in threads would each swap sys.stdout and restore it out of order.
        self.enterContext(contextlib.redirect_stdout(io.StringIO()))
        self.scratch = pathlib.Path(self.enterContext(tempfile.TemporaryDirectory()))
        self.config = self.scratch / 'config.json'
        self.config.write_text(json.dumps({'hosts': HOSTS, 'repositories': {
            'rusty-dagger': {'lanes': {lane: {} for lane in LANES}}}}))

    def send(self, workers, lane, host=None, take_last=False, wait=0):
        route.resources, route.launch = workers.state, workers.launch
        options = types.SimpleNamespace(config=str(self.config), whole=False, host=host, wait=wait, take_last=take_last)
        run = pathlib.Path(tempfile.mkdtemp(dir=self.scratch))
        return route.route(lane, COMMIT, options, run)[0]

    def send_together(self, workers, lane, hosts):
        """Sends a long lane to each host at once; held jobs end once all but one send has decided, or after ten
        seconds when more than one was admitted."""
        sends = [threading.Thread(target=self.send, args=(workers, lane, host)) for host in hosts]
        for send in sends:
            send.start()
        deadline = time.monotonic() + 10
        while sum(send.is_alive() for send in sends) > 1 and time.monotonic() < deadline:
            time.sleep(0.05)
        workers.ended.set()
        for send in sends:
            send.join()

    def test_a_long_lane_leaves_the_last_worker_the_router_can_use(self):
        # den-k8 builds and den-nimo's GPU is busy: den-m5 is the only worker left, so verify waits for another.
        workers = Workers(building=['den-k8'], gpu_busy=['den-nimo'])
        self.send(workers, 'verify')
        self.assertEqual(workers.started, [])

    def test_take_last_lets_a_long_lane_have_it(self):
        workers = Workers(building=['den-k8'], gpu_busy=['den-nimo'])
        self.send(workers, 'verify', take_last=True)
        self.assertEqual(workers.started, [('verify', 'den-m5')])

    def test_a_long_lane_takes_one_of_two_free_workers(self):
        workers = Workers(building=['den-k8'])
        self.send(workers, 'verify')
        self.assertEqual(len(workers.started), 1)

    def test_long_lanes_sent_together_still_leave_one_worker(self):
        workers = Workers(building=['den-k8'], held=True)
        self.send_together(workers, 'verify', ('den-nimo', 'den-m5'))
        self.assertEqual(len(workers.started), 1)

    def test_a_stale_row_does_not_admit_the_next_long_lane_early(self):
        # Both free workers carry a stale row from an earlier verify; neither may stand in for the new check-in.
        workers = Workers(building=['den-k8'], stale=['den-nimo', 'den-m5'], held=True)
        self.send_together(workers, 'verify', ('den-nimo', 'den-m5'))
        self.assertEqual(len(workers.started), 1)

    def test_the_data_lane_takes_the_last_worker(self):
        # The commit's suites wait on its data, so the data lane does not leave the last worker for a split run.
        workers = Workers(building=['den-k8'], gpu_busy=['den-nimo'])
        self.send(workers, 'data')
        self.assertEqual(workers.started, [('data', 'den-m5')])

    def test_a_split_run_takes_the_last_worker(self):
        workers = Workers(building=['den-k8'], gpu_busy=['den-nimo'])
        self.send(workers, 'ruleset', host='den-m5', wait=None)
        self.assertEqual(sorted(workers.started), [('ruleset-1', 'den-m5'), ('ruleset-2', 'den-m5'), ('ruleset-3', 'den-m5')])

    def test_unpinned_data_uses_the_router_order_with_comfy_as_a_fallback(self):
        hosts = [{key: value for key, value in host.items() if key != 'max_gpu_percent'} for host in HOSTS]
        config = json.loads(self.config.read_text())
        config['hosts'] = hosts
        self.config.write_text(json.dumps(config))
        building = ['den-k8']
        for expected in ('den-m5', 'den-nimo'):
            with self.subTest(expected=expected):
                workers = Workers(building=building, gpu_busy=['den-nimo'], hosts=hosts)
                self.assertTrue(self.send(workers, 'data'))
                self.assertEqual(workers.launched, [None])
                self.assertEqual(workers.started, [('data', expected)])
            building.append('den-m5')



class RoundTest(unittest.TestCase):
    """A round of main: it waits for two free workers, checks whatever is newest then, and says what it did."""

    def setUp(self):
        self.enterContext(contextlib.redirect_stdout(io.StringIO()))
        self.scratch = pathlib.Path(self.enterContext(tempfile.TemporaryDirectory()))
        self.config = self.scratch / 'config.json'
        self.config.write_text(json.dumps({'hosts': HOSTS, 'repositories': {
            'rusty-dagger': {'lanes': {lane: {} for lane in LANES}}}}))
        self.newest, self.recorded = COMMIT, []
        saved = {name: getattr(route, name) for name in
                 ('shared_routes', 'newest_main', 'has_result', 'record', 'ROUND_WAIT_MINUTES', 'POLL_SECONDS')}
        self.addCleanup(lambda: [setattr(route, name, value) for name, value in saved.items()])
        route.shared_routes = lambda: self.scratch
        route.newest_main = lambda: self.newest
        route.has_result = lambda commit: False
        route.record = lambda name, commit, text: self.recorded.append((name, commit, text.splitlines()[0]))
        route.ROUND_WAIT_MINUTES, route.POLL_SECONDS = 0.05, 0.05

    def round(self, workers):
        route.resources, route.launch = workers.state, workers.launch
        return route.main_round(types.SimpleNamespace(config=str(self.config)))

    def states(self):
        rounds = self.scratch / 'main-rounds.jsonl'
        lines = rounds.read_text().splitlines() if rounds.exists() else []
        return [(entry['commit'][:1], entry['state']) for entry in map(json.loads, lines)]

    def test_it_waits_for_two_free_workers_and_checks_the_newest_commit_then(self):
        workers = Workers(building=['den-k8', 'den-nimo', 'den-m5'])
        self.newest = 'e' * 40

        def split_run_ends():
            with workers.lock:
                workers.work.clear()
            self.newest = COMMIT
        threading.Timer(0.5, split_run_ends).start()
        self.assertEqual(self.round(workers), 0)
        self.assertEqual(len(workers.started), 1)
        self.assertEqual(self.states(), [('e', 'waiting'), ('e', 'superseded'), ('f', 'running'), ('f', 'passed')])
        self.assertEqual(self.recorded, [('verify', COMMIT, 'scripts/route.py main: passed')])

    def test_it_leaves_the_last_free_worker_and_says_it_did_not_start(self):
        workers = Workers(building=['den-k8', 'den-nimo'])
        self.round(workers)
        self.assertEqual(workers.started, [])
        self.assertEqual(self.states(), [('f', 'waiting'), ('f', 'did not start')])
        self.assertEqual(self.recorded, [])

    def test_a_round_that_finds_another_waiting_does_nothing(self):
        with (self.scratch / 'main.lock').open('w') as held:
            fcntl.flock(held, fcntl.LOCK_EX)
            self.assertEqual(self.round(Workers()), 0)
        self.assertEqual(self.states(), [])

    def test_a_failure_names_the_part_that_failed(self):
        took = 'lane: tooling took 12 s, peak memory 40 MiB\nlane: verify took 2400 s, peak memory 9000 MiB\n'
        self.assertEqual(route.failed_part(took + 'lane: verify failed (exit 1)\n')[0], 'product')
        self.assertEqual(route.failed_part('lane: tooling failed (exit 1)\n' + took),
                         ('tooling', "the lane's tooling step (scripts/test_*.py) failed; the product passed scripts/verify.sh"))
        self.assertEqual(route.failed_part('lane: tooling failed (exit 1)\nlane: verify failed (exit 1)\n')[0],
                         'product and tooling')
        self.assertEqual(route.failed_part('lane: pair and UI dependencies ready (40 s)\nnpm ERR! network\n'),
                         ('tooling', 'the lane or the router stopped before scripts/verify.sh finished: npm ERR! network'))

if __name__ == '__main__':
    unittest.main()
