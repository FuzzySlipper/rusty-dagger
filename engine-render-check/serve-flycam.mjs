#!/usr/bin/env node
/**
 * Long-lived interactive flycam server (den-serve entry point):
 *
 *     node engine-render-check/serve-flycam.mjs [host] [port]
 *
 * Builds the adapter + runtime bins if needed, dumps the protocol-14 frame
 * from the real dagger-studio-adapter, spawns the Rust directional-sprite
 * authority (dagger-sprite-frames --serve), and serves the flycam page
 * through an in-process vite server. /healthz answers the den-serve
 * readiness probe; /assignments proxies the sprite authority so the page
 * stays single-origin.
 */
import { createServer as createHttpServer } from 'node:http';
import { createServer as createNetServer } from 'node:net';
import { spawn, spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer as createViteServer } from 'vite';
import { dumpFrame } from './dump-frame.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const host = process.argv[2] ?? '127.0.0.1';
const port = Number(process.argv[3] ?? 4174);

/** A definitely-free localhost port (fixed arithmetic collides with whatever
 * else runs on this LAN box). */
function freePort() {
  return new Promise((resolvePort, rejectPort) => {
    const probe = createNetServer();
    probe.once('error', rejectPort);
    probe.listen(0, '127.0.0.1', () => {
      const assigned = probe.address().port;
      probe.close(() => resolvePort(assigned));
    });
  });
}
const spritePort = await freePort();
// vite's middleware-mode HMR websocket ignores hmr:false and always binds
// 24678 — a second flycam instance collides with the first and its pages log
// handshake errors. Give each instance its own HMR port.
const hmrPort = await freePort();

// Build the Rust binaries the dump + sprite authority need (cheap when fresh).
for (const pkg of ['dagger-studio-adapter', 'dagger-runtime']) {
  const built = spawnSync('cargo', ['build', '-q', '-p', pkg], { cwd: ROOT, stdio: 'inherit' });
  if (built.status !== 0) {
    console.error(`cargo build -p ${pkg} failed`);
    process.exit(1);
  }
}

await dumpFrame();
console.log('frame dumped from dagger-studio-adapter');

const spriteServer = spawn(
  resolve(ROOT, 'target/debug/dagger-sprite-frames'),
  [resolve(HERE, 'generated/enemies.json'), '--serve', `127.0.0.1:${spritePort}`],
  { stdio: ['ignore', 'ignore', 'inherit'] },
);
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    spriteServer.kill(signal);
    process.exit(0);
  });
}
// Readiness probe: fail loudly at startup rather than 404/502 per frame.
{
  const deadline = Date.now() + 10_000;
  for (;;) {
    try {
      const probe = await fetch(`http://127.0.0.1:${spritePort}/assignments?cam=25.6,1.6,-25.6`);
      if (probe.ok) break;
    } catch { /* not up yet */ }
    if (Date.now() > deadline) {
      console.error(`sprite authority did not come up on 127.0.0.1:${spritePort}`);
      process.exit(1);
    }
    await new Promise((r) => setTimeout(r, 100));
  }
}

const vite = await createViteServer({
  root: HERE,
  logLevel: 'warn',
  publicDir: resolve(ROOT, 'content'),
  appType: 'spa',
  server: { middlewareMode: true, fs: { allow: [ROOT] }, hmr: { port: hmrPort } },
});

const server = createHttpServer(async (request, response) => {
  const url = new URL(request.url ?? '/', `http://${host}:${port}`);
  if (url.pathname === '/healthz') {
    const body = '{"status":"ok","project":"rusty-dagger","view":"flycam"}';
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(body);
    return;
  }
  if (url.pathname === '/assignments') {
    // Proxy the Rust sprite authority. url.search forwards the raw query
    // (re-stringifying searchParams would URL-encode the commas).
    try {
      const upstream = await fetch(`http://127.0.0.1:${spritePort}/assignments${url.search}`);
      response.writeHead(upstream.status, { 'content-type': 'application/json' });
      response.end(Buffer.from(await upstream.arrayBuffer()));
    } catch (caught) {
      response.writeHead(502, { 'content-type': 'application/json' });
      response.end(JSON.stringify({ error: `sprite authority unavailable: ${caught}` }));
    }
    return;
  }
  if (url.pathname === '/') {
    request.url = '/flycam.html';
  }
  vite.middlewares(request, response);
});

server.listen(port, host, () => {
  console.log(`rusty-dagger flycam (rusty-engine renderer) on http://${host}:${port}/`);
});
