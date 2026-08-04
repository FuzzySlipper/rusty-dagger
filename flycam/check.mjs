#!/usr/bin/env node
/** Headless render proof for the flycam debug view (Chromium + swiftshader). */
import { chromium } from '/home/dev/rusty-engine/render/node_modules/.pnpm/playwright@1.61.1/node_modules/playwright/index.mjs';

const base = process.env.FLYCAM_URL ?? 'http://127.0.0.1:4174';
const outPath = process.argv[2] ?? '/tmp/flycam-proof.png';

const browser = await chromium.launch({
  headless: true,
  args: ['--no-sandbox', '--use-gl=swiftshader', '--enable-unsafe-swiftshader'],
});
const failures = [];
try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  const consoleErrors = [];
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', (e) => consoleErrors.push(String(e)));
  await page.goto(`${base}/`, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => window.__flyDone === true, null, { timeout: 30_000 });
  // Let a few frames render.
  await page.waitForTimeout(1500);
  const stats = await page.evaluate(() => window.__fly.stats);
  const errors = await page.evaluate(() => window.__fly.errors);
  await page.screenshot({ path: outPath });

  console.log('stats:', JSON.stringify(stats));
  if (errors.length) failures.push('page errors: ' + errors.join(' | '));
  if (consoleErrors.length) failures.push('console errors: ' + consoleErrors.join(' | '));
  if (stats.triCount < 9000) failures.push(`expected ~9263 tris, got ${stats.triCount}`);
  if (stats.texturedMats < 80) failures.push(`expected 81 textured mats, got ${stats.texturedMats}`);
  if (stats.billboards !== 130) failures.push(`expected 130 billboards, got ${stats.billboards}`);
  if (stats.lights !== 71) failures.push(`expected 71 lights, got ${stats.lights}`);
} finally {
  await browser.close();
}
if (failures.length) {
  console.error('FLYCAM PROOF FAILED:');
  for (const f of failures) console.error(' - ' + f);
  process.exit(1);
}
console.log('FLYCAM PROOF PASSED; screenshot at ' + outPath);
