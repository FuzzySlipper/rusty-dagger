"""route.py's admission of lanes to workers, with den-tool and the Resources service faked.

    python3 -m unittest discover -s scripts -p 'test_*.py'
"""
import contextlib, io, itertools, json, pathlib, tempfile, threading, time, types, unittest

import route

HOSTS = [{'id': 'den-k8', 'enabled': True}, {'id': 'den-nimo', 'enabled': True, 'max_gpu_percent': 10},
         {'id': 'den-m5', 'enabled': True, 'max_gpu_percent': 10}]
LANES = ['verify', 'ruleset-1', 'ruleset-2', 'ruleset-3']
COMMIT = 'f' * 40


class Job:
    """A den-tool build run as route.py holds it."""

    def __init__(self, poll, wait):
        self.poll, self.wait = poll, wait

    def terminate(self):
        pass


class Workers:
    """The Resources state the router reads, and den-tool build run against it: a job on a worker the router would
    refuse is refused, and an admitted one is checked in a moment after it starts. A stale row is what an earlier
    run left behind when its check-out failed."""

    ids = itertools.count()

    def __init__(self, building=(), gpu_busy=(), stale=()):
        self.lock, self.started = threading.Lock(), []
        self.work = [self.entry(host) for host in building] + [self.entry(host, 'verify', stale=True) for host in stale]
        self.hosts = [{'id': host['id'], 'status': 'quiet', 'stale': False, 'error': None,
                       'telemetry': {'gpus': [{'utilization_percent': 0}]},
                       'apps': [{'name': 'ComfyUI', 'online': True, 'stale': False,
                                 'running': 1 if host['id'] in gpu_busy else 0, 'queued': 0}]} for host in HOSTS]

    @classmethod
    def entry(cls, host, lane='x', stale=False):
        return {'id': next(cls.ids), 'host': host, 'source': 'build', 'state': 'running', 'stale': stale,
                'title': f'rusty-dagger / {lane}', 'note': f'Commit {COMMIT}', 'started_at': '2026-11-13T00:00:00.000000Z'}

    def state(self):
        with self.lock:
            return {'hosts': self.hosts, 'work': list(self.work)}

    def launch(self, argv, output):
        host, lane = argv[argv.index('--host') + 1], argv[argv.index('--lane') + 1]
        if route.workers_now(HOSTS, self.state())[host][0]:
            output.write('den-tool: no eligible host\n')
            return Job(lambda: 1, lambda: 1)
        with self.lock:
            self.started.append((lane, host))
        output.write(f'den-build: checkout at {COMMIT}\n')
        job = self.entry(host, lane)
        threading.Timer(0.3, lambda: self.work.append(job)).start()
        done = time.monotonic() + 0.6

        def wait():
            time.sleep(max(0, done - time.monotonic()))
            with self.lock:
                self.work.remove(job)
            return 0
        return Job(lambda: 0 if time.monotonic() > done else None, wait)


class AdmissionTest(unittest.TestCase):
    def setUp(self):
        self.scratch = pathlib.Path(tempfile.mkdtemp())
        self.config = self.scratch / 'config.json'
        self.config.write_text(json.dumps({'hosts': HOSTS, 'repositories': {
            'rusty-dagger': {'lanes': {lane: {} for lane in LANES}}}}))

    def send(self, workers, lane, host=None, take_last=False):
        route.resources, route.launch = workers.state, workers.launch
        options = types.SimpleNamespace(config=str(self.config), whole=False, host=host, wait=0, take_last=take_last)
        run = pathlib.Path(tempfile.mkdtemp(dir=self.scratch))
        with contextlib.redirect_stdout(io.StringIO()):
            return route.route(lane, COMMIT, options, run)[0]

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
        workers = Workers(building=['den-k8'])
        sends = [threading.Thread(target=self.send, args=(workers, 'verify', host)) for host in ('den-nimo', 'den-m5')]
        for send in sends:
            send.start()
        for send in sends:
            send.join()
        self.assertEqual(len(workers.started), 1)

    def test_a_stale_row_does_not_admit_the_next_long_lane_early(self):
        # Both free workers carry a stale row from an earlier verify; neither may stand in for the new check-in.
        workers = Workers(building=['den-k8'], stale=['den-nimo', 'den-m5'])
        sends = [threading.Thread(target=self.send, args=(workers, 'verify', host)) for host in ('den-nimo', 'den-m5')]
        for send in sends:
            send.start()
        for send in sends:
            send.join()
        self.assertEqual(len(workers.started), 1)

    def test_a_split_run_takes_the_last_worker(self):
        workers = Workers(building=['den-k8'], gpu_busy=['den-nimo'])
        self.send(workers, 'ruleset')
        self.assertEqual(sorted(workers.started), [('ruleset-1', 'den-m5'), ('ruleset-2', 'den-m5'), ('ruleset-3', 'den-m5')])


if __name__ == '__main__':
    unittest.main()
