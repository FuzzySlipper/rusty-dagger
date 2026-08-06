#!/usr/bin/env node
/**
 * Headless proof for sprite animation (task 6640): verifies the LIVE Rust
 * animation authority through the flycam's actual behavior.
 *
 * Strategy: load the page, intercept fetch('/assignments') responses, move
 * the camera to force enemy orientation changes (so enemies appear in the
 * diff alongside billboards), then verify:
 *
 * 1. Responses use the consolidated {updates} format (not {assignments})
 * 2. At least one response contains both enemy and billboard handles
 * 3. Frame indices advance over time for at least some handles
 * 4. Enemy frames are within atlas bounds
 * 5. Zero console errors
 *
 * Requires serve-flycam.mjs running:
 *   node engine-render-check/serve-flycam.mjs
 *   RUSTY_FLYCAM_URL=http://127.0.0.1:4174 node engine-render-check/check-flycam-animation.mjs
 */
import { chromium } from '@playwright/test';

const FLYCAM = process.env.RUSTY_FLYCAM_URL ?? 'http://127.0.0.1:4174';

function assert(cond, msg) {
  if (!cond) throw new Error(`assertion failed: ${msg}`);
}

const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage();
  const errors = [];
  page.on('console', (msg) => {
    if (msg.type() === 'error') errors.push(msg.text());
  });
  page.on('pageerror', (err) => errors.push(String(err)));

  // Install fetch interception BEFORE the page loads.
  await page.addInitScript(() => {
    window.__assignmentResponses = [];
    const origFetch = window.fetch;
    window.fetch = async (...args) => {
      const resp = await origFetch(...args);
      const url = typeof args[0] === 'string' ? args[0] : args[0]?.url ?? '';
      if (url.includes('/assignments')) {
        const clone = resp.clone();
        clone.json().then((body) => {
          window.__assignmentResponses.push(body);
        }).catch(() => {});
      }
      return resp;
    };
  });

  // Load the page (starts the renderer + animation loop)
  await page.goto(FLYCAM, { waitUntil: 'networkidle' });
  await page.waitForFunction(() => window.__flycam !== undefined, { timeout: 10_000 });

  // Wait for the initial animation polls
  await page.waitForTimeout(1500);

  // Move the camera sideways to force enemy orientation changes.
  // This makes enemy handles appear in the diff alongside billboards.
  await page.evaluate(() => {
    window.__flycam.position[0] += 3.0;
    window.__flycam.moved = true;
  });
  await page.waitForTimeout(1500);

  // Collect all intercepted responses
  const responses = await page.evaluate(() => window.__assignmentResponses ?? []);
  assert(responses.length >= 2, `expected at least 2 /assignments polls, got ${responses.length}`);

  // --- Check 1: all responses use the consolidated {updates} format ---
  for (const resp of responses) {
    assert(resp.updates !== undefined, `response missing "updates" key — expected consolidated format`);
  }
  console.log(`format: all ${responses.length} responses use {updates} format`);

  // Load metadata to classify handles
  const enemies = await page.evaluate(async () => {
    const r = await fetch('/generated/enemies.json');
    return r.json();
  });
  const enemyHandles = new Set(enemies.enemies.map((e) => e.handle));
  const billboardHandles = new Set((enemies.animatedBillboards ?? []).map((b) => b.handle));

  // --- Check 2: at least one response has both enemy and billboard ---
  let mixedResponse = null;
  for (const resp of responses) {
    const hasEnemy = resp.updates.some((u) => enemyHandles.has(u.handle));
    const hasBillboard = resp.updates.some((u) => billboardHandles.has(u.handle));
    if (hasEnemy && hasBillboard) {
      mixedResponse = resp;
      break;
    }
  }
  assert(mixedResponse !== null, 'no response contained both enemy and billboard handles');
  console.log(`mixed response: ${mixedResponse.updates.length} updates (enemy + billboard)`);

  // --- Check 3: frame advancement across responses ---
  // Track frames per handle across all responses; at least one must change.
  const frameHistory = new Map(); // handle → [frames across responses]
  for (const resp of responses) {
    for (const u of resp.updates) {
      if (!frameHistory.has(u.handle)) frameHistory.set(u.handle, []);
      frameHistory.get(u.handle).push(u.frame);
    }
  }

  let advanced = 0;
  for (const [handle, frames] of frameHistory) {
    if (new Set(frames).size > 1) advanced++;
  }
  assert(advanced > 0, `expected frame advancement for at least 1 handle, got ${advanced}`);
  console.log(`frame advancement: ${advanced} handles changed across ${responses.length} polls`);

  // --- Check 4: enemy frames within atlas bounds ---
  const atlasFrames = enemies.enemyAtlasFrames ?? {};
  let enemyChecks = 0;
  for (const resp of responses) {
    for (const u of resp.updates) {
      if (!enemyHandles.has(u.handle)) continue;
      const enemy = enemies.enemies.find((e) => e.handle === u.handle);
      if (!enemy) continue;
      const total = atlasFrames[String(enemy.mobileId)] ?? 8;
      assert(u.frame < total, `enemy ${u.handle} frame ${u.frame} >= atlas ${total}`);
      enemyChecks++;
    }
  }
  console.log(`enemy bounds: ${enemyChecks} frames checked`);

  // --- Check 5: zero console errors ---
  await page.waitForTimeout(500);
  assert(errors.length === 0, `console errors: ${errors.join('; ')}`);

  console.log('ALL ANIMATION CHECKS PASSED');
} finally {
  await browser.close();
}
