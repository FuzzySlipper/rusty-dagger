#!/usr/bin/env node
/**
 * Headless proof of the flycam nav-grid gizmo (task 6639).
 *
 * Boots the REAL flycam server (serve-flycam.mjs: adapter frame dump + Rust
 * sprite authority + vite), loads the flycam page in headless Chromium
 * (SwiftShader WebGL), positions the camera over the start room floor via the
 * __flycam debug seam, toggles the grid with 'N', and asserts the gizmo
 * cells actually render (cyan pixel coverage appears only when toggled on)
 * with zero console errors.
 *
 *     node engine-render-check/check-flycam-navgrid.mjs
 *
 * Screenshots: engine-render-check/flycam-navgrid-off.png / -on.png
 */
import { spawn } from 'node:child_process';
import { writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { decodePng } from '../scripts/studio-frame-metrics.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const PORT = Number(process.env.RUSTY_FLYCAM_CHECK_PORT ?? 4176);

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

/** Count gizmo-cyan pixels (grid color [0.2, 0.9, 1.0]). */
function cyanPixels(png) {
  const { width, height, rgb } = decodePng(png);
  let count = 0;
  for (let i = 0; i < width * height; i += 1) {
    const r = rgb[i * 3];
    const g = rgb[i * 3 + 1];
    const b = rgb[i * 3 + 2];
    if (g > 150 && b > 180 && r < 120) count += 1;
  }
  return count;
}

try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 960 }, deviceScaleFactor: 1 });
  const consoleErrors = [];
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => consoleErrors.push(String(error)));

  await page.goto(`http://127.0.0.1:${PORT}/`, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => window.__flycam !== undefined, undefined, { timeout: 60_000 });
  // Hover inside the start room, looking down at the main floor (y=32) — the
  // grid cells within the 10m gizmo radius should fill the lower view.
  await page.evaluate(() => {
    const state = window.__flycam;
    state.position = [28.25, 35.5, -12.25];
    state.yawDegrees = 180;
    state.pitchDegrees = -55;
    state.moved = true;
    // The click-to-fly overlay never engages headless (no pointer lock);
    // hide it so the canvas pixels are captured undimmed.
    document.getElementById('hint').style.display = 'none';
  });
  await page.waitForTimeout(1500);

  const off = await page.screenshot();
  await writeFile(resolve(HERE, 'flycam-navgrid-off.png'), off);

  await page.evaluate(() => window.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyN' })));
  await page.waitForTimeout(1000);
  const on = await page.screenshot();
  await writeFile(resolve(HERE, 'flycam-navgrid-on.png'), on);

  const offCyan = cyanPixels(off);
  const onCyan = cyanPixels(on);
  console.log(`navgrid gizmo: cyan pixels off=${offCyan} on=${onCyan}`);
  check(offCyan < 500, `grid-off screenshot already shows ${offCyan} cyan pixels (gizmo bleeding?)`);
  check(onCyan > 2000, `grid-on screenshot shows only ${onCyan} cyan pixels (grid not rendered)`);
  check(
    consoleErrors.length === 0,
    `${consoleErrors.length} console errors: ${consoleErrors.slice(0, 3).join(' | ')}`,
  );
  await page.close();
} finally {
  await browser.close();
  flycam.kill();
}

if (failures.length > 0) {
  console.log('FLYCAM NAVGRID CHECK FAILED:');
  for (const failure of failures) console.log(` - ${failure}`);
  process.exit(1);
}
console.log('FLYCAM NAVGRID CHECK PASSED; screenshots: engine-render-check/flycam-navgrid-off.png, engine-render-check/flycam-navgrid-on.png');
