#!/usr/bin/env node
// The real-Engine smoke: the product is started on its pinned runtime, a browser presses the entry
// screen's Begin button the way a player does, and the run passes only when the product reaches
// ordinary play with no error or terminal diagnostic on the way. Every other suite proves its paths
// against fakes that do not enforce Engine ownership rules; this is the one stage that does not.
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const repository = resolve(import.meta.dirname, '..');
const project = 'src/WorldRpg.Host/WorldRpg.Host.csproj';
const chromium = process.env.RUSTY_CHROMIUM_PATH ?? 'chromium';
// The opening cinematics play in full before the entry screen yields, which takes about three minutes.
const playDeadlineMs = 300_000;
const settleMs = 5_000;
// The two diagnostics a product-side ownership fault produces; either one fails the run even when an
// Engine build reports it below error severity.
const faultCodes = new Set(['CSHARP_RENDER_RESOURCE_IN_USE', 'CSHARP_RUNTIME_TAINTED']);

const started = Date.now();
const log = message => console.log(`play-smoke ${((Date.now() - started) / 1000).toFixed(1)}s ${message}`);
const sleep = ms => new Promise(done => setTimeout(done, ms));
const children = [];
let stopping = false;
const profile = mkdtempSync(join(tmpdir(), 'dagger-play-smoke-'));

// A synchronous pause, because every exit path (including a failure mid-run) stops through here.
const pause = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

function groupAlive(pid) {
  try { process.kill(-pid, 0); return true; } catch { return false; }
}

function stopAll() {
  // Each child leads its own process group, so the host's runtime worker and Chromium's renderers
  // stop with it. The profile is removed only once every group is gone: Chromium keeps writing it
  // until it has exited, and removing it earlier fails on a directory that refills.
  for (const child of children) {
    try { process.kill(-child.pid, 'SIGTERM'); } catch { /* already gone */ }
  }
  for (const [signal, waitMs] of [['SIGTERM', 10_000], ['SIGKILL', 5_000]]) {
    const deadline = Date.now() + waitMs;
    while (children.some(child => groupAlive(child.pid)) && Date.now() < deadline) pause(100);
    if (!children.some(child => groupAlive(child.pid))) break;
    if (signal === 'SIGTERM') {
      for (const child of children) {
        try { process.kill(-child.pid, 'SIGKILL'); } catch { /* already gone */ }
      }
    }
  }
  rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
}

function fail(message) {
  if (stopping) return;
  stopping = true;
  log(`FAILED: ${message}`);
  stopAll();
  process.exit(1);
}

process.on('SIGINT', () => fail('interrupted'));
process.on('SIGTERM', () => fail('terminated'));

function freePort() {
  return new Promise((done, reject) => {
    const server = createServer();
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => done(port));
    });
  });
}

function launch(command, args, options = {}) {
  const child = spawn(command, args, { cwd: repository, detached: true, ...options });
  children.push(child);
  child.once('exit', (code, signal) => {
    if (!stopping) fail(`${command} exited early (code ${code}, signal ${signal})`);
  });
  return child;
}


const hostPort = await freePort();
const debugPort = await freePort();
const origin = `http://127.0.0.1:${hostPort}`;

log(`starting the product at ${origin}`);
const host = launch('rusty', ['dev', '--project', project, '--live-debug', '--bind-host', '127.0.0.1', '--port', String(hostPort)],
  { stdio: ['ignore', 'pipe', 'pipe'] });
const hostOutput = [];
await new Promise(ready => {
  const deadline = setTimeout(() => fail(`the host did not listen within 180s:\n${hostOutput.join('').slice(-2000)}`), 180_000);
  const read = chunk => {
    hostOutput.push(chunk.toString());
    if (hostOutput.join('').includes(`listening at ${origin}`)) {
      clearTimeout(deadline);
      ready();
    }
  };
  host.stdout.on('data', read);
  host.stderr.on('data', read);
});
log('host listening');

async function post(path, body, contentType) {
  const response = await fetch(`${origin}${path}`, { method: 'POST', headers: { 'content-type': contentType }, body });
  return { status: response.status, text: await response.text() };
}

let cursor = null;
const faults = [];
let cueStarted = false;
let telemetry = null;
async function readDiagnostics() {
  const { status, text } = await post('/__rusty/product/runtime/diagnostics/read',
    JSON.stringify(cursor === null ? {} : { after: cursor }), 'application/json');
  if (status !== 200) fail(`diagnostics read returned ${status}: ${text}`);
  const read = JSON.parse(text);
  cursor = read.nextCursor;
  telemetry = read.telemetry;
  for (const event of read.events) {
    if (event.severity === 'error' || event.disposition === 'terminal' || faultCodes.has(event.code))
      faults.push(`${event.source} ${event.code}: ${event.message}`);
    if (event.source === 'daggerfall.music' && event.code === 'cue.started') cueStarted = true;
  }
}

