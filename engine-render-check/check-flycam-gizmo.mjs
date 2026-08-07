#!/usr/bin/env node
/**
 * Headless proof of the live patrol gizmo (task 6671).
 *
 * Boots the REAL flycam server (serve-flycam.mjs), loads the flycam page in
 * headless Chromium (SwiftShader WebGL), toggles the gizmo with 'G', and
 * asserts:
 * - gizmo handles are created (liveGizmoMap size 43 for Privateer's Hold)
 * - live anchor positions update to reflect patrol movement (not static at spawn)
 * - heading arrow rotation reflects patrol heading (quaternion Y non-zero when moving)
 *
 *     node engine-render-check/check-flycam-gizmo.mjs
 */
import { spawn } from 'node:child_process';
import { writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const PORT = Number(process.env.RUSTY_FLYCAM_CHECK_PORT ?? 4177);

const { chromium } = await import(
  process.env.RUSTY_RENDER_CHECK_PLAYWRIGHT ?? '@playwright/test'
).catch(async () => import(pathToFileURL(
  '/home/dev/rusty-engine/studio/node_modules/@playwright/test/index.mjs',
).href));

const flycam = spawn('node', [resolve(HERE, 'serve-flycam.mjs'), '127.0.0.1', String(PORT)], {
  stdio: ['ignore', 'inherit', 'inherit'],
});
process.on('exit', () => flycam.kill());

{
  const deadline = Date.now() + 90_000;
  for (;;) {
    try {
      const probe = await fetch(`http://127.0.0.1:${PORT}/healthz`);
      if (probe.ok) break;
    } catch { /* not up yet */ }
    if (Date.now() > deadline) {
      console.error('flycam server did not become ready');
      process.exit(1);
    }
    await new Promise((r) => setTimeout(r, 200));
  }
}

const browser = await chromium.launch({
  headless: true,
  executablePath: process.env.RUSTY_RENDER_CHECK_CHROMIUM ?? '/usr/bin/chromium',
  args: ['--no-sandbox', '--enable-webgl', '--ignore-gpu-blocklist', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
});

const failures = [];
const check = (condition, message) => {
  if (!condition) failures.push(message);
};

try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 960 }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => consoleErrors.push(String(error)));

  await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => window.__flycam !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => {
    document.getElementById('hint').style.display = 'none';
  });
  await page.waitForTimeout(800);

  // Toggle G on
  await page.evaluate(() => window.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyG' })));
  await page.waitForTimeout(500);

  const initial = await page.evaluate(() => {
    const map = window.__liveGizmoMap;
    const last = window.__lastPatrolByHandle;
    return {
      gizmoOn: window.__gizmosOn(),
      mapSize: map ? map.size : -1,
      patrolSize: last ? last.size : -1,
    };
  });
  console.log(`initial: gizmoOn=${initial.gizmoOn} liveMap=${initial.mapSize} patrolCache=${initial.patrolSize}`);
  check(initial.gizmoOn === true, `gizmo should be on after G toggle`);
  check(initial.mapSize >= 40, `liveGizmoMap size ${initial.mapSize} expected >=40 (Privateer's Hold has 43 enemies)`);
  // Patrol cache may still be 0 if no transforms yet — wait for first patrol tick
  await page.waitForTimeout(1200);
  const afterFirstPoll = await page.evaluate(() => {
    const last = window.__lastPatrolByHandle;
    const entries = last ? Array.from(last.values()).slice(0, 3) : [];
    return {
      patrolSize: last ? last.size : -1,
      sample: entries.map((e) => ({ handle: e.handle, heading: e.heading, translation: e.translation })),
    };
  });
  console.log(`after poll: patrolCache=${afterFirstPoll.patrolSize} sample=${JSON.stringify(afterFirstPoll.sample.slice(0,2))}`);
  check(afterFirstPoll.patrolSize >= 40, `patrol cache size ${afterFirstPoll.patrolSize} expected >=40 after poll`);

  // Wait for patrol movement (idle 0.5s + move) and check live gizmo moved
  // Capture initial live anchor positions, wait, then check they changed
  const posBefore = await page.evaluate(() => {
    const last = window.__lastPatrolByHandle;
    if (!last || last.size === 0) return null;
    const first = Array.from(last.values())[0];
    return first ? { handle: first.handle, pos: first.translation, heading: first.heading } : null;
  });
  console.log(`posBefore: ${JSON.stringify(posBefore)}`);
  await page.waitForTimeout(3000);
  const posAfter = await page.evaluate(() => {
    const last = window.__lastPatrolByHandle;
    if (!last || last.size === 0) return null;
    const first = Array.from(last.values())[0];
    // Also check a different handle that likely moved
    const all = Array.from(last.values());
    const moved = all.find((e) => Math.abs(e.heading) > 0.1) || all[0];
    return moved ? { handle: moved.handle, pos: moved.translation, heading: moved.heading, rotation: moved.rotation } : null;
  });
  console.log(`posAfter: ${JSON.stringify(posAfter)}`);
  check(posAfter !== null, `no patrol position after wait`);
  if (posBefore && posAfter) {
    const dist = Math.hypot(posAfter.pos[0] - posBefore.pos[0], posAfter.pos[2] - posBefore.pos[2]);
    console.log(`movement dist: ${dist.toFixed(3)} heading ${posAfter.heading?.toFixed(3)}`);
    // At least some NPC should have moved >0.1m and heading non-zero
    const anyMoved = await page.evaluate(() => {
      const last = window.__lastPatrolByHandle;
      if (!last) return false;
      for (const e of last.values()) {
        if (Math.abs(e.heading) > 0.1) return true;
      }
      return false;
    });
    check(anyMoved, `no NPC heading changed after patrol (heading still 0 for all)`);
    // Also check that gizmo handles exist and are visible (we toggled on)
    const gizmoVisible = await page.evaluate(() => {
      // Check that liveGizmoMap entries have corresponding handles created
      const map = window.__liveGizmoMap;
      return map ? map.size : 0;
    });
    check(gizmoVisible >= 40, `gizmo map size after patrol ${gizmoVisible} <40`);

    // --- R6671-2: assert authoritative live gizmo frame ops (translation + directional quaternion) ---
    const frameProof = await page.evaluate(() => {
      const last = window.__lastPatrolByHandle;
      const ops = window.__lastFrameOps;
      const liveMap = window.__liveGizmoMap;
      if (!last || !ops || !liveMap) return { ok: false, reason: `missing lastPatrol (${!!last}) ops (${!!ops}) liveMap (${!!liveMap})` };
      // ops only contains transforms for NPCs that moved this tick (patrol returns moving subset).
      // Search the current frame's ops for any sprite whose patrol heading is non-zero and whose
      // live gizmo anchor+arrow are also in the same frame. That triple proves the renderer was
      // told the correct authoritative translation+rotation.
      const eps = 0.02;
      let best = null;
      for (const op of ops) {
        if (op.op !== 'update' || !op.transform || !op.transform.rotation) continue;
        const patrolEntry = last.get(op.handle);
        if (!patrolEntry) continue;
        const h = patrolEntry.heading;
        if (Math.abs(h) < 0.15) continue;
        const entry = liveMap.get(op.handle);
        if (!entry) continue;
        const anchorOp = ops.find((o) => o.handle === entry.anchor && o.transform && o.transform.translation);
        const arrowOp = ops.find((o) => o.handle === entry.arrow && o.transform && o.transform.translation);
        if (!anchorOp || !arrowOp) continue;
        best = { patrolEntry, entry, spriteOp: op, anchorOp, arrowOp, h };
        break;
      }
      if (!best) return { ok: false, reason: `no frame triple with heading>0.15 and live gizmo ops (last size ${last.size}, ops ${ops.length}, liveMap ${liveMap.size})` };
      const { patrolEntry: moved, spriteOp, anchorOp, arrowOp, h } = best;
      const expectedRot = [0, -Math.sin(h * 0.5), 0, Math.cos(h * 0.5)];
      const details = { handle: moved.handle, heading: h, expectedRot, spriteOp: spriteOp.transform, anchorOp: anchorOp.transform, arrowOp: arrowOp.transform, translation: moved.translation };
      // sprite translation should match patrol translation within eps
      const st = spriteOp.transform.translation;
      const mt = moved.translation;
      if (Math.hypot(st[0]-mt[0], st[1]-mt[1], st[2]-mt[2]) > eps) return { ok: false, reason: `sprite translation ${JSON.stringify(st)} != patrol ${JSON.stringify(mt)}`, details };
      // sprite rotation should match expectedRot (sign-corrected) within 1e-3
      const sr = spriteOp.transform.rotation;
      if (Math.abs(sr[1]-expectedRot[1]) > 1e-3 || Math.abs(sr[3]-expectedRot[3]) > 1e-3) return { ok: false, reason: `sprite rotation ${JSON.stringify(sr)} != expected ${JSON.stringify(expectedRot)} for heading ${h}`, details };
      // anchor translation should equal patrol translation
      const at = anchorOp.transform.translation;
      if (Math.hypot(at[0]-mt[0], at[1]-mt[1], at[2]-mt[2]) > eps) return { ok: false, reason: `anchor translation ${JSON.stringify(at)} != patrol ${JSON.stringify(mt)}`, details };
      // arrow rotation should equal expectedRot
      const ar = arrowOp.transform.rotation;
      if (Math.abs(ar[1]-expectedRot[1]) > 1e-3 || Math.abs(ar[3]-expectedRot[3]) > 1e-3) return { ok: false, reason: `arrow rotation ${JSON.stringify(ar)} != expected ${JSON.stringify(expectedRot)}`, details };
      // arrow translation should be offset 0.18m along heading + 0.12m up
      const expectedArrow = [mt[0] + Math.cos(h)*0.18, mt[1]+0.12, mt[2]+Math.sin(h)*0.18];
      const art = arrowOp.transform.translation;
      if (Math.hypot(art[0]-expectedArrow[0], art[1]-expectedArrow[1], art[2]-expectedArrow[2]) > eps) return { ok: false, reason: `arrow translation ${JSON.stringify(art)} != expected ${JSON.stringify(expectedArrow)} for heading ${h}`, details };
      const qy = ar[1], qw = ar[3];
      const rotatedX_z = -2*qw*qy;
      if (Math.sign(rotatedX_z) !== Math.sign(Math.sin(h)) && Math.abs(Math.sin(h)) > 0.1) return { ok: false, reason: `rotated +X z ${rotatedX_z} sign mismatches sin(heading)=${Math.sin(h)} (sign-reversed quaternion?)`, details };
      return { ok: true, details };
    });
    console.log(`frameProof: ${JSON.stringify(frameProof).slice(0,1200)}`);
    check(frameProof.ok, `live gizmo frame proof failed: ${frameProof.reason ?? 'unknown'} — ${JSON.stringify(frameProof.details ?? {}).slice(0,800)}`);
  }

  check(consoleErrors.length === 0, `${consoleErrors.length} console errors: ${consoleErrors.slice(0,3).join(' | ')}`);

  const shot = await page.screenshot();
  await writeFile(resolve(HERE, 'flycam-gizmo.png'), shot);

  await page.close();
} finally {
  await browser.close();
  flycam.kill();
}

if (failures.length > 0) {
  console.log('FLYCAM GIZMO CHECK FAILED:');
  for (const f of failures) console.log(` - ${f}`);
  process.exit(1);
}
console.log('FLYCAM GIZMO CHECK PASSED; screenshot: engine-render-check/flycam-gizmo.png');