async function observedMode() {
  const { status, text } = await post('/__rusty/product/runtime/debug/execute', 'playtest.observe', 'text/plain; charset=utf-8');
  if (status !== 200) fail(`playtest.observe returned ${status}: ${text}`);
  return JSON.parse(text).mode;
}

// The Engine's own --headless browser offers no debugging endpoint, so the smoke opens one it can
// drive, with the flags the Engine's headless launch uses.
launch(chromium, ['--headless=new', '--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu', '--no-first-run',
  '--no-default-browser-check', '--password-store=basic', '--disable-background-networking', '--disable-extensions',
  '--disable-sync', '--hide-scrollbars', '--window-size=1280,720', `--user-data-dir=${profile}`,
  `--remote-debugging-port=${debugPort}`, 'about:blank'], { stdio: 'ignore' });

let page;
for (let attempt = 0; attempt < 100 && !page; attempt++) {
  await sleep(200);
  try {
    page = (await (await fetch(`http://127.0.0.1:${debugPort}/json`)).json()).find(target => target.type === 'page');
  } catch { /* the endpoint is not up yet */ }
}
if (!page) fail(`${chromium} exposed no page on its debugging port`);

const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((open, reject) => {
  socket.addEventListener('open', open, { once: true });
  socket.addEventListener('error', () => reject(new Error('debugging socket failed')), { once: true });
});
let nextId = 0;
const pending = new Map();
socket.addEventListener('message', event => {
  const message = JSON.parse(event.data);
  if (message.id !== undefined && pending.has(message.id)) {
    pending.get(message.id)(message);
    pending.delete(message.id);
  }
});
function cdp(method, params = {}) {
  return new Promise((done, reject) => {
    const id = ++nextId;
    const timer = setTimeout(() => reject(new Error(`${method} did not answer within 15s`)), 15_000);
    pending.set(id, message => {
      clearTimeout(timer);
      if (message.error) reject(new Error(`${method}: ${message.error.message}`));
      else done(message.result);
    });
    socket.send(JSON.stringify({ id, method, params }));
  });
}

await cdp('Page.navigate', { url: origin });
log('page opened');

// The entry screen accepts a pointer press on its Begin button; a synthetic element click does not
// reach the product's intent path, so the smoke presses where a player would.
let begin = null;
for (let attempt = 0; attempt < 120 && begin === null; attempt++) {
  await sleep(500);
  const { result } = await cdp('Runtime.evaluate', {
    returnByValue: true,
    expression: `(() => {
      const button = document.querySelector('button.dagger-entry-begin');
      if (!button || button.closest('[hidden]')) return null;
      const box = button.getBoundingClientRect();
      return box.width > 0 ? { x: box.x + box.width / 2, y: box.y + box.height / 2 } : null;
    })()`,
  });
  begin = result.value ?? null;
}
if (begin === null) fail('the entry screen never showed its Begin button');
for (const type of ['mouseMoved', 'mousePressed', 'mouseReleased'])
  await cdp('Input.dispatchMouseEvent', { type, x: begin.x, y: begin.y, button: 'left', clickCount: 1 });
const pressed = Date.now();
log('pressed Begin');

let mode = await observedMode();
while (mode !== 'Playing') {
  if (Date.now() - pressed > playDeadlineMs) fail(`the product stayed in ${mode} for ${playDeadlineMs / 1000}s after Begin`);
  await readDiagnostics();
  if (faults.length > 0) fail(`diagnostics before play:\n  ${faults.join('\n  ')}`);
  await sleep(2_000);
  mode = await observedMode();
}
log('reached Playing');
await sleep(settleMs);
await readDiagnostics();
if (faults.length > 0) fail(`diagnostics in play:\n  ${faults.join('\n  ')}`);
mode = await observedMode();
if (mode !== 'Playing') fail(`the product left play for ${mode} within ${settleMs / 1000}s`);

const latest = telemetry?.updateAttribution?.latest ?? {};
const characterSteps = Number(latest.characterStepCalls ?? 0);
if (characterSteps <= 0) fail('ordinary play admitted no character step in its latest update');
if (!cueStarted) log('warning: no daggerfall.music cue.started diagnostic was observed');
log(`passed: mode Playing, characterStepCalls ${characterSteps}, voxelResidencyCalls ${latest.voxelResidencyCalls ?? 'absent'}, `
  + `music cue ${cueStarted ? 'started' : 'not observed'}`);
stopping = true;
socket.close();
stopAll();
process.exit(0);
